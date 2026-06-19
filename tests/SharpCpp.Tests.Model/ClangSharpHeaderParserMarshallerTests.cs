using System.Reflection;

using SharpCpp.Model.Ast;
using SharpCpp.Pipeline;
using SharpCpp.Parsing;

using Xunit;

namespace SharpCpp.Tests.Model;

public sealed class ClangSharpHeaderParserMarshallerTests
{
    [Fact]
    public void Parse_applies_marshaller_metadata_from_token_spaced_configuration_text()
    {
        var repoRoot = FindRepositoryRoot();
        var fixtureDir = Path.Combine(repoRoot, "tests", "SharpCpp.Tests.Model", "Fixtures", "TokenSpacedConfig");
        var includeDir = Path.Combine(repoRoot, "include");
        var marshallerHeader = Path.Combine(includeDir, "SharpCpp", "Marshaller.hpp");
        var headerPath = Path.Combine(fixtureDir, "Sample.hpp");

        var parser = new ClangSharpHeaderParser();
        var clangArgs = CreateStandardLibraryClangArgs(fixtureDir, includeDir);
        var compilation = parser.Parse(new ClangSharpParseRequest(
            ProjectName: "TestProject",
            ManagedNamespace: "Test.Namespace",
            ManagedClassName: "Bindings",
            NativeLibraryName: "Test.Native",
            HeaderFilePath: headerPath,
            HeaderIncludePath: fixtureDir,
            ClangArguments: clangArgs,
            MarshallerConfigurationFiles: [marshallerHeader],
            EntryPointPrefix: "Test_"));

        var pointerFunction = Assert.Single(compilation.Functions.Where(static function => function.NativeName == "GetName"));
        Assert.Equal("global::System.ReadOnlySpan<byte>", pointerFunction.ReturnType.ManagedTypeName);
        Assert.Equal("const char*", pointerFunction.ReturnType.CppTypeName);

        var viewReturnFunction = Assert.Single(compilation.Functions.Where(static function => function.NativeName == "GetNameView"));
        Assert.Equal("global::System.ReadOnlySpan<byte>", viewReturnFunction.ReturnType.ManagedTypeName);
        Assert.Equal("global::SharpCpp.Support.Utf8SpanAbiReturnMarshaller", viewReturnFunction.ReturnType.ManagedMarshaller);

        var viewParameterFunction = Assert.Single(compilation.Functions.Where(static function => function.NativeName == "MeasureNameView"));
        Assert.Equal("global::System.ReadOnlySpan<byte>", viewParameterFunction.Parameters[0].Type.ManagedTypeName);
        Assert.Equal("global::SharpCpp.Support.Utf8SpanAbiMarshaller", viewParameterFunction.Parameters[0].Type.ManagedMarshaller);
        Assert.Equal(BindingParameterAbiKind.Value, viewParameterFunction.Parameters[0].AbiKind);
    }

    [Fact]
    public void Parse_and_pipeline_materialize_wrapper_surface_from_custom_string_marshaller()
    {
        var repoRoot = FindRepositoryRoot();
        var fixtureDir = Path.Combine(repoRoot, "tests", "SharpCpp.Tests.Model", "Fixtures", "CustomStringMarshaller");
        var headerPath = Path.Combine(fixtureDir, "Sample.hpp");
        var marshallerHeader = Path.Combine(fixtureDir, "SampleMarshallers.h");

        var parser = new ClangSharpHeaderParser();
        var clangArgs = new[] { "-x", "c++", "-std=c++20", $"-I{fixtureDir}" };
        var marshallerFiles = new[] { marshallerHeader };

        var compilation = parser.Parse(new ClangSharpParseRequest(
            ProjectName: "TestProject",
            ManagedNamespace: "Test.Namespace",
            ManagedClassName: "Bindings",
            NativeLibraryName: "Test.Native",
            HeaderFilePath: headerPath,
            HeaderIncludePath: fixtureDir,
            ClangArguments: clangArgs,
            MarshallerConfigurationFiles: marshallerFiles,
            EntryPointPrefix: "Test_"));

        MarshallerMetadataPhase2.ApplyWithClang(compilation, marshallerFiles, clangArgs);

        var function = Assert.Single(compilation.Functions.Where(static function => function.NativeName == "measure_ea_string"));
        Assert.Equal(BindingTypeKind.Handle, function.Parameters[0].Type.Kind);
        Assert.Equal("Ea.String", function.Parameters[0].Type.ManagedTypeName);

        var record = Assert.Single(compilation.Records.Where(static record => record.NativeName == "ea_compat_string"));
        Assert.Equal("String", record.ManagedName);
        Assert.Equal("Ea", record.ManagedNamespace);
        Assert.Equal("global::SharpCpp.Support.StringBase", record.ManagedBaseTypeName);
        Assert.Contains(record.Members.OfType<FunctionBinding>(), static member => member.ManagedName == "GetCountCore");
        Assert.Contains(record.Members.OfType<FunctionBinding>(), static member => member.ManagedName == "AddCore");
        Assert.Contains(record.Members.OfType<FunctionBinding>(), static member => member.ManagedName == "ClearCore");
    }

    [Fact]
    public void Parse_recognizes_alias_spelled_string_view_marshaller()
    {
        var repoRoot = FindRepositoryRoot();
        var fixtureDir = Path.Combine(repoRoot, "tests", "SharpCpp.Tests.Model", "Fixtures", "AliasStringViewMarshaller");
        var includeDir = Path.Combine(repoRoot, "include");
        var headerPath = Path.Combine(fixtureDir, "Sample.hpp");
        var marshallerHeader = Path.Combine(fixtureDir, "SampleMarshallers.h");

        var parser = new ClangSharpHeaderParser();
        var clangArgs = CreateStandardLibraryClangArgs(fixtureDir, includeDir);
        var marshallerFiles = new[] { marshallerHeader };

        var compilation = parser.Parse(new ClangSharpParseRequest(
            ProjectName: "TestProject",
            ManagedNamespace: "Test.Namespace",
            ManagedClassName: "Bindings",
            NativeLibraryName: "Test.Native",
            HeaderFilePath: headerPath,
            HeaderIncludePath: fixtureDir,
            ClangArguments: clangArgs,
            MarshallerConfigurationFiles: marshallerFiles,
            EntryPointPrefix: "Test_"));

        var parameterFunction = Assert.Single(compilation.Functions.Where(static function => function.NativeName == "measure_ea_string_view"));
        Assert.Equal("global::System.ReadOnlySpan<byte>", parameterFunction.Parameters[0].Type.ManagedTypeName);
        Assert.Equal("global::SharpCpp.Support.Utf8SpanAbiMarshaller", parameterFunction.Parameters[0].Type.ManagedMarshaller);
        Assert.Equal(BindingParameterAbiKind.Value, parameterFunction.Parameters[0].AbiKind);

        var defaultedFunction = Assert.Single(compilation.Functions.Where(static function => function.NativeName == "measure_default_ea_string_view"));
        Assert.True(defaultedFunction.Parameters[0].HasMetadata(MetadataKeys.UndefaultableParam));
        Assert.False(defaultedFunction.Parameters[0].HasMetadata(MetadataKeys.UnexpressibleDefault));

        var returnFunction = Assert.Single(compilation.Functions.Where(static function => function.NativeName == "get_ea_string_view"));
        Assert.Equal("global::System.ReadOnlySpan<byte>", returnFunction.ReturnType.ManagedTypeName);
        Assert.Equal("global::SharpCpp.Support.Utf8SpanAbiReturnMarshaller", returnFunction.ReturnType.ManagedMarshaller);
    }

    [Fact]
    public void Parse_defaults_managed_pointer_override_parameters_to_in_value_tokens()
    {
        var repoRoot = FindRepositoryRoot();
        var fixtureDir = Path.Combine(repoRoot, "tests", "SharpCpp.Tests.Model", "Fixtures", "BytePointerMarshaller");
        var includeDir = Path.Combine(repoRoot, "include");
        var headerPath = Path.Combine(fixtureDir, "Sample.hpp");
        var marshallerHeader = Path.Combine(includeDir, "SharpCpp", "Marshaller.hpp");

        var parser = new ClangSharpHeaderParser();
        var clangArgs = CreateStandardLibraryClangArgs(fixtureDir, includeDir);

        var compilation = parser.Parse(new ClangSharpParseRequest(
            ProjectName: "TestProject",
            ManagedNamespace: "Test.Namespace",
            ManagedClassName: "Bindings",
            NativeLibraryName: "Test.Native",
            HeaderFilePath: headerPath,
            HeaderIncludePath: fixtureDir,
            ClangArguments: clangArgs,
            MarshallerConfigurationFiles: [marshallerHeader],
            EntryPointPrefix: "Test_"));

        var unsignedBuffer = Assert.Single(compilation.Functions.Where(static function => function.NativeName == "consume_unsigned_buffer"));
        Assert.Equal("global::System.IntPtr", unsignedBuffer.Parameters[0].Type.ManagedTypeName);
        Assert.Equal(BindingParameterDirection.In, unsignedBuffer.Parameters[0].Direction);
        Assert.Equal(BindingParameterAbiKind.Value, unsignedBuffer.Parameters[0].AbiKind);

        var signedBuffer = Assert.Single(compilation.Functions.Where(static function => function.NativeName == "consume_signed_buffer"));
        Assert.Equal("global::System.IntPtr", signedBuffer.Parameters[0].Type.ManagedTypeName);
        Assert.Equal(BindingParameterDirection.In, signedBuffer.Parameters[0].Direction);
        Assert.Equal(BindingParameterAbiKind.Value, signedBuffer.Parameters[0].AbiKind);

        var explicitUnsignedOut = Assert.Single(compilation.Functions.Where(static function => function.NativeName == "write_unsigned_pointer"));
        Assert.Equal("global::System.IntPtr", explicitUnsignedOut.Parameters[0].Type.ManagedTypeName);
        Assert.Equal(BindingParameterDirection.Out, explicitUnsignedOut.Parameters[0].Direction);
        Assert.Equal(BindingParameterAbiKind.Value, explicitUnsignedOut.Parameters[0].AbiKind);

        var explicitSignedOut = Assert.Single(compilation.Functions.Where(static function => function.NativeName == "write_signed_pointer"));
        Assert.Equal("global::System.IntPtr", explicitSignedOut.Parameters[0].Type.ManagedTypeName);
        Assert.Equal(BindingParameterDirection.Out, explicitSignedOut.Parameters[0].Direction);
        Assert.Equal(BindingParameterAbiKind.Value, explicitSignedOut.Parameters[0].AbiKind);
    }

    [Fact]
    public void Parse_projects_shared_ptr_and_marks_returns_as_owning_handles()
    {
        var repoRoot = FindRepositoryRoot();
        var fixtureDir = Path.Combine(repoRoot, "tests", "SharpCpp.Tests.Model", "Fixtures", "SharedPtr");
        var headerPath = Path.Combine(fixtureDir, "Sample.hpp");
        var marshallerHeader = Path.Combine(fixtureDir, "SampleMarshallers.h");

        var parser = new ClangSharpHeaderParser();
        var clangArgs = new[] { "-x", "c++", "-std=c++20", $"-I{fixtureDir}" };
        var marshallerFiles = new[] { marshallerHeader };

        var compilation = parser.Parse(new ClangSharpParseRequest(
            ProjectName: "TestProject",
            ManagedNamespace: "Test.Namespace",
            ManagedClassName: "Bindings",
            NativeLibraryName: "Test.Native",
            HeaderFilePath: headerPath,
            HeaderIncludePath: fixtureDir,
            ClangArguments: clangArgs,
            MarshallerConfigurationFiles: marshallerFiles,
            EntryPointPrefix: "Test_"));

        MarshallerMetadataPhase2.ApplyWithClang(compilation, marshallerFiles, clangArgs);
        SharpCpp.Pipeline.InheritManagedRuntimePolicyPass.Apply(compilation);

        var refCountedRecord = Assert.Single(compilation.Records.Where(static record => record.NativeName == "RefCounted"));
        var payloadRecord = Assert.Single(compilation.Records.Where(static record => record.NativeName == "sample_payload"));
        Assert.Equal("global::Urho3D.Interop.RefCountedNativeObject", refCountedRecord.ManagedBaseTypeName);
        Assert.Equal("RefCounted", payloadRecord.ManagedBaseTypeName);
        Assert.Equal("global::Urho3D.Interop.RefCountedInstanceCache.Shared", payloadRecord.ManagedInstanceCacheFactoryExpression);
        Assert.False(payloadRecord.EmitDefaultNativeReleaseOverride);

        var sharedReturn = Assert.Single(compilation.Functions.Where(static function => function.NativeName == "get_payload_shared"));
        Assert.Equal(BindingTypeKind.Handle, sharedReturn.ReturnType.Kind);
        Assert.Equal(payloadRecord.SymbolId, sharedReturn.ReturnType.RecordId);
        Assert.Equal("Urho3D::SharedPtr<sample_payload>", sharedReturn.ReturnType.CppTypeName);
        Assert.True(sharedReturn.ReturnsOwnedHandle);

        var weakReturn = Assert.Single(compilation.Functions.Where(static function => function.NativeName == "get_payload_weak"));
        Assert.Equal(BindingTypeKind.Handle, weakReturn.ReturnType.Kind);
        Assert.Equal(payloadRecord.SymbolId, weakReturn.ReturnType.RecordId);
        Assert.Equal("Urho3D::WeakPtr<sample_payload>", weakReturn.ReturnType.CppTypeName);
        Assert.True(weakReturn.ReturnsOwnedHandle);

        var sharedParameter = Assert.Single(compilation.Functions.Where(static function => function.NativeName == "use_payload_shared"));
        Assert.Equal(BindingTypeKind.Handle, sharedParameter.Parameters[0].Type.Kind);
        Assert.Equal(payloadRecord.SymbolId, sharedParameter.Parameters[0].Type.RecordId);
    }

    [Fact]
    public void Bootstrap_pipeline_supports_vector_of_record_handle_parameters()
    {
        var repoRoot = FindRepositoryRoot();
        var fixtureDir = Path.Combine(repoRoot, "tests", "SharpCpp.Tests.Model", "Fixtures", "VectorOfRecordHandleParameters");
        var headerPath = Path.Combine(fixtureDir, "Sample.hpp");
        var marshallerHeader = Path.Combine(fixtureDir, "SampleMarshallers.h");

        var parser = new ClangSharpHeaderParser();
        var clangArgs = new[] { "-x", "c++", "-std=c++20", $"-I{fixtureDir}" };
        var marshallerFiles = new[] { marshallerHeader };

        var compilation = parser.Parse(new ClangSharpParseRequest(
            ProjectName: "TestProject",
            ManagedNamespace: "Test.Namespace",
            ManagedClassName: "Bindings",
            NativeLibraryName: "Test.Native",
            HeaderFilePath: headerPath,
            HeaderIncludePath: fixtureDir,
            ClangArguments: clangArgs,
            MarshallerConfigurationFiles: marshallerFiles,
            EntryPointPrefix: "Test_"));

        BindingCompilationBootstrapPipeline.Apply(compilation, marshallerFiles, clangArgs);

        var eventHub = Assert.Single(compilation.Records.Where(static record => record.NativeName == "EventHub"));
        var method = Assert.Single(eventHub.Members.OfType<FunctionBinding>().Where(static function => function.NativeName == "UnsubscribeFromAllEventsExcept"));

        Assert.False(
            method.IsIgnored(),
            $"Ignored because '{method.GetIgnoreReason()}' with managed type '{method.Parameters[0].Type.ManagedTypeName}'.");
        Assert.Equal("Ea.Vector<Object>", method.Parameters[0].Type.ManagedTypeName);
        Assert.Equal("const eastl::vector<Urho3D::Object*>&", method.Parameters[0].CppTypeName);
    }

    [Fact]
    public void Bootstrap_pipeline_supports_hash_map_alias_return_surface()
    {
        var repoRoot = FindRepositoryRoot();
        var fixtureDir = Path.Combine(repoRoot, "tests", "SharpCpp.Tests.Model", "Fixtures", "HashMapAlias");
        var headerPath = Path.Combine(fixtureDir, "Sample.hpp");
        var marshallerHeader = Path.Combine(fixtureDir, "SampleMarshallers.h");

        var parser = new ClangSharpHeaderParser();
        var clangArgs = new[] { "-x", "c++", "-std=c++20", $"-I{fixtureDir}" };
        var marshallerFiles = new[] { marshallerHeader };

        var compilation = parser.Parse(new ClangSharpParseRequest(
            ProjectName: "TestProject",
            ManagedNamespace: "Test",
            ManagedClassName: "Bindings",
            NativeLibraryName: "Test.Native",
            HeaderFilePath: headerPath,
            HeaderIncludePath: fixtureDir,
            ClangArguments: clangArgs,
            MarshallerConfigurationFiles: marshallerFiles,
            EntryPointPrefix: "Test_"));

        BindingCompilationBootstrapPipeline.Apply(compilation, marshallerFiles, clangArgs);

        var eventHub = Assert.Single(compilation.Records.Where(static record => record.NativeName == "EventHub"));
        var getEventDataMap = Assert.Single(eventHub.Members.OfType<FunctionBinding>().Where(static function => function.NativeName == "GetEventDataMap"));
        var getStringEventDataMap = Assert.Single(eventHub.Members.OfType<FunctionBinding>().Where(static function => function.NativeName == "GetStringEventDataMap"));

        Assert.False(getEventDataMap.IsIgnored(), $"Ignored because '{getEventDataMap.GetIgnoreReason()}'. Type: {DescribeType(getEventDataMap.ReturnType)}");
        Assert.False(getStringEventDataMap.IsIgnored(), $"Ignored because '{getStringEventDataMap.GetIgnoreReason()}'. Type: {DescribeType(getStringEventDataMap.ReturnType)}");
        Assert.NotEmpty(compilation.TypeAliases);
        var variantMapAlias = Assert.Single(compilation.TypeAliases.Where(static alias => alias.NativeName == "VariantMap"));
        var stringVariantMapAlias = Assert.Single(compilation.TypeAliases.Where(static alias => alias.NativeName == "StringVariantMap"));
        Assert.Equal("VariantMap", variantMapAlias.ManagedName);
        Assert.Equal("StringVariantMap", stringVariantMapAlias.ManagedName);
        Assert.Equal("Ea.UnorderedMap<global::Urho3D.Math.StringHash, Variant>", variantMapAlias.TargetType.ManagedTypeName);
        Assert.Equal("Ea.UnorderedMap<Ea.String, Variant>", stringVariantMapAlias.TargetType.ManagedTypeName);

        Assert.True(
            string.Equals("VariantMap", getEventDataMap.ReturnType.ManagedTypeName, StringComparison.Ordinal),
            $"Expected VariantMap but got {DescribeType(getEventDataMap.ReturnType)}; alias target {DescribeType(variantMapAlias.TargetType)}");
        Assert.True(
            string.Equals("StringVariantMap", getStringEventDataMap.ReturnType.ManagedTypeName, StringComparison.Ordinal),
            $"Expected StringVariantMap but got {DescribeType(getStringEventDataMap.ReturnType)}; alias target {DescribeType(stringVariantMapAlias.TargetType)}");
        Assert.Equal("const Urho3D::VariantMap&", getEventDataMap.CppReturnTypeName);
        Assert.Equal("const Urho3D::StringVariantMap&", getStringEventDataMap.CppReturnTypeName);
    }

    [Fact]
    public void Bootstrap_pipeline_prunes_aliases_with_unresolved_configured_generic_arguments()
    {
        var compilation = new BindingCompilation("TestProject", "Urho3D", "Bindings", "Test.Native", ["Sample.hpp"]);
        var vectorRecord = new RecordBinding("record:eastl::vector", "eastl::vector", "Vector", "eastl::vector")
        {
            ManagedTypeFormat = "global::Ea.Vector<{0}>"
        };
        compilation.Members.Add(vectorRecord);

        var unresolvedPair = BindingType.CreateConfigured("ea::pair<ea::string, bool>", "ea::pair<ea::string, bool>");
        var aliasTarget = BindingType.CreateHandle(
            "ea::vector<ea::pair<ea::string, bool>>*",
            "global::Ea.Vector<{0}>",
            vectorRecord.SymbolId,
            genericArguments: [unresolvedPair]);
        compilation.TypeAliases.Add(new BindingTypeAlias("Urho3D::EnabledRenderPasses", "EnabledRenderPasses", "EnabledRenderPasses", aliasTarget));

        var prune = typeof(BindingCompilationBootstrapPipeline).GetMethod(
            "PruneAliasesTargetingDegradedRecords",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(prune);

        prune.Invoke(null, [compilation]);

        Assert.Empty(compilation.TypeAliases);
    }

    private static string DescribeType(BindingType type)
    {
        var args = type.GenericArguments.Count == 0
            ? string.Empty
            : $"<{string.Join(", ", type.GenericArguments.Select(DescribeType))}>";
        return $"{type.Kind}:{type.ManagedTypeName}:{type.RecordId}:{type.CppTypeName}{args}";
    }

    [Fact(Skip = "Known issue: MapParameter passes canonical type for record references, losing typedef identity for managed-type-override lookup.")]
    public void Bootstrap_pipeline_preserves_alias_backed_value_type_parameters_and_emits_alias_metadata()
    {
        var repoRoot = FindRepositoryRoot();
        var fixtureDir = Path.Combine(repoRoot, "tests", "SharpCpp.Tests.Model", "Fixtures", "Vector3Alias");
        var headerPath = Path.Combine(fixtureDir, "Sample.hpp");
        var marshallerHeader = Path.Combine(fixtureDir, "SampleMarshallers.h");

        var parser = new ClangSharpHeaderParser();
        var clangArgs = new[] { "-x", "c++", "-std=c++20", $"-I{fixtureDir}" };
        var marshallerFiles = new[] { marshallerHeader };

        var compilation = parser.Parse(new ClangSharpParseRequest(
            ProjectName: "TestProject",
            ManagedNamespace: "Urho3D",
            ManagedClassName: "Bindings",
            NativeLibraryName: "Test.Native",
            HeaderFilePath: headerPath,
            HeaderIncludePath: fixtureDir,
            ClangArguments: clangArgs,
            MarshallerConfigurationFiles: marshallerFiles,
            EntryPointPrefix: "Test_"));

        BindingCompilationBootstrapPipeline.Apply(compilation, marshallerFiles, clangArgs);

        var actionBuilder = Assert.Single(compilation.Records.Where(static record => record.NativeName == "ActionBuilder"));
        var jumpBy = Assert.Single(actionBuilder.Members.OfType<FunctionBinding>().Where(static function => function.NativeName == "JumpBy"));
        Assert.False(jumpBy.IsIgnored(), $"Ignored because '{jumpBy.GetIgnoreReason()}'.");
        Assert.Equal("Vector3", jumpBy.Parameters[0].Type.ManagedTypeName);
        Assert.Equal("global::Urho3D.Math.Vector3", jumpBy.Parameters[0].Type.UnaliasedManagedTypeName);

        var alias = Assert.Single(compilation.TypeAliases.Where(static alias => alias.CppQualifiedName == "Urho3D::Vector3"));
        Assert.Equal("Vector3", alias.ManagedName);
        Assert.Equal("Urho3D", alias.ManagedNamespace);
        Assert.Equal("global::Urho3D.Math.Vector3", alias.TargetType.UnaliasedManagedTypeName);
    }

    [Fact]
    public void Bootstrap_pipeline_preserves_string_vector_alias_and_emits_alias_metadata()
    {
        var repoRoot = FindRepositoryRoot();
        var fixtureDir = Path.Combine(repoRoot, "tests", "SharpCpp.Tests.Model", "Fixtures", "IntVectorAlias");
        var headerPath = Path.Combine(fixtureDir, "Sample.hpp");
        var marshallerHeader = Path.Combine(fixtureDir, "SampleMarshallers.h");

        var parser = new ClangSharpHeaderParser();
        var clangArgs = new[] { "-x", "c++", "-std=c++20", $"-I{fixtureDir}" };
        var marshallerFiles = new[] { marshallerHeader };

        var compilation = parser.Parse(new ClangSharpParseRequest(
            ProjectName: "TestProject",
            ManagedNamespace: "Test",
            ManagedClassName: "Bindings",
            NativeLibraryName: "Test.Native",
            HeaderFilePath: headerPath,
            HeaderIncludePath: fixtureDir,
            ClangArguments: clangArgs,
            MarshallerConfigurationFiles: marshallerFiles,
            EntryPointPrefix: "Test_"));

        BindingCompilationBootstrapPipeline.Apply(compilation, marshallerFiles, clangArgs);

        // Verify the alias record exists with correct metadata.
        var alias = Assert.Single(compilation.TypeAliases.Where(static a => a.CppQualifiedName == "IntVector"));
        Assert.Equal("IntVector", alias.ManagedName);
        Assert.Equal("Test", alias.ManagedNamespace);

        // Target type should reference the record-backed vector type
        Assert.Contains("Vector", alias.TargetType.ManagedTypeName, StringComparison.Ordinal);

        // Target type must not contain C++ :: separators (would be pruned otherwise)
        Assert.DoesNotContain("::", alias.TargetType.ManagedTypeName.Replace("global::", "", StringComparison.Ordinal), StringComparison.Ordinal);

        // Verify the method is not ignored (container types with Marshaller metadata survive degradation)
        var consumer = Assert.Single(compilation.Records.Where(static record => record.NativeName == "IntVectorConsumer"));
        var accept = Assert.Single(consumer.Members.OfType<FunctionBinding>().Where(static function => function.NativeName == "Accept"));
        Assert.False(accept.IsIgnored(), $"Ignored because '{accept.GetIgnoreReason()}'.");
    }

    [Fact]
    public void Bootstrap_pipeline_fully_qualifies_value_aliases_inside_vector_cpp_signatures()
    {
        var repoRoot = FindRepositoryRoot();
        var fixtureDir = Path.Combine(repoRoot, "tests", "SharpCpp.Tests.Model", "Fixtures", "VectorOfValueAliasCollision");
        var headerPath = Path.Combine(fixtureDir, "Sample.hpp");
        var marshallerHeader = Path.Combine(fixtureDir, "SampleMarshallers.h");

        var parser = new ClangSharpHeaderParser();
        var clangArgs = new[] { "-x", "c++", "-std=c++20", $"-I{fixtureDir}" };
        var marshallerFiles = new[] { marshallerHeader };

        var compilation = parser.Parse(new ClangSharpParseRequest(
            ProjectName: "TestProject",
            ManagedNamespace: "Test.Namespace",
            ManagedClassName: "Bindings",
            NativeLibraryName: "Test.Native",
            HeaderFilePath: headerPath,
            HeaderIncludePath: fixtureDir,
            ClangArguments: clangArgs,
            MarshallerConfigurationFiles: marshallerFiles,
            EntryPointPrefix: "Test_"));

        BindingCompilationBootstrapPipeline.Apply(compilation, marshallerFiles, clangArgs);

        var hullBuilder = Assert.Single(compilation.Records.Where(static record => record.NativeName == "HullBuilder"));
        var getVertices = Assert.Single(hullBuilder.Members.OfType<FunctionBinding>().Where(static function => function.NativeName == "GetVertices"));
        var setVertices = Assert.Single(hullBuilder.Members.OfType<FunctionBinding>().Where(static function => function.NativeName == "SetVertices"));
        var stored = Assert.Single(hullBuilder.Members.OfType<FieldBinding>().Where(static field => field.NativeName == "stored_"));

        Assert.Equal("Ea.Vector<global::Urho3D.Math.Vector3>", getVertices.ReturnType.ManagedTypeName);
        Assert.Equal("Ea.Vector<global::Urho3D.Math.Vector3>", setVertices.Parameters[0].Type.ManagedTypeName);
        Assert.Equal("Ea.Vector<global::Urho3D.Math.Vector3>", stored.Type.ManagedTypeName);

        Assert.Equal("const eastl::vector<Urho3D::BaseVector3<float>>&", getVertices.CppReturnTypeName);
        Assert.Equal("const eastl::vector<Urho3D::BaseVector3<float>>&", setVertices.Parameters[0].CppTypeName);
        Assert.Equal("eastl::vector<Urho3D::BaseVector3<float>>", stored.CppTypeName);

        Assert.DoesNotContain("Rml::Vector3", getVertices.CppReturnTypeName, StringComparison.Ordinal);
        Assert.DoesNotContain("Rml::Vector3", setVertices.Parameters[0].CppTypeName, StringComparison.Ordinal);
        Assert.DoesNotContain("Rml::Vector3", stored.CppTypeName, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")) &&
                Directory.Exists(Path.Combine(directory.FullName, "include")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Unable to locate repository root.");
    }

    private static string[] CreateStandardLibraryClangArgs(params string[] includeDirs)
    {
        var args = new List<string> { "-x", "c++", "-std=c++20" };
        args.AddRange(includeDirs.Select(static includeDir => $"-I{includeDir}"));

        var systemIncludeDirs = new[]
        {
            "/usr/include/c++/16.1.1",
            "/usr/include/c++/16.1.1/x86_64-pc-linux-gnu",
            "/usr/include/c++/16.1.1/backward",
            "/usr/lib/gcc/x86_64-pc-linux-gnu/16.1.1/include",
            "/usr/include"
        };

        args.AddRange(systemIncludeDirs.Where(Directory.Exists).Select(static includeDir => $"-I{includeDir}"));
        return args.ToArray();
    }
}
