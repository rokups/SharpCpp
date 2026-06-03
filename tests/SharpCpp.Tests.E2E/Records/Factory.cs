using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Records;

public sealed class FactoryTests
{
    [Fact]
    public void Static_factory_method_returns_wrapper()
    {
        var box = FactoryBox.Create(10);

        Assert.False(box.IsNull);
        Assert.Equal(10, box.GetValue());
    }

    [Fact]
    public void Static_primitive_method_returns_value()
    {
        Assert.Equal(42, FactoryBox.Add(19, 23));
    }
}
