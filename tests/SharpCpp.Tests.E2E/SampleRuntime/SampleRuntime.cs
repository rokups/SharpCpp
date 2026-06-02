using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.SampleRuntime;

public sealed class SampleRuntimeTests
{
    [Fact]
    public void Primitive_arguments_roundtrip_through_generated_bindings()
    {
        Assert.Equal(42, E2EBindings.AddNumbers(19, 23));
    }

    [Fact]
    public void Simple_handle_return_creates_non_null_wrapper()
    {
        var context = E2EBindings.CreateContext();

        Assert.NotNull(context);
        Assert.False(context.IsNull);
        Assert.NotEqual((nint)0, context.Handle);
    }
}
