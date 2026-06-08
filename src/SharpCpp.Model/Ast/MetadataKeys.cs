namespace SharpCpp.Model.Ast;

public static class MetadataKeys
{
    public const string CanonicalCppIdentity = "canonical_cpp_identity";
    public const string ManagedTypeIdentity = "managed_type_identity";
    public const string Marshaller = "marshaller";
    public const string AccessorProperty = "accessor_property";
    public const string IgnoreReason = "ignore_reason";
    public const string Warnings = "warnings";
    public const string DocumentationComment = "documentation_comment";
    public const string ExplicitGenericInstantiations = "explicit_generic_instantiations";
    public const string ForwardDeclaration = "forward_declaration";
    public const string PlaceholderRecord = "placeholder_record";
    public const string SuppressEmission = "suppress_emission";
    public const string HasNativeDefinition = "has_native_definition";
    public const string NonPublicDestructor = "non_public_destructor";
    public const string UnexpressibleDefault = "unexpressible_default";
    public const string MarshallerExposedMethod = "marshaller_exposed_method";
}

public enum AccessorPropertyKind
{
    Getter = 0,
    Setter = 1
}

public sealed record AccessorPropertyAnnotation(string PropertyName, AccessorPropertyKind Kind);
