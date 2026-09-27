using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Marshallers;

public sealed class ConfiguredMemberBreadthTests
{
    [Fact]
    public void Configured_exposed_overloads_and_handles_round_trip_through_native_code()
    {
        using var value = E2EBindings.MakeConfiguredMemberValue(40);
        Assert.Equal(40, E2EBindings.ReadConfiguredMemberValue(value));
        Assert.Equal(42, value.Probe(2));
        Assert.Equal(42.5, value.Probe(2.5));

        using var payload = E2EBindings.MakeConfiguredMemberPayload(37);
        Assert.Same(payload, value.EchoPayload(payload));
        Assert.Equal(37, payload.GetValue());

        using var defaultValue = new ConfiguredMemberValue();
        Assert.Equal(41, defaultValue.Probe(1));
        Assert.Equal(40, E2EBindings.ReadConfiguredMemberValue(defaultValue));
    }
}
