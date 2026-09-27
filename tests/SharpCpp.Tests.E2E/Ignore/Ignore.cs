using System.Reflection;

using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Ignore;

public sealed class IgnoreTests
{
    private static readonly Assembly Bindings = typeof(E2EBindings).Assembly;

    [Fact]
    public void Ignored_declarations_share_a_header_with_a_compiled_native_round_trip()
    {
        Assert.Equal(42, E2EBindings.IgnoreSurfaceRoundTrip(41));
    }

    [Fact]
    public void Ignored_records_enums_and_functions_are_not_bound()
    {
        Assert.Null(Bindings.GetType("SharpCpp.Tests.E2E.Generated.SkippedRecord"));
        Assert.Null(Bindings.GetType("SharpCpp.Tests.E2E.Generated.SkippedRecordWithReason"));
        Assert.Null(Bindings.GetType("SharpCpp.Tests.E2E.Generated.SkippedEnum"));
        Assert.Null(Bindings.GetType("SharpCpp.Tests.E2E.Generated.MacroGeneratedMacroRecord"));
        Assert.Null(Bindings.GetType("SharpCpp.Tests.E2E.Generated.MacroGeneratedMacroEnum"));
        Assert.Null(typeof(E2EBindings).GetMethod("SkippedFreeFunction"));
        Assert.Null(typeof(E2EBindings).GetMethod("PrescanIgnoredFunction"));
    }

    [Fact]
    public void Ignored_members_are_not_bound_while_their_record_is()
    {
        var type = typeof(PartiallyIgnoredRecord);
        Assert.NotNull(type.GetProperty(nameof(PartiallyIgnoredRecord.VisibleField)));
        Assert.Null(type.GetProperty("SkippedField"));
        Assert.Null(type.GetMethod("InternalMethod"));
        Assert.Null(type.GetMethod("SkippedStatic"));
        Assert.Null(type.GetMethod("op_Addition"));
        Assert.Equal([], Assert.Single(type.GetConstructors()).GetParameters());
    }

    [Fact]
    public void Deleted_members_are_not_bound()
    {
        using var box = new DeletedBox();
        Assert.Equal(3, box.Value());
        Assert.Equal([], Assert.Single(typeof(DeletedBox).GetConstructors()).GetParameters());
        Assert.Null(typeof(DeletedBox).GetMethod("DeletedMethod"));
    }
}
