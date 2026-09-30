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
    [Fact]
    public void Secondary_view_interfaces_forward_accessor_methods_to_promoted_properties()
    {
        using var source = new DependencyTagSource { Tag = 4 };
        IDependencyTagSource view = source;
        Assert.Equal(4, view.GetTag());
        view.SetTag(7);
        Assert.Equal(7, source.Tag);
        Assert.True(view.IsMarked());
        Assert.Null(typeof(DependencyTagSource).GetMethod("GetTag"));

        using var tagged = new DependencyTagged();
        IDependencyTagSource taggedView = tagged;
        taggedView.SetTag(3);
        Assert.Equal(3, taggedView.GetTag());

        using var reader = new DependencyTagReader();
        IDependencyTagReader readerView = reader;
        readerView.SetTag(5);
        Assert.Equal(5, reader.Tag);
        Assert.Equal(5, readerView.GetTag());
        Assert.True(readerView.IsMarked());
        Assert.Equal(10, readerView.DoubledTag());

        using var reading = new DependencyTagReading();
        IDependencyTagReader readingView = reading;
        readingView.SetTag(2);
        Assert.Equal(4, readingView.DoubledTag());
    }
}
