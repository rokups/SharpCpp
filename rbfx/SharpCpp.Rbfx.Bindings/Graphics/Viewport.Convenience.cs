using SharpCpp.Support;

namespace Urho3D.Graphics;

public partial class Viewport
{
    public global::Urho3D.Scene.Scene? Scene
    {
        get
        {
            var handle = __RbfxViewportPInvoke.GetScene(NativeObject.GetHandle(this));
            return handle == 0 ? null : global::Urho3D.Scene.Scene.CreateManagedWrapper(handle, ownsHandle: false);
        }
        set => __RbfxViewportPInvoke.SetScene(NativeObject.GetHandle(this), NativeObject.GetHandle(value));
    }

    public Camera? Camera
    {
        get
        {
            var handle = __RbfxViewportPInvoke.GetCamera(NativeObject.GetHandle(this));
            return handle == 0 ? null : Camera.CreateManagedWrapper(handle, ownsHandle: false);
        }
        set => __RbfxViewportPInvoke.SetCamera(NativeObject.GetHandle(this), NativeObject.GetHandle(value));
    }
}

internal static partial class __RbfxViewportPInvoke
{
    [System.Runtime.InteropServices.LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_Viewport_GetScene", StringMarshalling = System.Runtime.InteropServices.StringMarshalling.Utf8)]
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal static partial nint GetScene(nint self);

    [System.Runtime.InteropServices.LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_Viewport_SetScene", StringMarshalling = System.Runtime.InteropServices.StringMarshalling.Utf8)]
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal static partial void SetScene(nint self, nint scene);

    [System.Runtime.InteropServices.LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_Viewport_GetCamera", StringMarshalling = System.Runtime.InteropServices.StringMarshalling.Utf8)]
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal static partial nint GetCamera(nint self);

    [System.Runtime.InteropServices.LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_Viewport_SetCamera", StringMarshalling = System.Runtime.InteropServices.StringMarshalling.Utf8)]
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal static partial void SetCamera(nint self, nint camera);
}
