using Non_assignable_elements;

using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Degradation;

public sealed class NonAssignableElementTests
{
    [Fact]
    public void Containers_of_non_assignable_elements_or_non_default_constructible_allocators_are_not_bound()
    {
        using var context = new StepContext { Total = 4 };
        Assert.Equal(8, context.DoubledTotal());
        Assert.Equal(0, context.CountItems());

        var type = typeof(StepContext);
        foreach (var name in new[] { "Steps", "ArenaValues", "PlainSteps" })
        {
            Assert.Null(type.GetProperty(name));
        }

        foreach (var name in new[] { "GetPlainSteps", "SetPlainSteps", "MakeSteps", "TakeSteps", "MakeArenaNumbers", "TakeArenaNumbers" })
        {
            Assert.Null(type.GetMethod(name));
        }

        Assert.Null(typeof(E2EBindings).GetMethod("CountSteps"));
    }

    [Fact]
    public void Containers_of_assignable_elements_with_default_allocators_stay_bound()
    {
        Assert.NotNull(typeof(StepContext).GetProperty("Items"));
        Assert.NotNull(typeof(StepContext).GetProperty("Numbers"));
        Assert.NotNull(typeof(E2EBindings).GetMethod("CountItems"));
    }

    [Fact]
    public void Degraded_surfaces_are_logged_with_the_missing_operation()
    {
        var log = Diagnostics.GeneratedArtifacts.ReadText("generator.log").Split('\n');
        Assert.Contains(
            log,
            line => line.StartsWith("IGNORED\tField\t", StringComparison.Ordinal) &&
                line.Contains("\tnon_assignable_elements::step_context::plain_steps\t", StringComparison.Ordinal) &&
                line.Contains("'non_assignable_elements::step' is not copy-assignable", StringComparison.Ordinal));
        Assert.Contains(
            log,
            line => line.StartsWith("IGNORED\tField\t", StringComparison.Ordinal) &&
                line.Contains("\tnon_assignable_elements::step_context::arena_values\t", StringComparison.Ordinal) &&
                line.Contains("is not default-constructible", StringComparison.Ordinal));
        Assert.Contains(
            log,
            line => line.StartsWith("IGNORED\tFunction\t", StringComparison.Ordinal) &&
                line.Contains("count_steps", StringComparison.Ordinal) &&
                line.Contains("is not copy-assignable", StringComparison.Ordinal));
    }
}
