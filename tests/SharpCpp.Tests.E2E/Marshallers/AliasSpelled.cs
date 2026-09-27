using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

using Alias_spelled;

using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Marshallers;

public readonly record struct AliasValue3(float X, float Y, float Z);

/// <summary>Converts through a distinct unmanaged layout, so the configured marshaller must run.</summary>
[CustomMarshaller(typeof(AliasValue3), MarshalMode.Default, typeof(AliasValue3Marshaller))]
public static class AliasValue3Marshaller
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Native
    {
        public float X;
        public float Y;
        public float Z;
    }

    public static Native ConvertToUnmanaged(AliasValue3 managed) => new() { X = managed.X, Y = managed.Y, Z = managed.Z };

    public static AliasValue3 ConvertToManaged(Native unmanaged) => new(unmanaged.X, unmanaged.Y, unmanaged.Z);
}

public sealed class AliasSpelledMarshallerTests
{
    [Fact]
    public void Marshallers_spelled_through_namespace_and_type_aliases_apply()
    {
        Assert.Equal(5, E2EBindings.MeasureView("hello"u8));
        Assert.True(E2EBindings.GetView().SequenceEqual("alias view"u8));
    }

    [Fact]
    public void Undefaultable_view_parameters_use_the_native_default()
    {
        Assert.Equal(107, E2EBindings.MeasureDefaultView());
        Assert.Equal(102, E2EBindings.MeasureDefaultView("ab"u8));
    }

    [Fact]
    public void Const_value_aliases_keep_the_configured_managed_marshaller()
    {
        Assert.Equal(321f, E2EBindings.ConsumeValue3(new AliasValue3(1, 2, 3)));

        using var box = new Value3Box();
        box.Value = new AliasValue3(4, 5, 6);
        Assert.Equal(new AliasValue3(4, 5, 6), box.Value);
    }
}
