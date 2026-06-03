using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Field;

public sealed class FieldTests
{
    [Fact]
    public void Public_fields_are_exposed_as_properties()
    {
        var box = E2EBindings.CreateFieldBox(2, true);

        Assert.Equal(2, box.Count);
        Assert.True(box.Enabled);

        box.Count = 7;
        box.Enabled = false;

        Assert.Equal(7, box.Count);
        Assert.False(box.Enabled);
    }
}
