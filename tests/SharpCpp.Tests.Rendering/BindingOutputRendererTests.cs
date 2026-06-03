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

    [Fact]
    public void Managed_output_uses_distinct_import_names_for_overloads()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "Records/Overload.hpp" });
        var freeOverloadA = new FunctionBinding("function:overload:int", "overload_value", "OverloadValue", BindingType.Int32, "entry_overload_int");
        freeOverloadA.Parameters.Add(new ParameterBinding("parameter:overload:value", "value", "value", BindingType.Int32, cppTypeName: "int"));
        var freeOverloadB = new FunctionBinding("function:overload:int,int", "overload_value", "OverloadValue", BindingType.Int32, "entry_overload_int_int");
        freeOverloadB.Parameters.Add(new ParameterBinding("parameter:overload:left", "left", "left", BindingType.Int32, cppTypeName: "int"));
        freeOverloadB.Parameters.Add(new ParameterBinding("parameter:overload:right", "right", "right", BindingType.Int32, cppTypeName: "int"));

        var record = new RecordBinding("record:overload_box", "overload_box", "OverloadBox");
        var ctorA = new FunctionBinding("ctor:overload_box:int", "overload_box", "OverloadBox", record.HandleType, "entry_ctor_int")
        {
            IsConstructor = true,
            OwningRecordCppTypeName = "overload_box"
        };
        ctorA.Parameters.Add(new ParameterBinding("parameter:ctor:value", "value", "value", BindingType.Int32, cppTypeName: "int"));
        var ctorB = new FunctionBinding("ctor:overload_box:int,int", "overload_box", "OverloadBox", record.HandleType, "entry_ctor_int_int")
        {
            IsConstructor = true,
            OwningRecordCppTypeName = "overload_box"
        };
        ctorB.Parameters.Add(new ParameterBinding("parameter:ctor:left", "left", "left", BindingType.Int32, cppTypeName: "int"));
        ctorB.Parameters.Add(new ParameterBinding("parameter:ctor:right", "right", "right", BindingType.Int32, cppTypeName: "int"));
        var methodA = new FunctionBinding("method:mix:int", "mix", "Mix", BindingType.Int32, "entry_mix_int") { OwningRecordCppTypeName = "overload_box" };
        methodA.Parameters.Add(new ParameterBinding("parameter:mix:value", "value", "value", BindingType.Int32, cppTypeName: "int"));
        var methodB = new FunctionBinding("method:mix:int,int", "mix", "Mix", BindingType.Int32, "entry_mix_int_int") { OwningRecordCppTypeName = "overload_box" };
        methodB.Parameters.Add(new ParameterBinding("parameter:mix:left", "left", "left", BindingType.Int32, cppTypeName: "int"));
        methodB.Parameters.Add(new ParameterBinding("parameter:mix:right", "right", "right", BindingType.Int32, cppTypeName: "int"));
        var staticA = new FunctionBinding("static:combine:int", "combine", "Combine", BindingType.Int32, "entry_combine_int") { IsStatic = true, OwningRecordCppTypeName = "overload_box" };
        staticA.Parameters.Add(new ParameterBinding("parameter:combine:value", "value", "value", BindingType.Int32, cppTypeName: "int"));
        var staticB = new FunctionBinding("static:combine:int,int", "combine", "Combine", BindingType.Int32, "entry_combine_int_int") { IsStatic = true, OwningRecordCppTypeName = "overload_box" };
        staticB.Parameters.Add(new ParameterBinding("parameter:combine:left", "left", "left", BindingType.Int32, cppTypeName: "int"));
        staticB.Parameters.Add(new ParameterBinding("parameter:combine:right", "right", "right", BindingType.Int32, cppTypeName: "int"));

        record.Members.Add(ctorA);
        record.Members.Add(ctorB);
        record.Members.Add(methodA);
        record.Members.Add(methodB);
        record.Members.Add(staticA);
        record.Members.Add(staticB);
        compilation.Members.Add(record);
        compilation.Members.Add(freeOverloadA);
        compilation.Members.Add(freeOverloadB);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;

        Assert.Contains("__PInvoke_OverloadValue_int", managedOutput, StringComparison.Ordinal);
        Assert.Contains("__PInvoke_OverloadValue_int_int", managedOutput, StringComparison.Ordinal);
        Assert.Contains("__PInvoke_OverloadBox_int", managedOutput, StringComparison.Ordinal);
        Assert.Contains("__PInvoke_OverloadBox_int_int", managedOutput, StringComparison.Ordinal);
        Assert.Contains("__PInvoke_Mix_int", managedOutput, StringComparison.Ordinal);
        Assert.Contains("__PInvoke_Mix_int_int", managedOutput, StringComparison.Ordinal);
        Assert.Contains("__PInvoke_Combine_int", managedOutput, StringComparison.Ordinal);
        Assert.Contains("__PInvoke_Combine_int_int", managedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Managed_output_renders_owned_wrapper_surface_for_root_records()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "Records/Constructor.hpp" });
        var record = new RecordBinding("record:widget", "widget", "Widget");
        compilation.Members.Add(record);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;

        Assert.Contains("public partial class Widget : global::System.IDisposable", managedOutput, StringComparison.Ordinal);
        Assert.Contains("public bool OwnsHandle { get; }", managedOutput, StringComparison.Ordinal);
        Assert.Contains("public void Dispose()", managedOutput, StringComparison.Ordinal);
        Assert.Contains("protected virtual void DisposeCore(nint handle)", managedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Managed_output_renders_dispose_override_for_derived_records()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "Records/Inheritance.hpp" });
        var baseRecord = new RecordBinding("record:base_widget", "base_widget", "BaseWidget");
        var derivedRecord = new RecordBinding("record:derived_widget", "derived_widget", "DerivedWidget")
        {
            ManagedBaseTypeName = "BaseWidget"
        };
        compilation.Members.Add(baseRecord);
        compilation.Members.Add(derivedRecord);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;

        Assert.Contains("public partial class DerivedWidget : BaseWidget", managedOutput, StringComparison.Ordinal);
        Assert.Contains("protected override void DisposeCore(nint handle)", managedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("public bool OwnsHandle { get; }", managedOutput[(managedOutput.IndexOf("public partial class DerivedWidget", StringComparison.Ordinal))..], StringComparison.Ordinal);
    }

    [Fact]
    public void Managed_output_can_mark_handle_returns_as_owned_from_ast()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "Records/Factory.hpp" });
        var record = new RecordBinding("record:widget", "widget", "Widget");
        var factory = new FunctionBinding("function:create_widget", "create_widget", "CreateWidget", record.HandleType, "entry_create_widget")
        {
            ReturnsOwnedHandle = true
        };
        compilation.Members.Add(record);
        compilation.Members.Add(factory);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;

        Assert.Contains("return new Widget(__result, true);", managedOutput, StringComparison.Ordinal);
    }
}
