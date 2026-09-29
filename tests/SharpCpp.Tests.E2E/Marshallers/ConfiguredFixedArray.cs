using System.Runtime.InteropServices;

using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Marshallers;

/// Mirrors JPH::Float3's projection to global::RedBolt.Math.Float3: a POD value type
/// projected to a namespaced managed type, with suppress_emission = true so SharpCpp does
/// not generate it. Layout must match the native abi_type (configured_vector3_abi) exactly.
[StructLayout(LayoutKind.Sequential)]
public struct ConfiguredVector3(float x, float y, float z)
{
    public float X = x;
    public float Y = y;
    public float Z = z;
}

public sealed class ConfiguredFixedArrayTests
{
    [Fact]
    public void Fixed_array_field_of_projected_value_type_reads_and_writes_elements()
    {
        using var triangle = E2EBindings.MakeConfiguredTriangle(
            1, 2, 3,
            4, 5, 6,
            7, 8, 9);

        var vertices = triangle.Vertices;
        Assert.Equal(3, vertices.Length);
        Assert.Equal(new ConfiguredVector3(1, 2, 3), vertices[0]);
        Assert.Equal(new ConfiguredVector3(4, 5, 6), vertices[1]);
        Assert.Equal(new ConfiguredVector3(7, 8, 9), vertices[2]);
        Assert.Equal(45.0f, E2EBindings.SumConfiguredTriangle(triangle));

        vertices[0] = new ConfiguredVector3(10, 20, 30);
        Assert.Equal(new ConfiguredVector3(10, 20, 30), triangle.Vertices[0]);
        Assert.Equal(99.0f, E2EBindings.SumConfiguredTriangle(triangle));

        using var replacement = new global::SharpCpp.NativeArray<ConfiguredVector3>(
            new[]
            {
                new ConfiguredVector3(1, 1, 1),
                new ConfiguredVector3(2, 2, 2),
                new ConfiguredVector3(3, 3, 3),
            });
        triangle.Vertices = replacement;
        Assert.Equal(new ConfiguredVector3(1, 1, 1), triangle.Vertices[0]);
        Assert.Equal(new ConfiguredVector3(2, 2, 2), triangle.Vertices[1]);
        Assert.Equal(new ConfiguredVector3(3, 3, 3), triangle.Vertices[2]);
        Assert.Equal(18.0f, E2EBindings.SumConfiguredTriangle(triangle));
    }

    [Fact]
    public void Fixed_array_field_assigned_from_another_instances_field_copies_values()
    {
        // Regression coverage for the whole-field setter's fixed-array branch: the source
        // NativeArray<ConfiguredVector3> here is a non-owning view straight into another live
        // record's cpp_type[N] storage (not a freshly constructed managed array), which is the
        // shape that originally failed to compile when abi_type and cpp_type diverged.
        using var source = E2EBindings.MakeConfiguredTriangle(
            21, 22, 23,
            24, 25, 26,
            27, 28, 29);
        using var target = E2EBindings.MakeConfiguredTriangle(
            1, 1, 1,
            2, 2, 2,
            3, 3, 3);

        target.Vertices = source.Vertices;

        Assert.Equal(new ConfiguredVector3(21, 22, 23), target.Vertices[0]);
        Assert.Equal(new ConfiguredVector3(24, 25, 26), target.Vertices[1]);
        Assert.Equal(new ConfiguredVector3(27, 28, 29), target.Vertices[2]);

        // Verify natively (not just via the managed getters) that the raw element copy landed
        // in target's own storage and left source untouched.
        Assert.Equal(225.0f, E2EBindings.SumConfiguredTriangle(target));
        Assert.Equal(225.0f, E2EBindings.SumConfiguredTriangle(source));
    }
}
