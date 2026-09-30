namespace SharpCpp.Model.Ast;

public sealed class RecordGenericInstantiationBinding
{
    private static int s_instantiationCounter;
    private string? _suffix;
    private readonly BindingType[] _typeArguments;
    private readonly BindingTemplateArgument[] _nativeTemplateArguments;

    public RecordGenericInstantiationBinding(
        IEnumerable<BindingType> typeArguments,
        IEnumerable<BindingTemplateArgument>? nativeTemplateArguments = null,
        bool isInferred = false)
    {
        ArgumentNullException.ThrowIfNull(typeArguments);

        _typeArguments = typeArguments.ToArray();
        _nativeTemplateArguments = nativeTemplateArguments?.ToArray() ??
            _typeArguments.Select(static argument => BindingTemplateArgument.FromType(argument)).ToArray();
        IsInferred = isInferred;
    }

    public IReadOnlyList<BindingType> TypeArguments => _typeArguments;

    public IReadOnlyList<BindingTemplateArgument> NativeTemplateArguments => _nativeTemplateArguments;

    /// <summary>
    /// True when this specialization was discovered from a surface use rather than
    /// requested explicitly by parser metadata or target configuration.
    /// </summary>
    public bool IsInferred { get; }

    public void UpdateTypeArgument(int index, BindingType updatedType)
    {
        _typeArguments[index] = updatedType;
    }

    public string Suffix
    {
        get
        {
            if (_suffix is null)
            {
                _suffix = TypeArguments.Count == 0
                    ? string.Empty
                    : $"__{++s_instantiationCounter}";
            }

            return _suffix;
        }
    }
}
