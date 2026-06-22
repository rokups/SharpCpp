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
