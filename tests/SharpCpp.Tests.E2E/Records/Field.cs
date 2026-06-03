using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Records;

public sealed class FieldTests
{
    [Fact]
    public void Public_fields_are_exposed_as_properties()
    {
        var payload = E2EBindings.CreateFieldPayload(10);
        var replacement = E2EBindings.CreateFieldPayload(30);
        var box = E2EBindings.CreateFieldBox(2, true, FieldMode.Active, payload);

        Assert.Equal(2, box.Count);
        Assert.True(box.Enabled);
        Assert.Equal(FieldMode.Active, box.Mode);
        Assert.Equal(10, box.Payload.Value);

        box.Count = 7;
        box.Enabled = false;
        box.Mode = FieldMode.Suspended;
        box.Payload = replacement;

        Assert.Equal(7, box.Count);
        Assert.False(box.Enabled);
        Assert.Equal(FieldMode.Suspended, box.Mode);
        Assert.Equal(30, box.Payload.Value);
    }
}
