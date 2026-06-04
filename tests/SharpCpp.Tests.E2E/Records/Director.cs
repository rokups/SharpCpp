using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Records;

public sealed class DirectorTests
{
    private sealed class ManagedCallbackCounter : CallbackCounter
    {
        public ManagedCallbackCounter(int seed)
            : base(seed)
        {
        }

        public override int GetValue()
        {
            return 21;
        }
    }

    [Fact]
    public void Native_virtual_dispatch_can_call_managed_override_through_director()
    {
        var counter = new ManagedCallbackCounter(10);

        Assert.Equal(42, counter.CallVirtualTwice());
    }
}
