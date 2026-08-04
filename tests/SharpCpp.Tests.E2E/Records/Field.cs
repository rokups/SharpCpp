using System.Collections.Generic;
using System.Linq;

using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Records;

public sealed class FieldTests
{
    [Fact]
    public void Public_fields_are_exposed_as_properties()
    {
        var payload = E2EBindings.CreateFieldPayload(10);
        var replacement = E2EBindings.CreateFieldPayload(30);
        var box = E2EBindings.CreateFieldBox(2, true, FieldMode.Active, payload, 99);

        Assert.Equal(2, box.Count);
        Assert.True(box.Enabled);
        Assert.Equal(FieldMode.Active, box.Mode);
        Assert.False(box.Payload.OwnsHandle);
        Assert.Equal(10, box.Payload.Value);
        Assert.Equal(new[] { 0, 0 }, box.PayloadSamples.Select(static sample => sample.Value).ToArray());
        Assert.Equal(new[] { 0, 0, 0 }, box.Samples.ToArray());
        Assert.Equal(99, box.FixedValue);

        box.Count = 7;
        box.Enabled = false;
        box.Mode = FieldMode.Suspended;
        box.Payload = replacement;
        using var payloadSamples = new global::SharpCpp.NativeArray<FieldPayload>(
            (IEnumerable<FieldPayload>)new[] { payload, replacement });
        using var samples = new global::SharpCpp.NativeArray<int>(
            (IEnumerable<int>)new[] { 3, 5, 7 });
        Assert.NotEqual(0, samples.Handle);
        Assert.True(samples.OwnsHandle);
        Assert.False(samples.IsNull);
        box.PayloadSamples = payloadSamples;
        box.Samples = samples;

        var abiWrappedSamples = global::SharpCpp.NativeArray<int>.CreateManagedWrapper(
            samples.ToNativeArrayAbi(), ownsHandle: false);
        var handleWrappedSamples = global::SharpCpp.NativeArray<int>.CreateManagedWrapper(
            samples.Handle, ownsHandle: false);
        Assert.Same(samples, abiWrappedSamples);
        Assert.Same(samples, handleWrappedSamples);
        Assert.Equal(new[] { 3, 5, 7 }, ((System.Collections.IEnumerable)handleWrappedSamples)
            .Cast<int>().ToArray());
        box.Samples = handleWrappedSamples;

        Assert.Equal(7, box.Count);
        Assert.False(box.Enabled);
        Assert.Equal(FieldMode.Suspended, box.Mode);
        Assert.False(box.Payload.OwnsHandle);
        Assert.Equal(30, box.Payload.Value);
        Assert.Equal(new[] { 10, 30 }, box.PayloadSamples.Select(static sample => sample.Value).ToArray());
        Assert.Equal(new[] { 3, 5, 7 }, box.Samples.ToArray());
        Assert.Equal(99, box.FixedValue);
    }

    [Fact]
    public void Native_fixed_array_fields_validate_setter_lengths()
    {
        var payload = E2EBindings.CreateFieldPayload(10);
        var box = E2EBindings.CreateFieldBox(2, true, FieldMode.Active, payload, 99);
        using var wrongLength = new global::SharpCpp.NativeArray<int>(2);
        wrongLength[0] = 11;
        wrongLength[1] = 13;
        Assert.Equal(new[] { 11, 13 }, box.Pair.ToArray());

        var exception = Assert.Throws<ArgumentException>(() => box.Samples = wrongLength);

        Assert.Contains("Expected array length 3", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Handle_backed_native_fixed_array_fields_validate_setter_lengths()
    {
        var payload = E2EBindings.CreateFieldPayload(10);
        var box = E2EBindings.CreateFieldBox(2, true, FieldMode.Active, payload, 99);
        using var wrongLength = new global::SharpCpp.NativeArray<FieldPayload>(1);
        wrongLength[0] = payload;
        Assert.Same(payload, wrongLength[0]);

        var exception = Assert.Throws<ArgumentException>(() => box.PayloadSamples = wrongLength);

        Assert.Contains("Expected array length 2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Enum_bool_handle_and_nested_fixed_arrays_round_trip()
    {
        using var first = E2EBindings.CreateFieldPayload(11);
        using var second = E2EBindings.CreateFieldPayload(22);
        using var box = E2EBindings.CreateFieldBox(0, false, FieldMode.Idle, first, 0);

        Assert.Equal(new[] { FieldMode.Idle, FieldMode.Active }, box.Modes.ToArray());
        Assert.Equal(new[] { false, true }, box.Switches.ToArray());
        box.PayloadHandles[0] = first;
        box.PayloadHandles[1] = second;
        Assert.Equal(new[] { 11, 22 }, box.PayloadHandles.Select(value => value.Value));
        Assert.Equal(new[] { 1, 2, 3 }, box.Matrix[0].ToArray());
        Assert.Equal(new[] { 4, 5, 6 }, box.Matrix[1].ToArray());

        using var modes = new global::SharpCpp.NativeArray<FieldMode>([FieldMode.Suspended, FieldMode.Active]);
        using var switches = new global::SharpCpp.NativeArray<bool>([true, false]);
        box.Modes = modes;
        box.Switches = switches;
        Assert.Equal(new[] { FieldMode.Suspended, FieldMode.Active }, box.Modes.ToArray());
        Assert.Equal(new[] { true, false }, box.Switches.ToArray());
    }

    [Fact]
    public void Pointer_count_fields_and_dynamic_native_arrays_round_trip_handles()
    {
        using var first = E2EBindings.CreateFieldPayload(31);
        using var second = E2EBindings.CreateFieldPayload(47);
        using var box = E2EBindings.CreatePointerCountFieldBox(first, second);

        Assert.Equal(2u, box.NumItems);
        Assert.Equal(31, box.GetItemsAt(0).Value);
        Assert.Equal(47, box.GetItemsAt(1).Value);

        using var values = new global::SharpCpp.NativeArray<FieldPayload>(2);
        values[0] = first;
        values[1] = second;
        Assert.Equal(new[] { 31, 47 }, values.Select(value => value.Value));
    }

    [Fact]
    public void Static_fields_are_exposed_as_static_properties()
    {
        var payload = E2EBindings.CreateFieldPayload(42);

        FieldPayload.SharedValue = 17;
        FieldPayload.SharedPayload = payload;

        Assert.Equal(17, FieldPayload.SharedValue);
        Assert.False(FieldPayload.SharedPayload.OwnsHandle);
        Assert.Equal(42, FieldPayload.SharedPayload.Value);
    }

    [Fact]
    public void Intrusive_reference_counted_returns_are_owned_and_released()
    {
        var destroyedCount = IntrusiveFieldPayload.GetDestroyedCount();

        using (var payload = E2EBindings.CreateIntrusiveFieldPayload())
        {
            Assert.True(payload.OwnsHandle);
            Assert.Equal(1, payload.GetRefCount());
        }

        Assert.Equal(destroyedCount + 1, IntrusiveFieldPayload.GetDestroyedCount());
    }

    [Fact]
    public void Projected_value_references_preserve_in_and_ref_abi()
    {
        var value = 41;

        Assert.Equal(41, E2EBindings.ReadProjectedFieldValue(in value));
        Assert.Equal(41, E2EBindings.ReadProjectedFieldValueCopy(value));
        Assert.Equal(42, E2EBindings.GetProjectedFieldValueReference());
        E2EBindings.IncrementProjectedFieldValue(ref value);

        Assert.Equal(42, value);
    }

    [Fact]
    public void Projected_value_fields_use_the_projected_abi()
    {
        using var box = E2EBindings.CreateProjectedFieldBox();

        box.Value = 41;

        Assert.Equal(41, box.Value);
    }

    [Fact]
    public void Handle_backed_properties_can_store_null_wrapper_values()
    {
        var payload = E2EBindings.CreateFieldPayload(10);
        var box = E2EBindings.CreateFieldBox(2, true, FieldMode.Active, payload, 99);

        box.Payload = null!;

        Assert.True(box.Payload.IsNull);
    }
}
