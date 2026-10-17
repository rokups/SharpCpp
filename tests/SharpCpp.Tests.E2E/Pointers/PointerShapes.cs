using Pointer_shapes;

using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Pointers;

public sealed partial class PointerShapeTests
{
    private sealed partial class ManagedRenderer : Renderer
    {
        public override unsafe int Render(nint indices, Value translation, int count)
        {
            var values = new ReadOnlySpan<int>((void*)indices, count);
            var sum = 0;
            foreach (var value in values)
                sum += value;
            return sum + translation.Data;
        }
    }

    [Fact]
    public void Record_returns_by_value_pointer_and_alias_pointer()
    {
        using var value = E2EBindings.GetPayload();
        Assert.Equal(1, value.Value);
        Assert.Equal(2, E2EBindings.GetPayloadPointer().Value);
        Assert.Equal(2, E2EBindings.GetPayloadAliasPointer().Value);
        Assert.Equal(typeof(nint), typeof(E2EBindings).GetMethod(nameof(E2EBindings.GetPayloadPointerPointer))!.ReturnType);
        Assert.Equal(2, Payload.CreateManagedWrapper(System.Runtime.InteropServices.Marshal.ReadIntPtr(E2EBindings.GetPayloadPointerPointer()), ownsHandle: false).Value);
        Assert.Equal(2, E2EBindings.ReadPayloadPointerPointer(out var pointed));
        Assert.Equal(2, pointed.Value);
    }

    [Fact]
    public void Parallel_record_and_primitive_arrays_are_spans_and_a_lone_pointer_is_a_value()
    {
        using var context = E2EBindings.CreateDeviceContext();
        using var first = new Pointer_shapes.Buffer { Id = 2 };
        using var second = new Pointer_shapes.Buffer { Id = 3 };
        Assert.Equal(1UL + 2 * 10 + 3 * 100, context.SetBuffers(1, 2, [first, second], [10UL, 100UL]));
        Assert.Equal(5UL, context.SetValue(5));
    }

    [Fact]
    public void Record_declared_in_a_record_scope_is_a_nested_wrapper()
    {
        using var owner = E2EBindings.CreateOwner(17);
        Owner.Value item = owner.Item;
        Assert.Equal(17, item.Data);
        Assert.Equal(typeof(Owner), typeof(Owner.Value).DeclaringType);
    }

    [Fact]
    public void Director_callbacks_receive_primitive_pointers_raw_and_records_as_wrappers()
    {
        using var renderer = new ManagedRenderer();
        Assert.Equal(16, E2EBindings.RenderWith(renderer));
    }
}
