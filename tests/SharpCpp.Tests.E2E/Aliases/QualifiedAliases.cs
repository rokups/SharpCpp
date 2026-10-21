using Config_breadth2;
using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Aliases;

public interface IConfig2NativeStatic<TSelf>
    where TSelf : IConfig2NativeStatic<TSelf>
{
    static abstract int GetTypeStatic();
}

public interface IConfig2NativeMarker<TSelf>
{
}

public sealed partial class ConfigBreadthTests
{
    [Fact]
    public void Configured_nested_aliases_and_inherited_metadata_round_trip_through_native_code()
    {
        using var value = new Config2Value(40);
        Assert.Equal(67, Config2Value.GetTypeStatic());
        Assert.Same(value, E2EBindings.Config2AliasConstReference(value));
        Assert.Same(value, E2EBindings.Config2AliasPointer(value));

        using var map = E2EBindings.MakeConfig2ValueMap(value);
        Assert.Same(value, map[4]);
        Assert.Equal(40, E2EBindings.ReadConfig2ValueMap(map));

        using var surface = E2EBindings.MakeConfig2Surface(value, 2);
        Assert.Equal(42, surface.ReadConfigured());
        Assert.Same(value, surface.EchoConfigured(value));
        surface.BumpConfigured(3);
        Assert.Equal(45, surface.ReadConfigured());

        using var clone = surface.Clone();
        surface.BumpConfigured(4);
        Assert.Equal(45, clone.ReadConfigured());
        Assert.Equal(49, surface.ReadConfigured());

        using var nested = E2EBindings.MakeConfig2Nested(surface, 5);
        Assert.Equal(54, E2EBindings.ReadConfig2Nested(nested));
        Assert.Equal(92, E2EBindings.InvokeConfig2NestedCallback(
            (configured, item) => configured.ReadConfigured() + item.GetValue(),
            surface,
            value));

        using var repair = E2EBindings.MakeConfig2RepairAlias(
            value,
            (left, right) => left.GetValue() + right.GetValue(),
            6);
        Assert.Equal(86, repair.InvokeRepairConfigured(value));
        Assert.Same(value, repair.EchoRepairConfigured());
        Assert.Same(repair, E2EBindings.Config2RepairConstReference(repair));
        Assert.Equal(87, E2EBindings.InvokeConfig2RepairAlias(repair, value));
        using var eaRepair = E2EBindings.MakeEaConfig2RepairAlias(
            value,
            (left, right) => left.GetValue() + right.GetValue(),
            6);
        Assert.Equal(86, eaRepair.InvokeRepairConfigured(value));
        Assert.Same(value, eaRepair.EchoRepairConfigured());
        Assert.Same(eaRepair, E2EBindings.EaConfig2RepairConstReference(eaRepair));
        Assert.Equal(87, E2EBindings.InvokeEaConfig2RepairAlias(eaRepair, value));
        Assert.Equal(96, E2EBindings.Config2InvokeSpecializedCallback<
            Func<Config2Surface<Config2Value>, Config2Value, int>>(
            (configured, item) => configured.ReadConfigured() + item.GetValue(),
            value));

        using var derived = new Config2Derived(50);
        Assert.Equal(52, derived.GetValue());
        Assert.Equal(73, ReadNativeStatic<Config2Derived>());
        using var derivedSurface = E2EBindings.MakeConfig2DerivedSurface(derived, 1);
        Assert.Equal(53, derivedSurface.ReadConfigured());

        using var rendered = new ManagedConfig2RenderRecord();
        Assert.Equal(23, rendered.BaseValue());
        Assert.Equal(13, rendered.DuplicateValue());
        Assert.Equal(11, rendered.ReadShadowedValue());
        Assert.Equal(5, rendered.SumInheritedValues());
        Assert.Equal(4, rendered.ReadExposedValue(0));
        rendered.WriteExposedValue(1, 8);
        Assert.Equal(12, rendered.SumExposedValues());
        Assert.Equal(14, rendered.InvokeTransform(7));
    }

    private sealed partial class ManagedConfig2RenderRecord : Config2RenderRecord
    {
        public override int Transform(int value) => value * 2;

        public int ReadExposedValue(int index) => ExposedValues[index];

        public void WriteExposedValue(int index, int value) => ExposedValues[index] = value;
    }

    private static int ReadNativeStatic<T>()
        where T : IConfig2NativeStatic<T>
        => T.GetTypeStatic();
}
