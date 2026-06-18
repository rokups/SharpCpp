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
}
