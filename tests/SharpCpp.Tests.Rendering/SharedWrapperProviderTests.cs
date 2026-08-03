using System;
using System.Linq;
using System.Text.RegularExpressions;

using SharpCpp.Model.Ast;
using SharpCpp.Rendering;
using Std.Infrastructure;

using Xunit;

namespace SharpCpp.Tests.Rendering;

public sealed class SharedWrapperProviderTests
{
    [Fact]
    public void Renderer_emits_module_local_provider_glue_for_a_shared_wrapper()
    {
        var compilation = new BindingCompilation(
            "Consumer.Module",
            "Consumer",
            "Bindings",
            "Consumer.Native",
            ["std/string"]);
        var record = new RecordBinding("record:std::string", "string", "String", "std::string")
        {
            ManagedNamespace = "Std",
            ManagedTypeFormat = "Std.String",
            ManagedBaseTypeName = "global::SharpCpp.Support.StringBase",
            HasCopyConstructor = true
        };
        record.SetMetadata(MetadataKeys.ManagedWrapperAssembly, "SharpCpp.Std");
        record.SetMetadata(MetadataKeys.ManagedWrapperProviderKind, "string");

        AddStringProviderMethods(record, "consumer_std_string");
        compilation.Members.Add(record);

        var other = new RecordBinding("record:eastl::string", "basic_string", "String", "eastl::basic_string<char>")
        {
            ManagedNamespace = "Ea",
            ManagedTypeFormat = "Ea.String",
            ManagedBaseTypeName = "global::SharpCpp.Support.StringBase",
            HasCopyConstructor = true
        };
        other.SetMetadata(MetadataKeys.ManagedWrapperAssembly, "SharpCpp.Ea");
        other.SetMetadata(MetadataKeys.ManagedWrapperProviderKind, "string");
        AddStringProviderMethods(other, "consumer_ea_string");
        compilation.Members.Add(other);

        var custom = new RecordBinding("record:acme::text", "text", "Text", "acme::text")
        {
            ManagedTypeFormat = "Acme.Text",
            ManagedBaseTypeName = "global::SharpCpp.Support.StringBase",
            HasCopyConstructor = true
        };
        custom.SetMetadata(MetadataKeys.ManagedWrapperAssembly, "Acme.Standard");
        custom.SetMetadata(MetadataKeys.ManagedWrapperProviderKind, "string");
        custom.SetMetadata(MetadataKeys.ManagedWrapperProviderNamespace, "Acme.Infrastructure");
        AddStringProviderMethods(custom, "consumer_acme_text");
        compilation.Members.Add(custom);

        var files = BindingOutputRenderer.Render(compilation, "Consumer").Files;
        var managed = files.Single(file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;
        var aliases = files.Single(file => file.RelativePath.EndsWith(".Bindings.Aliases.g.cs", StringComparison.Ordinal)).Content;
        var native = files.Single(file => file.RelativePath.EndsWith(".Bindings.g.cpp", StringComparison.Ordinal)).Content;
        Assert.DoesNotContain("public partial class String", managed, StringComparison.Ordinal);
        Assert.Contains("CreateManagedWrapper", managed, StringComparison.Ordinal);
        Assert.Contains("moduleId: \"Consumer.Module\"", managed, StringComparison.Ordinal);
        Assert.Contains("[global::System.Runtime.CompilerServices.ModuleInitializer]", aliases, StringComparison.Ordinal);
        Assert.Contains("ProviderRegistry.Register<global::Std.Infrastructure.IStringProvider>(new", aliases, StringComparison.Ordinal);
        Assert.Contains("IStringProvider", aliases, StringComparison.Ordinal);
        Assert.Contains("__StdProvider_String_", managed, StringComparison.Ordinal);
        Assert.Contains("__EaProvider_String_", managed, StringComparison.Ordinal);
        Assert.Contains("__AcmeProvider_Text_", managed, StringComparison.Ordinal);
        Assert.Contains("global::Acme.Infrastructure.ProviderRegistry.Register", aliases, StringComparison.Ordinal);
        Assert.Contains("string_", native, StringComparison.Ordinal);
        Assert.Contains("_shared_create", native, StringComparison.Ordinal);
        var cloneEntryPoint = Regex.Match(native, @"sharpcpp_e2e_string_[0-9a-f]{8}_clone").Value;
        Assert.NotEmpty(cloneEntryPoint);
        Assert.Contains($"EntryPoint = \"{cloneEntryPoint}\"", managed, StringComparison.Ordinal);
        Assert.DoesNotContain("sharpcpp_e2e_std_string_clone", native, StringComparison.Ordinal);
        Assert.Contains("consumer_std_string_size", native, StringComparison.Ordinal);
    }

    [Fact]
    public void Renderer_rejects_incomplete_shared_provider_contracts()
    {
        var compilation = new BindingCompilation(
            "Consumer.Module",
            "Consumer",
            "Bindings",
            "Consumer.Native",
            ["custom/text.h"]);
        var record = new RecordBinding("record:custom::text", "text", "Text", "custom::text")
        {
            ManagedTypeFormat = "Custom.Text"
        };
        record.SetMetadata(MetadataKeys.ManagedWrapperAssembly, "Custom.Standard");
        record.SetMetadata(MetadataKeys.ManagedWrapperProviderKind, "string");
        record.SetMetadata(MetadataKeys.ManagedWrapperProviderNamespace, "Custom.Infrastructure");
        compilation.Members.Add(record);

        var exception = Assert.Throws<InvalidOperationException>(() => BindingOutputRenderer.Render(compilation, "Consumer"));

        Assert.Contains("requires provider method 'GetCountCore'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Std_registry_prefers_the_requesting_module_and_rejects_ambiguous_fallbacks()
    {
        ProviderRegistry.Clear();
        try
        {
            var first = new StringProvider("module-a", "std::string");
            var second = new StringProvider("module-b", "std::string");
            ProviderRegistry.Register<IStringProvider>(first);
            ProviderRegistry.Register<IStringProvider>(second);

            Assert.Same(first, ProviderRegistry.Resolve<IStringProvider>("module-a"));
            Assert.Same(second, ProviderRegistry.Resolve<IStringProvider>("module-b"));
            using (ProviderRegistry.PushModule("module-a"))
            {
                Assert.Same(first, ProviderRegistry.Resolve<IStringProvider>());
            }

            Assert.Throws<InvalidOperationException>(() => ProviderRegistry.Resolve<IStringProvider>());
        }
        finally
        {
            ProviderRegistry.Clear();
        }
    }

    [Fact]
    public void Std_wrapper_instance_cache_is_scoped_to_provider_identity()
    {
        ProviderRegistry.Clear();
        var first = new StringProvider("module-a", "std::string");
        var second = new StringProvider("module-b", "std::string");
        try
        {
            var firstWrapper = Std.String.CreateManagedWrapper((nint)123, ownsHandle: false, first);
            var firstAgain = Std.String.CreateManagedWrapper((nint)123, ownsHandle: false, first);
            var secondWrapper = Std.String.CreateManagedWrapper((nint)123, ownsHandle: false, second);

            Assert.Same(firstWrapper, firstAgain);
            Assert.NotSame(firstWrapper, secondWrapper);
        }
        finally
        {
            ProviderRegistry.Clear();
        }
    }

    [Fact]
    public void Wrapper_assemblies_keep_their_provider_registries_isolated()
    {
        ProviderRegistry.Clear();
        Ea.Infrastructure.ProviderRegistry.Clear();
        try
        {
            ProviderRegistry.Register<IStringProvider>(new StringProvider("module-a", "std::string"));

            Assert.Throws<InvalidOperationException>(() => Ea.Infrastructure.ProviderRegistry.Resolve<Ea.Infrastructure.IStringProvider>());
        }
        finally
        {
            ProviderRegistry.Clear();
            Ea.Infrastructure.ProviderRegistry.Clear();
        }
    }

    private sealed class StringProvider(string moduleId, string shapeKey) : IStringProvider
    {
        public string ModuleId { get; } = moduleId;
        public string ShapeKey { get; } = shapeKey;

        public nint Create() => (nint)1;
        public void Delete(nint handle) { }
        public ulong GetCount(nint handle) => 0;
        public byte GetAt(nint handle, ulong index) => 0;
        public void Clear(nint handle) { }
        public void Add(nint handle, byte value) { }
    }

    private static void AddStringProviderMethods(RecordBinding record, string entryPointPrefix)
    {
        AddProviderMethod(record, "size", "GetCountCore", BindingType.UInt64, "std::size_t", $"{entryPointPrefix}_size");
        AddProviderMethod(
            record,
            "at",
            "GetAtCore",
            BindingType.UInt8,
            "unsigned char",
            $"{entryPointPrefix}_at",
            true,
            ("index", BindingType.UInt64, "std::size_t"));
        AddProviderMethod(record, "clear", "ClearCore", BindingType.Void, "void", $"{entryPointPrefix}_clear", isConst: false);
        AddProviderMethod(
            record,
            "push_back",
            "AddCore",
            BindingType.Void,
            "void",
            $"{entryPointPrefix}_add",
            isConst: false,
            ("value", BindingType.UInt8, "unsigned char"));
    }

    private static void AddProviderMethod(
        RecordBinding record,
        string nativeName,
        string managedName,
        BindingType returnType,
        string cppReturnTypeName,
        string entryPoint,
        bool isConst = true,
        params (string Name, BindingType Type, string CppTypeName)[] parameters)
    {
        var function = new FunctionBinding(
            $"method:{record.CppQualifiedName}:{nativeName}",
            nativeName,
            managedName,
            returnType,
            entryPoint,
            cppReturnTypeName,
            $"{record.CppQualifiedName}::{nativeName}")
        {
            IsConstMethod = isConst,
            IsStatic = false,
            OwningRecordCppTypeName = record.CppQualifiedName
        };
        foreach (var (name, type, cppTypeName) in parameters)
        {
            function.Parameters.Add(new ParameterBinding(
                $"{function.SymbolId}:param:{name}",
                name,
                name,
                type,
                cppTypeName: cppTypeName));
        }

        function.SetMetadata(MetadataKeys.MarshallerExposedMethod, record.CppQualifiedName);
        record.Members.Add(function);
    }
}
