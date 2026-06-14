using System.Collections.Generic;
using System.IO;

using SharpCpp.Model.Ast;
using SharpCpp.Pipeline;
using SharpCpp.Rendering;

using Xunit;

namespace SharpCpp.Tests.Rendering;

public sealed class BindingOutputRendererTests
{
    [Fact]
    public void Ignored_stubs_prefer_native_declaration_metadata()
    {
        var compilation = new BindingCompilation("SharpCpp.Sample", "SharpCpp.Sample", "SampleBindings", "SharpCpp.Sample.Native", new[] { "Sample.hpp" });
        var function = new FunctionBinding("function:test", "test", "Test", BindingType.Void, "sharpcpp_sample_test", "void")
        {
            IsStatic = true
        };
        function.SetIgnoreReason("manual stub");
        function.SetMetadata(MetadataKeys.DeclarationDisplayText, "static int native_test(const Foo& value);");
        compilation.Members.Add(function);

        var managedOutput = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files
            .Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;

        Assert.Contains("// static int native_test(const Foo& value); // excluded: manual stub", managedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Ignored_stubs_fall_back_to_native_shapes_without_metadata()
    {
        var compilation = new BindingCompilation("SharpCpp.Sample", "SharpCpp.Sample", "SampleBindings", "SharpCpp.Sample.Native", new[] { "Sample.hpp" });
        var record = new RecordBinding("record:test", "test", "Test");
        var field = new FieldBinding("field:test:value", "value", "Value", BindingType.Int32, "int", "test", isReadOnly: false);
        field.SetIgnoreReason("manual stub");
        record.Members.Add(field);
        compilation.Members.Add(record);

        var managedOutput = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files
            .Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;

        Assert.Contains("// int value; // excluded: manual stub", managedOutput, StringComparison.Ordinal);
    }

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
        Assert.Contains("#include \"SharpCpp/Export.hpp\"", nativeOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("extern \"C\"\n{", nativeOutput, StringComparison.Ordinal);
        Assert.Contains("#include \"SharpCpp/Marshaller.hpp\"", nativeOutput, StringComparison.Ordinal);
        Assert.Contains("SHARPCPP_EXPORT ret_t<int> sharpcpp_sample_add_numbers(arg_t<int> lhs, arg_t<int> rhs)", nativeOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Managed_output_uses_ReadOnlySpan_for_const_char_pointer_parameter()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "String/String.hpp" });
        var constCharType = BindingType.CreateConfigured("const char*", "global::System.ReadOnlySpan<byte>");
        var function = new FunctionBinding(
            "function:set_name",
            "set_name",
            "SetName",
            BindingType.Void,
            "sharpcpp_sample_set_name");
        function.Parameters.Add(new ParameterBinding(
            "parameter:set_name:name",
            "name",
            "name",
            constCharType,
            BindingParameterDirection.In,
            BindingParameterAbiKind.Value,
            "const char*"));
        compilation.Members.Add(function);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;

        Assert.Contains("ReadOnlySpan<byte> name", managedOutput, StringComparison.Ordinal);
        Assert.Contains("public static void SetName(global::System.ReadOnlySpan<byte> name)", managedOutput, StringComparison.Ordinal);
        Assert.Contains("internal static partial void SetName(global::System.ReadOnlySpan<byte> name);", managedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Managed_output_preserves_alias_shaped_public_signatures_and_emits_alias_file()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "Urho3D",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "Math/Vector3.hpp" });
        var vector3TargetType = BindingType.CreateConfigured(
            "Urho3D::Vector3",
            "global::Urho3D.Math.Vector3",
            "global::Urho3D.Math.Vector3");
        var vector3AliasType = vector3TargetType.WithManagedTypeFormatOverride("Vector3");
        compilation.TypeAliases.Add(new BindingTypeAlias("Urho3D::Vector3", "Vector3", "Vector3", vector3TargetType));

        var function = new FunctionBinding(
            "function:jump_by",
            "jump_by",
            "JumpBy",
            BindingType.Void,
            "sharpcpp_sample_jump_by");
        function.Parameters.Add(new ParameterBinding(
            "parameter:jump_by:offset",
            "offset",
            "offset",
            vector3AliasType,
            BindingParameterDirection.In,
            BindingParameterAbiKind.Reference,
            "const Urho3D::Vector3&"));
        compilation.Members.Add(function);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath == "SharpCppSample.Bindings.g.cs").Content;
        var aliasOutput = files.Single(static file => file.RelativePath == "SharpCppSample.Bindings.Aliases.g.cs").Content;

        Assert.Contains("public static void JumpBy(in Vector3 offset)", managedOutput, StringComparison.Ordinal);
        Assert.Contains("internal static partial void JumpBy(in global::Urho3D.Math.Vector3 offset);", managedOutput, StringComparison.Ordinal);
        Assert.Contains("global using Vector3 = global::Urho3D.Math.Vector3;", aliasOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Native_output_uses_arg_t_for_const_char_pointer_parameter()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "String/String.hpp" });
        var constCharType = BindingType.CreateConfigured("const char*", "global::System.ReadOnlySpan<byte>");
        var function = new FunctionBinding(
            "function:set_name",
            "set_name",
            "SetName",
            BindingType.Void,
            "sharpcpp_sample_set_name");
        function.Parameters.Add(new ParameterBinding(
            "parameter:set_name:name",
            "name",
            "name",
            constCharType,
            BindingParameterDirection.In,
            BindingParameterAbiKind.Value,
            "const char*"));
        compilation.Members.Add(function);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var nativeOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cpp", StringComparison.Ordinal)).Content;

        Assert.Contains("SHARPCPP_EXPORT ret_t<void> sharpcpp_sample_set_name(arg_t<const char*> name)", nativeOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Managed_output_uses_string_for_const_char_return()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "String/String.hpp" });
        var function = new FunctionBinding(
            "function:get_name",
            "get_name",
            "GetName",
            BindingType.CreateConfigured(
                "const char*",
                "global::System.ReadOnlySpan<byte>",
                null,
                "global::SharpCpp.Support.Utf8SpanReturnMarshaller"),
            "sharpcpp_sample_get_name",
            "const char*");
        compilation.Members.Add(function);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;

        Assert.Contains("public static global::System.ReadOnlySpan<byte> GetName()", managedOutput, StringComparison.Ordinal);
        Assert.Contains("internal static partial global::System.ReadOnlySpan<byte> GetName();", managedOutput, StringComparison.Ordinal);
        Assert.Contains("MarshalUsing", managedOutput, StringComparison.Ordinal);
        Assert.Contains("Utf8SpanReturnMarshaller", managedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Native_output_uses_ret_t_of_const_char_pointer_for_const_char_return()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "String/String.hpp" });
        var function = new FunctionBinding(
            "function:get_name",
            "get_name",
            "GetName",
            BindingType.CreateConfigured("const char*", "string"),
            "sharpcpp_sample_get_name",
            "const char*");
        compilation.Members.Add(function);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var nativeOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cpp", StringComparison.Ordinal)).Content;

        Assert.Contains("ret_t<const char*> sharpcpp_sample_get_name(", nativeOutput, StringComparison.Ordinal);
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
        Assert.Contains("internal static partial void WritePayload(out nint payload);", managedOutput, StringComparison.Ordinal);
        Assert.Contains("nint __abi_payload;", managedOutput, StringComparison.Ordinal);
        Assert.Contains("payload = PayloadRecord.CreateManagedWrapper(__abi_payload, ownsHandle: false);", managedOutput, StringComparison.Ordinal);
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
        Assert.DoesNotContain("Set_Count", managedOutput, StringComparison.Ordinal);
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

        Assert.Contains("OverloadValue", managedOutput, StringComparison.Ordinal);
        Assert.Contains("OverloadValue2", managedOutput, StringComparison.Ordinal);
        Assert.Contains("OverloadBox", managedOutput, StringComparison.Ordinal);
        Assert.Contains("OverloadBox2", managedOutput, StringComparison.Ordinal);
        Assert.Contains("Mix", managedOutput, StringComparison.Ordinal);
        Assert.Contains("Mix2", managedOutput, StringComparison.Ordinal);
        Assert.Contains("Combine", managedOutput, StringComparison.Ordinal);
        Assert.Contains("Combine2", managedOutput, StringComparison.Ordinal);
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

        Assert.Contains("public partial class Widget : global::SharpCpp.Support.NativeObject", managedOutput, StringComparison.Ordinal);
        Assert.Contains("public bool OwnsHandle => OwnsNativeHandle;", managedOutput, StringComparison.Ordinal);
        Assert.Contains("protected override bool ReleaseNativeHandle(nint handle)", managedOutput, StringComparison.Ordinal);
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
        Assert.Contains("protected override bool ReleaseNativeHandle(nint handle)", managedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("public bool OwnsHandle => OwnsNativeHandle;", managedOutput[(managedOutput.IndexOf("public partial class DerivedWidget", StringComparison.Ordinal))..], StringComparison.Ordinal);
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

        Assert.Contains("return Widget.CreateManagedWrapper(__result, ownsHandle: true);", managedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Managed_output_honors_custom_cache_policy_and_base_release_strategy()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "Records/RefCounted.hpp" });
        var record = new RecordBinding("record:payload", "payload", "Payload")
        {
            ManagedBaseTypeName = "global::Urho3D.Interop.RefCountedNativeObject",
            ManagedInstanceCacheFactoryExpression = "global::Urho3D.Interop.RefCountedInstanceCache.Shared",
            ManagedInstanceCacheTypeName = "global::SharpCpp.Support.INativeInstanceCache",
            EmitDefaultNativeReleaseOverride = false
        };
        var field = new FieldBinding("field:payload:child", "child", "Child", record.HandleType, "payload*", "payload")
        {
            ReturnsOwnedHandle = true
        };
        record.Members.Add(field);
        compilation.Members.Add(record);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;

        Assert.Contains("internal static readonly global::SharpCpp.Support.INativeInstanceCache s_instanceCache = global::Urho3D.Interop.RefCountedInstanceCache.Shared;", managedOutput, StringComparison.Ordinal);
        Assert.Contains("PrepareForOwnedRegistration();", managedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("protected override bool ReleaseNativeHandle(nint handle)", managedOutput, StringComparison.Ordinal);
        Assert.Contains("return Payload.CreateManagedWrapper(__PInvoke.Get_Child(this), ownsHandle: true);", managedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Managed_output_emits_clone_surface_for_records_with_copy_constructors()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "Records/Clone.hpp" });
        var record = new RecordBinding("record:clone_counter", "clone_counter", "CloneCounter")
        {
            HasCopyConstructor = true
        };
        compilation.Members.Add(record);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;
        var nativeOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cpp", StringComparison.Ordinal)).Content;

        Assert.Contains("global::System.ICloneable", managedOutput, StringComparison.Ordinal);
        Assert.Contains("public CloneCounter Clone()", managedOutput, StringComparison.Ordinal);
        Assert.Contains("object global::System.ICloneable.Clone()", managedOutput, StringComparison.Ordinal);
        Assert.Contains("internal static partial nint CloneCopyCtor(CloneCounter self);", managedOutput, StringComparison.Ordinal);
        Assert.Contains("sharpcpp_e2e_clone_counter_clone", nativeOutput, StringComparison.Ordinal);
        Assert.Contains("return detail::TryClone<clone_counter>(instance, 0);", nativeOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Managed_output_emits_polymorphic_wrapper_factory_and_registry_hooks()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "Records/Inheritance.hpp" });
        var baseRecord = new RecordBinding("record:base_counter", "base_counter", "BaseCounter")
        {
            IsPolymorphic = true
        };
        var derivedRecord = new RecordBinding("record:derived_counter", "derived_counter", "DerivedCounter")
        {
            ManagedBaseTypeName = "BaseCounter",
            IsPolymorphic = true
        };
        compilation.Members.Add(baseRecord);
        compilation.Members.Add(derivedRecord);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;
        var nativeOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cpp", StringComparison.Ordinal)).Content;

        Assert.Contains("internal static readonly global::SharpCpp.Support.DefaultNativeInstanceCache s_instanceCache = new global::SharpCpp.Support.DefaultNativeInstanceCache();", managedOutput, StringComparison.Ordinal);
        Assert.Contains("global::SharpCpp.Support.NativeObjectRegistry.Create<BaseCounter>(handle, ownsHandle)", managedOutput, StringComparison.Ordinal);
        Assert.Contains("global::SharpCpp.Support.NativeObjectRegistry.RegisterType<BaseCounter>();", managedOutput, StringComparison.Ordinal);
        Assert.Contains("global::SharpCpp.Support.NativeObjectRegistry.RegisterType<DerivedCounter>();", managedOutput, StringComparison.Ordinal);
        Assert.Contains("INativeObjectRegistration<BaseCounter>", managedOutput, StringComparison.Ordinal);
        Assert.Contains("sharpcpp_e2e_base_counter_type_id", nativeOutput, StringComparison.Ordinal);
        Assert.Contains("sharpcpp_e2e_base_counter_metadata", nativeOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Director_records_emit_virtual_managed_methods_and_native_director_hooks()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "Records/Director.hpp" });
        var record = new RecordBinding("record:callback_counter", "callback_counter", "CallbackCounter")
        {
            IsDirector = true
        };
        var ctor = new FunctionBinding("ctor:callback_counter:int", "callback_counter", "CallbackCounter", record.HandleType, "entry_ctor")
        {
            IsConstructor = true,
            OwningRecordCppTypeName = "callback_counter"
        };
        ctor.Parameters.Add(new ParameterBinding("parameter:seed", "seed", "seed", BindingType.Int32, cppTypeName: "int"));
        var getValue = new FunctionBinding("method:callback_counter:get_value", "get_value", "GetValue", BindingType.Int32, "entry_get")
        {
            IsStatic = false,
            IsVirtual = true,
            IsConstMethod = true,
            OwningRecordCppTypeName = "callback_counter"
        };
        var callTwice = new FunctionBinding("method:callback_counter:call_virtual_twice", "call_virtual_twice", "CallVirtualTwice", BindingType.Int32, "entry_call")
        {
            IsStatic = false,
            IsConstMethod = true,
            OwningRecordCppTypeName = "callback_counter"
        };
        record.Members.Add(ctor);
        record.Members.Add(getValue);
        record.Members.Add(callTwice);
        compilation.Members.Add(record);

        BindingCompilationPolymorphismPass.Apply(compilation);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;
        var nativeOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cpp", StringComparison.Ordinal)).Content;

        Assert.Contains("public virtual int GetValue()", managedOutput, StringComparison.Ordinal);
        Assert.Contains("private void ConnectDirector()", managedOutput, StringComparison.Ordinal);
        Assert.Contains("Marshal.GetFunctionPointerForDelegate", managedOutput, StringComparison.Ordinal);
        Assert.Contains("class __callback_counter_director : public callback_counter", nativeOutput, StringComparison.Ordinal);
        Assert.Contains("sharpcpp_e2e_callback_counter_connect_director", nativeOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Director_records_render_protected_members_with_director_helpers()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "Records/Protected.hpp" });
        var record = new RecordBinding("record:protected_counter", "protected_counter", "ProtectedCounter")
        {
            IsDirector = true
        };
        record.Members.Add(new FunctionBinding("ctor:protected_counter:int", "protected_counter", "ProtectedCounter", record.HandleType, "entry_ctor")
        {
            IsConstructor = true,
            AccessModifier = BindingAccessModifier.Protected,
            OwningRecordCppTypeName = "protected_counter"
        });
        ((FunctionBinding)record.Members[^1]).Parameters.Add(new ParameterBinding("parameter:seed", "seed", "seed", BindingType.Int32, cppTypeName: "int"));
        record.Members.Add(new FunctionBinding("method:protected_counter:increment", "increment", "Increment", BindingType.Int32, "entry_increment")
        {
            IsStatic = false,
            AccessModifier = BindingAccessModifier.Protected,
            OwningRecordCppTypeName = "protected_counter"
        });
        ((FunctionBinding)record.Members[^1]).Parameters.Add(new ParameterBinding("parameter:amount", "amount", "amount", BindingType.Int32, cppTypeName: "int"));
        record.Members.Add(new FunctionBinding("method:protected_counter:scale", "scale", "Scale", BindingType.Int32, "entry_scale")
        {
            IsStatic = false,
            IsVirtual = true,
            AccessModifier = BindingAccessModifier.Protected,
            OwningRecordCppTypeName = "protected_counter"
        });
        ((FunctionBinding)record.Members[^1]).Parameters.Add(new ParameterBinding("parameter:value", "value", "value", BindingType.Int32, cppTypeName: "int"));
        record.Members.Add(new FunctionBinding("method:protected_counter:call_scale", "call_scale", "CallScale", BindingType.Int32, "entry_call_scale")
        {
            IsStatic = false,
            OwningRecordCppTypeName = "protected_counter"
        });
        ((FunctionBinding)record.Members[^1]).Parameters.Add(new ParameterBinding("parameter:value", "value", "value", BindingType.Int32, cppTypeName: "int"));
        record.Members.Add(new FieldBinding("field:protected_counter:factor_", "factor_", "Factor", BindingType.Int32, "int", "protected_counter")
        {
            AccessModifier = BindingAccessModifier.Protected
        });
        compilation.Members.Add(record);

        BindingCompilationPolymorphismPass.Apply(compilation);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;
        var nativeOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cpp", StringComparison.Ordinal)).Content;

        Assert.Contains("protected ProtectedCounter(int", managedOutput, StringComparison.Ordinal);
        Assert.Contains("protected int Increment(int amount)", managedOutput, StringComparison.Ordinal);
        Assert.Contains("protected virtual int Scale(int value)", managedOutput, StringComparison.Ordinal);
        Assert.Contains("protected int Factor", managedOutput, StringComparison.Ordinal);
        Assert.Contains("__call_Increment", nativeOutput, StringComparison.Ordinal);
        Assert.Contains("__call_Scale", nativeOutput, StringComparison.Ordinal);
        Assert.Contains("__get_Factor", nativeOutput, StringComparison.Ordinal);
        Assert.Contains("protected member access requires a director-backed instance", nativeOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Secondary_base_methods_are_projected_with_adjusted_native_dispatch()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "Records/MultipleInheritance.hpp" });
        var left = new RecordBinding("record:mi_left", "mi_left", "MiLeft");
        var leftValue = new FunctionBinding("method:mi_left:left_value", "left_value", "LeftValue", BindingType.Int32, "entry_left")
        {
            IsStatic = false,
            IsVirtual = true,
            IsConstMethod = true,
            OwningRecordCppTypeName = "mi_left"
        };
        left.Members.Add(leftValue);

        var right = new RecordBinding("record:mi_right", "mi_right", "MiRight");
        var rightValue = new FunctionBinding("method:mi_right:right_value", "right_value", "RightValue", BindingType.Int32, "entry_right")
        {
            IsStatic = false,
            IsVirtual = true,
            IsConstMethod = true,
            OwningRecordCppTypeName = "mi_right"
        };
        var rightOnly = new FunctionBinding("method:mi_right:right_only", "right_only", "RightOnly", BindingType.Int32, "entry_right_only")
        {
            IsStatic = false,
            IsConstMethod = true,
            OwningRecordCppTypeName = "mi_right"
        };
        right.Members.Add(rightValue);
        right.Members.Add(rightOnly);

        var counter = new RecordBinding("record:mi_counter", "mi_counter", "MiCounter")
        {
            ManagedBaseTypeName = "MiLeft",
            IsDirector = true
        };
        counter.Bases.Add(new RecordBaseBinding(left.SymbolId, left.NativeName, left.ManagedName, left.CppQualifiedName, isVirtual: false));
        counter.Bases.Add(new RecordBaseBinding(right.SymbolId, right.NativeName, right.ManagedName, right.CppQualifiedName, isVirtual: false));
        var ctor = new FunctionBinding("ctor:mi_counter", "mi_counter", "MiCounter", counter.HandleType, "entry_ctor")
        {
            IsConstructor = true,
            OwningRecordCppTypeName = "mi_counter"
        };
        counter.Members.Add(ctor);

        compilation.Members.Add(left);
        compilation.Members.Add(right);
        compilation.Members.Add(counter);

        BindingCompilationInheritancePass.Apply(compilation);
        BindingCompilationPolymorphismPass.Apply(compilation);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;
        var nativeOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cpp", StringComparison.Ordinal)).Content;

        Assert.Contains("public virtual int RightValue()", managedOutput, StringComparison.Ordinal);
        Assert.Contains("public int RightOnly()", managedOutput, StringComparison.Ordinal);
        Assert.Contains("dynamic_cast<mi_right*>(instance)", nativeOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Generic_records_render_as_managed_generic_classes_with_concrete_instantiation_dispatch()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "Records/Generic.hpp" });
        var record = new RecordBinding("record:value_box", "value_box", "BoxOf", "value_box")
        {
            ManagedTypeFormat = "BoxOf<{0}>"
        };
        record.GenericParameters.Add(new BindingGenericParameter("T", 0));
        record.SupportedGenericInstantiations.Add(new RecordGenericInstantiationBinding(new[] { BindingType.Int32 }));
        record.SupportedGenericInstantiations.Add(new RecordGenericInstantiationBinding(new[] { BindingType.Single }));

        var ctor = new FunctionBinding("ctor:value_box:T", "value_box", "BoxOf", record.HandleType, "entry_ctor")
        {
            IsConstructor = true,
            OwningRecordCppTypeName = "value_box"
        };
        ctor.Parameters.Add(new ParameterBinding("parameter:value", "value", "value", BindingType.CreateGenericParameter("T"), cppTypeName: "T"));

        var getValue = new FunctionBinding("method:value_box:get_value", "get_value", "GetValue", BindingType.CreateGenericParameter("T"), "entry_get")
        {
            IsStatic = false,
            OwningRecordCppTypeName = "value_box",
            IsConstMethod = true
        };

        var setValue = new FunctionBinding("method:value_box:set_value", "set_value", "SetValue", BindingType.Void, "entry_set")
        {
            IsStatic = false,
            OwningRecordCppTypeName = "value_box"
        };
        setValue.Parameters.Add(new ParameterBinding("parameter:value", "value", "value", BindingType.CreateGenericParameter("T"), cppTypeName: "T"));

        var create = new FunctionBinding("static:value_box:create", "create", "Create", BindingType.CreateHandle("value_box<T>*", "BoxOf", record.SymbolId, genericArguments: new[] { BindingType.CreateGenericParameter("T") }), "entry_create")
        {
            IsStatic = true,
            OwningRecordCppTypeName = "value_box"
        };
        create.Parameters.Add(new ParameterBinding("parameter:value", "value", "value", BindingType.CreateGenericParameter("T"), cppTypeName: "T"));

        record.Members.Add(ctor);
        record.Members.Add(getValue);
        record.Members.Add(setValue);
        record.Members.Add(create);
        compilation.Members.Add(record);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;
        var nativeOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cpp", StringComparison.Ordinal)).Content;

        Assert.Contains("public partial class BoxOf<T>", managedOutput, StringComparison.Ordinal);
        Assert.Contains("typeof(T) == typeof(int)", managedOutput, StringComparison.Ordinal);
        Assert.Contains("typeof(T) == typeof(float)", managedOutput, StringComparison.Ordinal);
        Assert.Contains("public T GetValue()", managedOutput, StringComparison.Ordinal);
        Assert.Contains("(BoxOf<T>)(object)BoxOf<int>.CreateManagedWrapper", managedOutput, StringComparison.Ordinal);
        Assert.Contains("entry_get__1", nativeOutput, StringComparison.Ordinal);
        Assert.Contains("entry_get__2", nativeOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Managed_output_renders_comment_stub_for_ignored_record()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "Ignore/IgnoreTests.hpp" });
        var record = new RecordBinding("record:skipped", "skipped", "Skipped");
        record.SetMetadata(MetadataKeys.IgnoreReason, "not yet supported");
        compilation.Members.Add(record);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;

        var lines = managedOutput.Replace("\r\n", "\n").Split('\n');

        Assert.Contains("    // skipped; // excluded: not yet supported", lines);
        Assert.DoesNotContain("public partial class Skipped", managedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Managed_output_renders_comment_stub_for_ignored_free_function()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "Ignore/IgnoreTests.hpp" });
        var function = new FunctionBinding("function:skipped_fn", "skipped_fn", "SkippedFn", BindingType.Int32, "entry_skipped");
        function.SetMetadata(MetadataKeys.IgnoreReason, "manual stub");
        compilation.Members.Add(function);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;

        var lines = managedOutput.Replace("\r\n", "\n").Split('\n');

        Assert.Contains("        // static int skipped_fn(); // excluded: manual stub", lines);
        Assert.DoesNotContain("\n    public static int SkippedFn(", managedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Managed_output_renders_comment_stub_for_ignored_enum()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "Ignore/IgnoreTests.hpp" });
        var @enum = new EnumBinding("enum:skipped", "skipped", "Skipped", "unsigned int");
        @enum.SetMetadata(MetadataKeys.IgnoreReason, "marked [[sharpcpp::ignore]]");
        compilation.Members.Add(@enum);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;

        var lines = managedOutput.Replace("\r\n", "\n").Split('\n');

        Assert.Contains("    // enum skipped; // excluded: marked [[sharpcpp::ignore]]", lines);
    }

    [Fact]
    public void Managed_output_renders_comment_stub_for_ignored_members_within_record()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "Ignore/IgnoreTests.hpp" });
        var record = new RecordBinding("record:box", "box", "Box");
        var ignoredMethod = new FunctionBinding("method:box:skip", "skip", "Skip", BindingType.Int32, "entry_skip") { OwningRecordCppTypeName = "box", IsStatic = false };
        ignoredMethod.SetMetadata(MetadataKeys.IgnoreReason, "internal only");
        record.Members.Add(ignoredMethod);
        compilation.Members.Add(record);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;

        Assert.Contains("// excluded: internal only", managedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Managed_output_renders_comment_stub_for_ignored_field()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "Ignore/IgnoreTests.hpp" });
        var record = new RecordBinding("record:box", "box", "Box");
        var ignoredField = new FieldBinding("field:box:skip", "skip", "Skip", BindingType.Int32, "int", "box");
        ignoredField.SetMetadata(MetadataKeys.IgnoreReason, "marked [[sharpcpp::ignore]]");
        record.Members.Add(ignoredField);
        compilation.Members.Add(record);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;

        var lines = managedOutput.Replace("\r\n", "\n").Split('\n');

        Assert.Contains("        // int skip; // excluded: marked [[sharpcpp::ignore]]", lines);
    }

    [Fact]
    public void Managed_output_renders_documentation_comment_for_record()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample", "SharpCpp.Sample", "SampleBindings",
            "SharpCpp.Sample.Native", new[] { "Doc/Documentation.hpp" });
        var record = new RecordBinding("record:doc", "doc", "Doc");
        record.SetMetadata(MetadataKeys.DocumentationComment, "Represents a documented record.");
        compilation.Members.Add(record);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;

        Assert.Contains("/// <summary>Represents a documented record.</summary>", managedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Managed_output_escapes_xml_in_documentation_comments()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample", "SharpCpp.Sample", "SampleBindings",
            "SharpCpp.Sample.Native", new[] { "Doc/Documentation.hpp" });
        var function = new FunctionBinding("function:escape", "escape", "Escape", BindingType.Int32, "entry_escape");
        function.SetMetadata(MetadataKeys.DocumentationComment, "Handles <tag> escaping & special chars.");
        compilation.Members.Add(function);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;

        Assert.Contains("/// <summary>Handles &lt;tag&gt; escaping &amp; special chars.</summary>", managedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Managed_output_renders_record_in_assigned_namespace()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample", "SharpCpp.Sample", "SampleBindings",
            "SharpCpp.Sample.Native", new[] { "Namespace/Namespace.hpp" });
        var record = new RecordBinding("record:ns_type", "ns_type", "NsType", "outer::inner::ns_type");
        record.ManagedNamespace = "Outer.Inner";
        compilation.Members.Add(record);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;

        Assert.Contains("namespace Outer.Inner", managedOutput, StringComparison.Ordinal);
        Assert.Contains("public partial class NsType", managedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Managed_output_renders_global_scope_record_in_default_namespace()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample", "SharpCpp.Sample", "SampleBindings",
            "SharpCpp.Sample.Native", new[] { "Records/Field.hpp" });
        var record = new RecordBinding("record:global_type", "global_type", "GlobalType");
        record.ManagedNamespace = "SharpCpp.Sample";
        compilation.Members.Add(record);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;

        Assert.Contains("namespace SharpCpp.Sample", managedOutput, StringComparison.Ordinal);
        Assert.Contains("public partial class GlobalType", managedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Managed_output_groups_multiple_records_by_namespace()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample", "SharpCpp.Sample", "SampleBindings",
            "SharpCpp.Sample.Native", new[] { "Namespace/Namespace.hpp" });
        var recordA = new RecordBinding("record:a", "a", "A");
        var recordB = new RecordBinding("record:b", "b", "B", "ns1::b");
        var recordC = new RecordBinding("record:c", "c", "C", "ns1::c");
        compilation.Members.Add(recordA);
        compilation.Members.Add(recordB);
        compilation.Members.Add(recordC);

        NormalizeManagedNamespacesPass.Apply(compilation);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;

        Assert.Contains("namespace Ns1", managedOutput, StringComparison.Ordinal);
        Assert.Contains("public partial class B", managedOutput, StringComparison.Ordinal);
        Assert.Contains("public partial class C", managedOutput, StringComparison.Ordinal);

        var defaultNsIndex = managedOutput.IndexOf("namespace SharpCpp.Sample", StringComparison.Ordinal);
        var ns1Index = managedOutput.IndexOf("namespace Ns1", StringComparison.Ordinal);
        Assert.True(ns1Index < defaultNsIndex, "Non-default namespaces should appear before the default namespace.");
    }

    [Fact]
    public void Managed_output_renders_enum_in_assigned_namespace()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample", "SharpCpp.Sample", "SampleBindings",
            "SharpCpp.Sample.Native", new[] { "Enum/Enum.hpp" });
        var @enum = new EnumBinding("enum:color", "color", "Color", "int", "palette::color");
        @enum.ManagedNamespace = "Palette";
        @enum.Values.Add(new EnumValueBinding("red", "Red", "0"));
        @enum.Values.Add(new EnumValueBinding("green", "Green", "1"));
        compilation.Members.Add(@enum);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;

        Assert.Contains("namespace Palette", managedOutput, StringComparison.Ordinal);
        Assert.Contains("public enum Color", managedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Managed_output_uses_default_namespace_for_static_free_function_class()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample", "SharpCpp.Sample", "SampleBindings",
            "SharpCpp.Sample.Native", new[] { "Builtin/Builtin.hpp" });
        var function = new FunctionBinding("function:free_func", "free_func", "FreeFunc", BindingType.Void, "entry_free_func");
        compilation.Members.Add(function);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;

        Assert.Contains("namespace SharpCpp.Sample", managedOutput, StringComparison.Ordinal);
        Assert.Contains("public static partial class SampleBindings", managedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void NormalizeManagedNamespacesPass_assigns_identity_namespace()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample", "SharpCpp.Sample", "SampleBindings",
            "SharpCpp.Sample.Native", new[] { "Namespace/Namespace.hpp" });
        var record = new RecordBinding("record:ns_rec", "ns_rec", "NsRec", "myapp::utils::ns_rec");
        compilation.Members.Add(record);

        NormalizeManagedNamespacesPass.Apply(compilation);

        Assert.Equal("Myapp.Utils", record.ManagedNamespace);
    }

    [Fact]
    public void NormalizeManagedNamespacesPass_uses_default_namespace_for_global_scope()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample", "SharpCpp.Sample", "SampleBindings",
            "SharpCpp.Sample.Native", new[] { "Records/Field.hpp" });
        var record = new RecordBinding("record:global_rec", "global_rec", "GlobalRec", "global_rec");
        compilation.Members.Add(record);

        NormalizeManagedNamespacesPass.Apply(compilation);

        Assert.Equal("SharpCpp.Sample", record.ManagedNamespace);
    }

    [Fact]
    public void NormalizeManagedNamespacesPass_respects_namespace_mappings()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample", "SharpCpp.Sample", "SampleBindings",
            "SharpCpp.Sample.Native", new[] { "Namespace/Namespace.hpp" })
        {
            NamespaceMappings = new Dictionary<string, string>
            {
                { "myapp::utils", "SharpCpp.Generated.Utils" }
            }
        };
        var record = new RecordBinding("record:ns_rec", "ns_rec", "NsRec", "myapp::utils::ns_rec");
        compilation.Members.Add(record);

        NormalizeManagedNamespacesPass.Apply(compilation);

        Assert.Equal("SharpCpp.Generated.Utils", record.ManagedNamespace);
    }

    [Fact]
    public void Render_uses_first_rbfx_folder_as_output_subdirectory()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Rbfx.Bindings",
            "Urho3D",
            "Bindings",
            "SharpCpp.Rbfx.Native",
            new[] { "Urho3D/Urho3DAll.h" });

        var graphicsModel = new RecordBinding("record:graphics_model", "graphics_model", "GraphicsModel")
        {
            HeaderIncludePath = "Graphics/Model.h"
        };
        var graphicsMaterial = new RecordBinding("record:graphics_material", "graphics_material", "GraphicsMaterial")
        {
            HeaderIncludePath = "Graphics/Material.h"
        };
        var deepNode = new RecordBinding("record:deep_node", "deep_node", "DeepNode")
        {
            HeaderIncludePath = "Foo/Bar/Baz.h"
        };

        compilation.Members.Add(graphicsModel);
        compilation.Members.Add(graphicsMaterial);
        compilation.Members.Add(deepNode);

        var files = BindingOutputRenderer.Render(compilation, "SharpCpp.Rbfx", DeriveRbfxGroupStem).Files;

        Assert.Contains(files, static file => file.RelativePath == "Graphics/SharpCpp.Rbfx.Model.g.cs");
        Assert.Contains(files, static file => file.RelativePath == "Graphics/SharpCpp.Rbfx.Model.g.cpp");
        Assert.Contains(files, static file => file.RelativePath == "Graphics/SharpCpp.Rbfx.Material.g.cs");
        Assert.Contains(files, static file => file.RelativePath == "Graphics/SharpCpp.Rbfx.Material.g.cpp");
        Assert.Contains(files, static file => file.RelativePath == "Foo/SharpCpp.Rbfx.Bar.Baz.g.cs");
        Assert.Contains(files, static file => file.RelativePath == "Foo/SharpCpp.Rbfx.Bar.Baz.g.cpp");
        Assert.Contains(files, static file => file.RelativePath == "SharpCpp.Rbfx.Bindings.Aliases.g.cs");

        var modelManagedOutput = files.Single(static file => file.RelativePath == "Graphics/SharpCpp.Rbfx.Model.g.cs").Content;
        var materialManagedOutput = files.Single(static file => file.RelativePath == "Graphics/SharpCpp.Rbfx.Material.g.cs").Content;
        var deepManagedOutput = files.Single(static file => file.RelativePath == "Foo/SharpCpp.Rbfx.Bar.Baz.g.cs").Content;

        Assert.Contains("public partial class GraphicsModel", modelManagedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("public partial class GraphicsMaterial", modelManagedOutput, StringComparison.Ordinal);

        Assert.Contains("public partial class GraphicsMaterial", materialManagedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("public partial class GraphicsModel", materialManagedOutput, StringComparison.Ordinal);

        Assert.Contains("public partial class DeepNode", deepManagedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_routes_null_header_ignored_records_into_main_bindings_file()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Rbfx.Bindings",
            "Urho3D",
            "Bindings",
            "SharpCpp.Rbfx.Native",
            new[] { "Urho3D/Urho3DAll.h" });

        var foreignRecord = new RecordBinding(
            "record:std::basic_string<char>",
            "std::basic_string<char>",
            "BasicString",
            "std::basic_string<char>");
        foreignRecord.SetIgnoreReason("outside bindings api");
        compilation.Members.Add(foreignRecord);

        var files = BindingOutputRenderer.Render(compilation, "SharpCpp.Rbfx", DeriveRbfxGroupStem).Files;

        Assert.Contains(files, static file => file.RelativePath == "SharpCpp.Rbfx.Bindings.g.cs");
        Assert.DoesNotContain(files, static file => file.RelativePath.Contains("basic_string", StringComparison.Ordinal));

        var managedOutput = files.Single(static file => file.RelativePath == "SharpCpp.Rbfx.Bindings.g.cs").Content;
        Assert.Contains("// std::basic_string<char>; // excluded: outside bindings api", managedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_binding_set_removes_stale_generated_files_when_rewriting_directory()
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"SharpCpp.Tests.Rendering.{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDirectory);

        try
        {
            Directory.CreateDirectory(Path.Combine(outputDirectory, "Graphics"));
            File.WriteAllText(Path.Combine(outputDirectory, "SharpCpp.Rbfx.Bindings.g.cpp"), "stale native");
            File.WriteAllText(Path.Combine(outputDirectory, "Graphics", "SharpCpp.Rbfx.Model.g.cs"), "stale managed");
            File.WriteAllText(Path.Combine(outputDirectory, "keep.txt"), "keep");

            var generatedSet = new GeneratedBindingSet(
            [
                new GeneratedBindingFile("Graphics/SharpCpp.Rbfx.Material.g.cs", "material managed"),
                new GeneratedBindingFile("Graphics/SharpCpp.Rbfx.Material.g.cpp", "material native"),
                new GeneratedBindingFile("SharpCpp.Rbfx.Bindings.Aliases.g.cs", "aliases")
            ]);

            generatedSet.WriteToDirectory(outputDirectory);

            Assert.False(File.Exists(Path.Combine(outputDirectory, "SharpCpp.Rbfx.Bindings.g.cpp")));
            Assert.False(File.Exists(Path.Combine(outputDirectory, "Graphics", "SharpCpp.Rbfx.Model.g.cs")));
            Assert.True(File.Exists(Path.Combine(outputDirectory, "Graphics", "SharpCpp.Rbfx.Material.g.cs")));
            Assert.True(File.Exists(Path.Combine(outputDirectory, "Graphics", "SharpCpp.Rbfx.Material.g.cpp")));
            Assert.True(File.Exists(Path.Combine(outputDirectory, "SharpCpp.Rbfx.Bindings.Aliases.g.cs")));
            Assert.True(File.Exists(Path.Combine(outputDirectory, "keep.txt")));
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    private static string DeriveRbfxGroupStem(string? headerPath)
    {
        if (string.IsNullOrWhiteSpace(headerPath))
        {
            return "SharpCpp.Rbfx.Bindings";
        }

        var normalizedHeaderPath = headerPath.Replace('\\', '/');
        if (normalizedHeaderPath.StartsWith("Urho3D/", StringComparison.Ordinal))
        {
            normalizedHeaderPath = normalizedHeaderPath["Urho3D/".Length..];
        }

        var pathWithoutExtension = Path.ChangeExtension(normalizedHeaderPath, null)!.Replace('\\', '/');
        var pathParts = pathWithoutExtension.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (pathParts.Length == 0)
        {
            return "SharpCpp.Rbfx.Bindings";
        }

        if (pathParts.Length == 1)
        {
            return $"SharpCpp.Rbfx.{pathParts[0]}";
        }

        return $"{pathParts[0]}/SharpCpp.Rbfx.{string.Join('.', pathParts.Skip(1))}";
    }

    [Fact]
    public void Managed_output_uses_StringBase_for_string_record_wrapper()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "String/String.hpp" });
        var stringRecord = new RecordBinding("record:std::basic_string", "std::basic_string", "String")
        {
            ManagedBaseTypeName = "global::SharpCpp.Support.StringBase",
            ManagedTypeFormat = "String"
        };
        compilation.Members.Add(stringRecord);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;

        Assert.Contains("public partial class String : global::SharpCpp.Support.StringBase", managedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain(": global::SharpCpp.Support.NativeObject", managedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Managed_output_renders_protected_core_methods_for_string_record()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "String/String.hpp" });
        var stringRecord = new RecordBinding("record:std::basic_string", "std::basic_string", "String")
        {
            ManagedBaseTypeName = "global::SharpCpp.Support.StringBase",
            ManagedTypeFormat = "String",
            IsDirector = true
        };

        var getCountCore = new FunctionBinding("method:std::basic_string:size", "size", "GetCountCore",
            BindingType.UInt64, "sharpcpp_e2e_std_basic_string_size", "std::size_t", "std::basic_string::size")
        {
            IsStatic = false,
            AccessModifier = BindingAccessModifier.Protected,
            IsConstMethod = true,
            OwningRecordCppTypeName = "std::basic_string"
        };
        stringRecord.Members.Add(getCountCore);

        var addCore = new FunctionBinding("method:std::basic_string:push_back", "push_back", "AddCore",
            BindingType.Void, "sharpcpp_e2e_std_basic_string_push_back", "void", "std::basic_string::push_back")
        {
            IsStatic = false,
            AccessModifier = BindingAccessModifier.Protected,
            OwningRecordCppTypeName = "std::basic_string"
        };
        addCore.Parameters.Add(new ParameterBinding(
            "parameter:std::basic_string:push_back:value", "value", "value",
            BindingType.UInt8, BindingParameterDirection.In, BindingParameterAbiKind.Value, "unsigned char"));
        stringRecord.Members.Add(addCore);

        compilation.Members.Add(stringRecord);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;
        var nativeOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cpp", StringComparison.Ordinal)).Content;

        Assert.Contains("GetCountCore()", managedOutput, StringComparison.Ordinal);
        Assert.Contains("AddCore", managedOutput, StringComparison.Ordinal);
        Assert.Contains("SHARPCPP_EXPORT ret_t<std::size_t> sharpcpp_e2e_std_basic_string_size(arg_t<std::basic_string*> self)", nativeOutput, StringComparison.Ordinal);
        Assert.Contains("SHARPCPP_EXPORT ret_t<void> sharpcpp_e2e_std_basic_string_push_back(arg_t<std::basic_string*> self", nativeOutput, StringComparison.Ordinal);
    }
}
