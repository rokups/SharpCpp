using ClangSharp.Interop;
using System.Text.RegularExpressions;

using SharpCpp.Model.Ast;

namespace SharpCpp.Parsing;

public sealed class ClangSharpHeaderParser
{
    private static readonly Regex ManagedTypeMarshallerExpression = new(
        @"template\s*<>\s*struct\s*\[\[\s*sharpcpp::managed_type\((?<body>.*?)\)\s*\]\]\s*Marshaller\s*<\s*(?<cppType>.*?)\s*>\s*\{(?<members>.*?)\};",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex PublicTypeExpression = new(
        @"public_type\s*=\s*\""(?<value>[^\""\\]*(?:\\.[^\""\\]*)*)\""",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex AbiTypeAliasExpression = new(
        @"using\s+abi_type\s*=\s*(?<value>[^;]+);",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public unsafe BindingCompilation Parse(ClangSharpParseRequest request)
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

        using var index = CXIndex.Create();
        using var translationUnit = CXTranslationUnit.Parse(
            index,
            headerFilePath,
            request.ClangArguments.ToArray(),
            Array.Empty<CXUnsavedFile>(),
            CXTranslationUnit_Flags.CXTranslationUnit_None);

        ValidateDiagnostics(translationUnit, headerFilePath);

        var compilation = new BindingCompilation(
            request.ProjectName,
            request.ManagedNamespace,
            request.ManagedClassName,
            request.NativeLibraryName,
            new[] { request.HeaderIncludePath });
        var recordsByName = new Dictionary<string, RecordBinding>(StringComparer.Ordinal);
        var managedTypeOverrides = LoadManagedTypeOverrides(request.MarshallerConfigurationFiles);

        translationUnit.Cursor.VisitChildren(
            (child, _, _) =>
            {
                if (!IsOwnedCursor(child, headerFilePath))
                {
                    return CXChildVisitResult.CXChildVisit_Continue;
                }

                switch (child.Kind)
                {
                    case CXCursorKind.CXCursor_StructDecl:
                        RegisterRecord(child, compilation, recordsByName);
                        break;

                    case CXCursorKind.CXCursor_FunctionDecl:
                        compilation.Members.Add(CreateFunction(child, request.EntryPointPrefix, recordsByName, managedTypeOverrides));
                        break;
                }

                return CXChildVisitResult.CXChildVisit_Continue;
            },
            new CXClientData(IntPtr.Zero));

        return compilation;
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

    private static bool IsOwnedCursor(CXCursor cursor, string headerFilePath)
    {
        cursor.Location.GetFileLocation(out var file, out _, out _, out _);
        var filePath = file.Name.CString;
        return string.Equals(Path.GetFullPath(filePath), headerFilePath, StringComparison.Ordinal);
    }

    private static void RegisterRecord(CXCursor cursor, BindingCompilation compilation, IDictionary<string, RecordBinding> recordsByName)
    {
        var nativeName = cursor.Spelling.ToString();
        if (string.IsNullOrWhiteSpace(nativeName) || recordsByName.ContainsKey(nativeName))
        {
            return;
        }

        var record = new RecordBinding($"record:{nativeName}", nativeName, nativeName);
        recordsByName.Add(nativeName, record);
        compilation.Members.Add(record);
    }

    private static FunctionBinding CreateFunction(
        CXCursor cursor,
        string entryPointPrefix,
        IDictionary<string, RecordBinding> recordsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides)
    {
        var nativeName = cursor.Spelling.ToString();
        var function = new FunctionBinding(
            $"function:{nativeName}",
            nativeName,
            ToPascalCase(nativeName),
            MapType(cursor.ResultType, recordsByName, managedTypeOverrides),
            entryPointName: $"{entryPointPrefix}_{nativeName}",
            cppReturnTypeName: NormalizeTypeName(cursor.ResultType.Spelling.ToString()));

        for (uint parameterIndex = 0; parameterIndex < cursor.NumArguments; parameterIndex++)
        {
            var parameterCursor = cursor.GetArgument(parameterIndex);
            var parameterName = parameterCursor.Spelling.ToString();
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                parameterName = $"arg{parameterIndex}";
            }

            var (parameterType, direction, abiKind) = MapParameter(parameterCursor.Type, managedTypeOverrides);
            function.Parameters.Add(
                new ParameterBinding(
                    $"parameter:{nativeName}:{parameterName}",
                    parameterName,
                    parameterName,
                    parameterType,
                    direction,
                    abiKind,
                    NormalizeTypeName(parameterCursor.Type.Spelling.ToString())));
        }

        return function;
    }

    private static (BindingType Type, BindingParameterDirection Direction, BindingParameterAbiKind AbiKind) MapParameter(
        CXType type,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides)
    {
        var normalizedType = type.CanonicalType;

        if (normalizedType.kind == CXTypeKind.CXType_LValueReference)
        {
            var pointeeType = normalizedType.PointeeType.CanonicalType;
            return (
                MapType(pointeeType, new Dictionary<string, RecordBinding>(StringComparer.Ordinal), managedTypeOverrides),
                pointeeType.IsConstQualified ? BindingParameterDirection.In : BindingParameterDirection.Ref,
                BindingParameterAbiKind.Reference);
        }

        if (normalizedType.kind == CXTypeKind.CXType_Pointer)
        {
            var pointeeType = normalizedType.PointeeType.CanonicalType;

            if (pointeeType.kind is CXTypeKind.CXType_Void or CXTypeKind.CXType_Record)
            {
                return (
                    MapType(type, new Dictionary<string, RecordBinding>(StringComparer.Ordinal), managedTypeOverrides),
                    BindingParameterDirection.In,
                    BindingParameterAbiKind.Value);
            }

            return (
                MapType(pointeeType, new Dictionary<string, RecordBinding>(StringComparer.Ordinal), managedTypeOverrides),
                pointeeType.IsConstQualified ? BindingParameterDirection.In : BindingParameterDirection.Out,
                BindingParameterAbiKind.Pointer);
        }

        return (MapType(type, new Dictionary<string, RecordBinding>(StringComparer.Ordinal), managedTypeOverrides), BindingParameterDirection.In, BindingParameterAbiKind.Value);
    }

    private static BindingType MapType(
        CXType type,
        IDictionary<string, RecordBinding> recordsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides)
    {
        var normalizedType = type.CanonicalType;
        var normalizedTypeName = NormalizeTypeName(type.Spelling.ToString());

        if (managedTypeOverrides.TryGetValue(normalizedTypeName, out var managedTypeOverride))
        {
            return BindingType.CreateConfigured(
                managedTypeOverride.CppTypeName,
                managedTypeOverride.PublicTypeName,
                managedTypeOverride.AbiTypeName);
        }

        if (normalizedType.kind == CXTypeKind.CXType_Pointer)
        {
            var pointeeType = normalizedType.PointeeType.CanonicalType;
            if (pointeeType.kind == CXTypeKind.CXType_Void &&
                managedTypeOverrides.TryGetValue("void*", out var voidPointerOverride))
            {
                return BindingType.CreateConfigured(
                    NormalizeTypeName(type.Spelling.ToString()),
                    voidPointerOverride.PublicTypeName,
                    voidPointerOverride.AbiTypeName);
            }

            if (pointeeType.kind == CXTypeKind.CXType_Record)
            {
                var recordName = NormalizeRecordName(pointeeType.Spelling.ToString());
                if (!recordsByName.TryGetValue(recordName, out var record))
                {
                    record = new RecordBinding($"record:{recordName}", recordName, recordName);
                    recordsByName.Add(recordName, record);
                }

                return record.HandleType;
            }
        }

        return normalizedType.kind switch
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
    }

    private static string ToPascalCase(string nativeName)
    {
        return string.Concat(
            nativeName
                .Split('_', StringSplitOptions.RemoveEmptyEntries)
                .Select(static part => char.ToUpperInvariant(part[0]) + part[1..]));
    }

    private static string NormalizeRecordName(string spelling)
    {
        return spelling
            .Replace("struct ", string.Empty, StringComparison.Ordinal)
            .Replace("class ", string.Empty, StringComparison.Ordinal)
            .Trim();
    }

    private static string NormalizeTypeName(string spelling)
    {
        return Regex.Replace(spelling.Trim(), @"\s+", " ")
            .Replace(" *", "*", StringComparison.Ordinal)
            .Replace(" &", "&", StringComparison.Ordinal);
    }

    private static IReadOnlyDictionary<string, ManagedTypeOverride> LoadManagedTypeOverrides(IReadOnlyList<string> files)
    {
        var result = new Dictionary<string, ManagedTypeOverride>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            var path = Path.GetFullPath(file);
            if (!File.Exists(path))
            {
                continue;
            }

            var sourceText = File.ReadAllText(path);
            foreach (Match match in ManagedTypeMarshallerExpression.Matches(sourceText))
            {
                var body = match.Groups["body"].Value;
                var publicTypeMatch = PublicTypeExpression.Match(body);
                var abiTypeMatch = AbiTypeAliasExpression.Match(match.Groups["members"].Value);
                if (!publicTypeMatch.Success || !abiTypeMatch.Success)
                {
                    continue;
                }

                var cppTypeName = NormalizeTypeName(match.Groups["cppType"].Value);
                result[cppTypeName] = new ManagedTypeOverride(
                    cppTypeName,
                    publicTypeMatch.Groups["value"].Value,
                    NormalizeTypeName(abiTypeMatch.Groups["value"].Value));
            }
        }

        return result;
    }

    private sealed record ManagedTypeOverride(string CppTypeName, string PublicTypeName, string AbiTypeName);
}
