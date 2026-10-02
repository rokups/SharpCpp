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

    [Fact]
    public void Non_virtual_hiding_of_a_secondary_base_method_with_a_different_return_type_implements_the_base_interface_explicitly()
    {
        using var meter = new RateMeter();
        using var scaler = new RateScaler();
        using var panel = new RatePanel();
        AssertExplicitRateImplementation(meter, meter);
        AssertExplicitRateImplementation(scaler, scaler);
        AssertExplicitRateImplementation(panel, panel);

        Assert.Equal(typeof(double), typeof(RateMeter).GetMethod("Rate")!.ReturnType);
        Assert.Equal(typeof(double), typeof(RateScaler).GetMethod("Rate")!.ReturnType);
        Assert.Equal(typeof(double), typeof(RatePanel).GetMethod("Rate")!.ReturnType);
        Assert.Null(typeof(IRateScaler).GetMethod("Rate", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly));
    }

    private static void AssertExplicitRateImplementation(IRateSource source, object instance)
    {
        var type = instance.GetType();
        Assert.Equal(2.5, (double)type.GetMethod("Rate")!.Invoke(instance, null)!);
        Assert.Equal(5, source.Rate());
        var map = type.GetInterfaceMap(typeof(IRateSource));
        var index = Array.FindIndex(map.InterfaceMethods, static method => method.Name == "Rate");
        Assert.False(map.TargetMethods[index].IsPublic);
        Assert.Equal(5, SharpCpp.Tests.E2E.Generated.E2EBindings.RateViaSource(source));
    }

    [Fact]
    public void A_covariant_override_of_a_rendered_interface_member_on_a_secondary_bases_primary_chain_dispatches_through_the_view_interface()
    {
        using var user = new TrailUser();
        Assert.Equal(typeof(TrailMid), typeof(TrailUser).GetMethod("Head")!.ReturnType);
        // The covariant result is the trail_mid subobject, wrapped as a TrailMid view rather than the TrailUser.
        Assert.Equal(2, user.Head().MidTag());
        Assert.Equal(2, ((ITrailMid)user).Head().MidTag());
        Assert.Equal(1, ((ITrailMid)user).BaseTag());
        Assert.Equal(2, ((ITrailMid)user).MidTag());

        // Known gap (PROGRESS.md, tracker RB-33): a class reaching a rendered interface only through a secondary
        // base's primary chain does not implement it. Flip these when that is bound.
        Assert.False(typeof(ITrailBase).IsAssignableFrom(typeof(TrailUser)));
        Assert.False(typeof(ITrailBase).IsAssignableFrom(typeof(ITrailMid)));

        using var mid = new TrailMid();
        Assert.Same(mid, mid.Head());
        Assert.Same(mid, ((ITrailBase)mid).Head());
        Assert.Same(mid, SharpCpp.Tests.E2E.Generated.E2EBindings.TrailHeadViaBase(mid));

        using var direct = new TrailDirect();
        // trail_base is a secondary base of trail_direct, so the result is its subobject wrapped as a TrailBase view.
        Assert.Equal(1, direct.Head().BaseTag());
        Assert.Equal(1, ((ITrailBase)direct).Head().BaseTag());
        Assert.Equal(1, SharpCpp.Tests.E2E.Generated.E2EBindings.TrailHeadViaBase(direct).BaseTag());
        Assert.Same(direct, SharpCpp.Tests.E2E.Generated.E2EBindings.TrailHeadViaBase(direct));
    }

    [Fact]
    public void Members_reached_through_a_record_that_uses_virtual_base_inheritance_are_not_projected()
    {
        // ridge_left and ridge_right are ignored for their virtual base, so nothing is projected through them
        // (a clone of the hidden ridge_root::level would otherwise reach ridge_user).
        foreach (var type in new[] { typeof(RidgeUser), typeof(RidgeJoin), typeof(IRidgeJoin) })
        {
            Assert.Null(type.GetMethod("Level"));
            Assert.Null(type.GetMethod("LeftTag"));
            Assert.Null(type.GetMethod("RootOnly"));
        }

        // ridge_join's interface has no rendered members, so ridge_user is not passed as a ridge_join.
        using var join = new RidgeJoin();
        Assert.Equal(2, SharpCpp.Tests.E2E.Generated.E2EBindings.RidgeLevelViaJoin(join));

        // The skip leaves ridge_join's interface without members, so a class reaching it as a secondary base does not implement it.
        Assert.False(typeof(IRidgeJoin).IsAssignableFrom(typeof(RidgeUser)));

        using var root = new RidgeRoot();
        Assert.Equal(1, root.Level());
        Assert.Equal(4, root.RootOnly());
    }

    [Fact]
    public void Base_overloads_hidden_by_another_parameter_list_are_projected_and_dispatched_through_their_declaring_record()
    {
        // span_mid::span(int, int) hides span_base::span() and span(int) for C++ lookup through span_mid;
        // class inheritance still exposes them on SpanMid, so the projected surface of SpanUser matches it.
        using var user = new SpanUser();
        Assert.Equal(1, user.Span());
        Assert.Equal(5, user.Span(5));
        Assert.Equal(5, user.Span(2, 3));

        var view = (ISpanMid)user;
        Assert.Equal(1, view.Span());
        Assert.Equal(5, view.Span(5));
        Assert.Equal(5, view.Span(2, 3));

        using var mid = new SpanMid();
        Assert.Equal(1, mid.Span());
        Assert.Equal(5, mid.Span(5));
        Assert.Equal(5, mid.Span(2, 3));
    }
}
