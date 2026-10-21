using System.Collections.Generic;

using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Callback;

// The ea::function forms of the callback shape tests (Callback.cs, CallbackShapes.cs,
// AliasCallback.cs and ManagedCallback.cs).
public sealed partial class EaCallbackTests
{
    [Fact]
    public void Shared_wrapper_with_non_type_template_argument_crosses()
    {
        Assert.Equal(7, E2EBindings.EaInvokeArrayCallback(values => values[0] + values[1]));
    }

    [Fact]
    public void Object_reference_callback_receives_borrowed_wrapper()
    {
        Assert.Equal(42, E2EBindings.EaInvokeObjectReferenceCallback(box => box.SetValue(42), 1));
    }

    [Fact]
    public unsafe void Primitive_pointer_callback_receives_raw_pointer()
    {
        Assert.Equal(42, E2EBindings.EaInvokePrimitivePointerCallback(pointer => *(int*)pointer.ToPointer() = 42, 1));
    }

    [Fact]
    public void Delegate_attribute_projects_ea_function_parameter_to_named_and_external_delegates()
    {
        var observed = 0;
        E2EBindings.EaValueCallback callback = value => observed = value;
        E2EBindings.EaInvokeNamedDelegateCallback(callback, 42);
        Assert.Equal(42, observed);

        ExternalValueCallback external = value => value * 2;
        Assert.Equal(42, E2EBindings.EaInvokeExternalDelegateCallback(external, 21));
    }

    [Fact]
    public void Ref_parameter_with_a_result_and_span_view_cross()
    {
        var result = E2EBindings.EaInvokeIntRefCallback((ref int value) =>
        {
            value += 4;
            return 7;
        }, 3);
        Assert.Equal(707, result);

        float[]? received = null;
        var handled = E2EBindings.EaInvokeFloatSpanRefCallback((System.ReadOnlySpan<float> values, ref bool flag) =>
        {
            received = values.ToArray();
            flag = true;
        });
        Assert.True(handled);
        Assert.Equal(new[] { 1.5f, 2.5f, 3.5f }, received);
    }

    [Fact]
    public void Noexcept_native_function_takes_an_ea_function()
    {
        Assert.Equal(42, E2EBindings.EaInvokeNoexceptFunctionCallback(value => value + 1, 41));
        Assert.Equal(42, E2EBindings.EaInvokeNoexceptFunctionReferenceCallback(value => value * 2, 21));
    }

    [Fact]
    public void Noexcept_signature_of_an_ea_function_is_bound()
    {
        Assert.Equal(42, E2EBindings.EaInvokeNoexceptSignatureCallback(value => value + 1, 41));
        Assert.Equal(42, E2EBindings.EaInvokeNoexceptSignatureRefCallback((ref int value) => value += 41, 1));
        Assert.Equal(typeof(System.Func<int, int>), typeof(E2EBindings).GetMethod("EaInvokeNoexceptSignatureCallback")!.GetParameters()[0].ParameterType);
    }

    [Fact]
    public void Void_and_value_callbacks_without_and_with_defaults()
    {
        var calls = new List<(int, float)>();
        Assert.Equal(2f, E2EBindings.EaForEach((index, value) => calls.Add((index, value))));
        Assert.Equal([(1, 0.5f), (2, 1.5f)], calls);

        var notified = 0;
        Assert.Equal(1, E2EBindings.EaNotify(() => ++notified));
        Assert.Equal(1, notified);

        Assert.Equal(99u, E2EBindings.EaInvokeOptional());
        Assert.Equal(99u, E2EBindings.EaInvokeOptional(null));
        Assert.Equal(5u, E2EBindings.EaInvokeOptional(() => 5u));

        Assert.Equal(99, E2EBindings.EaInvokeOptionalCallback());
        Assert.Equal(99, E2EBindings.EaInvokeOptionalCallback(null));
        Assert.Equal(5, E2EBindings.EaInvokeOptionalCallback(() => 5));
    }

    [Fact]
    public void Configured_value_struct_callback_arguments_cross()
    {
        Assert.Equal(34, E2EBindings.EaUsePointCallback(static point => point.X * 10 + point.Y));
        Assert.Equal(56, E2EBindings.EaUsePointValueCallback(static point => point.X * 10 + point.Y));
        Assert.Equal(6006, E2EBindings.EaInvokePointRefCallback((ref CallbackPoint2 point) => point = new CallbackPoint2(point.X * 2, point.Y + 1), 3, 5));
    }

    [Fact]
    public void Aliased_ea_function_callback_handles_round_trip()
    {
        using var first = new AliasCallbackPayload(19);
        Assert.Same(first, E2EBindings.EaInvokeAliasCallback(value => value, first));
    }

    [Fact]
    public void Unsupported_ea_callback_shapes_are_not_bound()
    {
        Assert.Null(typeof(E2EBindings).GetMethod("EaInvokeViewReturnCallback"));
        Assert.Null(typeof(E2EBindings).GetMethod("EaInvokeMutableSpanCallback"));
    }
}
