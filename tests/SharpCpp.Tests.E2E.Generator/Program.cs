using SharpCpp.Metadata.Export;
using SharpCpp.Metadata.Import;
using SharpCpp.Model.Ast;
using SharpCpp.Parsing;
using SharpCpp.Pipeline;
using SharpCpp.Rendering;
using SharpCpp.Toolchain;
using Microsoft.Extensions.Logging;
using SharpCpp.Model.Logging;

namespace SharpCpp.Tests.E2E.Generator;

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
        return GenerateBindings(args);
    }

    // Consumers such as the rbfx generator make records director-capable by name.
    private static readonly IReadOnlySet<string> ForcedDirectorRecords = new HashSet<string>(StringComparer.Ordinal)
    {
        "forced_directors::forced_abstract",
        "forced_directors::forced_implicit_abstract",
        "forced_directors::forced_derived_abstract"
    };

    private static int GenerateBindings(string[] args)
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
        var toolchain = CompilerToolchainDiscovery.Discover(
            GetOptionValue(args, "--compiler") ?? "c++",
            clangResourceDirectoryOverride: Environment.GetEnvironmentVariable("SHARPCPP_CLANG_RESOURCE_DIRECTORY"));
        clangArguments.AddRange(toolchain.GetClangArguments(clangArguments));
        Logger.LogInformation(
            "Toolchain: target {TargetTriple}, resource directory {ResourceDirectory}, {IncludeCount} system include directories",
            toolchain.TargetTriple ?? "(default)",
            toolchain.ClangResourceDirectory ?? "(none)",
            toolchain.IncludeDirectories.Count);
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
            // Modules/ holds fixtures generated as separate binding modules.
            .Where(path => !Path.GetRelativePath(fixtureRoot, path).StartsWith($"Modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
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
                    EntryPointPrefix: "sharpcpp_e2e",
                    DirectorRecordCppTypeNames: ForcedDirectorRecords));

            compilation.HeaderFiles.Add(relativeHeaderPath);
            foreach (var member in parsedHeaderCompilation.Members.ToArray())
            {
                compilation.Members.Add(member);
            }
        }

        ApplyConsumerExtensions(compilation);
        BindingCompilationBootstrapPipeline.Apply(
            compilation,
            marshallerConfigurationFiles,
            clangArguments,
            // Renamed after the collision resolver has run, so the field keeps its name and clashes with a live method.
            configureAfterValidation: static compilation => compilation.Functions
                .Single(function => string.Equals(function.CppQualifiedName, "silent_drop_field_renamed_clash::Sum", StringComparison.Ordinal))
                .ManagedName = "Total");
        // Secondary-base interface forwarders only exist for accessor-promoted members; promote just this fixture's.
        GenerateAccessorPropertiesPass.Apply(
            compilation,
            recordFilter: static record => record.CppQualifiedName.StartsWith("secondary_bases::", StringComparison.Ordinal));
        // Reserved SafeHandle/NativeObject names are renamed before this pass and never become properties.
        GenerateAccessorPropertiesPass.Apply(
            compilation,
            [
                new AccessorPropertyPattern("Get", "Set"),
                new AccessorPropertyPattern("Is", "Set", propertyNamePrefix: "Is", allowGetterOnly: true)
            ],
            recordFilter: static record => record.CppQualifiedName.StartsWith("reserved_names::", StringComparison.Ordinal));

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
        generatedFiles.AddRange(GenerateCrossModuleBindings(
            fixtureRoot,
            generatedRoot,
            parser,
            clangArguments,
            marshallerConfigurationFiles));
        generatedFiles.AddRange(GenerateModuleBindings(
            fixtureRoot,
            generatedRoot,
            parser,
            clangArguments,
            marshallerConfigurationFiles));
        new GeneratedBindingSet(generatedFiles).WriteToDirectory(generatedRoot);
        Logger.LogInformation("Wrote sample bindings to {GeneratedRoot}", generatedRoot);
        return 0;
    }

    private static void SetPointerPointerShape(BindingCompilation compilation, string functionName, string parameterName, string shape)
    {
        compilation.Functions
            .Single(function => string.Equals(function.CppQualifiedName, functionName, StringComparison.Ordinal))
            .Parameters
            .Single(parameter => string.Equals(parameter.NativeName, parameterName, StringComparison.Ordinal))
            .SetMetadata(MetadataKeys.PointerPointerShape, shape);
    }

    private static void SetCountedBy(BindingCompilation compilation, string functionName, string parameterName, string countName)
    {
        compilation.Functions
            .Single(function => string.Equals(function.CppQualifiedName, functionName, StringComparison.Ordinal))
            .Parameters
            .Single(parameter => string.Equals(parameter.NativeName, parameterName, StringComparison.Ordinal))
            .SetMetadata(MetadataKeys.CountedBy, countName);
    }

    // A null parameterName tags the function's char* return.
    private static void SetFreedBy(BindingCompilation compilation, string functionName, string? parameterName, string freeName)
    {
        var function = compilation.Functions
            .Single(candidate => string.Equals(candidate.CppQualifiedName, functionName, StringComparison.Ordinal));
        BindingNode target = parameterName is null
            ? function
            : function.Parameters.Single(parameter => string.Equals(parameter.NativeName, parameterName, StringComparison.Ordinal));
        target.SetMetadata(MetadataKeys.FreedBy, freeName);
    }

    private static void SetEndedBy(BindingCompilation compilation, string functionName, string parameterName, string endName)
    {
        compilation.Functions
            .Single(function => string.Equals(function.CppQualifiedName, functionName, StringComparison.Ordinal))
            .Parameters
            .Single(parameter => string.Equals(parameter.NativeName, parameterName, StringComparison.Ordinal))
            .SetMetadata(MetadataKeys.EndedBy, endName);
    }

    private static void ApplyConsumerExtensions(BindingCompilation compilation)
    {
        const string headerPath = "Consumer/ConsumerProjection.hpp";

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

        // fill_configured_wide_vector3s writes three elements through a bare pointer; the span
        // wrapper rejects shorter spans before the native call.
        compilation.Functions.Single(function =>
                string.Equals(function.CppQualifiedName, "fill_configured_wide_vector3s", StringComparison.Ordinal))
            .Parameters[0]
            .SetMetadata(MetadataKeys.MinimumSpanLength, 3);

        // Consumers cannot edit third-party headers, so they select the shape of an ambiguous
        // char** / T** parameter by name.
        SetPointerPointerShape(compilation, "measure_configured_names", "names", PointerPointerShapes.Array);
        SetPointerPointerShape(compilation, "get_configured_label", "value", PointerPointerShapes.Single);
        SetPointerPointerShape(compilation, "sum_configured_items", "items", PointerPointerShapes.Array);
        SetPointerPointerShape(compilation, "pick_configured_item", "item", PointerPointerShapes.Single);
        SetPointerPointerShape(compilation, "measure_bad_shape", "names", PointerPointerShapes.Array);
        SetPointerPointerShape(compilation, "measure_bad_value", "names", "sideways");
        SetPointerPointerShape(compilation, "pick_const_item", "item", PointerPointerShapes.Single);
        SetPointerPointerShape(compilation, "get_const_label", "value", PointerPointerShapes.Single);
        // Consumers also name the count of an array parameter by name.
        SetCountedBy(compilation, "sum_configured_counted_values", "values", "n");
        SetCountedBy(compilation, "sum_configured_counted_items", "items", "n");
        SetCountedBy(compilation, "sum_configured_counted_noop", "items", "item_count");
        SetCountedBy(compilation, "pick_configured_counted_single", "items", "item_count");
        SetPointerPointerShape(compilation, "pick_configured_counted_single", "items", PointerPointerShapes.Single);
        // Consumers also name the free function of an owned string, on a return or an output parameter.
        SetFreedBy(compilation, "make_consumer_owned_text", null, "release_owned_text");
        SetFreedBy(compilation, "get_consumer_owned_label", "value", "release_owned_text");
        // Consumers also pair a begin pointer with its end pointer by name.
        SetEndedBy(compilation, "sum_configured_range", "begin", "end");
        // The director virtual's pointers were lowered to address values at parse time, so this tag
        // cannot be honoured.
        SetEndedBy(compilation, "lowered_range_visitor::visit", "begin", "end");
        // A consumer that projects the pointee to a raw pointer cannot also ask for a record array.
        compilation.Functions
            .Single(function => string.Equals(function.CppQualifiedName, "pick_retyped_item", StringComparison.Ordinal))
            .Parameters[0].Type = BindingType.IntPtr;
        SetPointerPointerShape(compilation, "pick_retyped_item", "item", PointerPointerShapes.Array);

        compilation.Records.Single(record =>
                string.Equals(record.CppQualifiedName, "consumer_projection_query_target", StringComparison.Ordinal))
            .SetMetadata(
                MetadataKeys.NativeInterfaceQuery,
                new NativeInterfaceQueryAnnotation("consumer_projection_query_source", "73"));

        // The parser does not bind forwarding references, so a consumer declares the member
        // template itself and instantiates it with a record passed as `const T&`, keeping the
        // record-value flag of the handle type (the shape of a variant assignment operator).
        var projectedValue = compilation.Records.Single(record =>
            string.Equals(record.CppQualifiedName, "consumer_projection_value", StringComparison.Ordinal));
        var slot = compilation.Records.Single(record =>
            string.Equals(record.CppQualifiedName, "consumer_projection_slot", StringComparison.Ordinal));
        foreach (var parsedStore in slot.Members.OfType<FunctionBinding>().Where(static member => member.NativeName == "store").ToList())
            slot.Members.Remove(parsedStore);
        var store = new FunctionBinding(
            "method:consumer_projection_slot::store:T",
            "store",
            "Store",
            BindingType.Int32,
            entryPointName: "consumer_projection_slot_store_generic",
            cppReturnTypeName: "int",
            cppQualifiedName: "consumer_projection_slot::store")
        {
            HeaderIncludePath = slot.HeaderIncludePath,
            IsStatic = false,
            IsConstMethod = false,
            OwningRecordCppTypeName = slot.CppQualifiedName
        };
        store.GenericParameters.Add(new BindingGenericParameter("T", 0));
        store.Parameters.Add(new ParameterBinding(
            "parameter:consumer_projection_slot::store:T:value",
            "value",
            "value",
            BindingType.CreateGenericParameter("T"),
            cppTypeName: "T&&"));
        store.SetMetadata(MetadataKeys.ExplicitGenericInstantiations, new GenericInstantiationMetadata(
        [
            new RecordGenericInstantiationBinding(
            [
                BindingType.CreateHandle(
                        "consumer_projection_value",
                        projectedValue.ManagedName,
                        projectedValue.SymbolId,
                        cppIsRecordValue: true)
                    .WithCppTypeName("const consumer_projection_value&")
                    .WithCppIsReference(true)
            ])
        ]));
        slot.Members.Add(store);

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
        projectedConsumer.BodyCodeBlocks.Add(
            "public int RoundTripNormalized(int value)\r\n{\r\n    const string marker = \"\\\"{\";\r\n    return global::SharpCpp.Tests.E2E.Generated.E2EBindings.ConsumerProjectionRoundTrip(value) + _offset + marker.Length - 2;\r\n}\t");
        compilation.Members.Add(projectedConsumer);
    }

    /// <summary>
    /// Two modules in the shape of an engine and an application binding against it: the
    /// second imports the first's metadata. Output is used exactly as generated.
    /// </summary>
    private static IReadOnlyList<GeneratedBindingFile> GenerateCrossModuleBindings(
        string fixtureRoot,
        string generatedRoot,
        ClangSharpHeaderParser parser,
        IReadOnlyList<string> clangArguments,
        IReadOnlyList<string> fixtureMarshallerConfigurationFiles)
    {
        // Like a real application, only the core marshallers; fixture-local ones would
        // re-emit the main module's locally configured wrappers.
        IReadOnlyList<string> marshallerConfigurationFiles = fixtureMarshallerConfigurationFiles
            .Where(static path => string.Equals(Path.GetFileName(path), "Marshaller.hpp", StringComparison.Ordinal))
            .ToArray();
        var library = ParseModuleHeader(
            "SharpCpp.Tests.E2E.Modules.CrossModule.Library",
            "SharpCpp.Tests.E2E.Modules.CrossModule.Library",
            "LibraryBindings",
            "Modules/CrossModule/Library.hpp",
            "sharpcpp_cross_library",
            fixtureRoot,
            parser,
            clangArguments,
            marshallerConfigurationFiles);
        BindingCompilationBootstrapPipeline.Apply(library, marshallerConfigurationFiles, clangArguments);
        var libraryMetadataPath = Path.Combine(generatedRoot, "SharpCppCrossModuleLibrary.Bindings.json");
        Directory.CreateDirectory(generatedRoot);
        File.WriteAllText(
            libraryMetadataPath,
            BindingMetadataSerializer.Serialize(BindingMetadataExporter.Export(
                library,
                new BindingMetadataExportOptions(library.ProjectName, "cross-library", "cross-clang"))));

        var app = ParseModuleHeader(
            "SharpCpp.Tests.E2E.Modules.CrossModule.App",
            "SharpCpp.Tests.E2E.Modules.CrossModule.App",
            "AppBindings",
            "Modules/CrossModule/App.hpp",
            "sharpcpp_cross_app",
            fixtureRoot,
            parser,
            clangArguments,
            marshallerConfigurationFiles,
            BindingMetadataImportCatalog.Load([libraryMetadataPath]));
        BindingCompilationBootstrapPipeline.Apply(app, marshallerConfigurationFiles, clangArguments);

        return BindingOutputRenderer.Render(library, "SharpCppCrossModuleLibrary").Files
            .Concat(BindingOutputRenderer.Render(app, "SharpCppCrossModuleApp").Files)
            .ToArray();
    }

    private static IReadOnlyList<GeneratedBindingFile> GenerateModuleBindings(
        string fixtureRoot,
        string generatedRoot,
        ClangSharpHeaderParser parser,
        IReadOnlyList<string> clangArguments,
        IReadOnlyList<string> marshallerConfigurationFiles)
    {
        var dependency = ParseModuleHeader(
            "SharpCpp.Tests.E2E.Modules.Metadata.Dependency",
            "SharpCpp.Tests.E2E.Modules.Metadata.Dependency",
            "DependencyBindings",
            "Modules/Metadata/Dependency.hpp",
            "sharpcpp_metadata_dependency",
            fixtureRoot,
            parser,
            clangArguments,
            marshallerConfigurationFiles);
        BindingCompilationBootstrapPipeline.Apply(dependency, marshallerConfigurationFiles, clangArguments);
        // Consumers pick their accessor naming; this one also promotes `is_x()` getters.
        GenerateAccessorPropertiesPass.Apply(
            dependency,
            [
                new AccessorPropertyPattern("Get", "Set"),
                new AccessorPropertyPattern("Is", "Set", propertyNamePrefix: "Is", allowGetterOnly: true)
            ]);
        RemoveLocalConfiguredWrapperRecords(dependency);
        ApplyModuleProjection(dependency);

        var dependencyMetadata = BindingMetadataExporter.Export(
            dependency,
            new BindingMetadataExportOptions(dependency.ProjectName, "metadata-dependency", "module-clang"));
        var dependencyMetadataPath = Path.Combine(generatedRoot, "SharpCppMetadataDependency.Bindings.json");
        File.WriteAllText(dependencyMetadataPath, BindingMetadataSerializer.Serialize(dependencyMetadata));
        var compatibilityDependencyMetadata = CreateCompatibilityDependencyMetadata(dependencyMetadata);
        var compatibilityDependencyMetadataPath = Path.Combine(generatedRoot, "SharpCppMetadataDependency.CompatibilityBindings.json");
        File.WriteAllText(compatibilityDependencyMetadataPath, BindingMetadataSerializer.Serialize(compatibilityDependencyMetadata));

        var dependencyImports = BindingMetadataImportCatalog.Load([
            compatibilityDependencyMetadataPath,
            dependencyMetadataPath
        ]);
        var consumer = ParseModuleHeader(
            "SharpCpp.Tests.E2E.Modules.Metadata.Consumer",
            "SharpCpp.Tests.E2E.Modules.Metadata.Consumer",
            "ConsumerBindings",
            "Modules/Metadata/Consumer.hpp",
            "sharpcpp_metadata_consumer",
            fixtureRoot,
            parser,
            clangArguments,
            marshallerConfigurationFiles,
            dependencyImports);
        BindingCompilationBootstrapPipeline.Apply(consumer, marshallerConfigurationFiles, clangArguments);
        RemoveLocalConfiguredWrapperRecords(consumer);
        ApplyModuleProjection(consumer);
        consumer.TypeAliases.Clear();
        var consumerMetadata = BindingMetadataExporter.Export(
            consumer,
            new BindingMetadataExportOptions(
                consumer.ProjectName,
                Phase2ClosureHash: "metadata-consumer",
                ClangArgsHash: "module-clang",
                Imports: dependencyImports.Imports));
        File.WriteAllText(
            Path.Combine(generatedRoot, "SharpCppMetadataConsumer.Bindings.json"),
            BindingMetadataSerializer.Serialize(consumerMetadata));

        var namespaceMapping = ParseModuleHeader(
            "SharpCpp.Tests.E2E.Modules.NamespaceMapping",
            "SharpCpp.Tests.E2E.Modules.NamespaceMapping.Generated",
            "DegradationBindings",
            "Modules/NamespaceMapping/NamespaceMapping.hpp",
            "sharpcpp_namespace_mapping",
            fixtureRoot,
            parser,
            clangArguments,
            marshallerConfigurationFiles);
        namespaceMapping.NamespaceMappings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["coverage_allowed"] = namespaceMapping.ManagedNamespace
        };
        ApplyNamespaceMappingOverrides(namespaceMapping);
        BindingCompilationBootstrapPipeline.Apply(namespaceMapping, marshallerConfigurationFiles, clangArguments);
        RemoveLocalConfiguredWrapperRecords(namespaceMapping);
        ApplyModuleProjection(namespaceMapping);

        var cClangArguments = clangArguments
            .Where(static argument => argument is not "-xc++" and not "-std=c++20")
            .Prepend("-std=c17")
            .Prepend("-xc")
            .ToArray();
        var cModule = ParseModuleHeader(
            "SharpCpp.Tests.E2E.Modules.C",
            "SharpCpp.Tests.E2E.Modules.C.Generated",
            "CBindings",
            "Modules/C/C.h",
            "sharpcpp_c_opaque",
            fixtureRoot,
            parser,
            cClangArguments,
            marshallerConfigurationFiles,
            marshallerClangArguments: clangArguments);
        BindingCompilationBootstrapPipeline.Apply(cModule, marshallerConfigurationFiles, clangArguments);
        RemoveLocalConfiguredWrapperRecords(cModule);
        ApplyModuleProjection(cModule);

        var ownedRootDirectory = Path.Combine(fixtureRoot, "Modules", "OwnedFiles");
        var owned = parser.Parse(new ClangSharpParseRequest(
            "SharpCpp.Tests.E2E.Modules.OwnedFiles",
            "SharpCpp.Tests.E2E.Modules.OwnedFiles.Generated",
            "PendingOwnedBindings",
            "SharpCppTestsE2E_Native",
            Path.Combine(ownedRootDirectory, "OwnedUmbrella.hpp"),
            "Modules/OwnedFiles/OwnedUmbrella.hpp",
            clangArguments,
            Array.Empty<string>(),
            EntryPointPrefix: "sharpcpp_owned_files",
            OwnedFileRoots: [ownedRootDirectory, string.Empty],
            OwnedFileRootIncludePrefixes: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ownedRootDirectory] = "/Modules/OwnedFiles/",
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
                compilation.SetWarning("owned files post-validation diagnostic");
                compilation.ManagedClassName = "OwnedBindings";
                compilation.Functions
                    .Single(function => string.Equals(function.NativeName, "owned_second_round_trip", StringComparison.Ordinal))
                    .ManagedName = "OwnedRootSecond";
            });
        ApplyModuleProjection(owned);

        return BindingOutputRenderer.Render(dependency, "SharpCppMetadataDependency").Files
            .Concat(BindingOutputRenderer.Render(consumer, "SharpCppMetadataConsumer").Files)
            .Concat(BindingOutputRenderer.Render(namespaceMapping, "SharpCppNamespaceMapping").Files)
            .Concat(BindingOutputRenderer.Render(cModule, "SharpCppC").Files)
            .Concat(BindingOutputRenderer.Render(owned, "SharpCppOwnedFiles").Files)
            .ToArray();
    }

    private static void ApplyNamespaceMappingOverrides(BindingCompilation compilation)
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
                    "dependency_small_state" => type with
                    {
                        Projection = type.Projection with { CppType = "unsigned short" },
                        Members = members
                    },
                    "dependency_i8_state" => type with
                    {
                        Projection = type.Projection with { CppType = "signed char" },
                        Members = members
                    },
                    "dependency_u8_state" => type with
                    {
                        Projection = type.Projection with { CppType = "unsigned char" },
                        Members = members
                    },
                    "dependency_i16_state" => type with
                    {
                        Projection = type.Projection with { CppType = "short" },
                        Members = members
                    },
                    "dependency_i32_state" => type with
                    {
                        Projection = type.Projection with { CppType = "int" },
                        Members = members
                    },
                    "dependency_i64_state" => type with
                    {
                        Projection = type.Projection with { CppType = "long long" },
                        Members = members
                    },
                    "dependency_u64_state" => type with
                    {
                        Projection = type.Projection with { CppType = "unsigned long long" },
                        Members = members
                    },
                    "dependency_void_projection_state" => type with
                    {
                        Projection = type.Projection with { CppType = "void" },
                        Members = members
                    },
                    "dependency_bool_projection_state" => type with
                    {
                        Projection = type.Projection with { CppType = "bool" },
                        Members = members
                    },
                    "dependency_u32_projection_state" => type with
                    {
                        Projection = type.Projection with { CppType = "unsigned int" },
                        Members = members
                    },
                    "dependency_float_projection_state" => type with
                    {
                        Projection = type.Projection with { CppType = "float" },
                        Members = members
                    },
                    "dependency_double_projection_state" => type with
                    {
                        Projection = type.Projection with { CppType = "double" },
                        Members = members
                    },
                    "dependency_long_projection_state" => type with
                    {
                        Projection = type.Projection with { CppType = "long" },
                        Members = members
                    },
                    "dependency_ulong_projection_state" => type with
                    {
                        Projection = type.Projection with { CppType = "unsigned long" },
                        Members = members
                    },
                    "dependency_pointer_projection_state" => type with
                    {
                        Projection = type.Projection with { CppType = "void*" },
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
                "dependency_value_box_alias" => alias with
                {
                    TargetType = alias.TargetType with
                    {
                        GenericArguments = alias.TargetType.GenericArguments!
                            .Select(argument => argument with
                            {
                                CppType = "dependency_value_alias_surface*",
                                PublicType = "DependencyValueAliasSurface"
                            })
                            .ToArray()
                    }
                },
                _ => alias
            }).ToArray()
        };
    }

    private static BindingCompilation ParseModuleHeader(
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

    private static void ApplyModuleProjection(BindingCompilation compilation)
    {
        var projection = new ManagedBindingProjection(compilation.ManagedNamespace);
        // Shared wrapper types belong to their wrapper assembly's namespace.
        foreach (var record in compilation.Records.Where(record =>
                     !record.GetMetadata<bool>(MetadataKeys.SuppressEmission) &&
                     !record.HasMetadata(MetadataKeys.ManagedWrapperAssembly)))
            projection.AddDeclaration(compilation.ManagedNamespace, record);
        foreach (var enumBinding in compilation.Enums.Where(enumBinding => !enumBinding.GetMetadata<bool>(MetadataKeys.SuppressEmission)))
            projection.AddDeclaration(compilation.ManagedNamespace, enumBinding);
        compilation.SetMetadata(MetadataKeys.ManagedBindingProjection, projection);
    }

    // Secondary modules share the main module's assembly, which already emits the
    // module-local configured wrappers; fields typed by one of them are not bound here.
    private static void RemoveLocalConfiguredWrapperRecords(BindingCompilation compilation)
    {
        var removed = compilation.Records
            .Where(record => !record.HasMetadata(MetadataKeys.ManagedWrapperAssembly))
            .Where(record =>
                record.GetMetadata<bool>(MetadataKeys.MarshallerConfiguredRecord) ||
                !string.IsNullOrWhiteSpace(record.ManagedBaseTypeName) && record.Bases.Count == 0 ||
                !string.IsNullOrWhiteSpace(record.GetMetadata<string>(MetadataKeys.ManagedWrapperProviderKind)) ||
                record.Members.OfType<FunctionBinding>().Any(member => member.HasMetadata(MetadataKeys.MarshallerExposedMethod)))
            .ToArray();
        foreach (var record in removed)
            compilation.Members.Remove(record);
        var removedIds = removed.Select(static record => record.SymbolId).ToHashSet(StringComparer.Ordinal);
        foreach (var field in compilation.Descendants<FieldBinding>()
                     .Where(field => field.Type.RecordId is { } recordId && removedIds.Contains(recordId)))
            field.SetIgnoreReason("the field's wrapper type is emitted by the main E2E module");
        compilation.InvalidateCaches();
    }


    private static void PrintUsage()
    {
        Console.WriteLine("""
            SharpCpp E2E binding generator

            Usage:
              --generated-root <dir> [--project-root <dir>] [--file-stem <stem>] [--compiler <path>] [--log-file <path>]
            """);
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
