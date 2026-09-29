using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

using Pv_ns;

using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Marshallers;

/// Hand-written managed side of the pv_ns::pv_vec value projection (mirrors
/// JPH::Float3 -> global::RedBolt.Math.Float3). Layout must match the native abi_type
/// (pv_vec_abi) exactly.
[StructLayout(LayoutKind.Sequential)]
public struct ProjectedValueVec(float x, float y, float z)
{
    public float X = x;
    public float Y = y;
    public float Z = z;
}

/// A suppress_emission Marshaller<T> with a namespaced public_type and no wrapper class is a
/// pure value projection. When the by-value record still reaches the renderer as a handle-shaped
/// binding type (fixed-array items, class-template arguments) the renderer must import it as the
/// projected struct itself rather than as nint / a generated handle marshaller.
public sealed class ProjectedValueHandleTests
{
    private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static MethodInfo[] GetPInvokeImports(Type owner, string namePrefix)
    {
        return owner
            .GetNestedTypes(BindingFlags.NonPublic)
            .Single(static nested => nested.Name == "__PInvoke")
            .GetMethods(AnyStatic)
            .Where(method => method.Name.StartsWith(namePrefix, StringComparison.Ordinal))
            .ToArray();
    }

    [Fact]
    public void Fixed_array_field_items_round_trip_as_the_projected_struct()
    {
        using var holder = E2EBindings.MakePvHolder();
        Assert.Equal(3, holder.Line.Count);

        holder.Line[0] = new ProjectedValueVec(1, 2, 3);
        holder.Line[1] = new ProjectedValueVec(4, 5, 6);
        holder.Line[2] = new ProjectedValueVec(7, 8, 9);

        Assert.Equal(new ProjectedValueVec(1, 2, 3), holder.Line[0]);
        Assert.Equal(new ProjectedValueVec(4, 5, 6), holder.Line[1]);
        Assert.Equal(new ProjectedValueVec(7, 8, 9), holder.Line[2]);

        // The native side observes what the managed setter wrote.
        Assert.Equal(45.0f, holder.SumLine());
        holder.Line[1] = new ProjectedValueVec(0, 0, 0);
        Assert.Equal(30.0f, holder.SumLine());

        using var triangle = E2EBindings.MakePvTri();
        triangle.V[2] = new ProjectedValueVec(10, 11, 12);
        Assert.Equal(new ProjectedValueVec(), triangle.V[0]);
        Assert.Equal(new ProjectedValueVec(10, 11, 12), triangle.V[2]);
    }

    [Fact]
    public void Fixed_array_item_imports_use_the_projected_struct_not_a_handle()
    {
        var getters = GetPInvokeImports(typeof(PvHolder), "Get_Line_Item");
        var setters = GetPInvokeImports(typeof(PvHolder), "Set_Line_Item");
        Assert.Single(getters);
        Assert.Single(setters);

        // By-value return: the struct itself, with no MarshalUsing handle-return marshaller.
        var getter = getters[0];
        Assert.Equal(typeof(ProjectedValueVec), getter.ReturnType);
        Assert.Empty(getter.ReturnParameter.GetCustomAttributes<MarshalUsingAttribute>());

        // By-value parameter: the struct itself, with no handle-parameter import (not nint).
        var valueParameter = setters[0].GetParameters().Last();
        Assert.Equal(typeof(ProjectedValueVec), valueParameter.ParameterType);
        Assert.Empty(valueParameter.GetCustomAttributes<MarshalUsingAttribute>());
    }

    [Fact]
    public void Class_template_argument_round_trips_by_value_as_the_projected_struct()
    {
        using var box = E2EBindings.MakePvBox();

        box.Set(new ProjectedValueVec(1, 2, 3));
        Assert.Equal(new ProjectedValueVec(1, 2, 3), box.Get());

        box.SetAt(1, new ProjectedValueVec(4, 5, 6));
        Assert.Equal(new ProjectedValueVec(), box.GetAt(0));
        Assert.Equal(new ProjectedValueVec(4, 5, 6), box.GetAt(1));

        var getters = GetPInvokeImports(typeof(E2EBindings), "PvBox_Get__");
        Assert.NotEmpty(getters);
        Assert.All(getters, static getter =>
        {
            Assert.Equal(typeof(ProjectedValueVec), getter.ReturnType);
            Assert.Empty(getter.ReturnParameter.GetCustomAttributes<MarshalUsingAttribute>());
        });

        var setters = GetPInvokeImports(typeof(E2EBindings), "PvBox_Set__");
        Assert.NotEmpty(setters);
        Assert.All(setters, static setter => Assert.Equal(typeof(ProjectedValueVec), setter.GetParameters()[1].ParameterType));
    }

    [Fact]
    public void Members_and_functions_taking_the_projected_value_by_value_round_trip()
    {
        Assert.Equal(new ProjectedValueVec(5, 7, 9), E2EBindings.PvAdd(new ProjectedValueVec(1, 2, 3), new ProjectedValueVec(4, 5, 6)));

        using var holder = E2EBindings.MakePvHolder();
        holder.SetSingle(new ProjectedValueVec(3, 2, 1));
        Assert.Equal(new ProjectedValueVec(3, 2, 1), holder.GetSingle());
        Assert.Equal(new ProjectedValueVec(3, 2, 1), holder.Single);

        holder.Single = new ProjectedValueVec(9, 8, 7);
        Assert.Equal(new ProjectedValueVec(9, 8, 7), holder.GetSingle());

        holder.Aliased = new ProjectedValueVec(1, 1, 1);
        Assert.Equal(new ProjectedValueVec(1, 1, 1), holder.Aliased);
    }
}
