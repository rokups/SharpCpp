using Attribute_syntax;

using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Rename;

public sealed class AttributeSyntaxTests
{
    [Fact]
    public void Spaced_split_and_stacked_attributes_apply()
    {
        using var spaced = new SpacedRename();
        using var bracketSpaced = new BracketSpacedRename();
        Assert.Equal(1, spaced.Value);
        Assert.Equal(2, bracketSpaced.Value);

        Assert.True(typeof(MultilineFlags).IsDefined(typeof(FlagsAttribute), inherit: false));
        Assert.Equal(MultilineFlags.First | MultilineFlags.Second, E2EBindings.CombineMultilineFlags(MultilineFlags.First, MultilineFlags.Second));
    }

    [Fact]
    public void Ignore_reason_with_escaped_quotes_and_newlines_applies()
    {
        Assert.Null(typeof(E2EBindings).GetMethod("EscapedIgnoreReason"));
    }
}
