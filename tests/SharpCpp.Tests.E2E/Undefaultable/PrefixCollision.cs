using System.Reflection;

using E2e.Undefaultable_prefix;

using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Undefaultable;

public sealed class UndefaultablePrefixCollisionTests
{
    private static readonly sbyte[] Source = [(sbyte)'a', (sbyte)'b', (sbyte)'c', 0];
    private static readonly sbyte[] Entry = [(sbyte)'e', (sbyte)'n', 0];
    private static readonly sbyte[] Main = [(sbyte)'m', 0];

    [Fact]
    public void Shared_prefix_constructors_keep_only_their_full_overloads()
    {
        // Both constructors split at the undefaultable `entry`; their `(source, length)` prefixes
        // are ambiguous natively, so neither is emitted.
        Assert.Equal(
            [[typeof(sbyte).MakeByRefType(), typeof(nuint), typeof(sbyte).MakeByRefType(), typeof(double)],
             [typeof(sbyte).MakeByRefType(), typeof(nuint), typeof(sbyte).MakeByRefType(), typeof(int), typeof(int)]],
            PublicParameterLists(typeof(PrefixSharedInfo).GetConstructors()));

        using var macros = new PrefixSharedInfo(in Source[0], 3, in Entry[0], 5, 6);
        Assert.Equal(1, macros.Origin());
        Assert.Equal(56312, macros.Value());

        using var macrosDefault = new PrefixSharedInfo(in Source[0], 3, in Main[0], 5);
        Assert.Equal(1, macrosDefault.Origin());
        Assert.Equal(53311, macrosDefault.Value());

        using var language = new PrefixSharedInfo(in Source[0], 3, in Entry[0], 2.5);
        Assert.Equal(2, language.Origin());
        Assert.Equal(2812, language.Value());
    }

    [Fact]
    public void Shared_prefix_methods_and_functions_keep_only_their_full_overloads()
    {
        Assert.All(
            PublicParameterLists(typeof(PrefixSharedInfo).GetMethods().Where(static method => method.Name == nameof(PrefixSharedInfo.Describe))),
            static parameters => Assert.Equal(4, parameters.Length));
        Assert.All(
            PublicParameterLists(typeof(E2EBindings).GetMethods().Where(static method => method.Name == nameof(E2EBindings.PrefixSharedDescribe))),
            static parameters => Assert.Equal(4, parameters.Length));

        using var info = new PrefixSharedInfo(in Source[0], 3, in Entry[0], 5, 6);
        Assert.Equal(105312, info.Describe(Source, 3, Entry, 5));
        Assert.Equal(202812, info.Describe(Source, 3, Entry, 2.5));
        Assert.Equal(505312, E2EBindings.PrefixSharedDescribe(Source, 3, Entry, 5));
        Assert.Equal(602812, E2EBindings.PrefixSharedDescribe(Source, 3, Entry, 2.5));
    }

    [Fact]
    public void Prefix_matching_a_full_overload_binds_to_that_overload()
    {
        Assert.Equal(
            [2, 4],
            typeof(PrefixExactInfo).GetMethods()
                .Where(static method => method.Name == nameof(PrefixExactInfo.Describe))
                .Select(static method => method.GetParameters().Length)
                .Order());
        Assert.Equal(
            [2, 4],
            typeof(E2EBindings).GetMethods()
                .Where(static method => method.Name == nameof(E2EBindings.PrefixExactDescribe))
                .Select(static method => method.GetParameters().Length)
                .Order());

        using var info = new PrefixExactInfo();
        Assert.Equal(300310, info.Describe(Source, 3));
        Assert.Equal(403311, info.Describe(Source, 3, Main));
        Assert.Equal(404312, info.Describe(Source, 3, Entry, 4));
        Assert.Equal(700310, E2EBindings.PrefixExactDescribe(Source, 3));
        Assert.Equal(803311, E2EBindings.PrefixExactDescribe(Source, 3, Main));
        Assert.Equal(804312, E2EBindings.PrefixExactDescribe(Source, 3, Entry, 4));
    }

    private static Type[][] PublicParameterLists(IEnumerable<MethodBase> members)
    {
        return members
            .Select(static member => member.GetParameters().Select(static parameter => parameter.ParameterType).ToArray())
            .OrderBy(static parameters => parameters.Length)
            .ToArray();
    }
}
