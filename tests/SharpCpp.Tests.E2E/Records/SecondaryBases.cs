using Secondary_bases;

using Xunit;

namespace SharpCpp.Tests.E2E.Records;

public sealed class SecondaryBaseTests
{
    [Fact]
    public void Private_override_of_a_secondary_pure_virtual_leaves_the_class_concrete()
    {
        Assert.False(typeof(HiddenRunner).IsAbstract);
        using var runner = new HiddenRunner();
        Assert.Equal(1, runner.PrimaryValue());
        Assert.Equal(5, runner.Run());
        Assert.True(typeof(IRunnable).IsAssignableFrom(typeof(HiddenRunner)));
    }
}
