using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Marshallers;

/// Mirrors JPH::Vec3's projection to global::RedBolt.Math.BaseRealVector3&lt;float&gt;: a POD
/// value type whose native layout (16-byte SIMD) genuinely differs from its abi_type layout
/// (12-byte tightly packed), with suppress_emission = true so SharpCpp does not generate it.
/// Layout must match the native abi_type (configured_wide_vector3_abi) exactly.
[StructLayout(LayoutKind.Sequential)]
public struct ConfiguredWideVector3(float x, float y, float z)
{
    public float X = x;
    public float Y = y;
    public float Z = z;
}

public sealed class ConfiguredLayoutMismatchTests
{
    [Fact]
    public void Fixed_array_field_of_layout_mismatched_projected_value_type_reads_and_writes_elements()
    {
        using var triangle = E2EBindings.MakeConfiguredWideTriangle(
            1, 2, 3,
            4, 5, 6,
            7, 8, 9);

        var arr = triangle.Arr;
        Assert.Equal(3, arr.Length);
        Assert.Equal(new ConfiguredWideVector3(1, 2, 3), arr[0]);
        Assert.Equal(new ConfiguredWideVector3(4, 5, 6), arr[1]);
        Assert.Equal(new ConfiguredWideVector3(7, 8, 9), arr[2]);
        Assert.Equal(45.0f, E2EBindings.SumConfiguredWideTriangle(triangle));

        arr[0] = new ConfiguredWideVector3(10, 20, 30);
        Assert.Equal(new ConfiguredWideVector3(10, 20, 30), triangle.Arr[0]);
        Assert.Equal(99.0f, E2EBindings.SumConfiguredWideTriangle(triangle));

        using var replacement = new global::SharpCpp.NativeArray<ConfiguredWideVector3>(
            new[]
            {
                new ConfiguredWideVector3(1, 1, 1),
                new ConfiguredWideVector3(2, 2, 2),
                new ConfiguredWideVector3(3, 3, 3),
            });
        triangle.Arr = replacement;
        Assert.Equal(new ConfiguredWideVector3(1, 1, 1), triangle.Arr[0]);
        Assert.Equal(new ConfiguredWideVector3(2, 2, 2), triangle.Arr[1]);
        Assert.Equal(new ConfiguredWideVector3(3, 3, 3), triangle.Arr[2]);
        Assert.Equal(18.0f, E2EBindings.SumConfiguredWideTriangle(triangle));
    }

    [Fact]
    public void Const_reference_parameter_of_layout_mismatched_projected_value_type_reads_natively()
    {
        var value = E2EBindings.MakeConfiguredWideVector3(2, 3, 4);

        // Exercises ProjectedValueConstReferenceMarshallerBase<configured_wide_vector3>.
        Assert.Equal(9.0f, E2EBindings.SumConfiguredWideVector3Ref(in value));
    }

    [Fact]
    public void Const_pointer_parameter_of_layout_mismatched_projected_value_type_reads_natively()
    {
        var value = E2EBindings.MakeConfiguredWideVector3(5, 6, 7);

        // Exercises ProjectedValueConstPointerMarshallerBase<configured_wide_vector3>'s non-null
        // path: native reads value.v[0..2] through the (correctly laid out) abi pointer.
        Assert.Equal(18.0f, E2EBindings.SumConfiguredWideVector3Ptr(in value));

        // A null pointer: Marshaller<const configured_wide_vector3*> renders `in T` (by_reference
        // = true, matching Marshaller<const char**>'s "string" projection), so there is no direct
        // way to spell "no value" in C#. Unsafe.NullRef<T>() produces a reference to address 0,
        // which crosses the same P/Invoke `in` slot as a genuine null pointer and reaches
        // ProjectedValueConstPointerMarshallerBase::ConvertToAbi's null branch; the native
        // function returns a distinct sentinel (-1) rather than reading through a null pointer.
        Assert.Equal(-1.0f, E2EBindings.SumConfiguredWideVector3Ptr(in Unsafe.NullRef<ConfiguredWideVector3>()));
    }
}
