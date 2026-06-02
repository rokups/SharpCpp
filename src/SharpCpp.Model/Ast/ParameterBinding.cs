namespace SharpCpp.Model.Ast;

public enum BindingParameterDirection
{
    In = 0,
    Ref = 1,
    Out = 2
}

public sealed class ParameterBinding : BindingNamedNode
{
    public ParameterBinding(
        string symbolId,
        string nativeName,
        string managedName,
        BindingType type,
        BindingParameterDirection direction = BindingParameterDirection.In)
        : base(symbolId, nativeName, managedName)
    {
        ArgumentNullException.ThrowIfNull(type);

        Type = type;
        Direction = direction;
    }

    public override BindingNodeKind Kind => BindingNodeKind.Parameter;

    public override IEnumerable<BindingNode> Children => Array.Empty<BindingNode>();

    public BindingType Type { get; set; }

    public BindingParameterDirection Direction { get; set; }
}
