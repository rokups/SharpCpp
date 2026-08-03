using System;
using System.Linq;

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

        var size = new FunctionBinding(
            "method:std::string:size",
            "size",
            "GetCountCore",
            BindingType.UInt64,
            "consumer_std_string_size",
            "std::size_t",
            "std::string::size")
        {
            IsConstMethod = true,
            IsStatic = false,
            OwningRecordCppTypeName = "std::string"
        };
        size.SetMetadata(MetadataKeys.MarshallerExposedMethod, "std::string");
        record.Members.Add(size);
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
        var otherSize = new FunctionBinding(
            "method:eastl::string:size",
            "size",
            "GetCountCore",
            BindingType.UInt64,
            "consumer_ea_string_size",
            "std::size_t",
            "eastl::basic_string<char>::size")
        {
            IsConstMethod = true,
            IsStatic = false,
            OwningRecordCppTypeName = "eastl::basic_string<char>"
        };
        otherSize.SetMetadata(MetadataKeys.MarshallerExposedMethod, "eastl::basic_string<char>");
        other.Members.Add(otherSize);
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
        compilation.Members.Add(custom);

        var files = BindingOutputRenderer.Render(compilation, "Consumer").Files;
        var managed = files.Single(file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;
        var aliases = files.Single(file => file.RelativePath.EndsWith(".Bindings.Aliases.g.cs", StringComparison.Ordinal)).Content;
        var native = files.Single(file => file.RelativePath.EndsWith(".Bindings.g.cpp", StringComparison.Ordinal)).Content;
        Assert.DoesNotContain("public partial class String", managed, StringComparison.Ordinal);
        Assert.Contains("CreateManagedWrapper", managed, StringComparison.Ordinal);
        Assert.Contains("moduleId: \"Consumer.Module\"", managed, StringComparison.Ordinal);
        Assert.Contains("[global::System.Runtime.CompilerServices.ModuleInitializer]", aliases, StringComparison.Ordinal);
        Assert.Contains("ProviderRegistry.Register(new", aliases, StringComparison.Ordinal);
        Assert.Contains("IStringProvider", aliases, StringComparison.Ordinal);
        Assert.Contains("__StdProvider_String_", managed, StringComparison.Ordinal);
        Assert.Contains("__EaProvider_String_", managed, StringComparison.Ordinal);
        Assert.Contains("__AcmeProvider_Text_", managed, StringComparison.Ordinal);
        Assert.Contains("global::Acme.Infrastructure.ProviderRegistry.Register", aliases, StringComparison.Ordinal);
        Assert.Contains("string_", native, StringComparison.Ordinal);
        Assert.Contains("_shared_create", native, StringComparison.Ordinal);
        Assert.Contains("consumer_std_string_size", native, StringComparison.Ordinal);
    }

    [Fact]
    public void Std_registry_prefers_the_requesting_module_and_rejects_ambiguous_fallbacks()
    {
        ProviderRegistry.Clear();
        try
        {
            var first = new StringProvider("module-a", "std::string");
            var second = new StringProvider("module-b", "std::string");
            ProviderRegistry.Register(first);
            ProviderRegistry.Register(second);

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
}
