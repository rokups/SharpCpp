using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Arrays;

public sealed class SurfaceTests
{
    [Fact]
    public void Custom_standard_library_fixed_arrays_round_trip_values_and_handles()
    {
        using var returned = E2EBindings.MakeSurfaceFixedArray(4);
        Assert.Equal(new[] { 4, 5, 6 }, returned.ToArray());
        Assert.Equal(15, E2EBindings.SumSurfaceFixedArray(returned));

        using var fromSpan = new global::E2EStd.Array<int>(new[] { 7, 8, 9 });
        Assert.Equal(24, E2EBindings.SumSurfaceFixedArray(fromSpan));

        using var first = new SurfacePayload(11);
        using var second = new SurfacePayload(13);
        using var handles = new global::E2EStd.Array<SurfacePayload>([first, second]);
        Assert.Same(first, handles[0]);
        Assert.Equal(24, E2EBindings.SumSurfaceFixedHandles(handles));
    }

    [Fact]
    public void C_array_references_bind_as_native_arrays_and_round_trip_values()
    {
        using var input = new global::SharpCpp.NativeArray<int>((IEnumerable<int>)new[] { 2, 3, 4 });
        Assert.Equal(9, E2EBindings.SumCArrayRef(input));

        using var output = new global::SharpCpp.NativeArray<uint>(3);
        E2EBindings.FillCArrayRef(output, 10);
        Assert.Equal(new uint[] { 10, 11, 12 }, output.ToArray());

        using var tooShort = new global::SharpCpp.NativeArray<int>((IEnumerable<int>)new[] { 1, 2 });
        Assert.Throws<ArgumentException>(() => E2EBindings.SumCArrayRef(tooShort));
        Assert.Throws<ArgumentNullException>(() => E2EBindings.SumCArrayRef(null!));

        using var first = new SurfacePayload(5);
        using var second = new SurfacePayload(6);
        using var handles = new global::SharpCpp.NativeArray<SurfacePayload>(
            (IEnumerable<SurfacePayload>)new[] { first, second });
        Assert.Equal(11, E2EBindings.SumCArrayRefHandles(handles));
    }

    [Fact]
    public void Custom_standard_library_shared_arrays_round_trip_values_and_handles()
    {
        using var returned = E2EBindings.MakeSurfaceSharedArray(3);
        Assert.False(returned.HasKnownLength);
        Assert.Equal(new[] { 3, 4, 5 }, returned.ToArray(3));
        Assert.Equal(12, E2EBindings.SumSurfaceSharedArray(returned));

        using var values = new global::E2EStd.SharedArray<int>((IEnumerable<int>)new[] { 5, 7, 9 });
        Assert.True(values.HasKnownLength);
        Assert.Equal(3, values.Length);
        values[(nuint)1] = 8;
        Assert.Equal(8, values[(nuint)1]);
        Assert.Equal(22, E2EBindings.SumSurfaceSharedArray(values));
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = values[-1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => values[-1] = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => values.ToArray(-1));

        using var first = new SurfacePayload(17);
        using var second = new SurfacePayload(19);
        using var handles = new global::E2EStd.SharedArray<SurfacePayload>(
            new ReadOnlySpan<SurfacePayload>([first, second]));
        Assert.Same(second, handles[1]);
        Assert.Equal(36, E2EBindings.SumSurfaceSharedHandles(handles));
    }

    [Fact]
    public void Shared_provider_dynamic_arrays_round_trip_values_and_handles()
    {
        using var returned = E2EBindings.MakeSurfaceProviderArray(3);
        Assert.NotEqual(0, returned.Handle);
        Assert.True(returned.OwnsHandle);
        Assert.False(returned.IsNull);
        Assert.NotNull(returned.Provider);
        Assert.False(returned.HasKnownLength);
        Assert.Equal(new[] { 3, 4, 5 }, returned.ToArray(3));
        Assert.Equal(12, E2EBindings.SumSurfaceProviderArray(returned));

        using var module = global::Std.Infrastructure.ProviderRegistry.PushModule("SharpCpp.Tests.E2E");
        Assert.Equal("SharpCpp.Tests.E2E", global::Std.Infrastructure.ProviderRegistry.CurrentModuleId);
        using var values = new global::E2EStd.ProviderArray<int>((IEnumerable<int>)new[] { 5, 7, 9 });
        values[(nuint)1] = 8;
        Assert.Equal(8, values[(nuint)1]);
        Assert.Equal(22, E2EBindings.SumSurfaceProviderArray(values));

        using var first = new SurfacePayload(17);
        using var second = new SurfacePayload(19);
        using var handles = new global::E2EStd.ProviderArray<SurfacePayload>(
            new ReadOnlySpan<SurfacePayload>([first, second]));
        Assert.Same(second, handles[1]);
        Assert.Equal(36, E2EBindings.SumSurfaceProviderHandles(handles));
    }

    [Fact]
    public void Array_discovery_through_aliases_fields_and_callbacks_round_trips_native_values()
    {
        using var host = new SurfaceArrayDiscoveryHost(2);
        Assert.Equal(39, host.Sum());

        Assert.Equal(39, E2EBindings.InvokeSurfaceFixedDiscoveryCallback(
            (fixedValues, sharedValues) =>
            {
                fixedValues.AddToFirst(sharedValues.Sum() % 10);
                return fixedValues;
            },
            2));

        Assert.Equal(41, E2EBindings.InvokeSurfaceDynamicDiscoveryCallback(
            (sharedValues, providerValues) =>
            {
                sharedValues.AddToFirst(providerValues.Sum() % 10);
                return sharedValues;
            },
            3));
    }

    [Fact]
    public void Dense_surface_compiles_and_round_trips_supported_declarations()
    {
        Assert.Equal(42, E2EBindings.CoverageSurfaceSentinel(41));
        Assert.Equal(9, E2EBindings.CoverageLinkageAdd(4, 5));

        using var linkage = E2EBindings.CreateCoverageLinkageBox(17);
        Assert.Equal(17, E2EBindings.ReadCoverageLinkageBox(linkage));

        using var derived = E2EBindings.CreateUsableSurfaceDerived(23);
        Assert.Equal(23, derived.GetValue());
    }

    [Fact]
    public void Generic_free_functions_round_trip_primitive_and_handle_instantiations()
    {
        Assert.Equal(12, E2EBindings.SurfaceIdentity(12));
        Assert.Equal(12, E2EBindings.SurfaceMeasure(12));
        E2EBindings.SurfaceConsume(12);

        using var payload = new SurfacePayload(31);
        Assert.Same(payload, E2EBindings.SurfaceIdentity(payload));
        Assert.Equal(31, E2EBindings.SurfaceMeasure(payload));
        E2EBindings.SurfaceConsume(payload);
    }

    [Fact]
    public void Generic_method_on_concrete_record_dispatches_to_native_instantiations()
    {
        using var host = new SurfaceGenericHost(3);

        Assert.Equal(8, host.Project(5));
        Assert.Equal(5.5f, host.Project(2.5f));
        Assert.Equal(10, host.Project<int>(7));
    }

    [Fact]
    public void Flattened_and_colliding_fields_round_trip_through_generated_accessors()
    {
        using var flattened = new FlattenedOwner(1);
        Assert.Equal(21, flattened.Total());
        Assert.Equal(1, flattened.StateBackX);
        Assert.Equal(2, flattened.StateBackY);
        Assert.Equal(3, flattened.AnonymousDataValue);
        Assert.Equal(4, flattened.AnonymousDataInnerDeep);
        Assert.Equal(5, flattened.ChoiceNumber);
        Assert.Equal(6, flattened.Sentinel);

        flattened.StateBackX = 10;
        flattened.AnonymousDataInnerDeep = 20;
        flattened.ChoiceNumber = 30;
        Assert.Equal(71, flattened.Total());

        using var collision = E2EBindings.CreateCollisionSurface(4, 6);
        Assert.Equal(4, collision.Value());
        Assert.Equal(6, collision.Amount());
        Assert.Equal(4, collision.ValueValue);
        Assert.Equal(6, collision.AmountValue);
        Assert.Equal(7, CollisionSurface.Limit());
        Assert.Equal(7, CollisionSurface.LimitConstant);

        collision.ValueValue = 8;
        collision.AmountValue = 9;
        Assert.Equal(8, collision.Value());
        Assert.Equal(9, collision.Amount());
    }
}
