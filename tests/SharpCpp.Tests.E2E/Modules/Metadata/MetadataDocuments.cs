using System.Text.Json;

using SharpCpp.Tests.E2E.Diagnostics;
using SharpCpp.Tests.E2E.Modules.Metadata.Consumer;
using SharpCpp.Tests.E2E.Modules.Metadata.Dependency;

using Xunit;

namespace SharpCpp.Tests.E2E.Modules.Metadata;

/// <summary>Facts the exported *.Bindings.json documents carry for importing modules.</summary>
public sealed class MetadataDocumentTests
{
    private static readonly string MainText = GeneratedArtifacts.ReadText("SharpCppTestsE2E.Bindings.json");
    private static readonly JsonElement Main = JsonDocument.Parse(MainText).RootElement;
    private static readonly JsonElement DependencyDocument = JsonDocument.Parse(GeneratedArtifacts.ReadText("SharpCppMetadataDependency.Bindings.json")).RootElement;
    private static readonly JsonElement ConsumerDocument = JsonDocument.Parse(GeneratedArtifacts.ReadText("SharpCppMetadataConsumer.Bindings.json")).RootElement;

    [Fact]
    public void Documents_are_versioned_and_sorted_deterministically()
    {
        Assert.Equal(9, Main.GetProperty("schema_version").GetInt32());
        Assert.EndsWith(Environment.NewLine, MainText, StringComparison.Ordinal);

        // Records, then enums, each ordered by managed name.
        var types = Main.GetProperty("types").EnumerateArray().ToArray();
        var records = types.TakeWhile(static type => type.GetProperty("kind").GetString() != "enum").Select(Name).ToArray();
        var enums = types.SkipWhile(static type => type.GetProperty("kind").GetString() != "enum").ToArray();
        Assert.All(enums, static type => Assert.Equal("enum", type.GetProperty("kind").GetString()));
        AssertOrdered(records);
        AssertOrdered(enums.Select(Name));
        AssertOrdered(Main.GetProperty("functions").EnumerateArray().Select(Name));
        AssertOrdered(DependencyDocument.GetProperty("aliases").EnumerateArray().Select(Name));
        AssertOrdered(Main.GetProperty("fingerprint").GetProperty("header_files").EnumerateArray().Select(static header => header.GetString()!));
        AssertOrdered(Type(Main, "OperatorBox").GetProperty("members").EnumerateArray().Select(Name));

        var imports = ConsumerDocument.GetProperty("imports").EnumerateArray()
            .Select(static item => item.GetProperty("package").GetString() + "|" + item.GetProperty("hash").GetString())
            .ToArray();
        Assert.Equal(2, imports.Length);
        AssertOrdered(imports);
    }

    [Fact]
    public void Enums_carry_flags_and_values()
    {
        var permissions = Type(DependencyDocument, "DependencyPermissions");
        Assert.True(permissions.GetProperty("is_flags").GetBoolean());
        Assert.Equal(
            ["all=All=3u", "none=None=0u", "read=Read=1u", "write=Write=2u"],
            permissions.GetProperty("values").EnumerateArray().Select(static value =>
                $"{value.GetProperty("native_name").GetString()}={value.GetProperty("managed_name").GetString()}={value.GetProperty("value_expression").GetString()}"));
        Assert.False(Type(DependencyDocument, "DependencyState").GetProperty("is_flags").GetBoolean());
    }

    [Fact]
    public void Aliases_carry_fully_qualified_concrete_callback_and_projected_targets()
    {
        var box = Alias(DependencyDocument, "DependencyIntBoxAlias").GetProperty("target_type");
        Assert.Equal("global::SharpCpp.Tests.E2E.Modules.Metadata.Dependency.DependencyBox<int>", box.GetProperty("public_type").GetString());
        Assert.Equal("int", Assert.Single(box.GetProperty("generic_arguments").EnumerateArray()).GetProperty("public_type").GetString());
        Assert.Equal("Sharpcpp_coverage", Alias(DependencyDocument, "DependencyIntBoxAlias").GetProperty("managed_namespace").GetString());

        var transform = Alias(DependencyDocument, "DependencyTransform").GetProperty("target_type");
        Assert.True(transform.GetProperty("is_callback").GetBoolean());
        Assert.Equal("global::System.Func<int, int>", transform.GetProperty("public_type").GetString());
        Assert.Equal("int", transform.GetProperty("callback_return_type").GetProperty("cpp_type").GetString());
        Assert.Equal("int", Assert.Single(transform.GetProperty("callback_parameter_types").EnumerateArray()).GetProperty("cpp_type").GetString());

        Assert.Equal("global::Std.Vector<int>", Alias(DependencyDocument, "DependencyDynamicAlias").GetProperty("target_type").GetProperty("public_type").GetString());
    }

    [Fact]
    public void Types_and_references_use_the_projected_namespaces()
    {
        Assert.Equal("Container_aliases.Alpha", Type(Main, "Context", "record:container_aliases::alpha::context").GetProperty("managed_namespace").GetString());
        Assert.Equal("global::Container_aliases.Intersection", Function(Main, "Classify").GetProperty("return_type").GetProperty("public_type").GetString());
    }

    [Fact]
    public void Members_carry_defaults_operators_delegate_policy_and_array_shapes()
    {
        var operatorBox = Type(Main, "OperatorBox").GetProperty("members").EnumerateArray().ToArray();
        Assert.Contains(operatorBox, static member => member.TryGetProperty("operator_token", out var token) && token.GetString() == "+");
        var toBool = Assert.Single(operatorBox, static member => Name(member) == "OperatorToBool");
        Assert.True(toBool.GetProperty("is_conversion_operator").GetBoolean());
        Assert.True(toBool.GetProperty("is_explicit_conversion_operator").GetBoolean());

        var describe = Function(Main, "DescribeLevel").GetProperty("parameters").EnumerateArray().ToArray();
        Assert.False(describe[0].TryGetProperty("default_value_expression", out _));
        Assert.Equal("5", describe[1].GetProperty("default_value_expression").GetString());

        var callback = Function(Main, "InvokeNamedDelegateCallback").GetProperty("parameters")[0];
        Assert.True(callback.GetProperty("use_callback_delegate").GetBoolean());
        Assert.Equal("ValueCallback", callback.GetProperty("callback_delegate_type_name").GetString());

        Assert.True(Type(Main, "SharedArray").GetProperty("is_dynamic_array").GetBoolean());
        var sharedArray = Function(Main, "MakeSurfaceSharedArray").GetProperty("return_type");
        Assert.Equal("int", sharedArray.GetProperty("dynamic_array_element_type").GetProperty("public_type").GetString());
    }

    [Fact]
    public void Ownership_and_runtime_policy_are_exported()
    {
        Assert.True(Function(Main, "CreateRefTarget").GetProperty("returns_owned_handle").GetBoolean());
        var target = Assert.Single(Type(Main, "RefHolder").GetProperty("members").EnumerateArray(), static member => Name(member) == "Target");
        Assert.True(target.GetProperty("getter_returns_owned_handle").GetBoolean());

        var policy = Type(Main, "RuntimePolicyBase");
        Assert.Equal("global::SharpCpp.Tests.E2E.Lifetime.CoveragePolicyCache.Shared", policy.GetProperty("managed_instance_cache_factory_expression").GetString());
        Assert.Equal("global::SharpCpp.Support.INativeInstanceCache", policy.GetProperty("managed_instance_cache_type_name").GetString());
        Assert.False(policy.GetProperty("emit_default_native_release_override").GetBoolean());
    }

    [Fact]
    public void Ignored_surface_is_not_exported()
    {
        Assert.DoesNotContain(Main.GetProperty("types").EnumerateArray(), static type => Name(type).StartsWith("SkippedRecord", StringComparison.Ordinal));
        Assert.Equal(
            ["PartiallyIgnoredRecord", "VisibleField"],
            Type(Main, "PartiallyIgnoredRecord").GetProperty("members").EnumerateArray().Select(Name));
    }

    [Fact]
    public void Imported_records_keep_their_managed_identity_and_bases()
    {
        Assert.Equal(typeof(DependencyBase), typeof(ConsumerDerived).BaseType);
        var consumerDerived = Type(ConsumerDocument, "ConsumerDerived");
        Assert.Equal("global::SharpCpp.Tests.E2E.Modules.Metadata.Dependency.DependencyBase", consumerDerived.GetProperty("managed_base_type_name").GetString());
        Assert.DoesNotContain(ConsumerDocument.GetProperty("types").EnumerateArray(), static type => Name(type) == "DependencyBase");
    }

    [Fact]
    public void Owned_string_returns_export_the_public_string_and_their_free_function()
    {
        // Header attribute and consumer tag alike: the exported return is the managed string the
        // wrapper exposes (not the raw address the import returns), plus the function that frees it.
        foreach (var name in new[] { "MakeOwnedText", "MakeOwnedConstText", "MakeConsumerOwnedText" })
        {
            var function = Function(Main, name);
            var returnType = function.GetProperty("return_type");
            Assert.Equal("string", returnType.GetProperty("public_type").GetString());
            Assert.Equal("unsupported", returnType.GetProperty("primitive").GetString());
            Assert.EndsWith("*", returnType.GetProperty("cpp_type").GetString(), StringComparison.Ordinal);
            Assert.True(function.TryGetProperty("return_string_free_function", out _), name);
        }

        var owned = Function(Main, "MakeOwnedText").GetProperty("return_string_free_function");
        Assert.Equal("release_owned_text", owned.GetProperty("native_name").GetString());
        Assert.Equal("release_owned_text", owned.GetProperty("cpp_qualified_name").GetString());
        Assert.Equal("release_owned_memory", Function(Main, "MakeOwnedConstText").GetProperty("return_string_free_function").GetProperty("native_name").GetString());
        Assert.Equal("release_owned_text", Function(Main, "MakeConsumerOwnedText").GetProperty("return_string_free_function").GetProperty("native_name").GetString());
    }

    [Fact]
    public void Owned_string_output_parameters_export_their_free_function()
    {
        foreach (var name in new[] { "GetOwnedLabel", "GetConsumerOwnedLabel" })
        {
            var function = Function(Main, name);
            Assert.False(function.TryGetProperty("return_string_free_function", out _));
            var parameter = Assert.Single(function.GetProperty("parameters").EnumerateArray());
            Assert.Equal("out", parameter.GetProperty("direction").GetString());
            Assert.Equal("string", parameter.GetProperty("type").GetProperty("public_type").GetString());
            Assert.Equal("release_owned_text", parameter.GetProperty("string_free_function").GetProperty("native_name").GetString());
        }

        // Both forms on one function: the return and the output parameter each name their own free.
        var paired = Function(Main, "MakeOwnedTextWithLabel");
        Assert.Equal("release_owned_text", paired.GetProperty("return_string_free_function").GetProperty("native_name").GetString());
        Assert.Equal("release_owned_text", Assert.Single(paired.GetProperty("parameters").EnumerateArray()).GetProperty("string_free_function").GetProperty("native_name").GetString());
    }

    [Fact]
    public void Functions_without_freed_by_export_no_free_function()
    {
        // An ordinary char* return stays the raw address; an ordinary input string has no free either.
        var plain = Function(Main, "MakeBleedUntaggedAfterBody");
        Assert.Equal("global::System.IntPtr", plain.GetProperty("return_type").GetProperty("public_type").GetString());
        Assert.False(plain.TryGetProperty("return_string_free_function", out _));

        // Only the validated freed_by functions carry the fields; every other function (and the
        // dependency/consumer documents) omits them entirely.
        static string[] WithFreeFunction(JsonElement document) => document.GetProperty("functions").EnumerateArray()
            .Where(static function => function.TryGetProperty("return_string_free_function", out _)
                || function.GetProperty("parameters").EnumerateArray().Any(static parameter => parameter.TryGetProperty("string_free_function", out _)))
            .Select(Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        string[] expected =
            [
                "GetConsumerOwnedLabel", "GetOwnedLabel", "GetOwnedLabelUnwritten", "GetOwnedNullLabel",
                "MakeConsumerOwnedText", "MakeNullOwnedText", "MakeOwnedBleedTagged", "MakeOwnedConstText",
                "MakeOwnedText", "MakeOwnedTextWithLabel",
            ];
        Assert.Equal(expected, WithFreeFunction(Main));
        Assert.Empty(WithFreeFunction(DependencyDocument));
        Assert.Empty(WithFreeFunction(ConsumerDocument));
    }

    private static string Name(JsonElement element) => element.GetProperty("managed_name").GetString()!;

    private static JsonElement Type(JsonElement document, string managedName, string? symbolId = null) =>
        Assert.Single(document.GetProperty("types").EnumerateArray(), type =>
            Name(type) == managedName && (symbolId is null || type.GetProperty("symbol_id").GetString() == symbolId));

    private static JsonElement Function(JsonElement document, string managedName) =>
        Assert.Single(document.GetProperty("functions").EnumerateArray(), function => Name(function) == managedName);

    private static JsonElement Alias(JsonElement document, string managedName) =>
        Assert.Single(document.GetProperty("aliases").EnumerateArray(), alias => Name(alias) == managedName);

    private static void AssertOrdered(IEnumerable<string> values)
    {
        var actual = values.ToArray();
        Assert.Equal(actual.Order(StringComparer.Ordinal), actual);
    }
}
