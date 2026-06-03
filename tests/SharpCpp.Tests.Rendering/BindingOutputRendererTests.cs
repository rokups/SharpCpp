using SharpCpp.Model.Ast;
using SharpCpp.Rendering;

using Xunit;

namespace SharpCpp.Tests.Rendering;

public sealed class BindingOutputRendererTests
{
    [Fact]
    public void Native_output_uses_export_macro_with_extern_c()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "Builtin/Builtin.hpp" });
        var function = new FunctionBinding(
            "function:add_numbers",
            "add_numbers",
            "AddNumbers",
            BindingType.Int32,
            "sharpcpp_sample_add_numbers");
        function.Parameters.Add(new ParameterBinding("parameter:add_numbers:lhs", "lhs", "lhs", BindingType.Int32));
        function.Parameters.Add(new ParameterBinding("parameter:add_numbers:rhs", "rhs", "rhs", BindingType.Int32));
        compilation.Members.Add(function);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var nativeOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cpp", StringComparison.Ordinal)).Content;

        Assert.Contains("using namespace SharpCpp;", nativeOutput, StringComparison.Ordinal);
        Assert.Contains("#define SHARPCPP_EXPORT extern \"C\"", nativeOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("extern \"C\"\n{", nativeOutput, StringComparison.Ordinal);
        Assert.Contains("#include \"SharpCpp/Marshaller.hpp\"", nativeOutput, StringComparison.Ordinal);
        Assert.Contains("SHARPCPP_EXPORT ret_t<int> sharpcpp_sample_add_numbers(arg_t<int> lhs, arg_t<int> rhs)", nativeOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Managed_output_uses_ast_direction_and_abi_kind_for_handle_parameters()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "Records/Composition.hpp" });
        var payloadRecord = new RecordBinding("record:payload_record", "payload_record", "PayloadRecord");
        var function = new FunctionBinding(
            "function:write_payload",
            "write_payload",
            "WritePayload",
            BindingType.Void,
            "sharpcpp_sample_write_payload");
        function.Parameters.Add(new ParameterBinding(
            "parameter:write_payload:payload",
            "payload",
            "payload",
            payloadRecord.HandleType,
            BindingParameterDirection.Out,
            BindingParameterAbiKind.Value,
            "payload_record*&"));
        compilation.Members.Add(payloadRecord);
        compilation.Members.Add(function);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;

        Assert.Contains("public static void WritePayload(out PayloadRecord payload)", managedOutput, StringComparison.Ordinal);
        Assert.Contains("private static partial void __PInvoke_WritePayload_payload_recordPtrRef(out nint payload);", managedOutput, StringComparison.Ordinal);
        Assert.Contains("nint __abi_payload;", managedOutput, StringComparison.Ordinal);
        Assert.Contains("payload = new PayloadRecord(__abi_payload);", managedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Managed_output_renders_flags_enum_from_ast()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "Enum/Enum.hpp" });
        var @enum = new EnumBinding("enum:permissions", "permissions", "Permissions", "unsigned int")
        {
            IsFlags = true
        };
        @enum.Values.Add(new EnumValueBinding("none", "None", "0u"));
        @enum.Values.Add(new EnumValueBinding("read", "Read", "1u"));
        compilation.Members.Add(@enum);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;

        Assert.Contains("[Flags]", managedOutput, StringComparison.Ordinal);
        Assert.Contains("public enum Permissions : uint", managedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Managed_output_uses_ast_base_type_and_readonly_field_shape()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "Records/Inheritance.hpp" });
        var baseRecord = new RecordBinding("record:base_box", "base_box", "BaseBox");
        var derivedRecord = new RecordBinding("record:derived_box", "derived_box", "DerivedBox")
        {
            ManagedBaseTypeName = "BaseBox"
        };
        derivedRecord.Members.Add(new FieldBinding(
            "field:derived_box:count",
            "count",
            "Count",
            BindingType.Int32,
            "int",
            "derived_box",
            isReadOnly: true));
        compilation.Members.Add(baseRecord);
        compilation.Members.Add(derivedRecord);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;

        Assert.Contains("public partial class DerivedBox : BaseBox", managedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("__PInvoke_Set_Count", managedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Managed_output_can_synthesize_properties_from_ast_annotations()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "Records/AccessorProperty.hpp" });
        var record = new RecordBinding("record:accessor_box", "accessor_box", "AccessorBox");
        var getter = new FunctionBinding("method:accessor_box:get_count", "get_count", "GetCount", BindingType.Int32, "entry_get") { OwningRecordCppTypeName = "accessor_box" };
        getter.SetMetadata(MetadataKeys.AccessorProperty, new AccessorPropertyAnnotation("Count", AccessorPropertyKind.Getter));
        var setter = new FunctionBinding("method:accessor_box:set_count", "set_count", "SetCount", BindingType.Void, "entry_set") { OwningRecordCppTypeName = "accessor_box" };
        setter.Parameters.Add(new ParameterBinding("parameter:set_count:value", "value", "value", BindingType.Int32));
        setter.SetMetadata(MetadataKeys.AccessorProperty, new AccessorPropertyAnnotation("Count", AccessorPropertyKind.Setter));
        record.Members.Add(getter);
        record.Members.Add(setter);
        compilation.Members.Add(record);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;

        Assert.Contains("public int Count", managedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("public int GetCount()", managedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("public void SetCount(int value)", managedOutput, StringComparison.Ordinal);
    }
}
