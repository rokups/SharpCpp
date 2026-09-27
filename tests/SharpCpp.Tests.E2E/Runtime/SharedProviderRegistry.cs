using SharpCpp.Support;

using Xunit;

namespace SharpCpp.Tests.E2E.Runtime;

public sealed class SharedProviderRegistryTests
{
    [Fact]
    public void Resolve_accepts_the_same_shape_from_multiple_modules()
    {
        var first = new CompatibleProvider("first", "shared-shape");
        var second = new CompatibleProvider("second", "shared-shape");
        SharedProviderRegistry<ICompatibleProvider>.Register<ICompatibleProvider>(first);
        SharedProviderRegistry<ICompatibleProvider>.Register<ICompatibleProvider>(second);

        Assert.Same(first, SharedProviderRegistry<ICompatibleProvider>.Resolve<ICompatibleProvider>());
        Assert.Same(second, SharedProviderRegistry<ICompatibleProvider>.Resolve<ICompatibleProvider>("second"));
    }

    [Fact]
    public void Resolve_rejects_different_shapes_without_a_module()
    {
        SharedProviderRegistry<IIncompatibleProvider>.Register<IIncompatibleProvider>(
            new IncompatibleProvider("first", "first-shape"));
        SharedProviderRegistry<IIncompatibleProvider>.Register<IIncompatibleProvider>(
            new IncompatibleProvider("second", "second-shape"));

        Assert.Throws<InvalidOperationException>(() =>
            SharedProviderRegistry<IIncompatibleProvider>.Resolve<IIncompatibleProvider>());
    }

    private interface ICompatibleProvider : ISharedProvider;

    private sealed record CompatibleProvider(string ModuleId, string ShapeKey) : ICompatibleProvider;

    private interface IIncompatibleProvider : ISharedProvider;

    private sealed record IncompatibleProvider(string ModuleId, string ShapeKey) : IIncompatibleProvider;
}
