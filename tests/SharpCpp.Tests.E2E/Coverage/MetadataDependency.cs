using SharpCpp.Tests.E2E.Coverage.Consumer;
using SharpCpp.Tests.E2E.Coverage.Dependency;
using Xunit;

namespace SharpCpp.Tests.E2E.Coverage;

public sealed class MetadataDependencyTests
{
    [Fact]
    public void Exported_dependency_metadata_drives_a_compiled_consumer_round_trip()
    {
        using var value = DependencyBindings.CreateDependencyValue(42, DependencyState.Ready);

        Assert.Equal(42, value.Value);
        Assert.Equal(DependencyState.Ready, value.State);
        Assert.Equal(42, ConsumerBindings.ConsumeDependencyValue(value));

        value.Value = 73;
        value.State = DependencyState.Idle;
        value.ExposedValue = 29;
        Assert.Equal(73, ConsumerBindings.ConsumeDependencyValue(value));
        Assert.Equal(DependencyState.Idle, value.State);
        Assert.Equal(29, value.ExposedValue);

        Assert.Equal(73, ConsumerBindings.ConsumeDependencyHandleAlias(value));
        Assert.Equal(42UL, ConsumerBindings.ConsumeDependencyUnsignedAlias(41));
        Assert.Equal(42L, ConsumerBindings.ConsumeDependencySignedAlias(40));
        Assert.Equal(
            (ushort)DependencySmallState.Ready,
            ConsumerBindings.ConsumeDependencySmallStateAlias((ushort)DependencySmallState.Ready));
        Assert.Equal(6UL, DependencyBindings.ConsumeDependencyEnumMatrix(
            DependencyI8State.Ready,
            DependencyU8State.Ready,
            DependencyI16State.Ready,
            DependencyI32State.Ready,
            DependencyI64State.Ready,
            DependencyU64State.Ready));
        Assert.Equal(73, ConsumerBindings.ConsumeDependencyReboundHandleAlias(value));

        using var valueBox = DependencyBindings.CreateDependencyValueBox(value);
        Assert.Same(value, valueBox.GetValue());
        Assert.Equal(73, ConsumerBindings.ConsumeDependencyValueBoxAlias(valueBox));

        using var replacement = DependencyBindings.CreateDependencyValue(31, DependencyState.Idle);
        using var holder = DependencyBindings.CreateDependencyHolder(value);
        Assert.Same(value, holder.Item);
        holder.Item = replacement;
        Assert.Same(replacement, holder.Item);
        Assert.Equal(31, holder.ReadItem());

        using var box = DependencyBindings.CreateDependencyBox(19);
        Assert.Equal(19, box.GetValue());
        Assert.Equal(19, ConsumerBindings.ConsumeDependencyBoxAlias(box));
        box.SetValue(23);
        Assert.Equal(23, ConsumerBindings.ConsumeDependencyBoxAlias(box));

        using var primitives = new DependencyPrimitives();
        Assert.True(primitives.EchoBool(true));
        Assert.Equal((sbyte)-8, primitives.EchoInt8(-8));
        Assert.Equal((byte)8, primitives.EchoUint8(8));
        Assert.Equal((short)-16, primitives.EchoInt16(-16));
        Assert.Equal((ushort)16, primitives.EchoUint16(16));
        Assert.Equal(-32, primitives.EchoInt32(-32));
        Assert.Equal(32U, primitives.EchoUint32(32));
        Assert.Equal(-64L, primitives.EchoInt64(-64));
        Assert.Equal(64UL, primitives.EchoUint64(64));
        Assert.Equal(1.25F, primitives.EchoSingle(1.25F));
        Assert.Equal(2.5, primitives.EchoDouble(2.5));
        Assert.Equal((nint)(-17), primitives.EchoNativeInt(-17));
        Assert.Equal((nuint)17, primitives.EchoNativeUint(17));
        Assert.Equal((nint)0x1234, primitives.EchoPointer((nint)0x1234));
        Assert.Equal(31, primitives.EchoReference(31));

        var dependencyObserved = 0;
        primitives.InvokeAction(() => dependencyObserved = 37);
        Assert.Equal(37, dependencyObserved);
        Assert.Equal(42, primitives.InvokeTransform(number => number * 2, 21));

        var consumerObserved = 0;
        ConsumerBindings.ConsumeDependencyAction(() => consumerObserved = 41);
        Assert.Equal(41, consumerObserved);
        Assert.Equal(41, ConsumerBindings.ConsumeDependencyTransform(number => number * 2, 20));

        var updated = 9;
        primitives.Update(ref updated);
        Assert.Equal(10, updated);
        primitives.Write(out var written);
        Assert.Equal(17, written);
        Assert.Equal(42, DependencyPrimitives.StaticValue(41));

        using var derived = new ConsumerDerived(13);
        Assert.Equal(13, derived.GetBaseValue());
        Assert.Equal(26, derived.Doubled());
    }

    [Fact]
    public void Imported_fixed_array_metadata_drives_a_compiled_consumer_round_trip()
    {
        using var values = new global::Std.Array<int>(new[] { 5, 7, 11, 19 });
        Assert.Equal(42, ConsumerBindings.ConsumeDependencyFixedAlias(values));
    }

    [Fact]
    public void Imported_dynamic_array_metadata_drives_a_compiled_consumer_round_trip()
    {
        using var values = new global::Std.Vector<int>(new[] { 11, 14, 17 });
        Assert.Equal(42, ConsumerBindings.ConsumeDependencyDynamicAlias(values));
    }
}
