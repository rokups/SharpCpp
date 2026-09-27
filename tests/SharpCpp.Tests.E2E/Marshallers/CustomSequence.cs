using Custom_sequence;

using CustomSequences;

using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Marshallers;

public sealed class CustomSequenceTests
{
    [Fact]
    public void Consumer_sequence_projects_elements_through_their_marshallers()
    {
        using var owner = E2EBindings.CreateOwner();
        Assert.Equal(new uint[] { 3, 4 }, owner.Values.ToArray());
        Assert.Equal(7u, E2EBindings.SumIds(owner.Values));

        owner.Values.Add(5);
        owner.Values[0] = 10;
        Assert.Equal(19u, E2EBindings.SumIds(owner.Values));

        Assert.Equal(new[] { 5, 6 }, owner.References.Select(static target => target.Value));
        Assert.Null(typeof(Array<uint>).GetProperty("ImplementationDetail"));
    }

    [Fact]
    public void Record_deriving_from_a_configured_sequence_is_that_sequence()
    {
        Assert.Equal(typeof(Array<uint>), typeof(Custom_sequence.Buffer).BaseType);
        using var buffer = new Custom_sequence.Buffer();
        buffer.Add(2);
        buffer.Add(3);
        Assert.Equal(5u, E2EBindings.SumBuffer(buffer));
    }

    [Fact]
    public void Aliased_string_view_parameters_are_spans()
    {
        Assert.Equal(4, E2EBindings.ConsumeView("text"u8));
    }

    [Fact]
    public void Return_ownership_configured_on_a_reference_projection_owns_the_result()
    {
        using var result = E2EBindings.CreateTargetResult(9);
        using var target = result.Get();
        Assert.True(target.OwnsNativeHandle);
        Assert.Equal(9, target.Value);
    }
}
