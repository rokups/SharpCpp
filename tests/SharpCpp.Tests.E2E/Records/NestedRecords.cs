using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Records;

public sealed class NestedRecordsTests
{
    [Fact]
    public void Record_nested_two_records_deep_is_a_nested_wrapper_class()
    {
        using var outer = E2EBindings.CreateNestedOuter();
        Assert.Equal(100, outer.OuterId());

        using var middle = E2EBindings.CreateNestedMiddle();
        Assert.Equal(200, middle.MiddleId());

        using var inner = E2EBindings.CreateNestedInner(21);
        Assert.Equal(21, inner.Value);
        Assert.Equal(42, inner.Doubled());

        Assert.Equal(
            typeof(NestedOuter.NestedMiddle),
            typeof(NestedOuter.NestedMiddle.NestedInner).DeclaringType);
        Assert.Equal(
            typeof(NestedOuter),
            typeof(NestedOuter.NestedMiddle).DeclaringType);
        Assert.EndsWith(
            ".NestedOuter+NestedMiddle+NestedInner",
            typeof(NestedOuter.NestedMiddle.NestedInner).FullName,
            System.StringComparison.Ordinal);
    }

    [Fact]
    public void Record_nested_two_records_deep_is_used_as_a_parameter_and_return_type_from_outside()
    {
        using var byPointer = E2EBindings.CreateNestedInner(11);
        Assert.Equal(22, E2EBindings.ReadNestedInner(byPointer));
        Assert.Equal(22, E2EBindings.ReadNestedInnerCopy(byPointer));

        using var byValue = E2EBindings.MakeNestedInner(15);
        Assert.Equal(15, byValue.Value);
        Assert.Equal(30, byValue.Doubled());
    }

    [Fact]
    public void Record_nested_three_records_deep_is_a_nested_wrapper_class()
    {
        using var deepest = E2EBindings.CreateNestedDeepest(7);
        Assert.Equal(7, deepest.Value);
        Assert.Equal(7, E2EBindings.ReadNestedDeepest(deepest));

        Assert.Equal(
            typeof(NestedOuter.NestedMiddle.NestedInner),
            typeof(NestedOuter.NestedMiddle.NestedInner.NestedDeepest).DeclaringType);
        Assert.EndsWith(
            ".NestedOuter+NestedMiddle+NestedInner+NestedDeepest",
            typeof(NestedOuter.NestedMiddle.NestedInner.NestedDeepest).FullName,
            System.StringComparison.Ordinal);
    }
}
