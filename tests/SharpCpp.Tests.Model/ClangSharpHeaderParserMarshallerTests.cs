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
        var compilation = parser.Parse(new ClangSharpParseRequest(
            ProjectName: "TestProject",
            ManagedNamespace: "Test.Namespace",
            ManagedClassName: "Bindings",
            NativeLibraryName: "Test.Native",
            HeaderFilePath: headerPath,
            HeaderIncludePath: fixtureDir,
            ClangArguments: ["-x", "c++", "-std=c++20", $"-I{fixtureDir}", $"-I{includeDir}"],
            MarshallerConfigurationFiles: [marshallerHeader],
            EntryPointPrefix: "Test_"));

        var function = Assert.Single(compilation.Functions);
        Assert.Equal("global::System.ReadOnlySpan<byte>", function.ReturnType.ManagedTypeName);
        Assert.Equal("const char*", function.ReturnType.CppTypeName);
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
}
