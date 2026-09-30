namespace SharpCpp.Model.Ast;

public enum BindingParameterDirection
{
    In = 0,
    Ref = 1,
    Out = 2
}

public enum BindingParameterAbiKind
{
    Value = 0,
    Reference = 1,
    Pointer = 2
}

/// <summary>The element of a pointer-to-pointer parameter, when it can be bound as an array or a single value.</summary>
public enum PointerPointerKind
{
    None = 0,
    Chars = 1,
    Handles = 2
}

public sealed class ParameterBinding : BindingNamedNode
{
    public ParameterBinding(
        string symbolId,
        string nativeName,
        string managedName,
        BindingType type,
        BindingParameterDirection direction = BindingParameterDirection.In,
        BindingParameterAbiKind abiKind = BindingParameterAbiKind.Value,
        string? cppTypeName = null)
        : base(symbolId, nativeName, managedName)
    {
        ArgumentNullException.ThrowIfNull(type);

        Type = type;
        Direction = direction;
        AbiKind = abiKind;
        CppTypeName = string.IsNullOrWhiteSpace(cppTypeName) ? type.CppTypeName : cppTypeName;
    }

    public override BindingNodeKind Kind => BindingNodeKind.Parameter;

    public override IEnumerable<BindingNode> Children => Array.Empty<BindingNode>();

    public BindingType Type { get; set; }

    public BindingParameterDirection Direction { get; set; }

    public BindingParameterAbiKind AbiKind { get; set; }

    public string CppTypeName { get; set; }

    public string? ManagedDefaultValueExpression { get; set; }

    public bool UseCallbackDelegate { get; set; }

    public string? CallbackDelegateTypeName { get; set; }

    public bool CallbackTakesOwnership { get; set; }

    /// <summary>
    /// Set for `char**` and `T**` (T a record) parameters whose binding shape is ambiguous
    /// (see <see cref="MetadataKeys.PointerPointerShape"/>); None for every other parameter.
    /// </summary>
    public PointerPointerKind PointerPointer { get; set; }

    /// <summary>True when the pointed-to pointer is const (`T* const*`); such a pointer cannot be written.</summary>
    public bool PointeeIsConst { get; set; }
}
