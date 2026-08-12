using SharpCpp.Model.Ast;
using SharpCpp.Pipeline;
using SharpCpp.Parsing;

using Xunit;

namespace SharpCpp.Tests.Model;

public sealed class ClangSharpHeaderParserGenericMethodTests
{
    [Fact]
    public void Parse_preserves_qualified_generic_method_parameter()
    {
        var compilation = ParseTempHeader(
            """
            class Element
            {
            public:
                template <typename T>
                T GetAttribute(const T& default_value) const;
            };
            """);

        var element = Assert.Single(compilation.Records, static record => record.CppQualifiedName == "Element");
        var method = Assert.Single(element.Members.OfType<FunctionBinding>(), static member => member.NativeName == "GetAttribute");
        var parameter = Assert.Single(method.Parameters);
        Assert.Equal(BindingTypeKind.GenericParameter, parameter.Type.Kind);
        Assert.Equal("T", parameter.Type.GenericParameterName);
        Assert.Equal(BindingParameterAbiKind.Reference, parameter.AbiKind);
        Assert.True(parameter.Type.Marshaller.CppIsReference);
        Assert.Equal("const T&", parameter.CppTypeName);
    }

    [Fact]
    public void Parse_does_not_resolve_record_template_parameters_as_unrelated_aliases()
    {
        var compilation = ParseTempHeader(
            """
            namespace Noise
            {
            struct Vector
            {
                using Type = int;
            };
            }

            template <typename Type>
            class Result
            {
            public:
                const Type& Get() const;
                void Set(const Type& value);
            };
            """);

        var result = Assert.Single(compilation.Records, static record => record.CppQualifiedName == "Result");
        var get = Assert.Single(result.Members.OfType<FunctionBinding>(), static member => member.NativeName == "Get");
        var set = Assert.Single(result.Members.OfType<FunctionBinding>(), static member => member.NativeName == "Set");

        Assert.Equal("const Type&", get.CppReturnTypeName);
        Assert.Equal("const Type&", Assert.Single(set.Parameters).CppTypeName);
    }

    [Fact]
    public void Parse_excludes_generic_constructor()
    {
        var compilation = ParseTempHeader(
            """
            class Guard
            {
            public:
                template <typename T>
                explicit Guard(const T& value);
            };
            """);

        var guard = Assert.Single(compilation.Records, static record => record.CppQualifiedName == "Guard");
        var constructor = Assert.Single(guard.Members.OfType<FunctionBinding>(), static member => member.NativeName == "Guard");
        Assert.True(constructor.HasMetadata(MetadataKeys.IgnoreReason));
        Assert.False(constructor.IsSilentlyExcluded());
    }

    [Fact]
    public void Parse_materializes_implicit_default_constructor_for_empty_concrete_class()
    {
        var compilation = ParseTempHeader(
            """
            class Settings
            {
            public:
                void Apply();
            };
            """);

        var settings = Assert.Single(compilation.Records, static record => record.CppQualifiedName == "Settings");
        var constructor = Assert.Single(settings.Members.OfType<FunctionBinding>(), static member => member.IsConstructor);
        Assert.Empty(constructor.Parameters);
        Assert.False(constructor.IsIgnored(), constructor.GetIgnoreReason());
    }

    [Fact]
    public void Parse_materializes_implicit_default_constructor_for_concrete_generic_derived_class()
    {
        var compilation = ParseTempHeader(
            """
            template <typename T>
            class Sink
            {
            public:
                virtual ~Sink() = default;
                virtual void Set(T value) = 0;
            };

            class IntSink : public Sink<int>
            {
            public:
                void Set(int value) override;
            };
            """);

        var sink = Assert.Single(compilation.Records, static record => record.CppQualifiedName == "IntSink");
        var constructor = Assert.Single(sink.Members.OfType<FunctionBinding>(), static member => member.IsConstructor);
        Assert.Empty(constructor.Parameters);
        Assert.False(constructor.IsIgnored(), constructor.GetIgnoreReason());
    }

    [Fact]
    public void Parse_does_not_synthesize_default_constructor_when_ast_declares_non_default_constructor()
    {
        var compilation = ParseTempHeader(
            """
            class Settings
            {
            public:
                explicit Settings(int value);
            };
            """);

        var settings = Assert.Single(compilation.Records, static record => record.CppQualifiedName == "Settings");
        var constructor = Assert.Single(settings.Members.OfType<FunctionBinding>(), static member => member.IsConstructor);
        Assert.Single(constructor.Parameters);
    }

    [Fact]
    public void Parse_materializes_inherited_constructor_from_closed_generic_base()
    {
        var compilation = ParseTempHeader(
            """
            template <typename T>
            class Base
            {
            public:
                explicit Base(T value);
            };

            struct Derived : Base<int>
            {
                using Base<int>::Base;
            };
            """);

        var derived = Assert.Single(compilation.Records, static record => record.CppQualifiedName == "Derived");
        var constructor = Assert.Single(derived.Members.OfType<FunctionBinding>(), static member => member.IsConstructor && member.Parameters.Count == 1);
        Assert.Equal(BindingPrimitiveKind.Int32, Assert.Single(constructor.Parameters).Type.PrimitiveKind);
        Assert.DoesNotContain(derived.Members.OfType<FunctionBinding>(), static member => member.IsConstructor && member.Parameters.Count == 0 && !member.IsIgnored());
    }

    [Fact]
    public void Parse_preserves_nested_closed_template_arguments_on_aliases()
    {
        var compilation = ParseTempHeader(
            """
            struct Traits { };
            struct Result { };

            template <typename TResult, typename TTraits>
            class Collector { };

            using ResultCollector = Collector<Result, Traits>;

            template <typename TCollector>
            class AllHits : public TCollector { };

            using ResultHits = AllHits<ResultCollector>;
            """);

        BindingCompilationGenericInstantiationPass.Apply(compilation);

        var collector = Assert.Single(compilation.Records, static record => record.CppQualifiedName == "Collector");
        var collectorInstantiation = Assert.Single(collector.SupportedGenericInstantiations);
        Assert.Equal(new[] { "Result", "Traits" }, collectorInstantiation.TypeArguments.Select(static type => type.CppTypeName));

        var allHits = Assert.Single(compilation.Records, static record => record.CppQualifiedName == "AllHits");
        var hitsInstantiation = Assert.Single(allHits.SupportedGenericInstantiations);
        var collectorArgument = Assert.Single(hitsInstantiation.TypeArguments);
        Assert.Equal("ResultCollector", collectorArgument.CppTypeName);
        Assert.Equal(new[] { "Result", "Traits" }, collectorArgument.GenericArguments.Select(static type => type.CppTypeName));
    }

    [Fact]
    public void Parse_materializes_annotated_closed_template_alias()
    {
        var compilation = ParseTempHeader(
            """
            struct Result { };

            template <typename TResult>
            class Collector
            {
            public:
                virtual void AddHit(const TResult& result) = 0;
            };

            template <typename TCollector>
            class AllHits : public TCollector
            {
            public:
                using ResultType = Result;
                void AddHit(const ResultType&) override { }
                bool HadHit() const;
                ResultType hit;
            };

            using ResultCollector = Collector<Result>;
            using ResultHits [[sharpcpp::materialize]] = AllHits<ResultCollector>;
            static_assert(sizeof(ResultHits) > 0);
            """);

        var materialized = Assert.Single(compilation.Records, static record => record.CppQualifiedName == "ResultHits");
        Assert.False(materialized.IsGeneric);
        Assert.False(materialized.IsAbstract);
        Assert.Contains(materialized.Members.OfType<FunctionBinding>(), static method => method.IsConstructor && method.Parameters.Count == 0);
        Assert.Contains(materialized.Members.OfType<FunctionBinding>(), static method => method.NativeName == "HadHit");
        var addHit = Assert.Single(materialized.Members.OfType<FunctionBinding>(), static method => method.NativeName == "AddHit");
        Assert.Equal("const Result&", Assert.Single(addHit.Parameters).CppTypeName);
        var hit = Assert.Single(materialized.Members.OfType<FieldBinding>());
        Assert.Equal("Result*", hit.Type.CppTypeName);
        Assert.Equal("Result", hit.CppTypeName);

        var baseBinding = Assert.Single(materialized.Bases);
        Assert.Equal("Collector<Result>", baseBinding.CppQualifiedName);
        Assert.Equal("Collector<Result>", baseBinding.Type.CppTypeName.TrimEnd('*'));

        Assert.DoesNotContain(compilation.TypeAliases, static alias => alias.CppQualifiedName == "ResultHits");
    }

    private static BindingCompilation ParseTempHeader(string source)
    {
        var fixtureDir = Path.Combine(Path.GetTempPath(), "sharpcpp-generic-methods-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixtureDir);
        try
        {
            const string fileName = "Sample.hpp";
            var headerPath = Path.Combine(fixtureDir, fileName);
            File.WriteAllText(headerPath, source);

            var parser = new ClangSharpHeaderParser();
            return parser.Parse(new ClangSharpParseRequest(
                ProjectName: "TestProject",
                ManagedNamespace: "Test.Namespace",
                ManagedClassName: "Bindings",
                NativeLibraryName: "Test.Native",
                HeaderFilePath: headerPath,
                HeaderIncludePath: fileName,
                ClangArguments: ["-x", "c++", "-std=c++20", $"-I{fixtureDir}"],
                MarshallerConfigurationFiles: Array.Empty<string>(),
                EntryPointPrefix: "Test_"));
        }
        finally
        {
            Directory.Delete(fixtureDir, recursive: true);
        }
    }
}
