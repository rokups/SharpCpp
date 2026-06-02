namespace SharpCpp.Metadata.Export;

public sealed record BindingMetadataDocument(
    int SchemaVersion,
    string GeneratorVersion,
    string SupportRuntimeVersion,
    BindingMetadataSource Source,
    IReadOnlyList<BindingMetadataImport> Imports,
    BindingMetadataFingerprint Fingerprint,
    IReadOnlyList<BindingMetadataType> Types,
    IReadOnlyList<BindingMetadataFunction> Functions);

public sealed record BindingMetadataSource(string Package, string ManagedNamespace, string NativeLibrary);

public sealed record BindingMetadataImport(string Package, string Hash);

public sealed record BindingMetadataFingerprint(
    string Phase2ClosureHash,
    string ClangArgsHash,
    string AttributeVocabularyHash,
    IReadOnlyList<string> HeaderFiles);

public sealed record BindingMetadataType(
    string SymbolId,
    string Kind,
    string NativeName,
    string ManagedName,
    string? ManagedBaseTypeName,
    BindingMetadataTypeReference Projection);

public sealed record BindingMetadataFunction(
    string SymbolId,
    string NativeName,
    string ManagedName,
    string EntryPoint,
    BindingMetadataTypeReference ReturnType,
    IReadOnlyList<BindingMetadataParameter> Parameters);

public sealed record BindingMetadataParameter(string Name, string Direction, BindingMetadataTypeReference Type);

public sealed record BindingMetadataTypeReference(
    string Kind,
    string CppType,
    string PublicType,
    string AbiType,
    string? Primitive,
    string? RecordId,
    string? MarshallerType);

public sealed record BindingMetadataExportOptions(
    string PackageId,
    string GeneratorVersion = BindingMetadataSchema.PreviewGeneratorVersion,
    string SupportRuntimeVersion = BindingMetadataSchema.PreviewSupportRuntimeVersion,
    string Phase2ClosureHash = "phase2-bootstrap",
    string ClangArgsHash = "clang-bootstrap",
    string AttributeVocabularyHash = BindingMetadataSchema.AttributeVocabularyVersion,
    IReadOnlyList<BindingMetadataImport>? Imports = null);
