using SharpCpp.Support;

namespace Urho3D.Graphics;

public partial class Renderer
{
    public void SetDefaultZoneFogColor(in global::Urho3D.Math.Color color)
    {
        __RbfxGraphicsPInvoke.RendererSetDefaultZoneFogColor(NativeObject.GetHandle(this), color);
    }
}

internal static partial class __RbfxGraphicsPInvoke
{
    [System.Runtime.InteropServices.LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_Renderer_SetDefaultZoneFogColor", StringMarshalling = System.Runtime.InteropServices.StringMarshalling.Utf8)]
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal static partial void RendererSetDefaultZoneFogColor(nint self, in global::Urho3D.Math.Color color);
}
