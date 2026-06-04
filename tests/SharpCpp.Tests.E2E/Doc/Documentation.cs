using Xunit;

namespace SharpCpp.Tests.E2E.Doc;

public sealed class DocumentationTests
{
    [Fact]
    public void Generated_public_api_includes_xml_docs_for_implemented_shapes()
    {
        var generatedSource = File.ReadAllText(Path.Combine(GetGeneratedDir(), "SharpCppTestsE2E.Bindings.g.cs"));

        Assert.Contains("/// <summary>Represents a documented record with a brief description.</summary>", generatedSource, StringComparison.Ordinal);
        Assert.Contains("/// <summary>Gets the stored value.</summary>", generatedSource, StringComparison.Ordinal);
        Assert.Contains("/// <summary>The current value stored in the record.</summary>", generatedSource, StringComparison.Ordinal);
        Assert.Contains("/// <summary>A free function with a block-style documentation comment.</summary>", generatedSource, StringComparison.Ordinal);
        Assert.Contains("/// <summary>Enumeration of status values for document processing.</summary>", generatedSource, StringComparison.Ordinal);
        Assert.Contains("/// <summary>Escapes XML-sensitive characters: &lt;tag&gt;value &amp; &quot;other&quot;</summary>", generatedSource, StringComparison.Ordinal);
    }

    private static string GetGeneratedDir()
    {
        var dir = Environment.GetEnvironmentVariable("SharpCppGeneratedDir");
        if (string.IsNullOrWhiteSpace(dir))
        {
            dir = Path.Combine(
                Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".",
                "..", "..", "..", "..", "..",
                "out", "build", "dev", "generated", "sharpcpp-e2e");
        }

        return Path.GetFullPath(dir);
    }
}
