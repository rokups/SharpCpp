using System.Text;
using Microsoft.CodeAnalysis;

namespace SharpCpp.ApiUnion;

/// <summary>
/// Builds the union of the public API of several platform builds of one assembly.
/// Usage: SharpCpp.ApiUnion report [--output file] platform=assembly.dll platform=assembly.dll ...
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        try { return Run(args); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException)
        {
            Console.Error.WriteLine($"Error: {exception.Message}");
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        if (args.Length > 0 && args[0] == "prepare")
            return Prepare(args);
        if (args.Length < 2 || args[0] is not ("report" or "emit" or "validate"))
        {
            Console.Error.WriteLine("Usage: SharpCpp.ApiUnion report|emit [--output <path>] [--references <directory>] <rid>=<assembly.dll> ...");
            return 2;
        }

        string? outputPath = null;
        var surfaces = new List<ApiSurface>();
        var inputs = new Dictionary<string, string>(StringComparer.Ordinal);
        var references = new List<string>();
        for (var index = 1; index < args.Length; index++)
        {
            if (args[index] == "--output" && index + 1 < args.Length)
            {
                outputPath = args[++index];
                continue;
            }

            if (args[index] == "--references" && index + 1 < args.Length)
            {
                references.AddRange(Directory.GetFiles(args[++index], "*.dll"));
                continue;
            }

            var separator = args[index].IndexOf('=');
            if (separator <= 0)
            {
                Console.Error.WriteLine($"Error: expected <platform>=<assembly.dll>, got '{args[index]}'.");
                return 2;
            }

            var platform = args[index][..separator];
            if (platform.Contains("..", StringComparison.Ordinal) || platform != Path.GetFileName(platform))
                throw new ArgumentException($"Invalid platform identifier: {platform}.");
            var path = args[index][(separator + 1)..];
            inputs.Add(platform, path);
            surfaces.Add(ApiSurfaceReader.Read(platform, path));
        }

        if (args[0] == "emit")
        {
            if (outputPath is null || references.Count == 0)
                throw new ArgumentException("emit requires --output and --references (framework and dependency assemblies).");
            new SourceEmitter(inputs, references).Emit(outputPath);
            return 0;
        }

        var union = ApiUnionResult.Build(surfaces);
        if (args[0] == "validate")
        {
            var reference = surfaces[0];
            var failures = 0;
            foreach (var implementation in surfaces.Skip(1))
            foreach (var member in reference.Members)
            {
                if (!implementation.MembersByKey.TryGetValue(member.Key, out var actual) ||
                    member.Kind == ApiMemberKind.Method &&
                    member.Details.Split(';')[0] != actual.Details.Split(';')[0] ||
                    member.Kind == ApiMemberKind.Property && member.Details.Split(';', StringSplitOptions.RemoveEmptyEntries)
                        .Except(actual.Details.Split(';', StringSplitOptions.RemoveEmptyEntries)).Any() ||
                    member.Kind == ApiMemberKind.Field && member.Details != actual.Details)
                {
                    Console.Error.WriteLine($"{implementation.Platform}: incompatible or missing {member.Key}");
                    failures++;
                }
            }
            Console.WriteLine($"Validated {reference.Members.Count} reference members against {surfaces.Count - 1} implementations: {failures} failures.");
            return failures == 0 ? 0 : 1;
        }
        var report = UnionReport.Render(union);
        if (outputPath is null)
        {
            Console.Out.Write(report);
        }
        else
        {
            File.WriteAllText(outputPath, report, new UTF8Encoding(false));
        }

        return union.Members.Any(member => member.HasDetailConflict) ? 1 : 0;
    }

    private static int Prepare(string[] args)
    {
        string? output = null, stubs = null, concrete = null;
        var sources = new List<string>();
        for (var index = 1; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--output": output = args[++index]; break;
                case "--stubs": stubs = args[++index]; break;
                case "--concrete": concrete = args[++index]; break;
                case "--sources": sources.Add(args[++index]); break;
                default: throw new ArgumentException($"Unexpected prepare option {args[index]}.");
            }
        }
        if (output is null || stubs is null || concrete is null || sources.Count == 0)
            throw new ArgumentException("prepare requires --output, --stubs, --concrete and --sources.");
        var options = new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions(Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview);
        var originals = sources.SelectMany(directory => Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories))
            .Order(StringComparer.Ordinal).Select(path => Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(File.ReadAllText(path), options, path)).ToList();
        var stubTree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(File.ReadAllText(stubs), options, stubs);
        var prepared = SourceAugmenter.Augment(originals, stubTree, File.ReadAllLines(concrete).ToHashSet());
        Directory.CreateDirectory(output);
        for (var index = 0; index < prepared.Count; index++)
            File.WriteAllText(Path.Combine(output, index.ToString("D4") + ".cs"), prepared[index].GetRoot().NormalizeWhitespace().ToFullString());
        return 0;
    }
}
