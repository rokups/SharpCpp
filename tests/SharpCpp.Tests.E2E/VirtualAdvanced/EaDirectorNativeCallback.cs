using System;
using System.Runtime.CompilerServices;

using SharpCpp.Support;

using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.VirtualAdvanced;

// The ea::function forms of the director tests (DirectorNativeCallback.cs). They use the same
// native counters, so they are part of the same class and run one after the other.
public sealed partial class DirectorNativeCallbackTests
{
    private sealed partial class ManagedEaVisitor : EaDncVisitor
    {
        public float MovedPosition { get; private set; }

        public Func<int, int, int>? KeptCombine { get; private set; }

        public bool ReceivedNull { get; private set; }

        public override void Move(E2EBindings.EaDncVisitor_Move_callbackCallback callback)
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

        public override int BorrowNoexcept(Func<int, int> callback)
        {
            Keep(callback);
            return callback(4) + 1;
        }

        public override int SinkNoexcept(Func<int, int> callback)
        {
            Keep(callback);
            return callback(5) + 1;
        }

        private void Keep(Func<int, int>? callback)
        {
            KeptIsOwned = callback.IsOwnedNativeCallback();
            Kept = KeepCallbacks ? callback : null;
        }
    }

    private static ManagedEaVisitor CreateCountingEaVisitor()
    {
        E2EBindings.DncResetCounts();
        return new ManagedEaVisitor();
    }

    [Fact]
    public void Ea_sink_parameter_kept_by_the_override_works_after_the_native_call_returns()
    {
        using var visitor = CreateCountingEaVisitor();

        Assert.Equal(3, E2EBindings.EaDncCallSink(visitor, 3));
        Assert.True(visitor.KeptIsOwned);
        Assert.Equal(0, E2EBindings.DncCopyCount());
        Assert.Equal(1, E2EBindings.DncLiveCount());
        Assert.Equal(0, E2EBindings.DncDestroyedCount());

        var kept = Assert.IsType<Func<int, int>>(visitor.Kept);
        Assert.Equal(15, kept(5));
        Assert.Equal(21, E2EBindings.EaDncInvoke(kept, 7));

        kept.Dispose();
        Assert.Equal(0, E2EBindings.DncLiveCount());
        Assert.Equal(1, E2EBindings.DncDestroyedCount());
        Assert.Throws<ObjectDisposedException>(() => kept(5));

        kept.Dispose();
        Assert.Equal(1, E2EBindings.DncDestroyedCount());
    }

    [Fact]
    public void Ea_rvalue_sink_parameter_moves_the_source_into_the_holder()
    {
        using var visitor = CreateCountingEaVisitor();

        Assert.Equal(8, E2EBindings.EaDncCallSinkRvalue(visitor, 4));
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
    public void Ea_sink_parameter_that_the_override_does_not_keep_is_released_by_the_finalizer()
    {
        using var visitor = CreateCountingEaVisitor();
        visitor.KeepCallbacks = false;

        CallEaSinkAndDropTheDelegate(visitor);
        for (var attempt = 0; attempt < 10 && E2EBindings.DncLiveCount() != 0; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.Equal(0, E2EBindings.DncLiveCount());
        Assert.Equal(1, E2EBindings.DncDestroyedCount());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CallEaSinkAndDropTheDelegate(ManagedEaVisitor visitor)
    {
        Assert.Equal(2, E2EBindings.EaDncCallSink(visitor, 2));
        Assert.True(visitor.KeptIsOwned);
        Assert.Equal(1, E2EBindings.DncLiveCount());
    }

    [Fact]
    public void Ea_empty_sink_parameter_gives_null()
    {
        using var visitor = CreateCountingEaVisitor();

        Assert.Equal(-1, E2EBindings.EaDncCallSinkEmpty(visitor));
        Assert.Null(visitor.Kept);
    }

    [Fact]
    public void Ea_const_reference_without_retain_does_not_copy_the_function_object()
    {
        using var visitor = CreateCountingEaVisitor();

        Assert.Equal(12, E2EBindings.EaDncCallBorrow(visitor, 4));
        Assert.False(visitor.KeptIsOwned);
        Assert.Equal(0, E2EBindings.DncCopyCount());
        Assert.Equal(0, E2EBindings.DncLiveCount());
        Assert.Equal(1, E2EBindings.DncDestroyedCount());

        var kept = Assert.IsType<Func<int, int>>(visitor.Kept);
        Assert.Throws<ObjectDisposedException>(() => kept(1));
        Assert.Throws<ObjectDisposedException>(() => kept.Retain());
    }

    [Fact]
    public void Ea_retain_on_a_const_reference_copies_the_function_object_for_a_call_after_return()
    {
        using var visitor = CreateCountingEaVisitor();
        visitor.RetainBorrowed = true;

        Assert.Equal(15, E2EBindings.EaDncCallBorrow(visitor, 5));
        Assert.True(visitor.KeptIsOwned);
        Assert.Equal(1, E2EBindings.DncCopyCount());
        Assert.Equal(1, E2EBindings.DncLiveCount());
        Assert.Equal(1, E2EBindings.DncDestroyedCount());

        var kept = Assert.IsType<Func<int, int>>(visitor.Kept);
        Assert.Equal(30, kept(6));

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

    private sealed partial class ThreadedEaVisitor : EaDncVisitor
    {
        public override int Combine(Func<int, int, int> callback) =>
            System.Threading.Tasks.Task.Run(() => callback(2, 4)).GetAwaiter().GetResult();
    }

    [Fact]
    public void Ea_borrowed_native_callback_can_be_called_from_another_thread_during_the_override_call()
    {
        using var visitor = new ThreadedEaVisitor();

        Assert.Equal(18, E2EBindings.EaDncCallCombine(visitor, 3));
    }

    [Fact]
    public void Ea_override_writes_a_ref_parameter_back_and_gets_return_values()
    {
        using var visitor = new ManagedEaVisitor();

        Assert.Equal(7.5f, E2EBindings.EaDncCallMove(visitor, 2));
        Assert.Equal(7.5f, visitor.MovedPosition);
        Assert.Equal(26, E2EBindings.EaDncCallCombine(visitor, 5));
        Assert.Equal(10.0, E2EBindings.EaDncCallScale(visitor));
        Assert.Equal(DncMode.First, E2EBindings.EaDncCallChoose(visitor));
    }

    [Fact]
    public void Ea_override_gets_null_for_an_empty_function_object()
    {
        using var visitor = new ManagedEaVisitor();

        Assert.Equal(-1, E2EBindings.EaDncCallCombineEmpty(visitor));
        Assert.True(visitor.ReceivedNull);
    }

    [Fact]
    public void Ea_native_callback_kept_after_the_override_returns_throws()
    {
        using var visitor = new ManagedEaVisitor();

        Assert.Equal(26, E2EBindings.EaDncCallCombine(visitor, 5));
        var kept = Assert.IsType<Func<int, int, int>>(visitor.KeptCombine);
        Assert.False(kept.IsOwnedNativeCallback());
        Assert.Throws<ObjectDisposedException>(() => kept(2, 3));
    }

    [Fact]
    public void Ea_noexcept_signature_reaches_the_override_as_borrowed_and_owned_delegates()
    {
        using var visitor = CreateCountingEaVisitor();

        Assert.Equal(13, E2EBindings.EaDncCallBorrowNoexcept(visitor, 3));
        Assert.False(visitor.KeptIsOwned);
        var borrowed = Assert.IsType<Func<int, int>>(visitor.Kept);
        Assert.Throws<ObjectDisposedException>(() => borrowed(1));

        Assert.Equal(31, E2EBindings.EaDncCallSinkNoexcept(visitor, 6));
        Assert.True(visitor.KeptIsOwned);
        var owned = Assert.IsType<Func<int, int>>(visitor.Kept);
        Assert.Equal(12, owned(2));
        owned.Dispose();
        Assert.Throws<ObjectDisposedException>(() => owned(2));
    }

    [Fact]
    public void Ea_native_base_implementation_calls_the_native_callback()
    {
        using var visitor = new EaDncVisitor();

        Assert.Equal(15, E2EBindings.EaDncCallCombine(visitor, 5));
        Assert.Equal(2.0, E2EBindings.EaDncCallScale(visitor));
        Assert.Equal(3.0f, E2EBindings.EaDncCallMove(visitor, 2));
        Assert.Equal(DncMode.Second, E2EBindings.EaDncCallChoose(visitor));
        Assert.Equal(7, E2EBindings.EaDncCallSink(visitor, 7));
        Assert.Equal(14, E2EBindings.EaDncCallSinkRvalue(visitor, 7));
        Assert.Equal(21, E2EBindings.EaDncCallBorrow(visitor, 7));
        Assert.Equal(28, E2EBindings.EaDncCallBorrowNoexcept(visitor, 7));
        Assert.Equal(35, E2EBindings.EaDncCallSinkNoexcept(visitor, 7));
    }
}
