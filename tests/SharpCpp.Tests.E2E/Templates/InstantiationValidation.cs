using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Templates;

public sealed class ValidationBreadthTests
{
    [Fact]
    public void Dense_validation_surface_keeps_supported_native_round_trip()
    {
        Assert.Equal(42, E2EBindings.ValidationBreadthRoundTrip(39));
    }
}
