using ClangSharp.Interop;
using ClangSharp;
using System.Collections;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

using SharpCpp.Metadata.Import;
using SharpCpp.Model.Ast;

using ManagedTypeOverride = SharpCpp.Parsing.MarshallerConfigExtractor.ManagedTypeOverride;

namespace SharpCpp.Parsing;

public sealed partial class ClangSharpHeaderParser
{
    private sealed class BindingIndex<TBinding>(Func<TBinding, string?> primaryKeySelector) : IDictionary<string, TBinding>
        where TBinding : class
    {
        private readonly Dictionary<string, TBinding> _lookup = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TBinding> _primary = new(StringComparer.Ordinal);

        public TBinding this[string key]
        {
            get => _lookup[key];
            set
            {
                Remove(key);
                Add(key, value);
            }
        }

        public ICollection<string> Keys => _lookup.Keys;

        public ICollection<TBinding> Values => _primary.Values;

        public int Count => _lookup.Count;

        public bool IsReadOnly => false;

        public void Add(string key, TBinding value)
        {
            _lookup.Add(key, value);

            var primaryKey = primaryKeySelector(value);
            if (!string.IsNullOrWhiteSpace(primaryKey) && !_primary.ContainsKey(primaryKey))
            {
                _primary.Add(primaryKey, value);
            }
        }

        public bool ContainsKey(string key)
        {
            return _lookup.ContainsKey(key);
        }

        public bool Remove(string key)
        {
            if (!_lookup.TryGetValue(key, out var value))
            {
                return false;
            }

            var aliases = _lookup
                .Where(entry => ReferenceEquals(entry.Value, value))
                .Select(static entry => entry.Key)
                .ToArray();

            foreach (var alias in aliases)
            {
                _lookup.Remove(alias);
            }

            var primaryKey = primaryKeySelector(value);
            if (!string.IsNullOrWhiteSpace(primaryKey))
            {
                _primary.Remove(primaryKey);
            }

            return true;
        }

        public bool TryGetValue(string key, out TBinding value)
        {
            return _lookup.TryGetValue(key, out value!);
        }

        public void Add(KeyValuePair<string, TBinding> item)
        {
            Add(item.Key, item.Value);
        }

        public void Clear()
        {
            _lookup.Clear();
            _primary.Clear();
        }

        public bool Contains(KeyValuePair<string, TBinding> item)
        {
            return ((ICollection<KeyValuePair<string, TBinding>>)_lookup).Contains(item);
        }

        public void CopyTo(KeyValuePair<string, TBinding>[] array, int arrayIndex)
        {
            ((ICollection<KeyValuePair<string, TBinding>>)_lookup).CopyTo(array, arrayIndex);
        }

        public bool Remove(KeyValuePair<string, TBinding> item)
        {
            return Contains(item) && Remove(item.Key);
        }

        public IEnumerator<KeyValuePair<string, TBinding>> GetEnumerator()
        {
            return _lookup.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
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

    private static readonly Dictionary<string, string> s_typeAliases = new(StringComparer.Ordinal);

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
        var recordsByName = new BindingIndex<RecordBinding>(static record => record.NativeName);
        var enumsByName = new BindingIndex<EnumBinding>(static @enum => @enum.NativeName);
        var managedTypeOverrides = MarshallerConfigExtractor.ExtractManagedTypeOverrides(
            request.MarshallerConfigurationFiles,
            request.ClangArguments);

        request.ImportedBindingMetadata?.Seed(compilation, recordsByName, enumsByName);

        var managedTranslationUnit = TranslationUnit.GetOrCreate(translationUnit);
        BuildTypeAliasIndex(managedTranslationUnit.TranslationUnitDecl);
        foreach (var child in managedTranslationUnit.TranslationUnitDecl.Decls)
        {
            if (!IsOwnedDecl(child, ownedFileRoots))
            {
                continue;
            }

            switch (child)
            {
                case NamespaceDecl nsChild:
                    VisitNamespace(nsChild, compilation, recordsByName, enumsByName, managedTypeOverrides, request.EntryPointPrefix, ownedFileRoots, request.HeaderIncludePath);
                    break;

                case RecordDecl recordDecl:
                    RegisterRecord(recordDecl, compilation, recordsByName, enumsByName, managedTypeOverrides, request.EntryPointPrefix,
                        ResolveDeclIncludePath(recordDecl, ownedFileRoots, request.HeaderIncludePath));
                    break;

                case ClassTemplateDecl classTemplateDecl:
                    RegisterTemplateRecord(classTemplateDecl, compilation, recordsByName, enumsByName, managedTypeOverrides, request.EntryPointPrefix,
                        ResolveDeclIncludePath(classTemplateDecl, ownedFileRoots, request.HeaderIncludePath));
                    break;

                case EnumDecl enumChildDecl:
                    RegisterEnum(enumChildDecl, compilation, enumsByName,
                        ResolveDeclIncludePath(child, ownedFileRoots, request.HeaderIncludePath));
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
                        ExtractAndApplyDocumentation(funcTemplateDecl, function);
                        ApplyCallableDefinitionMetadata(funcTemplateDecl, function);
                        ApplyDeletedCallableIgnore(funcTemplateDecl.TemplatedDecl, function);
                        function.HeaderIncludePath = ResolveDeclIncludePath(child, ownedFileRoots, request.HeaderIncludePath);
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
                    ExtractAndApplyDocumentation(functionDecl, function);
                    ApplyCallableDefinitionMetadata(functionDecl, function);
                    ApplyDeletedCallableIgnore(functionDecl, function);
                    function.HeaderIncludePath = ResolveDeclIncludePath(child, ownedFileRoots, request.HeaderIncludePath);
                    compilation.Members.Add(function);
                    break;
                }
            }
        }

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

    // ─── Decl-based helpers (ClangSharp managed API) ────────────────────────────

    private static string GetCppQualifiedName(Decl decl)
    {
        var names = new List<string>();
        var current = decl.SemanticParentCursor;

        while (current is NamespaceDecl or RecordDecl)
        {
            var parentName = current is NamedDecl named ? named.Name ?? named.Spelling.ToString() : string.Empty;
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

    private static string ResolveDeclIncludePath(Decl decl, IReadOnlyList<string> ownedFileRoots, string fallbackHeaderIncludePath)
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

            return Path.GetRelativePath(ownedFileRoot, filePath).Replace('\\', '/');
        }

        return fallbackHeaderIncludePath;
    }

    private static void RegisterRecord(
        RecordDecl recordDecl,
        BindingCompilation compilation,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        string entryPointPrefix,
        string? headerIncludePath)
    {
        // If this record is the templated declaration of a class template,
        // delegate to RegisterTemplateRecord for proper generic parameter
        // extraction (RecordDecl.DescribedClassTemplate fills this role).
        if (recordDecl is CXXRecordDecl cxx && cxx.DescribedClassTemplate is { } classTemplate)
        {
            RegisterTemplateRecord(classTemplate, compilation, recordsByName, enumsByName, managedTypeOverrides, entryPointPrefix, headerIncludePath);
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
        var isForwardDeclaration = braceIndex < 0;

        // Look up ghost records under both the unqualified spelling and the fully
        // qualified name — placeholders created by MapType may be keyed by either.
        RecordBinding? ghostRecord = null;
        var ghostKey = nativeName;
        if (!recordsByName.TryGetValue(nativeName, out ghostRecord) &&
            !string.IsNullOrWhiteSpace(cppQualifiedName) &&
            !string.Equals(nativeName, cppQualifiedName, StringComparison.Ordinal))
        {
            if (recordsByName.TryGetValue(cppQualifiedName, out ghostRecord))
            {
                ghostKey = cppQualifiedName;
            }
        }

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
        var record = new RecordBinding($"record:{nativeName}", nativeName, ToPascalCase(nativeName), cppQualifiedName)
        {
            IsAbstract = cxxRecord?.IsAbstract ?? false,
            IsDirector = HasSharpCppAttribute(declarationText, "director"),
            HasCopyConstructor = recordDecl.Handle.HasUserDeclaredCopyConstructor ||
                ContainsCopyConstructorDeclaration(sourceText, nativeName) ||
                ContainsCopyConstructorDeclaration(GetDeclFileText(recordDecl), nativeName)
        };
        record.SetMetadata(MetadataKeys.DeclarationDisplayText, GetDeclarationDisplayText(recordDecl));
        if (isForwardDeclaration)
        {
            record.SetMetadata(MetadataKeys.ForwardDeclaration, true);
        }
        CheckAndApplyIgnoreAttribute(recordDecl, record);
        TryApplyRename(recordDecl, record);
        ExtractAndApplyDocumentation(recordDecl, record);
        IndexRecordKeys(recordsByName, record);
        record.HeaderIncludePath = headerIncludePath;
        compilation.Members.Add(record);

        ApplyDestructorMetadata(cxxRecord, record);
        PopulateRecordBases(cxxRecord, record, recordsByName, enumsByName, managedTypeOverrides, EmptyGenericParameters);
        PopulateRecordMembers(recordDecl, record, recordsByName, enumsByName, managedTypeOverrides, EmptyGenericParameters, entryPointPrefix);

        UpdatePolymorphism(record, recordsByName);
    }

    private static void RegisterTemplateRecord(
        ClassTemplateDecl classTemplate,
        BindingCompilation compilation,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        string entryPointPrefix,
        string? headerIncludePath)
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
        RecordBinding? ghostRecord = null;
        var ghostKey = nativeName;
        if (!recordsByName.TryGetValue(nativeName, out ghostRecord) &&
            !string.IsNullOrWhiteSpace(cppQualifiedName) &&
            !string.Equals(nativeName, cppQualifiedName, StringComparison.Ordinal))
        {
            if (recordsByName.TryGetValue(cppQualifiedName, out ghostRecord))
            {
                ghostKey = cppQualifiedName;
            }
        }

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
        var record = new RecordBinding($"record:{nativeName}", nativeName, ToPascalCase(nativeName), cppQualifiedName)
        {
            IsAbstract = templatedRecord is CXXRecordDecl cxx && cxx.IsAbstract,
            IsDirector = HasSharpCppAttribute(declarationText, "director"),
            HasCopyConstructor = classTemplate.Handle.HasUserDeclaredCopyConstructor ||
                ContainsCopyConstructorDeclaration(sourceText, nativeName) ||
                ContainsCopyConstructorDeclaration(GetDeclFileText(classTemplate), nativeName)
        };
        record.SetMetadata(MetadataKeys.DeclarationDisplayText, GetDeclarationDisplayText(classTemplate));
        if (isForwardDeclaration)
        {
            record.SetMetadata(MetadataKeys.ForwardDeclaration, true);
        }

        foreach (var templateParameter in templateParameters)
        {
            record.GenericParameters.Add(templateParameter);
        }

        ApplyExplicitGenericInstantiations(classTemplate.Handle, record, recordsByName, enumsByName, managedTypeOverrides);
        CheckAndApplyIgnoreAttribute(classTemplate, record);
        TryApplyRename(classTemplate, record);
        ExtractAndApplyDocumentation(classTemplate, record);
        IndexRecordKeys(recordsByName, record);
        record.HeaderIncludePath = headerIncludePath;
        compilation.Members.Add(record);

        var genericParameters = CreateGenericParameterScope(templateParameters);
        ApplyDestructorMetadata(templatedRecord as CXXRecordDecl, record);
        PopulateRecordBases(templatedRecord as CXXRecordDecl, record, recordsByName, enumsByName, managedTypeOverrides, genericParameters);
        PopulateRecordMembers(templatedRecord, record, recordsByName, enumsByName, managedTypeOverrides, genericParameters, entryPointPrefix);

        UpdatePolymorphism(record, recordsByName);
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
            var baseCppTypeName = ResolveQualifiedTypeName(baseSpec.Type.Handle, recordsByName);
            var (baseManagedType, baseIgnoreReason) = TryMapType(baseSpec.Type.Handle, recordsByName, enumsByName, managedTypeOverrides, genericParameters);
            if (!recordsByName.TryGetValue(baseTypeName, out var baseRecord))
            {
                var baseQualifiedName = NormalizeNamedTypeName(baseSpec.Type.Handle.Spelling.ToString());
                baseRecord = new RecordBinding($"record:{baseTypeName}", baseTypeName, ToPascalCase(baseTypeName), baseQualifiedName);
                baseRecord.SetMetadata(MetadataKeys.PlaceholderRecord, true);
                IndexRecordKeys(recordsByName, baseRecord);
            }

            record.Bases.Add(new RecordBaseBinding(
                baseRecord.SymbolId,
                baseRecord.NativeName,
                baseIgnoreReason is null ? baseManagedType.ManagedTypeName : baseRecord.ManagedName,
                baseCppTypeName,
                baseSpec.IsVirtual));

            if (baseIgnoreReason is null &&
                record.ManagedBaseTypeName is null &&
                !baseSpec.IsVirtual &&
                !baseRecord.HasMetadata(MetadataKeys.PlaceholderRecord))
            {
                record.ManagedBaseTypeName = baseManagedType.ManagedTypeName;
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
        string entryPointPrefix)
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
                    ExtractAndApplyDocumentation(ctorDecl, ctor);
                    ApplyCallableDefinitionMetadata(ctorDecl, ctor);

                    if (record.IsAbstract && !record.IsDirector)
                    {
                        ctor.SetIgnoreReason($"constructors on abstract record '{record.CppQualifiedName}' are not materializable in this bootstrap surface");
                    }
                    else if (record.GetMetadata<bool>(MetadataKeys.NonPublicDestructor))
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
                    ExtractAndApplyDocumentation(conversionMethod, method);
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
                    ExtractAndApplyDocumentation(methodDecl, method);
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
                    ExtractAndApplyDocumentation(staticMethod, method);
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
                        ExtractAndApplyDocumentation(funcTemplate, templateMethod);
                        ApplyCallableDefinitionMetadata(funcTemplate, templateMethod);
                        ApplyDeletedCallableIgnore(funcTemplate.TemplatedDecl, templateMethod);
                        record.Members.Add(templateMethod);
                    }
                    break;
                }

                case FieldDecl fieldDecl:
                {
                    var field = CreateField(record, fieldDecl, recordsByName, enumsByName, managedTypeOverrides, genericParameters);
                    CheckAndApplyIgnoreAttribute(fieldDecl, field);
                    TryApplyRename(fieldDecl, field);
                    ExtractAndApplyDocumentation(fieldDecl, field);
                    record.Members.Add(field);
                    break;
                }
            }
        }

        record.HasCopyConstructor |= recordDecl.Decls
            .OfType<CXXConstructorDecl>()
            .Any(ctor =>
                !ctor.IsDeleted &&
                ShouldParseRecordMember(record, ctor.Handle.CXXAccessSpecifier) &&
                IsCopyConstructorLike(ctor, record));
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
        var (returnType, ignoreReason) = TryMapType(methodDecl.ReturnType.Handle, recordsByName, enumsByName, managedTypeOverrides, genericParameters, isReturn: true);
        var cppReturnTypeName = ResolveQualifiedTypeName(methodDecl.ReturnType.Handle, recordsByName);
        var function = new FunctionBinding(
            $"method:{record.NativeName}:{nativeName}",
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
            OwningRecordCppTypeName = record.CppQualifiedName
        };
        function.ReturnsOwnedHandle = ShouldReturnOwnedHandle(methodDecl.ReturnType.Handle, returnType, managedTypeOverrides);
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

            var (parameterType, direction, abiKind, parameterIgnoreReason) = TryMapParameter(methodDecl.Handle, parameter.Handle, recordsByName, enumsByName, managedTypeOverrides, genericParameters);
            ignoreReason ??= parameterIgnoreReason;
            var param = new ParameterBinding(
                $"parameter:{record.NativeName}:{nativeName}:{parameterName}",
                parameterName,
                managedParameterName,
                parameterType,
                direction,
                abiKind,
                ResolveQualifiedTypeName(parameter.Type.Handle, recordsByName));
            ApplyManagedDefaultValue(param, parameter, parameterType, managedTypeOverrides);
            function.Parameters.Add(param);
        }

        function.EntryPointName = BuildEntrypointName(entryPointPrefix, $"{SanitizeEntrypointNamePart(record.CppQualifiedName)}_{GetEntrypointSuffix(methodDecl, nativeName)}");

        if (ignoreReason is not null)
        {
            function.SetIgnoreReason(ignoreReason);
        }

        function.SetMetadata(MetadataKeys.DeclarationDisplayText, GetDeclarationDisplayText(methodDecl));

        return function;
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

        var combinedGenericParameters = new Dictionary<string, BindingType>(genericParameters, StringComparer.Ordinal);
        foreach (var templateParameter in methodTemplateParameters)
        {
            combinedGenericParameters[templateParameter.Name] = BindingType.CreateGenericParameter(templateParameter.Name);
        }

        var templatedFunction = templateDecl.TemplatedDecl;
        var nativeName = templateDecl.Name ?? templateDecl.Spelling.ToString();
        var (returnType, ignoreReason) = TryMapType(templatedFunction.ReturnType.Handle, recordsByName, enumsByName, managedTypeOverrides, combinedGenericParameters, isReturn: true);
        var cppReturnTypeName = ResolveQualifiedTypeName(templatedFunction.ReturnType.Handle, recordsByName);
        var function = new FunctionBinding(
            $"method:{record.NativeName}:{nativeName}",
            nativeName,
            GetManagedMethodName(templateDecl, nativeName),
            returnType,
            cppReturnTypeName: cppReturnTypeName,
            cppQualifiedName: $"{record.CppQualifiedName}::{nativeName}")
        {
            IsStatic = false,
            AccessModifier = MapAccessModifier(templateDecl.Access),
            IsVirtual = templatedFunction is CXXMethodDecl cxxMethod && cxxMethod.IsVirtual,
            IsPureVirtual = templatedFunction is CXXMethodDecl cxxMethod2 && cxxMethod2.IsPure,
            IsConstMethod = templatedFunction is CXXMethodDecl cxxMethod3 && cxxMethod3.IsConst,
            OwningRecordCppTypeName = record.CppQualifiedName
        };
        function.ReturnsOwnedHandle = ShouldReturnOwnedHandle(templatedFunction.ReturnType.Handle, returnType, managedTypeOverrides);

        foreach (var templateParameter in methodTemplateParameters)
        {
            function.GenericParameters.Add(templateParameter);
        }

        ApplyExplicitGenericInstantiations(templateDecl.Handle, function, recordsByName, enumsByName, managedTypeOverrides);

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

            var (parameterType, direction, abiKind, parameterIgnoreReason) = TryMapParameter(templatedFunction.Handle, parameter.Handle, recordsByName, enumsByName, managedTypeOverrides, combinedGenericParameters);
            ignoreReason ??= parameterIgnoreReason;
            var templateParam = new ParameterBinding(
                $"parameter:{record.NativeName}:{nativeName}:{parameterName}",
                parameterName,
                managedParameterName,
                parameterType,
                direction,
                abiKind,
                ResolveQualifiedTypeName(parameter.Type.Handle, recordsByName));
            ApplyManagedDefaultValue(templateParam, parameter, parameterType, managedTypeOverrides);
            function.Parameters.Add(templateParam);
        }

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
        var (returnType, ignoreReason) = TryMapType(methodDecl.ReturnType.Handle, recordsByName, enumsByName, managedTypeOverrides, genericParameters, isReturn: true);
        var cppReturnTypeName = ResolveQualifiedTypeName(methodDecl.ReturnType.Handle, recordsByName);
        var function = new FunctionBinding(
            $"static-method:{record.NativeName}:{nativeName}",
            nativeName,
            GetManagedMethodName(methodDecl, nativeName),
            returnType,
            entryPointName: $"{entryPointPrefix}_{record.NativeName}_{GetEntrypointSuffix(methodDecl, nativeName)}",
            cppReturnTypeName: cppReturnTypeName,
            cppQualifiedName: $"{record.CppQualifiedName}::{nativeName}")
        {
            IsStatic = true,
            AccessModifier = MapAccessModifier(methodDecl.Access),
            OwningRecordCppTypeName = record.CppQualifiedName
        };
        function.ReturnsOwnedHandle = ShouldReturnOwnedHandle(methodDecl.ReturnType.Handle, returnType, managedTypeOverrides);
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

            var (parameterType, direction, abiKind, parameterIgnoreReason) = TryMapParameter(methodDecl.Handle, parameter.Handle, recordsByName, enumsByName, managedTypeOverrides, genericParameters);
            ignoreReason ??= parameterIgnoreReason;
            var staticParam = new ParameterBinding(
                $"parameter:{record.NativeName}:static:{nativeName}:{parameterName}",
                parameterName,
                managedParameterName,
                parameterType,
                direction,
                abiKind,
                ResolveQualifiedTypeName(parameter.Type.Handle, recordsByName));
            ApplyManagedDefaultValue(staticParam, parameter, parameterType, managedTypeOverrides);
            function.Parameters.Add(staticParam);
        }

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
            $"ctor:{record.NativeName}:{ctorDecl.GetHashCode()}",
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

            var (parameterType, direction, abiKind, parameterIgnoreReason) = TryMapParameter(ctorDecl.Handle, parameter.Handle, recordsByName, enumsByName, managedTypeOverrides, genericParameters);
            ignoreReason ??= parameterIgnoreReason;
            var ctorParam = new ParameterBinding(
                $"parameter:{record.NativeName}:ctor:{parameterName}",
                parameterName,
                managedParameterName,
                parameterType,
                direction,
                abiKind,
                ResolveQualifiedTypeName(parameter.Type.Handle, recordsByName));
            ApplyManagedDefaultValue(ctorParam, parameter, parameterType, managedTypeOverrides);
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
        FieldDecl fieldDecl,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        IReadOnlyDictionary<string, BindingType> genericParameters)
    {
        var nativeName = fieldDecl.Name ?? fieldDecl.Spelling.ToString();
        var initialIgnoreReason = IsArrayType(fieldDecl.Type.Handle.CanonicalType)
            ? "fixed-size array fields are not yet supported in this bootstrap surface"
            : null;
        var (fieldType, ignoreReason) = TryMapType(fieldDecl.Type.Handle, recordsByName, enumsByName, managedTypeOverrides, genericParameters);
        ignoreReason ??= initialIgnoreReason;
        var field = new FieldBinding(
            $"field:{record.NativeName}:{nativeName}",
            nativeName,
            ToPascalCase(nativeName),
            fieldType,
            ResolveQualifiedTypeName(fieldDecl.Type.Handle, recordsByName),
            record.CppQualifiedName,
            isReadOnly: fieldDecl.Type.Handle.IsConstQualified)
        {
            AccessModifier = MapAccessModifier(fieldDecl.Access)
        };
        field.ReturnsOwnedHandle = ShouldReturnOwnedHandle(fieldDecl.Type.Handle, field.Type, managedTypeOverrides, preferReturnOverride: false);

        if (ignoreReason is not null)
        {
            field.SetIgnoreReason(ignoreReason);
        }

        field.SetMetadata(MetadataKeys.DeclarationDisplayText, GetDeclarationDisplayText(fieldDecl));

        return field;
    }

    private static bool IsArrayType(CXType type)
    {
        return type.kind is CXTypeKind.CXType_ConstantArray or CXTypeKind.CXType_IncompleteArray;
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
        var (returnType, ignoreReason) = TryMapType(functionDecl.ReturnType.Handle, recordsByName, enumsByName, managedTypeOverrides, genericParameters, isReturn: true);
        var cppReturnTypeName = ResolveQualifiedTypeName(functionDecl.ReturnType.Handle, recordsByName);
        var function = new FunctionBinding(
            $"function:{nativeName}",
            nativeName,
            ToPascalCase(nativeName),
            returnType,
            entryPointName: $"{entryPointPrefix}_{nativeName}",
            cppReturnTypeName: cppReturnTypeName,
            cppQualifiedName: GetCppQualifiedName(functionDecl));
        function.ReturnsOwnedHandle = ShouldReturnOwnedHandle(functionDecl.ReturnType.Handle, returnType, managedTypeOverrides);

        for (int parameterIndex = 0; parameterIndex < functionDecl.Parameters.Count; parameterIndex++)
        {
            var parameter = functionDecl.Parameters[parameterIndex];
            var parameterName = parameter.Name ?? parameter.Spelling.ToString();
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                parameterName = $"arg{parameterIndex}";
            }
            var managedParameterName = EscapeManagedIdentifier(parameterName);

            var (parameterType, direction, abiKind, parameterIgnoreReason) = TryMapParameter(functionDecl.Handle, parameter.Handle, recordsByName, enumsByName, managedTypeOverrides, genericParameters);
            ignoreReason ??= parameterIgnoreReason;
            var freeParam = new ParameterBinding(
                $"parameter:{nativeName}:{parameterName}",
                parameterName,
                managedParameterName,
                parameterType,
                direction,
                abiKind,
                ResolveQualifiedTypeName(parameter.Type.Handle, recordsByName));
            ApplyManagedDefaultValue(freeParam, parameter, parameterType, managedTypeOverrides);
            function.Parameters.Add(freeParam);
        }

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

        var genericParameters = CreateGenericParameterScope(templateParameters);
        var templatedFunction = templateDecl.TemplatedDecl;
        var nativeName = templateDecl.Name ?? templateDecl.Spelling.ToString();
        if (string.IsNullOrWhiteSpace(nativeName))
        {
            return null;
        }

        var (returnType, ignoreReason) = TryMapType(templatedFunction.ReturnType.Handle, recordsByName, enumsByName, managedTypeOverrides, genericParameters, isReturn: true);
        var cppReturnTypeName = ResolveQualifiedTypeName(templatedFunction.ReturnType.Handle, recordsByName);
        var function = new FunctionBinding(
            $"function:{nativeName}",
            nativeName,
            ToPascalCase(nativeName),
            returnType,
            cppReturnTypeName: cppReturnTypeName,
            cppQualifiedName: GetCppQualifiedName(templateDecl));
        function.ReturnsOwnedHandle = ShouldReturnOwnedHandle(templatedFunction.ReturnType.Handle, returnType, managedTypeOverrides);

        foreach (var templateParameter in templateParameters)
        {
            function.GenericParameters.Add(templateParameter);
        }

        ApplyExplicitGenericInstantiations(templateDecl.Handle, function, recordsByName, enumsByName, managedTypeOverrides);

        for (int parameterIndex = 0; parameterIndex < templatedFunction.Parameters.Count; parameterIndex++)
        {
            var parameter = templatedFunction.Parameters[parameterIndex];
            var parameterName = parameter.Name ?? parameter.Spelling.ToString();
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                parameterName = $"arg{function.Parameters.Count}";
            }
            var managedParameterName = EscapeManagedIdentifier(parameterName);

            var (parameterType, direction, abiKind, parameterIgnoreReason) = TryMapParameter(templatedFunction.Handle, parameter.Handle, recordsByName, enumsByName, managedTypeOverrides, genericParameters);
            ignoreReason ??= parameterIgnoreReason;
            var freeTemplateParam = new ParameterBinding(
                $"parameter:{nativeName}:{parameterName}",
                parameterName,
                managedParameterName,
                parameterType,
                direction,
                abiKind,
                ResolveQualifiedTypeName(parameter.Type.Handle, recordsByName));
            ApplyManagedDefaultValue(freeTemplateParam, parameter, parameterType, managedTypeOverrides);
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
    private static bool TypeIsRValueReference(CXType type) => type.TypeClass == CX_TypeClass.CX_TypeClass_RValueReference;
    private static bool TypeIsAnyReference(CXType type) => type.TypeClass is CX_TypeClass.CX_TypeClass_LValueReference or CX_TypeClass.CX_TypeClass_RValueReference;
    private static bool TypeIsPointer(CXType type) => type.TypeClass == CX_TypeClass.CX_TypeClass_Pointer;
    private static bool TypeIsFunctionProto(CXType type) => type.TypeClass is CX_TypeClass.CX_TypeClass_FunctionProto or CX_TypeClass.CX_TypeClass_FunctionNoProto;
    private static bool TypeIsRecord(CXType type) => type.TypeClass == CX_TypeClass.CX_TypeClass_Record;
    private static bool TypeIsEnum(CXType type) => type.TypeClass == CX_TypeClass.CX_TypeClass_Enum;

    private static (BindingType Type, string? IgnoreReason) TryMapType(
        CXType type,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        IReadOnlyDictionary<string, BindingType> genericParameters,
        bool isReturn = false)
    {
        try
        {
            return (MapType(type, recordsByName, enumsByName, managedTypeOverrides, genericParameters, isReturn), null);
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
        if (TypeIsFunctionProto(normalizedType) || (TypeIsPointer(normalizedType) && TypeIsFunctionProto(normalizedType.PointeeType.CanonicalType)))
        {
            throw new NotSupportedException($"function pointer type '{NormalizeTypeName(type.Spelling.ToString())}' is not yet supported");
        }

        var explicitDirection = TryGetPassKindOverride(functionCursor, parameterCursor, out var overrideDirection)
            ? overrideDirection
            : (BindingParameterDirection?)null;

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
        var normalizedType = type.CanonicalType;
        if (TypeIsFunctionProto(normalizedType) || (TypeIsPointer(normalizedType) && TypeIsFunctionProto(normalizedType.PointeeType.CanonicalType)))
        {
            return BindingType.CreateConfigured(NormalizeTypeName(type.Spelling.ToString()), "global::System.IntPtr");
        }

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
        if (!functionDecl.IsDeleted || function.IsIgnored())
        {
            return;
        }

        var callableLabel = function.IsConstructor
            ? $"constructor '{function.CppQualifiedName}'"
            : $"callable '{function.CppQualifiedName}'";
        function.SetIgnoreReason($"{callableLabel} is deleted in the native surface");
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
        return $"{entryPointPrefix}_{baseName}_{counter}";
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

        if (TypeIsLValueReference(normalizedType))
        {
            var sourcePointeeType = type.PointeeType;
            var normalizedPointeeType = normalizedType.PointeeType.CanonicalType;

            if (TypeIsRecord(normalizedPointeeType))
            {
                var mappedType = MapType(normalizedPointeeType, recordsByName, enumsByName, managedTypeOverrides, genericParameters);
                var abiKind = mappedType.Kind == BindingTypeKind.Primitive
                    ? BindingParameterAbiKind.Reference
                    : BindingParameterAbiKind.Value;
                return (
                    mappedType,
                    explicitDirection ?? BindingParameterDirection.In,
                    abiKind);
            }

            var pointeeTypeName = NormalizeTypeName(sourcePointeeType.Spelling.ToString());
            if (TypeIsPointer(normalizedPointeeType) &&
                managedTypeOverrides.TryGetValue(pointeeTypeName, out var pointeeOverride) &&
                pointeeOverride.PublicTypeName.Contains("ReadOnlySpan", StringComparison.Ordinal))
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
            if (TryGetManagedTypeOverride(normalizedTypeName, normalizedTypeName, managedTypeOverrides, isReturn: false, out _))
            {
                var pointeeIsVoidOrRecord = TypeIsRecord(normalizedType.PointeeType.CanonicalType) ||
                    normalizedType.PointeeType.CanonicalType.kind == CXTypeKind.CXType_Void;
                return (
                    MapType(type, recordsByName, enumsByName, managedTypeOverrides, genericParameters),
                    explicitDirection ?? (pointeeIsVoidOrRecord
                        ? BindingParameterDirection.In
                        : (type.PointeeType.IsConstQualified ? BindingParameterDirection.In : BindingParameterDirection.Out)),
                    BindingParameterAbiKind.Value);
            }

            var sourcePointeeType = type.PointeeType;
            var normalizedPointeeType = normalizedType.PointeeType.CanonicalType;

            if (normalizedPointeeType.kind == CXTypeKind.CXType_Void || TypeIsRecord(normalizedPointeeType))
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

    private static bool TryGetManagedTypeOverride(
        string spelledTypeName,
        string canonicalTypeName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        bool isReturn,
        out ManagedTypeOverride? managedTypeOverride)
    {
        foreach (var candidate in EnumerateManagedTypeOverrideKeys(spelledTypeName, canonicalTypeName, isReturn))
        {
            if (managedTypeOverrides.TryGetValue(candidate, out managedTypeOverride))
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

            if (ConcreteTypePrefixMatches(spelledTypeName, candidateOverride.CppTypeName) ||
                ConcreteTypePrefixMatches(canonicalTypeName, candidateOverride.CppTypeName))
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
                    GetVisibleTemplateArgumentCount(candidateOverride)))
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

        if (!string.Equals(candidateHead, configuredHead, StringComparison.Ordinal) ||
            candidateArguments.Count < configuredArguments.Count)
        {
            return false;
        }

        for (var index = 0; index < configuredArguments.Count; index++)
        {
            if (!string.Equals(candidateArguments[index], configuredArguments[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
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

        foreach (var candidateTypeName in new[] { spelledTypeName, canonicalTypeName })
        {
            if (!TryGetTopLevelTemplateArguments(candidateTypeName, out var candidateHead, out var candidateArguments))
            {
                continue;
            }

            if (string.Equals(candidateHead, patternHead, StringComparison.Ordinal) &&
                patternArguments.Count == expectedArgumentCount &&
                candidateArguments.Count >= visibleArgumentCount &&
                candidateArguments.Count <= expectedArgumentCount)
            {
                return true;
            }
        }

        return false;
    }

    private static int GetVisibleTemplateArgumentCount(ManagedTypeOverride managedTypeOverride)
    {
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

    private static IEnumerable<string> EnumerateManagedTypeOverrideKeys(string spelledTypeName, string canonicalTypeName, bool isReturn)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in new[] { spelledTypeName, canonicalTypeName })
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
    }

    private static BindingType MapType(
        CXType type,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        IReadOnlyDictionary<string, BindingType> genericParameters,
        bool isReturn = false)
    {
        var normalizedType = type.CanonicalType;
        var normalizedTypeName = NormalizeTypeName(type.Spelling.ToString());
        var canonicalTypeName = NormalizeTypeName(normalizedType.Spelling.ToString());
        normalizedTypeName = SelectConcreteTemplateTypeName(normalizedTypeName, ResolveAliasTypeName(normalizedTypeName));
        canonicalTypeName = SelectConcreteTemplateTypeName(canonicalTypeName, GetUnderlyingTypeName(type));
        var resolvedTypeName = SelectConcreteTemplateTypeName(normalizedTypeName, canonicalTypeName);

        if (TryMapGenericParameterType(normalizedTypeName, genericParameters, out var genericParameterType))
        {
            return genericParameterType;
        }

        if (isReturn && TryGetManagedTypeOverride(normalizedTypeName, canonicalTypeName, managedTypeOverrides, isReturn: true, out var returnOverride))
        {
            return CreateConfiguredOrWrapperType(returnOverride!, recordsByName, enumsByName, managedTypeOverrides, genericParameters, resolvedTypeName);
        }

        TryGetManagedTypeOverride(normalizedTypeName, canonicalTypeName, managedTypeOverrides, isReturn: false, out var managedTypeOverride);

        if (managedTypeOverride is not null)
        {
            return CreateConfiguredOrWrapperType(managedTypeOverride, recordsByName, enumsByName, managedTypeOverrides, genericParameters, resolvedTypeName);
        }

        if (TypeIsLValueReference(normalizedType))
        {
            return MapType(normalizedType.PointeeType, recordsByName, enumsByName, managedTypeOverrides, genericParameters)
                .WithCppIsRecordValue(false);
        }

        if (TypeIsPointer(normalizedType))
        {
            var pointeeType = normalizedType.PointeeType.CanonicalType;
            var concretePointeeTypeName = SelectConcreteTemplateTypeName(
                NormalizeTypeName(pointeeType.Spelling.ToString()),
                NormalizeTypeName(type.PointeeType.Spelling.ToString()));
            if (TryCreateSpecializedRecordHandleType(
                    normalizedTypeName,
                    concretePointeeTypeName,
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

            if (TypeIsRecord(pointeeType))
            {
                var recordName = GetRecordQualifiedName(pointeeType, recordsByName);
                if (!recordsByName.TryGetValue(recordName, out var record))
                {
                    // Use the resolved qualified name for CppQualifiedName so the
                    // placeholder passes namespace-exclusion checks in the pipeline.
                    record = new RecordBinding($"record:{recordName}", recordName, ToPascalCase(recordName), recordName);
                    record.SetMetadata(MetadataKeys.PlaceholderRecord, true);
                    IndexRecordKeys(recordsByName, record);
                }

                var genericArguments = record.HasMetadata(MetadataKeys.PlaceholderRecord)
                    ? Array.Empty<BindingType>()
                    : TryParseGenericArguments(concretePointeeTypeName, recordsByName, enumsByName, managedTypeOverrides, genericParameters);
                return genericArguments.Count == 0
                    ? record.HandleType
                    : BindingType.CreateHandle(normalizedTypeName, record.ManagedName, record.SymbolId, record.HandleMarshallerTypeName, genericArguments);
            }
        }

        if (TypeIsRecord(normalizedType))
        {
            var concreteRecordTypeName = SelectConcreteTemplateTypeName(normalizedTypeName, canonicalTypeName);
            if (TryCreateSpecializedRecordHandleType(
                    $"{normalizedTypeName}*",
                    concreteRecordTypeName,
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
                record = new RecordBinding($"record:{recordName}", recordName, ToPascalCase(recordName), recordName);
                record.SetMetadata(MetadataKeys.PlaceholderRecord, true);
                IndexRecordKeys(recordsByName, record);
            }

            var genericArguments = record.HasMetadata(MetadataKeys.PlaceholderRecord)
                ? Array.Empty<BindingType>()
                : TryParseGenericArguments(concreteRecordTypeName, recordsByName, enumsByName, managedTypeOverrides, genericParameters);
            return genericArguments.Count == 0
                ? record.HandleType.WithCppIsRecordValue(true)
                : BindingType.CreateHandle($"{NormalizeTypeName(normalizedType.Spelling.ToString())}*", record.ManagedName, record.SymbolId, record.HandleMarshallerTypeName, genericArguments, cppIsRecordValue: true);
        }

        if (TypeIsEnum(normalizedType))
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

        if (!managedTypeOverride.HasManagedBaseType && !managedTypeOverride.HasExposedMembers)
        {
            return BindingType.CreateConfigured(
                effectiveCppTypeName,
                managedTypeOverride.PublicTypeName,
                managedTypeOverride.MarshallerTypeName,
                managedTypeOverride.ManagedMarshaller);
        }

        var recordName = StripGenericArguments(NormalizeTypeName(managedTypeOverride.CppTypeName));
        if (!recordsByName.TryGetValue(recordName, out var record))
        {
            record = new RecordBinding(
                $"record:{recordName}",
                recordName,
                GetSimpleManagedTypeName(managedTypeOverride.PublicTypeName),
                managedTypeOverride.TemplateParameterNames.Count > 0 ? recordName : managedTypeOverride.CppTypeName)
            {
                ManagedTypeFormat = managedTypeOverride.PublicTypeName,
                ManagedNamespace = managedTypeOverride.ManagedNamespace
            };
            ApplySyntheticGenericParameters(record, managedTypeOverride.TemplateParameterNames);
            record.SetMetadata(MetadataKeys.PlaceholderRecord, true);
            IndexRecordKeys(recordsByName, record);
        }
        else
        {
            record.ManagedTypeFormat ??= managedTypeOverride.PublicTypeName;
            record.ManagedNamespace ??= managedTypeOverride.ManagedNamespace;
            if (record.HasMetadata(MetadataKeys.PlaceholderRecord))
            {
                record.ManagedName = GetSimpleManagedTypeName(managedTypeOverride.PublicTypeName);
            }

            ApplySyntheticGenericParameters(record, managedTypeOverride.TemplateParameterNames);
        }

        var genericArguments = managedTypeOverride.TemplateParameterNames.Count == 0
            ? Array.Empty<BindingType>()
            : ProjectVisibleTemplateArguments(
                ParseConcreteTemplateArgumentsOrOpaque(effectiveCppTypeName, recordsByName, enumsByName, managedTypeOverrides, genericParameters),
                managedTypeOverride.VisibleTemplateParameterIndices);

        return genericArguments.Count == 0
            ? record.HandleType
            : BindingType.CreateHandle($"{effectiveCppTypeName}*", record.ManagedName, record.SymbolId, record.HandleMarshallerTypeName, genericArguments);
    }

    private static BindingType CreateProjectedHandleType(
        ManagedTypeOverride managedTypeOverride,
        string effectiveCppTypeName,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        IReadOnlyDictionary<string, BindingType> genericParameters)
    {
        var concreteArguments = managedTypeOverride.TemplateParameterNames.Count == 0
            ? Array.Empty<BindingType>()
            : ProjectVisibleTemplateArguments(
                ParseConcreteTemplateArgumentsOrOpaque(effectiveCppTypeName, recordsByName, enumsByName, managedTypeOverrides, genericParameters),
                managedTypeOverride.VisibleTemplateParameterIndices);

        if (concreteArguments.Count != 1 || concreteArguments[0].Kind != BindingTypeKind.Handle)
        {
            throw new NotSupportedException($"Projected managed_type on '{managedTypeOverride.CppTypeName}' must resolve to a single handle-like managed type.");
        }

        var projectedType = concreteArguments[0];
        var projectedRecord = projectedType.RecordId is null
            ? null
            : recordsByName.Values.FirstOrDefault(candidate => string.Equals(candidate.SymbolId, projectedType.RecordId, StringComparison.Ordinal));
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
        CXType sourceType,
        BindingType mappedType,
        IReadOnlyDictionary<string, ManagedTypeOverride> managedTypeOverrides,
        bool preferReturnOverride = true)
    {
        if (mappedType.Kind != BindingTypeKind.Handle)
        {
            return false;
        }

        var normalizedType = sourceType.CanonicalType;
        var normalizedTypeName = NormalizeTypeName(sourceType.Spelling.ToString());
        var canonicalTypeName = NormalizeTypeName(normalizedType.Spelling.ToString());
        normalizedTypeName = SelectConcreteTemplateTypeName(normalizedTypeName, ResolveAliasTypeName(normalizedTypeName));
        canonicalTypeName = SelectConcreteTemplateTypeName(canonicalTypeName, GetUnderlyingTypeName(sourceType));

        if (preferReturnOverride &&
            TryGetManagedTypeOverride(normalizedTypeName, canonicalTypeName, managedTypeOverrides, isReturn: true, out var returnOverride) &&
            string.Equals(returnOverride?.ReturnOwnership, "owning", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return TryGetManagedTypeOverride(normalizedTypeName, canonicalTypeName, managedTypeOverrides, isReturn: false, out var overrideValue) &&
            string.Equals(overrideValue?.ReturnOwnership, "owning", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryGetUnsupportedFunctionPointerReason(CXType type, string label)
    {
        var normalized = type.CanonicalType;
        var normalizedText = NormalizeTypeName(normalized.Spelling.ToString());
        var underlyingText = GetUnderlyingTypeName(type);
        var aliasText = ResolveAliasTypeName(NormalizeTypeName(type.Spelling.ToString()));
        var canonicalAliasText = ResolveAliasTypeName(NormalizeTypeName(type.CanonicalType.Spelling.ToString()));
        if (TypeIsFunctionProto(normalized) ||
            (TypeIsPointer(normalized) && TypeIsFunctionProto(normalized.PointeeType.CanonicalType)) ||
            normalizedText.Contains("(*)", StringComparison.Ordinal) ||
            underlyingText.Contains("(*)", StringComparison.Ordinal) ||
            aliasText.Contains("(*)", StringComparison.Ordinal) ||
            canonicalAliasText.Contains("(*)", StringComparison.Ordinal) ||
            normalizedText.Contains(")(", StringComparison.Ordinal) ||
            underlyingText.Contains(")(", StringComparison.Ordinal) ||
            aliasText.Contains(")(", StringComparison.Ordinal) ||
            canonicalAliasText.Contains(")(", StringComparison.Ordinal))
        {
            return $"{label} is unsupported in this bootstrap surface: function pointer types are not yet supported";
        }

        return null;
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

    private static void ApplySyntheticGenericParameters(RecordBinding record, IReadOnlyList<string> templateParameterNames)
    {
        if (templateParameterNames.Count == 0)
        {
            return;
        }

        record.CppQualifiedName = StripGenericArguments(record.CppQualifiedName);

        if (record.GenericParameters.Count > 0)
        {
            return;
        }

        foreach (var (name, index) in templateParameterNames.Select((name, index) => (name, index)))
        {
            record.GenericParameters.Add(new BindingGenericParameter(name, index));
        }
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
                result.Add(ParseTypeToken(argument, recordsByName, enumsByName, managedTypeOverrides, genericParameters));
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

        if (TryGetQualifiedNamedType(type, out _, out var qualifiedName))
        {
            var strippedQualifiedName = NormalizeNamedTypeName(StripGenericArguments(qualifiedName));
            if (!string.IsNullOrWhiteSpace(strippedQualifiedName))
            {
                return strippedQualifiedName;
            }
        }

        return unqualifiedName;
    }

    private static string ResolveQualifiedTypeName(CXType type, IDictionary<string, RecordBinding> recordsByName)
    {
        var spelling = NormalizeTypeName(type.Spelling.ToString());

        if (TryGetQualifiedNamedType(type, out var unqualifiedName, out var qualifiedName))
        {
            var replacementSource = qualifiedName.Contains('<', StringComparison.Ordinal)
                ? StripGenericArguments(unqualifiedName)
                : unqualifiedName;
            var replacementTarget = qualifiedName.Contains('<', StringComparison.Ordinal)
                ? StripGenericArguments(qualifiedName)
                : qualifiedName;

            var pattern = $@"(?<![:\w]){Regex.Escape(replacementSource)}(?![:\w])";
            spelling = Regex.Replace(spelling, pattern, replacementTarget, RegexOptions.CultureInvariant);
        }

        foreach (var record in recordsByName.Values.OrderByDescending(static record => record.NativeName.Length))
        {
            if (record.IsGeneric)
            {
                continue;
            }

            var pattern = $@"(?<![:\w]){Regex.Escape(record.NativeName)}(?![:\w])";
            spelling = Regex.Replace(spelling, pattern, record.CppQualifiedName, RegexOptions.CultureInvariant);
        }

        return spelling;
    }

    private static bool TryGetQualifiedNamedType(CXType type, out string unqualifiedName, out string qualifiedName)
    {
        var sourceType = type;
        while (TypeIsAnyReference(sourceType) || TypeIsPointer(sourceType))
        {
            sourceType = sourceType.PointeeType;
        }

        if (TryPopulateQualifiedNamedType(sourceType.Declaration, out unqualifiedName, out qualifiedName))
        {
            return true;
        }

        var resolvedType = sourceType.CanonicalType;
        while (TypeIsAnyReference(resolvedType) || TypeIsPointer(resolvedType))
        {
            resolvedType = resolvedType.PointeeType.CanonicalType;
        }

        return TryPopulateQualifiedNamedType(resolvedType.Declaration, out unqualifiedName, out qualifiedName);
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
            .Replace("__cxx11::", string.Empty, StringComparison.Ordinal)
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
            .Replace("__cxx11::", string.Empty, StringComparison.Ordinal)
            .Replace(" *", "*", StringComparison.Ordinal)
            .Replace(" &", "&", StringComparison.Ordinal)
            .Replace(":: ", "::", StringComparison.Ordinal)
            .Replace(" ::", "::", StringComparison.Ordinal)
            .Replace(" <", "<", StringComparison.Ordinal)
            .Replace("> ", ">", StringComparison.Ordinal);
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
                    node.SetMetadata(MetadataKeys.IgnoreReason, reason);
                    return;
                }
            }

            return;
        }

        node.SetMetadata(MetadataKeys.IgnoreReason, NormalizeIgnoreReason(match.Body) ?? "marked [[sharpcpp::ignore]]");
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

    private static void ExtractAndApplyDocumentation(Decl decl, BindingNode node)
    {
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
        if (string.IsNullOrWhiteSpace(nativeName) || enumsByName.ContainsKey(nativeName))
        {
            return;
        }

        var @enum = new EnumBinding(
            $"enum:{nativeName}",
            nativeName,
            ToPascalCase(nativeName),
            NormalizeTypeName(enumDecl.IntegerType.AsString),
            GetCppQualifiedName(enumDecl))
        {
            IsFlags = HasSharpCppAttribute(GetSourceText(enumDecl.TranslationUnit.Handle, enumDecl.Extent), "flags")
        };
        @enum.SetMetadata(MetadataKeys.DeclarationDisplayText, GetDeclarationDisplayText(enumDecl));

        CheckAndApplyIgnoreAttribute(enumDecl, @enum);
        TryApplyRename(enumDecl, @enum);
        ExtractAndApplyDocumentation(enumDecl, @enum);

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

    private static void IndexRecordKeys(IDictionary<string, RecordBinding> recordsByName, RecordBinding record)
    {
        TryAddBindingKey(recordsByName, record.NativeName, record);
        TryAddBindingKey(recordsByName, record.CppQualifiedName, record);
    }

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
        TryAddBindingKey(enumsByName, @enum.CppQualifiedName, @enum);
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
        var defaultValue = GetManagedDefaultValueExpression(parameterDecl, parameterType);
        parameter.ManagedDefaultValueExpression = defaultValue;

        if (defaultValue is null && parameterDecl.HasDefaultArg)
        {
            parameter.SetMetadata(MetadataKeys.UnexpressibleDefault, true);
        }

        // Only mark as undefaultable if the parameter actually has a C++ default value.
        // If there's no default at all, the overload split is unnecessary — the parameter
        // is already required.
        if (parameterDecl.HasDefaultArg &&
            managedTypeOverrides is not null &&
            managedTypeOverrides.TryGetValue(parameterType.CppTypeName, out var overrideInfo) &&
            overrideInfo.Undefaultable)
        {
            parameter.SetMetadata(MetadataKeys.UndefaultableParam, true);
        }
    }

    private static string? GetManagedDefaultValueExpression(ParmVarDecl parameterDecl, BindingType parameterType)
    {
        if (!parameterDecl.HasDefaultArg)
        {
            return null;
        }

        // Use the CXCursor handle for expression-level operations since
        // Expr doesn't fully expose the SubExprAsWritten/kind pattern.
        var parameterCursor = parameterDecl.Handle;
        var defaultArgumentCursor = parameterCursor.DefaultArg;
        if (defaultArgumentCursor.Kind == CXCursorKind.CXCursor_FirstInvalid && parameterDecl.HasUninstantiatedDefaultArg)
        {
            defaultArgumentCursor = parameterDecl.UninstantiatedDefaultArg.Handle;
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

        if (parameterType.Kind == BindingTypeKind.Handle)
        {
            return null;
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
            return Regex.IsMatch(expression, "^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)
                ? $"{managedTypeName}.{ToPascalCase(expression)}"
                : expression;
        }

        var valueName = expression[(separatorIndex + 2)..];
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
        string? headerIncludePath)
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
                    VisitNamespace(nsChild, compilation, recordsByName, enumsByName, managedTypeOverrides, entryPointPrefix, ownedFileRoots, headerIncludePath);
                    break;

                case RecordDecl recordDecl:
                    RegisterRecord(recordDecl, compilation, recordsByName, enumsByName, managedTypeOverrides, entryPointPrefix,
                        ResolveDeclIncludePath(recordDecl, ownedFileRoots, headerIncludePath ?? string.Empty));
                    break;

                case ClassTemplateDecl classTemplateDecl:
                    RegisterTemplateRecord(classTemplateDecl, compilation, recordsByName, enumsByName, managedTypeOverrides, entryPointPrefix,
                        ResolveDeclIncludePath(classTemplateDecl, ownedFileRoots, headerIncludePath ?? string.Empty));
                    break;

                case EnumDecl enumChildDecl:
                    RegisterEnum(enumChildDecl, compilation, enumsByName,
                        ResolveDeclIncludePath(child, ownedFileRoots, headerIncludePath ?? string.Empty));
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
                        ExtractAndApplyDocumentation(funcTemplateDecl, function);
                        ApplyCallableDefinitionMetadata(funcTemplateDecl, function);
                        ApplyDeletedCallableIgnore(funcTemplateDecl.TemplatedDecl, function);
                        function.HeaderIncludePath = ResolveDeclIncludePath(child, ownedFileRoots, headerIncludePath ?? string.Empty);
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
                    ExtractAndApplyDocumentation(functionDecl, function);
                    ApplyCallableDefinitionMetadata(functionDecl, function);
                    ApplyDeletedCallableIgnore(functionDecl, function);
                    function.HeaderIncludePath = ResolveDeclIncludePath(child, ownedFileRoots, headerIncludePath ?? string.Empty);
                    compilation.Members.Add(function);
                    break;
                }
            }
        }
    }

    // ManagedTypeOverride is now in MarshallerConfigExtractor (see using alias above).

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
        var normalized = SelectConcreteTemplateTypeName(NormalizeTypeName(token), ResolveAliasTypeName(token));

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
                return record.HandleType;
            }
        }

        if (TryGetManagedTypeOverride(normalized, normalized, managedTypeOverrides, isReturn: false, out var managedTypeOverride))
        {
            return CreateConfiguredOrWrapperType(managedTypeOverride!, recordsByName, enumsByName, managedTypeOverrides, genericParameters, normalized);
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
            _ when enumsByName.TryGetValue(NormalizeNamedTypeName(normalized), out var @enum) => BindingType.CreateEnum(@enum.NativeName, @enum.ManagedName),
            _ when normalized.Contains("::", StringComparison.Ordinal) && enumsByName.TryGetValue(NormalizeNamedTypeName(normalized[(normalized.LastIndexOf("::", StringComparison.Ordinal) + 2)..]), out var shortEnum) => BindingType.CreateEnum(shortEnum.NativeName, shortEnum.ManagedName),
            _ when recordsByName.TryGetValue(NormalizeNamedTypeName(normalized), out var record) => record.HandleType,
            _ when normalized.Contains("::", StringComparison.Ordinal) && recordsByName.TryGetValue(NormalizeNamedTypeName(normalized[(normalized.LastIndexOf("::", StringComparison.Ordinal) + 2)..]), out var shortRecord) => shortRecord.HandleType,
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

    private static void BuildTypeAliasIndex(Decl decl)
    {
        s_typeAliases.Clear();
        CollectTypeAliases(decl);
    }

    private static void CollectTypeAliases(Decl decl)
    {
        foreach (var child in decl.Decls)
        {
            if (child is TypedefNameDecl typedefNameDecl)
            {
                RegisterTypeAlias(typedefNameDecl);
            }

            CollectTypeAliases(child);
        }
    }

    private static void RegisterTypeAlias(TypedefNameDecl typedefNameDecl)
    {
        var aliasName = GetCppQualifiedName(typedefNameDecl);
        var shortAliasName = typedefNameDecl.Name ?? typedefNameDecl.Spelling.ToString();
        var underlyingTypeName = NormalizeTypeName(typedefNameDecl.UnderlyingType.Handle.Spelling.ToString());

        if (string.IsNullOrWhiteSpace(underlyingTypeName))
        {
            return;
        }

        AddTypeAlias(aliasName, underlyingTypeName);
        AddTypeAlias(shortAliasName, underlyingTypeName);
    }

    private static void AddTypeAlias(string aliasName, string underlyingTypeName)
    {
        var normalizedAlias = NormalizeTypeName(aliasName);
        if (string.IsNullOrWhiteSpace(normalizedAlias))
        {
            return;
        }

        s_typeAliases[normalizedAlias] = underlyingTypeName;
    }

    private static string ResolveAliasTypeName(string typeName)
    {
        var normalizedTypeName = NormalizeTypeName(typeName);
        if (string.IsNullOrWhiteSpace(normalizedTypeName))
        {
            return string.Empty;
        }

        var suffix = string.Empty;
        var baseTypeName = normalizedTypeName;
        while (baseTypeName.EndsWith("*", StringComparison.Ordinal) || baseTypeName.EndsWith("&", StringComparison.Ordinal))
        {
            suffix = baseTypeName[^1] + suffix;
            baseTypeName = baseTypeName[..^1].TrimEnd();
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (s_typeAliases.TryGetValue(baseTypeName, out var aliasedTypeName) && visited.Add(baseTypeName))
        {
            baseTypeName = NormalizeTypeName(aliasedTypeName);
        }

        return baseTypeName + suffix;
    }

    private static int GetTemplateSpecificityScore(string typeName)
    {
        var score = 0;

        score += Regex.Matches(typeName, "::", RegexOptions.CultureInvariant).Count * 2;
        score += Regex.Matches(typeName, @"(?<![:\w])T[A-Za-z0-9_]*(?![:\w])", RegexOptions.CultureInvariant).Count * -10;

        return score;
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
                var instantiations = ParseInstantiationMatches(SharpCppAttributeScanner.Scan(lines[lineIndex]).Where(match => string.Equals(match.Name, "instantiate", StringComparison.Ordinal)), new Dictionary<string, RecordBinding>(StringComparer.Ordinal), new Dictionary<string, EnumBinding>(StringComparer.Ordinal), new Dictionary<string, ManagedTypeOverride>(StringComparer.Ordinal));
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

        bool isOverride = false;
        bool isFinal = false;

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
}
