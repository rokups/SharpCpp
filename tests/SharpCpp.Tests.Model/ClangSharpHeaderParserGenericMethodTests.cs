using SharpCpp.Model.Ast;
using SharpCpp.Parsing;

using Xunit;

namespace SharpCpp.Tests.Model;

public sealed class ClangSharpHeaderParserGenericMethodTests
{
    [Fact]
    public void Parse_preserves_qualified_generic_method_parameter()
    {
        var compilation = ParseTempHeader(
            """
            class Element
            {
            public:
                template <typename T>
                T GetAttribute(const T& default_value) const;
            };
            """);

        var element = Assert.Single(compilation.Records, static record => record.CppQualifiedName == "Element");
        var method = Assert.Single(element.Members.OfType<FunctionBinding>(), static member => member.NativeName == "GetAttribute");
        var parameter = Assert.Single(method.Parameters);
        Assert.Equal(BindingTypeKind.GenericParameter, parameter.Type.Kind);
        Assert.Equal("T", parameter.Type.GenericParameterName);
        Assert.Equal("const T&", parameter.CppTypeName);
    }

    [Fact]
    public void Parse_excludes_generic_constructor()
    {
        var compilation = ParseTempHeader(
            """
            class Guard
            {
            public:
                template <typename T>
                explicit Guard(const T& value);
            };
            """);

        var guard = Assert.Single(compilation.Records, static record => record.CppQualifiedName == "Guard");
        var constructor = Assert.Single(guard.Members.OfType<FunctionBinding>(), static member => member.NativeName == "Guard");
        Assert.True(constructor.HasMetadata(MetadataKeys.IgnoreReason));
        Assert.False(constructor.IsSilentlyExcluded());
    }

    private static BindingCompilation ParseTempHeader(string source)
    {
        var fixtureDir = Path.Combine(Path.GetTempPath(), "sharpcpp-generic-methods-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixtureDir);
        try
        {
            const string fileName = "Sample.hpp";
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
                ClangArguments: ["-x", "c++", "-std=c++20", $"-I{fixtureDir}"],
                MarshallerConfigurationFiles: Array.Empty<string>(),
                EntryPointPrefix: "Test_"));
        }
        finally
        {
            Directory.Delete(fixtureDir, recursive: true);
        }
    }
}
