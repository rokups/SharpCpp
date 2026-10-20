namespace SharpCpp.Model.Ast;

public enum BindingTemplateArgumentKind
{
    Type,
    Integral
}

public sealed class BindingTemplateArgument
{
    private BindingTemplateArgument(BindingTemplateArgumentKind kind, BindingType? type, long integralValue, bool isBoolean = false, bool isNonDefaultConstructibleAllocator = false)
    {
        IsNonDefaultConstructibleAllocator = isNonDefaultConstructibleAllocator;
        Kind = kind;
        Type = type;
        IntegralValue = integralValue;
        IsBoolean = isBoolean;
    }

    public BindingTemplateArgumentKind Kind { get; }

    public BindingType? Type { get; }

    public long IntegralValue { get; }

    /// <summary>A bool non-type argument; spelled true/false in C++.</summary>
    public bool IsBoolean { get; }

    /// <summary>
    /// A type argument that looks like an allocator (declares allocate) but cannot be
    /// default-constructed. Generated wrappers default-construct every container instantiation.
    /// </summary>
    public bool IsNonDefaultConstructibleAllocator { get; }

    /// <summary>
    /// True when the source does not spell the argument, so that it comes from the template default.
    /// </summary>
    public bool IsDefaulted { get; init; }

    public static BindingTemplateArgument FromType(BindingType type, bool isNonDefaultConstructibleAllocator = false, bool isDefaulted = false)
    {
        ArgumentNullException.ThrowIfNull(type);
        return new BindingTemplateArgument(BindingTemplateArgumentKind.Type, type, 0, isNonDefaultConstructibleAllocator: isNonDefaultConstructibleAllocator)
        {
            IsDefaulted = isDefaulted
        };
    }

    public static BindingTemplateArgument FromIntegral(long value, bool isBoolean = false) =>
        new(BindingTemplateArgumentKind.Integral, null, isBoolean ? (value != 0 ? 1 : 0) : value, isBoolean);

    public static string FormatIntegral(long value, bool isBoolean) =>
        isBoolean
            ? value != 0 ? "true" : "false"
            : value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
