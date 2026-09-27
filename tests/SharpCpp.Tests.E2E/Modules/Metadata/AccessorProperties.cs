using SharpCpp.Tests.E2E.Modules.Metadata.Dependency;

using Xunit;

namespace SharpCpp.Tests.E2E.Modules.Metadata;

public sealed class AccessorPropertyTests
{
    [Fact]
    public void Matching_accessor_pairs_and_configured_getter_only_patterns_become_properties()
    {
        using var accessors = new DependencyAccessors { Count = 3 };
        Assert.Equal(3, accessors.Count);
        Assert.False(accessors.IsHovered);
        accessors.Hover();
        Assert.True(accessors.IsHovered);
        Assert.Null(typeof(DependencyAccessors).GetMethod("GetCount"));
    }

    [Fact]
    public void Mismatched_or_shadowed_accessors_stay_methods()
    {
        using var accessors = new DependencyAccessors();
        accessors.SetRatio(2.5f);
        Assert.Equal(2, accessors.GetRatio());
        Assert.Equal(3, accessors.GetSize());
        Assert.Equal(4, accessors.Size());
        Assert.Equal(3, accessors.GetLimit());
        Assert.Equal(2, accessors.Limit);
        using var layout = accessors.GetLayout();
        Assert.Equal(8, layout.Stride);
    }

    [Fact]
    public void Accessors_projected_from_a_secondary_base_stay_methods()
    {
        using var leveled = new DependencyLeveled();
        leveled.SetLevel(6);
        Assert.Equal(6, leveled.GetLevel());
        Assert.Null(typeof(DependencyLeveled).GetProperty("Level"));
    }
}
