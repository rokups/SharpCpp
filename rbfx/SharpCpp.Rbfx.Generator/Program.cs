using SharpCpp.Metadata.Export;
using SharpCpp.Model.Ast;
using SharpCpp.Parsing;
using SharpCpp.Pipeline;
using SharpCpp.Rendering;

namespace SharpCpp.Rbfx.Generator;

internal static class Program
{
    private static readonly string[] ParseCompatibilityDefines =
    [
        "SDL_DISABLE_MM3DNOW_H=1",
        "SDL_DISABLE_LSX_H=1",
        "SDL_DISABLE_LASX_H=1",
        "SDL_DISABLE_IMMINTRIN_H=1",
        "SDL_DISABLE_MMINTRIN_H=1",
        "SDL_DISABLE_XMMINTRIN_H=1",
        "SDL_DISABLE_EMMINTRIN_H=1",
        "SDL_DISABLE_PMMINTRIN_H=1",
        "IMGUI_DISABLE_SSE=1"
    ];

    private static readonly string[] ParseSimdDisableArguments =
    [
        "-mno-sse",
        "-mno-sse2",
        "-mno-sse3",
        "-mno-ssse3",
        "-mno-sse4.1",
        "-mno-sse4.2",
        "-mno-avx",
        "-mno-avx2",
        "-mno-avx512f",
        "-mno-avx512bw",
        "-mno-avx512cd",
        "-mno-avx512dq",
        "-mno-avx512vl"
    ];

    private static readonly string[] ParseWarningDisableArguments =
    [
        "-Wno-deprecated-literal-operator",
        "-Wno-pragma-once-outside-header"
    ];

    private const string ManagedNamespace = "Urho3D";
    private const string ManagedClassName = "Bindings";
    private const string NativeLibraryName = "SharpCpp.Rbfx.Native";
    private const string ProjectName = "SharpCpp.Rbfx.Bindings";
    private const string GeneratedFileStem = "SharpCpp.Rbfx";
    private const string EntryPointPrefix = "scrbfx";
    private const string NativeOutputHeader = "Urho3D/Urho3DAll.h";

    public static int Main(string[] args)
    {
        var options = GeneratorOptions.Parse(args);
        using var logWriter = CreateLogWriter(options.LogFilePath);

        try
        {
            var sdkRoot = Path.GetFullPath(options.SdkRoot);
            var includeRoot = Path.Combine(sdkRoot, "include");
            var urhoIncludeRoot = Path.Combine(includeRoot, "Urho3D");
            if (!Directory.Exists(urhoIncludeRoot))
            {
                throw new InvalidOperationException($"rbfx SDK include directory was not found at '{urhoIncludeRoot}'.");
            }

            var marshallerConfigurationFiles = new[]
            {
                Path.Combine(Path.GetFullPath(options.ProjectRoot), "include", "SharpCpp", "Marshaller.hpp")
            };

            Log(logWriter, $"rbfx SDK root: {sdkRoot}");
            Log(logWriter, $"SDK flags file: {options.FlagsFilePath}");
            Log(logWriter, $"Generated output root: {options.GeneratedRoot}");

            var sdkFlags = SdkParseFlags.Load(options.FlagsFilePath);
            var clangArguments = BuildClangArguments(includeRoot, sdkFlags);
            var umbrellaHeaderPath = Path.Combine(urhoIncludeRoot, "Urho3DAll.h");
            if (!File.Exists(umbrellaHeaderPath))
            {
                throw new InvalidOperationException($"rbfx umbrella header was not found at '{umbrellaHeaderPath}'.");
            }

            Log(logWriter, $"Parsing umbrella header '{NativeOutputHeader}' with owned declaration root '{urhoIncludeRoot}'.");

            var parser = new ClangSharpHeaderParser();
            var compilation = parser.Parse(
                new ClangSharpParseRequest(
                    ProjectName,
                    ManagedNamespace,
                    ManagedClassName,
                    NativeLibraryName,
                    umbrellaHeaderPath,
                    NativeOutputHeader,
                    clangArguments,
                    marshallerConfigurationFiles,
                    EntryPointPrefix,
                    OwnedFileRoots: new[] { urhoIncludeRoot }));
            compilation.NamespaceMappings = BuildNamespaceMappings(compilation);

            BindingCompilationBootstrapPipeline.Apply(compilation, marshallerConfigurationFiles);
            ApplySdkExportFilter(compilation, options.SdkLibraryPath, logWriter);

            var renderedBindings = BindingOutputRenderer.Render(compilation, GeneratedFileStem);
            var metadataDocument = BindingMetadataExporter.Export(
                compilation,
                new BindingMetadataExportOptions(
                    PackageId: ProjectName,
                    Phase2ClosureHash: "rbfx-bootstrap-phase2",
                    ClangArgsHash: "rbfx-bootstrap-clang"));

            Directory.CreateDirectory(options.GeneratedRoot);
            renderedBindings.WriteToDirectory(options.GeneratedRoot);
            File.WriteAllText(
                Path.Combine(options.GeneratedRoot, $"{GeneratedFileStem}.Bindings.json"),
                BindingMetadataSerializer.Serialize(metadataDocument));

            Log(logWriter, $"Generated rbfx bindings for {compilation.Records.Count(record => !record.IsIgnored())} retained record(s) from {compilation.Records.Count} ingested record declaration(s).");
            return 0;
        }
        catch (Exception exception)
        {
            Log(logWriter, exception.ToString());
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static IReadOnlyList<string> BuildClangArguments(string includeRoot, SdkParseFlags sdkFlags)
    {
        var arguments = new List<string>
        {
            "-xc++",
            $"-std={sdkFlags.LanguageStandard}"
        };

        arguments.AddRange(ParseWarningDisableArguments.Where(argument => !arguments.Contains(argument, StringComparer.Ordinal)));

        if (!sdkFlags.IncludeDirectories.Contains(includeRoot, StringComparer.Ordinal))
        {
            arguments.Add($"-I{includeRoot}");
        }

        foreach (var includeDirectory in sdkFlags.IncludeDirectories)
        {
            arguments.Add($"-I{includeDirectory}");
        }

        foreach (var define in sdkFlags.Defines)
        {
            arguments.Add($"-D{define}");
        }

        foreach (var define in ParseCompatibilityDefines)
        {
            var argument = $"-D{define}";
            if (!arguments.Contains(argument, StringComparer.Ordinal))
            {
                arguments.Add(argument);
            }
        }

        arguments.AddRange(sdkFlags.AdditionalArguments);
        arguments.AddRange(ParseSimdDisableArguments);

        var toolchainInfo = CompilerToolchainDiscovery.Discover(sdkFlags.CompilerPath);
        if (!string.IsNullOrWhiteSpace(toolchainInfo.TargetTriple) &&
            !arguments.Any(static argument => argument.StartsWith("--target=", StringComparison.Ordinal) || argument == "-target"))
        {
            arguments.Add($"--target={toolchainInfo.TargetTriple}");
        }

        if (!string.IsNullOrWhiteSpace(toolchainInfo.ClangResourceDirectory) &&
            !arguments.Any(static argument => argument.StartsWith("-resource-dir=", StringComparison.Ordinal) || argument == "-resource-dir"))
        {
            arguments.Add($"-resource-dir={toolchainInfo.ClangResourceDirectory}");
        }

        foreach (var systemIncludeDirectory in toolchainInfo.IncludeDirectories)
        {
            arguments.Add($"-isystem{systemIncludeDirectory}");
        }

        return arguments.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static StreamWriter? CreateLogWriter(string? logFilePath)
    {
        if (string.IsNullOrWhiteSpace(logFilePath))
        {
            return null;
        }

        var directory = Path.GetDirectoryName(logFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return new StreamWriter(logFilePath, append: false);
    }

    private static void Log(TextWriter? logWriter, string message)
    {
        Console.WriteLine(message);
        if (logWriter is null)
        {
            return;
        }

        logWriter.WriteLine(message);
        logWriter.Flush();
    }

    private static void ApplySdkExportFilter(BindingCompilation compilation, string? sdkLibraryPath, TextWriter? logWriter)
    {
        if (string.IsNullOrWhiteSpace(sdkLibraryPath) || !File.Exists(sdkLibraryPath))
        {
            return;
        }

        var exportedSymbols = LoadExportedSymbols(sdkLibraryPath);
        if (exportedSymbols.Count == 0)
        {
            return;
        }

        var removedCount = 0;
        foreach (var function in compilation.Functions.Where(static function => (function.Parent is null || function.IsStatic) && !function.IsIgnored()))
        {
            if (!function.CppQualifiedName.Contains("::", StringComparison.Ordinal))
            {
                continue;
            }

            if (exportedSymbols.Any(symbol => symbol.Contains($"{function.CppQualifiedName}(", StringComparison.Ordinal)))
            {
                continue;
            }

            function.SetIgnoreReason($"function '{function.CppQualifiedName}' is not exported by the linked rbfx SDK library");
            removedCount++;
        }

        if (removedCount > 0)
        {
            Log(logWriter, $"Ignored {removedCount} top-level function(s) that are not exported by '{sdkLibraryPath}'.");
        }
    }

    private static IReadOnlyList<string> LoadExportedSymbols(string sdkLibraryPath)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "nm",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("-D");
            startInfo.ArgumentList.Add("--defined-only");
            startInfo.ArgumentList.Add("--demangle");
            startInfo.ArgumentList.Add(sdkLibraryPath);

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process is null)
            {
                return Array.Empty<string>();
            }

            var output = process.StandardOutput.ReadToEnd();
            _ = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                return Array.Empty<string>();
            }

            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static IReadOnlyDictionary<string, string> BuildNamespaceMappings(BindingCompilation compilation)
    {
        var mappings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Urho3D"] = "Urho3D"
        };

        foreach (var cppQualifiedName in compilation.Records.Select(static record => record.CppQualifiedName)
                     .Concat(compilation.Enums.Select(static @enum => @enum.CppQualifiedName)))
        {
            var lastSeparator = cppQualifiedName.LastIndexOf("::", StringComparison.Ordinal);
            if (lastSeparator < 0)
            {
                continue;
            }

            var cppNamespace = cppQualifiedName[..lastSeparator];
            if (cppNamespace.StartsWith("Urho3D::", StringComparison.Ordinal) && !mappings.ContainsKey(cppNamespace))
            {
                mappings[cppNamespace] = "Urho3D";
            }
        }

        return mappings;
    }
}
