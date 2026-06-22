using Xunit;

namespace SharpCpp.Rbfx.Tests.E2E;

public sealed class GeneratedSurfaceTests
{
    [Fact]
    public void EventHandler_keeps_supported_surface_and_prunes_open_template_constructor()
    {
        var content = File.ReadAllText(Path.Combine(GetGeneratedDir(), "Core", "SharpCpp.Rbfx.Object.g.cs"));

        Assert.Contains("template <class T> EventHandler(Object *receiver, T handler) // excluded", content, StringComparison.Ordinal);
        Assert.Contains("public global::Urho3D.Math.StringHash GetEventType()", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Spline_keeps_current_supported_surface()
    {
        var content = File.ReadAllText(Path.Combine(GetGeneratedDir(), "Core", "SharpCpp.Rbfx.Spline.g.cs"));

        Assert.Contains("public global::Urho3D.Core.Variant GetPoint(float f)", content, StringComparison.Ordinal);
        Assert.Contains("public global::Urho3D.Core.InterpolationMode GetInterpolationMode()", content, StringComparison.Ordinal);
        Assert.Contains("public void SetInterpolationMode(global::Urho3D.Core.InterpolationMode interpolationMode)", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Context_generates_default_constructor_surface()
    {
        var managedContent = File.ReadAllText(Path.Combine(GetGeneratedDir(), "Core", "SharpCpp.Rbfx.Context.g.cs"));
        var nativeContent = File.ReadAllText(Path.Combine(GetGeneratedDir(), "Core", "SharpCpp.Rbfx.Context.g.cpp"));

        Assert.Contains("public Context()", managedContent, StringComparison.Ordinal);
        Assert.Contains("__PInvoke.Context()", managedContent, StringComparison.Ordinal);
        Assert.Contains("MarshallerInvoker::construct<Urho3D::Context>()", nativeContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Enabled_RmlUI_bindings_generate_rml_namespace_surface()
    {
        if (!GeneratorLogContains("RmlUI bindings: enabled"))
        {
            return;
        }

        var content = File.ReadAllText(Path.Combine(GetGeneratedDir(), "ThirdParty", "RmlUi", "SharpCpp.Rbfx.Core.ElementDocument.g.cs"));

        Assert.Contains("namespace Rml", content, StringComparison.Ordinal);
        Assert.Contains("public partial class ElementDocument", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Enabled_ImGui_bindings_generate_imguinet_surface()
    {
        if (!GeneratorLogContains("Dear ImGui bindings: enabled"))
        {
            return;
        }

        var content = File.ReadAllText(Path.Combine(GetGeneratedDir(), "ThirdParty", "ImGui", "SharpCpp.Rbfx.imgui.g.cs"));

        Assert.Contains("namespace ImGuiNet", content, StringComparison.Ordinal);
        Assert.Contains("public static partial class ImGui", content, StringComparison.Ordinal);
        Assert.Contains("public enum ImGuiCond", content, StringComparison.Ordinal);
        Assert.Contains("public static void SetNextWindowPos", content, StringComparison.Ordinal);
        Assert.Contains("public static void PushTextWrapPos", content, StringComparison.Ordinal);
        Assert.Contains("public static bool Button", content, StringComparison.Ordinal);
        Assert.Contains("public static bool BeginItemTooltip", content, StringComparison.Ordinal);
        Assert.Contains("public static void EndTooltip", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Variant_helpers_expose_v1_style_type_mapping()
    {
        Assert.Equal(global::Urho3D.Core.VariantType.VarInt, global::Urho3D.Core.Variant.GetVariantType(typeof(int)));
        Assert.Equal(global::Urho3D.Core.VariantType.VarBool, global::Urho3D.Core.Variant.GetVariantType(typeof(bool)));
        Assert.Equal(global::Urho3D.Core.VariantType.VarString, global::Urho3D.Core.Variant.GetVariantType(typeof(string)));
        Assert.Equal(global::Urho3D.Core.VariantType.VarVector3, global::Urho3D.Core.Variant.GetVariantType(typeof(global::Urho3D.Math.Vector3)));
        Assert.Equal(global::Urho3D.Core.VariantType.VarResourceref, global::Urho3D.Core.Variant.GetVariantType(typeof(global::Urho3D.Core.ResourceRef)));
        Assert.Equal(global::Urho3D.Core.VariantType.VarVariantvector, global::Urho3D.Core.Variant.GetVariantType(typeof(global::Ea.Vector<global::Urho3D.Core.Variant>)));
        Assert.Equal(global::Urho3D.Core.VariantType.VarStringvariantmap, global::Urho3D.Core.Variant.GetVariantType(typeof(global::Ea.UnorderedMap<global::Ea.String, global::Urho3D.Core.Variant>)));
        Assert.Equal(global::Urho3D.Core.VariantType.VarNone, global::Urho3D.Core.Variant.GetVariantType(typeof(GeneratedSurfaceTests)));
    }

    private static string GetGeneratedDir()
    {
        var dir = Environment.GetEnvironmentVariable("SharpCppGeneratedDir");
        if (string.IsNullOrWhiteSpace(dir))
        {
            dir = Path.Combine(
                Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".",
                "..", "..", "..", "..", "..",
                "out", "build", "dev", "generated", "rbfx");
        }

        return Path.GetFullPath(dir);
    }

    private static bool GeneratorLogContains(string value)
    {
        var logPath = Path.Combine(GetGeneratedDir(), "generator.log");
        return File.Exists(logPath) && File.ReadAllText(logPath).Contains(value, StringComparison.Ordinal);
    }
}
