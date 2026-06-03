using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Records;

public sealed class OperatorTests
{
    [Fact]
    public void Managed_surface_exposes_member_operator_wrappers()
    {
        var value = new OperatorBox(10);
        var added = value + 5;
        var negated = -value;

        Assert.Equal(15, added);
        Assert.Equal(-10, negated);
    }

    [Fact]
    public void Managed_surface_exposes_conversion_operator_wrappers()
    {
        var value = new OperatorBox(10);
        var zero = new OperatorBox(0);

        Assert.Equal(10, (int)value);
        Assert.True((bool)value);
        Assert.False((bool)zero);
    }

    [Fact]
    public void Managed_surface_exposes_comparison_operator_wrappers()
    {
        var left = new OperatorBox(10);
        var right = new OperatorBox(7);
        var same = new OperatorBox(10);

        Assert.True(left == same);
        Assert.True(left != right);
        Assert.False(left == right);
        Assert.True(right < left);
        Assert.True(left > right);
    }
}
