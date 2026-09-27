using Instantiations;

using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Templates;

public sealed class InstantiationTests
{
    [Fact]
    public void Container_named_only_by_a_callback_signature_is_instantiated()
    {
        Assert.Equal(30, E2EBindings.InvokeWithMap(static values => values[1] + values[2]));
    }

    [Fact]
    public void Open_container_member_is_not_bound_and_does_not_instantiate_the_container()
    {
        using var box = E2EBindings.CreateOpenMemberBox();
        Assert.Equal(6, box.Identity(6));
        Assert.Null(typeof(OpenMemberBox<int>).GetMethod("Count"));
    }

    [Fact]
    public void Defaulted_template_arguments_are_completed_from_the_specialization()
    {
        using var box = E2EBindings.CreateTaggedBox(8);
        Assert.IsType<TaggedBox<int, DefaultTag>>(box);
        Assert.Equal(8, box.Get());
    }

    [Fact]
    public void Explicit_instantiation_survives_while_a_degraded_use_is_dropped()
    {
        using var box = E2EBindings.CreateExplicitBox(3);
        Assert.False(box.IsNull);
        Assert.Null(typeof(E2EBindings).GetMethod("DegradedExplicitBoxUse"));
    }

    [Fact]
    public void Instantiations_that_do_not_cover_every_parameter_or_hold_ref_structs_are_dropped()
    {
        Assert.Null(typeof(E2EBindings).GetMethod("BitCastValue"));
        Assert.Null(typeof(E2EBindings).GetMethod("CountViews"));
        Assert.Null(typeof(E2EBindings).GetMethod("CountLocales"));
        Assert.Null(typeof(E2EBindings).GetMethod("CountIgnoredItems"));
    }
}
