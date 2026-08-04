using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.VirtualAdvanced;

public sealed class VirtualAdvancedTests
{
    private sealed class ManagedAdvVirtualCounter : AdvVirtualCounter
    {
        public ManagedAdvVirtualCounter(int seed)
            : base(seed)
        {
        }

        public int CallbackCount { get; private set; }

        public int Offset { get; private set; }

        public override int GetValue()
        {
            CallbackCount++;
            return GetSeed() + Offset;
        }

        public override int Add(int amount)
        {
            CallbackCount++;
            Offset += amount;
            return GetSeed() + Offset;
        }
    }

    private sealed class ManagedAdvAccumulator : AdvAbstractAccumulator
    {
        public ManagedAdvAccumulator(int baseValue)
            : base(baseValue)
        {
        }

        public int CallbackCount { get; private set; }

        public int Extra { get; set; }

        public override int Compute(int value)
        {
            CallbackCount++;
            return GetBaseValue() + value + Extra;
        }
    }

    [Fact]
    public void Native_calls_dispatch_to_managed_virtual_overrides()
    {
        using var counter = new ManagedAdvVirtualCounter(10);

        Assert.Equal(10, E2EBindings.InvokeAdvVirtualCounterGetValue(counter));
        Assert.Equal(1, counter.CallbackCount);

        Assert.Equal(15, E2EBindings.InvokeAdvVirtualCounterAdd(counter, 5));
        Assert.Equal(2, counter.CallbackCount);
        Assert.Equal(5, counter.Offset);

        Assert.Equal(15, E2EBindings.InvokeAdvVirtualCounterGetValue(counter));
        Assert.Equal(3, counter.CallbackCount);
    }

    [Fact]
    public void Native_calls_dispatch_to_managed_pure_virtual_overrides()
    {
        using var accumulator = new ManagedAdvAccumulator(7)
        {
            Extra = 5
        };

        Assert.Equal(13, E2EBindings.InvokeAdvAbstractAccumulatorCompute(accumulator, 1));
        Assert.Equal(1, accumulator.CallbackCount);

        Assert.Equal(14, E2EBindings.InvokeAdvAbstractAccumulatorCompute(accumulator, 2));
        Assert.Equal(2, accumulator.CallbackCount);
    }

    [Fact]
    public void Protected_override_of_public_base_dispatches_through_public_binding()
    {
        using var value = new AdvProtectedOverrideDerived(1);
        AdvPublicVirtualBase baseView = value;

        Assert.Equal(16, value.Adjust(5));
        Assert.Equal(16, baseView.Adjust(5));
        Assert.Equal(16, E2EBindings.InvokeAdvPublicVirtualBaseAdjust(value, 5));
    }

    [Fact]
    public void Public_override_of_protected_base_dispatches_through_native_base_surface()
    {
        using var value = new AdvPublicOverrideDerived(1);
        AdvProtectedVirtualBase baseView = value;

        Assert.Equal(36, value.InvokeAdjust(5));
        Assert.Equal(36, baseView.InvokeAdjust(5));
        Assert.Equal(36, E2EBindings.InvokeAdvProtectedVirtualBaseAdjust(value, 5));
    }

    [Fact]
    public void Final_and_leaf_overrides_dispatch_through_native_base_views()
    {
        using var leaf = new AdvFinalLeaf(5);
        AdvFinalRoot rootView = leaf;
        AdvFinalMid midView = leaf;

        Assert.Equal(25, leaf.Mode());
        Assert.Equal(25, rootView.Mode());
        Assert.Equal(35, leaf.Value());
        Assert.Equal(35, midView.Value());
        Assert.Equal(25, E2EBindings.InvokeAdvFinalRootMode(leaf));
        Assert.Equal(35, E2EBindings.InvokeAdvFinalMidValue(leaf));
    }

    private sealed class ManagedAdvVirtualCounterCallingBase : AdvVirtualCounter
    {
        public ManagedAdvVirtualCounterCallingBase(int seed)
            : base(seed)
        {
        }

        public int GetValueCallbackCount { get; private set; }

        public int AddCallbackCount { get; private set; }

        public override int GetValue()
        {
            GetValueCallbackCount++;
            return 100 + base.GetValue();
        }

        public override int Add(int amount)
        {
            AddCallbackCount++;
            return 1000 + base.Add(amount);
        }
    }

    [Fact]
    public void Managed_virtual_overrides_can_delegate_to_base_implementation()
    {
        using var counter = new ManagedAdvVirtualCounterCallingBase(10);

        Assert.Equal(110, E2EBindings.InvokeAdvVirtualCounterGetValue(counter));
        Assert.Equal(1, counter.GetValueCallbackCount);

        Assert.Equal(1015, E2EBindings.InvokeAdvVirtualCounterAdd(counter, 5));
        Assert.Equal(1, counter.AddCallbackCount);

        Assert.Equal(115, E2EBindings.InvokeAdvVirtualCounterGetValue(counter));
        Assert.Equal(2, counter.GetValueCallbackCount);

        Assert.Equal(1020, E2EBindings.InvokeAdvVirtualCounterAdd(counter, 5));
        Assert.Equal(2, counter.AddCallbackCount);
    }
}
