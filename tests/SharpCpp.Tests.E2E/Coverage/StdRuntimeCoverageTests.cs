using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

using StdInfrastructure = Std.Infrastructure;
using SharpCpp.Support;

using Xunit;

namespace SharpCpp.Tests.E2E.Coverage;

public sealed class StdRuntimeCoverageTests
{
    private const string ModuleId = "coverage-stdlib";

    [Fact]
    public void Std_string_covers_native_storage_fallbacks_cloning_and_registry_paths()
    {
        var provider = new MemoryStringProvider(ModuleId, "coverage-string", exposeData: true);
        Register<StdInfrastructure.IStringProvider>(provider);
        using var module = StdInfrastructure.ProviderRegistry.PushModule(ModuleId);

        using var value = new Std.String(provider);
        Assert.Equal(provider, value.Provider);
        Assert.Equal(provider.GetHandleFor(""), value.Handle);
        Assert.True(value.OwnsHandle);
        Assert.False(value.IsNull);

        value.Assign("hé");
        Assert.Equal("hé", value.ToString());
        Assert.Equal(2, value.Count);
        Assert.Equal('é', value[1]);
        Assert.Equal("hé"u8.ToArray(), value.ToUtf8Bytes());
        Assert.Equal("hé"u8.ToArray(), value.AsUtf8Span().ToArray());
        ReadOnlySpan<byte> implicitSpan = value;
        Assert.Equal("hé"u8.ToArray(), implicitSpan.ToArray());
        Assert.Equal(new[] { 'h', 'é' }, value.ToArray());
        Assert.True(value.Equals("hé"));
        Assert.True(value.Equals((object)"hé"));
        Assert.True(value.Equals((object)new Std.String("hé", provider)));
        Assert.False(value.Equals((object)42));
        _ = value.GetHashCode();

        value.Assign("fallback"u8);
        provider.ExposeData = false;
        Assert.Equal("fallback", value.ToString());
        Assert.Equal("fallback"u8.ToArray(), value.ToUtf8Bytes());
        value.Assign(ReadOnlySpan<byte>.Empty);
        Assert.Empty(value.ToUtf8Bytes());
        Assert.Empty(value.AsUtf8Span().ToArray());
        Assert.Empty(value);

        using var fromDefault = new Std.String();
        using var fromValue = new Std.String("default-value");
        using var fromNull = new Std.String(null, provider);
        Assert.Equal("default-value", fromValue.ToString());
        Assert.Empty(fromNull.ToUtf8Bytes());
        Std.String implicitString = "implicit";
        using (implicitString)
        {
            Assert.Equal("implicit", implicitString.ToString());
        }

        using var clone = value.Clone();
        Assert.NotSame(value, clone);
        Assert.Equal(value.ToUtf8Bytes(), clone.ToUtf8Bytes());
        using var interfaceClone = (Std.String)((ICloneable)value).Clone();
        Assert.Equal(value.ToString(), interfaceClone.ToString());

        var wrappedHandle = provider.Create();
        using var wrapped = Std.String.CreateManagedWrapper(wrappedHandle, ownsHandle: true, provider);
        Assert.Same(wrapped, Std.String.CreateManagedWrapper(wrappedHandle, ownsHandle: true, provider));
        using var moduleWrapped = Std.String.CreateManagedWrapper(provider.Create(), ownsHandle: true, ModuleId);
        using var defaultWrapped = Std.String.CreateManagedWrapper(provider.Create(), ownsHandle: true);
        using var nullWrapped = Std.String.CreateManagedWrapper(0, ownsHandle: false, provider);
        Assert.True(nullWrapped.IsNull);
        Assert.False(nullWrapped.OwnsHandle);
        nullWrapped.Dispose();

        using var borrowed = Std.String.CreateManagedWrapper(provider.Create(), ownsHandle: false, provider);
        borrowed.Dispose();
        Assert.Equal(0, provider.DeleteCountForBorrowedHandles);

        Assert.Throws<ArgumentNullException>(() => Std.String.CreateManagedWrapper(1, true, (StdInfrastructure.IStringProvider)null!));
        Assert.Throws<ArgumentNullException>(() => _ = (ReadOnlySpan<byte>)(Std.String)null!);
        Assert.Empty(fromDefault.ToUtf8Bytes());
    }

    [Fact]
    public void Std_vector_list_array_and_maps_cover_provider_backed_wrapper_surfaces()
    {
        var vectorProvider = new StdVectorProvider<int>(ModuleId, "coverage-vector");
        var listProvider = new StdListProvider<int>(ModuleId, "coverage-list");
        var fixedProvider = new StdFixedArrayProvider<int>(ModuleId, "coverage-array", 3);
        var mapProvider = new StdMapProvider(ModuleId, "coverage-map");
        var unorderedProvider = new StdUnorderedMapProvider(ModuleId, "coverage-unordered-map");
        Register<StdInfrastructure.IVectorProvider<int>>(vectorProvider);
        Register<StdInfrastructure.IListProvider<int>>(listProvider);
        Register<StdInfrastructure.IFixedArrayProvider<int>>(fixedProvider);
        Register<StdInfrastructure.IMapProvider<string, int>>(mapProvider);
        Register<StdInfrastructure.IUnorderedMapProvider<string, int>>(unorderedProvider);
        using var module = StdInfrastructure.ProviderRegistry.PushModule(ModuleId);

        using var vector = new Std.Vector<int>(new[] { 1, 2, 3 });
        Assert.Equal(vectorProvider, vector.Provider);
        Assert.Equal(3, vector.Count);
        Assert.Equal(2, vector[1]);
        Assert.False(vector.IsReadOnly);
        Assert.False(vector.IsNull);
        Assert.True(vector.OwnsHandle);
        vector[1] = 20;
        vector.Add(4);
        vector.Insert(0, 0);
        Assert.Equal(new[] { 0, 1, 20, 3, 4 }, vector.ToArray());
        Assert.Equal(2, vector.IndexOf(20));
        Assert.Contains(3, vector);
        Assert.True(vector.Remove(20));
        Assert.False(vector.Remove(99));
        vector.RemoveAt(0);
        var vectorCopy = new int[5];
        vector.CopyTo(vectorCopy, 1);
        Assert.Equal(new[] { 0, 1, 3, 4, 0 }, vectorCopy);
        Assert.Equal(new[] { 1, 3, 4 }, vector.Cast<int>().ToArray());
        Assert.Equal(new[] { 1, 3, 4 }, ((IEnumerable)vector).Cast<int>().ToArray());
        using var vectorClone = vector.Clone();
        using var vectorInterfaceClone = (Std.Vector<int>)((ICloneable)vector).Clone();
        Assert.Equal(vector.ToArray(), vectorClone.ToArray());
        Assert.Equal(vector.ToArray(), vectorInterfaceClone.ToArray());
        vector.Clear();
        Assert.Empty(vector);
        Assert.Throws<ArgumentNullException>(() => new Std.Vector<int>((IEnumerable<int>)null!));
        ExerciseVectorWrapperFactories(vectorProvider);

        using var list = new Std.List<int>(new ReadOnlySpan<int>([5, 6, 7]));
        Assert.Equal(listProvider, list.Provider);
        list.Add(8);
        list.Insert(1, 50);
        list[1] = 60;
        Assert.Equal(60, list[1]);
        Assert.True(list.Remove(60));
        list.RemoveAt(0);
        Assert.Equal(new[] { 6, 7, 8 }, list.ToArray());
        using var listClone = list.Clone();
        using var listInterfaceClone = (Std.List<int>)((ICloneable)list).Clone();
        Assert.Equal(list.ToArray(), listClone.ToArray());
        Assert.Equal(list.ToArray(), listInterfaceClone.ToArray());
        Assert.False(list.IsNull);
        list.Clear();
        Assert.Empty(list);
        Assert.Throws<ArgumentNullException>(() => new Std.List<int>((IEnumerable<int>)null!));
        ExerciseListWrapperFactories(listProvider);

        using var array = new Std.Array<int>(new ReadOnlySpan<int>([1, 2, 3]));
        Assert.Equal(fixedProvider, array.Provider);
        Assert.Equal(3, array.Length);
        Assert.Equal(3, array.Count);
        Assert.False(array.IsReadOnly);
        Assert.Equal(2, array[1]);
        array[1] = 22;
        Assert.Equal(new[] { 1, 22, 3 }, array.ToArray());
        Assert.Equal(1, array.IndexOf(22));
        Assert.Contains(3, array);
        Assert.Equal(new NativeArrayAbi(array.Handle, 3), array.ToNativeArrayAbi());
        using var arrayClone = array.Clone();
        using var arrayInterfaceClone = (Std.Array<int>)((ICloneable)array).Clone();
        Assert.Equal(array.ToArray(), arrayClone.ToArray());
        Assert.Equal(array.ToArray(), arrayInterfaceClone.ToArray());
        Assert.Throws<ArgumentException>(() => new Std.Array<int>(0, 2, false, fixedProvider));
        Assert.Throws<ArgumentNullException>(() => new Std.Array<int>((IEnumerable<int>)null!));
        ExerciseArrayWrapperFactories(fixedProvider);

        using var map = new Std.Map<string, int>(new[]
        {
            new KeyValuePair<string, int>("one", 1),
            new KeyValuePair<string, int>("two", 2)
        });
        ExerciseMap(map, mapProvider);
        ExerciseMapWrapperFactories(mapProvider);

        using var unordered = new Std.UnorderedMap<string, int>(new[]
        {
            new KeyValuePair<string, int>("one", 1),
            new KeyValuePair<string, int>("two", 2)
        });
        ExerciseUnorderedMap(unordered, unorderedProvider);
        ExerciseUnorderedMapWrapperFactories(unorderedProvider);
    }

    [Fact]
    public void Std_provider_contract_defaults_are_exercised_by_minimal_e2e_providers()
    {
        var stringProvider = new MinimalStringProvider(ModuleId, "minimal-string");
        Assert.Throws<NotSupportedException>(() => ((StdInfrastructure.IStringProvider)stringProvider).Clone(1));
        Assert.Equal(0, ((StdInfrastructure.IStringProvider)stringProvider).GetData(1));

        var sequenceProvider = new MinimalSequenceProvider(ModuleId, "minimal-sequence");
        Assert.Throws<NotSupportedException>(() => ((StdInfrastructure.ISequenceProvider<int>)sequenceProvider).Clone(1));

        var fixedProvider = new MinimalFixedArrayProvider(ModuleId, "minimal-fixed", 1);
        Assert.Throws<NotSupportedException>(() => ((StdInfrastructure.IFixedArrayProvider<int>)fixedProvider).Clone(1));

        var dynamicProvider = new MinimalDynamicArrayProvider(ModuleId, "minimal-dynamic");
        Assert.Throws<NotSupportedException>(() => ((StdInfrastructure.IDynamicArrayProvider<int>)dynamicProvider).Clone(1));
        Assert.Null(((StdInfrastructure.IDynamicArrayProvider<int>)dynamicProvider).GetLength(1));

        var mapProvider = new MinimalMapProvider(ModuleId, "minimal-map");
        Assert.Throws<NotSupportedException>(() => ((StdInfrastructure.IDictionaryProvider<string, int>)mapProvider).Clone(1));
    }

    private static void Register<TProvider>(TProvider provider)
        where TProvider : class, StdInfrastructure.IProvider
    {
        StdInfrastructure.ProviderRegistry.Register<TProvider>(provider);
    }

    private static void ExerciseVectorWrapperFactories(StdInfrastructure.IVectorProvider<int> provider)
    {
        var handle = provider.Create();
        using var explicitWrapper = Std.Vector<int>.CreateManagedWrapper(handle, true, provider);
        Assert.Same(explicitWrapper, Std.Vector<int>.CreateManagedWrapper(handle, true, provider));
        using var moduleWrapper = Std.Vector<int>.CreateManagedWrapper(provider.Create(), true, ModuleId);
        using var defaultWrapper = Std.Vector<int>.CreateManagedWrapper(provider.Create(), true);
        using var borrowed = Std.Vector<int>.CreateManagedWrapper(provider.Create(), false, provider);
        Assert.False(borrowed.OwnsHandle);
    }

    private static void ExerciseListWrapperFactories(StdInfrastructure.IListProvider<int> provider)
    {
        var handle = provider.Create();
        using var explicitWrapper = Std.List<int>.CreateManagedWrapper(handle, true, provider);
        Assert.Same(explicitWrapper, Std.List<int>.CreateManagedWrapper(handle, true, provider));
        using var moduleWrapper = Std.List<int>.CreateManagedWrapper(provider.Create(), true, ModuleId);
        using var defaultWrapper = Std.List<int>.CreateManagedWrapper(provider.Create(), true);
        using var borrowed = Std.List<int>.CreateManagedWrapper(provider.Create(), false, provider);
        Assert.False(borrowed.OwnsHandle);
    }

    private static void ExerciseArrayWrapperFactories(StdInfrastructure.IFixedArrayProvider<int> provider)
    {
        using var byLength = Std.Array<int>.CreateManagedWrapper(provider.Create(), 3, true, provider);
        using var byProvider = Std.Array<int>.CreateManagedWrapper(provider.Create(), false, provider);
        using var byAbi = Std.Array<int>.CreateManagedWrapper(new NativeArrayAbi(provider.Create(), 3), false, provider);
        using var byAbiModule = Std.Array<int>.CreateManagedWrapper(new NativeArrayAbi(provider.Create(), 3), false, ModuleId);
        using var byLengthModule = Std.Array<int>.CreateManagedWrapper(provider.Create(), 3, true, ModuleId);
        using var byDefaultLength = Std.Array<int>.CreateManagedWrapper(provider.Create(), 3, true);
        using var byDefaultProviderLength = Std.Array<int>.CreateManagedWrapper(provider.Create(), true);
        Assert.False(byProvider.OwnsHandle);
        Assert.Equal(3, byAbi.Length);
    }

    private static void ExerciseMap(Std.Map<string, int> map, StdInfrastructure.IMapProvider<string, int> provider)
    {
        Assert.Equal(provider, map.Provider);
        Assert.Equal(2, map.Count);
        Assert.False(map.IsReadOnly);
        Assert.Equal(1, map["one"]);
        map["one"] = 10;
        map.Add("three", 3);
        map.Add(new KeyValuePair<string, int>("four", 4));
        Assert.True(map.ContainsKey("three"));
        Assert.True(map.TryGetValue("four", out var four));
        Assert.Equal(4, four);
        Assert.Equal(new[] { "one", "two", "three", "four" }, map.Keys);
        Assert.Equal(new[] { 10, 2, 3, 4 }, map.Values);
        Assert.Contains(new KeyValuePair<string, int>("three", 3), map);
        Assert.DoesNotContain(new KeyValuePair<string, int>("three", 99), map);
        var copy = new KeyValuePair<string, int>[5];
        map.CopyTo(copy, 1);
        Assert.Equal(new KeyValuePair<string, int>("one", 10), copy[1]);
        Assert.Equal(4, map.Cast<KeyValuePair<string, int>>().Count());
        Assert.Equal(4, ((IEnumerable)map).Cast<KeyValuePair<string, int>>().Count());
        using var clone = map.Clone();
        using var interfaceClone = (Std.Map<string, int>)((ICloneable)map).Clone();
        Assert.Equal(map["one"], clone["one"]);
        Assert.Equal(map["one"], interfaceClone["one"]);
        Assert.True(map.Remove(new KeyValuePair<string, int>("four", 4)));
        Assert.False(map.Remove(new KeyValuePair<string, int>("four", 4)));
        Assert.True(map.Remove("three"));
        Assert.False(map.Remove("missing"));
        Assert.Throws<ArgumentException>(() => map.Add("one", 11));
        Assert.Throws<KeyNotFoundException>(() => _ = map["missing"]);
        Assert.Throws<ArgumentNullException>(() => _ = map[(string)null!]);
        Assert.Throws<ArgumentNullException>(() => map[(string)null!] = 1);
        Assert.Throws<ArgumentNullException>(() => map.Add(null!, 1));
        Assert.Throws<ArgumentNullException>(() => map.ContainsKey(null!));
        Assert.Throws<ArgumentNullException>(() => map.Remove(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.CopyTo([], -1));
        Assert.Throws<ArgumentException>(() => map.CopyTo(new KeyValuePair<string, int>[1], 0));
        Assert.Throws<ArgumentNullException>(() => map.CopyTo(null!, 0));
        map.Clear();
        Assert.Empty(map);
    }

    private static void ExerciseMapWrapperFactories(StdInfrastructure.IMapProvider<string, int> provider)
    {
        var handle = provider.Create();
        using var explicitWrapper = Std.Map<string, int>.CreateManagedWrapper(handle, true, provider);
        Assert.Same(explicitWrapper, Std.Map<string, int>.CreateManagedWrapper(handle, true, provider));
        using var moduleWrapper = Std.Map<string, int>.CreateManagedWrapper(provider.Create(), true, ModuleId);
        using var defaultWrapper = Std.Map<string, int>.CreateManagedWrapper(provider.Create(), true);
    }

    private static void ExerciseUnorderedMap(Std.UnorderedMap<string, int> map, StdInfrastructure.IUnorderedMapProvider<string, int> provider)
    {
        Assert.Equal(provider, map.Provider);
        Assert.Equal(2, map.Count);
        map["one"] = 11;
        map.Add("three", 3);
        Assert.True(map.ContainsKey("three"));
        Assert.Equal(11, map["one"]);
        Assert.True(map.TryGetValue("three", out var three));
        Assert.Equal(3, three);
        using var clone = map.Clone();
        using var interfaceClone = (Std.UnorderedMap<string, int>)((ICloneable)map).Clone();
        Assert.Equal(map["one"], clone["one"]);
        Assert.Equal(map["one"], interfaceClone["one"]);
        Assert.True(map.Remove("three"));
        Assert.False(map.Remove("missing"));
        Assert.Throws<ArgumentException>(() => map.Add("one", 12));
        Assert.Contains(new KeyValuePair<string, int>("one", 11), map);
        Assert.Equal(2, map.Keys.Count);
        Assert.Equal(2, map.Values.Count);
        map.Clear();
        Assert.Empty(map);
    }

    private static void ExerciseUnorderedMapWrapperFactories(StdInfrastructure.IUnorderedMapProvider<string, int> provider)
    {
        var handle = provider.Create();
        using var explicitWrapper = Std.UnorderedMap<string, int>.CreateManagedWrapper(handle, true, provider);
        Assert.Same(explicitWrapper, Std.UnorderedMap<string, int>.CreateManagedWrapper(handle, true, provider));
        using var moduleWrapper = Std.UnorderedMap<string, int>.CreateManagedWrapper(provider.Create(), true, ModuleId);
        using var defaultWrapper = Std.UnorderedMap<string, int>.CreateManagedWrapper(provider.Create(), true);
    }

    private abstract class ProviderBase
    {
        protected ProviderBase(string moduleId, string shapeKey)
        {
            ModuleId = moduleId;
            ShapeKey = shapeKey;
        }

        public string ModuleId { get; }
        public string ShapeKey { get; }
    }

    private sealed class MemoryStringProvider : ProviderBase, StdInfrastructure.IStringProvider
    {
        private readonly Dictionary<nint, byte[]> _values = [];
        private readonly Dictionary<nint, nint> _pointers = [];
        private nint _nextHandle = 0x2100;

        public MemoryStringProvider(string moduleId, string shapeKey, bool exposeData)
            : base(moduleId, shapeKey)
        {
            ExposeData = exposeData;
        }

        public bool ExposeData { get; set; }
        public int DeleteCountForBorrowedHandles { get; private set; }

        public nint Create()
        {
            var handle = ++_nextHandle;
            _values.Add(handle, []);
            return handle;
        }

        public nint GetHandleFor(string value) => _values.Keys.FirstOrDefault();

        public nint Clone(nint handle)
        {
            var clone = Create();
            _values[clone] = [.. _values[handle]];
            return clone;
        }

        public void Delete(nint handle)
        {
            FreePointer(handle);
            _values.Remove(handle);
        }

        public ulong GetCount(nint handle) => (ulong)_values[handle].Length;

        public nint GetData(nint handle)
        {
            if (!ExposeData || _values[handle].Length == 0)
            {
                return 0;
            }

            if (!_pointers.TryGetValue(handle, out var pointer))
            {
                pointer = Marshal.AllocHGlobal(_values[handle].Length);
                _pointers.Add(handle, pointer);
            }

            Marshal.Copy(_values[handle], 0, pointer, _values[handle].Length);
            return pointer;
        }

        public byte GetAt(nint handle, ulong index) => _values[handle][checked((int)index)];

        public void Clear(nint handle)
        {
            _values[handle] = [];
            FreePointer(handle);
        }

        public void Add(nint handle, byte value)
        {
            _values[handle] = [.. _values[handle], value];
            FreePointer(handle);
        }

        private void FreePointer(nint handle)
        {
            if (_pointers.Remove(handle, out var pointer))
            {
                Marshal.FreeHGlobal(pointer);
            }
        }
    }

    private abstract class SequenceProvider<T> : ProviderBase, StdInfrastructure.ISequenceProvider<T>
    {
        private readonly Dictionary<nint, System.Collections.Generic.List<T>> _values = [];
        private nint _nextHandle = 0x2200;

        protected SequenceProvider(string moduleId, string shapeKey)
            : base(moduleId, shapeKey)
        {
        }

        public nint Create()
        {
            var handle = ++_nextHandle;
            _values.Add(handle, []);
            return handle;
        }

        public nint Clone(nint handle)
        {
            var clone = Create();
            _values[clone].AddRange(_values[handle]);
            return clone;
        }

        public void Delete(nint handle) => _values.Remove(handle);
        public ulong GetCount(nint handle) => (ulong)_values[handle].Count;
        public void Clear(nint handle) => _values[handle].Clear();
        public T GetAt(nint handle, ulong index) => _values[handle][checked((int)index)];
        public void SetAt(nint handle, ulong index, T value) => _values[handle][checked((int)index)] = value;
        public void Add(nint handle, T value) => _values[handle].Add(value);
        public void InsertAt(nint handle, ulong index, T value) => _values[handle].Insert(checked((int)index), value);
        public void RemoveAt(nint handle, ulong index) => _values[handle].RemoveAt(checked((int)index));
    }

    private sealed class StdVectorProvider<T> : SequenceProvider<T>, StdInfrastructure.IVectorProvider<T>
    {
        public StdVectorProvider(string moduleId, string shapeKey) : base(moduleId, shapeKey) { }
    }

    private sealed class StdListProvider<T> : SequenceProvider<T>, StdInfrastructure.IListProvider<T>
    {
        public StdListProvider(string moduleId, string shapeKey) : base(moduleId, shapeKey) { }
    }

    private sealed class StdFixedArrayProvider<T> : ProviderBase, StdInfrastructure.IFixedArrayProvider<T>
    {
        private readonly Dictionary<nint, T[]> _values = [];
        private nint _nextHandle = 0x2300;

        public StdFixedArrayProvider(string moduleId, string shapeKey, int length)
            : base(moduleId, shapeKey)
        {
            Length = length;
        }

        public int Length { get; }

        public nint Create()
        {
            var handle = ++_nextHandle;
            _values.Add(handle, new T[Length]);
            return handle;
        }

        public nint Clone(nint handle)
        {
            var clone = Create();
            System.Array.Copy(_values[handle], _values[clone], Length);
            return clone;
        }

        public void Delete(nint handle) => _values.Remove(handle);
        public T GetAt(nint handle, nuint index) => _values[handle][checked((int)index)];
        public void SetAt(nint handle, nuint index, T value) => _values[handle][checked((int)index)] = value;
    }

    private abstract class MapProvider<TKey, TValue> : ProviderBase, StdInfrastructure.IDictionaryProvider<TKey, TValue>
        where TKey : notnull
    {
        private readonly Dictionary<nint, System.Collections.Generic.List<KeyValuePair<TKey, TValue>>> _values = [];
        private nint _nextHandle = 0x2400;

        protected MapProvider(string moduleId, string shapeKey) : base(moduleId, shapeKey) { }

        public nint Create()
        {
            var handle = ++_nextHandle;
            _values.Add(handle, []);
            return handle;
        }

        public nint Clone(nint handle)
        {
            var clone = Create();
            _values[clone].AddRange(_values[handle]);
            return clone;
        }

        public void Delete(nint handle) => _values.Remove(handle);
        public ulong GetCount(nint handle) => (ulong)_values[handle].Count;
        public void Clear(nint handle) => _values[handle].Clear();
        public bool ContainsKey(nint handle, TKey key) => FindIndex(handle, key) >= 0;
        public TValue GetValue(nint handle, TKey key) => _values[handle][FindIndexOrThrow(handle, key)].Value;

        public void SetValue(nint handle, TKey key, TValue value)
        {
            var index = FindIndex(handle, key);
            if (index < 0)
            {
                _values[handle].Add(new KeyValuePair<TKey, TValue>(key, value));
            }
            else
            {
                _values[handle][index] = new KeyValuePair<TKey, TValue>(key, value);
            }
        }

        public bool Add(nint handle, TKey key, TValue value)
        {
            if (FindIndex(handle, key) >= 0)
            {
                return false;
            }

            _values[handle].Add(new KeyValuePair<TKey, TValue>(key, value));
            return true;
        }

        public bool Remove(nint handle, TKey key)
        {
            var index = FindIndex(handle, key);
            if (index < 0)
            {
                return false;
            }

            _values[handle].RemoveAt(index);
            return true;
        }

        public TKey GetKeyAt(nint handle, ulong index) => _values[handle][checked((int)index)].Key;
        public TValue GetValueAt(nint handle, ulong index) => _values[handle][checked((int)index)].Value;

        private int FindIndex(nint handle, TKey key)
            => _values[handle].FindIndex(item => EqualityComparer<TKey>.Default.Equals(item.Key, key));

        private int FindIndexOrThrow(nint handle, TKey key)
        {
            var index = FindIndex(handle, key);
            if (index < 0)
            {
                throw new KeyNotFoundException();
            }

            return index;
        }
    }

    private sealed class StdMapProvider : MapProvider<string, int>, StdInfrastructure.IMapProvider<string, int>
    {
        public StdMapProvider(string moduleId, string shapeKey) : base(moduleId, shapeKey) { }
    }

    private sealed class StdUnorderedMapProvider : MapProvider<string, int>, StdInfrastructure.IUnorderedMapProvider<string, int>
    {
        public StdUnorderedMapProvider(string moduleId, string shapeKey) : base(moduleId, shapeKey) { }
    }

    private sealed class MinimalStringProvider : ProviderBase, StdInfrastructure.IStringProvider
    {
        public MinimalStringProvider(string moduleId, string shapeKey) : base(moduleId, shapeKey) { }
        public nint Create() => 1;
        public void Delete(nint handle) { }
        public ulong GetCount(nint handle) => 0;
        public byte GetAt(nint handle, ulong index) => 0;
        public void Clear(nint handle) { }
        public void Add(nint handle, byte value) { }
    }

    private sealed class MinimalSequenceProvider : ProviderBase, StdInfrastructure.IVectorProvider<int>
    {
        public MinimalSequenceProvider(string moduleId, string shapeKey) : base(moduleId, shapeKey) { }
        public nint Create() => 1;
        public void Delete(nint handle) { }
        public ulong GetCount(nint handle) => 0;
        public void Clear(nint handle) { }
        public int GetAt(nint handle, ulong index) => 0;
        public void SetAt(nint handle, ulong index, int value) { }
        public void Add(nint handle, int value) { }
        public void InsertAt(nint handle, ulong index, int value) { }
        public void RemoveAt(nint handle, ulong index) { }
    }

    private sealed class MinimalFixedArrayProvider : ProviderBase, StdInfrastructure.IFixedArrayProvider<int>
    {
        public MinimalFixedArrayProvider(string moduleId, string shapeKey, int length) : base(moduleId, shapeKey) => Length = length;
        public int Length { get; }
        public nint Create() => 1;
        public void Delete(nint handle) { }
        public int GetAt(nint handle, nuint index) => 0;
        public void SetAt(nint handle, nuint index, int value) { }
    }

    private sealed class MinimalDynamicArrayProvider : ProviderBase, StdInfrastructure.IDynamicArrayProvider<int>
    {
        public MinimalDynamicArrayProvider(string moduleId, string shapeKey) : base(moduleId, shapeKey) { }
        public nint Create(int length) => 1;
        public void Delete(nint handle) { }
        public int GetAt(nint handle, nuint index) => 0;
        public void SetAt(nint handle, nuint index, int value) { }
    }

    private sealed class MinimalMapProvider : ProviderBase, StdInfrastructure.IMapProvider<string, int>
    {
        public MinimalMapProvider(string moduleId, string shapeKey) : base(moduleId, shapeKey) { }
        public nint Create() => 1;
        public void Delete(nint handle) { }
        public ulong GetCount(nint handle) => 0;
        public void Clear(nint handle) { }
        public bool ContainsKey(nint handle, string key) => false;
        public int GetValue(nint handle, string key) => 0;
        public void SetValue(nint handle, string key, int value) { }
        public bool Add(nint handle, string key, int value) => true;
        public bool Remove(nint handle, string key) => false;
        public string GetKeyAt(nint handle, ulong index) => "";
        public int GetValueAt(nint handle, ulong index) => 0;
    }
}
