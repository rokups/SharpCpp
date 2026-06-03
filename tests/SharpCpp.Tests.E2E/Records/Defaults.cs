using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Records;

public sealed class DefaultsTests
{
    [Fact]
    public void Free_function_uses_builtin_defaults()
    {
        Assert.Equal(221, E2EBindings.DefaultScore(2));
        Assert.Equal(241, E2EBindings.DefaultScore(2, 4));
        Assert.Equal(242, E2EBindings.DefaultScore(2, 4, false));
    }

    [Fact]
    public void Constructor_uses_builtin_and_enum_defaults()
    {
        var box = new DefaultBox(3);

        Assert.Equal(6, box.GetValue());
        Assert.Equal(DefaultMode.Active, box.GetMode());
    }

    [Fact]
    public void Static_method_uses_builtin_defaults()
    {
        Assert.Equal(42, DefaultBox.Add(19));
        Assert.Equal(24, DefaultBox.Add(1));
    }

    [Fact]
    public void Instance_method_uses_builtin_and_enum_defaults()
    {
        var box = new DefaultBox(3);

        Assert.Equal(146, box.Combine(10));
        Assert.Equal(227, box.Combine(1, 2, DefaultMode.Idle));
    }

    [Fact]
    public void Static_factory_uses_builtin_and_enum_defaults()
    {
        var box = DefaultBox.Create(4);

        Assert.Equal(8, box.GetValue());
        Assert.Equal(DefaultMode.Active, box.GetMode());
    }
}
