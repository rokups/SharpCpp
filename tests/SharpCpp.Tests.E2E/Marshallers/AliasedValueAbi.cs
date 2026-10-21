using System.Runtime.InteropServices;

using Aliased_value_abi;

using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Marshallers;

/// Managed mirror of aliased_int_pair_abi. The native type aliased_value_abi::int_pair has a base
/// class and a user constructor, so it does not cross the boundary itself.
[StructLayout(LayoutKind.Sequential)]
public record struct AliasedIntPair(int X, int Y);

public sealed class AliasedValueAbiTests
{
    [Fact]
    public void Alias_of_a_specialization_with_a_different_abi_type_crosses_as_parameter_and_return()
    {
        Assert.Equal(new AliasedIntPair(3, 4), E2EBindings.MakeIntPair(3, 4));
        Assert.Equal(34, E2EBindings.SumIntPair(new AliasedIntPair(3, 4)));
        Assert.Equal(56, E2EBindings.SumIntPairConstReference(new AliasedIntPair(5, 6)));

        var value = new AliasedIntPair(1, 2);
        E2EBindings.DoubleIntPair(ref value);
        Assert.Equal(new AliasedIntPair(2, 4), value);
    }

    [Fact]
    public void Fields_spelled_as_the_alias_or_the_specialization_convert_through_the_marshaller()
    {
        using var holder = new IntPairHolder { Id = 1 };
        holder.Value = new AliasedIntPair(2, 3);
        holder.Canonical = new AliasedIntPair(4, 5);

        Assert.Equal(new AliasedIntPair(2, 3), holder.Value);
        Assert.Equal(new AliasedIntPair(4, 5), holder.Canonical);
        Assert.Equal(12345, E2EBindings.ReadIntPairHolder(holder));
    }
}
