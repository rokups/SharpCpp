using SharpCpp.Metadata.Export;
using SharpCpp.Metadata.Import;
using SharpCpp.Model.Ast;
using SharpCpp.Parsing;
using SharpCpp.Pipeline;
using SharpCpp.Rendering;

namespace Rbfx.UserCode.Generator;

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

    private const string ProjectName = "Rbfx.UserCode.Bindings";
    private const string ManagedNamespace = "RbfxUserCode";
    private const string ManagedClassName = "Bindings";
    private const string NativeLibraryName = "SharpCpp.Rbfx.UserCode.Native";
    private const string GeneratedFileStem = "Rbfx.UserCode";
    private const string EntryPointPrefix = "scrbfxuc";
    private const string HeaderIncludePath = "RbfxUserCode/RotatingComponent.hpp";

    public static int Main(string[] args)
    {
        var options = GeneratorOptions.Parse(args);
        using var logWriter = CreateLogWriter(options.LogFilePath);

        try
        {
            var sdkRoot = Path.GetFullPath(options.SdkRoot);
            var includeRoot = Path.Combine(sdkRoot, "include");
            var projectRoot = Environment.CurrentDirectory;
            var userCodeRoot = Path.GetFullPath(options.UserCodeRoot);
            var userIncludeRoot = Path.Combine(userCodeRoot, "include");
            var headerPath = Path.Combine(userIncludeRoot, HeaderIncludePath);
            if (!File.Exists(headerPath))
            {
                throw new InvalidOperationException($"User code header was not found at '{headerPath}'.");
            }

            var marshallerConfigurationFiles = new[]
            {
                Path.Combine(projectRoot, "include", "SharpCpp", "Marshaller.hpp")
            }.Select(Path.GetFullPath).ToArray();

            Log(logWriter, $"rbfx SDK root: {sdkRoot}");
            Log(logWriter, $"Flags file: {options.FlagsFilePath}");
            Log(logWriter, $"User code root: {userCodeRoot}");
            Log(logWriter, $"Generated output root: {options.GeneratedRoot}");

            var importedBindings = BindingMetadataImportCatalog.Load(options.ImportedBindingMetadataFiles);
            foreach (var metadataFile in options.ImportedBindingMetadataFiles)
            {
                Log(logWriter, $"Imported binding metadata: '{metadataFile}'");
            }

            var sdkFlags = SdkParseFlags.Load(options.FlagsFilePath);
            var clangArguments = BuildClangArguments(includeRoot, userIncludeRoot, sdkFlags);
            var parser = new ClangSharpHeaderParser();
            var compilation = parser.Parse(
                new ClangSharpParseRequest(
                    ProjectName,
                    ManagedNamespace,
                    ManagedClassName,
                    NativeLibraryName,
                    headerPath,
                    HeaderIncludePath,
                    clangArguments,
                    marshallerConfigurationFiles,
                    EntryPointPrefix,
                    OwnedFileRoots: new[] { userIncludeRoot },
                    ImportedBindingMetadata: importedBindings));

            compilation.NamespaceMappings = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Urho3D"] = "Urho3D",
                ["RbfxUserCode"] = "RbfxUserCode"
            };

            BindingCompilationBootstrapPipeline.Apply(compilation, marshallerConfigurationFiles, clangArguments);

            var renderedBindings = BindingOutputRenderer.Render(compilation, GeneratedFileStem);
            var metadataDocument = BindingMetadataExporter.Export(
                compilation,
                new BindingMetadataExportOptions(
                    PackageId: ProjectName,
                    Phase2ClosureHash: "rbfx-usercode-phase2",
                    ClangArgsHash: "rbfx-usercode-clang",
                    Imports: importedBindings.Imports));

            Directory.CreateDirectory(options.GeneratedRoot);
            renderedBindings.WriteToDirectory(options.GeneratedRoot);
            File.WriteAllText(
                Path.Combine(options.GeneratedRoot, $"{GeneratedFileStem}.Bindings.json"),
                BindingMetadataSerializer.Serialize(metadataDocument));

            Log(logWriter, $"Generated user-code bindings for {compilation.Records.Count(record => !record.IsIgnored() && !record.GetMetadata<bool>(MetadataKeys.SuppressEmission))} retained local record(s).");
            return 0;
        }
        catch (Exception exception)
        {
            Log(logWriter, exception.ToString());
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static IReadOnlyList<string> BuildClangArguments(string sdkIncludeRoot, string userIncludeRoot, SdkParseFlags sdkFlags)
    {
        var arguments = new List<string>
        {
            "-xc++",
            $"-std={sdkFlags.LanguageStandard}",
            $"-I{userIncludeRoot}"
        };

        arguments.AddRange(ParseWarningDisableArguments.Where(argument => !arguments.Contains(argument, StringComparer.Ordinal)));

        if (!sdkFlags.IncludeDirectories.Contains(sdkIncludeRoot, StringComparer.Ordinal))
        {
            arguments.Add($"-I{sdkIncludeRoot}");
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

        var fullPath = Path.GetFullPath(logFilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        return new StreamWriter(fullPath, append: false, System.Text.Encoding.UTF8) { AutoFlush = true };
    }

    private static void Log(TextWriter? logWriter, string message)
    {
        logWriter?.WriteLine(message);
        if (logWriter is null)
        {
            Console.WriteLine(message);
        }
    }
}
