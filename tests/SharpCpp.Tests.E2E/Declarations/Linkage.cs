using SharpCpp.Tests.E2E.Generated;
using Xunit;

namespace SharpCpp.Tests.E2E.Declarations;

public sealed class LinkageTests
{
    [Fact]
    public void Nested_language_linkage_declarations_round_trip_through_native_code()
    {
        using var record = new LinkageRecord(17);
        Assert.Equal(17, record.GetValue());
        Assert.Equal(9, E2EBindings.LinkageApply(LinkageMode.Add, 4, 5));
        Assert.Equal(20, E2EBindings.LinkageApply(LinkageMode.Multiply, 4, 5));

        using var nested = new global::Linkage_nested.NestedRecord(11);
        Assert.Equal(22, nested.Doubled());
        Assert.Equal(8, E2EBindings.NestedAdd(5));

        using var box = new LinkageBox<int>(23);
        Assert.Equal(23, box.GetValue());
        Assert.Equal(29, E2EBindings.LinkageIdentity(29));
    }
}
