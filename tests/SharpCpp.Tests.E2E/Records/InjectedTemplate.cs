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
        using var made = E2EBindings.MakeSlotMap(allocator);
        Assert.Equal(8, made.Capacity());
    }
}
