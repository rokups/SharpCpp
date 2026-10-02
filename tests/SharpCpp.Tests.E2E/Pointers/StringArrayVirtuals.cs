using System;
using System.Collections.Generic;
using System.Linq;

using SharpCpp.Tests.E2E.Diagnostics;
using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Pointers;

public sealed class StringArrayVirtualTests
{
    [Fact]
    public void A_director_callback_with_a_string_range_receives_managed_strings()
    {
        using var visitor = new ManagedStringRangeVisitor();

        Assert.Equal(3, E2EBindings.InvokeStringRangeVisitor(visitor));
        Assert.Equal(new string?[] { "ab", null, "cde" }, visitor.Seen);
        Assert.Equal(0, E2EBindings.InvokeEmptyStringRangeVisitor(visitor));
        Assert.Empty(visitor.Seen);
        Assert.Equal(new[] { typeof(ReadOnlySpan<string>) }, typeof(StringRangeVisitor).GetMethod(nameof(StringRangeVisitor.Visit))!.GetParameters().Select(static parameter => parameter.ParameterType));
    }

    [Fact]
    public void A_director_callback_with_a_counted_string_array_receives_managed_strings_and_the_count()
    {
        using var visitor = new ManagedCountedStringVisitor();

        Assert.Equal(9, E2EBindings.InvokeCountedStringVisitor(visitor));
        Assert.Equal(new string?[] { "ab", null, "cde" }, visitor.AfterNames);
        Assert.Equal(3u, visitor.AfterCount);
        Assert.Equal(new string?[] { "ab", null, "cde" }, visitor.BeforeNames);
        Assert.Equal(3, visitor.BeforeCount);
        Assert.Equal(new string?[] { "ab", null, "cde" }, visitor.TaggedNames);
        Assert.Equal((7, 3), (visitor.TaggedTag, visitor.TaggedTotal));

        Assert.Equal(0, E2EBindings.InvokeEmptyCountedStringVisitor(visitor));
        Assert.Empty(visitor.AfterNames);
        Assert.Equal(0u, visitor.AfterCount);
        Assert.Empty(visitor.BeforeNames);
        Assert.Equal(0, visitor.BeforeCount);
        Assert.Empty(visitor.TaggedNames);
        Assert.Equal(0, visitor.TaggedTotal);

        Assert.Equal(
            new[] { typeof(ReadOnlySpan<string>), typeof(uint) },
            typeof(CountedStringVisitor).GetMethod(nameof(CountedStringVisitor.After))!.GetParameters().Select(static parameter => parameter.ParameterType));
        Assert.Equal(
            new[] { typeof(int), typeof(ReadOnlySpan<string>) },
            typeof(CountedStringVisitor).GetMethod(nameof(CountedStringVisitor.Before))!.GetParameters().Select(static parameter => parameter.ParameterType));
    }

    [Theory]
    [InlineData("UncountedStringVisitor", "uncounted_string_visitor", "parameter 'names' is a string array without a count parameter; tag it with [[sharpcpp::counted_by(...)]]")]
    [InlineData("UnrelatedCountStringVisitor", "unrelated_count_string_visitor", "parameter 'names' is a string array without a count parameter; tag it with [[sharpcpp::counted_by(...)]]")]
    [InlineData("RawCountedStringVisitor", "raw_counted_string_visitor", "parameter 'names' must be an in-value parameter")]
    public void A_director_callback_with_a_string_array_it_cannot_decode_is_ignored_with_a_diagnostic(
        string managedName,
        string nativeName,
        string reason)
    {
        Assert.Null(typeof(E2EBindings).Assembly.GetType($"SharpCpp.Tests.E2E.Generated.{managedName}"));
        Assert.Contains(
            GeneratedArtifacts.ReadText("generator.log").Split('\n'),
            line => line.Contains(nativeName, StringComparison.Ordinal) &&
                line.Contains(reason, StringComparison.Ordinal));
    }

    [Fact]
    public void A_non_director_virtual_method_marshals_string_arrays_and_ranges()
    {
        using var shape = E2EBindings.CreateTotalLengthNamesShape();

        Assert.Equal(5, shape.SumCounted(["ab", "cde"], 2));
        Assert.Equal(5, shape.SumRange(["ab", "cde"]));
        Assert.Equal(0, shape.SumRange([]));
        Assert.Equal(typeof(ReadOnlySpan<string>), Assert.Single(typeof(NamesShape).GetMethod(nameof(NamesShape.SumRange))!.GetParameters()).ParameterType);
    }

    private sealed class ManagedCountedStringVisitor : CountedStringVisitor
    {
        public IReadOnlyList<string?> AfterNames { get; private set; } = [];

        public uint AfterCount { get; private set; }

        public IReadOnlyList<string?> BeforeNames { get; private set; } = [];

        public int BeforeCount { get; private set; }

        public IReadOnlyList<string?> TaggedNames { get; private set; } = [];

        public int TaggedTag { get; private set; }

        public int TaggedTotal { get; private set; }

        public override int After(ReadOnlySpan<string?> names, uint count)
        {
            AfterNames = names.ToArray();
            AfterCount = count;
            return names.Length;
        }

        public override int Before(int count, ReadOnlySpan<string?> names)
        {
            BeforeNames = names.ToArray();
            BeforeCount = count;
            return names.Length;
        }

        public override int Tagged(ReadOnlySpan<string?> names, int tag, int total)
        {
            TaggedNames = names.ToArray();
            TaggedTag = tag;
            TaggedTotal = total;
            return names.Length;
        }
    }

    private sealed class ManagedStringRangeVisitor : StringRangeVisitor
    {
        public IReadOnlyList<string?> Seen { get; private set; } = [];

        public override int Visit(ReadOnlySpan<string?> begin)
        {
            Seen = begin.ToArray();
            return begin.Length;
        }
    }
}
