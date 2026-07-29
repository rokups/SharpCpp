using SharpCpp.Model.Ast;
using SharpCpp.Parsing;

using Xunit;

namespace SharpCpp.Tests.Model;

public sealed class ClangSharpHeaderParserPointerTests
{
    [Fact]
    public void Parse_maps_primitive_pointer_function_types_to_intptr()
    {
        var compilation = ParseTempHeader(
            "Sample.h",
            """
            const char* get_message(void);
            unsigned int* get_words(void);
            void compile(const char** source);
            """,
            ["-x", "c", "-std=c17"]);

        var getMessage = Assert.Single(compilation.Functions, static function => function.NativeName == "get_message");
        var getWords = Assert.Single(compilation.Functions, static function => function.NativeName == "get_words");
        var compile = Assert.Single(compilation.Functions, static function => function.NativeName == "compile");

        AssertPointer(getMessage.ReturnType, "const char*");
        AssertPointer(getWords.ReturnType, "unsigned int*");
        var source = Assert.Single(compile.Parameters);
        AssertPointer(source.Type, "const char*");
        Assert.Equal("const char**", source.CppTypeName);
    }

    [Fact]
    public void Parse_infers_parallel_input_primitive_pointer_arrays()
    {
        var compilation = ParseTempHeader(
            "Sample.hpp",
            """
            struct Buffer {};

            struct DeviceContext
            {
                virtual void SetBuffers(
                    unsigned int startSlot,
                    unsigned int numBuffers,
                    Buffer* const* buffers,
                    const unsigned long long* offsets) = 0;

                virtual void SetValue(const unsigned long long* value) = 0;
            };
            """,
            ["-x", "c++", "-std=c++20"]);

        var deviceContext = Assert.Single(compilation.Records, static record => record.CppQualifiedName == "DeviceContext");
        var setBuffers = Assert.Single(deviceContext.Members.OfType<FunctionBinding>(), static function => function.NativeName == "SetBuffers");
        var bufferOffsets = Assert.Single(setBuffers.Parameters, static parameter => parameter.NativeName == "offsets");
        var setValue = Assert.Single(deviceContext.Members.OfType<FunctionBinding>(), static function => function.NativeName == "SetValue");
        var value = Assert.Single(setValue.Parameters);

        Assert.True(bufferOffsets.HasMetadata(MetadataKeys.InputPointerArray));
        Assert.False(value.HasMetadata(MetadataKeys.InputPointerArray));
    }

    [Fact]
    public void Parse_maps_raw_function_pointer_parameters_to_intptr()
    {
        var compilation = ParseTempHeader(
            "Sample.hpp",
            """
            using Callback = void (*)(int value);
            void set_callback(Callback callback);
            """,
            ["-x", "c++", "-std=c++20"]);

        var function = Assert.Single(compilation.Functions);
        var callback = Assert.Single(function.Parameters);

        Assert.False(function.IsIgnored(), function.GetIgnoreReason());
        Assert.Equal(BindingPrimitiveKind.IntPtr, callback.Type.PrimitiveKind);
        Assert.Equal("global::System.IntPtr", callback.Type.ManagedTypeName);
        Assert.Equal("Callback", callback.CppTypeName);
    }

    [Fact]
    public void Parse_preserves_owned_opaque_pointer_typedefs_as_handles()
    {
        var compilation = ParseTempHeader(
            "Sample.hpp",
            """
            struct NativeHandle_T;
            typedef NativeHandle_T* NativeHandle;
            void use_handle(NativeHandle handle);
            """,
            ["-x", "c++", "-std=c++20"]);

        var function = Assert.Single(compilation.Functions);
        var handle = Assert.Single(function.Parameters);

        Assert.False(function.IsIgnored(), function.GetIgnoreReason());
        Assert.Equal(BindingTypeKind.Handle, handle.Type.Kind);
        Assert.Equal("NativeHandle", handle.Type.ManagedTypeName);
        Assert.Equal("NativeHandle", handle.CppTypeName);
    }

    [Fact]
    public void Parse_maps_only_director_primitive_indirect_parameters_to_intptr()
    {
        var compilation = ParseTempHeader(
            "Sample.hpp",
            """
            namespace Sample
            {
            struct Value {};
            struct Renderer
            {
                virtual void render(int* indices, const Value& translation, int count) = 0;
            };
            }
            """,
            ["-x", "c++", "-std=c++20"],
            new HashSet<string>(StringComparer.Ordinal) { "Sample::Renderer" });

        var renderer = Assert.Single(
            compilation.Records,
            static record => record.CppQualifiedName == "Sample::Renderer");
        var render = Assert.Single(
            renderer.Members.OfType<FunctionBinding>(),
            static function => function.NativeName == "render");
        var indices = Assert.Single(render.Parameters, static parameter => parameter.NativeName == "indices");
        var translation = Assert.Single(render.Parameters, static parameter => parameter.NativeName == "translation");

        Assert.Equal(BindingPrimitiveKind.IntPtr, indices.Type.PrimitiveKind);
        Assert.Equal(BindingParameterDirection.In, indices.Direction);
        Assert.Equal(BindingParameterAbiKind.Value, indices.AbiKind);
        Assert.Equal(BindingTypeKind.Handle, translation.Type.Kind);
        Assert.Equal("Value", translation.Type.ManagedTypeName);
        Assert.Equal(BindingParameterDirection.In, translation.Direction);
        Assert.Equal(BindingParameterAbiKind.Value, translation.AbiKind);
        Assert.False(render.IsIgnored(), render.GetIgnoreReason());
    }

    private static void AssertPointer(BindingType type, string cppTypeName)
    {
        Assert.Equal(BindingTypeKind.Handle, type.Kind);
        Assert.Equal("global::System.IntPtr", type.ManagedTypeName);
        Assert.Equal(cppTypeName, type.CppTypeName);
    }

    private static BindingCompilation ParseTempHeader(
        string fileName,
        string source,
        string[] clangArgs,
        IReadOnlySet<string>? directorRecordCppTypeNames = null)
    {
        var fixtureDir = Path.Combine(Path.GetTempPath(), "sharpcpp-pointers-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixtureDir);
        try
        {
            var headerPath = Path.Combine(fixtureDir, fileName);
            File.WriteAllText(headerPath, source);

            var parser = new ClangSharpHeaderParser();
            return parser.Parse(new ClangSharpParseRequest(
                ProjectName: "TestProject",
                ManagedNamespace: "Test.Namespace",
                ManagedClassName: "Bindings",
                NativeLibraryName: "Test.Native",
                HeaderFilePath: headerPath,
                HeaderIncludePath: fileName,
                ClangArguments: [.. clangArgs, $"-I{fixtureDir}"],
                MarshallerConfigurationFiles: Array.Empty<string>(),
                EntryPointPrefix: "Test_",
                DirectorRecordCppTypeNames: directorRecordCppTypeNames));
        }
        finally
        {
            Directory.Delete(fixtureDir, recursive: true);
        }
    }
}
