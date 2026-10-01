using System;
using System.Linq;
using System.Runtime.InteropServices;

using SharpCpp.Tests.E2E.Diagnostics;
using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Pointers;

public sealed class PointerRangeTests
{
    private static System.Reflection.MethodInfo Method(string name) => typeof(E2EBindings).GetMethod(name)!;

    [Fact]
    public void Const_range_binds_as_a_read_only_span_and_hides_the_end_pointer()
    {
        Assert.Equal(10, E2EBindings.SumRange([1, 2, 3, 4]));
        Assert.Equal(0, E2EBindings.SumRange([]));
        Assert.Equal(0, E2EBindings.SumRange(default));

        var parameters = Method(nameof(E2EBindings.SumRange)).GetParameters();
        var parameter = Assert.Single(parameters);
        Assert.Equal(typeof(ReadOnlySpan<int>), parameter.ParameterType);
        Assert.Equal("begin", parameter.Name);
    }

    [Fact]
    public void Mutable_range_binds_as_a_span_and_writes_are_visible()
    {
        var values = new int[4];
        E2EBindings.FillRange(values);
        Assert.Equal(new[] { 1, 2, 3, 4 }, values);
        E2EBindings.FillRange([]);

        Assert.Equal(typeof(Span<int>), Assert.Single(Method(nameof(E2EBindings.FillRange)).GetParameters()).ParameterType);
    }

    [Fact]
    public void Parameters_after_the_end_pointer_keep_their_order()
    {
        Assert.Equal(6, E2EBindings.SumRangeThenFlag([1, 2, 3], true));
        Assert.Equal(-6, E2EBindings.SumRangeThenFlag([1, 2, 3], false));

        var parameters = Method(nameof(E2EBindings.SumRangeThenFlag)).GetParameters();
        Assert.Equal(new[] { typeof(ReadOnlySpan<int>), typeof(bool) }, parameters.Select(static parameter => parameter.ParameterType));
    }

    [Fact]
    public void A_trailing_default_survives_the_hidden_end_pointer()
    {
        Assert.Equal(6, E2EBindings.SumRangeScaled([1, 2, 3]));
        Assert.Equal(12, E2EBindings.SumRangeScaled([1, 2, 3], 2));

        var parameters = Method(nameof(E2EBindings.SumRangeScaled)).GetParameters();
        Assert.Equal(new[] { typeof(ReadOnlySpan<int>), typeof(int) }, parameters.Select(static parameter => parameter.ParameterType));
        Assert.True(parameters[1].HasDefaultValue);
    }

    [Fact]
    public void Consumer_metadata_binds_an_untagged_pair_as_a_range()
    {
        Assert.Equal(6, E2EBindings.SumConfiguredRange([1, 2, 3]));
        Assert.Equal(typeof(ReadOnlySpan<int>), Assert.Single(Method(nameof(E2EBindings.SumConfiguredRange)).GetParameters()).ParameterType);
    }

    [Fact]
    public void An_untagged_pair_has_no_name_based_default()
    {
        Assert.Equal(2, Method(nameof(E2EBindings.SumUntaggedPair)).GetParameters().Length);
    }

    [Theory]
    [InlineData("BadEndedUnknown", "bad_ended_unknown", "parameter 'begin' is ended by 'missing' but that is not another parameter of the function")]
    [InlineData("BadEndedEmpty", "bad_ended_empty", "parameter 'begin' has an empty [[sharpcpp::ended_by]] attribute")]
    [InlineData("BadEndedType", "bad_ended_type", "parameter 'begin' is ended by 'end' but its end is not a pointer of the same type")]
    [InlineData("BadEndedScalar", "bad_ended_scalar", "parameter 'begin' is ended by 'end' but the parameter is not a pointer")]
    [InlineData("BadEndedGap", "bad_ended_gap", "parameter 'begin' is ended by 'end' but its end is not the parameter directly after it")]
    [InlineData("BadEndedCounted", "bad_ended_counted", "parameter 'begin' is ended by 'end' but the parameter is also counted_by")]
    [InlineData("BadEndedChain", "bad_ended_chain", "parameter 'begin' is ended by 'mid' but its end is itself counted_by or ended_by")]
    [InlineData("BadEndedEndCounted", "bad_ended_end_counted", "parameter 'begin' is ended by 'end' but its end is itself counted_by or ended_by")]
    [InlineData("BadEndedRecord", "bad_ended_record", "parameter 'begin' is ended by 'end' but has no input array projection")]
    [InlineData("BadEndedTemplate", "bad_ended_template", "ended_by on a template function, a class template member or a constructor is not supported")]
    [InlineData("BadEndedWidgetOut", "bad_ended_widget_out", "the parameter has an explicit single or out pointer-pointer shape")]
    [InlineData("BadEndedWidgetEndOut", "bad_ended_widget_end_out", "its end has an explicit pointer-pointer shape")]
    [InlineData("BadEndedBeginDefault", "bad_ended_begin_default", "pointer range begin/end cannot have a default value")]
    [InlineData("BadEndedEndDefault", "bad_ended_end_default", "pointer range begin/end cannot have a default value")]
    [InlineData("BadEndedWidgetConstMismatch", "bad_ended_widget_const_mismatch", "parameter 'begin' is ended by 'end' but its end is not a pointer of the same type")]
    public void Ended_by_that_cannot_be_honoured_degrades_the_function_with_a_diagnostic(
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

    [Fact]
    public void Ended_by_on_a_constructor_degrades_the_constructor_with_a_diagnostic()
    {
        Assert.Contains(
            GeneratedArtifacts.ReadText("generator.log").Split('\n'),
            line => line.Contains("range_holder", StringComparison.Ordinal) &&
                line.Contains("ended_by on a template function, a class template member or a constructor is not supported", StringComparison.Ordinal));
    }

    [Fact]
    public void Ended_by_on_a_class_template_member_degrades_the_member_with_a_diagnostic()
    {
        Assert.Null(typeof(RangeTemplateHolder<int>).GetMethod("BadEndedMember"));
        Assert.Contains(
            GeneratedArtifacts.ReadText("generator.log").Split('\n'),
            line => line.Contains("bad_ended_member", StringComparison.Ordinal) &&
                line.Contains("ended_by on a template function, a class template member or a constructor is not supported", StringComparison.Ordinal));
    }

    [Fact]
    public void A_range_survives_the_projection_of_a_secondary_base_method()
    {
        using var both = new RangeBoth();

        Assert.Equal(6, both.SumRightRange([1, 2, 3]));
        Assert.Equal(0, both.SumRightRange([]));
        var parameter = Assert.Single(typeof(RangeBoth).GetMethod(nameof(RangeBoth.SumRightRange))!.GetParameters());
        Assert.Equal(typeof(ReadOnlySpan<int>), parameter.ParameterType);

        IRangeRight view = both;
        Assert.Equal(3, view.SumRightRange([1, 2]));
    }

    [Fact]
    public void Enum_ranges_bind_as_spans_of_the_enum()
    {
        Assert.Equal(7, E2EBindings.SumColorRange([RangeColor.Red, RangeColor.Green, RangeColor.Blue]));
        Assert.Equal(0, E2EBindings.SumColorRange([]));
        Assert.Equal(typeof(ReadOnlySpan<RangeColor>), Assert.Single(Method(nameof(E2EBindings.SumColorRange)).GetParameters()).ParameterType);

        var colors = new RangeColor[3];
        E2EBindings.FillColorRange(colors);
        Assert.All(colors, static color => Assert.Equal(RangeColor.Blue, color));
        E2EBindings.FillColorRange([]);
        Assert.Equal(typeof(Span<RangeColor>), Assert.Single(Method(nameof(E2EBindings.FillColorRange)).GetParameters()).ParameterType);
    }

    [Fact]
    public void Record_pointer_ranges_bind_as_spans_of_nullable_records()
    {
        using var first = new RangeWidget(5);
        using var second = new RangeWidget(6);

        Assert.Equal(11, E2EBindings.SumWidgetRange([first, second]));
        Assert.Equal(105, E2EBindings.SumWidgetRange([first, null]));
        Assert.Equal(0, E2EBindings.SumWidgetRange([]));
        Assert.Equal(11, E2EBindings.SumConstWidgetRange([first, second]));
        Assert.Equal(0, E2EBindings.SumConstWidgetRange([]));

        Assert.Equal(typeof(ReadOnlySpan<RangeWidget?>), Assert.Single(Method(nameof(E2EBindings.SumWidgetRange)).GetParameters()).ParameterType);
        Assert.Equal(typeof(ReadOnlySpan<RangeWidget?>), Assert.Single(Method(nameof(E2EBindings.SumConstWidgetRange)).GetParameters()).ParameterType);
    }

    [Fact]
    public void Char_pointer_pointer_ranges_bind_as_spans_of_strings()
    {
        string?[] strings = ["ab", "cde"];
        Assert.Equal(5, E2EBindings.MeasureCharRange(strings));
        Assert.Equal(5, E2EBindings.MeasureConstCharRange(strings));
        Assert.Equal(2, E2EBindings.MeasureCharRange(strings.AsSpan(0, 1)));
        Assert.Equal(0, E2EBindings.MeasureCharRange([]));
        Assert.Equal(0, E2EBindings.MeasureConstCharRange([]));

        Assert.Equal(typeof(ReadOnlySpan<string>), Assert.Single(Method(nameof(E2EBindings.MeasureCharRange)).GetParameters()).ParameterType);
        Assert.Equal(typeof(ReadOnlySpan<string>), Assert.Single(Method(nameof(E2EBindings.MeasureConstCharRange)).GetParameters()).ParameterType);
    }

    [Fact]
    public void String_ranges_marshal_null_elements_empty_spans_non_ascii_text_and_large_ranges()
    {
        Assert.Equal('a' + 'b' + 'c', E2EBindings.CharRangeChecksum(["ab", "c"]));
        Assert.Equal('a' + 1000, E2EBindings.CharRangeChecksum(["a", null]));
        Assert.Equal(-1, E2EBindings.CharRangeChecksum([]));
        Assert.Equal(0xC3 + 0xA9, E2EBindings.CharRangeChecksum(["é"]));

        var names = new string?[100];
        Array.Fill(names, "a");
        Assert.Equal(100 * 'a', E2EBindings.CharRangeChecksum(names));
    }

    [Fact]
    public void A_mutable_string_range_is_input_and_native_rewrites_are_not_read_back()
    {
        string?[] strings = ["ab", "cde"];

        Assert.Equal(11 + 3, E2EBindings.RewriteCharRange(strings));
        Assert.Equal(new string?[] { "ab", "cde" }, strings);
    }

    [Fact]
    public void Const_void_pointer_ranges_bind_as_spans_of_raw_pointers()
    {
        Assert.Equal(2, E2EBindings.CountAddressRange([1, 0, 2]));
        Assert.Equal(0, E2EBindings.CountAddressRange([]));
        Assert.Equal(typeof(ReadOnlySpan<IntPtr>), Assert.Single(Method(nameof(E2EBindings.CountAddressRange)).GetParameters()).ParameterType);
    }

    [Fact]
    public void An_explicit_in_shape_on_a_record_range_is_the_default_input_array()
    {
        using var first = new RangeWidget(5);
        using var second = new RangeWidget(6);

        Assert.Equal(11, E2EBindings.SumInWidgetRange([first, second]));
        Assert.Equal(typeof(ReadOnlySpan<RangeWidget?>), Assert.Single(Method(nameof(E2EBindings.SumInWidgetRange)).GetParameters()).ParameterType);
    }

    [Fact]
    public void A_pure_virtual_range_binds_through_the_base_and_the_derived_wrapper()
    {
        using var shape = E2EBindings.CreateDoublingRangeShape();
        Assert.Equal(12, shape.Sum([1, 2, 3]));
        Assert.Equal(0, shape.Sum([]));
        Assert.Equal(typeof(ReadOnlySpan<int>), Assert.Single(typeof(RangeShape).GetMethod(nameof(RangeShape.Sum))!.GetParameters()).ParameterType);

        using var derived = new DoublingRangeShape();
        Assert.Equal(6, derived.Sum([1, 2]));
        Assert.Equal(0, derived.Sum(default));
        Assert.Equal(typeof(ReadOnlySpan<int>), Assert.Single(typeof(DoublingRangeShape).GetMethod(nameof(DoublingRangeShape.Sum))!.GetParameters()).ParameterType);
    }

    [Fact]
    public void A_virtual_record_range_keeps_the_trailing_parameter_and_skips_null_elements()
    {
        var parameters = typeof(WidgetQuery).GetMethod(nameof(WidgetQuery.TestWidgets))!.GetParameters();
        Assert.Equal(new[] { typeof(ReadOnlySpan<RangeWidget?>), typeof(bool) }, parameters.Select(static parameter => parameter.ParameterType));

        using var first = new RangeWidget(5);
        using var second = new RangeWidget(6);
        using var query = E2EBindings.CreateSummingWidgetQuery();

        query.TestWidgets([first, second], true);
        Assert.Equal(11, query.Total);
        query.TestWidgets([first, null, second], true);
        Assert.Equal(22, query.Total);
        query.TestWidgets([first, second], false);
        Assert.Equal(22, query.Total);
        query.TestWidgets([], true);
        Assert.Equal(22, query.Total);
    }

    [Fact]
    public void A_director_callback_with_a_record_range_calls_the_managed_override_with_a_span()
    {
        using var first = new RangeWidget(5);
        using var second = new RangeWidget(6);
        using var visitor = new ManagedRangeVisitor();

        Assert.Equal(5 + 6 - 100, E2EBindings.InvokeRangeVisitor(visitor, first, second));
        Assert.Equal(3, visitor.LastLength);
        Assert.Equal(0, E2EBindings.InvokeRangeVisitorEmpty(visitor));
        Assert.Equal(0, visitor.LastLength);
        Assert.Equal(new[] { typeof(ReadOnlySpan<RangeWidget?>) }, typeof(RangeVisitor).GetMethod(nameof(RangeVisitor.Visit))!.GetParameters().Select(static parameter => parameter.ParameterType));

        // The span elements are borrowed: the native widgets survive the callback.
        Assert.Equal(5, first.GetValue());
        Assert.Equal(6, second.GetValue());
    }

    [Fact]
    public void A_director_range_without_a_managed_override_keeps_the_native_implementation()
    {
        using var first = new RangeWidget(5);
        using var second = new RangeWidget(6);
        using var passthrough = new PassthroughRangeVisitor();

        Assert.Equal(3, E2EBindings.InvokeRangeVisitorCount(passthrough, first, second));
        Assert.Equal(0, E2EBindings.InvokeRangeVisitorCountEmpty(passthrough));
    }

    [Fact]
    public void A_managed_override_of_a_non_pure_director_range_runs()
    {
        using var first = new RangeWidget(5);
        using var second = new RangeWidget(6);
        using var visitor = new CountingRangeVisitor();

        Assert.Equal(11, E2EBindings.InvokeRangeVisitorCount(visitor, first, second));
        Assert.Equal(3, visitor.LastLength);
        Assert.Equal(0, E2EBindings.InvokeRangeVisitorCountEmpty(visitor));
        Assert.Equal(0, visitor.LastLength);
    }

    [Fact]
    public void A_director_callback_with_an_int_range_calls_the_managed_override_with_a_span()
    {
        using var visitor = new ManagedIntRangeVisitor();

        Assert.Equal(6, E2EBindings.InvokeIntRangeVisitor(visitor));
        Assert.Equal(3, visitor.LastLength);
        Assert.Equal(0, E2EBindings.InvokeIntRangeVisitorEmpty(visitor));
        Assert.Equal(0, visitor.LastLength);
        Assert.Equal(new[] { typeof(ReadOnlySpan<int>) }, typeof(IntRangeVisitor).GetMethod(nameof(IntRangeVisitor.Visit))!.GetParameters().Select(static parameter => parameter.ParameterType));
    }

    [Fact]
    public void A_director_callback_with_a_writable_range_writes_through_to_native_memory()
    {
        using var visitor = new ManagedIntRangeVisitor();

        Assert.Equal(10 + 20 + 30 + 40, E2EBindings.InvokeIntRangeVisitorFill(visitor));
        Assert.Equal(4, visitor.LastLength);
        Assert.Equal(new[] { typeof(Span<int>) }, typeof(IntRangeVisitor).GetMethod(nameof(IntRangeVisitor.Fill))!.GetParameters().Select(static parameter => parameter.ParameterType));
    }

    [Fact]
    public void A_director_callback_with_an_enum_range_calls_the_managed_override_with_a_span()
    {
        using var visitor = new ManagedColorRangeVisitor();

        Assert.Equal(1 + 4 + 2, E2EBindings.InvokeColorRangeVisitor(visitor));
        Assert.Equal(new[] { RangeColor.Red, RangeColor.Blue, RangeColor.Green }, visitor.Seen);
        Assert.Equal(new[] { typeof(ReadOnlySpan<RangeColor>) }, typeof(ColorRangeVisitor).GetMethod(nameof(ColorRangeVisitor.Visit))!.GetParameters().Select(static parameter => parameter.ParameterType));
    }

    [Fact]
    public void A_director_callback_with_an_address_range_receives_a_span_of_pointers()
    {
        var value = 0;
        using var visitor = new ManagedAddressRangeVisitor();
        unsafe
        {
            Assert.Equal(1, E2EBindings.InvokeAddressRangeVisitor(visitor, (IntPtr)(&value)));
            Assert.Equal(new[] { (IntPtr)(&value), IntPtr.Zero }, visitor.Seen);
        }

        Assert.Equal(new[] { typeof(ReadOnlySpan<IntPtr>) }, typeof(AddressRangeVisitor).GetMethod(nameof(AddressRangeVisitor.Visit))!.GetParameters().Select(static parameter => parameter.ParameterType));
    }

    [Fact]
    public void A_consumer_tag_on_a_lowered_director_pointer_is_not_bound_and_the_reason_is_logged()
    {
        Assert.Null(typeof(E2EBindings).Assembly.GetType("SharpCpp.Tests.E2E.Generated.LoweredRangeVisitor"));
        var lines = GeneratedArtifacts.ReadText("generator.log").Split('\n').Where(static line => line.Contains("lowered_range_visitor", StringComparison.Ordinal)).ToArray();
        Assert.Contains(lines, static line => line.Contains("was lowered to an address value for the director callback before the consumer tag was applied; tag it with [[sharpcpp::ended_by]] in the header", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, static line => line.Contains("the parameter is not a pointer", StringComparison.Ordinal));
    }

    private sealed class ManagedIntRangeVisitor : IntRangeVisitor
    {
        public int LastLength { get; private set; } = -1;

        public override int Visit(ReadOnlySpan<int> begin)
        {
            LastLength = begin.Length;
            var result = 0;
            foreach (var item in begin)
                result += item;
            return result;
        }

        public override int Fill(Span<int> begin)
        {
            LastLength = begin.Length;
            for (var index = 0; index < begin.Length; index++)
                begin[index] = (index + 1) * 10;
            return begin.Length;
        }
    }

    private sealed class ManagedAddressRangeVisitor : AddressRangeVisitor
    {
        public IntPtr[] Seen { get; private set; } = [];

        public override int Visit(ReadOnlySpan<IntPtr> begin)
        {
            Seen = begin.ToArray();
            return begin.ToArray().Count(static item => item != IntPtr.Zero);
        }
    }

    private sealed class ManagedColorRangeVisitor : ColorRangeVisitor
    {
        public RangeColor[] Seen { get; private set; } = [];

        public override int Visit(ReadOnlySpan<RangeColor> begin)
        {
            Seen = begin.ToArray();
            var result = 0;
            foreach (var item in begin)
                result += (int)item;
            return result;
        }
    }

    private class ManagedRangeVisitor : RangeVisitor
    {
        public int LastLength { get; protected set; } = -1;

        public override int Visit(ReadOnlySpan<RangeWidget?> begin)
        {
            LastLength = begin.Length;
            var result = 0;
            foreach (var widget in begin)
                result += widget is null ? -100 : widget.GetValue();
            return result;
        }
    }

    private sealed class PassthroughRangeVisitor : RangeVisitor
    {
        public override int Visit(ReadOnlySpan<RangeWidget?> begin) => begin.Length;
    }

    private sealed class CountingRangeVisitor : ManagedRangeVisitor
    {
        public override int Count(ReadOnlySpan<RangeWidget?> begin)
        {
            LastLength = begin.Length;
            var result = 0;
            foreach (var widget in begin)
                result += widget?.GetValue() ?? 0;
            return result;
        }
    }
}
