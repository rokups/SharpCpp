using SharpCpp.Model.Ast;
using SharpCpp.Parsing;

using Xunit;

namespace SharpCpp.Tests.Model;

public sealed class ClangSharpHeaderParserIgnoreTests
{
    [Fact]
    public void Parse_normalizes_ignore_reasons_from_attributes_and_prescan()
    {
        var repoRoot = FindRepositoryRoot();
        var fixtureDir = Path.Combine(repoRoot, "tests", "SharpCpp.Tests.Model", "Fixtures", "IgnoreReasons");
        var headerPath = Path.Combine(fixtureDir, "Sample.hpp");

        var parser = new ClangSharpHeaderParser();
        var compilation = parser.Parse(new ClangSharpParseRequest(
            ProjectName: "TestProject",
            ManagedNamespace: "Test.Namespace",
            ManagedClassName: "Bindings",
            NativeLibraryName: "Test.Native",
            HeaderFilePath: headerPath,
            HeaderIncludePath: fixtureDir,
            ClangArguments: ["-x", "c++", "-std=c++20", $"-I{fixtureDir}"],
            MarshallerConfigurationFiles: Array.Empty<string>(),
            EntryPointPrefix: "Test_"));

        var functions = compilation.Functions.OrderBy(static function => function.NativeName).ToArray();
        Assert.Equal(3, functions.Length);
        Assert.Equal("marked [[sharpcpp::ignore]]", functions[0].GetMetadata<string>(MetadataKeys.IgnoreReason));
        Assert.Equal("manual stub", functions[1].GetMetadata<string>(MetadataKeys.IgnoreReason));
        Assert.Equal("prescan only", functions[2].GetMetadata<string>(MetadataKeys.IgnoreReason));
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
