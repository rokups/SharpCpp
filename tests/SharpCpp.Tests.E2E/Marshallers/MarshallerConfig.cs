using Marshaller_config;

using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Marshallers;

public sealed class MarshallerConfigTests
{
    [Fact]
    public void Token_spaced_marshaller_attributes_configure_the_type()
    {
        Assert.Equal(25, E2EBindings.Describe(2));
        Assert.Equal(27, E2EBindings.Describe(2, 7));

        // undefaultable: the native default is reached through an overload, not a C# default.
        var describe = typeof(E2EBindings).GetMethods()
            .Where(static method => method.Name == nameof(E2EBindings.Describe))
            .ToArray();
        Assert.Equal(2, describe.Length);
        Assert.All(describe.SelectMany(static method => method.GetParameters()), static parameter => Assert.False(parameter.HasDefaultValue));
    }

    [Fact]
    public void Exposed_members_are_inherited_from_helper_bases_and_leaf_declarations_win()
    {
        var tally = E2EBindings.GetTally();
        Assert.Equal(4, tally.Count());
        Assert.Equal(12, tally.Weight());
        Assert.True(typeof(Tally).GetMethod(nameof(Tally.Weight))!.IsPublic);
    }

    [Fact]
    public void Same_named_map_value_records_keep_their_own_identity()
    {
        using var store = new VariantStore();
        using var key = new Std.String("a");
        Assert.Equal(1, store.GetUrho()[key].Get());
        Assert.Equal(200, store.GetRml()[key].Get());
        Assert.IsType<Marshaller_config.Urho.Variant>(store.GetUrho()[key]);
        Assert.IsType<Marshaller_config.Rml.Variant>(store.GetRml()[key]);
    }

    [Fact]
    public void Closed_generic_map_values_keep_their_element_type()
    {
        using var store = new VariantStore();
        var tags = store.GetTags();
        Assert.IsType<Std.UnorderedMap<uint, Std.Vector<uint>>>(tags);
        Assert.Equal(new uint[] { 7, 8 }, tags[3u]);
    }

    [Fact]
    public void Marshaller_patterns_repeating_a_template_parameter_match_equal_arguments()
    {
        Assert.Equal(7008L, E2EBindings.PackTwin(7008L));
        // A specialization the pattern does not match stays an instantiation of the record.
        Assert.Equal(typeof(Twin<int, short>), Assert.Single(typeof(E2EBindings).GetMethod(nameof(E2EBindings.PackMixed))!.GetParameters()).ParameterType);
    }

    [Fact]
    public void Marshaller_patterns_repeating_a_non_type_parameter_match_equal_arguments()
    {
        Assert.Equal(7003L, E2EBindings.PackSquare(7003L));
        // Unequal non-type arguments do not match: an ordinary instantiation of the record.
        Assert.Equal(typeof(Triple<int>), Assert.Single(typeof(E2EBindings).GetMethod(nameof(E2EBindings.PackWide))!.GetParameters()).ParameterType);
    }

    [Fact]
    public void Marshaller_patterns_with_a_single_non_type_parameter_bind_any_value()
    {
        Assert.Equal(typeof(int), Assert.Single(typeof(E2EBindings).GetMethod(nameof(E2EBindings.Unbox))!.GetParameters()).ParameterType);
        Assert.Equal(12, E2EBindings.Unbox(12));
    }

    [Fact]
    public void Strings_without_contiguous_storage_are_copied_byte_by_byte()
    {
        var rope = E2EBindings.GetRope();
        Assert.Equal("knot", rope.ToString());
        Assert.True(rope.AsUtf8Span().SequenceEqual("knot"u8));
    }
}
