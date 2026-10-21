using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace SharpCpp.Support;

/// <summary>
/// The layout of <c>SharpCpp::ManagedCallbackHandleAbi</c> in SharpCpp/Marshaller.hpp.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct NativeCallbackHandleAbi
{
    public nint State;
    public nint Callback;
    public nint Release;
    public nint Retain;
}

/// <summary>
/// The target of a managed delegate that calls a native function object. A director gives the
/// function object to a managed override in one of two forms:
/// <list type="bullet">
/// <item>Borrowed (a const reference parameter): the delegate is valid only during the override
/// call. After the call, the delegate throws <see cref="ObjectDisposedException"/>. No native
/// memory is allocated. <see cref="NativeCallback.Retain{TDelegate}"/> copies the function object
/// into an owned delegate, and is valid only during the override call.</item>
/// <item>Owned (a by-value or rvalue-reference parameter): the delegate owns a native holder of the
/// function object. The delegate is valid until <see cref="NativeCallback.Dispose"/> or the
/// finalizer releases the holder.</item>
/// </list>
/// Each call increments an in-use counter. A borrowed scope waits for the calls of other threads
/// before the override call returns, so no call uses the function object after the caller
/// destroys it. An owned holder is released after the last call returns.
/// <para>
/// Owned holder: when the override does not dispose the delegate, the .NET finalizer releases the
/// holder. Then the native destructor of the function object runs on the finalizer thread.
/// The destructor of the captured state must be safe on any thread.
/// </para>
/// <para>
/// Borrowed scope: the end of the override call waits for calls on other threads. The wait has no
/// time limit. If a call on another thread blocks until the director thread continues, the end of
/// the override call waits for ever. Do not block on the director thread while a borrowed call
/// runs on another thread.
/// </para>
/// </summary>
public abstract unsafe class NativeCallbackScope
{
    private const string ClosedMessage =
        "A native callback that a director gives to a managed override is valid only during the override call, or until it is disposed.";

    private readonly nint _retain;
    private readonly Releaser? _releaser;
    private nint _state;
    private int _users;

    protected NativeCallbackScope(nint state, nint invoke, nint release, nint retain)
    {
        _state = state;
        _retain = retain;
        InvokePointer = invoke;
        if (state != 0 && release != 0)
        {
            _releaser = new Releaser(state, release);
        }
    }

    /// <summary>The native invoker. Its first argument is the state that <see cref="Enter"/> gives.</summary>
    protected nint InvokePointer { get; }

    /// <summary>True when the scope owns a native holder of the function object.</summary>
    public bool IsOwned => _releaser is not null;

    /// <summary>
    /// Starts a call and gives the native state. Each <see cref="Enter"/> that returns must have
    /// one <see cref="Exit"/>.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The scope is closed.</exception>
    protected nint Enter()
    {
        // The increment is a full fence, so a close either sees this call or this call sees the close.
        Interlocked.Increment(ref _users);
        var state = Volatile.Read(ref _state);
        if (state == 0)
        {
            Exit();
            throw new ObjectDisposedException(nameof(NativeCallbackScope), ClosedMessage);
        }

        return state;
    }

    /// <summary>Ends a call. The last call after a close releases an owned holder.</summary>
    protected void Exit()
    {
        if (Interlocked.Decrement(ref _users) == 0 && Volatile.Read(ref _state) == 0)
        {
            _releaser?.Release();
        }
    }

    /// <summary>
    /// The generated director code calls this method after the override call. A borrowed scope
    /// closes and waits for the calls of other threads. An owned scope stays valid.
    /// </summary>
    public void EndCall()
    {
        if (_releaser is not null)
        {
            return;
        }

        Interlocked.Exchange(ref _state, 0);
        var spin = default(SpinWait);
        while (Volatile.Read(ref _users) != 0)
        {
            spin.SpinOnce();
        }
    }

    /// <summary>
    /// Closes the scope. An owned holder is released after the last call returns. A call after
    /// the close throws <see cref="ObjectDisposedException"/>.
    /// </summary>
    public void Close()
    {
        if (Interlocked.Exchange(ref _state, 0) != 0 && Volatile.Read(ref _users) == 0)
        {
            _releaser?.Release();
        }
    }

    /// <summary>
    /// Gives a new owned delegate. For a borrowed scope, the native function object is copied into
    /// a new holder.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The scope is closed.</exception>
    public Delegate Retain()
    {
        var state = Enter();
        try
        {
            if (_retain == 0)
            {
                throw new NotSupportedException("The native callback cannot be retained.");
            }

            NativeCallbackHandleAbi handle;
            ((delegate* unmanaged[Cdecl]<nint, NativeCallbackHandleAbi*, void>)_retain)(state, &handle);
            return CreateRetained(handle.State, handle.Callback, handle.Release, handle.Retain);
        }
        finally
        {
            Exit();
        }
    }

    /// <summary>Makes a delegate of the same type over an owned handle.</summary>
    protected abstract Delegate CreateRetained(nint state, nint invoke, nint release, nint retain);

    // Only an owned scope has a finalizer, so a borrowed scope (one for each override call) is not
    // registered for finalization. The scope holds the releaser, so the releaser is not finalized
    // while a call can use the holder.
    private sealed class Releaser
    {
        private readonly nint _release;
        private nint _state;

        public Releaser(nint state, nint release)
        {
            _state = state;
            _release = release;
        }

        ~Releaser()
        {
            ReleaseState();
        }

        public void Release()
        {
            if (ReleaseState())
            {
                GC.SuppressFinalize(this);
            }
        }

        private bool ReleaseState()
        {
            var state = Interlocked.Exchange(ref _state, 0);
            if (state == 0)
            {
                return false;
            }

            ((delegate* unmanaged[Cdecl]<nint, void>)_release)(state);
            return true;
        }
    }
}

/// <summary>
/// Operations on a delegate that a director gives to a managed override. See
/// <see cref="NativeCallbackScope"/>.
/// </summary>
public static class NativeCallback
{
    /// <summary>
    /// Gives a delegate that stays valid after the override call, until it is disposed or
    /// finalized. For a borrowed native function object, this method copies the function object
    /// and is valid only during the override call. A delegate that does not call a native function
    /// object is returned without a change.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The borrowed scope is closed.</exception>
    public static TDelegate Retain<TDelegate>(this TDelegate callback)
        where TDelegate : Delegate
    {
        ArgumentNullException.ThrowIfNull(callback);
        return callback.Target is NativeCallbackScope scope
            ? (TDelegate)scope.Retain()
            : callback;
    }

    /// <summary>
    /// Releases the native function object of an owned delegate after the last call returns. For
    /// a borrowed delegate, this method closes it before the override call ends. For a delegate
    /// that does not call a native function object, this method does nothing.
    /// </summary>
    public static void Dispose(this Delegate? callback)
    {
        if (callback?.Target is NativeCallbackScope scope)
        {
            scope.Close();
        }
    }

    /// <summary>True when the delegate owns a native holder of a function object.</summary>
    public static bool IsOwnedNativeCallback(this Delegate? callback) =>
        callback?.Target is NativeCallbackScope { IsOwned: true };
}
