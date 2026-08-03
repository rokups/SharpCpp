using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace SharpCpp.Support;

/// <summary>Common identity for a native provider owned by one binding module.</summary>
public interface ISharedProvider
{
    string ModuleId { get; }

    string ShapeKey { get; }
}

/// <summary>
/// Process-local registry for one family of shared managed wrappers.
/// </summary>
/// <remarks>
/// The root provider type keeps unrelated wrapper assemblies isolated. A lookup prefers
/// the requesting module and falls back only when one compatible provider exists.
/// </remarks>
public static class SharedProviderRegistry<TRootProvider>
    where TRootProvider : class, ISharedProvider
{
    private sealed record Registration(string ModuleId, TRootProvider Provider);

    // ponytail: registration is startup work and lookups contain only a handful of entries;
    // split locks only if measured contention ever makes this gate a real bottleneck.
    private static readonly object s_gate = new();
    private static readonly Dictionary<(Type Contract, string ShapeKey), List<Registration>> s_registrations = new();
    private static readonly AsyncLocal<string?> s_currentModule = new();

    public static string? CurrentModuleId => s_currentModule.Value;

    public static void Register<TProvider>(TProvider provider)
        where TProvider : class, TRootProvider
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider.ModuleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider.ShapeKey);

        var contract = typeof(TProvider);
        var key = (contract, provider.ShapeKey);

        lock (s_gate)
        {
            if (!s_registrations.TryGetValue(key, out var registrations))
            {
                registrations = [];
                s_registrations.Add(key, registrations);
            }

            var existing = registrations.Find(item =>
                string.Equals(item.ModuleId, provider.ModuleId, StringComparison.Ordinal));
            if (existing is not null && !ReferenceEquals(existing.Provider, provider))
            {
                throw new InvalidOperationException(
                    $"A provider for contract '{contract.FullName}' and shape '{provider.ShapeKey}' is already registered by module '{provider.ModuleId}'.");
            }

            if (existing is null)
            {
                registrations.Add(new Registration(provider.ModuleId, provider));
            }
        }
    }

    public static IDisposable PushModule(string moduleId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        var previous = s_currentModule.Value;
        s_currentModule.Value = moduleId;
        return new ModuleScope(previous);
    }

    public static void Clear()
    {
        lock (s_gate)
        {
            s_registrations.Clear();
            s_currentModule.Value = null;
        }
    }

    public static TProvider Resolve<TProvider>(string? moduleId = null, string? shapeKey = null)
        where TProvider : class, TRootProvider
        => Resolve<TProvider>(moduleId, shapeKey, static _ => true);

    public static TProvider Resolve<TProvider>(string? moduleId, Func<TProvider, bool> predicate)
        where TProvider : class, TRootProvider
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return Resolve(moduleId, shapeKey: null, predicate);
    }

    private static TProvider Resolve<TProvider>(
        string? moduleId,
        string? shapeKey,
        Func<TProvider, bool> predicate)
        where TProvider : class, TRootProvider
    {
        moduleId ??= CurrentModuleId;
        var contract = typeof(TProvider);
        TProvider[] candidates;

        lock (s_gate)
        {
            candidates = s_registrations
                .Where(pair => pair.Key.Contract == contract &&
                    (shapeKey is null || string.Equals(pair.Key.ShapeKey, shapeKey, StringComparison.Ordinal)))
                .SelectMany(static pair => pair.Value)
                .Select(static registration => (TProvider)registration.Provider)
                .Where(predicate)
                .ToArray();
        }

        if (moduleId is not null)
        {
            var local = candidates.Where(candidate =>
                string.Equals(candidate.ModuleId, moduleId, StringComparison.Ordinal)).ToArray();
            if (local.Length == 1)
            {
                return local[0];
            }

            if (local.Length > 1)
            {
                throw new InvalidOperationException(
                    $"Multiple providers for contract '{contract.FullName}' and module '{moduleId}' match the requested shape.");
            }
        }

        if (candidates.Length == 1)
        {
            return candidates[0];
        }

        if (candidates.Length == 0)
        {
            throw new InvalidOperationException(
                $"No provider registered for contract '{contract.FullName}'{FormatShape(shapeKey)}{FormatModule(moduleId)}.");
        }

        var modules = string.Join(", ", candidates
            .Select(static candidate => candidate.ModuleId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal));
        throw new InvalidOperationException(
            $"Multiple providers registered for contract '{contract.FullName}'{FormatShape(shapeKey)}{FormatModule(moduleId)}: {modules}. Supply the module provider explicitly.");
    }

    private static string FormatShape(string? shapeKey) => shapeKey is null ? string.Empty : $" and shape '{shapeKey}'";

    private static string FormatModule(string? moduleId) => moduleId is null ? string.Empty : $" for module '{moduleId}'";

    private sealed class ModuleScope(string? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            s_currentModule.Value = previous;
        }
    }
}
