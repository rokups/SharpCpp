using System;
using System.Text;

using SharpCpp.Support;
using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.String;

public sealed class StringTests
{
    [Fact]
    public void StringLength_returns_correct_length()
    {
        ReadOnlySpan<byte> value = "hello"u8;
        Assert.Equal(5, E2EBindings.StringLength(value));
    }

    [Fact]
    public void StringLength_returns_correct_length_for_short_string()
    {
        Assert.Equal(1, E2EBindings.StringLength("a"u8));
    }

    [Fact]
    public void StringEquals_returns_true_for_equal_strings()
    {
        Assert.True(E2EBindings.StringEquals("hello"u8, "hello"u8));
    }

    [Fact]
    public void StringEquals_returns_false_for_different_strings()
    {
        Assert.False(E2EBindings.StringEquals("hello"u8, "world"u8));
    }

    [Fact]
    public void StringEquals_returns_false_for_different_length_strings()
    {
        Assert.False(E2EBindings.StringEquals("hi"u8, "hello"u8));
    }

    [Fact]
    public void GetName_returns_SharpCpp_string()
    {
        E2EBindings.GetName(out var value);
        Assert.Equal("SharpCpp", value);
    }

    [Fact]
    public void ReplaceName_replaces_string_value()
    {
        var value = "original";
        E2EBindings.ReplaceName(ref value);
        Assert.Equal("replaced", value);
    }

    [Fact]
    public void GetMessage_returns_SharpCpp_string()
    {
        Assert.True(E2EBindings.GetMessage().SequenceEqual("Hello from SharpCpp"u8));
    }

    [Fact]
    public void String_type_exists_and_inherits_StringBase()
    {
        Assert.True(typeof(Std.String).IsSubclassOf(typeof(StringBase)));
    }
}
