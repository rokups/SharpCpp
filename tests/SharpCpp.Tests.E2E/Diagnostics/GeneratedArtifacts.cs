using System.Reflection;

namespace SharpCpp.Tests.E2E.Diagnostics;

/// <summary>Files the generator wrote next to the bindings this assembly compiles.</summary>
internal static class GeneratedArtifacts
{
    public static string Directory { get; } = typeof(GeneratedArtifacts).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(static attribute => attribute.Key == "SharpCppGeneratedDir")
        .Value!;

    // Generated files carry the line endings of the platform that wrote them; tests split on '\n'.
    public static string ReadText(string fileName) =>
        File.ReadAllText(Path.Combine(Directory, fileName)).Replace("\r\n", "\n", StringComparison.Ordinal);
}
