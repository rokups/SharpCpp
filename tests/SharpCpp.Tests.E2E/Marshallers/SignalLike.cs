using System.Reflection;

using Signal_like;

using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Marshallers;

public sealed class SignalLikeTests
{
    [Fact]
    public void FieldIsBound()
    {
        using var button = new Button();
        E2EBindings.AddNativeSlot(button);

        // The getter borrows the field inside the owner. Thus the native slot is visible.
        Sig_Int pressed = button.Pressed;
        Assert.Equal(1, pressed.Count());
        Assert.Equal(0, button.Released.Count());
        Assert.Equal(0, button.Clicked.Count());
    }

    [Fact]
    public void NamesAreStable()
    {
        Assert.Equal("Sig_Void", typeof(Button).GetProperty(nameof(Button.Clicked))!.PropertyType.Name);
        Assert.Equal("Sig_Int", typeof(Button).GetProperty(nameof(Button.Pressed))!.PropertyType.Name);
        Assert.Equal("Sig_Point", typeof(Button).GetProperty(nameof(Button.Moved))!.PropertyType.Name);
        Assert.Equal("Sig_IntRef_ByCustomSender", typeof(Button).GetProperty(nameof(Button.Adjusted))!.PropertyType.Name);
        Assert.False(typeof(Sig_Int).IsGenericType);
        Assert.True(typeof(Sig_Int).IsSubclassOf(typeof(global::SharpCpp.Support.NativeObject)));
    }

    [Fact]
    public void SameInstantiationSharesOneClass()
    {
        var pressed = typeof(Button).GetProperty(nameof(Button.Pressed))!.PropertyType;
        Assert.Same(pressed, typeof(Button).GetProperty(nameof(Button.Released))!.PropertyType);
        Assert.Same(pressed, typeof(Slider).GetProperty(nameof(Slider.Changed))!.PropertyType);
        Assert.Single(typeof(Button).Assembly.GetTypes(), static type => type.Name == "Sig_Int");
    }

    [Fact]
    public void FunctionTypeTemplateArgumentHasCallbackParameters()
    {
        // The names come from the typed callback parameters of the function-type argument:
        // a record parameter gives its wrapper name, and a mutable reference adds Ref.
        Assert.NotNull(typeof(Button).Assembly.GetType("Signal_like.Sig_Point"));
        Assert.NotNull(typeof(Button).Assembly.GetType("Signal_like.Sig_IntRef_ByCustomSender"));
        Assert.Null(typeof(Button).Assembly.GetType("Signal_like.Sig`2"));
    }

    [Fact]
    public void SubscribeInvokeUnsubscribe()
    {
        using var button = new Button();
        using var receiver = new Receiver();
        var values = new List<int>();

        button.Pressed.Subscribe(receiver, value => values.Add(value));
        Assert.True(button.Pressed.HasSubscriptions());
        button.Pressed.Invoke(null!, 4);
        button.Pressed.Invoke(null!, 5);
        Assert.Equal([4, 5], values);

        button.Pressed.Unsubscribe(receiver);
        Assert.False(button.Pressed.HasSubscriptions());
        button.Pressed.Invoke(null!, 6);
        Assert.Equal([4, 5], values);
    }

    [Fact]
    public void SubscribeWithSender()
    {
        using var button = new Button();
        using var receiver = new Receiver();
        var senderWasNull = false;
        var received = 0;

        // The default sender is a typed handle. A null sender gives a null argument.
        button.Pressed.SubscribeWithSender(receiver, (sender, value) =>
        {
            senderWasNull = sender is null || sender.IsNull;
            received = value;
        });
        button.Pressed.Invoke(null!, 9);

        Assert.True(senderWasNull);
        Assert.Equal(9, received);
    }

    [Fact]
    public void NonDefaultSender()
    {
        using var button = new Button();
        using var receiver = new Receiver();
        using var sender = new CustomSender { Id = 7 };
        var received = 0;

        button.Sent.SubscribeWithSender(receiver, (CustomSender from, int value) => received = from.Id * 100 + value);
        button.Sent.Invoke(sender, 3);

        Assert.Equal(703, received);
    }

    [Fact]
    public void ReentrantInvokeReturnsFalse()
    {
        using var button = new Button();
        using var receiver = new Receiver();
        var calls = 0;
        Exception? reentrant = null;

        button.Pressed.Subscribe(receiver, value =>
        {
            ++calls;
            try
            {
                button.Pressed.Invoke(null!, value + 1);
            }
            catch (InvalidOperationException exception)
            {
                reentrant = exception;
            }
        });
        button.Pressed.Invoke(null!, 1);

        Assert.Equal(1, calls);
        Assert.IsType<InvalidOperationException>(reentrant);
        // The C++ string literal of throw_on_false keeps its escaped quotes.
        Assert.Equal("The signal \"sig\" is already being invoked.", reentrant.Message);
    }

    [Fact]
    public void PriorityOrdering()
    {
        using var button = new Button();
        using var low = new Receiver();
        using var high = new Receiver();
        var order = new List<string>();

        button.Ordered.Subscribe(low, 1, _ => order.Add("low"));
        button.Ordered.Subscribe(high, 5, _ => order.Add("high"));
        button.Ordered.Invoke(null!, 0);
        Assert.Equal(["high", "low"], order);

        order.Clear();
        button.Weighted.Subscribe(low, 0.5f, _ => order.Add("low"));
        button.Weighted.Subscribe(high, 2.5f, _ => order.Add("high"));
        button.Weighted.Invoke(null!, 0);
        Assert.Equal(["high", "low"], order);

        Assert.Equal("PrioSig_Int", typeof(Button).GetProperty(nameof(Button.Ordered))!.PropertyType.Name);
        Assert.Equal("PrioSig_Int_PriorityFloat", typeof(Button).GetProperty(nameof(Button.Weighted))!.PropertyType.Name);
    }

    [Fact]
    public void BoolRefWritesBack()
    {
        using var button = new Button();
        using var receiver = new Receiver();
        using var sender = new CustomSender { Id = 4 };

        // A mutable reference parameter of a callback is a declared `ref` delegate.
        button.Handled.Subscribe(receiver, (ref bool handled) => handled = true);
        Assert.True(E2EBindings.InvokeHandled(button));

        var value = false;
        button.Handled.Invoke(null!, ref value);
        Assert.True(value);

        button.Adjusted.SubscribeWithSender(receiver, (CustomSender from, ref int adjusted) => adjusted += from.Id);
        Assert.Equal(14, E2EBindings.InvokeAdjusted(button, sender, 10));
    }

    [Fact]
    public void SpanViewSignalArgument()
    {
        using var button = new Button();
        using var receiver = new Receiver();
        byte[]? received = null;

        // A span of const bytes is a ReadOnlySpan<byte> view that is valid only during the slot.
        button.Received.Subscribe(receiver, (System.ReadOnlySpan<byte> data, ref bool handled) =>
        {
            received = data.ToArray();
            handled = data.Length > 0;
        });

        Assert.True(E2EBindings.InvokeReceived(button, 3));
        Assert.Equal(new byte[] { 10, 20, 30 }, received);

        Assert.False(E2EBindings.InvokeReceived(button, 0));
        Assert.Empty(received!);

        var handled = false;
        button.Received.Invoke(null!, new byte[] { 7, 8 }, ref handled);
        Assert.True(handled);
        Assert.Equal(new byte[] { 7, 8 }, received);
    }

    [Fact]
    public void StringViewSignalArgumentWithSender()
    {
        using var button = new Button();
        using var receiver = new Receiver();
        using var sender = new CustomSender { Id = 9 };
        string? label = null;
        int[]? values = null;
        var senderId = 0;

        button.Labelled.SubscribeWithSender(receiver, (from, text, items) =>
        {
            senderId = from.Id;
            label = System.Text.Encoding.UTF8.GetString(text);
            values = items.ToArray();
        });
        E2EBindings.InvokeLabelled(button, sender);

        Assert.Equal(9, senderId);
        Assert.Equal("label", label);
        Assert.Equal(new[] { 3, 4 }, values);
    }

    [Fact]
    public void ZeroArgSignal()
    {
        using var button = new Button();
        using var receiver = new Receiver();
        var calls = 0;

        button.Clicked.Subscribe(receiver, () => ++calls);
        button.Clicked.Invoke(null!);
        button.Clicked.Invoke(null!);

        Assert.Equal(2, calls);
        Assert.Equal(1, button.Clicked.Count());
    }

    [Fact]
    public void StructArgumentPassesByValue()
    {
        using var button = new Button();
        using var receiver = new Receiver();
        using var point = new Point { X = 2, Y = 3 };
        var byValue = 0;
        var byConstReference = 0;

        button.Dropped.Subscribe(receiver, received => byValue = received.X * 10 + received.Y);
        button.Moved.Subscribe(receiver, received => byConstReference = received.X * 10 + received.Y);
        button.Dropped.Invoke(null!, point);
        button.Moved.Invoke(null!, point);

        Assert.Equal(23, byValue);
        Assert.Equal(23, byConstReference);
    }

    [Fact]
    public void FlattenedFieldsAreReadOnly()
    {
        // The native setter moves the signal. Thus the binding has no setter for a flattened field.
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        var properties = typeof(Button).GetProperties()
            .Where(static property => property.PropertyType.Name.StartsWith("Sig_", StringComparison.Ordinal) ||
                property.PropertyType.Name.StartsWith("PrioSig_", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(12, properties.Length);
        Assert.All(properties, static property => Assert.False(property.CanWrite, property.Name));

        var setters = typeof(Button).GetNestedTypes(all)
            .SelectMany(type => type.GetMethods(all))
            .Where(method => properties.Any(property => method.Name.Contains("Set", StringComparison.Ordinal) &&
                method.Name.Contains(property.Name, StringComparison.Ordinal)))
            .Select(static method => method.Name);
        Assert.Empty(setters);
    }

    [Fact]
    public void ThrowOnFalseNeedsBoolResult()
    {
        // The generator ignores a throw_on_false member that does not return bool.
        Assert.Null(typeof(Sig_Int).GetMethod("CountOrFail"));
        Assert.NotNull(typeof(Sig_Int).GetMethod("Invoke"));
    }
}
