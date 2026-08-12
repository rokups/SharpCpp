using SharpCpp.Model.Ast;
using SharpCpp.Pipeline;

using Xunit;

namespace SharpCpp.Tests.Model;

public sealed class BindingCompilationInheritancePassTests
{
    [Fact]
    public void OverridesSignature_matches_concrete_override_of_generic_virtual_parameter()
    {
        var genericMethod = new FunctionBinding("method:Base::Add", "Add", "Add", BindingType.Void)
        {
            IsStatic = false,
            IsVirtual = true,
            IsPureVirtual = true,
            NativeDeclarationId = "c:@S@Base@F@Add#&1t0.0#"
        };
        genericMethod.Parameters.Add(new ParameterBinding(
            "parameter:Base::Add:value",
            "value",
            "value",
            BindingType.CreateGenericParameter("T"),
            BindingParameterDirection.In,
            BindingParameterAbiKind.Value,
            "const T&"));

        var concreteMethod = new FunctionBinding("method:Derived::Add", "Add", "Add", BindingType.Void)
        {
            IsStatic = false,
            IsVirtual = true,
            IsOverride = true
        };
        concreteMethod.OverriddenNativeDeclarationIds.Add("c:@S@Base@F@Add#&1t0.0#");
        concreteMethod.Parameters.Add(new ParameterBinding(
            "parameter:Derived::Add:value",
            "value",
            "value",
            BindingType.Int32,
            BindingParameterDirection.In,
            BindingParameterAbiKind.Reference,
            "const int&"));

        Assert.True(RecordBindingExtensions.OverridesSignature(concreteMethod, genericMethod));

        concreteMethod.OverriddenNativeDeclarationIds.Clear();
        Assert.False(RecordBindingExtensions.OverridesSignature(concreteMethod, genericMethod));
    }

    [Fact]
    public void Apply_preserves_callback_delegate_policy_on_projected_secondary_base_methods()
    {
        var compilation = new BindingCompilation("SharpCpp.Tests", "Test", "Bindings", "Native", ["Sample.hpp"]);
        var primaryBase = new RecordBinding("record:Primary", "Primary", "Primary", "Test::Primary");
        var secondaryBase = new RecordBinding("record:Secondary", "Secondary", "Secondary", "Test::Secondary");
        var derived = new RecordBinding("record:Derived", "Derived", "Derived", "Test::Derived");
        derived.Bases.Add(new RecordBaseBinding(primaryBase.SymbolId, primaryBase.NativeName, primaryBase.ManagedName, primaryBase.CppQualifiedName, isVirtual: false));
        derived.Bases.Add(new RecordBaseBinding(secondaryBase.SymbolId, secondaryBase.NativeName, secondaryBase.ManagedName, secondaryBase.CppQualifiedName, isVirtual: false));

        var callbackType = BindingType.CreateCallback(
            "std::function<void(int)>",
            "global::System.Action<{0}>",
            managedMarshaller: null,
            BindingType.Void,
            [BindingType.Int32]);
        var method = new FunctionBinding("method:Secondary::OnProgress", "OnProgress", "OnProgress", BindingType.Void, "OnProgress")
        {
            IsStatic = false,
            OwningRecordCppTypeName = secondaryBase.CppQualifiedName
        };
        method.Parameters.Add(new ParameterBinding("parameter:callback", "callback", "callback", callbackType)
        {
            UseCallbackDelegate = true,
            CallbackDelegateTypeName = "ProgressCallback"
        });
        secondaryBase.Members.Add(method);
        compilation.Members.Add(primaryBase);
        compilation.Members.Add(secondaryBase);
        compilation.Members.Add(derived);

        BindingCompilationInheritancePass.Apply(compilation);

        var projectedMethod = Assert.Single(derived.Members.OfType<FunctionBinding>());
        var projectedParameter = Assert.Single(projectedMethod.Parameters);
        Assert.True(projectedParameter.UseCallbackDelegate);
        Assert.Equal("ProgressCallback", projectedParameter.CallbackDelegateTypeName);
    }

    [Fact]
    public void Apply_does_not_make_concrete_derived_records_abstract_for_inaccessible_secondary_overrides()
    {
        var compilation = new BindingCompilation("SharpCpp.Tests", "Test", "Bindings", "Native", ["Sample.hpp"]);
        var primaryBase = new RecordBinding("record:Primary", "Primary", "Primary", "Test::Primary");
        var secondaryBase = new RecordBinding("record:Secondary", "Secondary", "Secondary", "Test::Secondary")
        {
            IsAbstract = true
        };
        var derived = new RecordBinding("record:Derived", "Derived", "Derived", "Test::Derived");
        derived.Bases.Add(new RecordBaseBinding(primaryBase.SymbolId, primaryBase.NativeName, primaryBase.ManagedName, primaryBase.CppQualifiedName, isVirtual: false));
        derived.Bases.Add(new RecordBaseBinding(secondaryBase.SymbolId, secondaryBase.NativeName, secondaryBase.ManagedName, secondaryBase.CppQualifiedName, isVirtual: false));
        secondaryBase.Members.Add(new FunctionBinding("method:Secondary::Run", "Run", "Run", BindingType.Void, "Run")
        {
            IsStatic = false,
            IsVirtual = true,
            IsPureVirtual = true,
            OwningRecordCppTypeName = secondaryBase.CppQualifiedName
        });
        compilation.Members.Add(primaryBase);
        compilation.Members.Add(secondaryBase);
        compilation.Members.Add(derived);

        BindingCompilationInheritancePass.Apply(compilation);

        Assert.False(Assert.Single(derived.Members.OfType<FunctionBinding>()).IsPureVirtual);
    }
}
