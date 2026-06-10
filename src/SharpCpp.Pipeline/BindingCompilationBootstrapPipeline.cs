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
        RunTimed("Polymorphism", () => BindingCompilationPolymorphismPass.Apply(compilation));
        RunTimed("ValidateBootstrap", () => ValidateBootstrapSurfacePass.Apply(compilation));

        // Invalidate cached member lookups so rendering picks up any members
        // added by pipeline passes (MI projections, generic instantiations, etc.)
        compilation.InvalidateCaches();
    }

    private static void RunTimed(string name, Action action)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            action();
        }
        catch
        {
            Logger.LogWarning("Pass '{Name}' FAILED after {Duration}", name, FormatDuration(sw.Elapsed));
            throw;
        }
        if (sw.Elapsed.TotalSeconds >= 1)
        {
            Logger.LogInformation("Pass '{Name}' completed in {Duration}", name, FormatDuration(sw.Elapsed));
        }
    }

    private static string FormatDuration(TimeSpan elapsed) => $"{elapsed.TotalSeconds:F1}s";
}
