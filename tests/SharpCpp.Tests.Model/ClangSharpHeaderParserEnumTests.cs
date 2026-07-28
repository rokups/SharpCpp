using SharpCpp.Model.Ast;
using SharpCpp.Parsing;

using Xunit;

namespace SharpCpp.Tests.Model;

public sealed class ClangSharpHeaderParserEnumTests
{
    [Fact]
    public void Parse_skips_anonymous_enums()
    {
        var compilation = ParseTempHeader(
            "Sample.h",
            """
            enum
            {
                first_value,
                second_value
            };

            enum named_value
            {
                third_value
            };
            """,
            ["-x", "c", "-std=c11"]);

        var @enum = Assert.Single(compilation.Enums);
        Assert.Equal("named_value", @enum.NativeName);
    }

    [Fact]
    public void Parse_preserves_enums_with_duplicate_short_names()
    {
        var compilation = ParseTempHeader(
            "Sample.hpp",
            """
            namespace First
            {
                enum Type
                {
                    FirstValue
                };
            }

            namespace Second
            {
                class Log
                {
                public:
                    enum Type
                    {
                        SecondValue
                    };
                };
            }
            """,
            ["-x", "c++", "-std=c++20"]);

        Assert.Collection(
            compilation.Enums.OrderBy(static @enum => @enum.CppQualifiedName, StringComparer.Ordinal),
            @enum => Assert.Equal("First::Type", @enum.CppQualifiedName),
            @enum => Assert.Equal("Second::Log::Type", @enum.CppQualifiedName));
    }

    private static BindingCompilation ParseTempHeader(string fileName, string source, string[] clangArgs)
    {
        var fixtureDir = Path.Combine(Path.GetTempPath(), "sharpcpp-enums-" + Guid.NewGuid().ToString("N"));
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
