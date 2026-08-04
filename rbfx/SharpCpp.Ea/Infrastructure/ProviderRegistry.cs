using System;

namespace Ea.Infrastructure;

/// <summary>Provider registry used by shared <c>Ea.*</c> wrappers.</summary>
public static class ProviderRegistry
{
    public static string? CurrentModuleId => global::SharpCpp.Support.SharedProviderRegistry<IProvider>.CurrentModuleId;

    public static void Register<TProvider>(TProvider provider)
        where TProvider : class, IProvider
        => global::SharpCpp.Support.SharedProviderRegistry<IProvider>.Register(provider);

    public static IDisposable PushModule(string moduleId)
        => global::SharpCpp.Support.SharedProviderRegistry<IProvider>.PushModule(moduleId);

    public static TProvider Resolve<TProvider>(string? moduleId = null, string? shapeKey = null)
        where TProvider : class, IProvider
        => global::SharpCpp.Support.SharedProviderRegistry<IProvider>.Resolve<TProvider>(moduleId, shapeKey);

    public static TProvider Resolve<TProvider>(string? moduleId, Func<TProvider, bool> predicate)
        where TProvider : class, IProvider
        => global::SharpCpp.Support.SharedProviderRegistry<IProvider>.Resolve(moduleId, predicate);
}
