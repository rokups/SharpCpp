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

    [Fact]
    public void Register_rejects_a_second_provider_for_the_same_module_and_shape()
    {
        var provider = new DuplicateProvider("module", "shape");
        SharedProviderRegistry<IDuplicateProvider>.Register<IDuplicateProvider>(provider);
        SharedProviderRegistry<IDuplicateProvider>.Register<IDuplicateProvider>(provider);

        Assert.Throws<InvalidOperationException>(() =>
            SharedProviderRegistry<IDuplicateProvider>.Register<IDuplicateProvider>(new DuplicateProvider("module", "shape")));
    }

    [Fact]
    public void Resolve_rejects_missing_and_ambiguous_module_providers()
    {
        Assert.Throws<InvalidOperationException>(() =>
            SharedProviderRegistry<IAmbiguousProvider>.Resolve<IAmbiguousProvider>("module"));

        SharedProviderRegistry<IAmbiguousProvider>.Register<IAmbiguousProvider>(new AmbiguousProvider("module", "first-shape"));
        SharedProviderRegistry<IAmbiguousProvider>.Register<IAmbiguousProvider>(new AmbiguousProvider("module", "second-shape"));

        Assert.Throws<InvalidOperationException>(() =>
            SharedProviderRegistry<IAmbiguousProvider>.Resolve<IAmbiguousProvider>("module"));
        Assert.Equal("second-shape", SharedProviderRegistry<IAmbiguousProvider>.Resolve<IAmbiguousProvider>("module", "second-shape").ShapeKey);

        using (SharedProviderRegistry<IAmbiguousProvider>.PushModule("other"))
        {
            Assert.Equal("other", SharedProviderRegistry<IAmbiguousProvider>.CurrentModuleId);
        }
    }

    private interface IDuplicateProvider : ISharedProvider;

    private sealed record DuplicateProvider(string ModuleId, string ShapeKey) : IDuplicateProvider;

    private interface IAmbiguousProvider : ISharedProvider;

    private sealed record AmbiguousProvider(string ModuleId, string ShapeKey) : IAmbiguousProvider;

    private interface ICompatibleProvider : ISharedProvider;

    private sealed record CompatibleProvider(string ModuleId, string ShapeKey) : ICompatibleProvider;

    private interface IIncompatibleProvider : ISharedProvider;

    private sealed record IncompatibleProvider(string ModuleId, string ShapeKey) : IIncompatibleProvider;
}
