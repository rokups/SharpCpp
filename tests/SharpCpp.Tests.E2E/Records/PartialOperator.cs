using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Records;

public sealed class PartialOperatorTests
{
    [Fact]
    public void Partial_operator_named_methods_dispatch_through_native_code()
    {
        using var left = new PartialOperatorBox(10);
        using var right = new PartialOperatorBox(7);
        using var same = new PartialOperatorBox(10);

        Assert.True(left.Equals(same));
        Assert.False(left.Equals(right));
        Assert.True(right.LessThan(left));
        Assert.False(left.LessThan(right));
    }
}
