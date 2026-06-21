using SharpCpp.Model.Ast;
using SharpCpp.Rbfx.Generator;

using Xunit;

namespace SharpCpp.Tests.Model;

public sealed class RbfxSubmoduleSurfacePassTests
{
    [Fact]
    public void Rml_pass_prunes_public_record_methods_with_non_public_rml_types()
    {
        var compilation = CreateCompilation();
        var element = new RecordBinding("record:Rml::Element", "Element", "Element", "Rml::Element");
        var method = new FunctionBinding(
            "method:Rml::Element::GetDisplay",
            "GetDisplay",
            "GetDisplay",
            BindingType.CreateEnum("Style::Display", "Display"),
            cppReturnTypeName: "Style::Display",
            cppQualifiedName: "Rml::Element::GetDisplay")
        {
            IsStatic = false
        };
        element.Members.Add(method);
        compilation.Members.Add(element);

        RbfxRmlUiSurfacePass.Apply(compilation);

        Assert.True(method.IsIgnored());
        Assert.True(method.IsSilentlyExcluded());
    }

    [Fact]
    public void Rml_pass_removes_generic_instantiations_with_non_public_rml_types()
    {
        var compilation = CreateCompilation();
        var map = new RecordBinding("record:eastl::hash_map", "hash_map", "UnorderedMap", "eastl::hash_map");
        map.SupportedGenericInstantiations.Add(new RecordGenericInstantiationBinding(
        [
            BindingType.CreateEnum("PropertyId", "PropertyId"),
            BindingType.CreateHandle("Rml::Property*", "Property", recordId: "record:Rml::Property")
        ]));
        compilation.Members.Add(map);

        RbfxRmlUiSurfacePass.Apply(compilation);

        Assert.Empty(map.SupportedGenericInstantiations);
    }

    [Fact]
    public void Rml_pass_removes_non_public_rml_bases_and_aliases()
    {
        var compilation = CreateCompilation();
        var element = new RecordBinding("record:Rml::Element", "Element", "Element", "Rml::Element")
        {
            ManagedBaseTypeName = "ScriptInterface"
        };
        element.Bases.Add(new RecordBaseBinding("record:Rml::ScriptInterface", "ScriptInterface", "ScriptInterface", "Rml::ScriptInterface", isVirtual: false));
        compilation.Members.Add(element);
        compilation.TypeAliases.Add(new BindingTypeAlias(
            "Rml::Units",
            "Units",
            "Units",
            BindingType.CreateEnum("Rml::Unit", "Unit")));

        RbfxRmlUiSurfacePass.Apply(compilation);

        Assert.Empty(element.Bases);
        Assert.Null(element.ManagedBaseTypeName);
        Assert.Empty(compilation.TypeAliases);
    }

    [Fact]
    public void Rml_pass_keeps_data_model_records_for_application_sample()
    {
        var compilation = CreateCompilation();
        var records = new[]
        {
            "Rml::Context",
            "Rml::DataModelConstructor",
            "Rml::DataModelHandle",
            "Rml::Event",
            "Rml::Variant",
            "Rml::VariantList"
        }.Select(name => new RecordBinding($"record:{name}", name.Split("::")[1], name.Split("::")[1], name)).ToArray();

        foreach (var record in records)
        {
            record.SetSilentExclusionReason("outside default rbfx surface");
            compilation.Members.Add(record);
        }

        RbfxRmlUiSurfacePass.Apply(compilation);

        foreach (var record in records)
        {
            Assert.Equal("Rml", record.ManagedNamespace);
            Assert.False(record.IsIgnored());
            Assert.False(record.IsSilentlyExcluded());
        }
    }

    [Fact]
    public void ImGui_pass_prunes_texture_id_fields()
    {
        var compilation = CreateCompilation();
        var command = new RecordBinding("record:ImDrawCmd", "ImDrawCmd", "ImDrawCmd", "ImDrawCmd");
        var field = new FieldBinding(
            "field:ImDrawCmd::TextureId",
            "TextureId",
            "TextureId",
            BindingType.CreateHandle("ImTextureID*", "global::System.IntPtr"),
            "ImTextureID*",
            "ImDrawCmd");
        command.Members.Add(field);
        compilation.Members.Add(command);

        RbfxImGuiSurfacePass.Apply(compilation);

        Assert.True(field.IsIgnored());
        Assert.True(field.IsSilentlyExcluded());
    }

    [Fact]
    public void ImGui_pass_restores_public_records_after_prior_silent_exclusion()
    {
        var compilation = CreateCompilation();
        var drawList = new RecordBinding("record:ImDrawList", "ImDrawList", "ImDrawList", "ImDrawList");
        drawList.SetSilentExclusionReason("outside default rbfx surface");
        compilation.Members.Add(drawList);

        RbfxImGuiSurfacePass.Apply(compilation);

        Assert.Equal("ImGuiNet", drawList.ManagedNamespace);
        Assert.False(drawList.IsIgnored());
        Assert.False(drawList.IsSilentlyExcluded());
    }

    [Fact]
    public void ImGui_pass_restores_free_functions_after_prior_silent_exclusion()
    {
        var compilation = CreateCompilation();
        var begin = new FunctionBinding(
            "function:ImGui::Begin",
            "Begin",
            "Begin",
            BindingType.Boolean,
            cppQualifiedName: "ImGui::Begin")
        {
            HeaderIncludePath = "ImGui/imgui.h"
        };
        begin.SetSilentExclusionReason("outside default rbfx surface");
        compilation.Members.Add(begin);

        RbfxImGuiSurfacePass.Apply(compilation);

        Assert.Equal("ImGuiNet", begin.ManagedNamespace);
        Assert.Equal("ImGui", begin.ManagedClassName);
        Assert.False(begin.IsIgnored());
        Assert.False(begin.IsSilentlyExcluded());
    }

    [Fact]
    public void ImGui_pass_keeps_internal_free_functions_silently_excluded()
    {
        var compilation = CreateCompilation();
        var recover = new FunctionBinding(
            "function:ImGui::ErrorCheckEndFrameRecover",
            "ErrorCheckEndFrameRecover",
            "ErrorCheckEndFrameRecover",
            BindingType.Void,
            cppQualifiedName: "ImGui::ErrorCheckEndFrameRecover")
        {
            HeaderIncludePath = "ImGui/imgui_internal.h"
        };
        compilation.Members.Add(recover);

        RbfxImGuiSurfacePass.Apply(compilation);

        Assert.Equal("ImGuiNet", recover.ManagedNamespace);
        Assert.Equal("ImGui", recover.ManagedClassName);
        Assert.True(recover.IsIgnored());
        Assert.True(recover.IsSilentlyExcluded());
    }

    [Fact]
    public void ImGui_pass_keeps_non_sample_public_free_functions_silently_excluded()
    {
        var compilation = CreateCompilation();
        var plotLines = new FunctionBinding(
            "function:ImGui::PlotLines",
            "PlotLines",
            "PlotLines",
            BindingType.Void,
            cppQualifiedName: "ImGui::PlotLines")
        {
            HeaderIncludePath = "ImGui/imgui.h"
        };
        compilation.Members.Add(plotLines);

        RbfxImGuiSurfacePass.Apply(compilation);

        Assert.Equal("ImGuiNet", plotLines.ManagedNamespace);
        Assert.Equal("ImGui", plotLines.ManagedClassName);
        Assert.True(plotLines.IsIgnored());
        Assert.True(plotLines.IsSilentlyExcluded());
    }

    [Fact]
    public void ImGui_pass_prunes_char_pointer_fields()
    {
        var compilation = CreateCompilation();
        var io = new RecordBinding("record:ImGuiIO", "ImGuiIO", "ImGuiIO", "ImGuiIO");
        var field = new FieldBinding(
            "field:ImGuiIO::IniFilename",
            "IniFilename",
            "IniFilename",
            BindingType.CreateHandle("const char*", "global::System.ReadOnlySpan<byte>"),
            "const char*",
            "ImGuiIO");
        io.Members.Add(field);
        compilation.Members.Add(io);

        RbfxImGuiSurfacePass.Apply(compilation);

        Assert.True(field.IsIgnored());
        Assert.True(field.IsSilentlyExcluded());
    }

    [Fact]
    public void ImGui_pass_prunes_const_imvec4_pointer_methods()
    {
        var compilation = CreateCompilation();
        var drawList = new RecordBinding("record:ImDrawList", "ImDrawList", "ImDrawList", "ImDrawList");
        var method = new FunctionBinding(
            "method:ImDrawList::AddText",
            "AddText",
            "AddText",
            BindingType.Void,
            cppQualifiedName: "ImDrawList::AddText")
        {
            IsStatic = false
        };
        method.Parameters.Add(new ParameterBinding(
            "parameter:ImDrawList::AddText:cpu_fine_clip_rect",
            "cpu_fine_clip_rect",
            "cpuFineClipRect",
            BindingType.CreateHandle("const ImVec4*", "global::Urho3D.Math.Vector4?"),
            cppTypeName: "const ImVec4*"));
        drawList.Members.Add(method);
        compilation.Members.Add(drawList);

        RbfxImGuiSurfacePass.Apply(compilation);

        Assert.True(method.IsIgnored());
        Assert.True(method.IsSilentlyExcluded());
    }

    [Fact]
    public void ImGui_pass_prunes_vararg_free_functions()
    {
        var compilation = CreateCompilation();
        var text = new FunctionBinding(
            "function:ImGui::Text",
            "Text",
            "Text",
            BindingType.Void,
            cppQualifiedName: "ImGui::Text");
        text.SetMetadata(MetadataKeys.DeclarationDisplayText, "IMGUI_API void Text(const char* fmt, ...)");
        compilation.Members.Add(text);

        RbfxImGuiSurfacePass.Apply(compilation);

        Assert.True(text.IsIgnored());
        Assert.True(text.IsSilentlyExcluded());
    }

    [Fact]
    public void ImGui_pass_prunes_callback_function_parameters()
    {
        var compilation = CreateCompilation();
        var inputText = new FunctionBinding(
            "function:ImGui::InputText",
            "InputText",
            "InputText",
            BindingType.Boolean,
            cppQualifiedName: "ImGui::InputText");
        inputText.Parameters.Add(new ParameterBinding(
            "parameter:ImGui::InputText:callback",
            "callback",
            "callback",
            BindingType.CreateHandle("ImGuiInputTextCallback", "global::System.IntPtr"),
            cppTypeName: "ImGuiInputTextCallback"));
        compilation.Members.Add(inputText);

        RbfxImGuiSurfacePass.Apply(compilation);

        Assert.True(inputText.IsIgnored());
        Assert.True(inputText.IsSilentlyExcluded());
    }

    [Fact]
    public void ImGui_pass_prunes_raw_array_parameters()
    {
        var compilation = CreateCompilation();
        var combo = new FunctionBinding(
            "function:ImGui::Combo",
            "Combo",
            "Combo",
            BindingType.Boolean,
            cppQualifiedName: "ImGui::Combo");
        combo.Parameters.Add(new ParameterBinding(
            "parameter:ImGui::Combo:items",
            "items",
            "items",
            BindingType.CreateHandle("const char* const[]", "global::System.ReadOnlySpan<nint>"),
            cppTypeName: "const char*const[]"));
        compilation.Members.Add(combo);

        RbfxImGuiSurfacePass.Apply(compilation);

        Assert.True(combo.IsIgnored());
        Assert.True(combo.IsSilentlyExcluded());
    }

    [Fact]
    public void ImGui_pass_prunes_user_defined_literal_operators()
    {
        var compilation = CreateCompilation();
        var literal = new FunctionBinding(
            "function:ImGui::Literals::operator\"\"_pt",
            "operator\"\"_pt",
            "operator\"\"_pt",
            BindingType.Single,
            cppQualifiedName: "ImGui::Literals::operator\"\"_pt");
        compilation.Members.Add(literal);

        RbfxImGuiSurfacePass.Apply(compilation);

        Assert.True(literal.IsIgnored());
        Assert.True(literal.IsSilentlyExcluded());
    }

    [Fact]
    public void ImGui_pass_prunes_fixed_array_methods()
    {
        var compilation = CreateCompilation();
        var atlas = new RecordBinding("record:ImFontAtlas", "ImFontAtlas", "ImFontAtlas", "ImFontAtlas");
        var method = new FunctionBinding(
            "method:ImFontAtlas::GetMouseCursorTexData",
            "GetMouseCursorTexData",
            "GetMouseCursorTexData",
            BindingType.Boolean,
            cppQualifiedName: "ImFontAtlas::GetMouseCursorTexData")
        {
            IsStatic = false
        };
        method.Parameters.Add(new ParameterBinding(
            "parameter:ImFontAtlas::GetMouseCursorTexData:out_uv_border",
            "out_uv_border",
            "outUvBorder",
            BindingType.CreateHandle("ImVec2", "global::Urho3D.Math.Vector2").WithFixedArray(BindingType.CreateHandle("ImVec2", "global::Urho3D.Math.Vector2"), 2),
            cppTypeName: "ImVec2[2]"));
        atlas.Members.Add(method);
        compilation.Members.Add(atlas);

        RbfxImGuiSurfacePass.Apply(compilation);

        Assert.True(method.IsIgnored());
        Assert.True(method.IsSilentlyExcluded());
    }

    [Fact]
    public void ImGui_pass_removes_fixed_array_instantiations_for_configured_value_types()
    {
        var compilation = CreateCompilation();
        var array = new RecordBinding("record:NativeArray", "NativeArray", "NativeArray", "NativeArray");
        array.SupportedGenericInstantiations.Add(new RecordGenericInstantiationBinding(
        [
            BindingType.CreateHandle("ImVec2", "global::Urho3D.Math.Vector2"),
            BindingType.CreateConfigured("2", "2")
        ]));
        compilation.Members.Add(array);

        RbfxImGuiSurfacePass.Apply(compilation);

        Assert.Empty(array.SupportedGenericInstantiations);
    }

    [Fact]
    public void ImGui_pass_removes_bool_fixed_array_instantiations()
    {
        var compilation = CreateCompilation();
        var array = new RecordBinding("record:NativeArray", "NativeArray", "NativeArray", "NativeArray");
        array.SupportedGenericInstantiations.Add(new RecordGenericInstantiationBinding(
        [
            BindingType.Boolean,
            BindingType.CreateConfigured("5", "5")
        ]));
        compilation.Members.Add(array);

        RbfxImGuiSurfacePass.Apply(compilation);

        Assert.Empty(array.SupportedGenericInstantiations);
    }

    private static BindingCompilation CreateCompilation()
        => new("SharpCpp.Rbfx.Bindings", "Urho3D", "Bindings", "Native", ["Urho3D/Urho3DAll.h"]);
}
