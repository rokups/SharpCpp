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
        compilation.Members.Add(stringView);

        var count = RbfxSilentExclusionPass.Apply(compilation);

        Assert.Equal(0, count);
        Assert.False(stringView.IsIgnored());
        Assert.False(stringView.IsSilentlyExcluded());
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
}
