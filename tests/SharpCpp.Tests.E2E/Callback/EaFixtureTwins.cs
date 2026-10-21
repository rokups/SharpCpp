using System.Collections.Generic;

using Config_breadth2;

using Container_aliases;

using Phase2_breadth;

using Signal_like;

using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Callback;

// The ea::function forms of std::function fixtures that live in other areas: templates,
// records, declarations, aliases, defaults, arrays and signals.
public sealed class EaFixtureTwinTests
{
    [Fact]
    public void Container_named_only_by_an_ea_callback_signature_is_instantiated()
    {
        Assert.Equal(30, E2EBindings.EaInvokeWithMap(static values => values[1] + values[2]));
    }

    [Fact]
    public void Generic_function_instantiated_with_an_ea_function_dispatches_to_the_delegate()
    {
        Assert.Equal(41, E2EBindings.EaGenericInvokeCallback<System.Func<int, int>>(value => value * 2, 20));
    }

    [Fact]
    public void Ea_function_parameter_projects_to_a_delegate_of_an_inherited_method()
    {
        using var derived = new DerivedCounter(10, 2);
        Assert.Equal(42, derived.EaProjectedCallback(value => value * 2, 20));
    }

    [Fact]
    public void Ea_callback_reference_parameter_is_bound()
    {
        Assert.Equal(42, E2EBindings.EaInvokeBreadthCallbackReference(value => value + 1, 40));
    }

    [Fact]
    public void Ea_callback_with_a_projected_array_parameter_is_not_bound()
    {
        Assert.Null(typeof(E2EBindings).GetMethod("EaInvokePaddedPairCallback"));
    }

    [Fact]
    public void Ea_function_alias_of_a_forward_declared_type_round_trips()
    {
        using var value = new Phase2Value(40);
        Assert.Equal(42, E2EBindings.Phase2InvokeEaAliasCallback(item => item.ReadConfigured() + 1, value));
    }

    [Fact]
    public void Ea_function_map_alias_callback_arguments_are_wrapped()
    {
        using var hub = new DataHub();
        using var receiver = new Container_aliases.Object(4);
        Assert.Equal(74, hub.EaSend(receiver, (target, hash, data) => target.GetId() + data[hash].GetValue()));
    }

    [Fact]
    public void Ea_function_configured_nested_alias_callback_round_trips()
    {
        using var value = new Config2Value(40);
        using var surface = E2EBindings.MakeConfig2Surface(value, 2);
        Assert.Equal(85, E2EBindings.InvokeConfig2NestedEaCallback(
            (configured, item) => configured.ReadConfigured() + item.GetValue(),
            surface,
            value));
    }

    [Fact]
    public void Ea_surface_discovery_callbacks_round_trip_native_values()
    {
        Assert.Equal(39, E2EBindings.EaInvokeSurfaceFixedDiscoveryCallback(
            (fixedValues, sharedValues) =>
            {
                fixedValues.AddToFirst(sharedValues.Sum() % 10);
                return fixedValues;
            },
            2));

        Assert.Equal(41, E2EBindings.EaInvokeSurfaceDynamicDiscoveryCallback(
            (sharedValues, providerValues) =>
            {
                sharedValues.AddToFirst(providerValues.Sum() % 10);
                return sharedValues;
            },
            3));
    }

    [Fact]
    public void Unsupported_ea_callback_shapes_of_the_array_fixture_are_not_bound()
    {
        foreach (var name in new[]
        {
            "EaUnsupportedCallbackReturn",
            "EaUnsupportedCallbackReference",
            "EaUnsupportedDelegateName",
            "EaUnsupportedExternalDelegateName",
            "EaUnsupportedDuplicateDelegateA",
            "EaUnsupportedDuplicateDelegateB"
        })
        {
            Assert.Null(typeof(E2EBindings).GetMethod(name));
        }
    }

    [Fact]
    public void Ea_signal_subscribe_invoke_unsubscribe()
    {
        using var button = new EaButton();
        using var receiver = new Receiver();
        var values = new List<int>();

        button.Pressed.Subscribe(receiver, value => values.Add(value));
        Assert.True(button.Pressed.HasSubscriptions());
        Assert.Equal(1, button.Pressed.Count());
        button.Pressed.Invoke(null!, 4);
        button.Pressed.Invoke(null!, 5);
        Assert.Equal([4, 5], values);

        button.Pressed.Unsubscribe(receiver);
        Assert.False(button.Pressed.HasSubscriptions());

        var calls = 0;
        button.Clicked.Subscribe(receiver, () => ++calls);
        button.Clicked.Invoke(null!);
        Assert.Equal(1, calls);

        E2EBindings.EaAddNativeSlot(button);
        Assert.Equal(1, button.Pressed.Count());
    }

    [Fact]
    public void Ea_signal_passes_structs_sender_and_ref_values()
    {
        using var button = new EaButton();
        using var receiver = new Receiver();
        using var point = new Point { X = 2, Y = 3 };
        using var sender = new CustomSender { Id = 4 };
        var byValue = 0;
        var byConstReference = 0;

        button.Dropped.Subscribe(receiver, received => byValue = received.X * 10 + received.Y);
        button.Moved.Subscribe(receiver, received => byConstReference = received.X * 10 + received.Y);
        button.Dropped.Invoke(null!, point);
        button.Moved.Invoke(null!, point);
        Assert.Equal(23, byValue);
        Assert.Equal(23, byConstReference);

        button.Handled.Subscribe(receiver, (ref bool handled) => handled = true);
        Assert.True(E2EBindings.EaInvokeHandled(button));

        button.Adjusted.SubscribeWithSender(receiver, (CustomSender from, ref int adjusted) => adjusted += from.Id);
        Assert.Equal(14, E2EBindings.EaInvokeAdjusted(button, sender, 10));

        var senderId = 0;
        button.Sent.SubscribeWithSender(receiver, (CustomSender from, int value) => senderId = from.Id * 100 + value);
        button.Sent.Invoke(sender, 5);
        Assert.Equal(405, senderId);
    }

    [Fact]
    public void Ea_signal_passes_a_span_view_with_a_ref_flag()
    {
        using var button = new EaButton();
        using var receiver = new Receiver();
        byte[]? received = null;

        button.Received.Subscribe(receiver, (System.ReadOnlySpan<byte> data, ref bool handled) =>
        {
            received = data.ToArray();
            handled = data.Length > 0;
        });

        Assert.True(E2EBindings.EaInvokeReceived(button, 3));
        Assert.Equal(new byte[] { 10, 20, 30 }, received);
        Assert.False(E2EBindings.EaInvokeReceived(button, 0));
        Assert.Empty(received!);
    }
}
