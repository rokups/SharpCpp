using Urho3D;
using Urho3D.Graphics;

using Xunit;

namespace SharpCpp.Rbfx.Tests.E2E;

public sealed class BiasParametersTests
{
    [Fact]
    public void Generated_bias_parameters_wrapper_can_construct_set_properties_and_call_method()
    {
        using var parameters = new BiasParameters();

        parameters.ConstantBias = 0.5f;
        parameters.SlopeScaledBias = 1.5f;
        parameters.NormalOffset = 0.25f;
        parameters.Validate();

        Assert.Equal(0.5f, parameters.ConstantBias);
        Assert.Equal(1.5f, parameters.SlopeScaledBias);
        Assert.Equal(0.25f, parameters.NormalOffset);
    }
}
