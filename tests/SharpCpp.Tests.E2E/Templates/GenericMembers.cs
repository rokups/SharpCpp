using Generic_members;

using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Templates;

public sealed class GenericMemberTests
{
    [Fact]
    public void Member_template_takes_its_parameter_by_const_reference()
    {
        using var element = new Element();
        Assert.Equal(5, element.GetAttribute(5));
        Assert.Equal(2.5f, element.GetAttribute(2.5f));
        Assert.Equal(7, element.GetAttribute<int>(7));
    }

    [Fact]
    public void Template_parameter_is_not_confused_with_an_unrelated_member_alias()
    {
        using var result = E2EBindings.CreateIntResult(4);
        Assert.Equal(4, result.Get());
        result.Set(9);
        Assert.Equal(9, result.Get());
    }

    [Fact]
    public void Template_constructors_are_not_bound()
    {
        Assert.Empty(typeof(Guard).GetConstructors());
        using var guard = Guard.Create(3);
        Assert.Equal(3, guard.Value());
    }

    [Fact]
    public void Implicit_default_constructors_are_materialized_only_when_declared_by_the_language()
    {
        using var settings = new Settings();
        Assert.Equal(1, settings.Apply());

        using var sink = new IntSink();
        sink.Set(9);
        Assert.Equal(9, sink.Get());

        Assert.Equal([typeof(int)], Assert.Single(typeof(ExplicitSettings).GetConstructors()).GetParameters().Select(static p => p.ParameterType));

        using var inherited = new InheritedDerived(4);
        Assert.Equal(4, inherited.Value());
        Assert.Null(typeof(InheritedDerived).GetConstructor(Type.EmptyTypes));
    }

    [Fact]
    public void Materialized_alias_of_a_template_on_a_template_base_is_a_concrete_record()
    {
        Assert.False(typeof(HitResults).IsAbstract);
        Assert.False(typeof(HitResults).IsGenericType);

        using var hits = new HitResults();
        Assert.False(hits.HadHit());
        Assert.Equal(11, E2EBindings.AddHitResult(hits, 11));
        Assert.True(hits.HadHit());
        Assert.Equal(11, hits.HitId);
    }

    [Fact]
    public void Template_deriving_from_its_parameter_binds_through_a_closed_alias()
    {
        using var hits = E2EBindings.CreatePairHits();
        var before = E2EBindings.CountPairHits(hits);
        hits.Hit();
        hits.Hit();
        Assert.Equal(before + 2, E2EBindings.CountPairHits(hits));
    }

    [Fact]
    public void Concrete_override_of_a_generic_const_reference_virtual_overrides_it()
    {
        Assert.Equal(typeof(Adder<int>), typeof(IntAdder).GetMethod(nameof(IntAdder.Add))!.GetBaseDefinition().DeclaringType);
        using var adder = new IntAdder();
        E2EBindings.AddThroughBase(adder, 4);
        adder.Add(5);
        Assert.Equal(9, adder.Total());
    }
}
