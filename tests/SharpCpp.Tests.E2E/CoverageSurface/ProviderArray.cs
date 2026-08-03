using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Std.Infrastructure;

namespace E2EStd;

public sealed class ProviderArray<T> : global::SharpCpp.Support.SharedArrayBase<T>
{
    private static readonly ConditionalWeakTable<object, global::SharpCpp.Support.DefaultNativeInstanceCache> s_instanceCaches = new();
    private readonly IDynamicArrayProvider<T> _provider;
    private readonly global::SharpCpp.Support.DefaultNativeInstanceCache _instanceCache;

    public ProviderArray(int length)
        : this(ProviderRegistry.Resolve<IDynamicArrayProvider<T>>(), length)
    {
    }

    public ProviderArray(ReadOnlySpan<T> values)
        : this(values.Length)
    {
        Assign(values);
    }

    public ProviderArray(IEnumerable<T> values)
        : this(Materialize(values))
    {
    }

    private ProviderArray(IDynamicArrayProvider<T> provider, int length)
        : this(provider.Create(length), length, ownsHandle: true, provider)
    {
    }

    private ProviderArray(nint handle, int? length, bool ownsHandle, IDynamicArrayProvider<T> provider)
        : base(handle, length, ownsHandle)
    {
        _provider = provider;
        _instanceCache = s_instanceCaches.GetValue(provider, static _ => new global::SharpCpp.Support.DefaultNativeInstanceCache());
        if (!IsInvalid)
        {
            _instanceCache.Register(this);
        }
    }

    protected override global::SharpCpp.Support.INativeInstanceCache? InstanceCache => _instanceCache;

    public nint Handle => DangerousGetHandle();

    public bool OwnsHandle => OwnsNativeHandle;

    public bool IsNull => IsClosed || IsInvalid;

    public IDynamicArrayProvider<T> Provider => _provider;

    public static ProviderArray<T> CreateManagedWrapper(nint handle, bool ownsHandle)
        => CreateManagedWrapper(handle, ownsHandle, ProviderRegistry.Resolve<IDynamicArrayProvider<T>>());

    public static ProviderArray<T> CreateManagedWrapper(nint handle, bool ownsHandle, string? moduleId)
        => CreateManagedWrapper(handle, ownsHandle, ProviderRegistry.Resolve<IDynamicArrayProvider<T>>(moduleId));

    public static ProviderArray<T> CreateManagedWrapper(nint handle, bool ownsHandle, IDynamicArrayProvider<T> provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var cache = s_instanceCaches.GetValue(provider, static _ => new global::SharpCpp.Support.DefaultNativeInstanceCache());
        if (handle != 0 && cache.TryGetExisting(handle, ownsHandle, out var existing) && existing is ProviderArray<T> cached)
        {
            return cached;
        }

        return new ProviderArray<T>(handle, provider.GetLength(handle), ownsHandle, provider);
    }

    protected override T GetAtCore(nuint index) => _provider.GetAt(DangerousGetHandle(), index);

    protected override void SetAtCore(nuint index, T value) => _provider.SetAt(DangerousGetHandle(), index, value);

    protected override bool ReleaseNativeHandle(nint handle)
    {
        if (OwnsNativeHandle)
        {
            _provider.Delete(handle);
        }

        return true;
    }

    private static T[] Materialize(IEnumerable<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return global::System.Linq.Enumerable.ToArray(values);
    }
}
