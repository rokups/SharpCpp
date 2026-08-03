using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Undefaultable;

public sealed class UndefaultableTests
{
    [Fact]
    public void Free_function_overloads_apply_native_defaults_and_explicit_values()
    {
        Assert.Equal(1, E2EBindings.DescribeLevel(9));
        Assert.Equal(0, E2EBindings.DescribeLevel(9, 10, "high"u8));
        Assert.Equal(42, E2EBindings.CheckUndefaultableOnly());
        Assert.Equal(42, E2EBindings.CheckUndefaultableOnly("value"u8));
        Assert.Equal(103, E2EBindings.MixedDefaults());
        Assert.Equal(204, E2EBindings.MixedDefaults(2, "value"u8, 4));

        E2EBindings.ApplyConfig();
        Assert.Equal(103, E2EBindings.GetAppliedConfigValue());
        E2EBindings.ApplyConfig("x"u8, 4);
        Assert.Equal(124, E2EBindings.GetAppliedConfigValue());
    }

    [Fact]
    public void Undefaultable_then_native_default_free_functions_dispatch_every_suffix()
    {
        Assert.Equal(1515, E2EBindings.UndefaultableCascadeValue(1));
        Assert.Equal(1502, E2EBindings.UndefaultableCascadeValue(1, "a"u8, null));
        Assert.Equal(1120, E2EBindings.UndefaultableCascadeValue(1, "a"u8, 2, 3));

        E2EBindings.UndefaultableCascadeVoid(2, "b"u8, null);
        Assert.Equal(2503, E2EBindings.GetAppliedConfigValue());
        E2EBindings.UndefaultableCascadeVoid(2, "b"u8, 3, 4);
        Assert.Equal(2132, E2EBindings.GetAppliedConfigValue());

        using var defaultResult = E2EBindings.SelectUndefaultableResult(3);
        Assert.Equal(3515, defaultResult.GetValue());
        using var explicitResult = E2EBindings.SelectUndefaultableResult(3, "c"u8, 4, 6);
        Assert.Equal(3145, explicitResult.GetValue());
    }

    [Fact]
    public void Constructor_overloads_round_trip_native_defaults()
    {
        using var defaultName = new UndefaultableHandler(7);
        Assert.Equal(7, defaultName.GetId());
        Assert.True(defaultName.GetName().SequenceEqual("unnamed"u8));

        using var explicitName = new UndefaultableHandler(8, "named"u8);
        Assert.Equal(8, explicitName.GetId());
        Assert.True(explicitName.GetName().SequenceEqual("named"u8));

        using var defaultCascade = new UndefaultableHandler(3, 4);
        Assert.Equal(52, defaultCascade.GetId());
        using var namedCascade = new UndefaultableHandler(3, 4, "x"u8, null);
        Assert.Equal(52, namedCascade.GetId());
        using var explicitCascade = new UndefaultableHandler(3, 4, "x"u8, 6, 7);
        Assert.Equal(20, explicitCascade.GetId());
    }

    [Fact]
    public void Instance_and_static_overloads_dispatch_to_native()
    {
        using var handler = new UndefaultableHandler(2);
        Assert.Equal(8, handler.Process(4));
        Assert.Equal(11, handler.Process(4, "tag"u8, 5));
        Assert.Equal(23514, handler.Cascade(3));
        Assert.Equal(23527, handler.Cascade(3, "z"u8, null));
        Assert.Equal(23168, handler.Cascade(3, "z"u8, 4, 6));

        Assert.Equal(-1, UndefaultableHandler.Lookup());
        Assert.Equal(9, UndefaultableHandler.Lookup("key"u8, 9));
        Assert.Equal(1520, UndefaultableHandler.StaticCascade(1));
        Assert.Equal(1120, UndefaultableHandler.StaticCascade(1, "a"u8, 2, 3));

        UndefaultableHandler.ClearCache();
        Assert.Equal(117, UndefaultableHandler.GetCacheValue());
        UndefaultableHandler.ClearCache("z"u8);
        Assert.Equal(122, UndefaultableHandler.GetCacheValue());
        UndefaultableHandler.StaticCascadeVoid(2, "b"u8, null);
        Assert.Equal(2503, UndefaultableHandler.GetCacheValue());
        UndefaultableHandler.StaticCascadeVoid(2, "b"u8, 3, 4);
        Assert.Equal(2132, UndefaultableHandler.GetCacheValue());
    }
}
