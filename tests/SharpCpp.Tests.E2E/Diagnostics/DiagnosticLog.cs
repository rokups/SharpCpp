using Xunit;

namespace SharpCpp.Tests.E2E.Diagnostics;

public sealed class DiagnosticLogTests
{
    private static readonly string[] Lines = GeneratedArtifacts.ReadText("generator.log").Split('\n');

    [Fact]
    public void Ignored_and_excluded_declarations_are_logged_with_their_source_and_reason()
    {
        Assert.Contains(Lines, static line =>
            line.StartsWith("EXCLUDED\tRecord\t", StringComparison.Ordinal) &&
            line.Contains("Ignore.hpp(9,", StringComparison.Ordinal) &&
            line.EndsWith("\tskipped_record_with_reason\tnot yet supported", StringComparison.Ordinal));
        Assert.Contains(Lines, static line =>
            line.StartsWith("IGNORED\tFunction\t", StringComparison.Ordinal) &&
            line.Contains("\tunsupported_operator_surface::operator*\t", StringComparison.Ordinal));
    }

    [Fact]
    public void Silent_exclusions_and_warnings_are_logged()
    {
        Assert.Contains(Lines, static line => line.StartsWith("EXCLUDED\tRecord\t(unknown)\tstd::locale\t", StringComparison.Ordinal));
        Assert.Contains(Lines, static line =>
            line.StartsWith("WARNING\tRecord\t", StringComparison.Ordinal) &&
            line.Contains("dropping base", StringComparison.Ordinal));
    }

    [Fact]
    public void Diagnostics_configured_after_validation_are_logged_with_a_summary_per_module()
    {
        Assert.Contains(Lines, static line => line == "WARNING\tCompilation\t(unknown)\tSharpCpp.Tests.E2E.Modules.OwnedFiles\towned files post-validation diagnostic");
        Assert.Contains(Lines, static line => line.StartsWith("SharpCpp binding diagnostics: ", StringComparison.Ordinal) && line.EndsWith(" warnings.", StringComparison.Ordinal));
    }
}
