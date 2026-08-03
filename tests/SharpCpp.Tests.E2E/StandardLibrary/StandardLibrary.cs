using System.Collections.Generic;
using System.Linq;
using System.Text;

using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.StandardLibrary;

public sealed class StandardLibraryTests
{
    [Fact]
    public void Managed_vector_operations_mutate_native_standard_library_storage()
    {
        using var numbers = E2EBindings.E2eMakeStdNumbers();

        Assert.Equal(3, numbers.Count);
        Assert.Equal(new[] { 2, 4, 6 }, numbers.ToArray());
        Assert.Equal(12, E2EBindings.E2eSumStdNumbers(numbers));

        numbers[1] = 10;
        var containsTen = numbers.Contains(10);
        var containsNinetyNine = numbers.Contains(99);
        Assert.True(containsTen);
        Assert.False(containsNinetyNine);
        Assert.Equal(1, numbers.IndexOf(10));
        Assert.Equal(-1, numbers.IndexOf(99));

        var copied = new int[5];
        numbers.CopyTo(copied, 1);
        Assert.Equal(new[] { 0, 2, 10, 6, 0 }, copied);

        numbers.Add(8);
        numbers.Insert(1, 5);
        Assert.True(numbers.Remove(2));
        Assert.False(numbers.Remove(99));
        numbers.RemoveAt(numbers.Count - 1);

        Assert.Equal(new[] { 5, 10, 6 }, numbers.ToArray());
        Assert.Equal(21, E2EBindings.E2eSumStdNumbers(numbers));

        using var clone = numbers.Clone();
        clone.Add(9);
        Assert.Equal(30, E2EBindings.E2eSumStdNumbers(clone));
        Assert.Equal(21, E2EBindings.E2eSumStdNumbers(numbers));

        using var interfaceClone = (global::Std.Vector<int>)((ICloneable)numbers).Clone();
        Assert.Equal(21, E2EBindings.E2eSumStdNumbers(interfaceClone));

        numbers.Clear();
        numbers.Add(11);
        Assert.Equal(11, E2EBindings.E2eSumStdNumbers(numbers));
    }

    [Fact]
    public void Managed_vector_constructors_create_native_standard_library_storage()
    {
        using var enumerable = new global::Std.Vector<int>((IEnumerable<int>)new[] { 3, 5, 7 });
        Assert.Equal(15, E2EBindings.E2eSumStdNumbers(enumerable));

        using var span = new global::Std.Vector<int>(new ReadOnlySpan<int>(new[] { 11, 13 }));
        Assert.Equal(24, E2EBindings.E2eSumStdNumbers(span));

        using var providerCreated = new global::Std.Vector<int>(enumerable.Provider);
        providerCreated.Add(17);
        Assert.Equal(17, E2EBindings.E2eSumStdNumbers(providerCreated));
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

        var containsTen = numbers.Contains(10);
        var containsNinetyNine = numbers.Contains(99);
        Assert.True(containsTen);
        Assert.False(containsNinetyNine);
        Assert.Equal(1, numbers.IndexOf(10));
        Assert.Equal(-1, numbers.IndexOf(99));

        var copied = new int[6];
        numbers.CopyTo(copied, 1);
        Assert.Equal(new[] { 0, 2, 10, 6, 8, 0 }, copied);

        using var clone = numbers.Clone();
        clone[0] = 20;
        Assert.Equal(44, E2EBindings.E2eSumStdArrayNumbers(clone));
        Assert.Equal(26, E2EBindings.E2eSumStdArrayNumbers(numbers));

        using var interfaceClone = (global::Std.Array<int>)((ICloneable)numbers).Clone();
        Assert.Equal(26, E2EBindings.E2eSumStdArrayNumbers(interfaceClone));
    }

    [Fact]
    public void Std_array_return_wraps_native_array_object()
    {
        using var numbers = E2EBindings.E2eMakeStdArrayNumbers();

        Assert.Equal(4, numbers.Length);
        Assert.Equal(new[] { 1, 3, 5, 7 }, numbers.ToArray());
        Assert.Equal(16, E2EBindings.E2eSumStdArrayNumbers(numbers));

        using var empty = new global::Std.Array<int>(4);
        Assert.Equal(0, E2EBindings.E2eSumStdArrayNumbers(empty));

        using var enumerable = new global::Std.Array<int>((IEnumerable<int>)new[] { 9, 8, 7, 6 });
        Assert.Equal(30, E2EBindings.E2eSumStdArrayNumbers(enumerable));

        using var providerCreated = new global::Std.Array<int>(numbers.Provider);
        providerCreated[2] = 12;
        Assert.Equal(12, E2EBindings.E2eSumStdArrayNumbers(providerCreated));
    }

    [Fact]
    public void Const_reference_container_methods_round_trip_values()
    {
        using var owner = new StdLibraryOwner();

        using var numbers = owner.GetNumbersRef();
        var cachedNumbers = owner.GetNumbersRef();
        Assert.Same(numbers, cachedNumbers);
        Assert.Equal(3, numbers.Count);
        Assert.Equal(6, E2EBindings.E2eSumStdNumbers(numbers));
    }

    [Fact]
    public void String_and_list_round_trip_through_native_standard_library_storage()
    {
        using var text = E2EBindings.E2eMakeStdText("hello"u8);
        Assert.Equal("hello", text.ToString());
        Assert.Equal(Checksum("hello"u8), E2EBindings.E2eChecksumStdText(text));
        Assert.Equal("hello"u8.ToArray(), text.ToUtf8Bytes());
        Assert.Equal("hello"u8.ToArray(), text.AsUtf8Span().ToArray());
        Assert.Equal(5, text.Count);
        Assert.Equal('e', text[1]);
        Assert.True(text.Equals("hello"));
        Assert.True(text.Equals((object)"hello"));
        Assert.False(text.Equals((object)42));
        Assert.Equal("hello".GetHashCode(StringComparison.Ordinal), text.GetHashCode());
        Assert.Equal("hello", new string(((IEnumerable<char>)text).ToArray()));

        text.Assign("updated");
        Assert.Equal("updated", text.ToString());
        Assert.Equal(Checksum("updated"u8), E2EBindings.E2eChecksumStdText(text));

        text.Assign("bytes"u8);
        ReadOnlySpan<byte> nativeSpan = text;
        Assert.Equal("bytes"u8.ToArray(), nativeSpan.ToArray());
        Assert.Equal(Checksum("bytes"u8), E2EBindings.E2eChecksumStdText(text));

        using var equalText = E2EBindings.E2eMakeStdText("bytes"u8);
        Assert.True(text.Equals(equalText));

        using var clone = text.Clone();
        clone.Assign("clone");
        Assert.Equal(Checksum("clone"u8), E2EBindings.E2eChecksumStdText(clone));
        Assert.Equal(Checksum("bytes"u8), E2EBindings.E2eChecksumStdText(text));

        using var interfaceClone = (global::Std.String)((ICloneable)text).Clone();
        Assert.Equal(Checksum("bytes"u8), E2EBindings.E2eChecksumStdText(interfaceClone));

        using var constructed = new global::Std.String("constructed");
        Assert.Equal(Checksum("constructed"u8), E2EBindings.E2eChecksumStdText(constructed));

        using global::Std.String implicitText = "implicit";
        Assert.Equal(Checksum("implicit"u8), E2EBindings.E2eChecksumStdText(implicitText));

        using var providerCreated = new global::Std.String(text.Provider);
        Assert.Equal(0, E2EBindings.E2eChecksumStdText(providerCreated));

        text.Assign((string?)null);
        Assert.Equal(0, text.AsUtf8Span().Length);
        Assert.Empty(text.ToUtf8Bytes());
        Assert.Equal(0, E2EBindings.E2eChecksumStdText(text));

        using var words = E2EBindings.E2eMakeStdWords();
        Assert.Equal(3, E2EBindings.E2eCountStdWords(words));
        Assert.Equal(new[] { "red", "green", "blue" }, words.Select(word => word.ToString()));

        using var extra = E2EBindings.E2eMakeStdText("white"u8);
        words.Add(extra);
        words.Insert(1, extra);
        words.RemoveAt(0);
        Assert.Equal(4, E2EBindings.E2eCountStdWords(words));
        using var joined = E2EBindings.E2eJoinStdWords(words);
        Assert.Equal("white,green,blue,white", joined.ToString());

        words[0] = equalText;
        using var updatedJoin = E2EBindings.E2eJoinStdWords(words);
        Assert.Equal("bytes,green,blue,white", updatedJoin.ToString());

        using var wordsClone = words.Clone();
        wordsClone.Clear();
        wordsClone.Add(extra);
        Assert.Equal(1, E2EBindings.E2eCountStdWords(wordsClone));
        Assert.Equal(4, E2EBindings.E2eCountStdWords(words));

        using var interfaceWordsClone = (global::Std.List<global::Std.String>)((ICloneable)words).Clone();
        Assert.Equal(4, E2EBindings.E2eCountStdWords(interfaceWordsClone));

        using var providerWords = new global::Std.List<global::Std.String>(words.Provider);
        providerWords.Add(extra);
        Assert.Equal(1, E2EBindings.E2eCountStdWords(providerWords));
    }

    [Fact]
    public void Ordered_and_unordered_maps_round_trip_through_dictionary_bases()
    {
        using var scores = E2EBindings.E2eMakeStdScores();
        Assert.Equal(3, E2EBindings.E2eTotalStdScores(scores));
        Assert.Equal(2, scores.Count);
        Assert.Equal(new[] { 1, 2 }, scores.Values.Order());

        using var one = E2EBindings.E2eMakeStdText("one"u8);
        using var three = E2EBindings.E2eMakeStdText("three"u8);
        using var missing = E2EBindings.E2eMakeStdText("missing"u8);
        Assert.True(scores.ContainsKey(one));
        Assert.False(scores.ContainsKey(missing));
        Assert.True(scores.TryGetValue(one, out var oneValue));
        Assert.Equal(1, oneValue);
        Assert.False(scores.TryGetValue(missing, out _));
        Assert.Equal(1, scores[one]);

        scores[one] = 10;
        scores.Add(three, 3);
        var containsThree = scores.Contains(new KeyValuePair<global::Std.String, int>(three, 3));
        var containsWrongThree = scores.Contains(new KeyValuePair<global::Std.String, int>(three, 4));
        Assert.True(containsThree);
        Assert.False(containsWrongThree);
        Assert.Equal(15, E2EBindings.E2eTotalStdScores(scores));

        var scorePairs = new KeyValuePair<global::Std.String, int>[scores.Count + 1];
        scores.CopyTo(scorePairs, 1);
        Assert.Equal(15, scorePairs.Skip(1).Sum(pair => pair.Value));
        DisposeKeys(scorePairs.Skip(1));

        Assert.False(scores.Remove(new KeyValuePair<global::Std.String, int>(three, 4)));
        Assert.True(scores.Remove(new KeyValuePair<global::Std.String, int>(three, 3)));
        Assert.False(scores.Remove(missing));
        Assert.Equal(12, E2EBindings.E2eTotalStdScores(scores));

        using var scoresClone = scores.Clone();
        scoresClone.Clear();
        Assert.Equal(0, E2EBindings.E2eTotalStdScores(scoresClone));
        Assert.Equal(12, E2EBindings.E2eTotalStdScores(scores));

        using var interfaceScoresClone = (global::Std.Map<global::Std.String, int>)((ICloneable)scores).Clone();
        Assert.Equal(12, E2EBindings.E2eTotalStdScores(interfaceScoresClone));

        using var providerScores = new global::Std.Map<global::Std.String, int>(scores.Provider);
        providerScores.Add(one, 5);
        Assert.Equal(5, E2EBindings.E2eTotalStdScores(providerScores));

        using var lookup = E2EBindings.E2eMakeStdLookup();
        Assert.Equal(16, E2EBindings.E2eTotalStdLookup(lookup));
        Assert.Equal(2, lookup.Count);
        Assert.Equal(new[] { 7, 9 }, lookup.Values.Order());

        using var left = E2EBindings.E2eMakeStdText("left"u8);
        using var center = E2EBindings.E2eMakeStdText("center"u8);
        Assert.True(lookup.TryGetValue(left, out var leftValue));
        Assert.Equal(7, leftValue);
        lookup[left] = 17;
        lookup.Add(new KeyValuePair<global::Std.String, int>(center, 5));
        var containsCenter = lookup.Contains(new KeyValuePair<global::Std.String, int>(center, 5));
        Assert.True(containsCenter);
        Assert.Equal(31, E2EBindings.E2eTotalStdLookup(lookup));

        Assert.True(lookup.Remove(center));
        Assert.False(lookup.Remove(center));
        Assert.Equal(26, E2EBindings.E2eTotalStdLookup(lookup));

        using var lookupClone = lookup.Clone();
        Assert.True(lookupClone.Remove(new KeyValuePair<global::Std.String, int>(left, 17)));
        Assert.Equal(9, E2EBindings.E2eTotalStdLookup(lookupClone));
        Assert.Equal(26, E2EBindings.E2eTotalStdLookup(lookup));

        using var interfaceLookupClone = (global::Std.UnorderedMap<global::Std.String, int>)((ICloneable)lookup).Clone();
        Assert.Equal(26, E2EBindings.E2eTotalStdLookup(interfaceLookupClone));

        using var providerLookup = new global::Std.UnorderedMap<global::Std.String, int>(lookup.Provider);
        providerLookup.Add(center, 6);
        Assert.Equal(6, E2EBindings.E2eTotalStdLookup(providerLookup));

        using var owner = new StdLibraryOwner();
        using var ownerScores = owner.GetScoresRef();
        using var ownerWords = owner.GetWordsRef();
        using var ownerLookup = owner.GetLookupRef();
        Assert.Equal(3, ownerScores.Values.Sum());
        Assert.Equal(2, E2EBindings.E2eCountStdWords(ownerWords));
        Assert.Equal(16, E2EBindings.E2eTotalStdLookup(ownerLookup));
    }

    private static int Checksum(ReadOnlySpan<byte> value)
        => value.ToArray().Sum(static byteValue => byteValue);

    private static void DisposeKeys(IEnumerable<KeyValuePair<global::Std.String, int>> pairs)
    {
        foreach (var pair in pairs)
        {
            pair.Key?.Dispose();
        }
    }
}
