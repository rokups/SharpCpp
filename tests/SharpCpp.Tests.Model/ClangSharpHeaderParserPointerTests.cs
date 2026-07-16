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

    private static void AssertPointer(BindingType type, string cppTypeName)
    {
        Assert.Equal(BindingTypeKind.Handle, type.Kind);
        Assert.Equal("global::System.IntPtr", type.ManagedTypeName);
        Assert.Equal(cppTypeName, type.CppTypeName);
    }

    private static BindingCompilation ParseTempHeader(string fileName, string source, string[] clangArgs)
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
                EntryPointPrefix: "Test_"));
        }
        finally
        {
            Directory.Delete(fixtureDir, recursive: true);
        }
    }
}
