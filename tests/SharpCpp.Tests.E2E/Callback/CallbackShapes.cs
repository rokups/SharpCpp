using System.Runtime.InteropServices;

using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Callback;

[StructLayout(LayoutKind.Sequential)]
public record struct CallbackPoint2(int X, int Y);

public sealed class CallbackShapeTests
{
    [Fact]
    public void Void_callbacks_with_and_without_arguments_are_actions()
    {
        var calls = new List<(int, float)>();
        Assert.Equal(2f, E2EBindings.ForEach((index, value) => calls.Add((index, value))));
        Assert.Equal([(1, 0.5f), (2, 1.5f)], calls);

        var notified = 0;
        Assert.Equal(1, E2EBindings.Notify(() => ++notified));
        Assert.Equal(1, notified);
    }

    [Fact]
    public void Null_defaulted_callbacks_default_to_null()
    {
        Assert.Equal(99u, E2EBindings.InvokeOptional());
        Assert.Equal(99u, E2EBindings.InvokeOptional(null));
        Assert.Equal(5u, E2EBindings.InvokeOptional(() => 5u));
    }

    [Fact]
    public void Configured_value_struct_callback_arguments_cross_by_value()
    {
        Assert.Equal(34, E2EBindings.UsePointCallback(static point => point.X * 10 + point.Y));
        Assert.Equal(56, E2EBindings.UsePointValueCallback(static point => point.X * 10 + point.Y));
    }

    [Fact]
    public void Configured_value_parameters_and_returns_cross_outside_callbacks()
    {
        Assert.Equal(7, E2EBindings.PointSum(new CallbackPoint2(3, 4)));
        Assert.Equal(new CallbackPoint2(5, 6), E2EBindings.MakePoint(5, 6));
    }
}
