using SharpCpp.Model.Ast;
using SharpCpp.Pipeline;

using Xunit;

namespace SharpCpp.Tests.Model;

public sealed class DegradeUnsupportedSurfacePassTests
{
    [Fact]
    public void Apply_keeps_generic_enum_arguments_resolved_by_enum_identity()
    {
        var compilation = new BindingCompilation("SharpCpp.Tests", "Urho3D", "Bindings", "Native", ["Attribute.h"]);

        var enumBinding = new EnumBinding(
            "enum:Urho3D::OwnedMode",
            "OwnedMode",
            "OwnedMode",
            cppQualifiedName: "Urho3D::OwnedMode");
        var flagSetRecord = new RecordBinding("record:Urho3D::FlagSet", "FlagSet", "FlagSet", "Urho3D::FlagSet")
        {
            ManagedTypeFormat = "FlagSet<{0}>"
        };
        flagSetRecord.GenericParameters.Add(new BindingGenericParameter("T", 0));

        var enumArgument = BindingType.CreateEnum("AttributeMode", enumBinding.ManagedName, enumId: enumBinding.SymbolId);
        var flagSetType = BindingType.CreateHandle(
            "Urho3D::FlagSet<AttributeMode>*",
            flagSetRecord.ManagedName,
            flagSetRecord.SymbolId,
            genericArguments: [enumArgument]);

        var attributeInfo = new RecordBinding("record:Urho3D::AttributeInfo", "AttributeInfo", "AttributeInfo", "Urho3D::AttributeInfo");
        var constructor = new FunctionBinding(
            "method:Urho3D::AttributeInfo::AttributeInfo",
            "AttributeInfo",
            "AttributeInfo",
            BindingType.Void,
            cppQualifiedName: "Urho3D::AttributeInfo::AttributeInfo")
        {
            IsConstructor = true,
            IsStatic = false
        };
        constructor.Parameters.Add(new ParameterBinding("parameter:mode", "mode", "mode", flagSetType, cppTypeName: "AttributeModeFlags"));
        attributeInfo.Members.Add(constructor);
        attributeInfo.Members.Add(new FieldBinding("field:mode", "mode_", "Mode", flagSetType, "AttributeModeFlags", attributeInfo.CppQualifiedName));

        compilation.Members.Add(enumBinding);
        compilation.Members.Add(flagSetRecord);
        compilation.Members.Add(attributeInfo);

        DegradeUnsupportedSurfacePass.Apply(compilation);

        Assert.False(attributeInfo.IsIgnored(), attributeInfo.GetIgnoreReason());
        Assert.False(constructor.IsIgnored(), constructor.GetIgnoreReason());
        Assert.False(attributeInfo.Members.OfType<FieldBinding>().Single().IsIgnored(), attributeInfo.Members.OfType<FieldBinding>().Single().GetIgnoreReason());
    }

    [Fact]
    public void Apply_ignores_protected_handle_fields_before_rendering()
    {
        var compilation = new BindingCompilation("SharpCpp.Tests", "Urho3D", "Bindings", "Native", ["Object.h"]);
        var context = new RecordBinding("record:Urho3D::Context", "Context", "Context", "Urho3D::Context");
        var @object = new RecordBinding("record:Urho3D::Object", "Object", "Object", "Urho3D::Object")
        {
            IsDirector = true
        };
        var contextType = BindingType.CreateHandle("Urho3D::Context*", "Context", context.SymbolId);
        var field = new FieldBinding("field:Urho3D::Object::context_", "context_", "Context", contextType, "Urho3D::Context*", @object.CppQualifiedName)
        {
            AccessModifier = BindingAccessModifier.Protected
        };

        @object.Members.Add(field);
        compilation.Members.Add(context);
        compilation.Members.Add(@object);

        DegradeUnsupportedSurfacePass.Apply(compilation);

        Assert.False(@object.IsIgnored(), @object.GetIgnoreReason());
        Assert.True(field.IsIgnored());
        Assert.Contains("protected handle fields", field.GetIgnoreReason(), StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_ignores_non_pure_director_virtuals_with_unsupported_callback_parameters()
    {
        var compilation = new BindingCompilation("SharpCpp.Tests", "Urho3D", "Bindings", "Native", ["Drawable.h"]);
        var drawable = new RecordBinding("record:Urho3D::Drawable", "Drawable", "Drawable", "Urho3D::Drawable")
        {
            IsDirector = true
        };
        var update = new FunctionBinding("method:Urho3D::Drawable::UpdateWorldOrigin", "UpdateWorldOrigin", "UpdateWorldOrigin", BindingType.Void, cppQualifiedName: "Urho3D::Drawable::UpdateWorldOrigin")
        {
            IsVirtual = true,
            OwningRecordCppTypeName = drawable.CppQualifiedName
        };
        update.Parameters.Add(new ParameterBinding("parameter:oldOrigin", "oldOrigin", "oldOrigin", BindingType.Int32, BindingParameterDirection.In, BindingParameterAbiKind.Reference, "const int&"));
        drawable.Members.Add(update);
        drawable.Members.Add(new FieldBinding("field:Urho3D::Drawable::value_", "value_", "Value", BindingType.Int32, "int", drawable.CppQualifiedName));
        compilation.Members.Add(drawable);

        DegradeUnsupportedSurfacePass.Apply(compilation);

        Assert.False(drawable.IsIgnored(), drawable.GetIgnoreReason());
        Assert.True(update.IsIgnored());
        Assert.Contains("director callback", update.GetIgnoreReason(), StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_ignores_director_records_with_unsupported_pure_virtual_callbacks()
    {
        var compilation = new BindingCompilation("SharpCpp.Tests", "Urho3D", "Bindings", "Native", ["Abstract.h"]);
        var record = new RecordBinding("record:Urho3D::AbstractThing", "AbstractThing", "AbstractThing", "Urho3D::AbstractThing")
        {
            IsDirector = true,
            IsAbstract = true
        };
        var compute = new FunctionBinding("method:Urho3D::AbstractThing::Compute", "Compute", "Compute", BindingType.Int32, cppQualifiedName: "Urho3D::AbstractThing::Compute")
        {
            IsVirtual = true,
            IsPureVirtual = true,
            OwningRecordCppTypeName = record.CppQualifiedName
        };
        compute.Parameters.Add(new ParameterBinding("parameter:value", "value", "value", BindingType.Int32, BindingParameterDirection.Ref, BindingParameterAbiKind.Reference, "int&"));
        record.Members.Add(compute);
        compilation.Members.Add(record);

        DegradeUnsupportedSurfacePass.Apply(compilation);

        Assert.True(record.IsIgnored());
        Assert.Contains("pure virtual director callback", record.GetIgnoreReason(), StringComparison.Ordinal);
    }
}
