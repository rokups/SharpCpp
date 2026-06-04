namespace SharpCpp.Model.Ast;

public static class MetadataKeys
{
    public const string CanonicalCppIdentity = "canonical_cpp_identity";
    public const string ManagedTypeIdentity = "managed_type_identity";
    public const string Marshaller = "marshaller";
    public const string AccessorProperty = "accessor_property";
    public const string IgnoreReason = "ignore_reason";
    public const string DocumentationComment = "documentation_comment";
    public const string ExplicitGenericInstantiations = "explicit_generic_instantiations";
}

public enum AccessorPropertyKind
{
    Getter = 0,
    Setter = 1
}

public sealed record AccessorPropertyAnnotation(string PropertyName, AccessorPropertyKind Kind);
