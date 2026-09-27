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
}
