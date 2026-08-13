namespace SharpCpp.Model.Ast;

public sealed class FieldBinding : BindingNamedNode
{
    public FieldBinding(
        string symbolId,
        string nativeName,
        string managedName,
        BindingType type,
        string cppTypeName,
        string owningRecordCppTypeName,
        bool isReadOnly = false,
        bool isReference = false,
        bool isPointer = false)
        : base(symbolId, nativeName, managedName)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(cppTypeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(owningRecordCppTypeName);

        Type = type;
        CppTypeName = cppTypeName;
        OwningRecordCppTypeName = owningRecordCppTypeName;
        IsReadOnly = isReadOnly;
        IsReference = isReference;
        IsPointer = isPointer;
    }

    public override BindingNodeKind Kind => BindingNodeKind.Field;

    public override IEnumerable<BindingNode> Children => Array.Empty<BindingNode>();

    public BindingType Type { get; set; }

    public string CppTypeName { get; set; }

    public string OwningRecordCppTypeName { get; set; }

    public bool IsReadOnly { get; set; }

    public bool IsReference { get; set; }

    public bool IsPointer { get; set; }

    public bool IsStatic { get; set; }
    public BindingAccessModifier AccessModifier { get; set; } = BindingAccessModifier.Public;

    public bool ReturnsOwnedHandle { get; set; }

    /// <summary>
    /// Structured signature for a field whose native type is a function pointer.
    /// The signature is detached from the binding tree and is available to target
    /// generators that project C-style vtables into callable functions.
    /// </summary>
    public FunctionBinding? FunctionPointerSignature { get; set; }
}
