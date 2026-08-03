using Coverage_matrix;
using SharpCpp.Tests.E2E.Generated;
using Xunit;

namespace SharpCpp.Tests.E2E.CoverageMatrix;

public sealed class ProjectionTests
{
    [Fact]
    public void Projected_owner_round_trips_as_its_generated_payload_wrapper()
    {
        using var payload = E2EBindings.MakeProjectedPayload(37);

        Assert.Equal(37, payload.GetValue());
        Assert.Equal(37, E2EBindings.ConsumeProjectedPayload(payload));
    }
}
