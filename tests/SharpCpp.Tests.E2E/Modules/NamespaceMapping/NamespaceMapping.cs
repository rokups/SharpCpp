using SharpCpp.Tests.E2E.Modules.NamespaceMapping.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Modules.NamespaceMapping;

public sealed class DegradationTests
{
    [Fact]
    public void Mapped_and_degraded_header_keeps_supported_native_round_trips()
    {
        Assert.Equal(42, DegradationBindings.DegradationRoundTrip(41));

        using var counter = new MappedCounter(17);
        Assert.Equal(17, counter.GetValue());

        using var collision = new ConstCollision(23);
        Assert.Equal(23, collision.Read());

        using var fieldCollision = DegradationBindings.CreateDegradationFieldCollision(11);
        Assert.Equal(11, fieldCollision.ValueValue2);
        fieldCollision.ValueValue2 = 13;
        Assert.Equal(14, fieldCollision.Value());
        Assert.Equal(15, fieldCollision.ValueValue());

        using var operators = new DegradationOperatorSurface(19);
        Assert.Equal(19, operators.GetValue());
        Assert.Equal(22, +operators);

        using var box = new FilteredBox<int>();
        Assert.Equal(29, box.Echo(29));
        Assert.Equal(31, DegradationBindings.FilteredIdentity(31));
    }

    [Fact]
    public void Nested_records_stay_inside_their_enclosing_class_under_a_flat_projection()
    {
        using var first = new FirstNode.Instance();
        using var second = new SecondNode.Instance();
        Assert.Equal(1, first.Read());
        Assert.Equal(2, second.Read());
        Assert.Equal(typeof(FirstNode), typeof(FirstNode.Instance).DeclaringType);

        using var third = new SharpCpp.Tests.E2E.Modules.NamespaceMapping.Generated.ThirdNode.Instance();
        Assert.Equal(3, third.Read());
        Assert.EndsWith(".ThirdNode", typeof(SharpCpp.Tests.E2E.Modules.NamespaceMapping.Generated.ThirdNode.Instance).Namespace, StringComparison.Ordinal);
    }
}

public sealed class EaDegradationTests
{
    [Fact]
    public void Ea_callbacks_degrade_like_std_callbacks()
    {
        foreach (var name in new[]
        {
            "CallbackWithUnsupportedReturn",
            "CallbackWithIgnoredReturn",
            "CallbackWithUnsupportedParameter",
            "CallbackWithIgnoredParameter"
        })
        {
            Assert.Null(typeof(DegradationBindings).GetMethod(name));
            Assert.Null(typeof(DegradationBindings).GetMethod("Ea" + name));
        }

        // A [[sharpcpp::ref]] callback by value is excluded for both function types.
        Assert.Null(typeof(DegradationBindings).GetMethod("CallbackMarkedRef"));
        Assert.Null(typeof(DegradationBindings).GetMethod("EaCallbackMarkedRef"));

        Assert.Null(typeof(UnsupportedFields).GetProperty("Callback"));
        Assert.Null(typeof(UnsupportedFields).GetProperty("EaCallback"));
    }
}
