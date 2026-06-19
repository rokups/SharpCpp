using System.Reflection;

using Xunit;

namespace SharpCpp.Tests.Model;

public sealed class MarshallerConfigExtractorTests
{
    [Fact]
    public void ExtractManagedTypeOverrides_reads_token_spaced_attribute_text_from_ast_extent()
    {
        var repoRoot = FindRepositoryRoot();
        var fixtureDir = Path.Combine(repoRoot, "tests", "SharpCpp.Tests.Model", "Fixtures", "MarshallerConfigParsing");
        var headerPath = Path.Combine(fixtureDir, "MarshallerConfig.hpp");

        var extractorType = Type.GetType("SharpCpp.Parsing.MarshallerConfigExtractor, SharpCpp.Parsing", throwOnError: true)!;
        var method = extractorType.GetMethod("ExtractManagedTypeOverrides", BindingFlags.Public | BindingFlags.Static)!;

        var overrides = (System.Collections.IDictionary)method.Invoke(
            null,
            [new[] { headerPath }, new[] { "-x", "c++", "-std=c++20", $"-I{fixtureDir}" }])!;

        Assert.Equal(3, overrides.Count);
        Assert.True(overrides.Contains("int"));
        var overrideInfo = overrides["int"]!;

        var overrideType = overrideInfo.GetType();
        Assert.Equal("global::Test.Managed", (string)overrideType.GetProperty("PublicTypeName")!.GetValue(overrideInfo)!);
        Assert.True((bool)overrideType.GetProperty("Undefaultable")!.GetValue(overrideInfo)!);

        Assert.True(overrides.Contains("return:float"));
        var returnOverride = overrides["return:float"]!;
        var returnOverrideType = returnOverride.GetType();
        Assert.Equal("owning", (string)returnOverrideType.GetProperty("ReturnOwnership")!.GetValue(returnOverride)!);
        Assert.False((bool)returnOverrideType.GetProperty("HasPublicTypeMapping")!.GetValue(returnOverride)!);

        var callbackOverride = overrides.Values.Cast<object>().Single(candidate =>
            (bool)candidate.GetType().GetProperty("Callback")!.GetValue(candidate)!);
        var callbackOverrideType = callbackOverride.GetType();
        Assert.Contains("std::function", (string)callbackOverrideType.GetProperty("CppTypeName")!.GetValue(callbackOverride)!, StringComparison.Ordinal);
        Assert.True((bool)callbackOverrideType.GetProperty("HasPublicTypeMapping")!.GetValue(callbackOverride)!);
        Assert.Equal("global::Test.CallbackMarshaller", (string)callbackOverrideType.GetProperty("ManagedMarshaller")!.GetValue(callbackOverride)!);
    }

    [Fact]
    public void ApplyRecordMetadata_uses_expose_member_entries_from_marshaller_configs()
    {
        var compilation = new SharpCpp.Model.Ast.BindingCompilation("TestProject", "Test.Namespace", "Bindings", "Test.Native", new[] { "MarshallerConfig.hpp" });
        var record = new SharpCpp.Model.Ast.RecordBinding("record:widget", "Widget", "Widget", "Widget");
        compilation.Members.Add(record);

        var extractorType = Type.GetType("SharpCpp.Parsing.MarshallerConfigExtractor, SharpCpp.Parsing", throwOnError: true)!;
        var exposedMemberType = extractorType.GetNestedType("ExposedMemberInfo", BindingFlags.NonPublic)!;
        var configType = extractorType.GetNestedType("MarshallerBodyConfig", BindingFlags.NonPublic)!;

        var exposedMemberCtor = exposedMemberType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(ctor => ctor.GetParameters().Length == 6);
        var exposedParameters = Array.CreateInstance(typeof(ValueTuple<string, string>), 0);
        var exposedMember = exposedMemberCtor.Invoke(["Value", "int", true, "property", false, exposedParameters])!;

        var configCtor = configType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(ctor => ctor.GetParameters().Length == 6);
        var exposedMembers = Array.CreateInstance(exposedMemberType, 1);
        exposedMembers.SetValue(exposedMember, 0);
        var genericParameters = Array.CreateInstance(typeof(string), 0);
        var config = configCtor.Invoke(["[[sharpcpp::managed_type(public_type = \"global::Test.Managed\")]]", exposedMembers, genericParameters, null, null, true])!;

        var applyMethod = Type.GetType("SharpCpp.Parsing.MarshallerMetadataPhase2, SharpCpp.Parsing", throwOnError: true)!
            .GetMethod("ApplyRecordMetadata", BindingFlags.NonPublic | BindingFlags.Static)!;
        var configs = (System.Collections.IDictionary)Activator.CreateInstance(
            typeof(Dictionary<,>).MakeGenericType(typeof(string), configType),
            StringComparer.Ordinal)!;
        configs["Widget"] = config;

        var recordsByNativeName = (System.Collections.IDictionary)Activator.CreateInstance(
            typeof(Dictionary<,>).MakeGenericType(typeof(string), typeof(SharpCpp.Model.Ast.RecordBinding)),
            StringComparer.Ordinal)!;
        recordsByNativeName[record.NativeName] = record;

        applyMethod.Invoke(null, [compilation, configs, recordsByNativeName]);

        var function = Assert.Single(record.Members.OfType<SharpCpp.Model.Ast.FunctionBinding>());
        Assert.Equal("Value", function.ManagedName);
        Assert.Equal("int", function.ReturnType.ManagedTypeName);
        Assert.True(function.IsConstMethod);
        Assert.Empty(function.Parameters);
    }

    [Fact]
    public void ApplyRecordMetadata_leaves_synthetic_record_header_path_null()
    {
        var compilation = new SharpCpp.Model.Ast.BindingCompilation("TestProject", "Test.Namespace", "Bindings", "Test.Native", new[] { "MarshallerConfig.hpp" });

        var extractorType = Type.GetType("SharpCpp.Parsing.MarshallerConfigExtractor, SharpCpp.Parsing", throwOnError: true)!;
        var exposedMemberType = extractorType.GetNestedType("ExposedMemberInfo", BindingFlags.NonPublic)!;
        var configType = extractorType.GetNestedType("MarshallerBodyConfig", BindingFlags.NonPublic)!;

        var configCtor = configType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(ctor => ctor.GetParameters().Length == 6);
        var exposedMembers = Array.CreateInstance(exposedMemberType, 0);
        var genericParameters = Array.CreateInstance(typeof(string), 0);
        var config = configCtor.Invoke(["[[sharpcpp::managed_type(managed_base_type = \"global::SharpCpp.Support.StringBase\")]]", exposedMembers, genericParameters, null, null, true])!;

        var applyMethod = Type.GetType("SharpCpp.Parsing.MarshallerMetadataPhase2, SharpCpp.Parsing", throwOnError: true)!
            .GetMethod("ApplyRecordMetadata", BindingFlags.NonPublic | BindingFlags.Static)!;
        var configs = (System.Collections.IDictionary)Activator.CreateInstance(
            typeof(Dictionary<,>).MakeGenericType(typeof(string), configType),
            StringComparer.Ordinal)!;
        configs["std::basic_string<char>"] = config;

        var recordsByNativeName = (System.Collections.IDictionary)Activator.CreateInstance(
            typeof(Dictionary<,>).MakeGenericType(typeof(string), typeof(SharpCpp.Model.Ast.RecordBinding)),
            StringComparer.Ordinal)!;

        applyMethod.Invoke(null, [compilation, configs, recordsByNativeName]);

        var record = Assert.Single(compilation.Members.OfType<SharpCpp.Model.Ast.RecordBinding>());
        Assert.Equal("std::basic_string", record.NativeName);
        Assert.Null(record.HeaderIncludePath);
    }

    [Fact]
    public void ExtractMarshallerBodies_includes_inherited_expose_members_and_leaf_overrides()
    {
        var repoRoot = FindRepositoryRoot();
        var fixtureDir = Path.Combine(repoRoot, "tests", "SharpCpp.Tests.Model", "Fixtures", "InheritedMarshallerConfig");
        var headerPath = Path.Combine(fixtureDir, "InheritedMarshallerConfig.hpp");

        var extractorType = Type.GetType("SharpCpp.Parsing.MarshallerConfigExtractor, SharpCpp.Parsing", throwOnError: true)!;
        var method = extractorType.GetMethod("ExtractMarshallerBodies", BindingFlags.Public | BindingFlags.Static)!;

        var bodies = (System.Collections.IDictionary)method.Invoke(
            null,
            [new[] { headerPath }, new[] { "-x", "c++", "-std=c++20", $"-I{fixtureDir}" }])!;

        var body = bodies["widget"]!;
        var bodyType = body.GetType();
        var members = ((System.Collections.IEnumerable)bodyType.GetProperty("ExposedMembers")!.GetValue(body)!)
            .Cast<object>()
            .ToArray();

        Assert.Equal(2, members.Length);
        Assert.Contains(members, member => (string)member.GetType().GetProperty("MethodName")!.GetValue(member)! == "GetCountCore");
        var addCore = Assert.Single(members.Where(member => (string)member.GetType().GetProperty("MethodName")!.GetValue(member)! == "AddCore"));
        Assert.Equal("method", (string)addCore.GetType().GetProperty("Shape")!.GetValue(addCore)!);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")) &&
                Directory.Exists(Path.Combine(directory.FullName, "include")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Unable to locate repository root.");
    }
}
