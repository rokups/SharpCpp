using Xunit;

namespace SharpCpp.Tests.E2E.Diagnostics;

public sealed class InstantiationConflictTests
{
    // Modules/InstantiationConflict has two specializations of one template. They differ only in an
    // argument that public_type "HiddenBox<{0}>" does not use. One managed class would call the
    // entry points of one specialization on an object of the other, thus the generator fails.
    [Fact]
    public void Specializations_that_differ_only_in_a_hidden_argument_are_rejected_with_both_names()
    {
        var message = GeneratedArtifacts.ReadText("InstantiationConflict.error.txt");

        Assert.Contains("'hidden_argument_conflict::hidden_box' has two specializations with one managed type 'HiddenBox'", message, StringComparison.Ordinal);
        Assert.Contains("'hidden_argument_conflict::hidden_box<hidden_argument_conflict::hidden_item*, hidden_argument_conflict::tag_one>'", message, StringComparison.Ordinal);
        Assert.Contains("'hidden_argument_conflict::hidden_box<hidden_argument_conflict::hidden_item*, hidden_argument_conflict::tag_two>'", message, StringComparison.Ordinal);
    }

    // Modules/InstantiationSpelling spells one specialization through a defaulted argument, an
    // explicit default, an alias and a defaulted nested argument. These are not a conflict.
    [Fact]
    public void Different_spellings_of_one_specialization_are_accepted()
    {
        var message = GeneratedArtifacts.ReadText("InstantiationSpelling.error.txt");

        Assert.StartsWith("The generator accepted the module.", message, StringComparison.Ordinal);
        Assert.Contains("hidden_argument_spelling::spelled_box: 1 instantiation(s)", message, StringComparison.Ordinal);
        Assert.Contains("hidden_argument_spelling::nested_box: 1 instantiation(s)", message, StringComparison.Ordinal);
    }
}
