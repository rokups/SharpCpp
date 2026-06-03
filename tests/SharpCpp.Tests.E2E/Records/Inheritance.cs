using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Records;

public sealed class InheritanceTests
{
    [Fact]
    public void Derived_wrapper_inherits_from_base_wrapper()
    {
        var derived = new DerivedCounter(10, 2);

        Assert.IsAssignableFrom<BaseCounter>(derived);
        Assert.Equal(10, derived.GetValue());
        Assert.Equal(2, derived.GetBonus());
        Assert.Equal(12, derived.AddAll());
    }

    [Fact]
    public void Derived_wrapper_can_be_used_through_base_view()
    {
        BaseCounter counter = new DerivedCounter(10, 2);

        Assert.Equal(10, counter.GetValue());
    }

    [Fact]
    public void Free_function_can_accept_derived_wrapper_as_base_parameter()
    {
        var counter = new DerivedCounter(10, 2);

        Assert.Equal(10, E2EBindings.ReadBaseCounter(counter));
    }

    [Fact]
    public void Static_method_can_roundtrip_base_typed_wrapper()
    {
        var counter = new DerivedCounter(10, 2);
        var result = DerivedCounter.IdentityBase(counter);

        Assert.IsAssignableFrom<BaseCounter>(result);
        Assert.Equal(10, result.GetValue());
    }

    [Fact]
    public void Instance_method_can_roundtrip_base_typed_wrapper()
    {
        var owner = new DerivedCounter(10, 2);
        var argument = new DerivedCounter(20, 3);
        var result = owner.EchoBase(argument);

        Assert.IsAssignableFrom<BaseCounter>(result);
        Assert.Equal(20, result.GetValue());
    }
}
