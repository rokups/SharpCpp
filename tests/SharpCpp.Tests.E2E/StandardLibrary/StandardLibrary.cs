using System.Linq;

using SharpCpp.Support;
using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.StandardLibrary;

public sealed class StandardLibraryTests
{
    [Fact]
    public void Standard_library_containers_use_support_bases_and_protected_core_methods()
    {
        Assert.Equal(typeof(VectorBase<>), typeof(global::Std.Vector<int>).BaseType!.GetGenericTypeDefinition());

        AssertProtectedOverride(typeof(global::Std.Vector<int>), "GetAtCore");
        AssertProtectedOverride(typeof(global::Std.Vector<int>), "SetAtCore");
        AssertProtectedOverride(typeof(global::Std.Vector<int>), "AddCore");
        AssertProtectedOverride(typeof(global::Std.Vector<int>), "InsertAtCore");
        AssertProtectedOverride(typeof(global::Std.Vector<int>), "RemoveAtCore");
    }

    [Fact]
    public void Vector_round_trips_through_support_base()
    {
        using var numbers = E2EBindings.E2eMakeStdNumbers();

        Assert.Equal(3, numbers.Count);
        Assert.Equal(2, numbers[0]);
        Assert.Equal(4, numbers[1]);
        Assert.Equal(6, numbers[2]);
        Assert.Equal(12, E2EBindings.E2eSumStdNumbers(numbers));

        numbers.Add(8);
        numbers.Insert(1, 5);
        numbers.RemoveAt(0);

        Assert.Equal(4, numbers.Count);
        Assert.Equal(23, E2EBindings.E2eSumStdNumbers(numbers));
    }

    [Fact]
    public void Const_reference_container_methods_preserve_wrapper_return_types()
    {
        Assert.Equal(typeof(global::Std.Vector<int>), typeof(StdLibraryOwner).GetMethod("GetNumbersRef")!.ReturnType);
    }

    [Fact]
    public void Const_reference_container_methods_round_trip_values()
    {
        using var owner = new StdLibraryOwner();

        using var numbers = owner.GetNumbersRef();
        Assert.Equal(3, numbers.Count);
        Assert.Equal(6, numbers.Sum());
    }

    private static void AssertProtectedOverride(Type type, string name)
    {
        var method = type.GetMethod(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
        Assert.True(method!.IsFamily);
        Assert.True(method.IsVirtual);
    }
}
