namespace SharpCpp.Model.Ast;

public enum BindingTemplateArgumentKind
{
    Type,
    Integral
}

public sealed class BindingTemplateArgument
{
    private BindingTemplateArgument(BindingTemplateArgumentKind kind, BindingType? type, long integralValue)
    {
        Kind = kind;
        Type = type;
        IntegralValue = integralValue;
    }

    public BindingTemplateArgumentKind Kind { get; }

    public BindingType? Type { get; }

    public long IntegralValue { get; }

    public static BindingTemplateArgument FromType(BindingType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return new BindingTemplateArgument(BindingTemplateArgumentKind.Type, type, 0);
    }

    public static BindingTemplateArgument FromIntegral(long value) =>
        new(BindingTemplateArgumentKind.Integral, null, value);
}
