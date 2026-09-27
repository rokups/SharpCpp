using Coverage_matrix;
using SharpCpp.Tests.E2E.Generated;
using Xunit;

namespace SharpCpp.Tests.E2E.Marshallers;

public sealed class ProjectionTests
{
    private sealed class ManagedProjectedOwnerDirector : ProjectedOwnerDirector
    {
        public int CallbackCount { get; private set; }

        public override ProjectedPayload CreatePayload(int value)
        {
            CallbackCount++;
            return E2EBindings.MakeProjectedPayload(value + 5);
        }
    }

    [Fact]
    public void Projected_owner_round_trips_as_its_generated_payload_wrapper()
    {
        using var payload = E2EBindings.MakeProjectedPayload(37);

        Assert.Equal(37, payload.GetValue());
        Assert.Equal(37, E2EBindings.ConsumeProjectedPayload(payload));
    }

    [Fact]
    public void Owned_projected_virtual_return_transfers_to_native_dispatch()
    {
        using var director = new ManagedProjectedOwnerDirector();

        Assert.Equal(46, director.InvokeAndConsume(41));
        Assert.Equal(1, director.CallbackCount);
    }
}
