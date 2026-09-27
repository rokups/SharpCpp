using System.Collections.Generic;
using System.Linq;

using SharpCpp.Support;
using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Lifetime;

public abstract class RuntimeBreadthManagedObject : NativeObject
{
    protected RuntimeBreadthManagedObject(nint handle, bool ownsHandle)
        : base(handle, ownsHandle)
    {
    }

    public nint Handle => DangerousGetHandle();

    public bool OwnsHandle => OwnsNativeHandle;

    public bool IsNull => IsClosed || IsInvalid;

    protected override bool ManagesInstanceCacheLifecycle => true;

    protected void DisconnectFromNativeRuntime()
    {
        DisposeFromNativeRuntimeCore();
    }
}

public sealed class RuntimeBreadthTests
{
    private sealed class ManagedLifecycle : RuntimeBreadthLifecycle
    {
        public ManagedLifecycle(int value)
            : base(value)
        {
        }

        public override void OnNativeDestroying()
        {
            DisconnectFromNativeRuntime();
        }
    }

    private sealed class ManagedTransferDirector : RuntimeBreadthTransferDirector
    {
        private readonly bool _returnNull;

        public ManagedTransferDirector(bool returnNull)
        {
            _returnNull = returnNull;
        }

        public RuntimeBreadthPayload? LastPayload { get; private set; }

        public override RuntimeBreadthPayload CreatePayload(int value)
        {
            if (_returnNull)
                return null!;

            LastPayload = E2EBindings.RuntimeBreadthMakePayload(value + 2);
            return LastPayload;
        }
    }

    [Fact]
    public void Generated_cache_promotes_unregisters_and_replaces_closed_native_wrappers()
    {
        Assert.Equal(-1, E2EBindings.RuntimeBreadthReadCacheValue(null!));

        var borrowed = E2EBindings.RuntimeBreadthGetCacheBorrowed(31);
        var cachedBorrowed = E2EBindings.RuntimeBreadthGetCacheBorrowed(32);

        Assert.Same(borrowed, cachedBorrowed);
        Assert.False(borrowed.OwnsHandle);
        Assert.Equal(32, E2EBindings.RuntimeBreadthReadCacheValue(borrowed));

        var promoted = E2EBindings.RuntimeBreadthTakeCacheOwned(41);

        Assert.Same(borrowed, promoted);
        Assert.True(promoted.OwnsHandle);
        Assert.Equal(41, E2EBindings.RuntimeBreadthReadCacheValue(promoted));

        var alreadyOwned = E2EBindings.RuntimeBreadthTakeCacheOwned(42);
        Assert.Same(promoted, alreadyOwned);
        Assert.Equal(42, E2EBindings.RuntimeBreadthReadCacheValue(alreadyOwned));

        promoted.Dispose();

        var staleReplacement = E2EBindings.RuntimeBreadthGetCacheBorrowed(51);
        var cachedReplacement = E2EBindings.RuntimeBreadthGetCacheBorrowed(52);

        Assert.NotSame(promoted, staleReplacement);
        Assert.Same(staleReplacement, cachedReplacement);
        Assert.Equal(52, E2EBindings.RuntimeBreadthReadCacheValue(staleReplacement));

        staleReplacement.Dispose();

        var afterUnregister = E2EBindings.RuntimeBreadthGetCacheBorrowed(61);
        Assert.NotSame(staleReplacement, afterUnregister);
        Assert.Equal(61, E2EBindings.RuntimeBreadthReadCacheValue(afterUnregister));
        afterUnregister.Dispose();
    }

    [Fact]
    public void Base_typed_native_return_uses_the_registered_derived_wrapper()
    {
        Assert.Null(E2EBindings.RuntimeBreadthGetPolymorphicNull());

        using var value = E2EBindings.RuntimeBreadthGetPolymorphic(37);

        Assert.Equal(2, value.GetKind());
        Assert.Equal(37, value.GetValue());

        var derived = Assert.IsType<RuntimeBreadthPolyDerived>(value);
        Assert.Equal(42, derived.GetBonus());
    }

    [Fact]
    public void Native_destruction_disconnects_the_generated_managed_director()
    {
        var destroyedBefore = E2EBindings.RuntimeBreadthGetLifecycleDestroyedCount();
        var value = new ManagedLifecycle(73);

        Assert.Equal(73, value.GetValue());
        Assert.Equal(73, E2EBindings.RuntimeBreadthDestroyLifecycle(value));
        Assert.True(value.IsNull);
        Assert.Equal(destroyedBefore + 1, E2EBindings.RuntimeBreadthGetLifecycleDestroyedCount());

        value.Dispose();
    }

    [Fact]
    public void Owned_director_return_transfers_to_native_and_accepts_null()
    {
        var destroyedBefore = E2EBindings.RuntimeBreadthGetPayloadDestroyedCount();
        using var director = new ManagedTransferDirector(returnNull: false);

        Assert.Equal(42, director.InvokeAndDestroy(40));
        var transferred = Assert.IsType<RuntimeBreadthPayload>(director.LastPayload);
        Assert.False(transferred.OwnsHandle);
        Assert.Equal(destroyedBefore + 1, E2EBindings.RuntimeBreadthGetPayloadDestroyedCount());

        transferred.Dispose();
        Assert.Equal(destroyedBefore + 1, E2EBindings.RuntimeBreadthGetPayloadDestroyedCount());

        using var nullDirector = new ManagedTransferDirector(returnNull: true);
        Assert.Equal(-1, nullDirector.InvokeAndDestroy(99));
        Assert.Equal(destroyedBefore + 1, E2EBindings.RuntimeBreadthGetPayloadDestroyedCount());
    }

    [Fact]
    public void Generated_standard_library_providers_resolve_and_round_trip_native_storage()
    {
        using var nativeArray = E2EBindings.RuntimeBreadthMakeArray();
        Assert.Equal(15u, E2EBindings.RuntimeBreadthSumArray(nativeArray));
        Assert.Equal(15u, ((System.Collections.IEnumerable)nativeArray).Cast<uint>()
            .Aggregate(0u, static (sum, value) => sum + value));
        Assert.Equal(15u, E2EBindings.RuntimeBreadthSumArray(
            global::Std.Array<uint>.CreateManagedWrapper(nativeArray.Handle, ownsHandle: false)));

        using var managedArray = new global::Std.Array<uint>(
            (IEnumerable<uint>)new uint[] { 10u, 20u, 12u });
        Assert.Equal(42u, E2EBindings.RuntimeBreadthSumArray(managedArray));

        using var nativeVector = E2EBindings.RuntimeBreadthMakeVector();
        Assert.Equal(12u, E2EBindings.RuntimeBreadthSumVector(nativeVector));
        Assert.Equal(12u, ((System.Collections.IEnumerable)nativeVector).Cast<uint>()
            .Aggregate(0u, static (sum, value) => sum + value));
        Assert.Equal(12u, E2EBindings.RuntimeBreadthSumVector(
            global::Std.Vector<uint>.CreateManagedWrapper(nativeVector.Handle, ownsHandle: false)));

        using var managedVector = new global::Std.Vector<uint>(
            (IEnumerable<uint>)new uint[] { 9u, 14u, 19u });
        Assert.Equal(42u, E2EBindings.RuntimeBreadthSumVector(managedVector));

        using var nativeList = E2EBindings.RuntimeBreadthMakeList();
        Assert.Equal(24u, E2EBindings.RuntimeBreadthSumList(nativeList));
        Assert.Equal(24u, E2EBindings.RuntimeBreadthSumList(
            global::Std.List<uint>.CreateManagedWrapper(nativeList.Handle, ownsHandle: false)));

        using var managedList = new global::Std.List<uint>((IEnumerable<uint>)new uint[] { 20u, 22u });
        Assert.Equal(42u, E2EBindings.RuntimeBreadthSumList(managedList));

        using var nativeMap = E2EBindings.RuntimeBreadthMakeMap();
        Assert.Equal(17u, E2EBindings.RuntimeBreadthSumMap(nativeMap));
        Assert.Equal(17u, nativeMap.Sum(static pair => pair.Key + pair.Value));
        Assert.Equal(2, ((System.Collections.IEnumerable)nativeMap)
            .Cast<KeyValuePair<uint, uint>>().Count());
        Assert.Equal(17u, E2EBindings.RuntimeBreadthSumMap(
            global::Std.Map<uint, uint>.CreateManagedWrapper(nativeMap.Handle, ownsHandle: false)));

        using var managedMap = new global::Std.Map<uint, uint>(
            new[] { new KeyValuePair<uint, uint>(10u, 11u), new KeyValuePair<uint, uint>(9u, 12u) });
        Assert.Equal(42u, E2EBindings.RuntimeBreadthSumMap(managedMap));

        using var nativeUnorderedMap = E2EBindings.RuntimeBreadthMakeUnorderedMap();
        Assert.Equal(21u, E2EBindings.RuntimeBreadthSumUnorderedMap(nativeUnorderedMap));
        Assert.Equal(21u, E2EBindings.RuntimeBreadthSumUnorderedMap(
            global::Std.UnorderedMap<uint, uint>.CreateManagedWrapper(nativeUnorderedMap.Handle, ownsHandle: false)));

        using var managedUnorderedMap = new global::Std.UnorderedMap<uint, uint>(
            new[] { new KeyValuePair<uint, uint>(8u, 13u), new KeyValuePair<uint, uint>(7u, 14u) });
        Assert.Equal(42u, E2EBindings.RuntimeBreadthSumUnorderedMap(managedUnorderedMap));
        managedUnorderedMap.Clear();
        Assert.Equal(0u, E2EBindings.RuntimeBreadthSumUnorderedMap(managedUnorderedMap));

    }

    [Fact]
    public void Generated_standard_library_string_provider_round_trips_native_storage()
    {
        using var nativeString = E2EBindings.RuntimeBreadthMakeString();
        Assert.Equal("runtime", nativeString.ToString());
        Assert.Equal("runtime", new string(((System.Collections.IEnumerable)nativeString).Cast<char>().ToArray()));
        Assert.Equal(772u, E2EBindings.RuntimeBreadthSumString(nativeString));
        Assert.Equal(E2EBindings.RuntimeBreadthSumString(nativeString),
            E2EBindings.RuntimeBreadthSumString(
                global::Std.String.CreateManagedWrapper(nativeString.Handle, ownsHandle: false)));
    }

    [Fact]
    public void Promoting_an_owning_or_disposed_wrapper_does_not_change_ownership()
    {
        var owned = new Container_aliases.ActionBuilder();
        Assert.True(owned.TryPromoteToOwningHandle());
        owned.Dispose();
        Assert.False(owned.TryPromoteToOwningHandle());
    }
}
