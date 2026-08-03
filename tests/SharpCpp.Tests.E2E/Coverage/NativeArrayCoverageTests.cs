using System.Collections.Generic;
using System.Linq;

using SharpCpp;
using SharpCpp.Support;

using Xunit;

namespace SharpCpp.Tests.E2E.Coverage;

public sealed class NativeArrayCoverageTests
{
    [Fact]
    public void NativeArray_registration_wrappers_marshallers_and_dynamic_shapes_are_exercised_e2e()
    {
        NativeArray<FixedValue>.__Register(
            3,
            FixedStorage.Create,
            FixedStorage.Delete,
            FixedStorage.Get,
            FixedStorage.Set);

        Assert.Throws<ArgumentOutOfRangeException>(() => NativeArray<FixedValue>.__Register(
            0,
            FixedStorage.Create,
            FixedStorage.Delete,
            FixedStorage.Get,
            FixedStorage.Set));
        Assert.Throws<ArgumentNullException>(() => NativeArray<FixedValue>.__Register(4, null!, FixedStorage.Delete, FixedStorage.Get, FixedStorage.Set));
        Assert.Throws<ArgumentNullException>(() => NativeArray<FixedValue>.__Register(4, FixedStorage.Create, null!, FixedStorage.Get, FixedStorage.Set));
        Assert.Throws<ArgumentNullException>(() => NativeArray<FixedValue>.__Register(4, FixedStorage.Create, FixedStorage.Delete, null!, FixedStorage.Set));
        Assert.Throws<ArgumentNullException>(() => NativeArray<FixedValue>.__Register(4, FixedStorage.Create, FixedStorage.Delete, FixedStorage.Get, null!));

        using var fromEnumerable = new NativeArray<FixedValue>((IEnumerable<FixedValue>)[new(1), new(2), new(3)]);
        Assert.Equal(3, fromEnumerable.Length);
        Assert.False(fromEnumerable.IsNull);
        Assert.True(fromEnumerable.OwnsHandle);
        Assert.Equal(new FixedValue(2), fromEnumerable[1]);
        fromEnumerable[1] = new FixedValue(20);
        Assert.Equal(new[] { new FixedValue(1), new FixedValue(20), new FixedValue(3) }, fromEnumerable.ToArray());
        Assert.Equal(new NativeArrayAbi(fromEnumerable.Handle, 3), fromEnumerable.ToNativeArrayAbi());
        Assert.Contains(new FixedValue(3), fromEnumerable);
        Assert.Equal(1, fromEnumerable.IndexOf(new FixedValue(20)));
        Assert.Equal(new[] { new FixedValue(1), new FixedValue(20), new FixedValue(3) }, fromEnumerable.Cast<FixedValue>().ToArray());

        using var fromSpan = new NativeArray<FixedValue>(new ReadOnlySpan<FixedValue>([new(4), new(5), new(6)]));
        Assert.Equal(new FixedValue(5), fromSpan[1]);
        Assert.Throws<ArgumentNullException>(() => new NativeArray<FixedValue>((IEnumerable<FixedValue>)null!));

        var inAbi = __NativeArrayInMarshaller<FixedValue>.ConvertToUnmanaged(fromEnumerable);
        Assert.Equal(fromEnumerable.Handle, inAbi.Handle);
        Assert.Equal((nuint)3, inAbi.Length);
        Assert.Throws<ArgumentNullException>(() => __NativeArrayInMarshaller<FixedValue>.ConvertToUnmanaged(null!));

        var borrowedAbi = FixedStorage.Create();
        using var borrowed = __NativeArrayReturnMarshaller<FixedValue>.ConvertToManaged(borrowedAbi);
        Assert.False(borrowed.OwnsHandle);
        Assert.Equal(3, borrowed.Length);
        borrowed[0] = new FixedValue(40);
        Assert.Equal(new FixedValue(40), borrowed[0]);

        var ownedAbi = FixedStorage.Create();
        using var owned = __NativeArrayOwnedReturnMarshaller<FixedValue>.ConvertToManaged(ownedAbi);
        Assert.True(owned.OwnsHandle);
        Assert.Equal(3, owned.Length);

        var cachedAbi = FixedStorage.Create();
        using var cached = NativeArray<FixedValue>.CreateManagedWrapper(cachedAbi, ownsHandle: false);
        Assert.Same(cached, NativeArray<FixedValue>.__CreateManagedWrapper(cachedAbi, ownsHandle: false));
        using var cachedByHandle = NativeArray<FixedValue>.CreateManagedWrapper(cachedAbi.Handle, ownsHandle: false);
        Assert.Same(cached, cachedByHandle);
        using var invalid = NativeArray<FixedValue>.__CreateManagedWrapper(new NativeArrayAbi(0, 3), ownsHandle: false);
        Assert.True(invalid.IsNull);
        using var zeroLength = NativeArray<FixedValue>.CreateManagedWrapper((nint)0x2fff, ownsHandle: false);
        Assert.Equal(0, zeroLength.Length);

        NativeArray<DynamicValue>.__RegisterDynamic(
            DynamicStorage.Create,
            DynamicStorage.Delete,
            DynamicStorage.Get,
            DynamicStorage.Set);
        using var dynamic = new NativeArray<DynamicValue>(2);
        Assert.Equal(2, dynamic.Length);
        dynamic[0] = new DynamicValue(7);
        Assert.Equal(new DynamicValue(7), dynamic[0]);

        Assert.Throws<NotSupportedException>(() => new NativeArray<UnsupportedValue>(1));
        using var unsupported = NativeArray<UnsupportedValue>.CreateManagedWrapper(new NativeArrayAbi((nint)0x2ffe, 1), ownsHandle: true);
        Assert.True(unsupported.OwnsHandle);
    }

    private readonly record struct FixedValue(int Value);
    private readonly record struct DynamicValue(int Value);
    private readonly record struct UnsupportedValue(int Value);

    private static class FixedStorage
    {
        private static readonly Dictionary<nint, FixedValue[]> Values = [];
        private static nint _nextHandle = 0x3100;

        public static NativeArrayAbi Create()
        {
            var handle = ++_nextHandle;
            Values.Add(handle, new FixedValue[3]);
            return new NativeArrayAbi(handle, 3);
        }

        public static void Delete(NativeArrayAbi abi) => Values.Remove(abi.Handle);
        public static FixedValue Get(NativeArrayAbi abi, nuint index) => Values[abi.Handle][checked((int)index)];
        public static void Set(NativeArrayAbi abi, nuint index, FixedValue value) => Values[abi.Handle][checked((int)index)] = value;
    }

    private static class DynamicStorage
    {
        private static readonly Dictionary<nint, DynamicValue[]> Values = [];
        private static nint _nextHandle = 0x3200;

        public static NativeArrayAbi Create(nuint length)
        {
            var handle = ++_nextHandle;
            Values.Add(handle, new DynamicValue[checked((int)length)]);
            return new NativeArrayAbi(handle, length);
        }

        public static void Delete(NativeArrayAbi abi) => Values.Remove(abi.Handle);
        public static DynamicValue Get(NativeArrayAbi abi, nuint index) => Values[abi.Handle][checked((int)index)];
        public static void Set(NativeArrayAbi abi, nuint index, DynamicValue value) => Values[abi.Handle][checked((int)index)] = value;
    }
}
