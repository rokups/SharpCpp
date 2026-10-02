using System;
using System.Linq;

using SharpCpp.Tests.E2E.Diagnostics;
using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Pointers;

public sealed class NullTerminatedStringTests
{
    private static System.Reflection.ParameterInfo Parameter(Type type, string method, int index) =>
        type.GetMethod(method)!.GetParameters()[index];

    [Fact]
    public void The_wrapper_appends_the_terminator_of_a_tagged_array()
    {
        var length = E2EBindings.MeasureTerminatedNames(["ab", "cde"], out var elements);
        Assert.Equal(5, length);
        Assert.Equal(2, elements);

        length = E2EBindings.MeasureTerminatedNames(["ab"], out elements);
        Assert.Equal(2, length);
        Assert.Equal(1, elements);

        Assert.Equal(typeof(ReadOnlySpan<string>), Parameter(typeof(E2EBindings), nameof(E2EBindings.MeasureTerminatedNames), 0).ParameterType);
    }

    [Fact]
    public void An_empty_span_still_passes_a_null_pointer()
    {
        var length = E2EBindings.MeasureTerminatedNames([], out var elements);
        Assert.Equal(-1, length);
        Assert.Equal(-1, elements);
    }

    [Fact]
    public void A_caller_that_already_appends_a_null_keeps_working()
    {
        var length = E2EBindings.MeasureTerminatedNames(["ab", "cde", null], out var elements);
        Assert.Equal(5, length);
        Assert.Equal(2, elements);
    }

    [Fact]
    public void A_long_tagged_array_takes_the_heap_path()
    {
        var names = Enumerable.Repeat("xy", 100).ToArray();
        var length = E2EBindings.MeasureTerminatedNames(names, out var elements);
        Assert.Equal(200, length);
        Assert.Equal(100, elements);

        names = Enumerable.Repeat("x", 64).ToArray();
        Assert.Equal(64, E2EBindings.MeasureTerminatedNames(names, out elements));
        Assert.Equal(64, elements);
    }

    [Fact]
    public void Consumer_metadata_tags_a_terminated_array()
    {
        var length = E2EBindings.MeasureConfiguredTerminatedNames(["ab", "cde"], out var elements);
        Assert.Equal(5, length);
        Assert.Equal(2, elements);

        Assert.Equal(-1, E2EBindings.MeasureConfiguredTerminatedNames([], out elements));
        Assert.Equal(-1, elements);

        elements = 0;
        E2EBindings.MeasureConfiguredTerminatedNames(Enumerable.Repeat("x", 70).ToArray(), out elements);
        Assert.Equal(70, elements);
    }

    [Fact]
    public void An_untagged_array_passes_exactly_the_strings()
    {
        // The caller appends the NULL itself, as before the tag existed.
        var length = E2EBindings.MeasureUntaggedNames(["ab", "cde", null], out var elements);
        Assert.Equal(5, length);
        Assert.Equal(2, elements);
        Assert.Equal(typeof(ReadOnlySpan<string>), Parameter(typeof(E2EBindings), nameof(E2EBindings.MeasureUntaggedNames), 0).ParameterType);
    }

    [Fact]
    public void A_count_is_passed_through_and_does_not_include_the_terminator()
    {
        // The trailing int* after the count binds as a span (the existing count-adjacent rule).
        var terminated = new int[1];
        var length = E2EBindings.MeasureCountedTerminatedNames(["ab", "cde"], 2, terminated);
        Assert.Equal(5, length);
        Assert.Equal(1, terminated[0]);

        // The count stays the caller's: a smaller count reads fewer strings.
        length = E2EBindings.MeasureCountedTerminatedNames(["ab", "cde"], 1, terminated);
        Assert.Equal(2, length);
        Assert.Equal(0, terminated[0]);

        length = E2EBindings.MeasureCountedTerminatedNames(Enumerable.Repeat("x", 100).ToArray(), 100, terminated);
        Assert.Equal(100, length);
        Assert.Equal(1, terminated[0]);
    }

    [Fact]
    public void A_mutable_array_with_the_in_attribute_is_terminated_and_freed_correctly()
    {
        Assert.Equal(2, E2EBindings.RewriteTerminatedNames(["ab", "cde"], out var elements));
        Assert.Equal(2, elements);

        Assert.Equal(70, E2EBindings.RewriteTerminatedNames(Enumerable.Repeat("x", 70).ToArray(), out elements));
        Assert.Equal(70, elements);

        Assert.Equal(-1, E2EBindings.RewriteTerminatedNames([], out elements));
        Assert.Equal(typeof(ReadOnlySpan<string>), Parameter(typeof(E2EBindings), nameof(E2EBindings.RewriteTerminatedNames), 0).ParameterType);
    }

    [Fact]
    public void The_tag_survives_the_projection_of_a_secondary_base_method()
    {
        using var both = new TerminatedNamesBoth();
        Assert.Equal(5, both.MeasureNames(["ab", "cde"], out var elements));
        Assert.Equal(2, elements);

        ITerminatedNamesRight view = both;
        Assert.Equal(2, view.MeasureNames(["ab"], out elements));
        Assert.Equal(1, elements);
        Assert.Equal(-1, view.MeasureNames([], out elements));
    }

    [Theory]
    [InlineData("BadTerminatedInts", "bad_terminated_ints", "parameter 'values' is tagged NULL-terminated but is not an input string array")]
    [InlineData("BadTerminatedSingle", "bad_terminated_single", "parameter 'value' is tagged NULL-terminated but is not an input string array")]
    [InlineData("BadTerminatedRange", "bad_terminated_range", "parameter 'begin' is tagged NULL-terminated but is also ended_by")]
    [InlineData("BadTerminatedRaw", "bad_terminated_raw", "parameter 'names' is tagged NULL-terminated but has the 'pointers' shape")]
    public void A_tag_that_cannot_be_honoured_degrades_the_function_with_a_diagnostic(
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
