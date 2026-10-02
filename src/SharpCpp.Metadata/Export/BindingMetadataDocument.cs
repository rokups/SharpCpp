namespace SharpCpp.Metadata.Export;

public sealed record BindingMetadataDocument(
    int SchemaVersion,
    string GeneratorVersion,
    string SupportRuntimeVersion,
    BindingMetadataSource Source,
    IReadOnlyList<BindingMetadataImport> Imports,
    BindingMetadataFingerprint Fingerprint,
    IReadOnlyList<BindingMetadataType> Types,
    IReadOnlyList<BindingMetadataFunction> Functions,
    IReadOnlyList<BindingMetadataAlias>? Aliases = null,
    // The release thunks this module exports for its own owned-string free functions, so a
    // consumer can free a string freed_by a function of this module. Null when there are none.
    IReadOnlyList<BindingMetadataStringFreeExport>? StringFreeFunctions = null);

public sealed record BindingMetadataSource(string Package, string ManagedNamespace, string NativeLibrary);

public sealed record BindingMetadataImport(string Package, string Hash);

public sealed record BindingMetadataFingerprint(
    string Phase2ClosureHash,
    string ClangArgsHash,
    string AttributeVocabularyHash,
    IReadOnlyList<string> HeaderFiles,
    IReadOnlyList<string>? NativeSupportHeaders = null);

public sealed record BindingMetadataType(
    string SymbolId,
    string Kind,
    string NativeName,
    string ManagedName,
    string? ManagedNamespace,
    string? ManagedBaseTypeName,
    BindingMetadataTypeReference Projection,
    string? ManagedBaseTypeConstraints = null,
    string? ManagedInstanceCacheFactoryExpression = null,
    string? ManagedInstanceCacheTypeName = null,
    bool EmitDefaultNativeReleaseOverride = true,
    bool IsFixedArray = false,
    bool IsDynamicArray = false,
    bool IsFlags = false,
    IReadOnlyList<BindingMetadataEnumValue>? Values = null,
    IReadOnlyList<BindingMetadataRecordMember>? Members = null,
    string? ManagedWrapperProviderKind = null,
    bool? HasCopyConstructor = null,
    string? ManagedWrapperAssembly = null,
    string? ManagedWrapperProviderNamespace = null,
    bool? ManagedWrapperCopyable = null,
    // Managed generic shape ("Ea.Map<{0}, {1}>"): which native template parameters are
    // managed type arguments. The projection lists every native parameter.
    string? ManagedTypeFormat = null);

public sealed record BindingMetadataEnumValue(string NativeName, string ManagedName, string ValueExpression);

public sealed record BindingMetadataRecordMember(
    string Kind,
    string NativeName,
    string ManagedName,
    BindingMetadataTypeReference? Type,
    IReadOnlyList<BindingMetadataParameter>? Parameters,
    bool IsReadOnly,
    bool IsStatic,
    bool IsConstructor,
    string? OperatorToken = null,
    bool IsConversionOperator = false,
    bool IsExplicitConversionOperator = false,
    bool ReturnsOwnedHandle = false,
    bool GetterReturnsOwnedHandle = false,
    string? EntryPoint = null,
    string? CppReturnTypeName = null,
    string? CppQualifiedName = null,
    string? OwningRecordCppTypeName = null,
    string? MarshallerExposedMethod = null,
    bool IsConstMethod = false,
    string? AccessModifier = null,
    bool IsVirtual = false,
    bool IsPureVirtual = false,
    bool IsFinal = false,
    bool IsOverride = false,
    string? SymbolId = null,
    int ReturnPointerDepth = 0);

public sealed record BindingMetadataFunction(
    string SymbolId,
    string NativeName,
    string ManagedName,
    string EntryPoint,
    BindingMetadataTypeReference ReturnType,
    IReadOnlyList<BindingMetadataParameter> Parameters,
    bool ReturnsOwnedHandle = false,
    int ReturnPointerDepth = 0,
    // Set when the char* return is a caller-owned string: ReturnType is then the public managed
    // string and this names the function that frees the native text.
    BindingMetadataStringFreeFunction? ReturnStringFreeFunction = null);

/// <summary>The free function of an owned string (an owned return or an owned output parameter).</summary>
public sealed record BindingMetadataStringFreeFunction(string NativeName, string CppQualifiedName);

/// <summary>A release thunk exported by a module: the free function and the native entry point that calls it.</summary>
public sealed record BindingMetadataStringFreeExport(
    string NativeName,
    string CppQualifiedName,
    string ParameterCppTypeName,
    string EntryPoint);

public sealed record BindingMetadataAlias(
    string CppQualifiedName,
    string NativeName,
    string ManagedName,
    string? ManagedNamespace,
    BindingMetadataTypeReference TargetType);

public sealed record BindingMetadataParameter(
    string Name,
    string Direction,
    BindingMetadataTypeReference Type,
    string? DefaultValueExpression = null,
    bool UseCallbackDelegate = false,
    string? CallbackDelegateTypeName = null,
    string? CppTypeName = null,
    string? AbiKind = null,
    BindingMetadataStringFreeFunction? StringFreeFunction = null);

public sealed record BindingMetadataTypeReference(
    string Kind,
    string CppType,
    string PublicType,
    string? Primitive,
    string? RecordId,
    string? MarshallerType,
    IReadOnlyList<BindingMetadataTypeReference>? GenericArguments = null,
    string? EnumId = null,
    string? ManagedMarshaller = null,
    bool IsCallback = false,
    BindingMetadataTypeReference? CallbackReturnType = null,
    IReadOnlyList<BindingMetadataTypeReference>? CallbackParameterTypes = null,
    BindingMetadataTypeReference? FixedArrayElementType = null,
    int? FixedArrayLength = null,
    BindingMetadataTypeReference? DynamicArrayElementType = null);

public sealed record BindingMetadataExportOptions(
    string PackageId,
    string GeneratorVersion = BindingMetadataSchema.PreviewGeneratorVersion,
    string SupportRuntimeVersion = BindingMetadataSchema.PreviewSupportRuntimeVersion,
    string Phase2ClosureHash = "phase2-bootstrap",
    string ClangArgsHash = "clang-bootstrap",
    string AttributeVocabularyHash = BindingMetadataSchema.AttributeVocabularyVersion,
    IReadOnlyList<BindingMetadataImport>? Imports = null);
