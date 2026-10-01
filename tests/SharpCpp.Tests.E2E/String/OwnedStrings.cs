using System;
using System.IO;
using System.Linq;
using System.Reflection;

using SharpCpp.Tests.E2E.Diagnostics;
using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.String;

public sealed class OwnedStringTests
{
    private static MethodInfo Method(string name) => typeof(E2EBindings).GetMethod(name)!;

    [Fact]
    public void Owned_char_pointer_return_is_copied_and_freed()
    {
        var before = E2EBindings.OwnedTextLiveAllocations();

        var text = E2EBindings.MakeOwnedText("abc"u8);

        Assert.Equal("abc-owned", text);
        Assert.Equal(before, E2EBindings.OwnedTextLiveAllocations());
        Assert.Equal(typeof(string), Method(nameof(E2EBindings.MakeOwnedText)).ReturnType);
    }

    [Fact]
    public void Owned_const_char_pointer_return_is_copied_and_freed_by_a_void_pointer_release()
    {
        var before = E2EBindings.OwnedTextLiveAllocations();

        Assert.Equal("owned const text", E2EBindings.MakeOwnedConstText());

        Assert.Equal(before, E2EBindings.OwnedTextLiveAllocations());
        Assert.Equal(typeof(string), Method(nameof(E2EBindings.MakeOwnedConstText)).ReturnType);
    }

    [Fact]
    public void Null_owned_return_is_null_and_skips_the_free()
    {
        var before = E2EBindings.OwnedTextLiveAllocations();

        Assert.Null(E2EBindings.MakeNullOwnedText());

        Assert.Equal(before, E2EBindings.OwnedTextLiveAllocations());
    }

    [Fact]
    public void Owned_output_string_is_copied_and_freed()
    {
        var before = E2EBindings.OwnedTextLiveAllocations();

        E2EBindings.GetOwnedLabel(out var label);

        Assert.Equal("owned label", label);
        Assert.Equal(before, E2EBindings.OwnedTextLiveAllocations());
        var parameter = Method(nameof(E2EBindings.GetOwnedLabel)).GetParameters().Single();
        Assert.Equal(typeof(string).MakeByRefType(), parameter.ParameterType);
        Assert.True(parameter.IsOut);
    }

    [Fact]
    public void Null_owned_output_string_is_null_and_skips_the_free()
    {
        var before = E2EBindings.OwnedTextLiveAllocations();

        E2EBindings.GetOwnedNullLabel(out var label);

        Assert.Null(label);
        Assert.Equal(before, E2EBindings.OwnedTextLiveAllocations());
    }

    [Fact]
    public void Unwritten_owned_output_string_is_null_and_is_not_freed()
    {
        var before = E2EBindings.OwnedTextLiveAllocations();

        var status = E2EBindings.GetOwnedLabelUnwritten(out var label);

        Assert.Equal(-1, status);
        Assert.Null(label);
        Assert.Equal(before, E2EBindings.OwnedTextLiveAllocations());
    }

    [Fact]
    public void Tagged_function_declared_before_its_definition_is_owned_once()
    {
        var before = E2EBindings.OwnedTextLiveAllocations();

        Assert.Equal("bleed", E2EBindings.MakeOwnedBleedTagged());

        Assert.Equal(before, E2EBindings.OwnedTextLiveAllocations());
        Assert.Equal(typeof(string), Method(nameof(E2EBindings.MakeOwnedBleedTagged)).ReturnType);
        Assert.Single(typeof(E2EBindings).GetMethods(), static method => method.Name == nameof(E2EBindings.MakeOwnedBleedTagged));
    }

    [Theory]
    [InlineData(nameof(E2EBindings.MakeBleedUntaggedAfterPrototype), "make_bleed_untagged_after_prototype")]
    [InlineData(nameof(E2EBindings.MakeBleedUntaggedAfterBody), "make_bleed_untagged_after_body")]
    public void Untagged_neighbours_of_a_tagged_function_keep_their_plain_return(string managedName, string nativeName)
    {
        Assert.Equal(typeof(IntPtr), Method(managedName).ReturnType);
        Assert.DoesNotContain(
            GeneratedArtifacts.ReadText("generator.log").Split('\n'),
            line => line.Contains($"\t{nativeName}\t", StringComparison.Ordinal));
    }

    [Fact]
    public void Owned_return_and_owned_output_string_in_one_call_are_both_freed()
    {
        var before = E2EBindings.OwnedTextLiveAllocations();

        var text = E2EBindings.MakeOwnedTextWithLabel(out var label);

        Assert.Equal("paired text", text);
        Assert.Equal("paired label", label);
        Assert.Equal(before, E2EBindings.OwnedTextLiveAllocations());
    }

    [Fact]
    public void Consumer_tagged_return_and_output_string_are_owned()
    {
        var before = E2EBindings.OwnedTextLiveAllocations();

        Assert.Equal("consumer text", E2EBindings.MakeConsumerOwnedText());
        E2EBindings.GetConsumerOwnedLabel(out var label);

        Assert.Equal("consumer label", label);
        Assert.Equal(before, E2EBindings.OwnedTextLiveAllocations());
        Assert.Equal(typeof(string), Method(nameof(E2EBindings.MakeConsumerOwnedText)).ReturnType);
    }

    [Fact]
    public void Each_free_function_gets_one_native_thunk_and_one_managed_import()
    {
        var generated = Directory.GetFiles(GeneratedArtifacts.Directory, "*.g.cpp")
            .Select(File.ReadAllText)
            .ToArray();
        Assert.Equal(
            1,
            generated.Sum(static text => CountOccurrences(text, "SHARPCPP_EXPORT_NAMED(sharpcpp_e2e_free_release_owned_text)")));
        Assert.Equal(
            1,
            generated.Sum(static text => CountOccurrences(text, "SHARPCPP_EXPORT_NAMED(sharpcpp_e2e_free_release_owned_memory)")));
        Assert.DoesNotContain(generated, static text => text.Contains("sharpcpp_e2e_free_release_two_args", StringComparison.Ordinal));

        var managed = Directory.GetFiles(GeneratedArtifacts.Directory, "*.g.cs")
            .Select(File.ReadAllText)
            .ToArray();
        Assert.Equal(
            1,
            managed.Sum(static text => CountOccurrences(text, "internal static partial void __free_release_owned_text(nint value);")));
    }

    [Theory]
    [InlineData("MakeUnfreedText", "make_unfreed_text", "return value is freed by 'no_such_release', which is not a free function of the compilation")]
    [InlineData("MakeTwoArgFreedText", "make_two_arg_freed_text", "return value is freed by 'release_two_args', which does not take one pointer parameter")]
    [InlineData("MakeNonPointerOwned", "make_non_pointer_owned", "return value is freed by 'release_owned_text' but does not return a char pointer")]
    [InlineData("MeasureInFreed", "measure_in_freed", "parameter 'names' is freed by 'release_owned_text' but is not an output string")]
    [InlineData("SumCountedFreed", "sum_counted_freed", "parameter 'names' is freed by 'release_owned_text' but is not an output string")]
    [InlineData("MakeEmptyFreedBy", "make_empty_freed_by", "has an empty [[sharpcpp::freed_by]] attribute")]
    [InlineData("MakeOverloadedFreed", "make_overloaded_freed", "return value is freed by 'release_overloaded', which is overloaded")]
    [InlineData("MakeDefaultedOwnedText", "make_defaulted_owned_text", "which is not supported together with default-valued, pointer-array, derived-argument or record-pointer input parameters")]
    public void Owned_strings_that_cannot_be_honoured_degrade_the_function_with_a_diagnostic(
        string managedName,
        string nativeName,
        string reason)
    {
        Assert.Null(typeof(E2EBindings).GetMethod(managedName));
        AssertLogged(nativeName, reason);
    }

    [Fact]
    public void Freed_by_on_a_static_method_degrades_the_method_with_a_diagnostic()
    {
        var holder = typeof(E2EBindings).Assembly.GetTypes().Single(static type => type.Name == "OwnedTextHolder");
        Assert.Null(holder.GetMethod("MakeStatic"));
        AssertLogged("owned_text_holder::make_static", "freed_by is supported on free functions only");
    }

    private static void AssertLogged(string nativeName, string reason) =>
        Assert.Contains(
            GeneratedArtifacts.ReadText("generator.log").Split('\n'),
            line => line.Contains($"\t{nativeName}\t", StringComparison.Ordinal) &&
                line.Contains(reason, StringComparison.Ordinal));

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
            count++;
        return count;
    }
}
