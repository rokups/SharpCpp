using Generic_fields;

using Xunit;

using System.Runtime.InteropServices;

namespace SharpCpp.Tests.E2E.Templates;

/// Blittable managed mirror of generic_fields::spin, projected through GenericFieldsMarshallers.h.
[StructLayout(LayoutKind.Sequential)]
public struct SpinValue(float w, float x, float y, float z)
{
    public float W = w;
    public float X = x;
    public float Y = y;
    public float Z = z;
}

public sealed class GenericFieldTests
{
    [Fact]
    public void Value_fields_round_trip_for_primitive_instantiations()
    {
        using var ints = new Cell<int>();
        ints.Value = 42;
        ints.Count = 3;
        Assert.Equal(42, ints.Value);
        Assert.Equal(3, ints.Count);
        Assert.Equal(0, ints.Frozen);

        using var floats = new Cell<float>();
        floats.Value = 2.5f;
        Assert.Equal(2.5f, floats.Value);
        Assert.Equal(0f, floats.Frozen);
    }

    [Fact]
    public void Record_valued_field_borrows_its_storage()
    {
        using var cell = new Cell<Point>();
        cell.Value.X = 4;
        Assert.Equal(4, cell.Value.X);

        using var replacement = new Point { Y = 9 };
        cell.Value = replacement;
        Assert.Equal(9, cell.Value.Y);
        Assert.Equal(0, cell.Value.X);
    }

    [Fact]
    public void Unsupported_field_shapes_stay_unbound_and_clashing_names_are_renamed()
    {
        Assert.Null(typeof(Cell<int>).GetProperty("Frozen")!.SetMethod);
        Assert.Null(typeof(Cell<int>).GetProperty("Shared"));
        Assert.Null(typeof(Cell<int>).GetProperty("Pair"));

        using var cell = new Cell<int> { Count = 2, TotalValue = 5 };
        Assert.Equal(20, cell.Total());
        Assert.Equal(5, cell.TotalValue);
    }

    [Fact]
    public void Value_mapped_argument_field_round_trips_as_the_projected_struct()
    {
        using var slot = new Slot<SpinValue>();
        Assert.Equal(1f, slot.Value.W);

        slot.Value = new SpinValue(0.5f, 1f, 2f, 3f);
        Assert.Equal(new SpinValue(0.5f, 1f, 2f, 3f), slot.Value);

        using var ints = new Slot<int>();
        ints.Value = 7;
        Assert.Equal(7, ints.Value);
    }

    [Fact]
    public void Container_of_argument_field_is_reported_instead_of_leaking_an_open_type()
    {
        // std::vector<T> keeps an unresolved dependent spelling; the degrade pass reports it
        // rather than collecting an open vector<T> instantiation.
        Assert.Null(typeof(Slot<int>).GetProperty("History"));
        using var slot = new Slot<int>();
        Assert.Equal(0, slot.HistorySize());
    }

    [Fact]
    public void Record_value_argument_field_round_trips_as_a_projected_value()
    {
        using var probe = SlotUser.MakeProbe(0.25f);
        Assert.Equal(0.25f, probe.Value.W);
        probe.Value = new SpinValue(1f, 2f, 3f, 4f);
        Assert.Equal(new SpinValue(1f, 2f, 3f, 4f), probe.Value);
    }

    [Fact]
    public void Explicit_full_specialization_suppresses_primary_template_field_accessors()
    {
        // tracked<spin> is an explicit full specialization with a different derivative type, so the
        // primary template's field accessors are reported instead of rendered against it.
        Assert.Null(typeof(Tracked<int>).GetProperty("Value"));
        Assert.Null(typeof(Tracked<int>).GetProperty("Derivative"));
        using var ints = new Tracked<int>();
        Assert.Equal(0, ints.Kind());
        using var spins = SlotUser.MakeTracked();
        Assert.Equal(1, spins.Kind());
    }
}
