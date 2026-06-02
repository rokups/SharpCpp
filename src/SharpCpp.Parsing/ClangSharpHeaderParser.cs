using ClangSharp.Interop;

using SharpCpp.Model.Ast;

namespace SharpCpp.Parsing;

public sealed class ClangSharpHeaderParser
{
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
                        compilation.Members.Add(CreateFunction(child, request.EntryPointPrefix, recordsByName));
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

    private static FunctionBinding CreateFunction(CXCursor cursor, string entryPointPrefix, IDictionary<string, RecordBinding> recordsByName)
    {
        var nativeName = cursor.Spelling.ToString();
        var function = new FunctionBinding(
            $"function:{nativeName}",
            nativeName,
            ToPascalCase(nativeName),
            MapType(cursor.ResultType, recordsByName),
            entryPointName: $"{entryPointPrefix}_{nativeName}");

        for (uint parameterIndex = 0; parameterIndex < cursor.NumArguments; parameterIndex++)
        {
            var parameterCursor = cursor.GetArgument(parameterIndex);
            var parameterName = parameterCursor.Spelling.ToString();
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                parameterName = $"arg{parameterIndex}";
            }

            var (parameterType, direction, abiKind) = MapParameter(parameterCursor.Type);
            function.Parameters.Add(
                new ParameterBinding(
                    $"parameter:{nativeName}:{parameterName}",
                    parameterName,
                    parameterName,
                    parameterType,
                    direction,
                    abiKind));
        }

        return function;
    }

    private static (BindingType Type, BindingParameterDirection Direction, BindingParameterAbiKind AbiKind) MapParameter(CXType type)
    {
        var normalizedType = type.CanonicalType;

        if (normalizedType.kind == CXTypeKind.CXType_LValueReference)
        {
            var pointeeType = normalizedType.PointeeType.CanonicalType;
            return (
                MapType(pointeeType, new Dictionary<string, RecordBinding>(StringComparer.Ordinal)),
                pointeeType.IsConstQualified ? BindingParameterDirection.In : BindingParameterDirection.Ref,
                BindingParameterAbiKind.Reference);
        }

        if (normalizedType.kind == CXTypeKind.CXType_Pointer)
        {
            var pointeeType = normalizedType.PointeeType.CanonicalType;
            return (
                MapType(pointeeType, new Dictionary<string, RecordBinding>(StringComparer.Ordinal)),
                pointeeType.IsConstQualified ? BindingParameterDirection.In : BindingParameterDirection.Out,
                BindingParameterAbiKind.Pointer);
        }

        return (MapType(type, new Dictionary<string, RecordBinding>(StringComparer.Ordinal)), BindingParameterDirection.In, BindingParameterAbiKind.Value);
    }

    private static BindingType MapType(CXType type, IDictionary<string, RecordBinding> recordsByName)
    {
        var normalizedType = type.CanonicalType;

        if (normalizedType.kind == CXTypeKind.CXType_Pointer)
        {
            var pointeeType = normalizedType.PointeeType.CanonicalType;
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
}
