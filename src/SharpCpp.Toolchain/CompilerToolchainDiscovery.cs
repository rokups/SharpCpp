namespace SharpCpp.Toolchain;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

/// <summary>
/// Discovers system include directories, target triple and a compatible Clang resource
/// directory by querying the compiler a native project is built with, so generator parses
/// see the same headers as the native build on every host and target. For MSVC <c>cl.exe</c>
/// the target triple comes from the compiler directory and the include directories come from
/// the <c>INCLUDE</c> environment variable.
/// </summary>
public static class CompilerToolchainDiscovery
{
    private const string FrameworkDirectorySuffix = " (framework directory)";
    private static readonly TimeSpan DefaultProcessTimeout = TimeSpan.FromSeconds(30);

    /// <param name="clangResourceDirectoryOverride">
    /// Resource directory to use instead of the compiler's (callers typically read it from
    /// an environment variable of their own).
    /// </param>
    public static CompilerToolchainInfo Discover(
        string compilerPath,
        string? targetTripleOverride = null,
        string? sysroot = null,
        string? clangResourceDirectoryOverride = null)
    {
        if (string.IsNullOrWhiteSpace(compilerPath))
            return new CompilerToolchainInfo(Array.Empty<string>(), null, null, null, null);

        var parserClangMajorVersion = DiscoverParserClangMajorVersion();
        var toolchainClangCompilerPath = ResolveToolchainCompiler(compilerPath, parserClangMajorVersion);
        var isMsvcCompiler = LooksLikeMsvcCompiler(compilerPath);
        var targetTriple = string.IsNullOrWhiteSpace(targetTripleOverride)
            ? (isMsvcCompiler ? DeriveMsvcTargetTriple(compilerPath) : DiscoverTargetTriple(compilerPath))
            : targetTripleOverride;
        var requiresTargetToolchainResourceDirectory =
            targetTriple?.Contains("android", StringComparison.OrdinalIgnoreCase) == true;
        var useRequestedCompilerIncludes =
            targetTriple?.StartsWith("wasm", StringComparison.OrdinalIgnoreCase) == true ||
            targetTriple?.Contains("windows", StringComparison.OrdinalIgnoreCase) == true ||
            targetTriple?.Contains("mingw", StringComparison.OrdinalIgnoreCase) == true ||
            requiresTargetToolchainResourceDirectory;
        var includeCompilerPath = useRequestedCompilerIncludes
            ? compilerPath
            : toolchainClangCompilerPath;
        var includeDirectories = isMsvcCompiler
            ? ReadMsvcIncludeEnvironment()
            : DiscoverVerboseSearchPaths(includeCompilerPath, targetTriple, sysroot);
        var clangResourceDirectory = NormalizeExistingDirectory(clangResourceDirectoryOverride);
        var usesResourceDirectoryOverride = clangResourceDirectory is not null;
        var resourceCompilerPath = requiresTargetToolchainResourceDirectory
            ? compilerPath
            : toolchainClangCompilerPath;

        if (clangResourceDirectory is null && LooksLikeClangCompiler(resourceCompilerPath))
            clangResourceDirectory = DiscoverClangResourceDirectory(resourceCompilerPath);

        string? ignoredClangResourceDirectory = null;
        if (!usesResourceDirectoryOverride &&
            !requiresTargetToolchainResourceDirectory &&
            !string.IsNullOrWhiteSpace(clangResourceDirectory) &&
            parserClangMajorVersion is int parserClangMajor &&
            TryGetClangMajorVersionFromResourceDirectory(clangResourceDirectory, out var resourceClangMajor) &&
            resourceClangMajor != parserClangMajor)
        {
            ignoredClangResourceDirectory = clangResourceDirectory;
            clangResourceDirectory = null;
        }

        if (!isMsvcCompiler &&
            clangResourceDirectory is null &&
            !string.Equals(includeCompilerPath, compilerPath, StringComparison.Ordinal))
        {
            includeDirectories = DiscoverVerboseSearchPaths(compilerPath, targetTriple, sysroot);
        }

        includeDirectories = FilterClangBuiltinIncludeDirectories(
            includeDirectories,
            clangResourceDirectory,
            ignoredClangResourceDirectory);

        var existingIncludeDirectories = includeDirectories.Where(Directory.Exists).Distinct(StringComparer.Ordinal).ToArray();
        return new CompilerToolchainInfo(
            existingIncludeDirectories,
            targetTriple,
            clangResourceDirectory,
            parserClangMajorVersion,
            ignoredClangResourceDirectory,
            CompilerToolchainInfo.IsAppleTriple(targetTriple)
                ? DiscoverAppleSdkRoot(existingIncludeDirectories, sysroot)
                : null);
    }

    /// <summary>
    /// Finds the Apple SDK root: an explicit sysroot has priority, otherwise the root is derived
    /// from the discovered <c>*.sdk/usr/include</c> directory.
    /// </summary>
    private static string? DiscoverAppleSdkRoot(IReadOnlyList<string> includeDirectories, string? sysroot)
    {
        if (!string.IsNullOrWhiteSpace(sysroot))
            return NormalizePath(sysroot);

        const string sdkIncludeSuffix = ".sdk/usr/include";
        foreach (var includeDirectory in includeDirectories)
        {
            var normalizedPath = NormalizePath(includeDirectory)
                .Replace('\\', '/')
                .TrimEnd('/');
            if (normalizedPath.EndsWith(sdkIncludeSuffix, StringComparison.OrdinalIgnoreCase))
                return normalizedPath[..^"/usr/include".Length];
        }

        return null;
    }

    private static string ResolveToolchainCompiler(string compilerPath, int? parserClangMajorVersion)
    {
        if (LooksLikeClangCompiler(compilerPath))
        {
            if (parserClangMajorVersion is not int parserClangMajor)
                return compilerPath;

            var resourceDirectory = DiscoverClangResourceDirectory(compilerPath);
            if (resourceDirectory is not null &&
                TryGetClangMajorVersionFromResourceDirectory(resourceDirectory, out var compilerClangMajor) &&
                compilerClangMajor == parserClangMajor)
            {
                return compilerPath;
            }
        }

        var candidates = new List<string>();
        if (parserClangMajorVersion is int majorVersion)
        {
            candidates.Add($"clang++-{majorVersion}");
            candidates.Add($"clang-{majorVersion}");

            var compilerDirectory = Path.GetDirectoryName(Path.GetFullPath(compilerPath));
            var compilerPrefix = compilerDirectory is null ? null : Directory.GetParent(compilerDirectory)?.FullName;
            if (compilerPrefix is not null)
            {
                candidates.Add(Path.Combine(compilerPrefix, "lib", $"llvm{majorVersion}", "bin", "clang++"));
                candidates.Add(Path.Combine(compilerPrefix, "lib", $"llvm-{majorVersion}", "bin", "clang++"));
                candidates.Add(Path.Combine(compilerPrefix, "lib", $"llvm{majorVersion}", "bin", "clang"));
                candidates.Add(Path.Combine(compilerPrefix, "lib", $"llvm-{majorVersion}", "bin", "clang"));
            }
        }

        candidates.Add("clang++");
        candidates.Add("clang");

        foreach (var candidate in candidates.Distinct(StringComparer.Ordinal))
        {
            if (CommandExists(candidate))
                return candidate;
        }

        return compilerPath;
    }

    private static IReadOnlyList<string> DiscoverVerboseSearchPaths(
        string compilerPath,
        string? targetTriple,
        string? sysroot)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = compilerPath,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };

            if (!string.IsNullOrWhiteSpace(targetTriple) && LooksLikeClangCompiler(compilerPath))
                startInfo.ArgumentList.Add($"--target={targetTriple}");

            if (!string.IsNullOrWhiteSpace(sysroot) && LooksLikeClangCompiler(compilerPath))
                startInfo.ArgumentList.Add($"--sysroot={sysroot}");

            startInfo.ArgumentList.Add("-E");
            startInfo.ArgumentList.Add("-x");
            startInfo.ArgumentList.Add("c++");
            startInfo.ArgumentList.Add("-");
            startInfo.ArgumentList.Add("-v");

            var result = RunProcess(startInfo);
            if (result is not { ExitCode: 0 } completed)
                return Array.Empty<string>();

            var errorOutput = completed.StandardError;

            var includeDirectories = new List<string>();
            var capture = false;
            foreach (var rawLine in errorOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var line = rawLine.Trim();
                if (line.Contains("#include <...> search starts here:", StringComparison.Ordinal) ||
                    line.Contains("#include \"...\" search starts here:", StringComparison.Ordinal))
                {
                    capture = true;
                    continue;
                }

                if (!capture)
                    continue;

                if (line.StartsWith("End of search list.", StringComparison.Ordinal))
                    break;

                if (line.EndsWith(FrameworkDirectorySuffix, StringComparison.Ordinal))
                    continue;

                var normalizedPath = NormalizeSearchPath(line);
                if (!string.IsNullOrWhiteSpace(normalizedPath))
                    includeDirectories.Add(normalizedPath);
            }

            return includeDirectories;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static string? NormalizeExistingDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var normalizedPath = NormalizeResourceDirectory(path);
        return Directory.Exists(normalizedPath) ? normalizedPath : null;
    }

    private static string? DiscoverClangResourceDirectory(string compilerPath)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = compilerPath,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("-print-resource-dir");

            var result = RunProcess(startInfo);
            if (result is not { ExitCode: 0 } completed)
                return null;

            var standardOutput = completed.StandardOutput.Trim();
            if (string.IsNullOrWhiteSpace(standardOutput))
                return null;

            var resourceDirectory = NormalizeResourceDirectory(standardOutput);
            return Directory.Exists(Path.Combine(resourceDirectory, "include")) ? resourceDirectory : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? DiscoverTargetTriple(string compilerPath)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = compilerPath,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("-dumpmachine");

            var result = RunProcess(startInfo);
            if (result is not { ExitCode: 0 } completed)
                return null;

            var standardOutput = completed.StandardOutput.Trim();
            return !string.IsNullOrWhiteSpace(standardOutput) ? standardOutput : null;
        }
        catch
        {
            return null;
        }
    }

    private static int? DiscoverParserClangMajorVersion()
    {
        try
        {
            var clangSharpAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(static assembly => string.Equals(assembly.GetName().Name, "ClangSharp", StringComparison.Ordinal))
                ?? Assembly.Load("ClangSharp");
            return clangSharpAssembly.GetName().Version?.Major;
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<string> FilterClangBuiltinIncludeDirectories(
        IReadOnlyList<string> includeDirectories,
        string? clangResourceDirectory,
        string? ignoredClangResourceDirectory)
    {
        if (includeDirectories.Count == 0)
            return includeDirectories;

        var keptIncludeDirectories = new List<string>(includeDirectories.Count);
        foreach (var includeDirectory in includeDirectories)
        {
            if (string.IsNullOrWhiteSpace(includeDirectory))
                continue;

            if (string.IsNullOrWhiteSpace(clangResourceDirectory) && string.IsNullOrWhiteSpace(ignoredClangResourceDirectory))
            {
                keptIncludeDirectories.Add(includeDirectory);
                continue;
            }

            if (LooksLikeGccBuiltinIncludeDirectory(includeDirectory))
                continue;

            if (!LooksLikeClangBuiltinIncludeDirectory(includeDirectory))
            {
                keptIncludeDirectories.Add(includeDirectory);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(clangResourceDirectory) &&
                IsPathUnderDirectory(includeDirectory, Path.Combine(clangResourceDirectory, "include")))
            {
                keptIncludeDirectories.Add(includeDirectory);
            }
        }

        return keptIncludeDirectories;
    }

    private static bool LooksLikeClangBuiltinIncludeDirectory(string path)
    {
        var normalizedPath = NormalizePath(path).Replace('\\', '/');
        return normalizedPath.Contains("/lib/clang/", StringComparison.Ordinal) ||
               normalizedPath.Contains("/lib64/clang/", StringComparison.Ordinal);
    }

    private static bool LooksLikeGccBuiltinIncludeDirectory(string path)
    {
        var normalizedPath = NormalizePath(path).Replace('\\', '/');
        var marker = normalizedPath.Contains("/lib/gcc/", StringComparison.Ordinal)
            ? "/lib/gcc/"
            : "/lib64/gcc/";
        var markerIndex = normalizedPath.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
            return false;

        var segments = normalizedPath[(markerIndex + marker.Length)..].Split('/');
        return segments.Length >= 3 && segments[2] is "include" or "include-fixed";
    }

    internal static bool IsPathUnderDirectory(string path, string candidateRoot)
    {
        var normalizedPath = NormalizePath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedRoot = NormalizePath(candidateRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
               string.Equals(normalizedPath, normalizedRoot, StringComparison.Ordinal);
    }

    private static bool TryGetClangMajorVersionFromResourceDirectory(string resourceDirectory, out int majorVersion)
    {
        var normalizedResourceDirectory = NormalizeResourceDirectory(resourceDirectory).Replace('\\', '/');
        var marker = "/lib/clang/";
        var markerIndex = normalizedResourceDirectory.LastIndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            majorVersion = 0;
            return false;
        }

        var versionStart = markerIndex + marker.Length;
        var versionEnd = normalizedResourceDirectory.IndexOf('/', versionStart);
        var versionText = versionEnd >= 0
            ? normalizedResourceDirectory[versionStart..versionEnd]
            : normalizedResourceDirectory[versionStart..];

        var majorText = versionText.Split('.', 2)[0];
        return int.TryParse(majorText, out majorVersion);
    }

    private static bool CommandExists(string command)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = command,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("--version");

            return RunProcess(startInfo) is not null;
        }
        catch
        {
            return false;
        }
    }

    private static (int ExitCode, string StandardOutput, string StandardError)? RunProcess(
        ProcessStartInfo startInfo,
        TimeSpan? timeout = null)
    {
        startInfo.RedirectStandardInput = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.UseShellExecute = false;

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
                return null;

            process.StandardInput.Close();
            var standardOutputTask = process.StandardOutput.ReadToEndAsync();
            var standardErrorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeout ?? DefaultProcessTimeout))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }

            return (process.ExitCode, standardOutputTask.GetAwaiter().GetResult(), standardErrorTask.GetAwaiter().GetResult());
        }
        catch
        {
            return null;
        }
    }

    private static bool LooksLikeClangCompiler(string compilerPath)
    {
        var fileName = Path.GetFileNameWithoutExtension(compilerPath);
        return fileName.Contains("clang", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeMsvcCompiler(string compilerPath)
    {
        var fileName = Path.GetFileNameWithoutExtension(compilerPath);
        return string.Equals(fileName, "cl", StringComparison.OrdinalIgnoreCase);
    }

    private static string DeriveMsvcTargetTriple(string compilerPath)
    {
        var directoryName = Path.GetFileName(
            Path.GetDirectoryName(compilerPath)?.TrimEnd('/', '\\') ?? string.Empty);

        if (string.Equals(directoryName, "x64", StringComparison.OrdinalIgnoreCase))
            return "x86_64-pc-windows-msvc";
        if (string.Equals(directoryName, "x86", StringComparison.OrdinalIgnoreCase))
            return "i686-pc-windows-msvc";
        if (string.Equals(directoryName, "arm64", StringComparison.OrdinalIgnoreCase))
            return "aarch64-pc-windows-msvc";

        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X86 => "i686-pc-windows-msvc",
            Architecture.Arm64 => "aarch64-pc-windows-msvc",
            _ => "x86_64-pc-windows-msvc",
        };
    }

    private static IReadOnlyList<string> ReadMsvcIncludeEnvironment()
    {
        var include = Environment.GetEnvironmentVariable("INCLUDE");
        if (string.IsNullOrWhiteSpace(include))
            return Array.Empty<string>();

        return include
            .Split(';')
            .Select(static entry => entry.Trim())
            .Where(static entry => entry.Length > 0)
            .ToArray();
    }

    private static string NormalizeSearchPath(string path)
    {
        return NormalizePath(path);
    }

    private static string NormalizeResourceDirectory(string resourceDirectory)
    {
        return NormalizePath(resourceDirectory);
    }

    private static string NormalizePath(string path)
    {
        var trimmedPath = path.Trim();
        return string.IsNullOrWhiteSpace(trimmedPath) ? trimmedPath : Path.GetFullPath(trimmedPath);
    }
}

public sealed record CompilerToolchainInfo(
    IReadOnlyList<string> IncludeDirectories,
    string? TargetTriple,
    string? ClangResourceDirectory,
    int? ParserClangMajorVersion,
    string? IgnoredClangResourceDirectory,
    string? AppleSdkRoot = null)
{
    public bool IsMinGwTarget =>
        TargetTriple?.Contains("windows-gnu", StringComparison.OrdinalIgnoreCase) == true ||
        TargetTriple?.Contains("mingw", StringComparison.OrdinalIgnoreCase) == true;

    public bool IsMsvcTarget => IsMsvcTriple(TargetTriple);

    internal static bool IsMsvcTriple(string? targetTriple) =>
        targetTriple?.Contains("windows-msvc", StringComparison.OrdinalIgnoreCase) == true;

    public bool IsAppleTarget => IsAppleTriple(TargetTriple);

    internal static bool IsAppleTriple(string? targetTriple) =>
        targetTriple?.Contains("apple", StringComparison.OrdinalIgnoreCase) == true ||
        targetTriple?.Contains("darwin", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Clang arguments that make a parse see the toolchain's target and system headers.
    /// Arguments already present in <paramref name="existingArguments"/> are not repeated.
    /// </summary>
    public IReadOnlyList<string> GetClangArguments(IReadOnlyCollection<string>? existingArguments = null, string? sysroot = null)
    {
        existingArguments ??= Array.Empty<string>();
        var arguments = new List<string>();

        // Bundled libclang otherwise auto-detects incompatible GCC intrinsic headers.
        if (IsMinGwTarget)
            arguments.Add("-nostdinc");

        if (!string.IsNullOrWhiteSpace(TargetTriple) &&
            !existingArguments.Any(static argument => argument.StartsWith("--target=", StringComparison.Ordinal) || argument == "-target"))
        {
            arguments.Add($"--target={TargetTriple}");
        }

        if (!string.IsNullOrWhiteSpace(sysroot))
            arguments.Add($"--sysroot={sysroot}");

        // The driver orders SDK headers after the builtin ones only when it derives them from the
        // SDK root. A plain -isystem of the SDK include directory hides the builtin stddef.h.
        var sdkRoot = IsAppleTarget ? AppleSdkRoot : null;
        if (!string.IsNullOrWhiteSpace(sdkRoot) &&
            string.IsNullOrWhiteSpace(sysroot) &&
            !existingArguments.Any(static argument => argument.StartsWith("-isysroot", StringComparison.Ordinal) || argument.StartsWith("--sysroot", StringComparison.Ordinal)))
        {
            arguments.Add("-isysroot");
            arguments.Add(sdkRoot);
        }

        if (!string.IsNullOrWhiteSpace(ClangResourceDirectory) &&
            !existingArguments.Any(static argument => argument.StartsWith("-resource-dir=", StringComparison.Ordinal) || argument == "-resource-dir"))
        {
            arguments.Add($"-resource-dir={ClangResourceDirectory}");
            if (IsMinGwTarget)
                arguments.Add($"-isystem{Path.Combine(ClangResourceDirectory, "include")}");
        }

        foreach (var includeDirectory in IncludeDirectories)
        {
            if (!string.IsNullOrWhiteSpace(sdkRoot) && CompilerToolchainDiscovery.IsPathUnderDirectory(includeDirectory, sdkRoot))
                continue;

            arguments.Add($"-isystem{includeDirectory}");
        }

        return arguments;
    }
}
