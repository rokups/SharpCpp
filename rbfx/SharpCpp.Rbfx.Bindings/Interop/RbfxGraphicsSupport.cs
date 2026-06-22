using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using SharpCpp.Support;
using Urho3D.Core;
using Urho3D.Math;
using Urho3D.Scene;

namespace Urho3D.Scene
{
public partial class Node
{
    public Vector3 Position
    {
        get => GetPosition();
        set => SetPosition(value);
    }

    public Node? CreateChild(string? name = null)
    {
        var handle = __RbfxGraphicsPInvoke.NodeCreateChild(NativeObject.GetHandle(this), name ?? string.Empty);
        return handle == 0 ? null : CreateManagedWrapper(handle, ownsHandle: false);
    }

    public T? CreateComponent<T>(uint id = 0)
        where T : Component, global::SharpCpp.Rbfx.Bindings.Interop.IStaticTypeInfo<T>
    {
        return CreateComponent(T.GetTypeStatic(), id) as T;
    }
}
}

namespace Urho3D.Graphics
{
public partial class Renderer
{
    public void SetDefaultZoneFogColor(in Color color)
    {
        __RbfxGraphicsPInvoke.RendererSetDefaultZoneFogColor(NativeObject.GetHandle(this), color);
    }
}

public partial class Viewport
{
    public global::Urho3D.Scene.Scene? Scene
    {
        get
        {
            var handle = __RbfxGraphicsPInvoke.ViewportGetScene(NativeObject.GetHandle(this));
            return handle == 0 ? null : Urho3D.Scene.Scene.CreateManagedWrapper(handle, ownsHandle: false);
        }
        set => __RbfxGraphicsPInvoke.ViewportSetScene(NativeObject.GetHandle(this), NativeObject.GetHandle(value));
    }

    public Camera? Camera
    {
        get
        {
            var handle = __RbfxGraphicsPInvoke.ViewportGetCamera(NativeObject.GetHandle(this));
            return handle == 0 ? null : Camera.CreateManagedWrapper(handle, ownsHandle: false);
        }
        set => __RbfxGraphicsPInvoke.ViewportSetCamera(NativeObject.GetHandle(this), NativeObject.GetHandle(value));
    }
}
}

namespace Urho3D
{
internal static partial class __RbfxGraphicsPInvoke
{
    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_Node_CreateChild", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial nint NodeCreateChild(nint self, string name);

    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_Viewport_GetScene", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial nint ViewportGetScene(nint self);

    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_Viewport_SetScene", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial void ViewportSetScene(nint self, nint scene);

    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_Viewport_GetCamera", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial nint ViewportGetCamera(nint self);

    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_Viewport_SetCamera", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial void ViewportSetCamera(nint self, nint camera);

    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_Renderer_SetDefaultZoneFogColor", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial void RendererSetDefaultZoneFogColor(nint self, in Color color);
}
}
