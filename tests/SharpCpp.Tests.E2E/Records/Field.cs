using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Records;

public sealed class FieldTests
{
    [Fact]
    public void Public_fields_are_exposed_as_properties()
    {
        var box = E2EBindings.CreateFieldBox(2, true, FieldMode.Active);

        Assert.Equal(2, box.Count);
        Assert.True(box.Enabled);
        Assert.Equal(FieldMode.Active, box.Mode);

        box.Count = 7;
        box.Enabled = false;
        box.Mode = FieldMode.Suspended;

        Assert.Equal(7, box.Count);
        Assert.False(box.Enabled);
        Assert.Equal(FieldMode.Suspended, box.Mode);
    }
}
