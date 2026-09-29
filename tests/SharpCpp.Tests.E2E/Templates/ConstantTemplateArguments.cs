using Constant_template_arguments;
using Step = Constant_template_arguments.UpdateContext.Step;

using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Templates;

public sealed class ConstantTemplateArgumentTests
{
    [Fact]
    public void Record_elements_of_containers_with_constant_expression_capacities_are_values()
    {
        using var step = new Step { Id = 3 };
        step.Fill(10);

        Assert.Equal([10, 11], step.StepCells.Select(static cell => cell.Value));
        Assert.Equal([12, 13], step.WideCells.Select(static cell => cell.Value));
        Assert.Equal([14, 15], step.CellList.Select(static cell => cell.Value));
        Assert.Equal(75, step.Sum());

        // Elements cross by value: an element read is a copy, an indexed write replaces it.
        using var replacement = new UpdateContext.Cell { Value = 21 };
        step.StepCells[1] = replacement;
        replacement.Value = 30;
        step.StepCells[0].Value = 40;
        Assert.Equal([10, 21], step.StepCells.Select(static cell => cell.Value));
        Assert.Equal(85, step.Sum());
    }

    [Fact]
    public void Containers_of_non_copyable_elements_with_constant_expression_capacities_are_not_bound()
    {
        Assert.Null(typeof(Step).GetProperty("StepQueues"));
        Assert.NotNull(typeof(Step).GetProperty("StepCells"));
        Assert.False(typeof(System.ICloneable).IsAssignableFrom(typeof(Step)));

        var log = Diagnostics.GeneratedArtifacts.ReadText("generator.log").Split('\n');
        Assert.Contains(
            log,
            static line => line.StartsWith("IGNORED\tField\t", StringComparison.Ordinal) &&
                line.Contains("\tconstant_template_arguments::update_context::step::step_queues\t", StringComparison.Ordinal) &&
                line.Contains("'constant_template_arguments::update_context::queue' is not copyable", StringComparison.Ordinal));
    }

    [Fact]
    public void Namespace_and_template_aliases_with_constant_expression_capacities_hold_values()
    {
        using var holder = new PlainHolder();
        using var first = new Plain { Value = 1 };
        using var second = new Plain { Value = 2 };
        holder.Aliased.Add(first);
        holder.Templated.Add(second);
        first.Value = 5;

        Assert.Equal(1, holder.Aliased[0].Value);
        Assert.Equal(21, holder.Sum());
    }

    [Fact]
    public void Fixed_arrays_of_enum_elements_with_constant_lengths_round_trip()
    {
        using var order = E2EBindings.DefaultChannelOrder();
        Assert.Equal([Channel.Alpha, Channel.Blue, Channel.Green, Channel.Red], order);
        Assert.Equal(8u + 8u + 6u + 4u, E2EBindings.WeighChannelOrder(order));

        order[0] = Channel.Red;
        Assert.Equal(Channel.Red, order[0]);
        Assert.Equal(1u + 8u + 6u + 4u, E2EBindings.WeighChannelOrder(order));
    }

    [Fact]
    public void Fixed_array_parameters_with_constant_lengths_keep_native_defaults()
    {
        Assert.Equal(15, E2EBindings.PeekMagic(10));

        using var magic = new global::Std.Array<byte>(new byte[] { 5, 0, 0, 6 });
        Assert.Equal(21, E2EBindings.PeekMagic(10, magic));
    }
}
