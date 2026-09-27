using Default_expressions;

using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Records;

public sealed class DefaultExpressionTests
{
    [Fact]
    public void Lambda_default_is_applied_natively()
    {
        Assert.Equal(42u, E2EBindings.ReadTime());
    }

    [Fact]
    public void Enum_and_floating_defaults_become_managed_defaults()
    {
        Assert.Equal(1, E2EBindings.UseMode());
        Assert.Equal(Default_expressions.DefaultMode.Active, typeof(E2EBindings).GetMethod(nameof(E2EBindings.UseMode))!.GetParameters()[0].DefaultValue);

        Assert.Equal(0.75, E2EBindings.SumFloatingDefaults());
        var parameters = typeof(E2EBindings).GetMethod(nameof(E2EBindings.SumFloatingDefaults))!.GetParameters();
        Assert.Equal(-1f, parameters[0].DefaultValue);
        Assert.Equal(0.5f, parameters[1].DefaultValue);
        Assert.Equal(1.25, parameters[2].DefaultValue);
    }
}
