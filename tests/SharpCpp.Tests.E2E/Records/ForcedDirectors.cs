using Forced_directors;

using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Records;

public sealed class ForcedDirectorTests
{
    private sealed class Seeded(int seed) : ForcedAbstract(seed)
    {
        public override int Compute() => 30;
    }

    private sealed class Implicit : ForcedImplicitAbstract
    {
        public override int Compute() => 7;
    }

    private sealed class Derived : ForcedDerivedAbstract
    {
        public override int Compute() => 9;
    }

    [Fact]
    public void Consumer_forced_director_keeps_the_abstract_record_constructible_from_managed_code()
    {
        using var value = new Seeded(12);
        Assert.Equal(42, E2EBindings.ComputeForcedAbstract(value));
    }

    [Fact]
    public void Abstract_record_without_a_director_has_no_constructor()
    {
        Assert.Null(typeof(UnforcedAbstract).GetConstructor(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
            [typeof(int)]));
    }

    [Fact]
    public void Forced_director_materializes_the_implicit_default_constructor_only()
    {
        using var implicitValue = new Implicit();
        Assert.Equal(7, E2EBindings.ComputeForcedImplicitAbstract(implicitValue));

        using var derived = new Derived();
        Assert.Equal(9, E2EBindings.ComputeForcedDerivedAbstract(derived));
        Assert.Equal([], Assert.Single(typeof(ForcedDerivedAbstract).GetConstructors()).GetParameters());
    }

    [Fact]
    public void Abstract_record_without_a_director_wraps_an_unbound_native_implementation()
    {
        var value = E2EBindings.HiddenUnforcedAbstract();

        Assert.True(typeof(UnforcedAbstract).IsAbstract);
        Assert.False(value.GetType().IsVisible);
        Assert.Equal(45, value.Compute());
        Assert.Equal(5, value.Seed());
        Assert.Same(value, E2EBindings.HiddenUnforcedAbstract());
        Assert.Same(value, E2EBindings.HiddenUnforcedAbstractRef());
    }

    [Fact]
    public void Abstract_record_without_a_director_returns_null_for_a_null_pointer()
    {
        Assert.Null(E2EBindings.NullUnforcedAbstract());
    }

    [Fact]
    public void Abstract_record_without_a_director_resolves_a_bound_derived_wrapper()
    {
        var value = E2EBindings.SharedUnforcedConcrete();

        Assert.IsType<UnforcedConcrete>(value);
        Assert.Equal(70, value.Compute());
        Assert.Equal(3, value.Seed());
    }

    [Fact]
    public void Abstract_record_without_a_director_overrides_inherited_abstract_members()
    {
        var value = E2EBindings.HiddenUnforcedAbstractChild();

        Assert.True(typeof(UnforcedAbstractChild).IsAbstract);
        Assert.Equal(16, value.Compute());
        Assert.Equal(12, value.Twice());
        Assert.Equal(6, value.Seed());
    }

    [Fact]
    public void Abstract_record_without_a_director_keeps_inherited_implementations()
    {
        var value = E2EBindings.HiddenUnforcedPartialChild();

        Assert.True(typeof(UnforcedPartialChild).IsAbstract);
        Assert.Equal(9, value.Compute());
        Assert.Equal(108, value.Extra());
        Assert.Null(value.GetType().GetMethod(nameof(UnforcedPartialChild.Compute), System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly));
    }
}
