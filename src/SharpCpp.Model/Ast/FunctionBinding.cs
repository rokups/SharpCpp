namespace SharpCpp.Model.Ast;

public sealed class FunctionBinding : BindingNamedNode
{
    private readonly BindingNodeCollection<ParameterBinding> _parameters;

    public FunctionBinding(
        string symbolId,
        string nativeName,
        string managedName,
        BindingType returnType,
        string? entryPointName = null)
        : base(symbolId, nativeName, managedName)
    {
        ArgumentNullException.ThrowIfNull(returnType);

        ReturnType = returnType;
        EntryPointName = string.IsNullOrWhiteSpace(entryPointName) ? nativeName : entryPointName;
        _parameters = new BindingNodeCollection<ParameterBinding>(this);
    }

    public override BindingNodeKind Kind => BindingNodeKind.Function;

    public override IEnumerable<BindingNode> Children => _parameters;

    public BindingType ReturnType { get; set; }

    public string EntryPointName { get; set; }

    public bool IsStatic { get; set; } = true;

    public IList<ParameterBinding> Parameters => _parameters;
}
