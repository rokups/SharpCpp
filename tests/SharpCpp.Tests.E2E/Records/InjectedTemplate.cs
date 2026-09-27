using Injected_template;

using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Records;

public sealed class InjectedTemplateTests
{
    [Fact]
    public void Constructors_of_instantiated_templates_with_injected_name_aliases_are_concrete()
    {
        using var allocator = new SlotAllocator();
        using var created = new SlotMap<int, float>(allocator);
        Assert.Equal(8, created.Capacity());
        Assert.Equal(24, created.Scaled(3));
        Assert.False(typeof(SlotMap<int, float>).GetMethod(nameof(SlotMap<int, float>.Scaled))!.GetParameters()[0].HasDefaultValue);
        using var made = E2EBindings.MakeSlotMap(allocator);
        Assert.Equal(8, made.Capacity());
    }

    [Fact]
    public void Nested_record_constructor_defaults_resolve_in_the_enclosing_namespace()
    {
        using var position = new Point(1, 2);
        using var vertex = new Injected_template.Settings.Vertex(position);
        Assert.Equal(4f, vertex.Sum);
    }
}
