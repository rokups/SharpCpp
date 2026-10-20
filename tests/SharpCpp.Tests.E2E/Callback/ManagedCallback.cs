using System.Runtime.CompilerServices;

using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Callback;

// The tests share one native slot, so they must not run in parallel with each other.
[Collection(nameof(ManagedCallbackTests))]
public sealed class ManagedCallbackTests
{
    [Fact]
    public void FitsFixedFunction32()
    {
        var offset = 7;
        Assert.Equal(12, E2EBindings.InvokeThroughFixedFunction(value => value + offset, 5));
    }

    [Fact]
    public void NoexceptSignatureIsBound()
    {
        var offset = 2;
        Assert.Equal(9, E2EBindings.InvokeNoexceptCallback(value => value + offset, 7));
    }

    [Fact]
    public void ReleasesStateWhenSlotDestroyed()
    {
        var weak = Connect(3);

        Collect();
        Assert.True(weak.IsAlive);
        Assert.Equal(8, E2EBindings.EmitSlot(5));

        E2EBindings.DisconnectSlot();
        Collect();
        Assert.False(weak.IsAlive);
        Assert.Equal(-1, E2EBindings.EmitSlot(5));
    }

    // A separate frame keeps the JIT from extending the delegate lifetime to the test method.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference Connect(int offset)
    {
        // The capture makes a new delegate instance and not a cached static one.
        Func<int, int> callback = value => value + offset;
        E2EBindings.ConnectSlot(callback);
        return new WeakReference(callback);
    }

    private static void Collect()
    {
        for (var index = 0; index < 3; ++index)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }
}

public sealed class CallbackRefParameterTests
{
    [Fact]
    public void WritesBackBool()
    {
        var received = false;
        var result = E2EBindings.InvokeBoolRefCallback((ref bool value) =>
        {
            received = value;
            value = !value;
        }, true);

        Assert.True(received);
        Assert.False(result);
    }

    [Fact]
    public void WritesBackStruct()
    {
        CallbackPoint2 received = default;
        var result = E2EBindings.InvokePointRefCallback((ref CallbackPoint2 value) =>
        {
            received = value;
            value = new CallbackPoint2(value.X + 10, value.Y + 20);
        }, 1, 2);

        Assert.Equal(new CallbackPoint2(1, 2), received);
        Assert.Equal(11022, result);
    }

    [Fact]
    public void WritesBackIntWithResult()
    {
        var result = E2EBindings.InvokeIntRefCallback((ref int value) =>
        {
            value += 4;
            return 7;
        }, 3);

        Assert.Equal(707, result);
    }
}
