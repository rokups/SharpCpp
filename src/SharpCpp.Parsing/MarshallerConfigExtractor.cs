using ClangSharp;
using ClangSharp.Interop;
using Microsoft.Extensions.Logging;
using SharpCpp.Model.Logging;
using System.Reflection;
using System.Text.RegularExpressions;

namespace SharpCpp.Parsing;

/// <summary>
/// Replaces regex-only <c>LoadManagedTypeOverrides</c> and the regex-heavy portion of
/// <c>MarshallerMetadataPhase2</c> with a ClangSharp-backed pass that
/// walks the AST for Marshaller&lt;T&gt; / ReturnMarshaller&lt;T&gt; declarations and extracts
/// <c>[[sharpcpp::managed_type(...)]]</c> and <c>[[sharpcpp::expose_member(...)]]</c>
/// attributes from cursor properties, not from source-text regex.
///
/// The C++ compiler handles template resolution; regex is reduced to parsing
/// attribute body key=value pairs only.
/// </summary>
internal static class MarshallerConfigExtractor
{
    private static readonly MethodInfo GetOrCreateCursorMethod = typeof(TranslationUnit)
        .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
        .Single(static method => method.Name == "GetOrCreate" &&
            method.IsGenericMethodDefinition &&
            method.GetParameters() is [{ ParameterType: var parameterType }] &&
            parameterType == typeof(CXCursor))
        .MakeGenericMethod(typeof(Decl));

    // Key=value extractors (attribute body parsing only — NOT C++ code parsing).
    private static readonly Regex PublicTypeExpression = new(
        @"public_type\s*=\s*\""(?<value>[^\""\\]*(?:\\.[^\""\\]*)*)\""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex UndefaultableExpression = new(
        @"undefaultable\s*=\s*(?<value>true|false)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ByReferenceExpression = new(
        @"by_reference\s*=\s*(?<value>true|false)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex MarshallerTypeInAttributeExpression = new(
        @"marshaller_type\s*=\s*\""(?<value>[^\""\\]*(?:\\.[^\""\\]*)*)\""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ManagedMarshallerInAttributeExpression = new(
        @"managed_marshaller\s*=\s*\""(?<value>[^\""\\]*(?:\\.[^\""\\]*)*)\""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ManagedNamespaceInAttributeExpression = new(
        @"managed_namespace\s*=\s*\""(?<value>[^\""\\]*(?:\\.[^\""\\]*)*)\""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ManagedWrapperAssemblyInAttributeExpression = new(
        @"managed_wrapper_assembly\s*=\s*\""(?<value>[^\""\\]*(?:\\.[^\""\\]*)*)\""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ManagedWrapperProviderKindInAttributeExpression = new(
        @"managed_wrapper_provider_kind\s*=\s*\""(?<value>[^\""\\]*(?:\\.[^\""\\]*)*)\""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ManagedWrapperProviderNamespaceInAttributeExpression = new(
        @"managed_wrapper_provider_namespace\s*=\s*\""(?<value>[^\""\\]*(?:\\.[^\""\\]*)*)\""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ManagedWrapperCopyableInAttributeExpression = new(
        @"managed_wrapper_copyable\s*=\s*(?<value>true|false)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ProjectionExpression = new(
        @"projection\s*=\s*(?<value>true|false)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CallbackExpression = new(
        @"callback\s*=\s*(?<value>true|false)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex FixedArrayExpression = new(
        @"fixed_array\s*=\s*(?<value>true|false)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex NativeArrayExpression = new(
        @"native_array\s*=\s*(?<value>true|false)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DynamicArrayExpression = new(
        @"dynamic_array\s*=\s*(?<value>true|false)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ReturnOwnershipExpression = new(
        @"return_ownership\s*=\s*\""(?<value>[^\""\\]*(?:\\.[^\""\\]*)*)\""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ManagedInstanceCacheFactoryExpression = new(
        @"managed_instance_cache_factory\s*=\s*\""(?<value>[^\""\\]*(?:\\.[^\""\\]*)*)\""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ManagedInstanceCacheTypeExpression = new(
        @"managed_instance_cache_type\s*=\s*\""(?<value>[^\""\\]*(?:\\.[^\""\\]*)*)\""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ReleaseStrategyExpression = new(
        @"release_strategy\s*=\s*\""(?<value>[^\""\\]*(?:\\.[^\""\\]*)*)\""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ShapeExpression = new(
        @"shape\s*=\s*\""(?<value>[^\""]+)\""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ExposedExpression = new(
        @"exposed\s*=\s*(?<value>true|false)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);


    // ManagedTypeOverride record (same shape as ClangSharpHeaderParser's private record).
    internal sealed record ManagedTypeOverride(string CppTypeName, string PublicTypeName)
    {
        public CppTypePattern? NativeTypePattern { get; init; }

        /// <summary>The pattern repeats a template parameter (`twin<T, T>`); see <see cref="MarshallerBodyConfig.ConstrainsTemplateArguments"/>.</summary>
        public bool ConstrainsTemplateArguments =>
            NativeTypePattern is { } pattern && HasRepeatedTemplateParameter(pattern, new HashSet<(uint Depth, uint Index)>());
        public bool Undefaultable { get; init; }
        /// <summary>
        /// The public type is the pointee's projection passed by reference (`out string`
        /// over `const char**`); positions that cannot pass by reference keep the pointer ABI.
        /// </summary>
        public bool ByReference { get; init; }
        public string? MarshallerTypeName { get; init; }
        public string? ManagedMarshaller { get; init; }
        public string? ManagedNamespace { get; init; }
        public string? ManagedWrapperAssembly { get; init; }
        public string? ManagedWrapperProviderKind { get; init; }
        public string? ManagedWrapperProviderNamespace { get; init; }
        public bool? ManagedWrapperCopyable { get; init; }
        public bool HasManagedBaseType { get; init; }
        public bool HasExposedMembers { get; init; }
        public IReadOnlyList<string> TemplateParameterNames { get; init; } = Array.Empty<string>();
        public IReadOnlyList<int> VisibleTemplateParameterIndices { get; init; } = Array.Empty<int>();
        public bool Projection { get; init; }
        public bool Callback { get; init; }
        public bool FixedArray { get; init; }
        public bool NativeArray { get; init; }
        public bool DynamicArray { get; init; }
        public string? ReturnOwnership { get; init; }
        public string? ManagedInstanceCacheFactoryExpression { get; init; }
        public string? ManagedInstanceCacheTypeName { get; init; }
        public bool EmitDefaultNativeReleaseOverride { get; init; } = true;
        public bool HasPublicTypeMapping { get; init; } = true;
    }

    internal enum CppTypePatternKind
    {
        Builtin,
        Declared,
        TemplateParameter,
        Pointer,
        LValueReference,
        RValueReference,
        /// <summary>A reference to a non-type template parameter, identified by depth and index.</summary>
        NonTypeTemplateParameter,
        /// <summary>A constant non-type template argument, compared by value.</summary>
        IntegralValue
    }

    internal sealed record CppTypePattern(
        CppTypePatternKind Kind,
        bool IsConst = false,
        string? DeclarationId = null,
        CXTypeKind BuiltinKind = CXTypeKind.CXType_Invalid,
        uint TemplateParameterDepth = 0,
        uint TemplateParameterIndex = 0,
        CppTypePattern? Pointee = null,
        IReadOnlyList<CppTypePattern>? TemplateArguments = null,
        long IntegralValue = 0);

    /// <summary>
    /// Lightweight representation of a <c>[[sharpcpp::expose_member(...)]]</c> member function
    /// extracted from the ClangSharp AST cursor, not from source-text regex.
    /// </summary>
    internal sealed record ExposedMemberInfo(
        string MethodName,
        string CppReturnType,
        bool IsConst,
        string Shape,
        bool Exposed,
        IReadOnlyList<(string Name, string CppType)> Parameters);

    /// <summary>
    /// Full attribute body + extracted exposed member info for a Marshaller specialization.
    /// Used by <c>MarshallerMetadataPhase2</c> for record-level back-patching.
    /// No struct body source text — member info comes from AST cursors.
    /// </summary>
    internal sealed record MarshallerBodyConfig(
        string AttributeBody,
        IReadOnlyList<ExposedMemberInfo> ExposedMembers,
        IReadOnlyList<string> TemplateParameterNames,
        string? ManagedMarshaller = null,
        string? ManagedInstanceCacheFactoryExpression = null,
        string? ManagedInstanceCacheTypeName = null,
        bool EmitDefaultNativeReleaseOverride = true)
    {
        public string? ManagedWrapperProviderKind { get; init; }
        public string? ManagedWrapperProviderNamespace { get; init; }
        public bool? ManagedWrapperCopyable { get; init; }

        /// <summary>
        /// The configured pattern repeats a template parameter (`twin<T, T>`), so it
        /// covers some specializations of the record, not the record itself.
        /// </summary>
        public bool ConstrainsTemplateArguments { get; init; }
    }

    internal static string? GetManagedWrapperProviderKind(string attributeBody)
    {
        ArgumentNullException.ThrowIfNull(attributeBody);
        return ManagedWrapperProviderKindInAttributeExpression.Match(attributeBody) is { Success: true } match
            ? match.Groups["value"].Value
            : null;
    }

    /// <summary>
    /// Parse marshaller configuration headers via ClangSharp and extract
    /// <c>[[sharpcpp::managed_type(...)]]</c> overrides from Marshaller&lt;T&gt;
    /// and ReturnMarshaller&lt;T&gt; specializations.
    /// </summary>
    public static IReadOnlyDictionary<string, ManagedTypeOverride> ExtractManagedTypeOverrides(
        IReadOnlyList<string> marshallerFiles,
        IReadOnlyList<string> clangArgs)
    {
        var result = new Dictionary<string, ManagedTypeOverride>(StringComparer.Ordinal);

        PerformManagedClangParse(marshallerFiles, clangArgs, rootDecl =>
        {
            var declIndex = BuildRecordLikeDeclIndex(rootDecl);
            WalkForMarshallerDeclarations(rootDecl, declIndex, result);
            AddExplicitIndirectionMarshallerOverrides(rootDecl, result);
        });

        return result;
    }

    /// <summary>
    /// Extract Marshaller specialization attribute bodies and exposed member info
    /// from marshaller configuration headers. All method metadata comes from the
    /// ClangSharp AST cursors — no C++ source-text regex.
    /// </summary>
    public static IReadOnlyDictionary<string, MarshallerBodyConfig> ExtractMarshallerBodies(
        IReadOnlyList<string> marshallerFiles,
        IReadOnlyList<string> clangArgs)
    {
        var result = new Dictionary<string, MarshallerBodyConfig>(StringComparer.Ordinal);

        PerformManagedClangParse(marshallerFiles, clangArgs, rootDecl =>
        {
            var declIndex = BuildRecordLikeDeclIndex(rootDecl);
            WalkForMarshallerBodies(rootDecl, declIndex, result);
        });

        return result;
    }

    private static void PerformManagedClangParse(
        IReadOnlyList<string> marshallerFiles,
        IReadOnlyList<string> clangArgs,
        Action<Decl> action)
    {
        var tempCppPath = GenerateTempCpp(marshallerFiles);
        try
        {
            using var index = CXIndex.Create();

            // NOTE: No `using` on translationUnit — TranslationUnit.GetOrCreate() below
            // takes ownership of the native handle. Disposing the interop struct would
            // double-free: the managed TU's finalizer also calls clang_disposeTranslationUnit.
            var parseError = CXTranslationUnit.TryParse(
                index,
                tempCppPath,
                clangArgs.ToArray(),
                Array.Empty<CXUnsavedFile>(),
                CXTranslationUnit_Flags.CXTranslationUnit_None,
                out var translationUnit);

            if (parseError != CXErrorCode.CXError_Success || translationUnit.Handle == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    $"libclang failed to parse marshaller configuration headers " +
                    $"(error: {parseError}, translation unit handle: 0x{translationUnit.Handle.ToInt64():X}). " +
                    $"Arguments: {System.Text.Json.JsonSerializer.Serialize(clangArgs)}");
            }

            if (translationUnit.NumDiagnostics > 0)
            {
                for (uint diagIndex = 0; diagIndex < translationUnit.NumDiagnostics; diagIndex++)
                {
                    using var diagnostic = translationUnit.GetDiagnostic(diagIndex);
                    if (diagnostic.Severity == CXDiagnosticSeverity.CXDiagnostic_Error ||
                        diagnostic.Severity == CXDiagnosticSeverity.CXDiagnostic_Fatal)
                    {
                        Logger.LogWarning("Marshaller config parse error: {Error}",
                            diagnostic.Format(CXDiagnostic.DefaultDisplayOptions).ToString());
                    }
                }
            }

            // Get the managed TranslationUnit and its root Decl
            var managedTu = TranslationUnit.GetOrCreate(translationUnit);
            action(managedTu.TranslationUnitDecl);
        }
        finally
        {
            TryDeleteTempFile(tempCppPath);
        }
    }

    private static string GenerateTempCpp(IReadOnlyList<string> marshallerFiles)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"sharpcpp_marshaller_config_{Guid.NewGuid():N}.cpp");
        using (var writer = new StreamWriter(tempPath))
        {
            writer.WriteLine("// Auto-generated by SharpCpp MarshallerConfigExtractor");
            foreach (var file in marshallerFiles)
            {
                var fullPath = Path.GetFullPath(file);
                if (!File.Exists(fullPath))
                {
                    continue;
                }

                var includePath = fullPath.Replace('\\', '/');
                writer.WriteLine($"#include \"{includePath}\"");
            }
        }

        return tempPath;
    }

    private static void TryDeleteTempFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    // ─── Type override extraction (Marshaller<T> / ReturnMarshaller<T>) ─────────

    private static void WalkForMarshallerDeclarations(
        Decl decl,
        IReadOnlyDictionary<string, Decl> declIndex,
        Dictionary<string, ManagedTypeOverride> result)
    {
        foreach (var child in EnumerateDescendantDecls(decl))
        {
            if (child is RecordDecl or ClassTemplateDecl or ClassTemplatePartialSpecializationDecl &&
                IsPotentialMarshallerDeclaration(child))
            {
                if (TryGetMarshallerTemplateKind(child, out var isReturn))
                {
                    ProcessMarshallerDeclaration(child, declIndex, isReturn, result);
                }
            }
        }
    }

    private static void ProcessMarshallerDeclaration(
        Decl decl,
        IReadOnlyDictionary<string, Decl> declIndex,
        bool isReturn,
        Dictionary<string, ManagedTypeOverride> result)
    {
        var sourceText = GetSourceText(decl.Handle.TranslationUnit, decl.Extent);

        var attrMatch = SharpCppAttributeScanner.Scan(sourceText)
            .FirstOrDefault(static match => string.Equals(match.Name, "managed_type", StringComparison.Ordinal));
        if (attrMatch is null)
        {
            return;
        }

        var body = attrMatch.Body;
        var publicTypeMatch = PublicTypeExpression.Match(body);
        var returnOwnershipMatch = ReturnOwnershipExpression.Match(body);
        var callbackMatch = CallbackExpression.Match(body);
        var isCallback = callbackMatch.Success &&
            string.Equals(callbackMatch.Groups["value"].Value, "true", StringComparison.OrdinalIgnoreCase);
        if (!publicTypeMatch.Success && !returnOwnershipMatch.Success && !isCallback)
        {
            return;
        }

        // Extract the C++ template argument type from cursor-extent source text.
        // GetSourceText preserves spaces between tokens so "const char*" stays correct.
        var cppTypeName = ExtractTemplateArgFromCursorType(decl, isReturn);
        if (string.IsNullOrWhiteSpace(cppTypeName))
        {
            return;
        }

        cppTypeName = NormalizeTypeName(cppTypeName);

        var key = isReturn ? "return:" + cppTypeName : cppTypeName;

        var undefaultableMatch = UndefaultableExpression.Match(body);
        var marshallerTypeMatch = MarshallerTypeInAttributeExpression.Match(body);
        var managedMarshallerMatch = ManagedMarshallerInAttributeExpression.Match(body);
        var managedNamespaceMatch = ManagedNamespaceInAttributeExpression.Match(body);
        var projectionMatch = ProjectionExpression.Match(body);
        var fixedArrayMatch = FixedArrayExpression.Match(body);
        var nativeArrayMatch = NativeArrayExpression.Match(body);
        var dynamicArrayMatch = DynamicArrayExpression.Match(body);
        var managedInstanceCacheFactoryMatch = ManagedInstanceCacheFactoryExpression.Match(body);
        var managedInstanceCacheTypeMatch = ManagedInstanceCacheTypeExpression.Match(body);
        var releaseStrategyMatch = ReleaseStrategyExpression.Match(body);
        var hasManagedBaseType = body.Contains("managed_base_type", StringComparison.Ordinal);
        var hasExposedMembers = ExtractExposedMembersFromDecl(decl, declIndex).Count > 0;
        var templateParameterNames = ExtractTemplateParameterNames(decl);
        var nativeTypePattern = decl is ClassTemplateSpecializationDecl { TemplateArgs: { Count: > 0 } configured } &&
            configured[0].Kind == CXTemplateArgumentKind.CXTemplateArgumentKind_Type
                ? CreateCppTypePattern(configured[0].AsType)
                : null;
        var visibleArgumentCount = Math.Max(
            templateParameterNames.Count,
            nativeTypePattern?.TemplateArguments?.Count ?? 0);
        var publicTypeName = publicTypeMatch.Success ? publicTypeMatch.Groups["value"].Value : cppTypeName;
        var visibleTemplateParameterIndices = publicTypeMatch.Success
            ? ExtractVisibleTemplateParameterIndices(publicTypeName, visibleArgumentCount)
            : Enumerable.Range(0, templateParameterNames.Count).ToArray();

        result[key] = new ManagedTypeOverride(
            cppTypeName,
            publicTypeName)
        {
            NativeTypePattern = nativeTypePattern,
            Undefaultable = undefaultableMatch.Success &&
                string.Equals(undefaultableMatch.Groups["value"].Value, "true", StringComparison.OrdinalIgnoreCase),
            ByReference = ByReferenceExpression.Match(body) is { Success: true } byReferenceMatch &&
                string.Equals(byReferenceMatch.Groups["value"].Value, "true", StringComparison.OrdinalIgnoreCase),
            MarshallerTypeName = marshallerTypeMatch.Success ? marshallerTypeMatch.Groups["value"].Value : null,
            ManagedMarshaller = managedMarshallerMatch.Success ? managedMarshallerMatch.Groups["value"].Value : null,
            ManagedNamespace = managedNamespaceMatch.Success ? managedNamespaceMatch.Groups["value"].Value : null,
            ManagedWrapperAssembly = ManagedWrapperAssemblyInAttributeExpression.Match(body) is { Success: true } assemblyMatch
                ? assemblyMatch.Groups["value"].Value
                : null,
            ManagedWrapperProviderKind = ManagedWrapperProviderKindInAttributeExpression.Match(body) is { Success: true } providerKindMatch
                ? providerKindMatch.Groups["value"].Value
                : null,
            ManagedWrapperProviderNamespace = ManagedWrapperProviderNamespaceInAttributeExpression.Match(body) is { Success: true } providerNamespaceMatch
                ? providerNamespaceMatch.Groups["value"].Value
                : null,
            ManagedWrapperCopyable = ManagedWrapperCopyableInAttributeExpression.Match(body) is { Success: true } copyableMatch &&
                bool.TryParse(copyableMatch.Groups["value"].Value, out var copyable)
                ? copyable
                : null,
            HasManagedBaseType = hasManagedBaseType,
            HasExposedMembers = hasExposedMembers,
            TemplateParameterNames = templateParameterNames,
            VisibleTemplateParameterIndices = visibleTemplateParameterIndices,
            Projection = projectionMatch.Success &&
                string.Equals(projectionMatch.Groups["value"].Value, "true", StringComparison.OrdinalIgnoreCase),
            Callback = isCallback,
            FixedArray = fixedArrayMatch.Success &&
                string.Equals(fixedArrayMatch.Groups["value"].Value, "true", StringComparison.OrdinalIgnoreCase),
            NativeArray = nativeArrayMatch.Success &&
                string.Equals(nativeArrayMatch.Groups["value"].Value, "true", StringComparison.OrdinalIgnoreCase),
            DynamicArray = dynamicArrayMatch.Success &&
                string.Equals(dynamicArrayMatch.Groups["value"].Value, "true", StringComparison.OrdinalIgnoreCase),
            ReturnOwnership = returnOwnershipMatch.Success ? returnOwnershipMatch.Groups["value"].Value : null,
            ManagedInstanceCacheFactoryExpression = managedInstanceCacheFactoryMatch.Success ? managedInstanceCacheFactoryMatch.Groups["value"].Value : null,
            ManagedInstanceCacheTypeName = managedInstanceCacheTypeMatch.Success ? managedInstanceCacheTypeMatch.Groups["value"].Value : null,
            EmitDefaultNativeReleaseOverride = !releaseStrategyMatch.Success ||
                !string.Equals(releaseStrategyMatch.Groups["value"].Value, "base", StringComparison.OrdinalIgnoreCase),
            HasPublicTypeMapping = publicTypeMatch.Success || isCallback
        };
    }

    private static void AddExplicitIndirectionMarshallerOverrides(
        Decl rootDecl,
        Dictionary<string, ManagedTypeOverride> result)
    {
        var configuredOverrides = result.ToArray();
        foreach (var specialization in EnumerateDescendantDecls(rootDecl).OfType<ClassTemplateSpecializationDecl>())
        {
            if (!TryGetMarshallerTemplateKind(specialization, out var isReturn) ||
                specialization.TemplateArgs is not { Count: > 0 } arguments ||
                arguments[0].Kind != CXTemplateArgumentKind.CXTemplateArgumentKind_Type ||
                CreateCppTypePattern(arguments[0].AsType) is not { Pointee: { } pointee } nativeTypePattern ||
                nativeTypePattern.Kind is not (CppTypePatternKind.Pointer or CppTypePatternKind.LValueReference or CppTypePatternKind.RValueReference) ||
                FormatConfiguredCppType(arguments[0].AsType, GetTemplateParameters(specialization)) is not { Length: > 0 } cppTypeName)
            {
                continue;
            }

            var key = isReturn ? "return:" + cppTypeName : cppTypeName;
            if (result.ContainsKey(key))
            {
                continue;
            }

            var projectedTypePattern = pointee with { IsConst = false };
            var inherited = configuredOverrides.FirstOrDefault(candidate =>
                candidate.Key.StartsWith("return:", StringComparison.Ordinal) == isReturn &&
                candidate.Value.NativeTypePattern is { } configuredTypePattern &&
                MatchesCppTypePattern(
                    configuredTypePattern with { IsConst = false },
                    projectedTypePattern,
                    new Dictionary<(uint Depth, uint Index), CppTypePattern>()));
            if (inherited.Value is not null)
            {
                result[key] = inherited.Value with
                {
                    NativeTypePattern = nativeTypePattern
                };
            }
        }
    }

    // ─── Body extraction (attribute + exposed members from AST cursors) ─────────

    private static void WalkForMarshallerBodies(
        Decl decl,
        IReadOnlyDictionary<string, Decl> declIndex,
        Dictionary<string, MarshallerBodyConfig> result)
    {
        foreach (var child in EnumerateDescendantDecls(decl))
        {
            if (child is RecordDecl or ClassTemplateDecl or ClassTemplatePartialSpecializationDecl &&
                IsPotentialMarshallerDeclaration(child))
            {
                if (TryGetMarshallerTemplateKind(child, out var isReturn) && !isReturn)
                {
                    ProcessMarshallerBody(child, declIndex, result);
                }
            }
        }
    }

    private static void ProcessMarshallerBody(
        Decl decl,
        IReadOnlyDictionary<string, Decl> declIndex,
        Dictionary<string, MarshallerBodyConfig> result)
    {
        var sourceText = GetSourceText(decl.Handle.TranslationUnit, decl.Extent);
        var exposedMembers = ExtractExposedMembersFromDecl(decl, declIndex);

        var attrMatch = SharpCppAttributeScanner.Scan(sourceText)
            .FirstOrDefault(static match => string.Equals(match.Name, "managed_type", StringComparison.Ordinal));
        if (attrMatch is null && exposedMembers.Count == 0)
        {
            return;
        }

        var body = attrMatch?.Body ?? string.Empty;
        var managedMarshallerMatch = ManagedMarshallerInAttributeExpression.Match(body);
        var managedInstanceCacheFactoryMatch = ManagedInstanceCacheFactoryExpression.Match(body);
        var managedInstanceCacheTypeMatch = ManagedInstanceCacheTypeExpression.Match(body);
        var releaseStrategyMatch = ReleaseStrategyExpression.Match(body);

        var cppTypeName = ExtractTemplateArgFromCursorType(decl, isReturn: false);
        if (string.IsNullOrWhiteSpace(cppTypeName))
        {
            return;
        }

        cppTypeName = NormalizeTypeName(cppTypeName);

        result[cppTypeName] = new MarshallerBodyConfig(
            AttributeBody: body,
            ExposedMembers: exposedMembers,
            TemplateParameterNames: ExtractTemplateParameterNames(decl),
            ManagedMarshaller: managedMarshallerMatch.Success ? managedMarshallerMatch.Groups["value"].Value : null,
            ManagedInstanceCacheFactoryExpression: managedInstanceCacheFactoryMatch.Success ? managedInstanceCacheFactoryMatch.Groups["value"].Value : null,
            ManagedInstanceCacheTypeName: managedInstanceCacheTypeMatch.Success ? managedInstanceCacheTypeMatch.Groups["value"].Value : null,
            EmitDefaultNativeReleaseOverride: !releaseStrategyMatch.Success ||
                !string.Equals(releaseStrategyMatch.Groups["value"].Value, "base", StringComparison.OrdinalIgnoreCase))
        {
            ManagedWrapperProviderKind = GetManagedWrapperProviderKind(body),
            ManagedWrapperProviderNamespace = GetManagedWrapperProviderNamespace(body),
            ManagedWrapperCopyable = GetManagedWrapperCopyable(body),
            ConstrainsTemplateArguments = decl is ClassTemplateSpecializationDecl { TemplateArgs: { Count: > 0 } configured } &&
                configured[0].Kind == CXTemplateArgumentKind.CXTemplateArgumentKind_Type &&
                CreateCppTypePattern(configured[0].AsType) is { } pattern &&
                HasRepeatedTemplateParameter(pattern, new HashSet<(uint Depth, uint Index)>())
        };
    }

    internal static bool HasRepeatedTemplateParameter(CppTypePattern pattern, ISet<(uint Depth, uint Index)> seen) =>
        pattern.Kind is CppTypePatternKind.TemplateParameter or CppTypePatternKind.NonTypeTemplateParameter
            ? !seen.Add((pattern.TemplateParameterDepth, pattern.TemplateParameterIndex))
            : pattern.Pointee is { } pointee && HasRepeatedTemplateParameter(pointee, seen) ||
                (pattern.TemplateArguments ?? []).Any(argument => HasRepeatedTemplateParameter(argument, seen));

    private static string? GetManagedWrapperProviderNamespace(string attributeBody)
    {
        ArgumentNullException.ThrowIfNull(attributeBody);
        return ManagedWrapperProviderNamespaceInAttributeExpression.Match(attributeBody) is { Success: true } match
            ? match.Groups["value"].Value
            : null;
    }

    private static bool? GetManagedWrapperCopyable(string attributeBody)
    {
        ArgumentNullException.ThrowIfNull(attributeBody);
        return ManagedWrapperCopyableInAttributeExpression.Match(attributeBody) is { Success: true } match &&
            bool.TryParse(match.Groups["value"].Value, out var value)
            ? value
            : null;
    }

    private static List<ExposedMemberInfo> ExtractExposedMembersFromDecl(
        Decl structDecl,
        IReadOnlyDictionary<string, Decl> declIndex)
    {
        var members = new Dictionary<string, ExposedMemberInfo>(StringComparer.Ordinal);
        var visitedDecls = new HashSet<string>(StringComparer.Ordinal);
        CollectExposedMembers(structDecl, declIndex, members, visitedDecls);
        return members.Values.ToList();
    }

    private static void CollectExposedMembers(
        Decl decl,
        IReadOnlyDictionary<string, Decl> declIndex,
        IDictionary<string, ExposedMemberInfo> members,
        ISet<string> visitedDecls)
    {
        var recordDecl = ResolveRecordDecl(decl);
        if (recordDecl is null)
        {
            return;
        }

        var visitedKey = GetDeclLocationKey(recordDecl.Handle);
        if (visitedKey is not null && !visitedDecls.Add(visitedKey))
        {
            return;
        }

        if (recordDecl is CXXRecordDecl cxxRecordDecl)
        {
            foreach (var baseSpec in cxxRecordDecl.Bases)
            {
                if (!TryResolveBaseDecl(baseSpec, declIndex, out var baseDecl))
                {
                    continue;
                }

                CollectExposedMembers(baseDecl, declIndex, members, visitedDecls);
            }
        }

        foreach (var child in recordDecl.Decls)
        {
            if (child is not CXXMethodDecl methodDecl || !TryExtractExposedMember(methodDecl, out var member))
            {
                continue;
            }

            members[GetExposedMemberSignature(member)] = member;
        }
    }

    private static string GetExposedMemberSignature(ExposedMemberInfo member)
    {
        var effectiveParameters = member.Parameters.Skip(1).Select(static parameter => parameter.CppType);
        return $"{member.MethodName}({string.Join(',', effectiveParameters)})::{member.IsConst}";
    }

    private static bool TryExtractExposedMember(CXXMethodDecl methodDecl, out ExposedMemberInfo member)
    {
        var exposeMatch = SharpCppAttributeScanner.Scan(GetMethodAttributeSearchText(methodDecl))
            .FirstOrDefault(static match => string.Equals(match.Name, "expose_member", StringComparison.Ordinal));
        if (exposeMatch is null)
        {
            member = null!;
            return false;
        }

        var attrBody = exposeMatch.Body;
        var shape = ShapeExpression.Match(attrBody) is { Success: true } shapeMatch
            ? shapeMatch.Groups["value"].Value
            : "method";
        var exposed = ExposedExpression.Match(attrBody) is { Success: true } exposedMatch
            ? string.Equals(exposedMatch.Groups["value"].Value, "true", StringComparison.OrdinalIgnoreCase)
            : true;

        var methodName = methodDecl.Name ?? methodDecl.Spelling.ToString();
        var parameters = new List<(string Name, string CppType)>();
        foreach (var param in methodDecl.Parameters)
        {
            var paramName = param.Name ?? param.Spelling.ToString();
            parameters.Add((paramName, NormalizeTypeName(param.Type.AsString)));
        }

        member = new ExposedMemberInfo(
            MethodName: methodName,
            CppReturnType: NormalizeTypeName(methodDecl.ReturnType.AsString),
            IsConst: methodDecl.IsConst,
            Shape: shape,
            Exposed: exposed,
            Parameters: parameters.AsReadOnly());
        return true;
    }

    private static string GetMethodAttributeSearchText(CXXMethodDecl methodDecl)
    {
        var sourceText = GetSourceText(methodDecl.Handle.TranslationUnit, methodDecl.Extent);
        if (SharpCppAttributeScanner.Scan(sourceText).Any(static match => string.Equals(match.Name, "expose_member", StringComparison.Ordinal)))
        {
            return sourceText;
        }

        methodDecl.Location.GetFileLocation(out var file, out var line, out _, out _);
        var filePath = file.Name.CString;
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return sourceText;
        }

        var lines = File.ReadAllLines(filePath);
        var currentLineIndex = Math.Max(0, (int)line - 1);
        if (currentLineIndex >= lines.Length)
        {
            return sourceText;
        }

        var collectedLines = new List<string>();
        var sawExposeAttribute = false;
        for (var index = currentLineIndex - 1; index >= 0; index--)
        {
            var trimmed = lines[index].Trim();
            if (!sawExposeAttribute)
            {
                if (trimmed.Contains("[[sharpcpp::", StringComparison.Ordinal) || trimmed.Contains("]]", StringComparison.Ordinal))
                {
                    collectedLines.Insert(0, lines[index]);
                    sawExposeAttribute = true;
                    if (trimmed.Contains("[[sharpcpp::", StringComparison.Ordinal))
                    {
                        break;
                    }

                    continue;
                }

                break;
            }

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                break;
            }

            collectedLines.Insert(0, lines[index]);
            if (trimmed.Contains("[[sharpcpp::", StringComparison.Ordinal))
            {
                break;
            }
        }

        if (collectedLines.Count == 0)
        {
            return sourceText;
        }

        collectedLines.Add(sourceText);
        return string.Join(Environment.NewLine, collectedLines);
    }

    private static IReadOnlyDictionary<string, Decl> BuildRecordLikeDeclIndex(Decl rootDecl)
    {
        var declIndex = new Dictionary<string, Decl>(StringComparer.Ordinal);
        IndexRecordLikeDecls(rootDecl, declIndex);
        return declIndex;
    }

    private static void IndexRecordLikeDecls(Decl decl, IDictionary<string, Decl> declIndex)
    {
        foreach (var child in EnumerateDescendantDecls(decl))
        {
            switch (child)
            {
                case ClassTemplateDecl classTemplateDecl:
                    AddDeclIndexEntry(classTemplateDecl, declIndex);
                    if (classTemplateDecl.TemplatedDecl is not null)
                    {
                        AddDeclIndexEntry(classTemplateDecl.TemplatedDecl, declIndex);
                    }
                    break;

                case RecordDecl:
                    AddDeclIndexEntry(child, declIndex);
                    break;
            }
        }
    }

    private static unsafe IEnumerable<Decl> EnumerateDescendantDecls(Cursor root)
    {
        var result = new List<Decl>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var translationUnit = TranslationUnit.GetOrCreate(root.Handle.TranslationUnit);

        root.Handle.VisitChildren((cursor, _, _) =>
        {
            if (!IsRecordLikeCursor(cursor))
            {
                return CXChildVisitResult.CXChildVisit_Recurse;
            }

            if (!TryCreateDecl(translationUnit, cursor, out var decl))
            {
                return CXChildVisitResult.CXChildVisit_Recurse;
            }

            var key = GetDeclLocationKey(decl.Handle) ?? decl.Handle.GetHashCode().ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (seen.Add(key))
            {
                result.Add(decl);
            }

            return CXChildVisitResult.CXChildVisit_Recurse;
        }, default);

        return result;
    }

    private static bool IsRecordLikeCursor(CXCursor cursor)
    {
        return cursor.Kind is CXCursorKind.CXCursor_StructDecl or
            CXCursorKind.CXCursor_ClassDecl or
            CXCursorKind.CXCursor_ClassTemplate or
            CXCursorKind.CXCursor_ClassTemplatePartialSpecialization;
    }

    private static bool TryCreateDecl(TranslationUnit translationUnit, CXCursor cursor, out Decl decl)
    {
        try
        {
            decl = (Decl)GetOrCreateCursorMethod.Invoke(translationUnit, [cursor])!;
            return true;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is ArgumentOutOfRangeException or InvalidOperationException)
        {
            decl = null!;
            return false;
        }
    }

    private static bool IsPotentialMarshallerDeclaration(Decl decl)
    {
        if (decl is not NamedDecl namedDecl)
        {
            return false;
        }

        var name = namedDecl.Name ?? namedDecl.Spelling.ToString();
        return string.Equals(name, "Marshaller", StringComparison.Ordinal) ||
            string.Equals(name, "ReturnMarshaller", StringComparison.Ordinal);
    }

    private static void AddDeclIndexEntry(Decl decl, IDictionary<string, Decl> declIndex)
    {
        var locationKey = GetDeclLocationKey(decl.Handle);
        if (locationKey is not null)
        {
            declIndex[locationKey] = decl;
        }
    }

    private static bool TryResolveBaseDecl(
        CXXBaseSpecifier baseSpec,
        IReadOnlyDictionary<string, Decl> declIndex,
        out Decl baseDecl)
    {
        var locationKey = GetDeclLocationKey(baseSpec.Type.Handle.Declaration);
        if (locationKey is not null && declIndex.TryGetValue(locationKey, out baseDecl!))
        {
            return true;
        }

        baseDecl = null!;
        return false;
    }

    private static RecordDecl? ResolveRecordDecl(Decl decl)
    {
        return decl switch
        {
            ClassTemplateDecl classTemplateDecl => classTemplateDecl.TemplatedDecl as RecordDecl,
            _ => decl as RecordDecl
        };
    }

    private static string? GetDeclLocationKey(CXCursor cursor)
    {
        cursor.Location.GetFileLocation(out var file, out var line, out var column, out _);
        var filePath = file.Name.CString;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        return $"{Path.GetFullPath(filePath)}:{line}:{column}";
    }

    private static IReadOnlyList<string> ExtractTemplateParameterNames(Decl decl)
    {
        return GetTemplateParameters(decl)
            .Select(static parameter => parameter.Name ?? parameter.Spelling.ToString())
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .ToArray();
    }

    private static IReadOnlyList<NamedDecl> GetTemplateParameters(Decl decl)
    {
        return decl switch
        {
            ClassTemplatePartialSpecializationDecl partialSpecialization => partialSpecialization.TemplateParameters,
            ClassTemplateDecl classTemplate => classTemplate.TemplateParameters,
            _ => Array.Empty<NamedDecl>()
        };
    }

    private static IReadOnlyList<int> ExtractVisibleTemplateParameterIndices(string publicTypeName, int parameterCount)
    {
        if (parameterCount == 0)
        {
            return Array.Empty<int>();
        }

        var usedIndices = new HashSet<int>();
        for (var index = 0; index < publicTypeName.Length; index++)
        {
            if (publicTypeName[index] != '{')
            {
                continue;
            }

            var closeIndex = publicTypeName.IndexOf('}', index + 1);
            if (closeIndex <= index + 1)
            {
                continue;
            }

            if (int.TryParse(publicTypeName[(index + 1)..closeIndex], out var placeholderIndex) &&
                placeholderIndex >= 0 &&
                placeholderIndex < parameterCount)
            {
                usedIndices.Add(placeholderIndex);
            }

            index = closeIndex;
        }

        return usedIndices.Count == 0
            ? Enumerable.Range(0, parameterCount).ToArray()
            : usedIndices.OrderBy(static index => index).ToArray();
    }

    private static readonly ILogger Logger = SharpCppLog.For(nameof(MarshallerConfigExtractor));

    // ─── Utilities ───────────────────────────────────────────────────────────

    private static string GetSourceText(CXTranslationUnit translationUnit, CXSourceRange sourceRange)
    {
        var tokens = translationUnit.Tokenize(sourceRange);
        try
        {
            var builder = new System.Text.StringBuilder();
            var first = true;
            foreach (var token in tokens)
            {
                if (!first)
                {
                    builder.Append(' ');
                }
                builder.Append(token.GetSpelling(translationUnit).ToString());
                first = false;
            }

            return builder.ToString();
        }
        finally
        {
            translationUnit.DisposeTokens(tokens);
        }
    }

    private static string? ExtractTemplateArgFromCursorType(Decl decl, bool isReturn)
    {
        _ = isReturn;
        return decl is ClassTemplateSpecializationDecl { TemplateArgs: { Count: > 0 } arguments } &&
            arguments[0].Kind == CXTemplateArgumentKind.CXTemplateArgumentKind_Type
                ? FormatConfiguredCppType(arguments[0].AsType, GetTemplateParameters(decl))
                : null;
    }

    internal static CppTypePattern? CreateCppTypePattern(
        ClangSharp.Type type,
        IReadOnlyDictionary<(uint Depth, uint Index), ClangSharp.Type>? substitutions = null)
    {
        CppTypePattern? pattern = type switch
        {
            LValueReferenceType reference => CreateIndirectionPattern(
                CppTypePatternKind.LValueReference,
                reference.PointeeType,
                substitutions),
            RValueReferenceType reference => CreateIndirectionPattern(
                CppTypePatternKind.RValueReference,
                reference.PointeeType,
                substitutions),
            PointerType pointer => CreateIndirectionPattern(
                CppTypePatternKind.Pointer,
                pointer.PointeeType,
                substitutions),
            ElaboratedType elaborated => CreateCppTypePattern(elaborated.NamedType, substitutions),
            ParenType paren => CreateCppTypePattern(paren.InnerType, substitutions),
            TypedefType typedef => CreateCppTypePattern(typedef.Desugar, substitutions),
            SubstTemplateTypeParmType substitution => CreateCppTypePattern(substitution.ReplacementType, substitutions),
            TemplateTypeParmType parameter when substitutions?.TryGetValue(
                (parameter.Depth, parameter.Index), out var replacement) == true =>
                    CreateCppTypePattern(replacement, substitutions),
            TemplateTypeParmType parameter => new(
                CppTypePatternKind.TemplateParameter,
                TemplateParameterDepth: parameter.Depth,
                TemplateParameterIndex: parameter.Index),
            TemplateSpecializationType specialization => CreateDeclaredPattern(
                specialization.TemplateName.AsTemplateDecl,
                specialization.Args,
                substitutions),
            RecordType { Decl: ClassTemplateSpecializationDecl specialization } => CreateDeclaredPattern(
                GetSpecializedTemplateDecl(specialization) ?? specialization,
                specialization.TemplateArgs,
                substitutions),
            RecordType record => CreateDeclaredPattern(record.Decl, [], substitutions),
            EnumType @enum => CreateDeclaredPattern(@enum.Decl, [], substitutions),
            BuiltinType => new(
                CppTypePatternKind.Builtin,
                BuiltinKind: type.Handle.kind),
            _ => null
        };

        if (pattern is not null && type.IsLocalConstQualified)
        {
            pattern = pattern with { IsConst = true };
        }
        return pattern;
    }

    internal static bool ReturnTypeOwnsHandle(
        CppTypePattern? actualType,
        IEnumerable<ManagedTypeOverride> managedTypeOverrides)
    {
        if (actualType is null)
        {
            return false;
        }

        return managedTypeOverrides.Any(managedTypeOverride =>
            string.Equals(managedTypeOverride.ReturnOwnership, "owning", StringComparison.OrdinalIgnoreCase) &&
            managedTypeOverride.NativeTypePattern is { } configuredType &&
            MatchesCppTypePattern(configuredType, actualType, new Dictionary<(uint Depth, uint Index), CppTypePattern>()));
    }

    internal static bool MatchesManagedTypeOverride(
        ManagedTypeOverride managedTypeOverride,
        CXType actualType)
    {
        ArgumentNullException.ThrowIfNull(managedTypeOverride);

        if (managedTypeOverride.NativeTypePattern is not { } configuredType)
            return true;

        var actualKind = actualType.CanonicalType.kind;
        var indirectionMatches = configuredType.Kind switch
        {
            CppTypePatternKind.Pointer => actualKind == CXTypeKind.CXType_Pointer,
            CppTypePatternKind.LValueReference => actualKind == CXTypeKind.CXType_LValueReference,
            CppTypePatternKind.RValueReference => actualKind == CXTypeKind.CXType_RValueReference,
            _ => actualKind is not CXTypeKind.CXType_Pointer and
                not CXTypeKind.CXType_LValueReference and
                not CXTypeKind.CXType_RValueReference
        };
        return indirectionMatches &&
            BindsTemplateParametersConsistently(
                configuredType,
                actualType.CanonicalType,
                new Dictionary<(uint Depth, uint Index), CXType>(),
                new Dictionary<(uint Depth, uint Index), long>());
    }

    // The override is found by name; a template parameter repeated in the configured
    // pattern (`twin<T, T>`) must still bind the same type everywhere, as C++ partial
    // specialization matching requires; a repeated non-type parameter (`triple<T, N, N>`)
    // must bind the same value everywhere.
    private static bool BindsTemplateParametersConsistently(
        CppTypePattern configured,
        CXType actual,
        IDictionary<(uint Depth, uint Index), CXType> bindings,
        IDictionary<(uint Depth, uint Index), long> valueBindings)
    {
        switch (configured.Kind)
        {
            case CppTypePatternKind.TemplateParameter:
                var key = (configured.TemplateParameterDepth, configured.TemplateParameterIndex);
                if (bindings.TryGetValue(key, out var bound))
                    return clang.equalTypes(bound, actual) != 0;
                bindings[key] = actual;
                return true;

            case CppTypePatternKind.Pointer or CppTypePatternKind.LValueReference or CppTypePatternKind.RValueReference:
                return configured.Pointee is null ||
                    BindsTemplateParametersConsistently(configured.Pointee, actual.PointeeType.CanonicalType, bindings, valueBindings);

            case CppTypePatternKind.Declared when configured.TemplateArguments is { Count: > 0 } arguments &&
                actual.NumTemplateArguments == arguments.Count:
                for (var index = 0; index < arguments.Count; ++index)
                {
                    using var argument = actual.GetTemplateArgument(checked((uint)index));
                    if (argument.AsType.kind != CXTypeKind.CXType_Invalid)
                    {
                        if (!BindsTemplateParametersConsistently(
                            arguments[index], argument.AsType.CanonicalType, bindings, valueBindings))
                        {
                            return false;
                        }
                    }
                    else if (argument.IntegralType.kind != CXTypeKind.CXType_Invalid &&
                        !BindsValueConsistently(arguments[index], argument.AsIntegral, valueBindings))
                    {
                        return false;
                    }
                }
                return true;

            // A type never stands for a constant.
            case CppTypePatternKind.NonTypeTemplateParameter or CppTypePatternKind.IntegralValue:
                return false;

            default:
                return true;
        }
    }

    private static bool BindsValueConsistently(
        CppTypePattern configured,
        long actual,
        IDictionary<(uint Depth, uint Index), long> valueBindings)
    {
        switch (configured.Kind)
        {
            case CppTypePatternKind.NonTypeTemplateParameter:
                var key = (configured.TemplateParameterDepth, configured.TemplateParameterIndex);
                if (valueBindings.TryGetValue(key, out var bound))
                    return bound == actual;
                valueBindings[key] = actual;
                return true;

            case CppTypePatternKind.IntegralValue:
                return configured.IntegralValue == actual;

            // A constant never stands for a type.
            default:
                return false;
        }
    }

    private static CppTypePattern? CreateIndirectionPattern(
        CppTypePatternKind kind,
        ClangSharp.Type pointeeType,
        IReadOnlyDictionary<(uint Depth, uint Index), ClangSharp.Type>? substitutions)
    {
        return CreateCppTypePattern(pointeeType, substitutions) is { } pointee
            ? new CppTypePattern(kind, Pointee: pointee)
            : null;
    }

    private static CppTypePattern? CreateDeclaredPattern(
        NamedDecl declaration,
        IReadOnlyList<TemplateArgument> templateArguments,
        IReadOnlyDictionary<(uint Depth, uint Index), ClangSharp.Type>? substitutions)
    {
        var declarationId = declaration.Handle.Usr.CString;
        if (string.IsNullOrWhiteSpace(declarationId))
        {
            return null;
        }

        var arguments = new List<CppTypePattern>(templateArguments.Count);
        foreach (var templateArgument in templateArguments)
        {
            if ((templateArgument.Kind == CXTemplateArgumentKind.CXTemplateArgumentKind_Type
                    ? CreateCppTypePattern(templateArgument.AsType, substitutions)
                    : CreateNonTypeArgumentPattern(templateArgument)) is not { } argument)
            {
                return null;
            }
            arguments.Add(argument);
        }

        return new(
            CppTypePatternKind.Declared,
            DeclarationId: declarationId,
            TemplateArguments: arguments);
    }

    // Only a constant or a plain non-type parameter reference is modelled; packs,
    // declarations, template-template and nullptr arguments give no pattern.
    private static CppTypePattern? CreateNonTypeArgumentPattern(TemplateArgument templateArgument) =>
        templateArgument.Kind switch
        {
            CXTemplateArgumentKind.CXTemplateArgumentKind_Integral =>
                new CppTypePattern(CppTypePatternKind.IntegralValue, IntegralValue: templateArgument.AsIntegral),
            CXTemplateArgumentKind.CXTemplateArgumentKind_Expression when !templateArgument.IsPackExpansion =>
                CreateNonTypeExpressionPattern(templateArgument.AsExpr),
            _ => null
        };

    private static CppTypePattern? CreateNonTypeExpressionPattern(Expr expression) =>
        expression.IgnoreImplicit.IgnoreParens switch
        {
            DeclRefExpr { Decl: NonTypeTemplateParmDecl parameter } => new CppTypePattern(
                CppTypePatternKind.NonTypeTemplateParameter,
                TemplateParameterDepth: parameter.Depth,
                TemplateParameterIndex: parameter.Index),
            IntegerLiteral literal => new CppTypePattern(CppTypePatternKind.IntegralValue, IntegralValue: literal.Value),
            CXXBoolLiteralExpr literal => new CppTypePattern(CppTypePatternKind.IntegralValue, IntegralValue: literal.Value ? 1 : 0),
            SubstNonTypeTemplateParmExpr substitution => CreateNonTypeExpressionPattern(substitution.Replacement),
            _ => null
        };

    private static bool MatchesCppTypePattern(
        CppTypePattern configured,
        CppTypePattern actual,
        IDictionary<(uint Depth, uint Index), CppTypePattern> parameterBindings)
    {
        if (configured.Kind is CppTypePatternKind.TemplateParameter or CppTypePatternKind.NonTypeTemplateParameter)
        {
            // A type parameter binds a type and a non-type parameter a value; never each other.
            if ((configured.Kind == CppTypePatternKind.NonTypeTemplateParameter) !=
                (actual.Kind is CppTypePatternKind.IntegralValue or CppTypePatternKind.NonTypeTemplateParameter))
            {
                return false;
            }
            var key = (configured.TemplateParameterDepth, configured.TemplateParameterIndex);
            if (parameterBindings.TryGetValue(key, out var bound))
            {
                return CppTypePatternsEqual(bound, actual);
            }
            parameterBindings[key] = actual;
            return true;
        }

        if (configured.Kind != actual.Kind || configured.IsConst != actual.IsConst)
        {
            return false;
        }

        return configured.Kind switch
        {
            CppTypePatternKind.Builtin => configured.BuiltinKind == actual.BuiltinKind,
            CppTypePatternKind.IntegralValue => configured.IntegralValue == actual.IntegralValue,
            CppTypePatternKind.Declared =>
                string.Equals(configured.DeclarationId, actual.DeclarationId, StringComparison.Ordinal) &&
                MatchCppTypePatternArguments(configured, actual, parameterBindings),
            CppTypePatternKind.Pointer or
            CppTypePatternKind.LValueReference or
            CppTypePatternKind.RValueReference =>
                configured.Pointee is not null && actual.Pointee is not null &&
                MatchesCppTypePattern(configured.Pointee, actual.Pointee, parameterBindings),
            _ => false
        };
    }

    private static bool MatchCppTypePatternArguments(
        CppTypePattern configured,
        CppTypePattern actual,
        IDictionary<(uint Depth, uint Index), CppTypePattern> parameterBindings)
    {
        var configuredArguments = configured.TemplateArguments ?? [];
        var actualArguments = actual.TemplateArguments ?? [];
        return configuredArguments.Count == actualArguments.Count &&
            configuredArguments.Zip(actualArguments).All(pair =>
                MatchesCppTypePattern(pair.First, pair.Second, parameterBindings));
    }

    private static bool CppTypePatternsEqual(CppTypePattern left, CppTypePattern right)
    {
        if (left.Kind != right.Kind ||
            left.IsConst != right.IsConst ||
            left.BuiltinKind != right.BuiltinKind ||
            left.IntegralValue != right.IntegralValue ||
            left.Kind == CppTypePatternKind.NonTypeTemplateParameter &&
                (left.TemplateParameterDepth != right.TemplateParameterDepth ||
                    left.TemplateParameterIndex != right.TemplateParameterIndex) ||
            !string.Equals(left.DeclarationId, right.DeclarationId, StringComparison.Ordinal))
        {
            return false;
        }

        if (left.Pointee is not null || right.Pointee is not null)
        {
            return left.Pointee is not null && right.Pointee is not null &&
                CppTypePatternsEqual(left.Pointee, right.Pointee);
        }

        var leftArguments = left.TemplateArguments ?? [];
        var rightArguments = right.TemplateArguments ?? [];
        return leftArguments.Count == rightArguments.Count &&
            leftArguments.Zip(rightArguments).All(static pair => CppTypePatternsEqual(pair.First, pair.Second));
    }

    private static string? FormatConfiguredCppType(
        ClangSharp.Type type,
        IReadOnlyList<NamedDecl> templateParameters)
    {
        string? rendered = type switch
        {
            LValueReferenceType referenceType => AppendIndirection(
                FormatConfiguredCppType(referenceType.PointeeType, templateParameters), "&"),
            RValueReferenceType referenceType => AppendIndirection(
                FormatConfiguredCppType(referenceType.PointeeType, templateParameters), "&&"),
            PointerType pointerType => AppendIndirection(
                FormatConfiguredCppType(pointerType.PointeeType, templateParameters),
                pointerType.IsLocalConstQualified ? "* const" : "*"),
            ElaboratedType elaboratedType => FormatConfiguredCppType(elaboratedType.NamedType, templateParameters),
            ParenType parenType => FormatConfiguredCppType(parenType.InnerType, templateParameters),
            TypedefType typedefType => FormatConfiguredCppType(typedefType.Desugar, templateParameters),
            SubstTemplateTypeParmType substitutionType => FormatConfiguredCppType(substitutionType.ReplacementType, templateParameters),
            TemplateTypeParmType parameterType => GetTemplateParameterName(parameterType, templateParameters),
            TemplateSpecializationType specializationType => FormatTemplateSpecialization(
                specializationType.TemplateName.AsTemplateDecl,
                specializationType.Args,
                templateParameters),
            RecordType { Decl: ClassTemplateSpecializationDecl specialization } => FormatTemplateSpecialization(
                GetSpecializedTemplateDecl(specialization) ?? specialization,
                specialization.TemplateArgs,
                templateParameters),
            RecordType recordType => GetCppQualifiedName(recordType.Decl),
            EnumType enumType => GetCppQualifiedName(enumType.Decl),
            BuiltinType => type.AsString,
            _ => null
        };

        if (string.IsNullOrWhiteSpace(rendered))
        {
            return null;
        }

        return type.IsLocalConstQualified &&
            type is not PointerType &&
            !rendered.StartsWith("const ", StringComparison.Ordinal)
            ? "const " + rendered
            : rendered;
    }

    private static string? FormatTemplateSpecialization(
        NamedDecl? templateDeclaration,
        IReadOnlyList<TemplateArgument> arguments,
        IReadOnlyList<NamedDecl> templateParameters)
    {
        if (templateDeclaration is null)
        {
            return null;
        }

        var renderedArguments = new string[arguments.Count];
        for (var index = 0; index < arguments.Count; index++)
        {
            var renderedArgument = FormatTemplateArgument(arguments[index], templateParameters);
            if (string.IsNullOrWhiteSpace(renderedArgument))
            {
                return null;
            }

            renderedArguments[index] = renderedArgument;
        }

        return $"{GetCppQualifiedName(templateDeclaration)}<{string.Join(", ", renderedArguments)}>";
    }

    private static string? FormatTemplateArgument(
        TemplateArgument argument,
        IReadOnlyList<NamedDecl> templateParameters)
    {
        return argument.Kind switch
        {
            CXTemplateArgumentKind.CXTemplateArgumentKind_Type => FormatConfiguredCppType(argument.AsType, templateParameters),
            CXTemplateArgumentKind.CXTemplateArgumentKind_Declaration => GetDeclarationName(argument.AsDecl),
            CXTemplateArgumentKind.CXTemplateArgumentKind_Integral =>
                argument.AsIntegral.ToString(System.Globalization.CultureInfo.InvariantCulture),
            CXTemplateArgumentKind.CXTemplateArgumentKind_Template or
            CXTemplateArgumentKind.CXTemplateArgumentKind_TemplateExpansion =>
                GetDeclarationName(argument.AsTemplateOrTemplatePattern.AsTemplateDecl),
            CXTemplateArgumentKind.CXTemplateArgumentKind_Expression =>
                FormatTemplateArgumentExpression(argument.AsExpr, templateParameters),
            CXTemplateArgumentKind.CXTemplateArgumentKind_NullPtr => "nullptr",
            _ => null
        };
    }

    private static string? FormatTemplateArgumentExpression(
        Expr expression,
        IReadOnlyList<NamedDecl> templateParameters)
    {
        return expression.IgnoreImplicit.IgnoreParens switch
        {
            DeclRefExpr { Decl: NonTypeTemplateParmDecl parameter } =>
                GetTemplateParameterName(parameter, templateParameters),
            DeclRefExpr declarationReference => GetDeclarationName(declarationReference.Decl),
            IntegerLiteral integerLiteral => integerLiteral.ValueString,
            _ => null
        };
    }

    private static string? GetDeclarationName(NamedDecl? declaration)
    {
        return declaration is null
            ? null
            : declaration.Name ?? declaration.Spelling.ToString();
    }

    private static string? GetTemplateParameterName(
        TemplateTypeParmType parameterType,
        IReadOnlyList<NamedDecl> templateParameters)
    {
        return GetTemplateParameterName(parameterType.Index, templateParameters) ??
            GetDeclarationName(parameterType.Decl);
    }

    private static string? GetTemplateParameterName(
        NonTypeTemplateParmDecl parameter,
        IReadOnlyList<NamedDecl> templateParameters)
    {
        return GetTemplateParameterName(parameter.Index, templateParameters) ??
            GetDeclarationName(parameter);
    }

    private static string? GetTemplateParameterName(
        uint parameterIndex,
        IReadOnlyList<NamedDecl> templateParameters)
    {
        return parameterIndex < templateParameters.Count
            ? GetDeclarationName(templateParameters[(int)parameterIndex])
            : null;
    }

    private static string GetCppQualifiedName(Decl declaration)
    {
        var names = new List<string>();
        var current = declaration.SemanticParentCursor;
        while (current is NamespaceDecl or RecordDecl)
        {
            if (!(current is NamespaceDecl namespaceDeclaration &&
                  clang.Cursor_isInlineNamespace(namespaceDeclaration.Handle) != 0) &&
                current is NamedDecl parentDeclaration &&
                GetDeclarationName(parentDeclaration) is { Length: > 0 } parentName)
            {
                names.Add(parentName);
            }

            current = current.SemanticParentCursor;
        }

        names.Reverse();
        if (declaration is NamedDecl namedDeclaration &&
            GetDeclarationName(namedDeclaration) is { Length: > 0 } declarationName)
        {
            names.Add(declarationName);
        }

        return string.Join("::", names);
    }

    private static string? AppendIndirection(string? typeName, string indirection)
    {
        return string.IsNullOrWhiteSpace(typeName) ? null : typeName + indirection;
    }

    private static bool TryGetMarshallerTemplateKind(Decl decl, out bool isReturn)
    {
        var templateName = decl is ClassTemplateSpecializationDecl specialization
            ? GetSpecializedTemplateDecl(specialization)?.Name
            : null;
        isReturn = string.Equals(templateName, "ReturnMarshaller", StringComparison.Ordinal);
        return isReturn || string.Equals(templateName, "Marshaller", StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolve the AST declaration selected by a class-template specialization without
    /// assuming it is the primary <see cref="ClassTemplateDecl"/>. Clang may point a
    /// concrete specialization at the matching partial specialization, while
    /// ClangSharp's <see cref="ClassTemplateSpecializationDecl.SpecializedTemplate"/>
    /// property always casts that cursor to <see cref="ClassTemplateDecl"/>.
    /// </summary>
    internal static NamedDecl? GetSpecializedTemplateDecl(ClassTemplateSpecializationDecl specialization)
    {
        var cursor = specialization.Handle.SpecializedCursorTemplate;
        if (cursor.IsNull)
        {
            return null;
        }

        var translationUnit = TranslationUnit.GetOrCreate(specialization.Handle.TranslationUnit);
        return TryCreateDecl(translationUnit, cursor, out var declaration)
            ? declaration as NamedDecl
            : null;
    }

    internal static string NormalizeTypeName(string spelling)
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

    internal static string? GetManagedNamespaceFromTypeName(string managedTypeName)
    {
        var genericStart = managedTypeName.IndexOf('<');
        var typeHead = genericStart >= 0 ? managedTypeName[..genericStart] : managedTypeName;
        typeHead = typeHead.StartsWith("global::", StringComparison.Ordinal)
            ? typeHead["global::".Length..]
            : typeHead;
        var lastDot = typeHead.LastIndexOf('.');
        return lastDot > 0 ? typeHead[..lastDot] : null;
    }
}
