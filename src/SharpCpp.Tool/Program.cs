using SharpCpp.Metadata.Export;
using SharpCpp.Parsing;
using SharpCpp.Rendering;

namespace SharpCpp.Tool;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintUsage();
            return 0;
        }

        return args[0] switch
        {
            "sample-metadata" => EmitSampleMetadata(args.Skip(1).ToArray()),
            "generate-sample" => GenerateSampleBindings(args.Skip(1).ToArray()),
            _ => UnknownCommand(args[0])
        };
    }

    private static int EmitSampleMetadata(string[] args)
    {
        var compilation = SampleCompilationFactory.CreateMetadataSample();
        var document = BindingMetadataExporter.Export(
            compilation,
            new BindingMetadataExportOptions(
                PackageId: compilation.ProjectName,
                Phase2ClosureHash: "sample-phase2",
                ClangArgsHash: "sample-clang"));

        var json = BindingMetadataSerializer.Serialize(document);

        if (args.Length == 0)
        {
            Console.Write(json);
            return 0;
        }

        var outputPath = Path.GetFullPath(args[0]);
        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        File.WriteAllText(outputPath, json);
        Console.WriteLine($"Wrote sample metadata to {outputPath}");
        return 0;
    }

    private static int GenerateSampleBindings(string[] args)
    {
        var generatedRoot = GetPathOption(args, "--generated-root");
        if (string.IsNullOrWhiteSpace(generatedRoot))
        {
            Console.Error.WriteLine("Missing required option '--generated-root'.");
            PrintUsage();
            return 1;
        }

        var fileStem = GetOptionValue(args, "--file-stem") ?? "SharpCppTestsE2E";
        var projectRoot = GetPathOption(args, "--project-root") ?? Environment.CurrentDirectory;
        var fixtureRoot = Path.Combine(projectRoot, "tests", "SharpCpp.Tests.E2E");
        var parser = new ClangSharpHeaderParser();
        var compilation = parser.Parse(
            new ClangSharpParseRequest(
                ProjectName: "SharpCpp.Tests.E2E",
                ManagedNamespace: "SharpCpp.Tests.E2E.Generated",
                ManagedClassName: "E2EBindings",
                NativeLibraryName: "SharpCppTestsE2E_Native",
                HeaderFilePath: Path.Combine(fixtureRoot, "SampleRuntime", "SampleRuntime.hpp"),
                HeaderIncludePath: "SampleRuntime/SampleRuntime.hpp",
                ClangArguments: new[]
                {
                    "-xc++",
                    "-std=c++20",
                    $"-I{fixtureRoot}"
                },
                EntryPointPrefix: "sharpcpp_e2e"));
        var renderedBindings = BindingOutputRenderer.Render(compilation, fileStem);
        var metadataDocument = BindingMetadataExporter.Export(
            compilation,
            new BindingMetadataExportOptions(
                PackageId: compilation.ProjectName,
                Phase2ClosureHash: "e2e-sample-phase2",
                ClangArgsHash: "e2e-sample-clang"));

        Directory.CreateDirectory(generatedRoot);
        renderedBindings.WriteToDirectory(generatedRoot);
        File.WriteAllText(
            Path.Combine(generatedRoot, $"{fileStem}.Bindings.json"),
            BindingMetadataSerializer.Serialize(metadataDocument));
        Console.WriteLine($"Wrote sample bindings to {generatedRoot}");
        return 0;
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'.");
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("SharpCpp bootstrap tool");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  sample-metadata [output-path]   Emit deterministic sample metadata.");
        Console.WriteLine("  generate-sample --generated-root <dir> [--project-root <dir>] [--file-stem <stem>]   Emit sample bindings artifacts.");
    }

    private static string? GetOptionValue(IReadOnlyList<string> args, string name)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.Ordinal))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static string? GetPathOption(IReadOnlyList<string> args, string name)
    {
        var value = GetOptionValue(args, name);
        return string.IsNullOrWhiteSpace(value) ? null : Path.GetFullPath(value);
    }
}
