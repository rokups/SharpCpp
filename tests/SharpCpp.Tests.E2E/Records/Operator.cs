using E2e.Records;

using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Records;

public sealed class OperatorTests
{
    [Fact]
    public void Managed_surface_exposes_member_operator_wrappers()
    {
        using var value = new OperatorBox(10);
        var added = value + 5;
        var negated = -value;

        Assert.Equal(15, added);
        Assert.Equal(-10, negated);
    }

    [Fact]
    public void Managed_surface_exposes_conversion_operator_wrappers()
    {
        using var value = new OperatorBox(10);
        using var zero = new OperatorBox(0);

        Assert.Equal(10, (int)value);
        Assert.True((bool)value);
        Assert.False((bool)zero);

        using var converted = (OperatorValue)value;
        Assert.Equal(10, converted.Value);
    }

    [Fact]
    public void Managed_surface_exposes_comparison_operator_wrappers()
    {
        using var left = new OperatorBox(10);
        using var right = new OperatorBox(7);
        using var same = new OperatorBox(10);

        Assert.True(left == same);
        Assert.True(left != right);
        Assert.False(left == right);
        Assert.True(right < left);
        Assert.True(left > right);
    }

    [Fact]
    public void Unary_and_handle_returning_operators_round_trip_through_native_code()
    {
        using var truth = new OperatorTruth(true);
        using var falsity = new OperatorTruth(false);
        Assert.False(!truth);
        Assert.True(!falsity);

        using var value = new PointerOperatorBox(10);
        using var added = value + 7;
        Assert.Equal(17, added.GetValue());

        using var negated = -value;
        Assert.Equal(-10, negated.GetValue());
    }
}
