using SharpCpp.Model.Ast;
using SharpCpp.Parsing;

namespace SharpCpp.Pipeline;

public static class BindingCompilationBootstrapPipeline
{
    public static void Apply(BindingCompilation compilation, IEnumerable<string> metadataFiles)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(metadataFiles);

        NormalizeManagedNamespacesPass.Apply(compilation);
        MarshallerMetadataPhase2.Apply(compilation, metadataFiles);
        BindingCompilationGenericInstantiationPass.Apply(compilation);
        BindingCompilationInheritancePass.Apply(compilation);
        BindingCompilationPolymorphismPass.Apply(compilation);
        ValidateBootstrapSurfacePass.Apply(compilation);
    }
}
