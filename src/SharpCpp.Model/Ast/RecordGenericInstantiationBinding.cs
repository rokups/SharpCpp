namespace SharpCpp.Model.Ast;

public sealed class RecordGenericInstantiationBinding
{
    private static int s_instantiationCounter;
    private string? _suffix;

    public RecordGenericInstantiationBinding(IEnumerable<BindingType> typeArguments)
    {
        ArgumentNullException.ThrowIfNull(typeArguments);

        TypeArguments = typeArguments.ToArray();
    }

    public IReadOnlyList<BindingType> TypeArguments { get; }

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
