using Intrusive_references;

using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Lifetime;

public sealed class IntrusiveReferenceTests
{
    [Fact]
    public void Inherited_intrusive_targets_are_owned_by_returns_and_field_reads()
    {
        var destroyed = RefTargetBase.Destroyed();
        using (var target = E2EBindings.CreateRefTarget())
        {
            Assert.True(target.OwnsHandle);
            Assert.Equal(1, target.Refs());
            Assert.Equal(7, target.Value());

            // A read returns the live wrapper; the reference the read took is released.
            using var holder = new RefHolder { Target = target };
            Assert.Same(target, holder.Target);
            Assert.Equal(1, target.Refs());
        }

        Assert.Equal(destroyed + 1, RefTargetBase.Destroyed());
    }
}
