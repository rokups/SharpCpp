using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Records;

public sealed class CloneTests
{
    [Fact]
    public void Records_with_copy_constructors_implement_ICloneable_and_clone_state()
    {
        using var original = new CloneCounter(7);
        using var clone = original.Clone();

        Assert.NotSame(original, clone);
        Assert.Equal(7, clone.GetValue());

        clone.SetValue(13);

        Assert.Equal(7, original.GetValue());
        Assert.Equal(13, clone.GetValue());
    }

    [Fact]
    public void ICloneable_Clone_returns_typed_wrapper_copy()
    {
        using var original = new CloneCounter(9);
        using var clone = (CloneCounter)((System.ICloneable)original).Clone();

        Assert.NotSame(original, clone);
        Assert.Equal(9, clone.GetValue());
    }

    [Fact]
    public void Native_clone_name_collision_keeps_explicit_copy_contract()
    {
        using var original = new CloneCollisionCounter(17);
        Assert.Equal(18, original.Clone());

        using var clone = (CloneCollisionCounter)((System.ICloneable)original).Clone();
        Assert.NotSame(original, clone);
        Assert.Equal(17, clone.GetValue());
    }
}
