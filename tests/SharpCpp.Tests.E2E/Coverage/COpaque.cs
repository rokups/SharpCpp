using SharpCpp.Tests.E2E.Coverage.COpaque.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Coverage;

public sealed class COpaqueTests
{
    [Fact]
    public void C_opaque_handles_typedefs_and_unions_round_trip_through_native_code()
    {
        using var handle = COpaqueBindings.CoverageCGetHandle();
        Assert.Equal(41, COpaqueBindings.CoverageCUseHandle(handle));

        var context = COpaqueBindings.CoverageCGetContext();
        Assert.NotEqual(0, context);
        Assert.Equal(42, COpaqueBindings.CoverageCUseContext(context));

        using var @event = COpaqueBindings.CoverageCMakeEvent(19, 23);
        Assert.Equal(42, COpaqueBindings.CoverageCSumEvent(@event));

        using var arguments = COpaqueBindings.CoverageCMakeArguments(43);
        Assert.Equal(43, COpaqueBindings.CoverageCReadArguments(arguments));
        Assert.Equal(43, arguments.Ray.Value);
    }
}
