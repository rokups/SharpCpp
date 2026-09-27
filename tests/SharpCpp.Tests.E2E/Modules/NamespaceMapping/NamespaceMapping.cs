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
}
