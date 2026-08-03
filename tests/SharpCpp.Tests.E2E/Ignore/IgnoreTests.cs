using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Ignore;

public sealed class IgnoreTests
{
    [Fact]
    public void Ignored_declarations_share_a_header_with_a_compiled_native_round_trip()
    {
        Assert.Equal(42, E2EBindings.IgnoreSurfaceRoundTrip(41));
    }
}
