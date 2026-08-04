using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Records;

public sealed class MultipleInheritanceTests
{
    private sealed class ManagedMiCounter : MiCounter
    {
        public override int LeftValue()
        {
            return 7;
        }

        public override int RightValue()
        {
            return 5;
        }
    }

    [Fact]
    public void Secondary_base_methods_are_projected_onto_the_derived_wrapper()
    {
        using var counter = new MiCounter();

        Assert.Equal(200, counter.RightValue());
        Assert.Equal(201, counter.RightOnly());
        Assert.Equal(300, counter.SumVirtuals());
    }

    [Fact]
    public void Secondary_base_interface_dispatches_native_methods()
    {
        using var counter = new MiCounter();

        IMiRight rightView = counter;
        Assert.Equal(200, rightView.RightValue());
        Assert.Equal(201, rightView.RightOnly());
    }

    [Fact]
    public void Secondary_base_virtuals_can_dispatch_to_managed_overrides_through_director()
    {
        using var counter = new ManagedMiCounter();

        Assert.Equal(12, counter.SumVirtuals());
        Assert.Equal(6, counter.RightOnly());
    }
}
