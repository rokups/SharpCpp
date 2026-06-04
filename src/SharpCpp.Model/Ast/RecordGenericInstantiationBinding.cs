namespace SharpCpp.Model.Ast;

public sealed class RecordGenericInstantiationBinding
{
    public RecordGenericInstantiationBinding(IEnumerable<BindingType> typeArguments)
    {
        ArgumentNullException.ThrowIfNull(typeArguments);

        TypeArguments = typeArguments.ToArray();
    }

    public IReadOnlyList<BindingType> TypeArguments { get; }
}
