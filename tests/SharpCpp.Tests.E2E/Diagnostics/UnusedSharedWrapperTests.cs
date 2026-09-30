using Xunit;

namespace SharpCpp.Tests.E2E.Diagnostics;

/// <summary>
/// The shared std::basic_string wrapper is emitted only in modules whose surface uses it.
/// </summary>
public sealed class UnusedSharedWrapperTests
{
    private static string[] Generated(string module) => Directory
        .GetFiles(GeneratedArtifacts.Directory, module + ".*")
        .Where(static path => path.EndsWith(".g.cs", StringComparison.Ordinal) || path.EndsWith(".g.cpp", StringComparison.Ordinal) || path.EndsWith(".json", StringComparison.Ordinal))
        .Select(File.ReadAllText)
        .ToArray();

    [Theory]
    [InlineData("SharpCppC")]
    [InlineData("SharpCppNamespaceMapping")]
    [InlineData("SharpCppMetadataDependency")]
    [InlineData("SharpCppMetadataConsumer")]
    public void Module_not_using_std_string_does_not_emit_the_string_wrapper(string module)
    {
        var files = Generated(module);
        Assert.NotEmpty(files);
        Assert.DoesNotContain(files, static text => text.Contains("__StdProvider_String", StringComparison.Ordinal));
        Assert.DoesNotContain(files, static text => text.Contains("basic_string", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("SharpCppTestsE2E")]
    [InlineData("SharpCppCrossModuleLibrary")]
    public void Module_using_std_string_keeps_the_string_wrapper(string module)
    {
        var files = Generated(module);
        Assert.Contains(files, static text => text.Contains("__StdProvider_String", StringComparison.Ordinal));
    }
}
