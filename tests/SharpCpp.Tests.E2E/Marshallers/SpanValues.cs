using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Span_values;

using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Marshallers;

/// Hand-written managed side of span_values::point. The layout must match the native struct
/// exactly, because a span gives managed code the native elements in place.
[StructLayout(LayoutKind.Sequential)]
public struct SpanPoint(float x, float y, float z)
{
    public float X = x;
    public float Y = y;
    public float Z = z;
}

/// Hand-written managed side of span_values::swapped. The layout is the swapped_abi layout (b, a).
[StructLayout(LayoutKind.Sequential)]
public struct SpanSwapped(float b, float a)
{
    public float B = b;
    public float A = a;
}

public sealed class SpanValuesTests
{
    [Fact]
    public void Managed_element_sizes_match_native_sizes()
    {
        Assert.Equal((nuint)Unsafe.SizeOf<SpanPoint>(), E2EBindings.SpanPointSize());
        Assert.Equal((nuint)Unsafe.SizeOf<Shade>(), E2EBindings.SpanShadeSize());
    }

    [Fact]
    public void Read_only_span_parameters_pass_builtin_elements()
    {
        Assert.Equal(10, E2EBindings.SpanSumInts([1, 2, 3, 4]));
        Assert.Equal(0, E2EBindings.SpanSumInts(ReadOnlySpan<int>.Empty));
    }

    [Fact]
    public void Mutable_span_parameters_write_into_managed_memory()
    {
        Span<int> values = stackalloc int[3];
        E2EBindings.SpanFillInts(values, 5);
        Assert.Equal(new[] { 5, 6, 7 }, values.ToArray());
    }

    [Fact]
    public void Const_reference_span_parameters_pass_bytes()
    {
        Assert.Equal((nuint)2, E2EBindings.SpanCountBytes([1, 7, 3, 7], 7));

        var bytes = new byte[] { 1, 2, 3 };
        E2EBindings.SpanIncrementBytes(bytes);
        Assert.Equal(new byte[] { 2, 3, 4 }, bytes);
    }

    [Fact]
    public void Span_parameters_pass_struct_elements_in_place()
    {
        Assert.Equal(21.0f, E2EBindings.SpanSumPoints([new SpanPoint(1, 2, 3), new SpanPoint(4, 5, 6)]));

        var points = new[] { new SpanPoint(1, 2, 3) };
        E2EBindings.SpanScalePoints(points, 2.0f);
        Assert.Equal(new SpanPoint(2, 4, 6), points[0]);
    }

    [Fact]
    public void Span_parameters_pass_enum_elements()
    {
        Assert.Equal(6, E2EBindings.SpanSumShades([Shade.Dark, Shade.Mid, Shade.Light]));
    }

    [Fact]
    public void Fixed_extent_span_parameters_accept_any_managed_length()
    {
        Assert.Equal(6, E2EBindings.SpanSumFixed([1, 2, 3]));
        // A short span reaches the callee as a local array with zero elements at the end.
        Assert.Equal(3, E2EBindings.SpanSumFixed([1, 2]));
        // A long span gives the callee the first elements only.
        Assert.Equal(6, E2EBindings.SpanSumFixed([1, 2, 3, 4]));

        var exact = new int[2];
        E2EBindings.SpanFillFixed(exact);
        Assert.Equal(new[] { 11, 22 }, exact);

        // A short span gets the elements that fit back.
        var shortValues = new int[1];
        E2EBindings.SpanFillFixed(shortValues);
        Assert.Equal(new[] { 11 }, shortValues);
    }

    [Fact]
    public void Span_parameters_keep_following_default_values()
    {
        Assert.Equal(103, E2EBindings.SpanSumWithOffset([1, 2]));
        Assert.Equal(4, E2EBindings.SpanSumWithOffset([1, 2], 1));
    }

    [Fact]
    public void Span_returns_are_views_of_native_memory()
    {
        var storage = E2EBindings.SpanMutableStorage();
        Assert.Equal(new[] { 1, 2, 3, 4 }, storage.ToArray());

        storage[2] = 30;
        Assert.Equal(30, E2EBindings.SpanStorageValue(2));
        storage[2] = 3;

        var points = E2EBindings.SpanConstPoints();
        Assert.Equal(2, points.Length);
        Assert.Equal(new SpanPoint(4, 5, 6), points[1]);

        Assert.True(E2EBindings.SpanEmptyView().IsEmpty);
        Assert.Equal(new[] { 7, 8, 9 }, E2EBindings.SpanFixedView().ToArray());
    }

    [Fact]
    public void Span_returns_of_members_address_the_object_storage()
    {
        using var buffer = new ByteBuffer([1, 2, 3]);
        var bytes = buffer.Bytes();
        Assert.Equal(new byte[] { 1, 2, 3 }, bytes.ToArray());

        bytes[0] = 9;
        Assert.Equal(new byte[] { 9, 2, 3 }, buffer.View().ToArray());
    }

    [Fact]
    public void Spans_of_elements_without_native_layout_are_not_bound()
    {
        Assert.Null(typeof(E2EBindings).GetMethod("SpanSumItems"));
        Assert.Null(typeof(E2EBindings).GetMethod("SpanSumSwapped"));
    }
}
