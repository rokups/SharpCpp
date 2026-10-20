using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Optional_values;

using SharpCpp.Support;
using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Marshallers;

/// Hand-written managed side of the optional_values::point value projection. The layout must
/// match the native abi_type (optional_values::point) exactly.
[StructLayout(LayoutKind.Sequential)]
public struct OptionalPoint(float x, float y, float z)
{
    public float X = x;
    public float Y = y;
    public float Z = z;
}

/// Hand-written managed side of optional_values::vec. The layout is the vec_abi layout (y, x).
[StructLayout(LayoutKind.Sequential)]
public struct OptionalVec(float y, float x)
{
    public float Y = y;
    public float X = x;
}

/// Hand-written managed side of optional_values::fixed_pair. The layout is the fixed_pair_abi layout.
[StructLayout(LayoutKind.Sequential)]
public struct OptionalFixedPair(int first, int second)
{
    public int First = first;
    public int Second = second;
}

public sealed class OptionalValuesTests
{
    [Fact]
    public void Optional_primitive_returns_are_nullable_values()
    {
        Assert.Equal(7, E2EBindings.Pick(true));
        Assert.Null(E2EBindings.Pick(false));
    }

    [Fact]
    public void Optional_primitive_parameters_pass_values_and_nulls()
    {
        Assert.Equal(1100, E2EBindings.Sum(null, null));
        Assert.Equal(3, E2EBindings.Sum(1, 2.5f));
        Assert.Equal(1005, E2EBindings.Sum(5, null));
        Assert.Equal(1000, E2EBindings.Sum(null, 900.0f));
    }

    [Fact]
    public void Optional_projected_values_are_nullable_structs()
    {
        var value = E2EBindings.Make(true);
        Assert.NotNull(value);
        Assert.Equal(1.0f, value.Value.X);
        Assert.Equal(2.0f, value.Value.Y);
        Assert.Equal(3.0f, value.Value.Z);
        Assert.Null(E2EBindings.Make(false));

        Assert.Equal(6.0f, E2EBindings.ComponentSum(new OptionalPoint(1.0f, 2.0f, 3.0f)));
        Assert.Equal(-1.0f, E2EBindings.ComponentSum(null));
    }

    [Fact]
    public void Optional_handle_returns_are_owned_nullable_wrappers()
    {
        using var found = E2EBindings.Find(true);
        Assert.NotNull(found);
        Assert.True(found.OwnsNativeHandle);
        Assert.Equal(42, found.GetValue());
        Assert.Null(E2EBindings.Find(false));
    }

    [Fact]
    public void Optional_handle_parameters_pass_wrappers_and_nulls()
    {
        using var item = new Item(9);
        Assert.Equal(9, E2EBindings.Take(item));
        Assert.Equal(-1, E2EBindings.Take(null));
        Assert.Equal(9, E2EBindings.TakeReference(item));
        Assert.Equal(-1, E2EBindings.TakeReference(null));
        Assert.Equal(9, item.GetValue());
    }

    [Fact]
    public void Optional_fields_read_and_write_nullable_values()
    {
        using var settings = new Settings();
        Assert.Null(settings.Limit);

        settings.Limit = 5;
        Assert.Equal(5u, settings.Limit);

        settings.Limit = null;
        Assert.Null(settings.Limit);
    }

    [Fact]
    public void Optional_enum_uses_the_exact_underlying_type()
    {
        Assert.Equal((int)E2EBindings.OptionalModeAbiSize(), Unsafe.SizeOf<OptionalAbi<Mode>>());
        Assert.Equal(2, Unsafe.SizeOf<OptionalAbi<Mode>>());
        Assert.Equal(Mode.On, E2EBindings.PickMode(true));
        Assert.Null(E2EBindings.PickMode(false));
        Assert.Equal(1, E2EBindings.ModeValue(Mode.Off));
        Assert.Equal(2, E2EBindings.ModeValue(Mode.On));
        Assert.Equal(-1, E2EBindings.ModeValue(null));
    }

    [Fact]
    public void Optional_converted_struct_crosses_without_a_handle()
    {
        var value = E2EBindings.MakeVec(true);
        Assert.NotNull(value);
        Assert.Equal(3.0f, value.Value.X);
        Assert.Equal(4.0f, value.Value.Y);
        Assert.Null(E2EBindings.MakeVec(false));

        Assert.Equal(-1.0f, E2EBindings.VecDifference(new OptionalVec(4.0f, 3.0f)));
        Assert.Equal(-100.0f, E2EBindings.VecDifference(null));
    }

    [Fact]
    public void Optional_parameter_with_a_nullopt_default_is_nullable_once()
    {
        Assert.Equal(-50.0f, E2EBindings.VecDifferenceOrDefault());
        Assert.Equal(-2.0f, E2EBindings.VecDifferenceOrDefault(new OptionalVec(4.0f, 3.0f), 2));
    }

    [Fact]
    public void Optional_and_plain_fixed_arrays_do_not_collide()
    {
        using var arrays = new OptionalArrays();
        Assert.NotNull(arrays.OptionalItems);
        Assert.NotNull(arrays.PlainItems);

        // A fixed-array getter points into the record, so the array wrapper never owns it.
        Assert.False(arrays.OptionalItems.OwnsNativeHandle);
        Assert.False(arrays.PlainItems.OwnsNativeHandle);
    }

    [Fact]
    public void Optional_field_of_a_class_without_assignment_is_still_usable()
    {
        using var holder = new PinnedHolder();
        holder.Marker = 5;
        Assert.Equal(5, holder.Marker);
        Assert.Null(holder.Data);
    }

    [Fact]
    public void Optional_const_reference_of_a_type_without_assignment_is_built_in_place()
    {
        using var pinned = new Pinned(3);
        Assert.Equal(3, E2EBindings.TakePinned(pinned));
        Assert.Equal(-1, E2EBindings.TakePinned(null));
        Assert.Equal(3, pinned.GetId());

        Assert.Equal(7, E2EBindings.FixedPairSum(new OptionalFixedPair(3, 4)));
        Assert.Equal(-1, E2EBindings.FixedPairSum(null));
        var made = E2EBindings.MakeFixedPair(true);
        Assert.NotNull(made);
        Assert.Equal(3, made.Value.First);
        Assert.Equal(4, made.Value.Second);
        Assert.Null(E2EBindings.MakeFixedPair(false));
    }

    [Fact]
    public void Optional_field_setter_of_a_type_without_assignment_resets_and_emplaces()
    {
        using var holder = new LooseHolder();
        Assert.Null(holder.Data);

        using (var pinned = new Pinned(11))
        {
            holder.Data = pinned;
        }

        using (var first = holder.Data)
        {
            Assert.NotNull(first);
            Assert.True(first.OwnsNativeHandle);
            Assert.Equal(11, first.GetId());
        }

        using (var replacement = new Pinned(12))
        {
            holder.Data = replacement;
        }

        using (var second = holder.Data)
        {
            Assert.NotNull(second);
            Assert.Equal(12, second.GetId());
        }

        holder.Data = null;
        Assert.Null(holder.Data);
    }

    [Fact]
    public void Optional_handle_field_reads_an_owned_copy_of_a_value()
    {
        using var holder = new ItemHolder();
        Assert.Null(holder.Held);

        using (var source = new Item(21))
        {
            holder.Held = source;
        }

        var first = holder.Held;
        var second = holder.Held;
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.True(first.OwnsNativeHandle);
        Assert.True(second.OwnsNativeHandle);
        Assert.NotEqual(first.DangerousGetHandle(), second.DangerousGetHandle());
        Assert.Equal(21, first.GetValue());

        // Each read is an independent copy: changing one copy does not change the field.
        first.SetValue(99);
        Assert.Equal(21, second.GetValue());
        first.Dispose();
        Assert.Equal(21, second.GetValue());
        second.Dispose();

        using var third = holder.Held;
        Assert.NotNull(third);
        Assert.Equal(21, third.GetValue());
    }

    [Fact]
    public void Optional_of_a_move_only_class_is_not_bound()
    {
        Assert.Null(typeof(E2EBindings).GetMethod("MakeMoveOnly"));
    }

    [Fact]
    public void Optional_abi_layout_matches_the_native_struct()
    {
        Assert.Equal((int)E2EBindings.OptionalIntAbiSize(), Unsafe.SizeOf<OptionalAbi<int>>());
        Assert.Equal((int)E2EBindings.OptionalDoubleAbiSize(), Unsafe.SizeOf<OptionalAbi<double>>());
        Assert.Equal((int)E2EBindings.OptionalPointAbiSize(), Unsafe.SizeOf<OptionalAbi<OptionalPoint>>());
    }
}
