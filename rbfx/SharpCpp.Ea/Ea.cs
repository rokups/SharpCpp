using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Ea.Infrastructure;

namespace Ea;

/// <summary>Shared managed wrapper for <c>eastl::basic_string&lt;char&gt;</c>.</summary>
public sealed class String : global::SharpCpp.Support.StringBase, ICloneable
{
    private static readonly ConditionalWeakTable<object, global::SharpCpp.Support.DefaultNativeInstanceCache> s_instanceCaches = new();
    private readonly IStringProvider _provider;
    private readonly global::SharpCpp.Support.DefaultNativeInstanceCache _instanceCache;

    public String() : this(ProviderRegistry.Resolve<IStringProvider>()) { }

    public String(string? value) : this(ProviderRegistry.Resolve<IStringProvider>()) => Assign(value);

    public String(IStringProvider provider) : this(provider.Create(), ownsHandle: true, provider) { }

    public String(string? value, IStringProvider provider) : this(provider) => Assign(value);

    public String(nint handle, bool ownsHandle, IStringProvider provider) : base(handle, ownsHandle)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _instanceCache = GetInstanceCache(_provider);
        if (!IsInvalid) _instanceCache.Register(this);
    }

    private static global::SharpCpp.Support.DefaultNativeInstanceCache GetInstanceCache(IStringProvider provider)
        => s_instanceCaches.GetValue(provider, static _ => new global::SharpCpp.Support.DefaultNativeInstanceCache());

    protected override global::SharpCpp.Support.INativeInstanceCache? InstanceCache => _instanceCache;

    public nint Handle => DangerousGetHandle();
    public bool OwnsHandle => OwnsNativeHandle;
    public bool IsNull => IsClosed || IsInvalid;
    public IStringProvider Provider => _provider;

    public String Clone() => CreateManagedWrapper(_provider.Clone(DangerousGetHandle()), ownsHandle: true, _provider);

    object ICloneable.Clone() => Clone();

    public static implicit operator String(string? value) => new(value);

    public static implicit operator ReadOnlySpan<byte>(String value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.AsUtf8Span();
    }

    public static String CreateManagedWrapper(nint handle, bool ownsHandle)
        => CreateManagedWrapper(handle, ownsHandle, ProviderRegistry.Resolve<IStringProvider>());

    public static String CreateManagedWrapper(nint handle, bool ownsHandle, string? moduleId)
        => CreateManagedWrapper(handle, ownsHandle, ProviderRegistry.Resolve<IStringProvider>(moduleId));

    public static String CreateManagedWrapper(nint handle, bool ownsHandle, IStringProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (handle != 0 && GetInstanceCache(provider).TryGetExisting(handle, ownsHandle, out var existing) && existing is String cached)
            return cached;
        return new String(handle, ownsHandle, provider);
    }

    protected override ulong GetCountCore() => _provider.GetCount(DangerousGetHandle());
    protected override nint GetDataCore() => _provider.GetData(DangerousGetHandle());
    protected override void ClearCore() => _provider.Clear(DangerousGetHandle());
    protected override byte GetAtCore(ulong index) => _provider.GetAt(DangerousGetHandle(), index);
    protected override void AddCore(byte value) => _provider.Add(DangerousGetHandle(), value);

    protected override bool ReleaseNativeHandle(nint handle)
    {
        if (OwnsNativeHandle) _provider.Delete(handle);
        return true;
    }
}

/// <summary>Shared managed wrapper for <c>eastl::vector&lt;T&gt;</c>.</summary>
public sealed class Vector<T> : global::SharpCpp.Support.VectorBase<T>, ICloneable
{
    private static readonly ConditionalWeakTable<object, global::SharpCpp.Support.DefaultNativeInstanceCache> s_instanceCaches = new();
    private readonly IVectorProvider<T> _provider;
    private readonly global::SharpCpp.Support.DefaultNativeInstanceCache _instanceCache;

    public Vector() : this(ProviderRegistry.Resolve<IVectorProvider<T>>()) { }

    public Vector(IEnumerable<T> values) : this()
    {
        ArgumentNullException.ThrowIfNull(values);
        foreach (var value in values) Add(value);
    }

    public Vector(ReadOnlySpan<T> values) : this()
    {
        foreach (var value in values) Add(value);
    }

    public Vector(IVectorProvider<T> provider) : this(provider.Create(), ownsHandle: true, provider) { }

    public Vector(nint handle, bool ownsHandle, IVectorProvider<T> provider) : base(handle, ownsHandle)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _instanceCache = GetInstanceCache(_provider);
        if (!IsInvalid) _instanceCache.Register(this);
    }

    private static global::SharpCpp.Support.DefaultNativeInstanceCache GetInstanceCache(IVectorProvider<T> provider)
        => s_instanceCaches.GetValue(provider, static _ => new global::SharpCpp.Support.DefaultNativeInstanceCache());

    protected override global::SharpCpp.Support.INativeInstanceCache? InstanceCache => _instanceCache;
    public nint Handle => DangerousGetHandle();
    public bool OwnsHandle => OwnsNativeHandle;
    public bool IsNull => IsClosed || IsInvalid;
    public IVectorProvider<T> Provider => _provider;

    public Vector<T> Clone() => CreateManagedWrapper(_provider.Clone(DangerousGetHandle()), ownsHandle: true, _provider);

    object ICloneable.Clone() => Clone();

    public static Vector<T> CreateManagedWrapper(nint handle, bool ownsHandle)
        => CreateManagedWrapper(handle, ownsHandle, ProviderRegistry.Resolve<IVectorProvider<T>>());

    public static Vector<T> CreateManagedWrapper(nint handle, bool ownsHandle, string? moduleId)
        => CreateManagedWrapper(handle, ownsHandle, ProviderRegistry.Resolve<IVectorProvider<T>>(moduleId));

    public static Vector<T> CreateManagedWrapper(nint handle, bool ownsHandle, IVectorProvider<T> provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (handle != 0 && GetInstanceCache(provider).TryGetExisting(handle, ownsHandle, out var existing) && existing is Vector<T> cached)
            return cached;
        return new Vector<T>(handle, ownsHandle, provider);
    }

    protected override ulong GetCountCore() => _provider.GetCount(DangerousGetHandle());
    protected override void ClearCore() => _provider.Clear(DangerousGetHandle());
    protected override T GetAtCore(ulong index) => _provider.GetAt(DangerousGetHandle(), index);
    protected override void SetAtCore(ulong index, T value) => _provider.SetAt(DangerousGetHandle(), index, value);
    protected override void AddCore(T value) => _provider.Add(DangerousGetHandle(), value);
    protected override void InsertAtCore(ulong index, T value) => _provider.InsertAt(DangerousGetHandle(), index, value);
    protected override void RemoveAtCore(ulong index) => _provider.RemoveAt(DangerousGetHandle(), index);

    protected override bool ReleaseNativeHandle(nint handle)
    {
        if (OwnsNativeHandle) _provider.Delete(handle);
        return true;
    }
}

/// <summary>Shared managed wrapper for <c>eastl::list&lt;T&gt;</c>.</summary>
public sealed class List<T> : global::SharpCpp.Support.ListBase<T>, ICloneable
{
    private static readonly ConditionalWeakTable<object, global::SharpCpp.Support.DefaultNativeInstanceCache> s_instanceCaches = new();
    private readonly IListProvider<T> _provider;
    private readonly global::SharpCpp.Support.DefaultNativeInstanceCache _instanceCache;

    public List() : this(ProviderRegistry.Resolve<IListProvider<T>>()) { }

    public List(IEnumerable<T> values) : this()
    {
        ArgumentNullException.ThrowIfNull(values);
        foreach (var value in values) Add(value);
    }

    public List(ReadOnlySpan<T> values) : this()
    {
        foreach (var value in values) Add(value);
    }

    public List(IListProvider<T> provider) : this(provider.Create(), ownsHandle: true, provider) { }

    public List(nint handle, bool ownsHandle, IListProvider<T> provider) : base(handle, ownsHandle)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _instanceCache = GetInstanceCache(_provider);
        if (!IsInvalid) _instanceCache.Register(this);
    }

    private static global::SharpCpp.Support.DefaultNativeInstanceCache GetInstanceCache(IListProvider<T> provider)
        => s_instanceCaches.GetValue(provider, static _ => new global::SharpCpp.Support.DefaultNativeInstanceCache());

    protected override global::SharpCpp.Support.INativeInstanceCache? InstanceCache => _instanceCache;
    public nint Handle => DangerousGetHandle();
    public bool OwnsHandle => OwnsNativeHandle;
    public bool IsNull => IsClosed || IsInvalid;
    public IListProvider<T> Provider => _provider;

    public List<T> Clone() => CreateManagedWrapper(_provider.Clone(DangerousGetHandle()), ownsHandle: true, _provider);

    object ICloneable.Clone() => Clone();

    public static List<T> CreateManagedWrapper(nint handle, bool ownsHandle)
        => CreateManagedWrapper(handle, ownsHandle, ProviderRegistry.Resolve<IListProvider<T>>());

    public static List<T> CreateManagedWrapper(nint handle, bool ownsHandle, string? moduleId)
        => CreateManagedWrapper(handle, ownsHandle, ProviderRegistry.Resolve<IListProvider<T>>(moduleId));

    public static List<T> CreateManagedWrapper(nint handle, bool ownsHandle, IListProvider<T> provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (handle != 0 && GetInstanceCache(provider).TryGetExisting(handle, ownsHandle, out var existing) && existing is List<T> cached)
            return cached;
        return new List<T>(handle, ownsHandle, provider);
    }

    protected override ulong GetCountCore() => _provider.GetCount(DangerousGetHandle());
    protected override void ClearCore() => _provider.Clear(DangerousGetHandle());
    protected override T GetAtCore(ulong index) => _provider.GetAt(DangerousGetHandle(), index);
    protected override void SetAtCore(ulong index, T value) => _provider.SetAt(DangerousGetHandle(), index, value);
    protected override void AddCore(T value) => _provider.Add(DangerousGetHandle(), value);
    protected override void InsertAtCore(ulong index, T value) => _provider.InsertAt(DangerousGetHandle(), index, value);
    protected override void RemoveAtCore(ulong index) => _provider.RemoveAt(DangerousGetHandle(), index);

    protected override bool ReleaseNativeHandle(nint handle)
    {
        if (OwnsNativeHandle) _provider.Delete(handle);
        return true;
    }
}

/// <summary>Shared managed wrapper for <c>eastl::array&lt;T, N&gt;</c>.</summary>
public sealed class Array<T> : global::SharpCpp.Support.ArrayBase<T>, ICloneable
{
    private static readonly ConditionalWeakTable<object, global::SharpCpp.Support.DefaultNativeInstanceCache> s_instanceCaches = new();
    private readonly IFixedArrayProvider<T> _provider;
    private readonly global::SharpCpp.Support.DefaultNativeInstanceCache _instanceCache;

    public Array(int length) : this(ResolveProvider(length)) { }

    public Array(ReadOnlySpan<T> values) : this(values.Length) => Assign(values);

    public Array(IEnumerable<T> values) : this(Materialize(values)) { }

    public Array(IFixedArrayProvider<T> provider) : this(provider.Create(), provider.Length, ownsHandle: true, provider) { }

    public Array(nint handle, int length, bool ownsHandle, IFixedArrayProvider<T> provider) : base(handle, length, ownsHandle)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        if (_provider.Length != length)
            throw new ArgumentException($"Provider length {_provider.Length} does not match wrapper length {length}.", nameof(provider));
        _instanceCache = GetInstanceCache(_provider);
        if (!IsInvalid) _instanceCache.Register(this);
    }

    private static IFixedArrayProvider<T> ResolveProvider(int length)
        => ProviderRegistry.Resolve<IFixedArrayProvider<T>>(null, provider => provider.Length == length);

    private static T[] Materialize(IEnumerable<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return global::System.Linq.Enumerable.ToArray(values);
    }

    private static global::SharpCpp.Support.DefaultNativeInstanceCache GetInstanceCache(IFixedArrayProvider<T> provider)
        => s_instanceCaches.GetValue(provider, static _ => new global::SharpCpp.Support.DefaultNativeInstanceCache());

    protected override global::SharpCpp.Support.INativeInstanceCache? InstanceCache => _instanceCache;
    public nint Handle => DangerousGetHandle();
    public bool OwnsHandle => OwnsNativeHandle;
    public bool IsNull => IsClosed || IsInvalid;
    public IFixedArrayProvider<T> Provider => _provider;

    public Array<T> Clone() => CreateManagedWrapper(_provider.Clone(DangerousGetHandle()), Length, ownsHandle: true, _provider);

    object ICloneable.Clone() => Clone();

    public static Array<T> CreateManagedWrapper(nint handle, int length, bool ownsHandle)
        => CreateManagedWrapper(handle, length, ownsHandle, ResolveProvider(length));

    public static Array<T> CreateManagedWrapper(nint handle, bool ownsHandle)
    {
        var provider = ProviderRegistry.Resolve<IFixedArrayProvider<T>>();
        return CreateManagedWrapper(handle, provider.Length, ownsHandle, provider);
    }

    public static Array<T> CreateManagedWrapper(nint handle, bool ownsHandle, IFixedArrayProvider<T> provider)
        => CreateManagedWrapper(handle, provider.Length, ownsHandle, provider);

    public static Array<T> CreateManagedWrapper(global::SharpCpp.Support.NativeArrayAbi abi, bool ownsHandle)
        => CreateManagedWrapper(abi, ownsHandle, ResolveProvider(checked((int)abi.Length)));

    public static Array<T> CreateManagedWrapper(global::SharpCpp.Support.NativeArrayAbi abi, bool ownsHandle, IFixedArrayProvider<T> provider)
        => CreateManagedWrapper(abi.Handle, checked((int)abi.Length), ownsHandle, provider);

    public static Array<T> CreateManagedWrapper(nint handle, int length, bool ownsHandle, string? moduleId)
        => CreateManagedWrapper(handle, length, ownsHandle,
            ProviderRegistry.Resolve<IFixedArrayProvider<T>>(moduleId, provider => provider.Length == length));

    public static Array<T> CreateManagedWrapper(global::SharpCpp.Support.NativeArrayAbi abi, bool ownsHandle, string? moduleId)
        => CreateManagedWrapper(abi.Handle, checked((int)abi.Length), ownsHandle, moduleId);

    public static Array<T> CreateManagedWrapper(nint handle, int length, bool ownsHandle, IFixedArrayProvider<T> provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (handle != 0 && GetInstanceCache(provider).TryGetExisting(handle, ownsHandle, out var existing) && existing is Array<T> cached)
            return cached;
        return new Array<T>(handle, length, ownsHandle, provider);
    }

    protected override T GetAtCore(nuint index) => _provider.GetAt(DangerousGetHandle(), index);
    protected override void SetAtCore(nuint index, T value) => _provider.SetAt(DangerousGetHandle(), index, value);

    protected override bool ReleaseNativeHandle(nint handle)
    {
        if (OwnsNativeHandle) _provider.Delete(handle);
        return true;
    }
}

/// <summary>Shared managed wrapper for <c>eastl::shared_array&lt;T&gt;</c>.</summary>
public sealed class SharedArray<T> : global::SharpCpp.Support.SharedArrayBase<T>
{
    private static readonly ConditionalWeakTable<object, global::SharpCpp.Support.DefaultNativeInstanceCache> s_instanceCaches = new();
    private readonly IDynamicArrayProvider<T> _provider;
    private readonly global::SharpCpp.Support.DefaultNativeInstanceCache _instanceCache;

    public SharedArray(int length) : this(ProviderRegistry.Resolve<IDynamicArrayProvider<T>>(), length) { }

    public SharedArray(ReadOnlySpan<T> values) : this(values.Length)
    {
        Assign(values);
    }

    public SharedArray(IEnumerable<T> values) : this(Materialize(values)) { }

    private SharedArray(T[] values) : this(values.AsSpan()) { }

    private static T[] Materialize(IEnumerable<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return global::System.Linq.Enumerable.ToArray(values);
    }

    public SharedArray(IDynamicArrayProvider<T> provider, int length) : this(provider.Create(length), length, ownsHandle: true, provider) { }

    public SharedArray(nint handle, int? length, bool ownsHandle, IDynamicArrayProvider<T> provider) : base(handle, length, ownsHandle)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _instanceCache = GetInstanceCache(_provider);
        if (!IsInvalid) _instanceCache.Register(this);
    }

    private static global::SharpCpp.Support.DefaultNativeInstanceCache GetInstanceCache(IDynamicArrayProvider<T> provider)
        => s_instanceCaches.GetValue(provider, static _ => new global::SharpCpp.Support.DefaultNativeInstanceCache());

    protected override global::SharpCpp.Support.INativeInstanceCache? InstanceCache => _instanceCache;
    public nint Handle => DangerousGetHandle();
    public bool OwnsHandle => OwnsNativeHandle;
    public bool IsNull => IsClosed || IsInvalid;
    public IDynamicArrayProvider<T> Provider => _provider;

    public static SharedArray<T> CreateManagedWrapper(nint handle, bool ownsHandle)
        => CreateManagedWrapper(handle, length: null, ownsHandle, ProviderRegistry.Resolve<IDynamicArrayProvider<T>>());

    public static SharedArray<T> CreateManagedWrapper(nint handle, bool ownsHandle, string? moduleId)
        => CreateManagedWrapper(handle, length: null, ownsHandle, ProviderRegistry.Resolve<IDynamicArrayProvider<T>>(moduleId));

    public static SharedArray<T> CreateManagedWrapper(nint handle, int? length, bool ownsHandle, string? moduleId)
        => CreateManagedWrapper(handle, length, ownsHandle, ProviderRegistry.Resolve<IDynamicArrayProvider<T>>(moduleId));

    public static SharedArray<T> CreateManagedWrapper(nint handle, int? length, bool ownsHandle, IDynamicArrayProvider<T> provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (handle != 0 && GetInstanceCache(provider).TryGetExisting(handle, ownsHandle, out var existing) && existing is SharedArray<T> cached)
            return cached;
        return new SharedArray<T>(handle, length ?? provider.GetLength(handle), ownsHandle, provider);
    }

    protected override T GetAtCore(nuint index) => _provider.GetAt(DangerousGetHandle(), index);
    protected override void SetAtCore(nuint index, T value) => _provider.SetAt(DangerousGetHandle(), index, value);

    protected override bool ReleaseNativeHandle(nint handle)
    {
        if (OwnsNativeHandle) _provider.Delete(handle);
        return true;
    }
}

/// <summary>Shared managed wrapper for <c>eastl::map&lt;TKey, TValue&gt;</c>.</summary>
public sealed class Map<TKey, TValue> : global::SharpCpp.Support.MapBase<TKey, TValue>, ICloneable
    where TKey : notnull
{
    private static readonly ConditionalWeakTable<object, global::SharpCpp.Support.DefaultNativeInstanceCache> s_instanceCaches = new();
    private readonly IMapProvider<TKey, TValue> _provider;
    private readonly global::SharpCpp.Support.DefaultNativeInstanceCache _instanceCache;

    public Map() : this(ProviderRegistry.Resolve<IMapProvider<TKey, TValue>>()) { }

    public Map(IEnumerable<KeyValuePair<TKey, TValue>> values) : this()
    {
        ArgumentNullException.ThrowIfNull(values);
        foreach (var value in values) Add(value.Key, value.Value);
    }

    public Map(IMapProvider<TKey, TValue> provider) : this(provider.Create(), ownsHandle: true, provider) { }

    public Map(nint handle, bool ownsHandle, IMapProvider<TKey, TValue> provider) : base(handle, ownsHandle)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _instanceCache = GetInstanceCache(_provider);
        if (!IsInvalid) _instanceCache.Register(this);
    }

    private static global::SharpCpp.Support.DefaultNativeInstanceCache GetInstanceCache(IMapProvider<TKey, TValue> provider)
        => s_instanceCaches.GetValue(provider, static _ => new global::SharpCpp.Support.DefaultNativeInstanceCache());

    protected override global::SharpCpp.Support.INativeInstanceCache? InstanceCache => _instanceCache;
    public nint Handle => DangerousGetHandle();
    public bool OwnsHandle => OwnsNativeHandle;
    public bool IsNull => IsClosed || IsInvalid;
    public IMapProvider<TKey, TValue> Provider => _provider;

    public Map<TKey, TValue> Clone() => CreateManagedWrapper(_provider.Clone(DangerousGetHandle()), ownsHandle: true, _provider);

    object ICloneable.Clone() => Clone();

    public static Map<TKey, TValue> CreateManagedWrapper(nint handle, bool ownsHandle)
        => CreateManagedWrapper(handle, ownsHandle, ProviderRegistry.Resolve<IMapProvider<TKey, TValue>>());

    public static Map<TKey, TValue> CreateManagedWrapper(nint handle, bool ownsHandle, string? moduleId)
        => CreateManagedWrapper(handle, ownsHandle, ProviderRegistry.Resolve<IMapProvider<TKey, TValue>>(moduleId));

    public static Map<TKey, TValue> CreateManagedWrapper(nint handle, bool ownsHandle, IMapProvider<TKey, TValue> provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (handle != 0 && GetInstanceCache(provider).TryGetExisting(handle, ownsHandle, out var existing) && existing is Map<TKey, TValue> cached)
            return cached;
        return new Map<TKey, TValue>(handle, ownsHandle, provider);
    }

    protected override ulong GetCountCore() => _provider.GetCount(DangerousGetHandle());
    protected override void ClearCore() => _provider.Clear(DangerousGetHandle());
    protected override bool ContainsKeyCore(TKey key) => _provider.ContainsKey(DangerousGetHandle(), key);
    protected override TValue GetValueCore(TKey key) => _provider.GetValue(DangerousGetHandle(), key);
    protected override void SetValueCore(TKey key, TValue value) => _provider.SetValue(DangerousGetHandle(), key, value);
    protected override bool AddCore(TKey key, TValue value) => _provider.Add(DangerousGetHandle(), key, value);
    protected override bool RemoveCore(TKey key) => _provider.Remove(DangerousGetHandle(), key);
    protected override TKey GetKeyAtCore(ulong index) => _provider.GetKeyAt(DangerousGetHandle(), index);
    protected override TValue GetValueAtCore(ulong index) => _provider.GetValueAt(DangerousGetHandle(), index);

    protected override bool ReleaseNativeHandle(nint handle)
    {
        if (OwnsNativeHandle) _provider.Delete(handle);
        return true;
    }
}

/// <summary>Shared managed wrapper for <c>eastl::unordered_map&lt;TKey, TValue&gt;</c>.</summary>
public sealed class UnorderedMap<TKey, TValue> : global::SharpCpp.Support.UnorderedMapBase<TKey, TValue>, ICloneable
    where TKey : notnull
{
    private static readonly ConditionalWeakTable<object, global::SharpCpp.Support.DefaultNativeInstanceCache> s_instanceCaches = new();
    private readonly IUnorderedMapProvider<TKey, TValue> _provider;
    private readonly global::SharpCpp.Support.DefaultNativeInstanceCache _instanceCache;

    public UnorderedMap() : this(ProviderRegistry.Resolve<IUnorderedMapProvider<TKey, TValue>>()) { }

    public UnorderedMap(IEnumerable<KeyValuePair<TKey, TValue>> values) : this()
    {
        ArgumentNullException.ThrowIfNull(values);
        foreach (var value in values) Add(value.Key, value.Value);
    }

    public UnorderedMap(IUnorderedMapProvider<TKey, TValue> provider) : this(provider.Create(), ownsHandle: true, provider) { }

    public UnorderedMap(nint handle, bool ownsHandle, IUnorderedMapProvider<TKey, TValue> provider) : base(handle, ownsHandle)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _instanceCache = GetInstanceCache(_provider);
        if (!IsInvalid) _instanceCache.Register(this);
    }

    private static global::SharpCpp.Support.DefaultNativeInstanceCache GetInstanceCache(IUnorderedMapProvider<TKey, TValue> provider)
        => s_instanceCaches.GetValue(provider, static _ => new global::SharpCpp.Support.DefaultNativeInstanceCache());

    protected override global::SharpCpp.Support.INativeInstanceCache? InstanceCache => _instanceCache;
    public nint Handle => DangerousGetHandle();
    public bool OwnsHandle => OwnsNativeHandle;
    public bool IsNull => IsClosed || IsInvalid;
    public IUnorderedMapProvider<TKey, TValue> Provider => _provider;

    public UnorderedMap<TKey, TValue> Clone() => CreateManagedWrapper(_provider.Clone(DangerousGetHandle()), ownsHandle: true, _provider);

    object ICloneable.Clone() => Clone();

    public static UnorderedMap<TKey, TValue> CreateManagedWrapper(nint handle, bool ownsHandle)
        => CreateManagedWrapper(handle, ownsHandle, ProviderRegistry.Resolve<IUnorderedMapProvider<TKey, TValue>>());

    public static UnorderedMap<TKey, TValue> CreateManagedWrapper(nint handle, bool ownsHandle, string? moduleId)
        => CreateManagedWrapper(handle, ownsHandle, ProviderRegistry.Resolve<IUnorderedMapProvider<TKey, TValue>>(moduleId));

    public static UnorderedMap<TKey, TValue> CreateManagedWrapper(nint handle, bool ownsHandle, IUnorderedMapProvider<TKey, TValue> provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (handle != 0 && GetInstanceCache(provider).TryGetExisting(handle, ownsHandle, out var existing) && existing is UnorderedMap<TKey, TValue> cached)
            return cached;
        return new UnorderedMap<TKey, TValue>(handle, ownsHandle, provider);
    }

    protected override ulong GetCountCore() => _provider.GetCount(DangerousGetHandle());
    protected override void ClearCore() => _provider.Clear(DangerousGetHandle());
    protected override bool ContainsKeyCore(TKey key) => _provider.ContainsKey(DangerousGetHandle(), key);
    protected override TValue GetValueCore(TKey key) => _provider.GetValue(DangerousGetHandle(), key);
    protected override void SetValueCore(TKey key, TValue value) => _provider.SetValue(DangerousGetHandle(), key, value);
    protected override bool AddCore(TKey key, TValue value) => _provider.Add(DangerousGetHandle(), key, value);
    protected override bool RemoveCore(TKey key) => _provider.Remove(DangerousGetHandle(), key);
    protected override TKey GetKeyAtCore(ulong index) => _provider.GetKeyAt(DangerousGetHandle(), index);
    protected override TValue GetValueAtCore(ulong index) => _provider.GetValueAt(DangerousGetHandle(), index);

    protected override bool ReleaseNativeHandle(nint handle)
    {
        if (OwnsNativeHandle) _provider.Delete(handle);
        return true;
    }
}
