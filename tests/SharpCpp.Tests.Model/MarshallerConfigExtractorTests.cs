using System.Reflection;

using Xunit;

namespace SharpCpp.Tests.Model;

public sealed class MarshallerConfigExtractorTests
{
    [Fact]
    public void ExtractManagedTypeOverrides_reads_token_spaced_attribute_text_from_ast_extent()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"SharpCpp.Tests.Model.{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var headerPath = Path.Combine(tempDir, "MarshallerConfig.hpp");
        File.WriteAllText(headerPath, """
            #pragma once

            template <class T>
            struct Marshaller;

            template <>
            struct [[sharpcpp::managed_type(public_type = "global::Test.Managed", undefaultable = true)]] Marshaller<int> {};
            """);

        try
        {
            var extractorType = Type.GetType("SharpCpp.Parsing.MarshallerConfigExtractor, SharpCpp.Parsing", throwOnError: true)!;
            var method = extractorType.GetMethod("ExtractManagedTypeOverrides", BindingFlags.Public | BindingFlags.Static)!;

            var overrides = (System.Collections.IDictionary)method.Invoke(
                null,
                [new[] { headerPath }, new[] { "-x", "c++", "-std=c++20", $"-I{tempDir}" }])!;

            Assert.Single(overrides);
            Assert.True(overrides.Contains("int"));
            var overrideInfo = overrides["int"]!;

            var overrideType = overrideInfo.GetType();
            Assert.Equal("global::Test.Managed", (string)overrideType.GetProperty("PublicTypeName")!.GetValue(overrideInfo)!);
            Assert.True((bool)overrideType.GetProperty("Undefaultable")!.GetValue(overrideInfo)!);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
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
            .Single(ctor => ctor.GetParameters().Length == 2);
        var exposedMembers = Array.CreateInstance(exposedMemberType, 1);
        exposedMembers.SetValue(exposedMember, 0);
        var config = configCtor.Invoke(["[[sharpcpp::managed_type(public_type = \"global::Test.Managed\")]]", exposedMembers])!;

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
}
