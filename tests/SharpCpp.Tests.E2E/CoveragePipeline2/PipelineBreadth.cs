using Pipeline_breadth;
using SharpCpp.Tests.E2E.Generated;
using Xunit;

namespace SharpCpp.Tests.E2E.CoveragePipeline2;

public sealed class PipelineBreadthTests
{
    [Fact]
    public void Dense_pipeline_surface_keeps_generated_native_round_trips()
    {
        Assert.Equal(42, E2EBindings.PipelineRoundTrip(41));

        using var rebased = E2EBindings.CreatePipelineRebased(17);
        Assert.Equal(19, rebased.GetTotal());

        using var fields = E2EBindings.CreatePipelineFieldHost(20);
        Assert.Equal(86, fields.Read());

        using var openDerived = E2EBindings.CreatePipelineOpenDerived(38);
        Assert.Equal(42, openDerived.GetRoundTrip());

        using var orphan = new PipelineOrphanBase(31);
        Assert.Equal(31, orphan.GetValue());

        using var accessor = new PipelineAccessorProbe(7);
        accessor.SetPipelineMismatch(43.75f);
        Assert.Equal(43, accessor.GetPipelineMismatch());
        accessor.SetCollision(47);
        Assert.Equal(47, accessor.GetCollision());
        Assert.Equal(47, accessor.Collision());
    }
}
