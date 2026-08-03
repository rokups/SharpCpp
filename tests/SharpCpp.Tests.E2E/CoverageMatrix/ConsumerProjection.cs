using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.CoverageMatrix;

public sealed unsafe class ConsumerProjectionTests
{
    private sealed class ManagedDirector(int first, int second) : ConsumerProjectionDirector(first, second)
    {
        protected override int Transform(int value)
        {
            return value * 3;
        }

        public int ReadValue(int index)
        {
            return Values[index];
        }

        public void WriteValue(int index, int value)
        {
            Values[index] = value;
        }

        public int CallBaseMeasure(int value)
        {
            return base.Measure(value);
        }
    }

    [Fact]
    public void Consumer_metadata_hooks_generate_callable_native_surfaces()
    {
        Assert.Equal(42, CoverageConsumerProjection.RoundTrip(41));
        Assert.Equal(44, new global::SharpCpp.Tests.E2E.ConsumerProjection.ProjectedConsumer(2).RoundTrip(41));

        var pointer = E2EBindings.ConsumerProjectionFunctionPointerFunctionPointer;
        Assert.NotEqual(0, pointer);
        Assert.Equal(43, ((delegate* unmanaged[Cdecl]<int, int>)pointer)(41));

        using var holder = new ConsumerProjectionFunctionHolder(pointer);
        Assert.Equal(43, holder.Apply(41));

        Assert.Equal(44, E2EBindings.ConsumerProjectionAdaptInt(41));
        var adapted = 41;
        E2EBindings.ConsumerProjectionAdaptVoid(ref adapted);
        Assert.Equal(45, adapted);
        using var adaptedValue = E2EBindings.ConsumerProjectionAdaptValue(41);
        Assert.Equal(46, adaptedValue.GetValue());

        using var source = E2EBindings.CreateConsumerProjectionQuerySource();
        Assert.Equal(7, source.GetMarker());
        using var target = ConsumerProjectionQueryTarget.QueryManagedWrapper(source);
        Assert.NotNull(target);
        Assert.Equal(41, target.GetValue());
    }

    [Fact]
    public void Director_arrays_defaults_and_callbacks_round_trip()
    {
        using var value = new ManagedDirector(3, 5);

        Assert.Equal(3, value.ReadValue(0));
        Assert.Equal(8, value.SumValues());
        value.WriteValue(1, 7);
        Assert.Equal(10, value.SumValues());
        Assert.Equal(18, value.InvokeTransform(6));
        Assert.Equal(13, value.InvokeMeasure(10));
        Assert.Equal(13, value.CallBaseMeasure(10));
    }

    [Fact]
    public void Generic_methods_fields_and_pointer_defaults_round_trip()
    {
        using var first = new ConsumerProjectionValue(11);
        using var second = new ConsumerProjectionValue(13);
        using var host = new ConsumerProjectionHost(5);

        Assert.Equal(16, host.ConsumePayload(first));
        host.TouchPayload(first);
        Assert.Equal(11, ConsumerProjectionHost.StaticConsumePayload(first));
        ConsumerProjectionHost.StaticTouchPayload(first);

        using var values = new global::Std.Array<int>([2, 3]);
        Assert.Equal(10, host.SumArray(values));
        Assert.Equal(5, ConsumerProjectionHost.StaticSumArray(values));
        Assert.Equal(12, host.ProjectedValue(7));
        Assert.Equal(14, ConsumerProjectionHost.StaticProjectedValue(7));

        Assert.Same(first, host.EchoGeneric(first));
        host.ConsumeGeneric(first);
        Assert.Same(second, host.SelectGeneric(0, second));
        Assert.Equal(11, host.MeasureGeneric(first));

        using var replacements = new global::Std.Array<ConsumerProjectionValue>([first, second]);
        host.ValueArray = replacements;
        Assert.Equal(new[] { 11, 13 }, host.ValueArray.Select(item => item.GetValue()));

        Assert.Equal(26, E2EBindings.SumConsumerProjectionPayloads([first, second], 2));

        using var cloneSource = new ConsumerProjectionCloneBox<int>(17);
        using var nativeClone = cloneSource.Clone();
        using var interfaceClone = (ConsumerProjectionCloneBox<int>)((ICloneable)cloneSource).Clone();
        Assert.Equal(17, nativeClone.GetValue());
        Assert.Equal(17, interfaceClone.GetValue());
    }
}
