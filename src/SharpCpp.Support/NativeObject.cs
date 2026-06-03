using System.Reflection;
using System.Runtime.InteropServices;

namespace SharpCpp.Support;

public abstract class NativeObject : SafeHandle
{
    protected NativeObject(nint nativeHandle, bool ownsHandle)
        : base(IntPtr.Zero, ownsHandle)
    {
        OwnsNativeHandle = ownsHandle;
        SetHandle(nativeHandle);
    }

    public override bool IsInvalid => handle == IntPtr.Zero;

    public bool OwnsNativeHandle { get; }

    protected virtual bool ManagesInstanceCacheLifecycle => false;

    protected virtual INativeInstanceCache? InstanceCache => null;

    private GCHandle _managedKeepAlive;
    private nint _managedKeepAliveKey;

    protected static bool HasManagedOverride(Type runtimeType, Type declaringType, string methodName, Type[] parameterTypes)
    {
        var method = runtimeType.GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: parameterTypes,
            modifiers: null);

        if (method is null)
        {
            return false;
        }

        var declaringMethod = declaringType.GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: parameterTypes,
            modifiers: null);

        if (declaringMethod is null)
        {
            return false;
        }

        return method.DeclaringType is not null
            && method.DeclaringType != declaringType
            && method.GetBaseDefinition() == declaringMethod.GetBaseDefinition();
    }

    protected sealed override bool ReleaseHandle()
    {
        var instanceCache = InstanceCache;

        if (handle == IntPtr.Zero)
        {
            SetHandleAsInvalid();
            return true;
        }

        var released = ReleaseNativeHandle(handle);

        if (released)
        {
            if (!(ManagesInstanceCacheLifecycle && OwnsNativeHandle))
            {
                instanceCache?.Unregister(this);
            }

            ClearManagedKeepAlive();
            SetHandleAsInvalid();
        }

        return released;
    }

    protected override void Dispose(bool disposing)
    {
        var instanceCache = InstanceCache;

        if (!(ManagesInstanceCacheLifecycle && OwnsNativeHandle) && !IsClosed && !IsInvalid)
        {
            instanceCache?.Unregister(this);
        }

        base.Dispose(disposing);
        ClearManagedKeepAlive();
    }

    protected virtual bool ReleaseNativeHandle(nint handle)
    {
        return true;
    }

    protected void DisposeFromNativeRuntimeCore()
    {
        var instanceCache = InstanceCache;

        if (!IsClosed && !IsInvalid)
        {
            instanceCache?.Unregister(this);
        }

        if (!IsClosed)
        {
            SetHandleAsInvalid();
        }

        ClearManagedKeepAlive();
        GC.SuppressFinalize(this);
    }

    protected void SetManagedKeepAliveCore(IDisposable keepAlive, nint key)
    {
        ArgumentNullException.ThrowIfNull(keepAlive);

        if (_managedKeepAlive.IsAllocated && _managedKeepAliveKey == key)
        {
            keepAlive.Dispose();
            return;
        }

        ClearManagedKeepAlive();
        _managedKeepAlive = GCHandle.Alloc(keepAlive);
        _managedKeepAliveKey = key;
    }

    private void ClearManagedKeepAlive()
    {
        if (!_managedKeepAlive.IsAllocated)
        {
            _managedKeepAliveKey = 0;
            return;
        }

        if (_managedKeepAlive.Target is IDisposable keepAlive)
        {
            keepAlive.Dispose();
        }

        _managedKeepAlive.Free();
        _managedKeepAliveKey = 0;
    }

    public static NativeObject? GetObject(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is not NativeObject native)
        {
            throw new ArgumentException("Expected a SharpCpp-generated native wrapper.", nameof(value));
        }

        return native;
    }

    public static NativeObject RequireObject(object value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);

        if (value is not NativeObject native)
        {
            throw new ArgumentException("Expected a SharpCpp-generated native wrapper.", parameterName);
        }

        return native;
    }

    public static nint GetHandle(object? value)
    {
        var native = GetObject(value);

        if (native is null)
        {
            return 0;
        }

        if (native.IsClosed || native.IsInvalid)
        {
            throw new ObjectDisposedException(native.GetType().FullName);
        }

        return native.DangerousGetHandle();
    }

    public static nint RequireHandle(object value, string parameterName)
    {
        var native = RequireObject(value, parameterName);

        if (native.IsClosed || native.IsInvalid)
        {
            throw new ObjectDisposedException(native.GetType().FullName);
        }

        return native.DangerousGetHandle();
    }
}
