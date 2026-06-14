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
        var tempDir = Path.Combine(Path.GetTempPath(), $"SharpCpp.Tests.Model.{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var repoRoot = FindRepositoryRoot();
        var includeDir = Path.Combine(repoRoot, "include");
        var marshallerHeader = Path.Combine(includeDir, "SharpCpp", "Marshaller.hpp");

        var headerPath = Path.Combine(tempDir, "Sample.hpp");
        File.WriteAllText(headerPath, """
            #pragma once

            const char* GetName();
            """);

        try
        {
            var parser = new ClangSharpHeaderParser();
            var compilation = parser.Parse(new ClangSharpParseRequest(
                ProjectName: "TestProject",
                ManagedNamespace: "Test.Namespace",
                ManagedClassName: "Bindings",
                NativeLibraryName: "Test.Native",
                HeaderFilePath: headerPath,
                HeaderIncludePath: tempDir,
                ClangArguments: ["-x", "c++", "-std=c++20", $"-I{tempDir}", $"-I{includeDir}"],
                MarshallerConfigurationFiles: [marshallerHeader],
                EntryPointPrefix: "Test_"));

            var function = Assert.Single(compilation.Functions);
            Assert.Equal("global::System.ReadOnlySpan<byte>", function.ReturnType.ManagedTypeName);
            Assert.Equal("const char*", function.ReturnType.CppTypeName);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void Parse_and_pipeline_materialize_wrapper_surface_from_custom_string_marshaller()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"SharpCpp.Tests.Model.{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var headerPath = Path.Combine(tempDir, "Sample.hpp");
        var marshallerHeader = Path.Combine(tempDir, "SampleMarshallers.h");

        File.WriteAllText(headerPath, """
            #pragma once

            class ea_compat_string
            {
            public:
                ea_compat_string() = default;

                unsigned long long size() const
                {
                    return 0;
                }

                void push_back(char value)
                {
                }

                void clear()
                {
                }
            };

            int measure_ea_string(const ea_compat_string& value);
            """);

        File.WriteAllText(marshallerHeader, """
            #pragma once

            #include "Sample.hpp"

            namespace SharpCpp
            {
            template <class T>
            struct Marshaller;

            template <class TString>
            struct OwnedStringMarshallerBase
            {
                using cpp_type = TString;
                using abi_type = TString*;

                [[sharpcpp::expose_member(shape = "protected_override", exposed = false)]]
                static unsigned long long GetCountCore(const cpp_type& self);

                [[sharpcpp::expose_member(shape = "protected_override", exposed = false)]]
                static void AddCore(cpp_type& self, unsigned char value);

                [[sharpcpp::expose_member(shape = "protected_override", exposed = false)]]
                static void ClearCore(cpp_type& self);
            };

            template <class TString>
            struct OwnedStringConstReferenceMarshallerBase
            {
                using cpp_type = const TString&;
                using abi_type = TString*;
            };

            template <class TString>
            struct OwnedStringConstPointerMarshallerBase
            {
                using cpp_type = const TString*;
                using abi_type = TString*;
            };

            template <class TString>
            struct OwnedStringPointerMarshallerBase
            {
                using cpp_type = TString*;
                using abi_type = TString*;
            };

            template <>
            struct [[sharpcpp::managed_type(
                managed_base_type = "global::SharpCpp.Support.StringBase",
                public_type = "Ea.String",
                managed_namespace = "Ea"
            )]]
            Marshaller<ea_compat_string> : OwnedStringMarshallerBase<ea_compat_string> {};

            template <>
            struct Marshaller<const ea_compat_string&> : OwnedStringConstReferenceMarshallerBase<ea_compat_string> {};

            template <>
            struct Marshaller<const ea_compat_string*> : OwnedStringConstPointerMarshallerBase<ea_compat_string> {};

            template <>
            struct Marshaller<ea_compat_string*> : OwnedStringPointerMarshallerBase<ea_compat_string> {};
            }
            """);

        try
        {
            var parser = new ClangSharpHeaderParser();
            var clangArgs = new[] { "-x", "c++", "-std=c++20", $"-I{tempDir}" };
            var marshallerFiles = new[] { marshallerHeader };

            var compilation = parser.Parse(new ClangSharpParseRequest(
                ProjectName: "TestProject",
                ManagedNamespace: "Test.Namespace",
                ManagedClassName: "Bindings",
                NativeLibraryName: "Test.Native",
                HeaderFilePath: headerPath,
                HeaderIncludePath: tempDir,
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
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void Parse_projects_shared_ptr_and_marks_returns_as_owning_handles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"SharpCpp.Tests.Model.{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var headerPath = Path.Combine(tempDir, "Sample.hpp");
        var marshallerHeader = Path.Combine(tempDir, "SampleMarshallers.h");

        File.WriteAllText(headerPath, """
            #pragma once

            namespace Urho3D
            {
            class RefCounted
            {
            public:
                virtual ~RefCounted() = default;
                int AddRef();
                int ReleaseRef();
            };

            template <class T, class TRefCounted = T>
            class SharedPtr
            {
            public:
                T* Get() const;
            };

            template <class T, class TRefCounted = T>
            class WeakPtr
            {
            public:
                T* Get() const;
            };
            }

            class sample_payload : public Urho3D::RefCounted
            {
            public:
                int get_value() const;
            };

            Urho3D::SharedPtr<sample_payload> get_payload_shared();
            Urho3D::WeakPtr<sample_payload> get_payload_weak();
            void use_payload_shared(const Urho3D::SharedPtr<sample_payload>& value);
            """);

        File.WriteAllText(marshallerHeader, """
            #pragma once

            #include "Sample.hpp"

            namespace SharpCpp
            {
            template <class T>
            struct Marshaller;

            template <>
            struct [[sharpcpp::managed_type(
                public_type = "RefCounted",
                managed_base_type = "global::Urho3D.Interop.RefCountedNativeObject",
                managed_instance_cache_factory = "global::Urho3D.Interop.RefCountedInstanceCache.Shared",
                managed_instance_cache_type = "global::SharpCpp.Support.INativeInstanceCache",
                release_strategy = "base"
            )]] Marshaller<Urho3D::RefCounted*>
            {
                using cpp_type = Urho3D::RefCounted*;
                using abi_type = Urho3D::RefCounted*;
            };

            template <class TCpp, class TRefCounted>
            struct [[sharpcpp::managed_type(
                public_type = "{0}",
                projection = true,
                return_ownership = "owning"
            )]] Marshaller<Urho3D::SharedPtr<TCpp, TRefCounted>>
            {
                using cpp_type = Urho3D::SharedPtr<TCpp, TRefCounted>;
                using abi_type = TCpp*;
            };

            template <class TCpp, class TRefCounted>
            struct Marshaller<const Urho3D::SharedPtr<TCpp, TRefCounted>&>
            {
                using cpp_type = const Urho3D::SharedPtr<TCpp, TRefCounted>&;
                using abi_type = TCpp*;
            };

            template <class TCpp, class TRefCounted>
            struct [[sharpcpp::managed_type(
                public_type = "{0}",
                projection = true,
                return_ownership = "owning"
            )]] Marshaller<Urho3D::WeakPtr<TCpp, TRefCounted>>
            {
                using cpp_type = Urho3D::WeakPtr<TCpp, TRefCounted>;
                using abi_type = TCpp*;
            };
            }
            """);

        try
        {
            var parser = new ClangSharpHeaderParser();
            var clangArgs = new[] { "-x", "c++", "-std=c++20", $"-I{tempDir}" };
            var marshallerFiles = new[] { marshallerHeader };

            var compilation = parser.Parse(new ClangSharpParseRequest(
                ProjectName: "TestProject",
                ManagedNamespace: "Test.Namespace",
                ManagedClassName: "Bindings",
                NativeLibraryName: "Test.Native",
                HeaderFilePath: headerPath,
                HeaderIncludePath: tempDir,
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
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
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
    public void Bootstrap_pipeline_preserves_alias_backed_value_type_parameters_and_emits_alias_metadata()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"SharpCpp.Tests.Model.{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var headerPath = Path.Combine(tempDir, "Sample.hpp");
        var marshallerHeader = Path.Combine(tempDir, "SampleMarshallers.h");

        File.WriteAllText(headerPath, """
            #pragma once

            namespace Urho3D
            {
            template <class T>
            class BaseVector3
            {
            public:
                T x_;
                T y_;
                T z_;
            };

            using Vector3 = BaseVector3<float>;

            class ActionBuilder
            {
            public:
                void JumpBy(const Vector3& offset);
            };
            }
            """);

        File.WriteAllText(marshallerHeader, """
            #pragma once

            #include "Sample.hpp"

            namespace SharpCpp
            {
            template <class T>
            struct Marshaller;

            template <>
            struct [[sharpcpp::managed_type(
                public_type = "global::Urho3D.Math.Vector3",
                marshaller_type = "global::Urho3D.Math.Vector3"
            )]] Marshaller<Urho3D::Vector3>
            {
                using cpp_type = Urho3D::Vector3;
                using abi_type = Urho3D::Vector3;
            };

            template <>
            struct Marshaller<const Urho3D::Vector3&>
            {
                using cpp_type = const Urho3D::Vector3&;
                using abi_type = const Urho3D::Vector3*;
            };
            }
            """);

        try
        {
            var parser = new ClangSharpHeaderParser();
            var clangArgs = new[] { "-x", "c++", "-std=c++20", $"-I{tempDir}" };
            var marshallerFiles = new[] { marshallerHeader };

            var compilation = parser.Parse(new ClangSharpParseRequest(
                ProjectName: "TestProject",
                ManagedNamespace: "Urho3D",
                ManagedClassName: "Bindings",
                NativeLibraryName: "Test.Native",
                HeaderFilePath: headerPath,
                HeaderIncludePath: tempDir,
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
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
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
