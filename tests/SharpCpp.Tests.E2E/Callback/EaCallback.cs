using System;

using SharpCpp.Support;
using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Callback;

// The ea::function forms of the std::function callback tests.
public sealed partial class EaCallbackTests
{
    [Fact]
    public void Void_and_value_callbacks_round_trip()
    {
        var seen = 0;
        E2EBindings.EaInvokeVoidCallback(value => seen = value, 42);

        Assert.Equal(42, seen);
        Assert.Equal(42, E2EBindings.EaInvokeIntCallback((lhs, rhs) => lhs + rhs, 40, 2));
        Assert.Equal(42, E2EBindings.EaInvokeCopiedCallback(value => value * 2, 21));
        Assert.True(E2EBindings.EaInvokeRvalueCallback(value => value > 0, 1));
    }

    [Fact]
    public void Retained_callback_lives_until_native_code_clears_it()
    {
        E2EBindings.EaRetainCallback(value => value + 1);
        try
        {
            Assert.Equal(42, E2EBindings.EaInvokeRetainedCallback(41));
        }
        finally
        {
            E2EBindings.EaClearRetainedCallback();
        }
    }

    [Fact]
    public void Object_reference_and_string_parameters_cross()
    {
        Assert.Equal(42, E2EBindings.EaInvokeObjectPointerCallback(box => box.SetValue(42), 1));
        Assert.Equal(42, E2EBindings.EaInvokePrimitiveReferenceCallback((ref int value) => value += 41, 1));
        Assert.Equal(42, E2EBindings.EaInvokeStringReferenceCallback(value => value.IsInvalid ? 0 : 42));
        Assert.Equal(42, E2EBindings.EaInvokeGeneratedDelegateCallback(value => value + 1, 41));
    }

    [Fact]
    public void Alias_callback_with_a_return_value_crosses()
    {
        Assert.Equal(42, E2EBindings.EaApplyTransform(value => value * 2, 21));
        Assert.Equal(44, E2EBindings.EaApplyTransformTwice(value => value * 2, 11));
        Assert.Equal(42, E2EBindings.EaInvokeThroughManagedCallback(value => value + 2, 40));
    }

    [Fact]
    public void Virtual_method_with_a_ref_callback_alias_calls_the_callback()
    {
        using var agent = new EaAgent();
        agent.SetPositionCallback((int index, ref float position) => position += index + 0.5f);

        Assert.Equal(3.5f, agent.Apply(3, 0f));
    }

    private sealed partial class ManagedVisitor : EaVisitor
    {
        public float MovedPosition { get; private set; }

        public Func<int, int, int>? KeptCombine { get; private set; }

        public Func<int, int>? KeptSink { get; private set; }

        public override void Move(E2EBindings.EaVisitor_Move_callbackCallback callback)
        {
            var position = 1.5f;
            callback(3, ref position);
            MovedPosition = position;
        }

        public override int Combine(Func<int, int, int> callback)
        {
            KeptCombine = callback;
            return callback is null ? -1 : callback(2, 3) + 1;
        }

        public override int Sink(Func<int, int> callback)
        {
            KeptSink = callback;
            return callback(1);
        }

        public override int SinkRvalue(Func<int, int> callback)
        {
            KeptSink = callback;
            return callback(2);
        }
    }

    [Fact]
    public void Director_gives_a_borrowed_ea_function_to_the_override()
    {
        using var visitor = new ManagedVisitor();

        Assert.Equal(7.5f, E2EBindings.EaCallMove(visitor, 2));
        Assert.Equal(7.5f, visitor.MovedPosition);
        Assert.Equal(26, E2EBindings.EaCallCombine(visitor, 5));
        Assert.False(visitor.KeptCombine.IsOwnedNativeCallback());
        Assert.Throws<ObjectDisposedException>(() => visitor.KeptCombine!(2, 3));
        Assert.Equal(-1, E2EBindings.EaCallCombineEmpty(visitor));
    }

    [Fact]
    public void Director_gives_an_owned_ea_function_for_a_sink_parameter()
    {
        using var visitor = new ManagedVisitor();

        Assert.Equal(3, E2EBindings.EaCallSink(visitor, 3));
        var kept = Assert.IsType<Func<int, int>>(visitor.KeptSink);
        Assert.True(kept.IsOwnedNativeCallback());
        Assert.Equal(30, kept(10));
        kept.Dispose();
        Assert.Throws<ObjectDisposedException>(() => kept(10));

        Assert.Equal(8, E2EBindings.EaCallSinkRvalue(visitor, 4));
        kept = Assert.IsType<Func<int, int>>(visitor.KeptSink);
        Assert.Equal(40, kept(10));
        kept.Dispose();
    }

    [Fact]
    public void Native_base_implementation_calls_the_ea_function()
    {
        using var visitor = new EaVisitor();

        Assert.Equal(15, E2EBindings.EaCallCombine(visitor, 5));
        Assert.Equal(3.0f, E2EBindings.EaCallMove(visitor, 2));
        Assert.Equal(7, E2EBindings.EaCallSink(visitor, 7));
        Assert.Equal(14, E2EBindings.EaCallSinkRvalue(visitor, 7));
        Assert.Equal(20, E2EBindings.EaCallVisit(visitor));
    }

    [Fact]
    public void Unsupported_ea_function_shapes_are_not_bound()
    {
        Assert.Null(typeof(EaVisitor).GetMethod("Visit"));
        Assert.Null(typeof(E2EBindings).GetMethod("EaGetCallback"));
        Assert.Null(typeof(EaCallbackOwner).GetProperty("Callback"));
        Assert.NotNull(typeof(EaCallbackOwner).GetProperty("Value"));
        Assert.Null(typeof(E2EBindings).GetMethod("EaReplaceCallback"));
    }
}
