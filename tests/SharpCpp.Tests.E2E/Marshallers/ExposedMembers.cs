using System.Runtime.InteropServices;

using Exposed_members;

using SharpCpp.Support;
using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Marshallers;

public abstract class ExposedNativeObject : NativeObject
{
    protected ExposedNativeObject(nint handle, bool ownsHandle)
        : base(handle, ownsHandle)
    {
    }
}

public sealed class ExposedMemberTests
{
    [Fact]
    public void Exposed_void_pointer_member_returns_a_raw_pointer()
    {
        using var listener = E2EBindings.CreateListener(21);
        var handle = listener.GetHandle();
        Assert.Equal(21, Marshal.ReadInt32(handle));
    }

    [Fact]
    public void Configured_record_keeps_only_its_declared_constructor()
    {
        var constructor = Assert.Single(typeof(Constructed).GetConstructors());
        Assert.Equal([typeof(int)], constructor.GetParameters().Select(static parameter => parameter.ParameterType));
        using var value = new Constructed(4);
        Assert.Equal(8, value.Twice());
    }
}
