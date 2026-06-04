using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Records;

public sealed class GenericTests
{
    [Fact]
    public void Free_functions_can_return_concrete_generic_wrappers()
    {
        var intBox = E2EBindings.CreateIntBox(10);
        var floatBox = E2EBindings.CreateFloatBox(1.5f);

        Assert.Equal(10, intBox.GetValue());
        Assert.Equal(1.5f, floatBox.GetValue());
    }

    [Fact]
    public void Generic_record_constructor_and_instance_methods_dispatch_to_supported_instantiations()
    {
        var box = new BoxOf<int>(12);

        Assert.Equal(12, box.GetValue());

        box.SetValue(27);

        Assert.Equal(27, box.GetValue());
    }

    [Fact]
    public void Generic_record_static_methods_dispatch_to_supported_instantiations()
    {
        var box = BoxOf<float>.Create(2.5f);

        Assert.Equal(2.5f, box.GetValue());
    }

    [Fact]
    public void Unsupported_generic_instantiation_throws_at_runtime()
    {
        var exception = Assert.Throws<NotSupportedException>(() => BoxOf<long>.Create(1L));

        Assert.Contains("generic argument combination", exception.Message, StringComparison.Ordinal);
    }
}
