using Secondary_bases;

using Xunit;

namespace SharpCpp.Tests.E2E.Records;

public sealed class SecondaryBaseTests
{
    [Fact]
    public void Private_override_of_a_secondary_pure_virtual_leaves_the_class_concrete()
    {
        Assert.False(typeof(HiddenRunner).IsAbstract);
        using var runner = new HiddenRunner();
        Assert.Equal(1, runner.PrimaryValue());
        Assert.Equal(5, runner.Run());
        Assert.True(typeof(IRunnable).IsAssignableFrom(typeof(HiddenRunner)));
    }

    [Fact]
    public void Nested_secondary_base_interfaces_inherit_members_instead_of_redeclaring_them()
    {
        Assert.Contains(typeof(ITickable), typeof(ILabelled).GetInterfaces());
        var declared = typeof(ILabelled).GetMethods(
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.DeclaredOnly);
        Assert.DoesNotContain(declared, static method => method.Name == "Tick");
        Assert.DoesNotContain(declared, static method => method.Name is "GetRate" or "SetRate");

        using var clock = new Clock();
        Assert.Equal(9, SharpCpp.Tests.E2E.Generated.E2EBindings.TickViaTickable(clock));
        Assert.Equal(9, SharpCpp.Tests.E2E.Generated.E2EBindings.TickViaLabelled(clock));
        Assert.Equal(9, ((ITickable)clock).Tick());
        Assert.Equal(4, ((ITickable)clock).GetRate());
    }

    [Fact]
    public void Accessor_forwarders_of_inherited_interface_members_implement_the_declaring_interface()
    {
        var map = typeof(Labelled).GetInterfaceMap(typeof(ITickable));
        var index = Array.FindIndex(map.InterfaceMethods, static method => method.Name == "GetRate");
        Assert.True(index >= 0);
        Assert.False(map.TargetMethods[index].IsPublic);
        Assert.DoesNotContain(
            typeof(Labelled).GetInterfaceMap(typeof(ILabelled)).TargetMethods,
            static method => method.Name.EndsWith("GetRate", StringComparison.Ordinal));
    }

    [Fact]
    public void Covariant_overrides_of_secondary_base_virtuals_bind_the_base_declaration_in_their_place()
    {
        Assert.Equal(typeof(Tickable), typeof(Labelled).GetMethod("Self")!.ReturnType);
        Assert.Equal(typeof(Tickable), typeof(Clock).GetMethod("Self")!.ReturnType);
        Assert.Equal(typeof(Pulse), typeof(Lighthouse).GetMethod("Source")!.ReturnType);
        Assert.Equal(typeof(Beacon), typeof(Lighthouse).GetMethod("Origin")!.ReturnType);
        Assert.Equal(typeof(Beacon), typeof(SignalTower).GetMethod("Origin")!.ReturnType);

        // The projected base declaration is non-virtual so a director cannot override it covariantly.
        Assert.True(IsNonVirtual(typeof(Clock).GetMethod("Self")!));
        Assert.True(IsNonVirtual(typeof(SignalTower).GetMethod("Origin")!));
    }

    // An implicit interface implementation is reported as virtual final; only an overridable one is virtual.
    private static bool IsNonVirtual(System.Reflection.MethodInfo method) => !method.IsVirtual || method.IsFinal;

    [Fact]
    public void Covariant_secondary_base_overrides_dispatch_natively_through_the_base_declaration()
    {
        using var clock = new Clock();
        Assert.Equal(9, clock.Self().Tick());
        Assert.Equal(9, ((ITickable)clock).Self().Tick());

        using var lighthouse = new Lighthouse();
        Assert.NotNull(lighthouse.Source());
        Assert.NotNull(lighthouse.Origin());
    }

    private sealed class TowerSubclass : SignalTower
    {
    }

    [Fact]
    public void A_managed_subclass_of_a_director_with_a_degraded_covariant_override_still_constructs()
    {
        using var tower = new TowerSubclass();
        Assert.NotNull(tower.Origin());
        Assert.Equal(1, tower.PrimaryValue());
    }
}
