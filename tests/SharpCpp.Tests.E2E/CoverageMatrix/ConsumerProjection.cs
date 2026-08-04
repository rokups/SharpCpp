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
        var projected = new global::SharpCpp.Tests.E2E.ConsumerProjection.ProjectedConsumer(2);
        Assert.Equal(44, projected.RoundTrip(41));
        Assert.Equal(44, projected.RoundTripNormalized(41));

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

        var valueArray = host.ValueArray;
        Assert.Equal(2, valueArray.Length);
        Assert.Equal(2, valueArray.Count);
        Assert.False(valueArray.IsReadOnly);
        var embeddedFirst = valueArray[0];
        using var missingValue = new ConsumerProjectionValue(99);
        Assert.True(valueArray.Contains(embeddedFirst));
        Assert.False(valueArray.Contains(missingValue));
        Assert.Equal(0, valueArray.IndexOf(embeddedFirst));
        Assert.Equal(-1, valueArray.IndexOf(missingValue));
        Assert.Equal(new[] { 5, 6 }, valueArray.ToArray().Select(item => item.GetValue()));

        var copiedValues = new ConsumerProjectionValue[3];
        valueArray.CopyTo(copiedValues, 1);
        Assert.Equal(new[] { 5, 6 }, copiedValues.Skip(1).Select(item => item.GetValue()));
        Assert.Equal(
            new[] { 5, 6 },
            ((System.Collections.IEnumerable)valueArray).Cast<ConsumerProjectionValue>().Select(item => item.GetValue()));

        valueArray[1] = second;
        Assert.Equal(18, host.SumValueArray());
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = valueArray[-1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => valueArray[2] = second);
        Assert.Throws<ArgumentNullException>(() => valueArray.CopyTo(null!, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => valueArray.CopyTo(copiedValues, -1));
        Assert.Throws<ArgumentException>(() => valueArray.CopyTo(copiedValues, 2));
        Assert.Throws<NotSupportedException>(() => valueArray.Add(first));
        Assert.Throws<NotSupportedException>(() => valueArray.Clear());
        Assert.Throws<NotSupportedException>(() => valueArray.Insert(0, first));
        Assert.Throws<NotSupportedException>(() => valueArray.Remove(first));
        Assert.Throws<NotSupportedException>(() => valueArray.RemoveAt(0));

        using var replacements = new global::Std.Array<ConsumerProjectionValue>([first, second]);
        host.ValueArray = replacements;
        Assert.Equal(new[] { 11, 13 }, host.ValueArray.Select(item => item.GetValue()));

        var projectedArray = host.PrimitiveArray;
        Assert.Equal(2, projectedArray.Length);
        Assert.Equal(2, projectedArray.Count);
        Assert.False(projectedArray.IsReadOnly);
        var containsFive = projectedArray.Contains(5);
        var containsNinetyNine = projectedArray.Contains(99);
        Assert.True(containsFive);
        Assert.False(containsNinetyNine);
        Assert.Equal(1, projectedArray.IndexOf(6));
        Assert.Equal(-1, projectedArray.IndexOf(99));
        Assert.Equal(new[] { 5, 6 }, projectedArray.ToArray());
        projectedArray[1] = 9;
        Assert.Equal(14, host.SumPrimitiveArray());

        var copied = new int[3];
        projectedArray.CopyTo(copied, 1);
        Assert.Equal(new[] { 0, 5, 9 }, copied);
        Assert.Equal(new[] { 5, 9 }, ((System.Collections.IEnumerable)projectedArray).Cast<int>());

        Assert.Throws<ArgumentOutOfRangeException>(() => _ = projectedArray[-1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => projectedArray[2] = 0);
        Assert.Throws<ArgumentNullException>(() => projectedArray.CopyTo(null!, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => projectedArray.CopyTo(copied, -1));
        Assert.Throws<ArgumentException>(() => projectedArray.CopyTo(copied, 2));
        Assert.Throws<NotSupportedException>(() => projectedArray.Add(1));
        Assert.Throws<NotSupportedException>(() => projectedArray.Clear());
        Assert.Throws<NotSupportedException>(() => projectedArray.Insert(0, 1));
        Assert.Throws<NotSupportedException>(() => projectedArray.Remove(5));
        Assert.Throws<NotSupportedException>(() => projectedArray.RemoveAt(0));

        Assert.Equal(26, E2EBindings.SumConsumerProjectionPayloads([first, second], 2));

        using var cloneSource = new ConsumerProjectionCloneBox<int>(17);
        using var nativeClone = cloneSource.Clone();
        using var interfaceClone = (ConsumerProjectionCloneBox<int>)((ICloneable)cloneSource).Clone();
        Assert.Equal(17, nativeClone.GetValue());
        Assert.Equal(17, interfaceClone.GetValue());
    }
}
