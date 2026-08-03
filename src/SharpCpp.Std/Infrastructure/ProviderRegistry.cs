using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Std.Infrastructure;

/// <summary>
/// Process-local registry of module-owned stdlib providers.
/// </summary>
/// <remarks>
/// Multiple modules may register the same provider contract and shape. A lookup with a
/// module identity prefers that module's registration; when no local registration exists,
/// a unique compatible registration is used. The registry never picks an arbitrary module.
/// </remarks>
public static class ProviderRegistry
{
    private sealed record Registration(string ModuleId, string ShapeKey, IProvider Provider);

    private static readonly ConcurrentDictionary<(Type Contract, string ShapeKey), global::System.Collections.Generic.List<Registration>> s_registrations = new();
    private static readonly AsyncLocal<string?> s_currentModule = new();
    private static readonly object s_registrationGate = new();

    /// <summary>The module identity used by lookups that omit an explicit identity.</summary>
    public static string? CurrentModuleId => s_currentModule.Value;

    /// <summary>
    /// Registers all provider contracts implemented by <paramref name="provider"/>.
    /// Registering the same provider instance twice is idempotent; a different provider
    /// for one module, contract, and shape is rejected.
    /// </summary>
    public static void Register(IProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (string.IsNullOrWhiteSpace(provider.ModuleId))
        {
            throw new ArgumentException("A provider must specify a non-empty module identity.", nameof(provider));
        }

        if (string.IsNullOrWhiteSpace(provider.ShapeKey))
        {
            throw new ArgumentException("A provider must specify a non-empty native shape key.", nameof(provider));
        }

        var contracts = provider.GetType()
            .GetInterfaces()
            .Where(static contract => contract != typeof(IProvider) && typeof(IProvider).IsAssignableFrom(contract))
            .Distinct();

        lock (s_registrationGate)
        {
            var registrationLists = contracts
                .Select(contract => (Contract: contract, Registrations: s_registrations.GetOrAdd(
                    (contract, provider.ShapeKey),
                    static _ => new global::System.Collections.Generic.List<Registration>())))
                .ToArray();
            foreach (var (contract, registrations) in registrationLists)
            {
                lock (registrations)
                {
                    var existing = registrations.FirstOrDefault(item => string.Equals(item.ModuleId, provider.ModuleId, StringComparison.Ordinal));
                    if (existing is not null && !ReferenceEquals(existing.Provider, provider))
                    {
                        throw new InvalidOperationException(
                            $"A provider for contract '{contract.FullName}' and shape '{provider.ShapeKey}' is already registered by module '{provider.ModuleId}'.");
                    }
                }
            }

            foreach (var (_, registrations) in registrationLists)
            {
                lock (registrations)
                {
                    if (!registrations.Any(item => string.Equals(item.ModuleId, provider.ModuleId, StringComparison.Ordinal)))
                    {
                        registrations.Add(new Registration(provider.ModuleId, provider.ShapeKey, provider));
                    }
                }
            }
        }
    }

    /// <summary>Strongly typed convenience overload for generated registration code.</summary>
    public static void Register<TProvider>(TProvider provider)
        where TProvider : class, IProvider
        => Register((IProvider)provider);

    /// <summary>Registers a provider under an explicit module identity.</summary>
    public static void Register(string moduleId, IProvider provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        ArgumentNullException.ThrowIfNull(provider);
        if (!string.Equals(moduleId, provider.ModuleId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Provider module identity '{provider.ModuleId}' does not match the requested identity '{moduleId}'.",
                nameof(provider));
        }

        Register(provider);
    }

    /// <summary>Strongly typed convenience overload for generated registration code.</summary>
    public static void Register<TProvider>(string moduleId, TProvider provider)
        where TProvider : class, IProvider
        => Register(moduleId, (IProvider)provider);

    /// <summary>
    /// Temporarily selects a module for implicit wrapper construction and provider lookup.
    /// </summary>
    public static IDisposable PushModule(string moduleId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        var previous = s_currentModule.Value;
        s_currentModule.Value = moduleId;
        return new ModuleScope(previous);
    }

    /// <summary>Clears all registrations. Intended for isolated tests and unload hooks.</summary>
    public static void Clear()
    {
        lock (s_registrationGate)
        {
            s_registrations.Clear();
            s_currentModule.Value = null;
        }
    }

    /// <summary>
    /// Resolves one provider, preferring <paramref name="moduleId"/> (or the current module)
    /// and falling back only when exactly one compatible provider remains.
    /// </summary>
    public static TProvider Resolve<TProvider>(string? moduleId = null, string? shapeKey = null)
        where TProvider : class, IProvider
    {
        return Resolve<TProvider>(moduleId, shapeKey, static _ => true);
    }

    /// <summary>Resolves a provider after applying a family-specific shape predicate.</summary>
    public static TProvider Resolve<TProvider>(
        string? moduleId,
        Func<TProvider, bool> predicate)
        where TProvider : class, IProvider
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return Resolve(moduleId, shapeKey: null, predicate);
    }

    private static TProvider Resolve<TProvider>(
        string? moduleId,
        string? shapeKey,
        Func<TProvider, bool> predicate)
        where TProvider : class, IProvider
    {
        moduleId ??= CurrentModuleId;
        var contract = typeof(TProvider);
        var candidates = new global::System.Collections.Generic.List<TProvider>();

        foreach (var pair in s_registrations)
        {
            if (pair.Key.Contract != contract || (shapeKey is not null && !string.Equals(pair.Key.ShapeKey, shapeKey, StringComparison.Ordinal)))
            {
                continue;
            }

            lock (pair.Value)
            {
                candidates.AddRange(pair.Value.Select(static item => (TProvider)item.Provider).Where(predicate));
            }
        }

        if (moduleId is not null)
        {
            var local = candidates.Where(candidate => string.Equals(candidate.ModuleId, moduleId, StringComparison.Ordinal)).ToArray();
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

        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        if (candidates.Count == 0)
        {
            throw new InvalidOperationException(
                $"No provider registered for contract '{contract.FullName}'{FormatShape(shapeKey)}{FormatModule(moduleId)}.");
        }

        var modules = string.Join(", ", candidates.Select(static candidate => candidate.ModuleId).Distinct(StringComparer.Ordinal).OrderBy(static value => value, StringComparer.Ordinal));
        throw new InvalidOperationException(
            $"Multiple providers registered for contract '{contract.FullName}'{FormatShape(shapeKey)}{FormatModule(moduleId)}: {modules}. Supply the module provider explicitly.");
    }

    private static string FormatShape(string? shapeKey) => shapeKey is null ? string.Empty : $" and shape '{shapeKey}'";

    private static string FormatModule(string? moduleId) => moduleId is null ? string.Empty : $" for module '{moduleId}'";

    private sealed class ModuleScope : IDisposable
    {
        private readonly string? _previous;
        private bool _disposed;

        public ModuleScope(string? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            s_currentModule.Value = _previous;
        }
    }
}
