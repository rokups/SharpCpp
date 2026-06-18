using SharpCpp.Model.Ast;
using SharpCpp.Rbfx.Generator;

using Xunit;

namespace SharpCpp.Tests.Model;

public sealed class RbfxSilentExclusionPassTests
{
    [Fact]
    public void Apply_silently_excludes_non_urho3d_declarations()
    {
        var compilation = new BindingCompilation("SharpCpp.Rbfx.Bindings", "Urho3D", "Bindings", "Native", ["Urho3D/Urho3DAll.h"]);
        var external = new RecordBinding("record:ea::vector", "vector", "Vector", "ea::vector");
        compilation.Members.Add(external);

        var count = RbfxSilentExclusionPass.Apply(compilation);

        Assert.Equal(1, count);
        Assert.True(external.IsIgnored());
        Assert.True(external.IsSilentlyExcluded());
    }

    [Fact]
    public void Apply_keeps_configured_non_urho3d_records()
    {
        var compilation = new BindingCompilation("SharpCpp.Rbfx.Bindings", "Urho3D", "Bindings", "Native", ["Urho3D/Urho3DAll.h"]);
        var stringView = new RecordBinding("record:ea::string_view", "string_view", "ReadOnlySpan", "ea::string_view")
        {
            ManagedTypeFormat = "global::System.ReadOnlySpan<byte>"
        };
        var member = new FunctionBinding("method:ea::string_view:size", "size", "Size", BindingType.UInt64, cppQualifiedName: "ea::string_view::size");
        stringView.Members.Add(member);
        compilation.Members.Add(stringView);

        var count = RbfxSilentExclusionPass.Apply(compilation);

        Assert.Equal(0, count);
        Assert.False(stringView.IsIgnored());
        Assert.False(stringView.IsSilentlyExcluded());
        Assert.False(member.IsIgnored());
    }

    [Fact]
    public void Apply_silently_excludes_detail_parent_namespaces()
    {
        var compilation = new BindingCompilation("SharpCpp.Rbfx.Bindings", "Urho3D", "Bindings", "Native", ["Urho3D/Urho3DAll.h"]);
        var record = new RecordBinding("record:Urho3D::detail::Hidden", "Hidden", "Hidden", "Urho3D::detail::Hidden");
        var enumBinding = new EnumBinding("enum:Urho3D::Detail::Mode", "Mode", "Mode", cppQualifiedName: "Urho3D::Detail::Mode");
        var function = new FunctionBinding("function:Urho3D::detail::MakeHidden", "MakeHidden", "MakeHidden", BindingType.Int32, cppQualifiedName: "Urho3D::detail::MakeHidden");
        compilation.Members.Add(record);
        compilation.Members.Add(enumBinding);
        compilation.Members.Add(function);

        var count = RbfxSilentExclusionPass.Apply(compilation);

        Assert.Equal(3, count);
        Assert.True(record.IsSilentlyExcluded());
        Assert.True(enumBinding.IsSilentlyExcluded());
        Assert.True(function.IsSilentlyExcluded());
    }

    [Fact]
    public void Apply_silently_excludes_configured_detail_records()
    {
        var compilation = new BindingCompilation("SharpCpp.Rbfx.Bindings", "Urho3D", "Bindings", "Native", ["Urho3D/Urho3DAll.h"]);
        var record = new RecordBinding("record:Urho3D::detail::Configured", "Configured", "Configured", "Urho3D::detail::Configured")
        {
            ManagedTypeFormat = "Urho3D.Configured"
        };
        compilation.Members.Add(record);

        var count = RbfxSilentExclusionPass.Apply(compilation);

        Assert.Equal(1, count);
        Assert.True(record.IsIgnored());
        Assert.True(record.IsSilentlyExcluded());
    }

    [Fact]
    public void Apply_silently_excludes_custom_smart_pointer_wrapper_records()
    {
        var compilation = new BindingCompilation("SharpCpp.Rbfx.Bindings", "Urho3D", "Bindings", "Native", ["Urho3D/Urho3DAll.h"]);
        var sharedPtr = new RecordBinding("record:Urho3D::SharedPtr", "SharedPtr", "SharedPtr", "Urho3D::SharedPtr")
        {
            ManagedTypeFormat = "Urho3D.SharedPtr<{0}>"
        };
        var weakPtr = new RecordBinding("record:Urho3D::WeakPtr", "WeakPtr", "WeakPtr", "Urho3D::WeakPtr")
        {
            ManagedTypeFormat = "Urho3D.WeakPtr<{0}>"
        };
        compilation.Members.Add(sharedPtr);
        compilation.Members.Add(weakPtr);

        var count = RbfxSilentExclusionPass.Apply(compilation);

        Assert.Equal(2, count);
        Assert.True(sharedPtr.IsSilentlyExcluded());
        Assert.True(weakPtr.IsSilentlyExcluded());
    }

    [Fact]
    public void Apply_silently_excludes_custom_smart_pointer_deduction_guides()
    {
        var compilation = new BindingCompilation("SharpCpp.Rbfx.Bindings", "Urho3D", "Bindings", "Native", ["Urho3D/Urho3DAll.h"]);
        var sharedPtr = new FunctionBinding("function:Urho3D::SharedPtr", "deduced", "SharedPtr", BindingType.Void, cppQualifiedName: "Urho3D::PtrGuide");
        sharedPtr.SetMetadata(MetadataKeys.DeclarationDisplayText, "template <class InterfaceType> SharedPtr() -> SharedPtr<InterfaceType>");
        var weakPtr = new FunctionBinding("function:Urho3D::WeakPtr", "deduced", "WeakPtr", BindingType.Void, cppQualifiedName: "Urho3D::PtrGuide");
        weakPtr.SetMetadata(MetadataKeys.DeclarationDisplayText, "template <class InterfaceType> WeakPtr() -> WeakPtr<InterfaceType>");
        compilation.Members.Add(sharedPtr);
        compilation.Members.Add(weakPtr);

        var count = RbfxSilentExclusionPass.Apply(compilation);

        Assert.Equal(2, count);
        Assert.True(sharedPtr.IsSilentlyExcluded());
        Assert.True(weakPtr.IsSilentlyExcluded());
    }

    [Fact]
    public void Apply_silently_excludes_custom_smart_pointer_record_members()
    {
        var compilation = new BindingCompilation("SharpCpp.Rbfx.Bindings", "Urho3D", "Bindings", "Native", ["Urho3D/Urho3DAll.h"]);
        var record = new RecordBinding("record:Urho3D::SharedPtr", "SharedPtr", "SharedPtr", "Urho3D::SharedPtr");
        var constructor = new FunctionBinding("method:Urho3D::SharedPtr::SharedPtr", "SharedPtr", "SharedPtr", BindingType.Void, cppQualifiedName: "Urho3D::SharedPtr<InterfaceType, RefCountedType>");
        record.Members.Add(constructor);
        compilation.Members.Add(record);

        var count = RbfxSilentExclusionPass.Apply(compilation);

        Assert.Equal(2, count);
        Assert.True(record.IsSilentlyExcluded());
        Assert.True(constructor.IsSilentlyExcluded());
    }

    [Fact]
    public void Apply_silently_excludes_templated_smart_pointer_record_names()
    {
        var compilation = new BindingCompilation("SharpCpp.Rbfx.Bindings", "Urho3D", "Bindings", "Native", ["Urho3D/Urho3DAll.h"]);
        var record = new RecordBinding(
            "record:Urho3D::SharedPtr<InterfaceType, RefCountedType>",
            "SharedPtr",
            "SharedPtr",
            "Urho3D::SharedPtr<InterfaceType, RefCountedType>");
        compilation.Members.Add(record);

        var count = RbfxSilentExclusionPass.Apply(compilation);

        Assert.Equal(1, count);
        Assert.True(record.IsSilentlyExcluded());
    }

    [Fact]
    public void Apply_silently_excludes_flagset_only_operators()
    {
        var compilation = new BindingCompilation("SharpCpp.Rbfx.Bindings", "Urho3D", "Bindings", "Native", ["Urho3D/Urho3DAll.h"]);
        var flagSetType = BindingType.CreateConfigured("CXType_Unexposed", "unsupported");
        var enumType = BindingType.CreateEnum("PacketType", "PacketType");
        var bitwiseOr = new FunctionBinding("function:operator|", "operator|", "OperatorOr", flagSetType, cppQualifiedName: "Urho3D::operator|")
        {
            OperatorToken = "|"
        };
        bitwiseOr.SetMetadata(MetadataKeys.DeclarationDisplayText, "inline constexpr Urho3D::FlagSet<PacketType> operator|(const PacketType lhs, const PacketType rhs)");
        bitwiseOr.Parameters.Add(new ParameterBinding("parameter:operator|:lhs", "lhs", "lhs", enumType, cppTypeName: "const PacketType"));
        bitwiseOr.Parameters.Add(new ParameterBinding("parameter:operator|:rhs", "rhs", "rhs", enumType, cppTypeName: "PacketType"));
        var complement = new FunctionBinding("function:operator~", "operator~", "OperatorNot", flagSetType, cppQualifiedName: "Urho3D::operator~")
        {
            OperatorToken = "~"
        };
        complement.SetMetadata(MetadataKeys.DeclarationDisplayText, "inline constexpr Urho3D::FlagSet<PacketType> operator~(const PacketType rhs)");
        complement.Parameters.Add(new ParameterBinding("parameter:operator~:rhs", "rhs", "rhs", enumType, cppTypeName: "PacketType"));
        compilation.Members.Add(bitwiseOr);
        compilation.Members.Add(complement);

        var count = RbfxSilentExclusionPass.Apply(compilation);

        Assert.Equal(2, count);
        Assert.True(bitwiseOr.IsSilentlyExcluded());
        Assert.True(complement.IsSilentlyExcluded());
    }

    [Fact]
    public void Apply_keeps_non_flagset_bitwise_operators()
    {
        var compilation = new BindingCompilation("SharpCpp.Rbfx.Bindings", "Urho3D", "Bindings", "Native", ["Urho3D/Urho3DAll.h"]);
        var bitwiseOr = new FunctionBinding("function:operator|", "operator|", "OperatorOr", BindingType.UInt32, cppQualifiedName: "Urho3D::operator|")
        {
            OperatorToken = "|"
        };
        bitwiseOr.Parameters.Add(new ParameterBinding("parameter:operator|:lhs", "lhs", "lhs", BindingType.UInt32));
        bitwiseOr.Parameters.Add(new ParameterBinding("parameter:operator|:rhs", "rhs", "rhs", BindingType.UInt32));
        compilation.Members.Add(bitwiseOr);

        var count = RbfxSilentExclusionPass.Apply(compilation);

        Assert.Equal(0, count);
        Assert.False(bitwiseOr.IsIgnored());
    }
}
