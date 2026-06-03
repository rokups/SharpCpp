using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Constructor;

public sealed class ConstructorTests
{
    [Fact]
    public void Public_constructor_creates_wrapper_and_initializes_state()
    {
        var box = new ConstructorBox(10);

        Assert.False(box.IsNull);
        Assert.Equal(10, box.GetValue());
    }
}
