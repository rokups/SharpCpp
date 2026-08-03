using SharpCpp.Tests.E2E.Generated;
using Xunit;

namespace SharpCpp.Tests.E2E.CoverageMatrix;

public sealed class PointerArrayTests
{
    [Fact]
    public void Primitive_pointer_arrays_round_trip_as_read_only_and_writable_spans()
    {
        var values = new[] { 2, 3, 5 };
        Assert.Equal(10, E2EBindings.SumPointerValues(values, (uint)values.Length));

        E2EBindings.IncrementPointerValues(values, (uint)values.Length);
        Assert.Equal(new[] { 3, 4, 6 }, values);

        using var host = new PointerArrayHost(7);
        Assert.Equal(20, host.Sum(values, (uint)values.Length));
        host.Increment(values, (uint)values.Length);
        Assert.Equal(new[] { 4, 5, 7 }, values);
    }

    [Fact]
    public void Handle_and_raw_pointer_arrays_round_trip_through_native_pointer_buffers()
    {
        using var first = new PointerArrayPayload(11);
        using var second = new PointerArrayPayload(13);
        PointerArrayPayload?[] payloads = [first, second];

        Assert.Same(first, E2EBindings.FirstPointerPayload(payloads, (uint)payloads.Length));
        Assert.Equal(37, E2EBindings.WeightedPointerPayloadSum(
            (uint)payloads.Length,
            payloads,
            new[] { 1, 2 }));

        using var host = new PointerArrayHost(0);
        Assert.Same(first, host.First((uint)payloads.Length, payloads));
        Assert.Equal(61, PointerArrayHost.WeightedSum(
            (uint)payloads.Length,
            payloads,
            new[] { 2, 3 }));

        nint[] addresses = [3, 5, 7];
        Assert.Equal(15, E2EBindings.SumPointerAddresses(addresses, (uint)addresses.Length));
    }
}
