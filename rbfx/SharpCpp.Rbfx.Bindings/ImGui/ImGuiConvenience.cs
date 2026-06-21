using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Urho3D.Math;

namespace ImGuiNet;

public static partial class ImGui
{
    public static bool Begin(ReadOnlySpan<byte> name, ImGuiWindowFlags flags = 0)
    {
        var open = true;
        return Begin(name, out open, flags);
    }

    public static void SetNextWindowPos(in Vector2 pos, ImGuiCond cond = 0)
    {
        __ImGuiConveniencePInvoke.SetNextWindowPos(pos, (int)cond);
    }

    public static void PushTextWrapPos(float wrapLocalPosX = 0.0f)
    {
        __ImGuiConveniencePInvoke.PushTextWrapPos(wrapLocalPosX);
    }

    public static void PopTextWrapPos()
    {
        __ImGuiConveniencePInvoke.PopTextWrapPos();
    }

    public static bool Button(ReadOnlySpan<byte> label)
    {
        return __ImGuiConveniencePInvoke.Button(label);
    }

    public static bool BeginItemTooltip()
    {
        return __ImGuiConveniencePInvoke.BeginItemTooltip();
    }

    public static void EndTooltip()
    {
        __ImGuiConveniencePInvoke.EndTooltip();
    }
}

internal static partial class __ImGuiConveniencePInvoke
{
    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_ImGui_SetNextWindowPos", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial void SetNextWindowPos(in Vector2 pos, int cond);

    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_ImGui_PushTextWrapPos", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial void PushTextWrapPos(float wrapLocalPosX);

    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_ImGui_PopTextWrapPos", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial void PopTextWrapPos();

    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_ImGui_Button", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool Button(ReadOnlySpan<byte> label);

    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_ImGui_BeginItemTooltip", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool BeginItemTooltip();

    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_ImGui_EndTooltip", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial void EndTooltip();
}
