namespace SharpCpp.Model.Ast;

public sealed class EnumBinding : BindingNamedNode
{
    private readonly List<EnumValueBinding> _values = new();

    public EnumBinding(
        string symbolId,
        string nativeName,
        string managedName,
        string underlyingTypeName = "int",
        string? cppQualifiedName = null,
        BindingPrimitiveKind underlyingPrimitiveKind = BindingPrimitiveKind.Int32)
        : base(symbolId, nativeName, managedName)
    {
        UnderlyingTypeName = underlyingTypeName;
        UnderlyingPrimitiveKind = underlyingPrimitiveKind;
        CppQualifiedName = string.IsNullOrWhiteSpace(cppQualifiedName) ? nativeName : cppQualifiedName;
    }

    public override BindingNodeKind Kind => BindingNodeKind.Enum;

    public override IEnumerable<BindingNode> Children => Array.Empty<BindingNode>();

    public string UnderlyingTypeName { get; set; }

    public BindingPrimitiveKind UnderlyingPrimitiveKind { get; set; }

    public bool IsFlags { get; set; }

    public string CppQualifiedName { get; set; }

    /// <summary>
    /// The C++ name that sets the namespace scope of the enum. An external enum that an owned
    /// alias names has the scope of the alias. All other enums have the scope of their own name.
    /// </summary>
    public string ScopeCppQualifiedName
    {
        get
        {
            var externalTypeAlias = GetMetadata<string>(MetadataKeys.ExternalTypeAlias);
            return string.IsNullOrWhiteSpace(externalTypeAlias) ? CppQualifiedName : externalTypeAlias;
        }
    }

    public string? ManagedNamespace { get; set; }

    public IList<EnumValueBinding> Values => _values;
}
