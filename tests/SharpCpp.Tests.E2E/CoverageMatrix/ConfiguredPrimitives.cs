using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.CoverageMatrix;

public sealed class ConfiguredPrimitiveTests
{
    [Fact]
    public void Configured_primitive_shapes_round_trip_through_native_marshallers()
    {
        Assert.True(E2EBindings.ConfiguredBoolRoundTrip(true));
        Assert.Equal((sbyte)-7, E2EBindings.ConfiguredSbyteRoundTrip(-7));
        Assert.Equal((byte)251, E2EBindings.ConfiguredByteRoundTrip(251));
        Assert.Equal((short)-1234, E2EBindings.ConfiguredShortRoundTrip(-1234));
        Assert.Equal((ushort)54321, E2EBindings.ConfiguredUshortRoundTrip(54321));
        Assert.Equal(4_000_000_000u, E2EBindings.ConfiguredUintRoundTrip(4_000_000_000u));
        Assert.Equal(-5_000_000_000L, E2EBindings.ConfiguredLongRoundTrip(-5_000_000_000L));
        Assert.Equal(10_000_000_000UL, E2EBindings.ConfiguredUlongRoundTrip(10_000_000_000UL));
        Assert.Equal(1.25f, E2EBindings.ConfiguredFloatRoundTrip(1.25f));
        Assert.Equal(2.5, E2EBindings.ConfiguredDoubleRoundTrip(2.5));
        Assert.Equal((nint)(-37), E2EBindings.ConfiguredNintRoundTrip(-37));
        Assert.Equal((nuint)41, E2EBindings.ConfiguredNuintRoundTrip(41));
    }
}
