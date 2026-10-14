using SharpCpp.Metadata.Export;
using SharpCpp.Metadata.Import;
using SharpCpp.Model.Ast;

using Xunit;

namespace SharpCpp.Tests.E2E.Modules.Metadata;

public sealed class MetadataImportHeaderTests
{
    [Fact]
    public void Import_takes_the_include_relative_headers_and_ignores_absolute_marshaller_headers()
    {
        var fingerprint = new BindingMetadataFingerprint(
            "closure",
            "args",
            "vocabulary",
            ["Dependency/Header.hpp"],
            ["/build/machine/include/SharpCpp/Marshaller.hpp"]);
        var document = new BindingMetadataDocument(
            BindingMetadataSchema.CurrentVersion,
            "1.0",
            "1.0",
            new BindingMetadataSource("Dependency.Package", "Dependency", "dependency_native"),
            [],
            fingerprint,
            [],
            []);

        var directory = Directory.CreateTempSubdirectory("sharpcpp-import-headers");
        try
        {
            var file = Path.Combine(directory.FullName, "Dependency.Bindings.json");
            File.WriteAllText(file, BindingMetadataSerializer.Serialize(document));
            var catalog = BindingMetadataImportCatalog.Load([file]);
            var compilation = new BindingCompilation("Consumer", "Consumer", "ConsumerBindings", "consumer_native", ["Consumer/Header.hpp"]);
            compilation.NativeSupportHeaders.Add("/consumer/machine/include/SharpCpp/Marshaller.hpp");

            catalog.Seed(
                compilation,
                new Dictionary<string, RecordBinding>(StringComparer.Ordinal),
                new Dictionary<string, EnumBinding>(StringComparer.Ordinal));

            Assert.Equal(
                ["/consumer/machine/include/SharpCpp/Marshaller.hpp", "Dependency/Header.hpp"],
                compilation.NativeSupportHeaders);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void Export_does_not_write_the_marshaller_headers_of_the_build_machine()
    {
        var compilation = new BindingCompilation("Dependency", "Dependency", "DependencyBindings", "dependency_native", ["Dependency/Header.hpp"]);
        compilation.NativeSupportHeaders.Add("/build/machine/include/SharpCpp/Marshaller.hpp");

        var document = BindingMetadataExporter.Export(
            compilation,
            new BindingMetadataExportOptions("Dependency.Package"));
        var json = BindingMetadataSerializer.Serialize(document);

        Assert.DoesNotContain("/build/machine", json, StringComparison.Ordinal);
        Assert.DoesNotContain("native_support_headers", json, StringComparison.Ordinal);
        Assert.Equal(["Dependency/Header.hpp"], document.Fingerprint.HeaderFiles);
    }
}
