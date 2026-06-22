namespace ImGuiNet;

public static partial class ImGui
{
    public static bool Begin(global::System.ReadOnlySpan<byte> name, ImGuiWindowFlags flags = 0)
    {
        var open = true;
        return Begin(name, out open, flags);
    }

    public static void SetNextWindowPos(in global::Urho3D.Math.Vector2 pos, ImGuiCond cond = 0)
    {
        var pivot = new global::Urho3D.Math.Vector2(0.0f, 0.0f);
        SetNextWindowPos(pos, cond, pivot);
    }

    public static bool Button(global::System.ReadOnlySpan<byte> label)
    {
        var size = new global::Urho3D.Math.Vector2(0.0f, 0.0f);
        return Button(label, size);
    }
}
