namespace SharpCpp.Model.Ast;

public sealed class RecordBinding : BindingNamedContainerNode
{
    public RecordBinding(
        string symbolId,
        string nativeName,
        string managedName,
        string? cppQualifiedName = null,
        bool isHandleType = true,
        string? marshallerTypeName = null)
        : base(symbolId, nativeName, managedName)
    {
        CppQualifiedName = string.IsNullOrWhiteSpace(cppQualifiedName) ? nativeName : cppQualifiedName;
        IsHandleType = isHandleType;
        HandleType = BindingType.CreateHandle($"{CppQualifiedName}*", managedName, symbolId, marshallerTypeName: marshallerTypeName);
    }

    public override BindingNodeKind Kind => BindingNodeKind.Record;

    public bool IsHandleType { get; set; }

    public string? ManagedBaseTypeName { get; set; }

    public string CppQualifiedName { get; set; }

    public BindingType HandleType { get; }
}
