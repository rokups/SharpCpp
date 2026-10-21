using System;
using System.Runtime.CompilerServices;

using SharpCpp.Support;

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

        public bool KeepCallbacks { get; set; } = true;

        public bool RetainBorrowed { get; set; }

        public Func<int, int>? Kept { get; private set; }

        public bool KeptIsOwned { get; private set; }

        public override int Sink(Func<int, int> callback)
        {
            Keep(callback);
            return callback is null ? -1 : callback(1);
        }

        public override int SinkRvalue(Func<int, int> callback)
        {
            Keep(callback);
            return callback(2);
        }

        public override int Borrow(Func<int, int> callback)
        {
            Keep(RetainBorrowed ? callback.Retain() : callback);
            return callback(3);
        }

        private void Keep(Func<int, int>? callback)
        {
            KeptIsOwned = callback.IsOwnedNativeCallback();
            Kept = KeepCallbacks ? callback : null;
        }
    }

    // The native counters are global, so the tests that read them do not run in parallel. xunit
    // runs the tests of one class one after the other.
    private static ManagedVisitor CreateCountingVisitor()
    {
        E2EBindings.DncResetCounts();
        return new ManagedVisitor();
    }

    [Fact]
    public void Sink_parameter_kept_by_the_override_works_after_the_native_call_returns()
    {
        using var visitor = CreateCountingVisitor();

        Assert.Equal(3, E2EBindings.DncCallSink(visitor, 3));
        Assert.True(visitor.KeptIsOwned);

        // The source and the parameter are destroyed. They are moved-from, so only the holder is live.
        Assert.Equal(0, E2EBindings.DncCopyCount());
        Assert.Equal(1, E2EBindings.DncLiveCount());
        Assert.Equal(0, E2EBindings.DncDestroyedCount());

        var kept = Assert.IsType<Func<int, int>>(visitor.Kept);
        Assert.Equal(15, kept(5));

        // A managed delegate over the native holder can go back to native code.
        Assert.Equal(21, E2EBindings.DncInvoke(kept, 7));

        kept.Dispose();
        Assert.Equal(0, E2EBindings.DncLiveCount());
        Assert.Equal(1, E2EBindings.DncDestroyedCount());
        Assert.Throws<ObjectDisposedException>(() => kept(5));

        // A second release does not free the holder again.
        kept.Dispose();
        Assert.Equal(1, E2EBindings.DncDestroyedCount());
    }

    [Fact]
    public void Rvalue_sink_parameter_moves_the_source_into_the_holder()
    {
        using var visitor = CreateCountingVisitor();

        Assert.Equal(8, E2EBindings.DncCallSinkRvalue(visitor, 4));
        Assert.True(visitor.KeptIsOwned);
        Assert.Equal(0, E2EBindings.DncCopyCount());
        Assert.Equal(1, E2EBindings.DncLiveCount());

        var kept = Assert.IsType<Func<int, int>>(visitor.Kept);
        Assert.Equal(40, kept(10));

        kept.Dispose();
        Assert.Equal(0, E2EBindings.DncLiveCount());
        Assert.Equal(1, E2EBindings.DncDestroyedCount());
    }

    [Fact]
    public void Sink_parameter_that_the_override_does_not_keep_is_released_by_the_finalizer()
    {
        using var visitor = CreateCountingVisitor();
        visitor.KeepCallbacks = false;

        CallSinkAndDropTheDelegate(visitor);
        for (var attempt = 0; attempt < 10 && E2EBindings.DncLiveCount() != 0; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.Equal(0, E2EBindings.DncLiveCount());
        Assert.Equal(1, E2EBindings.DncDestroyedCount());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CallSinkAndDropTheDelegate(ManagedVisitor visitor)
    {
        Assert.Equal(2, E2EBindings.DncCallSink(visitor, 2));
        Assert.True(visitor.KeptIsOwned);
        Assert.Equal(1, E2EBindings.DncLiveCount());
    }

    [Fact]
    public void Empty_sink_parameter_gives_null()
    {
        using var visitor = CreateCountingVisitor();

        Assert.Equal(-1, E2EBindings.DncCallSinkEmpty(visitor));
        Assert.Null(visitor.Kept);
    }

    [Fact]
    public void Const_reference_without_retain_does_not_copy_the_function_object()
    {
        using var visitor = CreateCountingVisitor();

        Assert.Equal(12, E2EBindings.DncCallBorrow(visitor, 4));
        Assert.False(visitor.KeptIsOwned);

        // The caller destroys its function object. No copy exists.
        Assert.Equal(0, E2EBindings.DncCopyCount());
        Assert.Equal(0, E2EBindings.DncLiveCount());
        Assert.Equal(1, E2EBindings.DncDestroyedCount());

        var kept = Assert.IsType<Func<int, int>>(visitor.Kept);
        Assert.Throws<ObjectDisposedException>(() => kept(1));
        Assert.Throws<ObjectDisposedException>(() => kept.Retain());
    }

    [Fact]
    public void Retain_on_a_const_reference_copies_the_function_object_for_a_call_after_return()
    {
        using var visitor = CreateCountingVisitor();
        visitor.RetainBorrowed = true;

        Assert.Equal(15, E2EBindings.DncCallBorrow(visitor, 5));
        Assert.True(visitor.KeptIsOwned);

        // Retain made the only copy. The caller destroyed its function object.
        Assert.Equal(1, E2EBindings.DncCopyCount());
        Assert.Equal(1, E2EBindings.DncLiveCount());
        Assert.Equal(1, E2EBindings.DncDestroyedCount());

        var kept = Assert.IsType<Func<int, int>>(visitor.Kept);
        Assert.Equal(30, kept(6));

        // Retain on an owned delegate shares the holder. The last release frees it.
        var second = kept.Retain();
        Assert.NotSame(kept, second);
        kept.Dispose();
        Assert.Equal(1, E2EBindings.DncLiveCount());
        Assert.Equal(35, second(7));
        second.Dispose();
        Assert.Equal(0, E2EBindings.DncLiveCount());
        Assert.Equal(2, E2EBindings.DncDestroyedCount());
        Assert.Throws<ObjectDisposedException>(() => second(1));
    }

    [Fact]
    public void Borrowed_native_callback_can_be_called_from_another_thread_during_the_override_call()
    {
        using var visitor = new ThreadedVisitor();

        Assert.Equal(18, E2EBindings.DncCallCombine(visitor, 3));
    }

    private sealed partial class ThreadedVisitor : DncVisitor
    {
        public override int Combine(Func<int, int, int> callback) =>
            System.Threading.Tasks.Task.Run(() => callback(2, 4)).GetAwaiter().GetResult();
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
        Assert.Equal(7, E2EBindings.DncCallSink(visitor, 7));
        Assert.Equal(14, E2EBindings.DncCallSinkRvalue(visitor, 7));
        Assert.Equal(21, E2EBindings.DncCallBorrow(visitor, 7));
    }
}
