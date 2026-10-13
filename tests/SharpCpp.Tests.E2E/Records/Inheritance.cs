using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Records;

public delegate int InheritanceProjectionCallback(int value);

public sealed class InheritanceTests
{
    private sealed class ManagedDerivedCounter : DerivedCounter
    {
        public ManagedDerivedCounter(int value, int bonus)
            : base(value, bonus)
        {
        }

        public int ReadInheritedAccess(int value)
        {
            return InheritedAccess(value);
        }
    }

    [Fact]
    public void Record_deriving_from_variadic_template_keeps_the_template_base()
    {
        using var leaf = new VariadicLeaf();

        Assert.Equal(typeof(VariadicRoot), typeof(VariadicLeaf).BaseType);
        Assert.Equal(5, leaf.LeafValue());
        Assert.Equal(11, leaf.RootValue());
        Assert.Equal(11, E2EBindings.ReadVariadicRoot(leaf));
    }

    [Fact]
    public void Record_deriving_from_parameter_base_template_derives_from_the_argument()
    {
        using var leaf = new TrackedLeaf();

        Assert.Equal(typeof(TrackedBase), typeof(TrackedLeaf).BaseType);
        Assert.Equal(3, leaf.LeafValue());
        Assert.Equal(21, leaf.TrackedValue());
    }

    [Fact]
    public void Derived_wrapper_inherits_from_base_wrapper()
    {
        using var derived = new DerivedCounter(10, 2);

        Assert.Equal(2, derived.GetKind());
        Assert.Equal(10, derived.GetValue());
        Assert.Equal(2, derived.GetBonus());
        Assert.Equal(12, derived.AddAll());
    }

    [Fact]
    public void Derived_wrapper_can_be_used_through_base_view()
    {
        using var derived = new DerivedCounter(10, 2);
        BaseCounter counter = derived;

        Assert.Equal(2, counter.GetKind());
        Assert.Equal(10, counter.GetValue());
    }

    [Fact]
    public void Secondary_base_method_shapes_project_onto_the_derived_wrapper()
    {
        using var derived = new ManagedDerivedCounter(10, 2);

        Assert.Equal(12, derived.ProjectedConst(10));
        Assert.Equal(42, derived.ProjectedDefault(37));
        Assert.Equal(42, derived.ProjectedDefault(35, 5));

        var referenced = 38;
        derived.ProjectedRef(ref referenced);
        Assert.Equal(42, derived.ProjectedConst(referenced));

        Assert.Equal(40, derived.ProjectedOut(38, out var output));
        Assert.Equal(42, derived.ProjectedConst(output));

        using var argument = new DerivedCounter(42, 1);
        Assert.Equal(42, derived.ProjectedHandle(argument).GetValue());

        using var owned = derived.ProjectedOwnedValue(40);
        Assert.Equal(42, owned.GetValue());

        Assert.Equal(42, derived.ProjectedCallback(value => value * 2, 20));
        Assert.Equal(42, derived.ReadInheritedAccess(32));
        Assert.Equal(42, DerivedCounter.InheritedStatic(40));
        Assert.Equal(42, derived.ProjectedMutate(40));
    }

    [Fact]
    public void Free_function_can_accept_derived_wrapper_as_base_parameter()
    {
        using var counter = new DerivedCounter(10, 2);

        Assert.Equal(10, E2EBindings.ReadBaseCounter(counter));
        Assert.Equal(2, E2EBindings.ReadBaseCounterKind(counter));
    }

    [Fact]
    public void Static_method_can_roundtrip_base_typed_wrapper()
    {
        using var counter = new DerivedCounter(10, 2);
        var result = DerivedCounter.IdentityBase(counter);

        Assert.Equal(10, result.GetValue());
    }

    [Fact]
    public void Null_polymorphic_pointer_return_is_a_null_reference()
    {
        using var owner = new DerivedCounter(10, 2);

        Assert.Null(DerivedCounter.IdentityBase(null!));
        Assert.Null(owner.EchoBase(null!));
    }

    [Fact]
    public void Instance_method_can_roundtrip_base_typed_wrapper()
    {
        using var owner = new DerivedCounter(10, 2);
        using var argument = new DerivedCounter(20, 3);
        var result = owner.EchoBase(argument);

        Assert.Equal(20, result.GetValue());
    }

    [Fact]
    public void Base_typed_fields_can_store_derived_wrappers()
    {
        using var derived = new DerivedCounter(10, 2);
        var holder = E2EBindings.CreateInheritanceHolder(derived);

        Assert.Equal(10, holder.BaseView.GetValue());

        using var replacement = new DerivedCounter(20, 3);
        holder.BaseView = replacement;

        Assert.Equal(20, holder.BaseView.GetValue());
    }

    [Fact]
    public void Base_typed_free_function_return_retains_native_derived_behavior()
    {
        var first = E2EBindings.GetSharedDerivedAsBase();

        Assert.Equal(30, first.GetValue());
        Assert.Equal(4, ((DerivedCounter)first).GetBonus());
    }
}
