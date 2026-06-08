using System.Text.RegularExpressions;
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
    // This is the LAST remaining regex for C++ code structure — it matches the
    // attribute syntax, not template declarations or function signatures.
    private static readonly Regex ManagedTypeAttributeExpression = new(
        @"\[\[\s*sharpcpp::managed_type\s*\((?<body>[^)]*)\)\s*\]\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Extract [[sharpcpp::expose_member(attr)]] from a member function cursor extent text.
    private static readonly Regex ExposeMemberAttributeExpression = new(
        @"\[\s*\[\s*sharpcpp::expose_member\s*\(\s*(?<attr>[^)]*?)\s*\)\s*\]\s*\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Extract the template argument type from Marshaller<T>.
    // Separate from ReturnMarshaller to avoid matching base-class Marshaller<T>
    // in declarations like: struct ReturnMarshaller<X> : Marshaller<X> {};
    private static readonly Regex MarshallerTemplateArgPattern = new(
        @"\bMarshaller\s*<\s*(?<cppType>[^>]+)\s*>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Extract the template argument from ReturnMarshaller<T>.
    private static readonly Regex ReturnMarshallerTemplateArgPattern = new(
        @"\bReturnMarshaller\s*<\s*(?<cppType>[^>]+)\s*>",
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
    public static unsafe IReadOnlyDictionary<string, ManagedTypeOverride> ExtractManagedTypeOverrides(
        IReadOnlyList<string> marshallerFiles,
        IReadOnlyList<string> clangArgs)
    {
        var result = new Dictionary<string, ManagedTypeOverride>(StringComparer.Ordinal);

        PerformClangParse(marshallerFiles, clangArgs, tu =>
        {
            WalkForMarshallerDeclarations(tu.Cursor, result);
        });

        return result;
    }

    /// <summary>
    /// Extract Marshaller specialization attribute bodies and exposed member info
    /// from marshaller configuration headers. All method metadata comes from the
    /// ClangSharp AST cursors — no C++ source-text regex.
    /// </summary>
    public static unsafe IReadOnlyDictionary<string, MarshallerBodyConfig> ExtractMarshallerBodies(
        IReadOnlyList<string> marshallerFiles,
        IReadOnlyList<string> clangArgs)
    {
        var result = new Dictionary<string, MarshallerBodyConfig>(StringComparer.Ordinal);

        PerformClangParse(marshallerFiles, clangArgs, tu =>
        {
            WalkForMarshallerBodies(tu.Cursor, result);
        });

        return result;
    }

    private static unsafe void PerformClangParse(
        IReadOnlyList<string> marshallerFiles,
        IReadOnlyList<string> clangArgs,
        Action<CXTranslationUnit> action)
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

            action(translationUnit);
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

    private static unsafe void WalkForMarshallerDeclarations(
        CXCursor cursor,
        Dictionary<string, ManagedTypeOverride> result)
    {
        cursor.VisitChildren((child, _, _) =>
        {
            if (child.Kind is CXCursorKind.CXCursor_StructDecl or
                             CXCursorKind.CXCursor_ClassDecl or
                             CXCursorKind.CXCursor_ClassTemplate or
                             CXCursorKind.CXCursor_ClassTemplatePartialSpecialization)
            {
                var spelling = child.Spelling.ToString();
                if (string.Equals(spelling, "Marshaller", StringComparison.Ordinal))
                {
                    ProcessMarshallerDeclaration(child, isReturn: false, result);
                }
                else if (string.Equals(spelling, "ReturnMarshaller", StringComparison.Ordinal))
                {
                    ProcessMarshallerDeclaration(child, isReturn: true, result);
                }
            }

            if (child.Kind == CXCursorKind.CXCursor_Namespace)
            {
                WalkForMarshallerDeclarations(child, result);
            }

            return CXChildVisitResult.CXChildVisit_Continue;
        }, new CXClientData(IntPtr.Zero));
    }

    private static void ProcessMarshallerDeclaration(
        CXCursor cursor,
        bool isReturn,
        Dictionary<string, ManagedTypeOverride> result)
    {
        var sourceText = GetSourceText(cursor.TranslationUnit, cursor.Extent);

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

        var typePattern = isReturn ? ReturnMarshallerTemplateArgPattern : MarshallerTemplateArgPattern;
        var typeMatch = typePattern.Match(sourceText);
        if (!typeMatch.Success)
        {
            return;
        }

        var cppTypeName = NormalizeTypeName(typeMatch.Groups["cppType"].Value);
        if (string.IsNullOrWhiteSpace(cppTypeName))
        {
            return;
        }

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

    private static unsafe void WalkForMarshallerBodies(
        CXCursor cursor,
        Dictionary<string, MarshallerBodyConfig> result)
    {
        cursor.VisitChildren((child, _, _) =>
        {
            if (child.Kind is CXCursorKind.CXCursor_StructDecl or
                             CXCursorKind.CXCursor_ClassDecl or
                             CXCursorKind.CXCursor_ClassTemplate or
                             CXCursorKind.CXCursor_ClassTemplatePartialSpecialization)
            {
                var spelling = child.Spelling.ToString();
                if (string.Equals(spelling, "Marshaller", StringComparison.Ordinal) ||
                    string.Equals(spelling, "ReturnMarshaller", StringComparison.Ordinal))
                {
                    ProcessMarshallerBody(child, result);
                }
            }

            if (child.Kind == CXCursorKind.CXCursor_Namespace)
            {
                WalkForMarshallerBodies(child, result);
            }

            return CXChildVisitResult.CXChildVisit_Continue;
        }, new CXClientData(IntPtr.Zero));
    }

    private static void ProcessMarshallerBody(
        CXCursor cursor,
        Dictionary<string, MarshallerBodyConfig> result)
    {
        var sourceText = GetSourceText(cursor.TranslationUnit, cursor.Extent);

        var attrMatch = ManagedTypeAttributeExpression.Match(sourceText);
        if (!attrMatch.Success)
        {
            return;
        }

        var body = attrMatch.Groups["body"].Value;

        var typePattern = sourceText.Contains("ReturnMarshaller", StringComparison.Ordinal)
            ? ReturnMarshallerTemplateArgPattern
            : MarshallerTemplateArgPattern;
        var typeMatch = typePattern.Match(sourceText);
        if (!typeMatch.Success)
        {
            return;
        }

        var cppTypeName = NormalizeTypeName(typeMatch.Groups["cppType"].Value);
        if (string.IsNullOrWhiteSpace(cppTypeName) || result.ContainsKey(cppTypeName))
        {
            return;
        }

        // Walk struct children for [[sharpcpp::expose_member(...)]] on member functions.
        var exposedMembers = ExtractExposedMembersFromCursor(cursor);

        result[cppTypeName] = new MarshallerBodyConfig(
            AttributeBody: body,
            ExposedMembers: exposedMembers);
    }

    private static unsafe List<ExposedMemberInfo> ExtractExposedMembersFromCursor(CXCursor structCursor)
    {
        var members = new List<ExposedMemberInfo>();

        structCursor.VisitChildren((child, _, _) =>
        {
            if (child.Kind != CXCursorKind.CXCursor_CXXMethod)
            {
                return CXChildVisitResult.CXChildVisit_Continue;
            }

            // Check if this method has [[sharpcpp::expose_member(...)]].
            var childText = GetSourceText(child.TranslationUnit, child.Extent);
            var exposeMatch = ExposeMemberAttributeExpression.Match(childText);
            if (!exposeMatch.Success)
            {
                return CXChildVisitResult.CXChildVisit_Continue;
            }

            var attrBody = exposeMatch.Groups["attr"].Value;
            var shape = ShapeExpression.Match(attrBody) is { Success: true } shapeMatch
                ? shapeMatch.Groups["value"].Value
                : "method";
            var exposed = ExposedExpression.Match(attrBody) is { Success: true } exposedMatch
                ? string.Equals(exposedMatch.Groups["value"].Value, "true", StringComparison.OrdinalIgnoreCase)
                : true;

            // Extract method info from the AST cursor (not from source text).
            var methodName = child.Spelling.ToString();
            var returnTypeSpelling = child.ResultType.Spelling.ToString();
            var isConst = clang.CXXMethod_isConst(child) != 0;

            // Extract parameters from AST.
            var parameters = new List<(string Name, string CppType)>();
            for (uint paramIndex = 0; paramIndex < child.NumArguments; paramIndex++)
            {
                var paramCursor = child.GetArgument(paramIndex);
                var paramName = paramCursor.Spelling.ToString();
                var paramType = paramCursor.Type.Spelling.ToString();
                parameters.Add((paramName, NormalizeTypeName(paramType)));
            }

            members.Add(new ExposedMemberInfo(
                MethodName: methodName,
                CppReturnType: NormalizeTypeName(returnTypeSpelling),
                IsConst: isConst,
                Shape: shape,
                Exposed: exposed,
                Parameters: parameters.AsReadOnly()));

            return CXChildVisitResult.CXChildVisit_Continue;
        }, new CXClientData(IntPtr.Zero));

        return members;
    }

    // ─── Utilities ───────────────────────────────────────────────────────────

    private static string GetSourceText(CXTranslationUnit translationUnit, CXSourceRange sourceRange)
    {
        var tokens = translationUnit.Tokenize(sourceRange);
        try
        {
            var builder = new System.Text.StringBuilder();
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

    internal static string NormalizeTypeName(string spelling)
    {
        return Regex.Replace(spelling.Trim(), @"\s+", " ")
            .Replace(" *", "*", StringComparison.Ordinal)
            .Replace(" &", "&", StringComparison.Ordinal);
    }
}
