using System.Collections;
using System.Collections.Generic;

using SharpCpp.Support;

using Xunit;

namespace SharpCpp.Tests.E2E.Coverage;

public sealed class SupportEdgeCoverageTests
{
    [Fact]
    public void StringBase_default_virtuals_constructor_and_non_generic_enumerator_are_exercised()
    {
        using var value = new ConstructorString((nint)0x4101, "initial");
        Assert.Equal("initial", value.ToString());
        value.Assign("updated"u8);
        Assert.Equal("updated", value.ToString());
        Assert.Equal(new[] { 'u', 'p', 'd', 'a', 't', 'e', 'd' }, value.ToArray());
        Assert.Equal(new[] { 'u', 'p', 'd', 'a', 't', 'e', 'd' }, ((IEnumerable)value).Cast<char>().ToArray());

        using var bare = new BareString((nint)0x4102);
        bare.InvokeDefaultVirtuals();
    }

    [Fact]
    public void NativeObjectRegistry_covers_zero_factory_and_invalid_instance_paths()
    {
        NativeObjectRegistry.RegisterType<RegistryWrapper>();
        using var created = NativeObjectRegistry.Create<RegistryWrapper>((nint)0x4201, ownsHandle: true);
        Assert.IsType<RegistryWrapper>(created);
        Assert.Same(created, NativeObjectRegistry.Create<RegistryWrapper>((nint)0x4201, ownsHandle: false));
        Assert.Null(NativeObjectRegistry.Create<RegistryWrapper>(0, ownsHandle: false));

        using var invalid = new RegistryWrapper(0, ownsHandle: false);
        NativeObjectRegistry.RegisterInstance(invalid);

        Assert.Throws<InvalidOperationException>(() => NativeObjectRegistry.RegisterType<NoFactoryWrapper>());
        Assert.Throws<InvalidOperationException>(() => NativeObjectRegistry.Create<NoFactoryWrapper>((nint)0x4202, ownsHandle: true));
    }

    private sealed class ConstructorString : StringBase
    {
        private static readonly Dictionary<nint, List<byte>> Values = [];

        public ConstructorString(nint handle, string value)
            : base(handle, ownsHandle: true, value)
        {
        }

        protected override ulong GetCountCore() => (ulong)Values[DangerousGetHandle()].Count;
        protected override nint GetDataCore() => 0;
        protected override void ClearCore() => Values[DangerousGetHandle()] = [];
        protected override byte GetAtCore(ulong index) => Values[DangerousGetHandle()][checked((int)index)];
        protected override void AddCore(byte value) => Values[DangerousGetHandle()].Add(value);
        protected override bool ReleaseNativeHandle(nint handle)
        {
            Values.Remove(handle);
            return true;
        }
    }

    private sealed class BareString : StringBase
    {
        public BareString(nint handle)
            : base(handle, ownsHandle: false)
        {
        }

        public void InvokeDefaultVirtuals()
        {
            Assert.Throws<NotImplementedException>(() => GetCountCore());
            Assert.Equal(0, GetDataCore());
            Assert.Throws<NotImplementedException>(() => ClearCore());
            Assert.Throws<NotImplementedException>(() => GetAtCore(0));
            Assert.Throws<NotImplementedException>(() => AddCore(0));
        }
    }

    private class RegistryWrapper : NativeObject, INativeObjectRegistration<RegistryWrapper>
    {
        private static readonly DefaultNativeInstanceCache Cache = new();

        public RegistryWrapper(nint handle, bool ownsHandle)
            : base(handle, ownsHandle)
        {
        }

        public static bool RegistrationIsPolymorphic => true;
        public static nuint RegistrationDynamicTypeId => 0x4201;
        public static Type RegistrationExpectedType => typeof(RegistryWrapper);
        public static INativeInstanceCache RegistrationInstanceCache => Cache;
        public static Func<nint, bool, NativeObject>? RegistrationCreateFactory => static (handle, ownsHandle) => new RegistryWrapper(handle, ownsHandle);
        public static NativeObjectMetadata RegistrationGetMetadata(nint handle) => new(handle, RegistrationDynamicTypeId);
    }

    private sealed class NoFactoryWrapper : NativeObject, INativeObjectRegistration<NoFactoryWrapper>
    {
        private static readonly DefaultNativeInstanceCache Cache = new();

        public NoFactoryWrapper(nint handle, bool ownsHandle)
            : base(handle, ownsHandle)
        {
        }

        public static bool RegistrationIsPolymorphic => true;
        public static nuint RegistrationDynamicTypeId => 0x4202;
        public static Type RegistrationExpectedType => typeof(NoFactoryWrapper);
        public static INativeInstanceCache RegistrationInstanceCache => Cache;
        public static Func<nint, bool, NativeObject>? RegistrationCreateFactory => null;
        public static NativeObjectMetadata RegistrationGetMetadata(nint handle) => new(handle, RegistrationDynamicTypeId);
    }
}
