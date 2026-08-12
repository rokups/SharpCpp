using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Records;

public sealed class GenericTests
{
    [Fact]
    public void Generic_free_function_dispatches_to_supported_instantiations()
    {
        Assert.Equal(7, E2EBindings.GenericAdd(3, 4));
        Assert.Equal(7.5f, E2EBindings.GenericAdd(3.0f, 4.5f));
        Assert.Equal(9.0, E2EBindings.GenericAdd(4.5, 4.5));
        Assert.Equal(9, E2EBindings.GenericAddDefault(5, 4));
        Assert.Equal(3.5, E2EBindings.GenericAddDefault(2.5, 1.0));
        Assert.Equal(7.5, E2EBindings.GenericMixedAdd(3, 4.5f));
        Assert.Equal(9.5, E2EBindings.GenericMixedAdd(7.5, 2));
        Assert.Equal(41, E2EBindings.GenericInvokeCallback<Func<int, int>>(value => value * 2, 20));

        using var values = new global::Std.Vector<int>((IEnumerable<int>)new[] { 11, 14, 17 });
        using var copiedValues = E2EBindings.GenericHandleIdentity(values);
        Assert.Equal(new[] { 11, 14, 17 }, copiedValues.ToArray());
    }

    [Fact]
    public void Free_functions_can_return_concrete_generic_wrappers()
    {
        var intBox = E2EBindings.CreateIntBox(10);
        var floatBox = E2EBindings.CreateFloatBox(1.5f);

        Assert.Equal(10, intBox.GetValue());
        Assert.Equal(1.5f, floatBox.GetValue());
    }

    [Fact]
    public void Generic_const_reference_methods_preserve_concrete_and_generic_abi()
    {
        using var host = E2EBindings.CreateGenericMethodHost();

        Assert.True(host.ConstReferenceIdentity(true));
        Assert.False(host.ConstReferenceIdentity<bool>(false));
        Assert.Equal(41, host.ConstReferenceIdentity(41));
        Assert.Equal(73, host.ConstReferenceIdentity<int>(73));
    }

    [Fact]
    public void Generic_record_constructor_and_instance_methods_dispatch_to_supported_instantiations()
    {
        var box = new BoxOf<int>(12);

        Assert.Equal(12, box.GetValue());

        box.SetValue(27);

        Assert.Equal(27, box.GetValue());

        using var pair = new PairOf<int, float>(7, 2.5f);
        Assert.Equal(7, pair.GetFirst());
        Assert.Equal(2.5f, pair.GetSecond());
        Assert.Equal(18, pair.Combine(2, 1.5f));
        Assert.Equal(9.5, PairOf<int, float>.Sum(7.5, 2));

        using var createdPair = E2EBindings.CreateGenericPair(11, 3.5f);
        Assert.Equal(11, createdPair.GetFirst());
        Assert.Equal(3.5f, createdPair.GetSecond());
    }

    [Fact]
    public void Generic_record_static_methods_dispatch_to_supported_instantiations()
    {
        var box = BoxOf<float>.Create(2.5f);

        Assert.Equal(2.5f, box.GetValue());
    }

    [Fact]
    public void Generic_record_can_use_handle_typed_instantiation()
    {
        var payload = new GenericPayload(7);
        var replacement = new GenericPayload(9);
        var constructed = new HandleBoxOf<GenericPayload>(payload);
        var created = E2EBindings.CreateGenericPayloadBox(payload);

        Assert.Same(payload, constructed.GetValue());
        Assert.Same(payload, created.GetValue());
        Assert.Same(payload, E2EBindings.GenericHandleIdentity(payload));

        constructed.SetValue(replacement);

        Assert.Same(replacement, constructed.GetValue());
        Assert.Equal(9, constructed.GetValue().GetValue());

        created.Dispose();
        constructed.Dispose();
        replacement.Dispose();
        payload.Dispose();
    }

    [Fact]
    public void Explicit_instantiation_metadata_can_expand_generic_record_surface()
    {
        var constructed = new BoxOf<double>(2.5);
        var created = BoxOf<double>.Create(4.5);

        Assert.Equal(2.5, constructed.GetValue());
        Assert.Equal(4.5, created.GetValue());
    }

    [Fact]
    public void Generic_method_inside_generic_record_dispatches_to_concrete_instantiation()
    {
        var box = new BoxOf<int>(12);

        var result = box.Project(2);

        Assert.Equal(24, result);
        Assert.Equal(25, box.ProjectDefault<int>());
        Assert.Equal(37, box.ProjectDefault(3));
        Assert.Equal(41, box.ProjectDefault(3, 5));
        Assert.Equal(7, BoxOf<int>.Combine(4));
        Assert.Equal(9, BoxOf<int>.Combine(4, 5));
        Assert.Equal(12, box.GetReference());

        using var clone = box.Clone();
        Assert.Equal(12, clone.GetValue());
    }

    [Fact]
    public void Concrete_record_can_implement_abstract_generic_base()
    {
        using var sink = new GenericIntSink();

        sink.Set(42);

        Assert.Equal(42, sink.Get());
    }

}
