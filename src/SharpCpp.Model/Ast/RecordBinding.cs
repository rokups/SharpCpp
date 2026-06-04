namespace SharpCpp.Model.Ast;

public sealed class RecordBinding : BindingNamedContainerNode
{
    private readonly List<RecordBaseBinding> _bases = new();

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

    public bool IsDirector { get; set; }

    public bool IsAbstract { get; set; }

    public bool IsPolymorphic { get; set; }

    public IList<RecordBaseBinding> Bases => _bases;

    public string CppQualifiedName { get; set; }

    public BindingType HandleType { get; }
}
