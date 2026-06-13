using SharpCpp.Metadata.Export;
using SharpCpp.Metadata.Import;
using SharpCpp.Model.Ast;
using Xunit;

namespace SharpCpp.Tests.Metadata;

public sealed class BindingMetadataImportCatalogTests
{
    [Fact]
    public void Load_seeds_suppressed_imported_records_with_qualified_managed_names()
    {
        var document = new BindingMetadataDocument(
            SchemaVersion: BindingMetadataSchema.CurrentVersion,
            GeneratorVersion: BindingMetadataSchema.PreviewGeneratorVersion,
            SupportRuntimeVersion: BindingMetadataSchema.PreviewSupportRuntimeVersion,
            Source: new BindingMetadataSource("SharpCpp.Rbfx.Bindings", "Urho3D", "SharpCpp.Rbfx.Native"),
            Imports: Array.Empty<BindingMetadataImport>(),
            Fingerprint: new BindingMetadataFingerprint("phase2", "clang", "attrs", ["Urho3D/LogicComponent.h"]),
            Types:
            [
                new BindingMetadataType(
                    SymbolId: "record:LogicComponent",
                    Kind: "handle",
                    NativeName: "LogicComponent",
                    ManagedName: "LogicComponent",
                    ManagedNamespace: "Urho3D",
                    ManagedBaseTypeName: "Component",
                    Projection: new BindingMetadataTypeReference("handle", "Urho3D::LogicComponent*", "LogicComponent", null, "record:LogicComponent", null),
                    ManagedInstanceCacheFactoryExpression: "global::Urho3D.Interop.RefCountedInstanceCache.Shared",
                    ManagedInstanceCacheTypeName: "global::SharpCpp.Support.INativeInstanceCache",
                    EmitDefaultNativeReleaseOverride: false)
            ],
            Functions: Array.Empty<BindingMetadataFunction>());

        var tempFile = Path.Combine(Path.GetTempPath(), $"SharpCpp.Tests.Metadata.{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(tempFile, BindingMetadataSerializer.Serialize(document));
            var catalog = BindingMetadataImportCatalog.Load([tempFile]);

            var compilation = new BindingCompilation("Consumer", "RbfxUserCode", "Bindings", "Consumer.Native", ["RbfxUserCode/RotatingComponent.hpp"]);
            var recordsByName = new Dictionary<string, RecordBinding>(StringComparer.Ordinal);
            var enumsByName = new Dictionary<string, EnumBinding>(StringComparer.Ordinal);

            catalog.Seed(compilation, recordsByName, enumsByName);

            var importedRecord = Assert.Single(compilation.Records);
            Assert.Equal("LogicComponent", importedRecord.NativeName);
            Assert.Equal("global::Urho3D.LogicComponent", importedRecord.ManagedName);
            Assert.Equal("global::Urho3D.Component", importedRecord.ManagedBaseTypeName);
            Assert.Equal("global::Urho3D.Interop.RefCountedInstanceCache.Shared", importedRecord.ManagedInstanceCacheFactoryExpression);
            Assert.Equal("global::SharpCpp.Support.INativeInstanceCache", importedRecord.ManagedInstanceCacheTypeName);
            Assert.False(importedRecord.EmitDefaultNativeReleaseOverride);
            Assert.True(importedRecord.GetMetadata<bool>(MetadataKeys.SuppressEmission));
            Assert.Same(importedRecord, recordsByName["LogicComponent"]);
            Assert.Same(importedRecord, recordsByName["Urho3D::LogicComponent"]);
            Assert.Equal("SharpCpp.Rbfx.Bindings", Assert.Single(catalog.Imports).Package);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}
