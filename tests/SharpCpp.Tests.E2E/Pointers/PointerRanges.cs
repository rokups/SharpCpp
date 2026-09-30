using System;
using System.Linq;

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
    [InlineData("BadEndedPointerPointer", "bad_ended_pointer_pointer", "pointer-to-pointer ranges are not supported yet")]
    [InlineData("BadEndedTemplate", "bad_ended_template", "ended_by on a template function, a class template member or a constructor is not supported")]
    [InlineData("BadEndedBeginDefault", "bad_ended_begin_default", "pointer range begin/end cannot have a default value")]
    [InlineData("BadEndedEndDefault", "bad_ended_end_default", "pointer range begin/end cannot have a default value")]
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
}
