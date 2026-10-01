using System;
using System.Linq;

using SharpCpp.Tests.E2E.Diagnostics;
using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Pointers;

public sealed class PointerPointerTests
{
    private static System.Reflection.ParameterInfo Parameter(string method, int index) =>
        typeof(E2EBindings).GetMethod(method)!.GetParameters()[index];

    [Fact]
    public void Char_pointer_pointer_with_an_adjacent_count_is_an_input_string_array()
    {
        string?[] names = ["ab", "cde"];
        Assert.Equal(5, E2EBindings.MeasureArgv(2, names));
        Assert.Equal(2, E2EBindings.MeasureArgv(1, names));
        Assert.Equal(5, E2EBindings.MeasureMutableNames(names, 2));
        Assert.Equal(5, E2EBindings.MeasureConstNames(names, 2));
        Assert.Equal(typeof(ReadOnlySpan<string>), Parameter(nameof(E2EBindings.MeasureArgv), 1).ParameterType);
        Assert.Equal(typeof(ReadOnlySpan<string>), Parameter(nameof(E2EBindings.MeasureMutableNames), 0).ParameterType);
        Assert.Equal(typeof(ReadOnlySpan<string>), Parameter(nameof(E2EBindings.MeasureConstNames), 0).ParameterType);
    }

    [Fact]
    public void Const_element_pointer_is_an_input_string_array()
    {
        string?[] names = ["ab", "cde"];
        Assert.Equal(5, E2EBindings.MeasureStrings(names, 2));
        Assert.Equal(5, E2EBindings.MeasureCharConstNames(names, 2));
    }

    [Fact]
    public void Char_pointer_pointer_without_a_count_is_a_borrowed_output_string()
    {
        E2EBindings.GetBorrowedLabel(out var label);
        Assert.Equal("borrowed label", label);
        E2EBindings.GetBorrowedLabel(out label);
        Assert.Equal("borrowed label", label);

        E2EBindings.GetBorrowedConstLabel(out var constLabel);
        Assert.Equal("borrowed const label", constLabel);

        E2EBindings.GetNullLabel(out var nullLabel);
        Assert.Null(nullLabel);

        var parameter = Parameter(nameof(E2EBindings.GetBorrowedLabel), 0);
        Assert.Equal(typeof(string).MakeByRefType(), parameter.ParameterType);
        Assert.True(parameter.IsOut);
    }

    [Fact]
    public void Pointer_count_does_not_make_the_string_pointer_an_array()
    {
        E2EBindings.GetLabelWithLength(out var label, out var length);
        Assert.Equal("counted label", label);
        Assert.Equal(13, length);
    }

    [Fact]
    public void Sharpcpp_attributes_override_the_char_pointer_pointer_default()
    {
        Assert.Equal(5, E2EBindings.MeasureForcedNames(2, ["ab", "cde"]));

        E2EBindings.GetForcedLabel(1, out var label);
        Assert.Equal("forced label", label);
    }

    [Fact]
    public void Record_pointer_pointer_with_an_adjacent_count_is_an_input_pointer_array()
    {
        using var first = new PointerPointerItem(3);
        using var second = new PointerPointerItem(4);
        PointerPointerItem?[] items = [first, second];

        Assert.Equal(7, E2EBindings.SumItems(items, 2));
        Assert.Equal(3, E2EBindings.SumItems(items, 1));
        Assert.Equal(7, E2EBindings.SumConstItems(items, 2));
        Assert.Equal(typeof(ReadOnlySpan<PointerPointerItem?>), Parameter(nameof(E2EBindings.SumItems), 0).ParameterType);
    }

    [Fact]
    public void Record_pointer_pointer_without_a_value_count_stays_an_output()
    {
        E2EBindings.PickItem(out var item);
        Assert.Equal(41, item.GetValue());

        E2EBindings.PickItemWithPointerCount(out var items, out var count);
        Assert.Equal(43, items.GetValue());
        Assert.Equal(1u, count);
        Assert.True(Parameter(nameof(E2EBindings.PickItem), 0).IsOut);
    }

    [Fact]
    public void A_size_parameter_is_not_a_count_for_the_output_to_array_flip()
    {
        E2EBindings.PickSizedItem(out var item, 1);
        Assert.Equal(59, item.GetValue());
        Assert.True(Parameter(nameof(E2EBindings.PickSizedItem), 0).IsOut);

        E2EBindings.GetSizedLabel(out var label, 1);
        Assert.Equal("sized label", label);
        Assert.True(Parameter(nameof(E2EBindings.GetSizedLabel), 0).IsOut);
    }

    [Fact]
    public void An_unrelated_or_claimed_count_leaves_a_record_pointer_pointer_an_output()
    {
        ReadOnlySpan<uint> words = [5u];
        E2EBindings.ParseWords(words, 1, out var parsed);
        Assert.Equal(61, parsed.GetValue());
        Assert.True(Parameter(nameof(E2EBindings.ParseWords), 2).IsOut);

        ReadOnlySpan<int> data = [5];
        E2EBindings.ParseCountedData(data, 1, out var result);
        Assert.Equal(67, result.GetValue());
        Assert.True(Parameter(nameof(E2EBindings.ParseCountedData), 2).IsOut);

        E2EBindings.AllocateVertices(1, out var allocation);
        Assert.Equal(71, allocation.GetValue());
        Assert.True(Parameter(nameof(E2EBindings.AllocateVertices), 1).IsOut);

        E2EBindings.AllocateSwapchainData(1, 1, out var swapchain);
        Assert.Equal(73, swapchain.GetValue());
        Assert.True(Parameter(nameof(E2EBindings.AllocateSwapchainData), 2).IsOut);

        E2EBindings.PickItemAfterArgc(1, out var item);
        Assert.Equal(79, item.GetValue());
        Assert.True(Parameter(nameof(E2EBindings.PickItemAfterArgc), 1).IsOut);
    }

    [Fact]
    public void A_related_count_makes_a_record_pointer_pointer_an_input_array()
    {
        using var task = new PointerPointerItem(1);
        using var first = new PointerPointerItem(3);
        using var second = new PointerPointerItem(4);
        PointerPointerItem?[] items = [first, second];

        Assert.Equal(8, E2EBindings.EnqueueTask(task, items, 2));
        Assert.Equal(typeof(ReadOnlySpan<PointerPointerItem?>), Parameter(nameof(E2EBindings.EnqueueTask), 1).ParameterType);
        Assert.Equal(7, E2EBindings.TestDrawables(items, 2));
        Assert.Equal(typeof(ReadOnlySpan<PointerPointerItem?>), Parameter(nameof(E2EBindings.TestDrawables), 0).ParameterType);
    }

    [Fact]
    public void A_record_pointer_before_a_generic_count_owns_it()
    {
        using var owner = new PointerPointerItem(1);
        E2EBindings.PickAfterHandle(owner, 1, out var item);
        Assert.Equal(83, item.GetValue());
        Assert.True(Parameter(nameof(E2EBindings.PickAfterHandle), 2).IsOut);

        E2EBindings.PickLimited(out var limited, 1);
        Assert.Equal(89, limited.GetValue());
        Assert.True(Parameter(nameof(E2EBindings.PickLimited), 0).IsOut);
    }

    [Fact]
    public void Prefixed_and_leading_counts_make_a_record_pointer_pointer_an_input_array()
    {
        using var first = new PointerPointerItem(3);
        using var second = new PointerPointerItem(4);
        PointerPointerItem?[] items = [first, second];

        Assert.Equal(7, E2EBindings.SumCountPrefixed(2, items));
        Assert.Equal(7, E2EBindings.SumLeadingCount(2, items));
        Assert.Equal(7, E2EBindings.SumHungarianItems(items, 2));
        Assert.Equal(typeof(ReadOnlySpan<PointerPointerItem?>), Parameter(nameof(E2EBindings.SumCountPrefixed), 1).ParameterType);
        Assert.Equal(typeof(ReadOnlySpan<PointerPointerItem?>), Parameter(nameof(E2EBindings.SumLeadingCount), 1).ParameterType);
        Assert.Equal(typeof(ReadOnlySpan<PointerPointerItem?>), Parameter(nameof(E2EBindings.SumHungarianItems), 0).ParameterType);
    }

    [Fact]
    public void Counted_by_names_the_count_of_an_array_parameter()
    {
        using var first = new PointerPointerItem(3);
        using var second = new PointerPointerItem(4);
        PointerPointerItem?[] items = [first, second];
        ReadOnlySpan<int> values = [10, 20];

        Assert.Equal(7, E2EBindings.SumCountedItems(items, 2));
        Assert.Equal(typeof(ReadOnlySpan<PointerPointerItem?>), Parameter(nameof(E2EBindings.SumCountedItems), 0).ParameterType);
        Assert.Equal(5, E2EBindings.MeasureCountedNames(["ab", "cde"], 2));
        Assert.Equal(typeof(ReadOnlySpan<string>), Parameter(nameof(E2EBindings.MeasureCountedNames), 0).ParameterType);
        Assert.Equal(30, E2EBindings.SumCountedValues(values, 2));
        Assert.Equal(typeof(ReadOnlySpan<int>), Parameter(nameof(E2EBindings.SumCountedValues), 0).ParameterType);

        Assert.Equal(37, E2EBindings.SumSharedCount(values, items, 2));
        Assert.Equal(typeof(ReadOnlySpan<int>), Parameter(nameof(E2EBindings.SumSharedCount), 0).ParameterType);
        Assert.Equal(typeof(ReadOnlySpan<PointerPointerItem?>), Parameter(nameof(E2EBindings.SumSharedCount), 1).ParameterType);
    }

    [Fact]
    public void Counted_by_does_not_override_an_explicit_out_attribute()
    {
        E2EBindings.PickOutCounted(out var item, 1);
        Assert.Equal(97, item.GetValue());
        Assert.True(Parameter(nameof(E2EBindings.PickOutCounted), 0).IsOut);
    }

    [Fact]
    public void A_consumer_set_count_names_the_count_of_an_array_parameter()
    {
        using var first = new PointerPointerItem(3);
        using var second = new PointerPointerItem(4);
        PointerPointerItem?[] items = [first, second];
        ReadOnlySpan<int> values = [10, 20];

        Assert.Equal(30, E2EBindings.SumConfiguredCountedValues(values, 2));
        Assert.Equal(typeof(ReadOnlySpan<int>), Parameter(nameof(E2EBindings.SumConfiguredCountedValues), 0).ParameterType);
        Assert.Equal(7, E2EBindings.SumConfiguredCountedItems(items, 2));
        Assert.Equal(typeof(ReadOnlySpan<PointerPointerItem?>), Parameter(nameof(E2EBindings.SumConfiguredCountedItems), 0).ParameterType);
        Assert.Equal(7, E2EBindings.SumConfiguredCountedNoop(items, 2));
        Assert.Equal(typeof(ReadOnlySpan<PointerPointerItem?>), Parameter(nameof(E2EBindings.SumConfiguredCountedNoop), 0).ParameterType);

        E2EBindings.PickConfiguredCountedSingle(out var item, 1);
        Assert.Equal(101, item.GetValue());
        Assert.True(Parameter(nameof(E2EBindings.PickConfiguredCountedSingle), 0).IsOut);
    }

    [Fact]
    public void Argc_pairs_with_a_char_pointer_pointer_that_follows_it()
    {
        Assert.Equal(5, E2EBindings.MeasureArgs(2, ["ab", "cde"]));
        Assert.Equal(typeof(ReadOnlySpan<string>), Parameter(nameof(E2EBindings.MeasureArgs), 1).ParameterType);
    }

    [Fact]
    public void String_arrays_marshal_null_elements_empty_spans_and_non_ascii_text()
    {
        const int Checksum = 'a' + 'b' + 'c';
        Assert.Equal(Checksum, E2EBindings.NameChecksum(["ab", "c"], 2));
        Assert.Equal(Checksum + 1000, E2EBindings.NameChecksum(["abc", null], 2));
        Assert.Equal(-1, E2EBindings.NameChecksum([], 0));
        Assert.Equal(0xC3 + 0xA9 + 0xE2 + 0x82 + 0xAC, E2EBindings.NameChecksum(["é€"], 1));
        Assert.Equal(5, E2EBindings.MeasureCountedNames(["ab", "cde"], 2));
        Assert.Equal(0, E2EBindings.MeasureCountedNames([], 0));
    }

    [Fact]
    public void String_arrays_over_the_stack_limit_marshal_through_the_heap()
    {
        var names = new string?[100];
        Array.Fill(names, "a");
        names[70] = null;

        Assert.Equal(99 * 'a' + 1000, E2EBindings.NameChecksum(names, 100));
        Assert.Equal(100, E2EBindings.MeasureMutableNames(names.Select(static name => name ?? "a").ToArray(), 100));
    }

    [Fact]
    public void A_mutable_string_array_is_input_and_native_rewrites_are_not_read_back()
    {
        string?[] names = ["ab", "cde", "f"];

        Assert.Equal(3 + 2 + 11, E2EBindings.RewriteNames(names, 3));
        Assert.Equal(new string?[] { "ab", "cde", "f" }, names);
        Assert.Equal(typeof(ReadOnlySpan<string>), Parameter(nameof(E2EBindings.RewriteNames), 0).ParameterType);
    }

    [Fact]
    public void Address_arrays_stay_spans_of_raw_pointers()
    {
        Assert.Equal(typeof(ReadOnlySpan<IntPtr>), Parameter(nameof(E2EBindings.SumPointerAddresses), 0).ParameterType);
    }

    [Fact]
    public void Sharpcpp_attributes_override_the_record_pointer_pointer_default()
    {
        E2EBindings.PickForcedItem(out var item, 1);
        Assert.Equal(47, item.GetValue());

        using var first = new PointerPointerItem(5);
        using var second = new PointerPointerItem(6);
        Assert.Equal(11, E2EBindings.SumForcedItems(2, [first, second]));
    }

    [Fact]
    public void Consumer_metadata_selects_the_pointer_pointer_shape()
    {
        Assert.Equal(5, E2EBindings.MeasureConfiguredNames(2, ["ab", "cde"]));

        E2EBindings.GetConfiguredLabel(1, out var label);
        Assert.Equal("configured label", label);

        using var first = new PointerPointerItem(7);
        using var second = new PointerPointerItem(8);
        Assert.Equal(15, E2EBindings.SumConfiguredItems(2, [first, second]));

        E2EBindings.PickConfiguredItem(out var item, 1);
        Assert.Equal(53, item.GetValue());
    }

    [Theory]
    [InlineData("MeasureBadShape", "measure_bad_shape", "the parameter is not a char** or a pointer-to-pointer to a record")]
    [InlineData("MeasureBadValue", "measure_bad_value", "the value must be 'array' or 'single'")]
    [InlineData("PickConstItem", "pick_const_item", "the record pointer is const and cannot be written")]
    [InlineData("GetConstLabel", "get_const_label", "the string pointer is const and cannot be written")]
    [InlineData("GetAttributedConstLabel", "get_attributed_const_label", "the string pointer is const and cannot be written")]
    [InlineData("PickRetypedItem", "pick_retyped_item", "the parameter is not bound as a record pointer")]
    public void Shapes_that_cannot_be_honoured_degrade_the_function_with_a_diagnostic(
        string managedName,
        string nativeName,
        string reason)
    {
        Assert.Null(typeof(E2EBindings).GetMethod(managedName));
        Assert.Contains(
            GeneratedArtifacts.ReadText("generator.log").Split('\n'),
            line => line.Contains($"\t{nativeName}\t", StringComparison.Ordinal) &&
                line.Contains("cannot use pointer-pointer shape", StringComparison.Ordinal) &&
                line.Contains(reason, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("BadCountedUnknown", "bad_counted_unknown", "parameter 'items' is counted by 'missing', which is not an integer value parameter of the function")]
    [InlineData("BadCountedPointer", "bad_counted_pointer", "parameter 'items' is counted by 'n', which is not an integer value parameter of the function")]
    [InlineData("BadCountedValue", "bad_counted_value", "parameter 'values' is counted by 'n' but is not a pointer")]
    [InlineData("BadCountedRecord", "bad_counted_record", "parameter 'items' is counted by 'n' but has no input array projection")]
    [InlineData("BadCountedRef", "bad_counted_ref", "parameter 'names' is counted by 'n' but is not a pointer")]
    public void Counted_by_that_cannot_be_honoured_degrades_the_function_with_a_diagnostic(
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
