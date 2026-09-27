using Dropped_base;

using Xunit;

namespace SharpCpp.Tests.E2E.Records;

public sealed class DroppedBaseTests
{
    [Fact]
    public void Overrides_of_virtuals_behind_a_dropped_base_open_a_new_slot()
    {
        using var input = new JsonInput();
        Assert.Equal(7, input.Read());
        Assert.Equal(5, input.Name());
    }
}
