using Cross_header;
using SharpCpp.Tests.E2E.Generated;
using Xunit;

namespace SharpCpp.Tests.E2E.CoverageMatrix;

public sealed class CrossHeaderTests
{
    [Fact]
    public void Attributed_forward_declaration_merges_with_native_definition()
    {
        using var value = E2EBindings.MakeCrossHeaderPayload(47);

        Assert.IsType<CrossHeaderValue>(value);
        Assert.Equal(47, value.GetValue());
        Assert.Equal(47, E2EBindings.ReadCrossHeaderPayload(value));
    }
}
