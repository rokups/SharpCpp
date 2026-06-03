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
}
