using SharpCpp.Tests.E2E.Coverage.Consumer;
using SharpCpp.Tests.E2E.Coverage.Dependency;
using Xunit;

namespace SharpCpp.Tests.E2E.Coverage;

public sealed class MetadataDependencyTests
{
    [Fact]
    public void Exported_dependency_metadata_drives_a_compiled_consumer_round_trip()
    {
        using var value = DependencyBindings.CreateDependencyValue(42, DependencyState.Ready);

        Assert.Equal(42, value.Value);
        Assert.Equal(DependencyState.Ready, value.State);
        Assert.Equal(42, ConsumerBindings.ConsumeDependencyValue(value));

        value.Value = 73;
        value.State = DependencyState.Idle;
        Assert.Equal(73, ConsumerBindings.ConsumeDependencyValue(value));
        Assert.Equal(DependencyState.Idle, value.State);
    }
}
