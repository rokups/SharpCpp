using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.RecordMethod;

public sealed class RecordMethodTests
{
    [Fact]
    public void CreateCounterBox_returns_non_null_wrapper()
    {
        var box = E2EBindings.CreateCounterBox(10);

        Assert.NotNull(box);
        Assert.False(box.IsNull);
    }

    [Fact]
    public void Instance_method_can_read_state()
    {
        var box = E2EBindings.CreateCounterBox(10);

        Assert.Equal(10, box.GetValue());
    }

    [Fact]
    public void Instance_method_can_mutate_state()
    {
        var box = E2EBindings.CreateCounterBox(10);

        box.SetValue(20);

        Assert.Equal(20, box.GetValue());
    }

    [Fact]
    public void Instance_method_can_return_updated_value()
    {
        var box = E2EBindings.CreateCounterBox(10);

        Assert.Equal(15, box.Add(5));

        Assert.Equal(15, box.GetValue());
    }

    [Fact]
    public void Instance_method_can_return_bool()
    {
        var box = E2EBindings.CreateCounterBox(10);

        Assert.True(box.IsValue(10));
        Assert.False(box.IsValue(11));
    }
}
