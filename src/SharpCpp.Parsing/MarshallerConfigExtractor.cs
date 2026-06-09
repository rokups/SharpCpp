using System.Text.RegularExpressions;
using ClangSharp;
using ClangSharp.Interop;

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
    // Extract [[sharpcpp::managed_type(body)]] from a declaration's source text.
    // \s* handles spacing between tokens (GetSourceText adds spaces).
    private static readonly Regex ManagedTypeAttributeExpression = new(
        @"\[\s*\[\s*sharpcpp\s*::\s*managed_type\s*\((?<body>[^)]*)\)\s*\]\s*\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Extract [[sharpcpp::expose_member(attr)]] from a member function cursor extent text.
    private static readonly Regex ExposeMemberAttributeExpression = new(
        @"\[\s*\[\s*sharpcpp\s*::\s*expose_member\s*\(\s*(?<attr>[^)]*?)\s*\)\s*\]\s*\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Key=value extractors (attribute body parsing only — NOT C++ code parsing).
    private static readonly Regex PublicTypeExpression = new(
        @"public_type\s*=\s*\""(?<value>[^\""\\]*(?:\\.[^\""\\]*)*)\""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex UndefaultableExpression = new(
        @"undefaultable\s*=\s*(?<value>true|false)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex MarshallerTypeInAttributeExpression = new(
        @"marshaller_type\s*=\s*\""(?<value>[^\""\\]*(?:\\.[^\""\\]*)*)\""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ManagedMarshallerInAttributeExpression = new(
        @"managed_marshaller\s*=\s*\""(?<value>[^\""\\]*(?:\\.[^\""\\]*)*)\""",
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
        public bool Undefaultable { get; init; }
        public string? MarshallerTypeName { get; init; }
        public string? ManagedMarshaller { get; init; }
    }

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
        IReadOnlyList<ExposedMemberInfo> ExposedMembers);

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
            WalkForMarshallerDeclarations(rootDecl, result);
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
            WalkForMarshallerBodies(rootDecl, result);
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
            using var translationUnit = CXTranslationUnit.Parse(
                index,
                tempCppPath,
                clangArgs.ToArray(),
                Array.Empty<CXUnsavedFile>(),
                CXTranslationUnit_Flags.CXTranslationUnit_None);

            if (translationUnit.NumDiagnostics > 0)
            {
                for (uint diagIndex = 0; diagIndex < translationUnit.NumDiagnostics; diagIndex++)
                {
                    using var diagnostic = translationUnit.GetDiagnostic(diagIndex);
                    if (diagnostic.Severity == CXDiagnosticSeverity.CXDiagnostic_Error ||
                        diagnostic.Severity == CXDiagnosticSeverity.CXDiagnostic_Fatal)
                    {
                        Console.Error.WriteLine(
                            $"SharpCpp warning: marshaller config parse error: " +
                            $"{diagnostic.Format(CXDiagnostic.DefaultDisplayOptions).ToString()}");
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
        Dictionary<string, ManagedTypeOverride> result)
    {
        foreach (var child in decl.Decls)
        {
            if (child is RecordDecl or ClassTemplateDecl or ClassTemplatePartialSpecializationDecl)
            {
                var spelling = child is NamedDecl named ? named.Name ?? named.Spelling.ToString() : child.Spelling.ToString();
                if (string.Equals(spelling, "Marshaller", StringComparison.Ordinal))
                {
                    ProcessMarshallerDeclaration(child, isReturn: false, result);
                }
                else if (string.Equals(spelling, "ReturnMarshaller", StringComparison.Ordinal))
                {
                    ProcessMarshallerDeclaration(child, isReturn: true, result);
                }
            }

            if (child is NamespaceDecl)
            {
                WalkForMarshallerDeclarations(child, result);
            }
        }
    }

    private static void ProcessMarshallerDeclaration(
        Decl decl,
        bool isReturn,
        Dictionary<string, ManagedTypeOverride> result)
    {
        var sourceText = GetSourceText(decl.Handle.TranslationUnit, decl.Extent);

        var attrMatch = ManagedTypeAttributeExpression.Match(sourceText);
        if (!attrMatch.Success)
        {
            return;
        }

        var body = attrMatch.Groups["body"].Value;
        var publicTypeMatch = PublicTypeExpression.Match(body);
        if (!publicTypeMatch.Success)
        {
            return;
        }

        // Extract the C++ template argument type from cursor-extent source text.
        // GetSourceText preserves spaces between tokens so "const char*" stays correct.
        var cppTypeName = ExtractTemplateArgFromCursorType(decl);
        if (string.IsNullOrWhiteSpace(cppTypeName))
        {
            return;
        }

        cppTypeName = NormalizeTypeName(cppTypeName);

        var key = isReturn ? "return:" + cppTypeName : cppTypeName;

        if (result.ContainsKey(key))
        {
            return;
        }

        var undefaultableMatch = UndefaultableExpression.Match(body);
        var marshallerTypeMatch = MarshallerTypeInAttributeExpression.Match(body);
        var managedMarshallerMatch = ManagedMarshallerInAttributeExpression.Match(body);

        result[key] = new ManagedTypeOverride(
            cppTypeName,
            publicTypeMatch.Groups["value"].Value)
        {
            Undefaultable = undefaultableMatch.Success &&
                string.Equals(undefaultableMatch.Groups["value"].Value, "true", StringComparison.OrdinalIgnoreCase),
            MarshallerTypeName = marshallerTypeMatch.Success ? marshallerTypeMatch.Groups["value"].Value : null,
            ManagedMarshaller = managedMarshallerMatch.Success ? managedMarshallerMatch.Groups["value"].Value : null
        };
    }

    // ─── Body extraction (attribute + exposed members from AST cursors) ─────────

    private static void WalkForMarshallerBodies(
        Decl decl,
        Dictionary<string, MarshallerBodyConfig> result)
    {
        foreach (var child in decl.Decls)
        {
            if (child is RecordDecl or ClassTemplateDecl or ClassTemplatePartialSpecializationDecl)
            {
                var spelling = child is NamedDecl named ? named.Name ?? named.Spelling.ToString() : child.Spelling.ToString();
                if (string.Equals(spelling, "Marshaller", StringComparison.Ordinal) ||
                    string.Equals(spelling, "ReturnMarshaller", StringComparison.Ordinal))
                {
                    ProcessMarshallerBody(child, result);
                }
            }

            if (child is NamespaceDecl)
            {
                WalkForMarshallerBodies(child, result);
            }
        }
    }

    private static void ProcessMarshallerBody(
        Decl decl,
        Dictionary<string, MarshallerBodyConfig> result)
    {
        var sourceText = GetSourceText(decl.Handle.TranslationUnit, decl.Extent);

        var attrMatch = ManagedTypeAttributeExpression.Match(sourceText);
        if (!attrMatch.Success)
        {
            return;
        }

        var body = attrMatch.Groups["body"].Value;

        var cppTypeName = ExtractTemplateArgFromCursorType(decl);
        if (string.IsNullOrWhiteSpace(cppTypeName) || result.ContainsKey(cppTypeName))
        {
            return;
        }

        cppTypeName = NormalizeTypeName(cppTypeName);

        // Walk struct children for [[sharpcpp::expose_member(...)]] on member functions.
        var exposedMembers = ExtractExposedMembersFromDecl(decl);

        result[cppTypeName] = new MarshallerBodyConfig(
            AttributeBody: body,
            ExposedMembers: exposedMembers);
    }

    private static List<ExposedMemberInfo> ExtractExposedMembersFromDecl(Decl structDecl)
    {
        var members = new List<ExposedMemberInfo>();

        foreach (var child in structDecl.Decls)
        {
            if (child is not CXXMethodDecl methodDecl)
            {
                continue;
            }

            // Check if this method has [[sharpcpp::expose_member(...)]].
            var childText = GetSourceText(methodDecl.Handle.TranslationUnit, methodDecl.Extent);
            var exposeMatch = ExposeMemberAttributeExpression.Match(childText);
            if (!exposeMatch.Success)
            {
                continue;
            }

            var attrBody = exposeMatch.Groups["attr"].Value;
            var shape = ShapeExpression.Match(attrBody) is { Success: true } shapeMatch
                ? shapeMatch.Groups["value"].Value
                : "method";
            var exposed = ExposedExpression.Match(attrBody) is { Success: true } exposedMatch
                ? string.Equals(exposedMatch.Groups["value"].Value, "true", StringComparison.OrdinalIgnoreCase)
                : true;

            // Extract method info from the managed CXXMethodDecl.
            var methodName = methodDecl.Name ?? methodDecl.Spelling.ToString();
            var returnTypeSpelling = methodDecl.ReturnType.AsString;
            var isConst = methodDecl.IsConst;

            // Extract parameters from managed API.
            var parameters = new List<(string Name, string CppType)>();
            foreach (var param in methodDecl.Parameters)
            {
                var paramName = param.Name ?? param.Spelling.ToString();
                var paramType = param.Type.AsString;
                parameters.Add((paramName, NormalizeTypeName(paramType)));
            }

            members.Add(new ExposedMemberInfo(
                MethodName: methodName,
                CppReturnType: NormalizeTypeName(returnTypeSpelling),
                IsConst: isConst,
                Shape: shape,
                Exposed: exposed,
                Parameters: parameters.AsReadOnly()));
        }

        return members;
    }

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

    /// <summary>
    /// Extracts the template argument type from a Marshaller/ReturnMarsharker struct
    /// declaration cursor using the cursor's AST extent source text.
    ///
    /// For <c>template&lt;&gt; struct Marshaller&lt;const char*&gt; { ... };</c>,
    /// this returns <c>"const char*"</c> by matching <c>Marshaller&lt;...&gt;</c>
    /// in the cursor extent text (with spaces preserved between tokens).
    /// The cursor extent IS the AST's definitive representation of the node.
    /// </summary>
    private static string? ExtractTemplateArgFromCursorType(Decl decl)
    {
        // Use the managed ClassTemplateSpecializationDecl API for template argument access.
        // This handles explicit specializations like template<> struct Marshaller<const char*>.
        if (decl is ClassTemplateSpecializationDecl specDecl)
        {
            var templateArgs = specDecl.TemplateArgs;
            if (templateArgs.Count > 0)
            {
                var typeName = templateArgs[0].AsType.AsString;
                if (!string.IsNullOrWhiteSpace(typeName))
                {
                    return NormalizeTypeName(typeName);
                }
            }
        }

        return null;
    }

    internal static string NormalizeTypeName(string spelling)
    {
        return Regex.Replace(spelling.Trim(), @"\s+", " ")
            .Replace(" *", "*", StringComparison.Ordinal)
            .Replace(" &", "&", StringComparison.Ordinal);
    }
}
