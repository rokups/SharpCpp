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
    public void Managed_record_declarations_include_configured_static_interfaces()
    {
        var compilation = new BindingCompilation("SharpCpp.Sample", "SharpCpp.Sample", "SampleBindings", "SharpCpp.Sample.Native", new[] { "Sample.hpp" });
        var objectRecord = new RecordBinding("record:object", "Object", "Object")
        {
            IsAbstract = true
        };
        objectRecord.SetMetadata(MetadataKeys.ManagedStaticInterfaces, new[] { "global::Example.IStaticTypeInfo" });
        var nodeRecord = new RecordBinding("record:node", "Node", "Node")
        {
            ManagedBaseTypeName = "Object"
        };
        nodeRecord.Bases.Add(new RecordBaseBinding(
            objectRecord.SymbolId,
            objectRecord.NativeName,
            objectRecord.ManagedName,
            objectRecord.CppQualifiedName,
            isVirtual: false));
        nodeRecord.Members.Add(new FunctionBinding(
            "method:node:GetTypeStatic",
            "GetTypeStatic",
            "GetTypeStatic",
            BindingType.Int32,
            "node_get_type_static")
        {
            IsStatic = true
        });
        compilation.Members.Add(objectRecord);
        compilation.Members.Add(nodeRecord);

        var managedOutput = BindingOutputRenderer.Render(compilation, "SharpCppSample")
            .Files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;

        Assert.Contains(
            "public abstract partial class Object : global::SharpCpp.Support.NativeObject, global::Example.IStaticTypeInfo",
            managedOutput,
            StringComparison.Ordinal);
        Assert.Contains(
            "public partial class Node : global::SharpCpp.Sample.Object, global::Example.IStaticTypeInfo",
            managedOutput,
            StringComparison.Ordinal);
    }

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
    public void Free_functions_can_render_into_custom_managed_containers()
    {
        var compilation = new BindingCompilation("SharpCpp.Sample", "SharpCpp.Sample", "SampleBindings", "SharpCpp.Sample.Native", new[] { "Sample.hpp" });
        compilation.Members.Add(new FunctionBinding("function:default", "default_call", "Call", BindingType.Void, "entry_default"));
        compilation.Members.Add(new FunctionBinding("function:custom", "custom_call", "Call", BindingType.Void, "entry_custom")
        {
            ManagedNamespace = "Custom.Api",
            ManagedClassName = "CustomBindings"
        });

        var managedOutput = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files
            .Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;

        Assert.Contains("namespace SharpCpp.Sample", managedOutput, StringComparison.Ordinal);
        Assert.Contains("public static partial class SampleBindings", managedOutput, StringComparison.Ordinal);
        Assert.Contains("__PInvoke.Call();", managedOutput, StringComparison.Ordinal);
        Assert.Contains("EntryPoint = \"entry_default\"", managedOutput, StringComparison.Ordinal);
        Assert.Contains("namespace Custom.Api", managedOutput, StringComparison.Ordinal);
        Assert.Contains("public static partial class CustomBindings", managedOutput, StringComparison.Ordinal);
        Assert.Contains("EntryPoint = \"entry_custom\"", managedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Silent_exclusions_do_not_render_comment_stubs()
    {
        var compilation = new BindingCompilation("SharpCpp.Sample", "SharpCpp.Sample", "SampleBindings", "SharpCpp.Sample.Native", new[] { "Sample.hpp" });
        var record = new RecordBinding("record:silent", "silent", "Silent");
        record.SetSilentExclusionReason("internal detail");
        var ignoredMethodOnSilentRecord = new FunctionBinding("method:silent:ignored", "ignored", "Ignored", BindingType.Int32, "entry_ignored")
        {
            OwningRecordCppTypeName = "silent"
        };
        ignoredMethodOnSilentRecord.SetIgnoreReason("unsupported member");
        record.Members.Add(ignoredMethodOnSilentRecord);
        var function = new FunctionBinding("function:silent_fn", "silent_fn", "SilentFn", BindingType.Int32, "entry_silent");
        function.SetSilentExclusionReason("internal detail");
        var visibleRecord = new RecordBinding("record:visible", "visible", "Visible");
        var silentMethod = new FunctionBinding("method:visible:silent_method", "silent_method", "SilentMethod", BindingType.Int32, "entry_silent_method")
        {
            OwningRecordCppTypeName = "visible"
        };
        silentMethod.SetSilentExclusionReason("internal detail");
        visibleRecord.Members.Add(silentMethod);
        compilation.Members.Add(record);
        compilation.Members.Add(function);
        compilation.Members.Add(visibleRecord);

        var managedOutput = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files
            .Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;

        Assert.DoesNotContain("excluded: internal detail", managedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("public partial class Silent", managedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("SilentFn", managedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("SilentMethod", managedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("unsupported member", managedOutput, StringComparison.Ordinal);
        Assert.Contains("public partial class Visible", managedOutput, StringComparison.Ordinal);
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
    public void Managed_output_uses_configured_marshaller_for_string_view_parameter()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "String/String.hpp" });
        var stringViewType = BindingType.CreateConfigured(
            "std::string_view",
            "global::System.ReadOnlySpan<byte>",
            null,
            "global::SharpCpp.Support.Utf8SpanAbiMarshaller");
        var function = new FunctionBinding(
            "function:measure_view",
            "measure_view",
            "MeasureView",
            BindingType.Int32,
            "sharpcpp_sample_measure_view");
        function.Parameters.Add(new ParameterBinding(
            "parameter:measure_view:value",
            "value",
            "value",
            stringViewType,
            BindingParameterDirection.In,
            BindingParameterAbiKind.Value,
            "std::string_view"));
        compilation.Members.Add(function);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;

        Assert.Contains("public static int MeasureView(global::System.ReadOnlySpan<byte> value)", managedOutput, StringComparison.Ordinal);
        Assert.Contains("[MarshalUsing(typeof(global::SharpCpp.Support.Utf8SpanAbiMarshaller))] global::System.ReadOnlySpan<byte> value", managedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Managed_output_emits_callback_marshaller_helper_for_callback_parameters()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "Callbacks/Callbacks.hpp" });
        var callbackType = BindingType.CreateCallback(
            "std::function<void(int)>",
            "global::System.Action<{0}>",
            managedMarshaller: null,
            BindingType.Void,
            [BindingType.Int32]);
        var function = new FunctionBinding(
            "function:invoke_callback",
            "invoke_callback",
            "InvokeCallback",
            BindingType.Void,
            "sharpcpp_sample_invoke_callback");
        function.Parameters.Add(new ParameterBinding(
            "parameter:invoke_callback:callback",
            "callback",
            "callback",
            callbackType,
            BindingParameterDirection.In,
            BindingParameterAbiKind.Value,
            "std::function<void(int)>"));
        compilation.Members.Add(function);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;
        var aliasOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.Aliases.g.cs", StringComparison.Ordinal)).Content;
        var nativeOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cpp", StringComparison.Ordinal)).Content;

        Assert.Contains("public static void InvokeCallback(global::System.Action<int> callback)", managedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("internal readonly unsafe struct __SharpCppManagedCallbackHandleAbi", managedOutput, StringComparison.Ordinal);
        Assert.Contains("internal readonly unsafe struct __SharpCppManagedCallbackHandleAbi", aliasOutput, StringComparison.Ordinal);
        Assert.Contains("public __SharpCppManagedCallbackHandleAbi(nint state, nint callback)", aliasOutput, StringComparison.Ordinal);
        Assert.Equal(1, aliasOutput.Split("public nint State { get; }", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, aliasOutput.Split("public nint Callback { get; }", StringSplitOptions.None).Length - 1);
        Assert.Contains("__PInvoke.__SetManagedCallbackRelease(&__ReleaseManagedCallbackState);", aliasOutput, StringComparison.Ordinal);
        Assert.Contains("internal static unsafe partial void __SetManagedCallbackRelease(delegate* unmanaged[Cdecl]<nint, void> release);", aliasOutput, StringComparison.Ordinal);
        Assert.Contains("[CustomMarshaller(typeof(global::System.Action<int>), MarshalMode.ManagedToUnmanagedIn", aliasOutput, StringComparison.Ordinal);
        Assert.Contains("global::System.Runtime.InteropServices.GCHandle.Alloc(managed)", aliasOutput, StringComparison.Ordinal);
        Assert.Contains("private static void __ReleaseManagedCallbackState(nint state)", aliasOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("public delegate* unmanaged[Cdecl]<nint, void> Release", aliasOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("private static void Release(nint state)", aliasOutput, StringComparison.Ordinal);
        Assert.Contains("[MarshalUsing(typeof(global::SharpCpp.Sample.SampleBindings.__CallbackMarshaller_void_int))] global::System.Action<int>? callback", managedOutput, StringComparison.Ordinal);
        Assert.Contains("SHARPCPP_EXPORT void sharpcpp_set_managed_callback_release(void (*release)(void*))", nativeOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Optional_callback_defaults_use_null_dispatch_instead_of_nullable_value_dispatch()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "Callbacks/Callbacks.hpp" });
        var callbackType = BindingType.CreateCallback(
            "std::function<void(int)>",
            "global::System.Action<{0}>",
            managedMarshaller: null,
            BindingType.Void,
            [BindingType.Int32]);
        var function = new FunctionBinding(
            "function:invoke_optional_callback",
            "invoke_optional_callback",
            "InvokeOptionalCallback",
            BindingType.Void,
            "sharpcpp_sample_invoke_optional_callback");
        var callback = new ParameterBinding(
            "parameter:invoke_optional_callback:callback",
            "callback",
            "callback",
            callbackType,
            BindingParameterDirection.In,
            BindingParameterAbiKind.Value,
            "std::function<void(int)>");
        callback.SetMetadata(MetadataKeys.UnexpressibleDefault, true);
        function.Parameters.Add(callback);
        compilation.Members.Add(function);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;

        Assert.Contains("public static void InvokeOptionalCallback(global::System.Action<int>? callback)", managedOutput, StringComparison.Ordinal);
        Assert.Contains("if (callback is not null)", managedOutput, StringComparison.Ordinal);
        Assert.Contains("__PInvoke.InvokeOptionalCallback(callback);", managedOutput, StringComparison.Ordinal);
        Assert.Contains("if (callback is null)", managedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("callback.HasValue", managedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("callback.Value", managedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Callback_null_defaults_render_nullable_public_parameters()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "Callbacks/Callbacks.hpp" });
        var callbackType = BindingType.CreateCallback(
            "std::function<unsigned()>",
            "global::System.Func<{0}>",
            managedMarshaller: null,
            BindingType.UInt32,
            []);
        var function = new FunctionBinding(
            "function:register_timestamp",
            "register_timestamp",
            "RegisterTimestamp",
            BindingType.Void,
            "sharpcpp_sample_register_timestamp");
        function.Parameters.Add(new ParameterBinding(
            "parameter:register_timestamp:get_timestamp",
            "getTimestamp",
            "getTimestamp",
            callbackType,
            BindingParameterDirection.In,
            BindingParameterAbiKind.Value,
            "std::function<unsigned()>")
        {
            ManagedDefaultValueExpression = "null"
        });
        compilation.Members.Add(function);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;

        Assert.Contains("public static void RegisterTimestamp(global::System.Func<uint>? getTimestamp = default)", managedOutput, StringComparison.Ordinal);
        Assert.Contains("[MarshalUsing(typeof(global::SharpCpp.Sample.SampleBindings.__CallbackMarshaller_uint_none))] global::System.Func<uint>? getTimestamp", managedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("public static void RegisterTimestamp(global::System.Func<uint> getTimestamp = default)", managedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Grouped_managed_output_emits_callback_marshaller_helpers_once_in_alias_file()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "A/Callbacks.hpp", "B/Callbacks.hpp" });
        var callbackType = BindingType.CreateCallback(
            "std::function<void(int)>",
            "global::System.Action<{0}>",
            managedMarshaller: null,
            BindingType.Void,
            [BindingType.Int32]);
        var first = new FunctionBinding("function:a", "a_callback", "ACallback", BindingType.Void, "sharpcpp_sample_a_callback")
        {
            HeaderIncludePath = "A/Callbacks.hpp"
        };
        var second = new FunctionBinding("function:b", "b_callback", "BCallback", BindingType.Void, "sharpcpp_sample_b_callback")
        {
            HeaderIncludePath = "B/Callbacks.hpp"
        };
        first.Parameters.Add(new ParameterBinding("parameter:a:callback", "callback", "callback", callbackType, cppTypeName: "std::function<void(int)>"));
        second.Parameters.Add(new ParameterBinding("parameter:b:callback", "callback", "callback", callbackType, cppTypeName: "std::function<void(int)>"));
        compilation.Members.Add(first);
        compilation.Members.Add(second);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample", path => path?.Split('/')[0] ?? "Root").Files;
        var aliasOutput = files.Single(static file => file.RelativePath == "SharpCppSample.Bindings.Aliases.g.cs").Content;
        var groupOutputs = files.Where(static file => file.RelativePath.EndsWith(".g.cs", StringComparison.Ordinal) &&
            file.RelativePath != "SharpCppSample.Bindings.Aliases.g.cs");
        var nativeGroupOutputs = files.Where(static file => file.RelativePath.EndsWith(".g.cpp", StringComparison.Ordinal)).ToArray();

        Assert.Equal(1, aliasOutput.Split("internal readonly unsafe struct __SharpCppManagedCallbackHandleAbi", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, aliasOutput.Split("private static void __ReleaseManagedCallbackState(nint state)", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, aliasOutput.Split("internal static unsafe partial void __SetManagedCallbackRelease", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, aliasOutput.Split("internal static unsafe class __CallbackMarshaller_void_int", StringSplitOptions.None).Length - 1);
        Assert.All(groupOutputs, file => Assert.DoesNotContain("__SharpCppManagedCallbackHandleAbi", file.Content, StringComparison.Ordinal));
        Assert.Equal(1, nativeGroupOutputs.Count(static file => file.Content.Contains("sharpcpp_set_managed_callback_release", StringComparison.Ordinal)));
    }

    [Fact]
    public void Delegate_callback_parameter_emits_named_delegate_and_parameter_specific_marshaller()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "Callbacks/Callbacks.hpp" });
        var callbackType = BindingType.CreateCallback(
            "std::function<void(int)>",
            "global::System.Action<{0}>",
            managedMarshaller: null,
            BindingType.Void,
            [BindingType.Int32]);
        var function = new FunctionBinding(
            "function:invoke_callback",
            "invoke_callback",
            "InvokeCallback",
            BindingType.Void,
            "sharpcpp_sample_invoke_callback");
        function.Parameters.Add(new ParameterBinding(
            "parameter:invoke_callback:callback",
            "callback",
            "callback",
            callbackType,
            BindingParameterDirection.In,
            BindingParameterAbiKind.Value,
            "std::function<void(int)>")
        {
            UseCallbackDelegate = true,
            CallbackDelegateTypeName = "ProgressCallback"
        });
        compilation.Members.Add(function);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;
        var aliasOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.Aliases.g.cs", StringComparison.Ordinal)).Content;

        Assert.Contains("public static void InvokeCallback(global::SharpCpp.Sample.SampleBindings.ProgressCallback callback)", managedOutput, StringComparison.Ordinal);
        Assert.Contains("public delegate void ProgressCallback(int arg0);", aliasOutput, StringComparison.Ordinal);
        Assert.Contains("[CustomMarshaller(typeof(global::SharpCpp.Sample.SampleBindings.ProgressCallback), MarshalMode.ManagedToUnmanagedIn", aliasOutput, StringComparison.Ordinal);
        Assert.Contains("[MarshalUsing(typeof(global::SharpCpp.Sample.SampleBindings.__CallbackMarshaller_void_int_global__SharpCpp_Sample_SampleBindings_ProgressCallback))] global::SharpCpp.Sample.SampleBindings.ProgressCallback? callback", managedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Unnamed_delegate_callback_overloads_emit_distinct_generated_delegate_types()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "Callbacks/Callbacks.hpp" });
        var intCallbackType = BindingType.CreateCallback(
            "std::function<void(int)>",
            "global::System.Action<{0}>",
            managedMarshaller: null,
            BindingType.Void,
            [BindingType.Int32]);
        var floatCallbackType = BindingType.CreateCallback(
            "std::function<void(float)>",
            "global::System.Action<{0}>",
            managedMarshaller: null,
            BindingType.Void,
            [BindingType.Single]);
        var intOverload = new FunctionBinding("function:invoke_callback:int", "invoke_callback", "InvokeCallback", BindingType.Void, "sharpcpp_sample_invoke_callback_int")
        {
            ImportMethodName = "InvokeCallback"
        };
        intOverload.Parameters.Add(new ParameterBinding("parameter:int:callback", "callback", "callback", intCallbackType)
        {
            UseCallbackDelegate = true
        });
        var floatOverload = new FunctionBinding("function:invoke_callback:float", "invoke_callback", "InvokeCallback", BindingType.Void, "sharpcpp_sample_invoke_callback_float")
        {
            ImportMethodName = "InvokeCallback_2"
        };
        floatOverload.Parameters.Add(new ParameterBinding("parameter:float:callback", "callback", "callback", floatCallbackType)
        {
            UseCallbackDelegate = true
        });
        compilation.Members.Add(intOverload);
        compilation.Members.Add(floatOverload);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var aliasOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.Aliases.g.cs", StringComparison.Ordinal)).Content;

        Assert.Contains("public delegate void Global_InvokeCallback_callbackCallback(float arg0);", aliasOutput, StringComparison.Ordinal);
        Assert.Contains("public delegate void Global_InvokeCallback2_callbackCallback(int arg0);", aliasOutput, StringComparison.Ordinal);
        Assert.Contains("[CustomMarshaller(typeof(global::SharpCpp.Sample.SampleBindings.Global_InvokeCallback_callbackCallback), MarshalMode.ManagedToUnmanagedIn", aliasOutput, StringComparison.Ordinal);
        Assert.Contains("[CustomMarshaller(typeof(global::SharpCpp.Sample.SampleBindings.Global_InvokeCallback2_callbackCallback), MarshalMode.ManagedToUnmanagedIn", aliasOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Explicit_callback_managed_marshaller_takes_precedence_over_generated_helper()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "Callbacks/Callbacks.hpp" });
        var callbackType = BindingType.CreateCallback(
            "std::function<void(int)>",
            "global::System.Action<{0}>",
            "global::SharpCpp.Sample.CustomCallbackMarshaller",
            BindingType.Void,
            [BindingType.Int32]);
        var function = new FunctionBinding(
            "function:invoke_callback",
            "invoke_callback",
            "InvokeCallback",
            BindingType.Void,
            "sharpcpp_sample_invoke_callback");
        function.Parameters.Add(new ParameterBinding(
            "parameter:invoke_callback:callback",
            "callback",
            "callback",
            callbackType,
            BindingParameterDirection.In,
            BindingParameterAbiKind.Value,
            "std::function<void(int)>"));
        compilation.Members.Add(function);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;
        var aliasOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.Aliases.g.cs", StringComparison.Ordinal)).Content;

        Assert.Contains("[MarshalUsing(typeof(global::SharpCpp.Sample.CustomCallbackMarshaller))] global::System.Action<int>? callback", managedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("__CallbackMarshaller_void_int", aliasOutput, StringComparison.Ordinal);
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
    public void Managed_output_restores_exact_aliases_only_on_public_surface_for_generic_containers()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "Urho3D",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "Container/VariantMap.hpp" });
        var stringHashRecord = new RecordBinding("record:string_hash", "StringHash", "StringHash", "Urho3D::StringHash")
        {
            ManagedNamespace = "Urho3D"
        };
        var variantRecord = new RecordBinding("record:variant", "Variant", "Variant", "Urho3D::Variant")
        {
            ManagedNamespace = "Urho3D"
        };
        var unorderedMapRecord = new RecordBinding("record:ea_unordered_map", "hash_map", "UnorderedMap", "eastl::hash_map")
        {
            ManagedNamespace = "Ea",
            ManagedTypeFormat = "UnorderedMap<{0}, {1}>"
        };
        unorderedMapRecord.GenericParameters.Add(new BindingGenericParameter("TKey", 0));
        unorderedMapRecord.GenericParameters.Add(new BindingGenericParameter("TValue", 1));
        compilation.Members.Add(stringHashRecord);
        compilation.Members.Add(variantRecord);
        compilation.Members.Add(unorderedMapRecord);

        var variantMapType = BindingType.CreateHandle(
            "eastl::hash_map<Urho3D::StringHash, Urho3D::Variant>",
            "UnorderedMap",
            unorderedMapRecord.SymbolId,
            genericArguments: new[] { stringHashRecord.HandleType, variantRecord.HandleType });
        compilation.TypeAliases.Add(new BindingTypeAlias("Urho3D::VariantMap", "VariantMap", "VariantMap", variantMapType));

        var function = new FunctionBinding(
            "function:get_event_data_map",
            "get_event_data_map",
            "GetEventDataMap",
            variantMapType,
            "sharpcpp_sample_get_event_data_map");
        compilation.Members.Add(function);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath == "SharpCppSample.Bindings.g.cs").Content;
        var aliasOutput = files.Single(static file => file.RelativePath == "SharpCppSample.Bindings.Aliases.g.cs").Content;

        Assert.Contains("public static VariantMap GetEventDataMap()", managedOutput, StringComparison.Ordinal);
        Assert.Contains("internal static partial global::Ea.UnorderedMap<global::Urho3D.StringHash, global::Urho3D.Variant> GetEventDataMap();", managedOutput, StringComparison.Ordinal);
        Assert.Contains("global using VariantMap = global::Ea.UnorderedMap<global::Urho3D.StringHash, global::Urho3D.Variant>;", aliasOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Managed_output_restores_aliases_inside_callback_public_signatures()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "Urho3D",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "Core/Object.hpp" });
        var objectRecord = new RecordBinding("record:object", "Object", "Object", "Urho3D::Object")
        {
            ManagedNamespace = "Urho3D"
        };
        var stringHashRecord = new RecordBinding("record:string_hash", "StringHash", "StringHash", "Urho3D::StringHash")
        {
            ManagedNamespace = "Urho3D"
        };
        var variantRecord = new RecordBinding("record:variant", "Variant", "Variant", "Urho3D::Variant")
        {
            ManagedNamespace = "Urho3D"
        };
        var unorderedMapRecord = new RecordBinding("record:ea_unordered_map", "hash_map", "UnorderedMap", "eastl::hash_map")
        {
            ManagedNamespace = "Ea",
            ManagedTypeFormat = "UnorderedMap<{0}, {1}>"
        };
        unorderedMapRecord.GenericParameters.Add(new BindingGenericParameter("TKey", 0));
        unorderedMapRecord.GenericParameters.Add(new BindingGenericParameter("TValue", 1));
        compilation.Members.Add(objectRecord);
        compilation.Members.Add(stringHashRecord);
        compilation.Members.Add(variantRecord);
        compilation.Members.Add(unorderedMapRecord);

        var variantMapType = BindingType.CreateHandle(
            "Urho3D::VariantMap*",
            "UnorderedMap",
            unorderedMapRecord.SymbolId,
            genericArguments: new[] { stringHashRecord.HandleType, variantRecord.HandleType });
        compilation.TypeAliases.Add(new BindingTypeAlias("Urho3D::VariantMap", "VariantMap", "VariantMap", variantMapType));

        var callbackType = BindingType.CreateCallback(
            "ea::function<void(Urho3D::Object*, Urho3D::StringHash, Urho3D::VariantMap&)>",
            "global::System.Action<{0}, {1}, {2}>",
            managedMarshaller: null,
            BindingType.Void,
            [objectRecord.HandleType, stringHashRecord.HandleType, variantMapType]);
        var function = new FunctionBinding(
            "function:subscribe",
            "Subscribe",
            "Subscribe",
            BindingType.Void,
            "sharpcpp_sample_subscribe");
        function.Parameters.Add(new ParameterBinding(
            "parameter:subscribe:handler",
            "handler",
            "handler",
            callbackType,
            BindingParameterDirection.In,
            BindingParameterAbiKind.Value,
            "HandlerFunction"));
        compilation.Members.Add(function);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath == "SharpCppSample.Bindings.g.cs").Content;

        Assert.Contains("public static void Subscribe(global::System.Action<global::Urho3D.Object, global::Urho3D.StringHash, VariantMap> handler)", managedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("public static void Subscribe(global::System.Action<global::Urho3D.Object, global::Urho3D.StringHash, global::Ea.UnorderedMap", managedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Managed_output_uses_shadow_projection_for_namespaces_and_type_qualification()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Rbfx.Bindings",
            "Urho3D",
            "Bindings",
            "SharpCpp.Rbfx.Native",
            new[] { "Urho3D/Urho3DAll.h" });
        var context = new RecordBinding("record:Context", "Context", "Context", "Urho3D::Context")
        {
            ManagedNamespace = "Urho3D"
        };
        var scene = new RecordBinding("record:Scene", "Scene", "Scene", "Urho3D::Scene")
        {
            ManagedNamespace = "Urho3D"
        };
        var vectorBuffer = new RecordBinding("record:VectorBuffer", "VectorBuffer", "VectorBuffer", "Urho3D::VectorBuffer")
        {
            ManagedNamespace = "Urho3D"
        };
        var cubeMapFace = new EnumBinding("enum:CubeMapFace", "CubeMapFace", "CubeMapFace", "unsigned int", "Urho3D::CubeMapFace", BindingPrimitiveKind.UInt32)
        {
            ManagedNamespace = "Urho3D"
        };
        cubeMapFace.Values.Add(new EnumValueBinding("FACE_POSITIVE_X", "FACEPOSITIVEX", "0u"));
        var getContext = new FunctionBinding(
            "method:Scene:GetContext",
            "GetContext",
            "GetContext",
            context.HandleType,
            "scrbfx_Scene_GetContext")
        {
            OwningRecordCppTypeName = scene.CppQualifiedName
        };
        scene.Members.Add(getContext);
        var useBuffer = new FunctionBinding(
            "method:Scene:UseBuffer",
            "UseBuffer",
            "UseBuffer",
            BindingType.Void,
            "scrbfx_Scene_UseBuffer")
        {
            OwningRecordCppTypeName = scene.CppQualifiedName
        };
        useBuffer.Parameters.Add(new ParameterBinding(
            "parameter:Scene:UseBuffer:buffer",
            "buffer",
            "buffer",
            BindingType.CreateConfigured("Urho3D::VectorBuffer", "VectorBuffer")));
        scene.Members.Add(useBuffer);
        var clearFace = new FunctionBinding(
            "method:Scene:ClearFace",
            "ClearFace",
            "ClearFace",
            BindingType.Void,
            "scrbfx_Scene_ClearFace")
        {
            OwningRecordCppTypeName = scene.CppQualifiedName
        };
        clearFace.Parameters.Add(new ParameterBinding(
            "parameter:Scene:ClearFace:face",
            "face",
            "face",
            BindingType.CreateEnum("Urho3D::CubeMapFace", "CubeMapFace"))
        {
            ManagedDefaultValueExpression = "CubeMapFace.FACEPOSITIVEX"
        });
        scene.Members.Add(clearFace);
        compilation.Members.Add(context);
        compilation.Members.Add(scene);
        compilation.Members.Add(vectorBuffer);
        compilation.Members.Add(cubeMapFace);
        var contextAlias = new BindingTypeAlias("Urho3D::ContextPtr", "ContextPtr", "ContextPtr", context.HandleType);
        compilation.TypeAliases.Add(contextAlias);

        var projection = new ManagedBindingProjection(compilation.ManagedNamespace);
        projection.AddDeclaration("Urho3D.Core", context);
        projection.AddDeclaration("Urho3D.Scene", scene);
        projection.AddDeclaration("Urho3D.IO", vectorBuffer);
        projection.AddDeclaration("Urho3D.Graphics", cubeMapFace);
        compilation.SetMetadata(MetadataKeys.ManagedBindingProjection, projection);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppRbfx").Files;
        var managedOutput = files.Single(static file => file.RelativePath == "SharpCppRbfx.Bindings.g.cs").Content;
        var aliasOutput = files.Single(static file => file.RelativePath == "SharpCppRbfx.Bindings.Aliases.g.cs").Content;

        Assert.Contains("namespace Urho3D.Core", managedOutput, StringComparison.Ordinal);
        Assert.Contains("namespace Urho3D.Scene", managedOutput, StringComparison.Ordinal);
        Assert.Contains("global::Urho3D.Core.Context", managedOutput, StringComparison.Ordinal);
        Assert.Contains("global::Urho3D.IO.VectorBuffer buffer", managedOutput, StringComparison.Ordinal);
        Assert.Contains("global::Urho3D.Graphics.CubeMapFace face = global::Urho3D.Graphics.CubeMapFace.FACEPOSITIVEX", managedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("global::Urho3D.Context GetContext()", managedOutput, StringComparison.Ordinal);
        Assert.Contains("global using ContextPtr = global::Urho3D.Core.Context;", aliasOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace Urho3D.Core", aliasOutput, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(aliasOutput, "ContextPtr = global::Urho3D.Core.Context"));
    }

    [Fact]
    public void Grouped_alias_output_uses_projected_namespaces_for_registry_types()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Rbfx.Bindings",
            "Urho3D",
            "Bindings",
            "SharpCpp.Rbfx.Native",
            new[] { "Urho3D/Urho3DAll.h" });
        var rigidBody = new RecordBinding("record:RigidBody", "RigidBody", "RigidBody", "Urho3D::RigidBody")
        {
            HeaderIncludePath = "Physics/RigidBody.h",
            ManagedNamespace = "Urho3D",
            IsPolymorphic = true
        };
        compilation.Members.Add(rigidBody);

        var projection = new ManagedBindingProjection(compilation.ManagedNamespace);
        projection.AddDeclaration("Urho3D.Physics", rigidBody);
        compilation.SetMetadata(MetadataKeys.ManagedBindingProjection, projection);

        var aliasOutput = BindingOutputRenderer.Render(compilation, "SharpCppRbfx", _ => "SharpCppRbfx.Physics").Files
            .Single(static file => file.RelativePath == "SharpCppRbfx.Bindings.Aliases.g.cs").Content;

        Assert.Contains("global::SharpCpp.Support.NativeObjectRegistry.RegisterType<global::Urho3D.Physics.RigidBody>();", aliasOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("RegisterType<RigidBody>()", aliasOutput, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var offset = 0;
        while (true)
        {
            var index = text.IndexOf(value, offset, StringComparison.Ordinal);
            if (index < 0)
            {
                return count;
            }

            count++;
            offset = index + value.Length;
        }
    }

    [Fact]
    public void Alias_output_emits_catalog_aliases_even_when_unused_on_surviving_surface()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "Urho3D",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "Container/VariantMap.hpp" });
        var stringRecord = new RecordBinding("record:ea_string", "basic_string", "String", "eastl::basic_string")
        {
            ManagedNamespace = "Ea"
        };
        var variantRecord = new RecordBinding("record:variant", "Variant", "Variant", "Urho3D::Variant")
        {
            ManagedNamespace = "Urho3D"
        };
        var unorderedMapRecord = new RecordBinding("record:ea_unordered_map", "hash_map", "UnorderedMap", "eastl::hash_map")
        {
            ManagedNamespace = "Ea",
            ManagedTypeFormat = "UnorderedMap<{0}, {1}>"
        };
        unorderedMapRecord.GenericParameters.Add(new BindingGenericParameter("TKey", 0));
        unorderedMapRecord.GenericParameters.Add(new BindingGenericParameter("TValue", 1));
        compilation.Members.Add(stringRecord);
        compilation.Members.Add(variantRecord);
        compilation.Members.Add(unorderedMapRecord);

        var stringVariantMapType = BindingType.CreateHandle(
            "eastl::hash_map<eastl::basic_string<char>, Urho3D::Variant>",
            "UnorderedMap",
            unorderedMapRecord.SymbolId,
            genericArguments: new[] { stringRecord.HandleType, variantRecord.HandleType });
        compilation.TypeAliases.Add(new BindingTypeAlias("Urho3D::StringVariantMap", "StringVariantMap", "StringVariantMap", stringVariantMapType));

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var aliasOutput = files.Single(static file => file.RelativePath == "SharpCppSample.Bindings.Aliases.g.cs").Content;

        Assert.Contains("global using StringVariantMap = global::Ea.UnorderedMap<global::Ea.String, global::Urho3D.Variant>;", aliasOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Managed_output_keeps_aliases_public_but_internal_generic_signatures_fully_qualified()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "Urho3D",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "Container/VectorOfVector3.hpp" });
        var vector3TargetType = BindingType.CreateConfigured(
            "Urho3D::Vector3",
            "global::Urho3D.Math.Vector3",
            "global::Urho3D.Math.Vector3");
        var vectorRecord = new RecordBinding("record:ea_vector", "vector", "Vector", "eastl::vector")
        {
            ManagedNamespace = "Ea",
            ManagedTypeFormat = "Vector<{0}>"
        };
        vectorRecord.GenericParameters.Add(new BindingGenericParameter("T", 0));
        compilation.Members.Add(vectorRecord);
        compilation.TypeAliases.Add(new BindingTypeAlias("Urho3D::Vector3", "Vector3", "Vector3", vector3TargetType));

        var vectorOfVector3 = BindingType.CreateHandle(
            "eastl::vector<Urho3D::Vector3>",
            "Vector",
            vectorRecord.SymbolId,
            genericArguments: new[] { vector3TargetType });
        var function = new FunctionBinding(
            "function:get_path",
            "get_path",
            "GetPath",
            vectorOfVector3,
            "sharpcpp_sample_get_path");
        compilation.Members.Add(function);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath == "SharpCppSample.Bindings.g.cs").Content;

        Assert.Contains("public static global::Ea.Vector<Vector3> GetPath()", managedOutput, StringComparison.Ordinal);
        Assert.Contains("internal static partial global::Ea.Vector<global::Urho3D.Math.Vector3> GetPath();", managedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("internal static partial global::Ea.Vector<Vector3> GetPath();", managedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Managed_output_uses_concrete_primitive_types_when_no_alias_metadata_exists()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "Urho3D",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "Core/Thread.hpp" });
        var function = new FunctionBinding(
            "function:get_current_thread_id",
            "get_current_thread_id",
            "GetCurrentThreadID",
            BindingType.CreateConfigured("unsigned long", "ThreadID", primitiveKind: BindingPrimitiveKind.NativeUInt),
            "sharpcpp_sample_get_current_thread_id");
        compilation.Members.Add(function);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath == "SharpCppSample.Bindings.g.cs").Content;

        Assert.Contains("public static nuint GetCurrentThreadID()", managedOutput, StringComparison.Ordinal);
        Assert.Contains("internal static partial nuint GetCurrentThreadID();", managedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("ThreadID GetCurrentThreadID", managedOutput, StringComparison.Ordinal);
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

        Assert.Contains("public static void WritePayload(out global::SharpCpp.Sample.PayloadRecord payload)", managedOutput, StringComparison.Ordinal);
        Assert.Contains("internal static partial void WritePayload(out nint payload);", managedOutput, StringComparison.Ordinal);
        Assert.Contains("nint __abi_payload;", managedOutput, StringComparison.Ordinal);
        Assert.Contains("payload = global::SharpCpp.Sample.PayloadRecord.CreateManagedWrapper(__abi_payload, ownsHandle: false);", managedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Managed_output_uses_wrapper_types_for_non_ref_handle_import_parameters()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "Records/Composition.hpp" });
        var ownerRecord = new RecordBinding("record:chain", "chain", "Chain");
        var payloadRecord = new RecordBinding("record:payload_record", "payload_record", "PayloadRecord");
        var method = new FunctionBinding(
            "method:chain:set_payload",
            "set_payload",
            "SetPayload",
            BindingType.Void,
            "entry_set_payload")
        {
            OwningRecordCppTypeName = "chain"
        };
        method.Parameters.Add(new ParameterBinding(
            "parameter:set_payload:value",
            "value",
            "value",
            payloadRecord.HandleType,
            BindingParameterDirection.In,
            BindingParameterAbiKind.Value,
            "payload_record*"));
        ownerRecord.Members.Add(method);
        compilation.Members.Add(ownerRecord);
        compilation.Members.Add(payloadRecord);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;

        Assert.Contains("public static void SetPayload(global::SharpCpp.Sample.PayloadRecord value)", managedOutput, StringComparison.Ordinal);
        Assert.Contains("[MarshalUsing(typeof(global::SharpCpp.Sample.__PayloadRecordMarshaller<global::SharpCpp.Support.BorrowedNativeHandle>))] global::SharpCpp.Sample.PayloadRecord? value", managedOutput, StringComparison.Ordinal);
        Assert.Contains("internal static partial void SetPayload(", managedOutput, StringComparison.Ordinal);
        Assert.Contains("__PInvoke.SetPayload(value);", managedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("GetHandle(value)", managedOutput, StringComparison.Ordinal);
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
        var @enum = new EnumBinding("enum:permissions", "permissions", "Permissions", "unsigned int", underlyingPrimitiveKind: BindingPrimitiveKind.UInt32)
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
    public void Managed_output_casts_enum_arguments_to_import_abi_type()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "Enum/Enum.hpp" });
        var modeAlias = new BindingTypeAlias("mode", "Mode", "Mode", BindingType.Int32);
        var function = new FunctionBinding(
            "function:set_mode",
            "set_mode",
            "SetMode",
            BindingType.Void,
            cppQualifiedName: "set_mode");
        function.Parameters.Add(new ParameterBinding(
            "parameter:set_mode:mode",
            "mode",
            "mode",
            BindingType.Int32));
        compilation.TypeAliases.Add(modeAlias);
        compilation.Members.Add(function);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;

        Assert.Contains("public static void SetMode(Mode mode)", managedOutput, StringComparison.Ordinal);
        Assert.Contains("__PInvoke.SetMode((int)mode);", managedOutput, StringComparison.Ordinal);
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

        Assert.Contains("public partial class DerivedBox : global::SharpCpp.Sample.BaseBox", managedOutput, StringComparison.Ordinal);
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

        Assert.Contains("using System.Runtime.CompilerServices;", managedOutput, StringComparison.Ordinal);
        AssertAllLibraryImportsAreAggressivelyInlined(managedOutput);
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

        Assert.Contains("public partial class DerivedWidget : global::SharpCpp.Sample.BaseWidget", managedOutput, StringComparison.Ordinal);
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

        Assert.Contains("internal static class __WidgetMarshaller<TOwnership>", managedOutput, StringComparison.Ordinal);
        Assert.Contains("[return: MarshalUsing(typeof(global::SharpCpp.Sample.__WidgetMarshaller<global::SharpCpp.Support.OwnedNativeHandle>))]", managedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("__WidgetOwnedReturnMarshaller", managedOutput, StringComparison.Ordinal);
        Assert.Contains("internal static partial global::SharpCpp.Sample.Widget CreateWidget();", managedOutput, StringComparison.Ordinal);
        Assert.Contains("return __result;", managedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Output_treats_wrapped_record_value_returns_as_owned_handles()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "Records/Variant.hpp" });
        var variant = new RecordBinding("record:variant", "Variant", "Variant", "Sample::Variant");
        var spline = new RecordBinding("record:spline", "Spline", "Spline", "Sample::Spline");
        var getPoint = new FunctionBinding(
            "method:spline:get_point",
            "GetPoint",
            "GetPoint",
            BindingType.CreateHandle("Sample::Variant", "Variant", variant.SymbolId, cppIsRecordValue: true),
            cppQualifiedName: "Sample::Spline::GetPoint")
        {
            IsStatic = false,
            OwningRecordCppTypeName = spline.CppQualifiedName,
            CppReturnTypeName = "Sample::Variant"
        };
        spline.Members.Add(getPoint);
        compilation.Members.Add(variant);
        compilation.Members.Add(spline);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;
        var nativeOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cpp", StringComparison.Ordinal)).Content;

        Assert.Contains("[return: MarshalUsing(typeof(global::SharpCpp.Sample.__VariantMarshaller<global::SharpCpp.Support.OwnedNativeHandle>))]", managedOutput, StringComparison.Ordinal);
        Assert.Contains("internal static partial global::SharpCpp.Sample.Variant GetPoint(Spline self);", managedOutput, StringComparison.Ordinal);
        Assert.Contains("SHARPCPP_EXPORT arg_t<Sample::Variant*>", nativeOutput, StringComparison.Ordinal);
        Assert.Contains("return MarshallerInvoker::invoke_owned<FT>(&Sample::Spline::GetPoint, self);", nativeOutput, StringComparison.Ordinal);
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
        Assert.Contains("[return: MarshalUsing(typeof(global::SharpCpp.Sample.__PayloadMarshaller<global::SharpCpp.Support.OwnedNativeHandle>))]", managedOutput, StringComparison.Ordinal);
        Assert.Contains("internal static partial global::SharpCpp.Sample.Payload Get_Child(Payload self);", managedOutput, StringComparison.Ordinal);
        Assert.Contains("return __PInvoke.Get_Child(this);", managedOutput, StringComparison.Ordinal);
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
        Assert.Contains("[return: MarshalUsing(typeof(global::SharpCpp.Sample.__CloneCounterMarshaller<global::SharpCpp.Support.OwnedNativeHandle>))]", managedOutput, StringComparison.Ordinal);
        Assert.Contains("internal static partial global::SharpCpp.Sample.CloneCounter CloneCopyCtor(CloneCounter self);", managedOutput, StringComparison.Ordinal);
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
        Assert.Contains("global::SharpCpp.Support.NativeObjectRegistry.RegisterType<global::SharpCpp.Sample.BaseCounter>();", managedOutput, StringComparison.Ordinal);
        Assert.Contains("global::SharpCpp.Support.NativeObjectRegistry.RegisterType<global::SharpCpp.Sample.DerivedCounter>();", managedOutput, StringComparison.Ordinal);
        Assert.Contains("INativeObjectRegistration<BaseCounter>", managedOutput, StringComparison.Ordinal);
        Assert.Contains("sharpcpp_e2e_base_counter_type_id", nativeOutput, StringComparison.Ordinal);
        Assert.Contains("sharpcpp_e2e_base_counter_metadata", nativeOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Managed_registry_skips_polymorphic_records_without_wrapper_factory()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "Records/Inheritance.hpp" });
        var abstractBase = new RecordBinding("record:abstract_base", "abstract_base", "AbstractBase")
        {
            IsPolymorphic = true
        };
        var pureVirtual = new FunctionBinding("method:abstract_base:tick", "tick", "Tick", BindingType.Void, "entry_tick")
        {
            IsVirtual = true,
            IsPureVirtual = true,
            OwningRecordCppTypeName = "abstract_base"
        };
        abstractBase.Members.Add(pureVirtual);
        var inheritedAbstract = new RecordBinding("record:inherited_abstract", "inherited_abstract", "InheritedAbstract")
        {
            ManagedBaseTypeName = "AbstractBase",
            IsPolymorphic = true
        };
        inheritedAbstract.Bases.Add(new RecordBaseBinding(
            abstractBase.SymbolId,
            abstractBase.NativeName,
            abstractBase.ManagedName,
            abstractBase.CppQualifiedName,
            isVirtual: false));
        var concreteRecord = new RecordBinding("record:concrete_counter", "concrete_counter", "ConcreteCounter")
        {
            IsPolymorphic = true
        };
        compilation.Members.Add(abstractBase);
        compilation.Members.Add(inheritedAbstract);
        compilation.Members.Add(concreteRecord);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;

        Assert.Contains("public abstract partial class InheritedAbstract", managedOutput, StringComparison.Ordinal);
        Assert.Contains("INativeObjectRegistration<InheritedAbstract>.RegistrationCreateFactory => null", managedOutput, StringComparison.Ordinal);
        Assert.Contains("global::SharpCpp.Support.NativeObjectRegistry.RegisterType<global::SharpCpp.Sample.ConcreteCounter>();", managedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("global::SharpCpp.Support.NativeObjectRegistry.RegisterType<global::SharpCpp.Sample.InheritedAbstract>();", managedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Protected_pure_virtual_hooks_do_not_make_native_derived_wrappers_abstract()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "Records/Inheritance.hpp" });
        var nativeBase = new RecordBinding("record:native_base", "native_base", "NativeBase")
        {
            IsPolymorphic = true,
            IsAbstract = true
        };
        var protectedHook = new FunctionBinding("method:native_base:on_update", "on_update", "OnUpdate", BindingType.Void, "entry_on_update")
        {
            IsStatic = false,
            AccessModifier = BindingAccessModifier.Protected,
            IsVirtual = true,
            IsPureVirtual = true,
            OwningRecordCppTypeName = "native_base"
        };
        nativeBase.Members.Add(protectedHook);
        var nativeDerived = new RecordBinding("record:native_derived", "native_derived", "NativeDerived")
        {
            ManagedBaseTypeName = "NativeBase",
            IsPolymorphic = true
        };
        nativeDerived.Bases.Add(new RecordBaseBinding(
            nativeBase.SymbolId,
            nativeBase.NativeName,
            nativeBase.ManagedName,
            nativeBase.CppQualifiedName,
            isVirtual: false));
        compilation.Members.Add(nativeBase);
        compilation.Members.Add(nativeDerived);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;

        Assert.Contains("protected virtual void OnUpdate()", managedOutput, StringComparison.Ordinal);
        Assert.Contains("Native pure virtual method 'OnUpdate' is not callable from managed code.", managedOutput, StringComparison.Ordinal);
        Assert.Contains("public partial class NativeDerived", managedOutput, StringComparison.Ordinal);
        Assert.Contains("INativeObjectRegistration<NativeDerived>.RegistrationCreateFactory => static (handle, ownsHandle) => new NativeDerived(handle, ownsHandle)", managedOutput, StringComparison.Ordinal);
        Assert.Contains("global::SharpCpp.Support.NativeObjectRegistry.RegisterType<global::SharpCpp.Sample.NativeDerived>();", managedOutput, StringComparison.Ordinal);
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
        var childRecord = new RecordBinding("record:callback_child", "callback_child", "CallbackChild");
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
        getValue.Parameters.Add(new ParameterBinding("parameter:callback_counter:get_value:amount", "amount", "amount", BindingType.Int32, cppTypeName: "int"));
        var callTwice = new FunctionBinding("method:callback_counter:call_virtual_twice", "call_virtual_twice", "CallVirtualTwice", BindingType.Int32, "entry_call")
        {
            IsStatic = false,
            IsConstMethod = true,
            OwningRecordCppTypeName = "callback_counter"
        };
        var getChild = new FunctionBinding("method:callback_counter:get_child", "get_child", "GetChild", childRecord.HandleType, "entry_get_child")
        {
            IsStatic = false,
            IsVirtual = true,
            IsConstMethod = true,
            OwningRecordCppTypeName = "callback_counter"
        };
        record.Members.Add(ctor);
        record.Members.Add(getValue);
        record.Members.Add(callTwice);
        record.Members.Add(getChild);
        compilation.Members.Add(childRecord);
        compilation.Members.Add(record);

        BindingCompilationPolymorphismPass.Apply(compilation);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;
        var nativeOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cpp", StringComparison.Ordinal)).Content;

        Assert.Contains("public virtual int GetValue(int amount)", managedOutput, StringComparison.Ordinal);
        Assert.Contains("private void ConnectDirector()", managedOutput, StringComparison.Ordinal);
        Assert.Contains("Marshal.GetFunctionPointerForDelegate", managedOutput, StringComparison.Ordinal);
        Assert.Contains("ParameterMarshaller<int> __arg0_marshaller;", nativeOutput, StringComparison.Ordinal);
        Assert.Contains("auto __abi_arg0 = __arg0_marshaller.ConvertToAbi(amount);", nativeOutput, StringComparison.Ordinal);
        Assert.Contains("ReturnMarshaller<int> __return_marshaller;", nativeOutput, StringComparison.Ordinal);
        Assert.Contains("return __return_marshaller.ConvertToCpp(__abi_result);", nativeOutput, StringComparison.Ordinal);
        Assert.Contains("return global::SharpCpp.Support.NativeObject.GetHandle(__result);", managedOutput, StringComparison.Ordinal);
        Assert.Contains("class __callback_counter_director : public callback_counter", nativeOutput, StringComparison.Ordinal);
        Assert.Contains("sharpcpp_e2e_callback_counter_connect_director", nativeOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Director_callbase_entrypoints_are_unique_for_overloaded_virtual_methods()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "Records/Director.hpp" });
        var record = new RecordBinding("record:overloaded_director", "overloaded_director", "OverloadedDirector")
        {
            IsDirector = true
        };
        var loadFromId = new FunctionBinding("method:overloaded_director:load:int", "load", "Load", BindingType.Boolean, "entry_load_int")
        {
            IsStatic = false,
            IsVirtual = true,
            OwningRecordCppTypeName = "overloaded_director"
        };
        loadFromId.Parameters.Add(new ParameterBinding("parameter:overloaded_director:load:id", "id", "id", BindingType.Int32, cppTypeName: "int"));
        var loadFromName = new FunctionBinding("method:overloaded_director:load:name", "load", "Load", BindingType.Boolean, "entry_load_name")
        {
            IsStatic = false,
            IsVirtual = true,
            OwningRecordCppTypeName = "overloaded_director"
        };
        loadFromName.Parameters.Add(new ParameterBinding("parameter:overloaded_director:load:name", "name", "name", BindingType.CreateConfigured("const char*", "global::System.IntPtr"), cppTypeName: "const char*"));
        record.Members.Add(loadFromId);
        record.Members.Add(loadFromName);
        compilation.Members.Add(record);

        BindingCompilationPolymorphismPass.Apply(compilation);

        var nativeOutput = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files
            .Single(static file => file.RelativePath.EndsWith(".Bindings.g.cpp", StringComparison.Ordinal)).Content;

        Assert.Contains("sharpcpp_e2e_overloaded_director_callbase_Load(", nativeOutput, StringComparison.Ordinal);
        Assert.Contains("sharpcpp_e2e_overloaded_director_callbase_Load2(", nativeOutput, StringComparison.Ordinal);
        Assert.Contains("__callbase_Load2", nativeOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Director_records_qualify_inherited_callback_owner_references()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "Records/Director.hpp" });
        var baseRecord = new RecordBinding("record:base_callback", "base_callback", "BaseCallback", "Sample::BaseCallback")
        {
            IsDirector = true,
            ManagedNamespace = "SharpCpp.Sample.Core"
        };
        var onEvent = new FunctionBinding("method:base_callback:on_event", "on_event", "OnEvent", BindingType.Void, "entry_on_event")
        {
            IsStatic = false,
            IsVirtual = true,
            AccessModifier = BindingAccessModifier.Protected,
            OwningRecordCppTypeName = "Sample::BaseCallback"
        };
        baseRecord.Members.Add(onEvent);

        var derivedRecord = new RecordBinding("record:derived_callback", "derived_callback", "DerivedCallback", "Sample::DerivedCallback")
        {
            IsDirector = true,
            ManagedBaseTypeName = "global::SharpCpp.Sample.Core.BaseCallback",
            ManagedNamespace = "SharpCpp.Sample.Graphics"
        };
        derivedRecord.Bases.Add(new RecordBaseBinding(baseRecord.SymbolId, baseRecord.NativeName, baseRecord.ManagedName, baseRecord.CppQualifiedName, isVirtual: false));
        compilation.Members.Add(baseRecord);
        compilation.Members.Add(derivedRecord);

        BindingCompilationPolymorphismPass.Apply(compilation);

        var managedOutput = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files
            .Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;

        Assert.Contains("global::SharpCpp.Sample.Graphics.DerivedCallback.__GetBaseCallback_OnEventPointer(runtimeType)", managedOutput, StringComparison.Ordinal);
        Assert.Contains("typeof(global::SharpCpp.Sample.Core.BaseCallback)", managedOutput, StringComparison.Ordinal);
        Assert.Contains("\"OnEvent\"", managedOutput, StringComparison.Ordinal);
        Assert.Contains("global::SharpCpp.Sample.Graphics.DerivedCallback.CreateManagedWrapper(self, ownsHandle: false)", managedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("return HasManagedOverride(runtimeType, typeof(BaseCallback)", managedOutput, StringComparison.Ordinal);
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
        Assert.Contains("void __set_Factor(field_t<int> value)", nativeOutput, StringComparison.Ordinal);
        Assert.Contains("this->factor_ = *value;", nativeOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("void __set_Factor(arg_t<int> value)", nativeOutput, StringComparison.Ordinal);
        Assert.Contains("protected member access requires a director-backed instance", nativeOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Director_records_skip_explicitly_ignored_protected_field_helpers()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "Records/Protected.hpp" });
        var context = new RecordBinding("record:context", "context", "Context");
        var record = new RecordBinding("record:protected_counter", "protected_counter", "ProtectedCounter")
        {
            IsDirector = true
        };
        var getValue = new FunctionBinding("method:protected_counter:get_value", "get_value", "GetValue", BindingType.Int32, "entry_get")
        {
            IsStatic = false,
            IsVirtual = true,
            IsConstMethod = true,
            OwningRecordCppTypeName = "protected_counter"
        };
        var contextField = new FieldBinding(
            "field:protected_counter:context_",
            "context_",
            "Context",
            BindingType.CreateHandle("context*", "Context", context.SymbolId),
            "context*",
            "protected_counter")
        {
            AccessModifier = BindingAccessModifier.Protected
        };
        contextField.SetIgnoreReason("field explicitly ignored by pipeline");

        record.Members.Add(getValue);
        record.Members.Add(contextField);
        compilation.Members.Add(context);
        compilation.Members.Add(record);

        BindingCompilationPolymorphismPass.Apply(compilation);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var nativeOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cpp", StringComparison.Ordinal)).Content;

        Assert.DoesNotContain("__get_Context", nativeOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("__set_Context", nativeOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Director_records_render_pointer_handle_protected_field_helpers()
    {
        var compilation = CreateProtectedHandleFieldCompilation(
            BindingType.CreateHandle("payload*", "Payload", "record:payload"),
            "payload*");

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;
        var nativeOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cpp", StringComparison.Ordinal)).Content;

        Assert.Contains("protected global::SharpCpp.Sample.Payload Payload", managedOutput, StringComparison.Ordinal);
        Assert.Contains("internal static partial global::SharpCpp.Sample.Payload Get_Payload(ProtectedCounter self);", managedOutput, StringComparison.Ordinal);
        Assert.Contains("internal static partial void Set_Payload(ProtectedCounter self, [MarshalUsing(typeof(global::SharpCpp.Sample.__PayloadMarshaller<global::SharpCpp.Support.BorrowedNativeHandle>))] global::SharpCpp.Sample.Payload? value);", managedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("Set_Payload(ProtectedCounter self, [MarshalUsing(typeof(global::SharpCpp.Sample.__PayloadMarshaller<global::SharpCpp.Support.BorrowedNativeHandle>))] in global::SharpCpp.Sample.Payload? value);", managedOutput, StringComparison.Ordinal);
        Assert.Contains("__PInvoke.Set_Payload(this, value);", managedOutput, StringComparison.Ordinal);
        Assert.Contains("ret_t<payload*> __get_Payload()", nativeOutput, StringComparison.Ordinal);
        Assert.Contains("ReturnMarshaller<payload*> __return_marshaller;", nativeOutput, StringComparison.Ordinal);
        Assert.Contains("void __set_Payload(arg_t<payload*> value)", nativeOutput, StringComparison.Ordinal);
        Assert.Contains("ParameterMarshaller<payload*> __value_marshaller;", nativeOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Director_records_render_projected_handle_protected_field_with_marshaller_conversion()
    {
        var compilation = CreateProtectedHandleFieldCompilation(
            BindingType.CreateHandle("SharedPtr<payload>", "Payload", "record:payload", marshallerTypeName: "Marshaller<SharedPtr<payload>>"),
            "SharedPtr<payload>");

        var nativeOutput = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files
            .Single(static file => file.RelativePath.EndsWith(".Bindings.g.cpp", StringComparison.Ordinal)).Content;

        Assert.Contains("ret_t<SharedPtr<payload>> __get_Payload()", nativeOutput, StringComparison.Ordinal);
        Assert.Contains("ReturnMarshaller<SharedPtr<payload>> __return_marshaller;", nativeOutput, StringComparison.Ordinal);
        Assert.Contains("return __return_marshaller.ConvertToAbi(this->payload_);", nativeOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("return this->payload_;", nativeOutput, StringComparison.Ordinal);
        Assert.Contains("SHARPCPP_EXPORT ret_t<void> sharpcpp_e2e_protected_counter_payload__set(arg_t<protected_counter*> self, arg_t<SharedPtr<payload>> value)", nativeOutput, StringComparison.Ordinal);
        Assert.Contains("void __set_Payload(arg_t<SharedPtr<payload>> value)", nativeOutput, StringComparison.Ordinal);
        Assert.Contains("ParameterMarshaller<SharedPtr<payload>> __value_marshaller;", nativeOutput, StringComparison.Ordinal);
        Assert.Contains("this->payload_ = __value_marshaller.ConvertToCpp(value);", nativeOutput, StringComparison.Ordinal);
        Assert.Contains("detail::SyncMarshaller(__value_marshaller);", nativeOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("void __set_Payload(field_t<SharedPtr<payload>> value)", nativeOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("this->payload_ = *value;", nativeOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Director_records_render_borrowed_container_protected_field_with_address_return_and_null_clear()
    {
        var compilation = CreateProtectedHandleFieldCompilation(
            BindingType.CreateHandle("std::unordered_map<int, int>", "VariantMap", "record:payload", marshallerTypeName: "Marshaller<std::unordered_map<int, int>>"),
            "std::unordered_map<int, int>");

        var nativeOutput = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files
            .Single(static file => file.RelativePath.EndsWith(".Bindings.g.cpp", StringComparison.Ordinal)).Content;

        Assert.Contains("ret_t<std::unordered_map<int, int>> __get_Payload()", nativeOutput, StringComparison.Ordinal);
        Assert.Contains("return &this->payload_;", nativeOutput, StringComparison.Ordinal);
        Assert.Contains("SHARPCPP_EXPORT ret_t<void> sharpcpp_e2e_protected_counter_payload__set(arg_t<protected_counter*> self, field_t<std::unordered_map<int, int>> value)", nativeOutput, StringComparison.Ordinal);
        Assert.Contains("void __set_Payload(field_t<std::unordered_map<int, int>> value)", nativeOutput, StringComparison.Ordinal);
        Assert.Contains("if (value == nullptr)", nativeOutput, StringComparison.Ordinal);
        Assert.Contains("this->payload_.clear();", nativeOutput, StringComparison.Ordinal);
        Assert.Contains("auto* source = static_cast<std::unordered_map<int, int>*>(value);", nativeOutput, StringComparison.Ordinal);
        Assert.Contains("this->payload_ = std::move(*source);", nativeOutput, StringComparison.Ordinal);
    }

    private static BindingCompilation CreateProtectedHandleFieldCompilation(BindingType fieldType, string fieldCppTypeName)
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "Records/Protected.hpp" });
        var payloadRecord = new RecordBinding("record:payload", "payload", "Payload");
        var record = new RecordBinding("record:protected_counter", "protected_counter", "ProtectedCounter")
        {
            IsDirector = true
        };
        var virtualMethod = new FunctionBinding("method:protected_counter:get_value", "get_value", "GetValue", BindingType.Int32, "entry_get")
        {
            IsStatic = false,
            IsVirtual = true,
            IsConstMethod = true,
            OwningRecordCppTypeName = "protected_counter"
        };
        record.Members.Add(virtualMethod);
        record.Members.Add(new FieldBinding("field:protected_counter:payload_", "payload_", "Payload", fieldType, fieldCppTypeName, "protected_counter")
        {
            AccessModifier = BindingAccessModifier.Protected
        });
        compilation.Members.Add(payloadRecord);
        compilation.Members.Add(record);

        BindingCompilationPolymorphismPass.Apply(compilation);
        return compilation;
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
        Assert.Contains("(global::SharpCpp.Sample.BoxOf<T>)(object)__result!;", managedOutput, StringComparison.Ordinal);
        AssertAllLibraryImportsAreAggressivelyInlined(managedOutput);
        Assert.Contains("entry_get__1", nativeOutput, StringComparison.Ordinal);
        Assert.Contains("entry_get__2", nativeOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Managed_output_renders_comment_stub_for_non_silent_ignored_record()
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
    public void Managed_output_renders_comment_stub_for_non_silent_ignored_free_function()
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
    public void Managed_output_renders_comment_stub_for_non_silent_ignored_enum()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "Ignore/IgnoreTests.hpp" });
        var @enum = new EnumBinding("enum:skipped", "skipped", "Skipped", "unsigned int", underlyingPrimitiveKind: BindingPrimitiveKind.UInt32);
        @enum.SetMetadata(MetadataKeys.IgnoreReason, "marked [[sharpcpp::ignore]]");
        compilation.Members.Add(@enum);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;

        var lines = managedOutput.Replace("\r\n", "\n").Split('\n');

        Assert.Contains("    // enum skipped; // excluded: marked [[sharpcpp::ignore]]", lines);
    }

    [Fact]
    public void Managed_output_renders_comment_stub_for_non_silent_ignored_members_within_record()
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
    public void Managed_output_renders_comment_stub_for_non_silent_ignored_field()
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
    public void Grouped_render_keeps_files_for_silent_only_groups()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Rbfx.Bindings",
            "Urho3D",
            "Bindings",
            "SharpCpp.Rbfx.Native",
            new[] { "Urho3D/Urho3DAll.h" });
        var silentRecord = new RecordBinding("record:Urho3D::Detail::Hidden", "Hidden", "Hidden", "Urho3D::Detail::Hidden")
        {
            HeaderIncludePath = "Urho3D/RmlUI/Hidden.h"
        };
        silentRecord.SetSilentExclusionReason("internal detail");
        compilation.Members.Add(silentRecord);

        var files = BindingOutputRenderer.Render(compilation, "SharpCpp.Rbfx", DeriveRbfxGroupStem).Files;

        var managedOutput = Assert.Single(files, static file => file.RelativePath == "RmlUI/SharpCpp.Rbfx.Hidden.g.cs").Content;
        var nativeOutput = Assert.Single(files, static file => file.RelativePath == "RmlUI/SharpCpp.Rbfx.Hidden.g.cpp").Content;
        Assert.DoesNotContain("excluded: internal detail", managedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("public partial class Hidden", managedOutput, StringComparison.Ordinal);
        Assert.Contains("// <auto-generated />", nativeOutput, StringComparison.Ordinal);
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

    private static void AssertAllLibraryImportsAreAggressivelyInlined(string managedOutput)
    {
        var lines = managedOutput.Replace("\r\n", "\n").Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (!lines[index].Contains("[LibraryImport(", StringComparison.Ordinal))
            {
                continue;
            }

            Assert.True(
                index + 1 < lines.Length && lines[index + 1].Trim() == "[MethodImpl(MethodImplOptions.AggressiveInlining)]",
                $"LibraryImport at generated line {index + 1} is missing [MethodImpl(MethodImplOptions.AggressiveInlining)].");
        }
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

    [Fact]
    public void Generic_dynamic_array_records_use_pointer_abi_helpers()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "Array/SharedArray.hpp" });
        var sharedArrayRecord = new RecordBinding("record:ea::shared_array", "shared_array", "SharedArray", "ea::shared_array")
        {
            ManagedNamespace = "Ea",
            ManagedTypeFormat = "SharedArray<{0}>",
            ManagedBaseTypeName = "global::SharpCpp.Support.SharedArrayBase<{0}>"
        };
        sharedArrayRecord.GenericParameters.Add(new BindingGenericParameter("T", 0));
        sharedArrayRecord.SetMetadata(MetadataKeys.DynamicArrayRecord, true);
        compilation.Members.Add(sharedArrayRecord);

        var sharedIntArrayType = BindingType.CreateHandle(
            "ea::shared_array<int>*",
            "SharedArray",
            sharedArrayRecord.SymbolId,
            genericArguments: [BindingType.Int32])
            .WithDynamicArray(BindingType.Int32);
        var factory = new FunctionBinding(
            "function:make_shared_array",
            "make_shared_array",
            "MakeSharedArray",
            sharedIntArrayType,
            "sharpcpp_sample_make_shared_array",
            "ea::shared_array<int>",
            "make_shared_array")
        {
            ReturnsOwnedHandle = true
        };
        compilation.Members.Add(factory);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;
        var nativeOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cpp", StringComparison.Ordinal)).Content;

        Assert.Contains("public partial class SharedArray<T> : global::SharpCpp.Support.SharedArrayBase<T>", managedOutput, StringComparison.Ordinal);
        Assert.Contains("internal static partial nint __Array_Create_int(nuint length);", managedOutput, StringComparison.Ordinal);
        Assert.Contains("public static nint ConvertToUnmanaged(global::Ea.SharedArray<T> managed)", managedOutput, StringComparison.Ordinal);
        Assert.Contains("public static global::Ea.SharedArray<T> ConvertToManaged(nint unmanaged)", managedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("NativeArrayAbi", managedOutput, StringComparison.Ordinal);
        Assert.Contains("SHARPCPP_EXPORT ret_t<ea::shared_array<int>*> sharpcpp_e2e_ea_shared_array_dynamic_array_create_int(arg_t<std::size_t> length)", nativeOutput, StringComparison.Ordinal);
        Assert.Contains("return new ea::shared_array<int>(new int[length]{});", nativeOutput, StringComparison.Ordinal);
        Assert.Contains("(*self)[index]", nativeOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Ignored_fixed_array_surfaces_do_not_generate_helper_instantiations()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "Array/NativeArray.hpp" });
        var arrayRecord = new RecordBinding("record:NativeArray", "NativeArray", "NativeArray", "NativeArray")
        {
            ManagedTypeFormat = "global::SharpCpp.NativeArray<{0}>"
        };
        arrayRecord.GenericParameters.Add(new BindingGenericParameter("T", 0));
        arrayRecord.GenericParameters.Add(new BindingGenericParameter("N", 1));
        arrayRecord.SetMetadata(MetadataKeys.NativeArrayRecord, true);
        compilation.Members.Add(arrayRecord);

        var vectorType = BindingType.CreateHandle("Vector2", "global::SharpCpp.Sample.Vector2");
        var fixedArrayType = BindingType.CreateHandle(
            "NativeArray<Vector2, 2>",
            "global::SharpCpp.NativeArray<{0}>",
            arrayRecord.SymbolId,
            genericArguments: [vectorType, BindingType.CreateConfigured("2", "2")])
            .WithFixedArray(vectorType, 2);
        var ignoredFunction = new FunctionBinding("function:ignored", "ignored", "Ignored", BindingType.Void);
        ignoredFunction.Parameters.Add(new ParameterBinding("parameter:ignored:values", "values", "values", fixedArrayType));
        ignoredFunction.SetSilentExclusionReason("unsupported fixed array surface");
        compilation.Members.Add(ignoredFunction);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var nativeOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cpp", StringComparison.Ordinal)).Content;

        Assert.DoesNotContain("NativeArray_array_create", nativeOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("NativeArray_array_get", nativeOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Polymorphic_records_in_custom_namespace_qualify_registry_initializer()
    {
        var compilation = new BindingCompilation(
            "SharpCpp.Sample",
            "SharpCpp.Sample",
            "SampleBindings",
            "SharpCpp.Sample.Native",
            new[] { "Custom/Widget.hpp" });
        var record = new RecordBinding("record:Custom::Widget", "Widget", "Widget", "Custom::Widget")
        {
            ManagedNamespace = "Custom",
            IsPolymorphic = true
        };
        compilation.Members.Add(record);

        var files = BindingOutputRenderer.Render(compilation, "SharpCppSample").Files;
        var managedOutput = files.Single(static file => file.RelativePath.EndsWith(".Bindings.g.cs", StringComparison.Ordinal)).Content;

        Assert.Contains("global::SharpCpp.Sample.SampleBindings.__NativeObjectRegistryInitializer.EnsureRegistered();", managedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("\n            SampleBindings.__NativeObjectRegistryInitializer.EnsureRegistered();", managedOutput, StringComparison.Ordinal);
    }
}
