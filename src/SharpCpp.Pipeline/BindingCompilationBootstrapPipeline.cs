using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SharpCpp.Model.Ast;
using SharpCpp.Model.Logging;
using SharpCpp.Parsing;

namespace SharpCpp.Pipeline;

public static class BindingCompilationBootstrapPipeline
{
    private static readonly ILogger Logger = SharpCppLog.For(nameof(BindingCompilationBootstrapPipeline));

    /// <summary>
    /// Apply the bootstrap pipeline using ClangSharp-backed Phase 2 metadata extraction.
    /// The C++ compiler resolves template specializations; the AST walk replaces
    /// all fragile regex patterns for finding Marshaller/ReturnMarsharker declarations
    /// and extracting method signatures from struct body text.
    /// </summary>
    /// <param name="compilation">The binding compilation to process.</param>
    /// <param name="marshallerConfigFiles">Paths to marshaller configuration headers only
    /// (e.g., Marshaller.hpp), not all API header files.</param>
    /// <param name="clangArgs">Clang compiler arguments (same as used in Phase 1 parse).</param>
    public static void Apply(BindingCompilation compilation, IEnumerable<string> marshallerConfigFiles, IReadOnlyList<string> clangArgs)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(marshallerConfigFiles);

        RunTimed("NormalizeManagedNamespaces", () => NormalizeManagedNamespacesPass.Apply(compilation));
        RunTimed("MarshallerMetadataPhase2", () => MarshallerMetadataPhase2.ApplyWithClang(compilation, marshallerConfigFiles, clangArgs));
        RunTimed("DegradeUnsupportedSurface", () => DegradeUnsupportedSurfacePass.Apply(compilation));
        RunTimed("GenericInstantiation", () => BindingCompilationGenericInstantiationPass.Apply(compilation));
        RunTimed("Inheritance", () => BindingCompilationInheritancePass.Apply(compilation));
        RunTimed("InheritManagedRuntimePolicy", () => InheritManagedRuntimePolicyPass.Apply(compilation));
        RunTimed("Polymorphism", () => BindingCompilationPolymorphismPass.Apply(compilation));
        RunTimed("ValidateBootstrap", () => ValidateBootstrapSurfacePass.Apply(compilation));
        RunTimed("DegradeValidatedSurface", () => DegradeUnsupportedSurfacePass.Apply(compilation));

        // Prune aliases whose target records were degraded or excluded by
        // any of the pipeline passes (e.g. records outside configured
        // namespace mappings, unsupported surface, etc.).
        RunTimed("PruneDegradedAliases", () => PruneAliasesTargetingDegradedRecords(compilation));

        // Invalidate cached member lookups so rendering picks up any members
        // added by pipeline passes (MI projections, generic instantiations, etc.)
        compilation.InvalidateCaches();
    }

    private static void RunTimed(string name, Action action)
    {
        var sw = Stopwatch.StartNew();
        Logger.LogInformation("Pass '{Name}' starting", name);
        try
        {
            action();
        }
        catch
        {
            Logger.LogWarning("Pass '{Name}' FAILED after {Duration}", name, FormatDuration(sw.Elapsed));
            throw;
        }
        Logger.LogInformation("Pass '{Name}' completed in {Duration}", name, FormatDuration(sw.Elapsed));
    }

    private static string FormatDuration(TimeSpan elapsed) => elapsed.TotalSeconds < 1 ? $"{elapsed.TotalMilliseconds:F0}ms" : $"{elapsed.TotalSeconds:F1}s";

    private static void PruneAliasesTargetingDegradedRecords(BindingCompilation compilation)
    {
        if (compilation.TypeAliases.Count == 0)
        {
            return;
        }

        for (var i = compilation.TypeAliases.Count - 1; i >= 0; i--)
        {
            var alias = compilation.TypeAliases[i];
            if (ShouldPruneAliasTarget(alias, compilation))
            {
                compilation.TypeAliases.RemoveAt(i);
            }
        }
    }

    private static bool ShouldPruneAliasTarget(BindingTypeAlias alias, BindingCompilation compilation)
    {
        var targetType = alias.TargetType;

        // Check if the target record or enum was degraded.
        if (targetType.Kind == BindingTypeKind.Handle && targetType.RecordId is not null)
        {
            var record = compilation.Records.FirstOrDefault(r =>
                string.Equals(r.SymbolId, targetType.RecordId, StringComparison.Ordinal));
            if (record is null || record.IsIgnored())
            {
                return true;
            }
        }
        else if (targetType.Kind == BindingTypeKind.Enum)
        {
            // Enum aliases target the enum's ManagedName.  Check whether the
            // underlying enum survived degradation.
            var enumTarget = compilation.Enums.FirstOrDefault(e =>
                string.Equals(e.ManagedName, targetType.ManagedTypeName, StringComparison.Ordinal) ||
                string.Equals(e.NativeName, targetType.Marshaller.PublicTypeName, StringComparison.Ordinal));
            if (enumTarget is null || enumTarget.IsIgnored())
            {
                return true;
            }
        }
        else if (targetType.Kind == BindingTypeKind.Primitive)
        {
            // Configured types use CppTypeName as the lookup key for the
            // backing record/enum.  If the backing type was degraded, prune.
            var cppName = targetType.CppTypeName;
            if (!string.IsNullOrWhiteSpace(cppName))
            {
                var backingRecord = compilation.Records.FirstOrDefault(r =>
                    string.Equals(r.CppQualifiedName, cppName, StringComparison.Ordinal) ||
                    string.Equals(r.NativeName, cppName, StringComparison.Ordinal));
                if (backingRecord is not null && backingRecord.IsIgnored())
                {
                    return true;
                }

                var backingEnum = compilation.Enums.FirstOrDefault(e =>
                    string.Equals(e.CppQualifiedName, cppName, StringComparison.Ordinal) ||
                    string.Equals(e.NativeName, cppName, StringComparison.Ordinal));
                if (backingEnum is not null && backingEnum.IsIgnored())
                {
                    return true;
                }
            }
        }

        // Recurse into generic arguments — each must also be live.
        foreach (var argument in targetType.GenericArguments)
        {
            if (argument.Kind == BindingTypeKind.Handle &&
                argument.RecordId is not null)
            {
                var argRecord = compilation.Records.FirstOrDefault(r =>
                    string.Equals(r.SymbolId, argument.RecordId, StringComparison.Ordinal));
                if (argRecord is null || argRecord.IsIgnored())
                {
                    return true;
                }
            }
        }

        return false;
    }
}
