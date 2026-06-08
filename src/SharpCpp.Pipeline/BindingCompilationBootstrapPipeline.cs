using SharpCpp.Model.Ast;
using SharpCpp.Parsing;

namespace SharpCpp.Pipeline;

public static class BindingCompilationBootstrapPipeline
{
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

        NormalizeManagedNamespacesPass.Apply(compilation);
        MarshallerMetadataPhase2.ApplyWithClang(compilation, marshallerConfigFiles, clangArgs);
        DegradeUnsupportedSurfacePass.Apply(compilation);
        BindingCompilationGenericInstantiationPass.Apply(compilation);
        BindingCompilationInheritancePass.Apply(compilation);
        BindingCompilationPolymorphismPass.Apply(compilation);
        ValidateBootstrapSurfacePass.Apply(compilation);
    }
}
