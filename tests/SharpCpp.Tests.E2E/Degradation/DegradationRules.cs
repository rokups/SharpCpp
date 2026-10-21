using Degradation_rules;

using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Degradation;

public sealed partial class DegradationRuleTests
{
    private sealed class ContextSubject : ContextObject
    {
        public void Attach(Context context) => Context = context;
    }

    private sealed partial class UnsupportedSubject : UnsupportedAbstract
    {
        public override unsafe int Compute(nint value) => *(int*)value + 1;
    }

    [Fact]
    public void Static_field_colliding_with_an_instance_field_takes_a_constant_suffix()
    {
        using var settings = new BounceSettings { MaxBounces = 4 };
        Assert.Equal(8u, BounceSettings.MaxBouncesConstant);
        Assert.Equal(12u, E2EBindings.ReadBounceSettings(settings));
    }

    [Fact]
    public void Enum_generic_argument_resolves_through_an_alias()
    {
        Assert.NotNull(typeof(AttributeInfo).GetConstructor([typeof(FlagSet<AttributeMode>)]));
        Assert.NotNull(typeof(AttributeInfo).GetProperty(nameof(AttributeInfo.ModeBits)));
    }

    [Fact]
    public void Protected_handle_fields_are_bound_unless_their_record_is_not()
    {
        using var subject = new ContextSubject();
        Assert.Equal(-1, E2EBindings.ReadContextObject(subject));
        subject.Attach(E2EBindings.GetSharedContext());
        Assert.Equal(11, E2EBindings.ReadContextObject(subject));
        Assert.Null(typeof(ContextObject).GetProperty("Hidden", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic));
    }

    [Fact]
    public void Reference_fields_and_forward_declared_records_are_not_bound()
    {
        using var owner = E2EBindings.CreateReferenceOwner();
        Assert.Equal(6, owner.Plain);
        Assert.Null(typeof(ReferenceOwner).GetProperty("Value"));
        Assert.Null(typeof(E2EBindings).GetMethod("GetForwardOnly"));
        Assert.Null(typeof(E2EBindings).Assembly.GetType("Degradation_rules.ForwardOnly"));
    }

    [Fact]
    public unsafe void Director_virtuals_take_primitive_references_as_pointers()
    {
        using var drawable = new Drawable();
        var origin = 7;
        drawable.UpdateOrigin((nint)(&origin));
        Assert.Equal(7, drawable.Origin());

        using var subject = new UnsupportedSubject();
        var value = 4;
        Assert.Equal(5, subject.Compute((nint)(&value)));
    }

    [Fact]
    public void Director_virtual_with_a_callback_parameter_is_not_bound()
    {
        Assert.Null(typeof(CallbackVisitor).GetMethod("Visit"));
        Assert.Null(typeof(CallbackVisitor).GetMethod("VisitValue"));

        using var visitor = new CountingCallbackVisitor();
        Assert.Equal(5, E2EBindings.CallCount(visitor));
        Assert.Equal(23, E2EBindings.CallVisit(visitor));
    }

    private sealed partial class CountingCallbackVisitor : CallbackVisitor
    {
        public override int Count() => 5;
    }

    [Fact]
    public void Director_with_an_unavailable_pure_virtual_is_not_bound()
    {
        Assert.Null(typeof(E2EBindings).Assembly.GetType("Degradation_rules.UnavailableAbstract"));
    }

    [Fact]
    public void Static_only_records_and_record_value_callback_returns_are_bound()
    {
        Assert.Equal(12, Utility.Run());
        Assert.Equal(9, E2EBindings.InvokeResultCallback(static () => new CallbackResult { Value = 9 }));
        Assert.Equal(3, E2EBindings.InvokeValueArgumentCallback(static value => value.Value));
    }

    [Fact]
    public void Unsupported_callback_shapes_are_not_bound()
    {
        Assert.Null(typeof(E2EBindings).GetMethod("GetCallback"));
        Assert.Null(typeof(CallbackOwner).GetProperty("Callback"));
        Assert.Null(typeof(E2EBindings).GetMethod("InvokeViewReturnCallback"));
        Assert.Null(typeof(E2EBindings).GetMethod("InvokeMutableSpanCallback"));
        Assert.Null(typeof(E2EBindings).GetMethod("FirstSharedDelegate"));
        Assert.Null(typeof(E2EBindings).GetMethod("SecondSharedDelegate"));
        Assert.Null(typeof(E2EBindings).GetMethod("RefSharedDelegate"));
        Assert.Null(typeof(E2EBindings).GetMethod("ValueSharedDelegate"));
        Assert.Null(typeof(E2EBindings).GetMethod("ReplaceCallback"));
    }

    [Fact]
    public void Renamed_assignment_operator_is_a_method()
    {
        using var target = new Assignable();
        using var source = new Assignable { Value = 4 };
        target.Assign(source);
        Assert.Equal(5, target.Value);
    }

    [Fact]
    public void Equivalent_global_and_namespaced_declarations_bind_once()
    {
        Assert.Single(typeof(E2EBindings).GetMethods(), static method => method.Name == nameof(E2EBindings.EquivalentDeclaration));
        Assert.Equal(2, E2EBindings.EquivalentDeclaration());
    }

    [Fact]
    public void Const_overloads_bind_once_unless_the_non_const_one_is_unsupported()
    {
        using var value = new ConstOverloads();
        Assert.Single(typeof(ConstOverloads).GetMethods(), static method => method.Name == nameof(ConstOverloads.Data));
        Assert.Equal(5, value.Data());
        Assert.Equal(6, value.Take());
        Assert.Equal(8, value.Item(3));
        Assert.Equal(5, value.Item());
        Assert.Equal(4, value.Lookup(4));
        Assert.Equal(8, value.Lookup<int>(4));
    }

    [Fact]
    public void Iterator_endpoints_and_assignment_are_not_bound_while_equality_is()
    {
        using var first = new Iterable();
        using var second = new Iterable();
        Assert.Equal(2, first.Count());
        Assert.True(first.Equals(second));
        Assert.Null(typeof(Iterable).GetMethod("Begin"));
        Assert.Null(typeof(Iterable).GetMethod("End"));
    }

    [Fact]
    public void Unsupported_builtin_types_spelled_through_using_declarations_are_reported()
    {
        Assert.Null(typeof(E2EBindings).GetMethod("TakeNull"));
        Assert.Contains(
            Diagnostics.GeneratedArtifacts.ReadText("generator.log").Split('\n'),
            static line => line.StartsWith("IGNORED\tFunction\t", StringComparison.Ordinal) &&
                line.Contains("\tdegradation_rules::take_null\t", StringComparison.Ordinal) &&
                line.Contains("std::nullptr_t", StringComparison.Ordinal));
    }
}
