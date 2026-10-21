using System;

using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.VirtualAdvanced;

public sealed partial class DirectorNativeCallbackTests
{
    private sealed partial class ManagedVisitor : DncVisitor
    {
        public float MovedPosition { get; private set; }

        public Func<int, int, int>? KeptCombine { get; private set; }

        public bool ReceivedNull { get; private set; }

        public override void Move(E2EBindings.DncVisitor_Move_callbackCallback callback)
        {
            var position = 1.5f;
            callback(3, ref position);
            MovedPosition = position;
        }

        public override int Combine(Func<int, int, int> callback)
        {
            KeptCombine = callback;
            ReceivedNull = callback is null;
            return callback is null ? -1 : callback(2, 3) + 1;
        }

        public override double Scale(Func<double, double> callback) => callback(2.5);

        public override DncMode Choose(Func<DncMode, DncMode> callback) => callback(DncMode.Second);
    }

    [Fact]
    public void Override_writes_a_ref_parameter_back_through_the_native_callback()
    {
        using var visitor = new ManagedVisitor();

        // The native callback adds 3 * 2 to 1.5. The override and the native code see the same value.
        Assert.Equal(7.5f, E2EBindings.DncCallMove(visitor, 2));
        Assert.Equal(7.5f, visitor.MovedPosition);
    }

    [Fact]
    public void Override_gets_the_return_value_of_the_native_callback()
    {
        using var visitor = new ManagedVisitor();

        Assert.Equal(26, E2EBindings.DncCallCombine(visitor, 5));
        Assert.Equal(10.0, E2EBindings.DncCallScale(visitor));
        Assert.Equal(DncMode.First, E2EBindings.DncCallChoose(visitor));
    }

    [Fact]
    public void Override_gets_null_for_an_empty_native_function_object()
    {
        using var visitor = new ManagedVisitor();

        Assert.Equal(-1, E2EBindings.DncCallCombineEmpty(visitor));
        Assert.True(visitor.ReceivedNull);
    }

    [Fact]
    public void Native_callback_kept_after_the_override_returns_throws()
    {
        using var visitor = new ManagedVisitor();

        Assert.Equal(26, E2EBindings.DncCallCombine(visitor, 5));
        var kept = Assert.IsType<Func<int, int, int>>(visitor.KeptCombine);
        Assert.Throws<ObjectDisposedException>(() => kept(2, 3));
    }

    [Fact]
    public void Native_base_implementation_calls_the_native_callback()
    {
        using var visitor = new DncVisitor();

        Assert.Equal(15, E2EBindings.DncCallCombine(visitor, 5));
        Assert.Equal(2.0, E2EBindings.DncCallScale(visitor));
        Assert.Equal(3.0f, E2EBindings.DncCallMove(visitor, 2));
        Assert.Equal(DncMode.Second, E2EBindings.DncCallChoose(visitor));
    }
}
