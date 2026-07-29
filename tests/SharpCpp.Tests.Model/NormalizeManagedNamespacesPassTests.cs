using SharpCpp.Model.Ast;
using SharpCpp.Pipeline;

using Xunit;

namespace SharpCpp.Tests.Model;

public sealed class NormalizeManagedNamespacesPassTests
{
    [Fact]
    public void Apply_promotes_wrapper_enums_to_the_record_namespace()
    {
        var compilation = new BindingCompilation("Sample", "Sample", "Native", "SampleNative", [])
        {
            NamespaceMappings = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["sample"] = "Sample"
            }
        };
        var wrapper = new RecordBinding(
            "record:sample::IndexFormat",
            "IndexFormat",
            "IndexFormat",
            "sample::IndexFormat");
        var @enum = new EnumBinding(
            "enum:sample::IndexFormat::Enum",
            "Enum",
            "IndexFormat",
            cppQualifiedName: "sample::IndexFormat::Enum");
        compilation.Members.Add(wrapper);
        compilation.Members.Add(@enum);

        NormalizeManagedNamespacesPass.Apply(compilation);

        Assert.Equal("Sample", wrapper.ManagedNamespace);
        Assert.Equal(wrapper.ManagedNamespace, @enum.ManagedNamespace);
    }
}
