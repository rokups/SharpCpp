using SharpCpp.Model.Ast;
using SharpCpp.Parsing;

using Xunit;

namespace SharpCpp.Tests.Model;

public sealed class ClangSharpHeaderParserEnumTests
{
    [Fact]
    public void Parse_names_nested_enum_wrapper_after_its_record()
    {
        var compilation = ParseTempHeader(
            "Sample.hpp",
            """
            namespace Sample
            {
            struct IndexFormat
            {
                enum Enum
                {
                    UInt16,
                    UInt32
                };
            };

            IndexFormat::Enum get_index_format();
            }
            """,
            ["-x", "c++", "-std=c++20"]);

        var indexFormat = Assert.Single(
            compilation.Enums,
            static enumBinding => enumBinding.CppQualifiedName == "Sample::IndexFormat::Enum");
        var function = Assert.Single(compilation.Functions);

        Assert.Equal("IndexFormat", indexFormat.ManagedName);
        Assert.Equal(indexFormat.SymbolId, function.ReturnType.EnumId);
        Assert.Equal("IndexFormat", function.ReturnType.ManagedTypeName);
        Assert.Equal("Sample::IndexFormat::Enum", function.CppReturnTypeName);
    }

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

    [Fact]
    public void Parse_includes_nested_enums()
    {
        var compilation = ParseTempHeader(
            "Sample.hpp",
            """
            namespace Test
            {
            class Variant
            {
            public:
                enum Type : unsigned long
                {
                    None,
                    Integer
                };

                Type GetType() const;
            };
            }
            """,
            ["-x", "c++", "-std=c++20"]);

        var @enum = Assert.Single(compilation.Enums);
        Assert.Equal("Type", @enum.NativeName);
        Assert.Equal("Test::Variant::Type", @enum.CppQualifiedName);
    }

    [Fact]
    public void Parse_distinguishes_same_named_nested_enums()
    {
        var compilation = ParseTempHeader(
            "Sample.hpp",
            """
            namespace Test
            {
            class Log
            {
            public:
                enum Type
                {
                    Error
                };
            };

            class Variant
            {
            public:
                enum Type
                {
                    None
                };
            };
            }
            """,
            ["-x", "c++", "-std=c++20"]);

        Assert.Contains(compilation.Enums, static @enum => @enum.CppQualifiedName == "Test::Log::Type");
        Assert.Contains(compilation.Enums, static @enum => @enum.CppQualifiedName == "Test::Variant::Type");
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
