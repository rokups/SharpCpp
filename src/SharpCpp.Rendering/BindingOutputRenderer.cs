using SharpCpp.Model.Ast;
using SharpCpp.Model.Utilities;

using System.Text;
using System.Text.RegularExpressions;

namespace SharpCpp.Rendering;

public static partial class BindingOutputRenderer
{
    private static readonly Regex NativeExportDeclarationRegex = new(
        @"(?m)^(\s*)SHARPCPP_EXPORT(?:_NAMED\([A-Za-z_][A-Za-z0-9_]*\))?\s+(.+?)\s+([A-Za-z_][A-Za-z0-9_]*)(\([^\r\n]*\))\r?$",
        RegexOptions.CultureInvariant);
    private const string ICloneableFullName = "global::System.ICloneable";
    private const string BorrowedNativeHandleFullName = "global::SharpCpp.Support.BorrowedNativeHandle";
    private const string OwnedNativeHandleFullName = "global::SharpCpp.Support.OwnedNativeHandle";
    private const string NativeHandleOwnershipConstraintFullName = "global::SharpCpp.Support.INativeHandleOwnership";

    private sealed record ManagedCallbackUsage(BindingType CallbackType, string ManagedTypeName, string MarshallerDeclarationName);

    private sealed record ManagedCallbackDelegateDeclaration(string DeclarationName, BindingType CallbackType);

    private static ManagedBindingProjection? GetManagedProjection(BindingCompilation compilation)
    {
        return compilation.GetMetadata<ManagedBindingProjection>(MetadataKeys.ManagedBindingProjection);
    }

    private static string GetManagedNamespace(BindingCompilation compilation, RecordBinding record)
    {
        // A nested record is declared inside its enclosing record's class, whatever namespace
        // a consumer projection assigns to declarations of its scope.
        if (TryGetEnclosingRecord(compilation, record, out var enclosing))
        {
            return $"{GetManagedNamespace(compilation, enclosing)}.{GetManagedTypeHead(enclosing.ManagedName)}";
        }

        var projection = GetManagedProjection(compilation);
        if (projection is not null && projection.TryGetManagedNamespace(record, out var projected) &&
            !string.IsNullOrWhiteSpace(projected))
        {
            return projected;
        }

        if (!string.IsNullOrWhiteSpace(record.ManagedNamespace))
        {
            return record.ManagedNamespace!;
        }

        return compilation.ManagedNamespace;
    }

    private static bool TryGetEnclosingRecord(BindingCompilation compilation, RecordBinding record, out RecordBinding enclosing)
    {
        enclosing = null!;
        var separator = record.CppQualifiedName.LastIndexOf("::", StringComparison.Ordinal);
        if (separator <= 0 || record.CppQualifiedName.Contains('<', StringComparison.Ordinal))
            return false;

        var scope = record.CppQualifiedName[..separator];
        var candidate = compilation.FindRecordBySymbolId($"record:{scope}");
        // An enclosing record without a wrapper (no API of its own) still scopes the nested
        // record, as a namespace of the same name.
        if (candidate is null || candidate.IsGeneric ||
            candidate.GetMetadata<bool>(MetadataKeys.SuppressEmission) ||
            IsSharedManagedWrapper(candidate))
        {
            return false;
        }

        enclosing = candidate;
        return true;
    }

    private static string GetManagedNamespace(BindingCompilation compilation, EnumBinding enumBinding)
    {
        return GetManagedProjection(compilation)?.GetManagedNamespace(enumBinding, compilation) ??
            enumBinding.ManagedNamespace ??
            compilation.ManagedNamespace;
    }

    private static string GetManagedNamespace(BindingCompilation compilation, FunctionBinding function)
    {
        return string.IsNullOrWhiteSpace(function.ManagedNamespace)
            ? compilation.ManagedNamespace
            : function.ManagedNamespace!;
    }

    private static string GetManagedClassName(BindingCompilation compilation, FunctionBinding function)
    {
        return string.IsNullOrWhiteSpace(function.ManagedClassName)
            ? compilation.ManagedClassName
            : function.ManagedClassName!;
    }

    private static bool IsSharedManagedWrapper(RecordBinding record)
        => !string.IsNullOrWhiteSpace(record.GetMetadata<string>(MetadataKeys.ManagedWrapperAssembly));

    /// <summary>
    /// Marshaller helpers for a shared wrapper are per module (they bind the module's
    /// providers), so they live under the module namespace; several modules can then
    /// share one assembly, and wrappers from different namespaces cannot collide.
    /// </summary>
    private static HashSet<string> GetRenderedRecordClassNames(BindingCompilation compilation)
    {
        return compilation.Records
            .Where(record => !record.GetMetadata<bool>(MetadataKeys.SuppressEmission) &&
                !record.IsSilentlyExcluded() &&
                !record.HasMetadata(MetadataKeys.IgnoreReason) &&
                !record.IsGeneric &&
                !IsSharedManagedWrapper(record))
            .Select(record => $"{GetManagedNamespace(compilation, record)}.{record.ManagedName}")
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// A record's managed namespace string is a dotted path that may embed any number of
    /// enclosing-record class names ahead of the real C# namespace (see
    /// <see cref="GetManagedNamespace(BindingCompilation, RecordBinding)"/>): each nesting
    /// level appends its own enclosing class name, so a depth-N nested record's namespace
    /// carries N-1 embedded class segments. Peel matching segments off the tail one at a
    /// time - each corresponds to a still-open partial class - until what remains is not a
    /// rendered class's full name, which is the real namespace to open. This lets emission
    /// nest a record to any depth as partial classes, the same way depth-1 nesting already
    /// works.
    /// </summary>
    private static (string Namespace, IReadOnlyList<string> EnclosingClasses) DecomposeManagedNamespace(
        string ns, IReadOnlySet<string> renderedClassNames)
    {
        var enclosingClasses = new List<string>();
        var current = ns;
        while (renderedClassNames.Contains(current))
        {
            var lastDot = current.LastIndexOf('.');
            if (lastDot < 0)
            {
                break;
            }

            enclosingClasses.Insert(0, current[(lastDot + 1)..]);
            current = current[..lastDot];
        }

        return (current, enclosingClasses);
    }

    private static string GetMarshallerHelperNamespace(BindingCompilation compilation, RecordBinding record)
    {
        var ns = GetManagedNamespace(compilation, record);
        if (!IsSharedManagedWrapper(record))
        {
            return ns;
        }

        var wrapperNamespace = ns.StartsWith("global::", StringComparison.Ordinal) ? ns["global::".Length..] : ns;
        return $"{compilation.ManagedNamespace}.__SharedMarshallers.{wrapperNamespace}";
    }

    private static string GetSharedModuleIdLiteral(BindingCompilation compilation)
        => $"\"{EscapeCSharpString(compilation.ProjectName)}\"";

    private static string EscapeCSharpString(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

    private static string GetSharedManagedInfrastructureNamespace(BindingCompilation compilation, RecordBinding record)
    {
        var configured = record.GetMetadata<string>(MetadataKeys.ManagedWrapperProviderNamespace);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var normalized = configured.Trim();
            return normalized.StartsWith("global::", StringComparison.Ordinal)
                ? normalized
                : $"global::{normalized}";
        }

        var managedNamespace = record.ManagedNamespace ?? compilation.ManagedNamespace;
        if (managedNamespace.StartsWith("global::", StringComparison.Ordinal))
        {
            managedNamespace = managedNamespace["global::".Length..];
        }

        var root = managedNamespace.Split('.', 2)[0];
        return $"global::{root}.Infrastructure";
    }

    private static string GetSharedManagedTypeName(BindingCompilation compilation, BindingType type)
        => GetManagedPublicReturnTypeName(type, compilation);

    private static string GetSharedManagedProviderModuleArgument(BindingCompilation compilation, RecordBinding record)
        => IsSharedManagedWrapper(record) ? $", moduleId: {GetSharedModuleIdLiteral(compilation)}" : string.Empty;

    /// <param name="includeCallbackReleaseRegistration">
    /// No effect. The managed callback release export carries the module's entry point
    /// prefix and is always emitted, because every module's managed side registers
    /// through its own export. Kept so existing callers compile unchanged.
    /// </param>
    public static GeneratedBindingSet Render(
        BindingCompilation compilation,
        string fileStem,
        Func<string?, string>? deriveGroupStem = null,
        bool includeCallbackReleaseRegistration = true)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileStem);

        AssignImportMethodNames(compilation);

        if (deriveGroupStem is null)
        {
            var files = new[]
            {
                new GeneratedBindingFile($"{fileStem}.Bindings.g.cs", RenderManagedBindings(compilation)),
                new GeneratedBindingFile($"{fileStem}.Bindings.Aliases.g.cs", RenderAliasBindings(compilation, includeSharedCallbackHelpers: true)),
                new GeneratedBindingFile($"{fileStem}.Bindings.g.cpp", RenderNativeBindings(compilation))
            };
            return new GeneratedBindingSet(files);
        }

        var allFiles = new List<GeneratedBindingFile>();

        var groups = compilation.Members.OfType<BindingNode>()
            .Where(m => m is FunctionBinding ||
                        m is RecordBinding record && (IsSharedManagedWrapper(record) || !record.GetMetadata<bool>(MetadataKeys.SuppressEmission)) ||
                        m is EnumBinding @enum && !@enum.GetMetadata<bool>(MetadataKeys.SuppressEmission) ||
                        m is ManagedClassBinding managedClass && managedClass.Parent is not ManagedClassBinding)
            .GroupBy(m => deriveGroupStem(string.IsNullOrEmpty(m.HeaderIncludePath) ? null : m.HeaderIncludePath), StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .ToList();

        if (groups.Count == 0)
        {
            allFiles.Add(new GeneratedBindingFile($"{fileStem}.Bindings.g.cs", RenderManagedBindings(compilation)));
            allFiles.Add(new GeneratedBindingFile($"{fileStem}.Bindings.g.cpp", RenderNativeBindings(compilation)));
        }
        else
        {
            for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                var group = groups[groupIndex];
                var stem = group.Key;
                var memberSet = new HashSet<BindingNode>(group);

                allFiles.Add(new GeneratedBindingFile($"{stem}.g.cs",
                    RenderManagedBindings(
                        compilation,
                        node => memberSet.Contains(node) || (node is RecordBinding r && memberSet.Contains(r)))));
                allFiles.Add(new GeneratedBindingFile($"{stem}.g.cpp",
                    RenderNativeBindings(
                        compilation,
                        node => memberSet.Contains(node) || (node is RecordBinding r && memberSet.Contains(r)),
                        includeCallbackReleaseRegistration: groupIndex == 0)));
            }
        }

        allFiles.Add(new GeneratedBindingFile($"{fileStem}.Bindings.Aliases.g.cs",
            RenderAliasBindings(compilation, deriveGroupStem is not null, includeSharedCallbackHelpers: true)));
        return new GeneratedBindingSet(allFiles);
    }

    private static string RenderManagedBindings(
        BindingCompilation compilation,
        Func<BindingNode, bool>? memberFilter = null)
    {
        var writer = new CodeWriter();
        var allRecords = compilation.Records.Where(record => (IsSharedManagedWrapper(record) || !record.GetMetadata<bool>(MetadataKeys.SuppressEmission)) && !record.IsSilentlyExcluded()).AsEnumerable();
        var allEnums = compilation.Enums.Where(static @enum => !@enum.GetMetadata<bool>(MetadataKeys.SuppressEmission) && !@enum.IsSilentlyExcluded()).AsEnumerable();
        var allFunctions = compilation.Functions.Where(static function => !function.IsSilentlyExcluded() &&
            (function.Parent is not RecordBinding parentRecord || !parentRecord.IsSilentlyExcluded())).AsEnumerable();
        var allManagedClasses = compilation.ManagedClasses
            .Where(static managedClass => managedClass.Parent is not ManagedClassBinding)
            .AsEnumerable();

        if (memberFilter is not null)
        {
            allRecords = allRecords.Where(r => memberFilter(r));
            allEnums = allEnums.Where(e => memberFilter(e));
            allFunctions = allFunctions.Where(f => memberFilter(f));
            allManagedClasses = allManagedClasses.Where(c => memberFilter(c));
        }

        var records = allRecords.OrderBy(static record => record.ManagedName, StringComparer.Ordinal).ToArray();
        var enums = allEnums.OrderBy(static item => item.ManagedName, StringComparer.Ordinal).ToArray();
        var functions = allFunctions.OrderBy(static function => function.ManagedName, StringComparer.Ordinal).ToArray();
        var managedClasses = allManagedClasses.OrderBy(static item => item.ManagedName, StringComparer.Ordinal).ToArray();
        var interfaceRecordIds = compilation.Records
            .SelectMany(record => record.GetSecondaryBaseRecords(compilation))
            .Select(static record => record.SymbolId)
            .ToHashSet(StringComparer.Ordinal);
        var interfaceRecords = records
            .Where(record => interfaceRecordIds.Contains(record.SymbolId))
            .GroupBy(static record => record.SymbolId, StringComparer.Ordinal)
            .Select(static group => group.First())
            // Exclude concrete generic instantiations from interface rendering.
            // Each closed instantiation (e.g. IIDFamily<Geometry>, IIDFamily<Material>)
            // has a ManagedName containing angle brackets. Emitting them as separate
            // partial interfaces with different type parameter names causes CS0264.
            .Where(static record => !record.ManagedName.Contains('<'))
            .OrderBy(static record => record.ManagedName, StringComparer.Ordinal)
            .ToArray();
        var polymorphicConcreteRecords = records
            .Where(record => record.IsPolymorphic && !record.IsGeneric && CanCreateManagedWrapperFactory(compilation, record) && !record.HasMetadata(MetadataKeys.IgnoreReason))
            .ToArray();
        var genericImportDeclarations = new List<string>();
        var sharedImportDeclarations = new List<string>();

        writer.Write(
            $$"""
            // <auto-generated />
            #nullable enable
            using System;
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;
            using System.Runtime.InteropServices.Marshalling;
            """);

        var recordGroups = records.GroupBy(record => GetMarshallerHelperNamespace(compilation, record)).ToArray();
        var enumGroups = enums.GroupBy(@enum => GetManagedNamespace(compilation, @enum)).ToArray();
        var managedClassGroups = managedClasses.GroupBy(managedClass => GetManagedNamespace(compilation, managedClass)).ToArray();
        var allNamespaces = recordGroups.Select(group => group.Key)
            .Concat(enumGroups.Select(group => group.Key))
            .Concat(managedClassGroups.Select(group => group.Key))
            .Distinct()
            .OrderBy(ns => ns, StringComparer.Ordinal);

        var renderedClassNames = GetRenderedRecordClassNames(compilation);
        foreach (var ns in allNamespaces)
        {
            var nsRecords = records.Where(record => GetMarshallerHelperNamespace(compilation, record) == ns).ToArray();
            var nsInterfaces = interfaceRecords.Where(record => GetManagedNamespace(compilation, record) == ns).ToArray();
            var nsEnums = enums.Where(@enum => GetManagedNamespace(compilation, @enum) == ns).ToArray();
            var nsManagedClasses = managedClasses.Where(managedClass => GetManagedNamespace(compilation, managedClass) == ns).ToArray();

            // Declarations scoped by one or more enclosing records whose managed path is also
            // those records' classes are nested in matching partial classes, to any depth; a
            // namespace segment of the same name as an enclosing class cannot coexist.
            var (enclosingNamespace, enclosingClasses) = DecomposeManagedNamespace(ns, renderedClassNames);
            writer.WriteLine();
            writer.Write(
                $$"""
                namespace {{enclosingNamespace}}
                {
                """);
            foreach (var enclosingClass in enclosingClasses)
            {
                writer.Write(
                    $$"""
                    partial class {{enclosingClass}}
                    {
                    """);
            }

            foreach (var interfaceRecord in nsInterfaces)
            {
                writer.WriteLine();
                writer.Write(RenderInterface(compilation, interfaceRecord));
            }

            foreach (var record in nsRecords)
            {
                writer.WriteLine();
                if (record.HasMetadata(MetadataKeys.IgnoreReason))
                {
                    writer.Write(RenderIgnoredStub(record));
                }
                else
                {
                    if (record.IsGeneric && !IsSharedManagedWrapper(record))
                    {
                        genericImportDeclarations.AddRange(CollectGenericImportDeclarations(compilation, record));
                    }
                    else if (!IsSharedManagedWrapper(record))
                    {
                        genericImportDeclarations.AddRange(CollectNonGenericRecordGenericMethodImportDeclarations(compilation, record));
                    }

                    if (IsSharedManagedWrapper(record))
                    {
                        sharedImportDeclarations.AddRange(CollectSharedManagedWrapperImportDeclarations(compilation, record));
                        writer.Write(RenderSharedManagedWrapperMarshallerHelpers(compilation, record));
                    }
                    else
                    {
                        writer.Write(RenderRecord(compilation, record));
                    }
                }
            }

            foreach (var @enum in nsEnums)
            {
                writer.WriteLine();
                if (@enum.HasMetadata(MetadataKeys.IgnoreReason))
                {
                    writer.Write(RenderIgnoredStub(@enum));
                }
                else
                {
                    writer.Write(RenderEnum(@enum));
                }
            }

            foreach (var managedClass in nsManagedClasses)
            {
                writer.WriteLine();
                writer.Write(RenderManagedClass(managedClass));
            }

            foreach (var _ in enclosingClasses)
            {
                writer.Write(
                    """
                    }
                    """);
            }

            writer.Write(
                """
                }
                """);
        }

        var defaultContainer = (Namespace: compilation.ManagedNamespace, Class: compilation.ManagedClassName);
        var freeFunctionGroups = functions
            .Where(static function => function.Parent is not RecordBinding && !function.IsConstructor)
            .GroupBy(function => (Namespace: GetManagedNamespace(compilation, function), Class: GetManagedClassName(compilation, function)))
            .ToDictionary(static group => group.Key, static group => group.OrderBy(static function => function.ManagedName, StringComparer.Ordinal).ToArray());

        if ((memberFilter is null && polymorphicConcreteRecords.Length > 0) || genericImportDeclarations.Count > 0 || sharedImportDeclarations.Count > 0)
        {
            freeFunctionGroups.TryAdd(defaultContainer, Array.Empty<FunctionBinding>());
        }

        foreach (var (container, containerFunctions) in freeFunctionGroups
            .OrderBy(static pair => pair.Key.Namespace, StringComparer.Ordinal)
            .ThenBy(static pair => pair.Key.Class, StringComparer.Ordinal))
        {
            writer.WriteLine();
            writer.Write(
                $$"""
                namespace {{container.Namespace}}
                {
                public static partial class {{container.Class}}
                {
                """);

            var freeFunctionDeclarations = new List<string>();
            var lastFreeFunctionWasIgnoredStub = false;

            foreach (var function in containerFunctions)
            {
                var isIgnoredStub = function.HasMetadata(MetadataKeys.IgnoreReason);
                writer.WriteLine();
                if (lastFreeFunctionWasIgnoredStub && !isIgnoredStub)
                {
                    writer.WriteLine();
                }

                if (isIgnoredStub)
                {
                    writer.Write(RenderIgnoredStub(function));
                }
                else if (function.IsGeneric)
                {
                    writer.Write(RenderManagedGenericFreeFunction(compilation, function));
                    freeFunctionDeclarations.AddRange(CollectGenericFreeFunctionImportDeclarations(compilation, function));
                }
                else
                {
                    writer.Write(RenderManagedWrapperMethod(compilation, function));
                    freeFunctionDeclarations.Add(RenderManagedImportDeclaration(compilation, function));
                    if (TryGetRecordBufferOverloadParameters(function, out _, out _))
                    {
                        writer.WriteLine();
                        writer.WriteLine();
                        writer.Write(RenderManagedRecordBufferOverload(
                            compilation,
                            function,
                            "public static",
                            GetManagedPublicReturnTypeName(function.ReturnType, compilation),
                            GetManagedImportMethodName(function),
                            isInstance: false));
                        freeFunctionDeclarations.Add(RenderManagedRecordBufferImportDeclaration(compilation, function, selfTypeName: null));
                    }

                    if (function.GetMetadata<bool>(MetadataKeys.ExposeNativeFunctionPointer))
                    {
                        writer.WriteLine();
                        writer.Write($$"""
                            public static global::System.IntPtr {{function.ManagedName}}FunctionPointer
                            {
                                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                                get => __PInvoke.{{function.ImportMethodName}}_FunctionPointer();
                            }
                            """);
                        freeFunctionDeclarations.Add($$"""
                            [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "{{function.EntryPointName}}_function_pointer", StringMarshalling = StringMarshalling.Utf8)]
                            [MethodImpl(MethodImplOptions.AggressiveInlining)]
                            internal static partial global::System.IntPtr {{function.ImportMethodName}}_FunctionPointer();
                            """);
                    }

                    if (HasNativeDefaultDispatchParams(function) || HasUndefaultableParams(function))
                        freeFunctionDeclarations.Add(RenderUnexpressibleDefaultFreeFunctionImport(compilation, function));
                }

                lastFreeFunctionWasIgnoredStub = isIgnoredStub;
            }

            if (lastFreeFunctionWasIgnoredStub)
            {
                writer.WriteLine();
            }

            var isDefaultContainer = container == defaultContainer;
            if (isDefaultContainer && memberFilter is null && polymorphicConcreteRecords.Length > 0)
            {
                writer.WriteLine();
                writer.Write(RenderNativeObjectRegistryInitializer(compilation, polymorphicConcreteRecords));
            }

            if (isDefaultContainer)
            {
                var nativeArrayRegistryInitializer = RenderSharedNativeArrayRegistryInitializer(compilation, records);
                if (!string.IsNullOrEmpty(nativeArrayRegistryInitializer))
                {
                    writer.WriteLine();
                    writer.Write(nativeArrayRegistryInitializer);
                }
            }

            if (freeFunctionDeclarations.Count > 0 || isDefaultContainer &&
                (genericImportDeclarations.Count > 0 || sharedImportDeclarations.Count > 0))
            {
                writer.WriteLine();
                writer.Write(
                    """
                    internal static partial class __PInvoke
                    {
                    """);

                foreach (var declaration in freeFunctionDeclarations)
                {
                    writer.WriteLine();
                    writer.Write(declaration);
                }

                if (isDefaultContainer)
                {
                    foreach (var declaration in genericImportDeclarations)
                    {
                        writer.WriteLine();
                        writer.Write(declaration);
                    }

                    foreach (var declaration in sharedImportDeclarations.Distinct(StringComparer.Ordinal))
                    {
                        writer.WriteLine();
                        writer.Write(declaration);
                    }
                }

                writer.WriteLine();
                writer.Write(
                    """
                    }
                    """);
            }

            writer.WriteLine();
            writer.Write(
                """
                }
                }
                """);
        }

        return writer.ToString();
    }

    private static string GetManagedNamespace(BindingCompilation compilation, ManagedClassBinding managedClass)
    {
        return string.IsNullOrWhiteSpace(managedClass.ManagedNamespace)
            ? compilation.ManagedNamespace
            : managedClass.ManagedNamespace;
    }

    private static IEnumerable<string> CollectSharedManagedWrapperImportDeclarations(
        BindingCompilation compilation,
        RecordBinding record)
    {
        if (record.IsGeneric)
        {
            if (IsGenericArrayRecord(record))
            {
                foreach (var declaration in CollectGenericArrayImportDeclarations(compilation, record))
                {
                    yield return declaration;
                }

                yield break;
            }

            if (!IsGenericArrayRecord(record))
            {
                foreach (var instantiation in GetCanonicalManagedGenericInstantiations(compilation, record))
                {
                    yield return RenderSharedProviderCreateImportDeclaration(compilation, record, instantiation);
                    if (CanEmitSharedProviderClone(compilation, record, instantiation))
                    {
                        yield return RenderSharedProviderCloneImportDeclaration(compilation, record, instantiation);
                    }

                    foreach (var method in record.Members.OfType<FunctionBinding>()
                                 .Where(static function => !function.IsConstructor && !function.IsStatic && !function.IsGeneric &&
                                     function.OperatorToken is null && !function.IsConversionOperator && !function.IsSilentlyExcluded() &&
                                     IsSharedProviderMethod(function))
                                 .OrderBy(static function => function.ManagedName, StringComparer.Ordinal))
                    {
                        yield return RenderGenericInstanceImportDeclaration(
                            compilation,
                            record,
                            method,
                            instantiation,
                            GetSharedProviderMethodImportName(record, method, instantiation));
                    }

                    if (CanEmitSharedProviderDelete(record, instantiation))
                    {
                        yield return RenderGenericDeleteImportDeclaration(
                            compilation,
                            record,
                            instantiation,
                            GetSharedProviderDeleteImportName(record, instantiation));
                    }
                }
            }

            yield break;
        }

        if (CanEmitDeleteThunk(record))
        {
            yield return RenderManagedDeleteImportDeclaration(compilation, record, GetSharedProviderDeleteImportName(record, null));
        }

        yield return RenderSharedProviderCreateImportDeclaration(compilation, record);
        if (CanEmitSharedProviderClone(compilation, record, instantiation: null))
        {
            yield return RenderSharedProviderCloneImportDeclaration(compilation, record);
        }

        foreach (var method in record.Members.OfType<FunctionBinding>()
                     .Where(static function => !function.IsConstructor && !function.IsStatic && !function.IsGeneric &&
                         function.OperatorToken is null && !function.IsConversionOperator && !function.IsSilentlyExcluded() &&
                         IsSharedProviderMethod(function))
                     .OrderBy(static function => function.ManagedName, StringComparer.Ordinal))
        {
            yield return RenderSharedManagedInstanceImportDeclaration(compilation, record, method);
        }
    }

    private static string RenderSharedManagedWrapperMarshallerHelpers(
        BindingCompilation compilation,
        RecordBinding record)
    {
        var declarationName = record.IsGeneric
            ? GetGenericRecordDeclarationName(record)
            : GetManagedDeclarationName(record);
        return RenderManagedHandleReturnMarshallerHelpers(compilation, record, declarationName);
    }

    private static string GetManagedDeclarationName(RecordBinding record)
    {
        var name = GetManagedTypeHead(record.ManagedName);
        if (name.StartsWith("global::", StringComparison.Ordinal))
        {
            name = name["global::".Length..];
        }

        var separator = name.LastIndexOf('.');
        return separator >= 0 ? name[(separator + 1)..] : name;
    }

    private static string RenderManagedClass(ManagedClassBinding managedClass)
    {
        var writer = new CodeWriter();
        var modifiers = new List<string>
        {
            GetManagedVisibility(managedClass.AccessModifier)
        };

        if (managedClass.IsStatic)
        {
            modifiers.Add("static");
        }
        else if (managedClass.IsSealed)
        {
            modifiers.Add("sealed");
        }

        if (managedClass.IsPartial)
        {
            modifiers.Add("partial");
        }

        var baseClause = !managedClass.IsStatic && !string.IsNullOrWhiteSpace(managedClass.BaseTypeName)
            ? $" : {managedClass.BaseTypeName}"
            : string.Empty;

        writer.Write(
            $$"""
            {{string.Join(' ', modifiers)}} class {{managedClass.ManagedName}}{{baseClause}}
            {
            """);

        foreach (var field in managedClass.Members.OfType<ManagedFieldBinding>().OrderBy(static field => field.ManagedName, StringComparer.Ordinal))
        {
            writer.WriteLine();
            writer.Write(RenderManagedField(field));
        }

        foreach (var codeBlock in managedClass.BodyCodeBlocks)
        {
            writer.WriteLine();
            writer.Write(codeBlock);
        }

        foreach (var nestedClass in managedClass.Members.OfType<ManagedClassBinding>().OrderBy(static nestedClass => nestedClass.ManagedName, StringComparer.Ordinal))
        {
            writer.WriteLine();
            writer.Write(RenderManagedClass(nestedClass));
        }

        writer.WriteLine();
        writer.Write(
            """
            }
            """);
        return writer.ToString();
    }

    private static string RenderManagedField(ManagedFieldBinding field)
    {
        var modifiers = new List<string>
        {
            GetManagedVisibility(field.AccessModifier)
        };

        if (field.IsConst)
        {
            modifiers.Add("const");
        }
        else
        {
            if (field.IsStatic)
            {
                modifiers.Add("static");
            }

            if (field.IsReadOnly)
            {
                modifiers.Add("readonly");
            }
        }

        var initializer = string.IsNullOrWhiteSpace(field.InitializerExpression)
            ? string.Empty
            : $" = {field.InitializerExpression}";
        return $"{string.Join(' ', modifiers)} {field.ManagedTypeName} {field.ManagedName}{initializer};";
    }


    private static string RenderNativeDirectorClass(BindingCompilation compilation, RecordBinding record)
    {
        var writer = new CodeWriter();
        var directorName = GetNativeDirectorClassName(record);
        var constructors = record.Members.OfType<FunctionBinding>().Where(static function => function.IsConstructor).OrderBy(static function => function.Parameters.Count).ToArray();
        var slots = record.GetEffectiveVirtualMethodSlots(compilation);

        writer.Write($$"""
            class {{directorName}} : public {{record.CppQualifiedName}}
            {
            public:
            """);

        foreach (var (slot, index) in slots.Select((slot, index) => (slot, index)))
        {
            writer.WriteLine($"using {GetNativeDirectorCallbackTypeName(index)} = {BuildNativeDirectorCallbackSignature(slot.Method)};");
        }

        if (slots.Count > 0)
        {
            writer.WriteLine();
        }

        foreach (var constructor in constructors)
        {
            var parameterList = string.Join(", ", constructor.Parameters.Select(parameter => $"{parameter.CppTypeName} {parameter.NativeName}"));
            var arguments = string.Join(", ", constructor.Parameters.Select(static parameter => parameter.NativeName));
            writer.Write($$"""
                {{directorName}}({{parameterList}})
                    : {{record.CppQualifiedName}}({{arguments}})
                {
                }

                """);
        }

        foreach (var method in GetAccessibleProtectedMethods(compilation, record))
        {
            writer.WriteLine();
            writer.Write(RenderNativeProtectedMethodHelper(record, method));
        }

        foreach (var field in GetAccessibleProtectedFields(compilation, record))
        {
            writer.WriteLine();
            writer.Write(RenderNativeProtectedFieldHelpers(compilation, record, field));
        }

        var callbaseSlots = record.GetEffectiveVirtualMethodSlots(compilation)
            .Where(static slot => !slot.Method.IsPureVirtual)
            .ToArray();
        if (callbaseSlots.Length > 0)
        {
            writer.WriteLine();
            foreach (var slot in callbaseSlots)
            {
                writer.Write(RenderNativeCallbaseHelper(record, slot));
            }
        }

        writer.Write($$"""
            void connect_director({{string.Join(", ", slots.Select((_, index) => $"void* slot{index}"))}})
            {
            """);

        foreach (var (slot, index) in slots.Select((slot, index) => (slot, index)))
        {
            writer.WriteLine($"{GetNativeDirectorCallbackFieldName(index)} = reinterpret_cast<{GetNativeDirectorCallbackTypeName(index)}>(slot{index});");
        }

        writer.WriteLine("}");

        foreach (var (slot, index) in slots.Select((slot, index) => (slot, index)))
        {
            writer.WriteLine();
            writer.Write(RenderNativeDirectorMethod(slot, index));
        }

        writer.WriteLine();
        writer.WriteLine("private:");
        foreach (var (slot, index) in slots.Select((slot, index) => (slot, index)))
        {
            writer.WriteLine($"{GetNativeDirectorCallbackTypeName(index)} {GetNativeDirectorCallbackFieldName(index)} = nullptr;");
        }

        writer.Write(
            """
            };
            """);
        return writer.ToString();
    }

    private static string RenderNativeDirectorMethod(VirtualMethodSlotBinding slot, int index)
    {
        var method = slot.Method;
        var parameterList = string.Join(", ", method.Parameters.Select(parameter => $"{parameter.CppTypeName} {parameter.NativeName}"));
        var arguments = string.Join(", ", method.Parameters.Select(static parameter => parameter.NativeName));
        var callbackArguments = string.Join(", ", new[] { "const_cast<void*>(static_cast<const void*>(this))" }.Concat(method.Parameters.Select((parameter, parameterIndex) =>
            parameter.Type.PrimitiveKind == BindingPrimitiveKind.Boolean
                ? $"static_cast<std::uint8_t>(__abi_arg{parameterIndex})"
                : $"__abi_arg{parameterIndex}")));
        var writer = new CodeWriter();

        writer.Write($$"""
            {{method.CppReturnTypeName}} {{method.NativeName}}({{parameterList}}){{(method.IsConstMethod ? " const" : string.Empty)}} override
            {
            if ({{GetNativeDirectorCallbackFieldName(index)}} != nullptr)
            {
            """);

        foreach (var (parameter, parameterIndex) in method.Parameters.Select((parameter, parameterIndex) => (parameter, parameterIndex)))
        {
            writer.WriteLine($"    ParameterMarshaller<{parameter.CppTypeName}> __arg{parameterIndex}_marshaller;");
            writer.WriteLine($"    auto __abi_arg{parameterIndex} = __arg{parameterIndex}_marshaller.ConvertToAbi({parameter.NativeName});");
        }

        if (method.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void)
        {
            writer.WriteLine($"    {GetNativeDirectorCallbackFieldName(index)}({callbackArguments});");
            writer.WriteLine("    return;");
        }
        else
        {
            writer.WriteLine($"    ReturnMarshaller<{method.CppReturnTypeName}> __return_marshaller;");
            writer.WriteLine($"    auto __abi_result = {GetNativeDirectorCallbackFieldName(index)}({callbackArguments});");
            writer.WriteLine("    return __return_marshaller.ConvertToCpp(__abi_result);");
        }

        writer.WriteLine("}");
        if (method.IsPureVirtual)
        {
            writer.WriteLine($"throw std::runtime_error(\"SharpCpp callback for pure virtual '{slot.NativeImplementationRecord.NativeName}::{method.NativeName}' was not connected.\");");
        }
        else if (method.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void)
        {
            writer.WriteLine($"{slot.NativeImplementationRecord.CppQualifiedName}::{method.NativeName}({arguments});");
        }
        else
        {
            writer.WriteLine($"return {slot.NativeImplementationRecord.CppQualifiedName}::{method.NativeName}({arguments});");
        }

        writer.WriteLine("}");
        return writer.ToString();
    }

    private static string RenderNativeConnectDirector(BindingCompilation compilation, RecordBinding record, IReadOnlyList<VirtualMethodSlotBinding> slots)
    {
        var writer = new CodeWriter();
        var directorName = GetNativeDirectorClassName(record);
        var parameterList = string.Join(", ", new[] { $"arg_t<{record.CppQualifiedName}*> self" }.Concat(slots.Select((_, index) => $"void* slot{index}")));
        var arguments = string.Join(", ", slots.Select((_, index) => $"slot{index}"));

        writer.Write($$"""
            SHARPCPP_EXPORT ret_t<void> {{compilation.EntryPointPrefix}}_{{SanitizeNativeEntrypointName(record.CppQualifiedName)}}_connect_director({{parameterList}})
            {
            auto* director = dynamic_cast<{{directorName}}*>(self);
            if (director != nullptr)
            {
                director->connect_director({{arguments}});
            }
            }
            """);
        return writer.ToString();
    }

    private static string RenderNativeDelete(BindingCompilation compilation, RecordBinding record, bool requiresDirector)
    {
        var entryPoint = GetNativeDeleteEntryPointName(compilation, record);
        if (!requiresDirector)
        {
            return $$"""
                SHARPCPP_EXPORT ret_t<void> {{entryPoint}}(arg_t<{{record.CppQualifiedName}}*> self)
                {
                MarshallerInvoker::destroy<{{record.CppQualifiedName}}>(self);
                }
                """;
        }

        var directorName = GetNativeDirectorClassName(record);
        return $$"""
            SHARPCPP_EXPORT ret_t<void> {{entryPoint}}(arg_t<{{record.CppQualifiedName}}*> self)
            {
            if (auto* director = dynamic_cast<{{directorName}}*>(self))
            {
                delete director;
                return;
            }

            delete self;
            }
            """;
    }

    private static string RenderNativeArrayElement(BindingCompilation compilation, RecordBinding record)
    {
        return $$"""
            SHARPCPP_EXPORT ret_t<{{record.CppQualifiedName}}*> {{compilation.EntryPointPrefix}}_{{SanitizeNativeEntrypointName(record.CppQualifiedName)}}_array_element(arg_t<{{record.CppQualifiedName}}*> self, arg_t<std::size_t> index)
            {
            return self != nullptr ? self + index : nullptr;
            }
            """;
    }

    private static string RenderNativeClone(BindingCompilation compilation, RecordBinding record)
    {
        return $$"""
            SHARPCPP_EXPORT ret_t<{{record.CppQualifiedName}}*> {{GetNativeCloneEntryPointName(compilation, record)}}(arg_t<{{record.CppQualifiedName}}*> self)
            {
            Marshaller<{{record.CppQualifiedName}}*> self_marshaller;
            auto* instance = static_cast<{{record.CppQualifiedName}}*>(self_marshaller.ConvertToCpp(self));
            return detail::TryClone<{{record.CppQualifiedName}}>(instance, 0);
            }
            """;
    }

    private static string RenderNativeMethodInvocation(FunctionBinding method, string abiArguments, string memberSignature)
    {
        var argumentsSuffix = abiArguments.Length > 0 ? $", {abiArguments}" : string.Empty;
        var marshallerTemplateArguments = BuildMarshallerInvokerTemplateArguments("FT", method.Parameters.Select(static parameter => parameter.CppTypeName));
        var nativeName = method.IsConversionOperator ? $"operator {method.CppReturnTypeName}" : method.NativeName;

        if (string.IsNullOrWhiteSpace(method.NativeDispatchRecordCppTypeName))
        {
            return method.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void
                ? $$"""
                    MarshallerInvoker::invoke<{{marshallerTemplateArguments}}>(&{{method.OwningRecordCppTypeName}}::{{nativeName}}, self{{argumentsSuffix}});
                    """
                : RequiresOwnedRecordValueReturn(method)
                    ? $$"""
                        return MarshallerInvoker::invoke_owned<{{marshallerTemplateArguments}}>(&{{method.OwningRecordCppTypeName}}::{{nativeName}}, self{{argumentsSuffix}});
                        """
                : $$"""
                    return MarshallerInvoker::invoke<{{marshallerTemplateArguments}}>(&{{method.OwningRecordCppTypeName}}::{{nativeName}}, self{{argumentsSuffix}});
                    """;
        }

        return $$"""
                Marshaller<{{method.OwningRecordCppTypeName}}*> self_marshaller;
                auto* instance = static_cast<{{method.OwningRecordCppTypeName}}*>(self_marshaller.ConvertToCpp(self));
                auto* dispatch = dynamic_cast<{{method.NativeDispatchRecordCppTypeName}}*>(instance);
                if (dispatch == nullptr)
                {
                throw std::runtime_error("SharpCpp failed to adjust the native handle for secondary-base dispatch.");
                }

                using DispatchFT = {{memberSignature.Replace(method.OwningRecordCppTypeName + "::*", method.NativeDispatchRecordCppTypeName + "::*", StringComparison.Ordinal)}};
                {{(method.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void
                    ? $"MarshallerInvoker::invoke_instance<{BuildMarshallerInvokerTemplateArguments("DispatchFT", method.Parameters.Select(static parameter => parameter.CppTypeName))}>(static_cast<DispatchFT>(&{method.NativeDispatchRecordCppTypeName}::{nativeName}), dispatch{argumentsSuffix});"
                    : RequiresOwnedRecordValueReturn(method)
                        ? $"return MarshallerInvoker::invoke_owned_instance<{BuildMarshallerInvokerTemplateArguments("DispatchFT", method.Parameters.Select(static parameter => parameter.CppTypeName))}>(static_cast<DispatchFT>(&{method.NativeDispatchRecordCppTypeName}::{nativeName}), dispatch{argumentsSuffix});"
                        : $"return MarshallerInvoker::invoke_instance<{BuildMarshallerInvokerTemplateArguments("DispatchFT", method.Parameters.Select(static parameter => parameter.CppTypeName))}>(static_cast<DispatchFT>(&{method.NativeDispatchRecordCppTypeName}::{nativeName}), dispatch{argumentsSuffix});")}}
                """;
    }

    private static bool RequiresOwnedRecordValueReturn(FunctionBinding function)
    {
        return RequiresOwnedRecordValueReturn(function.ReturnType, function.CppReturnTypeName);
    }

    private static bool RequiresOwnedRecordValueReturn(BindingType returnType)
    {
        return RequiresOwnedRecordValueReturn(returnType, returnType.CppTypeName);
    }

    private static bool RequiresOwnedRecordValueReturn(BindingType returnType, string cppReturnTypeName)
    {
        return returnType.Kind == BindingTypeKind.Handle &&
            returnType.Marshaller.CppIsRecordValue &&
            !returnType.Marshaller.CppIsReference &&
            !IsPointerOrReferenceCppType(cppReturnTypeName);
    }

    private static bool IsPointerOrReferenceCppType(string cppTypeName)
    {
        var normalized = cppTypeName.Trim();
        return normalized.EndsWith('*') || normalized.EndsWith('&');
    }

    private static bool ReturnsOwnedHandle(FunctionBinding function)
    {
        return function.ReturnsOwnedHandle || RequiresOwnedRecordValueReturn(function);
    }

    private static bool ReturnsOwnedHandle(FunctionBinding function, BindingType returnType)
    {
        return function.ReturnsOwnedHandle || RequiresOwnedRecordValueReturn(returnType);
    }

    private static bool ReturnsOwnedHandle(
        FunctionBinding function,
        BindingType returnType,
        RecordGenericInstantiationBinding recordInstantiation)
    {
        return ReturnsOwnedHandle(function, returnType) ||
            function.OwnedReturnRecordInstantiations.Any(owned =>
                GenericInstantiationsEqual(owned, recordInstantiation));
    }

    private static bool GenericInstantiationsEqual(
        RecordGenericInstantiationBinding left,
        RecordGenericInstantiationBinding right)
    {
        return left.TypeArguments.Count == right.TypeArguments.Count &&
            left.TypeArguments.Zip(right.TypeArguments).All(static pair =>
                pair.First.Kind == pair.Second.Kind &&
                string.Equals(pair.First.CppTypeName, pair.Second.CppTypeName, StringComparison.Ordinal));
    }

    private static string GetNativeEntrypointReturnType(FunctionBinding function)
    {
        return GetNativeEntrypointReturnType(function.ReturnType, function.CppReturnTypeName);
    }

    private static string GetNativeEntrypointReturnType(BindingType returnType)
    {
        return GetNativeEntrypointReturnType(returnType, returnType.CppTypeName);
    }

    private static string GetNativeEntrypointReturnType(BindingType returnType, string cppReturnTypeName)
    {
        return RequiresOwnedRecordValueReturn(returnType, cppReturnTypeName)
            ? $"arg_t<{cppReturnTypeName}*>"
            : $"ret_t<{cppReturnTypeName}>";
    }

    private static string GetNativeDirectorClassName(RecordBinding record)
    {
        return $"__{SanitizeNativeEntrypointName(record.CppQualifiedName)}_director";
    }

    private static string BuildNativeDirectorCallbackSignature(FunctionBinding method)
    {
        var parameterList = string.Join(", ", new[] { "void* self" }.Concat(method.Parameters.Select(parameter =>
            parameter.Type.PrimitiveKind == BindingPrimitiveKind.Boolean
                ? "std::uint8_t"
                : $"arg_t<{GetNativeParameterMarshallerCppTypeName(parameter)}>")));
        var returnType = method.ReturnType.PrimitiveKind == BindingPrimitiveKind.Boolean
            ? "std::uint8_t"
            : $"ret_t<{method.CppReturnTypeName}>";
        return $"{returnType} (*)({parameterList})";
    }

    private static string GetNativeDirectorCallbackTypeName(int index)
    {
        return $"callback_{index}_t";
    }

    private static string GetNativeDirectorCallbackFieldName(int index)
    {
        return $"callback_{index}_";
    }

    private static string RenderNativeProtectedMethodHelper(RecordBinding record, FunctionBinding method)
    {
        ValidateProtectedMemberSupport(method);

        var parameterList = string.Join(", ", method.Parameters.Select(RenderNativeParameter));
        var abiArguments = string.Join(", ", method.Parameters.Select(static parameter => parameter.NativeName));
        var argumentsSuffix = abiArguments.Length > 0 ? $", {abiArguments}" : string.Empty;
        var writer = new CodeWriter();
        var helperName = GetNativeProtectedMethodHelperName(method);
        var directorName = GetNativeDirectorClassName(record);
        var signature = method.IsStatic
            ? BuildNativeFunctionSignature(method)
            : BuildNativeMethodSignature(method).Replace(method.OwningRecordCppTypeName + "::*", directorName + "::*", StringComparison.Ordinal);
        var callable = method.IsStatic
            ? $"static_cast<std::add_pointer_t<FT>>(&{directorName}::{method.NativeName})"
            : $"static_cast<FT>(&{directorName}::{method.NativeName})";
        var marshallerTemplateArguments = BuildMarshallerInvokerTemplateArguments("FT", method.Parameters.Select(static parameter => parameter.CppTypeName));
        var invocation = method.IsStatic
            ? $"MarshallerInvoker::{(RequiresOwnedRecordValueReturn(method) ? "invoke_owned_as" : "invoke_as")}<{marshallerTemplateArguments}>({callable}{argumentsSuffix})"
            : $"MarshallerInvoker::{(RequiresOwnedRecordValueReturn(method) ? "invoke_owned_instance" : "invoke_instance")}<{marshallerTemplateArguments}>({callable}, this{argumentsSuffix})";

        writer.Write($$"""
            {{(method.IsStatic ? "static " : string.Empty)}}{{GetNativeEntrypointReturnType(method)}} {{helperName}}({{parameterList}})
            {
            using FT = {{signature}};
            """);

        if (method.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void)
        {
            writer.WriteLine($"{invocation};");
        }
        else
        {
            writer.WriteLine($"return {invocation};");
        }

        writer.Write(
            """
            }
            """);
        return writer.ToString();
    }

    private static string RenderNativeProtectedFieldHelpers(BindingCompilation compilation, RecordBinding accessorRecord, FieldBinding field)
    {
        var writer = new CodeWriter();
        if (NeedsManagedFixedArrayProxy(field))
        {
            writer.Write($$"""
                {{GetNativeFixedArrayItemGetterReturnType(compilation, field)}} {{GetNativeProtectedFieldItemGetterHelperName(field)}}(std::size_t index)
                {
                {{RenderNativeFixedArrayItemGetInvocation(compilation, field, "this")}}
                }
                """);

            if (!field.IsReadOnly)
            {
                writer.WriteLine();
                writer.Write($$"""
                    void {{GetNativeProtectedFieldItemSetterHelperName(field)}}(std::size_t index, {{GetNativeFixedArrayItemSetterParameterType(compilation, field)}} value)
                    {
                    {{RenderNativeFixedArrayItemSetInvocation(compilation, field, "this")}}
                    }
                    """);
            }
        }
        else
        {
            var plan = CreateNativeFieldInteropPlan(compilation, field);
            var isNativeFixedArrayField = IsNativeFixedArrayType(compilation, field.Type);
            var getterReturnType = isNativeFixedArrayField ? "NativeArrayAbi" : $"ret_t<{plan.StorageCppType}>";
            var directorName = GetNativeDirectorClassName(accessorRecord);
            var getterBody = RenderNativeFieldGetInvocation(compilation, field, plan, "this", directorName);

            writer.Write($$"""
                {{getterReturnType}} {{GetNativeProtectedFieldGetterHelperName(field)}}()
                {
                {{getterBody}}
                }
                """);

            if (!field.IsReadOnly)
            {
                var setterParameterType = isNativeFixedArrayField ? "NativeArrayAbi" : plan.SetterParameterCppType;
                var setterBody = RenderNativeFieldSetInvocation(compilation, field, plan, "this", directorName);
                writer.WriteLine();
                writer.Write($$"""
                    void {{GetNativeProtectedFieldSetterHelperName(field)}}({{setterParameterType}} value)
                    {
                    {{setterBody}}
                    }
                    """);
            }
        }

        return writer.ToString();
    }

    private static string RenderNativeProtectedMethodInvocation(BindingCompilation compilation, FunctionBinding method, string abiArguments, string memberSignature)
    {
        var helperName = GetNativeProtectedMethodHelperName(method);
        var record = compilation.Records.First(candidate => string.Equals(candidate.CppQualifiedName, method.OwningRecordCppTypeName, StringComparison.Ordinal));
        return RenderNativeProtectedAccessInvocation(record, method.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void
            ? directorExpression => $"{directorExpression}->{helperName}({abiArguments}); return;"
            : directorExpression => $"return {directorExpression}->{helperName}({abiArguments});", "SharpCpp protected member access requires a director-backed instance.");
    }

    private static string RenderNativeProtectedFieldGetInvocation(RecordBinding accessorRecord, FieldBinding field)
    {
        var helperName = GetNativeProtectedFieldGetterHelperName(field);
        return RenderNativeProtectedAccessInvocation(accessorRecord, directorExpression => $"return {directorExpression}->{helperName}();", "SharpCpp protected field access requires a director-backed instance.");
    }

    private static string RenderNativeProtectedFieldSetInvocation(RecordBinding accessorRecord, FieldBinding field)
    {
        var helperName = GetNativeProtectedFieldSetterHelperName(field);
        return RenderNativeProtectedAccessInvocation(accessorRecord, directorExpression => $"{directorExpression}->{helperName}(value); return;", "SharpCpp protected field access requires a director-backed instance.");
    }

    private static string RenderNativeProtectedFieldItemGetInvocation(RecordBinding accessorRecord, FieldBinding field)
    {
        var helperName = GetNativeProtectedFieldItemGetterHelperName(field);
        return RenderNativeProtectedAccessInvocation(accessorRecord, directorExpression => $"return {directorExpression}->{helperName}(index);", "SharpCpp protected field access requires a director-backed instance.");
    }

    private static string RenderNativeProtectedFieldItemSetInvocation(RecordBinding accessorRecord, FieldBinding field)
    {
        var helperName = GetNativeProtectedFieldItemSetterHelperName(field);
        return RenderNativeProtectedAccessInvocation(accessorRecord, directorExpression => $"{directorExpression}->{helperName}(index, value); return;", "SharpCpp protected field access requires a director-backed instance.");
    }

    private static string RenderNativeProtectedAccessInvocation(
        RecordBinding accessorRecord,
        Func<string, string> renderDirectorBody,
        string errorMessage)
    {
        var writer = new CodeWriter();
        var directorName = GetNativeDirectorClassName(accessorRecord);
        var variableName = $"director_{SanitizeNativeEntrypointName(accessorRecord.CppQualifiedName)}";
        writer.Write($$"""
            if (auto* {{variableName}} = dynamic_cast<{{directorName}}*>(self))
            {
            {{renderDirectorBody(variableName)}}
            }

            """);

        writer.Write($"throw std::runtime_error(\"{errorMessage}\");");
        return writer.ToString();
    }

    private static IReadOnlyList<FunctionBinding> GetAccessibleProtectedMethods(BindingCompilation compilation, RecordBinding record)
    {
        return record.EnumerateAllBaseRecords(compilation)
            .Reverse()
            .Append(record)
            .SelectMany(static candidate => candidate.Members.OfType<FunctionBinding>())
            .Where(static function => !function.IsConstructor && function.AccessModifier == BindingAccessModifier.Protected && !function.HasMetadata(MetadataKeys.IgnoreReason))
            .GroupBy(GetNativeProtectedMethodHelperName, StringComparer.Ordinal)
            .Select(static group => group.Last())
            .ToArray();
    }

    private static IReadOnlyList<FieldBinding> GetAccessibleProtectedFields(BindingCompilation compilation, RecordBinding record)
    {
        return record.EnumerateAllBaseRecords(compilation)
            .Reverse()
            .Append(record)
            .SelectMany(static candidate => candidate.Members.OfType<FieldBinding>())
            .Where(static field => field.AccessModifier == BindingAccessModifier.Protected && !field.HasMetadata(MetadataKeys.IgnoreReason))
            .GroupBy(GetNativeProtectedFieldGetterHelperName, StringComparer.Ordinal)
            .Select(static group => group.Last())
            .ToArray();
    }

    private static IReadOnlyList<FieldBinding> GetInheritedProtectedFields(BindingCompilation compilation, RecordBinding record)
    {
        if (!record.RequiresDirector(compilation))
        {
            return Array.Empty<FieldBinding>();
        }

        return record.EnumerateAllBaseRecords(compilation)
            .Reverse()
            .SelectMany(static candidate => candidate.Members.OfType<FieldBinding>())
            .Where(static field => field.AccessModifier == BindingAccessModifier.Protected && !field.HasMetadata(MetadataKeys.IgnoreReason))
            .GroupBy(GetNativeProtectedFieldGetterHelperName, StringComparer.Ordinal)
            .Select(static group => group.Last())
            .ToArray();
    }

    private static IEnumerable<(RecordBinding AccessorRecord, FieldBinding Field)> GetNativeFieldEntries(BindingCompilation compilation, RecordBinding record)
    {
        foreach (var field in record.Members.OfType<FieldBinding>())
        {
            yield return (record, field);
        }

        foreach (var field in GetInheritedProtectedFields(compilation, record))
        {
            if (record.Members.OfType<FieldBinding>().Any(ownField => string.Equals(GetNativeProtectedFieldGetterHelperName(ownField), GetNativeProtectedFieldGetterHelperName(field), StringComparison.Ordinal)))
            {
                continue;
            }

            yield return (record, field);
        }
    }

    // The record token is length-prefixed: joining two identifiers with '_' is ambiguous
    // (record a_b field c vs flattened record a field b_c).
    private static string GetNativeFieldEntrypointStem(BindingCompilation compilation, string recordCppTypeName, FieldBinding field)
    {
        var recordToken = SanitizeNativeEntrypointName(recordCppTypeName);
        return $"{compilation.EntryPointPrefix}_{recordToken.Length}_{recordToken}_{field.NativeName}";
    }

    private static string GetNativeFieldGetterEntrypointName(BindingCompilation compilation, string recordCppTypeName, FieldBinding field)
    {
        return $"{GetNativeFieldEntrypointStem(compilation, recordCppTypeName, field)}_get";
    }

    private static string GetNativeFieldItemGetterEntrypointName(BindingCompilation compilation, string recordCppTypeName, FieldBinding field)
    {
        return $"{GetNativeFieldEntrypointStem(compilation, recordCppTypeName, field)}_item_get";
    }

    private static string GetNativeFieldSetterEntrypointName(BindingCompilation compilation, string recordCppTypeName, FieldBinding field)
    {
        return $"{GetNativeFieldEntrypointStem(compilation, recordCppTypeName, field)}_set";
    }

    private static string GetNativeFieldItemSetterEntrypointName(BindingCompilation compilation, string recordCppTypeName, FieldBinding field)
    {
        return $"{GetNativeFieldEntrypointStem(compilation, recordCppTypeName, field)}_item_set";
    }

    private enum FieldSetterMode
    {
        ValuePointer,
        ProjectedValuePointer,
        ValueAbi,
        HandleAbi,
        BorrowedStorage
    }

    private sealed record NativeFieldInteropPlan(
        string StorageCppType,
        bool BorrowsStorage,
        FieldSetterMode SetterMode)
    {
        public string SetterParameterCppType => SetterMode switch
        {
            FieldSetterMode.ProjectedValuePointer => $"arg_t<{StorageCppType}>*",
            FieldSetterMode.ValueAbi => $"arg_t<{StorageCppType}>",
            FieldSetterMode.HandleAbi => $"arg_t<{StorageCppType}>",
            _ => $"field_t<{StorageCppType}>"
        };
    }

    private static NativeFieldInteropPlan CreateNativeFieldInteropPlan(BindingCompilation compilation, FieldBinding field)
    {
        var storageCppType = GetNativeFieldCppType(compilation, field);
        var borrowsStorage = ShouldBorrowFieldStorage(compilation, field);
        var setterMode = borrowsStorage
            ? FieldSetterMode.BorrowedStorage
            : field.Type.Kind == BindingTypeKind.Enum
                ? FieldSetterMode.ValueAbi
            : field.Type.Kind == BindingTypeKind.Handle
                ? FieldSetterMode.HandleAbi
                : RequiresNativeFieldValueConversion(compilation, field)
                    ? FieldSetterMode.ProjectedValuePointer
                    : FieldSetterMode.ValuePointer;

        return new NativeFieldInteropPlan(storageCppType, borrowsStorage, setterMode);
    }

    private static bool RequiresNativeFieldValueConversion(BindingCompilation compilation, FieldBinding field)
    {
        if (field.Type.ManagedMarshaller is not null)
        {
            return true;
        }

        var declaredCppTypeName = NormalizeCppTypeName(field.CppTypeName);
        var mappedCppTypeName = NormalizeCppTypeName(field.Type.CppTypeName);
        return compilation.Records.Any(record =>
            record.GetMetadata<bool>(MetadataKeys.SuppressEmission) &&
            (string.Equals(record.CppQualifiedName, declaredCppTypeName, StringComparison.Ordinal) ||
                string.Equals(record.NativeName, declaredCppTypeName, StringComparison.Ordinal) ||
                string.Equals(record.CppQualifiedName, mappedCppTypeName, StringComparison.Ordinal) ||
                string.Equals(record.NativeName, mappedCppTypeName, StringComparison.Ordinal)));
    }

    private static string RenderNativeFieldGetInvocation(BindingCompilation compilation, FieldBinding field, NativeFieldInteropPlan plan, string selfExpression, string? fieldOwnerCppTypeName = null)
    {
        if (IsNativeFixedArrayType(compilation, field.Type))
        {
            var arrayAccessExpression = GetNativeFixedArrayFieldAccessExpression(field, selfExpression);
            return $"return {{ const_cast<void*>(static_cast<const void*>({arrayAccessExpression})), {field.Type.FixedArrayLength ?? 0} }};";
        }

        if (TryGetNativeFieldAccessExpression(field, selfExpression, out var accessExpression))
        {
            if (field.GetMetadata<bool>(MetadataKeys.FunctionPointerField))
                return $"return reinterpret_cast<void*>({accessExpression});";

            if (plan.BorrowsStorage)
            {
                return field.IsReadOnly
                    ? $"return const_cast<std::remove_const_t<std::remove_reference_t<{plan.StorageCppType}>>*>(&({accessExpression}));"
                    : $"return &({accessExpression});";
            }

            return $$"""
                ReturnMarshaller<{{plan.StorageCppType}}> marshaller;
                return marshaller.ConvertToAbi({{accessExpression}});
                """;
        }

        var fieldOwner = fieldOwnerCppTypeName ?? field.OwningRecordCppTypeName;
        var borrowedStorageTag = plan.BorrowsStorage ? ", std::true_type{}" : string.Empty;
        var invocation = $"MarshallerInvoker::get_field<{plan.StorageCppType}>(&{fieldOwner}::{field.NativeName}, {selfExpression}{borrowedStorageTag})";
        return plan.BorrowsStorage && field.IsReadOnly
            ? $"return const_cast<std::remove_const_t<std::remove_reference_t<{plan.StorageCppType}>>*>({invocation});"
            : $"return {invocation};";
    }

    private static string RenderNativeFieldSetInvocation(BindingCompilation compilation, FieldBinding field, NativeFieldInteropPlan plan, string selfExpression, string? fieldOwnerCppTypeName = null)
    {
        if (IsNativeFixedArrayType(compilation, field.Type))
        {
            var arrayAccessExpression = GetNativeFixedArrayFieldAccessExpression(field, selfExpression);
            var elementCppType = GetNativeFixedArrayElementCppType(compilation, field.Type);
            var length = field.Type.FixedArrayLength ?? 0;
            // NativeArrayAbi.handle for a whole native fixed-array field always points at raw
            // cpp_type[N] storage (see the getter below, and the shared NativeArray<T> array_get/
            // array_set accessors), never at abi_type[N]: crossing the abi boundary happens
            // per element, lazily, on the managed NativeArray<T> wrapper's own accessors. So the
            // incoming buffer is already cpp_type here and a plain element copy is correct for
            // every element kind that reaches this path (primitives, enums, and configured/
            // projected value types alike) -- routing it through ParameterMarshaller<T>::ConvertToCpp
            // (which expects an abi_type argument) was only ever an accident for kinds where
            // cpp_type and abi_type happen to coincide.
            return $$"""
                if (value.handle == nullptr || value.length != {{length}})
                {
                    throw std::runtime_error("SharpCpp fixed array field assignment length mismatch.");
                }

                auto* source = static_cast<{{elementCppType}}*>(value.handle);
                for (std::size_t index = 0; index < {{length}}; ++index)
                {
                    {{arrayAccessExpression}}[index] = source[index];
                }
                """;
        }

        if (TryGetNativeFieldAccessExpression(field, selfExpression, out var accessExpression))
        {
            if (field.GetMetadata<bool>(MetadataKeys.FunctionPointerField))
                return $"{accessExpression} = reinterpret_cast<decltype({accessExpression})>(value);";

            if (plan.SetterMode == FieldSetterMode.BorrowedStorage)
                return $"detail::AssignBorrowedStorage({accessExpression}, value);";

            var valueExpression = plan.SetterMode switch
            {
                FieldSetterMode.ProjectedValuePointer => "marshaller.ConvertToCpp(*value)",
                FieldSetterMode.ValueAbi => "marshaller.ConvertToCpp(value)",
                FieldSetterMode.ValuePointer => "*value",
                _ => "value"
            };
            if (ShouldCastDirectNativeFieldSetValue(field, plan))
            {
                valueExpression = $"static_cast<decltype({accessExpression})>({valueExpression})";
            }

            return plan.SetterMode is FieldSetterMode.ProjectedValuePointer or FieldSetterMode.ValueAbi
                ? $"ParameterMarshaller<{plan.StorageCppType}> marshaller;\n{accessExpression} = {valueExpression};"
                : $"{accessExpression} = {valueExpression};";
        }

        var fieldOwner = fieldOwnerCppTypeName ?? field.OwningRecordCppTypeName;
        var borrowedStorageTag = plan.SetterMode == FieldSetterMode.BorrowedStorage ? ", std::true_type{}" : string.Empty;
        return $"MarshallerInvoker::set_field<{plan.StorageCppType}>(&{fieldOwner}::{field.NativeName}, {selfExpression}, value{borrowedStorageTag});";
    }

    private static string RenderNativeFixedArrayItemGetInvocation(BindingCompilation compilation, FieldBinding field, string selfExpression)
    {
        var arrayAccessExpression = GetNativeFixedArrayFieldAccessExpression(field, selfExpression);
        var elementType = field.Type.FixedArrayElementType
            ?? throw new InvalidOperationException($"Fixed array field '{field.ManagedName}' is missing element type metadata.");

        if (elementType.IsFixedArray)
        {
            return $"return {{ const_cast<void*>(static_cast<const void*>({arrayAccessExpression}[index])), {elementType.FixedArrayLength ?? 0} }};";
        }

        var elementCppType = GetNativeFixedArrayElementCppType(compilation, elementType);
        var marshallerCppType = elementType.Marshaller.CppIsRecordValue
            ? $"{elementCppType}&"
            : elementCppType;
        return $$"""
            ReturnMarshaller<{{marshallerCppType}}> marshaller;
            return marshaller.ConvertToAbi({{arrayAccessExpression}}[index]);
            """;
    }

    private static string RenderNativeFixedArrayItemSetInvocation(BindingCompilation compilation, FieldBinding field, string selfExpression)
    {
        var arrayAccessExpression = GetNativeFixedArrayFieldAccessExpression(field, selfExpression);
        var elementType = field.Type.FixedArrayElementType
            ?? throw new InvalidOperationException($"Fixed array field '{field.ManagedName}' is missing element type metadata.");

        if (elementType.IsFixedArray)
        {
            var nestedElementCppType = GetNativeFixedArrayElementCppType(compilation, elementType);
            var nestedLength = elementType.FixedArrayLength ?? 0;
            return $$"""
                if (value.handle == nullptr || value.length != {{nestedLength}})
                {
                    throw std::runtime_error("SharpCpp fixed array element assignment length mismatch.");
                }

                auto& target = {{arrayAccessExpression}}[index];
                auto* source = reinterpret_cast<{{nestedElementCppType}} (*)[{{nestedLength}}]>(value.handle);
                for (std::size_t nestedIndex = 0; nestedIndex < {{nestedLength}}; ++nestedIndex)
                {
                    target[nestedIndex] = (*source)[nestedIndex];
                }
                """;
        }

        var elementCppType = GetNativeFixedArrayElementCppType(compilation, elementType);
        return $$"""
            ParameterMarshaller<{{elementCppType}}> marshaller;
            {{arrayAccessExpression}}[index] = marshaller.ConvertToCpp(value);
            """;
    }

    private static string GetNativeFixedArrayItemGetterReturnType(BindingCompilation compilation, FieldBinding field)
    {
        var elementType = field.Type.FixedArrayElementType
            ?? throw new InvalidOperationException($"Fixed array field '{field.ManagedName}' is missing element type metadata.");
        if (elementType.IsFixedArray)
        {
            return "NativeArrayAbi";
        }

        var elementCppType = GetNativeFixedArrayElementCppType(compilation, elementType);
        return elementType.Marshaller.CppIsRecordValue
            ? $"ret_t<{elementCppType}&>"
            : $"ret_t<{elementCppType}>";
    }

    private static string GetNativeFixedArrayItemSetterParameterType(BindingCompilation compilation, FieldBinding field)
    {
        var elementType = field.Type.FixedArrayElementType
            ?? throw new InvalidOperationException($"Fixed array field '{field.ManagedName}' is missing element type metadata.");
        return elementType.IsFixedArray
            ? "NativeArrayAbi"
            : $"arg_t<{GetNativeFixedArrayElementCppType(compilation, elementType)}>";
    }

    private static string GetNativeFixedArrayFieldAccessExpression(FieldBinding field, string selfExpression)
    {
        return TryGetNativeFieldAccessExpression(field, selfExpression, out var accessExpression)
            ? accessExpression
            : $"{selfExpression}->{field.NativeName}";
    }

    private static bool TryGetNativeFieldAccessExpression(FieldBinding field, string selfExpression, out string accessExpression)
    {
        if (field.IsStatic)
        {
            accessExpression = $"{field.OwningRecordCppTypeName}::{field.NativeName}";
            return true;
        }

        if (field.GetMetadata<bool>(MetadataKeys.BitField))
        {
            accessExpression = $"{selfExpression}->{field.NativeName}";
            return true;
        }

        if (field.TryGetMetadata<string>(MetadataKeys.NativeFieldAccessExpression, out var fieldAccessPath) &&
            !string.IsNullOrWhiteSpace(fieldAccessPath))
        {
            accessExpression = $"{selfExpression}->{fieldAccessPath}";
            return true;
        }

        accessExpression = string.Empty;
        return false;
    }

    private static bool ShouldCastDirectNativeFieldSetValue(FieldBinding field, NativeFieldInteropPlan plan)
    {
        return plan.SetterMode == FieldSetterMode.ValuePointer &&
            field.IsPointer &&
            field.Type.Kind == BindingTypeKind.Primitive &&
            field.Type.PrimitiveKind == BindingPrimitiveKind.IntPtr &&
            string.Equals(plan.StorageCppType, BindingType.IntPtr.CppTypeName, StringComparison.Ordinal);
    }

    private static string GetNativeProtectedMethodHelperName(FunctionBinding method)
    {
        return $"__call_{SanitizeManagedToken(method.ImportMethodName)}";
    }

    private static string GetNativeProtectedFieldGetterHelperName(FieldBinding field)
    {
        return $"__get_{SanitizeManagedToken(field.ManagedName)}";
    }

    private static string GetNativeProtectedFieldItemGetterHelperName(FieldBinding field)
    {
        return $"__get_{SanitizeManagedToken(field.ManagedName)}_item";
    }

    private static string GetNativeProtectedFieldSetterHelperName(FieldBinding field)
    {
        return $"__set_{SanitizeManagedToken(field.ManagedName)}";
    }

    private static string GetNativeProtectedFieldItemSetterHelperName(FieldBinding field)
    {
        return $"__set_{SanitizeManagedToken(field.ManagedName)}_item";
    }

    private static void ValidateProtectedMemberSupport(FunctionBinding method)
    {
        if (method.Parameters.Any(parameter => parameter.Direction is not BindingParameterDirection.In || parameter.AbiKind is not BindingParameterAbiKind.Value))
        {
            throw new NotSupportedException($"Protected member '{method.ManagedName}' only supports in-value parameters in this slice.");
        }
    }

    private static string RenderNativeCallbaseHelper(RecordBinding record, VirtualMethodSlotBinding slot)
    {
        var method = slot.Method;
        var parameterList = string.Join(", ", method.Parameters.Select(parameter => $"{parameter.CppTypeName} {parameter.NativeName}"));
        var arguments = string.Join(", ", method.Parameters.Select(static parameter => parameter.NativeName));
        var directorName = GetNativeDirectorClassName(record);
        var writer = new CodeWriter();

        writer.Write($$"""
            static {{method.CppReturnTypeName}} {{GetNativeCallbaseHelperName(method)}}({{record.CppQualifiedName}}* self{{(method.Parameters.Count > 0 ? $", {parameterList}" : string.Empty)}})
            {
            auto* director = static_cast<{{directorName}}*>(self);
            """);

        if (method.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void)
        {
            writer.WriteLine($"    director->{slot.NativeImplementationRecord.CppQualifiedName}::{method.NativeName}({arguments});");
        }
        else
        {
            writer.WriteLine($"    return director->{slot.NativeImplementationRecord.CppQualifiedName}::{method.NativeName}({arguments});");
        }

        writer.Write(
            """
            }
            """);
        return writer.ToString();
    }

    private static string GetNativeCallbaseHelperName(FunctionBinding method)
    {
        return $"__callbase_{SanitizeManagedToken(method.ImportMethodName)}";
    }

    private static string GetNativeCallbaseEntrypointName(BindingCompilation compilation, RecordBinding record, FunctionBinding method)
    {
        return $"{compilation.EntryPointPrefix}_{record.NativeName}_callbase_{method.ImportMethodName}";
    }

    private static string GetManagedCallbaseImportMethodName(FunctionBinding function)
    {
        return $"__CallBase_{function.ImportMethodName}";
    }


    private static string RenderManagedWrapperMethod(BindingCompilation compilation, FunctionBinding function)
    {
        var writer = new CodeWriter();
        WriteDocumentationComment(writer, function);
        var returnTypeName = GetManagedPublicReturnTypeName(function.ReturnType, compilation);
        var importMethodName = GetManagedImportMethodName(function);

        if (function.Parameters.Any(IsInputPointerArrayParameter))
        {
            writer.Write(RenderManagedPointerArrayFunction(
                compilation,
                function,
                "public static",
                returnTypeName,
                importMethodName,
                []));
            return writer.ToString();
        }

        if (HasUndefaultableParams(function))
        {
            WriteUndefaultableOverloads(compilation, writer, function, importMethodName, []);
            return writer.ToString();
        }

        if (HasUnexpressibleDefaultParams(function))
        {
            WriteManagedUnexpressibleDefaultConvenienceOverloads(
                writer,
                compilation,
                function,
                signaturePrefix: $"public static {returnTypeName} {function.ManagedName}",
                invocationPrefix: string.Empty,
                invocationTarget: function.ManagedName,
                returnTypeName: returnTypeName,
                isConstructor: false);
        }

        var parameterList = RenderManagedPublicParameterList(function.Parameters, compilation);
        var invocationPlan = BuildManagedInvocationPlan(compilation, function.Parameters);
        var invokeArguments = string.Join(", ", invocationPlan.Arguments);

        writer.Write($$"""
            public static {{returnTypeName}} {{function.ManagedName}}({{parameterList}})
            {
            """);

        if (HasNativeDefaultDispatchParams(function))
        {
            // The cascade writes its own pre-call statements.
            WriteUnexpressibleDefaultCascade(compilation, writer, function, importMethodName, []);
        }
        else
        {
            foreach (var statement in invocationPlan.PreCallStatements)
            {
                writer.Write(statement);
            }

        if (function.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void)
        {
            writer.Write($$"""
                __PInvoke.{{importMethodName}}({{invokeArguments}});
                """);

            foreach (var statement in invocationPlan.PostCallStatements)
            {
                writer.Write(statement);
            }
        }
        else if (function.ReturnType.Kind == BindingTypeKind.Handle)
        {
            writer.Write($$"""
                var __result = __PInvoke.{{importMethodName}}({{invokeArguments}});
                """);

            foreach (var statement in invocationPlan.PostCallStatements)
            {
                writer.Write(statement);
            }

            writer.Write($$"""
                return {{BuildManagedHandleReturnResultExpression(compilation, function.ReturnType, "__result", ReturnsOwnedHandle(function))}};
                """);
        }
        else if (function.ReturnType.MarshallerTypeName is not null)
        {
            WriteManagedReturnWithMarshallerConversion(compilation, writer, function, importMethodName, invokeArguments, invocationPlan);
        }
        else
        {
            writer.Write($$"""
                var __result = __PInvoke.{{importMethodName}}({{invokeArguments}});
                """);

            foreach (var statement in invocationPlan.PostCallStatements)
            {
                writer.Write(statement);
            }

            writer.Write($$"""
                return {{BuildManagedImportToPublicExpression(compilation, function.ReturnType, "__result")}};
                """);
        }

        }

        writer.Write(
            """
            }
            """);
        return writer.ToString();
    }

    private static bool TryGetRecordBufferOverloadParameters(
        FunctionBinding function,
        out ParameterBinding buffer,
        out ParameterBinding capacity)
    {
        buffer = null!;
        capacity = null!;
        if (function.IsGeneric ||
            function.IsConstructor ||
            function.OperatorToken is not null ||
            function.ReturnType.Kind is not (BindingTypeKind.Primitive or BindingTypeKind.Enum) ||
            function.Parameters.Any(IsInputPointerArrayParameter))
        {
            return false;
        }

        foreach (var parameter in function.Parameters)
        {
            if (parameter.GetMetadata<string>(MetadataKeys.RecordBufferCapacityParameter) is not { Length: > 0 } capacityName)
            {
                continue;
            }

            var capacityParameter = function.Parameters.FirstOrDefault(candidate =>
                string.Equals(candidate.NativeName, capacityName, StringComparison.Ordinal));
            if (capacityParameter is null)
            {
                return false;
            }

            buffer = parameter;
            capacity = capacityParameter;
            return true;
        }

        return false;
    }

    private static string RenderManagedRecordBufferOverload(
        BindingCompilation compilation,
        FunctionBinding function,
        string declarationPrefix,
        string returnTypeName,
        string importMethodName,
        bool isInstance)
    {
        if (!TryGetRecordBufferOverloadParameters(function, out var buffer, out var capacity))
        {
            return string.Empty;
        }

        var elementTypeName = GetManagedWrapperTypeName(buffer.Type, compilation).TrimEnd('?');
        var bufferName = buffer.ManagedName;
        var bufferTypeName = $"global::SharpCpp.NativeArray<{elementTypeName}>";
        var parameterList = string.Join(
            ", ",
            function.Parameters.Select(parameter => ReferenceEquals(parameter, buffer)
                ? $"{bufferTypeName} {bufferName}"
                : RenderManagedPublicParameterRequired(parameter, compilation)));

        var arguments = new List<string>();
        var preCallStatements = new List<string>();
        var postCallStatements = new List<string>();
        if (isInstance)
        {
            arguments.Add("this");
        }

        foreach (var parameter in function.Parameters)
        {
            if (ReferenceEquals(parameter, buffer))
            {
                arguments.Add("__bufferHandle");
                continue;
            }

            var plan = BuildManagedInvocationPlan(compilation, [parameter]);
            arguments.AddRange(plan.Arguments);
            preCallStatements.AddRange(plan.PreCallStatements);
            postCallStatements.AddRange(plan.PostCallStatements);
        }

        var invokeArguments = string.Join(", ", arguments);
        var nativeImportName = GetRecordBufferImportMethodName(importMethodName);
        var capacityName = capacity.ManagedName;
        var writer = new CodeWriter();
        writer.Write($$"""
            [global::System.Runtime.CompilerServices.OverloadResolutionPriority(1)]
            {{declarationPrefix}} {{returnTypeName}} {{function.ManagedName}}({{parameterList}})
            {
            """);

        foreach (var statement in preCallStatements)
        {
            writer.Write(statement);
        }

        writer.Write($$"""
            nint __bufferHandle = 0;
            if ({{bufferName}} is null || {{bufferName}}.IsNull)
            {
                if ({{capacityName}} != 0)
                {
                    throw new global::System.ArgumentNullException(nameof({{bufferName}}), "A buffer is required when the capacity is not zero.");
                }
            }
            else
            {
                if (unchecked((ulong){{capacityName}}) > (ulong){{bufferName}}.Length)
                {
                    throw new global::System.ArgumentOutOfRangeException(nameof({{capacityName}}), {{capacityName}}, "The capacity exceeds the length of the buffer.");
                }

                __bufferHandle = {{bufferName}}.Handle;
            }
            """);

        if (function.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void)
        {
            writer.Write($$"""
                __PInvoke.{{nativeImportName}}({{invokeArguments}});
                """);
        }
        else
        {
            writer.Write($$"""
                var __result = __PInvoke.{{nativeImportName}}({{invokeArguments}});
                """);
        }

        foreach (var statement in postCallStatements)
        {
            writer.Write(statement);
        }

        writer.Write($"global::System.GC.KeepAlive({bufferName});");
        if (function.ReturnType.PrimitiveKind != BindingPrimitiveKind.Void)
        {
            writer.Write($$"""
                return {{BuildManagedImportToPublicExpression(compilation, function.ReturnType, "__result")}};
                """);
        }

        writer.Write(
            """
            }
            """);
        return writer.ToString();
    }

    private static string GetRecordBufferImportMethodName(string importMethodName) => $"{importMethodName}NativeArray";

    private static string RenderManagedRecordBufferImportDeclaration(
        BindingCompilation compilation,
        FunctionBinding function,
        string? selfTypeName)
    {
        if (!TryGetRecordBufferOverloadParameters(function, out var buffer, out _))
        {
            return string.Empty;
        }

        var parameters = function.Parameters.Select(parameter => ReferenceEquals(parameter, buffer)
            ? $"nint {parameter.ManagedName}"
            : RenderManagedImportParameter(parameter, compilation));
        if (selfTypeName is not null)
        {
            parameters = new[] { $"{selfTypeName} self" }.Concat(parameters);
        }

        var parameterList = string.Join(", ", parameters);
        var returnTypeName = GetManagedImportReturnTypeName(compilation, function.ReturnType);
        var returnAttribute = GetManagedImportReturnAttribute(compilation, function.ReturnType, ReturnsOwnedHandle(function));
        var importMethodName = GetRecordBufferImportMethodName(GetManagedImportMethodName(function));
        var unsafeModifier = parameterList.Contains('*', StringComparison.Ordinal) ? "unsafe " : string.Empty;
        var importMethodLine = string.IsNullOrWhiteSpace(returnAttribute)
            ? $"internal static {unsafeModifier}partial {returnTypeName} {importMethodName}({parameterList});"
            : $"{returnAttribute}\ninternal static {unsafeModifier}partial {returnTypeName} {importMethodName}({parameterList});";

        var writer = new CodeWriter();
        writer.Write(
            $$"""
            [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "{{function.EntryPointName}}", StringMarshalling = StringMarshalling.Utf8)]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            {{importMethodLine}}
            """);
        return writer.ToString();
    }

    private static string RenderManagedImportDeclaration(BindingCompilation compilation, FunctionBinding function)
    {
        var writer = new CodeWriter();
        var parameterList = string.Join(
            ", ",
            function.Parameters.Select(parameter => RenderManagedImportParameter(parameter, compilation)));
        var returnTypeName = GetManagedImportReturnTypeName(compilation, function.ReturnType);
        var returnAttribute = GetManagedImportReturnAttribute(compilation, function.ReturnType, ReturnsOwnedHandle(function));
        var importMethodName = GetManagedImportMethodName(function);
        var unsafeModifier = parameterList.Contains('*', StringComparison.Ordinal) ? "unsafe " : string.Empty;
        var importMethodLine = string.IsNullOrWhiteSpace(returnAttribute)
            ? $"internal static {unsafeModifier}partial {returnTypeName} {importMethodName}({parameterList});"
            : $"{returnAttribute}\ninternal static {unsafeModifier}partial {returnTypeName} {importMethodName}({parameterList});";

        writer.Write(
            $$"""
            [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "{{function.EntryPointName}}", StringMarshalling = StringMarshalling.Utf8)]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            {{importMethodLine}}
            """);
        return writer.ToString();
    }

    private static IEnumerable<ParameterBinding> CollectCallbackParameters(
        IEnumerable<RecordBinding> records,
        IEnumerable<FunctionBinding> functions)
    {
        return functions
            .Where(static function => !function.IsIgnored())
            .SelectMany(static function => function.Parameters.Where(static parameter => parameter.Type.IsCallback))
            .Concat(records.SelectMany(static record => record.Members
                .OfType<FunctionBinding>()
                .Where(static function => !function.IsIgnored())
                .SelectMany(static function => function.Parameters.Where(static parameter => parameter.Type.IsCallback))))
            .Concat(records.SelectMany(static record => record.Members
                .OfType<FunctionBinding>()
                .Where(static function => !function.IsIgnored() && function.IsGeneric)
                .SelectMany(function => CollectGenericMethodCallbackParameters(record, function))));
    }

    private static IEnumerable<ParameterBinding> CollectGenericMethodCallbackParameters(RecordBinding record, FunctionBinding function)
    {
        var methodParams = (IReadOnlyList<BindingGenericParameter>)function.GenericParameters.ToList();
        var recordInstantiations = record.IsGeneric
            ? record.SupportedGenericInstantiations
            : [EmptyRecordGenericInstantiation()];

        foreach (var recordInstantiation in recordInstantiations)
        {
            foreach (var methodInstantiation in function.SupportedGenericInstantiations)
            {
                foreach (var parameter in function.Parameters)
                {
                    var concreteType = SubstituteGenericType(record, parameter.Type, recordInstantiation, methodParams, methodInstantiation);
                    if (concreteType.IsCallback)
                    {
                        yield return CreateConcreteParameter(parameter, concreteType);
                    }
                }
            }
        }
    }

    private static string RenderManagedCallbackSupport(BindingCompilation compilation, IReadOnlyList<ParameterBinding> callbackParameters)
    {
        var writer = new CodeWriter();
        var delegateDeclarations = CollectManagedCallbackDelegateDeclarations(callbackParameters).ToArray();
        var usages = CollectManagedCallbackUsages(compilation, callbackParameters).ToArray();

        foreach (var declaration in delegateDeclarations.OrderBy(static declaration => declaration.DeclarationName, StringComparer.Ordinal))
        {
            writer.Write(RenderManagedCallbackDelegateDeclaration(compilation, declaration));
        }

        if (usages.Length > 0)
        {
            if (delegateDeclarations.Length > 0)
            {
                writer.WriteLine();
            }

            writer.Write(
                $$"""
                [StructLayout(LayoutKind.Sequential)]
                internal readonly unsafe struct __SharpCppManagedCallbackHandleAbi
                {
                    public __SharpCppManagedCallbackHandleAbi(nint state, nint callback)
                    {
                        State = state;
                        Callback = callback;
                    }

                    public nint State { get; }

                    public nint Callback { get; }
                }

                private static unsafe void __RegisterManagedCallbackRelease()
                {
                    __PInvoke.__SetManagedCallbackRelease(
                        (nint)(delegate* unmanaged[Cdecl]<nint, void>)&__ReleaseManagedCallbackState);
                }

                [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
                private static void __ReleaseManagedCallbackState(nint state)
                {
                    if (state != 0)
                    {
                        global::System.Runtime.InteropServices.GCHandle.FromIntPtr(state).Free();
                    }
                }

                internal static partial class __PInvoke
                {
                    [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "{{GetManagedCallbackReleaseRegistrationEntryPointName(compilation)}}", StringMarshalling = StringMarshalling.Utf8)]
                    [MethodImpl(MethodImplOptions.AggressiveInlining)]
                    internal static partial void __SetManagedCallbackRelease(nint release);
                }
                """);

            foreach (var usage in usages.OrderBy(static usage => usage.MarshallerDeclarationName, StringComparer.Ordinal))
            {
                writer.WriteLine();
                writer.Write(RenderManagedCallbackMarshallerHelper(compilation, usage));
            }
        }

        return writer.ToString();
    }

    private static IEnumerable<ManagedCallbackDelegateDeclaration> CollectManagedCallbackDelegateDeclarations(IReadOnlyList<ParameterBinding> callbackParameters)
    {
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var parameter in callbackParameters.Where(static parameter => parameter.UseCallbackDelegate))
        {
            var declarationName = GetGeneratedCallbackDelegateDeclarationName(parameter);
            if (declarationName is null)
            {
                continue;
            }

            var signatureKey = GetCallbackSignatureKey(parameter.Type);
            if (seen.TryGetValue(declarationName, out var existingSignatureKey))
            {
                if (!string.Equals(existingSignatureKey, signatureKey, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Callback delegate '{declarationName}' is requested with incompatible callback signatures.");
                }

                continue;
            }

            seen.Add(declarationName, signatureKey);
            yield return new ManagedCallbackDelegateDeclaration(declarationName, parameter.Type);
        }
    }

    private static IEnumerable<ManagedCallbackUsage> CollectManagedCallbackUsages(BindingCompilation compilation, IReadOnlyList<ParameterBinding> callbackParameters)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parameter in callbackParameters.Where(static parameter => parameter.Type.ManagedMarshaller is null))
        {
            var usage = GetManagedCallbackUsage(compilation, parameter);
            if (seen.Add($"{usage.MarshallerDeclarationName}|{usage.ManagedTypeName}|{GetCallbackSignatureKey(usage.CallbackType)}"))
            {
                yield return usage;
            }
        }
    }

    private static string RenderManagedCallbackDelegateDeclaration(BindingCompilation compilation, ManagedCallbackDelegateDeclaration declaration)
    {
        var callbackReturnType = declaration.CallbackType.CallbackReturnType ?? BindingType.Void;
        var returnType = callbackReturnType.PrimitiveKind == BindingPrimitiveKind.Void
            ? "void"
            : GetManagedCallbackDelegateValueTypeName(compilation, callbackReturnType);
        var parameters = declaration.CallbackType.CallbackParameterTypes
            .Select((type, index) => $"{GetManagedCallbackDelegateValueTypeName(compilation, type)} arg{index}");
        return $$"""
            public delegate {{returnType}} {{declaration.DeclarationName}}({{string.Join(", ", parameters)}});
            """;
    }

    private static string RenderManagedCallbackMarshallerHelper(BindingCompilation compilation, ManagedCallbackUsage usage)
    {
        var callbackType = usage.CallbackType;
        var declarationName = usage.MarshallerDeclarationName;
        var callbackManagedType = usage.ManagedTypeName;
        var callbackReturnType = callbackType.CallbackReturnType ?? BindingType.Void;
        var callbackReturnAbiType = callbackReturnType.PrimitiveKind == BindingPrimitiveKind.Void
            ? "void"
            : GetManagedImportTypeName(callbackReturnType, compilation);
        var parameters = callbackType.CallbackParameterTypes
            .Select((type, index) => (type, index, abiType: GetManagedCallbackParameterAbiTypeName(compilation, type)))
            .ToArray();
        var invokeArguments = string.Join(", ", parameters.Select(item => BuildManagedCallbackInvocationArgument(
            compilation,
            item.type,
            IsManagedCallbackValueReference(item.type) ? $"*arg{item.index}" : $"arg{item.index}")));
        var invokeParameterList = string.Join(", ", new[] { "nint state" }.Concat(parameters.Select(static item => $"{item.abiType} arg{item.index}")));
        var invokeFunctionPointerTypes = string.Join(
            ", ",
            new[] { "nint" }.Concat(parameters.Select(static item => item.abiType)).Append(callbackReturnAbiType));
        var invokeMethodName = $"__InvokeManagedCallback_{declarationName}";
        var invokeBody = callbackReturnType.PrimitiveKind == BindingPrimitiveKind.Void
            ? $$"""
                      callback({{invokeArguments}});
                      return;
              """
            : $$"""
                      var result = callback({{invokeArguments}});
                      return {{BuildManagedCallbackReturnExpression(compilation, callbackReturnType, "result")}};
              """;

        return $$"""
            [CustomMarshaller(typeof({{callbackManagedType}}), MarshalMode.ManagedToUnmanagedIn, typeof({{declarationName}}))]
            internal static unsafe class {{declarationName}}
            {
                static {{declarationName}}()
                {
                    __RegisterManagedCallbackRelease();
                }

                private static readonly nint __Invoke =
                    (nint)(delegate* unmanaged[Cdecl]<{{invokeFunctionPointerTypes}}>)&global::{{compilation.ManagedNamespace}}.{{compilation.ManagedClassName}}.{{invokeMethodName}};

                public static nint ConvertToUnmanaged({{callbackManagedType}}? managed)
                {
                    if (managed is null)
                    {
                        return 0;
                    }

                    var handle = global::System.Runtime.InteropServices.GCHandle.Alloc(managed);
                    var callback = (__SharpCppManagedCallbackHandleAbi*)global::System.Runtime.InteropServices.NativeMemory.Alloc(
                        (nuint)sizeof(__SharpCppManagedCallbackHandleAbi));
                    *callback = new __SharpCppManagedCallbackHandleAbi(
                        global::System.Runtime.InteropServices.GCHandle.ToIntPtr(handle),
                        __Invoke);
                    return (nint)callback;
                }

            }

            [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
            private static unsafe {{callbackReturnAbiType}} {{invokeMethodName}}({{invokeParameterList}})
            {
                var callback = ({{callbackManagedType}})global::System.Runtime.InteropServices.GCHandle.FromIntPtr(state).Target!;
                try
                {
              {{invokeBody}}
                }
                catch (global::System.Exception exception)
                {
                    global::System.Environment.FailFast("SharpCpp managed callback threw an exception.", exception);
              {{(callbackReturnType.PrimitiveKind == BindingPrimitiveKind.Void ? "return;" : "return default;")}}
                }
            }
            """;
    }

    private static string GetManagedCallbackReleaseRegistrationEntryPointName(BindingCompilation compilation)
    {
        return $"{compilation.EntryPointPrefix}_set_managed_callback_release";
    }

    // A projected value passed by reference arrives as a pointer to its ABI value.
    private static bool IsManagedCallbackValueReference(BindingType type) =>
        type.Marshaller.CppIsReference && type.Kind == BindingTypeKind.Primitive && !type.Marshaller.IsRefStruct;

    private static string GetManagedCallbackParameterAbiTypeName(BindingCompilation compilation, BindingType type)
    {
        var abiType = GetManagedImportTypeName(type, compilation);
        return IsManagedCallbackValueReference(type) ? $"{abiType}*" : abiType;
    }

    private static string BuildManagedCallbackInvocationArgument(BindingCompilation compilation, BindingType type, string valueExpression)
    {
        return type.Kind == BindingTypeKind.Handle
            ? BuildManagedHandleWrap(compilation, type, valueExpression, ownsHandle: false)
            : BuildManagedImportToPublicExpression(compilation, type, valueExpression);
    }

    private static string BuildManagedCallbackReturnExpression(BindingCompilation compilation, BindingType type, string valueExpression)
    {
        return type.Kind == BindingTypeKind.Handle
            ? $"global::SharpCpp.Support.NativeObject.GetHandle({valueExpression})"
            : BuildManagedPublicToImportExpression(compilation, type, valueExpression);
    }

    private static string GetManagedCallbackMarshallerTypeName(BindingCompilation compilation, ParameterBinding parameter)
    {
        return $"global::{compilation.ManagedNamespace}.{compilation.ManagedClassName}.{GetManagedCallbackUsage(compilation, parameter).MarshallerDeclarationName}";
    }

    private static ManagedCallbackUsage GetManagedCallbackUsage(BindingCompilation compilation, ParameterBinding parameter)
    {
        var managedTypeName = GetManagedCallbackParameterTypeName(compilation, parameter);
        var defaultTypeName = GetManagedImportTypeName(parameter.Type, compilation);
        var declarationName = string.Equals(managedTypeName, defaultTypeName, StringComparison.Ordinal)
            ? GetManagedCallbackMarshallerDeclarationName(compilation, parameter.Type)
            : $"{GetManagedCallbackMarshallerDeclarationName(compilation, parameter.Type)}_{SanitizeManagedToken(managedTypeName)}";
        return new ManagedCallbackUsage(parameter.Type, managedTypeName, declarationName);
    }

    private static string GetManagedCallbackParameterTypeName(BindingCompilation compilation, ParameterBinding parameter)
    {
        if (!parameter.Type.IsCallback)
        {
            return GetManagedPublicReturnTypeName(parameter.Type, compilation);
        }

        if (!parameter.UseCallbackDelegate)
        {
            return GetManagedPublicReturnTypeName(parameter.Type, compilation);
        }

        if (!string.IsNullOrWhiteSpace(parameter.CallbackDelegateTypeName))
        {
            return IsExternalCallbackDelegateTypeName(parameter.CallbackDelegateTypeName!)
                ? NormalizeExternalCallbackDelegateTypeName(parameter.CallbackDelegateTypeName!)
                : GetQualifiedGeneratedCallbackDelegateTypeName(compilation, parameter.CallbackDelegateTypeName!);
        }

        return GetQualifiedGeneratedCallbackDelegateTypeName(compilation, GetGeneratedCallbackDelegateName(parameter));
    }

    private static string? GetGeneratedCallbackDelegateDeclarationName(ParameterBinding parameter)
    {
        if (!parameter.UseCallbackDelegate)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(parameter.CallbackDelegateTypeName) && IsExternalCallbackDelegateTypeName(parameter.CallbackDelegateTypeName!))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(parameter.CallbackDelegateTypeName)
            ? GetGeneratedCallbackDelegateName(parameter)
            : parameter.CallbackDelegateTypeName!;
    }

    private static string GetGeneratedCallbackDelegateName(ParameterBinding parameter)
    {
        var function = parameter.Parent as FunctionBinding;
        var ownerName = function?.FindOwningRecord()?.ManagedName ?? "Global";
        var functionName = function?.ImportMethodName ?? function?.ManagedName ?? "Callback";
        return $"{SanitizeManagedToken(ownerName)}_{SanitizeManagedToken(functionName)}_{SanitizeManagedToken(parameter.ManagedName)}Callback";
    }

    private static string GetQualifiedGeneratedCallbackDelegateTypeName(BindingCompilation compilation, string declarationName)
    {
        return $"global::{compilation.ManagedNamespace}.{compilation.ManagedClassName}.{declarationName}";
    }

    private static bool IsExternalCallbackDelegateTypeName(string value)
    {
        return value.StartsWith("global::", StringComparison.Ordinal) ||
            value.Contains('.', StringComparison.Ordinal);
    }

    private static string NormalizeExternalCallbackDelegateTypeName(string value)
    {
        return value.StartsWith("global::", StringComparison.Ordinal)
            ? value
            : $"global::{value}";
    }

    private static string GetManagedCallbackDelegateValueTypeName(BindingCompilation compilation, BindingType type)
    {
        return type.Kind == BindingTypeKind.Handle
            ? GetManagedWrapperTypeName(type, compilation)
            : GetManagedPublicReturnTypeName(type, compilation);
    }

    private static string GetCallbackSignatureKey(BindingType type)
    {
        var returnType = type.CallbackReturnType?.UnaliasedManagedTypeName ?? "void";
        return string.Join("|", returnType, string.Join(";", type.CallbackParameterTypes.Select(static parameter =>
            IsManagedCallbackValueReference(parameter) ? $"{parameter.UnaliasedManagedTypeName}&" : parameter.UnaliasedManagedTypeName)));
    }

    private static string GetManagedCallbackMarshallerDeclarationName(BindingCompilation compilation, BindingType type)
    {
        var returnToken = type.CallbackReturnType is null ? "void" : GetManagedPublicReturnTypeName(type.CallbackReturnType, compilation);
        var parameterToken = type.CallbackParameterTypes.Count == 0
            ? "none"
            : string.Join("_", type.CallbackParameterTypes.Select(parameter =>
                IsManagedCallbackValueReference(parameter)
                    ? $"in_{GetManagedPublicReturnTypeName(parameter, compilation)}"
                    : GetManagedPublicReturnTypeName(parameter, compilation)));
        return $"__CallbackMarshaller_{SanitizeManagedToken(returnToken)}_{SanitizeManagedToken(parameterToken)}";
    }

    private static string RenderManagedHandleReturnMarshallerHelpers(BindingCompilation compilation, RecordBinding record, string recordDeclarationName)
    {
        if (IsSharedNativeArrayRecord(record))
        {
            return string.Empty;
        }

        if (IsGenericArrayRecord(record))
        {
            return RenderManagedGenericArrayMarshallerHelpers(compilation, record, recordDeclarationName);
        }

        // Records with non-type template arguments (hash_map's bool) still need return
        // helpers: return sites resolve their marshaller from the use-site type.
        if (HasNativeNonTypeTemplateArguments(record) || !CanUseUnifiedHandleMarshaller(record))
        {
            return RenderManagedGenericHandleMarshallerHelpers(compilation, record, recordDeclarationName);
        }

        var qualifiedRecordTypeName = GetQualifiedManagedRecordDeclarationTypeName(compilation, record, recordDeclarationName);
        var qualifiedOpenRecordTypeName = GetQualifiedManagedOpenRecordTypeName(compilation, record);
        var marshallerDeclarationName = GetHandleMarshallerDeclarationName(record);
        var borrowedMarshallerTypeName = GetQualifiedUnifiedHandleMarshallerTypeName(compilation, record, BorrowedNativeHandleFullName);
        var ownedMarshallerTypeName = GetQualifiedUnifiedHandleMarshallerTypeName(compilation, record, OwnedNativeHandleFullName);
        var genericConstraints = GetHandleMarshallerGenericConstraints();
        var moduleArgument = GetSharedManagedProviderModuleArgument(compilation, record);

        return $$"""
            [CustomMarshaller(typeof({{qualifiedOpenRecordTypeName}}), MarshalMode.ManagedToUnmanagedOut, typeof({{borrowedMarshallerTypeName}}))]
            [CustomMarshaller(typeof({{qualifiedOpenRecordTypeName}}), MarshalMode.ManagedToUnmanagedOut, typeof({{ownedMarshallerTypeName}}))]
            [CustomMarshaller(typeof({{qualifiedOpenRecordTypeName}}), MarshalMode.ManagedToUnmanagedIn, typeof({{borrowedMarshallerTypeName}}))]
            internal static class {{marshallerDeclarationName}}{{genericConstraints}}
            {
                public static nint ConvertToUnmanaged({{qualifiedRecordTypeName}} managed)
                {
                    return global::SharpCpp.Support.NativeObject.GetHandle(managed);
                }

                public static {{qualifiedRecordTypeName}} ConvertToManaged(nint unmanaged)
                {
                    return {{qualifiedRecordTypeName}}.CreateManagedWrapper(unmanaged, ownsHandle: TOwnership.OwnsHandle{{moduleArgument}});
                }
            }
            """;
    }

    private static string RenderManagedGenericArrayMarshallerHelpers(BindingCompilation compilation, RecordBinding record, string recordDeclarationName)
    {
        return IsGenericDynamicArrayRecord(record)
            ? RenderManagedGenericDynamicArrayMarshallerHelpers(compilation, record, recordDeclarationName)
            : RenderManagedGenericFixedArrayMarshallerHelpers(compilation, record, recordDeclarationName);
    }

    private static string RenderManagedGenericFixedArrayMarshallerHelpers(BindingCompilation compilation, RecordBinding record, string recordDeclarationName)
    {
        var qualifiedRecordTypeName = GetQualifiedManagedRecordDeclarationTypeName(compilation, record, recordDeclarationName);
        var qualifiedOpenRecordTypeName = GetQualifiedManagedOpenRecordTypeName(compilation, record);
        var inMarshallerDeclarationName = GetHandleInMarshallerDeclarationName(record);
        var inMarshallerTypeName = GetQualifiedHandleInMarshallerTypeName(compilation, record);
        var borrowedMarshallerDeclarationName = GetHandleReturnMarshallerDeclarationName(record, ownsHandle: false);
        var borrowedMarshallerTypeName = GetQualifiedHandleReturnMarshallerTypeName(compilation, record, ownsHandle: false);
        var ownedMarshallerDeclarationName = GetHandleReturnMarshallerDeclarationName(record, ownsHandle: true);
        var ownedMarshallerTypeName = GetQualifiedHandleReturnMarshallerTypeName(compilation, record, ownsHandle: true);
        var genericConstraints = GetGenericRecordConstraints(record);
        var moduleArgument = GetSharedManagedProviderModuleArgument(compilation, record);

        return $$"""
            [CustomMarshaller(typeof({{qualifiedOpenRecordTypeName}}), MarshalMode.ManagedToUnmanagedIn, typeof({{inMarshallerTypeName}}))]
            internal static class {{inMarshallerDeclarationName}}{{genericConstraints}}
            {
                public static global::SharpCpp.Support.NativeArrayAbi ConvertToUnmanaged({{qualifiedRecordTypeName}} managed)
                {
                    global::System.ArgumentNullException.ThrowIfNull(managed);
                    return managed.ToNativeArrayAbi();
                }
            }

            [CustomMarshaller(typeof({{qualifiedOpenRecordTypeName}}), MarshalMode.ManagedToUnmanagedOut, typeof({{borrowedMarshallerTypeName}}))]
            internal static class {{borrowedMarshallerDeclarationName}}{{genericConstraints}}
            {
                public static {{qualifiedRecordTypeName}} ConvertToManaged(global::SharpCpp.Support.NativeArrayAbi unmanaged)
                {
                    return {{qualifiedRecordTypeName}}.CreateManagedWrapper(unmanaged, ownsHandle: false{{moduleArgument}});
                }
            }

            [CustomMarshaller(typeof({{qualifiedOpenRecordTypeName}}), MarshalMode.ManagedToUnmanagedOut, typeof({{ownedMarshallerTypeName}}))]
            internal static class {{ownedMarshallerDeclarationName}}{{genericConstraints}}
            {
                public static {{qualifiedRecordTypeName}} ConvertToManaged(global::SharpCpp.Support.NativeArrayAbi unmanaged)
                {
                    return {{qualifiedRecordTypeName}}.CreateManagedWrapper(unmanaged, ownsHandle: true{{moduleArgument}});
                }
            }
            """;
    }

    private static string RenderManagedGenericDynamicArrayMarshallerHelpers(BindingCompilation compilation, RecordBinding record, string recordDeclarationName)
    {
        var qualifiedRecordTypeName = GetQualifiedManagedRecordDeclarationTypeName(compilation, record, recordDeclarationName);
        var qualifiedOpenRecordTypeName = GetQualifiedManagedOpenRecordTypeName(compilation, record);
        var inMarshallerDeclarationName = GetHandleInMarshallerDeclarationName(record);
        var inMarshallerTypeName = GetQualifiedHandleInMarshallerTypeName(compilation, record);
        var borrowedMarshallerDeclarationName = GetHandleReturnMarshallerDeclarationName(record, ownsHandle: false);
        var borrowedMarshallerTypeName = GetQualifiedHandleReturnMarshallerTypeName(compilation, record, ownsHandle: false);
        var ownedMarshallerDeclarationName = GetHandleReturnMarshallerDeclarationName(record, ownsHandle: true);
        var ownedMarshallerTypeName = GetQualifiedHandleReturnMarshallerTypeName(compilation, record, ownsHandle: true);
        var genericConstraints = GetGenericRecordConstraints(record);
        var moduleArgument = GetSharedManagedProviderModuleArgument(compilation, record);

        return $$"""
            [CustomMarshaller(typeof({{qualifiedOpenRecordTypeName}}), MarshalMode.ManagedToUnmanagedIn, typeof({{inMarshallerTypeName}}))]
            internal static class {{inMarshallerDeclarationName}}{{genericConstraints}}
            {
                public static nint ConvertToUnmanaged({{qualifiedRecordTypeName}} managed)
                {
                    return global::SharpCpp.Support.NativeObject.GetHandle(managed);
                }
            }

            [CustomMarshaller(typeof({{qualifiedOpenRecordTypeName}}), MarshalMode.ManagedToUnmanagedOut, typeof({{borrowedMarshallerTypeName}}))]
            internal static class {{borrowedMarshallerDeclarationName}}{{genericConstraints}}
            {
                public static {{qualifiedRecordTypeName}} ConvertToManaged(nint unmanaged)
                {
                    return {{qualifiedRecordTypeName}}.CreateManagedWrapper(unmanaged, ownsHandle: false{{moduleArgument}});
                }
            }

            [CustomMarshaller(typeof({{qualifiedOpenRecordTypeName}}), MarshalMode.ManagedToUnmanagedOut, typeof({{ownedMarshallerTypeName}}))]
            internal static class {{ownedMarshallerDeclarationName}}{{genericConstraints}}
            {
                public static {{qualifiedRecordTypeName}} ConvertToManaged(nint unmanaged)
                {
                    return {{qualifiedRecordTypeName}}.CreateManagedWrapper(unmanaged, ownsHandle: true{{moduleArgument}});
                }
            }
            """;
    }

    private static string RenderManagedGenericHandleMarshallerHelpers(BindingCompilation compilation, RecordBinding record, string recordDeclarationName)
    {
        var qualifiedRecordTypeName = GetQualifiedManagedRecordDeclarationTypeName(compilation, record, recordDeclarationName);
        var qualifiedOpenRecordTypeName = GetQualifiedManagedOpenRecordTypeName(compilation, record);
        var inMarshallerDeclarationName = GetHandleInMarshallerDeclarationName(record);
        var inMarshallerTypeName = GetQualifiedHandleInMarshallerTypeName(compilation, record);
        var borrowedMarshallerDeclarationName = GetHandleReturnMarshallerDeclarationName(record, ownsHandle: false);
        var borrowedMarshallerTypeName = GetQualifiedHandleReturnMarshallerTypeName(compilation, record, ownsHandle: false);
        var ownedMarshallerDeclarationName = GetHandleReturnMarshallerDeclarationName(record, ownsHandle: true);
        var ownedMarshallerTypeName = GetQualifiedHandleReturnMarshallerTypeName(compilation, record, ownsHandle: true);
        var genericConstraints = GetGenericRecordConstraints(record);
        var moduleArgument = GetSharedManagedProviderModuleArgument(compilation, record);

        return $$"""
            [CustomMarshaller(typeof({{qualifiedOpenRecordTypeName}}), MarshalMode.ManagedToUnmanagedIn, typeof({{inMarshallerTypeName}}))]
            internal static class {{inMarshallerDeclarationName}}{{genericConstraints}}
            {
                public static nint ConvertToUnmanaged({{qualifiedRecordTypeName}} managed)
                {
                    return global::SharpCpp.Support.NativeObject.GetHandle(managed);
                }
            }

            [CustomMarshaller(typeof({{qualifiedOpenRecordTypeName}}), MarshalMode.ManagedToUnmanagedOut, typeof({{borrowedMarshallerTypeName}}))]
            internal static class {{borrowedMarshallerDeclarationName}}{{genericConstraints}}
            {
                public static {{qualifiedRecordTypeName}} ConvertToManaged(nint unmanaged)
                {
                    return {{qualifiedRecordTypeName}}.CreateManagedWrapper(unmanaged, ownsHandle: false{{moduleArgument}});
                }
            }

            [CustomMarshaller(typeof({{qualifiedOpenRecordTypeName}}), MarshalMode.ManagedToUnmanagedOut, typeof({{ownedMarshallerTypeName}}))]
            internal static class {{ownedMarshallerDeclarationName}}{{genericConstraints}}
            {
                public static {{qualifiedRecordTypeName}} ConvertToManaged(nint unmanaged)
                {
                    return {{qualifiedRecordTypeName}}.CreateManagedWrapper(unmanaged, ownsHandle: true{{moduleArgument}});
                }
            }
            """;
    }

    private static string RenderAliasBindings(
        BindingCompilation compilation,
        bool includeRegistry = false,
        bool includeSharedCallbackHelpers = false)
    {
        var aliases = compilation.TypeAliases
            .Where(alias => !alias.IsImported && IsRenderableAliasTarget(compilation, alias.TargetType))
            .ToArray();
        var needsRegistry = includeRegistry;
        var sharedProviderShapes = compilation.Records
            .Where(record => IsSharedManagedWrapper(record) &&
                !record.IsSilentlyExcluded() &&
                !record.HasMetadata(MetadataKeys.IgnoreReason))
            .SelectMany(record => record.IsGeneric
                ? GetSharedProviderInstantiations(compilation, record)
                    .Select(instantiation => (record, (RecordGenericInstantiationBinding?)instantiation))
                : [(record, (RecordGenericInstantiationBinding?)null)])
            .ToArray();
        var callbackParameters = includeSharedCallbackHelpers
            ? CollectCallbackParameters(
                compilation.Records.Where(static record => !record.GetMetadata<bool>(MetadataKeys.SuppressEmission) && !record.IsSilentlyExcluded()),
                compilation.Functions.Where(static function => !function.IsSilentlyExcluded() &&
                    (function.Parent is not RecordBinding parentRecord || !parentRecord.IsSilentlyExcluded())))
                .ToArray()
            : Array.Empty<ParameterBinding>();
        var virtualCallbacks = includeSharedCallbackHelpers
            ? compilation.Records
                .Where(record => !record.GetMetadata<bool>(MetadataKeys.SuppressEmission) &&
                    !record.IsSilentlyExcluded() &&
                    record.RequiresDirector(compilation))
                .SelectMany(record => record.GetEffectiveVirtualMethodSlots(compilation)
                    .Select(slot => (Record: record, Slot: slot)))
                .ToArray()
            : Array.Empty<(RecordBinding Record, VirtualMethodSlotBinding Slot)>();
        if (aliases.Length == 0 && !needsRegistry && callbackParameters.Length == 0 &&
            virtualCallbacks.Length == 0 && sharedProviderShapes.Length == 0)
        {
            return "// <auto-generated />\n// No alias glue is required for this bootstrap pipeline.\n";
        }

        var polymorphicConcreteRecords = needsRegistry
            ? compilation.Records
                .Where(record => record.IsPolymorphic && !record.IsGeneric && CanCreateManagedWrapperFactory(compilation, record) && !record.HasMetadata(MetadataKeys.IgnoreReason))
                .ToArray()
            : Array.Empty<RecordBinding>();
        if (aliases.Length == 0 && needsRegistry && polymorphicConcreteRecords.Length == 0 && callbackParameters.Length == 0 &&
            virtualCallbacks.Length == 0 && sharedProviderShapes.Length == 0)
        {
            return "// <auto-generated />\n// No registry contents to generate.\n";
        }

        var writer = new CodeWriter();
        writer.Write(
            """
            // <auto-generated />
            #nullable enable
            """);

        if (aliases.Length > 0)
        {
            writer.WriteLine();
            foreach (var alias in aliases
                .GroupBy(static a => a.ManagedName, StringComparer.Ordinal)
                .Select(static group => group.First())
                .OrderBy(static a => a.ManagedName, StringComparer.Ordinal))
            {
                var qualifiedTarget = FormatGlobalTypeName(alias.TargetType, compilation);
                writer.Write($"global using {alias.ManagedName} = {qualifiedTarget};");
            }
        }

        if (callbackParameters.Length > 0 || virtualCallbacks.Length > 0 || sharedProviderShapes.Length > 0)
        {
            writer.Write(
                """

                using System;
                using System.Runtime.CompilerServices;
                using System.Runtime.InteropServices;
                using System.Runtime.InteropServices.Marshalling;
                """);
        }

        if ((!needsRegistry || polymorphicConcreteRecords.Length == 0) && callbackParameters.Length == 0 &&
            virtualCallbacks.Length == 0 && sharedProviderShapes.Length == 0)
        {
            return writer.ToString();
        }

        writer.Write($$"""

            namespace {{compilation.ManagedNamespace}}
            {
            public static partial class {{compilation.ManagedClassName}}
            {
            """);
        if (needsRegistry && polymorphicConcreteRecords.Length > 0)
        {
            writer.Write(RenderNativeObjectRegistryInitializer(compilation, polymorphicConcreteRecords));
        }

        if (callbackParameters.Length > 0)
        {
            writer.WriteLine();
            writer.Write(RenderManagedCallbackSupport(compilation, callbackParameters));
        }

        if (virtualCallbacks.Length > 0)
        {
            writer.WriteLine();
            foreach (var callback in virtualCallbacks)
            {
                writer.Write(RenderManagedVirtualCallbackEntryPoint(compilation, callback.Record, callback.Slot));
                writer.WriteLine();
            }
        }

        if (sharedProviderShapes.Length > 0)
        {
            writer.WriteLine();
            writer.Write(RenderSharedManagedProviderRegistry(compilation, sharedProviderShapes));
        }

        writer.Write(
            """
            }
            }
            """);
        return writer.ToString();
    }

    private static string RenderManagedVirtualCallbackEntryPoint(
        BindingCompilation compilation,
        RecordBinding record,
        VirtualMethodSlotBinding slot)
    {
        var method = slot.ManagedCallbackMethod;
        var callbackReturnType = GetManagedVirtualCallbackAbiTypeName(compilation, method.ReturnType);
        var callbackParameterList = string.Join(", ",
            new[] { "nint self" }.Concat(method.Parameters.Select(parameter => RenderManagedCallbackParameter(compilation, parameter))));
        var callbackFunctionPointerTypes = string.Join(", ",
            new[] { "nint" }
                .Concat(method.Parameters.Select(parameter => GetManagedVirtualCallbackAbiTypeName(compilation, parameter.Type)))
                .Append(callbackReturnType));
        var invocationArguments = string.Join(", ",
            new[] { "self" }.Concat(method.Parameters.Select(static parameter => parameter.ManagedName)));
        var invocation = $"{GetManagedRecordTypeName(compilation, record)}.{GetVirtualCallbackDispatchName(method)}({invocationArguments})";
        var body = method.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void
            ? $"{invocation};"
            : $"return {invocation};";
        var fallbackReturn = method.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void
            ? "return;"
            : "return default;";
        var entryPointName = GetVirtualCallbackEntryPointName(record, method);

        return $$"""
            internal static readonly unsafe nint {{entryPointName}}Pointer =
                (nint)(delegate* unmanaged[Cdecl]<{{callbackFunctionPointerTypes}}>)&{{entryPointName}};

            [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
            private static {{callbackReturnType}} {{entryPointName}}({{callbackParameterList}})
            {
            try
            {
            {{body}}
            }
            catch (global::System.Exception exception)
            {
                global::System.Environment.FailFast("SharpCpp managed virtual callback threw an exception.", exception);
                {{fallbackReturn}}
            }
            }
            """;
    }

    private static string RenderSharedManagedProviderRegistry(
        BindingCompilation compilation,
        IReadOnlyList<(RecordBinding Record, RecordGenericInstantiationBinding? Instantiation)> shapes)
    {
        var writer = new CodeWriter();
        var distinctShapes = shapes
            .GroupBy(shape => $"{shape.Record.SymbolId}|{GetSharedProviderShapeKey(shape.Record, shape.Instantiation)}", StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(shape => GetSharedProviderTypeName(shape.Record, shape.Instantiation), StringComparer.Ordinal)
            .ToArray();

        foreach (var shape in distinctShapes)
        {
            writer.WriteLine();
            writer.Write(RenderSharedManagedProvider(compilation, shape.Record, shape.Instantiation));
        }

        writer.WriteLine();
        writer.Write(
            """
                [global::System.Runtime.CompilerServices.ModuleInitializer]
                public static void RegisterSharedProviders()
                {
            """);

        foreach (var shape in distinctShapes)
        {
            var interfaceName = GetSharedProviderInterfaceTypeName(compilation, shape.Record, shape.Instantiation);
            writer.WriteLine(
                $"        {GetSharedManagedInfrastructureNamespace(compilation, shape.Record)}.ProviderRegistry.Register<{interfaceName}>(new {GetSharedProviderTypeName(shape.Record, shape.Instantiation)}());");
        }

        writer.Write(
            """
                }
            """);
        return writer.ToString();
    }

    private static bool IsRenderableAliasTarget(BindingCompilation compilation, BindingType type)
    {
        if (type.RecordId is not null)
        {
            var record = compilation.FindRecordBySymbolId(type.RecordId);
            if (record is null || record.IsIgnored() ||
                (record.GetMetadata<bool>(MetadataKeys.SuppressEmission) && !IsSharedManagedWrapper(record)))
                return false;
        }
        if (type.EnumId is not null)
        {
            var @enum = compilation.FindEnumBySymbolId(type.EnumId);
            if (@enum is null || @enum.IsIgnored())
                return false;
        }
        return type.GenericArguments.All(argument => IsRenderableAliasTarget(compilation, argument));
    }

    /// <summary>
    /// Builds a fully-qualified concrete managed type name without applying
    /// public alias shorthand. This is the representation used for global
    /// using targets, internal signatures, and other non-public surfaces.
    /// </summary>
    private static string FormatGlobalTypeName(BindingType type, BindingCompilation compilation)
    {
        if (type.Kind == BindingTypeKind.GenericParameter)
        {
            return type.GenericParameterName ?? "T";
        }

        if (type.Kind == BindingTypeKind.Primitive)
        {
            if (type.PrimitiveKind != BindingPrimitiveKind.Unsupported)
            {
                return GetConcretePrimitiveManagedTypeName(type);
            }

            return FormatConfiguredManagedTypeName(type, compilation, publicSurface: false);
        }

        if (type.Kind == BindingTypeKind.Enum)
        {
            return GetQualifiedEnumTypeName(compilation, type);
        }

        if (type.Kind == BindingTypeKind.Handle)
        {
            return FormatQualifiedRecordTypeName(compilation, type, publicSurface: false);
        }

        return QualifyManagedTypeText(type.UnaliasedManagedTypeName);
    }

    private static string QualifyRecordTypeName(BindingCompilation compilation, string typeName)
    {
        // Already qualified?
        if (typeName.Contains("global::", StringComparison.Ordinal) || typeName.Contains('.', StringComparison.Ordinal))
            return typeName;

        var genericStart = typeName.IndexOf('<');
        if (genericStart >= 0)
        {
            var head = typeName[..genericStart];
            var suffix = typeName[genericStart..];
            return QualifyRecordTypeName(compilation, head) + suffix;
        }

        // Look up the record by its managed name to find its namespace
        var baseRecord = compilation.Records.FirstOrDefault(r =>
            string.Equals(r.ManagedName, typeName, StringComparison.Ordinal));
        var ns = baseRecord is null ? compilation.ManagedNamespace : GetManagedNamespace(compilation, baseRecord);
        return $"global::{ns}.{typeName}";
    }

    private static string FormatQualifiedRecordTypeName(BindingCompilation compilation, BindingType type, bool publicSurface)
    {
        var record = ResolveRecord(compilation, type);
        if (record is null)
        {
            return FormatConfiguredManagedTypeName(type, compilation, publicSurface);
        }

        var managedName = record.ManagedTypeFormat ?? record.ManagedName;
        var head = GetManagedTypeHead(managedName);
        var qualifiedHead = head.StartsWith("global::", StringComparison.Ordinal)
            ? head
            : head.Contains('.', StringComparison.Ordinal)
                ? $"global::{head}"
                : $"global::{GetManagedNamespace(compilation, record)}.{head}";
        var nullableSuffix = type.ManagedTypeFormatOverride?.EndsWith("?", StringComparison.Ordinal) == true
            ? "?"
            : string.Empty;
        if (type.GenericArguments.Count == 0 || (!record.IsGeneric && managedName.Contains('{', StringComparison.Ordinal) != true))
        {
            return qualifiedHead + nullableSuffix;
        }

        var renderedArguments = type.GenericArguments.Select(argument => publicSurface
            ? GetManagedPublicReturnTypeName(argument, compilation)
            : FormatGlobalTypeName(argument, compilation)).ToArray();
        if (managedName.Contains('{', StringComparison.Ordinal))
        {
            var formattedName = FormatManagedTypeNameWithPlaceholders(managedName, renderedArguments);
            var formattedHead = GetManagedTypeHead(formattedName);
            var qualifiedFormattedHead = formattedHead.StartsWith("global::", StringComparison.Ordinal)
                ? formattedHead
                : formattedHead.Contains('.', StringComparison.Ordinal)
                    ? $"global::{formattedHead}"
                    : $"global::{GetManagedNamespace(compilation, record)}.{formattedHead}";
            return qualifiedFormattedHead + formattedName[formattedHead.Length..] + nullableSuffix;
        }

        return $"{qualifiedHead}<{string.Join(", ", renderedArguments)}>{nullableSuffix}";
    }

    private static string FormatManagedTypeNameWithPlaceholders(string managedTypeFormat, IReadOnlyList<string> renderedArguments)
    {
        var argumentNames = renderedArguments.Cast<object>().ToArray();
        return AllPlaceholdersInRange(managedTypeFormat, argumentNames.Length)
            ? string.Format(System.Globalization.CultureInfo.InvariantCulture, managedTypeFormat, argumentNames)
            : $"{GetManagedTypeHead(managedTypeFormat)}<{string.Join(", ", renderedArguments)}>";
    }

    private static bool AllPlaceholdersInRange(string format, int argumentCount)
    {
        for (var index = 0; index < format.Length; index++)
        {
            if (format[index] != '{')
            {
                continue;
            }

            var closeIndex = format.IndexOf('}', index + 1);
            if (closeIndex <= index + 1)
            {
                continue;
            }

            if (int.TryParse(format[(index + 1)..closeIndex], out var placeholderIndex) &&
                placeholderIndex >= argumentCount)
            {
                return false;
            }

            index = closeIndex;
        }

        return true;
    }

    private static string GetManagedRecordTypeName(BindingCompilation compilation, RecordBinding record)
    {
        return FormatQualifiedRecordTypeName(compilation, record.HandleType, publicSurface: false);
    }

    private static string GetQualifiedEnumTypeName(BindingCompilation compilation, BindingType type)
    {
        var normalizedName = NormalizeEnumManagedTypeName(type.UnaliasedManagedTypeName);
        var enumBinding = (type.EnumId is null ? null : compilation.FindEnumBySymbolId(type.EnumId)) ??
            compilation.Enums.FirstOrDefault(candidate =>
                string.Equals(candidate.CppQualifiedName, type.CppTypeName, StringComparison.Ordinal));
        if (enumBinding is null)
        {
            // Short names are only evidence when they identify a single enum.
            var matches = compilation.Enums
                .Where(candidate =>
                    string.Equals(candidate.ManagedName, normalizedName, StringComparison.Ordinal) ||
                    string.Equals(candidate.NativeName, type.CppTypeName, StringComparison.Ordinal))
                .Take(2)
                .ToArray();
            enumBinding = matches.Length == 1 ? matches[0] : null;
        }
        if (enumBinding is null)
        {
            return QualifyManagedTypeText(compilation, normalizedName);
        }

        var managedName = enumBinding.ManagedName;
        if (managedName.StartsWith("global::", StringComparison.Ordinal))
        {
            return managedName;
        }

        if (managedName.Contains('.', StringComparison.Ordinal))
        {
            return $"global::{managedName}";
        }

        return $"global::{GetManagedNamespace(compilation, enumBinding)}.{managedName}";
    }

    private static string FormatConfiguredManagedTypeName(BindingType type, BindingCompilation compilation, bool publicSurface)
    {
        var managedTypeName = type.UnaliasedManagedTypeName;
        if (type.GenericArguments.Count == 0)
        {
            return QualifyManagedTypeText(compilation, managedTypeName, type.CppTypeName);
        }

        var qualifiedHead = QualifyManagedTypeText(compilation, GetManagedTypeHead(managedTypeName), type.CppTypeName);
        var renderedArguments = string.Join(", ", type.GenericArguments.Select(argument => publicSurface
            ? GetManagedPublicReturnTypeName(argument, compilation)
            : FormatGlobalTypeName(argument, compilation)));
        return $"{qualifiedHead}<{renderedArguments}>";
    }

    private static string GetManagedTypeHead(string managedTypeName)
    {
        var genericStart = managedTypeName.IndexOf('<');
        return genericStart >= 0 ? managedTypeName[..genericStart] : managedTypeName;
    }

    private static string QualifyManagedTypeText(string managedTypeName)
    {
        if (string.IsNullOrWhiteSpace(managedTypeName))
        {
            return managedTypeName;
        }

        var suffix = string.Empty;
        if (managedTypeName.EndsWith("?", StringComparison.Ordinal))
        {
            suffix = "?";
            managedTypeName = managedTypeName[..^1];
        }

        if (managedTypeName.StartsWith("global::", StringComparison.Ordinal) || IsManagedKeywordTypeName(managedTypeName))
        {
            return managedTypeName + suffix;
        }

        return managedTypeName.Contains('.', StringComparison.Ordinal)
            ? $"global::{managedTypeName}{suffix}"
            : managedTypeName + suffix;
    }

    private static string QualifyManagedTypeText(BindingCompilation compilation, string managedTypeName, string? cppTypeName = null)
    {
        if (string.IsNullOrWhiteSpace(managedTypeName) ||
            managedTypeName.StartsWith("global::", StringComparison.Ordinal) ||
            IsManagedKeywordTypeName(managedTypeName) ||
            managedTypeName.Contains('.', StringComparison.Ordinal))
        {
            return QualifyManagedTypeText(managedTypeName);
        }

        var nullableSuffix = string.Empty;
        if (managedTypeName.EndsWith("?", StringComparison.Ordinal))
        {
            nullableSuffix = "?";
            managedTypeName = managedTypeName[..^1];
        }

        var normalizedCppTypeName = NormalizeCppTypeName(cppTypeName);
        var records = compilation.Records.Where(static record => !record.IsIgnored() && !record.GetMetadata<bool>(MetadataKeys.SuppressEmission));
        var record = !string.IsNullOrWhiteSpace(normalizedCppTypeName)
            ? records.FirstOrDefault(record => string.Equals(record.CppQualifiedName, normalizedCppTypeName, StringComparison.Ordinal))
            : null;

        if (record is null)
        {
            var matches = records
                .Where(record =>
                    string.Equals(record.ManagedName, managedTypeName, StringComparison.Ordinal) ||
                    string.Equals(GetManagedTypeHead(record.ManagedTypeFormat ?? record.ManagedName), managedTypeName, StringComparison.Ordinal))
                .Take(2)
                .ToArray();
            record = matches.Length == 1 ? matches[0] : null;
        }

        if (record is not null)
        {
            return $"global::{GetManagedNamespace(compilation, record)}.{managedTypeName}{nullableSuffix}";
        }

        var normalizedEnumName = NormalizeEnumManagedTypeName(managedTypeName);
        var enums = compilation.Enums.Where(static @enum => !@enum.IsIgnored() && !@enum.GetMetadata<bool>(MetadataKeys.SuppressEmission));
        var enumBinding = !string.IsNullOrWhiteSpace(normalizedCppTypeName)
            ? enums.FirstOrDefault(candidate => string.Equals(candidate.CppQualifiedName, normalizedCppTypeName, StringComparison.Ordinal))
            : null;

        if (enumBinding is null)
        {
            var matches = enums
                .Where(candidate =>
                    string.Equals(candidate.ManagedName, managedTypeName, StringComparison.Ordinal) ||
                    string.Equals(candidate.ManagedName, normalizedEnumName, StringComparison.Ordinal))
                .Take(2)
                .ToArray();
            enumBinding = matches.Length == 1 ? matches[0] : null;
        }

        if (enumBinding is not null)
        {
            return $"global::{GetManagedNamespace(compilation, enumBinding)}.{enumBinding.ManagedName}{nullableSuffix}";
        }

        return QualifyManagedTypeText(managedTypeName + nullableSuffix);
    }

    private static string? NormalizeCppTypeName(string? cppTypeName)
    {
        if (string.IsNullOrWhiteSpace(cppTypeName))
        {
            return null;
        }

        var normalized = cppTypeName.Trim();
        if (normalized.StartsWith("const ", StringComparison.Ordinal))
        {
            normalized = normalized["const ".Length..].TrimStart();
        }

        while (normalized.EndsWith("*", StringComparison.Ordinal) || normalized.EndsWith("&", StringComparison.Ordinal))
        {
            normalized = normalized[..^1].TrimEnd();
        }

        return normalized;
    }

    private static bool IsManagedKeywordTypeName(string managedTypeName) => managedTypeName switch
    {
        "void" or "bool" or "byte" or "sbyte" or "short" or "ushort" or "int" or "uint" or "long" or "ulong" or
        "float" or "double" or "nint" or "nuint" or "char" or "string" or "object" or "IntPtr" or "UIntPtr" => true,
        _ => false
    };

    private static string GetConcretePrimitiveManagedTypeName(BindingType type) => type.PrimitiveKind switch
    {
        BindingPrimitiveKind.Void => "void",
        BindingPrimitiveKind.Boolean => "bool",
        BindingPrimitiveKind.Int8 => "sbyte",
        BindingPrimitiveKind.UInt8 => "byte",
        BindingPrimitiveKind.Int16 => "short",
        BindingPrimitiveKind.UInt16 => "ushort",
        BindingPrimitiveKind.Int32 => "int",
        BindingPrimitiveKind.UInt32 => "uint",
        BindingPrimitiveKind.Int64 => "long",
        BindingPrimitiveKind.UInt64 => "ulong",
        BindingPrimitiveKind.Single => "float",
        BindingPrimitiveKind.Double => "double",
        BindingPrimitiveKind.NativeInt => "nint",
        BindingPrimitiveKind.NativeUInt => "nuint",
        BindingPrimitiveKind.IntPtr => "global::System.IntPtr",
        _ => type.UnaliasedManagedTypeName
    };

    private static string RenderRecord(BindingCompilation compilation, RecordBinding record)
    {
        if (record.IsGeneric)
        {
            return RenderGenericRecord(compilation, record);
        }

        var writer = new CodeWriter();
        var canEmitDeleteThunk = CanEmitDeleteThunk(record);
        var pinvokeDeclarations = new List<string>();
        if (canEmitDeleteThunk)
        {
            pinvokeDeclarations.Add(RenderManagedDeleteImportDeclaration(compilation, record));
        }
        if (RequiresArrayElementAccess(compilation, record))
        {
            pinvokeDeclarations.Add(RenderManagedArrayElementImportDeclaration(compilation, record));
        }
        if (TryGetNativeInterfaceQuery(record, out var nativeInterfaceQuery))
        {
            pinvokeDeclarations.Add(RenderManagedNativeInterfaceQueryImportDeclaration(compilation, record, nativeInterfaceQuery));
        }
        if (record.HasMetadata(MetadataKeys.NativeInterfaceReleaseMethod))
        {
            pinvokeDeclarations.Add(RenderManagedNativeInterfaceReleaseImportDeclaration(compilation, record));
        }
        var requiresDirector = record.RequiresDirector(compilation);
        var virtualSlots = record.GetEffectiveVirtualMethodSlots(compilation);
        var isEffectivelyAbstract = IsManagedWrapperAbstract(compilation, record, virtualSlots);
        var isRegistrationPolymorphic = record.IsPolymorphic && !IsNativeInterfaceWrapper(record);
        if (isRegistrationPolymorphic)
        {
            pinvokeDeclarations.Add(RenderManagedStaticTypeIdImportDeclaration(compilation, record));
            pinvokeDeclarations.Add(RenderManagedNativeObjectMetadataImportDeclaration(compilation, record));
        }
        if (requiresDirector)
        {
            pinvokeDeclarations.Add(RenderManagedConnectDirectorImportDeclaration(compilation, record, virtualSlots));
        }

        WriteDocumentationComment(writer, record);
        var classModifier = isEffectivelyAbstract ? "abstract " : string.Empty;
        var hidesStaticHelpers = !string.IsNullOrWhiteSpace(record.ManagedBaseTypeName) &&
            (!record.ManagedBaseTypeName.Contains('.', StringComparison.Ordinal) ||
             record.GetPrimaryBaseRecord(compilation) is not null);
        var hidesArrayElement = InheritsArrayElementAccess(compilation, record) ||
            IsNativeInterfaceWrapper(record) && !string.IsNullOrWhiteSpace(record.ManagedBaseTypeName);
        var staticHelperModifier = hidesStaticHelpers ? "new " : string.Empty;
        var managedTypeIdHelperModifier = FindNearestNonGenericManagedAncestor(compilation, record) is { } primaryBase &&
            primaryBase.IsPolymorphic &&
            !IsNativeInterfaceWrapper(primaryBase)
                ? "new "
                : string.Empty;
        var queryHelperModifier = record.GetPrimaryBaseRecord(compilation) is { } queryBase &&
            TryGetNativeInterfaceQuery(queryBase, out _)
                ? "new "
                : string.Empty;
        var cacheFieldModifier = hidesStaticHelpers ? "new " : string.Empty;
        var interfaceNames = GetManagedInterfaceBaseBindings(compilation, record)
            .Where(pair => !pair.Record.IsIgnored() && pair.Record.GetRenderableInterfaceMethods(compilation).Count > 0)
            .Select(pair => GetManagedInterfaceTypeName(compilation, pair.Record, pair.Binding.Type))
            .Cast<string>()
            .ToArray();
        var canEmitClone = CanEmitClone(record, isEffectivelyAbstract, canEmitDeleteThunk);
        var baseTypes = new List<string>
        {
            string.IsNullOrWhiteSpace(record.ManagedBaseTypeName)
                ? "global::SharpCpp.Support.NativeObject"
                : QualifyRecordTypeName(compilation, record.ManagedBaseTypeName)
        };
        baseTypes.AddRange(interfaceNames);
        if (canEmitClone)
        {
            baseTypes.Add(ICloneableFullName);
        }
        baseTypes.AddRange(GetConfiguredManagedStaticInterfaces(compilation, record));
        var baseClause = $" : {string.Join(", ", baseTypes)}";
        writer.Write(
            $$"""
            public {{classModifier}}partial class {{record.ManagedName}}{{baseClause}}, global::SharpCpp.Support.INativeObjectRegistration<{{record.ManagedName}}>
            {
            {{cacheFieldModifier}}internal static readonly {{GetInstanceCacheFieldTypeName(record)}} s_instanceCache = {{GetInstanceCacheFactoryExpression(record)}};

            protected override global::SharpCpp.Support.INativeInstanceCache? InstanceCache => s_instanceCache;

            protected internal {{record.ManagedName}}(nint handle, bool ownsHandle = false)
                : base(handle, ownsHandle{{GetPrimaryBaseNativeTemplateKeyArgument(compilation, record)}})
            {
            }

            {{(string.IsNullOrWhiteSpace(record.ManagedBaseTypeName) && !record.HasOwnManagedMemberNamed("Handle") ? "public nint Handle => DangerousGetHandle();" : string.Empty)}}
            {{(string.IsNullOrWhiteSpace(record.ManagedBaseTypeName) && !record.HasOwnManagedMemberNamed("OwnsHandle") ? "public bool OwnsHandle => OwnsNativeHandle;" : string.Empty)}}
            {{(string.IsNullOrWhiteSpace(record.ManagedBaseTypeName) && !record.HasOwnManagedMemberNamed("IsNull") ? "public bool IsNull => IsClosed || IsInvalid;" : string.Empty)}}

            {{staticHelperModifier}}public static {{record.ManagedName}} CreateManagedWrapper(nint handle, bool ownsHandle)
            {
            if (handle == 0)
                {{(isEffectivelyAbstract ? "return null!;" : $"return new {record.ManagedName}(0, ownsHandle);")}}

            return ({{record.ManagedName}})global::SharpCpp.Support.NativeObjectRegistry.Create<{{record.ManagedName}}>(handle, ownsHandle);
            }
            """);

        if (TryGetNativeInterfaceQuery(record, out nativeInterfaceQuery))
        {
            var sourceRecord = compilation.Records.Single(candidate =>
                string.Equals(candidate.CppQualifiedName, nativeInterfaceQuery.SourceRecordCppQualifiedName, StringComparison.Ordinal));
            writer.WriteLine();
            writer.Write($$"""
                {{queryHelperModifier}}public static {{record.ManagedName}}? QueryManagedWrapper({{QualifyRecordTypeName(compilation, sourceRecord.ManagedName)}} source)
                {
                global::System.ArgumentNullException.ThrowIfNull(source);
                var handle = __PInvoke.QueryInterface(source);
                return handle == 0 ? null : CreateManagedWrapper(handle, ownsHandle: true);
                }
                """);
        }

        if (RequiresArrayElementAccess(compilation, record))
        {
            writer.WriteLine();
            writer.Write($$"""
                {{(hidesArrayElement ? "new " : string.Empty)}}public {{record.ManagedName}} GetArrayElement(nuint index)
                {
                return CreateManagedWrapper(__PInvoke.GetArrayElement(this, index), ownsHandle: false);
                }
                """);
        }

        if (record.Members.OfType<FunctionBinding>().Any(static function => function.OperatorToken is "==" or "!="))
        {
            writer.WriteLine();
            writer.Write($$"""
                public override bool Equals(object? obj)
                {
                return obj is {{record.ManagedName}} other && this == other;
                }

                public override int GetHashCode()
                {
                return Handle.GetHashCode();
                }
                """);
        }

        writer.WriteLine();
        writer.Write(RenderManagedDispose(record, canEmitDeleteThunk));

        if (canEmitClone)
        {
            writer.WriteLine();
            writer.Write(RenderManagedCloneMethod(compilation, record));
            pinvokeDeclarations.Add(RenderManagedCloneImportDeclaration(compilation, record));
        }

        var accessorProperties = CollectAccessorProperties(record);
        var accessorMethodIds = accessorProperties
            .SelectMany(static property => new[] { property.Getter?.SymbolId, property.Setter?.SymbolId })
            .Where(static symbolId => !string.IsNullOrWhiteSpace(symbolId))
            .ToHashSet(StringComparer.Ordinal);
        var lastRecordMemberWasIgnoredStub = false;

        void WriteRecordMemberSeparator(bool isIgnoredStub)
        {
            writer.WriteLine();
            if (lastRecordMemberWasIgnoredStub && !isIgnoredStub)
            {
                writer.WriteLine();
            }

            lastRecordMemberWasIgnoredStub = isIgnoredStub;
        }

        void CloseIgnoredStubCluster()
        {
            if (lastRecordMemberWasIgnoredStub)
            {
                writer.WriteLine();
                lastRecordMemberWasIgnoredStub = false;
            }
        }

        foreach (var constructor in record.Members.OfType<FunctionBinding>().Where(static function => function.IsConstructor && !function.IsSilentlyExcluded()).OrderBy(static function => function.Parameters.Count))
        {
            var isIgnoredStub = constructor.HasMetadata(MetadataKeys.IgnoreReason);
            WriteRecordMemberSeparator(isIgnoredStub);
            if (isIgnoredStub)
            {
                writer.Write(RenderIgnoredStub(constructor));
            }
            else
            {
                writer.Write(RenderManagedConstructor(compilation, record, constructor, requiresDirector));
                pinvokeDeclarations.Add(RenderManagedConstructorImportDeclaration(compilation, constructor));
                if (HasNativeDefaultDispatchParams(constructor) || HasUndefaultableParams(constructor))
                {
                    pinvokeDeclarations.Add(RenderManagedUnexpressibleDefaultConstructorImports(compilation, constructor));
                }
            }
        }

        foreach (var property in accessorProperties.OrderBy(static property => property.PropertyName, StringComparer.Ordinal))
        {
            WriteRecordMemberSeparator(isIgnoredStub: false);
            writer.Write(RenderManagedAccessorProperty(compilation, property));

            if (property.Getter is not null)
            {
                pinvokeDeclarations.Add(RenderManagedInstanceImportDeclaration(compilation, property.Getter, record.ManagedName));
            }

            if (property.Setter is not null)
            {
                pinvokeDeclarations.Add(RenderManagedInstanceImportDeclaration(compilation, property.Setter, record.ManagedName));
            }
        }

        foreach (var method in record.Members.OfType<FunctionBinding>().Where(function => !function.IsConstructor && !function.IsStatic && !function.IsGeneric && !function.IsConversionOperator && function.OperatorToken is null && !function.IsSilentlyExcluded() && !accessorMethodIds.Contains(function.SymbolId)).OrderBy(static method => method.ManagedName, StringComparer.Ordinal))
        {
            var isIgnoredStub = method.HasMetadata(MetadataKeys.IgnoreReason);
            WriteRecordMemberSeparator(isIgnoredStub);
            if (isIgnoredStub)
            {
                writer.Write(RenderIgnoredStub(method));
            }
            else
            {
                var isDirectorVirtual = requiresDirector && method.IsVirtual;
                writer.Write(RenderManagedInstanceMethod(compilation, record, method, isDirectorVirtual));
                if (!method.IsVirtual && TryGetRecordBufferOverloadParameters(method, out _, out _))
                {
                    writer.WriteLine();
                    writer.WriteLine();
                    writer.Write(RenderManagedRecordBufferOverload(
                        compilation,
                        method,
                        $"{GetManagedVisibilityNormalized(compilation, record, method)} {GetManagedInstanceMethodModifier(compilation, record, method)}".TrimEnd(),
                        GetManagedPublicReturnTypeName(method.ReturnType, compilation),
                        GetManagedImportMethodName(method),
                        isInstance: true));
                    pinvokeDeclarations.Add(RenderManagedRecordBufferImportDeclaration(compilation, method, record.ManagedName));
                }

                if (isDirectorVirtual && !method.IsPureVirtual)
                {
                    pinvokeDeclarations.Add(RenderManagedCallbaseImportDeclaration(compilation, method, record.ManagedName));
                    if (HasNativeDefaultDispatchParams(method) || HasUndefaultableParams(method))
                    {
                        pinvokeDeclarations.Add(RenderManagedUnexpressibleDefaultInstanceImports(compilation, method, record.ManagedName, useCallbase: true));
                    }
                }
                else if (!isDirectorVirtual)
                {
                    if (!ShouldRenderPureVirtualManagedStub(method))
                    {
                        pinvokeDeclarations.Add(RenderManagedInstanceImportDeclaration(compilation, method, record.ManagedName));
                        if (HasNativeDefaultDispatchParams(method) || HasUndefaultableParams(method))
                        {
                            pinvokeDeclarations.Add(RenderManagedUnexpressibleDefaultInstanceImports(compilation, method, record.ManagedName));
                        }
                    }
                }
            }
        }

        foreach (var method in record.Members.OfType<FunctionBinding>().Where(function => !function.IsConstructor && !function.IsStatic && function.IsGeneric && function.OperatorToken is null && !function.IsConversionOperator && !function.IsSilentlyExcluded() && !accessorMethodIds.Contains(function.SymbolId)).OrderBy(static method => method.ManagedName, StringComparer.Ordinal))
        {
            var isIgnoredStub = method.HasMetadata(MetadataKeys.IgnoreReason);
            WriteRecordMemberSeparator(isIgnoredStub);
            if (isIgnoredStub)
            {
                writer.Write(RenderIgnoredStub(method));
            }
            else
            {
                writer.Write(RenderConcreteGenericManagedMethodOverloads(compilation, record, method));
                writer.Write(RenderGenericManagedGenericMethod(compilation, record, method));
            }
        }

        foreach (var (baseBinding, baseRecord) in GetManagedInterfaceBaseBindings(compilation, record))
        {
            if (!baseRecord.IsGeneric || baseBinding.Type.GenericArguments.Count == 0)
            {
                continue;
            }

            var requestedInstantiation = new RecordGenericInstantiationBinding(
                baseBinding.Type.GenericArguments,
                baseBinding.Type.NativeTemplateArguments);
            var requestedKey = GetManagedGenericInstantiationKey(compilation, baseRecord, requestedInstantiation);
            var instantiation = GetCanonicalManagedGenericInstantiations(compilation, baseRecord)
                .FirstOrDefault(candidate => string.Equals(
                    GetManagedGenericInstantiationKey(compilation, baseRecord, candidate),
                    requestedKey,
                    StringComparison.Ordinal));
            if (instantiation is null)
            {
                continue;
            }
            foreach (var method in baseRecord.GetRenderableInterfaceMethods(compilation)
                .Where(static method => !method.IsStatic && !method.IsGeneric && !method.IsPureVirtual)
                .OrderBy(static method => method.ManagedName, StringComparer.Ordinal))
            {
                if (record.Members.OfType<FunctionBinding>().Any(candidate =>
                    !candidate.IsConstructor &&
                    !candidate.IsStatic &&
                    !candidate.IsGeneric &&
                    !candidate.IsConversionOperator &&
                    candidate.OperatorToken is null &&
                    !candidate.IsSilentlyExcluded() &&
                    !candidate.HasMetadata(MetadataKeys.IgnoreReason) &&
                    string.Equals(candidate.ManagedName, method.ManagedName, StringComparison.Ordinal) &&
                    candidate.Parameters.Count == method.Parameters.Count))
                {
                    continue;
                }

                WriteRecordMemberSeparator(isIgnoredStub: false);
                writer.Write(RenderGenericSecondaryBaseInterfaceMethod(
                    compilation,
                    baseRecord,
                    method,
                    instantiation));
            }
        }

        foreach (var method in record.Members.OfType<FunctionBinding>().Where(static function => function.IsStatic && !function.IsConversionOperator && function.OperatorToken is null && !function.IsSilentlyExcluded()).OrderBy(static method => method.ManagedName, StringComparer.Ordinal))
        {
            var isIgnoredStub = method.HasMetadata(MetadataKeys.IgnoreReason);
            WriteRecordMemberSeparator(isIgnoredStub);
            if (isIgnoredStub)
            {
                writer.Write(RenderIgnoredStub(method));
            }
            else
            {
                writer.Write(RenderManagedStaticMethod(compilation, record, method));
                pinvokeDeclarations.Add(RenderManagedStaticImportDeclaration(compilation, method));
                if (TryGetRecordBufferOverloadParameters(method, out _, out _))
                {
                    writer.WriteLine();
                    writer.WriteLine();
                    writer.Write(RenderManagedRecordBufferOverload(
                        compilation,
                        method,
                        $"{GetManagedVisibility(method.AccessModifier)} {(ShouldUseNewModifier(compilation, record, method) ? "new " : string.Empty)}static",
                        GetManagedPublicReturnTypeName(method.ReturnType, compilation),
                        GetManagedImportMethodName(method),
                        isInstance: false));
                    pinvokeDeclarations.Add(RenderManagedRecordBufferImportDeclaration(compilation, method, selfTypeName: null));
                }
                if (HasNativeDefaultDispatchParams(method) || HasUndefaultableParams(method))
                {
                    pinvokeDeclarations.Add(RenderManagedUnexpressibleDefaultStaticImports(compilation, method));
                }
            }
        }

        foreach (var method in record.Members.OfType<FunctionBinding>().Where(static function => (function.IsConversionOperator || function.OperatorToken is not null) && !function.IsSilentlyExcluded()).OrderBy(static method => method.ManagedName, StringComparer.Ordinal))
        {
            var isIgnoredStub = method.HasMetadata(MetadataKeys.IgnoreReason);
            WriteRecordMemberSeparator(isIgnoredStub);
            if (isIgnoredStub)
            {
                writer.Write(RenderIgnoredStub(method));
            }
            else
            {
                writer.Write(RenderManagedOperator(compilation, record, method));
                pinvokeDeclarations.Add(RenderManagedOperatorImportDeclaration(compilation, method, record.ManagedName));
            }
        }

        foreach (var field in record.Members.OfType<FieldBinding>().Where(static field => !field.IsSilentlyExcluded()).OrderBy(static field => field.ManagedName, StringComparer.Ordinal))
        {
            if (record.HasOwnManagedNonFieldMemberNamed(field.ManagedName))
            {
                continue;
            }

            var isIgnoredStub = field.HasMetadata(MetadataKeys.IgnoreReason);
            WriteRecordMemberSeparator(isIgnoredStub);
            if (isIgnoredStub)
            {
                writer.Write(RenderIgnoredStub(field));
            }
            else
            {
                if (NeedsManagedFixedArrayProxy(field))
                {
                    writer.Write(RenderManagedFixedArrayProxy(compilation, field, record.ManagedName));
                }

                writer.Write(RenderManagedProperty(
                    compilation,
                    field,
                    hidesInheritedMember: HidesInheritedManagedField(compilation, record, field)));
                if (TryGetPointerCountNativeArrayShape(compilation, field, out var pointerCountShape))
                {
                    writer.WriteLine();
                    writer.Write(RenderManagedPointerCountFieldAccessor(compilation, field, pointerCountShape));
                    pinvokeDeclarations.Add(RenderManagedPointerCountFieldItemGetterImportDeclaration(
                        compilation,
                        field,
                        pointerCountShape,
                        record.ManagedName,
                        record.CppQualifiedName));
                }
                if (NeedsManagedFixedArrayProxy(field))
                {
                    pinvokeDeclarations.Add(RenderManagedFieldItemGetterImportDeclaration(compilation, field, record.ManagedName, record.CppQualifiedName));

                    if (!field.IsReadOnly)
                    {
                        pinvokeDeclarations.Add(RenderManagedFieldItemSetterImportDeclaration(compilation, field, record.ManagedName, record.CppQualifiedName));
                    }
                }
                else
                {
                    pinvokeDeclarations.Add(RenderManagedFieldGetterImportDeclaration(compilation, field, record.ManagedName, record.CppQualifiedName));

                    if (!field.IsReadOnly)
                    {
                        pinvokeDeclarations.Add(RenderManagedFieldSetterImportDeclaration(compilation, field, record.ManagedName, record.CppQualifiedName));
                    }
                }
            }
        }

        foreach (var field in GetInheritedProtectedFields(compilation, record).OrderBy(static field => field.ManagedName, StringComparer.Ordinal))
        {
            if (record.HasOwnManagedMemberNamed(field.ManagedName))
            {
                continue;
            }

            WriteRecordMemberSeparator(false);
            writer.Write(RenderManagedProperty(compilation, field, hidesInheritedMember: true));
            if (NeedsManagedFixedArrayProxy(field))
            {
                pinvokeDeclarations.Add(RenderManagedFieldItemGetterImportDeclaration(compilation, field, record.ManagedName, record.CppQualifiedName));

                if (!field.IsReadOnly)
                {
                    pinvokeDeclarations.Add(RenderManagedFieldItemSetterImportDeclaration(compilation, field, record.ManagedName, record.CppQualifiedName));
                }
            }
            else
            {
                pinvokeDeclarations.Add(RenderManagedFieldGetterImportDeclaration(compilation, field, record.ManagedName, record.CppQualifiedName));

                if (!field.IsReadOnly)
                {
                    pinvokeDeclarations.Add(RenderManagedFieldSetterImportDeclaration(compilation, field, record.ManagedName, record.CppQualifiedName));
                }
            }
        }

        CloseIgnoredStubCluster();

        writer.WriteLine();
        writer.Write(
            $$"""
            {{(isRegistrationPolymorphic ? $$"""
            {{managedTypeIdHelperModifier}}internal static nuint GetManagedTypeId()
            {
            return __PInvoke.GetStaticTypeId();
            }

            """ : string.Empty)}}static bool global::SharpCpp.Support.INativeObjectRegistration<{{record.ManagedName}}>.RegistrationIsPolymorphic => {{isRegistrationPolymorphic.ToString().ToLowerInvariant()}};

            static nuint global::SharpCpp.Support.INativeObjectRegistration<{{record.ManagedName}}>.RegistrationDynamicTypeId => {{(isRegistrationPolymorphic ? "GetManagedTypeId()" : "0")}};

            static global::System.Type global::SharpCpp.Support.INativeObjectRegistration<{{record.ManagedName}}>.RegistrationExpectedType => typeof({{record.ManagedName}});

            static global::SharpCpp.Support.INativeInstanceCache global::SharpCpp.Support.INativeObjectRegistration<{{record.ManagedName}}>.RegistrationInstanceCache => s_instanceCache;

            static global::System.Func<nint, bool, global::SharpCpp.Support.NativeObject>? global::SharpCpp.Support.INativeObjectRegistration<{{record.ManagedName}}>.RegistrationCreateFactory => {{(isEffectivelyAbstract ? "null" : $"static (handle, ownsHandle) => new {record.ManagedName}(handle, ownsHandle)")}};

            static global::SharpCpp.Support.NativeObjectMetadata global::SharpCpp.Support.INativeObjectRegistration<{{record.ManagedName}}>.RegistrationGetMetadata(nint handle)
            {
            {{(isRegistrationPolymorphic ? "__PInvoke.GetNativeObjectMetadata(handle, out var resolvedHandle, out var dynamicTypeId);\nreturn new global::SharpCpp.Support.NativeObjectMetadata(resolvedHandle, dynamicTypeId);" : "return new global::SharpCpp.Support.NativeObjectMetadata(handle, 0);")}}
            }
            """);
        writer.WriteLine();

        if (requiresDirector)
        {
            foreach (var slot in virtualSlots)
            {
                writer.WriteLine();
                writer.Write(RenderManagedVirtualCallbackMembers(compilation, record, slot));
            }

            writer.WriteLine();
            writer.Write(RenderManagedConnectDirector(compilation, record, virtualSlots));
            writer.WriteLine();
        }

        var hidesPInvoke = !string.IsNullOrWhiteSpace(record.ManagedBaseTypeName) &&
            FindNearestNonGenericManagedAncestor(compilation, record) is not null ||
            IsNativeInterfaceWrapper(record) && !string.IsNullOrWhiteSpace(record.ManagedBaseTypeName);
        writer.Write(
            hidesPInvoke
            ? $$"""
            new internal static partial class __PInvoke
            {
            """
            : $$"""
            internal static partial class __PInvoke
            {
            """);

        foreach (var declaration in pinvokeDeclarations)
        {
            writer.WriteLine();
            writer.Write(declaration);
        }

        writer.WriteLine();
        writer.Write(
            """
            }
            """);

        writer.WriteLine();
        writer.Write(
            """
            }
            """);

        writer.WriteLine();
        writer.Write(RenderManagedHandleReturnMarshallerHelpers(compilation, record, record.ManagedName));
        return writer.ToString();
    }

    private static string RenderManagedPointerCountFieldAccessor(
        BindingCompilation compilation,
        FieldBinding field,
        PointerCountNativeArrayShape shape)
    {
        var returnTypeName = shape.IsStringArray
            ? "string?"
            : GetManagedPublicReturnTypeName(shape.ElementType, compilation);
        var resultExpression = shape.IsStringArray
            ? "global::System.Runtime.InteropServices.Marshal.PtrToStringUTF8(result)"
            : "result";
        return $$"""
            public {{returnTypeName}} Get{{field.ManagedName}}At(nuint index)
            {
                if (index >= unchecked((nuint){{shape.CountField.ManagedName}}))
                {
                    throw new global::System.ArgumentOutOfRangeException(nameof(index));
                }

                var result = __PInvoke.Get_{{field.ManagedName}}_Item(this, index);
                global::System.GC.KeepAlive(this);
                return {{resultExpression}};
            }
            """;
    }

    private static IReadOnlyList<string> GetConfiguredManagedStaticInterfaces(BindingCompilation compilation, RecordBinding record)
    {
        var interfaces = new List<string>();
        foreach (var source in record.EnumerateAllBaseRecords(compilation).Append(record))
        {
            var isDirectSource = ReferenceEquals(source, record);
            if (isDirectSource && source.GetMetadata<bool>(MetadataKeys.ManagedStaticInterfacesAbstract))
            {
                continue;
            }

            if (!isDirectSource && !HasPublicStaticMethod(record, "GetTypeStatic"))
            {
                continue;
            }

            var configured = source.GetMetadata<IReadOnlyList<string>>(MetadataKeys.ManagedStaticInterfaces);
            if (configured is null)
            {
                continue;
            }

            interfaces.AddRange(configured
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Select(item => FormatManagedStaticInterface(compilation, record, item)));
        }

        return interfaces.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string FormatManagedStaticInterface(BindingCompilation compilation, RecordBinding record, string interfaceTemplate)
    {
        return interfaceTemplate.Replace(
            "{managed_type}",
            GetQualifiedManagedSelfTypeName(compilation, record),
            StringComparison.Ordinal);
    }

    private static string GetQualifiedManagedSelfTypeName(BindingCompilation compilation, RecordBinding record)
    {
        var declarationName = record.IsGeneric
            ? GetGenericRecordDeclarationName(record)
            : record.ManagedName;
        return $"global::{GetManagedNamespace(compilation, record)}.{declarationName}";
    }

    private static bool HasPublicStaticMethod(RecordBinding record, string managedName)
    {
        return record.Members.OfType<FunctionBinding>().Any(function =>
            string.Equals(function.ManagedName, managedName, StringComparison.Ordinal) &&
            function is { IsStatic: true, AccessModifier: BindingAccessModifier.Public } &&
            !function.IsIgnored() &&
            !function.IsSilentlyExcluded());
    }

    private static string RenderManagedInstanceMethod(BindingCompilation compilation, RecordBinding record, FunctionBinding function, bool useCallbase = false)
    {
        var writer = new CodeWriter();
        WriteDocumentationComment(writer, function);
        var returnTypeName = GetManagedPublicReturnTypeName(function.ReturnType, compilation);
        var importMethodName = useCallbase
            ? GetManagedCallbaseImportMethodName(function)
            : GetManagedImportMethodName(function);
        var memberModifier = GetManagedInstanceMethodModifier(compilation, record, function);

        if (memberModifier.Contains("abstract ", StringComparison.Ordinal))
        {
            var parameterList = RenderManagedInstancePublicParameterList(compilation, record, function);
            writer.Write($$"""
                {{GetManagedVisibilityNormalized(compilation, record, function)}} {{memberModifier}}{{returnTypeName}} {{function.ManagedName}}({{parameterList}});
                """);
            return writer.ToString();
        }

        if (ShouldRenderPureVirtualManagedStub(function))
        {
            var parameterList = RenderManagedInstancePublicParameterList(compilation, record, function);
            writer.Write($$"""
                {{GetManagedVisibilityNormalized(compilation, record, function)}} {{memberModifier}}{{returnTypeName}} {{function.ManagedName}}({{parameterList}})
                {
                throw new global::System.InvalidOperationException("Native pure virtual method '{{function.ManagedName}}' is not callable from managed code.");
                }
                """);
            return writer.ToString();
        }

        if (function.Parameters.Any(IsInputPointerArrayParameter))
        {
            writer.Write(RenderManagedPointerArrayFunction(
                compilation,
                function,
                $"{GetManagedVisibilityNormalized(compilation, record, function)} {memberModifier}".TrimEnd(),
                returnTypeName,
                importMethodName,
                ["this"]));
            return writer.ToString();
        }

        if (HasUndefaultableParams(function))
        {
            WriteUndefaultableOverloads(compilation, writer, function, importMethodName, ["this"]);
            return writer.ToString();
        }

        if (HasUnexpressibleDefaultParams(function))
        {
            WriteManagedUnexpressibleDefaultConvenienceOverloads(
                writer,
                compilation,
                function,
                signaturePrefix: $"{GetManagedVisibilityNormalized(compilation, record, function)} {memberModifier}{returnTypeName} {function.ManagedName}",
                invocationPrefix: string.Empty,
                invocationTarget: function.ManagedName,
                returnTypeName: returnTypeName,
                isConstructor: false);
        }

        var parameterListFull = RenderManagedInstancePublicParameterList(compilation, record, function);

        writer.Write($$"""
            {{GetManagedVisibilityNormalized(compilation, record, function)}} {{memberModifier}}{{returnTypeName}} {{function.ManagedName}}({{parameterListFull}})
            {
            """);

        if (HasNativeDefaultDispatchParams(function))
        {
            WriteUnexpressibleDefaultCascade(compilation, writer, function, importMethodName, ["this"]);
        }
        else
        {
            var invocationPlan = BuildManagedInvocationPlan(compilation, function.Parameters);
            var invokeArguments = string.Join(", ", new[] { "this" }.Concat(invocationPlan.Arguments));

            foreach (var statement in invocationPlan.PreCallStatements)
            {
                writer.Write(statement);
            }

            if (function.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void)
            {
                writer.Write($$"""
                    __PInvoke.{{importMethodName}}({{invokeArguments}});
                    """);

                foreach (var statement in invocationPlan.PostCallStatements)
                {
                    writer.Write(statement);
                }
            }
        else if (function.ReturnType.Kind == BindingTypeKind.Handle)
        {
            writer.Write($$"""
                var __result = __PInvoke.{{importMethodName}}({{invokeArguments}});
                """);

                foreach (var statement in invocationPlan.PostCallStatements)
                {
                    writer.Write(statement);
                }

            writer.Write($$"""
                return {{BuildManagedHandleReturnResultExpression(compilation, function.ReturnType, "__result", ReturnsOwnedHandle(function))}};
                """);
        }
            else if (function.ReturnType.MarshallerTypeName is not null)
            {
                WriteManagedReturnWithMarshallerConversion(compilation, writer, function, importMethodName, invokeArguments, invocationPlan);
            }
            else
            {
                writer.Write($$"""
                    var __result = __PInvoke.{{importMethodName}}({{invokeArguments}});
                    """);

                foreach (var statement in invocationPlan.PostCallStatements)
                {
                    writer.Write(statement);
                }

                writer.Write($$"""
                    return {{BuildManagedImportToPublicExpression(compilation, function.ReturnType, "__result")}};
                    """);
            }
        }

        writer.Write(
            """
            }
            """);
        return writer.ToString();
    }

    private static string RenderManagedPointerArrayFunction(
        BindingCompilation compilation,
        FunctionBinding function,
        string declarationPrefix,
        string returnTypeName,
        string importMethodName,
        IReadOnlyList<string> invocationPrefix)
    {
        if (function.ReturnType.MarshallerTypeName is not null)
        {
            throw new NotSupportedException($"Pointer-array method '{function.ManagedName}' does not support custom return marshalling yet.");
        }

        var writer = new CodeWriter();
        var firstUndefaultable = GetFirstUndefaultableParamIndex(function);
        if (ShouldEmitUndefaultablePrefixOverload(compilation, function))
        {
            var prefixParameters = function.Parameters.Take(firstUndefaultable).ToArray();
            writer.Write(RenderManagedPointerArrayFunctionCore(
                compilation,
                function,
                declarationPrefix,
                returnTypeName,
                importMethodName,
                GetUnexpressibleVariantImportName(importMethodName, firstUndefaultable),
                invocationPrefix,
                prefixParameters,
                firstUndefaultable));
            writer.WriteLine();
        }

        writer.Write(RenderManagedPointerArrayFunctionCore(
            compilation,
            function,
            declarationPrefix,
            returnTypeName,
            importMethodName,
            importMethodName,
            invocationPrefix,
            function.Parameters.ToArray(),
            function.Parameters.Count));
        return writer.ToString();
    }

    private static string RenderManagedPointerArrayFunctionCore(
        BindingCompilation compilation,
        FunctionBinding function,
        string declarationPrefix,
        string returnTypeName,
        string importMethodName,
        string directImportMethodName,
        IReadOnlyList<string> invocationPrefix,
        IReadOnlyList<ParameterBinding> parameters,
        int parameterCount)
    {
        var writer = new CodeWriter();
        var parameterList = RenderManagedPublicParameterList(parameters, compilation);
        writer.Write($$"""
            {{declarationPrefix}} {{returnTypeName}} {{function.ManagedName}}({{parameterList}})
            {
            """);

        var pointerArrayParameters = parameters.Where(IsInputPointerArrayParameter).ToArray();
        foreach (var parameter in pointerArrayParameters.Where(IsInputHandlePointerArrayParameter))
        {
            var abiSpanName = GetPointerArrayAbiSpanName(parameter);
            var indexName = $"{abiSpanName}_i";
            writer.Write($$"""
                global::System.Span<nint> {{abiSpanName}} = {{parameter.ManagedName}}.Length <= 64 ? stackalloc nint[{{parameter.ManagedName}}.Length] : new nint[{{parameter.ManagedName}}.Length];
                for (var {{indexName}} = 0; {{indexName}} < {{parameter.ManagedName}}.Length; {{indexName}}++)
                {
                    {{abiSpanName}}[{{indexName}}] = global::SharpCpp.Support.NativeObject.GetHandle({{parameter.ManagedName}}[{{indexName}}]);
                }
                """);
        }

        var invocationPlan = BuildManagedInvocationPlan(compilation, parameters);
        var hasNativeDefaultDispatch = GetNativeDefaultDispatchParameterIndices(function)
            .Any(index => index < parameterCount);
        if (!hasNativeDefaultDispatch)
        {
            foreach (var statement in invocationPlan.PreCallStatements)
            {
                writer.Write(statement);
            }
        }

        if (pointerArrayParameters.Length > 0)
        {
            writer.WriteLine("unsafe");
            writer.WriteLine("{");
            foreach (var parameter in pointerArrayParameters)
            {
                var pointerTypeName = parameter.Type.Kind == BindingTypeKind.Enum
                    ? GetManagedPublicReturnTypeName(parameter.Type, compilation)
                    : GetManagedImportTypeName(parameter.Type, compilation);
                var spanName = IsInputHandlePointerArrayParameter(parameter)
                    ? GetPointerArrayAbiSpanName(parameter)
                    : parameter.ManagedName;
                writer.WriteLine($"fixed ({pointerTypeName}* {GetPointerArrayPointerName(parameter)} = {spanName})");
            }
            writer.WriteLine("{");
        }

        var invokeArguments = string.Join(", ", invocationPrefix.Concat(invocationPlan.Arguments));

        if (hasNativeDefaultDispatch)
        {
            WriteUnexpressibleDefaultCascade(
                compilation,
                writer,
                function,
                importMethodName,
                invocationPrefix,
                parameterCount);
        }
        else if (function.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void)
        {
            writer.WriteLine($"__PInvoke.{directImportMethodName}({invokeArguments});");
            foreach (var statement in invocationPlan.PostCallStatements)
            {
                writer.Write(statement);
            }
        }
        else if (function.ReturnType.Kind == BindingTypeKind.Handle)
        {
            writer.WriteLine($"var __result = __PInvoke.{directImportMethodName}({invokeArguments});");
            foreach (var statement in invocationPlan.PostCallStatements)
            {
                writer.Write(statement);
            }
            writer.WriteLine($"return {BuildManagedHandleReturnResultExpression(compilation, function.ReturnType, "__result", ReturnsOwnedHandle(function))};");
        }
        else
        {
            writer.WriteLine($"var __result = __PInvoke.{directImportMethodName}({invokeArguments});");
            foreach (var statement in invocationPlan.PostCallStatements)
            {
                writer.Write(statement);
            }
            writer.WriteLine($"return {BuildManagedImportToPublicExpression(compilation, function.ReturnType, "__result")};");
        }

        if (pointerArrayParameters.Length > 0)
        {
            writer.WriteLine("}");
            writer.WriteLine("}");
        }
        writer.WriteLine("}");
        return writer.ToString();
    }

    private static bool ShouldUseNewModifier(BindingCompilation compilation, RecordBinding record, FunctionBinding function)
    {
        if (function.ManagedName switch
            {
                nameof(GetType) when function.Parameters.Count == 0 => true,
                nameof(GetHashCode) when function.Parameters.Count == 0 => true,
                _ => false
            })
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(record.ManagedBaseTypeName))
        {
            return false;
        }

        if (record.ManagedBaseTypeName.Contains('.', StringComparison.Ordinal))
        {
            return false;
        }

        var visited = new HashSet<string>(StringComparer.Ordinal) { record.SymbolId };
        for (var current = record.GetPrimaryBaseRecord(compilation); IsManagedChainBase(current); current = current!.GetPrimaryBaseRecord(compilation))
        {
            if (!visited.Add(current!.SymbolId))
            {
                break;
            }

            if (current.Members.OfType<FunctionBinding>().Any(candidate =>
                    !candidate.IsIgnored() &&
                    ManagedSignaturesMatch(candidate, function)))
            {
                return true;
            }
        }

        return false;
    }

    private static string RenderManagedDispose(RecordBinding record, bool canEmitDeleteThunk)
    {
        if (!record.EmitDefaultNativeReleaseOverride)
        {
            return string.Empty;
        }

        if (record.HasMetadata(MetadataKeys.NativeInterfaceReleaseMethod))
        {
            return """
                protected override bool ReleaseNativeHandle(nint handle)
                {
                if (OwnsNativeHandle)
                {
                    __PInvoke.ReleaseOwnedHandle(handle);
                }
                return true;
                }
                """;
        }

        if (!canEmitDeleteThunk)
        {
            return """
                protected override bool ReleaseNativeHandle(nint handle)
                {
                return true;
                }
                """;
        }

        return """
            protected override bool ReleaseNativeHandle(nint handle)
            {
            if (OwnsNativeHandle)
            {
                __PInvoke.Delete(handle);
            }
            return true;
            }
            """;
    }

    private static string RenderManagedDeleteImportDeclaration(
        BindingCompilation compilation,
        RecordBinding record,
        string importMethodName = "Delete")
    {
        return $$"""
            [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "{{GetNativeDeleteEntryPointName(compilation, record)}}", StringMarshalling = StringMarshalling.Utf8)]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal static partial void {{importMethodName}}(nint self);
            """;
    }

    private static string RenderManagedArrayElementImportDeclaration(BindingCompilation compilation, RecordBinding record)
    {
        return $$"""
            [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "{{compilation.EntryPointPrefix}}_{{SanitizeNativeEntrypointName(record.CppQualifiedName)}}_array_element", StringMarshalling = StringMarshalling.Utf8)]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal static partial nint GetArrayElement({{record.ManagedName}} self, nuint index);
            """;
    }

    private static string RenderManagedNativeInterfaceQueryImportDeclaration(
        BindingCompilation compilation,
        RecordBinding record,
        NativeInterfaceQueryAnnotation query)
    {
        var sourceRecord = compilation.Records.Single(candidate =>
            string.Equals(candidate.CppQualifiedName, query.SourceRecordCppQualifiedName, StringComparison.Ordinal));
        return $$"""
            [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "{{compilation.EntryPointPrefix}}_{{SanitizeNativeEntrypointName(record.CppQualifiedName)}}_query_interface", StringMarshalling = StringMarshalling.Utf8)]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal static partial nint QueryInterface({{sourceRecord.ManagedName}} source);
            """;
    }

    private static string RenderManagedNativeInterfaceReleaseImportDeclaration(BindingCompilation compilation, RecordBinding record)
    {
        return $$"""
            [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "{{compilation.EntryPointPrefix}}_{{SanitizeNativeEntrypointName(record.CppQualifiedName)}}_release_interface", StringMarshalling = StringMarshalling.Utf8)]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal static partial void ReleaseOwnedHandle(nint self);
            """;
    }

    private static string RenderManagedCloneImportDeclaration(BindingCompilation compilation, RecordBinding record)
    {
        var returnTypeName = GetManagedImportReturnTypeName(compilation, record.HandleType);
        var returnAttribute = GetManagedImportReturnAttribute(compilation, record.HandleType, ownsHandle: true);
        var importMethodLine = string.IsNullOrWhiteSpace(returnAttribute)
            ? $"internal static partial {returnTypeName} CloneCopyCtor({record.ManagedName} self);"
            : $"{returnAttribute}\ninternal static partial {returnTypeName} CloneCopyCtor({record.ManagedName} self);";

        return $$"""
            [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "{{GetNativeCloneEntryPointName(compilation, record)}}", StringMarshalling = StringMarshalling.Utf8)]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            {{importMethodLine}}
            """;
    }

    private static string RenderManagedCloneMethod(BindingCompilation compilation, RecordBinding record)
    {
        var writer = new CodeWriter();

        if (!record.HasOwnManagedNonFieldMemberNamed("Clone"))
        {
            var hidesBaseClone = InheritsCloneInterface(compilation, record);
            writer.Write($$"""
                public {{(hidesBaseClone ? "new " : string.Empty)}}{{record.ManagedName}} Clone()
                {
                var __result = __PInvoke.CloneCopyCtor(this);
                if (__result is null)
                {
                throw new NotSupportedException("SharpCpp could not clone this native object because the native type is not copy-constructible.");
                }
                return __result;
                }

                object global::System.ICloneable.Clone()
                {
                return Clone();
                }
                """);
        }
        else
        {
            writer.Write($$"""
                object global::System.ICloneable.Clone()
                {
                var __result = __PInvoke.CloneCopyCtor(this);
                if (__result is null)
                {
                throw new NotSupportedException("SharpCpp could not clone this native object because the native type is not copy-constructible.");
                }
                return __result;
                }
                """);
        }

        return writer.ToString();
    }

    private static string RenderManagedAccessorProperty(BindingCompilation compilation, AccessorProperty property)
    {
        var writer = new CodeWriter();
        var type = property.Getter?.ReturnType ?? property.Setter!.Parameters[0].Type;
        var propertyTypeName = GetManagedPublicReturnTypeName(type, compilation);

        writer.Write($$"""
            {{GetManagedVisibility(GetAccessorPropertyAccessModifier(property))}} {{propertyTypeName}} {{property.PropertyName}}
            {
            """);

        if (property.Getter is not null)
        {
            if (property.Getter.ReturnType.Kind == BindingTypeKind.Handle)
            {
                writer.Write($$"""
                    get
                    {
                    return {{BuildManagedHandleReturnResultExpression(compilation, property.Getter.ReturnType, $"__PInvoke.{GetManagedImportMethodName(property.Getter)}(this)", ownsHandle: false)}};
                    }
                    """);
            }
            else
            {
                writer.Write($$"""
                    get
                    {
                    return {{BuildManagedImportToPublicExpression(compilation, property.Getter.ReturnType, $"__PInvoke.{GetManagedImportMethodName(property.Getter)}(this)")}};
                    }
                    """);
            }
        }

        if (property.Setter is not null)
        {
            var requiresWrapping = RequiresHandleWrapping(property.Setter.Parameters[0].Type);
            var useManagedHandleImport = CanUseManagedHandleParameterImport(compilation, property.Setter.Parameters[0], property.Setter.Parameters[0].Type);
            var setterArgument = useManagedHandleImport
                ? "value"
                : requiresWrapping
                ? "global::SharpCpp.Support.NativeObject.GetHandle(value)"
                : BuildManagedPublicToImportExpression(compilation, property.Setter.Parameters[0].Type, "value");
            var setterKeepAlive = requiresWrapping
                ? "\n            global::System.GC.KeepAlive(value);"
                : string.Empty;
            writer.Write($$"""
                set
                {
                __PInvoke.{{GetManagedImportMethodName(property.Setter)}}(this, {{setterArgument}});{{setterKeepAlive}}
                }
                """);
        }

        writer.Write(
            """
            }
            """);
        return writer.ToString();
    }

    private static IReadOnlyList<AccessorProperty> CollectAccessorProperties(RecordBinding record)
    {
        var getters = new Dictionary<string, FunctionBinding>(StringComparer.Ordinal);
        var setters = new Dictionary<string, FunctionBinding>(StringComparer.Ordinal);

        foreach (var method in record.Members.OfType<FunctionBinding>())
        {
            if (method.HasMetadata(MetadataKeys.IgnoreReason))
            {
                continue;
            }

            if (!method.TryGetMetadata<AccessorPropertyAnnotation>(MetadataKeys.AccessorProperty, out var accessor) || accessor is null)
            {
                continue;
            }

            if (accessor.Kind == AccessorPropertyKind.Getter)
            {
                getters[accessor.PropertyName] = method;
            }
            else
            {
                setters[accessor.PropertyName] = method;
            }
        }

        return getters.Keys
            .Concat(setters.Keys)
            .Distinct(StringComparer.Ordinal)
            .Select(propertyName => new AccessorProperty(
                propertyName,
                getters.TryGetValue(propertyName, out var getter) ? getter : null,
                setters.TryGetValue(propertyName, out var setter) ? setter : null))
            .ToArray();
    }

    private static string RenderManagedConstructor(BindingCompilation compilation, RecordBinding record, FunctionBinding function, bool requiresDirector)
    {
        var writer = new CodeWriter();
        WriteDocumentationComment(writer, function);
        var constructorName = function.Parent is RecordBinding owningRecord
            ? owningRecord.ManagedName
            : NormalizeEnumManagedTypeName(function.ManagedName);
        var importMethodName = GetManagedImportMethodName(function);

        if (HasUndefaultableParams(function))
        {
            WriteUndefaultableConstructorOverloads(writer, compilation, record, function, constructorName, importMethodName, requiresDirector);
            return writer.ToString();
        }

        if (HasUnexpressibleDefaultParams(function))
        {
            WriteManagedUnexpressibleDefaultConvenienceOverloads(
                writer,
                compilation,
                function,
                signaturePrefix: $"{GetManagedVisibility(function.AccessModifier)} {constructorName}",
                invocationPrefix: ": this",
                invocationTarget: string.Empty,
                returnTypeName: null,
                isConstructor: true);
        }

        var parameterList = RenderManagedPublicParameterList(function.Parameters, compilation);
        var invokeArguments = string.Join(", ", function.Parameters.Select(parameter => RenderManagedInvocationArgument(compilation, parameter)));
        var hasHandleParams = function.Parameters.Any(static parameter => parameter.Type.Kind == BindingTypeKind.Handle);
        var hasNativeDefaultDispatch = HasNativeDefaultDispatchParams(function);
        var postConstructStatements = RenderManagedConstructorPostConstruct(record, requiresDirector);

        if (hasHandleParams || hasNativeDefaultDispatch)
        {
            var factoryName = $"__CreateHandle_{importMethodName}";

            writer.Write($$"""
                {{GetManagedVisibility(function.AccessModifier)}} {{constructorName}}({{parameterList}})
                    : this({{factoryName}}({{string.Join(", ", function.Parameters.Select(RenderManagedForwardingArgument))}}), true)
                {
                {{postConstructStatements}}
                }

                private static nint {{factoryName}}({{parameterList}})
                {
                """);

            if (hasNativeDefaultDispatch)
            {
                WriteUnexpressibleDefaultConstructorCascade(compilation, writer, function, importMethodName);
            }
            else
            {
                var invocationPlan = BuildManagedInvocationPlan(compilation, function.Parameters);
                writer.Write($$"""
                    var __handle = __PInvoke.{{importMethodName}}({{invokeArguments}});
                    """);

                foreach (var statement in invocationPlan.PostCallStatements)
                {
                    writer.Write(statement);
                }

                writer.Write(
                    """
                    return __handle;
                    """);
            }

            writer.Write(
                """
                }
                """);
        }
        else
        {
            writer.Write($$"""
                {{GetManagedVisibility(function.AccessModifier)}} {{constructorName}}({{parameterList}})
                    : this(__PInvoke.{{importMethodName}}({{invokeArguments}}), true)
                {
                {{postConstructStatements}}
                }
                """);
        }

        return writer.ToString();
    }

    private static string RenderManagedConstructorPostConstruct(RecordBinding record, bool requiresDirector)
    {
        var registerInstance = $"global::SharpCpp.Support.NativeObjectRegistry.RegisterInstance<{record.ManagedName}>(this);";
        return requiresDirector
            ? $"ConnectDirector();\n    {registerInstance}"
            : registerInstance;
    }

    private static void WriteUndefaultableConstructorOverloads(
        CodeWriter writer,
        BindingCompilation compilation,
        RecordBinding record,
        FunctionBinding function,
        string constructorName,
        string importMethodName,
        bool requiresDirector)
    {
        var firstU = GetFirstUndefaultableParamIndex(function);
        if (firstU < 0)
        {
            return;
        }

        var postConstructStatements = RenderManagedConstructorPostConstruct(record, requiresDirector);

        // =========================================================
        // Overload A: params before first undefaultable index
        // =========================================================
        if (ShouldEmitUndefaultablePrefixOverload(compilation, function))
        {
            var beforeParams = function.Parameters.Take(firstU).ToArray();
            var aParamList = string.Join(", ", beforeParams.Select(p => RenderManagedPublicParameter(p, compilation)));
            var aArgNames = string.Join(", ", beforeParams.Select(RenderManagedForwardingArgument));
            var aHasUnexpressible = beforeParams.Any(p => p.HasMetadata(MetadataKeys.UnexpressibleDefault));

            var aFactoryName = $"__CreateHandle_{importMethodName}_OverloadA";

            writer.Write($$"""
                public {{constructorName}}({{aParamList}})
                    : this({{aFactoryName}}({{aArgNames}}), true)
                {
                {{postConstructStatements}}
                }

                private static nint {{aFactoryName}}({{aParamList}})
                {
                """);

            if (firstU == 0)
            {
                writer.Write($$"""
                    var __handle = __PInvoke.{{GetUnexpressibleVariantImportName(importMethodName, 0)}}();
                    return __handle;
                    """);
            }
            else if (aHasUnexpressible)
            {
                WriteUnexpressibleDefaultConstructorCascade(compilation, writer, function, importMethodName, firstU);
            }
            else
            {
                var aPlan = BuildManagedInvocationPlan(compilation, beforeParams);
                var aInvokeArgs = string.Join(", ", beforeParams.Select(parameter => RenderManagedInvocationArgument(compilation, parameter)));
                writer.Write($$"""
                    var __handle = __PInvoke.{{GetUnexpressibleVariantImportName(importMethodName, firstU)}}({{aInvokeArgs}});
                    """);
                foreach (var statement in aPlan.PostCallStatements)
                {
                    writer.Write(statement);
                }
                writer.Write("""
                    return __handle;
                    """);
            }

            writer.Write("""
                }
                """);
            writer.WriteLine();
        }

        // =========================================================
        // Overload B: all params, before/first undefaultable are required
        // =========================================================
        {
            var bParamParts = new List<string>();
            for (var i = 0; i < function.Parameters.Count; i++)
            {
                var param = function.Parameters[i];
                if (i < firstU || param.HasMetadata(MetadataKeys.UndefaultableParam))
                {
                    bParamParts.Add(RenderManagedPublicParameterRequired(param, compilation));
                }
                else
                {
                    bParamParts.Add(RenderManagedPublicParameter(param, compilation));
                }
            }
            var bParamList = string.Join(", ", bParamParts);
            var bArgNames = string.Join(", ", function.Parameters.Select(RenderManagedForwardingArgument));
            var bFactoryName = $"__CreateHandle_{importMethodName}_OverloadB";
            var bHasAfterUnexpressible = function.Parameters.Skip(firstU).Any(p => p.HasMetadata(MetadataKeys.UnexpressibleDefault));

            writer.Write($$"""
                public {{constructorName}}({{bParamList}})
                    : this({{bFactoryName}}({{bArgNames}}), true)
                {
                {{postConstructStatements}}
                }

                private static nint {{bFactoryName}}({{bParamList}})
                {
                """);

            if (bHasAfterUnexpressible)
            {
                // Cascade only for after-firstU unexpressible defaults.
                // We use a filtered approach: create a projection of the function
                // that only has the after-firstU params, so the standard cascade
                // only generates conditions/variants for those positions.
                // Reuse the cascade but with a builder that adjusts param names.
                var uFirst = GetFirstUndefaultableParamIndex(function);
                var afterDefaultIndices = new List<int>();
                for (var i = uFirst; i < function.Parameters.Count; i++)
                {
                    if (function.Parameters[i].HasMetadata(MetadataKeys.UnexpressibleDefault))
                    {
                        afterDefaultIndices.Add(i);
                    }
                }

                // Write a targeted cascade for the after-params only.
                // Filter defaultIndices to only after-firstU positions, since
                // before-firstU params are non-nullable in Overload B.
                WriteUndefaultableConstructorCascade_Filtered(compilation, writer, function, importMethodName, uFirst, afterDefaultIndices);
            }
            else
            {
                var bInvokeArgs = string.Join(", ", function.Parameters.Select(parameter => RenderManagedInvocationArgument(compilation, parameter)));
                writer.Write($$"""
                    var __handle = __PInvoke.{{importMethodName}}({{bInvokeArgs}});
                    return __handle;
                    """);
            }

            writer.Write("""
                }
                """);
        }
    }

    private static string RenderManagedConstructorImportDeclaration(BindingCompilation compilation, FunctionBinding function)
    {
        var writer = new CodeWriter();
        var parameterList = string.Join(
            ", ",
            function.Parameters.Select(parameter => RenderManagedImportParameter(parameter, compilation)));
        var importMethodName = GetManagedImportMethodName(function);

        writer.Write($$"""
            [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "{{function.EntryPointName}}", StringMarshalling = StringMarshalling.Utf8)]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal static partial nint {{importMethodName}}({{parameterList}});
            """);
        return writer.ToString();
    }

    private static string RenderManagedConnectDirectorImportDeclaration(
        BindingCompilation compilation,
        RecordBinding record,
        IReadOnlyList<VirtualMethodSlotBinding> slots)
    {
        var parameterList = string.Join(
            ", ",
            new[] { "nint self" }.Concat(slots.Select((_, index) => $"nint slot{index}")));

        return $$"""
            [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "{{compilation.EntryPointPrefix}}_{{SanitizeNativeEntrypointName(record.CppQualifiedName)}}_connect_director", StringMarshalling = StringMarshalling.Utf8)]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal static partial void ConnectDirector({{parameterList}});
            """;
    }

    private static string RenderManagedConnectDirector(BindingCompilation compilation, RecordBinding record, IReadOnlyList<VirtualMethodSlotBinding> slots)
    {
        var callbackArguments = slots.Count == 0
            ? string.Empty
            : ", " + string.Join(
                ", ",
                slots.Select(slot => $"{GetManagedRecordTypeName(compilation, record)}.{GetVirtualCallbackPointerAccessorName(slot.ManagedCallbackMethod)}(runtimeType)"));

        return $$"""
            private void ConnectDirector()
            {
            var runtimeType = ((object)this).GetType();
            __PInvoke.ConnectDirector(DangerousGetHandle(){{callbackArguments}});
            }
            """;
    }

    private static string RenderManagedVirtualCallbackMembers(BindingCompilation compilation, RecordBinding record, VirtualMethodSlotBinding slot)
    {
        var method = slot.ManagedCallbackMethod;
        ValidateVirtualCallbackSupport(method);

        var ownerRecord = slot.ManagedCallbackOwnerRecord;
        var dispatchRecord = method.AccessModifier == BindingAccessModifier.Protected ? record : ownerRecord;
        var ownerTypeName = GetManagedRecordTypeName(compilation, ownerRecord);
        var dispatchTypeName = GetManagedRecordTypeName(compilation, dispatchRecord);
        var methodNameExpression = method.AccessModifier == BindingAccessModifier.Protected
            ? $"\"{method.ManagedName}\""
            : $"nameof({ownerTypeName}.{method.ManagedName})";
        var writer = new CodeWriter();
        var callbackReturnType = GetManagedVirtualCallbackAbiTypeName(compilation, method.ReturnType);
        var callbackParameterList = string.Join(
            ", ",
            new[] { "nint self" }.Concat(method.Parameters.Select(parameter => RenderManagedCallbackParameter(compilation, parameter))));
        var callbackFunctionPointerTypes = string.Join(
            ", ",
            new[] { "nint" }
                .Concat(method.Parameters.Select(parameter => GetManagedVirtualCallbackAbiTypeName(compilation, parameter.Type)))
                .Append(callbackReturnType));
        var invocationArguments = string.Join(
            ", ",
            method.Parameters.Select(parameter => BuildManagedCallbackInvocationArgument(compilation, parameter)));

        var callbackPointerFieldName = $"{GetVirtualCallbackDispatchName(method)}Pointer";
        writer.Write($$"""
            private static readonly unsafe nint {{callbackPointerFieldName}} =
                global::{{compilation.ManagedNamespace}}.{{compilation.ManagedClassName}}.{{GetVirtualCallbackEntryPointName(record, method)}}Pointer;

            internal static unsafe nint {{GetVirtualCallbackPointerAccessorName(method)}}(Type runtimeType)
            {
            return HasManagedOverride(runtimeType, typeof({{ownerTypeName}}), {{methodNameExpression}}, {{FormatReflectionParameterTypes(compilation, method)}})
                ? {{callbackPointerFieldName}}
                : 0;
            }

            """);

        writer.Write($$"""
            internal static {{callbackReturnType}} {{GetVirtualCallbackDispatchName(method)}}({{callbackParameterList}})
            {
            var owner = {{dispatchTypeName}}.CreateManagedWrapper(self, ownsHandle: false);
            """);

        var invocation = $"owner.{method.ManagedName}({invocationArguments})";
        if (method.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void)
        {
            writer.Write($$"""
                {{invocation}};
            }
            """);
            return writer.ToString();
        }

        if (method.ReturnType.Kind == BindingTypeKind.Handle)
        {
            var handleExpression = ReturnsOwnedHandle(method)
                ? "global::SharpCpp.Support.NativeObject.TransferHandleOwnership(__result)"
                : "global::SharpCpp.Support.NativeObject.GetHandle(__result)";
            writer.Write($$"""
                var __result = {{invocation}};
                return {{handleExpression}};
            }
            """);
            return writer.ToString();
        }

        var returnExpression = method.ReturnType.PrimitiveKind == BindingPrimitiveKind.Boolean
            ? $"{invocation} ? (byte)1 : (byte)0"
            : BuildManagedPublicToImportExpression(compilation, method.ReturnType, invocation);
        writer.Write($$"""
            return {{returnExpression}};
            }
            """);
        return writer.ToString();
    }

    private static string RenderManagedStaticMethod(BindingCompilation compilation, RecordBinding record, FunctionBinding function)
    {
        var writer = new CodeWriter();
        WriteDocumentationComment(writer, function);
        var returnTypeName = GetManagedPublicReturnTypeName(function.ReturnType, compilation);
        var importMethodName = GetManagedImportMethodName(function);
        var declarationPrefix = $"{GetManagedVisibility(function.AccessModifier)} {(ShouldUseNewModifier(compilation, record, function) ? "new " : string.Empty)}static";

        if (function.Parameters.Any(IsInputPointerArrayParameter))
        {
            writer.Write(RenderManagedPointerArrayFunction(
                compilation,
                function,
                declarationPrefix,
                returnTypeName,
                importMethodName,
                []));
            return writer.ToString();
        }

        if (HasUndefaultableParams(function))
        {
            WriteUndefaultableOverloads(compilation, writer, function, importMethodName, [], declarationPrefix);
            return writer.ToString();
        }

        if (HasUnexpressibleDefaultParams(function))
        {
            WriteManagedUnexpressibleDefaultConvenienceOverloads(
                writer,
                compilation,
                function,
                signaturePrefix: $"{declarationPrefix} {returnTypeName} {function.ManagedName}",
                invocationPrefix: string.Empty,
                invocationTarget: function.ManagedName,
                returnTypeName: returnTypeName,
                isConstructor: false);
        }

        var parameterList = RenderManagedPublicParameterList(function.Parameters, compilation);

        writer.Write($$"""
            {{declarationPrefix}} {{returnTypeName}} {{function.ManagedName}}({{parameterList}})
            {
            """);

        if (HasNativeDefaultDispatchParams(function))
        {
            WriteUnexpressibleDefaultCascade(compilation, writer, function, importMethodName, []);
        }
        else
        {
            var invocationPlan = BuildManagedInvocationPlan(compilation, function.Parameters);
            var invokeArguments = string.Join(", ", invocationPlan.Arguments);

            foreach (var statement in invocationPlan.PreCallStatements)
            {
                writer.Write(statement);
            }

            if (function.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void)
            {
                writer.Write($$"""
                    __PInvoke.{{importMethodName}}({{invokeArguments}});
                    """);

                foreach (var statement in invocationPlan.PostCallStatements)
                {
                    writer.Write(statement);
                }
            }
            else if (function.ReturnType.Kind == BindingTypeKind.Handle)
            {
                writer.Write($$"""
                    var __result = __PInvoke.{{importMethodName}}({{invokeArguments}});
                    """);

                foreach (var statement in invocationPlan.PostCallStatements)
                {
                    writer.Write(statement);
                }

                writer.Write($$"""
                    return {{BuildManagedHandleReturnResultExpression(compilation, function.ReturnType, "__result", ReturnsOwnedHandle(function))}};
                    """);
            }
            else if (function.ReturnType.MarshallerTypeName is not null)
            {
                WriteManagedReturnWithMarshallerConversion(compilation, writer, function, importMethodName, invokeArguments, invocationPlan);
            }
            else
            {
                writer.Write($$"""
                    var __result = __PInvoke.{{importMethodName}}({{invokeArguments}});
                    """);

                foreach (var statement in invocationPlan.PostCallStatements)
                {
                    writer.Write(statement);
                }

                writer.Write($$"""
                    return {{BuildManagedImportToPublicExpression(compilation, function.ReturnType, "__result")}};
                    """);
            }
        }

        writer.Write(
            """
            }
            """);
        return writer.ToString();
    }

    private static string RenderManagedStaticImportDeclaration(BindingCompilation compilation, FunctionBinding function)
    {
        var writer = new CodeWriter();
        var parameterList = string.Join(
            ", ",
            function.Parameters.Select(parameter => RenderManagedImportParameter(parameter, compilation)));
        var returnTypeName = GetManagedImportReturnTypeName(compilation, function.ReturnType);
        var returnAttribute = GetManagedImportReturnAttribute(compilation, function.ReturnType, ReturnsOwnedHandle(function));
        var importMethodName = GetManagedImportMethodName(function);
        var unsafeModifier = parameterList.Contains('*', StringComparison.Ordinal) ? "unsafe " : string.Empty;
        var importMethodLine = string.IsNullOrWhiteSpace(returnAttribute)
            ? $"internal static {unsafeModifier}partial {returnTypeName} {importMethodName}({parameterList});"
            : $"{returnAttribute}\ninternal static {unsafeModifier}partial {returnTypeName} {importMethodName}({parameterList});";

        writer.Write(
            $$"""
            [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "{{function.EntryPointName}}", StringMarshalling = StringMarshalling.Utf8)]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            {{importMethodLine}}
            """);
        return writer.ToString();
    }

    private static string RenderManagedOperator(BindingCompilation compilation, RecordBinding record, FunctionBinding function)
    {
        var writer = new CodeWriter();
        WriteDocumentationComment(writer, function);

        if (function.Parameters.Count == 1 && function.OperatorToken == "==" && !HasOperator(record, "!="))
        {
            writer.Write($$"""
                public bool Equals({{record.ManagedName}} rhs)
                {
                return __PInvoke.{{function.ManagedName}}(this, rhs);
                }
            """);
            return writer.ToString();
        }

        if (function.Parameters.Count == 1 && function.OperatorToken == "<" && !HasOperator(record, ">"))
        {
            writer.Write($$"""
                public bool LessThan({{record.ManagedName}} rhs)
                {
                return __PInvoke.{{function.ManagedName}}(this, rhs);
                }
            """);
            return writer.ToString();
        }

        if (function.OperatorToken == "!" && function.Parameters.Count == 0)
        {
            var returnTypeName = GetManagedWrapperTypeName(function.ReturnType, compilation);
            writer.Write($$"""
                public static {{returnTypeName}} operator !({{record.ManagedName}} value)
                {
                return __PInvoke.{{function.ManagedName}}(value);
                }
                """);
            return writer.ToString();
        }

        if (function.IsConversionOperator)
        {
            var targetTypeName = GetManagedWrapperTypeName(function.ReturnType, compilation);
            var conversionKeyword = function.IsExplicitConversionOperator ? "explicit" : "implicit";
            writer.Write($$"""
                public static {{conversionKeyword}} operator {{targetTypeName}}({{record.ManagedName}} value)
                {
                return __PInvoke.{{function.ManagedName}}(value);
                }
                """);
            return writer.ToString();
        }

        if (function.OperatorToken == "+" && function.Parameters.Count == 1)
        {
            var returnTypeName = GetManagedWrapperTypeName(function.ReturnType, compilation);
            var parameter = RenderManagedPublicParameter(function.Parameters[0], compilation);
            if (function.ReturnType.Kind == BindingTypeKind.Handle)
            {
                writer.Write($$"""
                    public static {{returnTypeName}} operator +({{record.ManagedName}} left, {{parameter}})
                    {
                    return {{BuildManagedHandleReturnResultExpression(compilation, function.ReturnType, $"__PInvoke.{function.ManagedName}(left, {RenderManagedSafeHandleInvocationArgument(function.Parameters[0])})", ownsHandle: false)}};
                    }
                    """);
            }
            else
            {
                writer.Write($$"""
                    public static {{returnTypeName}} operator +({{record.ManagedName}} left, {{parameter}})
                    {
                    return __PInvoke.{{function.ManagedName}}(left, {{RenderManagedSafeHandleInvocationArgument(function.Parameters[0])}});
                    }
                    """);
            }
            return writer.ToString();
        }

        if (function.Parameters.Count == 1 && function.OperatorToken is "==" or "!=" or "<" or ">")
        {
            var returnTypeName = GetManagedWrapperTypeName(function.ReturnType, compilation);
            var parameterTypeName = GetManagedWrapperTypeName(function.Parameters[0].Type, compilation);
            writer.Write($$"""
                public static {{returnTypeName}} operator {{function.OperatorToken}}({{record.ManagedName}} left, {{parameterTypeName}} right)
                {
                return __PInvoke.{{function.ManagedName}}(left, right);
                }
                """);
            return writer.ToString();
        }

        if (function.OperatorToken is "+" or "-" && function.Parameters.Count == 0)
        {
            var returnTypeName = GetManagedWrapperTypeName(function.ReturnType, compilation);
            if (function.ReturnType.Kind == BindingTypeKind.Handle)
            {
                writer.Write($$"""
                    public static {{returnTypeName}} operator {{function.OperatorToken}}({{record.ManagedName}} value)
                    {
                    return {{BuildManagedHandleReturnResultExpression(compilation, function.ReturnType, $"__PInvoke.{function.ManagedName}(value)", ownsHandle: false)}};
                    }
                    """);
            }
            else
            {
                writer.Write($$"""
                    public static {{returnTypeName}} operator {{function.OperatorToken}}({{record.ManagedName}} value)
                    {
                    return __PInvoke.{{function.ManagedName}}(value);
                    }
                    """);
            }
            return writer.ToString();
        }

        throw new NotSupportedException($"Managed operator emission does not support '{function.ManagedName}'.");
    }

    private static bool HasOperator(RecordBinding record, string operatorToken)
    {
        return record.Members
            .OfType<FunctionBinding>()
            .Any(candidate => string.Equals(candidate.OperatorToken, operatorToken, StringComparison.Ordinal));
    }

    private static string RenderManagedOperatorImportDeclaration(BindingCompilation compilation, FunctionBinding function, string recordManagedName)
    {
        var writer = new CodeWriter();
        var parameterList = function.Parameters.Count == 0
            ? $"{recordManagedName} self"
            : string.Join(", ", new[] { $"{recordManagedName} self" }.Concat(function.Parameters.Select(p => RenderManagedImportParameter(p, compilation))));
        var returnTypeName = GetManagedImportReturnTypeName(compilation, function.ReturnType);
        var returnAttribute = GetManagedImportReturnAttribute(compilation, function.ReturnType, ReturnsOwnedHandle(function));
        var importMethodLine = string.IsNullOrWhiteSpace(returnAttribute)
            ? $"internal static partial {returnTypeName} {function.ManagedName}({parameterList});"
            : $"{returnAttribute}\ninternal static partial {returnTypeName} {function.ManagedName}({parameterList});";

        writer.Write(
            $$"""
            [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "{{function.EntryPointName}}", StringMarshalling = StringMarshalling.Utf8)]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            {{importMethodLine}}
            """);
        return writer.ToString();
    }

    private static string RenderManagedInstanceImportDeclaration(BindingCompilation compilation, FunctionBinding function, string recordManagedName)
    {
        var writer = new CodeWriter();
        var parameterList = string.Join(
            ", ",
            new[] { $"{recordManagedName} self" }.Concat(function.Parameters.Select(parameter => RenderManagedImportParameter(parameter, compilation))));
        var returnTypeName = GetManagedImportReturnTypeName(compilation, function.ReturnType);
        var returnAttribute = GetManagedImportReturnAttribute(compilation, function.ReturnType, ReturnsOwnedHandle(function));
        var importMethodName = GetManagedImportMethodName(function);
        var unsafeModifier = parameterList.Contains('*', StringComparison.Ordinal) ? "unsafe " : string.Empty;
        var importMethodLine = string.IsNullOrWhiteSpace(returnAttribute)
            ? $"internal static {unsafeModifier}partial {returnTypeName} {importMethodName}({parameterList});"
            : $"{returnAttribute}\ninternal static {unsafeModifier}partial {returnTypeName} {importMethodName}({parameterList});";

        writer.Write(
            $$"""
            [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "{{function.EntryPointName}}", StringMarshalling = StringMarshalling.Utf8)]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            {{importMethodLine}}
            """);
        return writer.ToString();
    }

    private static string RenderManagedCallbaseImportDeclaration(BindingCompilation compilation, FunctionBinding function, string recordManagedName)
    {
        var writer = new CodeWriter();
        var parameterList = string.Join(
            ", ",
            new[] { $"{recordManagedName} self" }.Concat(function.Parameters.Select(parameter => RenderManagedImportParameter(parameter, compilation))));
        var returnTypeName = GetManagedImportReturnTypeName(compilation, function.ReturnType);
        var returnAttribute = GetManagedImportReturnAttribute(compilation, function.ReturnType, ReturnsOwnedHandle(function));
        var importMethodName = GetManagedCallbaseImportMethodName(function);
        var importMethodLine = string.IsNullOrWhiteSpace(returnAttribute)
            ? $"internal static partial {returnTypeName} {importMethodName}({parameterList});"
            : $"{returnAttribute}\ninternal static partial {returnTypeName} {importMethodName}({parameterList});";
        var owningRecord = function.FindOwningRecord();
        var entryPointName = owningRecord is not null
            ? GetNativeCallbaseEntrypointName(compilation, owningRecord, function)
            : function.EntryPointName;
        var unsafeModifier = function.Parameters.Any(IsInputHandlePointerArrayParameter) ? "unsafe " : string.Empty;
        importMethodLine = string.IsNullOrWhiteSpace(returnAttribute)
            ? $"internal static {unsafeModifier}partial {returnTypeName} {importMethodName}({parameterList});"
            : $"{returnAttribute}\ninternal static {unsafeModifier}partial {returnTypeName} {importMethodName}({parameterList});";

        writer.Write(
            $$"""
            [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "{{entryPointName}}", StringMarshalling = StringMarshalling.Utf8)]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            {{importMethodLine}}
            """);
        return writer.ToString();
    }

    private static string RenderInterface(BindingCompilation compilation, RecordBinding record)
    {
        var writer = new CodeWriter();
        var interfaceBaseNames = GetManagedInterfaceBaseBindings(compilation, record)
            .Select(pair => GetManagedInterfaceTypeName(compilation, pair.Record, pair.Binding.Type))
            .ToArray();
        var baseClause = interfaceBaseNames.Length == 0
            ? string.Empty
            : $" : {string.Join(", ", interfaceBaseNames)}";

        writer.Write($$"""
            public partial interface {{record.GetManagedInterfaceName()}}{{GetGenericParameterList(record)}}{{baseClause}}
            {
            """);

        foreach (var method in record.GetRenderableInterfaceMethods(compilation).OrderBy(static method => method.ManagedName, StringComparer.Ordinal))
        {
            writer.WriteLine();
            var parameterList = RenderManagedPublicParameterList(method.Parameters, compilation);
            var returnTypeName = GetManagedPublicReturnTypeName(method.ReturnType, compilation);
            WriteDocumentationComment(writer, method);
            writer.Write($$"""
                {{returnTypeName}} {{method.ManagedName}}({{parameterList}});
                """);
        }

        writer.Write(
            """
            }
            """);
        return writer.ToString();
    }

    private static string RenderGenericSecondaryBaseInterfaceMethod(
        BindingCompilation compilation,
        RecordBinding baseRecord,
        FunctionBinding method,
        RecordGenericInstantiationBinding instantiation)
    {
        var writer = new CodeWriter();
        var concreteParameters = method.Parameters
            .Select(parameter => SubstituteGenericType(baseRecord, parameter.Type, instantiation))
            .ToArray();
        var concreteReturnType = SubstituteGenericType(baseRecord, method.ReturnType, instantiation);
        var parameterList = string.Join(", ", method.Parameters.Select((parameter, index) =>
            RenderConcreteManagedPublicParameter(compilation, parameter, concreteParameters[index])));
        var returnTypeName = GetManagedPublicReturnTypeName(concreteReturnType, compilation);
        var invocationArguments = new List<string>
        {
            "global::SharpCpp.Support.NativeObject.GetHandle(this)"
        };
        for (var index = 0; index < method.Parameters.Count; index++)
        {
            invocationArguments.Add(BuildGenericInvocationArgument(
                compilation,
                method.Parameters[index],
                concreteParameters[index]));
        }

        WriteDocumentationComment(writer, method);
        writer.WriteLine($"public {returnTypeName} {method.ManagedName}({parameterList})");
        writer.WriteLine("{");
        WriteGenericManagedMethodInvocation(
            writer,
            compilation,
            method,
            instantiation,
            returnTypeName,
            concreteReturnType,
            concreteParameters,
            GetGenericImportMethodName(method, instantiation),
            invocationArguments,
            "    ");
        writer.WriteLine("}");
        return writer.ToString();
    }

    private static IReadOnlyList<(RecordBaseBinding Binding, RecordBinding Record)> GetManagedInterfaceBaseBindings(
        BindingCompilation compilation,
        RecordBinding record)
    {
        var primaryBase = record.Bases.FirstOrDefault(static candidate => !candidate.IsVirtual);
        return record.Bases
            .Where(candidate => !candidate.IsVirtual && !ReferenceEquals(candidate, primaryBase))
            .Select(binding => (Binding: binding, Record: compilation.FindRecordBySymbolId(binding.RecordId)))
            .Where(static pair => pair.Record is not null)
            .Select(static pair => (pair.Binding, pair.Record!))
            .ToArray();
    }

    private static string GetManagedInterfaceTypeName(
        BindingCompilation compilation,
        RecordBinding interfaceRecord,
        BindingType implementedType)
    {
        var qualifiedName = $"global::{GetManagedNamespace(compilation, interfaceRecord)}.{interfaceRecord.GetManagedInterfaceName()}";
        if (!interfaceRecord.IsGeneric || implementedType.GenericArguments.Count == 0)
        {
            return qualifiedName;
        }

        var arguments = GetManagedVisibleGenericParameterIndices(interfaceRecord)
            .Where(index => index < implementedType.GenericArguments.Count)
            .Select(index => GetManagedPublicReturnTypeName(implementedType.GenericArguments[index], compilation));
        return $"{qualifiedName}<{string.Join(", ", arguments)}>";
    }

    private static string RenderEnum(EnumBinding @enum)
    {
        var writer = new CodeWriter();
        WriteDocumentationComment(writer, @enum);

        if (@enum.IsFlags)
        {
            writer.Write("[Flags]");
        }

        writer.Write($$"""
            public enum {{@enum.ManagedName}} : {{GetManagedEnumUnderlyingTypeName(@enum)}}
            {
            """);

        foreach (var value in @enum.Values)
        {
            writer.Write($$"""
                {{value.ManagedName}} = {{value.ValueExpression}},
                """);
        }

        writer.Write(
            """
            }
            """);
        return writer.ToString();
    }

    private static string RenderManagedProperty(BindingCompilation compilation, FieldBinding field, bool hidesInheritedMember = false)
    {
        var writer = new CodeWriter();
        WriteDocumentationComment(writer, field);
        var propertyTypeName = GetManagedFieldPropertyTypeName(compilation, field);
        var getterInvocation = field.IsStatic
            ? $"__PInvoke.Get_{field.ManagedName}()"
            : $"__PInvoke.Get_{field.ManagedName}(this)";
        writer.Write($$"""
            {{GetManagedVisibility(field.AccessModifier)}} {{(hidesInheritedMember ? "new " : string.Empty)}}{{(field.IsStatic ? "static " : string.Empty)}}{{propertyTypeName}} {{field.ManagedName}}
            {
            get
            {
            """);

        if (NeedsManagedFixedArrayProxy(field))
        {
            writer.Write($$"""
                return {{GetManagedFixedArrayProxyFieldName(field)}} ??= new {{GetManagedFixedArrayProxyClassName(field)}}(this);
                """);
        }
        else if (field.Type.Kind == BindingTypeKind.Handle)
        {
            writer.Write($$"""
                return {{BuildManagedHandleReturnResultExpression(compilation, field.Type, getterInvocation, field.ReturnsOwnedHandle)}};
                """);
        }
        else
        {
            writer.Write($$"""
                return {{BuildManagedImportToPublicExpression(compilation, field.Type, getterInvocation)}};
                """);
        }

        writer.Write(
            """
            }
            """);

        if (!field.IsReadOnly)
        {
            if (NeedsManagedFixedArrayProxy(field))
            {
                writer.Write($$"""
                    set
                    {
                    global::System.ArgumentNullException.ThrowIfNull(value);
                    if (value.Count != {{field.Type.FixedArrayLength ?? 0}})
                    {
                        throw new global::System.ArgumentException("Expected array length {{field.Type.FixedArrayLength ?? 0}}.", nameof(value));
                    }

                    var proxy = {{GetManagedFixedArrayProxyFieldName(field)}} ??= new {{GetManagedFixedArrayProxyClassName(field)}}(this);
                    for (var index = 0; index < value.Count; ++index)
                    {
                        proxy[index] = value[index];
                    }
                    global::System.GC.KeepAlive(value);
                    }
                    """);
            }
            else
            {
                var setterParameter = new ParameterBinding(
                    $"field:set:{field.NativeName}",
                    "value",
                    "value",
                    field.Type,
                    BindingParameterDirection.In,
                    field.Type.Kind is BindingTypeKind.Handle or BindingTypeKind.Enum
                        ? BindingParameterAbiKind.Value
                        : BindingParameterAbiKind.Pointer,
                    field.CppTypeName);
                var setterArgument = field.Type.IsFixedArray || IsRawPointerHandle(field.Type)
                    ? "value"
                    : CanUseManagedHandleParameterImport(compilation, setterParameter, field.Type)
                    ? "value"
                    : RequiresHandleWrapping(field.Type)
                    ? "global::SharpCpp.Support.NativeObject.GetHandle(value)"
                    : BuildManagedPublicToImportExpression(compilation, field.Type, "value");
                var setterPrecondition = field.Type.IsFixedArray
                ? $$"""
                    global::System.ArgumentNullException.ThrowIfNull(value);
                    if (value.Length != {{field.Type.FixedArrayLength ?? 0}})
                    {
                        throw new global::System.ArgumentException("Expected array length {{field.Type.FixedArrayLength ?? 0}}.", nameof(value));
                    }
            """
                    : string.Empty;
                var setterKeepAlive = field.Type.IsFixedArray || RequiresHandleWrapping(field.Type) && !IsRawPointerHandle(field.Type)
                    ? "\n            global::System.GC.KeepAlive(value);"
                    : string.Empty;
                var setterInvocation = field.IsStatic
                    ? $"__PInvoke.Set_{field.ManagedName}({setterArgument})"
                    : $"__PInvoke.Set_{field.ManagedName}(this, {setterArgument})";
                writer.Write($$"""
                    set
                    {
                    {{setterPrecondition}}
                    {{setterInvocation}};{{setterKeepAlive}}
                    }
                    """);
            }
        }

        writer.Write(
            """
            }
            """);
        return writer.ToString();
    }

    private static string GetManagedFieldPropertyTypeName(BindingCompilation compilation, FieldBinding field)
    {
        if (!NeedsManagedFixedArrayProxy(field))
        {
            return GetManagedPublicReturnTypeName(field.Type, compilation);
        }

        var elementType = field.Type.FixedArrayElementType
            ?? throw new InvalidOperationException($"Fixed array field '{field.ManagedName}' is missing element type metadata.");
        return $"global::SharpCpp.Support.IFixedArray<{GetManagedPublicReturnTypeName(elementType, compilation)}>";
    }

    private static bool NeedsManagedFixedArrayProxy(FieldBinding field)
    {
        return field.Type.IsFixedArray && field.Type.FixedArrayElementType?.Kind == BindingTypeKind.Handle;
    }

    private static string GetManagedFixedArrayProxyClassName(FieldBinding field)
    {
        return $"__{field.ManagedName}FixedArrayProxy";
    }

    private static string GetManagedFixedArrayProxyFieldName(FieldBinding field)
    {
        return $"__{char.ToLowerInvariant(field.ManagedName[0])}{field.ManagedName[1..]}FixedArrayProxy";
    }

    private static string RenderManagedFixedArrayProxy(BindingCompilation compilation, FieldBinding field, string recordManagedName)
    {
        if (!NeedsManagedFixedArrayProxy(field))
        {
            return string.Empty;
        }

        var writer = new CodeWriter();
        var elementType = field.Type.FixedArrayElementType
            ?? throw new InvalidOperationException($"Fixed array field '{field.ManagedName}' is missing element type metadata.");
        var elementTypeName = GetManagedPublicReturnTypeName(elementType, compilation);
        var setterKeepAlive = elementType.Kind == BindingTypeKind.Handle
            ? "\n            global::System.GC.KeepAlive(value);"
            : string.Empty;

        writer.Write($$"""
            private {{GetManagedFixedArrayProxyClassName(field)}}? {{GetManagedFixedArrayProxyFieldName(field)}};

            private sealed class {{GetManagedFixedArrayProxyClassName(field)}}({{recordManagedName}} owner)
                : global::SharpCpp.Support.FixedArrayProxy<{{elementTypeName}}>({{field.Type.FixedArrayLength ?? 0}})
            {
                private readonly {{recordManagedName}} owner = owner;

                protected override {{elementTypeName}} GetAtCore(nuint index)
                {
                    var result = __PInvoke.Get_{{field.ManagedName}}_Item(owner, index);
                    global::System.GC.KeepAlive(owner);
                    return result;
                }

                protected override void SetAtCore(nuint index, {{elementTypeName}} value)
                {
                    __PInvoke.Set_{{field.ManagedName}}_Item(owner, index, value);
                    global::System.GC.KeepAlive(owner);{{setterKeepAlive}}
                }
            }
            """);
        return writer.ToString();
    }

    private static string RenderManagedFieldGetterImportDeclaration(BindingCompilation compilation, FieldBinding field, string recordManagedName, string entryPointRecordCppTypeName)
    {
        var writer = new CodeWriter();
        var returnTypeName = GetManagedImportReturnTypeName(compilation, field.Type);
        var returnAttribute = GetManagedImportReturnAttribute(compilation, field.Type, field.ReturnsOwnedHandle);
        var parameters = field.IsStatic ? string.Empty : $"{recordManagedName} self";
        var importMethodLine = string.IsNullOrWhiteSpace(returnAttribute)
            ? $"internal static partial {returnTypeName} Get_{field.ManagedName}({parameters});"
            : $"{returnAttribute}\ninternal static partial {returnTypeName} Get_{field.ManagedName}({parameters});";

        writer.Write($$"""
            [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "{{GetNativeFieldGetterEntrypointName(compilation, entryPointRecordCppTypeName, field)}}", StringMarshalling = StringMarshalling.Utf8)]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            {{importMethodLine}}
            """);
        return writer.ToString();
    }

    private static string RenderManagedFieldSetterImportDeclaration(BindingCompilation compilation, FieldBinding field, string recordManagedName, string entryPointRecordCppTypeName)
    {
        var writer = new CodeWriter();
        var parameter = RenderManagedImportParameter(
            new ParameterBinding(
                $"field:set:{field.NativeName}",
                "value",
                "value",
                field.Type,
                BindingParameterDirection.In,
                field.Type.Kind is BindingTypeKind.Handle or BindingTypeKind.Enum
                    ? BindingParameterAbiKind.Value
                    : BindingParameterAbiKind.Pointer,
                field.CppTypeName),
            compilation);
        var parameters = field.IsStatic ? parameter : $"{recordManagedName} self, {parameter}";

        writer.Write($$"""
            [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "{{GetNativeFieldSetterEntrypointName(compilation, entryPointRecordCppTypeName, field)}}", StringMarshalling = StringMarshalling.Utf8)]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal static partial void Set_{{field.ManagedName}}({{parameters}});
            """);
        return writer.ToString();
    }

    private static string RenderManagedFieldItemGetterImportDeclaration(BindingCompilation compilation, FieldBinding field, string recordManagedName, string entryPointRecordCppTypeName)
    {
        var writer = new CodeWriter();
        var elementType = field.Type.FixedArrayElementType
            ?? throw new InvalidOperationException($"Fixed array field '{field.ManagedName}' is missing element type metadata.");
        var returnTypeName = GetManagedImportReturnTypeName(compilation, elementType);
        var returnAttribute = GetManagedImportReturnAttribute(compilation, elementType, ownsHandle: false);
        var importMethodLine = string.IsNullOrWhiteSpace(returnAttribute)
            ? $"internal static partial {returnTypeName} Get_{field.ManagedName}_Item({recordManagedName} self, nuint index);"
            : $"{returnAttribute}\ninternal static partial {returnTypeName} Get_{field.ManagedName}_Item({recordManagedName} self, nuint index);";

        writer.Write($$"""
            [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "{{GetNativeFieldItemGetterEntrypointName(compilation, entryPointRecordCppTypeName, field)}}", StringMarshalling = StringMarshalling.Utf8)]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            {{importMethodLine}}
            """);
        return writer.ToString();
    }

    private static string RenderManagedPointerCountFieldItemGetterImportDeclaration(
        BindingCompilation compilation,
        FieldBinding field,
        PointerCountNativeArrayShape shape,
        string recordManagedName,
        string entryPointRecordCppTypeName)
    {
        var writer = new CodeWriter();
        var elementType = shape.ElementType;
        var returnTypeName = GetManagedImportReturnTypeName(compilation, elementType);
        var returnAttribute = shape.IsStringArray
            ? string.Empty
            : GetManagedImportReturnAttribute(compilation, elementType, ownsHandle: false);
        var importMethodLine = string.IsNullOrWhiteSpace(returnAttribute)
            ? $"internal static partial {returnTypeName} Get_{field.ManagedName}_Item({recordManagedName} self, nuint index);"
            : $"{returnAttribute}\ninternal static partial {returnTypeName} Get_{field.ManagedName}_Item({recordManagedName} self, nuint index);";

        writer.Write($$"""
            [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "{{GetNativeFieldItemGetterEntrypointName(compilation, entryPointRecordCppTypeName, field)}}", StringMarshalling = StringMarshalling.Utf8)]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            {{importMethodLine}}
            """);
        return writer.ToString();
    }

    private static string RenderManagedFieldItemSetterImportDeclaration(BindingCompilation compilation, FieldBinding field, string recordManagedName, string entryPointRecordCppTypeName)
    {
        var writer = new CodeWriter();
        var elementType = field.Type.FixedArrayElementType
            ?? throw new InvalidOperationException($"Fixed array field '{field.ManagedName}' is missing element type metadata.");
        var parameter = RenderManagedImportParameter(
            new ParameterBinding(
                $"field:set:item:{field.NativeName}",
                "value",
                "value",
                elementType,
                BindingParameterDirection.In,
                elementType.Kind is BindingTypeKind.Handle or BindingTypeKind.Enum
                    ? BindingParameterAbiKind.Value
                    : BindingParameterAbiKind.Pointer,
                elementType.CppTypeName),
            compilation);

        writer.Write($$"""
            [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "{{GetNativeFieldItemSetterEntrypointName(compilation, entryPointRecordCppTypeName, field)}}", StringMarshalling = StringMarshalling.Utf8)]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal static partial void Set_{{field.ManagedName}}_Item({{recordManagedName}} self, nuint index, {{parameter}});
            """);
        return writer.ToString();
    }

    private static string RenderNativeBindings(
        BindingCompilation compilation,
        Func<BindingNode, bool>? memberFilter = null,
        string? groupHeaderPath = null,
        bool includeCallbackReleaseRegistration = true)
    {
        var writer = new CodeWriter();
        var allFunctions = compilation.Functions.AsEnumerable();
        var allRecords = compilation.Records.Where(record => IsSharedManagedWrapper(record) || !record.GetMetadata<bool>(MetadataKeys.SuppressEmission)).AsEnumerable();

        if (memberFilter is not null)
        {
            allFunctions = allFunctions.Where(f => memberFilter(f));
            allRecords = allRecords.Where(r => memberFilter(r));
        }

        var functions = allFunctions.OrderBy(static function => function.ManagedName, StringComparer.Ordinal).ToArray();
        var records = allRecords.OrderBy(static record => record.ManagedName, StringComparer.Ordinal).ToArray();

        writer.Write(
            """
            // <auto-generated />
            #include "SharpCpp/Export.hpp"
            #include "SharpCpp/Marshaller.hpp"
            #include "SharpCpp/detail/MarshallerSupport.hpp"
            #include <stdexcept>
            #include <typeinfo>

            using namespace SharpCpp;
            """);

        foreach (var headerFile in compilation.HeaderFiles.OrderBy(static header => header, StringComparer.Ordinal))
        {
            writer.Write($$"""

                #include "{{headerFile}}"
                """);
        }

        foreach (var headerFile in compilation.NativeSupportHeaders.OrderBy(static header => header, StringComparer.Ordinal))
        {
            writer.Write($$"""

                #include "{{headerFile}}"
                """);
        }

        if (includeCallbackReleaseRegistration)
        {
            writer.Write($$"""

                #if defined(__EMSCRIPTEN__)
                __attribute__((weak))
                #endif
                SHARPCPP_EXPORT_NAMED({{GetManagedCallbackReleaseRegistrationEntryPointName(compilation)}}) void {{GetManagedCallbackReleaseRegistrationEntryPointName(compilation)}}(void (*release)(void*))
                {
                    SharpCpp::detail::SetManagedCallbackRelease(release);
                }
                """);
        }

        foreach (var record in records.Where(record => record.RequiresDirector(compilation)).OrderBy(static record => record.ManagedName, StringComparer.Ordinal))
        {
            writer.WriteLine();
            writer.Write(RenderNativeDirectorClass(compilation, record));
        }

        var directorRecords = records.Where(record => record.RequiresDirector(compilation)).ToArray();
        foreach (var record in directorRecords)
        {
            var callbaseSlots = record.GetEffectiveVirtualMethodSlots(compilation)
                .Where(static slot => !slot.Method.IsPureVirtual)
                .ToArray();
            foreach (var slot in callbaseSlots)
            {
                var method = slot.Method;
                var parameterList = string.Join(", ", new[] { $"arg_t<{record.CppQualifiedName}*> self" }.Concat(method.Parameters.Select(RenderNativeParameter)));
                var abiArguments = string.Join(", ", method.Parameters.Select(static p => p.NativeName));
                var parameterCppList = string.Join(", ", method.Parameters.Select(static p => p.CppTypeName));
                var callbaseArguments = string.Join(", ", method.Parameters.Select(static p => p.NativeName));
                var directorName = GetNativeDirectorClassName(record);
                var helperName = GetNativeCallbaseHelperName(method);
                var marshallerTemplateArguments = BuildMarshallerInvokerTemplateArguments(
                    "FT",
                    new[] { $"{record.CppQualifiedName}*" }.Concat(method.Parameters.Select(static p => p.CppTypeName)));

                writer.WriteLine();
                if (method.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void)
                {
                    writer.Write($$"""
                        SHARPCPP_EXPORT ret_t<void> {{GetNativeCallbaseEntrypointName(compilation, record, method)}}({{parameterList}})
                        {
                        using FT = void({{record.CppQualifiedName}}*{{(parameterCppList.Length > 0 ? $", {parameterCppList}" : string.Empty)}});
                        MarshallerInvoker::invoke_as<{{marshallerTemplateArguments}}>(&{{directorName}}::{{helperName}}, self{{(callbaseArguments.Length > 0 ? $", {callbaseArguments}" : string.Empty)}});
                        }
                        """);
                }
                else
                {
                    writer.Write($$"""
                        SHARPCPP_EXPORT {{GetNativeEntrypointReturnType(method)}} {{GetNativeCallbaseEntrypointName(compilation, record, method)}}({{parameterList}})
                        {
                        using FT = {{method.CppReturnTypeName}}({{record.CppQualifiedName}}*{{(parameterCppList.Length > 0 ? $", {parameterCppList}" : string.Empty)}});
                        {{(RequiresOwnedRecordValueReturn(method)
                            ? $"return MarshallerInvoker::invoke_owned_as<{marshallerTemplateArguments}>(&{directorName}::{helperName}, self{(callbaseArguments.Length > 0 ? $", {callbaseArguments}" : string.Empty)});"
                            : $"return MarshallerInvoker::invoke_as<{marshallerTemplateArguments}>(&{directorName}::{helperName}, self{(callbaseArguments.Length > 0 ? $", {callbaseArguments}" : string.Empty)});")}}
                        }
                        """);
                }

                if (HasNativeDefaultDispatchParams(method) || HasUndefaultableParams(method))
                {
                    writer.WriteLine();
                    writer.Write(RenderNativeUnexpressibleDefaultCallbase(compilation, record, slot));
                }
            }
        }

        foreach (var record in records.Where(static record => record.IsGeneric && !record.HasMetadata(MetadataKeys.IgnoreReason)).OrderBy(static record => record.ManagedName, StringComparer.Ordinal))
        {
            var recordInstantiations = GetCanonicalManagedGenericInstantiations(compilation, record);
            if (IsSharedManagedWrapper(record) && !IsGenericArrayRecord(record))
            {
                foreach (var instantiation in GetSharedProviderInstantiations(compilation, record))
                {
                    writer.WriteLine();
                    writer.Write(RenderSharedProviderNativeCreate(compilation, record, instantiation));
                }
            }

            if (IsGenericArrayRecord(record))
            {
                writer.Write(RenderNativeGenericArrayRecord(compilation, record));
                continue;
            }

            foreach (var instantiation in recordInstantiations)
            {
                foreach (var constructor in record.Members.OfType<FunctionBinding>().Where(static function => function.IsConstructor && !function.HasMetadata(MetadataKeys.IgnoreReason)).OrderBy(static function => function.Parameters.Count))
                {
                    writer.WriteLine();
                    writer.Write(RenderNativeGenericConstructor(record, constructor, instantiation));
                }

                foreach (var method in record.Members.OfType<FunctionBinding>().Where(static function => !function.IsConstructor && !function.IsStatic && !function.IsGeneric && function.OperatorToken is null && !function.IsConversionOperator && !function.HasMetadata(MetadataKeys.IgnoreReason)).OrderBy(static function => function.ManagedName, StringComparer.Ordinal))
                {
                    writer.WriteLine();
                    writer.Write(RenderNativeGenericInstanceMethod(record, method, instantiation));
                }

                foreach (var method in record.Members.OfType<FunctionBinding>().Where(static function => function.IsStatic && !function.IsGeneric && function.OperatorToken is null && !function.IsConversionOperator && !function.HasMetadata(MetadataKeys.IgnoreReason)).OrderBy(static function => function.ManagedName, StringComparer.Ordinal))
                {
                    writer.WriteLine();
                    writer.Write(RenderNativeGenericStaticMethod(record, method, instantiation));
                }

                if (CanEmitClone(record, record.IsEffectivelyAbstract || record.HasUnimplementedAbstractBaseMethods(compilation), canEmitDeleteThunk: true))
                {
                    writer.WriteLine();
                    writer.Write(RenderNativeGenericClone(compilation, record, instantiation));
                }

                writer.WriteLine();
                writer.Write(RenderNativeGenericDelete(compilation, record, instantiation));
            }

            var genericMethods = record.Members.OfType<FunctionBinding>()
                .Where(static function => !function.IsConstructor && function.IsGeneric && function.OperatorToken is null && !function.IsConversionOperator && !function.HasMetadata(MetadataKeys.IgnoreReason))
                .OrderBy(static function => function.ManagedName, StringComparer.Ordinal).ToArray();
            foreach (var method in genericMethods)
            {
                var methodInstantiations = method.SupportedGenericInstantiations.ToArray();
                if (methodInstantiations.Length == 0)
                {
                    continue;
                }

                foreach (var instantiation in recordInstantiations)
                {
                    foreach (var methodInstantiation in methodInstantiations)
                    {
                        writer.WriteLine();
                        writer.Write(RenderNativeGenericMethodInstanceMethod(record, method, instantiation, methodInstantiation));
                    }
                }
            }
        }

        foreach (var function in functions)
        {
            if (function.Parent is RecordBinding || function.IsConstructor || function.HasMetadata(MetadataKeys.IgnoreReason))
            {
                continue;
            }

            if (function.IsGeneric)
            {
                foreach (var instantiation in function.SupportedGenericInstantiations)
                {
                    writer.WriteLine();
                    writer.Write(RenderNativeGenericFreeFunction(function, instantiation));
                }

                continue;
            }

            var parameterList = string.Join(", ", function.Parameters.Select(RenderNativeParameter));
            var abiArguments = string.Join(", ", function.Parameters.Select(static p => p.NativeName));
            var signature = BuildNativeFunctionSignature(function);
            var marshallerTemplateArguments = BuildMarshallerInvokerTemplateArguments("FT", function.Parameters.Select(static p => p.CppTypeName));
            var useNativeDefaultArgumentAdapter = function.GetMetadata<bool>(MetadataKeys.NativeDefaultArgumentAdapter);

            writer.WriteLine();
            writer.Write($$"""
                SHARPCPP_EXPORT_NAMED({{function.EntryPointName}}) {{GetNativeEntrypointReturnType(function)}} {{function.EntryPointName}}({{parameterList}})
                {
                using FT = {{signature}};
                """);

            if (useNativeDefaultArgumentAdapter)
            {
                var cppParameterList = string.Join(", ", function.Parameters.Select(
                    static parameter => $"{parameter.CppTypeName} {parameter.NativeName}"));
                writer.Write($$"""
                    auto fn = []({{cppParameterList}}) -> decltype(auto)
                    {
                    return {{function.CppQualifiedName}}({{abiArguments}});
                    };
                    {{(function.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void
                        ? $"MarshallerInvoker::invoke_as<{marshallerTemplateArguments}>(fn{(abiArguments.Length > 0 ? $", {abiArguments}" : string.Empty)});"
                        : RequiresOwnedRecordValueReturn(function)
                            ? $"return MarshallerInvoker::invoke_owned_as<{marshallerTemplateArguments}>(fn{(abiArguments.Length > 0 ? $", {abiArguments}" : string.Empty)});"
                            : $"return MarshallerInvoker::invoke_as<{marshallerTemplateArguments}>(fn{(abiArguments.Length > 0 ? $", {abiArguments}" : string.Empty)});")}}
                    """);
            }
            else if (function.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void)
            {
                writer.Write($$"""
                    MarshallerInvoker::invoke_as<{{marshallerTemplateArguments}}>(static_cast<std::add_pointer_t<FT>>(&{{function.CppQualifiedName}}){{(abiArguments.Length > 0 ? $", {abiArguments}" : string.Empty)}});
                    """);
            }
            else
            {
                writer.Write($$"""
                    {{(RequiresOwnedRecordValueReturn(function)
                        ? $"return MarshallerInvoker::invoke_owned_as<{marshallerTemplateArguments}>(static_cast<std::add_pointer_t<FT>>(&{function.CppQualifiedName}){(abiArguments.Length > 0 ? $", {abiArguments}" : string.Empty)});"
                        : $"return MarshallerInvoker::invoke_as<{marshallerTemplateArguments}>(static_cast<std::add_pointer_t<FT>>(&{function.CppQualifiedName}){(abiArguments.Length > 0 ? $", {abiArguments}" : string.Empty)});")}}
                    """);
            }

            writer.Write("""
                }
                """);

            if (function.GetMetadata<bool>(MetadataKeys.ExposeNativeFunctionPointer))
            {
                writer.WriteLine();
                writer.Write($$"""
                    SHARPCPP_EXPORT ret_t<void*> {{function.EntryPointName}}_function_pointer()
                    {
                    using FT = {{signature}};
                    return reinterpret_cast<void*>(static_cast<std::add_pointer_t<FT>>(&{{function.CppQualifiedName}}));
                    }
                    """);
            }

            if (HasNativeDefaultDispatchParams(function) || HasUndefaultableParams(function))
            {
                writer.WriteLine();
                writer.Write(RenderNativeUnexpressibleDefaultFreeFunction(compilation, function));
            }
        }

        foreach (var method in records.Where(static record => !record.IsGeneric && !record.HasMetadata(MetadataKeys.IgnoreReason)).SelectMany(static record => record.Members.OfType<FunctionBinding>()).Where(static function => !function.IsConstructor && !function.IsStatic && !function.IsGeneric && !function.HasMetadata(MetadataKeys.IgnoreReason)).OrderBy(static method => method.ManagedName, StringComparer.Ordinal))
        {
            var parameterList = string.Join(", ", new[] { $"arg_t<{method.OwningRecordCppTypeName}*> self" }.Concat(method.Parameters.Select(RenderNativeParameter)));
            var abiArguments = string.Join(", ", method.Parameters.Select(static p => p.NativeName));
            var memberSignature = BuildNativeMethodSignature(method);

            writer.WriteLine();
            if (method.TryGetMetadata(MetadataKeys.MarshallerExposedMethod, out string? marshallerCppType))
            {
                writer.Write($$"""
                    SHARPCPP_EXPORT_NAMED({{method.EntryPointName}}) {{GetNativeEntrypointReturnType(method)}} {{method.EntryPointName}}({{parameterList}})
                    {
                    {{RenderNativeMarshallerExposedInvocation(method, marshallerCppType!, abiArguments)}}
                    }
                    """);
            }
            else
            {
            writer.Write($$"""
                SHARPCPP_EXPORT_NAMED({{method.EntryPointName}}) {{GetNativeEntrypointReturnType(method)}} {{method.EntryPointName}}({{parameterList}})
                {
                using FT = {{memberSignature}};
                """);

            var owningRecord = method.Parent as RecordBinding;
            writer.Write(method.AccessModifier == BindingAccessModifier.Protected && owningRecord is not null && owningRecord.RequiresDirector(compilation)
                ? RenderNativeProtectedMethodInvocation(compilation, method, abiArguments, memberSignature)
                : RenderNativeMethodInvocation(method, abiArguments, memberSignature));

            writer.Write("""
                }
                """);
            }

            if (HasNativeDefaultDispatchParams(method) || HasUndefaultableParams(method))
            {
                writer.WriteLine();
                writer.Write(RenderNativeUnexpressibleDefaultInstanceMethod(compilation, method));
            }
        }

        foreach (var record in records.Where(static record => !record.IsGeneric && !record.HasMetadata(MetadataKeys.IgnoreReason)).OrderBy(static record => record.ManagedName, StringComparer.Ordinal))
        {
            foreach (var method in record.Members.OfType<FunctionBinding>().Where(static function => !function.IsConstructor && !function.IsStatic && function.IsGeneric && function.OperatorToken is null && !function.IsConversionOperator && !function.HasMetadata(MetadataKeys.IgnoreReason)).OrderBy(static method => method.ManagedName, StringComparer.Ordinal))
            {
                foreach (var methodInstantiation in method.SupportedGenericInstantiations)
                {
                    writer.WriteLine();
                    writer.Write(RenderNativeGenericMethodInstanceMethod(record, method, EmptyRecordGenericInstantiation(), methodInstantiation));
                }
            }
        }

        foreach (var method in records.Where(static record => !record.IsGeneric && !record.HasMetadata(MetadataKeys.IgnoreReason)).SelectMany(static record => record.Members.OfType<FunctionBinding>()).Where(static function => function.IsStatic && !function.HasMetadata(MetadataKeys.IgnoreReason) && function.AccessModifier != BindingAccessModifier.Protected).OrderBy(static method => method.ManagedName, StringComparer.Ordinal))
        {
            var parameterList = string.Join(", ", method.Parameters.Select(RenderNativeParameter));
            var abiArguments = string.Join(", ", method.Parameters.Select(static p => p.NativeName));
            var signature = BuildNativeFunctionSignature(method);
            var marshallerTemplateArguments = BuildMarshallerInvokerTemplateArguments("FT", method.Parameters.Select(static p => p.CppTypeName));

            writer.WriteLine();
            writer.Write($$"""
                SHARPCPP_EXPORT_NAMED({{method.EntryPointName}}) {{GetNativeEntrypointReturnType(method)}} {{method.EntryPointName}}({{parameterList}})
                {
                using FT = {{signature}};
                """);

            if (method.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void)
            {
                writer.Write($$"""
                    MarshallerInvoker::invoke_as<{{marshallerTemplateArguments}}>(&{{method.OwningRecordCppTypeName}}::{{method.NativeName}}{{(abiArguments.Length > 0 ? $", {abiArguments}" : string.Empty)}});
                    """);
            }
            else
            {
                writer.Write($$"""
                    {{(RequiresOwnedRecordValueReturn(method)
                        ? $"return MarshallerInvoker::invoke_owned_as<{marshallerTemplateArguments}>(&{method.OwningRecordCppTypeName}::{method.NativeName}{(abiArguments.Length > 0 ? $", {abiArguments}" : string.Empty)});"
                        : $"return MarshallerInvoker::invoke_as<{marshallerTemplateArguments}>(&{method.OwningRecordCppTypeName}::{method.NativeName}{(abiArguments.Length > 0 ? $", {abiArguments}" : string.Empty)});")}}
                    """);
            }

            writer.Write("""
                }
                """);

            if (HasNativeDefaultDispatchParams(method) || HasUndefaultableParams(method))
            {
                writer.WriteLine();
                writer.Write(RenderNativeUnexpressibleDefaultStaticMethod(compilation, method));
            }
        }

        foreach (var constructor in records.Where(static record => !record.IsGeneric && !record.HasMetadata(MetadataKeys.IgnoreReason)).SelectMany(static record => record.Members.OfType<FunctionBinding>()).Where(static function => function.IsConstructor && !function.HasMetadata(MetadataKeys.IgnoreReason)).OrderBy(static function => function.ManagedName, StringComparer.Ordinal))
        {
            var parameterList = string.Join(", ", constructor.Parameters.Select(RenderNativeParameter));
            var abiArguments = string.Join(", ", constructor.Parameters.Select(static p => p.NativeName));
            var constructorArgTypes = string.Join(", ", constructor.Parameters.Select(static p => p.CppTypeName));
            var owningRecord = constructor.Parent as RecordBinding;
            var constructedTypeName = owningRecord is not null && owningRecord.RequiresDirector(compilation)
                ? GetNativeDirectorClassName(owningRecord)
                : constructor.OwningRecordCppTypeName;

            writer.WriteLine();
            writer.Write($$"""
                SHARPCPP_EXPORT_NAMED({{constructor.EntryPointName}}) ret_t<{{constructor.CppReturnTypeName}}> {{constructor.EntryPointName}}({{parameterList}})
                {
                return MarshallerInvoker::construct<{{constructedTypeName}}{{(constructorArgTypes.Length > 0 ? $", {constructorArgTypes}" : string.Empty)}}>({{abiArguments}});
                }
                """);

            if (HasNativeDefaultDispatchParams(constructor) || HasUndefaultableParams(constructor))
            {
                writer.WriteLine();
                writer.Write(RenderNativeUnexpressibleDefaultConstructor(compilation, constructor, owningRecord is not null && owningRecord.RequiresDirector(compilation)));
            }
        }

        foreach (var (accessorRecord, field) in records
                     .Where(static record => !record.IsGeneric && !record.HasMetadata(MetadataKeys.IgnoreReason))
                     .SelectMany(record => GetNativeFieldEntries(compilation, record))
                     .Where(static entry => !entry.Field.HasMetadata(MetadataKeys.IgnoreReason))
                     .OrderBy(static entry => entry.Field.ManagedName, StringComparer.Ordinal))
        {
            if (TryGetPointerCountNativeArrayShape(compilation, field, out var pointerCountShape))
            {
                var itemCppTypeName = GetNativePointerCountFieldItemCppTypeName(field);
                var itemReturnType = pointerCountShape.IsStringArray ? "const char*" : $"ret_t<{itemCppTypeName}>";
                writer.WriteLine();
                writer.Write($$"""
                    SHARPCPP_EXPORT {{itemReturnType}} {{GetNativeFieldItemGetterEntrypointName(compilation, accessorRecord.CppQualifiedName, field)}}(arg_t<{{accessorRecord.CppQualifiedName}}*> self, std::size_t index)
                    {
                    {{RenderNativePointerCountFieldItemGetInvocation(field, pointerCountShape, "self")}}
                    }
                    """);
            }

            if (NeedsManagedFixedArrayProxy(field))
            {
                writer.WriteLine();
                writer.Write($$"""
                    SHARPCPP_EXPORT {{GetNativeFixedArrayItemGetterReturnType(compilation, field)}} {{GetNativeFieldItemGetterEntrypointName(compilation, accessorRecord.CppQualifiedName, field)}}(arg_t<{{accessorRecord.CppQualifiedName}}*> self, std::size_t index)
                    {
                    {{(field.AccessModifier == BindingAccessModifier.Protected
                        ? RenderNativeProtectedFieldItemGetInvocation(accessorRecord, field)
                        : RenderNativeFixedArrayItemGetInvocation(compilation, field, "self"))}}
                    }
                    """);

                if (!field.IsReadOnly)
                {
                    writer.WriteLine();
                    writer.Write($$"""
                        SHARPCPP_EXPORT ret_t<void> {{GetNativeFieldItemSetterEntrypointName(compilation, accessorRecord.CppQualifiedName, field)}}(arg_t<{{accessorRecord.CppQualifiedName}}*> self, std::size_t index, {{GetNativeFixedArrayItemSetterParameterType(compilation, field)}} value)
                        {
                        {{(field.AccessModifier == BindingAccessModifier.Protected
                            ? RenderNativeProtectedFieldItemSetInvocation(accessorRecord, field)
                            : RenderNativeFixedArrayItemSetInvocation(compilation, field, "self"))}}
                        }
                        """);
                }
            }
            else
            {
                var plan = CreateNativeFieldInteropPlan(compilation, field);
                var nativeFieldCppType = plan.StorageCppType;
                var isNativeFixedArrayField = IsNativeFixedArrayType(compilation, field.Type);
                var getterReturnType = isNativeFixedArrayField ? "NativeArrayAbi" : $"ret_t<{nativeFieldCppType}>";
                var getterParameters = field.IsStatic
                    ? string.Empty
                    : $"arg_t<{accessorRecord.CppQualifiedName}*> self";
                writer.WriteLine();
                writer.Write($$"""
                    SHARPCPP_EXPORT {{getterReturnType}} {{GetNativeFieldGetterEntrypointName(compilation, accessorRecord.CppQualifiedName, field)}}({{getterParameters}})
                    {
                    {{(field.AccessModifier == BindingAccessModifier.Protected
                        ? RenderNativeProtectedFieldGetInvocation(accessorRecord, field)
                        : RenderNativeFieldGetInvocation(compilation, field, plan, "self"))}}
                    }
                    """);

                if (!field.IsReadOnly)
                {
                    var setterParameterType = isNativeFixedArrayField ? "NativeArrayAbi" : plan.SetterParameterCppType;
                    var setterParameters = field.IsStatic
                        ? $"{setterParameterType} value"
                        : $"arg_t<{accessorRecord.CppQualifiedName}*> self, {setterParameterType} value";
                    writer.WriteLine();
                    writer.Write($$"""
                        SHARPCPP_EXPORT ret_t<void> {{GetNativeFieldSetterEntrypointName(compilation, accessorRecord.CppQualifiedName, field)}}({{setterParameters}})
                        {
                        {{(field.AccessModifier == BindingAccessModifier.Protected
                            ? RenderNativeProtectedFieldSetInvocation(accessorRecord, field)
                            : RenderNativeFieldSetInvocation(compilation, field, plan, "self"))}}
                        }
                        """);
                }
            }
        }

        foreach (var record in records.Where(static record => !record.IsGeneric && !record.HasMetadata(MetadataKeys.IgnoreReason)).OrderBy(static record => record.ManagedName, StringComparer.Ordinal))
        {
            writer.WriteLine();
            if (IsSharedManagedWrapper(record))
            {
                writer.Write(RenderSharedProviderNativeCreate(compilation, record, instantiation: null));
            }

            if (CanEmitDeleteThunk(record))
            {
                writer.Write(RenderNativeDelete(compilation, record, record.RequiresDirector(compilation)));
            }

            if (RequiresArrayElementAccess(compilation, record))
            {
                writer.WriteLine();
                writer.Write(RenderNativeArrayElement(compilation, record));
            }


            if (TryGetNativeInterfaceQuery(record, out var nativeInterfaceQuery))
            {
                writer.WriteLine();
                writer.Write(RenderNativeInterfaceQuery(compilation, record, nativeInterfaceQuery));
            }

            if (record.TryGetMetadata(MetadataKeys.NativeInterfaceReleaseMethod, out string? nativeInterfaceReleaseMethod))
            {
                writer.WriteLine();
                writer.Write(RenderNativeInterfaceRelease(compilation, record, nativeInterfaceReleaseMethod!));
            }

            if (CanEmitClone(record, record.IsEffectivelyAbstract || record.HasUnimplementedAbstractBaseMethods(compilation), CanEmitDeleteThunk(record)))
            {
                writer.WriteLine();
                writer.Write(RenderNativeClone(compilation, record));
            }

            if (record.RequiresDirector(compilation))
            {
                writer.WriteLine();
                writer.Write(RenderNativeConnectDirector(compilation, record, record.GetEffectiveVirtualMethodSlots(compilation)));
            }

            if (!record.IsPolymorphic || IsNativeInterfaceWrapper(record))
            {
                continue;
            }

            writer.WriteLine();
            writer.Write($$"""
                SHARPCPP_EXPORT ret_t<std::size_t> {{compilation.EntryPointPrefix}}_{{SanitizeNativeEntrypointName(record.CppQualifiedName)}}_type_id()
                {
                return typeid({{record.CppQualifiedName}}).hash_code();
                }
                """);

            writer.WriteLine();
            writer.Write($$"""
                SHARPCPP_EXPORT ret_t<void> {{compilation.EntryPointPrefix}}_{{SanitizeNativeEntrypointName(record.CppQualifiedName)}}_metadata(arg_t<{{record.CppQualifiedName}}*> self, void** handle, std::size_t* dynamicTypeId)
                {
                if (handle == nullptr || dynamicTypeId == nullptr)
                {
                return;
                }

                if (self == nullptr)
                {
                *handle = nullptr;
                *dynamicTypeId = 0;
                return;
                }

                *handle = dynamic_cast<void*>(self);
                *dynamicTypeId = typeid(*self).hash_code();
                }
                """);
        }

        return NativeExportDeclarationRegex.Replace(
            writer.ToString(),
            "$1#if defined(__EMSCRIPTEN__)\n" +
            "$1$2 $3$4 __asm__(\"$3\");\n" +
            "$1#endif\n" +
            "$1SHARPCPP_EXPORT_NAMED($3) $2 $3$4");
    }

    private static string RenderNativePointerCountFieldItemGetInvocation(
        FieldBinding field,
        PointerCountNativeArrayShape shape,
        string selfExpression)
    {
        var pointerExpression = GetNativeFixedArrayFieldAccessExpression(field, selfExpression);
        var countExpression = GetNativeFixedArrayFieldAccessExpression(shape.CountField, selfExpression);
        if (shape.IsStringArray)
        {
            return $$"""
                if ({{pointerExpression}} == nullptr || index >= static_cast<std::size_t>({{countExpression}}))
                {
                    throw std::out_of_range("SharpCpp pointer-count field access is out of range.");
                }

                return {{pointerExpression}}[index];
                """;
        }

        var itemCppTypeName = GetNativePointerCountFieldItemCppTypeName(field);
        var valueExpression = field.PointerDepth == 1
            ? $"&({pointerExpression}[index])"
            : $"{pointerExpression}[index]";
        return $$"""
            if ({{pointerExpression}} == nullptr || index >= static_cast<std::size_t>({{countExpression}}))
            {
                throw std::out_of_range("SharpCpp pointer-count field access is out of range.");
            }

            ReturnMarshaller<{{itemCppTypeName}}> marshaller;
            return marshaller.ConvertToAbi({{valueExpression}});
            """;
    }

    private static string GetNativePointerCountFieldItemCppTypeName(FieldBinding field)
    {
        if (field.PointerDepth == 1)
        {
            return field.CppTypeName;
        }

        return field.PointerElementPointeeIsConst
            ? $"const {field.Type.CppTypeName}"
            : field.Type.CppTypeName;
    }

    private static string GetManagedWrapperTypeName(BindingType type, BindingCompilation compilation)
    {
        return FormatGlobalTypeName(type, compilation);
    }

    private static bool TryGetPublicAliasName(BindingType type, BindingCompilation compilation, out string aliasName)
    {
        var unaliasedType = type.WithManagedTypeFormatOverride(null);
        var targetKey = BindingAliasTargetKey.From(unaliasedType);
        if (!string.IsNullOrWhiteSpace(type.ManagedTypeFormatOverride))
        {
            var explicitAlias = compilation.TypeAliases.FirstOrDefault(alias =>
                !alias.IsImported &&
                string.Equals(alias.ManagedName, type.ManagedTypeFormatOverride, StringComparison.Ordinal) &&
                BindingAliasTargetKey.From(alias.TargetType) == targetKey);
            if (explicitAlias is not null)
            {
                aliasName = explicitAlias.ManagedName;
                return true;
            }
        }

        // Only a type spelled through the alias takes its name; another type with the same
        // target keeps its own spelling.
        var spelledAlias = string.IsNullOrWhiteSpace(type.AliasCppQualifiedName)
            ? null
            : compilation.TypeAliases.FirstOrDefault(alias =>
                !alias.IsImported &&
                string.Equals(alias.CppQualifiedName, type.AliasCppQualifiedName, StringComparison.Ordinal) &&
                BindingAliasTargetKey.From(alias.TargetType) == targetKey);
        if (spelledAlias is not null)
        {
            aliasName = spelledAlias.ManagedName;
            return true;
        }

        aliasName = string.Empty;
        return false;
    }

    private static string GetManagedPublicReturnTypeName(BindingType type, BindingCompilation compilation)
    {
        if (TryGetPublicAliasName(type, compilation, out var aliasName))
        {
            return aliasName;
        }

        return type.Kind switch
        {
            BindingTypeKind.Handle => FormatQualifiedRecordTypeName(compilation, type, publicSurface: true),
            BindingTypeKind.Enum => GetQualifiedEnumTypeName(compilation, type),
            BindingTypeKind.Primitive when type.PrimitiveKind == BindingPrimitiveKind.Unsupported =>
                FormatConfiguredManagedTypeName(type, compilation, publicSurface: true),
            _ => GetManagedWrapperTypeName(type, compilation)
        };
    }

    /// <summary>
    /// Writes the P/Invoke result variable, post-call statements, and conversion
    /// for types where import type differs from public type (e.g., IntPtr → string).
    /// This path is used when a marshaller_type is set but NO managed_marshaller.
    /// New types should use managed_marshaller (C# custom marshaller) instead.
    /// </summary>
    private static void WriteManagedReturnWithMarshallerConversion(
        BindingCompilation compilation,
        CodeWriter writer,
        FunctionBinding function,
        string importMethodName,
        string invokeArguments,
        ManagedInvocationPlan invocationPlan)
    {
        var importTypeName = GetManagedImportTypeName(function.ReturnType, compilation);
        var publicTypeName = function.ReturnType.ManagedTypeName;
        writer.Write($$"""
            var __result_raw = __PInvoke.{{importMethodName}}({{invokeArguments}});
            """);

        foreach (var statement in invocationPlan.PostCallStatements)
        {
            writer.Write(statement);
        }

        var conversionExpression = importTypeName == BindingType.IntPtr.ManagedTypeName &&
                                   string.Equals(function.ReturnType.Marshaller.PublicTypeName, "string", StringComparison.Ordinal)
            ? "global::System.Runtime.InteropServices.Marshal.PtrToStringUTF8(__result_raw)!"
            : "__result_raw";
        writer.Write($$"""
            return {{conversionExpression}};
            """);
    }

    private static string GetManagedImportTypeName(BindingType type, BindingCompilation compilation)
    {
        if (type.MarshallerTypeName is not null)
        {
            return type.MarshallerTypeName;
        }

        return type.Kind switch
        {
            BindingTypeKind.Primitive when type.PrimitiveKind != BindingPrimitiveKind.Unsupported => GetConcretePrimitiveManagedTypeName(type),
            BindingTypeKind.Primitive => FormatGlobalTypeName(type, compilation),
            BindingTypeKind.Handle => GetManagedHandleImportTypeName(type, compilation),
            BindingTypeKind.Enum => GetManagedEnumAbiTypeName(compilation, type),
            BindingTypeKind.GenericParameter => type.UnaliasedManagedTypeName,
            _ => throw new NotSupportedException($"Managed import emission does not support '{type.Kind}'.")
        };
    }

    private static string GetManagedHandleImportTypeName(BindingType type, BindingCompilation compilation)
    {
        // Mirrors CanUseManagedHandleReturnMarshaller/CanUseManagedHandleParameterImport: a
        // record passed by value with no generated wrapper class (suppress_emission) has no
        // generated marshaller class and never crosses the ABI as a native handle — it
        // round-trips as its own blittable public type instead (see GetManagedImportReturnAttribute,
        // which correspondingly omits a MarshalUsing attribute for this case).
        return IsValueProjectedHandleType(compilation, type)
            ? FormatGlobalTypeName(type, compilation)
            : BindingType.NativeInt.ManagedTypeName;
    }

    // A Handle-kind BindingType with CppIsRecordValue means the record crosses this particular
    // site by value (e.g. a fixed-array element or field), not as a pointer/reference — that by
    // itself is normal and most such records still have a generated __{Name}Marshaller<TOwnership>
    // wrapper class to round-trip through. suppress_emission is *also* overloaded for two other,
    // still handle-shaped cases: a record whose wrapper class is emitted in a different module
    // (managed_wrapper_assembly) and re-imported here, and a plain alias of another bound record —
    // both keep CppIsRecordValue false, since they are still referenced as pointers/handles. Only
    // the combination of "by value" and "suppress_emission" identifies a pure value projection
    // (e.g. a suppress_emission Marshaller<T> with a namespaced public_type and no wrapper
    // infrastructure at all), whose cpp_type/abi_type round-trip directly as a blittable managed
    // struct instead of through the handle-marshaller machinery.
    private static bool IsValueProjectedHandleType(BindingCompilation compilation, BindingType type)
    {
        if (!type.Marshaller.CppIsRecordValue)
        {
            return false;
        }

        var record = ResolveRecord(compilation, type);
        return record is not null && record.GetMetadata<bool>(MetadataKeys.SuppressEmission);
    }

    private static string GetManagedEnumAbiTypeName(BindingCompilation compilation, BindingType type)
    {
        var @enum = compilation.FindEnumBySymbolId(type.EnumId)
            ?? throw new InvalidOperationException($"Enum binding '{type.EnumId ?? type.CppTypeName}' was not found.");
        return @enum.UnderlyingPrimitiveKind switch
        {
            BindingPrimitiveKind.Int8 or BindingPrimitiveKind.Int16 or BindingPrimitiveKind.Int32 => "int",
            BindingPrimitiveKind.UInt8 or BindingPrimitiveKind.UInt16 or BindingPrimitiveKind.UInt32 => "uint",
            BindingPrimitiveKind.Int64 => "long",
            BindingPrimitiveKind.UInt64 => "ulong",
            _ => throw new NotSupportedException($"Enum '{@enum.CppQualifiedName}' has unsupported underlying type '{@enum.UnderlyingPrimitiveKind}'.")
        };
    }

    private static string GetManagedImportReturnTypeName(BindingCompilation compilation, BindingType type)
    {
        if (type.IsFixedArray)
        {
            return GetManagedPublicReturnTypeName(type, compilation);
        }

        return CanUseManagedHandleReturnMarshaller(compilation, type)
            ? GetManagedWrapperTypeName(type, compilation)
            : GetManagedImportTypeName(type, compilation);
    }

    private static string GetManagedImportReturnAttribute(BindingCompilation compilation, BindingType type, bool ownsHandle)
    {
        if (type.IsFixedArray)
        {
            return $"[return: MarshalUsing(typeof({GetManagedFixedArrayReturnMarshallerTypeName(compilation, type, ownsHandle)}))]";
        }

        if (CanUseManagedHandleReturnMarshaller(compilation, type))
        {
            return $"[return: MarshalUsing(typeof({GetManagedHandleReturnMarshallerTypeName(compilation, type, ownsHandle)}))]";
        }

        return GetManagedReturnAttribute(type);
    }

    private static bool CanUseManagedHandleReturnMarshaller(BindingCompilation compilation, BindingType type)
    {
        if (type.IsFixedArray ||
            HasNativeNonTypeTemplateArguments(type) ||
            type.Kind != BindingTypeKind.Handle)
        {
            return false;
        }

        // A pure value-projection type (see IsValueProjectedHandleType) never gets the generated
        // __{Name}Marshaller<TOwnership> support class that this path references — it round-trips
        // through LibraryImport's own blittable-struct marshalling instead, same as any other
        // configured value type.
        return ResolveRecord(compilation, type) is not null && !IsValueProjectedHandleType(compilation, type);
    }

    private static string GetManagedHandleReturnMarshallerTypeName(BindingCompilation compilation, BindingType type, bool ownsHandle)
    {
        var record = ResolveRecord(compilation, type)
            ?? throw new InvalidOperationException($"Unable to resolve handle return marshaller record for '{type.ManagedTypeName}'.");
        return CanUseUnifiedHandleMarshaller(record)
            ? GetQualifiedUnifiedHandleMarshallerTypeName(
                compilation,
                record,
                ownsHandle ? OwnedNativeHandleFullName : BorrowedNativeHandleFullName)
            : GetQualifiedHandleReturnMarshallerTypeName(compilation, record, ownsHandle);
    }

    private static string BuildManagedHandleReturnResultExpression(BindingCompilation compilation, BindingType type, string resultExpression, bool ownsHandle)
    {
        if (type.IsFixedArray)
        {
            return resultExpression;
        }

        var usesManagedMarshaller = CanUseManagedHandleReturnMarshaller(compilation, type);
        if (type.ManagedTypeFormatOverride?.EndsWith("?", StringComparison.Ordinal) == true)
        {
            return usesManagedMarshaller
                ? $"{resultExpression} is {{ IsNull: false }} __nullableResult ? __nullableResult : null"
                : $"{resultExpression} is var __nullableHandle && __nullableHandle != 0 ? {BuildManagedHandleWrap(compilation, type, "__nullableHandle", ownsHandle)} : null";
        }

        return usesManagedMarshaller
            ? resultExpression
            : BuildManagedHandleWrap(compilation, type, resultExpression, ownsHandle);
    }

    private static string GetHandleMarshallerDeclarationName(RecordBinding record)
    {
        return $"__{record.ManagedName}Marshaller<TOwnership>";
    }

    private static bool CanUseUnifiedHandleMarshaller(RecordBinding record)
    {
        return GetManagedVisibleGenericParameters(record).Count == 0;
    }

    private static string GetHandleMarshallerGenericConstraints()
    {
        var ownershipConstraint = $"where TOwnership : {NativeHandleOwnershipConstraintFullName}";
        return $"\n{ownershipConstraint}";
    }

    private static string GetHandleReturnMarshallerDeclarationName(RecordBinding record, bool ownsHandle)
    {
        var visibleParameters = GetManagedVisibleGenericParameters(record);
        var genericSuffix = visibleParameters.Count == 0
            ? string.Empty
            : $"<{string.Join(", ", visibleParameters.Select(static parameter => parameter.Name))}>";
        return $"__{record.ManagedName}{(ownsHandle ? "Owned" : string.Empty)}ReturnMarshaller{genericSuffix}";
    }

    private static string GetHandleInMarshallerDeclarationName(RecordBinding record)
    {
        var visibleParameters = GetManagedVisibleGenericParameters(record);
        var genericSuffix = visibleParameters.Count == 0
            ? string.Empty
            : $"<{string.Join(", ", visibleParameters.Select(static parameter => parameter.Name))}>";
        return $"__{record.ManagedName}InMarshaller{genericSuffix}";
    }

    private static string GetQualifiedUnifiedHandleMarshallerTypeName(BindingCompilation compilation, RecordBinding record, string ownershipTypeName)
    {
        var ns = GetMarshallerHelperNamespace(compilation, record);
        return $"global::{ns}.__{record.ManagedName}Marshaller<{ownershipTypeName}>";
    }

    private static string GetQualifiedHandleReturnMarshallerTypeName(BindingCompilation compilation, RecordBinding record, bool ownsHandle)
    {
        var ns = GetMarshallerHelperNamespace(compilation, record);
        var arity = GetManagedVisibleGenericParameters(record).Count;
        var openGenericSuffix = arity == 0 ? string.Empty : $"<{new string(',', arity - 1)}>";
        return $"global::{ns}.__{record.ManagedName}{(ownsHandle ? "Owned" : string.Empty)}ReturnMarshaller{openGenericSuffix}";
    }

    private static string GetQualifiedHandleInMarshallerTypeName(BindingCompilation compilation, RecordBinding record)
    {
        var ns = GetMarshallerHelperNamespace(compilation, record);
        var arity = GetManagedVisibleGenericParameters(record).Count;
        var openGenericSuffix = arity == 0 ? string.Empty : $"<{new string(',', arity - 1)}>";
        return $"global::{ns}.__{record.ManagedName}InMarshaller{openGenericSuffix}";
    }

    private static string GetManagedFixedArrayReturnMarshallerTypeName(BindingCompilation compilation, BindingType type, bool ownsHandle)
    {
        var record = ResolveRecord(compilation, type)
            ?? throw new InvalidOperationException($"Unable to resolve fixed-array return marshaller record for '{type.ManagedTypeName}'.");
        return GetQualifiedHandleReturnMarshallerTypeName(compilation, record, ownsHandle);
    }

    private static string GetManagedFixedArrayInMarshallerAttribute(BindingCompilation compilation, BindingType type)
    {
        var record = ResolveRecord(compilation, type)
            ?? throw new InvalidOperationException($"Unable to resolve fixed-array parameter marshaller record for '{type.ManagedTypeName}'.");
        return $"[MarshalUsing(typeof({GetQualifiedHandleInMarshallerTypeName(compilation, record)}))] ";
    }

    private static string GetQualifiedManagedRecordDeclarationTypeName(BindingCompilation compilation, RecordBinding record, string recordDeclarationName)
    {
        var ns = GetManagedNamespace(compilation, record);
        return recordDeclarationName.StartsWith("global::", StringComparison.Ordinal)
            ? recordDeclarationName
            : $"global::{ns}.{recordDeclarationName}";
    }

    private static string GetQualifiedManagedOpenRecordTypeName(BindingCompilation compilation, RecordBinding record)
    {
        var ns = GetManagedNamespace(compilation, record);
        var arity = GetManagedVisibleGenericParameters(record).Count;
        var openGenericSuffix = arity == 0 ? string.Empty : $"<{new string(',', arity - 1)}>";
        var managedName = GetManagedDeclarationName(record);
        return $"global::{ns}.{managedName}{openGenericSuffix}";
    }

    private static string GetManagedHandleInMarshallerAttribute(BindingCompilation compilation, BindingType type)
    {
        var record = ResolveRecord(compilation, type);
        if (record is null)
        {
            return string.Empty;
        }

        if (HasNativeNonTypeTemplateArguments(type))
        {
            return $"[MarshalUsing(typeof({GetQualifiedHandleInMarshallerTypeName(compilation, record)}))] ";
        }

        return CanUseUnifiedHandleMarshaller(record)
            ? $"[MarshalUsing(typeof({GetQualifiedUnifiedHandleMarshallerTypeName(compilation, record, BorrowedNativeHandleFullName)}))] "
            : $"[MarshalUsing(typeof({GetQualifiedHandleInMarshallerTypeName(compilation, record)}))] ";
    }

    private static bool CanUseManagedHandleParameterImport(BindingCompilation compilation, ParameterBinding parameter, BindingType type)
    {
        if (type.Kind != BindingTypeKind.Handle ||
            IsRawPointerHandle(type) ||
            parameter.Direction is BindingParameterDirection.Ref or BindingParameterDirection.Out)
        {
            return false;
        }

        // Mirrors CanUseManagedHandleReturnMarshaller: a pure value-projection type (see
        // IsValueProjectedHandleType) has no generated handle-marshaller support class to import
        // through. Types with no resolved record at all keep the prior behavior (true) — only
        // that specific shape excludes this path.
        return ResolveRecord(compilation, type) is null || !IsValueProjectedHandleType(compilation, type);
    }

    // A primitive pointer field or return: a handle ABI without a wrapper record.
    private static bool IsRawPointerHandle(BindingType type)
    {
        return type.Kind == BindingTypeKind.Handle &&
            type.RecordId is null &&
            string.Equals(type.ManagedTypeName, BindingType.IntPtr.ManagedTypeName, StringComparison.Ordinal);
    }

    private static string RenderManagedImportParameter(ParameterBinding parameter, BindingCompilation compilation)
    {
        return RenderManagedImportParameter(parameter, parameter.Type, compilation);
    }

    private static string RenderManagedImportParameter(ParameterBinding parameter, BindingType type, BindingCompilation compilation)
    {
        return RenderManagedImportParameter(parameter, type, compilation, callbackMarshallerParameter: null);
    }

    private static string RenderManagedImportParameter(
        ParameterBinding parameter,
        BindingType type,
        BindingCompilation compilation,
        ParameterBinding? callbackMarshallerParameter)
    {
        if (IsInputHandlePointerArrayParameter(parameter))
        {
            return $"nint* {parameter.ManagedName}";
        }

        if (IsInputRawPointerArrayParameter(parameter))
        {
            return $"{GetManagedImportTypeName(type, compilation)}* {parameter.ManagedName}";
        }

        if (type.IsFixedArray)
        {
            var attribute = GetManagedFixedArrayInMarshallerAttribute(compilation, type);
            return $"{attribute}{GetManagedPublicReturnTypeName(type, compilation)} {parameter.ManagedName}";
        }

        if (CanUseManagedHandleParameterImport(compilation, parameter, type))
        {
            var modifier = parameter.Direction switch
            {
                BindingParameterDirection.In when parameter.AbiKind != BindingParameterAbiKind.Value => "in ",
                _ => string.Empty
            };
            var attribute = GetManagedParameterAttribute(parameter, type, compilation);
            var marshallerAttribute = GetManagedHandleInMarshallerAttribute(compilation, type);
            var wrapperTypeName = GetManagedWrapperTypeName(type, compilation);
            var typeName = wrapperTypeName.EndsWith("?", StringComparison.Ordinal)
                ? wrapperTypeName
                : $"{wrapperTypeName}?";
            return $"{marshallerAttribute}{attribute}{modifier}{typeName} {parameter.ManagedName}";
        }

        var modifierFallback = parameter.Direction switch
        {
            BindingParameterDirection.In when parameter.AbiKind != BindingParameterAbiKind.Value => "in ",
            BindingParameterDirection.Ref => "ref ",
            BindingParameterDirection.Out => "out ",
            _ => string.Empty
        };
        var attributeFallback = GetManagedParameterAttribute(callbackMarshallerParameter ?? parameter, type, compilation);
        var importTypeName = type.Kind == BindingTypeKind.Enum && parameter.AbiKind != BindingParameterAbiKind.Value
            ? GetManagedPublicReturnTypeName(type, compilation)
            : GetManagedImportTypeName(type, compilation);
        var typeNameFallback = type.IsCallback && parameter.Direction == BindingParameterDirection.In && parameter.AbiKind == BindingParameterAbiKind.Value
            ? $"{GetManagedCallbackParameterTypeName(compilation!, parameter)}?"
            : IsNullableManagedReferenceDefaultParameter(parameter) && IsManagedReferenceTypeName(type.ManagedTypeName)
                ? $"{importTypeName}?"
                : importTypeName;
        return $"{attributeFallback}{modifierFallback}{typeNameFallback} {parameter.ManagedName}";
    }

    private static string RenderManagedPublicParameter(ParameterBinding parameter, BindingCompilation compilation)
    {
        return RenderManagedPublicParameterCore(parameter, forceRequired: false, suppressDefault: false, compilation, renderUnexpressibleDefault: false);
    }

    private static string RenderManagedPublicParameterWithUnexpressibleDefault(ParameterBinding parameter, BindingCompilation compilation)
    {
        return RenderManagedPublicParameterCore(parameter, forceRequired: false, suppressDefault: false, compilation, renderUnexpressibleDefault: true);
    }

    private static string RenderManagedPublicParameterList(IEnumerable<ParameterBinding> parameters, BindingCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var parameterArray = parameters as ParameterBinding[] ?? parameters.ToArray();

        var firstUnexpressibleDefault = parameterArray
            .Select((parameter, index) => (parameter, index))
            .FirstOrDefault(static entry => entry.parameter.HasMetadata(MetadataKeys.UnexpressibleDefault));
        var lastRequiredParameterIndex = Array.FindLastIndex(parameterArray, static parameter => !CanRenderManagedPublicDefault(parameter));

        var parts = new List<string>(parameterArray.Length);
        for (var i = 0; i < parameterArray.Length; i++)
        {
            var parameter = parameterArray[i];
            if (firstUnexpressibleDefault.parameter is not null)
            {
                if (i < firstUnexpressibleDefault.index)
                {
                    parts.Add(RenderManagedPublicParameterRequired(parameter, compilation));
                    continue;
                }

                if (parameter.HasMetadata(MetadataKeys.UnexpressibleDefault))
                {
                    parts.Add(i < lastRequiredParameterIndex
                        ? RenderManagedPublicParameterNoDefault(parameter, compilation)
                        : RenderManagedPublicParameterWithUnexpressibleDefault(parameter, compilation));
                    continue;
                }
            }

            if (i < lastRequiredParameterIndex && CanRenderManagedPublicDefault(parameter))
            {
                parts.Add(RenderManagedPublicParameterNoDefault(parameter, compilation));
                continue;
            }

            parts.Add(RenderManagedPublicParameter(parameter, compilation));
        }

        return string.Join(", ", parts);
    }

    private static string RenderManagedInstancePublicParameterList(
        BindingCompilation compilation,
        RecordBinding record,
        FunctionBinding function)
    {
        var baseMethod = FindPrimaryBaseVirtualMatch(compilation, record, function);
        if (baseMethod is null || baseMethod.Parameters.Count != function.Parameters.Count)
        {
            return RenderManagedPublicParameterList(function.Parameters, compilation);
        }

        var parameters = function.Parameters
            .Select((parameter, index) => CreateManagedOverrideParameter(parameter, baseMethod.Parameters[index]))
            .ToArray();
        return RenderManagedPublicParameterList(parameters, compilation);
    }

    private static ParameterBinding CreateManagedOverrideParameter(
        ParameterBinding parameter,
        ParameterBinding baseParameter)
    {
        if (!baseParameter.Type.IsGenericParameter ||
            parameter.Direction == baseParameter.Direction && parameter.AbiKind == baseParameter.AbiKind)
        {
            return parameter;
        }

        return new ParameterBinding(
            parameter.SymbolId,
            parameter.NativeName,
            parameter.ManagedName,
            parameter.Type,
            baseParameter.Direction,
            baseParameter.AbiKind,
            parameter.CppTypeName)
        {
            ManagedDefaultValueExpression = parameter.ManagedDefaultValueExpression,
            UseCallbackDelegate = parameter.UseCallbackDelegate,
            CallbackDelegateTypeName = parameter.CallbackDelegateTypeName,
            CallbackTakesOwnership = parameter.CallbackTakesOwnership
        };
    }

    private static void WriteManagedUnexpressibleDefaultConvenienceOverloads(
        CodeWriter writer,
        BindingCompilation compilation,
        FunctionBinding function,
        string signaturePrefix,
        string invocationPrefix,
        string invocationTarget,
        string? returnTypeName,
        bool isConstructor)
    {
        foreach (var keepCount in GetUnexpressibleDefaultConvenienceOverloadParameterIndices(function))
        {
            if (CollidesWithSiblingManagedOverload(compilation, function, keepCount))
            {
                continue;
            }

            var variantParams = function.Parameters.Take(keepCount).ToArray();
            var parameterList = RenderManagedUnexpressibleDefaultOverloadParameterList(function, keepCount, compilation);
            var forwardedArgs = string.Join(
                ", ",
                variantParams.Select(RenderManagedForwardingArgument)
                    .Concat(function.Parameters.Skip(keepCount).Select(p => GetManagedOmittedDefaultArgument(p, compilation))));
            var overloadSignaturePrefix = signaturePrefix;

            if (!isConstructor &&
                string.Equals(invocationTarget, nameof(ToString), StringComparison.Ordinal) &&
                variantParams.Length == 0 &&
                returnTypeName is not null &&
                !overloadSignaturePrefix.Contains(" new ", StringComparison.Ordinal))
            {
                overloadSignaturePrefix = overloadSignaturePrefix.Replace(
                    $"{returnTypeName} {invocationTarget}",
                    $"new {returnTypeName} {invocationTarget}",
                    StringComparison.Ordinal);
            }

            if (isConstructor)
            {
                writer.Write($$"""
                    {{signaturePrefix}}({{parameterList}}) {{invocationPrefix}}({{forwardedArgs}})
                    {
                    }

                    """);
                continue;
            }

            writer.Write($$"""
                {{overloadSignaturePrefix}}({{parameterList}})
                {
                """);

            if (string.Equals(returnTypeName, "void", StringComparison.Ordinal))
            {
                writer.Write($$"""
                    {{invocationTarget}}({{forwardedArgs}});
                    }

                    """);
            }
            else
            {
                writer.Write($$"""
                    return {{invocationTarget}}({{forwardedArgs}});
                    }

                    """);
            }
        }
    }

    /// <summary>
    /// A prefix overload produced by default splitting (an unexpressible-default convenience
    /// overload or the prefix before the first undefaultable parameter) is dropped when another
    /// member of the same managed overload set exposes the same managed signature: a member
    /// whose full parameter list is the prefix keeps it, and prefixes shared by several members
    /// are ambiguous natively, so only their longer overloads remain.
    /// </summary>
    private static bool CollidesWithSiblingManagedOverload(BindingCompilation compilation, FunctionBinding function, int keepCount)
    {
        foreach (var other in GetManagedOverloadSiblings(compilation, function))
        {
            if (ExposesManagedSignatureWithArity(other, keepCount) &&
                ManagedParameterPrefixSignaturesMatch(compilation, function, other, keepCount))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ShouldEmitUndefaultablePrefixOverload(BindingCompilation compilation, FunctionBinding function)
    {
        var firstUndefaultable = GetFirstUndefaultableParamIndex(function);
        return firstUndefaultable >= 0 && !CollidesWithSiblingManagedOverload(compilation, function, firstUndefaultable);
    }

    private static IEnumerable<FunctionBinding> GetManagedOverloadSiblings(BindingCompilation compilation, FunctionBinding function)
    {
        IEnumerable<FunctionBinding> candidates;
        if (function.Parent is RecordBinding record)
        {
            candidates = function.IsConstructor
                ? record.Members.OfType<FunctionBinding>().Where(static member => member.IsConstructor)
                : record.Members.OfType<FunctionBinding>().Where(member =>
                    !member.IsConstructor &&
                    string.Equals(member.ManagedName, function.ManagedName, StringComparison.Ordinal));
        }
        else
        {
            var managedNamespace = GetManagedNamespace(compilation, function);
            var managedClassName = GetManagedClassName(compilation, function);
            candidates = compilation.Members.OfType<FunctionBinding>().Where(member =>
                member.Parent is not RecordBinding &&
                !member.IsConstructor &&
                string.Equals(member.ManagedName, function.ManagedName, StringComparison.Ordinal) &&
                string.Equals(GetManagedNamespace(compilation, member), managedNamespace, StringComparison.Ordinal) &&
                string.Equals(GetManagedClassName(compilation, member), managedClassName, StringComparison.Ordinal));
        }

        return candidates.Where(member =>
            !ReferenceEquals(member, function) &&
            !member.IsGeneric &&
            !member.HasMetadata(MetadataKeys.IgnoreReason) &&
            !member.IsSilentlyExcluded());
    }

    private static bool ExposesManagedSignatureWithArity(FunctionBinding function, int parameterCount)
    {
        if (function.Parameters.Count == parameterCount)
            return true;

        if (HasUndefaultableParams(function))
            return GetFirstUndefaultableParamIndex(function) == parameterCount;

        return GetUnexpressibleDefaultConvenienceOverloadParameterIndices(function).Contains(parameterCount);
    }

    private static bool ManagedParameterPrefixSignaturesMatch(BindingCompilation compilation, FunctionBinding left, FunctionBinding right, int parameterCount)
    {
        if (left.Parameters.Count < parameterCount || right.Parameters.Count < parameterCount)
            return false;

        for (var i = 0; i < parameterCount; ++i)
        {
            var leftParameter = left.Parameters[i];
            var rightParameter = right.Parameters[i];
            if (leftParameter.Direction != rightParameter.Direction)
                return false;

            var leftTypeName = GetManagedPublicParameterTypeName(leftParameter, compilation);
            var rightTypeName = GetManagedPublicParameterTypeName(rightParameter, compilation);
            if (!string.Equals(leftTypeName, rightTypeName, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static string RenderManagedUnexpressibleDefaultOverloadParameterList(
        FunctionBinding function,
        int keepCount,
        BindingCompilation compilation)
    {
        var parameterArray = function.Parameters;
        var keepParameters = parameterArray.Take(keepCount).ToArray();
        if (keepParameters.Length == 0)
        {
            return string.Empty;
        }

        var localDefaultFlags = GetManagedPublicParameterDefaultFlags(keepParameters);
        var parts = new List<string>(keepCount);
        for (var i = 0; i < keepParameters.Length; i++)
        {
            var parameter = keepParameters[i];
            if (parameter.HasMetadata(MetadataKeys.UnexpressibleDefault))
            {
                parts.Add(RenderManagedPublicParameterNoDefault(parameter, compilation));
                continue;
            }

            parts.Add(localDefaultFlags[i]
                ? RenderManagedPublicParameter(parameter, compilation)
                : RenderManagedPublicParameterNoDefault(parameter, compilation));
        }

        return string.Join(", ", parts);
    }

    private static string GetManagedOmittedDefaultArgument(ParameterBinding parameter, BindingCompilation compilation)
    {
        if (parameter.Direction == BindingParameterDirection.Out)
        {
            return "out _";
        }

        if (parameter.HasMetadata(MetadataKeys.UnexpressibleDefault) || parameter.ManagedDefaultValueExpression == "null")
        {
            return $"default({GetManagedOmittedDefaultTypeName(parameter, compilation)})";
        }

        if (parameter.ManagedDefaultValueExpression is { Length: > 0 } defaultValueExpression)
        {
            return RenderManagedDefaultValueExpression(parameter, defaultValueExpression, compilation);
        }

        return "default";
    }

    private static string GetManagedOmittedDefaultTypeName(ParameterBinding parameter, BindingCompilation compilation)
    {
        var baseTypeName = GetManagedPublicParameterTypeName(parameter, compilation);
        if (IsManagedReferenceDefaultParameter(parameter))
        {
            return $"{baseTypeName}?";
        }

        return parameter.HasMetadata(MetadataKeys.UnexpressibleDefault)
            ? $"{baseTypeName}?"
            : baseTypeName;
    }

    private static string GetManagedPublicParameterTypeName(ParameterBinding parameter, BindingCompilation compilation)
    {
        return parameter.Type.IsCallback
            ? GetManagedCallbackParameterTypeName(compilation, parameter)
            : GetManagedPublicReturnTypeName(parameter.Type, compilation);
    }

    private static string RenderManagedPublicParameterRequired(ParameterBinding parameter, BindingCompilation compilation)
    {
        return RenderManagedPublicParameterCore(parameter, forceRequired: true, suppressDefault: true, compilation, renderUnexpressibleDefault: false);
    }

    private static string RenderManagedPublicParameterNoDefault(ParameterBinding parameter, BindingCompilation compilation)
    {
        return RenderManagedPublicParameterCore(parameter, forceRequired: false, suppressDefault: true, compilation, renderUnexpressibleDefault: false);
    }

    private static bool CanRenderManagedPublicDefault(ParameterBinding parameter)
    {
        return !parameter.HasMetadata(MetadataKeys.UnexpressibleDefault) &&
            parameter.Direction == BindingParameterDirection.In &&
            parameter.AbiKind == BindingParameterAbiKind.Value &&
            parameter.ManagedDefaultValueExpression is { Length: > 0 };
    }

    private static string RenderManagedPublicParameterCore(
        ParameterBinding parameter,
        bool forceRequired,
        bool suppressDefault,
        BindingCompilation compilation,
        bool renderUnexpressibleDefault = false)
    {
        if (IsInputHandlePointerArrayParameter(parameter))
        {
            return $"global::System.ReadOnlySpan<{GetManagedPublicReturnTypeName(parameter.Type, compilation)}?> {parameter.ManagedName}";
        }

        if (IsInputRawPointerArrayParameter(parameter))
        {
            var spanType = parameter.HasMetadata(MetadataKeys.WritablePointerArray) ? "Span" : "ReadOnlySpan";
            return $"global::System.{spanType}<{GetManagedPublicReturnTypeName(parameter.Type, compilation)}> {parameter.ManagedName}";
        }

        var modifier = parameter.Direction switch
        {
            BindingParameterDirection.In when parameter.AbiKind != BindingParameterAbiKind.Value => "in ",
            BindingParameterDirection.Ref => "ref ",
            BindingParameterDirection.Out => "out ",
            _ => string.Empty
        };

        var isNullableManagedReferenceDefault = !forceRequired &&
            IsNullableManagedReferenceDefaultParameter(parameter);

        var typeName = (!forceRequired && parameter.HasMetadata(MetadataKeys.UnexpressibleDefault)) || isNullableManagedReferenceDefault
            ? $"{GetManagedPublicParameterTypeName(parameter, compilation)}?"
            : GetManagedPublicParameterTypeName(parameter, compilation);

        var signature = $"{modifier}{typeName} {parameter.ManagedName}";

        if (!forceRequired && !suppressDefault)
        {
            if (parameter.HasMetadata(MetadataKeys.UnexpressibleDefault))
            {
                if (renderUnexpressibleDefault)
                {
                    signature += " = default";
                }
            }
            else if (isNullableManagedReferenceDefault)
            {
                signature += " = default";
            }
            else if (parameter.Direction == BindingParameterDirection.In &&
                parameter.AbiKind == BindingParameterAbiKind.Value &&
                parameter.ManagedDefaultValueExpression is { Length: > 0 } defaultValueExpression)
            {
                var fallbackDefault = defaultValueExpression == "null"
                    ? "default"
                    : RenderManagedDefaultValueExpression(parameter, defaultValueExpression, compilation);
                signature += $" = {fallbackDefault}";
            }
        }
        return signature;
    }

    private static string RenderManagedDefaultValueExpression(ParameterBinding parameter, string defaultValueExpression, BindingCompilation compilation)
    {
        if (defaultValueExpression.Length >= 3 &&
            defaultValueExpression[0] == '\'' &&
            defaultValueExpression[^1] == '\'')
        {
            return parameter.Type.PrimitiveKind switch
            {
                BindingPrimitiveKind.Int8 => $"(sbyte){defaultValueExpression}",
                BindingPrimitiveKind.UInt8 => $"(byte){defaultValueExpression}",
                _ => defaultValueExpression
            };
        }

        if (parameter.Type.Kind == BindingTypeKind.Enum)
        {
            var separator = defaultValueExpression.IndexOf('.');
            if (separator > 0)
            {
                var enumTypeName = defaultValueExpression[..separator];
                var enumMemberName = defaultValueExpression[(separator + 1)..];
                var qualifiedEnumTypeName = GetQualifiedEnumTypeName(compilation, parameter.Type);
                if (string.Equals(enumTypeName, parameter.Type.UnaliasedManagedTypeName, StringComparison.Ordinal) ||
                    string.Equals(enumTypeName, NormalizeEnumManagedTypeName(parameter.Type.UnaliasedManagedTypeName), StringComparison.Ordinal))
                {
                    return $"{qualifiedEnumTypeName}.{enumMemberName}";
                }
            }
        }

        return defaultValueExpression;
    }

    private static string RenderNativeParameter(ParameterBinding parameter)
    {
        return $"arg_t<{GetNativeParameterMarshallerCppTypeName(parameter)}> {parameter.NativeName}";
    }

    private static string GetNativeParameterMarshallerCppTypeName(ParameterBinding parameter)
    {
        return parameter.Type.Marshaller.IsRefStruct
            ? parameter.Type.CppTypeName
            : parameter.CppTypeName;
    }

    private static string BuildMarshallerInvokerTemplateArguments(string functionTypeName, IEnumerable<string> cppParameterTypes)
    {
        var tupleTypes = string.Join(", ", cppParameterTypes);
        return $"{functionTypeName}, std::tuple<{tupleTypes}>";
    }

    private static string BuildNativeFunctionSignature(FunctionBinding function)
    {
        var returnTypeName = function.CppReturnTypeName;
        var parameterTypes = string.Join(", ", function.Parameters.Select(static p => p.CppTypeName));
        return $"auto({parameterTypes}) -> {returnTypeName}";
    }

    private static string BuildNativeMethodSignature(FunctionBinding method)
    {
        var returnTypeName = method.CppReturnTypeName;
        var parameterTypes = string.Join(", ", method.Parameters.Select(static p => p.CppTypeName));
        var constQualifier = method.IsConstMethod ? " const" : string.Empty;
        return $"auto({method.OwningRecordCppTypeName}::*)({parameterTypes}){constQualifier} -> {returnTypeName}";
    }

    private static string GetNativeFieldCppType(BindingCompilation compilation, FieldBinding field)
    {
        return field.CppTypeName;
    }

    private static string GetNativeFixedArrayElementCppType(BindingCompilation compilation, BindingType type)
    {
        var elementType = type.IsFixedArray ? type.FixedArrayElementType ?? type : type;
        return elementType.Kind switch
        {
            BindingTypeKind.Enum => compilation.FindEnumBySymbolId(elementType.EnumId)?.CppQualifiedName ?? elementType.CppTypeName,
            BindingTypeKind.Handle => GetNativeFixedArrayHandleElementCppType(compilation, elementType),
            _ => elementType.CppTypeName
        };
    }

    private static string GetNativeFixedArrayHandleElementCppType(BindingCompilation compilation, BindingType type)
    {
        var record = compilation.FindRecordBySymbolId(type.RecordId);
        if (record is null)
        {
            return type.CppTypeName;
        }

        var pointerSuffix = string.Empty;
        var cppTypeName = type.CppTypeName.TrimEnd();
        while (!type.Marshaller.CppIsRecordValue &&
               !type.Marshaller.CppPointerIsRecordValueProjection &&
               cppTypeName.EndsWith('*'))
        {
            pointerSuffix += "*";
            cppTypeName = cppTypeName[..^1].TrimEnd();
        }

        var qualifiedTypeName = record.GenericParameters.Count > 0 && type.GenericArguments.Count > 0
            ? record.FormatCppTypeName(type.GenericArguments)
            : record.CppQualifiedName;
        return qualifiedTypeName + pointerSuffix;
    }


    private static bool RequiresHandleWrapping(BindingType type)
    {
        return type.IsPointerType;
    }

    private static string RenderManagedInvocationArgument(BindingCompilation compilation, ParameterBinding parameter)
    {
        if (parameter.Type.IsFixedArray)
        {
            return parameter.ManagedName;
        }

        var requiresWrapping = RequiresHandleWrapping(parameter.Type);

        if (requiresWrapping && CanUseManagedHandleParameterImport(compilation, parameter, parameter.Type))
        {
            return RenderManagedSafeHandleInvocationArgument(parameter);
        }

        var valueExpression = requiresWrapping
            ? $"global::SharpCpp.Support.NativeObject.GetHandle({parameter.ManagedName})"
            : parameter.ManagedName;

        if (!requiresWrapping)
        {
            var importExpression = BuildManagedPublicToImportExpression(compilation, parameter.Type, valueExpression);
            return parameter.Direction switch
            {
                BindingParameterDirection.In when parameter.AbiKind != BindingParameterAbiKind.Value => $"in {valueExpression}",
                BindingParameterDirection.Ref => $"ref {valueExpression}",
                BindingParameterDirection.Out => $"out {valueExpression}",
                _ => importExpression
            };
        }

        return parameter.Direction switch
        {
            BindingParameterDirection.In when parameter.AbiKind != BindingParameterAbiKind.Value => $"in {valueExpression}",
            BindingParameterDirection.Ref => $"ref {valueExpression}",
            BindingParameterDirection.Out => $"out {valueExpression}",
            _ => valueExpression
        };
    }

    private static string RenderManagedSafeHandleInvocationArgument(ParameterBinding parameter)
    {
        return parameter.Direction switch
        {
            BindingParameterDirection.In when parameter.AbiKind != BindingParameterAbiKind.Value => $"in {parameter.ManagedName}",
            BindingParameterDirection.Ref => $"ref {parameter.ManagedName}",
            BindingParameterDirection.Out => $"out {parameter.ManagedName}",
            _ => parameter.ManagedName
        };
    }

    private static ManagedInvocationPlan BuildManagedInvocationPlan(BindingCompilation compilation, IEnumerable<ParameterBinding> parameters)
    {
        var arguments = new List<string>();
        var preCallStatements = new List<string>();
        var postCallStatements = new List<string>();

        foreach (var parameter in parameters)
        {
            if (IsInputPointerArrayParameter(parameter))
            {
                var pointerName = GetPointerArrayPointerName(parameter);
                arguments.Add(parameter.Type.Kind == BindingTypeKind.Enum
                    ? $"({GetManagedImportTypeName(parameter.Type, compilation)}*){pointerName}"
                    : pointerName);
                continue;
            }

            if (parameter.Type.IsFixedArray)
            {
                var expectedLength = parameter.Type.FixedArrayLength ?? 0;
                // An omitted native-default argument is null and dispatches to the variant
                // that lets native code supply the default.
                preCallStatements.Add(IsNativeDefaultDispatchParameter(parameter)
                    ? $$"""
                        if ({{parameter.ManagedName}} is not null && {{parameter.ManagedName}}.Length != {{expectedLength}})
                        {
                            throw new global::System.ArgumentException("Expected array length {{expectedLength}}.", nameof({{parameter.ManagedName}}));
                        }
                        """
                    : $$"""
                        global::System.ArgumentNullException.ThrowIfNull({{parameter.ManagedName}});
                        if ({{parameter.ManagedName}}.Length != {{expectedLength}})
                        {
                            throw new global::System.ArgumentException("Expected array length {{expectedLength}}.", nameof({{parameter.ManagedName}}));
                        }
                        """);
                arguments.Add(RenderManagedInvocationArgument(compilation, parameter));
                postCallStatements.Add($"global::System.GC.KeepAlive({parameter.ManagedName});");
                continue;
            }

            if (parameter.GetMetadata<int>(MetadataKeys.MinimumSpanLength) is > 0 and var minimumLength)
            {
                preCallStatements.Add($$"""
                    if ({{parameter.ManagedName}}.Length < {{minimumLength}})
                    {
                        throw new global::System.ArgumentException("Expected at least {{minimumLength}} elements.", nameof({{parameter.ManagedName}}));
                    }
                    """);
            }

            var requiresWrapping = RequiresHandleWrapping(parameter.Type);

            if (requiresWrapping && parameter.Direction is BindingParameterDirection.Ref or BindingParameterDirection.Out)
            {
                var abiVariableName = $"__abi_{parameter.ManagedName.TrimStart('@')}";

                if (parameter.Direction == BindingParameterDirection.Ref)
                {
                    preCallStatements.Add($$"""
                        var {{abiVariableName}} = global::SharpCpp.Support.NativeObject.GetHandle({{parameter.ManagedName}});
                        """);
                    arguments.Add($"ref {abiVariableName}");
                }
                else
                {
                    preCallStatements.Add($$"""
                        nint {{abiVariableName}};
                        """);
                    arguments.Add($"out {abiVariableName}");
                }

                postCallStatements.Add($$"""
                    {{parameter.ManagedName}} = {{BuildManagedHandleWrap(compilation, parameter.Type, abiVariableName, ownsHandle: false)}};
                    """);
                continue;
            }

            if (requiresWrapping)
            {
                postCallStatements.Add($"global::System.GC.KeepAlive({parameter.ManagedName});");
            }

            arguments.Add(RenderManagedInvocationArgument(compilation, parameter));
        }

        return new ManagedInvocationPlan(arguments, preCallStatements, postCallStatements);
    }

    private sealed record ManagedInvocationPlan(
        IReadOnlyList<string> Arguments,
        IReadOnlyList<string> PreCallStatements,
        IReadOnlyList<string> PostCallStatements);

    private static string BuildManagedPublicToImportExpression(BindingCompilation compilation, BindingType type, string expression)
    {
        if (type.Kind == BindingTypeKind.Handle || type.IsFixedArray)
        {
            return expression;
        }

        var importTypeName = GetManagedImportTypeName(type, compilation);
        var publicTypeName = GetManagedPublicReturnTypeName(type, compilation);
        return string.Equals(importTypeName, publicTypeName, StringComparison.Ordinal)
            ? expression
            : $"({importTypeName}){expression}";
    }

    private static string BuildManagedImportToPublicExpression(BindingCompilation compilation, BindingType type, string expression)
    {
        if (type.Kind == BindingTypeKind.Handle || type.IsFixedArray)
        {
            return expression;
        }

        var importTypeName = GetManagedImportTypeName(type, compilation);
        var publicTypeName = GetManagedPublicReturnTypeName(type, compilation);
        return string.Equals(importTypeName, publicTypeName, StringComparison.Ordinal)
            ? expression
            : $"({publicTypeName})({expression})";
    }

    private sealed record AccessorProperty(string PropertyName, FunctionBinding? Getter, FunctionBinding? Setter);

    // Generic wrappers do not declare the per-type static helpers; the nearest non-generic
    // ancestor in the managed chain does.
    private static RecordBinding? FindNearestNonGenericManagedAncestor(BindingCompilation compilation, RecordBinding record)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal) { record.SymbolId };
        for (var current = record.GetPrimaryBaseRecord(compilation); IsManagedChainBase(current); current = current!.GetPrimaryBaseRecord(compilation))
        {
            if (!visited.Add(current!.SymbolId))
                break;
            if (!current.IsGeneric)
                return current;
        }

        return null;
    }

    // A generic base with non-type template arguments dispatches on its native shape, which
    // the derived record's base specialization names.
    private static string GetPrimaryBaseNativeTemplateKeyArgument(BindingCompilation compilation, RecordBinding record)
    {
        var primaryBase = record.Bases.FirstOrDefault(static candidate => !candidate.IsVirtual);
        if (primaryBase is null ||
            compilation.FindRecordBySymbolId(primaryBase.RecordId) is not { IsGeneric: true } baseRecord ||
            !HasNativeNonTypeTemplateArguments(baseRecord) ||
            primaryBase.Type.NativeTemplateArguments.Count == 0)
        {
            return string.Empty;
        }

        return $", \"{EscapeCSharpString(GetNativeTemplateArgumentKey(primaryBase.Type.NativeTemplateArguments))}\"";
    }

    // A base in the managed chain: a dropped (ignored or excluded) base ends the chain.
    private static bool IsManagedChainBase(RecordBinding? primaryBase) =>
        primaryBase is not null &&
        !primaryBase.HasMetadata(MetadataKeys.IgnoreReason) &&
        !primaryBase.IsSilentlyExcluded();

    // A base whose wrapper this compilation renders (shared wrappers live in their own assembly).
    internal static bool IsRenderedPrimaryBase(RecordBinding? primaryBase) =>
        primaryBase is not null &&
        !primaryBase.HasMetadata(MetadataKeys.IgnoreReason) &&
        !primaryBase.IsSilentlyExcluded() &&
        !IsSharedManagedWrapper(primaryBase);

    private static string GetManagedInstanceMethodModifier(BindingCompilation compilation, RecordBinding record, FunctionBinding function)
    {
        var hasManagedBase = !string.IsNullOrWhiteSpace(record.ManagedBaseTypeName);
        var hidingObjectToString = string.Equals(function.ManagedName, nameof(ToString), StringComparison.Ordinal) &&
            function.Parameters.Count == 0 &&
            !function.IsOverride;
        var hidingModifier = hidingObjectToString ? "new " : string.Empty;
        var primaryBaseVirtualMatch = hasManagedBase ? FindPrimaryBaseVirtualMatch(compilation, record, function) : null;
        // A configured managed base (not bound here) is assumed to declare the C++ overridden
        // virtuals; a bound base only overrides what it actually declares, since its own
        // bases may have been dropped.
        var overridesUnboundManagedBase = function.IsOverride &&
            hasManagedBase &&
            record.ManagedBaseTypeName!.Contains('.', StringComparison.Ordinal) &&
            !IsRenderedPrimaryBase(record.GetPrimaryBaseRecord(compilation));

        if (IsNativeInterfaceWrapper(record) && IsManagedAbstractMethod(function))
        {
            return hidingModifier + (ShouldUseNewModifier(compilation, record, function) ? "new virtual " : "virtual ");
        }

        if (IsManagedAbstractMethod(function) && primaryBaseVirtualMatch is null)
        {
            return hidingModifier + "abstract ";
        }

        if (function.IsVirtual)
        {
            string modifier;
            if (primaryBaseVirtualMatch is null)
            {
                modifier = ShouldUseNewModifier(compilation, record, function)
                    ? "new virtual "
                    : (overridesUnboundManagedBase ? "override " : "virtual ");
            }
            else
            {
                modifier = "override ";
            }

            if (function.IsFinal && modifier == "override ")
            {
                modifier += "sealed ";
            }

            return hidingModifier + modifier;
        }

        if (overridesUnboundManagedBase)
        {
            return "override ";
        }

        return hidingModifier.Length > 0 || ShouldUseNewModifier(compilation, record, function) ? "new " : string.Empty;
    }

    private static string GetManagedVisibility(BindingAccessModifier accessModifier)
    {
        return accessModifier switch
        {
            BindingAccessModifier.Protected => "protected",
            BindingAccessModifier.Private => "private",
            _ => "public"
        };
    }

    private static string GetManagedVisibilityNormalized(BindingCompilation compilation, RecordBinding record, FunctionBinding function)
    {
        if (!function.IsVirtual)
        {
            return GetManagedVisibility(function.AccessModifier);
        }

        if (function.AccessModifier == BindingAccessModifier.Protected && FindPrimaryBaseVirtualMatch(compilation, record, function) is null)
        {
            return "protected";
        }

        var mostRestrictiveBaseAccess = FindMostRestrictiveBaseAccess(compilation, record, function);
        if (mostRestrictiveBaseAccess == BindingAccessModifier.Protected)
        {
            return "protected";
        }

        return "public";
    }

    private static BindingAccessModifier FindMostRestrictiveBaseAccess(BindingCompilation compilation, RecordBinding record, FunctionBinding function)
    {
        var foundBaseMatch = false;
        var mostRestrictive = BindingAccessModifier.Public;

        foreach (var @base in record.Bases)
        {
            var baseRecord = compilation.Records.FirstOrDefault(candidate => string.Equals(candidate.SymbolId, @base.RecordId, StringComparison.Ordinal));
            if (baseRecord is null)
            {
                continue;
            }

            var match = baseRecord.Members
                .OfType<FunctionBinding>()
                .FirstOrDefault(bm => !bm.IsIgnored() && bm.IsVirtual && bm.ManagedName == function.ManagedName && bm.Parameters.Count == function.Parameters.Count);

            if (match is not null)
            {
                foundBaseMatch = true;
                if (match.AccessModifier == BindingAccessModifier.Protected)
                {
                    mostRestrictive = BindingAccessModifier.Protected;
                }
                else
                {
                    var inheritedRestrictive = FindMostRestrictiveBaseAccess(compilation, baseRecord, match);
                    if (inheritedRestrictive == BindingAccessModifier.Protected)
                    {
                        mostRestrictive = BindingAccessModifier.Protected;
                    }
                }
            }
        }

        return foundBaseMatch ? mostRestrictive : function.AccessModifier;
    }

    private static BindingAccessModifier GetAccessorPropertyAccessModifier(AccessorProperty property)
    {
        if (property.Getter?.AccessModifier == BindingAccessModifier.Protected ||
            property.Setter?.AccessModifier == BindingAccessModifier.Protected)
        {
            return BindingAccessModifier.Protected;
        }

        return BindingAccessModifier.Public;
    }

    private static FunctionBinding? FindPrimaryBaseVirtualMatch(BindingCompilation compilation, RecordBinding record, FunctionBinding function)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal) { record.SymbolId };
        for (var current = record.GetPrimaryBaseRecord(compilation); IsManagedChainBase(current); current = current!.GetPrimaryBaseRecord(compilation))
        {
            if (!visited.Add(current!.SymbolId))
            {
                break;
            }

            var candidates = current.Members
                .OfType<FunctionBinding>()
                .Where(candidate =>
                    !candidate.IsIgnored() &&
                    candidate.IsVirtual)
                .ToArray();
            var match = candidates.FirstOrDefault(candidate =>
                RecordBindingExtensions.SignaturesMatch(candidate, function) &&
                string.Equals(candidate.ReturnType.ManagedTypeName, function.ReturnType.ManagedTypeName, StringComparison.Ordinal));
            match ??= candidates.FirstOrDefault(candidate =>
                RecordBindingExtensions.OverridesSignature(function, candidate));
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static string RenderManagedCallbackParameter(BindingCompilation compilation, ParameterBinding parameter)
    {
        ValidateVirtualCallbackSupport(parameter);
        return $"{GetManagedVirtualCallbackAbiTypeName(compilation, parameter.Type)} {parameter.ManagedName}";
    }

    private static string BuildManagedCallbackInvocationArgument(BindingCompilation compilation, ParameterBinding parameter)
    {
        ValidateVirtualCallbackSupport(parameter);

        return parameter.Type.PrimitiveKind == BindingPrimitiveKind.Boolean
            ? $"{parameter.ManagedName} != 0"
            : parameter.Type.IsPointerType
            ? BuildManagedHandleWrap(compilation, parameter.Type, parameter.ManagedName, parameter.CallbackTakesOwnership)
            : BuildManagedImportToPublicExpression(compilation, parameter.Type, parameter.ManagedName);
    }

    private static string GetManagedVirtualCallbackAbiTypeName(BindingCompilation compilation, BindingType type)
    {
        return type.PrimitiveKind == BindingPrimitiveKind.Boolean
            ? "byte"
            : GetManagedImportTypeName(type, compilation);
    }

    private static string FormatReflectionParameterTypes(BindingCompilation compilation, FunctionBinding method)
    {
        return method.Parameters.Count == 0
            ? "System.Array.Empty<global::System.Type>()"
            : $"new global::System.Type[] {{ {string.Join(", ", method.Parameters.Select(parameter => $"typeof({GetManagedWrapperTypeName(parameter.Type, compilation)})"))} }}";
    }

    private static string GetVirtualCallbackPointerAccessorName(FunctionBinding method)
    {
        return $"__Get{GetVirtualCallbackTokenPrefix(method)}Pointer";
    }

    private static string GetVirtualCallbackDispatchName(FunctionBinding method)
    {
        return $"__Dispatch{GetVirtualCallbackTokenPrefix(method)}";
    }

    private static string GetVirtualCallbackEntryPointName(RecordBinding record, FunctionBinding method)
    {
        return $"__InvokeVirtualCallback_{SanitizeManagedToken(record.ManagedName)}_{SanitizeManagedToken(method.ImportMethodName)}";
    }

    private static string GetVirtualCallbackTokenPrefix(FunctionBinding method)
    {
        var ownerName = method.FindOwningRecord()?.ManagedName ?? "Global";
        return $"{SanitizeManagedToken(ownerName)}_{SanitizeManagedToken(method.ImportMethodName)}";
    }

    private static string SanitizeManagedToken(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');
        }

        var token = builder.ToString().Trim('_');
        return token.Length == 0 ? "Callback" : token;
    }

    private static void ValidateVirtualCallbackSupport(FunctionBinding method)
    {
        foreach (var parameter in method.Parameters)
        {
            ValidateVirtualCallbackSupport(parameter);
        }

    }

    private static void ValidateVirtualCallbackSupport(ParameterBinding parameter)
    {
        if (parameter.Direction is not BindingParameterDirection.In || parameter.AbiKind is not BindingParameterAbiKind.Value)
        {
            throw new NotSupportedException($"Director callback parameter '{parameter.ManagedName}' must be an in-value parameter.");
        }
    }

    private static string BuildManagedHandleWrap(BindingCompilation compilation, BindingType type, string handleExpression, bool ownsHandle)
    {
        var record = ResolveRecord(compilation, type);
        if (record is not null)
        {
            var moduleArgument = GetSharedManagedProviderModuleArgument(compilation, record);
            // Shared wrappers resolve their native shape through the provider registry.
            var nativeTemplateArgument = HasNativeNonTypeTemplateArguments(type) && !IsSharedManagedWrapper(record)
                ? $", nativeTemplateKey: \"{EscapeCSharpString(GetNativeTemplateArgumentKey(type.NativeTemplateArguments))}\""
                : string.Empty;
            // Fixed arrays cross as NativeArrayAbi; a bare pointer (callback argument) is
            // paired with the compile-time length.
            if (IsGenericArrayRecord(record) && !IsGenericDynamicArrayRecord(record) &&
                type.NativeTemplateArguments.FirstOrDefault(static argument => argument.Kind == BindingTemplateArgumentKind.Integral) is { } lengthArgument)
            {
                handleExpression = $"new global::SharpCpp.Support.NativeArrayAbi({handleExpression}, {lengthArgument.IntegralValue.ToString(System.Globalization.CultureInfo.InvariantCulture)})";
            }

            return $"{GetManagedWrapperTypeName(type, compilation)}.CreateManagedWrapper({handleExpression}, ownsHandle: {ownsHandle.ToString().ToLowerInvariant()}{moduleArgument}{nativeTemplateArgument})";
        }

        var wrapperTypeName = GetManagedWrapperTypeName(type, compilation);
        return ownsHandle
            ? $"new {wrapperTypeName}({handleExpression}, true)"
            : $"new {wrapperTypeName}({handleExpression})";
    }

    private static RecordBinding? ResolveRecord(BindingCompilation compilation, BindingType type)
    {
        return string.IsNullOrWhiteSpace(type.RecordId)
            ? null
            : compilation.Records.FirstOrDefault(candidate => string.Equals(candidate.SymbolId, type.RecordId, StringComparison.Ordinal));
    }

    private static string RenderManagedForwardingArgument(ParameterBinding parameter)
    {
        return parameter.Direction switch
        {
            BindingParameterDirection.In when parameter.AbiKind != BindingParameterAbiKind.Value => $"in {parameter.ManagedName}",
            BindingParameterDirection.Ref => $"ref {parameter.ManagedName}",
            BindingParameterDirection.Out => $"out {parameter.ManagedName}",
            _ => parameter.ManagedName
        };
    }

    private static string RenderManagedStaticTypeIdImportDeclaration(BindingCompilation compilation, RecordBinding record)
    {
        return $$"""
            [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "{{compilation.EntryPointPrefix}}_{{SanitizeNativeEntrypointName(record.CppQualifiedName)}}_type_id", StringMarshalling = StringMarshalling.Utf8)]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal static partial nuint GetStaticTypeId();
            """;
    }

    private static string RenderManagedNativeObjectMetadataImportDeclaration(BindingCompilation compilation, RecordBinding record)
    {
        return $$"""
            [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "{{compilation.EntryPointPrefix}}_{{SanitizeNativeEntrypointName(record.CppQualifiedName)}}_metadata", StringMarshalling = StringMarshalling.Utf8)]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal static partial void GetNativeObjectMetadata(nint self, out nint handle, out nuint dynamicTypeId);
            """;
    }

    private static string RenderNativeObjectRegistryInitializer(BindingCompilation compilation, IReadOnlyList<RecordBinding> records)
    {
        var writer = new CodeWriter();
        writer.Write(
            """
            internal static class __NativeObjectRegistryInitializer
            {
            [global::System.Runtime.CompilerServices.ModuleInitializer]
            internal static void RegisterAll()
            {
            """);

        foreach (var record in records.OrderBy(static record => record.ManagedName, StringComparer.Ordinal))
        {
            var qualifiedRecordTypeName = FormatQualifiedRecordTypeName(compilation, record.HandleType, publicSurface: false);
            writer.Write($$"""
                global::SharpCpp.Support.NativeObjectRegistry.RegisterType<{{qualifiedRecordTypeName}}>();
                """);
        }

        writer.Write(
            """
            }
            }
            """);
        return writer.ToString();
    }


    private static string GetManagedImportMethodName(FunctionBinding function)
    {
        return function.ImportMethodName;
    }

    private static string SanitizeNativeEntrypointName(string value)
    {
        var sanitized = value.Replace("::", "_", StringComparison.Ordinal).ToCharArray();
        for (var index = 0; index < sanitized.Length; ++index)
        {
            if (!char.IsAsciiLetterOrDigit(sanitized[index]) && sanitized[index] != '_')
            {
                sanitized[index] = '_';
            }
        }

        return new string(sanitized);
    }

    private static string GetNativeDeleteEntryPointName(BindingCompilation compilation, RecordBinding record)
        => $"{compilation.EntryPointPrefix}_{GetNativeRecordEntryPointToken(record)}_delete";

    private static string GetNativeCloneEntryPointName(BindingCompilation compilation, RecordBinding record)
        => $"{compilation.EntryPointPrefix}_{GetNativeRecordEntryPointToken(record)}_clone";

    private static string GetNativeRecordEntryPointToken(RecordBinding record)
        => IsSharedManagedWrapper(record)
            ? GetSharedNativeShapeToken(record.CppQualifiedName)
            : SanitizeNativeEntrypointName(record.CppQualifiedName);

    private static string GetInstanceCacheFactoryExpression(RecordBinding record)
    {
        return string.IsNullOrWhiteSpace(record.ManagedInstanceCacheFactoryExpression)
            ? "new global::SharpCpp.Support.DefaultNativeInstanceCache()"
            : record.ManagedInstanceCacheFactoryExpression!;
    }

    private static string GetInstanceCacheFieldTypeName(RecordBinding record)
    {
        if (string.IsNullOrWhiteSpace(record.ManagedInstanceCacheFactoryExpression))
        {
            return "global::SharpCpp.Support.DefaultNativeInstanceCache";
        }

        return string.IsNullOrWhiteSpace(record.ManagedInstanceCacheTypeName)
            ? "global::SharpCpp.Support.INativeInstanceCache"
            : record.ManagedInstanceCacheTypeName!;
    }

    private static string GetManagedParameterAttribute(ParameterBinding parameter, BindingType type, BindingCompilation compilation)
    {
        if (type.IsCallback && type.ManagedMarshaller is null)
        {
            return $"[MarshalUsing(typeof({GetManagedCallbackMarshallerTypeName(compilation, parameter)}))] ";
        }

        return GetManagedParameterAttribute(type, compilation);
    }

    private static string GetManagedParameterAttribute(BindingType type, BindingCompilation compilation)
    {
        if (type.ManagedMarshaller is not null)
        {
            return $"[MarshalUsing(typeof({type.ManagedMarshaller}))] ";
        }

        if (type.IsCallback)
        {
            return $"[MarshalUsing(typeof(global::{compilation.ManagedNamespace}.{compilation.ManagedClassName}.{GetManagedCallbackMarshallerDeclarationName(compilation, type)}))] ";
        }

        return type.PrimitiveKind == BindingPrimitiveKind.Boolean
            ? "[MarshalAs(UnmanagedType.I1)] "
            : string.Empty;
    }

    private static string GetManagedReturnAttribute(BindingType type)
    {
        if (type.ManagedMarshaller is not null)
        {
            return $"[return: MarshalUsing(typeof({type.ManagedMarshaller}))]";
        }

        return type.PrimitiveKind == BindingPrimitiveKind.Boolean
            ? "[return: MarshalAs(UnmanagedType.I1)]"
            : string.Empty;
    }

    private static string GetManagedEnumUnderlyingTypeName(EnumBinding @enum)
    {
        return GetManagedPrimitiveTypeName(@enum.UnderlyingPrimitiveKind);
    }

    private static string GetManagedPrimitiveTypeName(BindingPrimitiveKind kind) => kind switch
    {
        BindingPrimitiveKind.Int8 => "sbyte",
        BindingPrimitiveKind.UInt8 => "byte",
        BindingPrimitiveKind.Int32 => "int",
        BindingPrimitiveKind.UInt32 => "uint",
        BindingPrimitiveKind.UInt16 => "ushort",
        BindingPrimitiveKind.Int16 => "short",
        BindingPrimitiveKind.UInt64 => "ulong",
        BindingPrimitiveKind.Int64 => "long",
        _ => "int"
    };

    private static string NormalizeEnumManagedTypeName(string managedTypeName)
    {
        var normalized = managedTypeName
            .Replace("Const ", string.Empty, StringComparison.Ordinal)
            .Replace("const ", string.Empty, StringComparison.Ordinal)
            .Trim();

        if (!normalized.Contains(' ', StringComparison.Ordinal) && !normalized.Contains('_', StringComparison.Ordinal))
        {
            return normalized;
        }

        return string.Concat(
            normalized
                .Split([' ', '_'], StringSplitOptions.RemoveEmptyEntries)
                .Select(static part => char.ToUpperInvariant(part[0]) + part[1..]));
    }

    private static string RenderIgnoredStub(BindingNode node)
    {
        var reason = node.TryGetMetadata(MetadataKeys.IgnoreReason, out string? metadataReason)
            ? metadataReason!
            : "excluded";
        var declarationText = node.TryGetMetadata(MetadataKeys.DeclarationDisplayText, out string? metadataDeclaration)
            ? metadataDeclaration
            : string.Empty;

        return node switch
        {
            RecordBinding record => RenderIgnoredDeclarationStub(declarationText, $"{record.NativeName};", reason),
            EnumBinding @enum => RenderIgnoredDeclarationStub(declarationText, $"enum {@enum.NativeName};", reason),
            FunctionBinding function => RenderIgnoredFunctionStub(function, declarationText, reason),
            FieldBinding field => RenderIgnoredDeclarationStub(declarationText, $"{field.Type.CppTypeName} {field.NativeName};", reason),
            _ => $"// {node.Kind}; // excluded: {reason}"
        };
    }

    private static string RenderIgnoredDeclarationStub(string? declarationText, string fallback, string reason)
    {
        var declaration = string.IsNullOrWhiteSpace(declarationText) ? fallback : declarationText;
        return $"// {declaration} // excluded: {reason}";
    }

    private static string RenderIgnoredFunctionStub(FunctionBinding function, string? declarationText, string reason)
    {
        if (!string.IsNullOrWhiteSpace(declarationText))
        {
            return $"// {declarationText} // excluded: {reason}";
        }

        var parameterList = string.Join(", ", function.Parameters.Select(RenderIgnoredNativeParameter));
        var returnTypeName = function.CppReturnTypeName ?? function.ReturnType.CppTypeName;

        if (function.IsConstructor)
        {
            var constructorName = function.CppQualifiedName[(function.CppQualifiedName.LastIndexOf("::", StringComparison.Ordinal) + 2)..];
            return $"// {constructorName}({parameterList}); // excluded: {reason}";
        }

        var staticModifier = function.IsStatic || function.Parent is null ? "static " : string.Empty;
        var qualifier = function.Parent is RecordBinding record ? $"{record.CppQualifiedName}::" : string.Empty;
        return $"// {staticModifier}{returnTypeName} {qualifier}{function.NativeName}({parameterList}); // excluded: {reason}";
    }

    private static bool CanEmitClone(RecordBinding record, bool isEffectivelyAbstract, bool canEmitDeleteThunk)
    {
        ArgumentNullException.ThrowIfNull(record);

        return record.HasCopyConstructor &&
            !isEffectivelyAbstract &&
            (!record.EmitDefaultNativeReleaseOverride || canEmitDeleteThunk);
    }

    private static bool CanEmitDeleteThunk(RecordBinding record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return !record.GetMetadata<bool>(MetadataKeys.NonPublicDestructor) &&
            !record.GetMetadata<bool>(MetadataKeys.OpaqueNativeHandle) &&
            !IsNativeInterfaceWrapper(record);
    }

    private static bool CanCreateManagedWrapperFactory(BindingCompilation compilation, RecordBinding record)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(record);

        return !IsManagedWrapperAbstract(compilation, record, record.GetEffectiveVirtualMethodSlots(compilation));
    }

    private static bool IsManagedWrapperAbstract(
        BindingCompilation compilation,
        RecordBinding record,
        IReadOnlyCollection<VirtualMethodSlotBinding> virtualSlots)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(virtualSlots);

        if (IsNativeInterfaceWrapper(record))
        {
            return false;
        }

        return record.IsAbstract ||
            virtualSlots.Any(static slot => IsManagedAbstractMethod(slot.Method)) ||
            HasUnimplementedManagedAbstractBaseMethods(compilation, record);
    }

    private static bool IsNativeInterfaceWrapper(RecordBinding record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return record.GetMetadata<bool>(MetadataKeys.NativeInterfaceWrapper);
    }

    private static bool TryGetNativeInterfaceQuery(RecordBinding record, out NativeInterfaceQueryAnnotation query)
    {
        return record.TryGetMetadata(MetadataKeys.NativeInterfaceQuery, out query!);
    }

    private static string RenderNativeInterfaceQuery(BindingCompilation compilation, RecordBinding record, NativeInterfaceQueryAnnotation query)
    {
        return $$"""
            SHARPCPP_EXPORT ret_t<{{record.CppQualifiedName}}*> {{compilation.EntryPointPrefix}}_{{SanitizeNativeEntrypointName(record.CppQualifiedName)}}_query_interface(arg_t<{{query.SourceRecordCppQualifiedName}}*> self)
            {
            if (self == nullptr)
            {
            return nullptr;
            }

            {{record.CppQualifiedName}}* result = nullptr;
            self->QueryInterface({{query.InterfaceIdExpression}}, reinterpret_cast<{{query.SourceRecordCppQualifiedName}}**>(&result));
            return result;
            }
            """;
    }

    private static string RenderNativeInterfaceRelease(BindingCompilation compilation, RecordBinding record, string releaseMethod)
    {
        return $$"""
            SHARPCPP_EXPORT ret_t<void> {{compilation.EntryPointPrefix}}_{{SanitizeNativeEntrypointName(record.CppQualifiedName)}}_release_interface(arg_t<{{record.CppQualifiedName}}*> self)
            {
            if (self != nullptr)
            {
            self->{{releaseMethod}}();
            }
            }
            """;
    }

    private static bool IsManagedAbstractMethod(FunctionBinding method)
    {
        ArgumentNullException.ThrowIfNull(method);

        return method.IsPureVirtual && method.AccessModifier == BindingAccessModifier.Public;
    }

    private static bool ShouldRenderPureVirtualManagedStub(FunctionBinding method)
    {
        ArgumentNullException.ThrowIfNull(method);

        return method.IsPureVirtual && method.AccessModifier != BindingAccessModifier.Public;
    }

    private static bool HasUnimplementedManagedAbstractBaseMethods(BindingCompilation compilation, RecordBinding record)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(record);

        var chain = new List<RecordBinding>();
        var visited = new HashSet<string>(StringComparer.Ordinal) { record.SymbolId };
        for (var current = record.GetPrimaryBaseRecord(compilation); IsManagedChainBase(current); current = current!.GetPrimaryBaseRecord(compilation))
        {
            if (!visited.Add(current!.SymbolId))
            {
                break;
            }

            chain.Insert(0, current);
        }

        chain.Add(record);

        var concreteImplementations = chain
            .SelectMany(static candidate => candidate.Members.OfType<FunctionBinding>())
            .Where(static method => !method.IsIgnored() && !method.IsPureVirtual)
            .ToArray();

        foreach (var baseRecord in chain.Take(chain.Count - 1))
        {
            foreach (var abstractMethod in baseRecord.Members.OfType<FunctionBinding>().Where(static method => !method.IsIgnored() && IsManagedAbstractMethod(method)))
            {
                if (!concreteImplementations.Any(candidate =>
                        ManagedSignaturesMatch(candidate, abstractMethod) ||
                        RecordBindingExtensions.OverridesSignature(candidate, abstractMethod)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ManagedSignaturesMatch(FunctionBinding candidate, FunctionBinding abstractMethod)
    {
        if (!string.Equals(candidate.ManagedName, abstractMethod.ManagedName, StringComparison.Ordinal) ||
            candidate.Parameters.Count != abstractMethod.Parameters.Count)
        {
            return false;
        }

        for (var i = 0; i < candidate.Parameters.Count; i++)
        {
            var left = candidate.Parameters[i].Type;
            var right = abstractMethod.Parameters[i].Type;
            if (!string.Equals(left.ManagedTypeName, right.ManagedTypeName, StringComparison.Ordinal) ||
                left.Kind != right.Kind)
            {
                return false;
            }
        }

        return true;
    }

    private static bool InheritsCloneInterface(BindingCompilation compilation, RecordBinding record)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal) { record.SymbolId };
        for (var current = record.GetPrimaryBaseRecord(compilation); IsManagedChainBase(current); current = current!.GetPrimaryBaseRecord(compilation))
        {
            if (!visited.Add(current!.SymbolId))
            {
                break;
            }

            var isManagedAbstract = IsManagedWrapperAbstract(
                compilation,
                current,
                current.GetEffectiveVirtualMethodSlots(compilation));
            if (CanEmitClone(
                    current,
                    isManagedAbstract || current.IsEffectivelyAbstract || current.HasUnimplementedAbstractBaseMethods(compilation),
                    current.IsGeneric || CanEmitDeleteThunk(current)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ShouldBorrowFieldStorage(BindingCompilation compilation, FieldBinding field)
    {
        if (field.Type.Kind != BindingTypeKind.Handle || field.IsPointer)
        {
            return false;
        }

        return field.Type.Marshaller.CppIsRecordValue || IsMarshallerBackedStorageWrapper(compilation, field.Type);
    }

    private static bool IsMarshallerBackedStorageWrapper(BindingCompilation compilation, BindingType type)
    {
        var record = ResolveRecord(compilation, type);
        return record is not null &&
            record.Members.OfType<FunctionBinding>().Any(static method => method.HasMetadata(MetadataKeys.MarshallerExposedMethod));
    }

    private static bool IsNativeFixedArrayType(BindingCompilation compilation, BindingType type)
    {
        return type.IsFixedArray &&
            ResolveRecord(compilation, type)?.GetMetadata<bool>(MetadataKeys.NativeArrayRecord) == true;
    }

    private static string RenderNativeMarshallerExposedInvocation(FunctionBinding method, string marshallerCppType, string abiArguments)
    {
        var signatureParameters = string.Join(", ", new[] { $"{method.OwningRecordCppTypeName}*" }.Concat(method.Parameters.Select(static parameter => parameter.CppTypeName)));
        var lambdaParameters = string.Join(", ", new[] { $"{method.OwningRecordCppTypeName}* __self" }
            .Concat(method.Parameters.Select((parameter, index) => $"{parameter.CppTypeName} __arg{index}")));
        var invokeArguments = string.Join(", ", new[] { "*__self" }
            .Concat(method.Parameters.Select((_, index) => $"__arg{index}")));
        var callArguments = string.IsNullOrWhiteSpace(abiArguments) ? "self" : $"self, {abiArguments}";
        var returnStatement = method.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void
            ? $"Marshaller<{marshallerCppType}>::{method.NativeName}({invokeArguments});"
            : $"return Marshaller<{marshallerCppType}>::{method.NativeName}({invokeArguments});";
        var marshallerTemplateArguments = BuildMarshallerInvokerTemplateArguments(
            "FT",
            new[] { $"{method.OwningRecordCppTypeName}*" }.Concat(method.Parameters.Select(static parameter => parameter.CppTypeName)));
        var dispatch = method.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void
            ? $"MarshallerInvoker::invoke_as<{marshallerTemplateArguments}>(__invoke, {callArguments});"
            : $"return MarshallerInvoker::invoke_as<{marshallerTemplateArguments}>(__invoke, {callArguments});";

        return $$"""
            using FT = {{method.CppReturnTypeName}}({{signatureParameters}});
            auto __invoke = []({{lambdaParameters}}) -> {{method.CppReturnTypeName}}
            {
            {{returnStatement}}
            };
            {{dispatch}}
            """;
    }

    private static string RenderIgnoredNativeParameter(ParameterBinding parameter)
        => $"{parameter.CppTypeName} {parameter.NativeName}";

    private static void WriteDocumentationComment(CodeWriter writer, BindingNode node)
    {
        if (!node.TryGetMetadata(MetadataKeys.DocumentationComment, out string? comment) || string.IsNullOrWhiteSpace(comment))
        {
            return;
        }

        var escaped = System.Security.SecurityElement.Escape(comment!);
        writer.Write($"/// <summary>{escaped}</summary>");
    }

    // Imports live in static classes, where these names would hide System.Object members.
    private static readonly HashSet<string> s_objectMemberNames = new(StringComparer.Ordinal)
    {
        "Equals", "Finalize", "GetHashCode", "GetType", "MemberwiseClone", "ReferenceEquals", "ToString"
    };

    private static string GetImportMethodName(FunctionBinding function, int counter)
    {
        var name = s_objectMemberNames.Contains(function.ManagedName) ? $"Native{function.ManagedName}" : function.ManagedName;
        return counter == 1 ? name : $"{name}{counter}";
    }

    private static void AssignImportMethodNames(BindingCompilation compilation)
    {
        foreach (var group in compilation.Members.OfType<FunctionBinding>()
            .Where(static function => function.Parent is not RecordBinding)
            .GroupBy(function => $"{GetManagedNamespace(compilation, function)}|{GetManagedClassName(compilation, function)}|{function.ManagedName}", StringComparer.Ordinal))
        {
            var counter = 0;
            foreach (var function in group.OrderBy(static f => f.SymbolId, StringComparer.Ordinal))
            {
                counter++;
                function.ImportMethodName = GetImportMethodName(function, counter);
            }
        }

        foreach (var record in compilation.Records)
        {
            foreach (var group in record.Members.OfType<FunctionBinding>()
                .GroupBy(static function => function.ManagedName, StringComparer.Ordinal))
            {
                var counter = 0;
                foreach (var function in group.OrderBy(static f => f.SymbolId, StringComparer.Ordinal))
                {
                    counter++;
                    function.ImportMethodName = GetImportMethodName(function, counter);
                }
            }
        }
    }

    private static bool HasUnexpressibleDefaultParams(FunctionBinding function)
    {
        return function.Parameters.Any(static parameter => parameter.HasMetadata(MetadataKeys.UnexpressibleDefault));
    }

    private static bool HasNativeDefaultDispatchParams(FunctionBinding function)
    {
        return function.Parameters.Any(IsNativeDefaultDispatchParameter);
    }

    private static bool IsNativeDefaultDispatchParameter(ParameterBinding parameter)
    {
        return parameter.HasMetadata(MetadataKeys.UnexpressibleDefault) ||
            IsNullableManagedReferenceDefaultParameter(parameter);
    }

    private static bool HasUndefaultableParams(FunctionBinding function)
    {
        return function.Parameters.Any(static parameter => parameter.HasMetadata(MetadataKeys.UndefaultableParam));
    }

    private static int GetFirstUndefaultableParamIndex(FunctionBinding function)
    {
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            if (function.Parameters[i].HasMetadata(MetadataKeys.UndefaultableParam))
            {
                return i;
            }
        }

        return -1;
    }

    private static IReadOnlyList<int> GetUnexpressibleDefaultParameterIndices(FunctionBinding function)
    {
        var indices = new List<int>();
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            if (function.Parameters[i].HasMetadata(MetadataKeys.UnexpressibleDefault))
            {
                indices.Add(i);
            }
        }

        return indices;
    }

    private static IReadOnlyList<int> GetUnexpressibleDefaultConvenienceOverloadParameterIndices(FunctionBinding function)
    {
        var unexpressibleDefaultParameters = GetUnexpressibleDefaultParameterIndices(function);
        if (unexpressibleDefaultParameters.Count == 0)
        {
            return Array.Empty<int>();
        }

        var mainDefaultFlags = GetManagedPublicParameterDefaultFlags(function);
        return unexpressibleDefaultParameters
            .Where(index => !IsRedundantUnexpressibleDefaultConvenienceOverload(function, index, mainDefaultFlags))
            .ToArray();
    }

    private static bool IsRedundantUnexpressibleDefaultConvenienceOverload(
        FunctionBinding function,
        int keepCount,
        IReadOnlyList<bool> mainDefaultFlags)
    {
        for (var i = 0; i < keepCount; i++)
        {
            if (HasManagedUnexpressibleDefaultOverloadParameterDefault(function, keepCount, i) && !mainDefaultFlags[i])
            {
                return false;
            }
        }

        for (var i = keepCount; i < function.Parameters.Count; i++)
        {
            if (!mainDefaultFlags[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasManagedUnexpressibleDefaultOverloadParameterDefault(FunctionBinding function, int keepCount, int parameterIndex)
    {
        if (function.Parameters[parameterIndex].HasMetadata(MetadataKeys.UnexpressibleDefault))
        {
            return false;
        }

        var prefixFlags = GetManagedPublicParameterDefaultFlags(function.Parameters.Take(keepCount).ToArray());
        return parameterIndex < prefixFlags.Count && prefixFlags[parameterIndex];
    }

    private static IReadOnlyList<bool> GetManagedPublicParameterDefaultFlags(FunctionBinding function)
    {
        return GetManagedPublicParameterDefaultFlags(function.Parameters.ToArray());
    }

    private static IReadOnlyList<bool> GetManagedPublicParameterDefaultFlags(IReadOnlyList<ParameterBinding> parameterArray)
    {
        var firstUnexpressibleDefault = parameterArray
            .Select((parameter, index) => (parameter, index))
            .FirstOrDefault(static entry => entry.parameter.HasMetadata(MetadataKeys.UnexpressibleDefault));
        var lastRequiredParameterIndex = -1;
        for (var i = parameterArray.Count - 1; i >= 0; i--)
        {
            if (!CanRenderManagedPublicDefault(parameterArray[i]))
            {
                lastRequiredParameterIndex = i;
                break;
            }
        }

        var flags = new bool[parameterArray.Count];

        for (var i = 0; i < parameterArray.Count; i++)
        {
            var parameter = parameterArray[i];
            if (firstUnexpressibleDefault.parameter is not null)
            {
                if (i < firstUnexpressibleDefault.index)
                {
                    continue;
                }

                if (parameter.HasMetadata(MetadataKeys.UnexpressibleDefault))
                {
                    flags[i] = i >= lastRequiredParameterIndex;
                    continue;
                }
            }

            flags[i] = i >= lastRequiredParameterIndex && CanRenderManagedPublicDefault(parameter);
        }

        return flags;
    }

    private static IReadOnlyList<int> GetNativeDefaultDispatchParameterIndices(FunctionBinding function)
    {
        var indices = new List<int>();
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            if (IsNativeDefaultDispatchParameter(function.Parameters[i]))
            {
                indices.Add(i);
            }
        }

        return indices;
    }

    private static IReadOnlyList<int> GetVariantParameterIndices(FunctionBinding function)
    {
        var indices = new HashSet<int>(GetNativeDefaultDispatchParameterIndices(function));
        var firstUndefaultable = GetFirstUndefaultableParamIndex(function);
        if (firstUndefaultable >= 0)
        {
            indices.Add(firstUndefaultable);
        }

        return indices.OrderBy(static i => i).ToArray();
    }

    /// <summary>Variant thunks that are called: the undefaultable prefix variant goes with its dropped overload.</summary>
    private static IReadOnlyList<int> GetEmittedVariantParameterIndices(BindingCompilation compilation, FunctionBinding function)
    {
        var indices = GetVariantParameterIndices(function);
        var firstUndefaultable = GetFirstUndefaultableParamIndex(function);
        if (firstUndefaultable < 0 ||
            ShouldEmitUndefaultablePrefixOverload(compilation, function) ||
            GetNativeDefaultDispatchParameterIndices(function).Contains(firstUndefaultable))
        {
            return indices;
        }

        return indices.Where(index => index != firstUndefaultable).ToArray();
    }

    private static IReadOnlyList<ParameterBinding> GetUnexpressibleVariantParams(FunctionBinding function, int keepCount)
    {
        return function.Parameters.Take(keepCount).ToArray();
    }

    private static string GetUnexpressibleVariantImportName(string importMethodName, int keepCount)
    {
        return $"{importMethodName}__Args{keepCount}";
    }

    private static string GetUnexpressibleVariantEntrypointName(string entryPointName, int keepCount)
    {
        return $"{entryPointName}__Args{keepCount}";
    }

    private static string RenderUnexpressibleDefaultFreeFunctionImport(BindingCompilation compilation, FunctionBinding function)
    {
        var writer = new CodeWriter();
        var importMethodName = GetManagedImportMethodName(function);
        foreach (var keepCount in GetEmittedVariantParameterIndices(compilation, function))
        {
            var variantParams = GetUnexpressibleVariantParams(function, keepCount);
            var parameterList = string.Join(", ", variantParams.Select(parameter => RenderManagedImportParameter(parameter, compilation)));
            var returnTypeName = GetManagedImportReturnTypeName(compilation, function.ReturnType);
            var returnAttribute = GetManagedImportReturnAttribute(compilation, function.ReturnType, ReturnsOwnedHandle(function));
            var importName = GetUnexpressibleVariantImportName(importMethodName, keepCount);
            var entrypointName = GetUnexpressibleVariantEntrypointName(function.EntryPointName, keepCount);
            var unsafeModifier = parameterList.Contains('*', StringComparison.Ordinal) ? "unsafe " : string.Empty;
            var importMethodLine = string.IsNullOrWhiteSpace(returnAttribute)
                ? $"internal static {unsafeModifier}partial {returnTypeName} {importName}({parameterList});"
                : $"{returnAttribute}\ninternal static {unsafeModifier}partial {returnTypeName} {importName}({parameterList});";

            writer.Write($$"""
                [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "{{entrypointName}}", StringMarshalling = StringMarshalling.Utf8)]
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                {{importMethodLine}}
                """);
        }
        return writer.ToString();
    }

    private static string RenderManagedUnexpressibleDefaultStaticImports(BindingCompilation compilation, FunctionBinding function)
    {
        var writer = new CodeWriter();
        var importMethodName = GetManagedImportMethodName(function);
        foreach (var keepCount in GetEmittedVariantParameterIndices(compilation, function))
        {
            var variantParams = GetUnexpressibleVariantParams(function, keepCount);
            var parameterList = string.Join(", ", variantParams.Select(parameter => RenderManagedImportParameter(parameter, compilation)));
            var returnTypeName = GetManagedImportReturnTypeName(compilation, function.ReturnType);
            var returnAttribute = GetManagedImportReturnAttribute(compilation, function.ReturnType, ReturnsOwnedHandle(function));
            var variantImportName = GetUnexpressibleVariantImportName(importMethodName, keepCount);
            var variantEntrypointName = GetUnexpressibleVariantEntrypointName(function.EntryPointName, keepCount);
            var unsafeModifier = parameterList.Contains('*', StringComparison.Ordinal) ? "unsafe " : string.Empty;
            var importMethodLine = string.IsNullOrWhiteSpace(returnAttribute)
                ? $"internal static {unsafeModifier}partial {returnTypeName} {variantImportName}({parameterList});"
                : $"{returnAttribute}\ninternal static {unsafeModifier}partial {returnTypeName} {variantImportName}({parameterList});";

            writer.Write($$"""
                [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "{{variantEntrypointName}}", StringMarshalling = StringMarshalling.Utf8)]
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                {{importMethodLine}}
                """);
        }

        return writer.ToString();
    }

    private static string RenderManagedUnexpressibleDefaultInstanceImports(BindingCompilation compilation, FunctionBinding function, string recordManagedName, bool useCallbase = false)
    {
        var writer = new CodeWriter();
        var importMethodName = useCallbase
            ? GetManagedCallbaseImportMethodName(function)
            : GetManagedImportMethodName(function);
        var owningRecord = function.FindOwningRecord();
        var entryPointName = useCallbase && owningRecord is not null
            ? GetNativeCallbaseEntrypointName(compilation, owningRecord, function)
            : function.EntryPointName;

        foreach (var keepCount in GetEmittedVariantParameterIndices(compilation, function))
        {
            var variantParams = GetUnexpressibleVariantParams(function, keepCount);
            var parameterList = string.Join(", ", new[] { $"{recordManagedName} self" }.Concat(variantParams.Select(parameter => RenderManagedImportParameter(parameter, compilation))));
            var returnTypeName = GetManagedImportReturnTypeName(compilation, function.ReturnType);
            var returnAttribute = GetManagedImportReturnAttribute(compilation, function.ReturnType, ReturnsOwnedHandle(function));
            var variantImportName = GetUnexpressibleVariantImportName(importMethodName, keepCount);
            var variantEntrypointName = GetUnexpressibleVariantEntrypointName(entryPointName, keepCount);
            var unsafeModifier = parameterList.Contains('*', StringComparison.Ordinal) ? "unsafe " : string.Empty;
            var importMethodLine = string.IsNullOrWhiteSpace(returnAttribute)
                ? $"internal static {unsafeModifier}partial {returnTypeName} {variantImportName}({parameterList});"
                : $"{returnAttribute}\ninternal static {unsafeModifier}partial {returnTypeName} {variantImportName}({parameterList});";

            writer.Write($$"""
                [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "{{variantEntrypointName}}", StringMarshalling = StringMarshalling.Utf8)]
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                {{importMethodLine}}
                """);
        }

        return writer.ToString();
    }

    private static string RenderManagedUnexpressibleDefaultConstructorImports(BindingCompilation compilation, FunctionBinding function)
    {
        var writer = new CodeWriter();
        var importMethodName = GetManagedImportMethodName(function);
        foreach (var keepCount in GetEmittedVariantParameterIndices(compilation, function))
        {
            var variantParams = GetUnexpressibleVariantParams(function, keepCount);
            var parameterList = string.Join(", ", variantParams.Select(parameter => RenderManagedImportParameter(parameter, compilation)));
            var variantImportName = GetUnexpressibleVariantImportName(importMethodName, keepCount);
            var variantEntrypointName = GetUnexpressibleVariantEntrypointName(function.EntryPointName, keepCount);
            var unsafeModifier = parameterList.Contains('*', StringComparison.Ordinal) ? "unsafe " : string.Empty;

            writer.Write($$"""
                [LibraryImport("{{compilation.NativeLibraryName}}", EntryPoint = "{{variantEntrypointName}}", StringMarshalling = StringMarshalling.Utf8)]
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                internal static {{unsafeModifier}}partial nint {{variantImportName}}({{parameterList}});
                """);
        }

        return writer.ToString();
    }

    private static void EmitUnexpressibleDefaultVariant(
        CodeWriter writer,
        FunctionBinding function,
        IReadOnlyList<ParameterBinding> variantParams,
        int keepCount,
        string entryPointBaseName,
        string invocation,
        string? selfCppTypeName = null)
    {
        var isSelf = selfCppTypeName is not null;

        var parameterParts = isSelf
            ? new[] { $"arg_t<{selfCppTypeName}*> self" }.Concat(variantParams.Select(RenderNativeParameter))
            : variantParams.Select(RenderNativeParameter);
        var parameterList = string.Join(", ", parameterParts);

        var abiArgs = string.Join(", ", variantParams.Select(static p => p.NativeName));

        var entrypointName = GetUnexpressibleVariantEntrypointName(entryPointBaseName, keepCount);

        var cppTypeParts = isSelf
            ? new[] { $"{selfCppTypeName}*" }.Concat(variantParams.Select(static p => p.CppTypeName))
            : variantParams.Select(static p => p.CppTypeName);
        var cppTypeNames = string.Join(", ", cppTypeParts);

        var paramDeclParts = isSelf
            ? new[] { "auto&& self" }.Concat(variantParams.Select(static p => $"auto&& {p.NativeName}"))
            : variantParams.Select(static p => $"auto&& {p.NativeName}");
        var cppParamDecls = string.Join(", ", paramDeclParts);

        var signature = $"{function.CppReturnTypeName}({cppTypeNames})";
        var marshallerTemplateArguments = BuildMarshallerInvokerTemplateArguments("FT", cppTypeParts);

        var invokeAsArgs = isSelf
            ? $"fn, self{(abiArgs.Length > 0 ? $", {abiArgs}" : string.Empty)}"
            : $"fn{(abiArgs.Length > 0 ? $", {abiArgs}" : string.Empty)}";

        writer.Write($$"""
            SHARPCPP_EXPORT_NAMED({{entrypointName}}) {{GetNativeEntrypointReturnType(function)}} {{entrypointName}}({{parameterList}})
            {
                using FT = {{signature}};
                auto fn = []({{cppParamDecls}}) -> decltype(auto)
                {
                    {{invocation}}
                };
                {{(RequiresOwnedRecordValueReturn(function)
                    ? $"return MarshallerInvoker::invoke_owned_as<{marshallerTemplateArguments}>({invokeAsArgs});"
                    : $"return MarshallerInvoker::invoke_as<{marshallerTemplateArguments}>({invokeAsArgs});")}}
            }

            """);
    }

    private static string ForwardVariantArgs(IReadOnlyList<ParameterBinding> variantParams)
    {
        return string.Join(", ", variantParams.Select(static p => $"std::forward<decltype({p.NativeName})>({p.NativeName})"));
    }

    private static string BuildNativeVariantCallArguments(FunctionBinding function, IReadOnlyList<ParameterBinding> variantParams)
    {
        var providedParameterNames = new HashSet<string>(variantParams.Select(static parameter => parameter.NativeName), StringComparer.Ordinal);
        var arguments = new List<string>(function.Parameters.Count);
        foreach (var parameter in function.Parameters)
        {
            if (providedParameterNames.Contains(parameter.NativeName))
            {
                arguments.Add($"std::forward<decltype({parameter.NativeName})>({parameter.NativeName})");
                continue;
            }

            if (!parameter.TryGetMetadata<string>(MetadataKeys.NativeDefaultValueExpression, out var defaultValue) ||
                string.IsNullOrWhiteSpace(defaultValue))
            {
                throw new InvalidOperationException($"Parameter '{parameter.ManagedName}' on '{function.ManagedName}' is missing native default metadata.");
            }

            arguments.Add(defaultValue);
        }

        return string.Join(", ", arguments);
    }

    // Default arguments are spelled in the record's namespace; a nested record's enclosing
    // classes are not namespaces.
    private static string BuildConstructorDefaultNamespacePreamble(BindingCompilation compilation, FunctionBinding constructor)
    {
        var cppScope = constructor.OwningRecordCppTypeName;
        if (string.IsNullOrWhiteSpace(cppScope))
            return string.Empty;

        do
        {
            var lastSeparatorIndex = cppScope.LastIndexOf("::", StringComparison.Ordinal);
            if (lastSeparatorIndex <= 0)
                return string.Empty;
            cppScope = cppScope[..lastSeparatorIndex];
        }
        while (compilation.Records.Any(record => string.Equals(record.CppQualifiedName, cppScope, StringComparison.Ordinal)));

        return $"using namespace {cppScope};";
    }

    private static string RenderNativeUnexpressibleDefaultFreeFunction(BindingCompilation compilation, FunctionBinding function)
    {
        var writer = new CodeWriter();
        foreach (var keepCount in GetEmittedVariantParameterIndices(compilation, function))
        {
            var variantParams = GetUnexpressibleVariantParams(function, keepCount);
            var invocation = $"return {function.CppQualifiedName}({ForwardVariantArgs(variantParams)});";
            EmitUnexpressibleDefaultVariant(writer, function, variantParams, keepCount, function.EntryPointName, invocation);
        }
        return writer.ToString();
    }

    private static string RenderNativeUnexpressibleDefaultStaticMethod(BindingCompilation compilation, FunctionBinding method)
    {
        var writer = new CodeWriter();
        foreach (var keepCount in GetEmittedVariantParameterIndices(compilation, method))
        {
            var variantParams = GetUnexpressibleVariantParams(method, keepCount);
            var invocation = $"return {method.OwningRecordCppTypeName}::{method.NativeName}({ForwardVariantArgs(variantParams)});";
            EmitUnexpressibleDefaultVariant(writer, method, variantParams, keepCount, method.EntryPointName, invocation);
        }
        return writer.ToString();
    }

    private static string RenderNativeUnexpressibleDefaultInstanceMethod(BindingCompilation compilation, FunctionBinding method)
    {
        if (method.AccessModifier == BindingAccessModifier.Protected)
        {
            throw new NotSupportedException($"Protected member '{method.ManagedName}' does not support unexpressible defaults yet.");
        }

        var writer = new CodeWriter();
        foreach (var keepCount in GetEmittedVariantParameterIndices(compilation, method))
        {
            var variantParams = GetUnexpressibleVariantParams(method, keepCount);
            var forwardedArgs = ForwardVariantArgs(variantParams);
            var invocation = method.NativeDispatchRecordCppTypeName is null
                ? $"return std::forward<decltype(self)>(self)->{method.NativeName}({forwardedArgs});"
                : $$"""
                    auto* dispatch = dynamic_cast<{{method.NativeDispatchRecordCppTypeName}}*>(std::forward<decltype(self)>(self));
                    if (dispatch == nullptr)
                    {
                        throw std::runtime_error("SharpCpp failed to adjust the native handle for secondary-base dispatch.");
                    }
                    return dispatch->{{method.NativeName}}({{forwardedArgs}});
                    """;
            EmitUnexpressibleDefaultVariant(writer, method, variantParams, keepCount, method.EntryPointName, invocation, method.OwningRecordCppTypeName);
        }
        return writer.ToString();
    }

    private static string RenderNativeUnexpressibleDefaultCallbase(BindingCompilation compilation, RecordBinding record, VirtualMethodSlotBinding slot)
    {
        var method = slot.Method;
        var writer = new CodeWriter();
        var directorName = GetNativeDirectorClassName(record);
        var baseEntrypointName = GetNativeCallbaseEntrypointName(compilation, record, method);

        foreach (var keepCount in GetEmittedVariantParameterIndices(compilation, method))
        {
            var variantParams = GetUnexpressibleVariantParams(method, keepCount);
            var forwardedArgs = ForwardVariantArgs(variantParams);
            var invocation = $"return static_cast<{directorName}*>(std::forward<decltype(self)>(self))->{slot.NativeImplementationRecord.CppQualifiedName}::{method.NativeName}({forwardedArgs});";
            EmitUnexpressibleDefaultVariant(writer, method, variantParams, keepCount, baseEntrypointName, invocation, record.CppQualifiedName);
        }
        return writer.ToString();
    }

    private static string RenderNativeUnexpressibleDefaultConstructor(BindingCompilation compilation, FunctionBinding constructor, bool requiresDirector)
    {
        var writer = new CodeWriter();
        var constructedTypeName = requiresDirector
            ? GetNativeDirectorClassName((RecordBinding)constructor.Parent!)
            : constructor.OwningRecordCppTypeName;
        var namespacePreamble = BuildConstructorDefaultNamespacePreamble(compilation, constructor);

        foreach (var keepCount in GetEmittedVariantParameterIndices(compilation, constructor))
        {
            var variantParams = GetUnexpressibleVariantParams(constructor, keepCount);
            var invocation = string.IsNullOrWhiteSpace(namespacePreamble)
                ? $"return new {constructedTypeName}({BuildNativeVariantCallArguments(constructor, variantParams)});"
                : $"{namespacePreamble}\n        return new {constructedTypeName}({BuildNativeVariantCallArguments(constructor, variantParams)});";
            EmitUnexpressibleDefaultVariant(writer, constructor, variantParams, keepCount, constructor.EntryPointName, invocation);
        }
        return writer.ToString();
    }

    private static string BuildManagedDispatchArgument(BindingCompilation compilation, ParameterBinding parameter, string plannedArgument)
    {
        if (!parameter.HasMetadata(MetadataKeys.UnexpressibleDefault) || IsManagedReferenceDefaultParameter(parameter))
        {
            return plannedArgument;
        }

        var valueExpression = BuildManagedPublicToImportExpression(
            compilation,
            parameter.Type,
            $"{parameter.ManagedName}.Value");
        return parameter.Direction switch
        {
            BindingParameterDirection.Ref => $"ref {valueExpression}",
            BindingParameterDirection.Out => $"out {valueExpression}",
            _ => valueExpression
        };
    }

    private static string BuildManagedDispatchArguments(BindingCompilation compilation, FunctionBinding function, ManagedInvocationPlan plan, IReadOnlyList<string> leadingArguments, int keepCount)
    {
        var arguments = new List<string>(leadingArguments);
        for (var i = 0; i < keepCount; i++)
        {
            var parameter = function.Parameters[i];
            arguments.Add(BuildManagedDispatchArgument(compilation, parameter, plan.Arguments[i]));
        }

        return string.Join(", ", arguments);
    }

    private static string BuildUnexpressibleDefaultCondition(ParameterBinding parameter, bool provided)
    {
        if (IsManagedReferenceDefaultParameter(parameter))
        {
            return $"{parameter.ManagedName} is {(provided ? "not null" : "null")}";
        }

        return provided
            ? $"{parameter.ManagedName}.HasValue"
            : $"!{parameter.ManagedName}.HasValue";
    }

    private static bool IsManagedReferenceDefaultParameter(ParameterBinding parameter)
    {
        return parameter.Type.IsPointerType ||
            parameter.Type.IsCallback ||
            IsManagedReferenceTypeName(parameter.Type.ManagedTypeName);
    }

    private static bool IsNullableManagedReferenceDefaultParameter(ParameterBinding parameter)
    {
        if (parameter.Direction != BindingParameterDirection.In ||
            parameter.AbiKind != BindingParameterAbiKind.Value)
        {
            return false;
        }

        if (parameter.ManagedDefaultValueExpression == "null")
        {
            return IsManagedReferenceDefaultParameter(parameter);
        }

        return parameter.ManagedDefaultValueExpression == "default" &&
            IsManagedReferenceTypeName(parameter.Type.ManagedTypeName);
    }

    private static bool IsManagedReferenceTypeName(string managedTypeName)
    {
        return string.Equals(managedTypeName, "string", StringComparison.Ordinal) ||
            string.Equals(managedTypeName, "global::System.String", StringComparison.Ordinal);
    }

    private static bool IsInputHandlePointerArrayParameter(ParameterBinding parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        return !parameter.HasMetadata(MetadataKeys.InputPointerArray) &&
            parameter.Direction == BindingParameterDirection.In &&
            parameter.AbiKind == BindingParameterAbiKind.Pointer &&
            parameter.Type.Kind == BindingTypeKind.Handle;
    }

    private static bool IsInputRawPointerArrayParameter(ParameterBinding parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        return parameter.HasMetadata(MetadataKeys.InputPointerArray) &&
            parameter.Direction == BindingParameterDirection.In &&
            parameter.AbiKind == BindingParameterAbiKind.Pointer &&
            (parameter.Type.Kind is BindingTypeKind.Primitive or BindingTypeKind.Enum ||
                parameter.Type.Kind == BindingTypeKind.Handle &&
                string.IsNullOrEmpty(parameter.Type.RecordId) &&
                string.Equals(parameter.Type.ManagedTypeName, "global::System.IntPtr", StringComparison.Ordinal));
    }

    private static bool IsInputPointerArrayParameter(ParameterBinding parameter)
    {
        return IsInputHandlePointerArrayParameter(parameter) ||
            IsInputRawPointerArrayParameter(parameter);
    }

    private static bool RequiresArrayElementAccess(BindingCompilation compilation, RecordBinding record)
    {
        if (record.HasMetadata(MetadataKeys.ForwardDeclaration))
            return false;

        var functions = compilation.Members.OfType<FunctionBinding>()
            .Concat(compilation.Records.SelectMany(static candidate => candidate.Members.OfType<FunctionBinding>()));
        return functions.Any(function =>
            (!function.IsConstructor &&
             function.ReturnPointerDepth == 1 &&
             function.ReturnType.Kind == BindingTypeKind.Handle &&
             string.Equals(function.ReturnType.RecordId, record.SymbolId, StringComparison.Ordinal)) ||
            function.Parameters.Any(parameter => parameter.Direction == BindingParameterDirection.Out
                && parameter.AbiKind == BindingParameterAbiKind.Pointer
                && parameter.Type.Kind == BindingTypeKind.Handle
                && string.Equals(parameter.Type.RecordId, record.SymbolId, StringComparison.Ordinal)));
    }

    private static bool InheritsArrayElementAccess(BindingCompilation compilation, RecordBinding record)
    {
        if (string.IsNullOrWhiteSpace(record.ManagedBaseTypeName))
        {
            return false;
        }

        for (var current = record.GetPrimaryBaseRecord(compilation); current is not null;
             current = current.GetPrimaryBaseRecord(compilation))
        {
            if (RequiresArrayElementAccess(compilation, current))
                return true;
        }
        return false;
    }

    private static bool HidesInheritedManagedField(
        BindingCompilation compilation,
        RecordBinding record,
        FieldBinding field)
    {
        if (string.Equals(field.ManagedName, "GetType", StringComparison.Ordinal) ||
            IsNativeInterfaceWrapper(record) &&
            string.Equals(field.NativeName, "pVtbl", StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(record.ManagedBaseTypeName) ||
            string.IsNullOrWhiteSpace(record.ManagedBaseTypeName) &&
            string.Equals(field.ManagedName, "Close", StringComparison.Ordinal))
        {
            return true;
        }

        for (var current = record.GetPrimaryBaseRecord(compilation); current is not null;
             current = current.GetPrimaryBaseRecord(compilation))
        {
            if (current.HasOwnManagedMemberNamed(field.ManagedName))
                return true;
        }
        return false;
    }

    private static string GetPointerArrayAbiSpanName(ParameterBinding parameter)
    {
        return $"__abi_{parameter.ManagedName.TrimStart('@')}";
    }

    private static string GetPointerArrayPointerName(ParameterBinding parameter)
    {
        return $"{GetPointerArrayAbiSpanName(parameter)}_ptr";
    }

    private static string BuildUnexpressibleDefaultCondition(FunctionBinding function, IReadOnlyList<int> defaultIndices, int providedDefaultCount)
    {
        var parts = new List<string>(defaultIndices.Count);
        for (var i = 0; i < defaultIndices.Count; i++)
        {
            parts.Add(BuildUnexpressibleDefaultCondition(function.Parameters[defaultIndices[i]], provided: i < providedDefaultCount));
        }

        return string.Join(" && ", parts);
    }

    private static void WriteUndefaultableOverloads(
        BindingCompilation compilation,
        CodeWriter writer,
        FunctionBinding function,
        string importMethodName,
        IReadOnlyList<string> leadingArguments,
        string? declarationPrefix = null)
    {
        var firstU = GetFirstUndefaultableParamIndex(function);
        declarationPrefix ??= function.IsStatic
            ? $"{GetManagedVisibility(function.AccessModifier)} static"
            : GetManagedVisibility(function.AccessModifier);

        // Build common invocation plan for the full parameter set
        var fullPlan = BuildManagedInvocationPlan(compilation, function.Parameters);

        // =========================================================
        // Overload A: params before first undefaultable index
        // =========================================================
        if (ShouldEmitUndefaultablePrefixOverload(compilation, function))
        {
            var beforeParams = function.Parameters.Take(firstU).ToArray();

            // Check if before-params have any unexpressible defaults
            var beforeHasUnexpressible = beforeParams.Any(p => p.HasMetadata(MetadataKeys.UnexpressibleDefault));

            // Render public signature for overload A
            var aParamList = string.Join(", ", beforeParams.Select(p => RenderManagedPublicParameter(p, compilation)));

            writer.Write($$"""
                {{declarationPrefix}} {{GetManagedPublicReturnTypeName(function.ReturnType, compilation)}} {{function.ManagedName}}({{aParamList}})
                {
                """);

            if (firstU == 0)
            {
                // Overload A has no params — call __Args0 variant
                var variantImportName = GetUnexpressibleVariantImportName(importMethodName, 0);
                if (function.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void)
                {
                    writer.Write($$"""
                        __PInvoke.{{variantImportName}}({{string.Join(", ", leadingArguments)}});
                        """);
                }
                else
                {
                    var leading = leadingArguments.Count > 0 ? string.Join(", ", leadingArguments) + ", " : string.Empty;
                    writer.Write($$"""
                        var __result = __PInvoke.{{variantImportName}}({{leading.TrimEnd(',', ' ')}});
                        """);
                }
            }
            else if (beforeHasUnexpressible)
            {
                // Cascade for before-params with unexpressible defaults
                WriteUnexpressibleDefaultCascade(compilation, writer, function, importMethodName, leadingArguments, firstU);
                // Need to get past the closing brace
            }
            else
            {
                // Direct call with firstU params — use __Args{firstU} variant
                var variantImportName = GetUnexpressibleVariantImportName(importMethodName, firstU);
                var beforePlan = BuildManagedInvocationPlan(compilation, beforeParams);
                var beforeArgs = string.Join(", ", leadingArguments.Concat(beforePlan.Arguments));

                if (function.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void)
                {
                    writer.Write($$"""
                        __PInvoke.{{variantImportName}}({{beforeArgs}});
                        """);
                }
                else
                {
                    writer.Write($$"""
                        var __result = __PInvoke.{{variantImportName}}({{beforeArgs}});
                        """);
                }
            }

            // Handle return and constructor post-call
            if (function.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void || beforeHasUnexpressible)
            {
                writer.Write("""
                    }
                    """);
            }
            else
            {
                writer.Write($$"""
                    return {{BuildManagedHandleWrapIfNeeded(compilation, function.ReturnType, "__result", ReturnsOwnedHandle(function))}};
                    }
                    """);
            }

            writer.WriteLine();
        }

        // =========================================================
        // Overload B: all params, undefaultable ones required
        // =========================================================
        {
            // Render public parameter list: params before firstU are required,
            // firstU is required, params after firstU get normal defaults
            var bParamParts = new List<string>();
            for (var i = 0; i < function.Parameters.Count; i++)
            {
                var param = function.Parameters[i];
                if (i < firstU || param.HasMetadata(MetadataKeys.UndefaultableParam))
                {
                    // Required: no default, non-nullable
                    bParamParts.Add(RenderManagedPublicParameterRequired(param, compilation));
                }
                else
                {
                    // Normal default rules
                    bParamParts.Add(RenderManagedPublicParameter(param, compilation));
                }
            }
            var bParamList = string.Join(", ", bParamParts);

            writer.Write($$"""
                {{declarationPrefix}} {{GetManagedPublicReturnTypeName(function.ReturnType, compilation)}} {{function.ManagedName}}({{bParamList}})
                {
                """);

            // Emit pre-call statements
            foreach (var statement in fullPlan.PreCallStatements)
            {
                writer.Write(statement);
            }

            // Check if after-firstU params have unexpressible defaults
            var afterParams = function.Parameters.Skip(firstU).ToArray();
            var afterHasUnexpressible = afterParams.Any(static p =>
                p.HasMetadata(MetadataKeys.UnexpressibleDefault) &&
                !p.HasMetadata(MetadataKeys.UndefaultableParam));

            if (afterHasUnexpressible)
            {
                // Build a cascade for the after-params portion (indices >= firstU)
                // This mirrors the standard cascade but only for after-firstU params.
                // Before-firstU params are always provided (required, non-nullable).
                var afterDefaultIndices = new List<int>();
                for (var i = firstU; i < function.Parameters.Count; i++)
                {
                    if (function.Parameters[i].HasMetadata(MetadataKeys.UnexpressibleDefault) &&
                        !function.Parameters[i].HasMetadata(MetadataKeys.UndefaultableParam))
                    {
                        afterDefaultIndices.Add(i);
                    }
                }

                // Each branch handles its own dispatch + post-call + return
                for (var providedDefaultCount = afterDefaultIndices.Count; providedDefaultCount >= 0; providedDefaultCount--)
                {
                    var keepCount = providedDefaultCount < afterDefaultIndices.Count
                        ? afterDefaultIndices[providedDefaultCount]
                        : function.Parameters.Count;
                    WriteUndefaultableBranchB(compilation, writer, function, importMethodName, fullPlan,
                        leadingArguments, keepCount, afterDefaultIndices, providedDefaultCount);
                }

                // Fallback: should never reach here if cascade conditions are complete
                writer.Write(
                    """
                        throw new global::System.ArgumentException("SharpCpp optional arguments that depend on native defaults must be supplied as a trailing contiguous suffix.");
                        }
                    """);
            }
            else
            {
                // Direct call with all params
                var allArgs = string.Join(", ", leadingArguments.Concat(fullPlan.Arguments));

                if (function.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void)
                {
                    writer.Write($$"""
                        __PInvoke.{{importMethodName}}({{allArgs}});
                        """);
                }
                else
                {
                    writer.Write($$"""
                        var __result = __PInvoke.{{importMethodName}}({{allArgs}});
                        """);
                }

                // Post-call statements (only on non-cascade path)
                foreach (var statement in fullPlan.PostCallStatements)
                {
                    writer.Write(statement);
                }

                // Return / constructor finalize (only on non-cascade path)
                if (function.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void)
                {
                    writer.Write("""
                        }
                        """);
                }
                else
                {
                    writer.Write($$"""
                        return {{BuildManagedHandleWrapIfNeeded(compilation, function.ReturnType, "__result", ReturnsOwnedHandle(function))}};
                        }
                        """);
                }
            }
        }
    }

    private static void WriteUndefaultableBranchB(
        BindingCompilation compilation,
        CodeWriter writer,
        FunctionBinding function,
        string importMethodName,
        ManagedInvocationPlan plan,
        IReadOnlyList<string> leadingArguments,
        int keepCount,
        IReadOnlyList<int> defaultIndices,
        int? providedDefaultCount = null)
    {
        // For Overload B cascade branches, the before-firstU params are always present (non-nullable).
        // The cascade condition only tests the after-firstU params that have unexpressible defaults.
        if (providedDefaultCount.HasValue)
        {
            // Build condition for the unexpressible after-params
            var relevantIndices = defaultIndices.Where(i => i >= GetFirstUndefaultableParamIndex(function)).ToList();
            if (relevantIndices.Count == 0)
            {
                // No cascade needed
                return;
            }

            var condition = BuildUndefaultableBCondition(function, relevantIndices, providedDefaultCount.Value);
            writer.WriteLine($"if ({condition})");
            writer.WriteLine("{");
        }

        // Build dispatch arguments — for required params, use the plan argument directly;
        // for unexpressible default after-params, unwrap .Value
        var args = new List<string>(leadingArguments);
        for (var i = 0; i < keepCount; i++)
        {
            var param = function.Parameters[i];
            if (param.HasMetadata(MetadataKeys.UndefaultableParam) ||
                (i < GetFirstUndefaultableParamIndex(function) && !param.HasMetadata(MetadataKeys.UnexpressibleDefault)))
            {
                // Required param — use plan argument directly
                args.Add(plan.Arguments[i]);
            }
            else
            {
                // May have unexpressible default — use .Value unwrapping if needed
                args.Add(BuildManagedDispatchArgument(compilation, param, plan.Arguments[i]));
            }
        }
        var invokeArgs = string.Join(", ", args);
        var branchImportName = keepCount == function.Parameters.Count
            ? importMethodName
            : GetUnexpressibleVariantImportName(importMethodName, keepCount);

        WriteOmittedOutParameterAssignments(writer, function, keepCount);
        if (function.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void)
        {
            writer.Write($$"""
                __PInvoke.{{branchImportName}}({{invokeArgs}});
                """);

            foreach (var statement in plan.PostCallStatements)
            {
                writer.Write(statement);
            }

            // Prevent fallthrough to next cascade branch or throw
            if (providedDefaultCount.HasValue)
            {
                writer.WriteLine("return;");
            }
        }
        else
        {
            writer.Write($$"""
                var __result = __PInvoke.{{branchImportName}}({{invokeArgs}});
                """);

            foreach (var statement in plan.PostCallStatements)
            {
                writer.Write(statement);
            }

            writer.Write($$"""
                return {{BuildManagedHandleWrapIfNeeded(compilation, function.ReturnType, "__result", ReturnsOwnedHandle(function))}};
                """);
        }

        if (providedDefaultCount.HasValue)
        {
            writer.WriteLine("}");
        }
    }

    private static string BuildUndefaultableBCondition(FunctionBinding function, IReadOnlyList<int> defaultIndices, int providedDefaultCount)
    {
        var firstU = GetFirstUndefaultableParamIndex(function);
        var relevantIndices = defaultIndices.Where(i => i >= firstU).ToList();
        var parts = new List<string>(relevantIndices.Count);
        for (var i = 0; i < relevantIndices.Count; i++)
        {
            var param = function.Parameters[relevantIndices[i]];
            parts.Add(param.HasMetadata(MetadataKeys.UndefaultableParam)
                ? $"{param.ManagedName} is not null"
                : BuildUnexpressibleDefaultCondition(param, provided: i < providedDefaultCount));
        }

        return string.Join(" && ", parts);
    }

    private static string BuildManagedHandleWrapIfNeeded(BindingCompilation compilation, BindingType type, string variableName, bool returnsOwnedHandle)
    {
        if (type.Kind != BindingTypeKind.Handle)
        {
            return variableName;
        }

        return BuildManagedHandleReturnResultExpression(compilation, type, variableName, returnsOwnedHandle);
    }

    private static void WriteUnexpressibleDefaultCascade(BindingCompilation compilation, CodeWriter writer, FunctionBinding function, string importMethodName, IReadOnlyList<string> leadingArguments, int? maxParameterCount = null)
    {
        var parameterCount = maxParameterCount ?? function.Parameters.Count;
        var defaultIndices = GetNativeDefaultDispatchParameterIndices(function)
            .Where(index => index < parameterCount)
            .ToArray();
        var invocationPlan = BuildManagedInvocationPlan(compilation, GetUnexpressibleVariantParams(function, parameterCount));
        var fullImportName = parameterCount == function.Parameters.Count
            ? importMethodName
            : GetUnexpressibleVariantImportName(importMethodName, parameterCount);

        foreach (var statement in invocationPlan.PreCallStatements)
        {
            writer.Write(statement);
        }

        WriteUnexpressibleDefaultBranch(compilation, writer, function, fullImportName, invocationPlan, leadingArguments, parameterCount, defaultIndices, defaultIndices.Length);

        for (var providedDefaultCount = defaultIndices.Length - 1; providedDefaultCount >= 0; providedDefaultCount--)
        {
            var keepCount = defaultIndices[providedDefaultCount];
            var variantPlan = BuildManagedInvocationPlan(compilation, GetUnexpressibleVariantParams(function, keepCount));
            var variantImportName = GetUnexpressibleVariantImportName(importMethodName, keepCount);
            WriteUnexpressibleDefaultBranch(compilation, writer, function, variantImportName, variantPlan, leadingArguments, keepCount, defaultIndices, providedDefaultCount);
        }

        writer.Write(
            """
                throw new global::System.ArgumentException("SharpCpp optional arguments that depend on native defaults must be supplied as a trailing contiguous suffix.");
            """);
    }

    private static void WriteUnexpressibleDefaultBranch(BindingCompilation compilation, CodeWriter writer, FunctionBinding function, string importName,
        ManagedInvocationPlan plan, IReadOnlyList<string> leadingArguments, int keepCount, IReadOnlyList<int> defaultIndices, int providedDefaultCount)
    {
        writer.WriteLine($"if ({BuildUnexpressibleDefaultCondition(function, defaultIndices, providedDefaultCount)})");
        writer.WriteLine("{");
        WriteDispatchCall(writer, compilation, function, importName, plan, leadingArguments, keepCount);
        if (function.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void)
        {
            writer.WriteLine("return;");
        }

        writer.WriteLine("}");
        writer.WriteLine();
    }

    private static void WriteDispatchCall(CodeWriter writer, BindingCompilation compilation, FunctionBinding function, string importName,
        ManagedInvocationPlan plan, IReadOnlyList<string> leadingArguments, int keepCount)
    {
        var resultVar = "__result";
        var args = BuildManagedDispatchArguments(compilation, function, plan, leadingArguments, keepCount);

        WriteOmittedOutParameterAssignments(writer, function, keepCount);
        if (function.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void)
        {
            writer.Write($$"""
                __PInvoke.{{importName}}({{args}});
                """);

            foreach (var statement in plan.PostCallStatements)
                writer.Write(statement);
        }
        else if (function.ReturnType.Kind == BindingTypeKind.Handle)
        {
            writer.Write($$"""
                var {{resultVar}} = __PInvoke.{{importName}}({{args}});
                """);

            foreach (var statement in plan.PostCallStatements)
                writer.Write(statement);

            writer.Write($$"""
                return {{BuildManagedHandleReturnResultExpression(compilation, function.ReturnType, resultVar, ReturnsOwnedHandle(function))}};
                """);
        }
        else
        {
            writer.Write($$"""
                var {{resultVar}} = __PInvoke.{{importName}}({{args}});
                """);

            foreach (var statement in plan.PostCallStatements)
                writer.Write(statement);

            writer.Write($$"""
                return {{BuildManagedImportToPublicExpression(compilation, function.ReturnType, resultVar)}};
                """);
        }
    }

    private static void WriteUnexpressibleDefaultConstructorCascade(BindingCompilation compilation, CodeWriter writer, FunctionBinding function, string importMethodName, int? maxParameterCount = null)
    {
        var parameterCount = maxParameterCount ?? function.Parameters.Count;
        var defaultIndices = GetNativeDefaultDispatchParameterIndices(function)
            .Where(index => index < parameterCount)
            .ToArray();
        var invocationPlan = BuildManagedInvocationPlan(compilation, GetUnexpressibleVariantParams(function, parameterCount));
        var fullImportName = parameterCount == function.Parameters.Count
            ? importMethodName
            : GetUnexpressibleVariantImportName(importMethodName, parameterCount);

        foreach (var statement in invocationPlan.PreCallStatements)
        {
            writer.Write(statement);
        }

        WriteUnexpressibleDefaultConstructorBranch(compilation, writer, function, fullImportName, invocationPlan, parameterCount, defaultIndices, defaultIndices.Length);

        for (var providedDefaultCount = defaultIndices.Length - 1; providedDefaultCount >= 0; providedDefaultCount--)
        {
            var keepCount = defaultIndices[providedDefaultCount];
            var variantPlan = BuildManagedInvocationPlan(compilation, GetUnexpressibleVariantParams(function, keepCount));
            var variantImportName = GetUnexpressibleVariantImportName(importMethodName, keepCount);
            WriteUnexpressibleDefaultConstructorBranch(compilation, writer, function, variantImportName, variantPlan, keepCount, defaultIndices, providedDefaultCount);
        }

        writer.Write(
            """
                throw new global::System.ArgumentException("SharpCpp optional arguments that depend on native defaults must be supplied as a trailing contiguous suffix.");
            """);
    }

    private static void WriteUndefaultableConstructorCascade_Filtered(
        BindingCompilation compilation,
        CodeWriter writer,
        FunctionBinding function,
        string importMethodName,
        int firstU,
        IReadOnlyList<int> afterDefaultIndices)
    {
        if (afterDefaultIndices.Count == 0)
        {
            return;
        }

        // Use only the full-params import variant for Overload B (before-firstU params
        // are always present, so we never call __ArgsN for before-firstU positions).
        var invocationPlan = BuildManagedInvocationPlan(compilation, function.Parameters);

        foreach (var statement in invocationPlan.PreCallStatements)
        {
            writer.Write(statement);
        }

        // Branch: all after-defaults provided → full dispatch
        var conditionAll = BuildUndefaultableBCondition(function, afterDefaultIndices, afterDefaultIndices.Count);
        writer.WriteLine($"if ({conditionAll})");
        writer.WriteLine("{");
        WriteConstructorDispatchCall(compilation, writer, function, importMethodName, invocationPlan, function.Parameters.Count);
        writer.WriteLine("}");
        writer.WriteLine();

        // Cascade branches: progressively fewer after-defaults provided
        // Each uses a __ArgsN variant that keeps only up to the relevant after-default index
        for (var providedDefaultCount = afterDefaultIndices.Count - 1; providedDefaultCount >= 0; providedDefaultCount--)
        {
            var keepCount = afterDefaultIndices[providedDefaultCount];
            var condition = BuildUndefaultableBCondition(function, afterDefaultIndices, providedDefaultCount);
            writer.WriteLine($"if ({condition})");
            writer.WriteLine("{");
            var variantImportName = GetUnexpressibleVariantImportName(importMethodName, keepCount);
            var variantPlan = BuildManagedInvocationPlan(compilation, GetUnexpressibleVariantParams(function, keepCount));
            WriteConstructorDispatchCall(compilation, writer, function, variantImportName, variantPlan, keepCount);
            writer.WriteLine("}");
            writer.WriteLine();
        }

        writer.Write(
            """
                throw new global::System.ArgumentException("SharpCpp optional arguments that depend on native defaults must be supplied as a trailing contiguous suffix.");
            """);
    }

    private static void WriteUnexpressibleDefaultConstructorBranch(BindingCompilation compilation, CodeWriter writer, FunctionBinding function, string importName,
        ManagedInvocationPlan plan, int keepCount, IReadOnlyList<int> defaultIndices, int providedDefaultCount)
    {
        writer.WriteLine($"if ({BuildUnexpressibleDefaultCondition(function, defaultIndices, providedDefaultCount)})");
        writer.WriteLine("{");
        WriteConstructorDispatchCall(compilation, writer, function, importName, plan, keepCount);
        writer.WriteLine("}");
        writer.WriteLine();
    }

    private static void WriteConstructorDispatchCall(BindingCompilation compilation, CodeWriter writer, FunctionBinding function, string importName,
        ManagedInvocationPlan plan, int keepCount)
    {
        var args = BuildManagedDispatchArguments(compilation, function, plan, [], keepCount);

        WriteOmittedOutParameterAssignments(writer, function, keepCount);
        writer.Write($$"""
            var __handle = __PInvoke.{{importName}}({{args}});
            """);

        foreach (var statement in plan.PostCallStatements)
        {
            writer.Write(statement);
        }

        writer.Write(
            """
            return __handle;
            """);
    }

    private static void WriteOmittedOutParameterAssignments(
        CodeWriter writer,
        FunctionBinding function,
        int keepCount)
    {
        foreach (var parameter in function.Parameters.Skip(keepCount)
                     .Where(static parameter => parameter.Direction == BindingParameterDirection.Out))
        {
            writer.WriteLine($"{parameter.ManagedName} = default!;");
        }
    }

}
