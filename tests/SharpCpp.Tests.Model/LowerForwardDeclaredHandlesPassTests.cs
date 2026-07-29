using SharpCpp.Model.Ast;
using SharpCpp.Pipeline;

using Xunit;

namespace SharpCpp.Tests.Model;

public sealed class LowerForwardDeclaredHandlesPassTests
{
    [Fact]
    public void Lowers_forward_declared_handle_uses_to_intptr()
    {
        var compilation = new BindingCompilation("Sample", "Sample", "Native", "SampleNative", []);
        var forward = new RecordBinding("record:Sample::Opaque", "Opaque", "Opaque", "Sample::Opaque");
        forward.SetMetadata(MetadataKeys.ForwardDeclaration, true);
        var function = new FunctionBinding(
            "function:UseOpaque",
            "UseOpaque",
            "UseOpaque",
            forward.HandleType,
            cppReturnTypeName: "Sample::Opaque*");
        function.Parameters.Add(new ParameterBinding(
            "parameter:opaque",
            "opaque",
            "opaque",
            forward.HandleType,
            cppTypeName: "Sample::Opaque*"));
        compilation.Members.Add(forward);
        compilation.Members.Add(function);

        LowerForwardDeclaredHandlesPass.Apply(compilation);

        Assert.Equal(BindingPrimitiveKind.IntPtr, function.ReturnType.PrimitiveKind);
        Assert.Equal("Sample::Opaque*", function.ReturnType.CppTypeName);
        Assert.Equal(BindingPrimitiveKind.IntPtr, function.Parameters[0].Type.PrimitiveKind);
        Assert.Equal("Sample::Opaque*", function.Parameters[0].Type.CppTypeName);
    }

    [Fact]
    public void Preserves_opaque_native_handles()
    {
        var compilation = new BindingCompilation("Sample", "Sample", "Native", "SampleNative", []);
        var forward = new RecordBinding("record:Sample::Opaque", "Opaque", "Opaque", "Sample::Opaque");
        forward.SetMetadata(MetadataKeys.ForwardDeclaration, true);
        forward.SetMetadata(MetadataKeys.OpaqueNativeHandle, true);
        var function = new FunctionBinding(
            "function:UseOpaque",
            "UseOpaque",
            "UseOpaque",
            BindingType.Void,
            cppReturnTypeName: "void");
        function.Parameters.Add(new ParameterBinding(
            "parameter:opaque",
            "opaque",
            "opaque",
            forward.HandleType,
            cppTypeName: "Sample::Opaque*"));
        compilation.Members.Add(forward);
        compilation.Members.Add(function);

        LowerForwardDeclaredHandlesPass.Apply(compilation);

        Assert.Equal(BindingTypeKind.Handle, function.Parameters[0].Type.Kind);
        Assert.Equal(forward.SymbolId, function.Parameters[0].Type.RecordId);
    }
}
