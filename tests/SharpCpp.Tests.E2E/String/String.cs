using System;

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
    public void StringViewLength_returns_correct_length()
    {
        Assert.Equal(5, E2EBindings.StringViewLength("hello"u8));
    }

    [Fact]
    public void StringViewEquals_compares_span_contents()
    {
        Assert.True(E2EBindings.StringViewEquals("hello"u8, "hello"u8));
        Assert.False(E2EBindings.StringViewEquals("hello"u8, "world"u8));
    }

    [Fact]
    public void StringViewLength_preserves_embedded_nulls()
    {
        Assert.Equal(4, E2EBindings.StringViewEmbeddedNullLength("hi\0x"u8));
    }

    [Fact]
    public void GetStringViewMessage_returns_span()
    {
        Assert.True(E2EBindings.GetStringViewMessage().SequenceEqual("Hello from string_view"u8));
    }

    [Fact]
    public void GetStringViewWithEmbeddedNull_returns_full_span()
    {
        Assert.True(E2EBindings.GetStringViewWithEmbeddedNull().SequenceEqual("a\0b"u8));
    }

    [Fact]
    public void CustomStringViewLength_returns_correct_length()
    {
        Assert.Equal(5, E2EBindings.CustomStringViewLength("hello"u8));
    }

    [Fact]
    public void CustomStringViewLength_preserves_embedded_nulls()
    {
        Assert.Equal(4, E2EBindings.CustomStringViewEmbeddedNullLength("hi\0x"u8));
    }

    [Fact]
    public void GetCustomStringViewMessage_returns_span()
    {
        Assert.True(E2EBindings.GetCustomStringViewMessage().SequenceEqual("Hello from custom string_view"u8));
    }

    [Fact]
    public void Custom_standard_library_string_round_trips_through_generated_storage()
    {
        using var value = E2EBindings.MakeCustomStdString("hello"u8);
        Assert.Equal("hello", value.ToString());
        Assert.Equal(5, E2EBindings.MeasureCustomStdString(value));
        Assert.Equal(5, E2EBindings.MeasureCustomStdStringPointer(value));
        Assert.Equal(-1, E2EBindings.MeasureCustomStdStringPointer(null!));

        value.Assign("updated");
        E2EBindings.AppendCustomStdString(value, (byte)'!');
        Assert.Equal("updated!", value.ToString());
        Assert.Equal(8, E2EBindings.MeasureCustomStdString(value));

        using var clone = value.Clone();
        clone.Assign("clone");
        Assert.Equal(5, E2EBindings.MeasureCustomStdString(clone));
        Assert.Equal(8, E2EBindings.MeasureCustomStdString(value));

        using var created = new global::E2EStd.String();
        created.Assign("created");
        Assert.Equal(7, E2EBindings.MeasureCustomStdString(created));
    }
}
