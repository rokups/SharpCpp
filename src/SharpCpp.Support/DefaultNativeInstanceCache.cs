using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace SharpCpp.Support;

public sealed class DefaultNativeInstanceCache : INativeInstanceCache
{
    private readonly ConcurrentDictionary<nint, WeakReference<NativeObject>> _instances = new();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetExisting(nint handle, bool ownsHandle, out NativeObject? instance)
    {
        if (handle == IntPtr.Zero || !_instances.TryGetValue(handle, out var reference))
        {
            instance = null;
            return false;
        }

        if (reference.TryGetTarget(out instance) &&
            !instance.IsClosed &&
            (!ownsHandle || instance.OwnsNativeHandle))
        {
            return true;
        }

        return RemoveStaleEntry(handle, out instance);
    }

    public void Register(NativeObject instance)
    {
        if (instance.IsClosed || instance.IsInvalid)
        {
            return;
        }

        var handle = instance.DangerousGetHandle();

        if (_instances.TryGetValue(handle, out var existingReference) &&
            existingReference.TryGetTarget(out var existing) &&
            !existing.IsClosed &&
            !existing.IsInvalid &&
            existing.OwnsNativeHandle &&
            !instance.OwnsNativeHandle &&
            !ReferenceEquals(existing, instance))
        {
            return;
        }

        _instances[handle] = new WeakReference<NativeObject>(instance);
    }

    public void Unregister(NativeObject instance)
    {
        if (instance.IsClosed || instance.IsInvalid)
        {
            return;
        }

        var handle = instance.DangerousGetHandle();

        if (!_instances.TryGetValue(handle, out var reference))
        {
            return;
        }

        if (!reference.TryGetTarget(out var existing) || ReferenceEquals(existing, instance))
        {
            _instances.TryRemove(handle, out _);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool RemoveStaleEntry(nint handle, out NativeObject? instance)
    {
        _instances.TryRemove(handle, out _);
        instance = null;
        return false;
    }
}
