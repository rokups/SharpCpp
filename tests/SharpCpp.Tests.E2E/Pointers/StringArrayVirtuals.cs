using System;
using System.Collections.Generic;
using System.Linq;

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
    public void A_non_director_virtual_method_marshals_string_arrays_and_ranges()
    {
        using var shape = E2EBindings.CreateTotalLengthNamesShape();

        Assert.Equal(5, shape.SumCounted(["ab", "cde"], 2));
        Assert.Equal(5, shape.SumRange(["ab", "cde"]));
        Assert.Equal(0, shape.SumRange([]));
        Assert.Equal(typeof(ReadOnlySpan<string>), Assert.Single(typeof(NamesShape).GetMethod(nameof(NamesShape.SumRange))!.GetParameters()).ParameterType);
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
