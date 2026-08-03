using SharpCpp.Metadata.Export;
using SharpCpp.Metadata.Import;
using SharpCpp.Model.Ast;
using SharpCpp.Parsing;
using SharpCpp.Pipeline;
using SharpCpp.Rendering;
using Microsoft.Extensions.Logging;
using SharpCpp.Model.Logging;

namespace SharpCpp.Tool;

internal static class Program
{
    private static readonly ILogger Logger = SharpCppLog.For(nameof(Program));

    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintUsage();
            return 0;
        }

        SharpCppLog.Configure();

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
        Logger.LogInformation("Wrote sample metadata to {OutputPath}", outputPath);
        return 0;
    }

    private static int GenerateSampleBindings(string[] args)
    {
        var generatedRoot = GetPathOption(args, "--generated-root");
        if (string.IsNullOrWhiteSpace(generatedRoot))
        {
            Logger.LogError("Missing required option '--generated-root'.");
            PrintUsage();
            return 1;
        }

        var logFilePath = GetPathOption(args, "--log-file");
        using var logWriter = string.IsNullOrWhiteSpace(logFilePath) ? null
            : new StreamWriter(logFilePath, append: false) { AutoFlush = true };
        SharpCppLog.Configure(fileWriter: logWriter);

        var fileStem = GetOptionValue(args, "--file-stem") ?? "SharpCppTestsE2E";
        var projectRoot = GetPathOption(args, "--project-root") ?? Environment.CurrentDirectory;
        var fixtureRoot = Path.Combine(projectRoot, "tests", "SharpCpp.Tests.E2E");
        var parser = new ClangSharpHeaderParser();
        var clangArguments = new List<string>
        {
            "-xc++",
            "-std=c++20",
            "-Wno-unknown-attributes",
            $"-I{Path.Combine(projectRoot, "include")}",
            $"-I{fixtureRoot}"
        };
        clangArguments.AddRange(GetSystemCppIncludeArguments());
        var marshallerConfigurationFiles = new[]
        {
            Path.Combine(projectRoot, "include", "SharpCpp", "Marshaller.hpp")
        }
        .Concat(Directory.EnumerateFiles(fixtureRoot, "*Marshallers.h", SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.Ordinal))
        .ToArray();

        var compilation = new BindingCompilation(
            "SharpCpp.Tests.E2E",
            "SharpCpp.Tests.E2E.Generated",
            "E2EBindings",
            "SharpCppTestsE2E_Native",
            Array.Empty<string>());
        foreach (var marshallerHeader in marshallerConfigurationFiles)
        {
            compilation.NativeSupportHeaders.Add(marshallerHeader.Replace('\\', '/'));
        }

        var headerFiles = Directory
            .EnumerateFiles(fixtureRoot, "*.hpp", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(fixtureRoot, path).StartsWith($"Coverage{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        foreach (var headerFilePath in headerFiles)
        {
            var relativeHeaderPath = Path.GetRelativePath(fixtureRoot, headerFilePath).Replace('\\', '/');
            var parsedHeaderCompilation = parser.Parse(
                new ClangSharpParseRequest(
                    ProjectName: compilation.ProjectName,
                    ManagedNamespace: compilation.ManagedNamespace,
                    ManagedClassName: compilation.ManagedClassName,
                    NativeLibraryName: compilation.NativeLibraryName,
                    HeaderFilePath: headerFilePath,
                    HeaderIncludePath: relativeHeaderPath,
                    ClangArguments: clangArguments,
                    MarshallerConfigurationFiles: marshallerConfigurationFiles,
                    EntryPointPrefix: "sharpcpp_e2e"));

            compilation.HeaderFiles.Add(relativeHeaderPath);
            foreach (var member in parsedHeaderCompilation.Members.ToArray())
            {
                compilation.Members.Add(member);
            }
        }

        BindingCompilationBootstrapPipeline.Apply(compilation, marshallerConfigurationFiles, clangArguments);

        var generatedFiles = BindingOutputRenderer.Render(compilation, fileStem, headerPath =>
        {
            if (string.IsNullOrEmpty(headerPath))
                return $"{fileStem}.Bindings";
            var group = Path.ChangeExtension(headerPath, null).Replace('/', '.').Replace('\\', '.');
            return $"{fileStem}.{group}";
        }).Files.ToList();
        var metadataDocument = BindingMetadataExporter.Export(
            compilation,
            new BindingMetadataExportOptions(
                PackageId: compilation.ProjectName,
                Phase2ClosureHash: "e2e-sample-phase2",
                ClangArgsHash: "e2e-sample-clang"));

        Directory.CreateDirectory(generatedRoot);
        File.WriteAllText(
            Path.Combine(generatedRoot, $"{fileStem}.Bindings.json"),
            BindingMetadataSerializer.Serialize(metadataDocument));
        generatedFiles.AddRange(GenerateCoverageBindings(
            fixtureRoot,
            generatedRoot,
            parser,
            clangArguments,
            marshallerConfigurationFiles));
        new GeneratedBindingSet(generatedFiles).WriteToDirectory(generatedRoot);
        Logger.LogInformation("Wrote sample bindings to {GeneratedRoot}", generatedRoot);
        return 0;
    }

    private static IReadOnlyList<GeneratedBindingFile> GenerateCoverageBindings(
        string fixtureRoot,
        string generatedRoot,
        ClangSharpHeaderParser parser,
        IReadOnlyList<string> clangArguments,
        IReadOnlyList<string> marshallerConfigurationFiles)
    {
        var dependency = ParseCoverageHeader(
            "SharpCpp.Tests.E2E.Coverage.Dependency",
            "SharpCpp.Tests.E2E.Coverage.Dependency",
            "DependencyBindings",
            "Coverage/Dependency.hpp",
            "sharpcpp_coverage_dependency",
            fixtureRoot,
            parser,
            clangArguments,
            marshallerConfigurationFiles);
        BindingCompilationBootstrapPipeline.Apply(dependency, marshallerConfigurationFiles, clangArguments);
        GenerateAccessorPropertiesPass.Apply(dependency);
        ApplyCoverageProjection(dependency);

        var dependencyMetadata = BindingMetadataExporter.Export(
            dependency,
            new BindingMetadataExportOptions(dependency.ProjectName, "coverage-dependency", "coverage-clang"));
        var dependencyMetadataPath = Path.Combine(generatedRoot, "SharpCppCoverageDependency.Bindings.json");
        File.WriteAllText(dependencyMetadataPath, BindingMetadataSerializer.Serialize(dependencyMetadata));
        RemoveSharedWrapperRecords(dependency);

        var consumer = ParseCoverageHeader(
            "SharpCpp.Tests.E2E.Coverage.Consumer",
            "SharpCpp.Tests.E2E.Coverage.Consumer",
            "ConsumerBindings",
            "Coverage/Consumer.hpp",
            "sharpcpp_coverage_consumer",
            fixtureRoot,
            parser,
            clangArguments,
            marshallerConfigurationFiles,
            BindingMetadataImportCatalog.Load([dependencyMetadataPath]));
        BindingCompilationBootstrapPipeline.Apply(consumer, marshallerConfigurationFiles, clangArguments);
        ApplyCoverageProjection(consumer);
        consumer.TypeAliases.Clear();
        RemoveSharedWrapperRecords(consumer);

        return BindingOutputRenderer.Render(dependency, "SharpCppCoverageDependency", includeCallbackReleaseRegistration: false).Files
            .Concat(BindingOutputRenderer.Render(consumer, "SharpCppCoverageConsumer", includeCallbackReleaseRegistration: false).Files)
            .ToArray();
    }

    private static BindingCompilation ParseCoverageHeader(
        string projectName,
        string managedNamespace,
        string managedClassName,
        string relativeHeaderPath,
        string entryPointPrefix,
        string fixtureRoot,
        ClangSharpHeaderParser parser,
        IReadOnlyList<string> clangArguments,
        IReadOnlyList<string> marshallerConfigurationFiles,
        BindingMetadataImportCatalog? importedMetadata = null)
    {
        var compilation = parser.Parse(new ClangSharpParseRequest(
            projectName,
            managedNamespace,
            managedClassName,
            "SharpCppTestsE2E_Native",
            Path.Combine(fixtureRoot, relativeHeaderPath),
            relativeHeaderPath,
            clangArguments,
            marshallerConfigurationFiles,
            EntryPointPrefix: entryPointPrefix,
            ImportedBindingMetadata: importedMetadata));
        foreach (var marshallerHeader in marshallerConfigurationFiles)
            compilation.NativeSupportHeaders.Add(marshallerHeader.Replace('\\', '/'));
        return compilation;
    }

    private static void ApplyCoverageProjection(BindingCompilation compilation)
    {
        var projection = new ManagedBindingProjection(compilation.ManagedNamespace);
        foreach (var record in compilation.Records.Where(record => !record.GetMetadata<bool>(MetadataKeys.SuppressEmission)))
            projection.AddDeclaration(compilation.ManagedNamespace, record);
        foreach (var enumBinding in compilation.Enums.Where(enumBinding => !enumBinding.GetMetadata<bool>(MetadataKeys.SuppressEmission)))
            projection.AddDeclaration(compilation.ManagedNamespace, enumBinding);
        compilation.SetMetadata(MetadataKeys.ManagedBindingProjection, projection);
    }

    private static void RemoveSharedWrapperRecords(BindingCompilation compilation)
    {
        foreach (var record in compilation.Records
                     .Where(record => record.HasMetadata(MetadataKeys.ManagedWrapperAssembly))
                     .ToArray())
            compilation.Members.Remove(record);
        compilation.InvalidateCaches();
    }

    private static int UnknownCommand(string command)
    {
        Logger.LogError("Unknown command '{Command}'.", command);
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("SharpCpp bootstrap tool");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  sample-metadata [output-path]   Emit deterministic sample metadata.");
        Console.WriteLine("  generate-sample --generated-root <dir> [--project-root <dir>] [--file-stem <stem>] [--log-file <path>]   Emit sample bindings artifacts.");
    }

    private static IEnumerable<string> GetSystemCppIncludeArguments()
    {
        foreach (var includePath in GetSystemCppIncludePaths())
        {
            yield return $"-I{includePath}";
        }
    }

    private static IEnumerable<string> GetSystemCppIncludePaths()
    {
        var cppRoot = "/usr/include/c++";
        var cppVersionDir = Directory.Exists(cppRoot)
            ? Directory.EnumerateDirectories(cppRoot)
                .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
                .FirstOrDefault()
            : null;

        if (!string.IsNullOrWhiteSpace(cppVersionDir))
        {
            yield return cppVersionDir;

            var targetDir = Path.Combine(cppVersionDir, "x86_64-pc-linux-gnu");
            if (Directory.Exists(targetDir))
            {
                yield return targetDir;
            }

            var backwardDir = Path.Combine(cppVersionDir, "backward");
            if (Directory.Exists(backwardDir))
            {
                yield return backwardDir;
            }
        }

        var gccRoot = "/usr/lib/gcc/x86_64-pc-linux-gnu";
        var gccIncludeDir = Directory.Exists(gccRoot)
            ? Directory.EnumerateDirectories(gccRoot)
                .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
                .Select(path => Path.Combine(path, "include"))
                .FirstOrDefault(Directory.Exists)
            : null;
        if (!string.IsNullOrWhiteSpace(gccIncludeDir))
        {
            yield return gccIncludeDir;
        }

        yield return "/usr/include";
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
