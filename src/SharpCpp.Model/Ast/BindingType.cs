namespace SharpCpp.Model.Ast;

public enum BindingTypeKind
{
    Unsupported = 0,
    Primitive = 1,
    Handle = 2,
    Enum = 3
}

public enum BindingPrimitiveKind
{
    Unsupported = 0,
    Void = 1,
    Boolean = 2,
    Int32 = 3,
    UInt32 = 4,
    Int64 = 5,
    UInt64 = 6,
    Single = 7,
    Double = 8,
    NativeInt = 9,
    NativeUInt = 10
}

public sealed record MarshallerDescriptor(
    string CppTypeName,
    string AbiTypeName,
    string PublicTypeName,
    string? MarshallerTypeName = null);

public sealed class BindingType
{
    private BindingType(
        BindingTypeKind kind,
        BindingPrimitiveKind primitiveKind,
        MarshallerDescriptor marshaller,
        string? recordId = null)
    {
        Kind = kind;
        PrimitiveKind = primitiveKind;
        Marshaller = marshaller;
        RecordId = recordId;
    }

    public static BindingType Void { get; } = Primitive(BindingPrimitiveKind.Void, "void", "void", "void");

    public static BindingType Boolean { get; } = Primitive(BindingPrimitiveKind.Boolean, "bool", "bool", "bool");

    public static BindingType Int32 { get; } = Primitive(BindingPrimitiveKind.Int32, "int", "int", "int");

    public static BindingType UInt32 { get; } = Primitive(BindingPrimitiveKind.UInt32, "unsigned int", "uint", "unsigned int");

    public static BindingType Int64 { get; } = Primitive(BindingPrimitiveKind.Int64, "long long", "long", "long long");

    public static BindingType UInt64 { get; } = Primitive(BindingPrimitiveKind.UInt64, "unsigned long long", "ulong", "unsigned long long");

    public static BindingType Single { get; } = Primitive(BindingPrimitiveKind.Single, "float", "float", "float");

    public static BindingType Double { get; } = Primitive(BindingPrimitiveKind.Double, "double", "double", "double");

    public static BindingType NativeInt { get; } = Primitive(BindingPrimitiveKind.NativeInt, "intptr_t", "nint", "intptr_t");

    public static BindingType NativeUInt { get; } = Primitive(BindingPrimitiveKind.NativeUInt, "uintptr_t", "nuint", "uintptr_t");

    public BindingTypeKind Kind { get; }

    public BindingPrimitiveKind PrimitiveKind { get; }

    public MarshallerDescriptor Marshaller { get; }

    public string? RecordId { get; }

    public string CppTypeName => Marshaller.CppTypeName;

    public string AbiTypeName => Marshaller.AbiTypeName;

    public string ManagedTypeName => Marshaller.PublicTypeName;

    public string? MarshallerTypeName => Marshaller.MarshallerTypeName;

    public bool HasCustomMarshaller => !string.IsNullOrWhiteSpace(MarshallerTypeName);

    public static BindingType CreateHandle(
        string cppTypeName,
        string publicTypeName,
        string? recordId = null,
        string abiTypeName = "void*",
        string? marshallerTypeName = null)
    {
        return new BindingType(
            BindingTypeKind.Handle,
            BindingPrimitiveKind.Unsupported,
            new MarshallerDescriptor(cppTypeName, abiTypeName, publicTypeName, marshallerTypeName),
            recordId);
    }

    public static BindingType CreateEnum(string cppTypeName, string publicTypeName, string abiTypeName = "int")
    {
        return new BindingType(
            BindingTypeKind.Enum,
            BindingPrimitiveKind.Unsupported,
            new MarshallerDescriptor(cppTypeName, abiTypeName, publicTypeName));
    }

    public override string ToString()
    {
        return ManagedTypeName;
    }

    private static BindingType Primitive(
        BindingPrimitiveKind primitiveKind,
        string cppTypeName,
        string publicTypeName,
        string abiTypeName)
    {
        return new BindingType(
            BindingTypeKind.Primitive,
            primitiveKind,
            new MarshallerDescriptor(cppTypeName, abiTypeName, publicTypeName));
    }
}
