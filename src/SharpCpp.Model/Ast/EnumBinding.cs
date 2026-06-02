namespace SharpCpp.Model.Ast;

public sealed class EnumBinding : BindingNamedNode
{
    public EnumBinding(string symbolId, string nativeName, string managedName, string underlyingTypeName = "int")
        : base(symbolId, nativeName, managedName)
    {
        UnderlyingTypeName = underlyingTypeName;
    }

    public override BindingNodeKind Kind => BindingNodeKind.Enum;

    public override IEnumerable<BindingNode> Children => Array.Empty<BindingNode>();

    public string UnderlyingTypeName { get; set; }
}
