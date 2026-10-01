using SharpCpp.Tests.E2E.Generated;
using Xunit;

namespace SharpCpp.Tests.E2E.Diagnostics;

public sealed class SilentDropsTests
{
    [Fact]
    public void Field_clashing_with_an_ignored_method_is_rendered_as_a_property()
    {
        using var record = new SilentDropFieldClash();

        Assert.Equal(3, record.Count);
        record.Count = 7;
        Assert.Equal(7, record.Count);
    }
}
