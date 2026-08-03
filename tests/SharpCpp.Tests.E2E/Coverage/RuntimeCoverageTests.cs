using System.Collections;
using System.Runtime.InteropServices;
using System.Text;

using SharpCpp.Support;

using Xunit;

namespace SharpCpp.Tests.E2E.Coverage;

public sealed class RuntimeCoverageTests
{
    [Fact]
    public void NativeObject_helpers_lifetime_and_instance_cache_are_exercised_end_to_end()
    {
        var cache = new DefaultNativeInstanceCache();
        using var owner = new ProbeObject((nint)0x1001, ownsHandle: true, cache);
        cache.Register(owner);

        Assert.Same(owner, NativeObject.GetObject(owner));
        Assert.Null(NativeObject.GetObject(null));
        Assert.Equal(owner.Handle, NativeObject.GetHandle(owner));
        Assert.Equal(owner.Handle, NativeObject.RequireHandle(owner, "owner"));
        Assert.Same(owner, NativeObject.RequireObject(owner, "owner"));
        Assert.Equal(0, NativeObject.GetHandle(null));
        Assert.Equal(0, NativeObject.TransferHandleOwnership(null));
        Assert.Throws<ArgumentException>(() => NativeObject.GetObject(42));
        Assert.Throws<ArgumentException>(() => NativeObject.RequireObject(42, "owner"));

        var borrowed = new ProbeObject(owner.Handle, ownsHandle: false, cache);
        cache.Register(borrowed);
        Assert.Same(owner, cache.TryGetExisting(owner.Handle, ownsHandle: false, out var existing) ? existing : null);
        Assert.True(cache.TryGetExisting(owner.Handle, ownsHandle: true, out existing));
        Assert.Same(owner, existing);
        Assert.False(cache.TryGetExisting(0, ownsHandle: false, out _));

        var promoted = new ProbeObject((nint)0x1002, ownsHandle: false, cache);
        cache.Register(promoted);
        Assert.True(cache.TryGetExisting(promoted.Handle, ownsHandle: true, out var promotedExisting));
        Assert.Same(promoted, promotedExisting);
        Assert.True(promoted.OwnsNativeHandle);
        Assert.Equal(1, promoted.AcquireCalls);
        Assert.True(promoted.TryPromoteToOwningHandle());
        Assert.Equal(1, promoted.AcquireCalls);

        var transferredHandle = NativeObject.TransferHandleOwnership(owner);
        Assert.Equal((nint)0x1001, transferredHandle);
        Assert.False(owner.OwnsNativeHandle);
        Assert.Throws<InvalidOperationException>(() => NativeObject.TransferHandleOwnership(owner));

        var firstKeepAlive = new DisposeProbe();
        var secondKeepAlive = new DisposeProbe();
        owner.SetKeepAlive(firstKeepAlive, (nint)1);
        owner.SetKeepAlive(secondKeepAlive, (nint)1);
        Assert.Equal(0, firstKeepAlive.DisposeCalls);
        Assert.Equal(1, secondKeepAlive.DisposeCalls);

        var replacementKeepAlive = new DisposeProbe();
        owner.SetKeepAlive(replacementKeepAlive, (nint)2);
        Assert.Equal(1, firstKeepAlive.DisposeCalls);
        Assert.Equal(0, replacementKeepAlive.DisposeCalls);

        Assert.True(owner.TryManagedOverride(typeof(ManagedOverride), typeof(ManagedOverrideBase), "Call", [typeof(int)]));
        Assert.False(owner.TryManagedOverride(typeof(ManagedOverrideBase), typeof(ManagedOverrideBase), "Call", [typeof(int)]));
        Assert.False(owner.TryManagedOverride(typeof(ManagedOverrideBase), typeof(ManagedOverrideBase), "Missing", [typeof(int)]));
        Assert.False(owner.TryManagedOverride(typeof(ManagedOverride), typeof(ManagedOverrideBase), "Missing", [typeof(int)]));
        Assert.False(owner.TryManagedOverride(typeof(ManagedOverride), typeof(string), "Call", [typeof(int)]));

        Assert.Throws<ArgumentNullException>(() => owner.SetKeepAlive(null!, (nint)3));
        Assert.Throws<ArgumentNullException>(() => NativeObject.RequireHandle(null!, "owner"));

        borrowed.Dispose();
        promoted.Dispose();
        owner.Dispose();
        Assert.True(owner.IsClosed);
        Assert.Throws<ObjectDisposedException>(() => NativeObject.GetHandle(owner));
        Assert.Throws<ObjectDisposedException>(() => NativeObject.RequireHandle(owner, "owner"));
        Assert.Throws<ObjectDisposedException>(() => NativeObject.TransferHandleOwnership(owner));
        Assert.False(cache.TryGetExisting((nint)0x1001, ownsHandle: false, out _));
    }

    [Fact]
    public void NativeObject_native_disposal_and_cache_edge_paths_are_exercised()
    {
        var cache = new DefaultNativeInstanceCache();
        var stale = new ProbeObject((nint)0x1101, ownsHandle: true, cache, managesCacheLifecycle: true);
        cache.Register(stale);
        stale.Dispose();
        Assert.False(cache.TryGetExisting((nint)0x1101, ownsHandle: false, out _));

        var invalid = new ProbeObject(0, ownsHandle: true, cache);
        cache.Register(invalid);
        Assert.False(cache.TryGetExisting(0, ownsHandle: false, out _));
        invalid.Dispose();

        var externallyDestroyed = new ProbeObject((nint)0x1102, ownsHandle: true, cache);
        cache.Register(externallyDestroyed);
        var keepAlive = new DisposeProbe();
        externallyDestroyed.SetKeepAlive(keepAlive, (nint)7);
        externallyDestroyed.DisposeFromNativeRuntime();
        Assert.True(externallyDestroyed.IsClosed || externallyDestroyed.IsInvalid);
        Assert.Equal(1, keepAlive.DisposeCalls);
        Assert.Equal(0, externallyDestroyed.ReleaseCalls);

        var retry = new ProbeObject((nint)0x1103, ownsHandle: true, cache) { ReleaseResult = false };
        retry.Dispose();
        Assert.Equal(1, retry.ReleaseCalls);
        retry.ReleaseResult = true;

        var other = new ProbeObject((nint)0x1104, ownsHandle: true, cache);
        cache.Register(other);
        cache.Unregister(new ProbeObject((nint)0x1105, ownsHandle: false, cache));
        Assert.True(cache.TryGetExisting((nint)0x1104, ownsHandle: false, out _));
        other.Dispose();

        var invalidBorrowed = new ProbeObject((nint)0x1105, ownsHandle: false, cache);
        invalidBorrowed.InvalidatePublic();
        Assert.False(invalidBorrowed.TryPromoteToOwningHandle());
        invalidBorrowed.Dispose();

        Assert.Throws<ArgumentNullException>(() => NativeObject.RequireObject(null!, "value"));
    }

    [Fact]
    public void Support_array_and_collection_bases_cover_success_and_failure_paths()
    {
        using var array = new ProbeArray(3, [2, 4, 6]);
        Assert.Equal(3, array.Length);
        Assert.Equal(3, array.Count);
        Assert.False(array.IsReadOnly);
        Assert.Equal((nint)0x1201, array.ToNativeArrayAbi().Handle);
        Assert.Equal((nuint)3, array.ToNativeArrayAbi().Length);
        Assert.Equal(4, array[1]);
        array[1] = 5;
        Assert.Equal(5, array[1]);
        Assert.Equal(new[] { 2, 5, 6 }, array.ToArray());
        var destination = new int[5];
        array.CopyTo(destination, 1);
        Assert.Equal(new[] { 0, 2, 5, 6, 0 }, destination);
        Assert.Equal(0, array.IndexOf(2));
        Assert.Equal(-1, array.IndexOf(9));
        Assert.Contains(5, array);
        Assert.Equal(new[] { 2, 5, 6 }, array.Cast<int>().ToArray());
        Assert.Equal(new[] { 2, 5, 6 }, ((IEnumerable)array).Cast<int>().ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = array[-1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = array[3]);
        Assert.Throws<ArgumentOutOfRangeException>(() => array.CopyTo([], -1));
        Assert.Throws<ArgumentException>(() => array.CopyTo(new int[2], 0));
        Assert.Throws<ArgumentNullException>(() => array.CopyTo(null!, 0));
        Assert.Throws<NotSupportedException>(() => array.Add(1));
        Assert.Throws<NotSupportedException>(() => array.Clear());
        Assert.Throws<NotSupportedException>(() => array.Insert(0, 1));
        Assert.Throws<NotSupportedException>(() => array.Remove(1));
        Assert.Throws<NotSupportedException>(() => array.RemoveAt(0));
        array.AssignPublic([7, 8, 9]);
        Assert.Equal(new[] { 7, 8, 9 }, array.ToArray());
        Assert.Throws<ArgumentException>(() => array.AssignPublic([1, 2]));
        using (var emptyArray = new ProbeArray(0, []))
        {
            Assert.Empty(emptyArray);
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProbeArray(-1, []));

        var proxy = new ProbeFixedArrayProxy(2, [11, 12]);
        Assert.Equal(2, proxy.Length);
        Assert.Equal(2, proxy.Count);
        Assert.False(proxy.IsReadOnly);
        Assert.Equal(11, proxy[0]);
        proxy[1] = 13;
        Assert.Equal(new[] { 11, 13 }, proxy.ToArray());
        Assert.Equal(1, proxy.IndexOf(13));
        Assert.Contains(11, proxy);
        Assert.Equal(new[] { 11, 13 }, proxy.Cast<int>().ToArray());
        Assert.Equal(new[] { 11, 13 }, ((IEnumerable)proxy).Cast<int>().ToArray());
        var proxyDestination = new int[3];
        proxy.CopyTo(proxyDestination, 1);
        Assert.Equal(new[] { 0, 11, 13 }, proxyDestination);
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = proxy[-1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = proxy[2]);
        Assert.Throws<ArgumentOutOfRangeException>(() => proxy.CopyTo([], -1));
        Assert.Throws<ArgumentException>(() => proxy.CopyTo(new int[1], 0));
        Assert.Throws<ArgumentNullException>(() => proxy.CopyTo(null!, 0));
        Assert.Throws<NotSupportedException>(() => proxy.Add(1));
        Assert.Throws<NotSupportedException>(() => proxy.Clear());
        Assert.Throws<NotSupportedException>(() => proxy.Insert(0, 1));
        Assert.Throws<NotSupportedException>(() => proxy.Remove(1));
        Assert.Throws<NotSupportedException>(() => proxy.RemoveAt(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProbeFixedArrayProxy(-1, []));

        using var sharedKnown = new ProbeSharedArray((nint)0x1202, 3, [20, 21, 22]);
        Assert.True(sharedKnown.HasKnownLength);
        Assert.Equal(3, sharedKnown.Length);
        Assert.Equal(21, sharedKnown[1]);
        Assert.Equal(22, sharedKnown[(nuint)2]);
        sharedKnown[0] = 23;
        sharedKnown[(nuint)1] = 24;
        Assert.Equal(new[] { 23, 24, 22 }, sharedKnown.ToArray(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = sharedKnown[-1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => sharedKnown[-1] = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => sharedKnown.ToArray(-1));
        sharedKnown.AssignPublic([30, 31]);
        Assert.Equal(new[] { 30, 31 }, sharedKnown.ToArray(2));

        using var sharedUnknown = new ProbeSharedArray((nint)0x1203, null, [40, 41]);
        Assert.False(sharedUnknown.HasKnownLength);
        Assert.Null(sharedUnknown.Length);
        Assert.Equal(40, sharedUnknown[(nuint)0]);
        Assert.Equal(new[] { 40, 41 }, sharedUnknown.ToArray(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProbeSharedArray((nint)0x1204, -1, []));

        using var sequence = new ProbeSequence([1, 2, 3]);
        Assert.Equal(3, sequence.Count);
        Assert.False(sequence.IsReadOnly);
        Assert.Equal(2, sequence[1]);
        sequence[1] = 4;
        sequence.Add(5);
        sequence.Insert(0, 0);
        Assert.Equal(new[] { 0, 1, 4, 3, 5 }, sequence.ToArray());
        Assert.Equal(2, sequence.IndexOf(4));
        Assert.Equal(-1, sequence.IndexOf(99));
        Assert.Contains(3, sequence);
        Assert.True(sequence.Remove(4));
        Assert.False(sequence.Remove(99));
        sequence.RemoveAt(0);
        Assert.Equal(new[] { 1, 3, 5 }, sequence.ToArray());
        var sequenceDestination = new int[5];
        sequence.CopyTo(sequenceDestination, 1);
        Assert.Equal(new[] { 0, 1, 3, 5, 0 }, sequenceDestination);
        Assert.Equal(new[] { 1, 3, 5 }, sequence.Cast<int>().ToArray());
        Assert.Equal(new[] { 1, 3, 5 }, ((IEnumerable)sequence).Cast<int>().ToArray());
        Assert.Throws<ArgumentException>(() => sequence.CopyTo(new int[0], 0));
        sequence.Clear();
        Assert.Empty(sequence);
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = sequence[-1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = sequence[0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => sequence.Insert(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => sequence.Insert(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => sequence.RemoveAt(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => sequence.CopyTo([], -1));
        Assert.Throws<ArgumentNullException>(() => sequence.CopyTo(null!, 0));

        using var dictionary = new ProbeDictionary([new("one", 1), new("two", 2)]);
        Assert.Equal(2, dictionary.Count);
        Assert.False(dictionary.IsReadOnly);
        Assert.Equal(1, dictionary["one"]);
        dictionary["one"] = 10;
        dictionary.Add("three", 3);
        dictionary.Add(new KeyValuePair<string, int>("four", 4));
        Assert.True(dictionary.ContainsKey("three"));
        Assert.False(dictionary.ContainsKey("missing"));
        Assert.True(dictionary.TryGetValue("four", out var four));
        Assert.Equal(4, four);
        Assert.False(dictionary.TryGetValue("missing", out _));
        Assert.Equal(new[] { "one", "two", "three", "four" }, dictionary.Keys);
        Assert.Equal(new[] { 10, 2, 3, 4 }, dictionary.Values);
        Assert.Equal(new[] { "one", "two", "three", "four" }, ((IReadOnlyDictionary<string, int>)dictionary).Keys);
        Assert.Equal(new[] { 10, 2, 3, 4 }, ((IReadOnlyDictionary<string, int>)dictionary).Values);
        Assert.Contains(new KeyValuePair<string, int>("three", 3), dictionary);
        Assert.DoesNotContain(new KeyValuePair<string, int>("three", 9), dictionary);
        var dictionaryDestination = new KeyValuePair<string, int>[5];
        dictionary.CopyTo(dictionaryDestination, 1);
        Assert.Equal(new KeyValuePair<string, int>("one", 10), dictionaryDestination[1]);
        Assert.Equal(4, dictionary.Cast<KeyValuePair<string, int>>().Count());
        Assert.Equal(4, ((IEnumerable)dictionary).Cast<KeyValuePair<string, int>>().Count());
        Assert.True(dictionary.Remove(new KeyValuePair<string, int>("four", 4)));
        Assert.False(dictionary.Remove(new KeyValuePair<string, int>("four", 4)));
        Assert.True(dictionary.Remove("three"));
        Assert.False(dictionary.Remove("missing"));
        Assert.Throws<ArgumentException>(() => dictionary.Add("one", 11));
        Assert.Throws<KeyNotFoundException>(() => _ = dictionary["missing"]);
        Assert.Throws<ArgumentNullException>(() => _ = dictionary[(string)null!]);
        Assert.Throws<ArgumentNullException>(() => dictionary[(string)null!] = 1);
        Assert.Throws<ArgumentNullException>(() => dictionary.Add(null!, 1));
        Assert.Throws<ArgumentNullException>(() => dictionary.ContainsKey(null!));
        Assert.Throws<ArgumentNullException>(() => dictionary.Remove(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => dictionary.CopyTo([], -1));
        Assert.Throws<ArgumentException>(() => dictionary.CopyTo(new KeyValuePair<string, int>[1], 0));
        Assert.Throws<ArgumentNullException>(() => dictionary.CopyTo(null!, 0));
        Assert.True(dictionary.Remove(new KeyValuePair<string, int>("one", 10)));
        dictionary.Clear();
        Assert.Empty(dictionary);
    }

    [Fact]
    public void Shared_provider_registry_covers_module_shape_and_ambiguity_rules()
    {
        SharedProviderRegistry<CoverageProvider>.Clear();
        var first = new CoverageProvider("module-a", "shape-a");
        var second = new CoverageProvider("module-b", "shape-a");
        var otherShape = new CoverageProvider("module-a", "shape-b");

        Assert.Null(SharedProviderRegistry<CoverageProvider>.CurrentModuleId);
        Assert.Throws<ArgumentNullException>(() => SharedProviderRegistry<CoverageProvider>.Register<CoverageProvider>(null!));
        Assert.Throws<ArgumentException>(() => SharedProviderRegistry<CoverageProvider>.Register(new CoverageProvider("", "shape")));
        Assert.Throws<ArgumentException>(() => SharedProviderRegistry<CoverageProvider>.Register(new CoverageProvider("module", "")));

        SharedProviderRegistry<CoverageProvider>.Register(first);
        SharedProviderRegistry<CoverageProvider>.Register(first);
        SharedProviderRegistry<CoverageProvider>.Register(second);
        SharedProviderRegistry<CoverageProvider>.Register(otherShape);
        Assert.Throws<InvalidOperationException>(() => SharedProviderRegistry<CoverageProvider>.Register(new CoverageProvider("module-a", "shape-a")));

        Assert.Same(first, SharedProviderRegistry<CoverageProvider>.Resolve<CoverageProvider>("module-a", "shape-a"));
        Assert.Same(otherShape, SharedProviderRegistry<CoverageProvider>.Resolve<CoverageProvider>("module-a", "shape-b"));
        Assert.Throws<InvalidOperationException>(() => SharedProviderRegistry<CoverageProvider>.Resolve<CoverageProvider>());
        Assert.Throws<InvalidOperationException>(() => SharedProviderRegistry<CoverageProvider>.Resolve<CoverageProvider>("missing"));
        Assert.Throws<InvalidOperationException>(() => SharedProviderRegistry<CoverageProvider>.Resolve<CoverageProvider>("module-a"));
        Assert.Throws<InvalidOperationException>(() => SharedProviderRegistry<CoverageProvider>.Resolve<CoverageProvider>("missing", "missing-shape"));
        Assert.Throws<ArgumentNullException>(() => SharedProviderRegistry<CoverageProvider>.Resolve<CoverageProvider>(null, (Func<CoverageProvider, bool>)null!));

        using (SharedProviderRegistry<CoverageProvider>.PushModule("module-b"))
        {
            Assert.Equal("module-b", SharedProviderRegistry<CoverageProvider>.CurrentModuleId);
            Assert.Same(second, SharedProviderRegistry<CoverageProvider>.Resolve<CoverageProvider>(shapeKey: "shape-a"));
        }

        Assert.Null(SharedProviderRegistry<CoverageProvider>.CurrentModuleId);
        var scope = SharedProviderRegistry<CoverageProvider>.PushModule("module-a");
        scope.Dispose();
        scope.Dispose();
        Assert.Throws<ArgumentException>(() => SharedProviderRegistry<CoverageProvider>.PushModule(" "));
        SharedProviderRegistry<CoverageProvider>.Clear();
        Assert.Null(SharedProviderRegistry<CoverageProvider>.CurrentModuleId);
    }

    [Fact]
    public void Utf8_metadata_and_native_array_abi_helpers_cover_empty_and_nonempty_values()
    {
        var metadata = new NativeObjectMetadata(0, 7).Normalize((nint)0x1301);
        Assert.Equal((nint)0x1301, metadata.Handle);
        Assert.Equal((nuint)7, metadata.DynamicTypeId);
        var unchanged = new NativeObjectMetadata((nint)0x1302, 8).Normalize((nint)0x1303);
        Assert.Equal((nint)0x1302, unchanged.Handle);

        var abi = new NativeArrayAbi((nint)0x1304, 4);
        Assert.Equal((nint)0x1304, abi.Handle);
        Assert.Equal((nuint)4, abi.Length);

        Assert.Empty(Utf8SpanAbiMarshaller.ConvertToManaged(default).ToArray());
        var input = "hello"u8.ToArray();
        var unmanaged = Utf8SpanAbiMarshaller.ConvertToUnmanaged(input);
        try
        {
            Assert.Equal(input, Utf8SpanAbiMarshaller.ConvertToManaged(unmanaged).ToArray());
            Assert.Equal(input, Utf8SpanAbiReturnMarshaller.ConvertToManaged(unmanaged).ToArray());
        }
        finally
        {
            Utf8SpanAbiMarshaller.Free(unmanaged);
        }

        Assert.Empty(Utf8SpanAbiMarshaller.ConvertToManaged(new Utf8SpanAbi((nint)1, 0)).ToArray());
        Utf8SpanAbiMarshaller.Free(default);
        Assert.Equal(default, Utf8SpanAbiMarshaller.ConvertToUnmanaged(ReadOnlySpan<byte>.Empty));
        Assert.Empty(Utf8SpanAbiReturnMarshaller.ConvertToManaged(default).ToArray());

        var text = Encoding.UTF8.GetBytes("native\0");
        var pointer = Marshal.AllocHGlobal(text.Length);
        try
        {
            Marshal.Copy(text, 0, pointer, text.Length);
            Assert.Equal("native", Encoding.UTF8.GetString(Utf8SpanReturnMarshaller.ConvertToManaged(pointer).ToArray()));
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }

        Assert.Empty(Utf8SpanReturnMarshaller.ConvertToManaged(IntPtr.Zero).ToArray());
    }

    private sealed class ProbeObject : NativeObject
    {
        private readonly INativeInstanceCache? _cache;
        private readonly bool _managesCacheLifecycle;

        public ProbeObject(nint handle, bool ownsHandle, INativeInstanceCache? cache = null, bool managesCacheLifecycle = false)
            : base(handle, ownsHandle)
        {
            _cache = cache;
            _managesCacheLifecycle = managesCacheLifecycle;
        }

        public nint Handle => DangerousGetHandle();
        public int AcquireCalls { get; private set; }
        public int ReleaseCalls { get; private set; }
        public bool ReleaseResult { get; set; } = true;

        protected override bool ManagesInstanceCacheLifecycle => _managesCacheLifecycle;
        protected override INativeInstanceCache? InstanceCache => _cache;

        protected override bool ReleaseNativeHandle(nint handle)
        {
            ReleaseCalls++;
            return ReleaseResult;
        }

        protected override void AcquireOwningHandleCore() => AcquireCalls++;

        public bool TryManagedOverride(Type runtimeType, Type declaringType, string name, Type[] parameterTypes)
            => HasManagedOverride(runtimeType, declaringType, name, parameterTypes);

        public void SetKeepAlive(IDisposable value, nint key) => SetManagedKeepAliveCore(value, key);

        public void DisposeFromNativeRuntime() => DisposeFromNativeRuntimeCore();

        public void InvalidatePublic() => SetHandleAsInvalid();
    }

    private sealed class DisposeProbe : IDisposable
    {
        public int DisposeCalls { get; private set; }

        public void Dispose() => DisposeCalls++;
    }

    private class ManagedOverrideBase
    {
        protected virtual void Call(int value)
        {
        }
    }

    private sealed class ManagedOverride : ManagedOverrideBase
    {
        protected override void Call(int value)
        {
        }
    }

    private sealed class ProbeArray : ArrayBase<int>
    {
        private readonly int[] _values;

        public ProbeArray(int length, int[] values)
            : base((nint)0x1201, length, ownsHandle: true)
        {
            _values = new int[length];
            if (values.Length != 0)
            {
                AssignPublic(values);
            }
        }

        public void AssignPublic(ReadOnlySpan<int> values) => Assign(values);

        protected override int GetAtCore(nuint index) => _values[checked((int)index)];
        protected override void SetAtCore(nuint index, int value) => _values[checked((int)index)] = value;
    }

    private sealed class ProbeFixedArrayProxy : FixedArrayProxy<int>
    {
        private readonly int[] _values;

        public ProbeFixedArrayProxy(int length, int[] values)
            : base(length)
        {
            _values = new int[length];
            values.AsSpan().CopyTo(_values);
        }

        protected override int GetAtCore(nuint index) => _values[checked((int)index)];
        protected override void SetAtCore(nuint index, int value) => _values[checked((int)index)] = value;
    }

    private sealed class ProbeSharedArray : SharedArrayBase<int>
    {
        private readonly int[] _values;

        public ProbeSharedArray(nint handle, int? length, int[] values)
            : base(handle, length, ownsHandle: false)
        {
            _values = values;
        }

        public void AssignPublic(ReadOnlySpan<int> values) => Assign(values);

        protected override int GetAtCore(nuint index) => _values[checked((int)index)];
        protected override void SetAtCore(nuint index, int value) => _values[checked((int)index)] = value;
    }

    private sealed class ProbeSequence : VectorBase<int>
    {
        private readonly List<int> _values;

        public ProbeSequence(int[] values)
            : base((nint)0x1205, ownsHandle: false)
        {
            _values = [.. values];
        }

        protected override ulong GetCountCore() => (ulong)_values.Count;
        protected override void ClearCore() => _values.Clear();
        protected override int GetAtCore(ulong index) => _values[checked((int)index)];
        protected override void SetAtCore(ulong index, int value) => _values[checked((int)index)] = value;
        protected override void AddCore(int value) => _values.Add(value);
        protected override void InsertAtCore(ulong index, int value) => _values.Insert(checked((int)index), value);
        protected override void RemoveAtCore(ulong index) => _values.RemoveAt(checked((int)index));
    }

    private sealed class ProbeDictionary : MapBase<string, int>
    {
        private readonly List<KeyValuePair<string, int>> _values;

        public ProbeDictionary(IEnumerable<KeyValuePair<string, int>> values)
            : base((nint)0x1206, ownsHandle: false)
        {
            _values = [.. values];
        }

        protected override ulong GetCountCore() => (ulong)_values.Count;
        protected override void ClearCore() => _values.Clear();
        protected override bool ContainsKeyCore(string key) => _values.Any(item => item.Key == key);
        protected override int GetValueCore(string key) => _values.Single(item => item.Key == key).Value;
        protected override void SetValueCore(string key, int value)
        {
            var index = _values.FindIndex(item => item.Key == key);
            if (index < 0)
            {
                _values.Add(new KeyValuePair<string, int>(key, value));
            }
            else
            {
                _values[index] = new KeyValuePair<string, int>(key, value);
            }
        }

        protected override bool AddCore(string key, int value)
        {
            if (ContainsKeyCore(key))
            {
                return false;
            }

            _values.Add(new KeyValuePair<string, int>(key, value));
            return true;
        }

        protected override bool RemoveCore(string key)
        {
            var index = _values.FindIndex(item => item.Key == key);
            if (index < 0)
            {
                return false;
            }

            _values.RemoveAt(index);
            return true;
        }

        protected override string GetKeyAtCore(ulong index) => _values[checked((int)index)].Key;
        protected override int GetValueAtCore(ulong index) => _values[checked((int)index)].Value;
    }

    private sealed class CoverageProvider : ISharedProvider
    {
        public CoverageProvider(string moduleId, string shapeKey)
        {
            ModuleId = moduleId;
            ShapeKey = shapeKey;
        }

        public string ModuleId { get; }
        public string ShapeKey { get; }
    }
}
