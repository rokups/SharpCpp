using ClangSharp.Interop;
using ClangSharp;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

using SharpCpp.Metadata.Import;
using SharpCpp.Model.Ast;
using SharpCpp.Model.Diagnostics;

using ManagedTypeOverride = SharpCpp.Parsing.MarshallerConfigExtractor.ManagedTypeOverride;

namespace SharpCpp.Parsing;

public sealed partial class ClangSharpHeaderParser
{
    private sealed record OwnedAliasInfo(string CppQualifiedName, string NativeName);

    private sealed class AstGenericParameterScope : Dictionary<string, BindingType>
    {
        private readonly Dictionary<(uint Depth, uint Index), BindingType> _parametersByAstPosition = new();

        public AstGenericParameterScope(IReadOnlyDictionary<string, BindingType>? parent = null)
            : base(parent ?? EmptyGenericParameters, StringComparer.Ordinal)
        {
            if (parent is AstGenericParameterScope parentScope)
            {
                foreach (var (position, type) in parentScope._parametersByAstPosition)
                {
                    _parametersByAstPosition[position] = type;
                }
            }
        }

        public void Add(TemplateTypeParmDecl parameter)
        {
            var name = parameter.Name ?? parameter.Spelling.ToString();
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            var type = BindingType.CreateGenericParameter(name);
            this[name] = type;
            _parametersByAstPosition[(checked((uint)parameter.Depth), checked((uint)parameter.Index))] = type;
        }

        public bool TryGet(TemplateTypeParmType parameter, out BindingType type)
            => _parametersByAstPosition.TryGetValue((parameter.Depth, parameter.Index), out type!);

        public bool TryGet(CXType parameter, out BindingType type)
            => _parametersByAstPosition.TryGetValue(
                (checked((uint)parameter.Declaration.TemplateTypeParmDepth), checked((uint)parameter.Declaration.TemplateTypeParmIndex)),
                out type!);
    }

    private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const", "continue",
        "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern", "false", "finally",
        "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
        "long", "namespace", "new", "null", "object", "operator", "out", "override", "params", "private", "protected", "public",
        "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch",
        "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void",
        "volatile", "while"
    };

    private static readonly IReadOnlyDictionary<string, BindingType> EmptyGenericParameters =
        new Dictionary<string, BindingType>(StringComparer.Ordinal);
    private static readonly object ParseLock = new();

    private static readonly Dictionary<string, string> s_typeAliases = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> s_namespaceAliases = new(StringComparer.Ordinal);
    // Reverse lookup: underlying canonical type -> list of typedef names.
    private static readonly Dictionary<string, List<string>> s_typeAliasOriginNames = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, OwnedAliasInfo> s_ownedManagedAliases = new(StringComparer.Ordinal);

    private static bool HasSharpCppAttribute(string sourceText, string attributeName)
        => SharpCppAttributeScanner.Scan(sourceText)
            .Any(match => string.Equals(match.Name, attributeName, StringComparison.Ordinal));

    private static SharpCppAttributeScanner.AttributeMatch? FindSharpCppAttribute(string sourceText, string attributeName)
        => SharpCppAttributeScanner.Scan(sourceText)
            .FirstOrDefault(match => string.Equals(match.Name, attributeName, StringComparison.Ordinal));

    // Note: ManagedTypeMarshallerExpression, ReturnMarshallerExpression, and the
    // attribute-body regex patterns (PublicTypeExpression, UndefaultableExpression,
    // MarshallerTypeInAttributeExpression, ManagedMarshallerInAttributeExpression)
    // have been removed. They were replaced by MarshallerConfigExtractor which uses
    // ClangSharp AST walking to find Marshaller/ReturnMarsharker declarations and
    // extracts attribute body data from cursor source text instead of whole-file regex.

    [GeneratedRegex(@"""(?<value>[^""\\]*(?:\\.[^""\\]*)*)""", RegexOptions.CultureInvariant)]
    private static partial Regex InstantiateArgumentExpression();

    public BindingCompilation Parse(ClangSharpParseRequest request)
    {
        lock (ParseLock)
        {
            return ParseCore(request);
        }
    }

    private static BindingCompilation ParseCore(ClangSharpParseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProjectName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ManagedNamespace);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ManagedClassName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.NativeLibraryName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.HeaderFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.HeaderIncludePath);

        var headerFilePath = Path.GetFullPath(request.HeaderFilePath);
        if (!File.Exists(headerFilePath))
        {
            throw new FileNotFoundException($"Header file '{headerFilePath}' was not found.", headerFilePath);
        }

        var ownedFileRoots = NormalizeOwnedFileRoots(headerFilePath, request.OwnedFileRoots);
        var ownedFileRootIncludePrefixes = NormalizeOwnedFileRootIncludePrefixes(request.OwnedFileRootIncludePrefixes);
        var annotationFiles = EnumerateOwnedHeaderFiles(headerFilePath, ownedFileRoots);
        var managedTypeOverrides = MarshallerConfigExtractor.ExtractManagedTypeOverrides(
            request.MarshallerConfigurationFiles,
            request.MarshallerClangArguments ?? request.ClangArguments);

        // Annotation scans are per parse: headers may change between parses in one process.
        s_ignoreAnnotations.Clear();
        s_renameAnnotations.Clear();
        s_instantiateAnnotations.Clear();
        PreScanIgnoreAnnotations(annotationFiles);
        PreScanRenameAnnotations(annotationFiles);
        PreScanInstantiateAnnotations(annotationFiles, managedTypeOverrides);

        using var index = CXIndex.Create();

        // NOTE: No `using` on translationUnit — TranslationUnit.GetOrCreate() below
        // takes ownership of the native handle. Disposing the interop struct would
        // double-free: the managed TU's finalizer also calls clang_disposeTranslationUnit.
        var translationUnit = CXTranslationUnit.Parse(
            index,
            headerFilePath,
            request.ClangArguments.ToArray(),
            Array.Empty<CXUnsavedFile>(),
            CXTranslationUnit_Flags.CXTranslationUnit_SkipFunctionBodies);

        ValidateDiagnostics(translationUnit, headerFilePath);

        var compilation = new BindingCompilation(
            request.ProjectName,
            request.ManagedNamespace,
            request.ManagedClassName,
            request.NativeLibraryName,
            new[] { request.HeaderIncludePath });
        compilation.EntryPointPrefix = request.EntryPointPrefix;
        var recordsByName = new Dictionary<string, RecordBinding>(StringComparer.Ordinal);
        var enumsByName = new Dictionary<string, EnumBinding>(StringComparer.Ordinal);
        request.ImportedBindingMetadata?.Seed(compilation, recordsByName, enumsByName);

        var managedTranslationUnit = TranslationUnit.GetOrCreate(translationUnit);
        BuildTypeAliasIndex(managedTranslationUnit.TranslationUnitDecl);
        BuildOwnedManagedAliasIndex(managedTranslationUnit.TranslationUnitDecl, ownedFileRoots);
        foreach (var child in managedTranslationUnit.TranslationUnitDecl.Decls)
        {
            if (!IsOwnedDecl(child, ownedFileRoots))
            {
                continue;
            }

            switch (child)
            {
                case NamespaceDecl nsChild:
                    VisitNamespace(nsChild, compilation, recordsByName, enumsByName, managedTypeOverrides, request.EntryPointPrefix, ownedFileRoots, ownedFileRootIncludePrefixes, request.HeaderIncludePath, request.DirectorRecordCppTypeNames);
                    break;

                case LinkageSpecDecl linkageSpecDecl:
                    VisitLinkageSpec(linkageSpecDecl, compilation, recordsByName, enumsByName, managedTypeOverrides, request.EntryPointPrefix, ownedFileRoots, ownedFileRootIncludePrefixes, request.HeaderIncludePath, request.DirectorRecordCppTypeNames);
                    break;

                case ClassTemplateSpecializationDecl:
                    break;
                case RecordDecl recordDecl:
                    RegisterRecord(recordDecl, compilation, recordsByName, enumsByName, managedTypeOverrides, request.EntryPointPrefix,
                        ResolveDeclIncludePath(recordDecl, ownedFileRoots, ownedFileRootIncludePrefixes, request.HeaderIncludePath), request.DirectorRecordCppTypeNames);
                    break;

                case ClassTemplateDecl classTemplateDecl:
                    RegisterTemplateRecord(classTemplateDecl, compilation, recordsByName, enumsByName, managedTypeOverrides, request.EntryPointPrefix,
                        ResolveDeclIncludePath(classTemplateDecl, ownedFileRoots, ownedFileRootIncludePrefixes, request.HeaderIncludePath), request.DirectorRecordCppTypeNames);
                    break;

                case EnumDecl enumChildDecl:
                    if (!string.IsNullOrEmpty(enumChildDecl.Name) &&
                        !GetCppQualifiedName(enumChildDecl).Contains("(unnamed enum", StringComparison.Ordinal))
                        RegisterEnum(enumChildDecl, compilation, enumsByName,
                            ResolveDeclIncludePath(child, ownedFileRoots, ownedFileRootIncludePrefixes, request.HeaderIncludePath));
                    break;

                case FunctionTemplateDecl funcTemplateDecl:
                    {
                        // Skip out-of-line template member definitions — the class-body
                        // declaration is authoritative for access and static/virtual status.
                        if (funcTemplateDecl.TemplatedDecl is CXXMethodDecl)
                            break;
                        var function = CreateTemplateFunction(funcTemplateDecl, request.EntryPointPrefix, recordsByName, enumsByName, managedTypeOverrides);
                        if (function is not null)
                        {
                            CheckAndApplyIgnoreAttribute(funcTemplateDecl, function);
                            TryApplyRename(funcTemplateDecl, function);
                            ApplyDeclarationFacts(funcTemplateDecl, function);
                            ApplyCallableDefinitionMetadata(funcTemplateDecl, function);
                            ApplyDeletedCallableIgnore(funcTemplateDecl.TemplatedDecl, function);
                            function.HeaderIncludePath = ResolveDeclIncludePath(child, ownedFileRoots, ownedFileRootIncludePrefixes, request.HeaderIncludePath);
                            compilation.Members.Add(function);
                        }
                        break;
                    }

                case FunctionDecl functionDecl:
                    {
                        // Out-of-line member definitions (CXXMethodDecl) at translation-unit
                        // scope belong to a record — the class-body declaration (inside
                        // RecordDecl.Decls) is the authoritative source for access and
                        // virtual/static status. Skip to avoid generating invalid thunks
                        // for non-public members.
                        if (functionDecl is CXXMethodDecl)
                            break;
                        var function = CreateFunction(functionDecl, request.EntryPointPrefix, recordsByName, enumsByName, managedTypeOverrides, EmptyGenericParameters);
                        CheckAndApplyIgnoreAttribute(functionDecl, function);
                        TryApplyRename(functionDecl, function);
                        ApplyDeclarationFacts(functionDecl, function);
                        ApplyCallableDefinitionMetadata(functionDecl, function);
                        ApplyDeletedCallableIgnore(functionDecl, function);
                        function.HeaderIncludePath = ResolveDeclIncludePath(child, ownedFileRoots, ownedFileRootIncludePrefixes, request.HeaderIncludePath);
                        compilation.Members.Add(function);
                        break;
                    }
            }
        }

        foreach (var record in recordsByName.Values.Distinct())
        {
            if (!compilation.Members.Contains(record))
            {
                compilation.Members.Add(record);
            }
        }

        CollectManagedTypeAliases(
            compilation,
            managedTranslationUnit.TranslationUnitDecl,
            ownedFileRoots,
            ownedFileRootIncludePrefixes,
            request.HeaderIncludePath,
            recordsByName,
            enumsByName,
            managedTypeOverrides,
            request.EntryPointPrefix);

        return compilation;
    }

    private static void VisitLinkageSpec(
        LinkageSpecDecl linkageSpecDecl,
        BindingCompilation compilation,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        string entryPointPrefix,
        IReadOnlyList<string> ownedFileRoots,
        IReadOnlyDictionary<string, string> ownedFileRootIncludePrefixes,
        string? headerIncludePath,
        IReadOnlySet<string>? directorRecordCppTypeNames)
    {
        foreach (var child in linkageSpecDecl.Decls)
        {
            if (!IsOwnedDecl(child, ownedFileRoots))
            {
                continue;
            }

            switch (child)
            {
                case NamespaceDecl nsChild:
                    VisitNamespace(nsChild, compilation, recordsByName, enumsByName, managedTypeOverrides, entryPointPrefix, ownedFileRoots, ownedFileRootIncludePrefixes, headerIncludePath, directorRecordCppTypeNames);
                    break;

                case LinkageSpecDecl nestedLinkageSpecDecl:
                    VisitLinkageSpec(nestedLinkageSpecDecl, compilation, recordsByName, enumsByName, managedTypeOverrides, entryPointPrefix, ownedFileRoots, ownedFileRootIncludePrefixes, headerIncludePath, directorRecordCppTypeNames);
                    break;

                case ClassTemplateSpecializationDecl:
                    break;
                case RecordDecl recordDecl:
                    RegisterRecord(recordDecl, compilation, recordsByName, enumsByName, managedTypeOverrides, entryPointPrefix,
                        ResolveDeclIncludePath(recordDecl, ownedFileRoots, ownedFileRootIncludePrefixes, headerIncludePath ?? string.Empty), directorRecordCppTypeNames);
                    break;

                case ClassTemplateDecl classTemplateDecl:
                    RegisterTemplateRecord(classTemplateDecl, compilation, recordsByName, enumsByName, managedTypeOverrides, entryPointPrefix,
                        ResolveDeclIncludePath(classTemplateDecl, ownedFileRoots, ownedFileRootIncludePrefixes, headerIncludePath ?? string.Empty), directorRecordCppTypeNames);
                    break;

                case EnumDecl enumChildDecl:
                    if (!string.IsNullOrEmpty(enumChildDecl.Name) &&
                        !GetCppQualifiedName(enumChildDecl).Contains("(unnamed enum", StringComparison.Ordinal))
                        RegisterEnum(enumChildDecl, compilation, enumsByName,
                            ResolveDeclIncludePath(child, ownedFileRoots, ownedFileRootIncludePrefixes, headerIncludePath ?? string.Empty));
                    break;

                case FunctionTemplateDecl funcTemplateDecl:
                    {
                        if (funcTemplateDecl.TemplatedDecl is CXXMethodDecl)
                            break;
                        var function = CreateTemplateFunction(funcTemplateDecl, entryPointPrefix, recordsByName, enumsByName, managedTypeOverrides);
                        if (function is not null)
                        {
                            CheckAndApplyIgnoreAttribute(funcTemplateDecl, function);
                            TryApplyRename(funcTemplateDecl, function);
                            ApplyDeclarationFacts(funcTemplateDecl, function);
                            ApplyCallableDefinitionMetadata(funcTemplateDecl, function);
                            ApplyDeletedCallableIgnore(funcTemplateDecl.TemplatedDecl, function);
                            function.HeaderIncludePath = ResolveDeclIncludePath(child, ownedFileRoots, ownedFileRootIncludePrefixes, headerIncludePath ?? string.Empty);
                            compilation.Members.Add(function);
                        }
                        break;
                    }

                case FunctionDecl functionDecl:
                    {
                        if (functionDecl is CXXMethodDecl)
                            break;
                        var function = CreateFunction(functionDecl, entryPointPrefix, recordsByName, enumsByName, managedTypeOverrides, EmptyGenericParameters);
                        CheckAndApplyIgnoreAttribute(functionDecl, function);
                        TryApplyRename(functionDecl, function);
                        ApplyDeclarationFacts(functionDecl, function);
                        ApplyCallableDefinitionMetadata(functionDecl, function);
                        ApplyDeletedCallableIgnore(functionDecl, function);
                        function.HeaderIncludePath = ResolveDeclIncludePath(child, ownedFileRoots, ownedFileRootIncludePrefixes, headerIncludePath ?? string.Empty);
                        compilation.Members.Add(function);
                        break;
                    }
            }
        }
    }

    private static void ValidateDiagnostics(CXTranslationUnit translationUnit, string headerFilePath)
    {
        var errors = new List<string>();

        for (uint index = 0; index < translationUnit.NumDiagnostics; index++)
        {
            using var diagnostic = translationUnit.GetDiagnostic(index);
            if (diagnostic.Severity != CXDiagnosticSeverity.CXDiagnostic_Error &&
                diagnostic.Severity != CXDiagnosticSeverity.CXDiagnostic_Fatal)
            {
                continue;
            }

            errors.Add(diagnostic.Format(CXDiagnostic.DefaultDisplayOptions).ToString());
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Clang failed to parse '{headerFilePath}':{Environment.NewLine}{string.Join(Environment.NewLine, errors)}");
        }
    }

    private static IReadOnlyList<string> NormalizeOwnedFileRoots(string headerFilePath, IReadOnlyList<string>? ownedFileRoots)
    {
        var normalizedRoots = new List<string> { Path.GetFullPath(headerFilePath) };
        if (ownedFileRoots is not null)
        {
            foreach (var root in ownedFileRoots)
            {
                if (string.IsNullOrWhiteSpace(root))
                {
                    continue;
                }

                normalizedRoots.Add(Path.GetFullPath(root));
            }
        }

        return normalizedRoots.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyDictionary<string, string> NormalizeOwnedFileRootIncludePrefixes(IReadOnlyDictionary<string, string>? includePrefixes)
    {
        if (includePrefixes is null || includePrefixes.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (root, prefix) in includePrefixes)
        {
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(prefix))
            {
                continue;
            }

            normalized[Path.GetFullPath(root)] = prefix.Replace('\\', '/').Trim('/');
        }

        return normalized;
    }

    private static IReadOnlyList<string> EnumerateOwnedHeaderFiles(string headerFilePath, IReadOnlyList<string> ownedFileRoots)
    {
        var files = new HashSet<string>(StringComparer.Ordinal)
        {
            Path.GetFullPath(headerFilePath)
        };

        foreach (var root in ownedFileRoots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(root, "*.h", SearchOption.AllDirectories))
            {
                files.Add(Path.GetFullPath(file));
            }

            foreach (var file in Directory.EnumerateFiles(root, "*.hpp", SearchOption.AllDirectories))
            {
                files.Add(Path.GetFullPath(file));
            }
        }

        return files.OrderBy(static path => path, StringComparer.Ordinal).ToArray();
    }

    // ─── Decl-based helpers (ClangSharp managed API) ────────────────────────────

    private static string GetCppQualifiedName(Decl decl)
    {
        var names = new List<string>();
        var current = decl.SemanticParentCursor;

        while (current is NamespaceDecl or RecordDecl)
        {
            var parentName = current is NamespaceDecl namespaceDecl &&
                clang.Cursor_isInlineNamespace(namespaceDecl.Handle) != 0
                ? string.Empty
                : current is NamedDecl named
                    ? named.Name ?? named.Spelling.ToString()
                    : string.Empty;
            if (!string.IsNullOrWhiteSpace(parentName))
            {
                names.Add(parentName);
            }
            current = current.SemanticParentCursor;
        }

        var ownName = decl is NamedDecl declNamed ? declNamed.Name ?? declNamed.Spelling.ToString() : decl.Spelling.ToString();
        if (names.Count == 0)
        {
            return ownName;
        }

        names.Reverse();
        names.Add(ownName);
        return string.Join("::", names);
    }

    private static bool IsOwnedDecl(Decl decl, IReadOnlyList<string> ownedFileRoots)
    {
        decl.Location.GetFileLocation(out var file, out _, out _, out _);
        var location = file.Name.CString;
        if (string.IsNullOrWhiteSpace(location))
        {
            return false;
        }

        var filePath = Path.GetFullPath(location);
        foreach (var ownedFileRoot in ownedFileRoots)
        {
            if (File.Exists(ownedFileRoot) && string.Equals(filePath, ownedFileRoot, StringComparison.Ordinal))
            {
                return true;
            }

            if (Directory.Exists(ownedFileRoot) &&
                filePath.StartsWith(ownedFileRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string ResolveDeclIncludePath(
        Decl decl,
        IReadOnlyList<string> ownedFileRoots,
        IReadOnlyDictionary<string, string> ownedFileRootIncludePrefixes,
        string fallbackHeaderIncludePath)
    {
        decl.Location.GetFileLocation(out var file, out _, out _, out _);
        var location = file.Name.CString;
        if (string.IsNullOrWhiteSpace(location))
        {
            return fallbackHeaderIncludePath;
        }

        var filePath = Path.GetFullPath(location);
        foreach (var ownedFileRoot in ownedFileRoots)
        {
            if (!Directory.Exists(ownedFileRoot) ||
                !filePath.StartsWith(ownedFileRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(ownedFileRoot, filePath).Replace('\\', '/');
            return ownedFileRootIncludePrefixes.TryGetValue(ownedFileRoot, out var includePrefix)
                ? $"{includePrefix}/{relativePath}"
                : relativePath;
        }

        return fallbackHeaderIncludePath;
    }

    private static bool IsForcedDirectorRecord(string cppQualifiedName, IReadOnlySet<string>? directorRecordCppTypeNames)
    {
        return !string.IsNullOrWhiteSpace(cppQualifiedName) &&
            directorRecordCppTypeNames?.Contains(cppQualifiedName) == true;
    }

    private static void RegisterRecord(
        RecordDecl recordDecl,
        BindingCompilation compilation,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        string entryPointPrefix,
        string? headerIncludePath,
        IReadOnlySet<string>? directorRecordCppTypeNames)
    {
        // If this record is the templated declaration of a class template,
        // delegate to RegisterTemplateRecord for proper generic parameter
        // extraction (RecordDecl.DescribedClassTemplate fills this role).
        if (recordDecl is CXXRecordDecl cxx && cxx.DescribedClassTemplate is { } classTemplate)
        {
            RegisterTemplateRecord(classTemplate, compilation, recordsByName, enumsByName, managedTypeOverrides, entryPointPrefix, headerIncludePath, directorRecordCppTypeNames);
            return;
        }

        // A lambda in a default argument declares its closure type in the enclosing scope.
        if (recordDecl is CXXRecordDecl { LambdaCallOperator: not null })
        {
            return;
        }

        var nativeName = recordDecl.Spelling;
        if (string.IsNullOrWhiteSpace(nativeName))
        {
            return;
        }

        var cppQualifiedName = GetCppQualifiedName(recordDecl);
        var sourceText = GetSourceText(recordDecl.Handle.TranslationUnit, recordDecl.Extent);
        var braceIndex = sourceText.IndexOf('{');
        var declarationText = braceIndex >= 0
            ? sourceText[..braceIndex]
            : sourceText;
        var isForwardDeclaration = braceIndex < 0 && !recordDecl.Decls.Any();

        // Look up ghost records under both the unqualified spelling and the fully
        // qualified name — placeholders created by MapType may be keyed by either.
        var ghostRecord = FindExistingRecordForDeclaration(recordsByName, nativeName, cppQualifiedName);

        if (ghostRecord?.GetMetadata<bool>(MetadataKeys.PlaceholderRecord) == true)
        {
            ghostRecord.Detach();
            RemoveRecordKeys(recordsByName, ghostRecord);
        }
        else if (ghostRecord is not null &&
                 ghostRecord.GetMetadata<bool>(MetadataKeys.ForwardDeclaration) &&
                 !isForwardDeclaration)
        {
            ghostRecord.Detach();
            RemoveRecordKeys(recordsByName, ghostRecord);
        }
        else if (ghostRecord is not null)
        {
            return;
        }
        var cxxRecord = recordDecl as CXXRecordDecl;
        var record = new RecordBinding(CreateRecordSymbolId(cppQualifiedName), nativeName, ToPascalCase(GetManagedTypeHead(nativeName)), cppQualifiedName)
        {
            IsAbstract = cxxRecord?.IsAbstract ?? false,
            IsDirector = HasSharpCppAttribute(declarationText, "director") || IsForcedDirectorRecord(cppQualifiedName, directorRecordCppTypeNames),
            HasCopyConstructor = recordDecl.Handle.HasUserDeclaredCopyConstructor ||
                ContainsCopyConstructorDeclaration(sourceText, nativeName) ||
                ContainsCopyConstructorDeclaration(GetDeclFileText(recordDecl), nativeName),
            IsNonCopyable = cxxRecord is not null && IsNonCopyableRecord(cxxRecord, new HashSet<CXXRecordDecl>())
        };
        if (HasSharpCppAttribute(declarationText, "native_interface"))
        {
            record.SetMetadata(MetadataKeys.NativeInterfaceWrapper, true);
        }
        record.SetMetadata(MetadataKeys.DeclarationDisplayText, GetDeclarationDisplayText(recordDecl));
        if (isForwardDeclaration)
        {
            record.SetMetadata(MetadataKeys.ForwardDeclaration, true);
            if (IsOpaqueNativeHandleDeclaration(recordDecl))
            {
                record.SetMetadata(MetadataKeys.OpaqueNativeHandle, true);
                record.SetMetadata(MetadataKeys.NonPublicDestructor, true);
            }
        }
        CheckAndApplyIgnoreAttribute(recordDecl, record);
        TryApplyRename(recordDecl, record);
        ApplyDeclarationFacts(recordDecl, record);
        IndexRecordKeys(recordsByName, record);
        record.HeaderIncludePath = headerIncludePath;
        compilation.Members.Add(record);

        ApplyDestructorMetadata(cxxRecord, record);
        PopulateRecordBases(cxxRecord, record, recordsByName, enumsByName, managedTypeOverrides, EmptyGenericParameters);
        PopulateRecordMembers(recordDecl, record, recordsByName, enumsByName, managedTypeOverrides, EmptyGenericParameters, entryPointPrefix, compilation, headerIncludePath);
        AddSyntheticDefaultConstructor(recordDecl, record, entryPointPrefix);

        UpdatePolymorphism(record, recordsByName);
    }

    private static bool IsOpaqueNativeHandleDeclaration(RecordDecl recordDecl)
    {
        return recordDecl is not CXXRecordDecl &&
            !recordDecl.Decls.Any();
    }

    private static void AddSyntheticDefaultConstructor(
        RecordDecl recordDecl,
        RecordBinding record,
        string entryPointPrefix)
    {
        var constructors = (recordDecl as CXXRecordDecl)?.Ctors ?? [];
        var hasUsableDefaultConstructor = constructors.Any(constructor =>
            constructor.IsDefaultConstructor &&
            !constructor.IsDeleted &&
            ShouldParseRecordMember(record, constructor.Handle.CXXAccessSpecifier));
        var boundConstructors = record.Members
            .OfType<FunctionBinding>()
            .Where(static member => member.IsConstructor)
            .ToArray();
        if (!recordDecl.Handle.HasUserDeclaredConstructor && hasUsableDefaultConstructor)
        {
            foreach (var boundConstructor in boundConstructors)
            {
                boundConstructor.Detach();
            }
            boundConstructors = [];
        }
        if (record.GetMetadata<bool>(MetadataKeys.ForwardDeclaration) ||
            (recordDecl.Handle.HasUserDeclaredConstructor && !hasUsableDefaultConstructor) ||
            recordDecl.Decls.OfType<ConstructorUsingShadowDecl>().Any() ||
            boundConstructors.Length > 0 ||
            record.Members.OfType<FieldBinding>().Any(static field => field.IsReference) ||
            recordDecl.Fields.Any(static field => field.Type.Handle.IsConstQualified) ||
            (!record.IsDirector &&
                (record.IsAbstract ||
                    // A record with neither members nor bases has nothing to construct for;
                    // a derived record's surface is its bases'.
                    record.Bases.Count == 0 && !record.Members.Any(static member => member is FieldBinding or FunctionBinding))))
        {
            return;
        }

        var constructor = new FunctionBinding(
            $"ctor:{record.NativeName}:c-default",
            record.NativeName,
            record.ManagedName,
            record.HandleType,
            entryPointName: BuildEntrypointName(entryPointPrefix, $"{SanitizeEntrypointNamePart(record.CppQualifiedName)}_ctor"),
            cppReturnTypeName: $"{record.CppQualifiedName}*",
            cppQualifiedName: $"{record.CppQualifiedName}::{record.NativeName}")
        {
            IsStatic = false,
            AccessModifier = BindingAccessModifier.Public,
            IsConstructor = true,
            OwningRecordCppTypeName = record.CppQualifiedName
        };
        constructor.SetMetadata(MetadataKeys.DeclarationDisplayText, $"{record.NativeName}();");
        record.Members.Add(constructor);
    }

    private static void RegisterTemplateRecord(
        ClassTemplateDecl classTemplate,
        BindingCompilation compilation,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        string entryPointPrefix,
        string? headerIncludePath,
        IReadOnlySet<string>? directorRecordCppTypeNames)
    {
        var nativeName = classTemplate.Spelling;
        if (string.IsNullOrWhiteSpace(nativeName))
        {
            return;
        }

        var sourceText = GetSourceText(classTemplate.Handle.TranslationUnit, classTemplate.Extent);
        var braceIndex = sourceText.IndexOf('{');
        var isForwardDeclaration = braceIndex < 0;
        var cppQualifiedName = GetCppQualifiedName(classTemplate);

        // Look up ghost records under both the unqualified spelling and the fully
        // qualified name — placeholders created by MapType may be keyed by either.
        var ghostRecord = FindExistingRecordForDeclaration(recordsByName, nativeName, cppQualifiedName);

        if (ghostRecord?.GetMetadata<bool>(MetadataKeys.PlaceholderRecord) == true)
        {
            ghostRecord.Detach();
            RemoveRecordKeys(recordsByName, ghostRecord);
        }
        else if (ghostRecord is not null &&
                 ghostRecord.GetMetadata<bool>(MetadataKeys.ForwardDeclaration) &&
                 !isForwardDeclaration)
        {
            ghostRecord.Detach();
            RemoveRecordKeys(recordsByName, ghostRecord);
        }
        else if (ghostRecord is not null)
        {
            return;
        }

        if (!TryExtractTemplateParameters(classTemplate, out var templateParameters))
        {
            return;
        }
        var declarationText = braceIndex >= 0
            ? sourceText[..braceIndex]
            : sourceText;
        var templatedRecord = classTemplate.TemplatedDecl;
        var record = new RecordBinding(CreateRecordSymbolId(cppQualifiedName), nativeName, ToPascalCase(GetManagedTypeHead(nativeName)), cppQualifiedName)
        {
            NativeTemplateParameterCount = classTemplate.TemplateParameters.Count,
            NativeTemplateRequiredParameterCount = GetRequiredNativeTemplateParameterCount(classTemplate),
            IsAbstract = templatedRecord is CXXRecordDecl cxx && cxx.IsAbstract,
            IsDirector = HasSharpCppAttribute(declarationText, "director") || IsForcedDirectorRecord(cppQualifiedName, directorRecordCppTypeNames),
            HasCopyConstructor = classTemplate.Handle.HasUserDeclaredCopyConstructor ||
                ContainsCopyConstructorDeclaration(sourceText, nativeName) ||
                ContainsCopyConstructorDeclaration(GetDeclFileText(classTemplate), nativeName)
        };
        if (HasSharpCppAttribute(declarationText, "native_interface"))
        {
            record.SetMetadata(MetadataKeys.NativeInterfaceWrapper, true);
        }
        record.SetMetadata(MetadataKeys.DeclarationDisplayText, GetDeclarationDisplayText(classTemplate));
        if (classTemplate.TemplateParameters.Any(static parameter =>
                parameter is TemplateTypeParmDecl { IsParameterPack: true } ||
                parameter is NonTypeTemplateParmDecl { IsParameterPack: true }))
        {
            // Derived records hoist this template's bases (DegradeUnsupportedSurfacePass).
            record.SetIgnoreReason($"variadic class template '{cppQualifiedName}' cannot be expressed as a C# generic");
        }
        else if (templatedRecord is CXXRecordDecl { Bases: var templateBases } &&
                 templateBases.Select(static baseSpecifier => baseSpecifier.Type.CanonicalType as TemplateTypeParmType)
                     .FirstOrDefault(static parameterType => parameterType is not null) is { } parameterBase &&
                 parameterBase.Index < classTemplate.TemplateParameters.Count &&
                 classTemplate.TemplateParameters[(int)parameterBase.Index] is TemplateTypeParmDecl parameterBaseDecl)
        {
            record.TemplateParameterBaseName = parameterBaseDecl.Name ?? parameterBaseDecl.Spelling;
        }
        if (isForwardDeclaration)
        {
            record.SetMetadata(MetadataKeys.ForwardDeclaration, true);
        }

        foreach (var templateParameter in templateParameters)
        {
            record.GenericParameters.Add(templateParameter);
        }

        ApplyExplicitGenericInstantiations(classTemplate, record, recordsByName, enumsByName, managedTypeOverrides);
        CheckAndApplyIgnoreAttribute(classTemplate, record);
        TryApplyRename(classTemplate, record);
        ApplyDeclarationFacts(classTemplate, record);
        IndexRecordKeys(recordsByName, record);
        record.HeaderIncludePath = headerIncludePath;
        compilation.Members.Add(record);

        var genericParameters = CreateGenericParameterScope(classTemplate.TemplateParameters);
        ApplyDestructorMetadata(templatedRecord as CXXRecordDecl, record);
        PopulateRecordBases(templatedRecord as CXXRecordDecl, record, recordsByName, enumsByName, managedTypeOverrides, genericParameters);
        PopulateRecordMembers(templatedRecord, record, recordsByName, enumsByName, managedTypeOverrides, genericParameters, entryPointPrefix, compilation, headerIncludePath);
        ApplySpecializedReturnOwnership(classTemplate, record, recordsByName, enumsByName, managedTypeOverrides);

        UpdatePolymorphism(record, recordsByName);
    }

    private static void ApplySpecializedReturnOwnership(
        ClassTemplateDecl classTemplate,
        RecordBinding record,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides)
    {
        var methodsByDeclaration = record.Members
            .OfType<FunctionBinding>()
            .Where(static method => !string.IsNullOrWhiteSpace(method.NativeDeclarationId))
            .ToLookup(static method => method.NativeDeclarationId!, StringComparer.Ordinal);
        if (methodsByDeclaration.Count == 0)
        {
            return;
        }
        var sourceMethods = classTemplate.TemplatedDecl.Methods;

        var templateDeclarations = new[]
            {
                classTemplate,
                classTemplate.CanonicalDecl,
                classTemplate.MostRecentDecl
            }
            .DistinctBy(static template => template.Handle);
        var specializations = templateDeclarations
            .SelectMany(static template => template.Specializations)
            .DistinctBy(static specialization => specialization.Handle);

        foreach (var specialization in specializations)
        {
            if (!TryCreateInstantiationFromTemplateArguments(
                    specialization.TemplateArgs,
                    recordsByName,
                    enumsByName,
                    managedTypeOverrides,
                    out var instantiation))
            {
                continue;
            }
            PreserveTemplateArgumentNativeTypeNames(
                specialization.TemplateArgs,
                instantiation,
                recordsByName);
            var substitutions = CreateTemplateTypeSubstitutions(
                classTemplate.TemplateParameters,
                specialization.TemplateArgs);

            foreach (var sourceMethod in sourceMethods)
            {
                var sourceDeclarationId = GetNativeDeclarationId(sourceMethod);
                if (sourceDeclarationId is null)
                {
                    continue;
                }

                var concreteReturnType = MarshallerConfigExtractor.CreateCppTypePattern(
                    sourceMethod.ReturnType,
                    substitutions);
                if (!MarshallerConfigExtractor.ReturnTypeOwnsHandle(
                        concreteReturnType,
                        managedTypeOverrides.Values))
                {
                    continue;
                }

                foreach (var method in methodsByDeclaration[sourceDeclarationId])
                {
                    if (!method.OwnedReturnRecordInstantiations.Any(existing =>
                            InstantiationsEqual(existing.TypeArguments, instantiation.TypeArguments)))
                    {
                        method.OwnedReturnRecordInstantiations.Add(instantiation);
                    }
                }
            }
        }
    }

    private static IReadOnlyDictionary<(uint Depth, uint Index), ClangSharp.Type> CreateTemplateTypeSubstitutions(
        IReadOnlyList<NamedDecl> templateParameters,
        IReadOnlyList<TemplateArgument> templateArguments)
    {
        var substitutions = new Dictionary<(uint Depth, uint Index), ClangSharp.Type>();
        for (var index = 0; index < templateParameters.Count && index < templateArguments.Count; index++)
        {
            if (templateParameters[index] is TemplateTypeParmDecl parameter &&
                templateArguments[index].Kind == CXTemplateArgumentKind.CXTemplateArgumentKind_Type)
            {
                substitutions[(checked((uint)parameter.Depth), checked((uint)parameter.Index))] =
                    templateArguments[index].AsType;
            }
        }
        return substitutions;
    }

    private static void PreserveTemplateArgumentNativeTypeNames(
        IReadOnlyList<TemplateArgument> templateArguments,
        RecordGenericInstantiationBinding instantiation,
        IDictionary<string, RecordBinding> recordsByName)
    {
        var bindingIndex = 0;
        foreach (var templateArgument in templateArguments)
        {
            var arguments = templateArgument.Kind == CXTemplateArgumentKind.CXTemplateArgumentKind_Pack
                ? templateArgument.PackElements
                : [templateArgument];
            foreach (var argument in arguments)
            {
                if (bindingIndex >= instantiation.TypeArguments.Count)
                {
                    return;
                }

                if (argument.Kind == CXTemplateArgumentKind.CXTemplateArgumentKind_Type)
                {
                    var bindingType = instantiation.TypeArguments[bindingIndex];
                    instantiation.UpdateTypeArgument(
                        bindingIndex,
                        bindingType.WithCppTypeName(ResolveQualifiedTypeName(
                            argument.AsType,
                            recordsByName,
                            EmptyGenericParameters,
                            bindingType)));
                }
                bindingIndex++;
            }
        }
    }

    private static void PopulateRecordBases(
        CXXRecordDecl? cxxRecord,
        RecordBinding record,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        IReadOnlyDictionary<string, BindingType> genericParameters)
    {
        if (cxxRecord is null)
        {
            return;
        }

        foreach (var baseSpec in cxxRecord.Bases)
        {
            var baseTypeName = GetRecordQualifiedName(baseSpec.Type.Handle, recordsByName);
            var baseCppTypeName = ResolveQualifiedTypeName(baseSpec.Type, recordsByName);
            var (baseManagedType, baseIgnoreReason) = TryMapType(baseSpec.Type, recordsByName, enumsByName, managedTypeOverrides, genericParameters);
            var baseRecord = baseManagedType.RecordId is not null
                ? recordsByName.Values.Distinct().FirstOrDefault(candidate =>
                    string.Equals(candidate.SymbolId, baseManagedType.RecordId, StringComparison.Ordinal))
                : null;
            if (baseRecord is null && !recordsByName.TryGetValue(baseTypeName, out baseRecord))
            {
                var baseQualifiedName = NormalizeNamedTypeName(baseSpec.Type.Handle.Spelling.ToString());
                var unqualifiedBaseName = GetManagedTypeHead(baseTypeName);
                baseRecord = new RecordBinding(CreateRecordSymbolId(baseQualifiedName), unqualifiedBaseName, ToPascalCase(unqualifiedBaseName), baseQualifiedName);
                baseRecord.SetMetadata(MetadataKeys.PlaceholderRecord, true);
                IndexRecordKeys(recordsByName, baseRecord);
            }

            record.Bases.Add(new RecordBaseBinding(
                baseRecord.SymbolId,
                baseRecord.NativeName,
                baseIgnoreReason is null ? baseManagedType.ManagedTypeName : baseRecord.ManagedName,
                baseCppTypeName,
                baseSpec.IsVirtual,
                baseIgnoreReason is null ? baseManagedType : null));

            if (baseIgnoreReason is null &&
                record.ManagedBaseTypeName is null &&
                !baseSpec.IsVirtual &&
                !baseRecord.HasMetadata(MetadataKeys.PlaceholderRecord))
            {
                record.ManagedBaseTypeName = baseManagedType.UnaliasedManagedTypeName;
            }
        }
    }

    private static void PopulateRecordMembers(
        RecordDecl recordDecl,
        RecordBinding record,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        IReadOnlyDictionary<string, BindingType> genericParameters,
        string entryPointPrefix,
        BindingCompilation compilation,
        string? headerIncludePath)
    {
        foreach (var child in recordDecl.Decls)
        {
            // Use Interop cursor's access specifier — the managed Decl.Access
            // may not reflect the true C++ access for members in protected
            // sections of template-context classes.
            if (!ShouldParseRecordMember(record, child.Handle.CXXAccessSpecifier))
            {
                continue;
            }

            switch (child)
            {
                // Destructors are handled by ApplyDestructorMetadata — skip them here
                // to avoid leaking the ~Name syntax into method creation (CXXDestructorDecl
                // extends CXXMethodDecl and would match the method case below).
                case CXXDestructorDecl:
                    continue;

                case ConstructorUsingShadowDecl inheritedConstructor when
                    TryGetInheritedConstructor(recordDecl, inheritedConstructor, out var targetConstructor) &&
                    !targetConstructor.IsCopyConstructor &&
                    !targetConstructor.IsMoveConstructor:
                    {
                        var ctor = CreateConstructor(record, targetConstructor, recordsByName, enumsByName, managedTypeOverrides, genericParameters, entryPointPrefix);
                        CheckAndApplyIgnoreAttribute(inheritedConstructor, ctor);
                        ApplyDeclarationFacts(targetConstructor, ctor);
                        ApplyCallableDefinitionMetadata(targetConstructor, ctor);
                        ApplyDeletedCallableIgnore(targetConstructor, ctor);
                        record.Members.Add(ctor);
                        break;
                    }

                // CXXConstructorDecl must be checked BEFORE CXXMethodDecl since it's
                // a subclass — the first matching case wins in C# pattern matching.
                // Implicit copy/move constructors are exposed by Decl.Decls but
                // were not returned by the old cursor VisitorChildren. Skip them
                // to avoid generating invalid native thunks (&ConstructorName).
                case CXXConstructorDecl ctorDecl when ctorDecl.IsCopyConstructor || ctorDecl.IsMoveConstructor:
                    if (ctorDecl.IsCopyConstructor && !ctorDecl.IsDeleted)
                    {
                        record.HasCopyConstructor = true;
                    }
                    continue;

                case CXXConstructorDecl ctorDecl:
                    {
                        var ctor = CreateConstructor(record, ctorDecl, recordsByName, enumsByName, managedTypeOverrides, genericParameters, entryPointPrefix);
                        CheckAndApplyIgnoreAttribute(ctorDecl, ctor);
                        ApplyDeclarationFacts(ctorDecl, ctor);
                        ApplyCallableDefinitionMetadata(ctorDecl, ctor);

                        if (record.GetMetadata<bool>(MetadataKeys.NonPublicDestructor))
                        {
                            ctor.SetIgnoreReason($"constructors on record '{record.CppQualifiedName}' with a non-public destructor are not materializable in this bootstrap surface");
                        }

                        ApplyDeletedCallableIgnore(ctorDecl, ctor);
                        record.Members.Add(ctor);
                        break;
                    }

                case CXXConversionDecl conversionMethod:
                    {
                        if (conversionMethod.Handle.CXXMethod_IsStatic)
                            continue;
                        var method = CreateMethod(record, conversionMethod, recordsByName, enumsByName, managedTypeOverrides, genericParameters, entryPointPrefix);
                        CheckAndApplyIgnoreAttribute(conversionMethod, method);
                        TryApplyRename(conversionMethod, method);
                        ApplyDeclarationFacts(conversionMethod, method);
                        ApplyCallableDefinitionMetadata(conversionMethod, method);
                        ApplyDeletedCallableIgnore(conversionMethod, method);
                        ApplyVirtualSpecifiers(conversionMethod, method);
                        record.Members.Add(method);
                        break;
                    }

                case CXXMethodDecl methodDecl when !methodDecl.Handle.CXXMethod_IsStatic:
                    {
                        // Prefer class-body declarations over out-of-line definitions.
                        // Decl.Decls may include both; only class-body declarations
                        // (with the RecordDecl as lexical parent) have correct access.
                        if (methodDecl.Handle.Kind != CXCursorKind.CXCursor_CXXMethod)
                            continue;

                        var method = CreateMethod(record, methodDecl, recordsByName, enumsByName, managedTypeOverrides, genericParameters, entryPointPrefix);
                        CheckAndApplyIgnoreAttribute(methodDecl, method);
                        TryApplyRename(methodDecl, method);
                        ApplyDeclarationFacts(methodDecl, method);
                        ApplyCallableDefinitionMetadata(methodDecl, method);
                        ApplyDeletedCallableIgnore(methodDecl, method);
                        ApplyVirtualSpecifiers(methodDecl, method);
                        record.Members.Add(method);
                        break;
                    }

                case CXXMethodDecl staticMethod when staticMethod.Handle.CXXMethod_IsStatic:
                    {
                        // Decl.Decls may include out-of-line inline definitions
                        // (CXCursor_FunctionDecl) in addition to class-body
                        // declarations (CXCursor_CXXMethod). Only the class-body
                        // declarations have correct access specifiers. Out-of-
                        // line definitions are for the linker, not the class API.
                        if (staticMethod.Handle.Kind != CXCursorKind.CXCursor_CXXMethod)
                            continue;

                        // Protected static methods can't be called from a
                        // standalone function (not a friend or derived class).
                        if (staticMethod.Access == CX_CXXAccessSpecifier.CX_CXXProtected ||
                            staticMethod.Handle.CXXAccessSpecifier == CX_CXXAccessSpecifier.CX_CXXProtected)
                            continue;

                        var method = CreateStaticMethod(record, staticMethod, recordsByName, enumsByName, managedTypeOverrides, genericParameters, entryPointPrefix);
                        CheckAndApplyIgnoreAttribute(staticMethod, method);
                        TryApplyRename(staticMethod, method);
                        ApplyDeclarationFacts(staticMethod, method);
                        ApplyCallableDefinitionMetadata(staticMethod, method);
                        ApplyDeletedCallableIgnore(staticMethod, method);
                        record.Members.Add(method);
                        break;
                    }

                case FunctionTemplateDecl funcTemplate:
                    {
                        var templateMethod = CreateTemplateMethod(record, funcTemplate, recordsByName, enumsByName, managedTypeOverrides, genericParameters, entryPointPrefix);
                        if (templateMethod is not null)
                        {
                            CheckAndApplyIgnoreAttribute(funcTemplate, templateMethod);
                            TryApplyRename(funcTemplate, templateMethod);
                            ApplyDeclarationFacts(funcTemplate, templateMethod);
                            ApplyCallableDefinitionMetadata(funcTemplate, templateMethod);
                            ApplyDeletedCallableIgnore(funcTemplate.TemplatedDecl, templateMethod);
                            record.Members.Add(templateMethod);
                        }
                        break;
                    }

                case VarDecl variableDecl when variableDecl.IsStaticDataMember:
                    {
                        var field = CreateField(record, variableDecl, recordsByName, enumsByName, managedTypeOverrides, genericParameters, isStatic: true);
                        CheckAndApplyIgnoreAttribute(variableDecl, field);
                        TryApplyRename(variableDecl, field);
                        ApplyDeclarationFacts(variableDecl, field);
                        record.Members.Add(field);
                        break;
                    }

                case FieldDecl fieldDecl:
                    {
                        if (TryAddFlattenedRecordFields(recordDecl, record, fieldDecl, recordsByName, enumsByName, managedTypeOverrides, genericParameters))
                            break;

                        var field = CreateField(record, fieldDecl, recordsByName, enumsByName, managedTypeOverrides, genericParameters);
                        CheckAndApplyIgnoreAttribute(fieldDecl, field);
                        TryApplyRename(fieldDecl, field);
                        ApplyDeclarationFacts(fieldDecl, field);
                        record.Members.Add(field);
                        break;
                    }

                // Declarations nested in a class template depend on its arguments.
                case EnumDecl enumDecl when !record.IsGeneric:
                    if (!string.IsNullOrEmpty(enumDecl.Name) &&
                        !GetCppQualifiedName(enumDecl).Contains("(unnamed enum", StringComparison.Ordinal))
                        RegisterEnum(enumDecl, compilation, enumsByName, headerIncludePath);
                    break;

                // Public records declared in the record's scope; the injected class name
                // is implicit, and anonymous records are flattened into their field.
                case CXXRecordDecl nestedRecordDecl when
                    !record.IsGeneric &&
                    nestedRecordDecl is not ClassTemplateSpecializationDecl &&
                    !nestedRecordDecl.IsInjectedClassName &&
                    !IsAnonymousRecordDecl(nestedRecordDecl) &&
                    !IsAnonymousFieldName(nestedRecordDecl.Name) &&
                    child.Handle.CXXAccessSpecifier == CX_CXXAccessSpecifier.CX_CXXPublic:
                    RegisterRecord(nestedRecordDecl, compilation, recordsByName, enumsByName, managedTypeOverrides, entryPointPrefix, headerIncludePath, directorRecordCppTypeNames: null);
                    break;
            }
        }

        record.HasCopyConstructor |= recordDecl.Decls
            .OfType<CXXConstructorDecl>()
            .Any(ctor =>
                !ctor.IsDeleted &&
                ShouldParseRecordMember(record, ctor.Handle.CXXAccessSpecifier) &&
                IsCopyConstructorLike(ctor, record));
    }

    private static bool TryGetInheritedConstructor(
        RecordDecl recordDecl,
        ConstructorUsingShadowDecl inheritedConstructor,
        out CXXConstructorDecl targetConstructor)
    {
        var inheritedConstructors = recordDecl.Decls.OfType<ConstructorUsingShadowDecl>().ToArray();
        var ordinal = Array.IndexOf(inheritedConstructors, inheritedConstructor);
        var candidates = inheritedConstructor.ConstructedBaseClass.Decls
            .OfType<CXXConstructorDecl>()
            .ToArray();
        targetConstructor = ordinal >= 0 && ordinal < candidates.Length
            ? candidates[ordinal]
            : null!;
        return targetConstructor is not null;
    }

    private static bool IsCopyConstructorLike(CXXConstructorDecl ctorDecl, RecordBinding record)
    {
        if (ctorDecl.IsCopyConstructor)
        {
            return true;
        }

        if (ctorDecl.Parameters.Count != 1)
        {
            return false;
        }

        var parameterType = NormalizeTypeName(ctorDecl.Parameters[0].Type.AsString);
        var normalizedRecordName = NormalizeTypeName(record.CppQualifiedName);
        var unqualifiedRecordName = NormalizeTypeName(record.NativeName);

        return string.Equals(parameterType, $"const {normalizedRecordName}&", StringComparison.Ordinal) ||
            string.Equals(parameterType, $"{normalizedRecordName}&", StringComparison.Ordinal) ||
            string.Equals(parameterType, $"const {unqualifiedRecordName}&", StringComparison.Ordinal) ||
            string.Equals(parameterType, $"{unqualifiedRecordName}&", StringComparison.Ordinal);
    }

    private static bool ContainsCopyConstructorDeclaration(string sourceText, string nativeName)
    {
        var normalizedSource = sourceText
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("\t", string.Empty, StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal);
        var normalizedName = NormalizeTypeName(nativeName).Replace(" ", string.Empty, StringComparison.Ordinal);

        return normalizedSource.Contains($"{normalizedName}(const{normalizedName}&)", StringComparison.Ordinal) ||
            normalizedSource.Contains($"{normalizedName}({normalizedName}&)", StringComparison.Ordinal);
    }

    private static string GetDeclFileText(Decl decl)
    {
        decl.Location.GetFileLocation(out var file, out _, out _, out _);
        var filePath = file.Name.CString;
        return !string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath)
            ? File.ReadAllText(filePath)
            : string.Empty;
    }

    private static FunctionBinding CreateMethod(
        RecordBinding record,
        CXXMethodDecl methodDecl,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        IReadOnlyDictionary<string, BindingType> genericParameters,
        string entryPointPrefix)
    {
        var nativeName = methodDecl.Name ?? methodDecl.Spelling.ToString();
        var (returnType, ignoreReason) = TryMapType(methodDecl.ReturnType, recordsByName, enumsByName, managedTypeOverrides, genericParameters, isReturn: true);
        var cppReturnTypeName = ResolveQualifiedTypeName(
            methodDecl.ReturnType, recordsByName, genericParameters, returnType);
        var function = new FunctionBinding(
            BuildMemberSymbolId("method", record, nativeName, methodDecl.Parameters.Select(static parameter => parameter.Type.AsString), methodDecl.IsConst),
            nativeName,
            GetManagedMethodName(methodDecl, nativeName),
            returnType,
            entryPointName: $"{entryPointPrefix}_{record.NativeName}_{GetEntrypointSuffix(methodDecl, nativeName)}",
            cppReturnTypeName: cppReturnTypeName,
            cppQualifiedName: $"{record.CppQualifiedName}::{nativeName}")
        {
            IsStatic = false,
            AccessModifier = MapAccessModifier(methodDecl.Access),
            IsVirtual = methodDecl.IsVirtual,
            IsPureVirtual = methodDecl.IsPure,
            IsConstMethod = methodDecl.IsConst,
            NativeDeclarationId = GetNativeDeclarationId(methodDecl),
            OwningRecordCppTypeName = record.CppQualifiedName,
            ReturnPointerDepth = GetPointerDepth(methodDecl.ReturnType.Handle)
        };
        function.ReturnsOwnedHandle = ShouldReturnOwnedHandle(methodDecl.ReturnType, returnType, managedTypeOverrides);
        ApplyOperatorMetadata(function, methodDecl);

        for (int parameterIndex = 0; parameterIndex < methodDecl.Parameters.Count; parameterIndex++)
        {
            var parameter = methodDecl.Parameters[parameterIndex];
            var parameterName = parameter.Name ?? parameter.Spelling.ToString();
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                parameterName = $"arg{parameterIndex}";
            }
            var managedParameterName = EscapeManagedIdentifier(parameterName);

            var (parameterType, direction, abiKind, parameterIgnoreReason) = TryMapParameter(methodDecl.Handle, parameter.Handle, parameter.Type, recordsByName, enumsByName, managedTypeOverrides, genericParameters);
            ignoreReason ??= parameterIgnoreReason;
            var param = new ParameterBinding(
                $"parameter:{record.NativeName}:{nativeName}:{parameterName}",
                parameterName,
                managedParameterName,
                parameterType,
                direction,
                abiKind,
                ResolveParameterCppTypeName(parameter.Type, recordsByName, genericParameters, parameterType));
            ApplyManagedDefaultValue(param, parameter, parameterType, managedTypeOverrides);
            ApplyCallbackDelegateAttribute(methodDecl.Handle, parameter.Handle, param, ref ignoreReason);
            function.Parameters.Add(param);
        }

        LowerDirectorIndirectParameters(record, function);
        ApplyInferredInputPointerArrayMetadata(function);

        function.EntryPointName = BuildEntrypointName(entryPointPrefix, $"{SanitizeEntrypointNamePart(record.CppQualifiedName)}_{GetEntrypointSuffix(methodDecl, nativeName)}");

        if (ignoreReason is not null)
        {
            function.SetIgnoreReason(ignoreReason);
        }

        function.SetMetadata(MetadataKeys.DeclarationDisplayText, GetDeclarationDisplayText(methodDecl));

        return function;
    }

    private static void LowerDirectorIndirectParameters(RecordBinding record, FunctionBinding function)
    {
        if (!record.IsDirector || !function.IsVirtual)
        {
            return;
        }

        foreach (var parameter in function.Parameters)
        {
            var isPrimitivePointer =
                parameter.AbiKind == BindingParameterAbiKind.Pointer &&
                parameter.Type.Kind == BindingTypeKind.Primitive;
            var isPrimitiveReference =
                parameter.CppTypeName.Contains('&', StringComparison.Ordinal) &&
                parameter.Type.Kind == BindingTypeKind.Primitive &&
                parameter.Type.PrimitiveKind != BindingPrimitiveKind.Unsupported &&
                string.IsNullOrWhiteSpace(parameter.Type.ManagedMarshaller);
            if (!isPrimitivePointer && !isPrimitiveReference)
            {
                continue;
            }

            parameter.Type = BindingType.CreateConfigured(
                parameter.CppTypeName,
                BindingType.IntPtr.ManagedTypeName,
                primitiveKind: BindingPrimitiveKind.IntPtr);
            parameter.Direction = BindingParameterDirection.In;
            parameter.AbiKind = BindingParameterAbiKind.Value;
        }
    }

    private static FunctionBinding? CreateTemplateMethod(
        RecordBinding record,
        FunctionTemplateDecl templateDecl,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        IReadOnlyDictionary<string, BindingType> genericParameters,
        string entryPointPrefix)
    {
        if (!TryExtractTemplateParameters(templateDecl, out var methodTemplateParameters))
        {
            return null;
        }

        if (!ShouldParseRecordMember(record, templateDecl.Access))
        {
            return null;
        }

        var combinedGenericParameters = CreateGenericParameterScope(templateDecl.TemplateParameters, genericParameters);

        var templatedFunction = templateDecl.TemplatedDecl;
        var nativeName = templateDecl.Name ?? templateDecl.Spelling.ToString();
        var (returnType, ignoreReason) = TryMapType(templatedFunction.ReturnType, recordsByName, enumsByName, managedTypeOverrides, combinedGenericParameters, isReturn: true);
        var cppReturnTypeName = ResolveQualifiedTypeName(
            templatedFunction.ReturnType, recordsByName, combinedGenericParameters, returnType);
        var function = new FunctionBinding(
            BuildMemberSymbolId(
                "method",
                record,
                nativeName,
                templatedFunction.Parameters.Select(static parameter => parameter.Type.AsString),
                templatedFunction is CXXMethodDecl cxxMethodForSignature && cxxMethodForSignature.IsConst),
            nativeName,
            GetManagedMethodName(templateDecl, nativeName),
            returnType,
            cppReturnTypeName: cppReturnTypeName,
            cppQualifiedName: $"{record.CppQualifiedName}::{nativeName}")
        {
            IsStatic = templatedFunction is CXXMethodDecl staticMethod && staticMethod.Handle.CXXMethod_IsStatic,
            AccessModifier = MapAccessModifier(templateDecl.Access),
            IsVirtual = templatedFunction is CXXMethodDecl cxxMethod && cxxMethod.IsVirtual,
            IsPureVirtual = templatedFunction is CXXMethodDecl cxxMethod2 && cxxMethod2.IsPure,
            IsConstMethod = templatedFunction is CXXMethodDecl cxxMethod3 && cxxMethod3.IsConst,
            NativeDeclarationId = GetNativeDeclarationId(templatedFunction),
            OwningRecordCppTypeName = record.CppQualifiedName,
            ReturnPointerDepth = GetPointerDepth(templatedFunction.ReturnType.Handle)
        };
        function.ReturnsOwnedHandle = ShouldReturnOwnedHandle(templatedFunction.ReturnType, returnType, managedTypeOverrides);
        if (templatedFunction is CXXConstructorDecl)
        {
            function.SetIgnoreReason("generic constructors are not supported");
        }

        foreach (var templateParameter in methodTemplateParameters)
        {
            function.GenericParameters.Add(templateParameter);
        }

        ApplyExplicitGenericInstantiations(templateDecl, function, recordsByName, enumsByName, managedTypeOverrides);

        // Use templatedFunction (FunctionDecl) for parameter access and cast for method-specific features
        for (int parameterIndex = 0; parameterIndex < templatedFunction.Parameters.Count; parameterIndex++)
        {
            var parameter = templatedFunction.Parameters[parameterIndex];
            var parameterName = parameter.Name ?? parameter.Spelling.ToString();
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                parameterName = $"arg{function.Parameters.Count}";
            }
            var managedParameterName = EscapeManagedIdentifier(parameterName);

            var (parameterType, direction, abiKind, parameterIgnoreReason) = TryMapParameter(templatedFunction.Handle, parameter.Handle, parameter.Type, recordsByName, enumsByName, managedTypeOverrides, combinedGenericParameters);
            ignoreReason ??= parameterIgnoreReason;
            var templateParam = new ParameterBinding(
                $"parameter:{record.NativeName}:{nativeName}:{parameterName}",
                parameterName,
                managedParameterName,
                parameterType,
                direction,
                abiKind,
                ResolveParameterCppTypeName(parameter.Type, recordsByName, combinedGenericParameters, parameterType));
            ApplyManagedDefaultValue(templateParam, parameter, parameterType, managedTypeOverrides);
            ApplyCallbackDelegateAttribute(templatedFunction.Handle, parameter.Handle, templateParam, ref ignoreReason);
            function.Parameters.Add(templateParam);
        }

        ApplyInferredInputPointerArrayMetadata(function);

        function.EntryPointName = BuildEntrypointName(entryPointPrefix, $"{SanitizeEntrypointNamePart(record.CppQualifiedName)}_{GetEntrypointSuffix(templateDecl, nativeName)}");

        if (ignoreReason is not null)
        {
            function.SetIgnoreReason(ignoreReason);
        }

        function.SetMetadata(MetadataKeys.DeclarationDisplayText, GetDeclarationDisplayText(templateDecl));

        if (templatedFunction is CXXMethodDecl templatedMethod)
        {
            ApplyVirtualSpecifiers(templatedMethod, function);
        }
        return function;
    }

    private static FunctionBinding CreateStaticMethod(
        RecordBinding record,
        CXXMethodDecl methodDecl,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        IReadOnlyDictionary<string, BindingType> genericParameters,
        string entryPointPrefix)
    {
        var nativeName = methodDecl.Name ?? methodDecl.Spelling.ToString();
        var (returnType, ignoreReason) = TryMapType(methodDecl.ReturnType, recordsByName, enumsByName, managedTypeOverrides, genericParameters, isReturn: true);
        var cppReturnTypeName = ResolveQualifiedTypeName(
            methodDecl.ReturnType, recordsByName, genericParameters, returnType);
        var function = new FunctionBinding(
            BuildMemberSymbolId("static-method", record, nativeName, methodDecl.Parameters.Select(static parameter => parameter.Type.AsString), isConst: false),
            nativeName,
            GetManagedMethodName(methodDecl, nativeName),
            returnType,
            entryPointName: $"{entryPointPrefix}_{record.NativeName}_{GetEntrypointSuffix(methodDecl, nativeName)}",
            cppReturnTypeName: cppReturnTypeName,
            cppQualifiedName: $"{record.CppQualifiedName}::{nativeName}")
        {
            IsStatic = true,
            AccessModifier = MapAccessModifier(methodDecl.Access),
            OwningRecordCppTypeName = record.CppQualifiedName,
            ReturnPointerDepth = GetPointerDepth(methodDecl.ReturnType.Handle)
        };
        function.ReturnsOwnedHandle = ShouldReturnOwnedHandle(methodDecl.ReturnType, returnType, managedTypeOverrides);
        ApplyOperatorMetadata(function, methodDecl);

        for (int parameterIndex = 0; parameterIndex < methodDecl.Parameters.Count; parameterIndex++)
        {
            var parameter = methodDecl.Parameters[parameterIndex];
            var parameterName = parameter.Name ?? parameter.Spelling.ToString();
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                parameterName = $"arg{parameterIndex}";
            }
            var managedParameterName = EscapeManagedIdentifier(parameterName);

            var (parameterType, direction, abiKind, parameterIgnoreReason) = TryMapParameter(methodDecl.Handle, parameter.Handle, parameter.Type, recordsByName, enumsByName, managedTypeOverrides, genericParameters);
            ignoreReason ??= parameterIgnoreReason;
            var staticParam = new ParameterBinding(
                $"parameter:{record.NativeName}:static:{nativeName}:{parameterName}",
                parameterName,
                managedParameterName,
                parameterType,
                direction,
                abiKind,
                ResolveParameterCppTypeName(parameter.Type, recordsByName, genericParameters, parameterType));
            ApplyManagedDefaultValue(staticParam, parameter, parameterType, managedTypeOverrides);
            ApplyCallbackDelegateAttribute(methodDecl.Handle, parameter.Handle, staticParam, ref ignoreReason);
            function.Parameters.Add(staticParam);
        }

        ApplyInferredInputPointerArrayMetadata(function);

        function.EntryPointName = BuildEntrypointName(entryPointPrefix, $"{SanitizeEntrypointNamePart(record.CppQualifiedName)}_{GetEntrypointSuffix(methodDecl, nativeName)}");

        if (ignoreReason is not null)
        {
            function.SetIgnoreReason(ignoreReason);
        }

        function.SetMetadata(MetadataKeys.DeclarationDisplayText, GetDeclarationDisplayText(methodDecl));

        return function;
    }

    private static FunctionBinding CreateConstructor(
        RecordBinding record,
        CXXConstructorDecl ctorDecl,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        IReadOnlyDictionary<string, BindingType> genericParameters,
        string entryPointPrefix)
    {
        var function = new FunctionBinding(
            BuildMemberSymbolId("ctor", record, record.NativeName, ctorDecl.Parameters.Select(static parameter => parameter.Type.AsString), isConst: false),
            record.NativeName,
            record.ManagedName,
            record.HandleType,
            entryPointName: $"{entryPointPrefix}_{record.NativeName}_ctor",
            cppReturnTypeName: $"{record.CppQualifiedName}*",
            cppQualifiedName: $"{record.CppQualifiedName}::{record.NativeName}")
        {
            IsStatic = false,
            AccessModifier = MapAccessModifier(ctorDecl.Access),
            IsConstructor = true,
            OwningRecordCppTypeName = record.CppQualifiedName
        };

        string? ignoreReason = null;

        for (int parameterIndex = 0; parameterIndex < ctorDecl.Parameters.Count; parameterIndex++)
        {
            var parameter = ctorDecl.Parameters[parameterIndex];
            var parameterName = parameter.Name ?? parameter.Spelling.ToString();
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                parameterName = $"arg{parameterIndex}";
            }
            var managedParameterName = EscapeManagedIdentifier(parameterName);

            var (parameterType, direction, abiKind, parameterIgnoreReason) = TryMapParameter(ctorDecl.Handle, parameter.Handle, parameter.Type, recordsByName, enumsByName, managedTypeOverrides, genericParameters);
            ignoreReason ??= parameterIgnoreReason;
            var ctorParam = new ParameterBinding(
                $"parameter:{record.NativeName}:ctor:{parameterName}",
                parameterName,
                managedParameterName,
                parameterType,
                direction,
                abiKind,
                ResolveParameterCppTypeName(parameter.Type, recordsByName, genericParameters, parameterType));
            ApplyManagedDefaultValue(ctorParam, parameter, parameterType, managedTypeOverrides);
            ApplyCallbackDelegateAttribute(ctorDecl.Handle, parameter.Handle, ctorParam, ref ignoreReason);
            function.Parameters.Add(ctorParam);
        }

        function.EntryPointName = BuildEntrypointName(entryPointPrefix, $"{SanitizeEntrypointNamePart(record.CppQualifiedName)}_ctor");

        if (ignoreReason is not null)
        {
            function.SetIgnoreReason(ignoreReason);
        }

        function.SetMetadata(MetadataKeys.DeclarationDisplayText, GetDeclarationDisplayText(ctorDecl));

        return function;
    }

    private static FieldBinding CreateField(
        RecordBinding record,
        ValueDecl fieldDecl,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        IReadOnlyDictionary<string, BindingType> genericParameters,
        bool isStatic = false)
    {
        var nativeName = fieldDecl.Name ?? fieldDecl.Spelling.ToString();
        var initialIgnoreReason = IsIncompleteArrayType(fieldDecl.Type.Handle.CanonicalType)
            ? "fixed-size array fields are not yet supported in this bootstrap surface"
            : null;
        var (fieldType, ignoreReason) = TryMapType(
            fieldDecl.Type,
            recordsByName,
            enumsByName,
            managedTypeOverrides,
            genericParameters,
            allowPrimitivePointer: true);
        var canonicalFieldType = fieldDecl.Type.Handle.CanonicalType;
        var isFunctionPointer = TypeIsPointer(canonicalFieldType)
            && TypeIsFunctionProto(canonicalFieldType.PointeeType.CanonicalType);
        if (isFunctionPointer)
        {
            fieldType = BindingType.IntPtr;
            ignoreReason = null;
        }
        if (ShouldExposeCharPointerFieldAsIntPtr(fieldDecl.Type.Handle) ||
            IsByReferenceProjection(fieldDecl.Type.Handle, managedTypeOverrides))
        {
            fieldType = BindingType.IntPtr;
            ignoreReason = null;
        }

        ignoreReason ??= initialIgnoreReason;
        if (isStatic && fieldType.IsFixedArray)
            ignoreReason ??= "static fixed-size array fields are not yet supported in this bootstrap surface";
        var field = new FieldBinding(
            $"field:{record.NativeName}:{nativeName}",
            nativeName,
            ToPascalCase(nativeName),
            fieldType,
            ResolveQualifiedTypeName(fieldDecl.Type, recordsByName, genericParameters, fieldType),
            record.CppQualifiedName,
            isReadOnly: fieldDecl.Type.Handle.IsConstQualified || IsNonAssignableType(fieldDecl.Type),
            isReference: TypeIsAnyReference(fieldDecl.Type.Handle),
            isPointer: TypeIsPointer(fieldDecl.Type.Handle.CanonicalType))
        {
            AccessModifier = MapAccessModifier(fieldDecl.Access),
            IsStatic = isStatic,
            PointerDepth = GetPointerDepth(fieldDecl.Type.Handle),
            PointerElementPointeeIsConst = IsPointerElementPointeeConst(fieldDecl.Type.Handle)
        };
        field.ReturnsOwnedHandle = ShouldReturnOwnedHandle(fieldDecl.Type, field.Type, managedTypeOverrides, preferReturnOverride: false);
        if (isFunctionPointer)
        {
            field.SetMetadata(MetadataKeys.FunctionPointerField, true);
        }
        if (fieldDecl is FieldDecl { IsBitField: true })
            field.SetMetadata(MetadataKeys.BitField, true);

        if (ignoreReason is not null)
        {
            field.SetIgnoreReason(ignoreReason);
        }

        field.SetMetadata(MetadataKeys.DeclarationDisplayText, GetDeclarationDisplayText(fieldDecl));

        return field;
    }

    private static bool TryAddFlattenedRecordFields(
        RecordDecl ownerDecl,
        RecordBinding ownerRecord,
        FieldDecl fieldDecl,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        IReadOnlyDictionary<string, BindingType> genericParameters)
    {
        if (!IsRecordValueField(fieldDecl.Type.Handle))
            return false;
        if (IsAnonymousRecordAlias(fieldDecl.Type, recordsByName))
            return false;

        var nativeName = fieldDecl.Name ?? fieldDecl.Spelling.ToString();
        var accessModifier = MapAccessModifier(fieldDecl.Access);
        var recordTypeName = GetRecordQualifiedName(fieldDecl.Type.Handle, recordsByName);
        if (HasManagedTypeOverride(fieldDecl.Type.Handle, managedTypeOverrides) ||
            recordsByName.TryGetValue(recordTypeName, out var recordValue) && IsMarshallerBackedRecordValue(recordValue))
        {
            return false;
        }
        if (TryGetRecordDeclaration(fieldDecl.Type, out var fieldRecordDecl) &&
            HasPublicCallableRecordSurface(fieldRecordDecl) &&
            fieldDecl.Type.Handle.NumTemplateArguments <= 0)
        {
            return false;
        }
        if (TryResolveNestedRecordDecl(ownerDecl, fieldDecl.Type.Handle, out var nestedRecordDecl, out var nestedAccessPrefix))
        {
            var flattenedAccessPath = IsAnonymousFieldName(nativeName)
                ? nestedAccessPrefix
                : JoinFieldAccessPath(nestedAccessPrefix, nativeName);
            var flattenedCount = AddFlattenedFieldsFromRecordDecl(
                ownerRecord,
                nestedRecordDecl,
                flattenedAccessPath,
                accessModifier,
                recordsByName,
                enumsByName,
                managedTypeOverrides,
                genericParameters);

            return flattenedCount > 0 || IsAnonymousRecordDecl(nestedRecordDecl);
        }

        if (TryGetRecordDeclaration(fieldDecl.Type, out nestedRecordDecl))
        {
            var flattenedCount = AddFlattenedFieldsFromRecordDecl(
                ownerRecord,
                nestedRecordDecl,
                nativeName,
                accessModifier,
                recordsByName,
                enumsByName,
                managedTypeOverrides,
                genericParameters);
            return flattenedCount > 0;
        }

        return false;
    }

    private static int AddFlattenedFieldsFromRecordDecl(
        RecordBinding ownerRecord,
        RecordDecl sourceRecordDecl,
        string accessPath,
        BindingAccessModifier inheritedAccessModifier,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        IReadOnlyDictionary<string, BindingType> genericParameters)
    {
        var flattenedCount = 0;
        foreach (var child in sourceRecordDecl.Decls.OfType<FieldDecl>())
        {
            var childNativeName = child.Name ?? child.Spelling.ToString();
            // Only anonymous members share the owner's access scope; a named member record's
            // protected fields are not reachable from the owner, even through a director.
            if (!IsAnonymousRecordDecl(sourceRecordDecl) && child.Access != CX_CXXAccessSpecifier.CX_CXXPublic)
                continue;

            var accessModifier = GetMostRestrictiveAccessModifier(inheritedAccessModifier, MapAccessModifier(child.Access));
            if (accessModifier == BindingAccessModifier.Private)
                continue;

            if (IsIncompleteArrayType(child.Type.Handle.CanonicalType))
                continue;

            if (IsRecordValueField(child.Type.Handle))
            {
                if (IsAnonymousRecordAlias(child.Type, recordsByName))
                    continue;

                var childRecordTypeName = GetRecordQualifiedName(child.Type.Handle, recordsByName);
                var preserveRecord = HasManagedTypeOverride(child.Type.Handle, managedTypeOverrides) ||
                    (recordsByName.TryGetValue(childRecordTypeName, out var childRecord) &&
                     IsMarshallerBackedRecordValue(childRecord)) ||
                    (TryGetRecordDeclaration(child.Type, out var childRecordDecl) &&
                     HasPublicCallableRecordSurface(childRecordDecl) &&
                     child.Type.Handle.NumTemplateArguments <= 0);
                if (!preserveRecord &&
                    TryResolveNestedRecordDecl(sourceRecordDecl, child.Type.Handle, out var nestedRecordDecl, out var nestedAccessPrefix))
                {
                    var nestedChildAccessPath = IsAnonymousFieldName(childNativeName)
                        ? JoinFieldAccessPath(accessPath, nestedAccessPrefix)
                        : JoinFieldAccessPath(accessPath, nestedAccessPrefix, childNativeName);
                    flattenedCount += AddFlattenedFieldsFromRecordDecl(
                        ownerRecord,
                        nestedRecordDecl,
                        nestedChildAccessPath,
                        accessModifier,
                        recordsByName,
                        enumsByName,
                        managedTypeOverrides,
                        genericParameters);
                    continue;
                }

                if (!preserveRecord && TryGetRecordDeclaration(child.Type, out nestedRecordDecl))
                {
                    if (string.IsNullOrWhiteSpace(childNativeName))
                        continue;

                    flattenedCount += AddFlattenedFieldsFromRecordDecl(
                        ownerRecord,
                        nestedRecordDecl,
                        JoinFieldAccessPath(accessPath, childNativeName),
                        accessModifier,
                        recordsByName,
                        enumsByName,
                        managedTypeOverrides,
                        genericParameters);
                    continue;
                }

            }

            if (string.IsNullOrWhiteSpace(childNativeName))
                continue;

            var childAccessPath = JoinFieldAccessPath(accessPath, childNativeName);
            var childNativePrefix = ToFlattenedFieldNativeName(childAccessPath);
            var childManagedPrefix = ToFlattenedFieldManagedName(childAccessPath);

            var flattenedField = CreateFlattenedLeafField(
                ownerRecord,
                child,
                childNativePrefix,
                childManagedPrefix,
                childAccessPath,
                accessModifier,
                recordsByName,
                enumsByName,
                managedTypeOverrides,
                genericParameters);
            ownerRecord.Members.Add(flattenedField);
            flattenedCount++;
        }

        return flattenedCount;
    }

    private static bool HasPublicCallableRecordSurface(RecordDecl recordDecl)
    {
        return recordDecl.Decls.Any(static child => child switch
        {
            CXXMethodDecl method when method is not CXXConstructorDecl and not CXXDestructorDecl &&
                !method.IsStatic && !method.Handle.IsImplicit && !method.IsOverloadedOperator &&
                method.Name?.StartsWith("operator", StringComparison.Ordinal) != true =>
                method.Handle.CXXAccessSpecifier == CX_CXXAccessSpecifier.CX_CXXPublic,
            FunctionTemplateDecl { TemplatedDecl: CXXMethodDecl method } when
                !method.IsStatic &&
                method.Name?.StartsWith("operator", StringComparison.Ordinal) != true =>
                method.Handle.CXXAccessSpecifier == CX_CXXAccessSpecifier.CX_CXXPublic,
            _ => false
        });
    }

    private static FieldBinding CreateFlattenedLeafField(
        RecordBinding ownerRecord,
        FieldDecl leafFieldDecl,
        string nativeName,
        string managedName,
        string accessPath,
        BindingAccessModifier accessModifier,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        IReadOnlyDictionary<string, BindingType> genericParameters)
    {
        var (fieldType, ignoreReason) = TryMapType(
            leafFieldDecl.Type,
            recordsByName,
            enumsByName,
            managedTypeOverrides,
            genericParameters,
            allowPrimitivePointer: true);
        var cppTypeName = ResolveQualifiedTypeName(leafFieldDecl.Type, recordsByName);
        if (ShouldExposeCharPointerFieldAsIntPtr(leafFieldDecl.Type.Handle))
        {
            fieldType = BindingType.IntPtr;
            ignoreReason = null;
        }
        else if (ShouldExposeFlattenedFieldAsIntPtr(leafFieldDecl.Type.Handle, fieldType, recordsByName, managedTypeOverrides))
        {
            fieldType = BindingType.IntPtr;
            cppTypeName = BindingType.IntPtr.CppTypeName;
            ignoreReason = null;
        }

        var field = new FieldBinding(
            $"field:{ownerRecord.NativeName}:{accessPath}",
            nativeName,
            managedName,
            fieldType,
            cppTypeName,
            ownerRecord.CppQualifiedName,
            isReadOnly: leafFieldDecl.Type.Handle.IsConstQualified || IsNonAssignableType(leafFieldDecl.Type),
            isReference: TypeIsAnyReference(leafFieldDecl.Type.Handle),
            isPointer: TypeIsPointer(leafFieldDecl.Type.Handle))
        {
            AccessModifier = accessModifier,
            PointerDepth = GetPointerDepth(leafFieldDecl.Type.Handle),
            PointerElementPointeeIsConst = IsPointerElementPointeeConst(leafFieldDecl.Type.Handle)
        };
        field.ReturnsOwnedHandle = ShouldReturnOwnedHandle(leafFieldDecl.Type, field.Type, managedTypeOverrides, preferReturnOverride: false);
        field.SetMetadata(MetadataKeys.NativeFieldAccessExpression, accessPath);
        field.SetMetadata(MetadataKeys.DeclarationDisplayText, GetDeclarationDisplayText(leafFieldDecl));
        ApplyDeclarationFacts(leafFieldDecl, field);

        if (ignoreReason is not null)
            field.SetIgnoreReason(ignoreReason);

        return field;
    }

    private static BindingAccessModifier GetMostRestrictiveAccessModifier(
        BindingAccessModifier left,
        BindingAccessModifier right)
    {
        return (BindingAccessModifier)Math.Max((int)left, (int)right);
    }

    private static bool IsRecordValueField(CXType type)
    {
        var normalizedType = type.CanonicalType;
        return TypeIsRecord(normalizedType) &&
            !TypeIsPointer(normalizedType) &&
            !TypeIsAnyReference(normalizedType) &&
            !IsIncompleteArrayType(normalizedType);
    }

    private static bool TryGetRecordDeclaration(ClangSharp.Type type, out RecordDecl recordDecl)
    {
        switch (type)
        {
            case RecordType recordType:
                recordDecl = recordType.Decl;
                return true;
            case ElaboratedType elaboratedType:
                return TryGetRecordDeclaration(elaboratedType.NamedType, out recordDecl);
            case TypedefType typedefType:
                return TryGetRecordDeclaration(typedefType.Desugar, out recordDecl);
            case SubstTemplateTypeParmType substitutionType:
                return TryGetRecordDeclaration(substitutionType.ReplacementType, out recordDecl);
            default:
                if (type.AsCXXRecordDecl is RecordDecl resolvedRecordDecl)
                {
                    recordDecl = resolvedRecordDecl;
                    return true;
                }
                if (type.IsSugared && type.Desugar.Handle != type.Handle &&
                    TryGetRecordDeclaration(type.Desugar, out recordDecl))
                {
                    return true;
                }
                if (type.CanonicalType.Handle != type.Handle &&
                    TryGetRecordDeclaration(type.CanonicalType, out recordDecl))
                {
                    return true;
                }
                recordDecl = null!;
                return false;
        }
    }

    private static bool IsAnonymousRecordAlias(
        ClangSharp.Type type,
        IDictionary<string, RecordBinding> recordsByName)
    {
        if (!TryGetRecordDeclaration(type, out var recordDecl) ||
            recordDecl.TypedefNameForAnonDecl is null)
        {
            return false;
        }

        // A C++ `using storage = struct { ... };` names a storage abstraction that
        // should stay intact. A flat C `typedef struct { ... } name;` is the ordinary
        // way to name a POD and remains eligible for flattening. Anonymous typedef
        // unions, external declarations, and nested aggregate graphs are preserved
        // because their C and C++ member surfaces may differ (for example Xlib's
        // `new` / `c_new` field).
        var recordTypeName = GetRecordQualifiedName(type.Handle, recordsByName);
        var isOwnedRecord = recordsByName.TryGetValue(recordTypeName, out var record) &&
            !record.GetMetadata<bool>(MetadataKeys.PlaceholderRecord);
        return type.Handle.Declaration.Kind == CXCursorKind.CXCursor_TypeAliasDecl ||
            !isOwnedRecord ||
            recordDecl.Handle.Kind == CXCursorKind.CXCursor_UnionDecl ||
            recordDecl.Decls.OfType<FieldDecl>().Any(static field => IsRecordValueField(field.Type.Handle));
    }

    private static bool TryResolveNestedRecordDecl(RecordDecl ownerDecl, CXType fieldType, out RecordDecl nestedRecordDecl, out string accessPrefix)
    {
        var lookupNames = GetRecordTypeLookupNames(fieldType);
        var targetLocationKey = GetCursorLocationKey(fieldType.Declaration);
        return TryResolveNestedRecordDecl(ownerDecl, lookupNames, targetLocationKey, string.Empty, out nestedRecordDecl, out accessPrefix);
    }

    private static bool TryResolveNestedRecordDecl(
        RecordDecl ownerDecl,
        HashSet<string> lookupNames,
        string? targetLocationKey,
        string currentAccessPath,
        out RecordDecl nestedRecordDecl,
        out string accessPrefix)
    {
        foreach (var childRecordDecl in ownerDecl.Decls.OfType<RecordDecl>())
        {
            if (IsMatchingRecordDecl(childRecordDecl, lookupNames, targetLocationKey))
            {
                nestedRecordDecl = childRecordDecl;
                accessPrefix = currentAccessPath;
                return true;
            }
        }

        foreach (var childFieldDecl in ownerDecl.Decls.OfType<FieldDecl>())
        {
            var childNativeName = childFieldDecl.Name ?? childFieldDecl.Spelling.ToString();
            if (!IsRecordValueField(childFieldDecl.Type.Handle))
                continue;

            if (!TryResolveDirectNestedRecordDecl(ownerDecl, childFieldDecl.Type.Handle, out var childRecordDecl))
                continue;

            var childAccessPath = IsAnonymousFieldName(childNativeName)
                ? currentAccessPath
                : JoinFieldAccessPath(currentAccessPath, childNativeName);
            if (TryResolveNestedRecordDecl(childRecordDecl, lookupNames, targetLocationKey, childAccessPath, out nestedRecordDecl, out accessPrefix))
                return true;
        }

        nestedRecordDecl = null!;
        accessPrefix = string.Empty;
        return false;
    }

    private static bool TryResolveDirectNestedRecordDecl(RecordDecl ownerDecl, CXType fieldType, out RecordDecl nestedRecordDecl)
    {
        var lookupNames = GetRecordTypeLookupNames(fieldType);
        var targetLocationKey = GetCursorLocationKey(fieldType.Declaration);
        foreach (var childRecordDecl in ownerDecl.Decls.OfType<RecordDecl>())
        {
            if (IsMatchingRecordDecl(childRecordDecl, lookupNames, targetLocationKey))
            {
                nestedRecordDecl = childRecordDecl;
                return true;
            }
        }

        nestedRecordDecl = null!;
        return false;
    }

    private static bool IsMatchingRecordDecl(RecordDecl recordDecl, HashSet<string> lookupNames, string? targetLocationKey)
    {
        return (!string.IsNullOrWhiteSpace(targetLocationKey) && string.Equals(GetDeclLocationKey(recordDecl), targetLocationKey, StringComparison.Ordinal)) ||
            lookupNames.Contains(NormalizeRecordLookupName(GetCppQualifiedName(recordDecl)));
    }

    private static string? GetDeclLocationKey(Decl decl)
    {
        return GetCursorLocationKey(decl.Handle);
    }

    private static string? GetCursorLocationKey(CXCursor cursor)
    {
        if (cursor.IsNull)
            return null;

        cursor.Location.GetFileLocation(out var file, out var line, out var column, out _);
        var path = file.Name.CString;
        return string.IsNullOrWhiteSpace(path) ? null : $"{Path.GetFullPath(path)}:{line}:{column}";
    }

    private static HashSet<string> GetRecordTypeLookupNames(CXType type)
    {
        var names = new HashSet<string>(StringComparer.Ordinal)
        {
            NormalizeRecordLookupName(type.Spelling.ToString()),
            NormalizeRecordLookupName(type.CanonicalType.Spelling.ToString())
        };

        var declaration = type.Declaration;
        if (!declaration.IsNull)
            names.Add(NormalizeRecordLookupName(GetCppQualifiedName(declaration)));

        return names;
    }

    private static string NormalizeRecordLookupName(string name)
    {
        return NormalizeNamedTypeName(StripGenericArguments(NormalizeTypeName(name)));
    }

    private static bool IsAnonymousRecordDecl(RecordDecl recordDecl)
    {
        return recordDecl.IsAnonymousStructOrUnion;
    }

    private static bool IsAnonymousFieldName(string? name)
    {
        return string.IsNullOrWhiteSpace(name) ||
            name.Contains("(unnamed", StringComparison.Ordinal) ||
            name.Contains("(anonymous", StringComparison.Ordinal);
    }

    private static bool HasManagedTypeOverride(
        CXType type, IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides)
        => TryGetManagedTypeOverride(type, managedTypeOverrides, out _);

    private static bool TryGetManagedTypeOverride(
        CXType type,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        out ManagedTypeOverride? managedTypeOverride)
    {
        var spelledTypeName = NormalizeTypeName(type.Spelling.ToString());
        var canonicalTypeName = NormalizeTypeName(type.CanonicalType.Spelling.ToString());
        var qualifiedSpelledTypeName = GetQualifiedTypeName(type, spelledTypeName);
        return TryGetManagedTypeOverride(
                spelledTypeName,
                canonicalTypeName,
                qualifiedSpelledTypeName,
                managedTypeOverrides,
                isReturn: false,
                out managedTypeOverride) &&
            managedTypeOverride!.HasPublicTypeMapping &&
            MarshallerConfigExtractor.MatchesManagedTypeOverride(managedTypeOverride, type);
    }

    // Fields and returns cannot pass by reference, so a by-reference projection keeps the
    // pointer ABI there.
    private static bool IsByReferenceProjection(CXType type, IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides)
    {
        return TypeIsPointer(type.CanonicalType) &&
            TryGetManagedTypeOverride(type, managedTypeOverrides, out var pointerOverride) &&
            pointerOverride!.ByReference;
    }

    private static bool IsMarshallerBackedRecordValue(RecordBinding record)
    {
        return record.GetMetadata<bool>(MetadataKeys.MarshallerConfiguredRecord) ||
            !string.IsNullOrWhiteSpace(record.ManagedBaseTypeName) ||
            record.Members.OfType<FunctionBinding>().Any(static member => member.HasMetadata(MetadataKeys.MarshallerExposedMethod));
    }

    private static bool ShouldExposeFlattenedFieldAsIntPtr(
        CXType type,
        BindingType fieldType,
        IDictionary<string, RecordBinding> recordsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides)
    {
        if (!TypeIsPointer(type.CanonicalType) || fieldType.Kind != BindingTypeKind.Handle || string.IsNullOrWhiteSpace(fieldType.RecordId))
            return false;

        // A configured pointee (a shared wrapper such as Std.String) is materialized by the
        // pipeline, so it is not a record yet; it keeps its configured projection.
        if (TryGetManagedTypeOverride(type.PointeeType, managedTypeOverrides, out _))
            return false;

        var record = recordsByName.Values.Distinct().FirstOrDefault(candidate => string.Equals(candidate.SymbolId, fieldType.RecordId, StringComparison.Ordinal));
        return record is null || record.GetMetadata<bool>(MetadataKeys.PlaceholderRecord);
    }

    private static bool ShouldExposeCharPointerFieldAsIntPtr(CXType type)
    {
        var normalizedType = type.CanonicalType;
        if (!TypeIsPointer(normalizedType))
            return false;

        var pointeeType = normalizedType.PointeeType.CanonicalType;
        return pointeeType.kind is CXTypeKind.CXType_Char_S or
            CXTypeKind.CXType_SChar or
            CXTypeKind.CXType_Char_U or
            CXTypeKind.CXType_UChar;
    }

    private static string JoinFieldAccessPath(params string[] segments)
    {
        return string.Join('.', segments.Where(static segment => !string.IsNullOrWhiteSpace(segment)));
    }

    private static string ToFlattenedFieldNativeName(string accessPath)
    {
        return string.Join('_', accessPath.Split('.', StringSplitOptions.RemoveEmptyEntries).Select(SanitizeFlattenedFieldSegment));
    }

    private static string ToFlattenedFieldManagedName(string accessPath)
    {
        return string.Concat(accessPath.Split('.', StringSplitOptions.RemoveEmptyEntries).Select(ToManagedFlattenedFieldSegment));
    }

    private static string SanitizeFlattenedFieldSegment(string value)
    {
        var sanitized = Regex.Replace(value, "[^A-Za-z0-9_]+", "_").Trim('_');
        return string.IsNullOrWhiteSpace(sanitized) ? "field" : sanitized;
    }

    private static string ToManagedFlattenedFieldSegment(string value)
    {
        var managedName = ToPascalCase(SanitizeFlattenedFieldSegment(value));
        return !string.IsNullOrWhiteSpace(managedName) && (char.IsLetter(managedName[0]) || managedName[0] == '_')
            ? managedName
            : $"Field{managedName}";
    }

    private static bool IsIncompleteArrayType(CXType type)
    {
        return type.kind is CXTypeKind.CXType_IncompleteArray;
    }

    private static FunctionBinding CreateFunction(
        FunctionDecl functionDecl,
        string entryPointPrefix,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        IReadOnlyDictionary<string, BindingType> genericParameters)
    {
        var nativeName = functionDecl.Name ?? functionDecl.Spelling.ToString();
        var (returnType, ignoreReason) = TryMapType(functionDecl.ReturnType, recordsByName, enumsByName, managedTypeOverrides, genericParameters, isReturn: true);
        var cppReturnTypeName = ResolveQualifiedTypeName(
            functionDecl.ReturnType, recordsByName, genericParameters, returnType);
        var function = new FunctionBinding(
            BuildFunctionSymbolId("function", GetCppQualifiedName(functionDecl), functionDecl.Parameters.Select(static parameter => parameter.Type.AsString)),
            nativeName,
            ToPascalCase(nativeName),
            returnType,
            entryPointName: $"{entryPointPrefix}_{nativeName}",
            cppReturnTypeName: cppReturnTypeName,
            cppQualifiedName: GetCppQualifiedName(functionDecl))
        {
            ReturnPointerDepth = GetPointerDepth(functionDecl.ReturnType.Handle)
        };
        function.ReturnsOwnedHandle = ShouldReturnOwnedHandle(functionDecl.ReturnType, returnType, managedTypeOverrides);

        for (int parameterIndex = 0; parameterIndex < functionDecl.Parameters.Count; parameterIndex++)
        {
            var parameter = functionDecl.Parameters[parameterIndex];
            var parameterName = parameter.Name ?? parameter.Spelling.ToString();
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                parameterName = $"arg{parameterIndex}";
            }
            var managedParameterName = EscapeManagedIdentifier(parameterName);

            var (parameterType, direction, abiKind, parameterIgnoreReason) = TryMapParameter(functionDecl.Handle, parameter.Handle, parameter.Type, recordsByName, enumsByName, managedTypeOverrides, genericParameters);
            ignoreReason ??= parameterIgnoreReason;
            var freeParam = new ParameterBinding(
                $"parameter:{nativeName}:{parameterName}",
                parameterName,
                managedParameterName,
                parameterType,
                direction,
                abiKind,
                ResolveParameterCppTypeName(parameter.Type, recordsByName, genericParameters, parameterType));
            ApplyManagedDefaultValue(freeParam, parameter, parameterType, managedTypeOverrides);
            ApplyCallbackDelegateAttribute(functionDecl.Handle, parameter.Handle, freeParam, ref ignoreReason);
            function.Parameters.Add(freeParam);
        }

        ApplyInferredInputPointerArrayMetadata(function);

        function.EntryPointName = BuildEntrypointName(entryPointPrefix, SanitizeEntrypointNamePart(function.CppQualifiedName));

        if (ignoreReason is not null)
        {
            function.SetIgnoreReason(ignoreReason);
        }

        function.SetMetadata(MetadataKeys.DeclarationDisplayText, GetDeclarationDisplayText(functionDecl));

        return function;
    }

    private static FunctionBinding? CreateTemplateFunction(
        FunctionTemplateDecl templateDecl,
        string entryPointPrefix,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides)
    {
        if (!TryExtractTemplateParameters(templateDecl, out var templateParameters))
        {
            return null;
        }

        var genericParameters = CreateGenericParameterScope(templateDecl.TemplateParameters);
        var templatedFunction = templateDecl.TemplatedDecl;
        var nativeName = templateDecl.Name ?? templateDecl.Spelling.ToString();
        if (string.IsNullOrWhiteSpace(nativeName))
        {
            return null;
        }

        var (returnType, ignoreReason) = TryMapType(templatedFunction.ReturnType, recordsByName, enumsByName, managedTypeOverrides, genericParameters, isReturn: true);
        var cppReturnTypeName = ResolveQualifiedTypeName(
            templatedFunction.ReturnType, recordsByName, genericParameters, returnType);
        var function = new FunctionBinding(
            BuildFunctionSymbolId("function", GetCppQualifiedName(templateDecl), templatedFunction.Parameters.Select(static parameter => parameter.Type.AsString)),
            nativeName,
            ToPascalCase(nativeName),
            returnType,
            cppReturnTypeName: cppReturnTypeName,
            cppQualifiedName: GetCppQualifiedName(templateDecl))
        {
            ReturnPointerDepth = GetPointerDepth(templatedFunction.ReturnType.Handle)
        };
        function.ReturnsOwnedHandle = ShouldReturnOwnedHandle(templatedFunction.ReturnType, returnType, managedTypeOverrides);

        foreach (var templateParameter in templateParameters)
        {
            function.GenericParameters.Add(templateParameter);
        }

        ApplyExplicitGenericInstantiations(templateDecl, function, recordsByName, enumsByName, managedTypeOverrides);

        for (int parameterIndex = 0; parameterIndex < templatedFunction.Parameters.Count; parameterIndex++)
        {
            var parameter = templatedFunction.Parameters[parameterIndex];
            var parameterName = parameter.Name ?? parameter.Spelling.ToString();
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                parameterName = $"arg{function.Parameters.Count}";
            }
            var managedParameterName = EscapeManagedIdentifier(parameterName);

            var (parameterType, direction, abiKind, parameterIgnoreReason) = TryMapParameter(templatedFunction.Handle, parameter.Handle, parameter.Type, recordsByName, enumsByName, managedTypeOverrides, genericParameters);
            ignoreReason ??= parameterIgnoreReason;
            var freeTemplateParam = new ParameterBinding(
                $"parameter:{nativeName}:{parameterName}",
                parameterName,
                managedParameterName,
                parameterType,
                direction,
                abiKind,
                ResolveParameterCppTypeName(parameter.Type, recordsByName, genericParameters, parameterType));
            ApplyManagedDefaultValue(freeTemplateParam, parameter, parameterType, managedTypeOverrides);
            ApplyCallbackDelegateAttribute(templatedFunction.Handle, parameter.Handle, freeTemplateParam, ref ignoreReason);
            function.Parameters.Add(freeTemplateParam);
        }

        function.EntryPointName = BuildEntrypointName(entryPointPrefix, SanitizeEntrypointNamePart(function.CppQualifiedName));

        if (ignoreReason is not null)
        {
            function.SetIgnoreReason(ignoreReason);
        }

        function.SetMetadata(MetadataKeys.DeclarationDisplayText, GetDeclarationDisplayText(templateDecl));

        return function;
    }




    // ─── CXType-kind-based type-check helpers ──────────────────────────────
    // These bridge toward managed Type pattern matching (Phase 2a+).
    // In ClangSharp v20 the managed Type/Decl hierarchy constructors are
    // internal, preventing CXType→Type conversion from outside the assembly.
    // Once a public Type.From(CXType) or equivalent API ships, replace these
    // with:
    //   var mt = Type.From(type);
    //   if (mt is RecordType)  …  if (mt is EnumType)  …
    // For now, CX_TypeClass provides the same classification exposed as a
    // property on CXType (the public face of the managed type system).

    private static bool TypeIsLValueReference(CXType type) => type.TypeClass == CX_TypeClass.CX_TypeClass_LValueReference;
    private static bool TypeIsAnyReference(CXType type) => type.TypeClass is CX_TypeClass.CX_TypeClass_LValueReference or CX_TypeClass.CX_TypeClass_RValueReference;
    private static bool TypeIsPointer(CXType type) => type.TypeClass == CX_TypeClass.CX_TypeClass_Pointer;

    private static int GetPointerDepth(CXType type)
    {
        var depth = 0;
        var current = type.CanonicalType;
        while (TypeIsPointer(current))
        {
            ++depth;
            current = current.PointeeType.CanonicalType;
        }

        return depth;
    }

    private static bool IsPointerElementPointeeConst(CXType type)
    {
        var pointerType = type.CanonicalType;
        if (!TypeIsPointer(pointerType))
        {
            return false;
        }

        var elementType = pointerType.PointeeType.CanonicalType;
        return TypeIsPointer(elementType) && elementType.PointeeType.IsConstQualified;
    }

    // ClangSharp reads a bool argument through a signed one-bit value (true is -1).
    private static bool IsBooleanType(CXType type) => type.CanonicalType.kind == CXTypeKind.CXType_Bool;

    private static bool TypeIsFunctionProto(CXType type) => type.TypeClass is CX_TypeClass.CX_TypeClass_FunctionProto or CX_TypeClass.CX_TypeClass_FunctionNoProto;
    private static bool TypeIsRecord(CXType type) => type.TypeClass == CX_TypeClass.CX_TypeClass_Record;
    private static bool TypeIsEnum(CXType type) => type.TypeClass == CX_TypeClass.CX_TypeClass_Enum;

    private static (BindingType Type, string? IgnoreReason) TryMapType(
        ClangSharp.Type type,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        IReadOnlyDictionary<string, BindingType> genericParameters,
        bool isReturn = false,
        bool allowPrimitivePointer = false)
    {
        var genericTypeNode = type is ReferenceType referenceType
            ? referenceType.PointeeType
            : type;
        if (TryMapGenericParameterType(genericTypeNode, genericParameters, out var genericParameterType))
        {
            return (
                genericParameterType.WithCppIsReference(type is ReferenceType),
                null);
        }

        var (mappedType, ignoreReason) = TryMapType(
            type.Handle,
            recordsByName,
            enumsByName,
            managedTypeOverrides,
            genericParameters,
            isReturn,
            allowPrimitivePointer);
        if (ignoreReason is not null)
        {
            return (mappedType, ignoreReason);
        }

        try
        {
            return (ApplyAstTemplateArguments(genericTypeNode, mappedType, recordsByName, enumsByName, managedTypeOverrides, genericParameters), null);
        }
        catch (NotSupportedException exception)
        {
            return (CreateIgnoredFallbackType(type.Handle), exception.Message);
        }
    }

    private static (BindingType Type, string? IgnoreReason) TryMapType(
        CXType type,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        IReadOnlyDictionary<string, BindingType> genericParameters,
        bool isReturn = false,
        bool allowPrimitivePointer = false)
    {
        if (IsFunctionPointerType(type))
        {
            var normalizedType = type.CanonicalType;
            var isDirectFunctionPointer =
                TypeIsFunctionProto(normalizedType) ||
                TypeIsPointer(normalizedType) && TypeIsFunctionProto(normalizedType.PointeeType.CanonicalType);
            return (
                CreateIgnoredFallbackType(type),
                isDirectFunctionPointer
                    ? null
                    : "type is unsupported in this bootstrap surface: nested function pointer types are not yet supported");
        }

        try
        {
            return (MapType(
                type,
                recordsByName,
                enumsByName,
                managedTypeOverrides,
                genericParameters,
                isReturn,
                allowPrimitivePointer: allowPrimitivePointer), null);
        }
        catch (NotSupportedException exception)
        {
            return (CreateIgnoredFallbackType(type), exception.Message);
        }
    }

    private static (BindingType Type, BindingParameterDirection Direction, BindingParameterAbiKind AbiKind, string? IgnoreReason) TryMapParameter(
        CXCursor functionCursor,
        CXCursor parameterCursor,
        ClangSharp.Type parameterType,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        IReadOnlyDictionary<string, BindingType> genericParameters)
    {
        if (IsFunctionPointerType(parameterCursor.Type))
        {
            var (direction, abiKind) = InferFallbackParameterPassing(functionCursor, parameterCursor);
            return (CreateIgnoredFallbackType(parameterCursor.Type), direction, abiKind, null);
        }

        try
        {
            var (type, direction, abiKind) = MapParameter(functionCursor, parameterCursor, parameterType, recordsByName, enumsByName, managedTypeOverrides, genericParameters);
            return (
                ApplyAstTemplateArguments(parameterType, type, recordsByName, enumsByName, managedTypeOverrides, genericParameters),
                direction,
                abiKind,
                null);
        }
        catch (NotSupportedException exception)
        {
            var (direction, abiKind) = InferFallbackParameterPassing(functionCursor, parameterCursor);
            return (CreateIgnoredFallbackType(parameterCursor.Type), direction, abiKind, exception.Message);
        }
    }

    /// <summary>
    /// True for record types (or arrays of them) whose copy assignment is deleted or
    /// inaccessible, explicitly or implicitly through const/reference members and bases.
    /// Field setters assign, so such fields are read-only.
    /// </summary>
    private static bool IsNonAssignableType(ClangSharp.Type type)
    {
        var canonical = type.CanonicalType;
        while (canonical is ArrayType arrayType)
        {
            canonical = arrayType.ElementType.CanonicalType;
        }

        return canonical is RecordType &&
            canonical.AsCXXRecordDecl is { } record &&
            IsNonAssignableRecord(record, new HashSet<CXXRecordDecl>());
    }

    private static bool IsNonAssignableRecord(CXXRecordDecl record, HashSet<CXXRecordDecl> visited)
    {
        var definition = record.Definition as CXXRecordDecl ?? record;
        if (!visited.Add(definition))
        {
            return false;
        }

        foreach (var method in definition.Methods)
        {
            if (method.IsCopyAssignmentOperator)
            {
                return method.IsDeleted || method.Access == CX_CXXAccessSpecifier.CX_CXXPrivate;
            }
        }

        // A user-declared move operation implicitly deletes the copy assignment.
        if (definition.HasUserDeclaredMoveConstructor || definition.HasUserDeclaredMoveAssignment)
        {
            return true;
        }

        foreach (var baseSpecifier in definition.Bases)
        {
            if (baseSpecifier.Type.CanonicalType.AsCXXRecordDecl is { } baseRecord &&
                IsNonAssignableRecord(baseRecord, visited))
            {
                return true;
            }
        }

        foreach (var field in definition.Fields)
        {
            if (field.Type.CanonicalType.IsLocalConstQualified ||
                field.Type.CanonicalType is ReferenceType ||
                IsNonAssignableType(field.Type))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsNonCopyableRecord(CXXRecordDecl record, HashSet<CXXRecordDecl> visited)
    {
        var definition = record.Definition as CXXRecordDecl ?? record;
        if (!visited.Add(definition))
        {
            return false;
        }

        foreach (var constructor in definition.Ctors)
        {
            if (constructor.IsCopyConstructor)
            {
                return constructor.IsDeleted || constructor.Access == CX_CXXAccessSpecifier.CX_CXXPrivate;
            }
        }

        // A user-declared move operation implicitly deletes the copy constructor.
        if (definition.HasUserDeclaredMoveConstructor || definition.HasUserDeclaredMoveAssignment)
        {
            return true;
        }

        foreach (var baseSpecifier in definition.Bases)
        {
            if (baseSpecifier.Type.CanonicalType.AsCXXRecordDecl is { } baseRecord &&
                IsNonCopyableRecord(baseRecord, visited))
            {
                return true;
            }
        }

        foreach (var field in definition.Fields)
        {
            var fieldType = field.Type.CanonicalType;
            while (fieldType is ArrayType arrayType)
            {
                fieldType = arrayType.ElementType.CanonicalType;
            }

            if (fieldType is RecordType && fieldType.AsCXXRecordDecl is { } fieldRecord &&
                IsNonCopyableRecord(fieldRecord, visited))
            {
                return true;
            }
        }

        return false;
    }

    // A reference to a record projected as a managed value crosses as a pointer to the
    // value, as for parameters (see MapParameter); ref-struct projections cross by value.
    private static BindingType MarkProjectedValueReference(ClangSharp.Type sourceType, BindingType mappedType)
    {
        return sourceType is LValueReferenceType referenceType &&
            referenceType.PointeeType.CanonicalType is RecordType &&
            mappedType.Kind == BindingTypeKind.Primitive &&
            !mappedType.Marshaller.IsRefStruct
                ? mappedType.WithCppIsReference(true)
                : mappedType;
    }

    private static BindingType ApplyAstTemplateArguments(
        ClangSharp.Type sourceType,
        BindingType mappedType,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        IReadOnlyDictionary<string, BindingType> genericParameters)
    {
        if (!TryGetAstTemplateArguments(sourceType, out var astArguments, out var writtenArgumentCount))
        {
            return ApplyCxAstTemplateArguments(
                sourceType.Handle,
                mappedType,
                recordsByName,
                enumsByName,
                managedTypeOverrides,
                genericParameters);
        }

        if (mappedType.RecordId is not null &&
            TryGetAstClassTemplateDeclaration(sourceType, out var classTemplate) &&
            recordsByName.Values.FirstOrDefault(record =>
                string.Equals(record.SymbolId, mappedType.RecordId, StringComparison.Ordinal)) is { } templateRecord)
        {
            var hadNativeTemplateShape = templateRecord.NativeTemplateParameterCount > 0;
            templateRecord.NativeTemplateParameterCount = Math.Max(
                templateRecord.NativeTemplateParameterCount,
                classTemplate.TemplateParameters.Count);
            var requiredParameterCount = GetRequiredNativeTemplateParameterCount(classTemplate);
            templateRecord.NativeTemplateRequiredParameterCount = hadNativeTemplateShape
                ? Math.Min(templateRecord.NativeTemplateRequiredParameterCount, requiredParameterCount)
                : requiredParameterCount;
        }

        if (mappedType.IsCallback &&
            astArguments.Count == 1 &&
            astArguments[0].Kind == CXTemplateArgumentKind.CXTemplateArgumentKind_Type &&
            astArguments[0].AsType.CanonicalType is FunctionProtoType callbackType)
        {
            var callbackReturnType = ApplyAstTemplateArguments(
                callbackType.ReturnType,
                mappedType.CallbackReturnType!,
                recordsByName,
                enumsByName,
                managedTypeOverrides,
                genericParameters);
            var callbackParameterTypes = callbackType.ParamTypes
                .Select((parameterType, index) => MarkProjectedValueReference(
                    parameterType,
                    ApplyAstTemplateArguments(
                        parameterType,
                        mappedType.CallbackParameterTypes[index],
                        recordsByName,
                        enumsByName,
                        managedTypeOverrides,
                        genericParameters)))
                .ToArray();
            mappedType = mappedType.WithCallbackSignature(callbackReturnType, callbackParameterTypes);
        }

        var nativeArguments = new List<BindingTemplateArgument>(astArguments.Count);
        for (var argumentIndex = 0; argumentIndex < astArguments.Count; ++argumentIndex)
        {
            var argument = astArguments[argumentIndex];
            switch (argument.Kind)
            {
                case CXTemplateArgumentKind.CXTemplateArgumentKind_Type:
                    // Defaulted arguments keep the default expression's sugar, which is only
                    // valid in the template's scope; spell them canonically.
                    var argumentType = argumentIndex < writtenArgumentCount ? argument.AsType : argument.AsType.CanonicalType;
                    var mappedArgument = argumentType.CanonicalType is FunctionType
                        ? BindingType.CreateConfigured(
                            FormatCppTypeFromAst(argumentType, recordsByName, genericParameters),
                            FormatCppTypeFromAst(argumentType, recordsByName, genericParameters))
                        : argumentIndex < mappedType.GenericArguments.Count
                        ? mappedType.GenericArguments[argumentIndex]
                        : MapType(
                            argumentType.Handle,
                            recordsByName,
                            enumsByName,
                            managedTypeOverrides,
                            genericParameters,
                            preserveAliasSurface: false);
                    mappedArgument = ApplyAstTemplateArguments(
                        argumentType,
                        mappedArgument,
                        recordsByName,
                        enumsByName,
                        managedTypeOverrides,
                        genericParameters);
                    mappedArgument = mappedArgument.WithCppTypeName(
                        FormatCppTypeFromAst(argumentType, recordsByName, genericParameters));
                    nativeArguments.Add(BindingTemplateArgument.FromType(mappedArgument));
                    break;

                case CXTemplateArgumentKind.CXTemplateArgumentKind_Integral:
                    nativeArguments.Add(BindingTemplateArgument.FromIntegral(argument.AsIntegral, IsBooleanType(argument.IntegralType.Handle)));
                    break;

                case CXTemplateArgumentKind.CXTemplateArgumentKind_Expression when
                    TryGetIntegralTemplateArgument(argument.AsExpr, out var integralValue, out var isBoolean):
                    nativeArguments.Add(BindingTemplateArgument.FromIntegral(integralValue, isBoolean));
                    break;

                default:
                    return mappedType;
            }
        }

        if (nativeArguments.Count == 0)
        {
            return mappedType;
        }

        if (mappedType.Kind == BindingTypeKind.Handle &&
            mappedType.GenericArguments.Count == 0 &&
            mappedType.RecordId is not null &&
            (recordsByName.Values.Any(record =>
                 string.Equals(record.SymbolId, mappedType.RecordId, StringComparison.Ordinal) &&
                 (record.IsGeneric || record.ManagedTypeFormat?.Contains('{', StringComparison.Ordinal) == true)) ||
             HasGenericManagedProjection(sourceType.Handle, managedTypeOverrides)))
        {
            mappedType = mappedType.WithGenericArguments(
                nativeArguments
                    .Where(static argument => argument.Kind == BindingTemplateArgumentKind.Type)
                    .Select(static argument => argument.Type!));
        }
        else if (!mappedType.IsCallback)
        {
            var astManagedArguments = nativeArguments
                .Where(static argument => argument.Kind == BindingTemplateArgumentKind.Type)
                .Select(static argument => argument.Type!)
                .ToArray();
            if (mappedType.GenericArguments.Count == nativeArguments.Count ||
                mappedType.GenericArguments.Count == astManagedArguments.Length)
            {
                mappedType = mappedType.WithGenericArguments(astManagedArguments);
            }
        }

        mappedType = mappedType.WithNativeTemplateArguments(nativeArguments, astVerified: true);

        var fixedArrayRecord = mappedType.RecordId is not null && recordsByName.Values.Any(record =>
            string.Equals(record.SymbolId, mappedType.RecordId, StringComparison.Ordinal) &&
            record.GetMetadata<bool>(MetadataKeys.FixedArrayRecord));
        if ((mappedType.IsFixedArray || fixedArrayRecord) &&
            nativeArguments.FirstOrDefault(static argument => argument.Kind == BindingTemplateArgumentKind.Type)?.Type is { } elementType &&
            nativeArguments.FirstOrDefault(static argument => argument.Kind == BindingTemplateArgumentKind.Integral) is { } lengthArgument &&
            lengthArgument.IntegralValue is >= 0 and <= int.MaxValue)
        {
            mappedType = mappedType.WithFixedArray(elementType, checked((int)lengthArgument.IntegralValue));
        }

        return mappedType;
    }

    private static BindingType ApplyCxAstTemplateArguments(
        CXType sourceType,
        BindingType mappedType,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        IReadOnlyDictionary<string, BindingType> genericParameters)
    {
        if (sourceType.NumTemplateArguments <= 0)
            return mappedType;

        // Written arguments keep their sugar; defaulted trailing arguments come from the
        // canonical specialization (see CompleteWithDefaultedTemplateArguments).
        var writtenCount = sourceType.NumTemplateArguments;
        var canonicalType = sourceType.CanonicalType;
        var argumentCount = Math.Max(writtenCount, canonicalType.NumTemplateArguments);
        var nativeArguments = new List<BindingTemplateArgument>(argumentCount);
        for (var index = 0; index < argumentCount; ++index)
        {
            var argument = (index < writtenCount ? sourceType : canonicalType).GetTemplateArgument(checked((uint)index));
            try
            {
                if (argument.AsType.kind != CXTypeKind.CXType_Invalid)
                {
                    // Defaulted arguments keep the sugar of the default expression, which is
                    // only valid in the template's scope; spell them canonically.
                    var argumentType = index < writtenCount ? argument.AsType : argument.AsType.CanonicalType;
                    var cppTypeName = index < writtenCount
                        ? ResolveQualifiedTypeName(argumentType, recordsByName, genericParameters)
                        : FormatCanonicalCxTypeName(argumentType);
                    var mappedArgument = TypeIsFunctionProto(argument.AsType.CanonicalType)
                        ? BindingType.CreateConfigured(cppTypeName, cppTypeName)
                        : index < mappedType.GenericArguments.Count
                            ? mappedType.GenericArguments[index]
                            : MapType(
                                argumentType,
                                recordsByName,
                                enumsByName,
                                managedTypeOverrides,
                                genericParameters,
                                preserveAliasSurface: false);
                    nativeArguments.Add(BindingTemplateArgument.FromType(mappedArgument.WithCppTypeName(cppTypeName)));
                    continue;
                }

                if (argument.IntegralType.kind != CXTypeKind.CXType_Invalid)
                {
                    nativeArguments.Add(BindingTemplateArgument.FromIntegral(argument.AsIntegral, IsBooleanType(argument.IntegralType)));
                    continue;
                }

                return mappedType;
            }
            finally
            {
                argument.Dispose();
            }
        }

        if (nativeArguments.Count == 0)
            return mappedType;

        if (mappedType.Kind == BindingTypeKind.Handle &&
            mappedType.GenericArguments.Count == 0 &&
            mappedType.RecordId is not null &&
            (recordsByName.Values.Any(record =>
                 string.Equals(record.SymbolId, mappedType.RecordId, StringComparison.Ordinal) &&
                 (record.IsGeneric || record.ManagedTypeFormat?.Contains('{', StringComparison.Ordinal) == true)) ||
             HasGenericManagedProjection(sourceType, managedTypeOverrides)))
        {
            mappedType = mappedType.WithGenericArguments(
                nativeArguments
                    .Where(static argument => argument.Kind == BindingTemplateArgumentKind.Type)
                    .Select(static argument => argument.Type!));
        }

        return mappedType.WithNativeTemplateArguments(nativeArguments, astVerified: true);
    }

    private static bool HasGenericManagedProjection(
        CXType sourceType,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides)
    {
        var projectedType = sourceType;
        while (TypeIsPointer(projectedType.CanonicalType) || TypeIsAnyReference(projectedType.CanonicalType))
            projectedType = projectedType.PointeeType;

        return TryGetManagedTypeOverride(projectedType, managedTypeOverrides, out var managedTypeOverride) &&
            managedTypeOverride!.PublicTypeName.Contains('{', StringComparison.Ordinal);
    }

    private static bool TryGetIntegralTemplateArgument(Expr expression, out long value, out bool isBoolean)
    {
        isBoolean = false;
        switch (expression)
        {
            case IntegerLiteral literal:
                value = literal.Value;
                return true;
            case CXXBoolLiteralExpr literal:
                value = literal.Value ? 1 : 0;
                isBoolean = true;
                return true;
            case ImplicitCastExpr implicitCast:
                return TryGetIntegralTemplateArgument(implicitCast.SubExpr, out value, out isBoolean);
            case ParenExpr parenthesized:
                return TryGetIntegralTemplateArgument(parenthesized.SubExpr, out value, out isBoolean);
            // An alias template forwarding a non-type parameter substitutes it in place.
            case SubstNonTypeTemplateParmExpr substitution:
                return TryGetIntegralTemplateArgument(substitution.Replacement, out value, out isBoolean);
            default:
                value = 0;
                return false;
        }
    }

    /// <summary>
    /// Written arguments keep their sugar (aliases drive the managed surface); defaulted
    /// trailing arguments come from the canonical specialization. Every spelling of one
    /// C++ type then yields the same native shape (provider shape keys, entry points).
    /// </summary>
    private static IReadOnlyList<TemplateArgument> CompleteWithDefaultedTemplateArguments(TemplateSpecializationType templateType)
    {
        var written = templateType.Args;
        // Dependent specializations have no canonical ClassTemplateSpecializationDecl.
        if (written.Any(static argument => argument.Kind == CXTemplateArgumentKind.CXTemplateArgumentKind_Pack || argument.IsPackExpansion) ||
            templateType.CanonicalType is not RecordType { Decl: ClassTemplateSpecializationDecl specialization })
        {
            return written;
        }

        var canonical = specialization.TemplateArgs;
        if (canonical.Count <= written.Count ||
            canonical.Any(static argument => argument.Kind == CXTemplateArgumentKind.CXTemplateArgumentKind_Pack))
        {
            return written;
        }

        return written.Concat(canonical.Skip(written.Count)).ToArray();
    }

    private static bool TryGetAstTemplateArguments(
        ClangSharp.Type type,
        out IReadOnlyList<TemplateArgument> arguments,
        out int writtenCount)
    {
        switch (type)
        {
            case ReferenceType referenceType:
                return TryGetAstTemplateArguments(referenceType.PointeeType, out arguments, out writtenCount);
            case PointerType pointerType:
                return TryGetAstTemplateArguments(pointerType.PointeeType, out arguments, out writtenCount);
            case TypedefType typedefType:
                return TryGetAstTemplateArguments(typedefType.Desugar, out arguments, out writtenCount);
            case ElaboratedType elaboratedType:
                return TryGetAstTemplateArguments(elaboratedType.NamedType, out arguments, out writtenCount);
            case SubstTemplateTypeParmType substitutionType:
                return TryGetAstTemplateArguments(substitutionType.ReplacementType, out arguments, out writtenCount);
            case TemplateSpecializationType { IsTypeAlias: true } templateType:
                return TryGetAstTemplateArguments(templateType.AliasedType, out arguments, out writtenCount);
            case TemplateSpecializationType templateType:
                arguments = CompleteWithDefaultedTemplateArguments(templateType);
                writtenCount = templateType.Args.Count;
                return arguments.Count > 0;
            case RecordType { Decl: ClassTemplateSpecializationDecl specialization }:
                arguments = specialization.TemplateArgs;
                writtenCount = arguments.Count;
                return arguments.Count > 0;
            default:
                if (type.IsSugared && type.Desugar.Handle != type.Handle &&
                    TryGetAstTemplateArguments(type.Desugar, out arguments, out writtenCount))
                {
                    return true;
                }
                if (type.AsCXXRecordDecl is ClassTemplateSpecializationDecl fallbackSpecialization)
                {
                    arguments = fallbackSpecialization.TemplateArgs;
                    writtenCount = arguments.Count;
                    return arguments.Count > 0;
                }
                if (type.CanonicalType.Handle != type.Handle &&
                    TryGetAstTemplateArguments(type.CanonicalType, out arguments, out writtenCount))
                {
                    return true;
                }
                arguments = Array.Empty<TemplateArgument>();
                writtenCount = 0;
                return false;
        }
    }

    private static bool TryGetAstClassTemplateDeclaration(
        ClangSharp.Type type,
        out ClassTemplateDecl classTemplate)
    {
        switch (type)
        {
            case ReferenceType referenceType:
                return TryGetAstClassTemplateDeclaration(referenceType.PointeeType, out classTemplate);
            case PointerType pointerType:
                return TryGetAstClassTemplateDeclaration(pointerType.PointeeType, out classTemplate);
            case TypedefType typedefType:
                return TryGetAstClassTemplateDeclaration(typedefType.Desugar, out classTemplate);
            case ElaboratedType elaboratedType:
                return TryGetAstClassTemplateDeclaration(elaboratedType.NamedType, out classTemplate);
            case SubstTemplateTypeParmType substitutionType:
                return TryGetAstClassTemplateDeclaration(substitutionType.ReplacementType, out classTemplate);
            case TemplateSpecializationType { IsTypeAlias: true } templateType:
                return TryGetAstClassTemplateDeclaration(templateType.AliasedType, out classTemplate);
            case TemplateSpecializationType templateType when
                templateType.TemplateName.AsTemplateDecl is ClassTemplateDecl templateDeclaration:
                classTemplate = templateDeclaration;
                return true;
            case RecordType { Decl: ClassTemplateSpecializationDecl specialization } when
                MarshallerConfigExtractor.GetSpecializedTemplateDecl(specialization) is ClassTemplateDecl specializationDeclaration:
                classTemplate = specializationDeclaration;
                return true;
            default:
                if (type.IsSugared && type.Desugar.Handle != type.Handle &&
                    TryGetAstClassTemplateDeclaration(type.Desugar, out classTemplate))
                {
                    return true;
                }
                if (type.AsCXXRecordDecl is ClassTemplateSpecializationDecl fallbackSpecialization &&
                    MarshallerConfigExtractor.GetSpecializedTemplateDecl(fallbackSpecialization) is ClassTemplateDecl fallbackDeclaration)
                {
                    classTemplate = fallbackDeclaration;
                    return true;
                }
                if (type.CanonicalType.Handle != type.Handle &&
                    TryGetAstClassTemplateDeclaration(type.CanonicalType, out classTemplate))
                {
                    return true;
                }
                classTemplate = null!;
                return false;
        }
    }

    private static (BindingParameterDirection Direction, BindingParameterAbiKind AbiKind) InferFallbackParameterPassing(CXCursor functionCursor, CXCursor parameterCursor)
    {
        var type = parameterCursor.Type;
        var normalizedType = type.CanonicalType;
        var explicitDirection = TryGetPassKindOverride(functionCursor, parameterCursor, out var overrideDirection)
            ? overrideDirection
            : (BindingParameterDirection?)null;

        if (TypeIsFunctionProto(normalizedType) || (TypeIsPointer(normalizedType) && TypeIsFunctionProto(normalizedType.PointeeType.CanonicalType)))
        {
            return (explicitDirection ?? BindingParameterDirection.In, BindingParameterAbiKind.Value);
        }

        if (TypeIsLValueReference(normalizedType))
        {
            var sourcePointeeType = type.PointeeType;
            var normalizedPointeeType = normalizedType.PointeeType.CanonicalType;
            return (
                explicitDirection ?? (sourcePointeeType.IsConstQualified ? BindingParameterDirection.In : BindingParameterDirection.Ref),
                TypeIsRecord(normalizedPointeeType) ? BindingParameterAbiKind.Value : BindingParameterAbiKind.Reference);
        }

        if (TypeIsPointer(normalizedType))
        {
            var sourcePointeeType = type.PointeeType;
            var normalizedPointeeType = normalizedType.PointeeType.CanonicalType;
            return (
                explicitDirection ?? (sourcePointeeType.IsConstQualified ? BindingParameterDirection.In : BindingParameterDirection.Out),
                normalizedPointeeType.kind == CXTypeKind.CXType_Void || TypeIsRecord(normalizedPointeeType) ? BindingParameterAbiKind.Value : BindingParameterAbiKind.Pointer);
        }

        return (explicitDirection ?? BindingParameterDirection.In, BindingParameterAbiKind.Value);
    }

    private static BindingType CreateIgnoredFallbackType(CXType type)
    {
        return BindingType.CreateConfigured(
            NormalizeTypeName(type.Spelling.ToString()),
            BindingType.IntPtr.ManagedTypeName,
            primitiveKind: BindingPrimitiveKind.IntPtr);
    }

    private static void ApplyOperatorMetadata(FunctionBinding function, Decl decl)
    {
        if (decl is CXXConversionDecl conversionDecl)
        {
            function.IsConversionOperator = true;
            function.IsExplicitConversionOperator = conversionDecl.IsExplicit;
            return;
        }

        var functionDecl = decl as FunctionDecl;
        if (functionDecl is null || !functionDecl.IsOverloadedOperator)
        {
            return;
        }

        function.OperatorToken = functionDecl.OverloadedOperator switch
        {
            CX_OverloadedOperatorKind.CX_OO_Plus => "+",
            CX_OverloadedOperatorKind.CX_OO_Minus => "-",
            CX_OverloadedOperatorKind.CX_OO_Exclaim => "!",
            CX_OverloadedOperatorKind.CX_OO_EqualEqual => "==",
            CX_OverloadedOperatorKind.CX_OO_ExclaimEqual => "!=",
            CX_OverloadedOperatorKind.CX_OO_Less => "<",
            CX_OverloadedOperatorKind.CX_OO_Greater => ">",
            _ => null
        };
    }

    private static string GetManagedMethodName(Decl decl, string nativeName)
    {
        if (decl is CXXConversionDecl conversionDecl)
        {
            return $"OperatorTo{ToPascalCase(SanitizeEntrypointNamePart(conversionDecl.ReturnType.Handle.Spelling.ToString()))}";
        }

        var functionDecl = decl as FunctionDecl;
        if (functionDecl is not null && functionDecl.IsOverloadedOperator)
        {
            return functionDecl.OverloadedOperator switch
            {
                CX_OverloadedOperatorKind.CX_OO_Plus => functionDecl.Parameters.Count == 0 ? "OperatorUnaryPlus" : "OperatorPlus",
                CX_OverloadedOperatorKind.CX_OO_Minus => functionDecl.Parameters.Count == 0 ? "OperatorUnaryMinus" : "OperatorMinus",
                CX_OverloadedOperatorKind.CX_OO_Exclaim => "OperatorNot",
                CX_OverloadedOperatorKind.CX_OO_EqualEqual => "OperatorEquals",
                CX_OverloadedOperatorKind.CX_OO_ExclaimEqual => "OperatorNotEquals",
                CX_OverloadedOperatorKind.CX_OO_Less => "OperatorLessThan",
                CX_OverloadedOperatorKind.CX_OO_Greater => "OperatorGreaterThan",
                _ => ToPascalCase(nativeName)
            };
        }

        return ToPascalCase(nativeName);
    }

    private static string GetEntrypointSuffix(Decl decl, string nativeName)
    {
        var methodDecl = decl as CXXMethodDecl;
        var constSuffix = methodDecl?.IsConst == true ? "_const" : string.Empty;

        if (decl is CXXConversionDecl)
        {
            return $"operator_to{constSuffix}";
        }

        var functionDecl = decl as FunctionDecl;
        if (functionDecl is not null && functionDecl.IsOverloadedOperator)
        {
            return (functionDecl.OverloadedOperator switch
            {
                CX_OverloadedOperatorKind.CX_OO_Plus => functionDecl.Parameters.Count == 0 ? "operator_unary_plus" : "operator_plus",
                CX_OverloadedOperatorKind.CX_OO_Minus => functionDecl.Parameters.Count == 0 ? "operator_unary_minus" : "operator_minus",
                CX_OverloadedOperatorKind.CX_OO_Exclaim => "operator_not",
                CX_OverloadedOperatorKind.CX_OO_EqualEqual => "operator_equal_equal",
                CX_OverloadedOperatorKind.CX_OO_ExclaimEqual => "operator_not_equal",
                CX_OverloadedOperatorKind.CX_OO_Less => "operator_less",
                CX_OverloadedOperatorKind.CX_OO_Greater => "operator_greater",
                _ => nativeName
            }) + constSuffix;
        }

        return nativeName + constSuffix;
    }

    private static void ApplyDeletedCallableIgnore(FunctionDecl functionDecl, FunctionBinding function)
    {
        if (!functionDecl.IsDeleted)
        {
            return;
        }

        function.SetMetadata(MetadataKeys.NativeCallableDeleted, true);
        var callableLabel = function.IsConstructor
            ? $"constructor '{function.CppQualifiedName}'"
            : $"callable '{function.CppQualifiedName}'";
        function.SetSilentExclusionReason($"{callableLabel} is deleted in the native surface");
    }

    private static void ApplyCallableDefinitionMetadata(Decl decl, FunctionBinding function)
    {
        function.SetMetadata(MetadataKeys.HasNativeDefinition, (decl as FunctionDecl)?.IsThisDeclarationADefinition ?? false);

        if (function.IsIgnored())
        {
            return;
        }

        var declarationText = GetDeclarationDisplayText(decl);
        if (declarationText.Contains("...", StringComparison.Ordinal))
        {
            function.SetIgnoreReason("variadic callables are not yet supported in this bootstrap surface");
        }
    }

    private static void ApplyDestructorMetadata(CXXRecordDecl? cxxRecord, RecordBinding record)
    {
        var destructor = cxxRecord?.Destructor;
        if (destructor is not null && destructor.Access != CX_CXXAccessSpecifier.CX_CXXPublic)
        {
            record.SetMetadata(MetadataKeys.NonPublicDestructor, true);
        }
    }

    private static int s_entrypointCounter;

    private static string BuildEntrypointName(string entryPointPrefix, string baseName)
    {
        var counter = ++s_entrypointCounter;
        // Operator spellings (operator=, operator[]) are not identifier characters; the
        // counter keeps sanitized names unique.
        var identifier = new string(baseName.Select(static character => char.IsAsciiLetterOrDigit(character) ? character : '_').ToArray());
        return $"{entryPointPrefix}_{identifier}_{counter}";
    }

    private static string BuildMemberSymbolId(
        string kind,
        RecordBinding record,
        string nativeName,
        IEnumerable<string> parameterTypes,
        bool isConst)
    {
        var signature = string.Join(",", parameterTypes.Select(static type => NormalizeTypeName(type)));
        return $"{kind}:{record.CppQualifiedName}:{nativeName}({signature}){(isConst ? ":const" : string.Empty)}";
    }

    private static string BuildFunctionSymbolId(string kind, string qualifiedName, IEnumerable<string> parameterTypes)
    {
        var signature = string.Join(",", parameterTypes.Select(static type => NormalizeTypeName(type)));
        return $"{kind}:{qualifiedName}({signature})";
    }

    private static string SanitizeEntrypointNamePart(string value)
    {
        return NormalizeTypeName(value)
            .Replace("const ", "const_", StringComparison.Ordinal)
            .Replace("::", "_", StringComparison.Ordinal)
            .Replace("*", "ptr", StringComparison.Ordinal)
            .Replace("&", "ref", StringComparison.Ordinal)
            .Replace("<", "_", StringComparison.Ordinal)
            .Replace(">", "_", StringComparison.Ordinal)
            .Replace(",", "_", StringComparison.Ordinal)
            .Replace(" ", "_", StringComparison.Ordinal);
    }

    private static (BindingType Type, BindingParameterDirection Direction, BindingParameterAbiKind AbiKind) MapParameter(
        CXCursor functionCursor,
        CXCursor parameterCursor,
        ClangSharp.Type parameterType,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        IReadOnlyDictionary<string, BindingType> genericParameters)
    {
        var type = parameterCursor.Type;
        var normalizedType = type.CanonicalType;
        var explicitDirection = TryGetPassKindOverride(functionCursor, parameterCursor, out var overrideDirection)
            ? overrideDirection
            : (BindingParameterDirection?)null;

        if (TryMapGenericParameterType(parameterType, genericParameters, out var directGenericParameterType))
        {
            return (
                directGenericParameterType.WithCppIsReference(parameterType is ReferenceType),
                explicitDirection ?? BindingParameterDirection.In,
                parameterType is ReferenceType
                    ? BindingParameterAbiKind.Reference
                    : BindingParameterAbiKind.Value);
        }

        if (IsIncompleteArrayType(normalizedType))
        {
            return MapIncompleteArrayParameter(
                functionCursor,
                type,
                explicitDirection,
                recordsByName,
                enumsByName,
                managedTypeOverrides,
                genericParameters);
        }

        if (TypeIsLValueReference(normalizedType))
        {
            var sourcePointeeType = type.PointeeType;
            var normalizedPointeeType = normalizedType.PointeeType.CanonicalType;
            var sourcePointeeTypeName = NormalizeTypeName(sourcePointeeType.Spelling.ToString());

            var pointeeAstType = parameterType is ReferenceType referenceType
                ? referenceType.PointeeType
                : parameterType.PointeeType;
            if (TryMapGenericParameterType(pointeeAstType, genericParameters, out var genericParameterType))
            {
                return (
                    genericParameterType.WithCppIsReference(true),
                    explicitDirection ?? (sourcePointeeType.IsConstQualified ? BindingParameterDirection.In : BindingParameterDirection.Ref),
                    BindingParameterAbiKind.Reference);
            }

            if (TypeIsRecord(normalizedPointeeType))
            {
                var mappedType = MapType(normalizedPointeeType, recordsByName, enumsByName, managedTypeOverrides, genericParameters);
                // A non-const reference lets the callee replace the function object, which a
                // managed delegate cannot observe; it is a ref parameter, not an input.
                if (mappedType.IsCallback)
                {
                    return sourcePointeeType.IsConstQualified
                        ? (mappedType, explicitDirection ?? BindingParameterDirection.In, BindingParameterAbiKind.Value)
                        : (mappedType, explicitDirection ?? BindingParameterDirection.Ref, BindingParameterAbiKind.Reference);
                }

                // A ref-struct projection (a span over a string view) has no by-reference
                // form; the view itself crosses by value.
                var isProjectedValue = mappedType.Kind == BindingTypeKind.Primitive && !mappedType.Marshaller.IsRefStruct;
                var abiKind = isProjectedValue
                    ? BindingParameterAbiKind.Reference
                    : BindingParameterAbiKind.Value;
                var direction = explicitDirection ??
                    (isProjectedValue && !sourcePointeeType.IsConstQualified
                        ? BindingParameterDirection.Ref
                        : BindingParameterDirection.In);
                return (mappedType, direction, abiKind);
            }

            if (TypeIsPointer(normalizedPointeeType) &&
                managedTypeOverrides.TryGetValue(sourcePointeeTypeName, out var pointeeOverride) &&
                IsManagedRefStruct(pointeeOverride.PublicTypeName))
            {
                return (
                    MapType(sourcePointeeType, recordsByName, enumsByName, managedTypeOverrides, genericParameters),
                    explicitDirection ?? BindingParameterDirection.In,
                    BindingParameterAbiKind.Value);
            }

            return (
                MapType(normalizedPointeeType, recordsByName, enumsByName, managedTypeOverrides, genericParameters),
                explicitDirection ?? (sourcePointeeType.IsConstQualified ? BindingParameterDirection.In : BindingParameterDirection.Ref),
                BindingParameterAbiKind.Reference);
        }

        if (TypeIsPointer(normalizedType))
        {
            var normalizedTypeName = NormalizeTypeName(type.Spelling.ToString());
            // An explicit out/ref writes the pointee, as for every other pointer; a configured
            // pointer token (a byte buffer as IntPtr) describes the pointer itself.
            if (TryGetManagedTypeOverride(normalizedTypeName, normalizedTypeName, normalizedTypeName, managedTypeOverrides, isReturn: false, out var pointerOverride) &&
                (pointerOverride!.ByReference || explicitDirection is not (BindingParameterDirection.Out or BindingParameterDirection.Ref)))
            {
                // out/ref modifiers already pass a by-reference projection by address; an
                // input one passes it with `in`.
                var direction = explicitDirection ?? BindingParameterDirection.In;
                return (
                    MapType(type, recordsByName, enumsByName, managedTypeOverrides, genericParameters),
                    direction,
                    pointerOverride!.ByReference && direction == BindingParameterDirection.In
                        ? BindingParameterAbiKind.Pointer
                        : BindingParameterAbiKind.Value);
            }

            var sourcePointeeType = type.PointeeType;
            var normalizedPointeeType = normalizedType.PointeeType.CanonicalType;

            // A single pointer parameter to a configured value type is a direct ABI
            // projection: C# passes the value by address and C++ receives T*. Keep
            // fields and returns opaque unless they have their own pointer marshaller.
            if (GetPointerDepth(type) == 1 &&
                sourcePointeeType.IsConstQualified &&
                TryGetManagedTypeOverride(sourcePointeeType, managedTypeOverrides, out var pointeeOverride) &&
                !pointeeOverride!.HasManagedBaseType &&
                !pointeeOverride.HasExposedMembers &&
                string.IsNullOrWhiteSpace(pointeeOverride.ManagedWrapperAssembly))
            {
                return (
                    MapType(sourcePointeeType, recordsByName, enumsByName, managedTypeOverrides, genericParameters),
                    explicitDirection ?? BindingParameterDirection.In,
                    BindingParameterAbiKind.Pointer);
            }

            if (normalizedPointeeType.kind == CXTypeKind.CXType_Void || TypeIsRecord(normalizedPointeeType))
            {
                return (
                    MapType(type, recordsByName, enumsByName, managedTypeOverrides, genericParameters),
                    explicitDirection ?? BindingParameterDirection.In,
                    BindingParameterAbiKind.Value);
            }

            return (
                MapType(normalizedPointeeType, recordsByName, enumsByName, managedTypeOverrides, genericParameters,
                    allowPrimitivePointer: true),
                explicitDirection ?? (sourcePointeeType.IsConstQualified ? BindingParameterDirection.In : BindingParameterDirection.Out),
                BindingParameterAbiKind.Pointer);
        }

        return (MapType(type, recordsByName, enumsByName, managedTypeOverrides, genericParameters), explicitDirection ?? BindingParameterDirection.In, BindingParameterAbiKind.Value);
    }

    private static (BindingType Type, BindingParameterDirection Direction, BindingParameterAbiKind AbiKind) MapIncompleteArrayParameter(
        CXCursor functionCursor,
        CXType type,
        BindingParameterDirection? explicitDirection,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        IReadOnlyDictionary<string, BindingType> genericParameters)
    {
        if (functionCursor.Kind != CXCursorKind.CXCursor_CXXMethod || functionCursor.CXXMethod_IsStatic)
        {
            throw new NotSupportedException("incomplete array parameters are only supported on instance methods in this bootstrap surface.");
        }

        if (explicitDirection is { } direction && direction != BindingParameterDirection.In)
        {
            throw new NotSupportedException("incomplete array parameters only support input passing in this bootstrap surface.");
        }

        var elementType = type.ArrayElementType;
        var normalizedElementType = elementType.CanonicalType;
        if (!TypeIsPointer(normalizedElementType))
        {
            throw new NotSupportedException("incomplete array parameters require pointer elements in this bootstrap surface.");
        }

        var mappedElementType = MapType(elementType, recordsByName, enumsByName, managedTypeOverrides, genericParameters);
        if (mappedElementType.Kind != BindingTypeKind.Handle)
        {
            throw new NotSupportedException("incomplete array parameters require handle pointer elements in this bootstrap surface.");
        }

        return (mappedElementType, BindingParameterDirection.In, BindingParameterAbiKind.Pointer);
    }

    private static bool TryGetManagedTypeOverride(
        string spelledTypeName,
        string canonicalTypeName,
        string qualifiedSpelledTypeName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        bool isReturn,
        out ManagedTypeOverride? managedTypeOverride)
        => TryGetManagedTypeOverride(
            spelledTypeName,
            canonicalTypeName,
            qualifiedSpelledTypeName,
            managedTypeOverrides,
            isReturn,
            predicate: null,
            out managedTypeOverride);

    private static bool TryGetManagedTypeOverride(
        string spelledTypeName,
        string canonicalTypeName,
        string qualifiedSpelledTypeName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        bool isReturn,
        Predicate<ManagedTypeOverride>? predicate,
        out ManagedTypeOverride? managedTypeOverride)
    {
        foreach (var candidate in EnumerateManagedTypeOverrideKeys(spelledTypeName, canonicalTypeName, qualifiedSpelledTypeName, isReturn))
        {
            if (managedTypeOverrides.TryGetValue(candidate, out managedTypeOverride) &&
                (predicate is null || predicate(managedTypeOverride)))
            {
                return true;
            }
        }

        foreach (var (key, candidateOverride) in managedTypeOverrides)
        {
            var isCandidateReturn = key.StartsWith("return:", StringComparison.Ordinal);
            if (isCandidateReturn != isReturn || candidateOverride.TemplateParameterNames.Count != 0)
            {
                continue;
            }

            if ((ConcreteTypePrefixMatches(spelledTypeName, candidateOverride.CppTypeName) ||
                ConcreteTypePrefixMatches(canonicalTypeName, candidateOverride.CppTypeName)) &&
                (predicate is null || predicate(candidateOverride)))
            {
                managedTypeOverride = candidateOverride;
                return true;
            }
        }

        foreach (var (key, candidateOverride) in managedTypeOverrides)
        {
            var isCandidateReturn = key.StartsWith("return:", StringComparison.Ordinal);
            if (isCandidateReturn != isReturn || candidateOverride.TemplateParameterNames.Count == 0)
            {
                continue;
            }

            if (GenericTypePatternMatches(
                    spelledTypeName,
                    canonicalTypeName,
                    candidateOverride.CppTypeName,
                    candidateOverride.TemplateParameterNames.Count,
                    GetVisibleTemplateArgumentCount(candidateOverride, managedTypeOverrides)) &&
                (predicate is null || predicate(candidateOverride)))
            {
                managedTypeOverride = candidateOverride;
                return true;
            }
        }

        managedTypeOverride = null;
        return false;
    }

    private static bool ConcreteTypePrefixMatches(string candidateTypeName, string configuredTypeName)
    {
        if (!TryGetTopLevelTemplateArguments(candidateTypeName, out var candidateHead, out var candidateArguments) ||
            !TryGetTopLevelTemplateArguments(configuredTypeName, out var configuredHead, out var configuredArguments))
        {
            return false;
        }

        if (!TemplateHeadsEquivalent(candidateHead, configuredHead))
        {
            return false;
        }

        var commonArgumentCount = Math.Min(candidateArguments.Count, configuredArguments.Count);
        for (var index = 0; index < commonArgumentCount; index++)
        {
            if (!string.Equals(candidateArguments[index], configuredArguments[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return commonArgumentCount > 0;
    }

    private static bool GenericTypePatternMatches(
        string spelledTypeName,
        string canonicalTypeName,
        string patternTypeName,
        int expectedArgumentCount,
        int visibleArgumentCount)
    {
        if (!TryGetTopLevelTemplateArguments(patternTypeName, out var patternHead, out var patternArguments))
        {
            return false;
        }

        foreach (var candidateTypeName in new[] { spelledTypeName, canonicalTypeName }
                     .Where(static candidateTypeName => !string.IsNullOrWhiteSpace(candidateTypeName))
                     .Distinct(StringComparer.Ordinal))
        {
            if (!TryGetTopLevelTemplateArguments(candidateTypeName, out var candidateHead, out var candidateArguments))
            {
                continue;
            }

            if (TemplateHeadsEquivalent(candidateHead, patternHead) &&
                patternArguments.Count >= expectedArgumentCount &&
                candidateArguments.Count >= visibleArgumentCount &&
                candidateArguments.Count <= patternArguments.Count)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TemplateHeadsEquivalent(string leftHead, string rightHead)
    {
        var normalizedLeft = NormalizeNamedTypeName(leftHead);
        var normalizedRight = NormalizeNamedTypeName(rightHead);
        if (string.Equals(normalizedLeft, normalizedRight, StringComparison.Ordinal))
        {
            return true;
        }

        var canonicalLeft = StripGenericArguments(ResolveAliasTypeName(normalizedLeft));
        var canonicalRight = StripGenericArguments(ResolveAliasTypeName(normalizedRight));
        return string.Equals(canonicalLeft, canonicalRight, StringComparison.Ordinal);
    }

    private static int GetVisibleTemplateArgumentCount(
        ManagedTypeOverride managedTypeOverride,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides)
    {
        if (!managedTypeOverride.HasPublicTypeMapping &&
            managedTypeOverrides.TryGetValue(managedTypeOverride.CppTypeName, out var parameterOverride))
        {
            return GetVisibleTemplateArgumentCount(parameterOverride, managedTypeOverrides);
        }

        return managedTypeOverride.VisibleTemplateParameterIndices.Count == 0
            ? managedTypeOverride.TemplateParameterNames.Count
            : managedTypeOverride.VisibleTemplateParameterIndices.Count;
    }

    private static bool TryGetTopLevelTemplateArguments(string typeName, out string head, out IReadOnlyList<string> arguments)
    {
        var genericStart = typeName.IndexOf('<');
        var genericEnd = typeName.LastIndexOf('>');
        if (genericStart < 0 || genericEnd <= genericStart)
        {
            head = string.Empty;
            arguments = Array.Empty<string>();
            return false;
        }

        head = NormalizeNamedTypeName(typeName[..genericStart]);
        arguments = SplitTopLevelParameters(typeName[(genericStart + 1)..genericEnd])
            .Select(argument => NormalizeTypeName(argument))
            .ToArray();
        return true;
    }

    private static IEnumerable<string> EnumerateManagedTypeOverrideKeys(string spelledTypeName, string canonicalTypeName, string qualifiedSpelledTypeName, bool isReturn)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in new[] { spelledTypeName, canonicalTypeName, qualifiedSpelledTypeName })
        {
            foreach (var variant in EnumerateManagedTypeOverrideNameVariants(name))
            {
                var candidate = isReturn ? "return:" + variant : variant;
                if (seen.Add(candidate))
                {
                    yield return candidate;
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateManagedTypeOverrideNameVariants(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            yield break;
        }

        yield return typeName;
        if (typeName.StartsWith("const ", StringComparison.Ordinal))
        {
            yield return typeName["const ".Length..].TrimStart();
        }

        // Also yield the unqualified name (last component after ::).  This
        // handles the case where a parameter is spelled with an unqualified
        // typedef name (e.g. "Vector3") but the managed type override is keyed
        // by the fully qualified name (e.g. "Urho3D::Vector3").
        var lastColons = typeName.LastIndexOf("::", StringComparison.Ordinal);
        if (lastColons >= 0)
        {
            yield return typeName[(lastColons + 2)..];
        }
    }

    private static BindingType MapType(
        CXType type,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        IReadOnlyDictionary<string, BindingType> genericParameters,
        bool isReturn = false,
        bool preserveAliasSurface = true,
        bool allowPrimitivePointer = false)
    {
        var normalizedType = type.CanonicalType;
        var spelledTypeName = NormalizeTypeName(type.Spelling.ToString());
        // Strip 'const ' prefix so that "const Vector3" resolves the same
        // typedef as plain "Vector3" in the alias resolution chain.
        var aliasLookupName = spelledTypeName.StartsWith("const ", StringComparison.Ordinal)
            ? spelledTypeName["const ".Length..]
            : spelledTypeName;
        var aliasResolvedTypeName = SelectConcreteTemplateTypeName(spelledTypeName, ResolveAliasTypeName(aliasLookupName));
        var canonicalTypeName = SelectConcreteTemplateTypeName(
            NormalizeTypeName(normalizedType.Spelling.ToString()),
            GetUnderlyingTypeName(type));
        var ownedAliasInfo = TryGetOwnedManagedAliasInfo(type, out var aliasInfo) ? aliasInfo : null;
        var overrideLookupTypeName = ownedAliasInfo is not null ? spelledTypeName : aliasResolvedTypeName;
        var resolvedTypeName = SelectConcreteTemplateTypeName(aliasResolvedTypeName, canonicalTypeName);
        var surfaceCppTypeName = resolvedTypeName;
        var managedOverrideTypeName = normalizedType.IsConstQualified && TypeIsRecord(normalizedType)
            ? canonicalTypeName
            : resolvedTypeName;

        // When the resolved type contains template arguments, use the
        // canonical type's fully-qualified argument names.  This keeps
        // the outer type alias (e.g. "container::vector") while guaranteeing
        // that template argument resolution is driven by the Clang AST
        // rather than by text-based short-name lookup, which can collide
        // with records from non-owned headers (e.g. "Rml::Vector2" vs
        // "Urho3D::Vector2").
        if (ownedAliasInfo is null &&
            surfaceCppTypeName.Contains('<') &&
            canonicalTypeName.Contains('<') &&
            TryGetTopLevelTemplateArguments(surfaceCppTypeName, out var surfaceHead, out _) &&
            TryGetTopLevelTemplateArguments(canonicalTypeName, out _, out var canonicalArgs))
        {
            surfaceCppTypeName = $"{surfaceHead}<{string.Join(", ", canonicalArgs)}>";
        }

        // When the spelled name is unqualified (no ::), derive the
        // fully-qualified version from the underlying Clang declaration
        // cursor so that managed-type-override lookups keyed on the
        // fully-qualified C++ type name (e.g. "Urho3D::Vector3") still
        // match.  Always query the origin-name index — even when the
        // spelled name already contains :: — because for canonical types
        // the AST cursor gives the template name, not the typedef.
        var qualifiedSpelledTypeName = GetQualifiedTypeName(type, spelledTypeName);

        if (normalizedType.kind == CXTypeKind.CXType_ConstantArray)
        {
            return CreateNativeFixedArrayType(
                type,
                recordsByName,
                enumsByName,
                managedTypeOverrides,
                genericParameters);
        }

        // A returned `T**` has no slot to project into (unlike an out parameter) and is not
        // a `T`; without its own marshaller it stays a raw pointer.
        if (isReturn &&
            (IsByReferenceProjection(type, managedTypeOverrides) ||
             GetPointerDepth(type) >= 2 && !TryGetManagedTypeOverride(type, managedTypeOverrides, out _)))
        {
            return BindingType.CreateConfigured(
                spelledTypeName,
                BindingType.IntPtr.ManagedTypeName,
                primitiveKind: BindingPrimitiveKind.IntPtr);
        }

        if (isReturn &&
            TryGetManagedTypeOverride(
                overrideLookupTypeName,
                managedOverrideTypeName,
                qualifiedSpelledTypeName,
                managedTypeOverrides,
                isReturn: true,
                candidate => MarshallerConfigExtractor.MatchesManagedTypeOverride(candidate, type),
                out var returnOverride) &&
            returnOverride!.HasPublicTypeMapping)
        {
            var mapped = CreateConfiguredOrWrapperType(returnOverride!, recordsByName, enumsByName, managedTypeOverrides, genericParameters, surfaceCppTypeName)
                .WithCppIsReference(TypeIsAnyReference(normalizedType));
            // Alias surface stamping is deferred to the pipeline post-pruning.
            return preserveAliasSurface
                ? TryApplyManagedAliasSurface(type, mapped, preserveAliasSurface, enumsByName)
                : mapped;
        }

        TryGetManagedTypeOverride(
            overrideLookupTypeName,
            managedOverrideTypeName,
            qualifiedSpelledTypeName,
            managedTypeOverrides,
            isReturn: false,
            candidate => MarshallerConfigExtractor.MatchesManagedTypeOverride(candidate, type),
            out var managedTypeOverride);

        if (managedTypeOverride is { HasPublicTypeMapping: true })
        {
            var mapped = CreateConfiguredOrWrapperType(managedTypeOverride, recordsByName, enumsByName, managedTypeOverrides, genericParameters, surfaceCppTypeName)
                .WithCppIsReference(TypeIsAnyReference(normalizedType));
            return preserveAliasSurface
                ? TryApplyManagedAliasSurface(type, mapped, preserveAliasSurface, enumsByName)
                : mapped;
        }

        if (managedTypeOverride is
            {
                HasPublicTypeMapping: true,
                HasManagedBaseType: false,
                HasExposedMembers: false,
                ManagedWrapperAssembly: null
            } && TypeIsPointer(normalizedType))
        {
            return TryApplyManagedAliasSurface(
                type,
                BindingType.CreateConfigured(
                    spelledTypeName,
                    BindingType.IntPtr.ManagedTypeName,
                    primitiveKind: BindingPrimitiveKind.IntPtr),
                preserveAliasSurface,
                enumsByName);
        }

        if (TypeIsLValueReference(normalizedType))
        {
            var sourcePointeeType = type.PointeeType;
            return TryApplyManagedAliasSurface(
                type,
                MapType(sourcePointeeType, recordsByName, enumsByName, managedTypeOverrides, genericParameters, preserveAliasSurface: false)
                    .WithCppIsRecordValue(false)
                    .WithCppIsReference(true),
                preserveAliasSurface,
                enumsByName);
        }

        if (TypeIsPointer(normalizedType))
        {
            var pointeeType = normalizedType.PointeeType.CanonicalType;
            var concretePointeeTypeName = SelectConcreteTemplateTypeName(
                NormalizeTypeName(pointeeType.Spelling.ToString()),
                NormalizeTypeName(type.PointeeType.Spelling.ToString()));

            // A by-value managed projection cannot also represent a pointer to that
            // native value. Unless the pointer has its own matching marshaller (handled
            // above), preserve the pointer ABI instead of applying an indirect conversion.
            if (TryGetManagedTypeOverride(type.PointeeType, managedTypeOverrides, out var pointeeOverride) &&
                !pointeeOverride!.HasManagedBaseType &&
                !pointeeOverride.HasExposedMembers &&
                string.IsNullOrWhiteSpace(pointeeOverride.ManagedWrapperAssembly))
            {
                return TryApplyManagedAliasSurface(
                    type,
                    BindingType.CreateConfigured(
                        spelledTypeName,
                        BindingType.IntPtr.ManagedTypeName,
                        primitiveKind: BindingPrimitiveKind.IntPtr),
                    preserveAliasSurface,
                    enumsByName);
            }

            if (pointeeType.kind == CXTypeKind.CXType_Void)
            {
                var publicTypeName = managedTypeOverrides.TryGetValue(BindingType.IntPtr.CppTypeName, out var voidPointerOverride)
                    ? voidPointerOverride.PublicTypeName
                    : BindingType.IntPtr.ManagedTypeName;
                return TryApplyManagedAliasSurface(
                    type,
                    BindingType.CreateConfigured(spelledTypeName, publicTypeName, primitiveKind: BindingPrimitiveKind.IntPtr),
                    preserveAliasSurface,
                    enumsByName);
            }

            if (TypeIsPointer(pointeeType) &&
                pointeeType.PointeeType.CanonicalType.kind is CXTypeKind.CXType_Char_S or CXTypeKind.CXType_Char_U)
            {
                return TryApplyManagedAliasSurface(
                    type,
                    BindingType.CreateConfigured(
                        spelledTypeName,
                        BindingType.IntPtr.ManagedTypeName,
                        primitiveKind: BindingPrimitiveKind.IntPtr),
                    preserveAliasSurface,
                    enumsByName);
            }

            if (ownedAliasInfo is null &&
                TypeIsRecord(pointeeType) &&
                !spelledTypeName.Contains('*', StringComparison.Ordinal) &&
                !recordsByName.ContainsKey(GetRecordQualifiedName(pointeeType, recordsByName)))
            {
                return BindingType.CreateConfigured(
                    spelledTypeName,
                    BindingType.IntPtr.ManagedTypeName,
                    primitiveKind: BindingPrimitiveKind.IntPtr);
            }

            if (TryCreateSpecializedRecordHandleType(
                    spelledTypeName,
                    concretePointeeTypeName,
                    recordsByName,
                    enumsByName,
                    managedTypeOverrides,
                    genericParameters,
                    out var specializedRecordHandleType,
                    FormatCanonicalCxTypeName(pointeeType)))
            {
                return TryApplyManagedAliasSurface(type, specializedRecordHandleType, preserveAliasSurface, enumsByName);
            }

            if (TypeIsRecord(pointeeType))
            {
                var recordName = GetRecordQualifiedName(pointeeType, recordsByName);
                if (!recordsByName.TryGetValue(recordName, out var record))
                {
                    // Use the resolved qualified name for CppQualifiedName so the
                    // placeholder passes namespace-exclusion checks in the pipeline.
                    var unqualifiedRecordName = GetManagedTypeHead(recordName);
                    record = new RecordBinding(CreateRecordSymbolId(recordName), unqualifiedRecordName, ToPascalCase(unqualifiedRecordName), recordName);
                    record.SetMetadata(MetadataKeys.PlaceholderRecord, true);
                    IndexRecordKeys(recordsByName, record);
                }

                var genericArguments = record.HasMetadata(MetadataKeys.PlaceholderRecord)
                    ? Array.Empty<BindingType>()
                    : ParseVisibleRecordTemplateArguments(record, concretePointeeTypeName, recordsByName, enumsByName, managedTypeOverrides, genericParameters);
                var recordHandleType = genericArguments.Count == 0
                    ? record.HandleType
                    : BindingType.CreateHandle(spelledTypeName, record.ManagedName, record.SymbolId, record.HandleMarshallerTypeName, genericArguments);
                var isFixedArray = IsFixedArrayRecordOrConfigured(record, concretePointeeTypeName, managedTypeOverrides);
                var isDynamicArray = IsDynamicArrayRecordOrConfigured(record, concretePointeeTypeName, managedTypeOverrides);
                recordHandleType = ApplyFixedArrayMetadata(
                    recordHandleType,
                    isFixedArray,
                    genericArguments);
                recordHandleType = ApplyDynamicArrayMetadata(
                    recordHandleType,
                    isDynamicArray,
                    genericArguments);
                return TryApplyManagedAliasSurface(type, recordHandleType, preserveAliasSurface, enumsByName);
            }

            if (TypeIsPointer(pointeeType) && TypeIsRecord(pointeeType.PointeeType.CanonicalType))
            {
                var elementType = pointeeType.PointeeType.CanonicalType;
                var recordName = GetRecordQualifiedName(elementType, recordsByName);
                if (!recordsByName.TryGetValue(recordName, out var record))
                {
                    var unqualifiedRecordName = GetManagedTypeHead(recordName);
                    record = new RecordBinding(CreateRecordSymbolId(recordName), unqualifiedRecordName, ToPascalCase(unqualifiedRecordName), recordName);
                    record.SetMetadata(MetadataKeys.PlaceholderRecord, true);
                    IndexRecordKeys(recordsByName, record);
                }

                return TryApplyManagedAliasSurface(
                    type,
                    record.HandleType,
                    preserveAliasSurface,
                    enumsByName);
            }

            if (isReturn || allowPrimitivePointer && IsPrimitiveValueType(pointeeType))
            {
                return TryApplyManagedAliasSurface(
                    type,
                    BindingType.CreateHandle(spelledTypeName, "global::System.IntPtr"),
                    preserveAliasSurface,
                    enumsByName);
            }

        }

        if (TypeIsRecord(normalizedType))
        {
            var concreteRecordTypeName = SelectConcreteTemplateTypeName(resolvedTypeName, canonicalTypeName);
            if (TryCreateSpecializedRecordHandleType(
                    $"{spelledTypeName}*",
                    concreteRecordTypeName,
                    recordsByName,
                    enumsByName,
                    managedTypeOverrides,
                    genericParameters,
                    out var specializedRecordHandleType,
                    FormatCanonicalCxTypeName(normalizedType)))
            {
                specializedRecordHandleType = specializedRecordHandleType
                    .WithCppIsRecordValue(true)
                    .WithCppPointerIsRecordValueProjection(true);
                return TryApplyManagedAliasSurface(type, specializedRecordHandleType, preserveAliasSurface, enumsByName);
            }

            var recordName = GetRecordQualifiedName(normalizedType, recordsByName);
            if (!recordsByName.TryGetValue(recordName, out var record))
            {
                var unqualifiedRecordName = GetManagedTypeHead(recordName);
                record = new RecordBinding(CreateRecordSymbolId(recordName), unqualifiedRecordName, ToPascalCase(unqualifiedRecordName), recordName);
                record.SetMetadata(MetadataKeys.PlaceholderRecord, true);
                IndexRecordKeys(recordsByName, record);
            }

            var genericArguments = record.HasMetadata(MetadataKeys.PlaceholderRecord)
                ? Array.Empty<BindingType>()
                : ParseVisibleRecordTemplateArguments(record, concreteRecordTypeName, recordsByName, enumsByName, managedTypeOverrides, genericParameters);
            var recordValueType = genericArguments.Count == 0
                ? record.HandleType.WithCppIsRecordValue(true).WithCppPointerIsRecordValueProjection(true)
                : BindingType.CreateHandle($"{spelledTypeName}*", record.ManagedName, record.SymbolId,
                    record.HandleMarshallerTypeName, genericArguments, cppIsRecordValue: true,
                    cppPointerIsRecordValueProjection: true);
            var isFixedArray = IsFixedArrayRecordOrConfigured(record, concreteRecordTypeName, managedTypeOverrides);
            var isDynamicArray = IsDynamicArrayRecordOrConfigured(record, concreteRecordTypeName, managedTypeOverrides);
            recordValueType = ApplyFixedArrayMetadata(
                recordValueType,
                isFixedArray,
                genericArguments);
            recordValueType = ApplyDynamicArrayMetadata(
                recordValueType,
                isDynamicArray,
                genericArguments);
            return TryApplyManagedAliasSurface(type, recordValueType, preserveAliasSurface, enumsByName);
        }

        if (TypeIsEnum(normalizedType))
        {
            var enumName = NormalizeNamedTypeName(normalizedType.Spelling.ToString());
            if (enumsByName.TryGetValue(enumName, out var @enum))
            {
                return TryApplyManagedAliasSurface(type, BindingType.CreateEnum(enumName, @enum.ManagedName, enumId: @enum.SymbolId), preserveAliasSurface, enumsByName);
            }

            return TryApplyManagedAliasSurface(type, BindingType.CreateEnum(enumName, ToPascalCase(GetManagedTypeHead(enumName))), preserveAliasSurface, enumsByName);
        }

        var primitiveType = normalizedType.kind switch
        {
            CXTypeKind.CXType_Void => BindingType.Void,
            CXTypeKind.CXType_Bool => BindingType.Boolean,
            CXTypeKind.CXType_Char_S => BindingType.Int8,
            CXTypeKind.CXType_SChar => BindingType.Int8,
            CXTypeKind.CXType_Char_U => BindingType.UInt8,
            CXTypeKind.CXType_UChar => BindingType.UInt8,
            CXTypeKind.CXType_Short => BindingType.Int16,
            CXTypeKind.CXType_UShort => BindingType.UInt16,
            CXTypeKind.CXType_Int => BindingType.Int32,
            CXTypeKind.CXType_UInt => BindingType.UInt32,
            CXTypeKind.CXType_LongLong => BindingType.Int64,
            CXTypeKind.CXType_ULongLong => BindingType.UInt64,
            CXTypeKind.CXType_Float => BindingType.Single,
            CXTypeKind.CXType_Double => BindingType.Double,
            CXTypeKind.CXType_Long => BindingType.NativeInt,
            CXTypeKind.CXType_ULong => BindingType.NativeUInt,
            _ => throw new NotSupportedException($"ClangSharp sample parser does not support '{normalizedType.kind}' ({normalizedType.Spelling}).")
        };
        return TryApplyManagedAliasSurface(type, primitiveType, preserveAliasSurface, enumsByName);
    }

    private static BindingType CreateConfiguredOrWrapperType(
        ManagedTypeOverride managedTypeOverride,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        IReadOnlyDictionary<string, BindingType> genericParameters,
        string? resolvedCppTypeName = null)
    {
        var effectiveCppTypeName = string.IsNullOrWhiteSpace(resolvedCppTypeName)
            ? managedTypeOverride.CppTypeName
            : NormalizeTypeName(resolvedCppTypeName);

        if (managedTypeOverride.Callback)
        {
            return CreateCallbackType(
                managedTypeOverride,
                effectiveCppTypeName,
                recordsByName,
                enumsByName,
                managedTypeOverrides,
                genericParameters);
        }

        var rawGenericArguments = ShouldInstantiateConfiguredGenericType(managedTypeOverride)
            ? ParseConcreteTemplateArgumentsOrOpaque(effectiveCppTypeName, recordsByName, enumsByName, managedTypeOverrides, genericParameters)
            : Array.Empty<BindingType>();
        var isConfiguredFixedArray = managedTypeOverride.FixedArray || IsFixedArrayConfiguredType(effectiveCppTypeName, managedTypeOverrides);
        var isConfiguredDynamicArray = managedTypeOverride.DynamicArray || IsDynamicArrayConfiguredType(effectiveCppTypeName, managedTypeOverrides);
        var configuredGenericArguments = isConfiguredFixedArray
            ? rawGenericArguments
            : ProjectVisibleTemplateArguments(
                rawGenericArguments,
                GetVisibleTemplateParameterIndices(managedTypeOverride, rawGenericArguments.Count));
        if (ShouldInstantiateConfiguredGenericType(managedTypeOverride) &&
            rawGenericArguments.Count == 0 &&
            managedTypeOverride.PublicTypeName.Contains('{', StringComparison.Ordinal))
        {
            return BindingType.CreateConfigured(effectiveCppTypeName, effectiveCppTypeName);
        }

        if (managedTypeOverride.Projection)
        {
            return CreateProjectedHandleType(
                managedTypeOverride,
                effectiveCppTypeName,
                recordsByName,
                enumsByName,
                managedTypeOverrides,
                genericParameters);
        }

        if (!managedTypeOverride.HasManagedBaseType &&
            !managedTypeOverride.HasExposedMembers &&
            string.IsNullOrWhiteSpace(managedTypeOverride.ManagedWrapperAssembly))
        {
            // A projection that does not name its template arguments (`twin<T, T>` as a
            // managed long) is not a generic managed type.
            var configuredType = BindingType.CreateConfigured(
                effectiveCppTypeName,
                managedTypeOverride.PublicTypeName,
                managedTypeOverride.MarshallerTypeName,
                managedTypeOverride.ManagedMarshaller,
                GetConfiguredPrimitiveKind(managedTypeOverride.PublicTypeName),
                genericArguments: managedTypeOverride.PublicTypeName.Contains('{', StringComparison.Ordinal) || isConfiguredFixedArray || isConfiguredDynamicArray
                    ? configuredGenericArguments
                    : []);
            return ApplyDynamicArrayMetadata(
                ApplyFixedArrayMetadata(configuredType, isConfiguredFixedArray, rawGenericArguments),
                isConfiguredDynamicArray,
                rawGenericArguments);
        }

        var recordName = StripGenericArguments(NormalizeTypeName(managedTypeOverride.CppTypeName));
        if (!recordsByName.TryGetValue(recordName, out var record))
        {
            record = new RecordBinding(
                CreateRecordSymbolId(recordName),
                recordName,
                GetSimpleManagedTypeName(managedTypeOverride.PublicTypeName),
                managedTypeOverride.TemplateParameterNames.Count > 0 ? recordName : managedTypeOverride.CppTypeName)
            {
                ManagedTypeFormat = managedTypeOverride.PublicTypeName,
                ManagedNamespace = managedTypeOverride.ManagedNamespace ??
                    (!string.IsNullOrWhiteSpace(managedTypeOverride.ManagedWrapperAssembly)
                        ? MarshallerConfigExtractor.GetManagedNamespaceFromTypeName(managedTypeOverride.PublicTypeName)
                        : null)
            };
            if (!string.IsNullOrWhiteSpace(managedTypeOverride.ManagedWrapperAssembly))
            {
                record.SetMetadata(MetadataKeys.ManagedWrapperAssembly, managedTypeOverride.ManagedWrapperAssembly);
            }
            if (!string.IsNullOrWhiteSpace(managedTypeOverride.ManagedWrapperProviderKind))
            {
                record.SetMetadata(MetadataKeys.ManagedWrapperProviderKind, managedTypeOverride.ManagedWrapperProviderKind);
            }
            if (!string.IsNullOrWhiteSpace(managedTypeOverride.ManagedWrapperProviderNamespace))
            {
                record.SetMetadata(MetadataKeys.ManagedWrapperProviderNamespace, managedTypeOverride.ManagedWrapperProviderNamespace);
            }
            if (managedTypeOverride.ManagedWrapperCopyable.HasValue)
            {
                record.SetMetadata(MetadataKeys.ManagedWrapperCopyable, managedTypeOverride.ManagedWrapperCopyable.Value);
                record.HasCopyConstructor = managedTypeOverride.ManagedWrapperCopyable.Value;
            }
            ApplySyntheticGenericParameters(record, managedTypeOverride.TemplateParameterNames);
            if (isConfiguredFixedArray)
            {
                record.SetMetadata(MetadataKeys.FixedArrayRecord, true);
            }
            if (managedTypeOverride.NativeArray)
            {
                record.SetMetadata(MetadataKeys.NativeArrayRecord, true);
            }
            if (isConfiguredDynamicArray)
            {
                record.SetMetadata(MetadataKeys.DynamicArrayRecord, true);
            }
            record.SetMetadata(MetadataKeys.PlaceholderRecord, true);
            IndexRecordKeys(recordsByName, record);
        }
        else
        {
            record.ManagedTypeFormat ??= managedTypeOverride.PublicTypeName;
            record.ManagedNamespace ??= managedTypeOverride.ManagedNamespace ??
                (!string.IsNullOrWhiteSpace(managedTypeOverride.ManagedWrapperAssembly)
                    ? MarshallerConfigExtractor.GetManagedNamespaceFromTypeName(managedTypeOverride.PublicTypeName)
                    : null);
            if (!string.IsNullOrWhiteSpace(managedTypeOverride.ManagedWrapperAssembly))
            {
                record.SetMetadata(MetadataKeys.ManagedWrapperAssembly, managedTypeOverride.ManagedWrapperAssembly);
            }
            if (!string.IsNullOrWhiteSpace(managedTypeOverride.ManagedWrapperProviderKind))
            {
                record.SetMetadata(MetadataKeys.ManagedWrapperProviderKind, managedTypeOverride.ManagedWrapperProviderKind);
            }
            if (!string.IsNullOrWhiteSpace(managedTypeOverride.ManagedWrapperProviderNamespace))
            {
                record.SetMetadata(MetadataKeys.ManagedWrapperProviderNamespace, managedTypeOverride.ManagedWrapperProviderNamespace);
            }
            if (managedTypeOverride.ManagedWrapperCopyable.HasValue)
            {
                record.SetMetadata(MetadataKeys.ManagedWrapperCopyable, managedTypeOverride.ManagedWrapperCopyable.Value);
                record.HasCopyConstructor = managedTypeOverride.ManagedWrapperCopyable.Value;
            }
            if (record.HasMetadata(MetadataKeys.PlaceholderRecord))
            {
                record.ManagedName = GetSimpleManagedTypeName(managedTypeOverride.PublicTypeName);
            }

            ApplySyntheticGenericParameters(record, managedTypeOverride.TemplateParameterNames);
            if (isConfiguredFixedArray)
            {
                record.SetMetadata(MetadataKeys.FixedArrayRecord, true);
            }
            if (managedTypeOverride.NativeArray)
            {
                record.SetMetadata(MetadataKeys.NativeArrayRecord, true);
            }
            if (isConfiguredDynamicArray)
            {
                record.SetMetadata(MetadataKeys.DynamicArrayRecord, true);
            }
        }

        var isFixedArrayRecord = record.GetMetadata<bool>(MetadataKeys.FixedArrayRecord);
        var isDynamicArrayRecord = record.GetMetadata<bool>(MetadataKeys.DynamicArrayRecord);
        if (isFixedArrayRecord && rawGenericArguments.Count > 0)
        {
            configuredGenericArguments = rawGenericArguments;
        }

        if (configuredGenericArguments.Count == 0 && (!record.IsGeneric || managedTypeOverride.TemplateParameterNames.Count != 0))
        {
            return ApplyDynamicArrayMetadata(
                ApplyFixedArrayMetadata(record.HandleType, isConfiguredFixedArray || isFixedArrayRecord, rawGenericArguments),
                isConfiguredDynamicArray || isDynamicArrayRecord,
                rawGenericArguments);
        }

        var handleType = BindingType.CreateHandle($"{effectiveCppTypeName}*", record.ManagedName, record.SymbolId, record.HandleMarshallerTypeName, configuredGenericArguments);
        return ApplyDynamicArrayMetadata(
            ApplyFixedArrayMetadata(handleType, isConfiguredFixedArray || isFixedArrayRecord, rawGenericArguments),
            isConfiguredDynamicArray || isDynamicArrayRecord,
            rawGenericArguments);
    }

    private static BindingPrimitiveKind GetConfiguredPrimitiveKind(string publicTypeName)
    {
        return publicTypeName switch
        {
            "void" => BindingPrimitiveKind.Void,
            "bool" => BindingPrimitiveKind.Boolean,
            "sbyte" => BindingPrimitiveKind.Int8,
            "byte" => BindingPrimitiveKind.UInt8,
            "short" => BindingPrimitiveKind.Int16,
            "ushort" => BindingPrimitiveKind.UInt16,
            "int" => BindingPrimitiveKind.Int32,
            "uint" => BindingPrimitiveKind.UInt32,
            "long" => BindingPrimitiveKind.Int64,
            "ulong" => BindingPrimitiveKind.UInt64,
            "float" => BindingPrimitiveKind.Single,
            "double" => BindingPrimitiveKind.Double,
            "nint" => BindingPrimitiveKind.NativeInt,
            "nuint" => BindingPrimitiveKind.NativeUInt,
            "global::System.IntPtr" => BindingPrimitiveKind.IntPtr,
            _ => BindingPrimitiveKind.Unsupported
        };
    }

    private static BindingType CreateNativeFixedArrayType(
        CXType arrayType,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        IReadOnlyDictionary<string, BindingType> genericParameters)
    {
        var length = arrayType.ArraySize;
        if (length < 0 || length > int.MaxValue)
        {
            throw new NotSupportedException($"Fixed-size array length '{length}' is not supported.");
        }

        var nativeArrayOverride = managedTypeOverrides.Values
            .Where(static managedTypeOverride => managedTypeOverride.FixedArray && managedTypeOverride.NativeArray)
            .OrderBy(static managedTypeOverride => managedTypeOverride.CppTypeName, StringComparer.Ordinal)
            .FirstOrDefault();
        if (nativeArrayOverride is null)
        {
            throw new NotSupportedException("fixed-size array fields require a native_array fixed-array marshaller configuration.");
        }

        var elementType = MapType(arrayType.ArrayElementType, recordsByName, enumsByName, managedTypeOverrides, genericParameters);

        var concreteArrayTypeName = FormatConfiguredNativeArrayTypeName(nativeArrayOverride.CppTypeName, elementType.CppTypeName, length);
        return CreateConfiguredOrWrapperType(
                nativeArrayOverride,
                recordsByName,
                enumsByName,
                managedTypeOverrides,
                genericParameters,
                concreteArrayTypeName)
            // A C array is not a template specialization in the source AST. Its
            // element type and extent are nevertheless first-class CXType facts,
            // so carry those facts into the synthetic NativeArray specialization.
            .WithGenericArguments([elementType])
            .WithNativeTemplateArguments([
                BindingTemplateArgument.FromType(elementType),
                BindingTemplateArgument.FromIntegral(length)], astVerified: true)
            .WithFixedArray(elementType, checked((int)length));
    }

    private static string FormatConfiguredNativeArrayTypeName(string configuredTypeName, string elementCppTypeName, long length)
    {
        return TryGetTopLevelTemplateArguments(configuredTypeName, out var head, out _)
            ? $"{head}<{elementCppTypeName}, {length}>"
            : $"{configuredTypeName}<{elementCppTypeName}, {length}>";
    }

    private static BindingType ApplyFixedArrayMetadata(
        BindingType type,
        ManagedTypeOverride managedTypeOverride,
        IReadOnlyList<BindingType> rawGenericArguments,
        bool isFixedArrayRecord = false)
    {
        return ApplyFixedArrayMetadata(type, managedTypeOverride.FixedArray || isFixedArrayRecord, rawGenericArguments);
    }

    private static BindingType ApplyFixedArrayMetadata(
        BindingType type,
        bool isFixedArray,
        IReadOnlyList<BindingType> rawGenericArguments)
    {
        // Non-type template arguments are applied from Clang's TemplateArgument
        // nodes in ApplyAstTemplateArguments. Do not infer them from type spelling.
        _ = isFixedArray;
        _ = rawGenericArguments;
        return type;
    }

    private static BindingType ApplyDynamicArrayMetadata(
        BindingType type,
        bool isDynamicArray,
        IReadOnlyList<BindingType> rawGenericArguments)
    {
        return isDynamicArray && rawGenericArguments.Count > 0
            ? type.WithDynamicArray(rawGenericArguments[0])
            : type;
    }

    private static BindingType CreateCallbackType(
        ManagedTypeOverride managedTypeOverride,
        string effectiveCppTypeName,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        IReadOnlyDictionary<string, BindingType> genericParameters)
    {
        if (!TryGetTopLevelTemplateArguments(effectiveCppTypeName, out _, out var templateArguments) ||
            templateArguments.Count != 1 ||
            !TryParseCallbackSignature(templateArguments[0], out var returnTypeName, out var parameterTypeNames))
        {
            throw new NotSupportedException($"Callback marshaller '{managedTypeOverride.CppTypeName}' must target a function-object type with one function signature template argument.");
        }

        var returnType = string.Equals(returnTypeName, "void", StringComparison.Ordinal)
            ? BindingType.Void
            : ParseTypeToken(returnTypeName, recordsByName, enumsByName, managedTypeOverrides, genericParameters, allowCallbackPointerLikeTokens: true);
        var parameterTypes = parameterTypeNames
            .Select(parameterTypeName => ParseTypeToken(parameterTypeName, recordsByName, enumsByName, managedTypeOverrides, genericParameters, allowCallbackPointerLikeTokens: true))
            .ToArray();

        return BindingType.CreateCallback(
            effectiveCppTypeName,
            FormatManagedCallbackType(returnType, parameterTypes),
            managedTypeOverride.ManagedMarshaller,
            returnType,
            parameterTypes);
    }

    private static bool TryParseCallbackSignature(
        string signature,
        out string returnTypeName,
        out IReadOnlyList<string> parameterTypeNames)
    {
        var normalized = NormalizeTypeName(signature);
        var parameterStart = normalized.IndexOf('(');
        var parameterEnd = normalized.LastIndexOf(')');
        if (parameterStart <= 0 || parameterEnd <= parameterStart)
        {
            returnTypeName = string.Empty;
            parameterTypeNames = Array.Empty<string>();
            return false;
        }

        returnTypeName = NormalizeTypeName(normalized[..parameterStart]);
        var parameterText = normalized[(parameterStart + 1)..parameterEnd].Trim();
        parameterTypeNames = string.IsNullOrWhiteSpace(parameterText) ||
            string.Equals(parameterText, "void", StringComparison.Ordinal)
                ? Array.Empty<string>()
                : SplitTopLevelParameters(parameterText).Select(NormalizeTypeName).ToArray();
        return !string.IsNullOrWhiteSpace(returnTypeName);
    }

    private static string FormatManagedCallbackType(BindingType returnType, IReadOnlyList<BindingType> parameterTypes)
    {
        if (returnType.PrimitiveKind == BindingPrimitiveKind.Void)
        {
            return parameterTypes.Count == 0
                ? "global::System.Action"
                : $"global::System.Action<{FormatGenericPlaceholders(parameterTypes.Count)}>";
        }

        return $"global::System.Func<{FormatGenericPlaceholders(parameterTypes.Count + 1)}>";
    }

    private static string FormatGenericPlaceholders(int count)
    {
        return string.Join(", ", Enumerable.Range(0, count).Select(static index => $"{{{index}}}"));
    }

    private static BindingType CreateProjectedHandleType(
        ManagedTypeOverride managedTypeOverride,
        string effectiveCppTypeName,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        IReadOnlyDictionary<string, BindingType> genericParameters)
    {
        var rawConcreteArguments = ShouldInstantiateConfiguredGenericType(managedTypeOverride)
            ? ParseConcreteTemplateArgumentsOrOpaque(effectiveCppTypeName, recordsByName, enumsByName, managedTypeOverrides, genericParameters)
            : Array.Empty<BindingType>();
        var concreteArguments = ProjectVisibleTemplateArguments(
            rawConcreteArguments,
            GetVisibleTemplateParameterIndices(managedTypeOverride, rawConcreteArguments.Count));

        if (concreteArguments.Count != 1 || concreteArguments[0].Kind != BindingTypeKind.Handle)
        {
            throw new NotSupportedException($"Projected managed_type on '{managedTypeOverride.CppTypeName}' must resolve to a single handle-like managed type.");
        }

        var projectedType = concreteArguments[0];
        var projectedRecord = projectedType.RecordId is null
            ? null
            : recordsByName.Values.Distinct().FirstOrDefault(candidate => string.Equals(candidate.SymbolId, projectedType.RecordId, StringComparison.Ordinal));
        var publicTypeName = projectedRecord?.ManagedTypeFormat ?? projectedRecord?.ManagedName ?? projectedType.ManagedTypeName;
        var genericArguments = projectedRecord is not null ? projectedType.GenericArguments : null;

        var projectedHandleType = BindingType.CreateHandle(
            effectiveCppTypeName,
            publicTypeName,
            projectedType.RecordId,
            managedTypeOverride.MarshallerTypeName,
            genericArguments);

        if (projectedRecord is not null)
        {
            projectedHandleType.SetManagedTypeFormatOverride(projectedRecord.ManagedTypeFormat ?? projectedRecord.ManagedName);
        }

        return projectedHandleType;
    }

    private static bool ShouldReturnOwnedHandle(
        ClangSharp.Type sourceType,
        BindingType mappedType,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        bool preferReturnOverride = true)
    {
        if (mappedType.Kind != BindingTypeKind.Handle)
        {
            return false;
        }

        var normalizedType = sourceType.Handle.CanonicalType;
        if (TypeIsAnyReference(normalizedType))
        {
            return preferReturnOverride && MarshallerConfigExtractor.ReturnTypeOwnsHandle(
                MarshallerConfigExtractor.CreateCppTypePattern(sourceType),
                managedTypeOverrides
                    .Where(static entry => entry.Key.StartsWith("return:", StringComparison.Ordinal))
                    .Select(static entry => entry.Value));
        }

        var spelledTypeName = NormalizeTypeName(sourceType.Handle.Spelling.ToString());
        var aliasResolvedTypeName = SelectConcreteTemplateTypeName(spelledTypeName, ResolveAliasTypeName(spelledTypeName));
        var canonicalTypeName = SelectConcreteTemplateTypeName(
            NormalizeTypeName(normalizedType.Spelling.ToString()),
            GetUnderlyingTypeName(sourceType.Handle));
        var overrideLookupTypeName = TryGetOwnedManagedAliasInfo(sourceType.Handle, out _)
            ? spelledTypeName
            : aliasResolvedTypeName;
        var resolvedTypeName = SelectConcreteTemplateTypeName(aliasResolvedTypeName, canonicalTypeName);
        var qualifiedSpelledTypeName = GetQualifiedTypeName(sourceType.Handle, spelledTypeName);

        if (preferReturnOverride &&
            TryGetManagedTypeOverride(overrideLookupTypeName, resolvedTypeName, qualifiedSpelledTypeName, managedTypeOverrides, isReturn: true, out var returnOverride) &&
            string.Equals(returnOverride?.ReturnOwnership, "owning", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return TryGetManagedTypeOverride(overrideLookupTypeName, resolvedTypeName, qualifiedSpelledTypeName, managedTypeOverrides, isReturn: false, out var overrideValue) &&
            string.Equals(overrideValue?.ReturnOwnership, "owning", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFunctionPointerType(CXType type)
    {
        var normalized = type.CanonicalType;
        var normalizedText = NormalizeTypeName(normalized.Spelling.ToString());
        var underlyingText = GetUnderlyingTypeName(type);
        var aliasText = ResolveAliasTypeName(NormalizeTypeName(type.Spelling.ToString()));
        var canonicalAliasText = ResolveAliasTypeName(NormalizeTypeName(type.CanonicalType.Spelling.ToString()));
        return TypeIsFunctionProto(normalized) ||
            (TypeIsPointer(normalized) && TypeIsFunctionProto(normalized.PointeeType.CanonicalType)) ||
            normalizedText.Contains("(*)", StringComparison.Ordinal) ||
            underlyingText.Contains("(*)", StringComparison.Ordinal) ||
            aliasText.Contains("(*)", StringComparison.Ordinal) ||
            canonicalAliasText.Contains("(*)", StringComparison.Ordinal) ||
            normalizedText.Contains(")(", StringComparison.Ordinal) ||
            underlyingText.Contains(")(", StringComparison.Ordinal) ||
            aliasText.Contains(")(", StringComparison.Ordinal) ||
            canonicalAliasText.Contains(")(", StringComparison.Ordinal);
    }

    private static bool IsPrimitiveValueType(CXType type)
    {
        return type.CanonicalType.kind is
            CXTypeKind.CXType_Bool or
            CXTypeKind.CXType_Char_S or
            CXTypeKind.CXType_SChar or
            CXTypeKind.CXType_Char_U or
            CXTypeKind.CXType_UChar or
            CXTypeKind.CXType_Short or
            CXTypeKind.CXType_UShort or
            CXTypeKind.CXType_Int or
            CXTypeKind.CXType_UInt or
            CXTypeKind.CXType_LongLong or
            CXTypeKind.CXType_ULongLong or
            CXTypeKind.CXType_Float or
            CXTypeKind.CXType_Double or
            CXTypeKind.CXType_Long or
            CXTypeKind.CXType_ULong;
    }

    private static IReadOnlyList<BindingType> ProjectVisibleTemplateArguments(
        IReadOnlyList<BindingType> genericArguments,
        IReadOnlyList<int> visibleIndices)
    {
        if (genericArguments.Count == 0 || visibleIndices.Count == 0)
        {
            return genericArguments;
        }

        var projected = new List<BindingType>(visibleIndices.Count);
        foreach (var index in visibleIndices)
        {
            if (index >= 0 && index < genericArguments.Count)
            {
                projected.Add(genericArguments[index]);
            }
        }

        return projected.Count == 0 ? genericArguments : projected;
    }

    private static IReadOnlyList<BindingType> ParseVisibleRecordTemplateArguments(
        RecordBinding record,
        string cppTypeName,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        IReadOnlyDictionary<string, BindingType> genericParameters,
        string? completeCppTypeName = null)
    {
        var rawGenericArguments = ParseConcreteTemplateArgumentsOrOpaque(cppTypeName, recordsByName, enumsByName, managedTypeOverrides, genericParameters);
        if (IsFixedArrayRecordOrConfigured(record, cppTypeName, managedTypeOverrides))
        {
            return rawGenericArguments;
        }

        var visibleIndices = GetVisibleRecordTemplateParameterIndices(record, rawGenericArguments.Count);
        if (visibleIndices.Count == 0)
        {
            visibleIndices = GetVisibleConfiguredTemplateParameterIndices(record, cppTypeName, managedTypeOverrides, rawGenericArguments.Count);
        }

        // A spelling that omits defaulted arguments the managed type exposes takes the
        // defaulted tail from the complete specialization.
        var exposedArgumentCount = visibleIndices.Count == 0 ? record.GenericParameters.Count : visibleIndices.Max() + 1;
        if (completeCppTypeName is not null && exposedArgumentCount > rawGenericArguments.Count)
        {
            var completeArguments = ParseConcreteTemplateArgumentsOrOpaque(completeCppTypeName, recordsByName, enumsByName, managedTypeOverrides, genericParameters);
            if (completeArguments.Count > rawGenericArguments.Count)
            {
                rawGenericArguments = rawGenericArguments.Concat(completeArguments.Skip(rawGenericArguments.Count)).ToArray();
            }
        }

        return ProjectVisibleTemplateArguments(
            rawGenericArguments,
            visibleIndices);
    }

    private static bool IsFixedArrayRecordOrConfigured(
        RecordBinding record,
        string cppTypeName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides)
    {
        if (record.GetMetadata<bool>(MetadataKeys.FixedArrayRecord))
        {
            return true;
        }

        var normalizedTypeName = NormalizeTypeName(cppTypeName);
        var resolvedTypeName = ResolveAliasTypeName(normalizedTypeName);
        var qualifiedTypeName = TryGetQualifiedAliasOriginName(normalizedTypeName, out var aliasOriginName)
            ? aliasOriginName
            : normalizedTypeName;
        if (TryGetManagedTypeOverride(
                normalizedTypeName,
                resolvedTypeName,
                qualifiedTypeName,
                managedTypeOverrides,
                isReturn: false,
                out var managedTypeOverride) &&
            managedTypeOverride?.FixedArray == true)
        {
            return true;
        }

        return IsFixedArrayConfiguredType(normalizedTypeName, managedTypeOverrides);
    }

    private static bool IsDynamicArrayRecordOrConfigured(
        RecordBinding record,
        string cppTypeName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides)
    {
        if (record.GetMetadata<bool>(MetadataKeys.DynamicArrayRecord))
        {
            return true;
        }

        var normalizedTypeName = NormalizeTypeName(cppTypeName);
        var resolvedTypeName = ResolveAliasTypeName(normalizedTypeName);
        var qualifiedTypeName = TryGetQualifiedAliasOriginName(normalizedTypeName, out var aliasOriginName)
            ? aliasOriginName
            : normalizedTypeName;
        if (TryGetManagedTypeOverride(
                normalizedTypeName,
                resolvedTypeName,
                qualifiedTypeName,
                managedTypeOverrides,
                isReturn: false,
                out var managedTypeOverride) &&
            managedTypeOverride?.DynamicArray == true)
        {
            return true;
        }

        return IsDynamicArrayConfiguredType(normalizedTypeName, managedTypeOverrides);
    }

    private static bool IsFixedArrayConfiguredType(
        string cppTypeName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides)
    {
        var normalizedTypeName = NormalizeTypeName(cppTypeName);
        if (!TryGetTopLevelTemplateArguments(normalizedTypeName, out var concreteHead, out _))
        {
            return false;
        }

        return managedTypeOverrides.Any(entry =>
            !entry.Key.StartsWith("return:", StringComparison.Ordinal) &&
            entry.Value.FixedArray &&
            TryGetTopLevelTemplateArguments(entry.Value.CppTypeName, out var configuredHead, out _) &&
            TemplateHeadsEquivalent(concreteHead, configuredHead));
    }

    private static bool IsDynamicArrayConfiguredType(
        string cppTypeName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides)
    {
        var normalizedTypeName = NormalizeTypeName(cppTypeName);
        if (!TryGetTopLevelTemplateArguments(normalizedTypeName, out var concreteHead, out _))
        {
            return false;
        }

        return managedTypeOverrides.Any(entry =>
            !entry.Key.StartsWith("return:", StringComparison.Ordinal) &&
            entry.Value.DynamicArray &&
            TryGetTopLevelTemplateArguments(entry.Value.CppTypeName, out var configuredHead, out _) &&
            TemplateHeadsEquivalent(concreteHead, configuredHead));
    }

    private static bool ShouldInstantiateConfiguredGenericType(ManagedTypeOverride managedTypeOverride)
    {
        return managedTypeOverride.TemplateParameterNames.Count > 0 ||
            managedTypeOverride.PublicTypeName.Contains('{', StringComparison.Ordinal);
    }

    private static IReadOnlyList<int> GetVisibleTemplateParameterIndices(ManagedTypeOverride managedTypeOverride, int concreteArgumentCount)
    {
        if (managedTypeOverride.VisibleTemplateParameterIndices.Count > 0)
        {
            return managedTypeOverride.VisibleTemplateParameterIndices;
        }

        if (!managedTypeOverride.PublicTypeName.Contains('{', StringComparison.Ordinal) || concreteArgumentCount == 0)
        {
            return Array.Empty<int>();
        }

        return ExtractVisiblePlaceholderIndices([managedTypeOverride.PublicTypeName], concreteArgumentCount);
    }

    private static IReadOnlyList<int> GetVisibleRecordTemplateParameterIndices(RecordBinding record, int concreteArgumentCount)
    {
        return ExtractVisiblePlaceholderIndices([record.ManagedTypeFormat, record.ManagedBaseTypeName], concreteArgumentCount);
    }

    private static IReadOnlyList<int> GetVisibleConfiguredTemplateParameterIndices(
        RecordBinding record,
        string cppTypeName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        int concreteArgumentCount)
    {
        var normalizedTypeName = NormalizeTypeName(cppTypeName);
        var resolvedTypeName = ResolveAliasTypeName(normalizedTypeName);
        var qualifiedTypeName = TryGetQualifiedAliasOriginName(normalizedTypeName, out var aliasOriginName)
            ? aliasOriginName
            : normalizedTypeName;
        if (!TryGetManagedTypeOverride(
                normalizedTypeName,
                resolvedTypeName,
                qualifiedTypeName,
                managedTypeOverrides,
                isReturn: false,
                out var managedTypeOverride) ||
            managedTypeOverride is null ||
            managedTypeOverride.ConstrainsTemplateArguments)
        {
            // A constrained pattern configures some specializations, not the template's
            // managed projection.
            managedTypeOverride = managedTypeOverrides
                .Where(static entry => !entry.Key.StartsWith("return:", StringComparison.Ordinal))
                .Select(static entry => entry.Value)
                .FirstOrDefault(candidate =>
                    !candidate.ConstrainsTemplateArguments &&
                    TryGetTopLevelTemplateArguments(candidate.CppTypeName, out var configuredHead, out _) &&
                    TryGetTopLevelTemplateArguments(normalizedTypeName, out var concreteHead, out _) &&
                    TemplateHeadsEquivalent(concreteHead, configuredHead));
            if (managedTypeOverride is null)
            {
                return Array.Empty<int>();
            }
        }

        return GetVisibleTemplateParameterIndices(managedTypeOverride, concreteArgumentCount);
    }

    private static IReadOnlyList<int> ExtractVisiblePlaceholderIndices(IEnumerable<string?> formats, int concreteArgumentCount)
    {
        if (concreteArgumentCount == 0)
        {
            return Array.Empty<int>();
        }

        var indices = new HashSet<int>();
        foreach (var format in formats)
        {
            if (string.IsNullOrWhiteSpace(format) || !format.Contains('{', StringComparison.Ordinal))
            {
                continue;
            }

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
                    placeholderIndex >= 0 &&
                    placeholderIndex < concreteArgumentCount)
                {
                    indices.Add(placeholderIndex);
                }

                index = closeIndex;
            }
        }

        return indices.OrderBy(static index => index).ToArray();
    }

    private static void ApplySyntheticGenericParameters(RecordBinding record, IReadOnlyList<string> templateParameterNames)
    {
        if (templateParameterNames.Count == 0)
        {
            return;
        }

        record.CppQualifiedName = StripGenericArguments(record.CppQualifiedName);
        if (record.NativeTemplateParameterCount == 0)
        {
            // Synthetic records originate in marshaller template declarations. With
            // no target class-template declaration to provide defaults, every pattern
            // parameter is required.
            record.NativeTemplateRequiredParameterCount = templateParameterNames.Count;
        }
        record.NativeTemplateParameterCount = Math.Max(record.NativeTemplateParameterCount, templateParameterNames.Count);

        if (record.GenericParameters.Count > 0)
        {
            return;
        }

        foreach (var (name, index) in templateParameterNames.Select((name, index) => (name, index)))
        {
            record.GenericParameters.Add(new BindingGenericParameter(name, index));
        }
    }

    private static bool IsRequiredNativeTemplateParameter(NamedDecl parameter) => parameter switch
    {
        TemplateTypeParmDecl typeParameter =>
            !typeParameter.HasDefaultArgument && !typeParameter.DefaultArgumentWasInherited,
        NonTypeTemplateParmDecl nonTypeParameter =>
            !nonTypeParameter.HasDefaultArgument && !nonTypeParameter.DefaultArgumentWasInherited,
        _ => true
    };

    private static int GetRequiredNativeTemplateParameterCount(ClassTemplateDecl classTemplate)
    {
        var declarations = new List<ClassTemplateDecl>();
        for (var declaration = classTemplate.MostRecentDecl;
             declaration is not null;
             declaration = declaration.PreviousDecl)
        {
            if (declarations.All(candidate => candidate.Handle != declaration.Handle))
                declarations.Add(declaration);
        }

        if (declarations.All(declaration => declaration.Handle != classTemplate.Handle))
            declarations.Add(classTemplate);
        if (declarations.All(declaration => declaration.Handle != classTemplate.CanonicalDecl.Handle))
            declarations.Add(classTemplate.CanonicalDecl);

        var parameterCount = declarations.Max(static declaration => declaration.TemplateParameters.Count);
        return Enumerable.Range(0, parameterCount).Count(index =>
            !declarations.Any(declaration =>
                index < declaration.TemplateParameters.Count &&
                !IsRequiredNativeTemplateParameter(declaration.TemplateParameters[index])));
    }

    private static IReadOnlyList<BindingType> ParseConcreteTemplateArgumentsOrOpaque(
        string cppTypeName,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        IReadOnlyDictionary<string, BindingType> genericParameters)
    {
        if (!TryGetTopLevelTemplateArguments(cppTypeName, out _, out var arguments))
        {
            return Array.Empty<BindingType>();
        }

        var result = new List<BindingType>(arguments.Count);
        foreach (var argument in arguments)
        {
            try
            {
                // A template argument names a C++ type, not its managed-call ABI
                // projection. Record values, for example, are represented by
                // pointer-backed managed wrappers but must remain `T` (not `T*`)
                // in `Container<T>`. Preserve the spelling from the enclosing
                // template-id while retaining the parsed managed projection.
                result.Add(ParseTypeToken(argument, recordsByName, enumsByName, managedTypeOverrides, genericParameters)
                    .WithCppTypeName(argument));
            }
            catch (NotSupportedException)
            {
                result.Add(BindingType.CreateConfigured(argument, argument));
            }
        }

        return result;
    }

    private static string StripGenericArguments(string name)
    {
        var genericStart = name.IndexOf('<');
        return genericStart >= 0 ? name[..genericStart] : name;
    }

    private static string GetSimpleManagedTypeName(string managedTypeName)
    {
        var typeHead = StripGenericArguments(managedTypeName);
        var lastDot = typeHead.LastIndexOf('.');
        return lastDot >= 0 ? typeHead[(lastDot + 1)..] : typeHead;
    }

    private static string ToPascalCase(string nativeName)
    {
        return string.Concat(
            nativeName
                .Split('_', StringSplitOptions.RemoveEmptyEntries)
                .Select(static part => char.ToUpperInvariant(part[0]) + NormalizePascalCaseSuffix(part)));
    }

    private static string NormalizePascalCaseSuffix(string part)
    {
        if (part.Length <= 1)
        {
            return string.Empty;
        }

        return part.All(static c => !char.IsLetter(c) || char.IsUpper(c))
            ? part[1..].ToLowerInvariant()
            : part[1..];
    }

    private static string EscapeManagedIdentifier(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return CSharpKeywords.Contains(name) ? $"@{name}" : name;
    }

    private static string GetRecordQualifiedName(CXType type, IDictionary<string, RecordBinding> recordsByName)
    {
        if (TryGetQualifiedNamedType(type, out var declaration, out _, out var semanticQualifiedName) &&
            !declaration.SemanticParent.Equals(declaration.LexicalParent))
        {
            return semanticQualifiedName;
        }

        var spelling = type.Spelling.ToString();
        var normalizedName = NormalizeNamedTypeName(StripGenericArguments(spelling));
        var canonicalName = NormalizeNamedTypeName(StripGenericArguments(type.CanonicalType.Spelling.ToString()));
        var unqualifiedName = normalizedName.Contains("::", StringComparison.Ordinal)
            ? normalizedName[(normalizedName.LastIndexOf("::", StringComparison.Ordinal) + 2)..]
            : normalizedName;

        if (recordsByName.TryGetValue(normalizedName, out _))
        {
            return normalizedName;
        }

        if (!string.Equals(canonicalName, normalizedName, StringComparison.Ordinal) &&
            recordsByName.TryGetValue(canonicalName, out _))
        {
            return canonicalName;
        }

        if (canonicalName.Contains("::", StringComparison.Ordinal))
        {
            return canonicalName;
        }

        if (normalizedName.Contains("::", StringComparison.Ordinal))
        {
            return normalizedName;
        }

        if (TryGetQualifiedNamedType(type, out _, out _, out var qualifiedName))
        {
            var strippedQualifiedName = NormalizeNamedTypeName(StripGenericArguments(qualifiedName));
            if (!string.IsNullOrWhiteSpace(strippedQualifiedName))
            {
                return strippedQualifiedName;
            }
        }

        if (recordsByName.TryGetValue(unqualifiedName, out _))
        {
            return unqualifiedName;
        }

        return unqualifiedName;
    }

    private static string ResolveQualifiedTypeName(
        ClangSharp.Type type,
        IDictionary<string, RecordBinding> recordsByName,
        IReadOnlyDictionary<string, BindingType>? genericParameters = null,
        BindingType? mappedType = null)
    {
        if (TryGetOwnedManagedAliasInfo(type.Handle, out _))
        {
            return ResolveQualifiedTypeName(type.Handle, recordsByName, genericParameters, mappedType);
        }

        return RequiresAstTypeFormatting(type)
            ? FormatCppTypeFromAst(type, recordsByName, genericParameters)
            : ResolveQualifiedTypeName(type.Handle, recordsByName, genericParameters, mappedType);
    }

    private static string ResolveQualifiedTypeName(
        CXType type,
        IDictionary<string, RecordBinding> recordsByName,
        IReadOnlyDictionary<string, BindingType>? genericParameters = null,
        BindingType? mappedType = null)
    {
        var spelling = NormalizeTypeName(type.Spelling.ToString());
        var canonicalTypeName = NormalizeTypeName(type.CanonicalType.Spelling.ToString());

        if (TryGetOwnedManagedAliasInfo(type, out var aliasInfo))
        {
            return BuildQualifiedAliasSurfaceTypeName(type, aliasInfo);
        }

        if (TryGetQualifiedNamedType(type, out var declaration, out _, out var semanticQualifiedName))
        {
            if (!declaration.SemanticParent.Equals(declaration.LexicalParent))
            {
                return BuildQualifiedNamedTypeSurface(type, semanticQualifiedName);
            }
        }

        if (TryGetQualifiedNamedType(type, out _, out var unqualifiedName, out var qualifiedName))
        {
            var surfaceNamedType = NormalizeNamedTypeName(spelling)
                .TrimEnd('*', '&')
                .TrimEnd();
            if (qualifiedName.EndsWith($"::{surfaceNamedType}", StringComparison.Ordinal))
            {
                var surfacePattern = $@"(?<![:\w]){Regex.Escape(surfaceNamedType)}(?![:\w])";
                spelling = Regex.Replace(
                    spelling,
                    surfacePattern,
                    qualifiedName,
                    RegexOptions.CultureInvariant);
            }

            var replacementSource = qualifiedName.Contains('<', StringComparison.Ordinal)
                ? StripGenericArguments(unqualifiedName)
                : unqualifiedName;
            var replacementTarget = qualifiedName.Contains('<', StringComparison.Ordinal)
                ? StripGenericArguments(qualifiedName)
                : qualifiedName;

            var pattern = $@"(?<![:\w]){Regex.Escape(replacementSource)}(?![:\w])";
            spelling = Regex.Replace(spelling, pattern, replacementTarget, RegexOptions.CultureInvariant);
        }

        foreach (var record in recordsByName.Values.Distinct().OrderByDescending(static record => record.NativeName.Length))
        {
            if (record.IsGeneric)
            {
                continue;
            }

            var pattern = $@"(?<![:\w]){Regex.Escape(record.NativeName)}(?![:\w])";
            spelling = Regex.Replace(spelling, pattern, record.CppQualifiedName, RegexOptions.CultureInvariant);
        }

        // For templated spellings, keep the spelled outer surface plus
        // const/ref qualifiers but replace
        // the top-level template arguments with Clang's canonical arguments.
        // This avoids short-name collisions such as Vector3 resolving to
        // Rml::Vector3 instead of Urho3D::Vector3 inside container signatures.
        spelling = RewriteTopLevelTemplateArgumentsFromCanonical(spelling, canonicalTypeName);

        spelling = Regex.Replace(
            spelling,
            @"(?<![:\w])_Bool(?![:\w])",
            "bool",
            RegexOptions.CultureInvariant);

        if (genericParameters is not null)
        {
            foreach (var genericParameterName in genericParameters.Keys.OrderByDescending(static name => name.Length))
            {
                var qualifiedGenericParameter =
                    $@"(?<![:\w])(?:[A-Za-z_]\w*::)+{Regex.Escape(genericParameterName)}(?!\w)";
                spelling = Regex.Replace(
                    spelling,
                    qualifiedGenericParameter,
                    genericParameterName,
                    RegexOptions.CultureInvariant);
            }
        }

        if (mappedType is { Kind: BindingTypeKind.Primitive, PrimitiveKind: BindingPrimitiveKind.Unsupported } &&
            mappedType.CppTypeName.Contains("::", StringComparison.Ordinal))
        {
            var surfaceTypeName = StripCallableCvAndIndirection(spelling);
            if (!surfaceTypeName.Contains("::", StringComparison.Ordinal) &&
                !surfaceTypeName.Contains('<', StringComparison.Ordinal))
            {
                var surfacePattern = $@"(?<![:\w]){Regex.Escape(surfaceTypeName)}(?![:\w])";
                spelling = Regex.Replace(
                    spelling,
                    surfacePattern,
                    mappedType.CppTypeName,
                    RegexOptions.CultureInvariant);
            }
        }

        return spelling;
    }

    private static bool RequiresAstTypeFormatting(ClangSharp.Type type)
    {
        if (type is TemplateTypeParmType or SubstTemplateTypeParmType or TemplateSpecializationType or FunctionType ||
            type is RecordType { Decl: ClassTemplateSpecializationDecl })
        {
            return true;
        }

        if (type is TypedefType typedefType && IsRecordScope(typedefType.Decl.Handle.SemanticParent.Kind))
        {
            return true;
        }

        return type switch
        {
            ReferenceType referenceType => RequiresAstTypeFormatting(referenceType.PointeeType),
            PointerType pointerType => RequiresAstTypeFormatting(pointerType.PointeeType),
            ElaboratedType elaboratedType => RequiresAstTypeFormatting(elaboratedType.NamedType),
            TypedefType nestedTypedefType => RequiresAstTypeFormatting(nestedTypedefType.Desugar),
            ArrayType arrayType => RequiresAstTypeFormatting(arrayType.ElementType),
            _ => false
        };
    }

    private static bool IsRecordScope(CXCursorKind kind)
    {
        return kind is
            CXCursorKind.CXCursor_StructDecl or
            CXCursorKind.CXCursor_UnionDecl or
            CXCursorKind.CXCursor_ClassDecl or
            CXCursorKind.CXCursor_ClassTemplate or
            CXCursorKind.CXCursor_ClassTemplatePartialSpecialization;
    }

    private static string FormatCppTypeFromAst(
        ClangSharp.Type type,
        IDictionary<string, RecordBinding> recordsByName,
        IReadOnlyDictionary<string, BindingType>? genericParameters)
    {
        switch (type)
        {
            case LValueReferenceType referenceType:
                return FormatCppTypeFromAst(referenceType.PointeeType, recordsByName, genericParameters) + "&";
            case RValueReferenceType referenceType:
                return FormatCppTypeFromAst(referenceType.PointeeType, recordsByName, genericParameters) + "&&";
            case PointerType pointerType when pointerType.PointeeType is FunctionType functionType:
                return FormatCppFunctionPointerFromAst(
                    functionType,
                    pointerType.IsLocalConstQualified,
                    recordsByName,
                    genericParameters);
            case PointerType pointerType:
                return FormatCppTypeFromAst(pointerType.PointeeType, recordsByName, genericParameters) +
                    "*" + (pointerType.IsLocalConstQualified ? " const" : string.Empty);
            case ElaboratedType elaboratedType:
                return AddLocalConstQualifier(
                    elaboratedType,
                    FormatCppTypeFromAst(elaboratedType.NamedType, recordsByName, genericParameters));
            case TypedefType typedefType:
                return AddLocalConstQualifier(
                    typedefType,
                    FormatCppTypeFromAst(typedefType.Desugar, recordsByName, genericParameters));
            case SubstTemplateTypeParmType substitutionType:
                return AddLocalConstQualifier(
                    substitutionType,
                    FormatCppTypeFromAst(substitutionType.ReplacementType, recordsByName, genericParameters));
            case TemplateSpecializationType templateType when
                TryFormatTemplateSpecializationFromAst(templateType, recordsByName, genericParameters, out var specialization):
                return AddLocalConstQualifier(templateType, specialization);
            case RecordType { Decl: ClassTemplateSpecializationDecl classSpecialization } recordType when
                TryFormatClassTemplateSpecializationFromAst(classSpecialization, recordsByName, genericParameters, out var specialization):
                return AddLocalConstQualifier(recordType, specialization);
            case FunctionProtoType functionType:
                return FormatCppFunctionTypeFromAst(functionType, recordsByName, genericParameters);
            case TemplateTypeParmType templateParameterType:
                var parameterName = templateParameterType.Decl.Name ?? templateParameterType.Decl.Spelling;
                if (!string.IsNullOrWhiteSpace(parameterName) &&
                    genericParameters?.ContainsKey(parameterName) == true)
                {
                    return AddLocalConstQualifier(templateParameterType, parameterName);
                }
                break;
        }

        return ResolveQualifiedTypeName(type.Handle, recordsByName, genericParameters);
    }

    private static string FormatCppFunctionPointerFromAst(
        FunctionType functionType,
        bool isConstPointer,
        IDictionary<string, RecordBinding> recordsByName,
        IReadOnlyDictionary<string, BindingType>? genericParameters)
    {
        var parameterTypes = functionType is FunctionProtoType prototype
            ? prototype.ParamTypes.Select(parameter => FormatCppTypeFromAst(parameter, recordsByName, genericParameters))
            : [];
        var returnType = FormatCppTypeFromAst(functionType.ReturnType, recordsByName, genericParameters);
        return $"auto(*{(isConstPointer ? " const" : string.Empty)})({string.Join(", ", parameterTypes)}) -> {returnType}";
    }

    private static string FormatCppFunctionTypeFromAst(
        FunctionProtoType functionType,
        IDictionary<string, RecordBinding> recordsByName,
        IReadOnlyDictionary<string, BindingType>? genericParameters)
    {
        var parameterTypes = functionType.ParamTypes
            .Select(parameter => FormatCppTypeFromAst(parameter, recordsByName, genericParameters));
        var returnType = FormatCppTypeFromAst(functionType.ReturnType, recordsByName, genericParameters);
        return $"{returnType}({string.Join(", ", parameterTypes)})";
    }

    private static bool TryFormatTemplateSpecializationFromAst(
        TemplateSpecializationType templateType,
        IDictionary<string, RecordBinding> recordsByName,
        IReadOnlyDictionary<string, BindingType>? genericParameters,
        out string result)
    {
        var templateDeclaration = templateType.TemplateName.AsTemplateDecl;
        var qualifiedName = templateDeclaration is null ? string.Empty : GetCppQualifiedName(templateDeclaration);
        if (string.IsNullOrWhiteSpace(qualifiedName))
        {
            result = string.Empty;
            return false;
        }

        if (!TryFormatTemplateArgumentsFromAst(templateType.Args, recordsByName, genericParameters, out var arguments))
        {
            result = string.Empty;
            return false;
        }
        result = $"{qualifiedName}<{string.Join(", ", arguments)}>";
        return true;
    }

    private static bool TryFormatClassTemplateSpecializationFromAst(
        ClassTemplateSpecializationDecl specialization,
        IDictionary<string, RecordBinding> recordsByName,
        IReadOnlyDictionary<string, BindingType>? genericParameters,
        out string result)
    {
        var specializedTemplate = MarshallerConfigExtractor.GetSpecializedTemplateDecl(specialization);
        var qualifiedName = specializedTemplate is null
            ? string.Empty
            : GetCppQualifiedName(specializedTemplate);
        if (string.IsNullOrWhiteSpace(qualifiedName))
        {
            result = string.Empty;
            return false;
        }

        // A specialization declaration's arguments carry the sugar of wherever it was
        // declared (eastl's explicit hash<string>), which is not valid at a thunk's scope.
        if (!TryFormatTemplateArgumentsFromAst(specialization.TemplateArgs, recordsByName, genericParameters, out var arguments, canonicalTypes: true))
        {
            result = string.Empty;
            return false;
        }
        result = $"{qualifiedName}<{string.Join(", ", arguments)}>";
        return true;
    }

    private static bool TryFormatTemplateArgumentsFromAst(
        IReadOnlyList<TemplateArgument> arguments,
        IDictionary<string, RecordBinding> recordsByName,
        IReadOnlyDictionary<string, BindingType>? genericParameters,
        out IReadOnlyList<string> formattedArguments,
        bool canonicalTypes = false)
    {
        var result = new List<string>(arguments.Count);
        foreach (var argument in arguments)
        {
            switch (argument.Kind)
            {
                case CXTemplateArgumentKind.CXTemplateArgumentKind_Type:
                    result.Add(FormatCppTypeFromAst(canonicalTypes ? argument.AsType.CanonicalType : argument.AsType, recordsByName, genericParameters));
                    break;
                case CXTemplateArgumentKind.CXTemplateArgumentKind_Integral:
                    result.Add(BindingTemplateArgument.FormatIntegral(argument.AsIntegral, IsBooleanType(argument.IntegralType.Handle)));
                    break;
                case CXTemplateArgumentKind.CXTemplateArgumentKind_Expression when
                    TryGetIntegralTemplateArgument(argument.AsExpr, out var integralValue, out var isBoolean):
                    result.Add(BindingTemplateArgument.FormatIntegral(integralValue, isBoolean));
                    break;
                default:
                    formattedArguments = Array.Empty<string>();
                    return false;
            }
        }

        formattedArguments = result;
        return true;
    }

    private static string AddLocalConstQualifier(ClangSharp.Type type, string renderedType)
    {
        return type.IsLocalConstQualified ? "const " + renderedType : renderedType;
    }

    private static string StripCallableCvAndIndirection(string typeName)
    {
        var result = typeName.Trim();
        while (result.StartsWith("const ", StringComparison.Ordinal) ||
               result.StartsWith("volatile ", StringComparison.Ordinal))
        {
            result = result.StartsWith("const ", StringComparison.Ordinal)
                ? result[6..].TrimStart()
                : result[9..].TrimStart();
        }

        while (result.EndsWith(" const", StringComparison.Ordinal) ||
               result.EndsWith(" volatile", StringComparison.Ordinal) ||
               result.EndsWith('&') ||
               result.EndsWith('*'))
        {
            if (result.EndsWith(" const", StringComparison.Ordinal))
                result = result[..^6].TrimEnd();
            else if (result.EndsWith(" volatile", StringComparison.Ordinal))
                result = result[..^9].TrimEnd();
            else
                result = result[..^1].TrimEnd();
        }

        return result;
    }

    private static string ResolveParameterCppTypeName(
        ClangSharp.Type type,
        IDictionary<string, RecordBinding> recordsByName,
        IReadOnlyDictionary<string, BindingType>? genericParameters = null,
        BindingType? mappedType = null)
    {
        if (!IsIncompleteArrayType(type.Handle.CanonicalType))
        {
            return ResolveQualifiedTypeName(type, recordsByName, genericParameters, mappedType);
        }

        var arrayType = type as ArrayType ?? type.CanonicalType as ArrayType
            ?? throw new InvalidOperationException("Clang reported an incomplete array without an array AST node.");
        var elementTypeName = ResolveQualifiedTypeName(arrayType.ElementType, recordsByName, genericParameters, mappedType);
        return NormalizeTypeName(elementTypeName + "*");
    }

    private static string RewriteTopLevelTemplateArgumentsFromCanonical(string surfaceTypeName, string canonicalTypeName)
    {
        var surfaceStart = surfaceTypeName.IndexOf('<');
        var surfaceEnd = surfaceTypeName.LastIndexOf('>');
        if (surfaceStart < 0 || surfaceEnd <= surfaceStart)
        {
            return surfaceTypeName;
        }

        if (!TryGetTopLevelTemplateArguments(canonicalTypeName, out _, out var canonicalArguments))
        {
            return surfaceTypeName;
        }

        return $"{surfaceTypeName[..(surfaceStart + 1)]}{string.Join(", ", canonicalArguments)}{surfaceTypeName[surfaceEnd..]}";
    }

    private static string BuildQualifiedAliasSurfaceTypeName(CXType type, OwnedAliasInfo aliasInfo)
    {
        var qualifiedAliasName = aliasInfo.CppQualifiedName;
        var spelledTypeName = StripTopLevelCvQualifier(NormalizeTypeName(type.Spelling.ToString()));
        if (string.Equals(spelledTypeName, aliasInfo.CppQualifiedName, StringComparison.Ordinal) ||
            string.Equals(spelledTypeName, aliasInfo.NativeName, StringComparison.Ordinal))
        {
            var aliasPrefix = type.IsConstQualified ? "const " : string.Empty;
            return aliasPrefix + qualifiedAliasName;
        }

        var indirections = new List<(char Token, bool IsConst)>();
        var current = type;
        while (true)
        {
            var currentTypeName = StripTopLevelCvQualifier(NormalizeTypeName(current.Spelling.ToString()));
            if (string.Equals(currentTypeName, aliasInfo.CppQualifiedName, StringComparison.Ordinal) ||
                string.Equals(currentTypeName, aliasInfo.NativeName, StringComparison.Ordinal))
            {
                break;
            }

            if (TypeIsAnyReference(current.CanonicalType))
            {
                indirections.Add(('&', current.IsConstQualified));
                current = current.PointeeType;
                continue;
            }

            if (TypeIsPointer(current.CanonicalType))
            {
                indirections.Add(('*', current.IsConstQualified));
                current = current.PointeeType;
                continue;
            }

            break;
        }

        var prefix = current.IsConstQualified ? "const " : string.Empty;
        return prefix + qualifiedAliasName + RenderIndirections(indirections);
    }

    private static string BuildQualifiedNamedTypeSurface(CXType type, string qualifiedName)
    {
        var indirections = new List<(char Token, bool IsConst)>();
        var current = type;
        while (TypeIsAnyReference(current) || TypeIsPointer(current))
        {
            indirections.Add((TypeIsAnyReference(current) ? '&' : '*', current.IsConstQualified));
            current = current.PointeeType;
        }

        return $"{(current.IsConstQualified ? "const " : string.Empty)}{qualifiedName}{RenderIndirections(indirections)}";
    }

    private static string RenderIndirections(IEnumerable<(char Token, bool IsConst)> indirections)
    {
        return string.Concat(indirections
            .Reverse()
            .Select(static indirection => $"{indirection.Token}{(indirection.IsConst ? " const" : string.Empty)}"));
    }

    private static bool TryGetQualifiedNamedType(
        CXType type,
        out CXCursor declaration,
        out string unqualifiedName,
        out string qualifiedName)
    {
        var sourceType = type;
        while (TypeIsAnyReference(sourceType) || TypeIsPointer(sourceType))
        {
            sourceType = sourceType.PointeeType;
        }

        declaration = sourceType.Declaration.CanonicalCursor;
        return TryPopulateQualifiedNamedType(
            declaration,
            out unqualifiedName,
            out qualifiedName);
    }

    private static bool TryPopulateQualifiedNamedType(CXCursor declaration, out string unqualifiedName, out string qualifiedName)
    {
        if (string.IsNullOrWhiteSpace(declaration.Spelling.ToString()))
        {
            unqualifiedName = string.Empty;
            qualifiedName = string.Empty;
            return false;
        }

        if (declaration.Kind is not (CXCursorKind.CXCursor_StructDecl or
                                     CXCursorKind.CXCursor_UnionDecl or
                                     CXCursorKind.CXCursor_ClassDecl or
                                     CXCursorKind.CXCursor_ClassTemplate or
                                     CXCursorKind.CXCursor_ClassTemplatePartialSpecialization or
                                     CXCursorKind.CXCursor_EnumDecl or
                                     CXCursorKind.CXCursor_TypedefDecl or
                                     CXCursorKind.CXCursor_TypeAliasDecl))
        {
            unqualifiedName = string.Empty;
            qualifiedName = string.Empty;
            return false;
        }

        unqualifiedName = declaration.Spelling.ToString();
        qualifiedName = GetCppQualifiedName(declaration);
        return !string.IsNullOrWhiteSpace(qualifiedName);
    }

    private static bool ShouldParseRecordMember(RecordBinding record, CX_CXXAccessSpecifier accessSpecifier)
    {
        return accessSpecifier switch
        {
            CX_CXXAccessSpecifier.CX_CXXPrivate => false,
            CX_CXXAccessSpecifier.CX_CXXProtected => record.IsDirector,
            _ => true
        };
    }

    private static BindingAccessModifier MapAccessModifier(CX_CXXAccessSpecifier accessSpecifier)
    {
        return accessSpecifier switch
        {
            CX_CXXAccessSpecifier.CX_CXXProtected => BindingAccessModifier.Protected,
            CX_CXXAccessSpecifier.CX_CXXPrivate => BindingAccessModifier.Private,
            _ => BindingAccessModifier.Public
        };
    }

    private static void UpdatePolymorphism(RecordBinding record, IDictionary<string, RecordBinding> recordsByName)
    {
        if (record.Members.OfType<FunctionBinding>().Any(static function => function.IsVirtual))
        {
            record.IsPolymorphic = true;
            return;
        }

        if (string.IsNullOrWhiteSpace(record.ManagedBaseTypeName))
        {
            return;
        }

        if (recordsByName.Values.Distinct().Any(candidate =>
                string.Equals(candidate.ManagedName, record.ManagedBaseTypeName, StringComparison.Ordinal) &&
                candidate.IsPolymorphic))
        {
            record.IsPolymorphic = true;
        }
    }

    /// <summary>
    /// Fully qualified canonical spelling. Clang prints explicit specializations with
    /// the arguments as written in their declaration (eastl's hash&lt;string&gt;), which
    /// only resolve inside that namespace; rebuild template specializations from their
    /// canonical arguments instead.
    /// </summary>
    private static string FormatCanonicalCxTypeName(CXType type)
    {
        var canonical = type.CanonicalType;
        var declaration = canonical.Declaration;
        if (canonical.kind != CXTypeKind.CXType_Record ||
            canonical.NumTemplateArguments <= 0 ||
            declaration.IsNull)
        {
            return NormalizeTypeName(canonical.Spelling.ToString());
        }

        var arguments = new List<string>(canonical.NumTemplateArguments);
        for (var index = 0; index < canonical.NumTemplateArguments; ++index)
        {
            using var argument = canonical.GetTemplateArgument(checked((uint)index));
            if (argument.AsType.kind != CXTypeKind.CXType_Invalid)
            {
                arguments.Add(FormatCanonicalCxTypeName(argument.AsType));
            }
            else if (argument.IntegralType.kind != CXTypeKind.CXType_Invalid)
            {
                arguments.Add(BindingTemplateArgument.FormatIntegral(argument.AsIntegral, IsBooleanType(argument.IntegralType)));
            }
            else
            {
                return NormalizeTypeName(canonical.Spelling.ToString());
            }
        }

        var constPrefix = canonical.IsConstQualified ? "const " : string.Empty;
        return $"{constPrefix}{GetCppQualifiedName(declaration)}<{string.Join(", ", arguments)}>";
    }

    private static string GetCppQualifiedName(CXCursor cursor)
    {
        var names = CollectCppQualifierNames(cursor.SemanticParent);
        var ownName = cursor.Spelling.ToString();

        if (names.Count == 0)
        {
            return ownName;
        }

        names.Reverse();
        names.Add(ownName);
        return string.Join("::", names);
    }

    private static List<string> CollectCppQualifierNames(CXCursor start)
    {
        var names = new List<string>();
        var current = start;
        while (current.Kind is CXCursorKind.CXCursor_Namespace
            or CXCursorKind.CXCursor_ClassDecl
            or CXCursorKind.CXCursor_StructDecl)
        {
            var parentName = current.Kind == CXCursorKind.CXCursor_Namespace &&
                clang.Cursor_isInlineNamespace(current) != 0
                    ? string.Empty
                    : current.Spelling.ToString();
            if (!string.IsNullOrWhiteSpace(parentName))
            {
                names.Add(parentName);
            }

            current = current.SemanticParent;
        }

        return names;
    }

    private static string NormalizeNamedTypeName(string spelling)
    {
        return spelling
            .Replace("__cxx11::", string.Empty, StringComparison.Ordinal)
            .Replace("enum class ", string.Empty, StringComparison.Ordinal)
            .Replace("enum ", string.Empty, StringComparison.Ordinal)
            .Replace("const ", string.Empty, StringComparison.Ordinal)
            .Replace("struct ", string.Empty, StringComparison.Ordinal)
            .Replace("union ", string.Empty, StringComparison.Ordinal)
            .Replace("class ", string.Empty, StringComparison.Ordinal)
            .Trim();
    }

    /// <summary>
    /// Returns the fully-qualified C++ name for the type declaration
    /// referenced by <paramref name="type"/>.  When the type is a typedef,
    /// this yields the qualified alias name (e.g. "Urho3D::Vector3").
    /// Falls back to <paramref name="fallback"/> when no declaration
    /// cursor is available.
    /// </summary>
    private static string GetQualifiedTypeName(CXType type, string fallback)
    {
        // For desugared canonical types (e.g. "BaseIntegerVector2<int>"),
        // the origin-name index maps the canonical type to the typedef that
        // introduced it (e.g. "Urho3D::IntVector2").  Try this first so that
        // managed-type-override matching works for canonical parameter types.
        var canonical = NormalizeTypeName(type.CanonicalType.Spelling.ToString());
        if (string.IsNullOrEmpty(canonical))
        {
            return fallback;
        }

        // Exact match on the full canonical spelling.
        if (s_typeAliasOriginNames.TryGetValue(canonical, out var originNames) && originNames.Count > 0)
        {
            return PickBestOriginName(originNames);
        }

        // Exact match on the template head (e.g. "BaseIntegerVector2"
        // from "BaseIntegerVector2<int>").
        if (TryGetTopLevelTemplateArguments(canonical, out var head, out _) &&
            s_typeAliasOriginNames.TryGetValue(head, out originNames) && originNames.Count > 0)
        {
            return PickBestOriginName(originNames);
        }

        // Slower path: the origin-name index is keyed on the spelling
        // from Clang's UnderlyingType, which may differ from the canonical
        // spelling in its namespace prefix.  Use EndsWith to match across
        // these namespace-format differences.
        foreach (var kv in s_typeAliasOriginNames)
        {
            if ((kv.Key.EndsWith(canonical, StringComparison.Ordinal) ||
                 canonical.EndsWith(kv.Key, StringComparison.Ordinal)) &&
                kv.Value.Count > 0)
            {
                return PickBestOriginName(kv.Value);
            }
        }

        // Fall back to the AST cursor (gives the template class name for
        // template specializations, which is less useful but better than
        // the fallback parameter).
        var decl = type.Declaration;
        if (!decl.IsNull)
        {
            var qualified = GetCppQualifiedName(decl);
            if (!string.IsNullOrWhiteSpace(qualified))
            {
                return NormalizeTypeName(qualified);
            }
        }

        return fallback;
    }

    /// <summary>
    /// Picks the best typedef name from the origin-name list, preferring
    /// a fully-qualified name (with ::) over an unqualified one.
    /// </summary>
    private static string PickBestOriginName(List<string> originNames)
    {
        return originNames.FirstOrDefault(n => n.Contains("::", StringComparison.Ordinal)) ?? originNames[0];
    }

    private static string NormalizeTypeName(string spelling)
    {
        return Regex.Replace(spelling.Trim(), @"\s+", " ")
            .Replace("__cxx11::", string.Empty, StringComparison.Ordinal)
            .Replace(" *", "*", StringComparison.Ordinal)
            .Replace(" &", "&", StringComparison.Ordinal)
            .Replace(":: ", "::", StringComparison.Ordinal)
            .Replace(" ::", "::", StringComparison.Ordinal)
            .Replace(" <", "<", StringComparison.Ordinal)
            .Replace("> ", ">", StringComparison.Ordinal);
    }

    /// <summary>
    /// Checks whether a managed type name corresponds to a C# ref struct.
    /// </summary>
    private static bool IsManagedRefStruct(string publicTypeName) =>
        publicTypeName is "global::System.ReadOnlySpan<byte>";

    /// <summary>
    /// Maps a C/C++ integer type spelling to the corresponding BindingPrimitiveKind.
    /// Used when constructing enum bindings to capture the underlying integer type.
    /// </summary>
    private static BindingPrimitiveKind GetPrimitiveKindFromTypeSpelling(string spelling)
    {
        var normalized = NormalizeTypeName(spelling);
        return normalized switch
        {
            "unsigned char" => BindingPrimitiveKind.UInt8,
            "unsigned int" => BindingPrimitiveKind.UInt32,
            "unsigned short" => BindingPrimitiveKind.UInt16,
            "unsigned long long" => BindingPrimitiveKind.UInt64,
            "unsigned long" => BindingPrimitiveKind.UInt64,
            "signed char" => BindingPrimitiveKind.Int8,
            "int" => BindingPrimitiveKind.Int32,
            "short" => BindingPrimitiveKind.Int16,
            "long long" => BindingPrimitiveKind.Int64,
            "long" => BindingPrimitiveKind.Int64,
            _ => BindingPrimitiveKind.Int32
        };
    }

    private static bool TryGetPassKindOverride(CXCursor functionCursor, CXCursor parameterCursor, out BindingParameterDirection direction)
    {
        var match = FindSharpCppAttribute(GetSourceText(parameterCursor.TranslationUnit, parameterCursor.Extent), "in")
            ?? FindSharpCppAttribute(GetSourceText(parameterCursor.TranslationUnit, parameterCursor.Extent), "out")
            ?? FindSharpCppAttribute(GetSourceText(parameterCursor.TranslationUnit, parameterCursor.Extent), "ref");
        string parameterSourceText = string.Empty;
        if (match is null && TryGetParameterDeclarationSourceText(functionCursor, parameterCursor, out parameterSourceText))
        {
            match = FindSharpCppAttribute(parameterSourceText, "in")
                ?? FindSharpCppAttribute(parameterSourceText, "out")
                ?? FindSharpCppAttribute(parameterSourceText, "ref");
        }

        if (match is null)
        {
            direction = default;
            return false;
        }

        direction = match.Name switch
        {
            "in" => BindingParameterDirection.In,
            "ref" => BindingParameterDirection.Ref,
            "out" => BindingParameterDirection.Out,
            _ => throw new NotSupportedException($"Unsupported pass-kind attribute '{match.Name}'.")
        };
        return true;
    }

    private static void ApplyInferredInputPointerArrayMetadata(FunctionBinding function)
    {
        for (var index = 0; index < function.Parameters.Count - 2; ++index)
        {
            var capacity = function.Parameters[index];
            var count = function.Parameters[index + 1];
            var values = function.Parameters[index + 2];
            if (!IsArrayCapacityInputParameter(capacity) || !IsArrayCountOutputParameter(count))
                continue;

            if (values.Direction == BindingParameterDirection.Out &&
                values.AbiKind == BindingParameterAbiKind.Pointer &&
                IsRawPointerArrayElementType(values.Type))
            {
                values.Direction = BindingParameterDirection.In;
                values.SetMetadata(MetadataKeys.InputPointerArray, true);
                values.SetMetadata(MetadataKeys.WritablePointerArray, true);
            }
        }

        for (var index = 0; index < function.Parameters.Count - 1; ++index)
        {
            var first = function.Parameters[index];
            var second = function.Parameters[index + 1];
            if (IsArrayCountParameter(second))
                ApplyAdjacentInputPointerArrayMetadata(first);
            else if (IsArrayCountParameter(first))
                ApplyAdjacentInputPointerArrayMetadata(second);
        }

        var countParameterSeen = false;
        var handlePointerArraySeen = false;

        foreach (var parameter in function.Parameters)
        {
            if (IsArrayCountParameter(parameter))
                countParameterSeen = true;

            if (countParameterSeen && IsInputHandlePointerArrayParameter(parameter))
            {
                handlePointerArraySeen = true;
                continue;
            }

            if (!handlePointerArraySeen)
                continue;

            if (parameter.Direction == BindingParameterDirection.In &&
                parameter.AbiKind == BindingParameterAbiKind.Pointer &&
                parameter.Type.Kind == BindingTypeKind.Primitive)
            {
                parameter.SetMetadata(MetadataKeys.InputPointerArray, true);
                continue;
            }

            handlePointerArraySeen = false;
        }
    }

    private static void ApplyAdjacentInputPointerArrayMetadata(ParameterBinding parameter)
    {
        if (parameter.AbiKind != BindingParameterAbiKind.Pointer)
            return;

        // A counted by-reference projection (`const char** flags, size_t count`) is an array
        // of pointers whose elements cross as raw pointers.
        if (IsConstPointerToPointer(parameter) &&
            string.Equals(NormalizeTypeName(parameter.Type.CppTypeName), NormalizeTypeName(parameter.CppTypeName), StringComparison.Ordinal))
        {
            var elementCppTypeName = NormalizeTypeName(parameter.CppTypeName);
            parameter.Type = BindingType.CreateHandle(elementCppTypeName[..elementCppTypeName.LastIndexOf('*')].TrimEnd(), BindingType.IntPtr.ManagedTypeName);
            parameter.SetMetadata(MetadataKeys.InputPointerArray, true);
            return;
        }

        if (parameter.Direction == BindingParameterDirection.In &&
            IsRawPointerArrayElementType(parameter.Type))
        {
            parameter.SetMetadata(MetadataKeys.InputPointerArray, true);
        }
        else if (IsRawPointerArrayElementType(parameter.Type) &&
            !parameter.CppTypeName.Contains("const", StringComparison.Ordinal))
        {
            parameter.Direction = BindingParameterDirection.In;
            parameter.SetMetadata(MetadataKeys.InputPointerArray, true);
            parameter.SetMetadata(MetadataKeys.WritablePointerArray, true);
        }
        else if (IsConstPointerToPointer(parameter) && IsRawPointerHandle(parameter.Type))
        {
            parameter.Direction = BindingParameterDirection.In;
            parameter.SetMetadata(MetadataKeys.InputPointerArray, true);
        }

    }

    private static bool IsRawPointerHandle(BindingType type)
    {
        return type.Kind == BindingTypeKind.Handle &&
            string.IsNullOrEmpty(type.RecordId) &&
            string.Equals(type.ManagedTypeName, "global::System.IntPtr", StringComparison.Ordinal);
    }

    private static bool IsRawPointerArrayElementType(BindingType type) =>
        type.Kind is BindingTypeKind.Primitive or BindingTypeKind.Enum;

    private static bool IsConstPointerToPointer(ParameterBinding parameter)
    {
        return parameter.CppTypeName.IndexOf('*') != parameter.CppTypeName.LastIndexOf('*') &&
            parameter.CppTypeName.Contains("const", StringComparison.Ordinal);
    }

    private static bool IsArrayCountParameter(ParameterBinding parameter)
    {
        if (parameter.Direction != BindingParameterDirection.In ||
            parameter.AbiKind != BindingParameterAbiKind.Value ||
            parameter.Type.Kind != BindingTypeKind.Primitive ||
            parameter.Type.PrimitiveKind is not (
                BindingPrimitiveKind.Int8 or BindingPrimitiveKind.UInt8 or
                BindingPrimitiveKind.Int16 or BindingPrimitiveKind.UInt16 or
                BindingPrimitiveKind.Int32 or BindingPrimitiveKind.UInt32 or
                BindingPrimitiveKind.Int64 or BindingPrimitiveKind.UInt64 or
                BindingPrimitiveKind.NativeInt or BindingPrimitiveKind.NativeUInt))
        {
            return false;
        }

        return parameter.NativeName.StartsWith("Num", StringComparison.OrdinalIgnoreCase) ||
            parameter.NativeName.Contains("Count", StringComparison.OrdinalIgnoreCase) ||
            parameter.NativeName.EndsWith("Length", StringComparison.OrdinalIgnoreCase) ||
            parameter.NativeName.EndsWith("Size", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsArrayCapacityInputParameter(ParameterBinding parameter)
    {
        return parameter.Direction == BindingParameterDirection.In &&
            parameter.AbiKind == BindingParameterAbiKind.Value &&
            parameter.Type.Kind == BindingTypeKind.Primitive &&
            parameter.NativeName.Contains("Capacity", StringComparison.OrdinalIgnoreCase) &&
            parameter.NativeName.EndsWith("Input", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsArrayCountOutputParameter(ParameterBinding parameter)
    {
        return parameter.Direction == BindingParameterDirection.Out &&
            parameter.AbiKind == BindingParameterAbiKind.Pointer &&
            parameter.Type.Kind == BindingTypeKind.Primitive &&
            parameter.NativeName.Contains("Count", StringComparison.OrdinalIgnoreCase) &&
            parameter.NativeName.EndsWith("Output", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInputHandlePointerArrayParameter(ParameterBinding parameter)
    {
        return parameter.Direction == BindingParameterDirection.In &&
            parameter.AbiKind == BindingParameterAbiKind.Pointer &&
            parameter.Type.Kind == BindingTypeKind.Handle;
    }

    private static void ApplyCallbackDelegateAttribute(
        CXCursor functionCursor,
        CXCursor parameterCursor,
        ParameterBinding parameter,
        ref string? ignoreReason)
    {
        var match = FindSharpCppAttribute(GetSourceText(parameterCursor.TranslationUnit, parameterCursor.Extent), "delegate");
        if (match is null && TryGetParameterDeclarationSourceText(functionCursor, parameterCursor, out var parameterSourceText))
        {
            match = FindSharpCppAttribute(parameterSourceText, "delegate");
        }

        if (match is null)
        {
            return;
        }

        if (!parameter.Type.IsCallback)
        {
            ignoreReason ??= $"parameter '{parameter.ManagedName}' is marked [[sharpcpp::delegate]] but is not a callback parameter";
            return;
        }

        parameter.UseCallbackDelegate = true;
        if (!TryParseCallbackDelegateTypeName(match.Body, out var delegateTypeName, out var error))
        {
            ignoreReason ??= $"parameter '{parameter.ManagedName}' has invalid [[sharpcpp::delegate]] attribute: {error}";
            return;
        }

        parameter.CallbackDelegateTypeName = delegateTypeName;
    }

    private static bool TryParseCallbackDelegateTypeName(string body, out string? delegateTypeName, out string error)
    {
        var trimmed = body.Trim();
        if (trimmed.Length == 0)
        {
            delegateTypeName = null;
            error = string.Empty;
            return true;
        }

        if (trimmed.Length < 2 || trimmed[0] != '"' || trimmed[^1] != '"')
        {
            delegateTypeName = null;
            error = "optional delegate type name must be a quoted string";
            return false;
        }

        var unquoted = trimmed[1..^1].Trim();
        if (unquoted.Length == 0)
        {
            delegateTypeName = null;
            error = "delegate type name cannot be empty";
            return false;
        }

        if (IsExternalManagedTypeName(unquoted))
        {
            if (!IsValidExternalManagedTypeName(unquoted))
            {
                delegateTypeName = null;
                error = $"external delegate type name '{unquoted}' is not a valid managed type name";
                return false;
            }
        }
        else if (!IsValidCSharpIdentifier(unquoted))
        {
            delegateTypeName = null;
            error = $"generated delegate type name '{unquoted}' is not a valid C# identifier";
            return false;
        }

        delegateTypeName = unquoted;
        error = string.Empty;
        return true;
    }

    private static bool IsExternalManagedTypeName(string value)
    {
        return value.StartsWith("global::", StringComparison.Ordinal) ||
            value.Contains('.', StringComparison.Ordinal);
    }

    private static bool IsValidExternalManagedTypeName(string value)
    {
        var normalized = value.StartsWith("global::", StringComparison.Ordinal)
            ? value[8..]
            : value;
        var segments = normalized.Split('.', StringSplitOptions.TrimEntries);
        return segments.Length > 0 && segments.All(IsValidCSharpIdentifier);
    }

    private static bool IsValidCSharpIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || CSharpKeywords.Contains(value))
        {
            return false;
        }

        if (value[0] != '_' && !char.IsLetter(value[0]))
        {
            return false;
        }

        return value.Skip(1).All(static ch => ch == '_' || char.IsLetterOrDigit(ch));
    }

    private static bool TryGetParameterDeclarationSourceText(CXCursor functionCursor, CXCursor parameterCursor, out string sourceText)
    {
        var functionSourceText = GetSourceText(functionCursor.TranslationUnit, functionCursor.Extent);
        var openParenthesisIndex = functionSourceText.IndexOf('(');
        if (openParenthesisIndex < 0)
        {
            sourceText = string.Empty;
            return false;
        }

        var nestingDepth = 0;
        var closeParenthesisIndex = -1;
        for (var index = openParenthesisIndex; index < functionSourceText.Length; index++)
        {
            switch (functionSourceText[index])
            {
                case '(':
                    nestingDepth++;
                    break;
                case ')':
                    nestingDepth--;
                    if (nestingDepth == 0)
                    {
                        closeParenthesisIndex = index;
                    }
                    break;
            }

            if (closeParenthesisIndex >= 0)
            {
                break;
            }
        }

        if (closeParenthesisIndex < 0)
        {
            sourceText = string.Empty;
            return false;
        }

        var parameterListText = functionSourceText[(openParenthesisIndex + 1)..closeParenthesisIndex];
        if (functionCursor.NumArguments == 1)
        {
            sourceText = parameterListText;
            return true;
        }

        var parameterName = parameterCursor.Spelling.ToString();
        if (string.IsNullOrWhiteSpace(parameterName))
        {
            sourceText = string.Empty;
            return false;
        }

        foreach (var part in SplitTopLevelParameters(parameterListText))
        {
            if (part.Contains(parameterName, StringComparison.Ordinal))
            {
                sourceText = part;
                return true;
            }
        }

        sourceText = string.Empty;
        return false;
    }

    private static IReadOnlyList<string> SplitTopLevelParameters(string parameterListText)
    {
        var result = new List<string>();
        var segmentStart = 0;
        var angleDepth = 0;
        var parenDepth = 0;

        for (var index = 0; index < parameterListText.Length; index++)
        {
            switch (parameterListText[index])
            {
                case '<':
                    angleDepth++;
                    break;
                case '>':
                    angleDepth = Math.Max(0, angleDepth - 1);
                    break;
                case '(':
                    parenDepth++;
                    break;
                case ')':
                    parenDepth = Math.Max(0, parenDepth - 1);
                    break;
                case ',' when angleDepth == 0 && parenDepth == 0:
                    result.Add(parameterListText[segmentStart..index]);
                    segmentStart = index + 1;
                    break;
            }
        }

        result.Add(parameterListText[segmentStart..]);
        return result;
    }

    private static void CheckAndApplyIgnoreAttribute(Decl decl, BindingNode node)
    {
        var sourceText = GetSourceText(decl.Handle.TranslationUnit, decl.Extent);
        if (node is RecordBinding || node is EnumBinding)
        {
            var braceIndex = sourceText.IndexOf('{');
            if (braceIndex >= 0)
            {
                sourceText = sourceText[..braceIndex];
            }
        }

        var match = FindSharpCppAttribute(sourceText, "ignore");
        if (match is null)
        {
            decl.Location.GetFileLocation(out var file, out var line, out var column, out _);
            var filePath = Path.GetFullPath(file.Name.CString);

            if (!string.IsNullOrWhiteSpace(filePath) &&
                s_ignoreAnnotations.TryGetValue(filePath, out var annotations))
            {
                var oneBasedCursorLine = (int)line;
                var matchingAnnotation = annotations
                    .Where(annotation => annotation.Line == oneBasedCursorLine || annotation.Line == oneBasedCursorLine - 1)
                    .OrderBy(annotation => annotation.Line)
                    .LastOrDefault();

                if (matchingAnnotation is not null)
                {
                    var reason = matchingAnnotation.Reason ?? "marked [[sharpcpp::ignore]]";
                    node.SetSilentExclusionReason(reason, overwriteExisting: true);
                    return;
                }
            }

            return;
        }

        node.SetSilentExclusionReason(NormalizeIgnoreReason(match.Body) ?? "marked [[sharpcpp::ignore]]", overwriteExisting: true);
    }

    private static readonly Dictionary<string, List<IgnoreAnnotation>> s_ignoreAnnotations = new(StringComparer.Ordinal);

    private sealed record IgnoreAnnotation(int Line, int Column, string? Reason);

    private static void PreScanIgnoreAnnotations(IReadOnlyList<string> headerFiles)
    {
        foreach (var headerFile in headerFiles)
        {
            var path = Path.GetFullPath(headerFile);
            if (!File.Exists(path) || s_ignoreAnnotations.ContainsKey(path))
            {
                continue;
            }

            var content = File.ReadAllText(path);
            var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            var annotations = new List<IgnoreAnnotation>();

            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                var lineText = lines[lineIndex];
                foreach (var match in SharpCppAttributeScanner.Scan(lineText).Where(match => string.Equals(match.Name, "ignore", StringComparison.Ordinal)))
                {
                    var column = match.StartIndex + 1;
                    var reason = NormalizeIgnoreReason(match.Body);
                    annotations.Add(new IgnoreAnnotation(lineIndex + 1, column, reason));
                }
            }

            if (annotations.Count > 0)
            {
                s_ignoreAnnotations[path] = annotations;
            }
        }
    }

    private static readonly Dictionary<string, List<RenameAnnotation>> s_renameAnnotations = new(StringComparer.Ordinal);

    private sealed record RenameAnnotation(int Line, int Column, string RenameValue);

    private static void PreScanRenameAnnotations(IReadOnlyList<string> headerFiles)
    {
        foreach (var headerFile in headerFiles)
        {
            var path = Path.GetFullPath(headerFile);
            if (!File.Exists(path) || s_renameAnnotations.ContainsKey(path))
            {
                continue;
            }

            var content = File.ReadAllText(path);
            var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            var annotations = new List<RenameAnnotation>();

            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                var lineText = lines[lineIndex];
                foreach (var match in SharpCppAttributeScanner.Scan(lineText).Where(match => string.Equals(match.Name, "rename", StringComparison.Ordinal)))
                {
                    var column = match.StartIndex + 1;
                    annotations.Add(new RenameAnnotation(lineIndex + 1, column, match.Body.Trim().Trim('(', ')').Trim().Trim('"')));
                }
            }

            if (annotations.Count > 0)
            {
                s_renameAnnotations[path] = annotations;
            }
        }
    }

    private static void TryApplyRename(Decl decl, BindingNode node)
    {
        if (node is RecordBinding or EnumBinding)
        {
            var sourceText = GetSourceText(decl.Handle.TranslationUnit, decl.Extent);
            var braceIndex = FindStructBodyBraceIndex(sourceText);
            if (braceIndex >= 0)
            {
                sourceText = sourceText[..braceIndex];
            }

            ApplyRenameFromSource(node, sourceText);
            return;
        }

        if (node is FunctionBinding or FieldBinding)
        {
            var sourceText = GetSourceText(decl.Handle.TranslationUnit, decl.Extent);
            var braceIndex = FindStructBodyBraceIndex(sourceText);
            if (braceIndex >= 0)
            {
                sourceText = sourceText[..braceIndex];
            }
            else
            {
                var semiIndex = sourceText.IndexOf(';');
                if (semiIndex >= 0)
                {
                    sourceText = sourceText[..semiIndex];
                }
            }

            if (FindSharpCppAttribute(sourceText, "rename") is not null)
            {
                ApplyRenameFromSource(node, sourceText);
            }
            else
            {
                ApplyRenameFromPreScan(decl, node);
            }
        }
    }

    private static int FindStructBodyBraceIndex(string sourceText)
    {
        var inString = false;
        for (var i = 0; i < sourceText.Length; i++)
        {
            if (sourceText[i] == '"')
            {
                inString = !inString;
            }
            else if (sourceText[i] == '{' && !inString)
            {
                return i;
            }
        }

        return -1;
    }

    private static void ApplyRenameFromSource(BindingNode node, string sourceText)
    {
        var match = FindSharpCppAttribute(sourceText, "rename");
        if (match is null)
        {
            return;
        }

        ApplyRename(node, match.Body.Trim().Trim('(', ')').Trim().Trim('"'));
    }

    private static string? NormalizeIgnoreReason(string body)
    {
        var reason = body.Trim();
        if (reason.Length == 0)
        {
            return null;
        }

        if (reason.StartsWith("(", StringComparison.Ordinal) && reason.EndsWith(")", StringComparison.Ordinal))
        {
            reason = reason[1..^1].Trim();
        }

        if (reason.Length == 0)
        {
            return null;
        }

        return reason.Length >= 2 && reason.StartsWith('"') && reason.EndsWith('"')
            ? reason[1..^1]
            : reason;
    }

    private static void ApplyRenameFromPreScan(Decl decl, BindingNode node)
    {
        decl.Location.GetFileLocation(out var file, out var line, out var column, out _);
        var filePath = Path.GetFullPath(file.Name.CString);

        if (string.IsNullOrWhiteSpace(filePath) || !s_renameAnnotations.TryGetValue(filePath, out var annotations))
        {
            return;
        }

        var oneBasedCursorLine = (int)line;
        var matchingAnnotation = annotations
            .Where(annotation => annotation.Line == oneBasedCursorLine - 1)
            .OrderBy(annotation => annotation.Line)
            .LastOrDefault();

        if (matchingAnnotation is not null)
        {
            ApplyRename(node, matchingAnnotation.RenameValue);
        }
    }

    private static void ApplyRename(BindingNode node, string renameValue)
    {
        var pascalName = ToPascalCase(
            node is BindingNamedContainerNode containerNode
                ? containerNode.NativeName
                : ((BindingNamedNode)node).NativeName);

        renameValue = renameValue.Replace("{Name}", pascalName, StringComparison.Ordinal);

        switch (node)
        {
            case RecordBinding record:
                var head = GetManagedTypeHead(renameValue);
                record.ManagedName = head;
                record.ManagedTypeFormat = renameValue;
                record.SetMetadata(MetadataKeys.ExplicitManagedRename, true);
                break;
            case EnumBinding @enum:
                @enum.ManagedName = renameValue;
                break;
            case FunctionBinding function:
                function.ManagedName = GetManagedTypeHead(renameValue);
                function.SetMetadata(MetadataKeys.ExplicitManagedRename, true);
                break;
            case FieldBinding field:
                field.ManagedName = renameValue;
                break;
        }
    }

    private static string GetManagedTypeHead(string managedTypeFormat)
    {
        // Strip C++ namespace qualifiers (::) — only the local type name
        // belongs in the managed head.  Namespace placement is handled
        // separately via ManagedNamespace.
        var lastColon = managedTypeFormat.LastIndexOf("::", StringComparison.Ordinal);
        var localName = lastColon >= 0 ? managedTypeFormat[(lastColon + 2)..] : managedTypeFormat;

        var genericStart = localName.IndexOf('<');
        return genericStart >= 0 ? localName[..genericStart] : localName;
    }

    /// <summary>Documentation comment and source location of the declaration.</summary>
    private static void ApplyDeclarationFacts(Decl decl, BindingNode node)
    {
        decl.Location.GetFileLocation(out var file, out var line, out var column, out _);
        var filePath = file.Name.CString;
        if (!string.IsNullOrEmpty(filePath))
        {
            node.Source = new BindingSourceReference(Path.GetFullPath(filePath), (int)line, (int)column);
        }

        var briefComment = decl.Handle.BriefCommentText;
        try
        {
            var commentText = briefComment.CString;
            if (!string.IsNullOrWhiteSpace(commentText))
            {
                node.SetMetadata(MetadataKeys.DocumentationComment, commentText.Trim());
            }
        }
        finally
        {
            briefComment.Dispose();
        }
    }

    private static void RegisterEnum(EnumDecl enumDecl, BindingCompilation compilation, IDictionary<string, EnumBinding> enumsByName, string? headerIncludePath)
    {
        var nativeName = enumDecl.Name ?? enumDecl.Spelling.ToString();
        var cppQualifiedName = GetCppQualifiedName(enumDecl);
        if (string.IsNullOrWhiteSpace(nativeName) ||
            nativeName.Contains("(unnamed", StringComparison.Ordinal) ||
            nativeName.Contains("(anonymous", StringComparison.Ordinal) ||
            enumsByName.ContainsKey(cppQualifiedName))
        {
            return;
        }

        var @enum = new EnumBinding(
            $"enum:{cppQualifiedName}",
            nativeName,
            GetManagedEnumName(enumDecl, nativeName),
            NormalizeTypeName(enumDecl.IntegerType.CanonicalType.AsString),
            cppQualifiedName,
            // The underlying type may be spelled through a typedef (`enum E : uint64`).
            underlyingPrimitiveKind: GetPrimitiveKindFromTypeSpelling(enumDecl.IntegerType.CanonicalType.AsString))
        {
            IsFlags = HasSharpCppAttribute(GetSourceText(enumDecl.TranslationUnit.Handle, enumDecl.Extent), "flags")
        };
        @enum.SetMetadata(MetadataKeys.DeclarationDisplayText, GetDeclarationDisplayText(enumDecl));

        CheckAndApplyIgnoreAttribute(enumDecl, @enum);
        TryApplyRename(enumDecl, @enum);
        ApplyDeclarationFacts(enumDecl, @enum);

        foreach (var child in enumDecl.Decls)
        {
            if (child is EnumConstantDecl enumConstant)
            {
                @enum.Values.Add(
                    new EnumValueBinding(
                        enumConstant.Name ?? enumConstant.Spelling.ToString(),
                        ToPascalCase(enumConstant.Name ?? enumConstant.Spelling.ToString()),
                        FormatEnumValue(@enum.UnderlyingTypeName, enumConstant)));
            }
        }

        IndexEnumKeys(enumsByName, @enum);
        @enum.HeaderIncludePath = headerIncludePath;
        compilation.Members.Add(@enum);
    }

    private static string GetManagedEnumName(EnumDecl enumDecl, string nativeName)
    {
        if (nativeName == "Enum" &&
            enumDecl.SemanticParentCursor is RecordDecl parentRecord &&
            !string.IsNullOrWhiteSpace(parentRecord.Name))
        {
            return ToPascalCase(parentRecord.Name);
        }

        return ToPascalCase(nativeName);
    }

    private static void IndexRecordKeys(IDictionary<string, RecordBinding> recordsByName, RecordBinding record)
    {
        TryAddBindingKey(recordsByName, record.NativeName, record);
        SetQualifiedBindingKey(recordsByName, record.CppQualifiedName, record, static existing => existing.CppQualifiedName);
    }

    private static RecordBinding? FindExistingRecordForDeclaration(
        IDictionary<string, RecordBinding> recordsByName,
        string nativeName,
        string cppQualifiedName)
    {
        if (recordsByName.TryGetValue(cppQualifiedName, out var qualifiedRecord))
        {
            return qualifiedRecord;
        }

        if (!recordsByName.TryGetValue(nativeName, out var unqualifiedRecord))
        {
            return null;
        }

        return string.Equals(unqualifiedRecord.CppQualifiedName, cppQualifiedName, StringComparison.Ordinal) ||
            string.Equals(unqualifiedRecord.CppQualifiedName, nativeName, StringComparison.Ordinal)
            ? unqualifiedRecord
            : null;
    }

    private static string CreateRecordSymbolId(string cppQualifiedName)
        => $"record:{NormalizeNamedTypeName(cppQualifiedName)}";

    private static void RemoveRecordKeys(IDictionary<string, RecordBinding> recordsByName, RecordBinding record)
    {
        var keys = recordsByName
            .Where(entry => ReferenceEquals(entry.Value, record))
            .Select(static entry => entry.Key)
            .ToArray();

        foreach (var key in keys)
        {
            recordsByName.Remove(key);
        }
    }

    private static void IndexEnumKeys(IDictionary<string, EnumBinding> enumsByName, EnumBinding @enum)
    {
        TryAddBindingKey(enumsByName, @enum.NativeName, @enum);
        SetQualifiedBindingKey(enumsByName, @enum.CppQualifiedName, @enum, static existing => existing.CppQualifiedName);
    }

    // A qualified name identifies one declaration; it replaces a short-name entry of the
    // same spelling (a global `mode` versus the short key of `ns::mode`).
    private static void SetQualifiedBindingKey<T>(IDictionary<string, T> dictionary, string? key, T value, Func<T, string> getQualifiedName)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        if (!dictionary.TryGetValue(key, out var existing) ||
            !string.Equals(getQualifiedName(existing), key, StringComparison.Ordinal))
        {
            dictionary[key] = value;
        }
    }

    private static void TryAddBindingKey<T>(IDictionary<string, T> dictionary, string? key, T value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        if (dictionary.TryGetValue(key, out var existing) && ReferenceEquals(existing, value))
        {
            return;
        }

        if (!dictionary.ContainsKey(key))
        {
            dictionary.Add(key, value);
        }
    }

    private static string FormatEnumValue(string underlyingTypeName, EnumConstantDecl enumConstant)
    {
        if (underlyingTypeName.StartsWith("unsigned", StringComparison.Ordinal))
        {
            var value = enumConstant.UnsignedInitVal;
            return underlyingTypeName switch
            {
                "unsigned int" => $"{value}u",
                "unsigned long long" => $"{value}UL",
                "unsigned short" => value.ToString(),
                _ => value.ToString()
            };
        }

        return enumConstant.InitVal.ToString();
    }

    private static string GetSourceText(CXTranslationUnit translationUnit, CXSourceRange sourceRange)
    {
        var tokens = translationUnit.Tokenize(sourceRange);

        try
        {
            var builder = new StringBuilder();

            foreach (var token in tokens)
            {
                builder.Append(token.GetSpelling(translationUnit).ToString());
            }

            return builder.ToString();
        }
        finally
        {
            translationUnit.DisposeTokens(tokens);
        }
    }

    private static string GetSourceTextWithTokenSpacing(CXTranslationUnit translationUnit, CXSourceRange sourceRange)
    {
        var tokens = translationUnit.Tokenize(sourceRange);

        try
        {
            var builder = new StringBuilder();
            foreach (var token in tokens)
            {
                if (builder.Length != 0)
                    builder.Append(' ');
                builder.Append(token.GetSpelling(translationUnit).ToString());
            }

            return builder.ToString().Trim();
        }
        finally
        {
            translationUnit.DisposeTokens(tokens);
        }
    }

    private static string GetDeclarationDisplayText(Decl decl)
    {
        if (!TryGetPrettyPrintedDeclarationText(decl, out var declarationText))
        {
            return string.Empty;
        }

        return NormalizeDeclarationDisplayText(declarationText);
    }

    private static unsafe bool TryGetPrettyPrintedDeclarationText(Decl decl, out string declarationText)
    {
        declarationText = string.Empty;

        try
        {
            var cursor = decl.Handle;
            var printingPolicy = clang.getCursorPrintingPolicy(cursor);
            try
            {
                declarationText = clang.getCursorPrettyPrinted(cursor, printingPolicy).ToString();
            }
            finally
            {
                clang.PrintingPolicy_dispose(printingPolicy);
            }

            return !string.IsNullOrWhiteSpace(declarationText);
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeDeclarationDisplayText(string declarationText)
        => string.Join(" ", declarationText
            .Replace("\r", string.Empty)
            .Replace("\n", " ")
            .Split(['\t', ' '], StringSplitOptions.RemoveEmptyEntries));

    private static void ApplyManagedDefaultValue(
        ParameterBinding parameter,
        ParmVarDecl parameterDecl,
        BindingType parameterType,
        IReadOnlyDictionary<string, ManagedTypeOverride>? managedTypeOverrides = null)
    {
        var nativeDefaultValue = GetNativeDefaultValueExpression(parameterDecl);
        if (!string.IsNullOrWhiteSpace(nativeDefaultValue))
            parameter.SetMetadata(MetadataKeys.NativeDefaultValueExpression, nativeDefaultValue);

        var defaultValue = GetManagedDefaultValueExpression(parameterDecl, parameterType);
        parameter.ManagedDefaultValueExpression = defaultValue;

        // The mapped type keeps the source spelling, which may be unqualified inside the
        // declaring namespace; the configuration is keyed by the qualified type.
        // A `const T&` parameter takes T's configuration; the override is keyed by the
        // qualified value type.
        var valueType = parameterDecl.Type is ReferenceType referenceType ? referenceType.PointeeType : parameterDecl.Type;
        var isUndefaultableDefault = parameterDecl.HasDefaultArg &&
            managedTypeOverrides is not null &&
            new[]
            {
                parameterType.CppTypeName,
                NormalizeTypeName(parameterDecl.Type.CanonicalType.AsString),
                StripConstQualifier(NormalizeTypeName(valueType.AsString)),
                StripConstQualifier(FormatCanonicalCxTypeName(valueType.CanonicalType.Handle))
            }.Any(key => managedTypeOverrides.TryGetValue(key, out var overrideInfo) && overrideInfo.Undefaultable);

        if (defaultValue is null && parameterDecl.HasDefaultArg && !isUndefaultableDefault)
        {
            parameter.SetMetadata(MetadataKeys.UnexpressibleDefault, true);
        }

        // Only mark as undefaultable if the parameter actually has a C++ default value.
        // If there's no default at all, the overload split is unnecessary — the parameter
        // is already required.
        if (isUndefaultableDefault)
        {
            parameter.SetMetadata(MetadataKeys.UndefaultableParam, true);
        }
    }

    private static string StripConstQualifier(string typeName) =>
        typeName.StartsWith("const ", StringComparison.Ordinal) ? typeName["const ".Length..] : typeName;

    private static string? GetManagedDefaultValueExpression(ParmVarDecl parameterDecl, BindingType parameterType)
    {
        if (!TryGetDefaultArgumentExpression(parameterDecl, out var expressionCursor, out var expression))
        {
            return null;
        }

        if (expression.Contains('#') || expression.Contains("///") || expression.Length > 100)
        {
            return null;
        }

        if (expression.Contains('{') || expression.Contains('}') || expression.Contains('(') || expression.Contains(')'))
        {
            return null;
        }

        if (IsNullDefaultExpression(expressionCursor.Kind, expression))
        {
            return parameterType.Kind == BindingTypeKind.Handle || parameterType.IsCallback ? "null" : "default";
        }

        if (expression.Length == 0)
        {
            return null;
        }

        if (parameterType.Kind == BindingTypeKind.Handle)
        {
            return null;
        }

        if (parameterType.Kind == BindingTypeKind.Enum)
        {
            return NormalizeManagedEnumDefaultExpression(parameterType.ManagedTypeName, expression);
        }

        if (TryNormalizeManagedFloatingPointDefaultValueExpression(
                expression,
                parameterType,
                out var normalizedExpression))
        {
            return normalizedExpression;
        }

        if (expression.Contains("::", StringComparison.Ordinal))
        {
            return null;
        }

        if (Regex.IsMatch(expression, "^[A-Z][A-Z0-9_]*$", RegexOptions.CultureInvariant))
        {
            return null;
        }

        if (Regex.IsMatch(expression, "^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant) &&
            !string.Equals(expression, "true", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(expression, "false", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(expression, "null", StringComparison.Ordinal) &&
            !string.Equals(expression, "default", StringComparison.Ordinal))
        {
            return null;
        }

        return expressionCursor.Kind switch
        {
            CXCursorKind.CXCursor_CXXBoolLiteralExpr => expression.ToLowerInvariant(),
            _ => expression
        };
    }

    private static bool TryNormalizeManagedFloatingPointDefaultValueExpression(
        string sourceExpression,
        BindingType parameterType,
        out string expression)
    {
        expression = string.Empty;
        if (parameterType.PrimitiveKind is not (BindingPrimitiveKind.Single or BindingPrimitiveKind.Double))
            return false;

        var literal = string.Concat(sourceExpression.Where(static character => !char.IsWhiteSpace(character)));
        if (literal.EndsWith('f') || literal.EndsWith('F') || literal.EndsWith('l') || literal.EndsWith('L'))
            literal = literal[..^1];

        if (!double.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
            !double.IsFinite(value))
        {
            return false;
        }

        if (parameterType.PrimitiveKind == BindingPrimitiveKind.Single)
        {
            var singleValue = (float)value;
            if (!float.IsFinite(singleValue))
                return false;

            expression = $"{singleValue.ToString("R", CultureInfo.InvariantCulture)}f";
            return true;
        }

        expression = value.ToString("R", CultureInfo.InvariantCulture);
        return true;
    }

    private static string? GetNativeDefaultValueExpression(ParmVarDecl parameterDecl)
    {
        if (!TryGetDefaultArgumentExpression(parameterDecl, out _, out var expression))
            return null;

        if (expression.Contains('#') || expression.Contains("///"))
            return null;

        return expression.Length == 0 ? null : expression;
    }

    private static bool TryGetDefaultArgumentExpression(ParmVarDecl parameterDecl, out CXCursor expressionCursor, out string expression)
    {
        expressionCursor = default;
        expression = string.Empty;
        var parameterCursor = parameterDecl.Handle;

        var defaultArgumentCursor = parameterCursor.DefaultArg;
        if (defaultArgumentCursor.Kind == CXCursorKind.CXCursor_FirstInvalid && parameterDecl.HasUninstantiatedDefaultArg)
        {
            defaultArgumentCursor = parameterDecl.UninstantiatedDefaultArg.Handle;
        }
        else if (defaultArgumentCursor.Kind == CXCursorKind.CXCursor_FirstInvalid && !parameterDecl.HasDefaultArg)
        {
            return TryGetDefaultArgumentExpressionFromDeclaration(parameterDecl, out expression);
        }

        if (defaultArgumentCursor.Kind == CXCursorKind.CXCursor_FirstInvalid)
        {
            return TryGetDefaultArgumentExpressionFromDeclaration(parameterDecl, out expression);
        }

        var asWrittenCursor = defaultArgumentCursor.SubExprAsWritten;
        expressionCursor = asWrittenCursor.Kind == CXCursorKind.CXCursor_FirstInvalid
            ? defaultArgumentCursor
            : asWrittenCursor;
        expression = GetSourceTextWithTokenSpacing(expressionCursor.TranslationUnit, expressionCursor.Extent);
        expression = expression.StartsWith("=", StringComparison.Ordinal)
            ? expression[1..].TrimStart()
            : expression;
        if (expression.Length != 0)
        {
            if (expression.Contains('#') ||
                expression.Contains("///", StringComparison.Ordinal))
            {
                if (TryGetDefaultArgumentExpressionFromDeclaration(parameterDecl, out var declarationExpression))
                {
                    expression = declarationExpression;
                }
                else
                {
                    return false;
                }
            }
            else if (expression.Contains(';') &&
                TryGetDefaultArgumentExpressionFromDeclaration(parameterDecl, out var declarationExpression) &&
                !declarationExpression.Contains(';'))
            {
                expression = declarationExpression;
            }
            return true;
        }

        var fallback = TryGetDefaultArgumentExpressionFromDeclaration(parameterDecl, out expression);
        return fallback;
    }

    private static bool TryGetDefaultArgumentExpressionFromDeclaration(ParmVarDecl parameterDecl, out string expression)
    {
        expression = string.Empty;
        var declarationText = Regex.Replace(GetSourceText(parameterDecl.Handle.TranslationUnit, parameterDecl.Extent).Trim(), "\\s+", " ");
        var separatorIndex = declarationText.IndexOf('=', StringComparison.Ordinal);
        if (separatorIndex < 0 || separatorIndex == declarationText.Length - 1)
            return false;

        expression = declarationText[(separatorIndex + 1)..].Trim();
        return expression.Length != 0;
    }

    private static bool IsNullDefaultExpression(CXCursorKind expressionKind, string expression)
    {
        return expressionKind == CXCursorKind.CXCursor_CXXNullPtrLiteralExpr ||
            expressionKind == CXCursorKind.CXCursor_GNUNullExpr ||
            string.Equals(expression, "nullptr", StringComparison.Ordinal) ||
            string.Equals(expression, "NULL", StringComparison.Ordinal) ||
            string.Equals(expression, "__null", StringComparison.Ordinal);
    }

    private static string NormalizeManagedEnumDefaultExpression(string managedTypeName, string expression)
    {
        var normalizedExpression = Regex.Replace(expression, @"\s*::\s*", "::", RegexOptions.CultureInvariant);
        var separatorIndex = normalizedExpression.LastIndexOf("::", StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            return Regex.IsMatch(normalizedExpression, "^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)
                ? $"{managedTypeName}.{ToPascalCase(normalizedExpression)}"
                : normalizedExpression;
        }

        var valueName = normalizedExpression[(separatorIndex + 2)..];
        return $"{managedTypeName}.{ToPascalCase(valueName)}";
    }

    // LoadManagedTypeOverrides was removed. Replaced by
    // MarshallerConfigExtractor.ExtractManagedTypeOverrides() which uses ClangSharp
    // AST walking instead of whole-file regex for finding template specializations.

    private static void VisitNamespace(
        Decl nsDecl,
        BindingCompilation compilation,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        string entryPointPrefix,
        IReadOnlyList<string> ownedFileRoots,
        IReadOnlyDictionary<string, string> ownedFileRootIncludePrefixes,
        string? headerIncludePath,
        IReadOnlySet<string>? directorRecordCppTypeNames)
    {
        foreach (var child in nsDecl.Decls)
        {
            if (!IsOwnedDecl(child, ownedFileRoots))
            {
                continue;
            }

            switch (child)
            {
                case NamespaceDecl nsChild:
                    VisitNamespace(nsChild, compilation, recordsByName, enumsByName, managedTypeOverrides, entryPointPrefix, ownedFileRoots, ownedFileRootIncludePrefixes, headerIncludePath, directorRecordCppTypeNames);
                    break;

                case ClassTemplateSpecializationDecl:
                    break;
                case RecordDecl recordDecl:
                    RegisterRecord(recordDecl, compilation, recordsByName, enumsByName, managedTypeOverrides, entryPointPrefix,
                        ResolveDeclIncludePath(recordDecl, ownedFileRoots, ownedFileRootIncludePrefixes, headerIncludePath ?? string.Empty), directorRecordCppTypeNames);
                    break;

                case ClassTemplateDecl classTemplateDecl:
                    RegisterTemplateRecord(classTemplateDecl, compilation, recordsByName, enumsByName, managedTypeOverrides, entryPointPrefix,
                        ResolveDeclIncludePath(classTemplateDecl, ownedFileRoots, ownedFileRootIncludePrefixes, headerIncludePath ?? string.Empty), directorRecordCppTypeNames);
                    break;

                case EnumDecl enumChildDecl:
                    if (!string.IsNullOrEmpty(enumChildDecl.Name) &&
                        !GetCppQualifiedName(enumChildDecl).Contains("(unnamed enum", StringComparison.Ordinal))
                        RegisterEnum(enumChildDecl, compilation, enumsByName,
                            ResolveDeclIncludePath(child, ownedFileRoots, ownedFileRootIncludePrefixes, headerIncludePath ?? string.Empty));
                    break;

                case FunctionTemplateDecl funcTemplateDecl:
                    {
                        // Skip out-of-line template member definitions — the class-body
                        // declaration is authoritative for access and static/virtual status.
                        if (funcTemplateDecl.TemplatedDecl is CXXMethodDecl)
                            break;
                        var function = CreateTemplateFunction(funcTemplateDecl, entryPointPrefix, recordsByName, enumsByName, managedTypeOverrides);
                        if (function is not null)
                        {
                            CheckAndApplyIgnoreAttribute(funcTemplateDecl, function);
                            TryApplyRename(funcTemplateDecl, function);
                            ApplyDeclarationFacts(funcTemplateDecl, function);
                            ApplyCallableDefinitionMetadata(funcTemplateDecl, function);
                            ApplyDeletedCallableIgnore(funcTemplateDecl.TemplatedDecl, function);
                            function.HeaderIncludePath = ResolveDeclIncludePath(child, ownedFileRoots, ownedFileRootIncludePrefixes, headerIncludePath ?? string.Empty);
                            compilation.Members.Add(function);
                        }
                        break;
                    }

                case FunctionDecl functionDecl:
                    {
                        // Out-of-line member definitions (CXXMethodDecl) at namespace
                        // scope belong to a record — the class-body declaration (inside
                        // RecordDecl.Decls) is the authoritative source for access and
                        // virtual/static status. Skip to avoid generating invalid thunks
                        // for non-public members.
                        if (functionDecl is CXXMethodDecl)
                            break;
                        var function = CreateFunction(functionDecl, entryPointPrefix, recordsByName, enumsByName, managedTypeOverrides, EmptyGenericParameters);
                        CheckAndApplyIgnoreAttribute(functionDecl, function);
                        TryApplyRename(functionDecl, function);
                        ApplyDeclarationFacts(functionDecl, function);
                        ApplyCallableDefinitionMetadata(functionDecl, function);
                        ApplyDeletedCallableIgnore(functionDecl, function);
                        function.HeaderIncludePath = ResolveDeclIncludePath(child, ownedFileRoots, ownedFileRootIncludePrefixes, headerIncludePath ?? string.Empty);
                        compilation.Members.Add(function);
                        break;
                    }
            }
        }
    }

    // ManagedTypeOverride is now in MarshallerConfigExtractor (see using alias above).

    private static bool TryMapGenericParameterType(
        ClangSharp.Type type,
        IReadOnlyDictionary<string, BindingType> genericParameters,
        out BindingType bindingType)
    {
        if (type.Handle.Declaration.Kind == CXCursorKind.CXCursor_TemplateTypeParameter &&
            genericParameters is AstGenericParameterScope parameterScope &&
            parameterScope.TryGet(type.Handle, out bindingType!))
        {
            return true;
        }

        switch (type)
        {
            case TemplateTypeParmType templateParameterType:
                if (genericParameters is AstGenericParameterScope astScope &&
                    astScope.TryGet(templateParameterType, out bindingType!))
                {
                    return true;
                }

                var parameterName = templateParameterType.Decl.Name ?? templateParameterType.Decl.Spelling;
                if (!string.IsNullOrWhiteSpace(parameterName) &&
                    genericParameters.TryGetValue(parameterName, out bindingType!))
                {
                    return true;
                }
                break;
            case SubstTemplateTypeParmType substitutionType:
                return TryMapGenericParameterType(substitutionType.ReplacementType, genericParameters, out bindingType);
            case ElaboratedType elaboratedType:
                return TryMapGenericParameterType(elaboratedType.NamedType, genericParameters, out bindingType);
            case TypedefType typedefType:
                return TryMapGenericParameterType(typedefType.Desugar, genericParameters, out bindingType);
        }

        bindingType = null!;
        return false;
    }

    private static bool TryMapGenericParameterType(string normalizedTypeName, IReadOnlyDictionary<string, BindingType> genericParameters, out BindingType type)
    {
        if (genericParameters.TryGetValue(normalizedTypeName, out type!))
        {
            return true;
        }

        var unqualifiedTypeName = normalizedTypeName;
        if (unqualifiedTypeName.StartsWith("const ", StringComparison.Ordinal))
        {
            unqualifiedTypeName = unqualifiedTypeName["const ".Length..];
        }
        unqualifiedTypeName = unqualifiedTypeName.TrimEnd('&').TrimEnd();
        if (unqualifiedTypeName.EndsWith(" const", StringComparison.Ordinal))
        {
            unqualifiedTypeName = unqualifiedTypeName[..^" const".Length].TrimEnd();
        }
        if (genericParameters.TryGetValue(unqualifiedTypeName, out type!))
        {
            return true;
        }

        type = null!;
        return false;
    }

    private static IReadOnlyDictionary<string, BindingType> CreateGenericParameterScope(
        IReadOnlyList<NamedDecl> templateParameters,
        IReadOnlyDictionary<string, BindingType>? parent = null)
    {
        if (templateParameters.Count == 0)
        {
            return parent ?? EmptyGenericParameters;
        }

        var scope = new AstGenericParameterScope(parent);
        foreach (var parameter in templateParameters.OfType<TemplateTypeParmDecl>())
        {
            scope.Add(parameter);
        }

        return scope;
    }

    private static bool TryExtractTemplateParameters(TemplateDecl templateDecl, out IReadOnlyList<BindingGenericParameter> templateParameters)
    {
        var parameters = new List<BindingGenericParameter>();

        foreach (var param in templateDecl.TemplateParameters)
        {
            if (param is TemplateTypeParmDecl typeParam)
            {
                var parameterName = typeParam.Name ?? typeParam.Spelling;
                if (!string.IsNullOrWhiteSpace(parameterName))
                {
                    parameters.Add(new BindingGenericParameter(parameterName, parameters.Count));
                }
            }
        }

        templateParameters = parameters;
        return parameters.Count > 0;
    }

    private static BindingType ParseTypeToken(
        string token,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        IReadOnlyDictionary<string, BindingType> genericParameters,
        bool allowCallbackPointerLikeTokens = false)
    {
        var spelledTypeName = NormalizeTypeName(token);
        var resolvedTypeName = SelectConcreteTemplateTypeName(spelledTypeName, ResolveAliasTypeName(token));
        var qualifiedSpelledTypeName = GetQualifiedTypeTokenName(spelledTypeName, resolvedTypeName, recordsByName);
        var normalized = resolvedTypeName;

        if (TryMapGenericParameterType(normalized, genericParameters, out var genericParameterType))
        {
            return genericParameterType;
        }

        if (allowCallbackPointerLikeTokens &&
            TryCreateCallbackPointerLikeType(normalized, recordsByName, enumsByName, managedTypeOverrides, genericParameters, out var callbackPointerLikeType))
        {
            return callbackPointerLikeType;
        }

        if (normalized.EndsWith("*", StringComparison.Ordinal))
        {
            var referredTypeName = NormalizeTypeName(normalized[..^1]);
            if (TryCreateSpecializedRecordHandleType(normalized, referredTypeName, recordsByName, enumsByName, managedTypeOverrides, genericParameters, out var specializedHandleType))
            {
                return specializedHandleType;
            }

            var pointeeName = NormalizeNamedTypeName(referredTypeName);
            if (recordsByName.TryGetValue(pointeeName, out var record))
            {
                return record.HandleType;
            }

            if (Regex.IsMatch(pointeeName, @"^(?:[A-Za-z_]\w*::)*[A-Za-z_]\w*$", RegexOptions.CultureInvariant))
            {
                var nativeName = GetManagedTypeHead(pointeeName);
                return BindingType.CreateHandle(
                    normalized,
                    ToPascalCase(nativeName),
                    CreateRecordSymbolId(pointeeName));
            }
        }

        if (TryGetManagedTypeOverride(spelledTypeName, resolvedTypeName, qualifiedSpelledTypeName, managedTypeOverrides, isReturn: false, out var managedTypeOverride))
        {
            var surfaceCppTypeName = qualifiedSpelledTypeName.Contains("::", StringComparison.Ordinal)
                ? qualifiedSpelledTypeName
                : normalized;
            surfaceCppTypeName = SelectConfiguredTypeInstantiationName(managedTypeOverride!, surfaceCppTypeName, resolvedTypeName);
            return CreateConfiguredOrWrapperType(managedTypeOverride!, recordsByName, enumsByName, managedTypeOverrides, genericParameters, surfaceCppTypeName);
        }

        if (long.TryParse(
                normalized,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out _))
        {
            return BindingType.CreateConfigured(normalized, normalized);
        }

        return normalized switch
        {
            "bool" => BindingType.Boolean,
            "signed char" => BindingType.Int8,
            "unsigned char" => BindingType.UInt8,
            "int8_t" => BindingType.Int8,
            "uint8_t" => BindingType.UInt8,
            "short" => BindingType.Int16,
            "unsigned short" => BindingType.UInt16,
            "int16_t" => BindingType.Int16,
            "uint16_t" => BindingType.UInt16,
            "int" => BindingType.Int32,
            "unsigned int" => BindingType.UInt32,
            "int32_t" => BindingType.Int32,
            "uint32_t" => BindingType.UInt32,
            "long long" => BindingType.Int64,
            "unsigned long long" => BindingType.UInt64,
            "int64_t" => BindingType.Int64,
            "uint64_t" => BindingType.UInt64,
            "float" => BindingType.Single,
            "double" => BindingType.Double,
            "long" => BindingType.NativeInt,
            "unsigned long" => BindingType.NativeUInt,
            "char" => BindingType.Int8,
            _ when enumsByName.TryGetValue(NormalizeNamedTypeName(StripGenericArguments(normalized)), out var @enum) => BindingType.CreateEnum(@enum.CppQualifiedName, @enum.ManagedName, enumId: @enum.SymbolId),
            _ when normalized.Contains("::", StringComparison.Ordinal) && enumsByName.TryGetValue(NormalizeNamedTypeName(StripGenericArguments(normalized[(normalized.LastIndexOf("::", StringComparison.Ordinal) + 2)..])), out var shortEnum) => BindingType.CreateEnum(shortEnum.CppQualifiedName, shortEnum.ManagedName, enumId: shortEnum.SymbolId),
            _ when recordsByName.TryGetValue(NormalizeNamedTypeName(StripGenericArguments(normalized)), out var record) =>
                TryCreateSpecializedRecordHandleType($"{normalized}*", normalized, recordsByName, enumsByName, managedTypeOverrides, genericParameters, out var concreteHandleType)
                    ? AsRecordValueProjection(concreteHandleType)
                    : AsRecordValueProjection(record.HandleType),
            _ when normalized.Contains("::", StringComparison.Ordinal) && recordsByName.TryGetValue(NormalizeNamedTypeName(StripGenericArguments(normalized[(normalized.LastIndexOf("::", StringComparison.Ordinal) + 2)..])), out var shortRecord) =>
                TryCreateSpecializedRecordHandleType($"{normalized}*", normalized, recordsByName, enumsByName, managedTypeOverrides, genericParameters, out var concreteHandleType)
                    ? AsRecordValueProjection(concreteHandleType)
                    : AsRecordValueProjection(shortRecord.HandleType),
            _ => throw new NotSupportedException($"ClangSharp sample parser does not support generic argument token '{normalized}'.")
        };
    }

    private static BindingType AsRecordValueProjection(BindingType type) =>
        type.WithCppIsRecordValue(true).WithCppPointerIsRecordValueProjection(true);

    private static bool TryCreateCallbackPointerLikeType(
        string normalized,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        IReadOnlyDictionary<string, BindingType> genericParameters,
        out BindingType type)
    {
        type = default!;
        if (!normalized.EndsWith("*", StringComparison.Ordinal) && !normalized.EndsWith("&", StringComparison.Ordinal))
        {
            return false;
        }

        var targetTypeName = StripTopLevelCvQualifier(NormalizeTypeName(normalized[..^1]));
        if (IsPrimitiveTypeName(targetTypeName) || string.Equals(targetTypeName, "void", StringComparison.Ordinal))
        {
            type = BindingType.CreateConfigured(normalized, "global::System.IntPtr", "global::System.IntPtr");
            return true;
        }

        var resolvedTargetTypeName = SelectConcreteTemplateTypeName(targetTypeName, ResolveAliasTypeName(targetTypeName));
        var qualifiedTargetTypeName = GetQualifiedTypeTokenName(targetTypeName, resolvedTargetTypeName, recordsByName);
        if (TryGetManagedTypeOverride(targetTypeName, resolvedTargetTypeName, qualifiedTargetTypeName, managedTypeOverrides, isReturn: false, out var managedTypeOverride))
        {
            var effectiveTargetTypeName = SelectConfiguredTypeInstantiationName(managedTypeOverride!, qualifiedTargetTypeName, resolvedTargetTypeName);
            type = CreateConfiguredOrWrapperType(managedTypeOverride!, recordsByName, enumsByName, managedTypeOverrides, genericParameters, effectiveTargetTypeName);
            return true;
        }

        if (TryCreateSpecializedRecordHandleType(normalized, resolvedTargetTypeName, recordsByName, enumsByName, managedTypeOverrides, genericParameters, out var specializedHandleType))
        {
            type = specializedHandleType;
            return true;
        }

        var targetRecordName = NormalizeNamedTypeName(StripGenericArguments(resolvedTargetTypeName));
        if (recordsByName.TryGetValue(targetRecordName, out var record))
        {
            type = record.HandleType;
            return true;
        }

        return false;
    }

    private static string StripTopLevelCvQualifier(string typeName)
    {
        var result = typeName;
        while (result.StartsWith("const ", StringComparison.Ordinal) || result.StartsWith("volatile ", StringComparison.Ordinal))
        {
            result = result.StartsWith("const ", StringComparison.Ordinal)
                ? result[6..]
                : result[9..];
        }

        return NormalizeTypeName(result);
    }

    private static string SelectConfiguredTypeInstantiationName(ManagedTypeOverride managedTypeOverride, string preferredTypeName, string resolvedTypeName)
    {
        var preferred = NormalizeTypeName(preferredTypeName);
        var resolved = NormalizeTypeName(resolvedTypeName);
        if ((managedTypeOverride.TemplateParameterNames.Count > 0 || managedTypeOverride.PublicTypeName.Contains('{', StringComparison.Ordinal)) &&
            !TryGetTopLevelTemplateArguments(preferred, out _, out _) &&
            TryGetTopLevelTemplateArguments(resolved, out _, out _))
        {
            return resolved;
        }

        return preferred;
    }

    private static bool IsPrimitiveTypeName(string typeName)
    {
        return typeName switch
        {
            "bool" or
            "signed char" or
            "unsigned char" or
            "int8_t" or
            "uint8_t" or
            "short" or
            "unsigned short" or
            "int16_t" or
            "uint16_t" or
            "int" or
            "unsigned int" or
            "int32_t" or
            "uint32_t" or
            "long long" or
            "unsigned long long" or
            "int64_t" or
            "uint64_t" or
            "float" or
            "double" or
            "long" or
            "unsigned long" or
            "char" => true,
            _ => false
        };
    }

    private static string GetQualifiedTypeTokenName(string spelledTypeName, string resolvedTypeName, IDictionary<string, RecordBinding> recordsByName)
    {
        if (string.IsNullOrWhiteSpace(spelledTypeName))
        {
            return resolvedTypeName;
        }

        if (TryGetQualifiedAliasOriginName(resolvedTypeName, out var qualifiedAliasName))
        {
            return qualifiedAliasName;
        }

        if (spelledTypeName.Contains("::", StringComparison.Ordinal))
        {
            return spelledTypeName;
        }

        var head = NormalizeNamedTypeName(StripGenericArguments(spelledTypeName));
        return recordsByName.TryGetValue(head, out var record)
            ? record.CppQualifiedName
            : spelledTypeName;
    }

    private static bool TryGetQualifiedAliasOriginName(string resolvedTypeName, out string qualifiedAliasName)
    {
        var normalized = NormalizeTypeName(resolvedTypeName);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            qualifiedAliasName = string.Empty;
            return false;
        }

        if (s_typeAliasOriginNames.TryGetValue(normalized, out var originNames) && originNames.Count > 0)
        {
            qualifiedAliasName = PickBestOriginName(originNames);
            return true;
        }

        if (TryGetTopLevelTemplateArguments(normalized, out var head, out _) &&
            s_typeAliasOriginNames.TryGetValue(head, out originNames) && originNames.Count > 0)
        {
            qualifiedAliasName = PickBestOriginName(originNames);
            return true;
        }

        foreach (var kv in s_typeAliasOriginNames)
        {
            if ((kv.Key.EndsWith(normalized, StringComparison.Ordinal) ||
                 normalized.EndsWith(kv.Key, StringComparison.Ordinal)) &&
                kv.Value.Count > 0)
            {
                qualifiedAliasName = PickBestOriginName(kv.Value);
                return true;
            }
        }

        qualifiedAliasName = string.Empty;
        return false;
    }

    private static bool TryCreateSpecializedRecordHandleType(
        string handleCppTypeName,
        string referredTypeName,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        IReadOnlyDictionary<string, BindingType> genericParameters,
        out BindingType type,
        string? completeTypeName = null)
    {
        var genericStart = referredTypeName.IndexOf('<');
        var genericEnd = referredTypeName.LastIndexOf('>');
        if (genericStart < 0 || genericEnd <= genericStart)
        {
            type = null!;
            return false;
        }

        var head = NormalizeNamedTypeName(referredTypeName[..genericStart]);
        if (!recordsByName.TryGetValue(head, out var record))
        {
            type = null!;
            return false;
        }

        if (!record.IsGeneric && record.ManagedTypeFormat?.Contains('{', StringComparison.Ordinal) != true)
        {
            type = record.HandleType;
            return true;
        }

        var genericArguments = ParseVisibleRecordTemplateArguments(record, referredTypeName, recordsByName, enumsByName, managedTypeOverrides, genericParameters, completeTypeName);
        type = BindingType.CreateHandle(handleCppTypeName, record.ManagedName, record.SymbolId, record.HandleMarshallerTypeName, genericArguments);
        type = ApplyFixedArrayMetadata(
            type,
            IsFixedArrayRecordOrConfigured(record, referredTypeName, managedTypeOverrides),
            genericArguments);
        return true;
    }

    private static string SelectConcreteTemplateTypeName(string preferredTypeName, string fallbackTypeName)
    {
        var normalizedPreferred = NormalizeTypeName(preferredTypeName);
        var normalizedFallback = NormalizeTypeName(fallbackTypeName);

        if (string.IsNullOrWhiteSpace(normalizedPreferred))
        {
            return normalizedFallback;
        }

        if (string.IsNullOrWhiteSpace(normalizedFallback))
        {
            return normalizedPreferred;
        }

        var preferredHasTemplateArguments = normalizedPreferred.Contains('<', StringComparison.Ordinal);
        var fallbackHasTemplateArguments = normalizedFallback.Contains('<', StringComparison.Ordinal);

        if (!preferredHasTemplateArguments && fallbackHasTemplateArguments)
        {
            return normalizedFallback;
        }

        if (preferredHasTemplateArguments &&
            fallbackHasTemplateArguments &&
            GetTemplateSpecificityScore(normalizedFallback) > GetTemplateSpecificityScore(normalizedPreferred))
        {
            return normalizedFallback;
        }

        return normalizedPreferred;
    }

    private static string GetUnderlyingTypeName(CXType type)
    {
        var underlyingType = type.UnderlyingType;
        if (underlyingType.kind == CXTypeKind.CXType_Invalid)
        {
            return string.Empty;
        }

        return NormalizeTypeName(underlyingType.Spelling.ToString());
    }

    private static void CollectManagedTypeAliases(
        BindingCompilation compilation,
        Decl decl,
        IReadOnlyList<string> ownedFileRoots,
        IReadOnlyDictionary<string, string> ownedFileRootIncludePrefixes,
        string headerIncludePath,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        string entryPointPrefix)
    {
        foreach (var child in EnumerateChildDeclsSafely(decl))
        {
            if (TryGetAliasDecl(child, out var aliasDecl) &&
                IsOwnedDecl(aliasDecl, ownedFileRoots) &&
                IsNamespaceOrTranslationUnitAlias(aliasDecl))
            {
                if (TryRegisterMaterializedAlias(
                        aliasDecl,
                        compilation,
                        recordsByName,
                        enumsByName,
                        managedTypeOverrides,
                        entryPointPrefix,
                        ResolveDeclIncludePath(aliasDecl, ownedFileRoots, ownedFileRootIncludePrefixes, headerIncludePath)))
                {
                    continue;
                }

                if (TryCreateManagedTypeAlias(aliasDecl, recordsByName, enumsByName, managedTypeOverrides, out var aliasBinding))
                {
                    compilation.TypeAliases.Add(aliasBinding);
                }
            }

            CollectManagedTypeAliases(
                compilation,
                child,
                ownedFileRoots,
                ownedFileRootIncludePrefixes,
                headerIncludePath,
                recordsByName,
                enumsByName,
                managedTypeOverrides,
                entryPointPrefix);
        }
    }

    private static bool TryRegisterMaterializedAlias(
        TypedefNameDecl aliasDecl,
        BindingCompilation compilation,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        string entryPointPrefix,
        string? headerIncludePath)
    {
        var sourceText = GetSourceText(aliasDecl.Handle.TranslationUnit, aliasDecl.Extent);
        var materialize = HasSharpCppAttribute(sourceText, "materialize") ||
            HasSharpCppAttributeOnDeclarationLine(aliasDecl, "materialize");
        if (!materialize)
        {
            return false;
        }

        var specialization = aliasDecl.UnderlyingType.AsCXXRecordDecl as ClassTemplateSpecializationDecl;
        if (specialization is null || !specialization.Decls.Any())
        {
            return false;
        }

        var cppQualifiedName = GetCppQualifiedName(aliasDecl);
        var nativeName = aliasDecl.Name ?? aliasDecl.Spelling.ToString();
        if (string.IsNullOrWhiteSpace(cppQualifiedName) || string.IsNullOrWhiteSpace(nativeName) ||
            FindExistingRecordForDeclaration(recordsByName, nativeName, cppQualifiedName) is not null)
        {
            return false;
        }

        var record = new RecordBinding(
            CreateRecordSymbolId(cppQualifiedName),
            nativeName,
            ToPascalCase(GetManagedTypeHead(nativeName)),
            cppQualifiedName)
        {
            HasCopyConstructor = specialization.Handle.HasUserDeclaredCopyConstructor,
            HeaderIncludePath = headerIncludePath
        };
        record.SetMetadata(MetadataKeys.DeclarationDisplayText, sourceText);
        ApplyDeclarationFacts(aliasDecl, record);
        IndexRecordKeys(recordsByName, record);
        compilation.Members.Add(record);

        ApplyDestructorMetadata(specialization, record);
        PopulateRecordBases(
            specialization,
            record,
            recordsByName,
            enumsByName,
            managedTypeOverrides,
            EmptyGenericParameters);
        PopulateRecordMembers(
            specialization,
            record,
            recordsByName,
            enumsByName,
            managedTypeOverrides,
            EmptyGenericParameters,
            entryPointPrefix,
            compilation,
            headerIncludePath);
        record.IsAbstract = record.Members
            .OfType<FunctionBinding>()
            .Any(static method => !method.IsIgnored() && method.IsPureVirtual) ||
            record.HasUnimplementedAbstractBaseMethods(compilation);
        AddSyntheticDefaultConstructor(specialization, record, entryPointPrefix);
        UpdatePolymorphism(record, recordsByName);
        return true;
    }

    private static bool HasSharpCppAttributeOnDeclarationLine(Decl decl, string attributeName)
    {
        decl.Location.GetFileLocation(out var file, out var line, out _, out _);
        var filePath = file.Name.CString;
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath) || line == 0)
        {
            return false;
        }

        var lines = File.ReadAllLines(filePath);
        var index = checked((int)line - 1);
        if (index < 0 || index >= lines.Length)
        {
            return false;
        }

        var declarationLines = index > 0
            ? $"{lines[index - 1]}\n{lines[index]}"
            : lines[index];
        return HasSharpCppAttribute(declarationLines, attributeName);
    }

    private static void BuildOwnedManagedAliasIndex(Decl decl, IReadOnlyList<string> ownedFileRoots)
    {
        s_ownedManagedAliases.Clear();
        CollectOwnedManagedAliases(decl, ownedFileRoots);
    }

    private static void BuildTypeAliasIndex(Decl decl)
    {
        s_typeAliases.Clear();
        s_namespaceAliases.Clear();
        s_typeAliasOriginNames.Clear();
        CollectTypeAliases(decl);
    }

    private static void CollectOwnedManagedAliases(Decl decl, IReadOnlyList<string> ownedFileRoots)
    {
        foreach (var child in EnumerateChildDeclsSafely(decl))
        {
            if (TryGetAliasDecl(child, out var aliasDecl) &&
                IsOwnedDecl(aliasDecl, ownedFileRoots) &&
                IsNamespaceOrTranslationUnitAlias(aliasDecl))
            {
                var cppQualifiedName = NormalizeTypeName(GetCppQualifiedName(aliasDecl));
                var nativeName = aliasDecl.Name ?? aliasDecl.Spelling.ToString();
                if (!string.IsNullOrWhiteSpace(cppQualifiedName) && !string.IsNullOrWhiteSpace(nativeName))
                {
                    s_ownedManagedAliases[cppQualifiedName] = new OwnedAliasInfo(cppQualifiedName, nativeName);
                }
            }

            CollectOwnedManagedAliases(child, ownedFileRoots);
        }
    }

    private static bool TryGetAliasDecl(Decl decl, out TypedefNameDecl aliasDecl)
    {
        if (decl is TypedefNameDecl typedefNameDecl)
        {
            aliasDecl = typedefNameDecl;
            return true;
        }

        if (decl is TypeAliasDecl typeAliasDecl)
        {
            aliasDecl = typeAliasDecl;
            return true;
        }

        aliasDecl = null!;
        return false;
    }

    private static IEnumerable<Decl> EnumerateChildDeclsSafely(Decl decl)
    {
        int count;
        try
        {
            count = decl.Decls.Count;
        }
        catch (ArgumentOutOfRangeException)
        {
            yield break;
        }

        for (var index = 0; index < count; ++index)
        {
            Decl child;
            try
            {
                child = decl.Decls[index];
            }
            catch (ArgumentOutOfRangeException)
            {
                // Some SDK headers expose Clang cursors that ClangSharp cannot wrap.
                continue;
            }

            yield return child;
        }
    }

    private static bool IsNamespaceOrTranslationUnitAlias(TypedefNameDecl typedefNameDecl)
    {
        return typedefNameDecl.SemanticParentCursor is NamespaceDecl or TranslationUnitDecl;
    }

    private static void CollectTypeAliases(Decl decl)
    {
        foreach (var child in EnumerateChildDeclsSafely(decl))
        {
            if (TryGetAliasDecl(child, out var aliasDecl))
            {
                RegisterTypeAlias(aliasDecl);
            }

            if (child is NamespaceAliasDecl namespaceAliasDecl)
            {
                RegisterNamespaceAlias(namespaceAliasDecl);
            }

            CollectTypeAliases(child);
        }
    }

    private static void RegisterNamespaceAlias(NamespaceAliasDecl namespaceAliasDecl)
    {
        var aliasName = NormalizeTypeName(GetCppQualifiedName(namespaceAliasDecl));
        var targetName = NormalizeTypeName(GetCppQualifiedName(namespaceAliasDecl.AliasedNamespace));
        if (!string.IsNullOrWhiteSpace(aliasName) && !string.IsNullOrWhiteSpace(targetName))
        {
            s_namespaceAliases[aliasName] = targetName;
        }
    }

    private static void RegisterTypeAlias(TypedefNameDecl typedefNameDecl)
    {
        var aliasName = GetCppQualifiedName(typedefNameDecl);
        var shortAliasName = typedefNameDecl.Name ?? typedefNameDecl.Spelling.ToString();
        var underlyingTypeName = GetDesugaredAliasTargetTypeName(GetAliasUnderlyingType(typedefNameDecl));

        if (string.IsNullOrWhiteSpace(underlyingTypeName))
        {
            return;
        }

        AddTypeAlias(aliasName, underlyingTypeName);
        AddTypeAlias(shortAliasName, underlyingTypeName);
    }

    private static string GetDesugaredAliasTargetTypeName(CXType type)
    {
        var spelledTypeName = NormalizeTypeName(type.Spelling.ToString());
        var canonicalTypeName = NormalizeTypeName(type.CanonicalType.Spelling.ToString());
        var desugaredTypeName = SelectConcreteTemplateTypeName(spelledTypeName, canonicalTypeName);

        if (TryGetTopLevelTemplateArguments(desugaredTypeName, out _, out var concreteArguments))
        {
            var canonicalDeclaration = type.CanonicalType.Declaration;
            if (!canonicalDeclaration.IsNull)
            {
                var canonicalHead = NormalizeTypeName(GetCppQualifiedName(canonicalDeclaration));
                if (!string.IsNullOrWhiteSpace(canonicalHead))
                {
                    return $"{canonicalHead}<{string.Join(", ", concreteArguments)}>";
                }
            }
        }

        return desugaredTypeName;
    }

    private static CXType GetAliasUnderlyingType(TypedefNameDecl typedefNameDecl)
    {
        return clang.getTypedefDeclUnderlyingType(typedefNameDecl.Handle);
    }

    private static void AddTypeAlias(string aliasName, string underlyingTypeName)
    {
        var normalizedAlias = NormalizeTypeName(aliasName);
        if (string.IsNullOrWhiteSpace(normalizedAlias))
        {
            return;
        }

        s_typeAliases[normalizedAlias] = underlyingTypeName;
        if (!s_typeAliasOriginNames.TryGetValue(underlyingTypeName, out var originNames))
        {
            originNames = new List<string>();
            s_typeAliasOriginNames[underlyingTypeName] = originNames;
        }
        originNames.Add(normalizedAlias);
    }

    private static string ResolveAliasTypeName(string typeName)
    {
        var normalizedTypeName = NormalizeTypeName(typeName);
        if (string.IsNullOrWhiteSpace(normalizedTypeName))
        {
            return string.Empty;
        }

        var suffix = string.Empty;
        var baseTypeName = ApplyNamespaceAliases(normalizedTypeName);
        while (baseTypeName.EndsWith("*", StringComparison.Ordinal) || baseTypeName.EndsWith("&", StringComparison.Ordinal))
        {
            suffix = baseTypeName[^1] + suffix;
            baseTypeName = baseTypeName[..^1].TrimEnd();
        }

        baseTypeName = StripTopLevelCvQualifier(baseTypeName);

        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (s_typeAliases.TryGetValue(baseTypeName, out var aliasedTypeName) && visited.Add(baseTypeName))
        {
            baseTypeName = ApplyNamespaceAliases(NormalizeTypeName(aliasedTypeName));
        }

        return baseTypeName + suffix;
    }

    private static string ApplyNamespaceAliases(string typeName)
    {
        foreach (var (aliasName, targetName) in s_namespaceAliases.OrderByDescending(static entry => entry.Key.Length))
        {
            if (string.Equals(typeName, aliasName, StringComparison.Ordinal))
            {
                return targetName;
            }

            if (typeName.StartsWith(aliasName + "::", StringComparison.Ordinal))
            {
                return targetName + typeName[aliasName.Length..];
            }
        }

        return typeName;
    }

    private static bool TryCreateManagedTypeAlias(
        TypedefNameDecl typedefNameDecl,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        out BindingTypeAlias aliasBinding)
    {
        var cppQualifiedName = GetCppQualifiedName(typedefNameDecl);
        var nativeName = typedefNameDecl.Name ?? typedefNameDecl.Spelling.ToString();
        if (string.IsNullOrWhiteSpace(cppQualifiedName) || string.IsNullOrWhiteSpace(nativeName))
        {
            aliasBinding = null!;
            return false;
        }

        var aliasName = NormalizeTypeName(cppQualifiedName);
        var repairTypeName = SelectConcreteTemplateTypeName(
            NormalizeTypeName(GetAliasUnderlyingType(typedefNameDecl).Spelling.ToString()),
            ResolveAliasTypeName(aliasName));

        var targetType = TryMapAliasTargetType(typedefNameDecl, recordsByName, enumsByName, managedTypeOverrides);
        if (targetType is not null)
        {
            targetType = RepairAliasTargetGenericArguments(targetType, repairTypeName, recordsByName, enumsByName, managedTypeOverrides);
        }
        if (targetType is null || !CanRenderManagedAliasTarget(targetType))
        {
            aliasBinding = null!;
            return false;
        }

        var managedName = GetManagedAliasName(nativeName);
        if (string.Equals(managedName, targetType.UnaliasedManagedTypeName, StringComparison.Ordinal))
        {
            aliasBinding = null!;
            return false;
        }

        aliasBinding = new BindingTypeAlias(cppQualifiedName, nativeName, managedName, targetType);
        return true;
    }

    private static BindingType RepairAliasTargetGenericArguments(
        BindingType targetType,
        string repairTypeName,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides)
    {
        repairTypeName = SelectConcreteTemplateTypeName(targetType.CppTypeName, repairTypeName);
        if (targetType.Kind != BindingTypeKind.Handle ||
            string.IsNullOrWhiteSpace(targetType.RecordId) ||
            !TryGetTopLevelTemplateArguments(repairTypeName, out _, out var concreteArguments) ||
            targetType.GenericArguments.Count >= concreteArguments.Count)
        {
            return targetType;
        }

        var record = recordsByName.Values.Distinct().FirstOrDefault(candidate => string.Equals(candidate.SymbolId, targetType.RecordId, StringComparison.Ordinal));
        if (record is null)
        {
            return targetType;
        }

        var repairedArguments = ParseVisibleRecordTemplateArguments(
            record,
            repairTypeName,
            recordsByName,
            enumsByName,
            managedTypeOverrides,
            EmptyGenericParameters);
        if (repairedArguments.Count <= targetType.GenericArguments.Count)
        {
            return targetType;
        }

        var repairedType = BindingType.CreateHandle(
            repairTypeName.EndsWith("*", StringComparison.Ordinal) ? repairTypeName : $"{repairTypeName}*",
            targetType.Marshaller.PublicTypeName,
            targetType.RecordId,
            targetType.MarshallerTypeName,
            repairedArguments,
            targetType.Marshaller.CppIsRecordValue);
        repairedType.SetManagedTypeFormatOverride(targetType.ManagedTypeFormatOverride);
        return repairedType;
    }

    private static BindingType? TryMapAliasTargetType(
        TypedefNameDecl typedefNameDecl,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides)
    {
        try
        {
            var aliasName = NormalizeTypeName(GetCppQualifiedName(typedefNameDecl));
            var underlyingTypeName = NormalizeTypeName(GetAliasUnderlyingType(typedefNameDecl).Spelling.ToString());
            underlyingTypeName = SelectConcreteTemplateTypeName(underlyingTypeName, ResolveAliasTypeName(aliasName));

            // Only match a managed type override directly keyed on the alias name.
            // Generic pattern matches (Step 3 of TryGetManagedTypeOverride) match
            // the canonical underlying type, not the alias, and would incorrectly
            // resolve open generic parameters when MapType on the underlying type
            // already produces the correct closed instantiation.
            var aliasWithoutConst = aliasName.StartsWith("const ", StringComparison.Ordinal)
                ? aliasName["const ".Length..].TrimStart()
                : aliasName;
            if (managedTypeOverrides.TryGetValue(aliasWithoutConst, out var managedTypeOverride) &&
                managedTypeOverride is not null)
            {
                var directOverrideType = GetAliasUnderlyingType(typedefNameDecl);
                return ApplyCxAstTemplateArguments(
                    directOverrideType,
                    CreateConfiguredOrWrapperType(
                    managedTypeOverride,
                    recordsByName,
                    enumsByName,
                    managedTypeOverrides,
                    EmptyGenericParameters,
                    underlyingTypeName),
                    recordsByName,
                    enumsByName,
                    managedTypeOverrides,
                    EmptyGenericParameters);
            }

            if (TryGetManagedTypeOverride(
                    underlyingTypeName,
                    ResolveAliasTypeName(underlyingTypeName),
                    underlyingTypeName,
                    managedTypeOverrides,
                    isReturn: false,
                    out var underlyingManagedTypeOverride) &&
                underlyingManagedTypeOverride is not null)
            {
                var patternOverrideType = GetAliasUnderlyingType(typedefNameDecl);
                return ApplyCxAstTemplateArguments(
                    patternOverrideType,
                    CreateConfiguredOrWrapperType(
                    underlyingManagedTypeOverride,
                    recordsByName,
                    enumsByName,
                    managedTypeOverrides,
                    EmptyGenericParameters,
                    underlyingTypeName),
                    recordsByName,
                    enumsByName,
                    managedTypeOverrides,
                    EmptyGenericParameters);
            }

            var aliasUnderlyingType = GetAliasUnderlyingType(typedefNameDecl);
            return ApplyCxAstTemplateArguments(
                aliasUnderlyingType,
                MapType(
                    aliasUnderlyingType,
                    recordsByName,
                    enumsByName,
                    managedTypeOverrides,
                    EmptyGenericParameters,
                    preserveAliasSurface: false),
                recordsByName,
                enumsByName,
                managedTypeOverrides,
                EmptyGenericParameters);
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static BindingType TryApplyManagedAliasSurface(
        CXType sourceType,
        BindingType mappedType,
        bool preserveAliasSurface,
        IDictionary<string, EnumBinding> enumsByName)
    {
        if (!preserveAliasSurface ||
            !CanRenderManagedAliasTarget(mappedType) ||
            mappedType.Marshaller.IsRefStruct ||
            !TryGetOwnedManagedAliasInfo(sourceType, out var aliasInfo))
        {
            return mappedType;
        }

        mappedType = mappedType.WithAliasSurface(aliasInfo.CppQualifiedName, aliasInfo.NativeName);
        var managedAliasName = GetManagedAliasName(aliasInfo.NativeName);
        return string.Equals(managedAliasName, mappedType.UnaliasedManagedTypeName, StringComparison.Ordinal)
            ? mappedType
            : mappedType.WithManagedTypeFormatOverride(managedAliasName);
    }

    private static bool TryGetQualifiedAliasType(CXType sourceType, out string unqualifiedAliasName, out string qualifiedAliasName)
    {
        var resolvedType = sourceType;
        while (TypeIsAnyReference(resolvedType) || TypeIsPointer(resolvedType))
        {
            resolvedType = resolvedType.PointeeType;
        }

        var declaration = resolvedType.Declaration;
        if (declaration.Kind is not (CXCursorKind.CXCursor_TypedefDecl or CXCursorKind.CXCursor_TypeAliasDecl))
        {
            var spelledAliasName = NormalizeTypeName(resolvedType.Spelling.ToString());
            if (!string.IsNullOrWhiteSpace(spelledAliasName) &&
                s_ownedManagedAliases.ContainsKey(spelledAliasName))
            {
                qualifiedAliasName = spelledAliasName;
                var lastColons = qualifiedAliasName.LastIndexOf("::", StringComparison.Ordinal);
                unqualifiedAliasName = lastColons >= 0 ? qualifiedAliasName[(lastColons + 2)..] : qualifiedAliasName;
                return true;
            }

            unqualifiedAliasName = string.Empty;
            qualifiedAliasName = string.Empty;
            return false;
        }

        return TryPopulateQualifiedNamedType(declaration, out unqualifiedAliasName, out qualifiedAliasName);
    }

    private static string GetManagedAliasName(string nativeAliasName)
    {
        return EscapeManagedIdentifier(ToPascalCase(nativeAliasName));
    }

    private static bool CanRenderManagedAliasTarget(BindingType type)
    {
        // Generic parameters are open type variables — can't be aliased.
        if (type.Kind == BindingTypeKind.GenericParameter)
        {
            return false;
        }

        return type.Kind is BindingTypeKind.Handle or BindingTypeKind.Enum ||
            type.Kind == BindingTypeKind.Primitive && type.PrimitiveKind != BindingPrimitiveKind.Void;
    }

    private static bool TryGetOwnedManagedAliasInfo(CXType sourceType, out OwnedAliasInfo aliasInfo)
    {
        if (TryGetQualifiedAliasType(sourceType, out _, out var qualifiedAliasName) &&
            s_ownedManagedAliases.TryGetValue(NormalizeTypeName(qualifiedAliasName), out aliasInfo!))
        {
            return true;
        }

        aliasInfo = null!;
        return false;
    }

    private static int GetTemplateSpecificityScore(string typeName)
    {
        var score = 0;

        score += Regex.Matches(typeName, "::", RegexOptions.CultureInvariant).Count * 2;
        score += Regex.Matches(typeName, @"(?<![:\w])T[A-Za-z0-9_]*(?![:\w])", RegexOptions.CultureInvariant).Count * -10;

        return score;
    }

    private static void ApplyExplicitGenericInstantiations(
        Decl decl,
        BindingNode node,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides)
    {
        if (node is not RecordBinding and not FunctionBinding)
        {
            return;
        }

        var cursor = decl.Handle;
        var sourceText = GetSourceText(cursor.TranslationUnit, cursor.Extent);
        if (node is RecordBinding)
        {
            var braceIndex = sourceText.IndexOf('{');
            if (braceIndex >= 0)
            {
                sourceText = sourceText[..braceIndex];
            }
        }

        var instantiations = ParseInstantiationMatches(SharpCppAttributeScanner.Scan(sourceText).Where(match => string.Equals(match.Name, "instantiate", StringComparison.Ordinal)), recordsByName, enumsByName, managedTypeOverrides);

        if (instantiations.Count == 0)
        {
            cursor.Location.GetFileLocation(out var file, out var line, out _, out _);
            var filePath = Path.GetFullPath(file.Name.CString);
            if (!string.IsNullOrWhiteSpace(filePath) && s_instantiateAnnotations.TryGetValue(filePath, out var annotations))
            {
                var oneBasedCursorLine = (int)line;
                foreach (var annotation in annotations.Where(annotation => annotation.Line == oneBasedCursorLine || annotation.Line == oneBasedCursorLine - 1))
                {
                    foreach (var instantiation in annotation.Instantiations)
                    {
                        if (!instantiations.Any(existing => InstantiationsEqual(existing.TypeArguments, instantiation.TypeArguments)))
                        {
                            instantiations.Add(instantiation);
                        }
                    }
                }
            }
        }

        foreach (var instantiation in EnumerateAstGenericInstantiations(decl, recordsByName, enumsByName, managedTypeOverrides))
        {
            if (!instantiations.Any(existing => InstantiationsEqual(existing.TypeArguments, instantiation.TypeArguments)))
            {
                instantiations.Add(instantiation);
            }
        }

        var normalizedInstantiations = new List<RecordGenericInstantiationBinding>();
        foreach (var instantiation in instantiations)
        {
            var normalized = NormalizeGenericInstantiation(instantiation, recordsByName, enumsByName);
            if (!normalizedInstantiations.Any(existing => InstantiationsEqual(existing.TypeArguments, normalized.TypeArguments)))
            {
                normalizedInstantiations.Add(normalized);
            }
        }

        if (normalizedInstantiations.Count > 0)
        {
            node.SetMetadata(MetadataKeys.ExplicitGenericInstantiations, new GenericInstantiationMetadata(normalizedInstantiations));
        }
    }

    private static RecordGenericInstantiationBinding NormalizeGenericInstantiation(
        RecordGenericInstantiationBinding instantiation,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName)
    {
        var typeArguments = instantiation.TypeArguments
            .Select(argument => NormalizeTemplateArgumentType(argument, recordsByName, enumsByName))
            .ToArray();
        var nativeTemplateArguments = instantiation.NativeTemplateArguments
            .Select(argument => argument.Kind == BindingTemplateArgumentKind.Type
                ? BindingTemplateArgument.FromType(NormalizeTemplateArgumentType(argument.Type!, recordsByName, enumsByName))
                : BindingTemplateArgument.FromIntegral(argument.IntegralValue, argument.IsBoolean))
            .ToArray();
        return new RecordGenericInstantiationBinding(typeArguments, nativeTemplateArguments, instantiation.IsInferred);
    }

    private static IEnumerable<RecordGenericInstantiationBinding> EnumerateAstGenericInstantiations(
        Decl decl,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides)
    {
        switch (decl)
        {
            case FunctionTemplateDecl functionTemplateDecl:
                var templateDeclarations = new[]
                    {
                        functionTemplateDecl,
                        functionTemplateDecl.CanonicalDecl,
                        functionTemplateDecl.MostRecentDecl
                    }
                    .DistinctBy(static template => template.Handle);
                var specializations = templateDeclarations
                    .SelectMany(static template => template.Specializations
                        .Concat(EnumerateChildDeclsSafely(template).OfType<FunctionDecl>()))
                    .Where(static specialization => specialization.TemplateSpecializationArgs.Count > 0)
                    .DistinctBy(static specialization => specialization.Handle);
                foreach (var specialization in specializations)
                {
                    if (TryCreateInstantiationFromTemplateArguments(specialization.TemplateSpecializationArgs, recordsByName, enumsByName, managedTypeOverrides, out var instantiation))
                    {
                        yield return instantiation;
                    }
                }
                break;
        }
    }

    private static bool TryCreateInstantiationFromTemplateArguments(
        IReadOnlyList<TemplateArgument> templateArguments,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        out RecordGenericInstantiationBinding instantiation)
    {
        var arguments = new List<BindingType>();
        foreach (var templateArgument in templateArguments)
        {
            if (templateArgument.Kind == CXTemplateArgumentKind.CXTemplateArgumentKind_Pack)
            {
                foreach (var packElement in templateArgument.PackElements)
                {
                    if (!TryMapTemplateArgument(packElement, recordsByName, enumsByName, managedTypeOverrides, out var packArgument))
                    {
                        instantiation = default!;
                        return false;
                    }

                    arguments.Add(packArgument);
                }
                continue;
            }

            if (!TryMapTemplateArgument(templateArgument, recordsByName, enumsByName, managedTypeOverrides, out var argument))
            {
                instantiation = default!;
                return false;
            }

            arguments.Add(argument);
        }

        if (arguments.Count == 0)
        {
            instantiation = default!;
            return false;
        }

        instantiation = new RecordGenericInstantiationBinding(arguments);
        return true;
    }

    private static bool TryMapTemplateArgument(
        TemplateArgument templateArgument,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        out BindingType argument)
    {
        if (templateArgument.Kind != CXTemplateArgumentKind.CXTemplateArgumentKind_Type)
        {
            argument = default!;
            return false;
        }

        try
        {
            argument = NormalizeTemplateArgumentType(MapType(
                templateArgument.AsType.Handle,
                recordsByName,
                enumsByName,
                managedTypeOverrides,
                EmptyGenericParameters),
                recordsByName,
                enumsByName);
            return true;
        }
        catch (NotSupportedException)
        {
            argument = default!;
            return false;
        }
    }

    private static BindingType NormalizeTemplateArgumentType(
        BindingType type,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName)
    {
        if (type.IsCallback)
        {
            var returnType = type.CallbackReturnType is null
                ? null
                : NormalizeTemplateArgumentType(type.CallbackReturnType, recordsByName, enumsByName);
            var parameterTypes = type.CallbackParameterTypes
                .Select(parameterType => NormalizeTemplateArgumentType(parameterType, recordsByName, enumsByName))
                .ToArray();
            return returnType is null
                ? type.WithCallbackSignature(BindingType.Void, parameterTypes)
                : type.WithCallbackSignature(returnType, parameterTypes);
        }

        var genericArguments = type.GenericArguments
            .Select(argument => NormalizeTemplateArgumentType(argument, recordsByName, enumsByName))
            .ToArray();
        var result = genericArguments.SequenceEqual(type.GenericArguments)
            ? type
            : type.WithGenericArguments(genericArguments);
        if (result.Kind == BindingTypeKind.Handle &&
            !string.IsNullOrWhiteSpace(result.RecordId) &&
            recordsByName.Values.Distinct().FirstOrDefault(record => string.Equals(record.SymbolId, result.RecordId, StringComparison.Ordinal)) is { } record)
        {
            return result.WithCppTypeName(FormatRecordTemplateArgumentTypeName(result, record));
        }

        if (result.Kind == BindingTypeKind.Enum &&
            !string.IsNullOrWhiteSpace(result.EnumId) &&
            enumsByName.Values.Distinct().FirstOrDefault(@enum => string.Equals(@enum.SymbolId, result.EnumId, StringComparison.Ordinal)) is { } enumBinding)
        {
            return result.WithCppTypeName(enumBinding.CppQualifiedName);
        }

        return result;
    }

    private static string FormatRecordTemplateArgumentTypeName(BindingType type, RecordBinding record)
    {
        var baseTypeName = record.FormatCppTypeName(type.GenericArguments);
        var currentTypeName = NormalizeTypeName(type.CppTypeName);
        var isConst = currentTypeName.StartsWith("const ", StringComparison.Ordinal);
        var isReference = type.Marshaller.CppIsReference || currentTypeName.EndsWith("&", StringComparison.Ordinal);
        var isPointer = !type.Marshaller.CppIsRecordValue && !isReference && currentTypeName.EndsWith("*", StringComparison.Ordinal);

        return $"{(isConst ? "const " : string.Empty)}{baseTypeName}{(isPointer ? "*" : string.Empty)}{(isReference ? "&" : string.Empty)}";
    }

    private static readonly Dictionary<string, List<InstantiateAnnotation>> s_instantiateAnnotations = new(StringComparer.Ordinal);

    private sealed record InstantiateAnnotation(int Line, IReadOnlyList<RecordGenericInstantiationBinding> Instantiations);

    private static void PreScanInstantiateAnnotations(
        IReadOnlyList<string> headerFiles,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides)
    {
        foreach (var headerFile in headerFiles)
        {
            var path = Path.GetFullPath(headerFile);
            if (!File.Exists(path) || s_instantiateAnnotations.ContainsKey(path))
            {
                continue;
            }

            var content = File.ReadAllText(path);
            var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            var annotations = new List<InstantiateAnnotation>();

            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                List<RecordGenericInstantiationBinding> instantiations;
                try
                {
                    instantiations = ParseInstantiationMatches(
                        SharpCppAttributeScanner.Scan(lines[lineIndex]).Where(match => string.Equals(match.Name, "instantiate", StringComparison.Ordinal)),
                        new Dictionary<string, RecordBinding>(StringComparer.Ordinal),
                        new Dictionary<string, EnumBinding>(StringComparer.Ordinal),
                        managedTypeOverrides);
                }
                catch (NotSupportedException)
                {
                    // The prescan runs before any declaration is known; arguments naming
                    // enums or records resolve when the annotated declaration is parsed.
                    continue;
                }

                if (instantiations.Count == 0)
                {
                    continue;
                }

                annotations.Add(new InstantiateAnnotation(lineIndex + 1, instantiations));
            }

            s_instantiateAnnotations[path] = annotations;
        }
    }

    private static List<RecordGenericInstantiationBinding> ParseInstantiationMatches(
        IEnumerable<SharpCppAttributeScanner.AttributeMatch> matches,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides)
    {
        var instantiations = new List<RecordGenericInstantiationBinding>();

        foreach (var match in matches)
        {
            var arguments = InstantiateArgumentExpression().Matches(match.Body)
                .Select(argumentMatch => ParseTypeToken(argumentMatch.Groups["value"].Value, recordsByName, enumsByName, managedTypeOverrides, EmptyGenericParameters))
                .ToArray();

            if (arguments.Length == 0)
            {
                continue;
            }

            instantiations.Add(new RecordGenericInstantiationBinding(arguments));
        }

        return instantiations;
    }

    private static bool InstantiationsEqual(IReadOnlyList<BindingType> left, IReadOnlyList<BindingType> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!string.Equals(left[index].CppTypeName, right[index].CppTypeName, StringComparison.Ordinal) ||
                left[index].Kind != right[index].Kind)
            {
                return false;
            }
        }

        return true;
    }

    private static void ApplyVirtualSpecifiers(CXXMethodDecl methodDecl, FunctionBinding function)
    {
        if (!function.IsVirtual)
        {
            return;
        }

        bool isOverride = methodDecl.OverriddenMethods.Count > 0;
        bool isFinal = false;

        foreach (var overriddenMethod in methodDecl.OverriddenMethods)
        {
            var declarationId = GetNativeDeclarationId(overriddenMethod);
            if (!string.IsNullOrWhiteSpace(declarationId) &&
                !function.OverriddenNativeDeclarationIds.Contains(declarationId, StringComparer.Ordinal))
            {
                function.OverriddenNativeDeclarationIds.Add(declarationId);
            }
        }

        if (methodDecl.HasAttrs)
        {
            foreach (var attr in methodDecl.Attrs)
            {
                if (attr.Kind == CX_AttrKind.CX_AttrKind_Override)
                {
                    isOverride = true;
                }
                else if (attr.Kind == CX_AttrKind.CX_AttrKind_Final)
                {
                    isFinal = true;
                }
            }
        }

        var sourceText = GetSourceText(methodDecl.Handle.TranslationUnit, methodDecl.Extent);
        var declarationText = sourceText.IndexOf('{') is var braceIndex and >= 0
            ? sourceText[..braceIndex]
            : sourceText;

        if (!isOverride && declarationText.Contains("override", StringComparison.Ordinal))
        {
            isOverride = true;
        }

        if (!isFinal && declarationText.Contains("final", StringComparison.Ordinal))
        {
            isFinal = true;
        }

        function.IsOverride = isOverride;
        function.IsFinal = isFinal;
    }

    private static string? GetNativeDeclarationId(FunctionDecl functionDecl)
    {
        var declaration = functionDecl.InstantiatedFromMemberFunction ??
            functionDecl.TemplateInstantiationPattern ??
            functionDecl.CanonicalDecl;
        var usr = declaration.Handle.Usr.CString;
        return string.IsNullOrWhiteSpace(usr) ? null : usr;
    }
}
