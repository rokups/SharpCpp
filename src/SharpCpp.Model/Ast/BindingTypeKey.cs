namespace SharpCpp.Model.Ast;

public sealed class BindingTypeKey : IEquatable<BindingTypeKey>
{
    private readonly BindingTypeKey[] _genericArguments;

    private BindingTypeKey(
        BindingTypeKind kind,
        BindingPrimitiveKind primitiveKind,
        string? recordId,
        string? enumId,
        string? genericParameterName,
        string cppTypeName,
        string publicTypeName,
        string? marshallerTypeName,
        string? managedMarshaller,
        IEnumerable<BindingTypeKey> genericArguments)
    {
        Kind = kind;
        PrimitiveKind = primitiveKind;
        RecordId = recordId;
        EnumId = enumId;
        GenericParameterName = genericParameterName;
        CppTypeName = cppTypeName;
        PublicTypeName = publicTypeName;
        MarshallerTypeName = marshallerTypeName;
        ManagedMarshaller = managedMarshaller;
        _genericArguments = genericArguments.ToArray();
    }

    public BindingTypeKind Kind { get; }

    public BindingPrimitiveKind PrimitiveKind { get; }

    public string? RecordId { get; }

    public string? EnumId { get; }

    public string? GenericParameterName { get; }

    public string CppTypeName { get; }

    public string PublicTypeName { get; }

    public string? MarshallerTypeName { get; }

    public string? ManagedMarshaller { get; }

    public IReadOnlyList<BindingTypeKey> GenericArguments => _genericArguments;

    public static BindingTypeKey From(BindingType type)
    {
        return new BindingTypeKey(
            type.Kind,
            type.PrimitiveKind,
            type.RecordId,
            type.EnumId,
            type.GenericParameterName,
            type.CppTypeName,
            type.Marshaller.PublicTypeName,
            type.MarshallerTypeName,
            type.ManagedMarshaller,
            type.GenericArguments.Select(From));
    }

    public bool Equals(BindingTypeKey? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return Kind == other.Kind &&
            PrimitiveKind == other.PrimitiveKind &&
            string.Equals(RecordId, other.RecordId, StringComparison.Ordinal) &&
            string.Equals(EnumId, other.EnumId, StringComparison.Ordinal) &&
            string.Equals(GenericParameterName, other.GenericParameterName, StringComparison.Ordinal) &&
            string.Equals(PublicTypeName, other.PublicTypeName, StringComparison.Ordinal) &&
            string.Equals(MarshallerTypeName, other.MarshallerTypeName, StringComparison.Ordinal) &&
            string.Equals(ManagedMarshaller, other.ManagedMarshaller, StringComparison.Ordinal) &&
            _genericArguments.SequenceEqual(other._genericArguments);
    }

    public override bool Equals(object? obj) => obj is BindingTypeKey other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(PrimitiveKind);
        hash.Add(RecordId, StringComparer.Ordinal);
        hash.Add(EnumId, StringComparer.Ordinal);
        hash.Add(GenericParameterName, StringComparer.Ordinal);
        hash.Add(PublicTypeName, StringComparer.Ordinal);
        hash.Add(MarshallerTypeName, StringComparer.Ordinal);
        hash.Add(ManagedMarshaller, StringComparer.Ordinal);
        foreach (var argument in _genericArguments)
        {
            hash.Add(argument);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(BindingTypeKey? left, BindingTypeKey? right) => Equals(left, right);

    public static bool operator !=(BindingTypeKey? left, BindingTypeKey? right) => !Equals(left, right);
}
