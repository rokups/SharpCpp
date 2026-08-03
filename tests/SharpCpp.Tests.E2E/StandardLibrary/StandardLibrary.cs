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
    public void Std_array_can_be_initialized_from_managed_arrays_and_passed_without_call_copy()
    {
        using var numbers = new global::Std.Array<int>(new[] { 2, 4, 6, 8 });

        Assert.Equal(4, numbers.Length);
        Assert.Equal(2, numbers[0]);
        Assert.Equal(20, E2EBindings.E2eSumStdArrayNumbers(numbers));

        numbers[1] = 10;

        Assert.Equal(26, E2EBindings.E2eSumStdArrayNumbers(numbers));
    }

    [Fact]
    public void Std_array_return_wraps_native_array_object()
    {
        using var numbers = E2EBindings.E2eMakeStdArrayNumbers();

        Assert.Equal(4, numbers.Length);
        Assert.Equal(new[] { 1, 3, 5, 7 }, numbers.ToArray());
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

    [Fact]
    public void String_and_list_round_trip_through_native_standard_library_storage()
    {
        using var text = E2EBindings.E2eMakeStdText("hello"u8);
        Assert.Equal("hello", text.ToString());
        text.Assign("updated");
        Assert.Equal("updated", text.ToString());

        using var words = E2EBindings.E2eMakeStdWords();
        Assert.Equal(3, E2EBindings.E2eCountStdWords(words));
        Assert.Equal(new[] { "red", "green", "blue" }, words.Select(word => word.ToString()));

        using var extra = E2EBindings.E2eMakeStdText("white"u8);
        words.Add(extra);
        words.Insert(1, extra);
        words.RemoveAt(0);
        Assert.Equal(4, E2EBindings.E2eCountStdWords(words));
    }

    [Fact]
    public void Ordered_and_unordered_maps_round_trip_through_dictionary_bases()
    {
        using var scores = E2EBindings.E2eMakeStdScores();
        Assert.Equal(3, E2EBindings.E2eTotalStdScores(scores));
        Assert.Equal(2, scores.Count);
        Assert.Equal(new[] { 1, 2 }, scores.Values.Order());

        using var lookup = E2EBindings.E2eMakeStdLookup();
        Assert.Equal(16, E2EBindings.E2eTotalStdLookup(lookup));
        Assert.Equal(2, lookup.Count);
        Assert.Equal(new[] { 7, 9 }, lookup.Values.Order());

        using var owner = new StdLibraryOwner();
        using var ownerScores = owner.GetScoresRef();
        using var ownerWords = owner.GetWordsRef();
        using var ownerLookup = owner.GetLookupRef();
        Assert.Equal(3, ownerScores.Values.Sum());
        Assert.Equal(2, ownerWords.Count);
        Assert.Equal(16, ownerLookup.Values.Sum());
    }

    private static void AssertProtectedOverride(Type type, string name)
    {
        var method = type.GetMethod(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
        Assert.True(method!.IsFamily);
        Assert.True(method.IsVirtual);
    }
}
