using System.Text;

namespace SharpCpp.ApiUnion;

/// <summary>
/// Builds the union of the public API of several platform builds of one assembly.
/// Usage: SharpCpp.ApiUnion report [--output file] platform=assembly.dll platform=assembly.dll ...
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 2 || args[0] != "report")
        {
            Console.Error.WriteLine("Usage: SharpCpp.ApiUnion report [--output <file>] <platform>=<assembly.dll> ...");
            return 2;
        }

        string? outputPath = null;
        var surfaces = new List<ApiSurface>();
        for (var index = 1; index < args.Length; index++)
        {
            if (args[index] == "--output" && index + 1 < args.Length)
            {
                outputPath = args[++index];
                continue;
            }

            var separator = args[index].IndexOf('=');
            if (separator <= 0)
            {
                Console.Error.WriteLine($"Error: expected <platform>=<assembly.dll>, got '{args[index]}'.");
                return 2;
            }

            surfaces.Add(ApiSurfaceReader.Read(args[index][..separator], args[index][(separator + 1)..]));
        }

        var union = ApiUnionResult.Build(surfaces);
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
}
