using Nested_enums;

using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Enum;

public sealed class NestedEnumTests
{
    [Fact]
    public void Enum_scoped_by_an_otherwise_empty_record_takes_the_record_name()
    {
        Assert.Equal(IndexFormat.UInt32, E2EBindings.GetIndexFormat());
        Assert.Equal(IndexFormat.UInt16, E2EBindings.EchoIndexFormat(IndexFormat.UInt16));
    }

    [Fact]
    public void Enums_with_the_same_short_name_stay_distinct_by_scope()
    {
        Assert.Equal(Nested_enums.First.Type.FirstValue, E2EBindings.GetFirstType());
        Assert.Equal(Nested_enums.Second.Log.Type.SecondValue, Nested_enums.Second.Log.GetType());
        Assert.Equal(Channel.Type.Error, Channel.GetType());

        using var variant = new Variant(Variant.Type.Integer);
        Assert.Equal(Variant.Type.Integer, variant.GetType());
    }

    [Fact]
    public void Nested_and_small_enums_keep_their_underlying_types()
    {
        Assert.Equal(typeof(ulong), System.Enum.GetUnderlyingType(typeof(Variant.Type)));
        Assert.Equal(typeof(sbyte), System.Enum.GetUnderlyingType(typeof(SmallI8)));
        Assert.Equal(typeof(byte), System.Enum.GetUnderlyingType(typeof(SmallU8)));
        Assert.Equal(typeof(short), System.Enum.GetUnderlyingType(typeof(SmallI16)));
        Assert.Equal(typeof(ushort), System.Enum.GetUnderlyingType(typeof(SmallU16)));
        Assert.Equal(-3 + 250 - 300 + 65000, E2EBindings.SumSmallEnums(SmallI8.Value, SmallU8.Value, SmallI16.Value, SmallU16.Value));
    }
}
