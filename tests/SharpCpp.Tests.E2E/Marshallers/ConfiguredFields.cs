using Configured_fields;

using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Marshallers;

public sealed class ConfiguredFieldTests
{
    [Fact]
    public void Configured_value_fields_keep_their_managed_type()
    {
        using var body = new BodySettings { Position = new ConfiguredIndirectPair(3, 4) };
        Assert.Equal(new ConfiguredIndirectPair(3, 4), body.Position);
        Assert.Equal(7, E2EBindings.SumBodyPosition(body));
        Assert.Null(typeof(BodySettings).GetProperty("PositionFirst"));
    }

    [Fact]
    public void Configured_value_inside_flattened_data_keeps_its_managed_type()
    {
        using var material = new Material { DataColor = new ConfiguredIndirectPair(5, 6) };
        Assert.Equal(11, E2EBindings.SumMaterialColor(material));
    }

    [Fact]
    public void Flattened_pointer_fields_do_not_take_the_pointee_value_marshaller()
    {
        using var owner = E2EBindings.CreatePointerOwner();
        Assert.Equal(typeof(nint), typeof(PointerOwner).GetProperty(nameof(PointerOwner.ValuesData))!.PropertyType);
        Assert.NotEqual(0, owner.ValuesData);
        Assert.Equal(9, E2EBindings.ReadPointerOwnerPair(owner));
        Assert.Equal("owner", owner.ValuesName.ToString());
    }
}
