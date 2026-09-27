using SharpCpp.Model.Ast;
using SharpCpp.Rendering;

using Xunit;

namespace SharpCpp.Tests.Model;

public sealed class ManagedCallbackRenderingTests
{
    [Fact]
    public void Callback_entry_point_is_emitted_on_top_level_binding_class()
    {
        var compilation = new BindingCompilation("TestProject", "Test", "Bindings", "Native", ["Sample.hpp"]);
        var callbackType = BindingType.CreateCallback(
            "std::function<void(int)>",
            "global::System.Action<{0}>",
            managedMarshaller: null,
            BindingType.Void,
            [BindingType.Int32]);
        var function = new FunctionBinding("function:invoke", "invoke", "Invoke", BindingType.Void);
        function.Parameters.Add(new ParameterBinding("parameter:callback", "callback", "callback", callbackType));
        compilation.Members.Add(function);

        var generated = BindingOutputRenderer.Render(compilation, "Test.Bindings");
        var aliases = Assert.Single(generated.Files, file => file.RelativePath.EndsWith("Aliases.g.cs", StringComparison.Ordinal));

        Assert.Contains("&global::Test.Bindings.__InvokeManagedCallback_", aliases.Content, StringComparison.Ordinal);
        Assert.Contains("private static void __InvokeManagedCallback_", aliases.Content, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "&__CallbackMarshaller",
            aliases.Content,
            StringComparison.Ordinal);
        Assert.DoesNotContain("ManagedCallback.GetFunctionPointer", aliases.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Virtual_callback_pointer_field_is_unsafe_and_direct()
    {
        var compilation = new BindingCompilation("TestProject", "Test", "Bindings", "Native", ["Sample.hpp"]);
        var record = new RecordBinding("record:sample", "Sample", "Sample", "Sample")
        {
            IsDirector = true,
            IsPolymorphic = true
        };
        record.Members.Add(new FunctionBinding("method:sample:run", "Run", "Run", BindingType.Void)
        {
            IsStatic = false,
            IsVirtual = true,
            OwningRecordCppTypeName = record.CppQualifiedName
        });
        compilation.Members.Add(record);

        var generated = BindingOutputRenderer.Render(compilation, "Test.Bindings");
        var bindings = Assert.Single(generated.Files, file =>
            file.RelativePath.EndsWith("Bindings.g.cs", StringComparison.Ordinal) &&
            !file.RelativePath.EndsWith("Aliases.g.cs", StringComparison.Ordinal));
        var aliases = Assert.Single(generated.Files, file => file.RelativePath.EndsWith("Aliases.g.cs", StringComparison.Ordinal));

        Assert.Contains("private static readonly unsafe nint __DispatchSample_RunPointer", bindings.Content, StringComparison.Ordinal);
        Assert.Contains("Bindings.__InvokeVirtualCallback_Sample_RunPointer", bindings.Content, StringComparison.Ordinal);
        Assert.Contains("internal static void __DispatchSample_Run", bindings.Content, StringComparison.Ordinal);
        Assert.Contains("internal static readonly unsafe nint __InvokeVirtualCallback_Sample_RunPointer", aliases.Content, StringComparison.Ordinal);
        Assert.Contains("private static void __InvokeVirtualCallback_Sample_Run", aliases.Content, StringComparison.Ordinal);
        Assert.Contains("Environment.FailFast(\"SharpCpp managed virtual callback threw an exception.\", exception)", aliases.Content, StringComparison.Ordinal);
        Assert.Contains(".__DispatchSample_Run(self);", aliases.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("ManagedCallback.GetFunctionPointer", bindings.Content, StringComparison.Ordinal);
    }
}
