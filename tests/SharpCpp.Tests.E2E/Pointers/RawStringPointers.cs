using System;
using System.Runtime.InteropServices;

using SharpCpp.Tests.E2E.Diagnostics;
using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Pointers;

public sealed class RawStringPointerTests
{
    private static System.Reflection.ParameterInfo Parameter(Type type, string method, int index) =>
        type.GetMethod(method)!.GetParameters()[index];

    private static nint[] Allocate(params string?[] strings)
    {
        var pointers = new nint[strings.Length];
        for (var index = 0; index < strings.Length; index++)
            pointers[index] = strings[index] is { } text ? Marshal.StringToCoTaskMemUTF8(text) : 0;
        return pointers;
    }

    private static void Free(nint[] pointers)
    {
        foreach (var pointer in pointers)
            Marshal.FreeCoTaskMem(pointer);
    }

    [Fact]
    public void The_pointers_shape_attribute_keeps_the_raw_pointer_binding()
    {
        var names = Allocate("ab", "cde", null);
        try
        {
            Assert.Equal(1005, E2EBindings.MeasureRawNames(names, 3));
            Assert.Equal(2, E2EBindings.MeasureRawNames(names.AsSpan(0, 1), 1));
        }
        finally
        {
            Free(names);
        }

        Assert.Equal(typeof(ReadOnlySpan<IntPtr>), Parameter(typeof(E2EBindings), nameof(E2EBindings.MeasureRawNames), 0).ParameterType);
    }

    [Fact]
    public void Consumer_metadata_selects_the_pointers_shape()
    {
        var names = Allocate("ab", "cde");
        try
        {
            Assert.Equal(5, E2EBindings.MeasureConfiguredRawNames(2, names));
        }
        finally
        {
            Free(names);
        }

        Assert.Equal(typeof(ReadOnlySpan<IntPtr>), Parameter(typeof(E2EBindings), nameof(E2EBindings.MeasureConfiguredRawNames), 1).ParameterType);
        // The sibling that carries no tag keeps the default string binding.
        Assert.Equal(typeof(ReadOnlySpan<string>), Parameter(typeof(E2EBindings), nameof(E2EBindings.MeasureConfiguredNames), 1).ParameterType);
    }

    [Fact]
    public void Native_code_may_retain_the_array_and_its_strings()
    {
        using var store = new RawNameStore();
        var names = Allocate("ab", "cde");
        try
        {
            store.Retain(names, 2);
            // The caller still owns the memory: a later call reads it back.
            Assert.Equal(2u, store.RetainedCount());
            Assert.Equal(5, store.RetainedLength());
        }
        finally
        {
            Free(names);
        }

        Assert.Equal(typeof(ReadOnlySpan<IntPtr>), Parameter(typeof(RawNameStore), nameof(RawNameStore.Retain), 0).ParameterType);
    }

    [Fact]
    public void A_range_begin_with_the_pointers_shape_is_a_raw_pointer_span()
    {
        var names = Allocate("ab", "cde");
        try
        {
            Assert.Equal(5, E2EBindings.MeasureRawNameRange(names));
            Assert.Equal(2, E2EBindings.MeasureRawNameRange(names.AsSpan(0, 1)));
            Assert.Equal(0, E2EBindings.MeasureRawNameRange([]));
        }
        finally
        {
            Free(names);
        }

        Assert.Equal(typeof(ReadOnlySpan<IntPtr>), Assert.Single(typeof(E2EBindings).GetMethod(nameof(E2EBindings.MeasureRawNameRange))!.GetParameters()).ParameterType);
        // An untagged range keeps the default string binding.
        Assert.Equal(5, E2EBindings.MeasureDefaultNameRange(["ab", "cde"]));
        Assert.Equal(typeof(ReadOnlySpan<string>), Assert.Single(typeof(E2EBindings).GetMethod(nameof(E2EBindings.MeasureDefaultNameRange))!.GetParameters()).ParameterType);
    }

    [Fact]
    public void A_mutable_char_pointer_pointer_with_the_pointers_shape_binds_whatever_the_raw_path_yields()
    {
        // The generic raw path (as before string arrays) binds a mutable char** as ReadOnlySpan<nint>,
        // and the wrapper adds no writability handling.
        var parameter = Parameter(typeof(E2EBindings), nameof(E2EBindings.RewriteRawNames), 0);
        Assert.Equal(typeof(ReadOnlySpan<IntPtr>), parameter.ParameterType);
    }

    [Fact]
    public void The_pointers_shape_survives_the_projection_of_a_secondary_base_method()
    {
        using var both = new RawNamesBoth();
        var names = Allocate("ab", "cde");
        try
        {
            Assert.Equal(5, both.SumRawNames(names, 2));
            IRawNamesRight view = both;
            Assert.Equal(2, view.SumRawNames(names.AsSpan(0, 1), 1));
        }
        finally
        {
            Free(names);
        }

        Assert.Equal(typeof(ReadOnlySpan<IntPtr>), Parameter(typeof(RawNamesBoth), nameof(RawNamesBoth.SumRawNames), 0).ParameterType);
        Assert.Equal(typeof(ReadOnlySpan<IntPtr>), Parameter(typeof(RawNamesRight), nameof(RawNamesRight.SumRawNames), 0).ParameterType);
    }

    [Theory]
    [InlineData("BadPointersRecord", "bad_pointers_record", "the 'pointers' shape applies to char** only")]
    [InlineData("BadShapeValue", "bad_shape_value", "the value must be 'array', 'single' or 'pointers'")]
    [InlineData("BadShapeScalar", "bad_shape_scalar", "has [[sharpcpp::shape(array)]] but is not a char** or a pointer-to-pointer to a record")]
    [InlineData("BadShapeConflictIn", "bad_shape_conflict_in", "which request different pointer-pointer shapes")]
    [InlineData("BadShapeConflictOut", "bad_shape_conflict_out", "which request different pointer-pointer shapes")]
    public void A_shape_attribute_that_cannot_be_honoured_degrades_the_function_with_a_diagnostic(
        string managedName,
        string nativeName,
        string reason)
    {
        Assert.Null(typeof(E2EBindings).GetMethod(managedName));
        Assert.Contains(
            GeneratedArtifacts.ReadText("generator.log").Split('\n'),
            line => line.Contains($"\t{nativeName}\t", StringComparison.Ordinal) &&
                line.Contains(reason, StringComparison.Ordinal));
    }
}
