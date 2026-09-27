using SharpCpp.Tests.E2E.Modules.OwnedFiles.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Modules.OwnedFiles;

public sealed class OwnedRootTests
{
    [Fact]
    public void Owned_header_roots_generate_compiled_native_round_trips()
    {
        Assert.Equal(42, OwnedBindings.OwnedRootFirst(41));
        Assert.Equal(43, OwnedBindings.OwnedRootSecond(41));
    }
}
