using Non_copyable_elements;

using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Degradation;

public sealed class NonCopyableElementTests
{
    [Fact]
    public void Container_fields_of_non_copyable_elements_are_not_bound_and_the_record_is()
    {
        using var owner = new CounterOwner { Total = 4 };
        Assert.Equal(8, owner.DoubledTotal());
        Assert.Null(typeof(CounterOwner).GetProperty("Counters"));
        Assert.Null(typeof(CounterOwner).GetProperty("FixedCounters"));
        Assert.NotNull(typeof(CounterOwner).GetProperty("Numbers"));
        Assert.False(typeof(System.ICloneable).IsAssignableFrom(typeof(CounterOwner)));

        var log = Diagnostics.GeneratedArtifacts.ReadText("generator.log").Split('\n');
        foreach (var field in new[] { "counters", "fixed_counters" })
        {
            Assert.Contains(
                log,
                line => line.StartsWith("IGNORED\tField\t", StringComparison.Ordinal) &&
                    line.Contains($"\tnon_copyable_elements::counter_owner::{field}\t", StringComparison.Ordinal) &&
                    line.Contains("'non_copyable_elements::atomic_counter' is not copyable", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Records_embedding_a_non_copyable_record_are_not_copyable()
    {
        using var holder = new CounterHolder { Id = 2 };
        Assert.Equal(2, holder.Id);
        Assert.False(typeof(System.ICloneable).IsAssignableFrom(typeof(CounterHolder)));
        Assert.Null(typeof(E2EBindings).GetMethod("CountCounterOwners"));
    }
}
