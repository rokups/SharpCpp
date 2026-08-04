using SharpCpp.Support;
using SharpCpp.Tests.E2E.Generated;
using Phase2_breadth;

using Xunit;

namespace SharpCpp.Tests.E2E.CoverageMatrix;

public abstract class Phase2ManagedBase : NativeObject
{
    protected Phase2ManagedBase(nint handle, bool ownsHandle)
        : base(handle, ownsHandle)
    {
    }
}

public interface IPhase2NativeStatic<TSelf>
    where TSelf : IPhase2NativeStatic<TSelf>
{
    static abstract int GetTypeStatic();
}

public interface IPhase2NativeMarker<TSelf>
{
}

public sealed class Phase2BreadthTests
{
    [Fact]
    public void Phase2_configuration_round_trips_aliases_generics_and_inherited_marshaller_members()
    {
        using var value = new Phase2Value(40);
        Assert.Equal(40, value.ReadConfigured());

        value.WriteConfigured(44);
        Assert.Equal(44, value.GetValue());

        using var clone = value.Clone();
        value.WriteConfigured(46);
        Assert.Equal(44, clone.ReadConfigured());
        Assert.Equal(46, value.ReadConfigured());

        Assert.Same(value, E2EBindings.Phase2ForwardIdentity(value));
        Assert.Same(value, E2EBindings.Phase2GenericIdentity(value));
        Assert.Equal(48, E2EBindings.Phase2InvokeAliasCallback(item => item.ReadConfigured() + 1, value));

        using var box = E2EBindings.CreatePhase2AliasBox(value);
        Assert.Same(value, box.GetValue());
        Assert.Equal(46, E2EBindings.ReadPhase2AliasBox(box));

        using var derived = new Phase2Derived(50);
        Assert.Equal(51, derived.GetValue());
        Assert.Equal(71, ReadNativeStatic<Phase2Derived>());
    }

    private static int ReadNativeStatic<T>()
        where T : IPhase2NativeStatic<T>
        => T.GetTypeStatic();
}
