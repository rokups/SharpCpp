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

    [Fact]
    public void Fields_left_out_because_of_a_managed_name_clash_are_logged()
    {
        Assert.Contains(Lines, static line =>
            line.StartsWith("WARNING\tField\t", StringComparison.Ordinal) &&
            line.Contains("SilentDrops.hpp", StringComparison.Ordinal) &&
            line.Contains("silent_drop_field_clash::count", StringComparison.Ordinal) &&
            line.EndsWith("field not rendered: managed name 'Count' clashes with a member of the same name", StringComparison.Ordinal));
        Assert.Contains(Lines, static line =>
            line.StartsWith("WARNING\tField\t", StringComparison.Ordinal) &&
            line.Contains("silent_drop_generic_field_clash::count", StringComparison.Ordinal) &&
            line.Contains("field not rendered", StringComparison.Ordinal));
    }

    [Fact]
    public void Bases_lost_while_hoisting_through_a_dropped_base_are_logged()
    {
        Assert.Contains(Lines, static line =>
            line.StartsWith("WARNING\tRecord\t", StringComparison.Ordinal) &&
            line.Contains("\tsilent_drop_hoisting_derived\t", StringComparison.Ordinal) &&
            line.Contains("base 'silent_drop_virtual_root' of the dropped base 'silent_drop_generic_base<int>' is not inherited", StringComparison.Ordinal));
    }

    [Fact]
    public void Generic_instantiations_removed_by_validation_are_logged_with_their_type_arguments()
    {
        Assert.Contains(Lines, static line =>
            line.StartsWith("WARNING\tFunction\t", StringComparison.Ordinal) &&
            line.Contains("\tvalidation_unresolved_instantiation\t", StringComparison.Ordinal) &&
            line.Contains("generic instantiation <validation_unresolved::payload*> of ", StringComparison.Ordinal) &&
            line.Contains(" removed because ", StringComparison.Ordinal));
    }

    [Fact]
    public void Type_aliases_that_are_not_generated_are_logged_on_the_compilation()
    {
        Assert.Contains(Lines, static line =>
            line.StartsWith("WARNING\tCompilation\t(unknown)\t", StringComparison.Ordinal) &&
            line.Contains("type alias 'c_file_handle' = ", StringComparison.Ordinal) &&
            line.Contains(" not generated: ", StringComparison.Ordinal));
    }
}
