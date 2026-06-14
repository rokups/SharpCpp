namespace SharpCpp.Model.Ast;

public enum BindingTypeKind
{
    Unsupported = 0,
    Primitive = 1,
    Handle = 2,
    Enum = 3,
    GenericParameter = 4
}

public enum BindingPrimitiveKind
{
    Unsupported = 0,
    Void = 1,
    Boolean = 2,
    Int8 = 3,
    UInt8 = 4,
    Int16 = 5,
    UInt16 = 6,
    Int32 = 7,
    UInt32 = 8,
    Int64 = 9,
    UInt64 = 10,
    Single = 11,
    Double = 12,
    NativeInt = 13,
    NativeUInt = 14,
    IntPtr = 15
}

public sealed record MarshallerDescriptor(
    string CppTypeName,
    string PublicTypeName,
    string? MarshallerTypeName = null,
    string? ManagedMarshaller = null)
{
    public bool CppIsRecordValue { get; init; }
}

public sealed class BindingType
{
    private readonly IReadOnlyList<BindingType> _genericArguments;
    private string? _managedTypeFormatOverride;

    private BindingType(
        BindingTypeKind kind,
        BindingPrimitiveKind primitiveKind,
        MarshallerDescriptor marshaller,
        string? recordId = null,
        string? genericParameterName = null,
        IEnumerable<BindingType>? genericArguments = null)
    {
        Kind = kind;
        PrimitiveKind = primitiveKind;
        Marshaller = marshaller;
        RecordId = recordId;
        GenericParameterName = genericParameterName;
        _genericArguments = genericArguments?.ToArray() ?? Array.Empty<BindingType>();
    }

    public static BindingType Void { get; } = Primitive(BindingPrimitiveKind.Void, "void", "void");

    public static BindingType Boolean { get; } = Primitive(BindingPrimitiveKind.Boolean, "bool", "bool");

    public static BindingType Int8 { get; } = Primitive(BindingPrimitiveKind.Int8, "signed char", "sbyte");

    public static BindingType UInt8 { get; } = Primitive(BindingPrimitiveKind.UInt8, "unsigned char", "byte");

    public static BindingType Int16 { get; } = Primitive(BindingPrimitiveKind.Int16, "short", "short");

    public static BindingType UInt16 { get; } = Primitive(BindingPrimitiveKind.UInt16, "unsigned short", "ushort");

    public static BindingType Int32 { get; } = Primitive(BindingPrimitiveKind.Int32, "int", "int");

    public static BindingType UInt32 { get; } = Primitive(BindingPrimitiveKind.UInt32, "unsigned int", "uint");

    public static BindingType Int64 { get; } = Primitive(BindingPrimitiveKind.Int64, "long long", "long");

    public static BindingType UInt64 { get; } = Primitive(BindingPrimitiveKind.UInt64, "unsigned long long", "ulong");

    public static BindingType Single { get; } = Primitive(BindingPrimitiveKind.Single, "float", "float");

    public static BindingType Double { get; } = Primitive(BindingPrimitiveKind.Double, "double", "double");

    public static BindingType NativeInt { get; } = Primitive(BindingPrimitiveKind.NativeInt, "long", "nint");

    public static BindingType NativeUInt { get; } = Primitive(BindingPrimitiveKind.NativeUInt, "unsigned long", "nuint");

    public static BindingType IntPtr { get; } = Primitive(BindingPrimitiveKind.IntPtr, "void*", "global::System.IntPtr");

    public BindingTypeKind Kind { get; }

    public BindingPrimitiveKind PrimitiveKind { get; }

    public MarshallerDescriptor Marshaller { get; }

    public string? RecordId { get; }

    public string? GenericParameterName { get; }

    public IReadOnlyList<BindingType> GenericArguments => _genericArguments;

    public string CppTypeName => Marshaller.CppTypeName;

    public string ManagedTypeName => Kind == BindingTypeKind.GenericParameter
        ? GenericParameterName ?? Marshaller.PublicTypeName
        : FormatManagedTypeName();

    public string UnaliasedManagedTypeName => Kind == BindingTypeKind.GenericParameter
        ? GenericParameterName ?? Marshaller.PublicTypeName
        : FormatManagedTypeName(managedTypeFormatOverride: null);

    public string? MarshallerTypeName => Marshaller.MarshallerTypeName;

    public string? ManagedMarshaller => Marshaller.ManagedMarshaller;

    public bool IsGenericParameter => Kind == BindingTypeKind.GenericParameter;

    public bool ContainsGenericParameters => IsGenericParameter || GenericArguments.Any(static argument => argument.ContainsGenericParameters);

    public bool IsPointerType => Kind == BindingTypeKind.Handle && string.IsNullOrEmpty(Marshaller.MarshallerTypeName);

    public void SetManagedTypeFormatOverride(string? managedTypeFormatOverride)
    {
        _managedTypeFormatOverride = string.IsNullOrWhiteSpace(managedTypeFormatOverride) ? null : managedTypeFormatOverride;
    }

    public BindingType WithManagedTypeFormatOverride(string? managedTypeFormatOverride)
    {
        var normalizedOverride = string.IsNullOrWhiteSpace(managedTypeFormatOverride)
            ? null
            : managedTypeFormatOverride;
        if (string.Equals(_managedTypeFormatOverride, normalizedOverride, StringComparison.Ordinal))
        {
            return this;
        }

        var copy = new BindingType(
            Kind,
            PrimitiveKind,
            Marshaller,
            RecordId,
            GenericParameterName,
            GenericArguments)
        {
            _managedTypeFormatOverride = normalizedOverride
        };
        return copy;
    }

    public static BindingType CreateHandle(
        string cppTypeName,
        string publicTypeName,
        string? recordId = null,
        string? marshallerTypeName = null,
        IEnumerable<BindingType>? genericArguments = null,
        bool cppIsRecordValue = false)
    {
        return new BindingType(
            BindingTypeKind.Handle,
            BindingPrimitiveKind.Unsupported,
            new MarshallerDescriptor(cppTypeName, publicTypeName, marshallerTypeName)
            {
                CppIsRecordValue = cppIsRecordValue
            },
            recordId,
            genericArguments: genericArguments);
    }

    public static BindingType CreateEnum(string cppTypeName, string publicTypeName, IEnumerable<BindingType>? genericArguments = null)
    {
        return new BindingType(
            BindingTypeKind.Enum,
            BindingPrimitiveKind.Unsupported,
            new MarshallerDescriptor(cppTypeName, publicTypeName),
            genericArguments: genericArguments);
    }

    public static BindingType CreateConfigured(string cppTypeName, string publicTypeName)
    {
        return CreateConfigured(cppTypeName, publicTypeName, null);
    }

    public static BindingType CreateConfigured(string cppTypeName, string publicTypeName, string? marshallerTypeName)
    {
        return CreateConfigured(cppTypeName, publicTypeName, marshallerTypeName, null);
    }

    public static BindingType CreateConfigured(string cppTypeName, string publicTypeName, string? marshallerTypeName, string? managedMarshaller)
    {
        var primitiveKind = string.Equals(publicTypeName, "global::System.IntPtr", StringComparison.Ordinal)
            ? BindingPrimitiveKind.IntPtr
            : BindingPrimitiveKind.Unsupported;

        return new BindingType(
            BindingTypeKind.Primitive,
            primitiveKind,
            new MarshallerDescriptor(cppTypeName, publicTypeName, marshallerTypeName, managedMarshaller));
    }

    public override string ToString()
    {
        return ManagedTypeName;
    }

    private static BindingType Primitive(
        BindingPrimitiveKind primitiveKind,
        string cppTypeName,
        string publicTypeName)
    {
        return new BindingType(
            BindingTypeKind.Primitive,
            primitiveKind,
            new MarshallerDescriptor(cppTypeName, publicTypeName));
    }

    public static BindingType CreateGenericParameter(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new BindingType(
            BindingTypeKind.GenericParameter,
            BindingPrimitiveKind.Unsupported,
            new MarshallerDescriptor(name, name),
            genericParameterName: name);
    }

    public BindingType WithCppIsRecordValue(bool value)
    {
        if (Marshaller.CppIsRecordValue == value)
            return this;
        return new BindingType(
            Kind,
            PrimitiveKind,
            Marshaller with { CppIsRecordValue = value },
            RecordId,
            GenericParameterName,
            GenericArguments);
    }

    private string FormatManagedTypeName(string? managedTypeFormatOverride = null)
    {
        // When an explicit managed type format override is set (via
        // Marshaller<T> attributes) and contains no format placeholders,
        // use it as-is — C++ template arguments don't apply to the
        // configured managed type.
        var effectiveFormatOverride = managedTypeFormatOverride ?? _managedTypeFormatOverride;
        if (effectiveFormatOverride is not null &&
            !effectiveFormatOverride.Contains('{', StringComparison.Ordinal))
        {
            return effectiveFormatOverride;
        }

        var managedTypeFormat = effectiveFormatOverride ?? Marshaller.PublicTypeName;
        if (GenericArguments.Count == 0)
        {
            return managedTypeFormat;
        }

        var argumentNames = GenericArguments.Select(static argument => argument.ManagedTypeName).Cast<object>().ToArray();
        return managedTypeFormat.Contains('{', StringComparison.Ordinal)
            ? string.Format(System.Globalization.CultureInfo.InvariantCulture, managedTypeFormat, argumentNames)
            : $"{managedTypeFormat}<{string.Join(", ", GenericArguments.Select(static argument => argument.ManagedTypeName))}>";
    }
}
