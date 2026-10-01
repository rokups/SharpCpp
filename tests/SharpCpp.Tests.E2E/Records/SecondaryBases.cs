using System;
using System.IO;
using System.Linq;
using Secondary_bases;
using SharpCpp.Tests.E2E.Diagnostics;

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
    public void Non_view_class_redeclaring_both_accessors_implements_the_interface_through_its_property()
    {
        using var gauge = new Gauge();
        var tickable = (ITickable)gauge;
        Assert.Equal(20, gauge.Rate);
        Assert.Equal(20, tickable.GetRate());
        tickable.SetRate(7);
        Assert.Equal(7, gauge.Rate);
        Assert.Equal(7, tickable.GetRate());
        gauge.Rate = 8;
        Assert.Equal(8, tickable.GetRate());
        Assert.Equal(21, tickable.Tick());
    }

    [Fact]
    public void Non_view_class_redeclaring_only_the_getter_implements_the_interface_through_its_property()
    {
        using var meter = new Meter();
        var tickable = (ITickable)meter;
        Assert.Equal(103, meter.Rate);
        Assert.Equal(103, tickable.GetRate());
        tickable.SetRate(9);
        Assert.Equal(109, tickable.GetRate());
    }

    [Fact]
    public void Meter_implements_the_getter_explicitly_and_the_setter_with_the_plain_method()
    {
        var map = typeof(Meter).GetInterfaceMap(typeof(ITickable));
        var getter = Array.FindIndex(map.InterfaceMethods, static method => method.Name == "GetRate");
        var setter = Array.FindIndex(map.InterfaceMethods, static method => method.Name == "SetRate");
        Assert.True(getter >= 0);
        Assert.True(setter >= 0);
        Assert.False(map.TargetMethods[getter].IsPublic);
        Assert.True(map.TargetMethods[setter].IsPublic);
    }

    [Fact]
    public void Rated_ticker_keeps_the_secondary_base_methods_beside_the_inherited_property()
    {
        var map = typeof(RatedTicker).GetInterfaceMap(typeof(ITickable));
        foreach (var name in new[] { "GetRate", "SetRate" })
        {
            var index = Array.FindIndex(map.InterfaceMethods, method => method.Name == name);
            Assert.True(index >= 0);
            Assert.True(map.TargetMethods[index].IsPublic);
        }

        Assert.Equal(typeof(RatedTicker), typeof(RatedTicker).GetMethod("GetRate")!.DeclaringType);
        Assert.Equal(typeof(RatedBase), typeof(RatedTicker).GetProperty("Rate")!.DeclaringType);
    }

    [Fact]
    public void Non_view_class_redeclaring_only_the_setter_keeps_the_interface_implemented()
    {
        using var splitter = new Splitter();
        var tickable = (ITickable)splitter;
        Assert.Equal(3, tickable.GetRate());
        tickable.SetRate(5);
        Assert.Equal(23, tickable.Tick());
    }

    [Fact]
    public void Primary_chain_accessors_next_to_a_secondary_base_of_the_same_name_compile_and_round_trip()
    {
        using var ticker = new RatedTicker();
        var tickable = (ITickable)ticker;
        tickable.SetRate(11);
        Assert.Equal(11, tickable.GetRate());
        Assert.Equal(24, tickable.Tick());
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

    [Fact]
    public void A_covariant_override_on_a_secondary_bases_primary_chain_is_projected_with_the_covariant_return_type()
    {
        Assert.Equal(typeof(OriginMid), typeof(OriginUser).GetMethod("Root")!.ReturnType);
        Assert.Equal(typeof(OriginMid), typeof(IOriginMid).GetMethod("Root")!.ReturnType);
        using var user = new OriginUser();
        Assert.NotNull(user.Root());
        Assert.NotNull(((IOriginMid)user).Root());
    }

    [Fact]
    public void Non_virtual_hiding_across_a_secondary_bases_chain_projects_the_most_derived_declaration()
    {
        var scale = typeof(ScaleHolder).GetMethod("Scale")!;
        Assert.Equal(typeof(int), scale.ReturnType);
        Assert.Equal(3, scale.GetParameters()[0].DefaultValue);

        using var holder = new ScaleHolder();
        Assert.Equal(300, holder.Scale());
        Assert.Equal(200, holder.Scale(2));
        Assert.Equal(300, ((IScaleHiding)holder).Scale());

        var generated = Directory.GetFiles(GeneratedArtifacts.Directory, "*.g.cs").Select(File.ReadAllText).First(static text => text.Contains("partial class ScaleHolder", StringComparison.Ordinal));
        var start = generated.IndexOf("partial class ScaleHolder", StringComparison.Ordinal);
        var end = generated.IndexOf("partial class", start + 1, StringComparison.Ordinal);
        var holderText = generated[start..(end < 0 ? generated.Length : end)];
        Assert.Contains("Scales by the hiding factor.", holderText, StringComparison.Ordinal);
        Assert.Contains("int Scale(int factor = 3)", holderText, StringComparison.Ordinal);
        Assert.DoesNotContain("Scales by the base factor.", holderText, StringComparison.Ordinal);
        Assert.DoesNotContain("factor = 2", holderText, StringComparison.Ordinal);
    }
}
