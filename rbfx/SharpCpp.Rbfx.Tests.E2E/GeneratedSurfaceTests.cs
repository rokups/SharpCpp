using Xunit;

namespace SharpCpp.Rbfx.Tests.E2E;

public sealed class GeneratedSurfaceTests
{
    [Fact]
    public void EventHandler_constructor_accepts_handler_function_callback()
    {
        var content = File.ReadAllText(Path.Combine(GetGeneratedDir(), "Core", "SharpCpp.Rbfx.Object.g.cs"));

        Assert.DoesNotContain("EventHandler(Object *receiver, HandlerFunction handler) // excluded", content, StringComparison.Ordinal);
        Assert.Contains("public EventHandler(global::Urho3D.Core.Object receiver, global::System.Action<global::Urho3D.Core.Object, global::Urho3D.Math.StringHash, VariantMap> handler)", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Spline_GetPoint_returns_Variant_wrapper()
    {
        var content = File.ReadAllText(Path.Combine(GetGeneratedDir(), "Core", "SharpCpp.Rbfx.Spline.g.cs"));

        Assert.DoesNotContain("Variant GetPoint(float f) const // excluded", content, StringComparison.Ordinal);
        Assert.Contains("public global::Urho3D.Core.Variant GetPoint(float f)", content, StringComparison.Ordinal);
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
}
