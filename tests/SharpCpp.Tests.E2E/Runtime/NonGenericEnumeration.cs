using System.Collections;

using Container_aliases;

using Xunit;

namespace SharpCpp.Tests.E2E.Runtime;

public sealed class NonGenericEnumerationTests
{
    [Fact]
    public void Wrapped_collections_enumerate_through_the_non_generic_interface()
    {
        using var text = new Std.String("ab");
        Assert.Equal(new object[] { 'a', 'b' }, Enumerate(text));

        using var vector = new Std.Vector<int>(new[] { 1, 2 });
        Assert.Equal(new object[] { 1, 2 }, Enumerate(vector));

        using var array = new Std.Array<int>(new[] { 3, 4, 5, 6 });
        Assert.Equal(new object[] { 3, 4, 5, 6 }, Enumerate(array));

        using var hub = new DataHub();
        var entry = Assert.IsType<KeyValuePair<uint, Variant>>(Assert.Single(Enumerate(hub.GetEventDataMap())));
        Assert.Equal(7u, entry.Key);
    }

    private static object[] Enumerate(IEnumerable values)
    {
        var result = new List<object>();
        foreach (var value in values)
            result.Add(value);
        return result.ToArray();
    }
}
