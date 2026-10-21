using Degradation_rules;

using Pipeline_breadth;

using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Degradation;

// The ea::function forms of the callback degradation tests.
public sealed partial class DegradationRuleTests
{
    private sealed partial class CountingEaCallbackVisitor : EaCallbackVisitor
    {
        public override int Count() => 5;

        public override int VisitValue(global::System.Func<int, int> callback) => callback(4) * 10;
    }

    [Fact]
    public void Director_virtual_with_an_unsupported_ea_callback_shape_is_not_bound()
    {
        Assert.Null(typeof(EaCallbackVisitor).GetMethod("Visit"));
        Assert.NotNull(typeof(EaCallbackVisitor).GetMethod("VisitValue"));

        using var visitor = new CountingEaCallbackVisitor();
        Assert.Equal(5, E2EBindings.EaCallCallbackCount(visitor));
        Assert.Equal(60, E2EBindings.EaCallCallbackVisit(visitor));
    }

    [Fact]
    public void Record_value_ea_callback_returns_are_bound()
    {
        Assert.Equal(9, E2EBindings.EaInvokeResultCallback(static () => new CallbackResult { Value = 9 }));
        Assert.Equal(3, E2EBindings.EaInvokeValueArgumentCallback(static value => value.Value));
    }

    [Fact]
    public void Unsupported_ea_callback_shapes_are_not_bound()
    {
        Assert.Null(typeof(E2EBindings).GetMethod("EaFirstSharedDelegate"));
        Assert.Null(typeof(E2EBindings).GetMethod("EaSecondSharedDelegate"));
        Assert.Null(typeof(E2EBindings).GetMethod("EaRefSharedDelegate"));
        Assert.Null(typeof(E2EBindings).GetMethod("EaValueSharedDelegate"));
    }
}

public sealed class EaPipelineBreadthTests
{
    [Fact]
    public void Ea_callback_field_and_rejected_callback_signatures_are_not_bound()
    {
        using var fields = E2EBindings.CreatePipelineFieldHost(20);
        Assert.Equal(86, fields.Read());
        Assert.Null(typeof(PipelineFieldHost).GetProperty("Callback"));
        Assert.Null(typeof(PipelineFieldHost).GetProperty("EaCallback"));

        foreach (var name in new[]
        {
            "RejectConfiguredCallbackReturn",
            "RejectNestedCallbackReturn",
            "RejectNestedCallbackParameter",
            "RejectEaConfiguredCallbackReturn",
            "RejectEaNestedCallbackReturn",
            "RejectEaNestedCallbackParameter",
            "RejectMixedNestedCallbackReturn",
            "RejectMixedNestedCallbackParameter"
        })
        {
            Assert.Null(typeof(E2EBindings).GetMethod(name));
        }
    }
}
