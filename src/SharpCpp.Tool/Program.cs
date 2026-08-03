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
            "generate-sample" => GenerateSampleBindings(args.Skip(1).ToArray()),
            _ => UnknownCommand(args[0])
        };
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

        ApplyConsumerExtensionCoverage(compilation);
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

    private static void ApplyConsumerExtensionCoverage(BindingCompilation compilation)
    {
        const string headerPath = "CoverageMatrix/ConsumerProjection.hpp";

        foreach (var nativeName in new[]
                 {
                     "consumer_projection_adapt_int",
                     "consumer_projection_adapt_void",
                     "consumer_projection_adapt_value"
                 })
        {
            compilation.Functions.Single(function =>
                    string.Equals(function.CppQualifiedName, nativeName, StringComparison.Ordinal))
                .SetMetadata(MetadataKeys.NativeDefaultArgumentAdapter, true);
        }

        compilation.Functions.Single(function =>
                string.Equals(function.CppQualifiedName, "consumer_projection_function_pointer", StringComparison.Ordinal))
            .SetMetadata(MetadataKeys.ExposeNativeFunctionPointer, true);

        compilation.Records.Single(record =>
                string.Equals(record.CppQualifiedName, "consumer_projection_query_target", StringComparison.Ordinal))
            .SetMetadata(
                MetadataKeys.NativeInterfaceQuery,
                new NativeInterfaceQueryAnnotation("consumer_projection_query_source", "73"));

        var staticProjection = new ManagedClassBinding(
            "managed:consumer-projection:static",
            "consumer_projection",
            "CoverageConsumerProjection")
        {
            HeaderIncludePath = headerPath,
            IsStatic = true,
            IsPartial = false
        };
        staticProjection.Members.Add(new ManagedFieldBinding(
            "managed:consumer-projection:constant",
            "consumer_projection_constant",
            "Constant",
            "int",
            "11")
        {
            IsConst = true
        });
        staticProjection.Members.Add(new ManagedFieldBinding(
            "managed:consumer-projection:readonly",
            "consumer_projection_readonly",
            "ReadOnlyValue",
            "int",
            "13"));
        staticProjection.Members.Add(new ManagedFieldBinding(
            "managed:consumer-projection:mutable",
            "consumer_projection_mutable",
            "MutableValue",
            "int",
            "17")
        {
            IsReadOnly = false
        });
        staticProjection.BodyCodeBlocks.Add(
            "public static int RoundTrip(int value) => global::SharpCpp.Tests.E2E.Generated.E2EBindings.ConsumerProjectionRoundTrip(value);");

        var nestedProjection = new ManagedClassBinding(
            "managed:consumer-projection:nested",
            "consumer_projection_nested",
            "NestedProjection")
        {
            AccessModifier = BindingAccessModifier.Private,
            IsSealed = true,
            BaseTypeName = "global::System.Object"
        };
        nestedProjection.BodyCodeBlocks.Add("public int Value => Constant;");
        staticProjection.Members.Add(nestedProjection);
        compilation.Members.Add(staticProjection);

        var projectedConsumer = new ManagedClassBinding(
            "managed:consumer-projection:consumer",
            "consumer_projection_consumer",
            "ProjectedConsumer")
        {
            HeaderIncludePath = headerPath,
            ManagedNamespace = "SharpCpp.Tests.E2E.ConsumerProjection",
            IsSealed = true,
            BaseTypeName = "global::System.Object"
        };
        projectedConsumer.Members.Add(new ManagedFieldBinding(
            "managed:consumer-projection:offset",
            "consumer_projection_offset",
            "_offset",
            "int")
        {
            AccessModifier = BindingAccessModifier.Private,
            IsStatic = false
        });
        projectedConsumer.BodyCodeBlocks.Add(
            "public ProjectedConsumer(int offset) { _offset = offset; }");
        projectedConsumer.BodyCodeBlocks.Add(
            "public int RoundTrip(int value) => global::SharpCpp.Tests.E2E.Generated.E2EBindings.ConsumerProjectionRoundTrip(value) + _offset;");
        compilation.Members.Add(projectedConsumer);
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
        RemoveLocalConfiguredWrapperRecords(dependency);
        ApplyCoverageProjection(dependency);

        var dependencyMetadata = BindingMetadataExporter.Export(
            dependency,
            new BindingMetadataExportOptions(dependency.ProjectName, "coverage-dependency", "coverage-clang"));
        var dependencyMetadataPath = Path.Combine(generatedRoot, "SharpCppCoverageDependency.Bindings.json");
        File.WriteAllText(dependencyMetadataPath, BindingMetadataSerializer.Serialize(dependencyMetadata));
        var compatibilityDependencyMetadata = CreateCompatibilityDependencyMetadata(dependencyMetadata);
        var compatibilityDependencyMetadataPath = Path.Combine(generatedRoot, "SharpCppCoverageDependency.CompatibilityBindings.json");
        File.WriteAllText(compatibilityDependencyMetadataPath, BindingMetadataSerializer.Serialize(compatibilityDependencyMetadata));
        ExcludeSharedWrapperRecords(dependency);

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
            BindingMetadataImportCatalog.Load([compatibilityDependencyMetadataPath]));
        BindingCompilationBootstrapPipeline.Apply(consumer, marshallerConfigurationFiles, clangArguments);
        RemoveLocalConfiguredWrapperRecords(consumer);
        ApplyCoverageProjection(consumer);
        consumer.TypeAliases.Clear();
        ExcludeSharedWrapperRecords(consumer);

        var degradation = ParseCoverageHeader(
            "SharpCpp.Tests.E2E.Coverage.Degradation",
            "SharpCpp.Tests.E2E.Coverage.Degradation.Generated",
            "DegradationBindings",
            "Coverage/Degradation.hpp",
            "sharpcpp_coverage_degradation",
            fixtureRoot,
            parser,
            clangArguments,
            marshallerConfigurationFiles);
        degradation.NamespaceMappings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["coverage_allowed"] = degradation.ManagedNamespace
        };
        ApplyDegradationCoverage(degradation);
        BindingCompilationBootstrapPipeline.Apply(degradation, marshallerConfigurationFiles, clangArguments);
        RemoveLocalConfiguredWrapperRecords(degradation);
        ApplyCoverageProjection(degradation);
        ExcludeSharedWrapperRecords(degradation);

        var cClangArguments = clangArguments
            .Where(static argument => argument is not "-xc++" and not "-std=c++20")
            .Prepend("-std=c17")
            .Prepend("-xc")
            .ToArray();
        var cOpaque = ParseCoverageHeader(
            "SharpCpp.Tests.E2E.Coverage.COpaque",
            "SharpCpp.Tests.E2E.Coverage.COpaque.Generated",
            "COpaqueBindings",
            "Coverage/COpaque.h",
            "sharpcpp_coverage_c_opaque",
            fixtureRoot,
            parser,
            cClangArguments,
            marshallerConfigurationFiles,
            marshallerClangArguments: clangArguments);
        BindingCompilationBootstrapPipeline.Apply(cOpaque, marshallerConfigurationFiles, clangArguments);
        RemoveLocalConfiguredWrapperRecords(cOpaque);
        ApplyCoverageProjection(cOpaque);
        ExcludeSharedWrapperRecords(cOpaque);

        var ownedRootDirectory = Path.Combine(fixtureRoot, "Coverage", "Owned");
        var owned = parser.Parse(new ClangSharpParseRequest(
            "SharpCpp.Tests.E2E.Coverage.Owned",
            "SharpCpp.Tests.E2E.Coverage.Owned.Generated",
            "PendingOwnedBindings",
            "SharpCppTestsE2E_Native",
            Path.Combine(ownedRootDirectory, "OwnedUmbrella.hpp"),
            "Coverage/Owned/OwnedUmbrella.hpp",
            clangArguments,
            Array.Empty<string>(),
            EntryPointPrefix: "sharpcpp_coverage_owned",
            OwnedFileRoots: [ownedRootDirectory, string.Empty],
            OwnedFileRootIncludePrefixes: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ownedRootDirectory] = "/Coverage/Owned/",
                [string.Empty] = "ignored"
            }));
        BindingCompilationBootstrapPipeline.Apply(
            owned,
            Array.Empty<string>(),
            clangArguments,
            configureBeforeValidation: static compilation => compilation.Functions
                .Single(function => string.Equals(function.NativeName, "owned_first_round_trip", StringComparison.Ordinal))
                .ManagedName = "OwnedRootFirst",
            configureAfterValidation: static compilation =>
            {
                compilation.ManagedClassName = "OwnedBindings";
                compilation.Functions
                    .Single(function => string.Equals(function.NativeName, "owned_second_round_trip", StringComparison.Ordinal))
                    .ManagedName = "OwnedRootSecond";
            });
        ApplyCoverageProjection(owned);

        return BindingOutputRenderer.Render(dependency, "SharpCppCoverageDependency", includeCallbackReleaseRegistration: false).Files
            .Concat(BindingOutputRenderer.Render(consumer, "SharpCppCoverageConsumer", includeCallbackReleaseRegistration: false).Files)
            .Concat(BindingOutputRenderer.Render(degradation, "SharpCppCoverageDegradation", includeCallbackReleaseRegistration: false).Files)
            .Concat(BindingOutputRenderer.Render(cOpaque, "SharpCppCoverageCOpaque", includeCallbackReleaseRegistration: false).Files)
            .Concat(BindingOutputRenderer.Render(owned, "SharpCppCoverageOwned", includeCallbackReleaseRegistration: false).Files)
            .ToArray();
    }

    private static void ApplyDegradationCoverage(BindingCompilation compilation)
    {
        compilation.Functions.Single(function =>
                string.Equals(function.NativeName, "callback_marked_ref", StringComparison.Ordinal))
            .ClearIgnoreReason();
    }

    private static BindingMetadataDocument CreateCompatibilityDependencyMetadata(BindingMetadataDocument document)
    {
        return document with
        {
            Types = document.Types.Select(type =>
            {
                var members = type.Members?.Select(member =>
                {
                    var parameters = member.Parameters?.Select(parameter =>
                    {
                        return type.NativeName == "dependency_primitives" &&
                            member.NativeName is "echo_int32" or "update" or "write"
                                ? parameter with { AbiKind = null }
                                : parameter;
                    }).ToArray();
                    return member with
                    {
                        SymbolId = type.ManagedWrapperAssembly is null ? member.SymbolId : null,
                        AccessModifier = type.NativeName == "dependency_primitives" && member.NativeName == "echo_bool"
                            ? "private"
                            : member.AccessModifier,
                        Parameters = parameters
                    };
                }).ToArray();

                return type.NativeName switch
                {
                    "std::array" => type with
                    {
                        ManagedNamespace = null,
                        Projection = type.Projection with { PublicType = "global::Std.Array<T, N>" },
                        Members = members
                    },
                    "std::vector" => type with
                    {
                        ManagedName = "global::Std.Vector",
                        ManagedNamespace = null,
                        IsDynamicArray = true,
                        Members = members
                    },
                    _ => type with { Members = members }
                };
            }).ToArray(),
            Aliases = document.Aliases?.Select(alias => alias.NativeName switch
            {
                "dependency_dynamic_alias" => alias with
                {
                    TargetType = alias.TargetType with
                    {
                        DynamicArrayElementType = alias.TargetType.GenericArguments![0]
                    }
                },
                "dependency_state_alias" => alias with
                {
                    TargetType = alias.TargetType with { EnumId = null }
                },
                _ => alias
            }).ToArray()
        };
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
        BindingMetadataImportCatalog? importedMetadata = null,
        IReadOnlyList<string>? marshallerClangArguments = null)
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
            ImportedBindingMetadata: importedMetadata,
            MarshallerClangArguments: marshallerClangArguments));
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

    private static void ExcludeSharedWrapperRecords(BindingCompilation compilation)
    {
        foreach (var record in compilation.Records
                     .Where(record => record.HasMetadata(MetadataKeys.ManagedWrapperAssembly))
                     .ToArray())
            record.SetSilentExclusionReason("shared wrapper implementation is emitted by the primary E2E compilation");
        compilation.InvalidateCaches();
    }

    private static void RemoveLocalConfiguredWrapperRecords(BindingCompilation compilation)
    {
        foreach (var record in compilation.Records
                     .Where(record => !record.HasMetadata(MetadataKeys.ManagedWrapperAssembly))
                     .Where(record =>
                         record.GetMetadata<bool>(MetadataKeys.MarshallerConfiguredRecord) ||
                         !string.IsNullOrWhiteSpace(record.ManagedBaseTypeName) && record.Bases.Count == 0 ||
                         !string.IsNullOrWhiteSpace(record.GetMetadata<string>(MetadataKeys.ManagedWrapperProviderKind)) ||
                         record.Members.OfType<FunctionBinding>().Any(member => member.HasMetadata(MetadataKeys.MarshallerExposedMethod)))
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
