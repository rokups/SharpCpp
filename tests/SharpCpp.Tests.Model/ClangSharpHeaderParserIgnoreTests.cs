using SharpCpp.Model.Ast;
using SharpCpp.Parsing;

using Xunit;

namespace SharpCpp.Tests.Model;

public sealed class ClangSharpHeaderParserIgnoreTests
{
    [Fact]
    public void Parse_normalizes_ignore_reasons_from_attributes_and_prescan()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"SharpCpp.Tests.Model.{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var headerPath = Path.Combine(tempDir, "Sample.hpp");
        File.WriteAllText(headerPath, """
            #pragma once

            // [[sharpcpp::ignore]]
            [[sharpcpp::ignore]] int IgnoredBare();
            // [[sharpcpp::ignore("manual stub")]]
            [[sharpcpp::ignore("manual stub")]] int IgnoredQuoted();

            // [[sharpcpp::ignore("prescan only")]]
            int PrescanIgnored();
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
                ClangArguments: ["-x", "c++", "-std=c++20", $"-I{tempDir}"],
                MarshallerConfigurationFiles: Array.Empty<string>(),
                EntryPointPrefix: "Test_"));

            var functions = compilation.Functions.OrderBy(static function => function.NativeName).ToArray();
            Assert.Equal(3, functions.Length);
            Assert.Equal("marked [[sharpcpp::ignore]]", functions[0].GetMetadata<string>(MetadataKeys.IgnoreReason));
            Assert.Equal("manual stub", functions[1].GetMetadata<string>(MetadataKeys.IgnoreReason));
            Assert.Equal("prescan only", functions[2].GetMetadata<string>(MetadataKeys.IgnoreReason));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
