using SharpCpp.Metadata.Export;

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
            _ => UnknownCommand(args[0])
        };
    }

    private static int EmitSampleMetadata(string[] args)
    {
        var compilation = SampleCompilationFactory.Create();
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
    }
}
