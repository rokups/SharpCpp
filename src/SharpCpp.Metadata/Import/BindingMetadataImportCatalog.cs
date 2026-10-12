using System.Security.Cryptography;
using System.Text;

using SharpCpp.Metadata.Export;
using SharpCpp.Model.Ast;
using SharpCpp.Model.Utilities;

namespace SharpCpp.Metadata.Import;

public sealed class BindingMetadataImportCatalog
{
    private readonly IReadOnlyList<BindingMetadataDocument> _documents;

    private BindingMetadataImportCatalog(IReadOnlyList<BindingMetadataDocument> documents, IReadOnlyList<BindingMetadataImport> imports)
    {
        _documents = documents;
        Imports = imports;
    }

    public static BindingMetadataImportCatalog Empty { get; } = new([], []);

    public IReadOnlyList<BindingMetadataImport> Imports { get; }

    public static BindingMetadataImportCatalog Load(IEnumerable<string> metadataFiles)
    {
        ArgumentNullException.ThrowIfNull(metadataFiles);

        var documents = new List<BindingMetadataDocument>();
        var imports = new List<BindingMetadataImport>();

        foreach (var metadataFile in metadataFiles)
        {
            var fullPath = Path.GetFullPath(metadataFile);
            var json = File.ReadAllText(fullPath);
            var document = BindingMetadataSerializer.Deserialize(json);
            if (document.SchemaVersion != BindingMetadataSchema.CurrentVersion)
            {
                throw new InvalidOperationException($"Imported metadata '{fullPath}' uses unsupported schema version '{document.SchemaVersion}'.");
            }

            documents.Add(document);
            imports.Add(new BindingMetadataImport(document.Source.Package, ComputeHash(json)));
        }

        return new BindingMetadataImportCatalog(documents, imports
            .OrderBy(static item => item.Package, StringComparer.Ordinal)
            .ThenBy(static item => item.Hash, StringComparer.Ordinal)
            .ToArray());
    }

    public void Seed(BindingCompilation compilation, IDictionary<string, RecordBinding> recordsByName, IDictionary<string, EnumBinding> enumsByName)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(recordsByName);
        ArgumentNullException.ThrowIfNull(enumsByName);

        // Native thunks for imported types (generic instantiations, shared helpers) need the
        // dependency's declarations in the consumer translation unit.
        // Their marshaller specializations are needed as well.
        foreach (var headerFile in _documents.SelectMany(static document =>
                     document.Fingerprint.HeaderFiles.Concat(document.Fingerprint.NativeSupportHeaders ?? [])))
        {
            if (!compilation.NativeSupportHeaders.Contains(headerFile, StringComparer.Ordinal))
            {
                compilation.NativeSupportHeaders.Add(headerFile);
            }
        }

        // First seed every top-level declaration. Member and alias references can
        // point across documents, so resolving them before this pass is complete
        // would leave dangling record/enum ids.
        foreach (var document in _documents)
        {
            foreach (var importedType in document.Types.OrderBy(static item => item.SymbolId, StringComparer.Ordinal))
            {
                switch (importedType.Kind)
                {
                    case MetadataTypeKind.Handle:
                    case MetadataTypeKind.Record:
                        SeedRecord(compilation, recordsByName, document, importedType);
                        break;

                    case MetadataTypeKind.Enum:
                        SeedEnum(compilation, enumsByName, document, importedType);
                        break;
                }
            }

        }

        // Restore record members after all imported type indexes are available.
        foreach (var document in _documents)
        {
            foreach (var importedType in document.Types
                .Where(static item => item.Kind is MetadataTypeKind.Handle or MetadataTypeKind.Record)
                .OrderBy(static item => item.SymbolId, StringComparer.Ordinal))
            {
                // The qualified C++ name is the identity. The short name key is only a
                // convenience key for the first record that used it, so verify the SymbolId.
                var importedSymbolId = $"import:{document.Source.Package}:{importedType.SymbolId}";
                var cppQualifiedName = GetImportedRecordCppName(importedType);
                if (!recordsByName.TryGetValue(cppQualifiedName, out var record) ||
                    !string.Equals(record.SymbolId, importedSymbolId, StringComparison.Ordinal))
                {
                    if (!recordsByName.TryGetValue(importedType.NativeName, out record) ||
                        !string.Equals(record.SymbolId, importedSymbolId, StringComparison.Ordinal))
                    {
                        record = recordsByName.Values.FirstOrDefault(candidate =>
                            string.Equals(candidate.SymbolId, importedSymbolId, StringComparison.Ordinal));
                    }
                }

                if (record is not null &&
                    string.Equals(record.SymbolId, importedSymbolId, StringComparison.Ordinal))
                {
                    SeedRecordMembers(compilation, recordsByName, enumsByName, document, importedType, record);
                }
            }
        }

        // Finally seed aliases so targets can resolve to declarations from any
        // imported package, regardless of metadata file ordering.
        foreach (var document in _documents)
        {
            foreach (var alias in (document.Aliases ?? Array.Empty<BindingMetadataAlias>())
                .OrderBy(static item => item.ManagedName, StringComparer.Ordinal)
                .ThenBy(static item => item.CppQualifiedName, StringComparer.Ordinal))
            {
                SeedAlias(compilation, recordsByName, enumsByName, document, alias);
            }
        }

        // Release thunks the dependencies export for their owned-string free functions. The same
        // document can be loaded more than once, so identical entries are added once.
        foreach (var document in _documents)
        {
            foreach (var export in (document.StringFreeFunctions ?? Array.Empty<BindingMetadataStringFreeExport>())
                .OrderBy(static item => item.NativeName, StringComparer.Ordinal))
            {
                var imported = new StringFreeFunction(
                    export.NativeName,
                    export.CppQualifiedName,
                    export.ParameterCppTypeName,
                    new StringFreeFunctionImport(document.Source.NativeLibrary, export.EntryPoint));
                if (!compilation.ImportedStringFreeFunctions.Contains(imported))
                {
                    compilation.ImportedStringFreeFunctions.Add(imported);
                }
            }
        }
    }

    private static void SeedRecord(BindingCompilation compilation, IDictionary<string, RecordBinding> recordsByName, BindingMetadataDocument document, BindingMetadataType importedType)
    {
        // Generic records re-apply their arguments per instantiation; a full
        // specialization (Marshaller<basic_string<char>>) is only valid spelled with them.
        var cppQualifiedName = GetImportedRecordCppName(importedType);

        // The qualified C++ name is the identity. A short name hit only counts as a
        // duplicate when it names the same qualified type ("Rml::Context" is not "Urho3D::Context").
        if (recordsByName.ContainsKey(cppQualifiedName) ||
            (recordsByName.TryGetValue(importedType.NativeName, out var sameShortName) &&
             string.Equals(sameShortName.CppQualifiedName, cppQualifiedName, StringComparison.Ordinal)))
        {
            return;
        }

        var isSharedWrapper = !string.IsNullOrWhiteSpace(importedType.ManagedWrapperAssembly);
        var managedNamespace = importedType.ManagedNamespace;
        if (isSharedWrapper && string.IsNullOrWhiteSpace(managedNamespace))
        {
            managedNamespace = GetManagedNamespaceFromQualifiedName(importedType.ManagedName) ??
                GetManagedNamespaceFromQualifiedName(importedType.Projection.PublicType);
        }

        managedNamespace ??= document.Source.ManagedNamespace;
        var managedDeclarationName = GetManagedDeclarationName(importedType.ManagedName);
        var managedTypeName = GetQualifiedManagedTypeName(managedNamespace, importedType.ManagedName);
        var managedBaseTypeName = string.IsNullOrWhiteSpace(importedType.ManagedBaseTypeName)
            ? null
            : ResolveQualifiedManagedBaseTypeName(document, importedType.ManagedBaseTypeName!);
        var hasCopyConstructor = importedType.ManagedWrapperCopyable ?? importedType.HasCopyConstructor ?? false;

        var record = new RecordBinding(
            $"import:{document.Source.Package}:{importedType.SymbolId}",
            importedType.NativeName,
            managedTypeName,
            cppQualifiedName)
        {
            ManagedNamespace = managedNamespace,
            ManagedBaseTypeName = managedBaseTypeName,
            ManagedTypeFormat = importedType.ManagedTypeFormat ?? (importedType.Projection.GenericArguments is { Count: > 0 }
                ? BuildQualifiedManagedTypeFormat(managedTypeName, importedType.IsFixedArray || importedType.IsDynamicArray ? 1 : importedType.Projection.GenericArguments.Count)
                : null),
            ManagedBaseTypeConstraints = importedType.ManagedBaseTypeConstraints,
            ManagedInstanceCacheFactoryExpression = importedType.ManagedInstanceCacheFactoryExpression,
            ManagedInstanceCacheTypeName = importedType.ManagedInstanceCacheTypeName,
            EmitDefaultNativeReleaseOverride = importedType.EmitDefaultNativeReleaseOverride,
            HasCopyConstructor = hasCopyConstructor
        };

        // RecordBinding.ManagedName is a declaration identifier. Keep namespace
        // qualification in ManagedNamespace/ManagedTypeFormat so generated
        // marshaller helper identifiers remain valid for imported wrappers.
        if (isSharedWrapper)
        {
            record.ManagedName = managedDeclarationName;
        }

        if (!string.IsNullOrWhiteSpace(importedType.ManagedWrapperAssembly))
        {
            record.SetMetadata(MetadataKeys.ManagedWrapperAssembly, importedType.ManagedWrapperAssembly);
        }

        if (!string.IsNullOrWhiteSpace(importedType.ManagedWrapperProviderKind))
        {
            record.SetMetadata(MetadataKeys.ManagedWrapperProviderKind, importedType.ManagedWrapperProviderKind);
        }

        if (!string.IsNullOrWhiteSpace(importedType.ManagedWrapperProviderNamespace))
        {
            record.SetMetadata(MetadataKeys.ManagedWrapperProviderNamespace, importedType.ManagedWrapperProviderNamespace);
        }

        if (importedType.ManagedWrapperCopyable.HasValue)
        {
            record.SetMetadata(MetadataKeys.ManagedWrapperCopyable, importedType.ManagedWrapperCopyable.Value);
        }

        if (importedType.IsFixedArray)
        {
            record.SetMetadata(MetadataKeys.FixedArrayRecord, true);
        }

        if (importedType.IsDynamicArray)
        {
            record.SetMetadata(MetadataKeys.DynamicArrayRecord, true);
        }

        if (importedType.Projection.GenericArguments is { Count: > 0 })
        {
            foreach (var (argument, index) in importedType.Projection.GenericArguments.Select((argument, index) => (argument, index)))
            {
                var name = argument.Kind == "genericparameter" && !string.IsNullOrWhiteSpace(argument.PublicType)
                    ? argument.PublicType
                    : $"T{index}";
                record.GenericParameters.Add(new BindingGenericParameter(name, index));
            }
        }

        record.SetMetadata(MetadataKeys.SuppressEmission, true);

        SeedRecordKeys(recordsByName, record);
        compilation.Members.Add(record);
    }

    private static string GetImportedRecordCppName(BindingMetadataType importedType)
    {
        var qualifiedCppTypeName = GetQualifiedCppTypeName(importedType.Projection.CppType);
        return importedType.Projection.GenericArguments is { Count: > 0 } || importedType.IsFixedArray || importedType.IsDynamicArray
            ? StripGenericArguments(qualifiedCppTypeName)
            : qualifiedCppTypeName;
    }

    private static void SeedRecordMembers(
        BindingCompilation compilation,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        BindingMetadataDocument document,
        BindingMetadataType importedType,
        RecordBinding record)
    {
        foreach (var member in importedType.Members ?? Array.Empty<BindingMetadataRecordMember>())
        {
            if (!string.Equals(member.Kind, "method", StringComparison.Ordinal) &&
                !string.Equals(member.Kind, "constructor", StringComparison.Ordinal))
            {
                continue;
            }

            var isConstructor = member.IsConstructor || string.Equals(member.Kind, "constructor", StringComparison.Ordinal);
            var managedName = string.IsNullOrWhiteSpace(member.ManagedName)
                ? member.NativeName
                : member.ManagedName;
            var importedSymbolId = string.IsNullOrWhiteSpace(member.SymbolId)
                ? null
                : $"import:{document.Source.Package}:{member.SymbolId}";
            if (record.Members.OfType<FunctionBinding>().Any(existing =>
                    (importedSymbolId is not null && string.Equals(existing.SymbolId, importedSymbolId, StringComparison.Ordinal)) ||
                    (importedSymbolId is null && HasSameImportedMemberSignature(existing, managedName, member))))
            {
                continue;
            }

            var returnType = isConstructor
                ? record.HandleType
                : member.Type is null
                    ? BindingType.Void
                    : CreateAliasTargetType(recordsByName, enumsByName, document, member.Type);
            var cppQualifiedName = string.IsNullOrWhiteSpace(member.CppQualifiedName)
                ? $"{record.CppQualifiedName}::{member.NativeName}"
                : member.CppQualifiedName;
            var entryPoint = string.IsNullOrWhiteSpace(member.EntryPoint)
                ? member.NativeName
                : member.EntryPoint;
            if (!string.IsNullOrWhiteSpace(importedType.ManagedWrapperAssembly))
            {
                entryPoint = BuildImportedSharedEntryPointName(compilation, member, record);
            }
            var function = new FunctionBinding(
                importedSymbolId ?? $"import:{document.Source.Package}:{importedType.SymbolId}:{member.Kind}:{member.NativeName}:{managedName}",
                member.NativeName,
                managedName,
                returnType,
                entryPoint,
                member.CppReturnTypeName ?? (isConstructor ? $"{record.CppQualifiedName}*" : returnType.CppTypeName),
                cppQualifiedName)
            {
                IsStatic = member.IsStatic,
                IsConstructor = isConstructor,
                IsConstMethod = member.IsConstMethod,
                OperatorToken = member.OperatorToken,
                IsConversionOperator = member.IsConversionOperator,
                IsExplicitConversionOperator = member.IsExplicitConversionOperator,
                ReturnsOwnedHandle = member.ReturnsOwnedHandle,
                OwningRecordCppTypeName = member.OwningRecordCppTypeName ?? record.CppQualifiedName,
                ManagedNamespace = record.ManagedNamespace,
                AccessModifier = ParseAccessModifier(member.AccessModifier),
                IsVirtual = member.IsVirtual,
                IsPureVirtual = member.IsPureVirtual,
                IsFinal = member.IsFinal,
                IsOverride = member.IsOverride,
                ReturnPointerDepth = member.ReturnPointerDepth
            };

            if (!string.IsNullOrWhiteSpace(member.MarshallerExposedMethod))
            {
                function.SetMetadata(MetadataKeys.MarshallerExposedMethod, member.MarshallerExposedMethod);
            }

            foreach (var parameter in member.Parameters ?? Array.Empty<BindingMetadataParameter>())
            {
                var type = CreateAliasTargetType(recordsByName, enumsByName, document, parameter.Type);
                var direction = ParseParameterDirection(parameter.Direction);
                var abiKind = ParseParameterAbiKind(parameter.AbiKind, direction, type);
                function.Parameters.Add(new ParameterBinding(
                    $"import:{document.Source.Package}:{importedType.SymbolId}:{member.NativeName}:{parameter.Name}",
                    parameter.Name,
                    parameter.Name,
                    type,
                    direction,
                    abiKind,
                    parameter.CppTypeName ?? parameter.Type.CppType)
                {
                    ManagedDefaultValueExpression = parameter.DefaultValueExpression,
                    UseCallbackDelegate = parameter.UseCallbackDelegate,
                    CallbackDelegateTypeName = parameter.CallbackDelegateTypeName
                });
            }

            record.Members.Add(function);
        }
    }

    private static BindingParameterDirection ParseParameterDirection(string? direction)
    {
        return direction?.ToLowerInvariant() switch
        {
            "ref" => BindingParameterDirection.Ref,
            "out" => BindingParameterDirection.Out,
            _ => BindingParameterDirection.In
        };
    }

    private static BindingAccessModifier ParseAccessModifier(string? accessModifier)
    {
        return accessModifier?.ToLowerInvariant() switch
        {
            "private" => BindingAccessModifier.Private,
            "protected" => BindingAccessModifier.Protected,
            _ => BindingAccessModifier.Public
        };
    }

    private static string BuildImportedSharedEntryPointName(
        BindingCompilation compilation,
        BindingMetadataRecordMember member,
        RecordBinding record)
    {
        var owner = SharedWrapperNaming.GetTypeToken(record.CppQualifiedName);
        // SymbolId is the signature-stable identity emitted by the exporting
        // module. Keep a deterministic fallback for older metadata documents
        // that predate member SymbolId export.
        var symbolId = string.IsNullOrWhiteSpace(member.SymbolId)
            ? BuildImportedMemberSymbolId(record, member)
            : member.SymbolId!;
        var suffix = member.IsConstructor && symbolId.EndsWith(":marshaller-default", StringComparison.Ordinal)
            ? "ctor"
            : SharedWrapperNaming.GetMemberToken(member.IsConstructor ? "ctor" : member.NativeName, symbolId);
        return $"{compilation.EntryPointPrefix}_{owner}_{suffix}";
    }

    private static bool HasSameImportedMemberSignature(
        FunctionBinding existing,
        string managedName,
        BindingMetadataRecordMember member)
    {
        if (!string.Equals(existing.ManagedName, managedName, StringComparison.Ordinal) ||
            existing.Parameters.Count != (member.Parameters?.Count ?? 0) ||
            existing.IsStatic != member.IsStatic ||
            existing.IsConstructor != member.IsConstructor ||
            existing.IsConstMethod != member.IsConstMethod)
        {
            return false;
        }

        return existing.Parameters.Zip(member.Parameters ?? Array.Empty<BindingMetadataParameter>(), static (left, right) =>
                string.Equals(left.CppTypeName ?? left.Type.CppTypeName, right.CppTypeName ?? right.Type.CppType, StringComparison.Ordinal) &&
                string.Equals(left.Direction.ToString(), ParseParameterDirection(right.Direction).ToString(), StringComparison.Ordinal))
            .All(static match => match);
    }

    private static string BuildImportedMemberSymbolId(RecordBinding record, BindingMetadataRecordMember member)
    {
        var parameters = string.Join(",", (member.Parameters ?? Array.Empty<BindingMetadataParameter>())
            .Select(parameter => parameter.CppTypeName ?? parameter.Type.CppType));
        var kind = member.IsConstructor ? "ctor" : "method";
        return $"{kind}:{record.CppQualifiedName}:{member.NativeName}({parameters}){(member.IsConstMethod ? ":const" : string.Empty)}";
    }

    private static BindingParameterAbiKind ParseParameterAbiKind(
        string? abiKind,
        BindingParameterDirection direction,
        BindingType type)
    {
        var parsed = abiKind?.ToLowerInvariant() switch
        {
            "reference" => BindingParameterAbiKind.Reference,
            "pointer" => BindingParameterAbiKind.Pointer,
            "value" => BindingParameterAbiKind.Value,
            _ => (BindingParameterAbiKind?)null
        };
        if (parsed.HasValue)
        {
            return parsed.Value;
        }

        if (direction is BindingParameterDirection.Ref or BindingParameterDirection.Out)
        {
            return BindingParameterAbiKind.Reference;
        }

        return BindingParameterAbiKind.Value;
    }

    private static void SeedEnum(BindingCompilation compilation, IDictionary<string, EnumBinding> enumsByName, BindingMetadataDocument document, BindingMetadataType importedType)
    {
        if (enumsByName.ContainsKey(importedType.Projection.CppType) ||
            (enumsByName.TryGetValue(importedType.NativeName, out var sameShortName) &&
             string.Equals(sameShortName.CppQualifiedName, importedType.Projection.CppType, StringComparison.Ordinal)))
        {
            return;
        }

        var enumBinding = new EnumBinding(
            $"import:{document.Source.Package}:{importedType.SymbolId}",
            importedType.NativeName,
            GetQualifiedManagedTypeName(importedType.ManagedNamespace ?? document.Source.ManagedNamespace, importedType.ManagedName),
            importedType.Projection.CppType,
            cppQualifiedName: importedType.Projection.CppType,
            underlyingPrimitiveKind: BindingType.PrimitiveKindFromCppName(importedType.Projection.CppType))
        {
            ManagedNamespace = importedType.ManagedNamespace ?? document.Source.ManagedNamespace,
            IsFlags = importedType.IsFlags
        };
        enumBinding.SetMetadata(MetadataKeys.SuppressEmission, true);

        SeedEnumKeys(enumsByName, enumBinding);
        compilation.Members.Add(enumBinding);
    }

    private static void SeedAlias(
        BindingCompilation compilation,
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        BindingMetadataDocument document,
        BindingMetadataAlias alias)
    {
        if (compilation.TypeAliases.Any(existing =>
                string.Equals(existing.CppQualifiedName, alias.CppQualifiedName, StringComparison.Ordinal) ||
                (string.Equals(existing.ManagedName, alias.ManagedName, StringComparison.Ordinal) &&
                 string.Equals(existing.ManagedNamespace, alias.ManagedNamespace ?? document.Source.ManagedNamespace, StringComparison.Ordinal))))
        {
            return;
        }

        var targetType = CreateAliasTargetType(recordsByName, enumsByName, document, alias.TargetType);
        var bindingAlias = new BindingTypeAlias(alias.CppQualifiedName, alias.NativeName, alias.ManagedName, targetType)
        {
            ManagedNamespace = alias.ManagedNamespace ?? document.Source.ManagedNamespace,
            IsImported = true
        };
        compilation.TypeAliases.Add(bindingAlias);
    }

    private static void SeedRecordKeys(IDictionary<string, RecordBinding> recordsByName, RecordBinding record)
    {
        TryAdd(recordsByName, record.NativeName, record);
        TryAdd(recordsByName, record.CppQualifiedName, record);
    }

    private static void SeedEnumKeys(IDictionary<string, EnumBinding> enumsByName, EnumBinding enumBinding)
    {
        TryAdd(enumsByName, enumBinding.NativeName, enumBinding);
        TryAdd(enumsByName, enumBinding.CppQualifiedName, enumBinding);
    }

    private static void TryAdd<T>(IDictionary<string, T> dictionary, string key, T value)
    {
        if (string.IsNullOrWhiteSpace(key) || dictionary.ContainsKey(key))
        {
            return;
        }

        dictionary.Add(key, value);
    }

    private static BindingType CreateAliasTargetType(
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        BindingMetadataDocument document,
        BindingMetadataTypeReference typeReference)
    {
        var genericArguments = typeReference.GenericArguments?
            .Select(argument => CreateAliasTargetType(recordsByName, enumsByName, document, argument))
            .ToArray();

        if (typeReference.IsCallback)
        {
            return CreateCallbackTargetType(recordsByName, enumsByName, document, typeReference);
        }

        var result = typeReference.Kind switch
        {
            MetadataTypeKind.Handle => CreateAliasHandleTargetType(recordsByName, document, typeReference, genericArguments),
            MetadataTypeKind.Enum => CreateAliasEnumTargetType(enumsByName, document, typeReference, genericArguments),
            "genericparameter" => BindingType.CreateGenericParameter(
                string.IsNullOrWhiteSpace(typeReference.PublicType) ? typeReference.CppType : typeReference.PublicType),
            _ => BindingType.CreateConfigured(
                typeReference.CppType,
                GetConcreteManagedTypeHead(typeReference),
                typeReference.MarshallerType,
                typeReference.ManagedMarshaller,
                ParsePrimitiveKind(typeReference.Primitive),
                genericArguments)
        };

        if (typeReference.FixedArrayElementType is not null && typeReference.FixedArrayLength is not null)
        {
            result = result.WithFixedArray(
                CreateAliasTargetType(recordsByName, enumsByName, document, typeReference.FixedArrayElementType),
                typeReference.FixedArrayLength.Value);
        }

        if (typeReference.DynamicArrayElementType is not null)
        {
            result = result.WithDynamicArray(
                CreateAliasTargetType(recordsByName, enumsByName, document, typeReference.DynamicArrayElementType));
        }

        return result;
    }

    private static BindingType CreateCallbackTargetType(
        IDictionary<string, RecordBinding> recordsByName,
        IDictionary<string, EnumBinding> enumsByName,
        BindingMetadataDocument document,
        BindingMetadataTypeReference typeReference)
    {
        var returnType = typeReference.CallbackReturnType is null
            ? BindingType.Void
            : CreateAliasTargetType(recordsByName, enumsByName, document, typeReference.CallbackReturnType);
        var parameterTypes = typeReference.CallbackParameterTypes?
            .Select(argument => CreateAliasTargetType(recordsByName, enumsByName, document, argument))
            .ToArray() ?? Array.Empty<BindingType>();

        return BindingType.CreateCallback(
            typeReference.CppType,
            BuildManagedCallbackTypeFormat(returnType, parameterTypes),
            typeReference.ManagedMarshaller,
            returnType,
            parameterTypes);
    }

    private static string BuildManagedCallbackTypeFormat(BindingType returnType, IReadOnlyList<BindingType> parameterTypes)
    {
        if (returnType.PrimitiveKind == BindingPrimitiveKind.Void)
        {
            return parameterTypes.Count == 0
                ? "global::System.Action"
                : $"global::System.Action<{BuildGenericPlaceholders(parameterTypes.Count)}>";
        }

        return $"global::System.Func<{BuildGenericPlaceholders(parameterTypes.Count + 1)}>";
    }

    private static string BuildGenericPlaceholders(int count)
    {
        return string.Join(", ", Enumerable.Range(0, count).Select(static index => $"{{{index}}}"));
    }

    private static BindingType CreateAliasHandleTargetType(
        IDictionary<string, RecordBinding> recordsByName,
        BindingMetadataDocument document,
        BindingMetadataTypeReference typeReference,
        IReadOnlyList<BindingType>? genericArguments)
    {
        var qualifiedCppTypeName = GetQualifiedCppTypeName(typeReference.CppType);
        var unparameterizedCppTypeName = StripGenericArguments(qualifiedCppTypeName);
        RecordBinding? record = null;
        if (!string.IsNullOrWhiteSpace(qualifiedCppTypeName) && recordsByName.TryGetValue(qualifiedCppTypeName, out var byCppName))
        {
            record = byCppName;
        }
        else if (!string.IsNullOrWhiteSpace(unparameterizedCppTypeName))
        {
            record = recordsByName.Values.FirstOrDefault(candidate =>
                string.Equals(StripGenericArguments(candidate.CppQualifiedName), unparameterizedCppTypeName, StringComparison.Ordinal));
        }

        if (record is null && !string.IsNullOrWhiteSpace(typeReference.RecordId))
        {
            var importedRecordId = $"import:{document.Source.Package}:{typeReference.RecordId}";
            record = recordsByName.Values.FirstOrDefault(candidate =>
                string.Equals(candidate.SymbolId, importedRecordId, StringComparison.Ordinal));
        }

        if (record is null && !string.IsNullOrWhiteSpace(typeReference.PublicType))
        {
            // Managed names are not unique across namespaces: use the name only when it is unambiguous.
            var managedHead = GetConcreteManagedTypeHead(typeReference);
            var candidates = recordsByName.Values
                .Where(candidate => string.Equals(candidate.ManagedName, managedHead, StringComparison.Ordinal))
                .Distinct()
                .Take(2)
                .ToArray();
            if (candidates.Length == 1)
            {
                record = candidates[0];
            }
        }

        return record is not null
            ? BindingType.CreateHandle(typeReference.CppType, record.ManagedName, record.SymbolId, typeReference.MarshallerType, genericArguments)
            : BindingType.CreateHandle(typeReference.CppType, GetConcreteManagedTypeHead(typeReference), typeReference.RecordId, typeReference.MarshallerType, genericArguments);
    }

    private static BindingType CreateAliasEnumTargetType(
        IDictionary<string, EnumBinding> enumsByName,
        BindingMetadataDocument document,
        BindingMetadataTypeReference typeReference,
        IReadOnlyList<BindingType>? genericArguments)
    {
        if (!string.IsNullOrWhiteSpace(typeReference.EnumId))
        {
            var importedEnumId = $"import:{document.Source.Package}:{typeReference.EnumId}";
            var enumById = enumsByName.Values.FirstOrDefault(candidate => string.Equals(candidate.SymbolId, importedEnumId, StringComparison.Ordinal));
            if (enumById is not null)
            {
                return BindingType.CreateEnum(typeReference.CppType, enumById.ManagedName, genericArguments, enumById.SymbolId);
            }
        }

        var qualifiedCppTypeName = GetQualifiedCppTypeName(typeReference.CppType);
        if (!string.IsNullOrWhiteSpace(qualifiedCppTypeName) && enumsByName.TryGetValue(qualifiedCppTypeName, out var enumBinding))
        {
            return BindingType.CreateEnum(typeReference.CppType, enumBinding.ManagedName, genericArguments, enumBinding.SymbolId);
        }

        return BindingType.CreateEnum(typeReference.CppType, typeReference.PublicType, genericArguments, typeReference.EnumId);
    }

    private static BindingPrimitiveKind ParsePrimitiveKind(string? primitive)
    {
        return primitive switch
        {
            "void" => BindingPrimitiveKind.Void,
            "boolean" => BindingPrimitiveKind.Boolean,
            "int8" => BindingPrimitiveKind.Int8,
            "uint8" => BindingPrimitiveKind.UInt8,
            "int16" => BindingPrimitiveKind.Int16,
            "uint16" => BindingPrimitiveKind.UInt16,
            "int32" => BindingPrimitiveKind.Int32,
            "uint32" => BindingPrimitiveKind.UInt32,
            "int64" => BindingPrimitiveKind.Int64,
            "uint64" => BindingPrimitiveKind.UInt64,
            "single" => BindingPrimitiveKind.Single,
            "double" => BindingPrimitiveKind.Double,
            "nativeint" => BindingPrimitiveKind.NativeInt,
            "nativeuint" => BindingPrimitiveKind.NativeUInt,
            "intptr" => BindingPrimitiveKind.IntPtr,
            _ => BindingPrimitiveKind.Unsupported
        };
    }

    private static string BuildQualifiedManagedTypeFormat(string managedTypeName, int genericArity)
    {
        if (genericArity <= 0)
        {
            return managedTypeName;
        }

        var placeholders = string.Join(", ", Enumerable.Range(0, genericArity).Select(static index => $"{{{index}}}"));
        return $"{managedTypeName}<{placeholders}>";
    }

    private static string GetConcreteManagedTypeHead(BindingMetadataTypeReference typeReference)
    {
        if (typeReference.GenericArguments is not { Count: > 0 })
        {
            return typeReference.PublicType;
        }

        var genericStart = typeReference.PublicType.IndexOf('<');
        return genericStart >= 0 ? typeReference.PublicType[..genericStart] : typeReference.PublicType;
    }

    private static string ResolveQualifiedManagedBaseTypeName(BindingMetadataDocument document, string managedBaseTypeName)
    {
        if (managedBaseTypeName.StartsWith("global::", StringComparison.Ordinal))
        {
            return managedBaseTypeName;
        }

        var baseType = document.Types.FirstOrDefault(candidate => string.Equals(candidate.ManagedName, managedBaseTypeName, StringComparison.Ordinal));
        if (baseType is not null)
        {
            return GetQualifiedManagedTypeName(baseType.ManagedNamespace ?? document.Source.ManagedNamespace, baseType.ManagedName);
        }

        return GetQualifiedManagedTypeName(document.Source.ManagedNamespace, managedBaseTypeName);
    }

    private static string GetQualifiedManagedTypeName(string? managedNamespace, string managedName)
    {
        if (managedName.StartsWith("global::", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(managedNamespace))
        {
            return managedName.StartsWith("global::", StringComparison.Ordinal)
                ? managedName
                : managedName.Contains('.', StringComparison.Ordinal)
                    ? $"global::{managedName}"
                    : managedName;
        }

        if (managedName.Contains('.', StringComparison.Ordinal))
        {
            return $"global::{managedName}";
        }

        return $"global::{managedNamespace}.{managedName}";
    }

    private static string GetManagedDeclarationName(string managedName)
    {
        var normalized = managedName.StartsWith("global::", StringComparison.Ordinal)
            ? managedName["global::".Length..]
            : managedName;
        var genericStart = normalized.IndexOf('<');
        if (genericStart >= 0)
        {
            normalized = normalized[..genericStart];
        }

        var separator = normalized.LastIndexOf('.');
        return separator >= 0 ? normalized[(separator + 1)..] : normalized;
    }

    private static string? GetManagedNamespaceFromQualifiedName(string managedName)
    {
        var normalized = managedName.StartsWith("global::", StringComparison.Ordinal)
            ? managedName["global::".Length..]
            : managedName;
        var genericStart = normalized.IndexOf('<');
        if (genericStart >= 0)
        {
            normalized = normalized[..genericStart];
        }

        var separator = normalized.LastIndexOf('.');
        return separator > 0 ? normalized[..separator] : null;
    }

    private static string GetQualifiedCppTypeName(string cppType)
    {
        var normalized = cppType
            .Replace("const ", string.Empty, StringComparison.Ordinal)
            .Replace("&", string.Empty, StringComparison.Ordinal)
            .Replace("*", string.Empty, StringComparison.Ordinal)
            .Trim();

        return normalized;
    }

    private static string StripGenericArguments(string typeName)
    {
        var genericStart = typeName.IndexOf('<');
        return genericStart >= 0 ? typeName[..genericStart].Trim() : typeName;
    }

    private static string ComputeHash(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
