using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Pointers;

public sealed class BytePointerTests
{
    [Fact]
    public void Byte_buffers_are_raw_input_pointers_in_every_cv_form()
    {
        var unsignedBuffer = E2EBindings.UnsignedStorage();
        var signedBuffer = E2EBindings.SignedStorage();
        Assert.Equal(200, E2EBindings.ConsumeUnsignedBuffer(unsignedBuffer));
        Assert.Equal(200, E2EBindings.ConsumeVolatileUnsignedBuffer(unsignedBuffer));
        Assert.Equal(200, E2EBindings.ConsumeConstVolatileUnsignedBuffer(unsignedBuffer));
        Assert.Equal(-5, E2EBindings.ConsumeSignedBuffer(signedBuffer));
        Assert.Equal(-5, E2EBindings.ConsumeVolatileSignedBuffer(signedBuffer));
        Assert.Equal(-5, E2EBindings.ConsumeConstVolatileSignedBuffer(signedBuffer));
    }

    [Fact]
    public void Explicit_out_byte_pointers_write_through()
    {
        E2EBindings.WriteUnsignedPointer(out var unsignedValue);
        E2EBindings.WriteSignedPointer(out var signedValue);
        Assert.Equal((byte)42, unsignedValue);
        Assert.Equal((sbyte)-42, signedValue);
    }
}
