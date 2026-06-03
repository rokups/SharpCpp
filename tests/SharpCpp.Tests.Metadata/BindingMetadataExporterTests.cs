using SharpCpp.Metadata.Export;
using SharpCpp.Model.Ast;

using Xunit;

namespace SharpCpp.Tests.Metadata;

public sealed class BindingMetadataExporterTests
{
    [Fact]
    public void Export_sorts_types_functions_imports_and_headers_deterministically()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "z.hpp", "a.hpp" });

        var zoo = new RecordBinding("record:Zoo", "Zoo", "Zoo");
        var alpha = new RecordBinding("record:Alpha", "Alpha", "Alpha");
        var zebra = new FunctionBinding("function:zebra", "zebra", "Zebra", alpha.HandleType, "entry_zebra");
        zebra.Parameters.Add(new ParameterBinding("parameter:zebra:value", "value", "value", BindingType.Int32));
        var add = new FunctionBinding("function:add", "add", "Add", BindingType.Int32, "entry_add");

        compilation.Members.Add(zoo);
        compilation.Members.Add(alpha);
        compilation.Members.Add(zebra);
        compilation.Members.Add(add);

        var document = BindingMetadataExporter.Export(
            compilation,
            new BindingMetadataExportOptions(
                PackageId: "SharpCpp.Sample",
                Imports: new[]
                {
                    new BindingMetadataImport("Zoo.Package", "z"),
                    new BindingMetadataImport("Alpha.Package", "a")
                }));

        Assert.Equal(new[] { "Alpha", "Zoo" }, document.Types.Select(static type => type.ManagedName).Take(2));
        Assert.Equal(new[] { "Add", "Zebra" }, document.Functions.Select(static function => function.ManagedName));
        Assert.Equal(new[] { "Alpha.Package", "Zoo.Package" }, document.Imports.Select(static importItem => importItem.Package));
        Assert.Equal(new[] { "a.hpp", "z.hpp" }, document.Fingerprint.HeaderFiles);
    }

    [Fact]
    public void Serialize_round_trips_metadata_document()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "sample.hpp" });
        compilation.Members.Add(new RecordBinding("record:Widget", "Widget", "Widget", marshallerTypeName: "WidgetMarshaller"));

        var original = BindingMetadataExporter.Export(compilation);
        var json = BindingMetadataSerializer.Serialize(original);
        var roundTripped = BindingMetadataSerializer.Deserialize(json);

        Assert.Contains("\"schema_version\": 1", json, StringComparison.Ordinal);
        Assert.EndsWith(Environment.NewLine, json, StringComparison.Ordinal);
        Assert.Equal(original.SchemaVersion, roundTripped.SchemaVersion);
        Assert.Equal(original.GeneratorVersion, roundTripped.GeneratorVersion);
        Assert.Equal(original.SupportRuntimeVersion, roundTripped.SupportRuntimeVersion);
        Assert.Equal(original.Source, roundTripped.Source);
        Assert.Equal(original.Imports, roundTripped.Imports);
        Assert.Equal(original.Fingerprint.Phase2ClosureHash, roundTripped.Fingerprint.Phase2ClosureHash);
        Assert.Equal(original.Fingerprint.ClangArgsHash, roundTripped.Fingerprint.ClangArgsHash);
        Assert.Equal(original.Fingerprint.AttributeVocabularyHash, roundTripped.Fingerprint.AttributeVocabularyHash);
        Assert.Equal(original.Fingerprint.HeaderFiles, roundTripped.Fingerprint.HeaderFiles);
        Assert.Equal(original.Types.Select(static type => type.SymbolId), roundTripped.Types.Select(static type => type.SymbolId));
        Assert.Equal(original.Types.Select(static type => type.IsFlags), roundTripped.Types.Select(static type => type.IsFlags));
        Assert.Equal(
            original.Types.Select(static type => type.Values?.Count ?? 0),
            roundTripped.Types.Select(static type => type.Values?.Count ?? 0));
        Assert.Equal(
            original.Types.Select(static type => type.Members?.Count ?? 0),
            roundTripped.Types.Select(static type => type.Members?.Count ?? 0));
        Assert.Equal(original.Functions, roundTripped.Functions);
    }

    [Fact]
    public void Export_includes_enum_flag_status_and_values()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "enum.hpp" });
        var permissions = new EnumBinding("enum:permissions", "permissions", "Permissions", "unsigned int")
        {
            IsFlags = true
        };
        permissions.Values.Add(new EnumValueBinding("read", "Read", "1u"));
        permissions.Values.Add(new EnumValueBinding("write", "Write", "2u"));
        compilation.Members.Add(permissions);

        var document = BindingMetadataExporter.Export(compilation);
        var exportedEnum = Assert.Single(document.Types);

        Assert.True(exportedEnum.IsFlags);
        Assert.Collection(
            exportedEnum.Values!,
            value =>
            {
                Assert.Equal("read", value.NativeName);
                Assert.Equal("Read", value.ManagedName);
                Assert.Equal("1u", value.ValueExpression);
            },
            value =>
            {
                Assert.Equal("write", value.NativeName);
                Assert.Equal("Write", value.ManagedName);
                Assert.Equal("2u", value.ValueExpression);
            });
    }

    [Fact]
    public void Export_includes_record_members()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "record.hpp" });
        var record = new RecordBinding("record:Counter", "counter", "Counter");
        record.Members.Add(new FieldBinding("field:counter:value", "value", "Value", BindingType.Int32, "int", "counter", isReadOnly: true));
        record.Members.Add(new FunctionBinding("ctor:counter", "counter", "Counter", record.HandleType, "entry_ctor") { IsConstructor = true });
        record.Members.Add(new FunctionBinding("method:counter:get", "get_value", "GetValue", BindingType.Int32, "entry_get") { OwningRecordCppTypeName = "counter" });
        compilation.Members.Add(record);

        var document = BindingMetadataExporter.Export(compilation);
        var exportedRecord = Assert.Single(document.Types);

        Assert.Collection(
            exportedRecord.Members!,
            member =>
            {
                Assert.Equal("constructor", member.Kind);
                Assert.Equal("Counter", member.ManagedName);
                Assert.True(member.IsConstructor);
            },
            member =>
            {
                Assert.Equal("method", member.Kind);
                Assert.Equal("GetValue", member.ManagedName);
                Assert.Equal("int", member.Type!.PublicType);
            },
            member =>
            {
                Assert.Equal("field", member.Kind);
                Assert.Equal("Value", member.ManagedName);
                Assert.True(member.IsReadOnly);
            });
    }
}
