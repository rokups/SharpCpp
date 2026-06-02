namespace SharpCpp.Model.Ast;

public sealed class RecordBinding : BindingNamedContainerNode
{
    public RecordBinding(
        string symbolId,
        string nativeName,
        string managedName,
        bool isHandleType = true,
        string? marshallerTypeName = null)
        : base(symbolId, nativeName, managedName)
    {
        IsHandleType = isHandleType;
        HandleType = BindingType.CreateHandle($"{nativeName}*", managedName, symbolId, marshallerTypeName: marshallerTypeName);
    }

    public override BindingNodeKind Kind => BindingNodeKind.Record;

    public bool IsHandleType { get; set; }

    public string? ManagedBaseTypeName { get; set; }

    public BindingType HandleType { get; }
}
