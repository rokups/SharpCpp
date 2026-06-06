using ClangSharp.Interop;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

using SharpCpp.Metadata.Import;
using SharpCpp.Model.Ast;

namespace SharpCpp.Parsing;

public sealed class ClangSharpHeaderParser
{
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
    private static readonly Regex IgnoreAttributeExpression = new(
        @"\[\[\s*sharpcpp::ignore(?:\s*\(\s*""(?<reason>[^""]*)""\s*\))?\s*\]\]",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex DirectorAttributeExpression = new(
        @"\[\[\s*sharpcpp::director\s*\]\]",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex RenameAttributeExpression = new(
        @"\[\[\s*sharpcpp::rename\s*\(\s*""(?<value>[^""]*)""\s*\)\s*\]\]",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex InstantiateAttributeExpression = new(
        @"\[\[\s*sharpcpp::instantiate\s*\((?<body>.*?)\)\s*\]\]",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex InstantiateArgumentExpression = new(
        @"""(?<value>[^""\\]*(?:\\.[^""\\]*)*)""",
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

        var ownedFileRoots = NormalizeOwnedFileRoots(headerFilePath, request.OwnedFileRoots);
        var annotationFiles = EnumerateOwnedHeaderFiles(headerFilePath, ownedFileRoots);

        PreScanIgnoreAnnotations(annotationFiles);
        PreScanRenameAnnotations(annotationFiles);
        PreScanInstantiateAnnotations(annotationFiles);

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

        request.ImportedBindingMetadata?.Seed(compilation, recordsByName, enumsByName);

        translationUnit.Cursor.VisitChildren(
            (child, _, _) =>
            {
                if (!IsOwnedCursor(child, ownedFileRoots))
                {
                    return CXChildVisitResult.CXChildVisit_Continue;
                }

                switch (child.Kind)
                {
                    case CXCursorKind.CXCursor_Namespace:
                        VisitNamespace(child, compilation, recordsByName, enumsByName, managedTypeOverrides, request.EntryPointPrefix, ownedFileRoots, request.HeaderIncludePath);
                        break;

                    case CXCursorKind.CXCursor_StructDecl:
                    case CXCursorKind.CXCursor_ClassDecl:
                        RegisterRecord(child, compilation, recordsByName, enumsByName, managedTypeOverrides, request.EntryPointPrefix,
                            ResolveOwnedHeaderIncludePath(child, ownedFileRoots, request.HeaderIncludePath));
                        break;

                    case CXCursorKind.CXCursor_ClassTemplate:
                        RegisterTemplateRecord(child, compilation, recordsByName, enumsByName, managedTypeOverrides, request.EntryPointPrefix,
                            ResolveOwnedHeaderIncludePath(child, ownedFileRoots, request.HeaderIncludePath));
                        break;

                    case CXCursorKind.CXCursor_EnumDecl:
                        RegisterEnum(child, compilation, enumsByName,
                            ResolveOwnedHeaderIncludePath(child, ownedFileRoots, request.HeaderIncludePath));
                        break;

                    case CXCursorKind.CXCursor_FunctionDecl:
                    {
                        var function = CreateFunction(child, request.EntryPointPrefix, recordsByName, enumsByName, managedTypeOverrides, EmptyGenericParameters);
                        CheckAndApplyIgnoreAttribute(child, function);
                        TryApplyRename(child, function);
                        ExtractAndApplyDocumentation(child, function);
                        ApplyCallableDefinitionMetadata(child, function);
                        ApplyDeletedCallableIgnore(child, function);
                        function.HeaderIncludePath = ResolveOwnedHeaderIncludePath(child, ownedFileRoots, request.HeaderIncludePath);
                        compilation.Members.Add(function);
                        break;
                    }

                    case CXCursorKind.CXCursor_FunctionTemplate:
                    {
                        var function = CreateTemplateFunction(child, request.EntryPointPrefix, recordsByName, enumsByName, managedTypeOverrides);
                        if (function is not null)
                        {
                            CheckAndApplyIgnoreAttribute(child, function);
                            TryApplyRename(child, function);
                            ExtractAndApplyDocumentation(child, function);
                            ApplyCallableDefinitionMetadata(child, function);
                            ApplyDeletedCallableIgnore(child, function);
                            function.HeaderIncludePath = ResolveOwnedHeaderIncludePath(child, ownedFileRoots, request.HeaderIncludePath);
                            compilation.Members.Add(function);
                        }
                        break;
                    }
                }

                return CXChildVisitResult.CXChildVisit_Continue;
            },
            new CXClientData(IntPtr.Zero));

        foreach (var record in recordsByName.Values)
        {
            if (!compilation.Members.Contains(record))
            {
                compilation.Members.Add(record);
            }
        }

        AssignImportMethodNames(compilation);

        return compilation;
    }

    private static void AssignImportMethodNames(BindingCompilation compilation)
    {
        foreach (var group in compilation.Members.OfType<FunctionBinding>()
            .Where(static function => function.Parent is not RecordBinding)
            .GroupBy(static function => function.ManagedName, StringComparer.Ordinal))
        {
            var counter = 0;
            foreach (var function in group.OrderBy(static f => f.SymbolId, StringComparer.Ordinal))
            {
                counter++;
                function.ImportMethodName = counter == 1 ? function.ManagedName : $"{function.ManagedName}{counter}";
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
                    function.ImportMethodName = counter == 1 ? function.ManagedName : $"{function.ManagedName}{counter}";
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

    private static bool IsOwnedCursor(CXCursor cursor, IReadOnlyList<string> ownedFileRoots)
    {
        cursor.Location.GetFileLocation(out var file, out _, out _, out _);
        var filePath = Path.GetFullPath(file.Name.CString);

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

    private static string ResolveOwnedHeaderIncludePath(CXCursor cursor, IReadOnlyList<string> ownedFileRoots, string fallbackHeaderIncludePath)
    {
        cursor.Location.GetFileLocation(out var file, out _, out _, out _);
        var fileName = file.Name.CString;
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return fallbackHeaderIncludePath;
        }

        var filePath = Path.GetFullPath(fileName);
        foreach (var ownedFileRoot in ownedFileRoots)
        {
            if (!Directory.Exists(ownedFileRoot) ||
                !filePath.StartsWith(ownedFileRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                continue;
            }

            return Path.GetRelativePath(ownedFileRoot, filePath).Replace('\\', '/');
        }

        return fallbackHeaderIncludePath;
    }

    private static unsafe void RegisterRecord(
        CXCursor cursor,
        BindingCompilation compilation,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        string entryPointPrefix,
        string? headerIncludePath)
    {
        var nativeName = cursor.Spelling.ToString();
        if (string.IsNullOrWhiteSpace(nativeName))
        {
            return;
        }

        var cppQualifiedName = GetCppQualifiedName(cursor);
        var sourceText = GetSourceText(cursor.TranslationUnit, cursor.Extent);
        var braceIndex = sourceText.IndexOf('{');
        var declarationText = braceIndex >= 0
            ? sourceText[..braceIndex]
            : sourceText;
        var isForwardDeclaration = braceIndex < 0;

        if (recordsByName.TryGetValue(nativeName, out var ghostRecord) &&
            ghostRecord.GetMetadata<bool>(MetadataKeys.PlaceholderRecord))
        {
            ghostRecord.Detach();
            recordsByName.Remove(nativeName);
        }
        else if (ghostRecord is not null &&
                 ghostRecord.GetMetadata<bool>(MetadataKeys.ForwardDeclaration) &&
                 !isForwardDeclaration)
        {
            ghostRecord.Detach();
            recordsByName.Remove(nativeName);
        }
        else if (ghostRecord is not null)
        {
            return;
        }
        var record = new RecordBinding($"record:{nativeName}", nativeName, ToPascalCase(nativeName), cppQualifiedName)
        {
            IsAbstract = cursor.CXXRecord_IsAbstract,
            IsDirector = DirectorAttributeExpression.IsMatch(declarationText)
        };
        if (isForwardDeclaration)
        {
            record.SetMetadata(MetadataKeys.ForwardDeclaration, true);
        }
        CheckAndApplyIgnoreAttribute(cursor, record);
        TryApplyRename(cursor, record);
        ExtractAndApplyDocumentation(cursor, record);
        recordsByName.Add(nativeName, record);
        record.HeaderIncludePath = headerIncludePath;
        compilation.Members.Add(record);

        ApplyDestructorMetadata(cursor, record);
        PopulateRecordBases(cursor, record, recordsByName);
        PopulateRecordMembers(cursor, record, recordsByName, enumsByName, managedTypeOverrides, EmptyGenericParameters, entryPointPrefix);

        UpdatePolymorphism(record, recordsByName);
    }

    private static unsafe void RegisterTemplateRecord(
        CXCursor cursor,
        BindingCompilation compilation,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        string entryPointPrefix,
        string? headerIncludePath)
    {
        var nativeName = cursor.Spelling.ToString();
        if (string.IsNullOrWhiteSpace(nativeName))
        {
            return;
        }

        var sourceText = GetSourceText(cursor.TranslationUnit, cursor.Extent);
        var braceIndex = sourceText.IndexOf('{');
        var isForwardDeclaration = braceIndex < 0;

        if (recordsByName.TryGetValue(nativeName, out var ghostRecord) &&
            ghostRecord.GetMetadata<bool>(MetadataKeys.PlaceholderRecord))
        {
            ghostRecord.Detach();
            recordsByName.Remove(nativeName);
        }
        else if (ghostRecord is not null &&
                 ghostRecord.GetMetadata<bool>(MetadataKeys.ForwardDeclaration) &&
                 !isForwardDeclaration)
        {
            ghostRecord.Detach();
            recordsByName.Remove(nativeName);
        }
        else if (ghostRecord is not null)
        {
            return;
        }

        if (!TryExtractTemplateParameters(cursor, out var templateParameters))
        {
            return;
        }

        var cppQualifiedName = GetCppQualifiedName(cursor);
        var declarationText = braceIndex >= 0
            ? sourceText[..braceIndex]
            : sourceText;
        var record = new RecordBinding($"record:{nativeName}", nativeName, ToPascalCase(nativeName), cppQualifiedName)
        {
            IsAbstract = cursor.CXXRecord_IsAbstract,
            IsDirector = DirectorAttributeExpression.IsMatch(declarationText)
        };
        if (isForwardDeclaration)
        {
            record.SetMetadata(MetadataKeys.ForwardDeclaration, true);
        }

        foreach (var templateParameter in templateParameters)
        {
            record.GenericParameters.Add(templateParameter);
        }

        ApplyExplicitGenericInstantiations(cursor, record, recordsByName, enumsByName, managedTypeOverrides);
        CheckAndApplyIgnoreAttribute(cursor, record);
        TryApplyRename(cursor, record);
        ExtractAndApplyDocumentation(cursor, record);
        recordsByName.Add(nativeName, record);
        record.HeaderIncludePath = headerIncludePath;
        compilation.Members.Add(record);

        var genericParameters = CreateGenericParameterScope(templateParameters);
        ApplyDestructorMetadata(cursor, record);
        PopulateRecordBases(cursor, record, recordsByName);
        PopulateRecordMembers(cursor, record, recordsByName, enumsByName, managedTypeOverrides, genericParameters, entryPointPrefix);

        UpdatePolymorphism(record, recordsByName);
    }

    private static unsafe void PopulateRecordBases(
        CXCursor cursor,
        RecordBinding record,
        IDictionary<string, RecordBinding> recordsByName)
    {
        cursor.VisitChildren(
            (child, _, _) =>
            {
                if (child.Kind != CXCursorKind.CXCursor_CXXBaseSpecifier)
                {
                    return CXChildVisitResult.CXChildVisit_Continue;
                }

                var baseTypeName = GetRecordQualifiedName(child.Type, recordsByName);
                if (!recordsByName.TryGetValue(baseTypeName, out var baseRecord))
                {
                    var baseQualifiedName = NormalizeNamedTypeName(child.Type.Spelling.ToString());
                    baseRecord = new RecordBinding($"record:{baseTypeName}", baseTypeName, ToPascalCase(baseTypeName), baseQualifiedName);
                    baseRecord.SetMetadata(MetadataKeys.PlaceholderRecord, true);
                    recordsByName.Add(baseTypeName, baseRecord);
                }

                record.Bases.Add(new RecordBaseBinding(baseRecord.SymbolId, baseRecord.NativeName, baseRecord.ManagedName, baseRecord.CppQualifiedName, child.IsVirtualBase));

                if (record.ManagedBaseTypeName is null && !child.IsVirtualBase)
                {
                    record.ManagedBaseTypeName = baseRecord.ManagedName;
                }

                return CXChildVisitResult.CXChildVisit_Continue;
            },
            new CXClientData(IntPtr.Zero));
    }

    private static unsafe void PopulateRecordMembers(
        CXCursor cursor,
        RecordBinding record,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        IReadOnlyDictionary<string, BindingType> genericParameters,
        string entryPointPrefix)
    {
        cursor.VisitChildren(
            (child, _, _) =>
            {
                if ((child.Kind != CXCursorKind.CXCursor_CXXMethod && child.Kind != CXCursorKind.CXCursor_ConversionFunction) ||
                    child.CXXMethod_IsStatic)
                {
                    return CXChildVisitResult.CXChildVisit_Continue;
                }

                if (!ShouldParseRecordMember(record, child.CXXAccessSpecifier))
                {
                    return CXChildVisitResult.CXChildVisit_Continue;
                }

                var method = CreateMethod(record, child, recordsByName, enumsByName, managedTypeOverrides, genericParameters, entryPointPrefix);
                CheckAndApplyIgnoreAttribute(child, method);
                TryApplyRename(child, method);
                ExtractAndApplyDocumentation(child, method);
                ApplyCallableDefinitionMetadata(child, method);
                ApplyDeletedCallableIgnore(child, method);
                ApplyVirtualSpecifiers(child, method);
                record.Members.Add(method);
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

                if (!ShouldParseRecordMember(record, child.CXXAccessSpecifier))
                {
                    return CXChildVisitResult.CXChildVisit_Continue;
                }

                var staticMethod = CreateStaticMethod(record, child, recordsByName, enumsByName, managedTypeOverrides, genericParameters, entryPointPrefix);
                CheckAndApplyIgnoreAttribute(child, staticMethod);
                TryApplyRename(child, staticMethod);
                ExtractAndApplyDocumentation(child, staticMethod);
                ApplyCallableDefinitionMetadata(child, staticMethod);
                ApplyDeletedCallableIgnore(child, staticMethod);
                record.Members.Add(staticMethod);
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

                if (!ShouldParseRecordMember(record, child.CXXAccessSpecifier))
                {
                    return CXChildVisitResult.CXChildVisit_Continue;
                }

                var ctor = CreateConstructor(record, child, recordsByName, enumsByName, managedTypeOverrides, genericParameters, entryPointPrefix);
                CheckAndApplyIgnoreAttribute(child, ctor);
                ExtractAndApplyDocumentation(child, ctor);
                ApplyCallableDefinitionMetadata(child, ctor);

                if (record.IsAbstract && !record.IsDirector)
                {
                    ctor.SetIgnoreReason($"constructors on abstract record '{record.CppQualifiedName}' are not materializable in this bootstrap surface");
                }
                else if (record.GetMetadata<bool>(MetadataKeys.NonPublicDestructor))
                {
                    ctor.SetIgnoreReason($"constructors on record '{record.CppQualifiedName}' with a non-public destructor are not materializable in this bootstrap surface");
                }

                ApplyDeletedCallableIgnore(child, ctor);

                record.Members.Add(ctor);
                return CXChildVisitResult.CXChildVisit_Continue;
            },
            new CXClientData(IntPtr.Zero));

        cursor.VisitChildren(
            (child, _, _) =>
            {
                if (child.Kind != CXCursorKind.CXCursor_FunctionTemplate)
                {
                    return CXChildVisitResult.CXChildVisit_Continue;
                }

                var templateMethod = CreateTemplateMethod(record, child, recordsByName, enumsByName, managedTypeOverrides, genericParameters, entryPointPrefix);
                if (templateMethod is null)
                {
                    return CXChildVisitResult.CXChildVisit_Continue;
                }

                CheckAndApplyIgnoreAttribute(child, templateMethod);
                TryApplyRename(child, templateMethod);
                ExtractAndApplyDocumentation(child, templateMethod);
                ApplyCallableDefinitionMetadata(child, templateMethod);
                ApplyDeletedCallableIgnore(child, templateMethod);
                record.Members.Add(templateMethod);
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

                if (!ShouldParseRecordMember(record, child.CXXAccessSpecifier))
                {
                    return CXChildVisitResult.CXChildVisit_Continue;
                }

                var field = CreateField(record, child, recordsByName, enumsByName, managedTypeOverrides, genericParameters);
                CheckAndApplyIgnoreAttribute(child, field);
                TryApplyRename(child, field);
                ExtractAndApplyDocumentation(child, field);
                record.Members.Add(field);
                return CXChildVisitResult.CXChildVisit_Continue;
            },
            new CXClientData(IntPtr.Zero));
    }

    private static FunctionBinding CreateMethod(
        RecordBinding record,
        CXCursor cursor,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        IReadOnlyDictionary<string, BindingType> genericParameters,
        string entryPointPrefix)
    {
        var nativeName = cursor.Spelling.ToString();
        var (returnType, ignoreReason) = TryMapType(cursor.ResultType, recordsByName, enumsByName, managedTypeOverrides, genericParameters);
        var cppReturnTypeName = ResolveQualifiedTypeName(cursor.ResultType, recordsByName);
        var function = new FunctionBinding(
            $"method:{record.NativeName}:{nativeName}",
            nativeName,
            GetManagedMethodName(cursor, nativeName),
            returnType,
            entryPointName: $"{entryPointPrefix}_{record.NativeName}_{GetEntrypointSuffix(cursor, nativeName)}",
            cppReturnTypeName: cppReturnTypeName,
            cppQualifiedName: $"{record.CppQualifiedName}::{nativeName}")
        {
            IsStatic = false,
            AccessModifier = MapAccessModifier(cursor.CXXAccessSpecifier),
            IsVirtual = cursor.CXXMethod_IsVirtual,
            IsPureVirtual = cursor.CXXMethod_IsPureVirtual,
            IsConstMethod = clang.CXXMethod_isConst(cursor) != 0,
            OwningRecordCppTypeName = record.CppQualifiedName
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
            var managedParameterName = EscapeManagedIdentifier(parameterName);

            var (parameterType, direction, abiKind, parameterIgnoreReason) = TryMapParameter(cursor, parameterCursor, recordsByName, enumsByName, managedTypeOverrides, genericParameters);
            ignoreReason ??= parameterIgnoreReason;
            var param = new ParameterBinding(
                $"parameter:{record.NativeName}:{nativeName}:{parameterName}",
                parameterName,
                managedParameterName,
                parameterType,
                direction,
                abiKind,
                ResolveQualifiedTypeName(parameterCursor.Type, recordsByName));
            ApplyManagedDefaultValue(param, parameterCursor, parameterType);
            function.Parameters.Add(param);
        }

        function.EntryPointName = BuildEntrypointName(entryPointPrefix, $"{SanitizeEntrypointNamePart(record.CppQualifiedName)}_{GetEntrypointSuffix(cursor, nativeName)}", function.Parameters);

        if (ignoreReason is not null)
        {
            function.SetIgnoreReason(ignoreReason);
        }

        return function;
    }

    private static unsafe FunctionBinding? CreateTemplateMethod(
        RecordBinding record,
        CXCursor templateCursor,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        IReadOnlyDictionary<string, BindingType> genericParameters,
        string entryPointPrefix)
    {
        if (!TryExtractTemplateParameters(templateCursor, out var methodTemplateParameters))
        {
            return null;
        }

        if (!ShouldParseRecordMember(record, templateCursor.CXXAccessSpecifier))
        {
            return null;
        }

        var combinedGenericParameters = new Dictionary<string, BindingType>(genericParameters, StringComparer.Ordinal);
        foreach (var templateParameter in methodTemplateParameters)
        {
            combinedGenericParameters[templateParameter.Name] = BindingType.CreateGenericParameter(templateParameter.Name);
        }

        var nativeName = templateCursor.Spelling.ToString();
        var (returnType, ignoreReason) = TryMapType(templateCursor.ResultType, recordsByName, enumsByName, managedTypeOverrides, combinedGenericParameters);
        var cppReturnTypeName = ResolveQualifiedTypeName(templateCursor.ResultType, recordsByName);
        var function = new FunctionBinding(
            $"method:{record.NativeName}:{nativeName}",
            nativeName,
            GetManagedMethodName(templateCursor, nativeName),
            returnType,
            cppReturnTypeName: cppReturnTypeName,
            cppQualifiedName: $"{record.CppQualifiedName}::{nativeName}")
        {
            IsStatic = false,
            AccessModifier = MapAccessModifier(templateCursor.CXXAccessSpecifier),
            IsVirtual = templateCursor.CXXMethod_IsVirtual,
            IsPureVirtual = templateCursor.CXXMethod_IsPureVirtual,
            IsConstMethod = clang.CXXMethod_isConst(templateCursor) != 0,
            OwningRecordCppTypeName = record.CppQualifiedName
        };

        foreach (var templateParameter in methodTemplateParameters)
        {
            function.GenericParameters.Add(templateParameter);
        }

        ApplyExplicitGenericInstantiations(templateCursor, function, recordsByName, enumsByName, managedTypeOverrides);

        templateCursor.VisitChildren(
            (child, _, _) =>
            {
                if (child.Kind != CXCursorKind.CXCursor_ParmDecl)
                {
                    return CXChildVisitResult.CXChildVisit_Continue;
                }

                var parameterName = child.Spelling.ToString();
                if (string.IsNullOrWhiteSpace(parameterName))
                {
                    parameterName = $"arg{function.Parameters.Count}";
                }
                var managedParameterName = EscapeManagedIdentifier(parameterName);

                var (parameterType, direction, abiKind, parameterIgnoreReason) = TryMapParameter(templateCursor, child, recordsByName, enumsByName, managedTypeOverrides, combinedGenericParameters);
                ignoreReason ??= parameterIgnoreReason;
                var templateParam = new ParameterBinding(
                    $"parameter:{record.NativeName}:{nativeName}:{parameterName}",
                    parameterName,
                    managedParameterName,
                    parameterType,
                    direction,
                    abiKind,
                    ResolveQualifiedTypeName(child.Type, recordsByName));
                ApplyManagedDefaultValue(templateParam, child, parameterType);
                function.Parameters.Add(templateParam);

                return CXChildVisitResult.CXChildVisit_Continue;
            },
            new CXClientData(IntPtr.Zero));

        function.EntryPointName = BuildEntrypointName(entryPointPrefix, $"{SanitizeEntrypointNamePart(record.CppQualifiedName)}_{GetEntrypointSuffix(templateCursor, nativeName)}", function.Parameters);

        if (ignoreReason is not null)
        {
            function.SetIgnoreReason(ignoreReason);
        }

        ApplyVirtualSpecifiers(templateCursor, function);
        return function;
    }

    private static FunctionBinding CreateStaticMethod(
        RecordBinding record,
        CXCursor cursor,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        IReadOnlyDictionary<string, BindingType> genericParameters,
        string entryPointPrefix)
    {
        var nativeName = cursor.Spelling.ToString();
        var (returnType, ignoreReason) = TryMapType(cursor.ResultType, recordsByName, enumsByName, managedTypeOverrides, genericParameters);
        var cppReturnTypeName = ResolveQualifiedTypeName(cursor.ResultType, recordsByName);
        var function = new FunctionBinding(
            $"static-method:{record.NativeName}:{nativeName}",
            nativeName,
            GetManagedMethodName(cursor, nativeName),
            returnType,
            entryPointName: $"{entryPointPrefix}_{record.NativeName}_{GetEntrypointSuffix(cursor, nativeName)}",
            cppReturnTypeName: cppReturnTypeName,
            cppQualifiedName: $"{record.CppQualifiedName}::{nativeName}")
        {
            IsStatic = true,
            AccessModifier = MapAccessModifier(cursor.CXXAccessSpecifier),
            OwningRecordCppTypeName = record.CppQualifiedName
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
            var managedParameterName = EscapeManagedIdentifier(parameterName);

            var (parameterType, direction, abiKind, parameterIgnoreReason) = TryMapParameter(cursor, parameterCursor, recordsByName, enumsByName, managedTypeOverrides, genericParameters);
            ignoreReason ??= parameterIgnoreReason;
            var staticParam = new ParameterBinding(
                $"parameter:{record.NativeName}:static:{nativeName}:{parameterName}",
                parameterName,
                managedParameterName,
                parameterType,
                direction,
                abiKind,
                ResolveQualifiedTypeName(parameterCursor.Type, recordsByName));
            ApplyManagedDefaultValue(staticParam, parameterCursor, parameterType);
            function.Parameters.Add(staticParam);
        }

        function.EntryPointName = BuildEntrypointName(entryPointPrefix, $"{SanitizeEntrypointNamePart(record.CppQualifiedName)}_{GetEntrypointSuffix(cursor, nativeName)}", function.Parameters);

        if (ignoreReason is not null)
        {
            function.SetIgnoreReason(ignoreReason);
        }

        return function;
    }

    private static FunctionBinding CreateConstructor(
        RecordBinding record,
        CXCursor cursor,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        IReadOnlyDictionary<string, BindingType> genericParameters,
        string entryPointPrefix)
    {
        var function = new FunctionBinding(
            $"ctor:{record.NativeName}:{cursor.Hash}",
            record.NativeName,
            record.ManagedName,
            record.HandleType,
            entryPointName: $"{entryPointPrefix}_{record.NativeName}_ctor",
            cppReturnTypeName: $"{record.CppQualifiedName}*",
            cppQualifiedName: $"{record.CppQualifiedName}::{record.NativeName}")
        {
            IsStatic = false,
            AccessModifier = MapAccessModifier(cursor.CXXAccessSpecifier),
            IsConstructor = true,
            OwningRecordCppTypeName = record.CppQualifiedName
        };

        string? ignoreReason = null;

        for (uint parameterIndex = 0; parameterIndex < cursor.NumArguments; parameterIndex++)
        {
            var parameterCursor = cursor.GetArgument(parameterIndex);
            var parameterName = parameterCursor.Spelling.ToString();
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                parameterName = $"arg{parameterIndex}";
            }
            var managedParameterName = EscapeManagedIdentifier(parameterName);

            var (parameterType, direction, abiKind, parameterIgnoreReason) = TryMapParameter(cursor, parameterCursor, recordsByName, enumsByName, managedTypeOverrides, genericParameters);
            ignoreReason ??= parameterIgnoreReason;
            var ctorParam = new ParameterBinding(
                $"parameter:{record.NativeName}:ctor:{parameterName}",
                parameterName,
                managedParameterName,
                parameterType,
                direction,
                abiKind,
                ResolveQualifiedTypeName(parameterCursor.Type, recordsByName));
            ApplyManagedDefaultValue(ctorParam, parameterCursor, parameterType);
            function.Parameters.Add(ctorParam);
        }

        function.EntryPointName = BuildEntrypointName(entryPointPrefix, $"{SanitizeEntrypointNamePart(record.CppQualifiedName)}_ctor", function.Parameters);

        if (ignoreReason is not null)
        {
            function.SetIgnoreReason(ignoreReason);
        }

        return function;
    }

    private static FieldBinding CreateField(
        RecordBinding record,
        CXCursor cursor,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        IReadOnlyDictionary<string, BindingType> genericParameters)
    {
        var nativeName = cursor.Spelling.ToString();
        var (fieldType, ignoreReason) = TryMapType(cursor.Type, recordsByName, enumsByName, managedTypeOverrides, genericParameters);
        var field = new FieldBinding(
            $"field:{record.NativeName}:{nativeName}",
            nativeName,
            ToPascalCase(nativeName),
            fieldType,
            ResolveQualifiedTypeName(cursor.Type, recordsByName),
            record.CppQualifiedName,
            isReadOnly: cursor.Type.IsConstQualified)
        {
            AccessModifier = MapAccessModifier(cursor.CXXAccessSpecifier)
        };

        if (ignoreReason is not null)
        {
            field.SetIgnoreReason(ignoreReason);
        }

        return field;
    }

    private static FunctionBinding CreateFunction(
        CXCursor cursor,
        string entryPointPrefix,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        IReadOnlyDictionary<string, BindingType> genericParameters)
    {
        var nativeName = cursor.Spelling.ToString();
        var (returnType, ignoreReason) = TryMapType(cursor.ResultType, recordsByName, enumsByName, managedTypeOverrides, genericParameters);
        var cppReturnTypeName = ResolveQualifiedTypeName(cursor.ResultType, recordsByName);
        var function = new FunctionBinding(
            $"function:{nativeName}",
            nativeName,
            ToPascalCase(nativeName),
            returnType,
            entryPointName: $"{entryPointPrefix}_{nativeName}",
            cppReturnTypeName: cppReturnTypeName,
            cppQualifiedName: GetCppQualifiedName(cursor));

        for (uint parameterIndex = 0; parameterIndex < cursor.NumArguments; parameterIndex++)
        {
            var parameterCursor = cursor.GetArgument(parameterIndex);
            var parameterName = parameterCursor.Spelling.ToString();
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                parameterName = $"arg{parameterIndex}";
            }
            var managedParameterName = EscapeManagedIdentifier(parameterName);

            var (parameterType, direction, abiKind, parameterIgnoreReason) = TryMapParameter(cursor, parameterCursor, recordsByName, enumsByName, managedTypeOverrides, genericParameters);
            ignoreReason ??= parameterIgnoreReason;
            var freeParam = new ParameterBinding(
                $"parameter:{nativeName}:{parameterName}",
                parameterName,
                managedParameterName,
                parameterType,
                direction,
                abiKind,
                ResolveQualifiedTypeName(parameterCursor.Type, recordsByName));
            ApplyManagedDefaultValue(freeParam, parameterCursor, parameterType);
            function.Parameters.Add(freeParam);
        }

        function.EntryPointName = BuildEntrypointName(entryPointPrefix, SanitizeEntrypointNamePart(function.CppQualifiedName), function.Parameters);

        if (ignoreReason is not null)
        {
            function.SetIgnoreReason(ignoreReason);
        }

        return function;
    }

    private static unsafe FunctionBinding? CreateTemplateFunction(
        CXCursor templateCursor,
        string entryPointPrefix,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides)
    {
        if (!TryExtractTemplateParameters(templateCursor, out var templateParameters))
        {
            return null;
        }

        var genericParameters = CreateGenericParameterScope(templateParameters);
        var nativeName = templateCursor.Spelling.ToString();
        if (string.IsNullOrWhiteSpace(nativeName))
        {
            return null;
        }

        var (returnType, ignoreReason) = TryMapType(templateCursor.ResultType, recordsByName, enumsByName, managedTypeOverrides, genericParameters);
        var cppReturnTypeName = ResolveQualifiedTypeName(templateCursor.ResultType, recordsByName);
        var function = new FunctionBinding(
            $"function:{nativeName}",
            nativeName,
            ToPascalCase(nativeName),
            returnType,
            cppReturnTypeName: cppReturnTypeName,
            cppQualifiedName: GetCppQualifiedName(templateCursor));

        foreach (var templateParameter in templateParameters)
        {
            function.GenericParameters.Add(templateParameter);
        }

        ApplyExplicitGenericInstantiations(templateCursor, function, recordsByName, enumsByName, managedTypeOverrides);

        templateCursor.VisitChildren(
            (child, _, _) =>
            {
                if (child.Kind != CXCursorKind.CXCursor_ParmDecl)
                {
                    return CXChildVisitResult.CXChildVisit_Continue;
                }

                var parameterName = child.Spelling.ToString();
                if (string.IsNullOrWhiteSpace(parameterName))
                {
                    parameterName = $"arg{function.Parameters.Count}";
                }
                var managedParameterName = EscapeManagedIdentifier(parameterName);

                var (parameterType, direction, abiKind, parameterIgnoreReason) = TryMapParameter(templateCursor, child, recordsByName, enumsByName, managedTypeOverrides, genericParameters);
                ignoreReason ??= parameterIgnoreReason;
                var freeTemplateParam = new ParameterBinding(
                    $"parameter:{nativeName}:{parameterName}",
                    parameterName,
                    managedParameterName,
                    parameterType,
                    direction,
                    abiKind,
                    ResolveQualifiedTypeName(child.Type, recordsByName));
                ApplyManagedDefaultValue(freeTemplateParam, child, parameterType);
                function.Parameters.Add(freeTemplateParam);

                return CXChildVisitResult.CXChildVisit_Continue;
            },
            new CXClientData(IntPtr.Zero));

        function.EntryPointName = BuildEntrypointName(entryPointPrefix, SanitizeEntrypointNamePart(function.CppQualifiedName), function.Parameters);

        if (ignoreReason is not null)
        {
            function.SetIgnoreReason(ignoreReason);
        }

        return function;
    }

    private static (BindingType Type, string? IgnoreReason) TryMapType(
        CXType type,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        IReadOnlyDictionary<string, BindingType> genericParameters)
    {
        try
        {
            return (MapType(type, recordsByName, enumsByName, managedTypeOverrides, genericParameters), null);
        }
        catch (NotSupportedException exception)
        {
            return (CreateIgnoredFallbackType(type), exception.Message);
        }
    }

    private static (BindingType Type, BindingParameterDirection Direction, BindingParameterAbiKind AbiKind, string? IgnoreReason) TryMapParameter(
        CXCursor functionCursor,
        CXCursor parameterCursor,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        IReadOnlyDictionary<string, BindingType> genericParameters)
    {
        try
        {
            var (type, direction, abiKind) = MapParameter(functionCursor, parameterCursor, recordsByName, enumsByName, managedTypeOverrides, genericParameters);
            return (type, direction, abiKind, null);
        }
        catch (NotSupportedException exception)
        {
            var (direction, abiKind) = InferFallbackParameterPassing(functionCursor, parameterCursor);
            return (CreateIgnoredFallbackType(parameterCursor.Type), direction, abiKind, exception.Message);
        }
    }

    private static (BindingParameterDirection Direction, BindingParameterAbiKind AbiKind) InferFallbackParameterPassing(CXCursor functionCursor, CXCursor parameterCursor)
    {
        var type = parameterCursor.Type;
        var normalizedType = type.CanonicalType;
        var explicitDirection = TryGetPassKindOverride(functionCursor, parameterCursor, out var overrideDirection)
            ? overrideDirection
            : (BindingParameterDirection?)null;

        if (normalizedType.kind == CXTypeKind.CXType_LValueReference)
        {
            var sourcePointeeType = type.PointeeType;
            var normalizedPointeeType = normalizedType.PointeeType.CanonicalType;
            return (
                explicitDirection ?? (sourcePointeeType.IsConstQualified ? BindingParameterDirection.In : BindingParameterDirection.Ref),
                normalizedPointeeType.kind == CXTypeKind.CXType_Record ? BindingParameterAbiKind.Value : BindingParameterAbiKind.Reference);
        }

        if (normalizedType.kind == CXTypeKind.CXType_Pointer)
        {
            var sourcePointeeType = type.PointeeType;
            var normalizedPointeeType = normalizedType.PointeeType.CanonicalType;
            return (
                explicitDirection ?? (sourcePointeeType.IsConstQualified ? BindingParameterDirection.In : BindingParameterDirection.Out),
                normalizedPointeeType.kind is CXTypeKind.CXType_Void or CXTypeKind.CXType_Record ? BindingParameterAbiKind.Value : BindingParameterAbiKind.Pointer);
        }

        return (explicitDirection ?? BindingParameterDirection.In, BindingParameterAbiKind.Value);
    }

    private static BindingType CreateIgnoredFallbackType(CXType type)
    {
        var normalizedType = type.CanonicalType;
        var normalizedTypeName = NormalizeTypeName(type.Spelling.ToString());

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
            _ => BindingType.CreateConfigured(normalizedTypeName, "global::System.IntPtr")
        };
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
            CX_OverloadedOperatorKind.CX_OO_Exclaim => "!",
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
            return $"OperatorTo{ToPascalCase(SanitizeEntrypointNamePart(cursor.ResultType.Spelling.ToString()))}";
        }

        if (cursor.IsOverloadedOperator)
        {
            return cursor.OverloadedOperatorKind switch
            {
                CX_OverloadedOperatorKind.CX_OO_Plus => cursor.NumArguments == 0 ? "OperatorUnaryPlus" : "OperatorPlus",
                CX_OverloadedOperatorKind.CX_OO_Minus => cursor.NumArguments == 0 ? "OperatorUnaryMinus" : "OperatorMinus",
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

    private static string GetEntrypointSuffix(CXCursor cursor, string nativeName)
    {
        var constSuffix = clang.CXXMethod_isConst(cursor) != 0 ? "_const" : string.Empty;

        if (cursor.Kind == CXCursorKind.CXCursor_ConversionFunction)
        {
            return $"operator_to_{SanitizeEntrypointNamePart(cursor.ResultType.Spelling.ToString()).ToLowerInvariant()}{constSuffix}";
        }

        if (cursor.IsOverloadedOperator)
        {
            return (cursor.OverloadedOperatorKind switch
            {
                CX_OverloadedOperatorKind.CX_OO_Plus => cursor.NumArguments == 0 ? "operator_unary_plus" : "operator_plus",
                CX_OverloadedOperatorKind.CX_OO_Minus => cursor.NumArguments == 0 ? "operator_unary_minus" : "operator_minus",
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

    private static void ApplyDeletedCallableIgnore(CXCursor cursor, FunctionBinding function)
    {
        if (!cursor.IsDeleted || function.IsIgnored())
        {
            return;
        }

        var callableLabel = function.IsConstructor
            ? $"constructor '{function.CppQualifiedName}'"
            : $"callable '{function.CppQualifiedName}'";
        function.SetIgnoreReason($"{callableLabel} is deleted in the native surface");
    }

    private static void ApplyCallableDefinitionMetadata(CXCursor cursor, FunctionBinding function)
    {
        function.SetMetadata(MetadataKeys.HasNativeDefinition, cursor.IsThisDeclarationADefinition);
    }

    private static unsafe void ApplyDestructorMetadata(CXCursor cursor, RecordBinding record)
    {
        cursor.VisitChildren(
            (child, _, _) =>
            {
                if (child.Kind == CXCursorKind.CXCursor_Destructor && child.CXXAccessSpecifier != CX_CXXAccessSpecifier.CX_CXXPublic)
                {
                    record.SetMetadata(MetadataKeys.NonPublicDestructor, true);
                    return CXChildVisitResult.CXChildVisit_Break;
                }

                return CXChildVisitResult.CXChildVisit_Continue;
            },
            new CXClientData(IntPtr.Zero));
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
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        IReadOnlyDictionary<string, BindingType> genericParameters)
    {
        var type = parameterCursor.Type;
        var normalizedType = type.CanonicalType;
        var explicitDirection = TryGetPassKindOverride(functionCursor, parameterCursor, out var overrideDirection)
            ? overrideDirection
            : (BindingParameterDirection?)null;

        if (normalizedType.kind == CXTypeKind.CXType_LValueReference)
        {
            var sourcePointeeType = type.PointeeType;
            var normalizedPointeeType = normalizedType.PointeeType.CanonicalType;

            if (normalizedPointeeType.kind == CXTypeKind.CXType_Record)
            {
                return (
                    MapType(normalizedPointeeType, recordsByName, enumsByName, managedTypeOverrides, genericParameters),
                    explicitDirection ?? BindingParameterDirection.In,
                    BindingParameterAbiKind.Value);
            }

            return (
                MapType(normalizedPointeeType, recordsByName, enumsByName, managedTypeOverrides, genericParameters),
                explicitDirection ?? (sourcePointeeType.IsConstQualified ? BindingParameterDirection.In : BindingParameterDirection.Ref),
                BindingParameterAbiKind.Reference);
        }

        if (normalizedType.kind == CXTypeKind.CXType_Pointer)
        {
            var normalizedTypeName = NormalizeTypeName(type.Spelling.ToString());
            if (managedTypeOverrides.TryGetValue(normalizedTypeName, out _))
            {
                var pointeeKind = normalizedType.PointeeType.CanonicalType.kind;
                return (
                    MapType(type, recordsByName, enumsByName, managedTypeOverrides, genericParameters),
                    explicitDirection ?? (pointeeKind is CXTypeKind.CXType_Void or CXTypeKind.CXType_Record
                        ? BindingParameterDirection.In
                        : (type.PointeeType.IsConstQualified ? BindingParameterDirection.In : BindingParameterDirection.Out)),
                    BindingParameterAbiKind.Value);
            }

            var sourcePointeeType = type.PointeeType;
            var normalizedPointeeType = normalizedType.PointeeType.CanonicalType;

            if (normalizedPointeeType.kind is CXTypeKind.CXType_Void or CXTypeKind.CXType_Record)
            {
                return (
                    MapType(type, recordsByName, enumsByName, managedTypeOverrides, genericParameters),
                    explicitDirection ?? BindingParameterDirection.In,
                    BindingParameterAbiKind.Value);
            }

            return (
                MapType(normalizedPointeeType, recordsByName, enumsByName, managedTypeOverrides, genericParameters),
                explicitDirection ?? (sourcePointeeType.IsConstQualified ? BindingParameterDirection.In : BindingParameterDirection.Out),
                BindingParameterAbiKind.Pointer);
        }

        return (MapType(type, recordsByName, enumsByName, managedTypeOverrides, genericParameters), explicitDirection ?? BindingParameterDirection.In, BindingParameterAbiKind.Value);
    }

    private static BindingType MapType(
        CXType type,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        IReadOnlyDictionary<string, BindingType> genericParameters)
    {
        var normalizedType = type.CanonicalType;
        var normalizedTypeName = NormalizeTypeName(type.Spelling.ToString());

        if (TryMapGenericParameterType(normalizedTypeName, genericParameters, out var genericParameterType))
        {
            return genericParameterType;
        }

        if (managedTypeOverrides.TryGetValue(normalizedTypeName, out var managedTypeOverride))
        {
            return BindingType.CreateConfigured(
                managedTypeOverride.CppTypeName,
                managedTypeOverride.PublicTypeName);
        }

        if (normalizedType.kind == CXTypeKind.CXType_LValueReference)
        {
            return MapType(normalizedType.PointeeType, recordsByName, enumsByName, managedTypeOverrides, genericParameters);
        }

        if (normalizedType.kind == CXTypeKind.CXType_Pointer)
        {
            var pointeeType = normalizedType.PointeeType.CanonicalType;
            if (TryCreateSpecializedRecordHandleType(
                    normalizedTypeName,
                    NormalizeTypeName(pointeeType.Spelling.ToString()),
                    recordsByName,
                    enumsByName,
                    managedTypeOverrides,
                    genericParameters,
                    out var specializedRecordHandleType))
            {
                return specializedRecordHandleType;
            }

            if (pointeeType.kind == CXTypeKind.CXType_Void &&
                managedTypeOverrides.TryGetValue("void*", out var voidPointerOverride))
            {
                return BindingType.CreateConfigured(
                    NormalizeTypeName(type.Spelling.ToString()),
                    voidPointerOverride.PublicTypeName);
            }

            if (pointeeType.kind == CXTypeKind.CXType_Record)
            {
                var recordName = GetRecordQualifiedName(pointeeType, recordsByName);
                if (!recordsByName.TryGetValue(recordName, out var record))
                {
                    record = new RecordBinding($"record:{recordName}", recordName, ToPascalCase(recordName), NormalizeNamedTypeName(pointeeType.Spelling.ToString()));
                    record.SetMetadata(MetadataKeys.PlaceholderRecord, true);
                    recordsByName.Add(recordName, record);
                }

                var genericArguments = record.HasMetadata(MetadataKeys.PlaceholderRecord)
                    ? Array.Empty<BindingType>()
                    : TryParseGenericArguments(pointeeType.Spelling.ToString(), recordsByName, enumsByName, managedTypeOverrides, genericParameters);
                return genericArguments.Count == 0
                    ? record.HandleType
                    : BindingType.CreateHandle(normalizedTypeName, record.ManagedName, record.SymbolId, record.HandleMarshallerTypeName, genericArguments);
            }
        }

        if (normalizedType.kind == CXTypeKind.CXType_Record)
        {
            if (TryCreateSpecializedRecordHandleType(
                    $"{normalizedTypeName}*",
                    NormalizeTypeName(normalizedType.Spelling.ToString()),
                    recordsByName,
                    enumsByName,
                    managedTypeOverrides,
                    genericParameters,
                    out var specializedRecordHandleType))
            {
                return specializedRecordHandleType;
            }

            var recordName = GetRecordQualifiedName(normalizedType, recordsByName);
            if (!recordsByName.TryGetValue(recordName, out var record))
            {
                record = new RecordBinding($"record:{recordName}", recordName, ToPascalCase(recordName), NormalizeNamedTypeName(normalizedType.Spelling.ToString()));
                record.SetMetadata(MetadataKeys.PlaceholderRecord, true);
                recordsByName.Add(recordName, record);
            }

            var genericArguments = record.HasMetadata(MetadataKeys.PlaceholderRecord)
                ? Array.Empty<BindingType>()
                : TryParseGenericArguments(normalizedType.Spelling.ToString(), recordsByName, enumsByName, managedTypeOverrides, genericParameters);
            return genericArguments.Count == 0
                ? record.HandleType
                : BindingType.CreateHandle($"{NormalizeTypeName(normalizedType.Spelling.ToString())}*", record.ManagedName, record.SymbolId, record.HandleMarshallerTypeName, genericArguments);
        }

        if (normalizedType.kind == CXTypeKind.CXType_Enum)
        {
            var enumName = NormalizeNamedTypeName(normalizedType.Spelling.ToString());
            if (enumsByName.TryGetValue(enumName, out var @enum))
            {
                return BindingType.CreateEnum(enumName, @enum.ManagedName);
            }

            return BindingType.CreateEnum(enumName, ToPascalCase(enumName));
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

    private static string EscapeManagedIdentifier(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return CSharpKeywords.Contains(name) ? $"@{name}" : name;
    }

    private static string GetRecordQualifiedName(CXType type, IDictionary<string, RecordBinding> recordsByName)
    {
        var spelling = type.Spelling.ToString();
        var normalizedName = NormalizeNamedTypeName(spelling);
        var unqualifiedName = normalizedName.Contains("::", StringComparison.Ordinal)
            ? normalizedName[(normalizedName.LastIndexOf("::", StringComparison.Ordinal) + 2)..]
            : normalizedName;

        if (recordsByName.TryGetValue(normalizedName, out _))
        {
            return normalizedName;
        }

        if (recordsByName.TryGetValue(unqualifiedName, out _))
        {
            return unqualifiedName;
        }

        foreach (var candidate in recordsByName.Values)
        {
            if (spelling.Contains(candidate.CppQualifiedName, StringComparison.Ordinal))
            {
                return candidate.NativeName;
            }
        }

        return unqualifiedName;
    }

    private static string ResolveQualifiedTypeName(CXType type, IDictionary<string, RecordBinding> recordsByName)
    {
        var spelling = NormalizeTypeName(type.Spelling.ToString());

        if (TryGetQualifiedNamedType(type, out var unqualifiedName, out var qualifiedName))
        {
            var pattern = $@"(?<![:\w]){Regex.Escape(unqualifiedName)}(?![:\w])";
            spelling = Regex.Replace(spelling, pattern, qualifiedName, RegexOptions.CultureInvariant);
        }

        foreach (var record in recordsByName.Values.OrderByDescending(static record => record.NativeName.Length))
        {
            var pattern = $@"(?<![:\w]){Regex.Escape(record.NativeName)}(?![:\w])";
            spelling = Regex.Replace(spelling, pattern, record.CppQualifiedName, RegexOptions.CultureInvariant);
        }

        return spelling;
    }

    private static bool TryGetQualifiedNamedType(CXType type, out string unqualifiedName, out string qualifiedName)
    {
        var resolvedType = type.CanonicalType;
        while (resolvedType.kind is CXTypeKind.CXType_LValueReference or CXTypeKind.CXType_RValueReference or CXTypeKind.CXType_Pointer)
        {
            resolvedType = resolvedType.PointeeType.CanonicalType;
        }

        var declaration = resolvedType.Declaration;
        if (string.IsNullOrWhiteSpace(declaration.Spelling.ToString()))
        {
            unqualifiedName = string.Empty;
            qualifiedName = string.Empty;
            return false;
        }

        if (declaration.Kind is not (CXCursorKind.CXCursor_StructDecl or CXCursorKind.CXCursor_ClassDecl or CXCursorKind.CXCursor_ClassTemplate or CXCursorKind.CXCursor_EnumDecl))
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

        if (recordsByName.Values.Any(candidate =>
                string.Equals(candidate.ManagedName, record.ManagedBaseTypeName, StringComparison.Ordinal) &&
                candidate.IsPolymorphic))
        {
            record.IsPolymorphic = true;
        }
    }

    private static string GetCppQualifiedName(CXCursor cursor)
    {
        var names = new List<string>();
        var current = cursor.SemanticParent;

        while (current.Kind is CXCursorKind.CXCursor_Namespace
            or CXCursorKind.CXCursor_ClassDecl
            or CXCursorKind.CXCursor_StructDecl)
        {
            var parentName = current.Spelling.ToString();
            if (!string.IsNullOrWhiteSpace(parentName))
            {
                names.Add(parentName);
            }

            current = current.SemanticParent;
        }

        var ownName = cursor.Spelling.ToString();

        if (names.Count == 0)
        {
            return ownName;
        }

        names.Reverse();
        names.Add(ownName);
        return string.Join("::", names);
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

    private static void CheckAndApplyIgnoreAttribute(CXCursor cursor, BindingNode node)
    {
        var sourceText = GetSourceText(cursor.TranslationUnit, cursor.Extent);
        if (node is RecordBinding || node is EnumBinding)
        {
            var braceIndex = sourceText.IndexOf('{');
            if (braceIndex >= 0)
            {
                sourceText = sourceText[..braceIndex];
            }
        }

        var match = IgnoreAttributeExpression.Match(sourceText);
        if (!match.Success)
        {
            cursor.Location.GetFileLocation(out var file, out var line, out var column, out _);
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
                    node.SetMetadata(MetadataKeys.IgnoreReason, reason);
                    return;
                }
            }

            return;
        }

        var reasonFromMatch = match.Groups["reason"].Success
            ? match.Groups["reason"].Value
            : "marked [[sharpcpp::ignore]]";
        node.SetMetadata(MetadataKeys.IgnoreReason, reasonFromMatch);
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
                foreach (Match match in IgnoreAttributeExpression.Matches(lineText))
                {
                    var column = match.Index + 1;
                    var reason = match.Groups["reason"].Success ? match.Groups["reason"].Value : null;
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
                foreach (Match match in RenameAttributeExpression.Matches(lineText))
                {
                    var column = match.Index + 1;
                    annotations.Add(new RenameAnnotation(lineIndex + 1, column, match.Groups["value"].Value));
                }
            }

            if (annotations.Count > 0)
            {
                s_renameAnnotations[path] = annotations;
            }
        }
    }

    private static void TryApplyRename(CXCursor cursor, BindingNode node)
    {
        if (node is RecordBinding or EnumBinding)
        {
            var sourceText = GetSourceText(cursor.TranslationUnit, cursor.Extent);
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
            var sourceText = GetSourceText(cursor.TranslationUnit, cursor.Extent);
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

            if (RenameAttributeExpression.Match(sourceText) is { Success: true })
            {
                ApplyRenameFromSource(node, sourceText);
            }
            else
            {
                ApplyRenameFromPreScan(cursor, node);
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
        var match = RenameAttributeExpression.Match(sourceText);
        if (!match.Success)
        {
            return;
        }

        ApplyRename(node, match.Groups["value"].Value);
    }

    private static void ApplyRenameFromPreScan(CXCursor cursor, BindingNode node)
    {
        cursor.Location.GetFileLocation(out var file, out var line, out var column, out _);
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
                break;
            case EnumBinding @enum:
                @enum.ManagedName = renameValue;
                break;
            case FunctionBinding function:
                function.ManagedName = GetManagedTypeHead(renameValue);
                break;
            case FieldBinding field:
                field.ManagedName = renameValue;
                break;
        }
    }

    private static string GetManagedTypeHead(string managedTypeFormat)
    {
        var genericStart = managedTypeFormat.IndexOf('<');
        return genericStart >= 0 ? managedTypeFormat[..genericStart].Trim() : managedTypeFormat;
    }

    private static void ExtractAndApplyDocumentation(CXCursor cursor, BindingNode node)
    {
        var briefComment = cursor.BriefCommentText;
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

    private static unsafe void RegisterEnum(CXCursor cursor, BindingCompilation compilation, IDictionary<string, EnumBinding> enumsByName, string? headerIncludePath)
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
            NormalizeTypeName(cursor.EnumDecl_IntegerType.Spelling.ToString()),
            GetCppQualifiedName(cursor))
        {
            IsFlags = FlagsAttributeExpression.IsMatch(GetSourceText(cursor.TranslationUnit, cursor.Extent))
        };

        CheckAndApplyIgnoreAttribute(cursor, @enum);
        TryApplyRename(cursor, @enum);
        ExtractAndApplyDocumentation(cursor, @enum);

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
        @enum.HeaderIncludePath = headerIncludePath;
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

    private static void ApplyManagedDefaultValue(ParameterBinding parameter, CXCursor parameterCursor, BindingType parameterType)
    {
        var defaultValue = GetManagedDefaultValueExpression(parameterCursor, parameterType);
        parameter.ManagedDefaultValueExpression = defaultValue;

        if (defaultValue is null && parameterCursor.HasDefaultArg)
        {
            parameter.SetMetadata(MetadataKeys.UnexpressibleDefault, true);
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
        var expression = GetSourceText(expressionCursor.TranslationUnit, expressionCursor.Extent).Trim();
        expression = expression.StartsWith("=", StringComparison.Ordinal)
            ? expression[1..].TrimStart()
            : expression;

        if (expression.Contains('#') || expression.Contains("///") || expression.Length > 100)
        {
            return null;
        }

        if (expression.Contains('{') || expression.Contains('}'))
        {
            return null;
        }

        if (string.Equals(expression, "nullptr", StringComparison.Ordinal))
        {
            return parameterType.Kind == BindingTypeKind.Handle ? "null" : "default";
        }

        if (parameterType.Kind == BindingTypeKind.Enum)
        {
            return NormalizeManagedEnumDefaultExpression(parameterType.ManagedTypeName, expression);
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
                if (!publicTypeMatch.Success)
                {
                    continue;
                }

                var cppTypeName = NormalizeTypeName(match.Groups["cppType"].Value);
                result[cppTypeName] = new ManagedTypeOverride(
                    cppTypeName,
                    publicTypeMatch.Groups["value"].Value);
            }
        }

        return result;
    }

    private static unsafe void VisitNamespace(
        CXCursor namespaceCursor,
        BindingCompilation compilation,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        string entryPointPrefix,
        IReadOnlyList<string> ownedFileRoots,
        string? headerIncludePath)
    {
        namespaceCursor.VisitChildren(
            (child, _, _) =>
            {
                if (!IsOwnedCursor(child, ownedFileRoots))
                {
                    return CXChildVisitResult.CXChildVisit_Continue;
                }

                switch (child.Kind)
                {
                    case CXCursorKind.CXCursor_Namespace:
                        VisitNamespace(child, compilation, recordsByName, enumsByName, managedTypeOverrides, entryPointPrefix, ownedFileRoots, headerIncludePath);
                        break;

                    case CXCursorKind.CXCursor_StructDecl:
                    case CXCursorKind.CXCursor_ClassDecl:
                        RegisterRecord(child, compilation, recordsByName, enumsByName, managedTypeOverrides, entryPointPrefix,
                            ResolveOwnedHeaderIncludePath(child, ownedFileRoots, headerIncludePath ?? string.Empty));
                        break;

                    case CXCursorKind.CXCursor_ClassTemplate:
                        RegisterTemplateRecord(child, compilation, recordsByName, enumsByName, managedTypeOverrides, entryPointPrefix,
                            ResolveOwnedHeaderIncludePath(child, ownedFileRoots, headerIncludePath ?? string.Empty));
                        break;

                    case CXCursorKind.CXCursor_EnumDecl:
                        RegisterEnum(child, compilation, enumsByName,
                            ResolveOwnedHeaderIncludePath(child, ownedFileRoots, headerIncludePath ?? string.Empty));
                        break;

                    case CXCursorKind.CXCursor_FunctionDecl:
                    {
                        var function = CreateFunction(child, entryPointPrefix, recordsByName, enumsByName, managedTypeOverrides, EmptyGenericParameters);
                        CheckAndApplyIgnoreAttribute(child, function);
                        TryApplyRename(child, function);
                        ExtractAndApplyDocumentation(child, function);
                        ApplyCallableDefinitionMetadata(child, function);
                        ApplyDeletedCallableIgnore(child, function);
                        function.HeaderIncludePath = ResolveOwnedHeaderIncludePath(child, ownedFileRoots, headerIncludePath ?? string.Empty);
                        compilation.Members.Add(function);
                        break;
                    }

                    case CXCursorKind.CXCursor_FunctionTemplate:
                    {
                        var function = CreateTemplateFunction(child, entryPointPrefix, recordsByName, enumsByName, managedTypeOverrides);
                        if (function is not null)
                        {
                            CheckAndApplyIgnoreAttribute(child, function);
                            TryApplyRename(child, function);
                            ExtractAndApplyDocumentation(child, function);
                            ApplyCallableDefinitionMetadata(child, function);
                            ApplyDeletedCallableIgnore(child, function);
                            function.HeaderIncludePath = ResolveOwnedHeaderIncludePath(child, ownedFileRoots, headerIncludePath ?? string.Empty);
                            compilation.Members.Add(function);
                        }
                        break;
                    }
                }

                return CXChildVisitResult.CXChildVisit_Continue;
            },
            new CXClientData(IntPtr.Zero));
    }

    private sealed record ManagedTypeOverride(string CppTypeName, string PublicTypeName);

    private static bool TryMapGenericParameterType(string normalizedTypeName, IReadOnlyDictionary<string, BindingType> genericParameters, out BindingType type)
    {
        if (genericParameters.TryGetValue(normalizedTypeName, out type!))
        {
            return true;
        }

        type = null!;
        return false;
    }

    private static IReadOnlyDictionary<string, BindingType> CreateGenericParameterScope(IReadOnlyList<BindingGenericParameter> templateParameters)
    {
        if (templateParameters.Count == 0)
        {
            return EmptyGenericParameters;
        }

        return templateParameters.ToDictionary(static parameter => parameter.Name, static parameter => BindingType.CreateGenericParameter(parameter.Name), StringComparer.Ordinal);
    }

    private static unsafe bool TryExtractTemplateParameters(CXCursor templateCursor, out IReadOnlyList<BindingGenericParameter> templateParameters)
    {
        var parameters = new List<BindingGenericParameter>();

        templateCursor.VisitChildren(
            (child, _, _) =>
            {
                if (child.Kind == CXCursorKind.CXCursor_TemplateTypeParameter)
                {
                    var parameterName = child.Spelling.ToString();
                    if (!string.IsNullOrWhiteSpace(parameterName))
                    {
                        parameters.Add(new BindingGenericParameter(parameterName, parameters.Count));
                    }
                }

                return CXChildVisitResult.CXChildVisit_Continue;
            },
            new CXClientData(IntPtr.Zero));

        templateParameters = parameters;
        return parameters.Count > 0;
    }

    private static IReadOnlyList<BindingType> TryParseGenericArguments(
        string spelling,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        IReadOnlyDictionary<string, BindingType> genericParameters)
    {
        var normalized = NormalizeTypeName(spelling);
        var genericStart = normalized.IndexOf('<');
        var genericEnd = normalized.LastIndexOf('>');
        if (genericStart < 0 || genericEnd <= genericStart)
        {
            return Array.Empty<BindingType>();
        }

        var argumentList = normalized[(genericStart + 1)..genericEnd];
        return SplitTopLevelParameters(argumentList)
            .Select(argument => ParseTypeToken(argument, recordsByName, enumsByName, managedTypeOverrides, genericParameters))
            .ToArray();
    }

    private static BindingType ParseTypeToken(
        string token,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        IReadOnlyDictionary<string, BindingType> genericParameters)
    {
        var normalized = NormalizeTypeName(token);

        if (TryMapGenericParameterType(normalized, genericParameters, out var genericParameterType))
        {
            return genericParameterType;
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
                return BindingType.CreateHandle(normalized, record.ManagedName, record.SymbolId, record.HandleMarshallerTypeName);
            }
        }

        if (managedTypeOverrides.TryGetValue(normalized, out var managedTypeOverride))
        {
            return BindingType.CreateConfigured(managedTypeOverride.CppTypeName, managedTypeOverride.PublicTypeName);
        }

        return normalized switch
        {
            "bool" => BindingType.Boolean,
            "signed char" => BindingType.Int8,
            "unsigned char" => BindingType.UInt8,
            "short" => BindingType.Int16,
            "unsigned short" => BindingType.UInt16,
            "int" => BindingType.Int32,
            "unsigned int" => BindingType.UInt32,
            "long long" => BindingType.Int64,
            "unsigned long long" => BindingType.UInt64,
            "float" => BindingType.Single,
            "double" => BindingType.Double,
            "long" => BindingType.NativeInt,
            "unsigned long" => BindingType.NativeUInt,
            "char" => BindingType.Int8,
            _ when enumsByName.TryGetValue(NormalizeNamedTypeName(normalized), out var @enum) => BindingType.CreateEnum(@enum.NativeName, @enum.ManagedName),
            _ when recordsByName.TryGetValue(NormalizeNamedTypeName(normalized), out var record) => BindingType.CreateHandle($"{normalized}*", record.ManagedName, record.SymbolId, record.HandleMarshallerTypeName),
            _ => throw new NotSupportedException($"ClangSharp sample parser does not support generic argument token '{normalized}'.")
        };
    }

    private static bool TryCreateSpecializedRecordHandleType(
        string handleCppTypeName,
        string referredTypeName,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        IReadOnlyDictionary<string, BindingType> genericParameters,
        out BindingType type)
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

        var genericArguments = TryParseGenericArguments(referredTypeName, recordsByName, enumsByName, managedTypeOverrides, genericParameters);
        type = BindingType.CreateHandle(handleCppTypeName, record.ManagedName, record.SymbolId, record.HandleMarshallerTypeName, genericArguments);
        return true;
    }

    private static void ApplyExplicitGenericInstantiations(
        CXCursor cursor,
        BindingNode node,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides)
    {
        if (node is not RecordBinding and not FunctionBinding)
        {
            return;
        }

        var sourceText = GetSourceText(cursor.TranslationUnit, cursor.Extent);
        if (node is RecordBinding)
        {
            var braceIndex = sourceText.IndexOf('{');
            if (braceIndex >= 0)
            {
                sourceText = sourceText[..braceIndex];
            }
        }

        var instantiations = ParseInstantiationMatches(InstantiateAttributeExpression.Matches(sourceText), recordsByName, enumsByName, managedTypeOverrides);

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

        if (instantiations.Count > 0)
        {
            node.SetMetadata(MetadataKeys.ExplicitGenericInstantiations, new GenericInstantiationMetadata(instantiations));
        }
    }

    private static readonly Dictionary<string, List<InstantiateAnnotation>> s_instantiateAnnotations = new(StringComparer.Ordinal);

    private sealed record InstantiateAnnotation(int Line, IReadOnlyList<RecordGenericInstantiationBinding> Instantiations);

    private static void PreScanInstantiateAnnotations(IReadOnlyList<string> headerFiles)
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
                var instantiations = ParseInstantiationMatches(InstantiateAttributeExpression.Matches(lines[lineIndex]), new Dictionary<string, RecordBinding>(StringComparer.Ordinal), new Dictionary<string, EnumBinding>(StringComparer.Ordinal), new Dictionary<string, ManagedTypeOverride>(StringComparer.Ordinal));
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
        MatchCollection matches,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides)
    {
        var instantiations = new List<RecordGenericInstantiationBinding>();

        foreach (Match match in matches)
        {
            var arguments = InstantiateArgumentExpression.Matches(match.Groups["body"].Value)
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

    private static unsafe void ApplyVirtualSpecifiers(CXCursor cursor, FunctionBinding function)
    {
        if (!function.IsVirtual)
        {
            return;
        }

        bool isOverride = false;
        bool isFinal = false;

        cursor.VisitChildren(
            (child, _, _) =>
            {
                if ((int)child.Kind == 417) // CXCursor_CXXOverrideAttr
                {
                    isOverride = true;
                }
                else if ((int)child.Kind == 418) // CXCursor_CXXFinalAttr
                {
                    isFinal = true;
                }

                return CXChildVisitResult.CXChildVisit_Continue;
            },
            new CXClientData(IntPtr.Zero));

        var sourceText = GetSourceText(cursor.TranslationUnit, cursor.Extent);
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
}
