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
    public void Unsupported_template_argument_degrades_only_its_declaration()
    {
        Assert.Null(typeof(E2EBindings).GetMethod("CustomU16TextViewLength"));
        Assert.Equal(3, E2EBindings.CustomTextViewLength("abc"u8));
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

        Assert.Equal(17, created.Echo(17));
        Assert.Equal(2.5, created.Echo(2.5));
        Assert.True(created.EchoBool(true));
        Assert.Equal((sbyte)-7, created.EchoChar((sbyte)-7));
        Assert.Equal((byte)251, created.EchoByte(251));
        Assert.Equal((short)-1234, created.EchoShort(-1234));
        Assert.Equal((ushort)54321, created.EchoUShort(54321));
        Assert.Equal(4_000_000_000u, created.EchoUInt(4_000_000_000u));
        // C long is 32 bits on Windows and 64 bits elsewhere, so the managed width differs.
        dynamic wide = created;
        long longValue = OperatingSystem.IsWindows() ? -2_000_000_000 : -5_000_000_000;
        ulong ulongValue = OperatingSystem.IsWindows() ? 4_000_000_000UL : 10_000_000_000UL;
        Assert.Equal(longValue, (long)wide.EchoLong((dynamic)(OperatingSystem.IsWindows() ? (object)(int)longValue : longValue)));
        Assert.Equal(ulongValue, (ulong)wide.EchoULong((dynamic)(OperatingSystem.IsWindows() ? (object)(uint)ulongValue : ulongValue)));
        Assert.Equal((long)-6_000_000_000, created.EchoLongLong(-6_000_000_000));
        Assert.Equal((ulong)11_000_000_000, created.EchoULongLong(11_000_000_000));
        Assert.Equal(1.25f, created.EchoFloat(1.25f));
        Assert.Equal((ulong)41, created.EchoSize(41));
        Assert.Equal(0, created.EchoPointer(0));
        created.Touch((byte)'!');
        Assert.Equal("created!", created.ToString());
    }
    [Fact]
    public void Custom_string_field_borrows_record_storage()
    {
        using var holder = new CustomStringHolder();
        var text = holder.Text;
        text.Touch((byte)'a');
        Assert.Equal("a", holder.Text.ToString());

        using var value = new global::E2EStd.String();
        value.Assign("set");
        holder.Text = value;
        Assert.Equal("set", holder.Text.ToString());
    }

    [Fact]
    public void Optional_custom_string_field_is_null_when_empty()
    {
        using var holder = new CustomStringHolder();
        Assert.Null(holder.OptionalText);
        Assert.Equal(-1, E2EBindings.OptionalTextSize(holder));
    }

    [Fact]
    public void Optional_custom_string_field_setter_copies_the_value()
    {
        using var holder = new CustomStringHolder();
        using var value = new global::E2EStd.String();
        value.Assign("hello");
        holder.OptionalText = value;
        Assert.Equal(5, E2EBindings.OptionalTextSize(holder));

        value.Assign("changed value");
        Assert.Equal(5, E2EBindings.OptionalTextSize(holder));

        holder.OptionalText = null;
        Assert.Equal(-1, E2EBindings.OptionalTextSize(holder));
        Assert.Null(holder.OptionalText);
    }

    [Fact]
    public void Optional_custom_string_field_getter_returns_owned_copies()
    {
        using var holder = new CustomStringHolder();
        using (var value = new global::E2EStd.String())
        {
            value.Assign("abc");
            holder.OptionalText = value;
        }

        var first = holder.OptionalText;
        var second = holder.OptionalText;
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal("abc", first.ToString());

        first.Touch((byte)'!');
        Assert.Equal("abc!", first.ToString());
        Assert.Equal("abc", second.ToString());
        Assert.Equal(3, E2EBindings.OptionalTextSize(holder));

        E2EBindings.AppendOptionalText(holder, (byte)'?');
        Assert.Equal("abc?", holder.OptionalText!.ToString());
        Assert.Equal("abc", second.ToString());

        first.Dispose();
        second.Dispose();
        Assert.Equal(4, E2EBindings.OptionalTextSize(holder));
    }

    [Fact]
    public void Optional_custom_string_only_field_round_trips()
    {
        using var holder = new OptionalTextOnly();
        Assert.Null(holder.Value);

        using var value = new global::E2EStd.String();
        value.Assign("only");
        holder.Value = value;
        using var copy = holder.Value;
        Assert.NotNull(copy);
        Assert.Equal("only", copy.ToString());

        holder.Value = null;
        Assert.Null(holder.Value);
        Assert.Equal("only", copy.ToString());
    }
}
