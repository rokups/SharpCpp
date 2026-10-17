using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Records;

public sealed partial class MultipleInheritanceTests
{
    private sealed partial class ManagedMiCounter : MiCounter
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

    [Fact]
    public void Concrete_record_with_private_secondary_override_stays_constructible()
    {
        using var value = new MiConcretePrivateOverride();

        Assert.Equal(30, value.PureRightValue());
    }

    [Fact]
    public void Secondary_base_parameters_accept_the_derived_wrapper()
    {
        using var counter = new MiCounter();

        Assert.Equal(200, E2EBindings.ConsumeRight(counter));
        Assert.Equal(201, E2EBindings.ConsumeRightPtr(counter));
        Assert.Equal(-1, E2EBindings.ConsumeRightPtr(null!));
    }

    [Fact]
    public void Secondary_base_parameters_accept_the_base_wrapper()
    {
        using var right = new MiRight();

        Assert.Equal(20, E2EBindings.ConsumeRight(right));
        Assert.Equal(21, E2EBindings.ConsumeRightPtr(right));
    }

    [Fact]
    public void Secondary_base_parameters_dispatch_to_managed_director_overrides()
    {
        using var counter = new ManagedMiCounter();

        Assert.Equal(5, E2EBindings.ConsumeRight(counter));
        Assert.Equal(6, E2EBindings.ConsumeRightPtr(counter));
    }

    [Fact]
    public void Polymorphic_secondary_base_return_resolves_to_the_most_derived_wrapper()
    {
        var shared = E2EBindings.SharedMiCounter();
        var picked = E2EBindings.PickRight();

        var counter = Assert.IsType<MiCounter>(picked);
        Assert.Same(shared, counter);
        Assert.Equal(200, picked.RightValue());
    }

    [Fact]
    public void Polymorphic_secondary_base_return_yields_the_passed_instance()
    {
        using var counter = new MiCounter();

        Assert.Same(counter, E2EBindings.PickCounterRight(counter));
    }

    [Fact]
    public void Non_polymorphic_secondary_base_takes_the_view_and_returns_the_base_class()
    {
        using var pair = new MiPlainPair();

        Assert.Equal(2, E2EBindings.ConsumePlainRight(pair));

        MiPlainRight picked = E2EBindings.PickPlainRight();
        Assert.Equal(5, picked.PlainRightValue());
    }

    [Fact]
    public void Secondary_base_const_pointer_parameters_take_the_view()
    {
        using var counter = new MiCounter();

        Assert.Equal(201, E2EBindings.ConsumeRightConstPtr(counter));
    }

    [Fact]
    public void Primary_base_return_of_a_director_object_yields_the_passed_instance()
    {
        using var counter = new ManagedMiCounter();

        var left = E2EBindings.PickCounterLeft(counter);

        Assert.Same(counter, left);
        Assert.Equal(7, left.LeftValue());
    }

    [Fact]
    public void Shared_secondary_base_parameters_accept_every_derived_wrapper()
    {
        using var pair = new MiNestedPair();
        using var other = new MiNestedOther();

        Assert.Equal(50, E2EBindings.ConsumeNestedInner(pair));
        Assert.Equal(50, E2EBindings.ConsumeNestedInner(other));
    }

    [Fact]
    public void Virtual_secondary_base_keeps_the_class_parameter_and_its_derived_record_is_logged()
    {
        using var right = new MiVirtualRight();

        Assert.Equal(40, E2EBindings.ConsumeVirtualRight(right));
        Assert.Equal(typeof(MiVirtualRight), typeof(E2EBindings).GetMethod(nameof(E2EBindings.ConsumeVirtualRight))!.GetParameters()[0].ParameterType);
        Assert.Contains(
            Diagnostics.GeneratedArtifacts.ReadText("generator.log").Split('\n'),
            static line => line.StartsWith("IGNORED\tRecord\t", StringComparison.Ordinal) &&
                line.Contains("\tmi_virtual_pair\t", StringComparison.Ordinal) &&
                line.Contains("virtual base inheritance", StringComparison.Ordinal));
    }
}
