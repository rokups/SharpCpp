using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Callback;

public sealed class AliasCallbackBreadthTests
{
    [Fact]
    public void Aliased_callback_handles_and_alternate_count_fields_round_trip_native_values()
    {
        using var first = new AliasCallbackPayload(19);
        using var second = new AliasCallbackPayload(23);

        Assert.Same(first, E2EBindings.InvokeAliasCallback(value => value, first));

        using var view = E2EBindings.MakeAliasCallbackPayloadView(first, second);
        Assert.Equal(2u, view.PayloadCount);
        Assert.Same(first, view.GetPayloadArrayAt(0));
        Assert.Same(second, view.GetPayloadArrayAt(1));
        Assert.Equal(42, view.GetPayloadArrayAt(0).GetValue() + view.GetPayloadArrayAt(1).GetValue());
    }
}
