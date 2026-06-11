using SharpCpp.Model.Ast;
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
