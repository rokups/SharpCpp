using SharpCpp.Support;
using SharpCpp.Tests.E2E.Generated;

using Smart_pointers;

using Xunit;

namespace SharpCpp.Tests.E2E.Lifetime;

/// <summary>Managed base configured for the reference-counted hierarchy; releases one reference.</summary>
public abstract class RefCountedNativeObject : NativeObject
{
    protected RefCountedNativeObject(nint handle, bool ownsHandle)
        : base(handle, ownsHandle)
    {
    }

    protected override bool ReleaseNativeHandle(nint handle)
    {
        E2EBindings.ReleaseRefCounted(handle);
        return true;
    }
}

public sealed class SmartPointerTests
{
    [Fact]
    public void Shared_and_weak_pointer_returns_hand_the_wrapper_a_reference()
    {
        var refs = E2EBindings.PayloadRefs();
        using (var shared = E2EBindings.GetPayloadShared())
        {
            Assert.True(shared.OwnsNativeHandle);
            Assert.Equal(11, shared.Value());
            Assert.Equal(refs + 1, E2EBindings.PayloadRefs());
        }

        Assert.Equal(refs, E2EBindings.PayloadRefs());

        using (var weak = E2EBindings.GetPayloadWeak())
        {
            Assert.Equal(11, weak.Value());
        }

        Assert.Equal(refs, E2EBindings.PayloadRefs());
    }

    [Fact]
    public void Unique_pointer_return_transfers_the_object_to_the_wrapper()
    {
        var destroyed = RefCounted.Destroyed();
        using (var unique = E2EBindings.GetPayloadUnique(4))
        {
            Assert.Equal(4, unique.Value());
            Assert.Equal(1, unique.Refs());
        }

        Assert.Equal(destroyed + 1, RefCounted.Destroyed());
    }

    [Fact]
    public void Shared_pointer_parameters_accept_the_wrapper()
    {
        using var shared = E2EBindings.GetPayloadShared();
        var refs = E2EBindings.PayloadRefs();
        Assert.Equal(110 + refs + 1, E2EBindings.UsePayloadShared(shared));
        Assert.IsAssignableFrom<RefCountedNativeObject>(shared);
    }
}
