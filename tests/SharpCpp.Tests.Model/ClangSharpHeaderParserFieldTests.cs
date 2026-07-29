using SharpCpp.Model.Ast;
using SharpCpp.Parsing;

using Xunit;

namespace SharpCpp.Tests.Model;

public sealed class ClangSharpHeaderParserFieldTests
{
    [Fact]
    public void Parse_maps_typedef_char_pointer_fields_to_intptr_without_ignoring_them()
    {
        var compilation = ParseTempHeader(
            "Sample.hpp",
            """
            namespace Test
            {
            typedef char Char;

            struct ShaderCreateInfo
            {
                const Char* Source = nullptr;
                const Char* EntryPoint = nullptr;
            };
            }
            """,
            ["-x", "c++", "-std=c++20"]);

        var record = Assert.Single(compilation.Records, static record => record.CppQualifiedName == "Test::ShaderCreateInfo");
        var source = Assert.Single(record.Members.OfType<FieldBinding>(), static field => field.NativeName == "Source");
        var entryPoint = Assert.Single(record.Members.OfType<FieldBinding>(), static field => field.NativeName == "EntryPoint");

        AssertCharPointerField(source);
        AssertCharPointerField(entryPoint);
    }

    [Fact]
    public void Parse_maps_function_pointer_fields_to_intptr_without_ignoring_them()
    {
        var compilation = ParseTempHeader(
            "Sample.hpp",
            """
            namespace Test
            {
            typedef int (*Writer)(const unsigned char* data, unsigned long size);

            struct Picture
            {
                Writer writer = nullptr;
            };
            }
            """,
            ["-x", "c++", "-std=c++20"]);

        var record = Assert.Single(compilation.Records, static record => record.CppQualifiedName == "Test::Picture");
        var writer = Assert.Single(record.Members.OfType<FieldBinding>(), static field => field.NativeName == "writer");

        Assert.False(writer.IsIgnored(), writer.GetIgnoreReason());
        Assert.True(writer.IsPointer);
        Assert.Equal(BindingPrimitiveKind.IntPtr, writer.Type.PrimitiveKind);
        Assert.True(writer.GetMetadata<bool>(MetadataKeys.FunctionPointerField));
    }

    [Fact]
    public void Parse_maps_primitive_pointer_fields_to_intptr_without_ignoring_them()
    {
        var compilation = ParseTempHeader(
            "Sample.hpp",
            """
            struct Mesh
            {
                unsigned int* indices;
                const float* weights;
            };
            """,
            ["-x", "c++", "-std=c++20"]);

        var record = Assert.Single(compilation.Records, static record => record.CppQualifiedName == "Mesh");
        var indices = Assert.Single(record.Members.OfType<FieldBinding>(), static field => field.NativeName == "indices");
        var weights = Assert.Single(record.Members.OfType<FieldBinding>(), static field => field.NativeName == "weights");

        AssertPointerField(indices, "unsigned int*");
        AssertPointerField(weights, "const float*");
    }

    [Fact]
    public void Parse_keeps_unsupported_nested_pointer_fields_ignored()
    {
        var compilation = ParseTempHeader(
            "Sample.hpp",
            """
            using Callback = void (*)(int value);

            struct Fields
            {
                const char** names;
                Callback callbacks[4];
            };
            """,
            ["-x", "c++", "-std=c++20"]);

        var record = Assert.Single(compilation.Records, static record => record.CppQualifiedName == "Fields");
        var names = Assert.Single(record.Members.OfType<FieldBinding>(), static field => field.NativeName == "names");
        var callbacks = Assert.Single(record.Members.OfType<FieldBinding>(), static field => field.NativeName == "callbacks");

        Assert.True(names.IsIgnored());
        Assert.True(callbacks.IsIgnored());
    }

    [Fact]
    public void Parse_preserves_bit_field_shape()
    {
        var compilation = ParseTempHeader(
            "Sample.hpp",
            """
            struct Flags
            {
                unsigned int visible : 1;
            };
            """,
            ["-x", "c++", "-std=c++20"]);

        var record = Assert.Single(compilation.Records, static record => record.CppQualifiedName == "Flags");
        var visible = Assert.Single(record.Members.OfType<FieldBinding>());

        Assert.True(visible.GetMetadata<bool>(MetadataKeys.BitField));
    }

    private static void AssertCharPointerField(FieldBinding field)
    {
        Assert.False(field.IsIgnored(), field.GetIgnoreReason());
        Assert.True(field.IsPointer);
        Assert.Equal(BindingPrimitiveKind.IntPtr, field.Type.PrimitiveKind);
        Assert.Equal("global::System.IntPtr", field.Type.ManagedTypeName);
        Assert.Contains("*", field.CppTypeName, StringComparison.Ordinal);
    }

    private static void AssertPointerField(FieldBinding field, string cppTypeName)
    {
        Assert.False(field.IsIgnored(), field.GetIgnoreReason());
        Assert.True(field.IsPointer);
        Assert.Equal("global::System.IntPtr", field.Type.ManagedTypeName);
        Assert.Equal(cppTypeName, field.CppTypeName);
    }

    private static BindingCompilation ParseTempHeader(string fileName, string source, string[] clangArgs)
    {
        var fixtureDir = Path.Combine(Path.GetTempPath(), "sharpcpp-fields-" + Guid.NewGuid().ToString("N"));
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
                EntryPointPrefix: "Test_"));
        }
        finally
        {
            Directory.Delete(fixtureDir, recursive: true);
        }
    }
}
