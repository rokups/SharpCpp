using SharpCpp.Tests.E2E.Coverage.Degradation.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Coverage;

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

        using var box = new FilteredBox<int>();
        Assert.Equal(29, box.Echo(29));
        Assert.Equal(31, DegradationBindings.FilteredIdentity(31));
    }
}
