using ClangSharp.Interop;
using System.Text;
using System.Text.RegularExpressions;

using SharpCpp.Model.Ast;

namespace SharpCpp.Parsing;

public sealed class ClangSharpHeaderParser
{
    private static readonly Regex PassKindAttributeExpression = new(
        @"\[\[\s*sharpcpp::(?<kind>in|out|ref)\s*\]\]",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex FlagsAttributeExpression = new(
        @"\[\[\s*sharpcpp::flags\s*\]\]",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
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
        var enumsByName = new Dictionary<string, EnumBinding>(StringComparer.Ordinal);
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
                        RegisterRecord(child, compilation, recordsByName, enumsByName, managedTypeOverrides);
                        break;

                    case CXCursorKind.CXCursor_EnumDecl:
                        RegisterEnum(child, compilation, enumsByName);
                        break;

                    case CXCursorKind.CXCursor_FunctionDecl:
                        compilation.Members.Add(CreateFunction(child, request.EntryPointPrefix, recordsByName, enumsByName, managedTypeOverrides));
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

    private static unsafe void RegisterRecord(
        CXCursor cursor,
        BindingCompilation compilation,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides)
    {
        var nativeName = cursor.Spelling.ToString();
        if (string.IsNullOrWhiteSpace(nativeName) || recordsByName.ContainsKey(nativeName))
        {
            return;
        }

        var record = new RecordBinding($"record:{nativeName}", nativeName, ToPascalCase(nativeName));
        recordsByName.Add(nativeName, record);
        compilation.Members.Add(record);

        cursor.VisitChildren(
            (child, _, _) =>
            {
                if (child.Kind != CXCursorKind.CXCursor_CXXBaseSpecifier)
                {
                    return CXChildVisitResult.CXChildVisit_Continue;
                }

                if (record.ManagedBaseTypeName is not null)
                {
                    return CXChildVisitResult.CXChildVisit_Continue;
                }

                var baseTypeName = NormalizeNamedTypeName(child.Type.Spelling.ToString());
                if (recordsByName.TryGetValue(baseTypeName, out var baseRecord))
                {
                    record.ManagedBaseTypeName = baseRecord.ManagedName;
                }

                return CXChildVisitResult.CXChildVisit_Continue;
            },
            new CXClientData(IntPtr.Zero));

        cursor.VisitChildren(
            (child, _, _) =>
            {
                if ((child.Kind != CXCursorKind.CXCursor_CXXMethod && child.Kind != CXCursorKind.CXCursor_ConversionFunction) ||
                    child.CXXMethod_IsStatic)
                {
                    return CXChildVisitResult.CXChildVisit_Continue;
                }

                if (child.CXXAccessSpecifier == CX_CXXAccessSpecifier.CX_CXXPrivate ||
                    child.CXXAccessSpecifier == CX_CXXAccessSpecifier.CX_CXXProtected)
                {
                    return CXChildVisitResult.CXChildVisit_Continue;
                }

                record.Members.Add(CreateMethod(record, child, recordsByName, enumsByName, managedTypeOverrides));
                return CXChildVisitResult.CXChildVisit_Continue;
            },
            new CXClientData(IntPtr.Zero));

        cursor.VisitChildren(
            (child, _, _) =>
            {
                if (child.Kind != CXCursorKind.CXCursor_CXXMethod || !child.CXXMethod_IsStatic)
                {
                    return CXChildVisitResult.CXChildVisit_Continue;
                }

                if (child.CXXAccessSpecifier == CX_CXXAccessSpecifier.CX_CXXPrivate ||
                    child.CXXAccessSpecifier == CX_CXXAccessSpecifier.CX_CXXProtected)
                {
                    return CXChildVisitResult.CXChildVisit_Continue;
                }

                record.Members.Add(CreateStaticMethod(record, child, recordsByName, enumsByName, managedTypeOverrides));
                return CXChildVisitResult.CXChildVisit_Continue;
            },
            new CXClientData(IntPtr.Zero));

        cursor.VisitChildren(
            (child, _, _) =>
            {
                if (child.Kind != CXCursorKind.CXCursor_Constructor)
                {
                    return CXChildVisitResult.CXChildVisit_Continue;
                }

                if (child.CXXAccessSpecifier == CX_CXXAccessSpecifier.CX_CXXPrivate ||
                    child.CXXAccessSpecifier == CX_CXXAccessSpecifier.CX_CXXProtected)
                {
                    return CXChildVisitResult.CXChildVisit_Continue;
                }

                record.Members.Add(CreateConstructor(record, child, recordsByName, enumsByName, managedTypeOverrides));
                return CXChildVisitResult.CXChildVisit_Continue;
            },
            new CXClientData(IntPtr.Zero));

        cursor.VisitChildren(
            (child, _, _) =>
            {
                if (child.Kind != CXCursorKind.CXCursor_FieldDecl)
                {
                    return CXChildVisitResult.CXChildVisit_Continue;
                }

                if (child.CXXAccessSpecifier == CX_CXXAccessSpecifier.CX_CXXPrivate ||
                    child.CXXAccessSpecifier == CX_CXXAccessSpecifier.CX_CXXProtected)
                {
                    return CXChildVisitResult.CXChildVisit_Continue;
                }

                record.Members.Add(CreateField(record, child, recordsByName, enumsByName, managedTypeOverrides));
                return CXChildVisitResult.CXChildVisit_Continue;
            },
            new CXClientData(IntPtr.Zero));
    }

    private static FunctionBinding CreateMethod(
        RecordBinding record,
        CXCursor cursor,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides)
    {
        var nativeName = cursor.Spelling.ToString();
        var function = new FunctionBinding(
            $"method:{record.NativeName}:{nativeName}",
            nativeName,
            GetManagedMethodName(cursor, nativeName),
            MapType(cursor.ResultType, recordsByName, enumsByName, managedTypeOverrides),
            entryPointName: $"sharpcpp_e2e_{record.NativeName}_{GetEntrypointSuffix(cursor, nativeName)}",
            cppReturnTypeName: NormalizeTypeName(cursor.ResultType.Spelling.ToString()))
        {
            IsStatic = false,
            OwningRecordCppTypeName = record.NativeName
        };
        ApplyOperatorMetadata(function, cursor);

        for (uint parameterIndex = 0; parameterIndex < cursor.NumArguments; parameterIndex++)
        {
            var parameterCursor = cursor.GetArgument(parameterIndex);
            var parameterName = parameterCursor.Spelling.ToString();
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                parameterName = $"arg{parameterIndex}";
            }

            var (parameterType, direction, abiKind) = MapParameter(cursor, parameterCursor, recordsByName, enumsByName, managedTypeOverrides);
            function.Parameters.Add(
                new ParameterBinding(
                    $"parameter:{record.NativeName}:{nativeName}:{parameterName}",
                    parameterName,
                    parameterName,
                    parameterType,
                    direction,
                    abiKind,
                    NormalizeTypeName(parameterCursor.Type.Spelling.ToString()))
                {
                    ManagedDefaultValueExpression = GetManagedDefaultValueExpression(parameterCursor, parameterType)
                });
        }

        return function;
    }

    private static FunctionBinding CreateStaticMethod(
        RecordBinding record,
        CXCursor cursor,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides)
    {
        var nativeName = cursor.Spelling.ToString();
        var function = new FunctionBinding(
            $"static-method:{record.NativeName}:{nativeName}",
            nativeName,
            GetManagedMethodName(cursor, nativeName),
            MapType(cursor.ResultType, recordsByName, enumsByName, managedTypeOverrides),
            entryPointName: $"sharpcpp_e2e_{record.NativeName}_{GetEntrypointSuffix(cursor, nativeName)}",
            cppReturnTypeName: NormalizeTypeName(cursor.ResultType.Spelling.ToString()))
        {
            IsStatic = true,
            OwningRecordCppTypeName = record.NativeName
        };
        ApplyOperatorMetadata(function, cursor);

        for (uint parameterIndex = 0; parameterIndex < cursor.NumArguments; parameterIndex++)
        {
            var parameterCursor = cursor.GetArgument(parameterIndex);
            var parameterName = parameterCursor.Spelling.ToString();
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                parameterName = $"arg{parameterIndex}";
            }

            var (parameterType, direction, abiKind) = MapParameter(cursor, parameterCursor, recordsByName, enumsByName, managedTypeOverrides);
            function.Parameters.Add(
                new ParameterBinding(
                    $"parameter:{record.NativeName}:static:{nativeName}:{parameterName}",
                    parameterName,
                    parameterName,
                    parameterType,
                    direction,
                    abiKind,
                    NormalizeTypeName(parameterCursor.Type.Spelling.ToString()))
                {
                    ManagedDefaultValueExpression = GetManagedDefaultValueExpression(parameterCursor, parameterType)
                });
        }

        return function;
    }

    private static FunctionBinding CreateConstructor(
        RecordBinding record,
        CXCursor cursor,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides)
    {
        var function = new FunctionBinding(
            $"ctor:{record.NativeName}:{cursor.Hash}",
            record.NativeName,
            record.ManagedName,
            record.HandleType,
            entryPointName: $"sharpcpp_e2e_{record.NativeName}_ctor",
            cppReturnTypeName: $"{record.NativeName}*")
        {
            IsStatic = false,
            IsConstructor = true,
            OwningRecordCppTypeName = record.NativeName
        };

        for (uint parameterIndex = 0; parameterIndex < cursor.NumArguments; parameterIndex++)
        {
            var parameterCursor = cursor.GetArgument(parameterIndex);
            var parameterName = parameterCursor.Spelling.ToString();
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                parameterName = $"arg{parameterIndex}";
            }

            var (parameterType, direction, abiKind) = MapParameter(cursor, parameterCursor, recordsByName, enumsByName, managedTypeOverrides);
            function.Parameters.Add(
                new ParameterBinding(
                    $"parameter:{record.NativeName}:ctor:{parameterName}",
                    parameterName,
                    parameterName,
                    parameterType,
                    direction,
                    abiKind,
                    NormalizeTypeName(parameterCursor.Type.Spelling.ToString()))
                {
                    ManagedDefaultValueExpression = GetManagedDefaultValueExpression(parameterCursor, parameterType)
                });
        }

        return function;
    }

    private static FieldBinding CreateField(
        RecordBinding record,
        CXCursor cursor,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides)
    {
        var nativeName = cursor.Spelling.ToString();
        return new FieldBinding(
            $"field:{record.NativeName}:{nativeName}",
            nativeName,
            ToPascalCase(nativeName),
            MapType(cursor.Type, recordsByName, enumsByName, managedTypeOverrides),
            NormalizeTypeName(cursor.Type.Spelling.ToString()),
            record.NativeName,
            isReadOnly: cursor.Type.IsConstQualified);
    }

    private static FunctionBinding CreateFunction(
        CXCursor cursor,
        string entryPointPrefix,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides)
    {
        var nativeName = cursor.Spelling.ToString();
        var function = new FunctionBinding(
            $"function:{nativeName}",
            nativeName,
            ToPascalCase(nativeName),
            MapType(cursor.ResultType, recordsByName, enumsByName, managedTypeOverrides),
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

            var (parameterType, direction, abiKind) = MapParameter(cursor, parameterCursor, recordsByName, enumsByName, managedTypeOverrides);
            function.Parameters.Add(
                new ParameterBinding(
                    $"parameter:{nativeName}:{parameterName}",
                    parameterName,
                    parameterName,
                    parameterType,
                    direction,
                    abiKind,
                    NormalizeTypeName(parameterCursor.Type.Spelling.ToString()))
                {
                    ManagedDefaultValueExpression = GetManagedDefaultValueExpression(parameterCursor, parameterType)
                });
        }

        function.EntryPointName = BuildEntrypointName(entryPointPrefix, nativeName, function.Parameters);

        return function;
    }

    private static void ApplyOperatorMetadata(FunctionBinding function, CXCursor cursor)
    {
        if (cursor.Kind == CXCursorKind.CXCursor_ConversionFunction)
        {
            function.IsConversionOperator = true;
            function.IsExplicitConversionOperator = clang.CXXMethod_isExplicit(cursor) != 0;
            return;
        }

        if (!cursor.IsOverloadedOperator)
        {
            return;
        }

        function.OperatorToken = cursor.OverloadedOperatorKind switch
        {
            CX_OverloadedOperatorKind.CX_OO_Plus => "+",
            CX_OverloadedOperatorKind.CX_OO_Minus => "-",
            CX_OverloadedOperatorKind.CX_OO_EqualEqual => "==",
            CX_OverloadedOperatorKind.CX_OO_ExclaimEqual => "!=",
            CX_OverloadedOperatorKind.CX_OO_Less => "<",
            CX_OverloadedOperatorKind.CX_OO_Greater => ">",
            _ => null
        };
    }

    private static string GetManagedMethodName(CXCursor cursor, string nativeName)
    {
        if (cursor.Kind == CXCursorKind.CXCursor_ConversionFunction)
        {
            return $"OperatorTo{NormalizeNamedTypeName(cursor.ResultType.Spelling.ToString())}";
        }

        if (cursor.IsOverloadedOperator)
        {
            return cursor.OverloadedOperatorKind switch
            {
                CX_OverloadedOperatorKind.CX_OO_Plus => cursor.NumArguments == 0 ? "OperatorUnaryPlus" : "OperatorPlus",
                CX_OverloadedOperatorKind.CX_OO_Minus => cursor.NumArguments == 0 ? "OperatorUnaryMinus" : "OperatorMinus",
                CX_OverloadedOperatorKind.CX_OO_EqualEqual => "OperatorEquals",
                CX_OverloadedOperatorKind.CX_OO_ExclaimEqual => "OperatorNotEquals",
                CX_OverloadedOperatorKind.CX_OO_Less => "OperatorLessThan",
                CX_OverloadedOperatorKind.CX_OO_Greater => "OperatorGreaterThan",
                _ => ToPascalCase(nativeName)
            };
        }

        return ToPascalCase(nativeName);
    }

    private static string GetEntrypointSuffix(CXCursor cursor, string nativeName)
    {
        if (cursor.Kind == CXCursorKind.CXCursor_ConversionFunction)
        {
            return $"operator_to_{NormalizeNamedTypeName(cursor.ResultType.Spelling.ToString()).ToLowerInvariant()}";
        }

        if (cursor.IsOverloadedOperator)
        {
            return cursor.OverloadedOperatorKind switch
            {
                CX_OverloadedOperatorKind.CX_OO_Plus => cursor.NumArguments == 0 ? "operator_unary_plus" : "operator_plus",
                CX_OverloadedOperatorKind.CX_OO_Minus => cursor.NumArguments == 0 ? "operator_unary_minus" : "operator_minus",
                CX_OverloadedOperatorKind.CX_OO_EqualEqual => "operator_equal_equal",
                CX_OverloadedOperatorKind.CX_OO_ExclaimEqual => "operator_not_equal",
                CX_OverloadedOperatorKind.CX_OO_Less => "operator_less",
                CX_OverloadedOperatorKind.CX_OO_Greater => "operator_greater",
                _ => nativeName
            };
        }

        return nativeName;
    }

    private static string BuildEntrypointName(string entryPointPrefix, string baseName, IEnumerable<ParameterBinding> parameters)
    {
        var builder = new StringBuilder(entryPointPrefix);
        builder.Append('_');
        builder.Append(baseName);

        foreach (var parameter in parameters)
        {
            builder.Append('_');
            builder.Append(SanitizeEntrypointNamePart(parameter.CppTypeName));
        }

        return builder.ToString();
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
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides)
    {
        var type = parameterCursor.Type;
        var normalizedType = type.CanonicalType;
        var explicitDirection = TryGetPassKindOverride(functionCursor, parameterCursor, out var overrideDirection)
            ? overrideDirection
            : (BindingParameterDirection?)null;

        if (normalizedType.kind == CXTypeKind.CXType_LValueReference)
        {
            var pointeeType = normalizedType.PointeeType.CanonicalType;

            if (pointeeType.kind == CXTypeKind.CXType_Record)
            {
                return (
                    MapType(pointeeType, recordsByName, enumsByName, managedTypeOverrides),
                    explicitDirection ?? BindingParameterDirection.In,
                    BindingParameterAbiKind.Value);
            }

            return (
                MapType(pointeeType, recordsByName, enumsByName, managedTypeOverrides),
                explicitDirection ?? (pointeeType.IsConstQualified ? BindingParameterDirection.In : BindingParameterDirection.Ref),
                BindingParameterAbiKind.Reference);
        }

        if (normalizedType.kind == CXTypeKind.CXType_Pointer)
        {
            var pointeeType = normalizedType.PointeeType.CanonicalType;

            if (pointeeType.kind is CXTypeKind.CXType_Void or CXTypeKind.CXType_Record)
            {
                return (
                    MapType(type, recordsByName, enumsByName, managedTypeOverrides),
                    explicitDirection ?? BindingParameterDirection.In,
                    BindingParameterAbiKind.Value);
            }

            return (
                MapType(pointeeType, recordsByName, enumsByName, managedTypeOverrides),
                explicitDirection ?? (pointeeType.IsConstQualified ? BindingParameterDirection.In : BindingParameterDirection.Out),
                BindingParameterAbiKind.Pointer);
        }

        return (MapType(type, recordsByName, enumsByName, managedTypeOverrides), explicitDirection ?? BindingParameterDirection.In, BindingParameterAbiKind.Value);
    }

    private static BindingType MapType(
        CXType type,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
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
                var recordName = NormalizeNamedTypeName(pointeeType.Spelling.ToString());
                if (!recordsByName.TryGetValue(recordName, out var record))
                {
                    record = new RecordBinding($"record:{recordName}", recordName, recordName);
                    recordsByName.Add(recordName, record);
                }

                return record.HandleType;
            }
        }

        if (normalizedType.kind == CXTypeKind.CXType_Record)
        {
            var recordName = NormalizeNamedTypeName(normalizedType.Spelling.ToString());
            if (recordsByName.TryGetValue(recordName, out var record))
            {
                return record.HandleType;
            }
        }

        if (normalizedType.kind == CXTypeKind.CXType_Enum)
        {
            var enumName = NormalizeNamedTypeName(normalizedType.Spelling.ToString());
            if (enumsByName.TryGetValue(enumName, out var @enum))
            {
                return BindingType.CreateEnum(enumName, @enum.ManagedName, @enum.UnderlyingTypeName);
            }

            return BindingType.CreateEnum(enumName, ToPascalCase(enumName), normalizedType.CanonicalType.Spelling.ToString());
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

    private static string NormalizeNamedTypeName(string spelling)
    {
        return spelling
            .Replace("enum class ", string.Empty, StringComparison.Ordinal)
            .Replace("enum ", string.Empty, StringComparison.Ordinal)
            .Replace("const ", string.Empty, StringComparison.Ordinal)
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

    private static bool TryGetPassKindOverride(CXCursor functionCursor, CXCursor parameterCursor, out BindingParameterDirection direction)
    {
        var match = PassKindAttributeExpression.Match(GetSourceText(parameterCursor.TranslationUnit, parameterCursor.Extent));
        if (!match.Success && TryGetParameterDeclarationSourceText(functionCursor, parameterCursor, out var parameterSourceText))
        {
            match = PassKindAttributeExpression.Match(parameterSourceText);
        }

        if (!match.Success)
        {
            direction = default;
            return false;
        }

        direction = match.Groups["kind"].Value switch
        {
            "in" => BindingParameterDirection.In,
            "ref" => BindingParameterDirection.Ref,
            "out" => BindingParameterDirection.Out,
            _ => throw new NotSupportedException($"Unsupported pass-kind attribute '{match.Groups["kind"].Value}'.")
        };
        return true;
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

    private static unsafe void RegisterEnum(CXCursor cursor, BindingCompilation compilation, IDictionary<string, EnumBinding> enumsByName)
    {
        var nativeName = cursor.Spelling.ToString();
        if (string.IsNullOrWhiteSpace(nativeName) || enumsByName.ContainsKey(nativeName))
        {
            return;
        }

        var @enum = new EnumBinding(
            $"enum:{nativeName}",
            nativeName,
            ToPascalCase(nativeName),
            NormalizeTypeName(cursor.EnumDecl_IntegerType.Spelling.ToString()))
        {
            IsFlags = FlagsAttributeExpression.IsMatch(GetSourceText(cursor.TranslationUnit, cursor.Extent))
        };

        cursor.VisitChildren(
            (child, _, _) =>
            {
                if (child.Kind != CXCursorKind.CXCursor_EnumConstantDecl)
                {
                    return CXChildVisitResult.CXChildVisit_Continue;
                }

                @enum.Values.Add(
                    new EnumValueBinding(
                        child.Spelling.ToString(),
                        ToPascalCase(child.Spelling.ToString()),
                        FormatEnumValue(@enum.UnderlyingTypeName, child)));
                return CXChildVisitResult.CXChildVisit_Continue;
            },
            new CXClientData(IntPtr.Zero));

        enumsByName.Add(nativeName, @enum);
        compilation.Members.Add(@enum);
    }

    private static string FormatEnumValue(string underlyingTypeName, CXCursor cursor)
    {
        if (underlyingTypeName.StartsWith("unsigned", StringComparison.Ordinal))
        {
            var value = cursor.EnumConstantDeclUnsignedValue;
            return underlyingTypeName switch
            {
                "unsigned int" => $"{value}u",
                "unsigned long long" => $"{value}UL",
                "unsigned short" => value.ToString(),
                _ => value.ToString()
            };
        }

        return cursor.EnumConstantDeclValue.ToString();
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

    private static string? GetManagedDefaultValueExpression(CXCursor parameterCursor, BindingType parameterType)
    {
        if (!parameterCursor.HasDefaultArg)
        {
            return null;
        }

        var defaultArgumentCursor = parameterCursor.DefaultArg;
        if (defaultArgumentCursor.Kind == CXCursorKind.CXCursor_FirstInvalid && parameterCursor.HasUninstantiatedDefaultArg)
        {
            defaultArgumentCursor = parameterCursor.UninstantiatedDefaultArg;
        }

        if (defaultArgumentCursor.Kind == CXCursorKind.CXCursor_FirstInvalid)
        {
            return null;
        }

        var asWrittenCursor = defaultArgumentCursor.SubExprAsWritten;
        var expressionCursor = asWrittenCursor.Kind == CXCursorKind.CXCursor_FirstInvalid
            ? defaultArgumentCursor
            : asWrittenCursor;
        var expression = GetSourceText(expressionCursor.TranslationUnit, expressionCursor.Extent);

        if (parameterType.Kind == BindingTypeKind.Enum)
        {
            return NormalizeManagedEnumDefaultExpression(parameterType.ManagedTypeName, expression);
        }

        return expressionCursor.Kind switch
        {
            CXCursorKind.CXCursor_CXXBoolLiteralExpr => expression.ToLowerInvariant(),
            _ => expression
        };
    }

    private static string NormalizeManagedEnumDefaultExpression(string managedTypeName, string expression)
    {
        var separatorIndex = expression.LastIndexOf("::", StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            return expression;
        }

        var valueName = expression[(separatorIndex + 2)..];
        return $"{managedTypeName}.{ToPascalCase(valueName)}";
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
