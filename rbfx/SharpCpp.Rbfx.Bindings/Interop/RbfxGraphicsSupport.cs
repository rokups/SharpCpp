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

    public Component? CreateComponent(StringHash type, uint id = 0)
    {
        var handle = __RbfxGraphicsPInvoke.NodeCreateComponent(NativeObject.GetHandle(this), type, id);
        return handle == 0 ? null : Component.CreateManagedWrapper(handle, ownsHandle: false);
    }
}

public partial class Scene
{
    public Scene(Context context)
        : this(__RbfxGraphicsPInvoke.SceneCreate(NativeObject.GetHandle(context)), ownsHandle: true)
    {
    }
}
}

namespace Urho3D.Graphics
{
public partial class Renderer
{
    public void SetViewport(uint index, Viewport? viewport)
    {
        __RbfxGraphicsPInvoke.RendererSetViewport(NativeObject.GetHandle(this), index, NativeObject.GetHandle(viewport));
    }

    public void SetDefaultZoneFogColor(in Color color)
    {
        __RbfxGraphicsPInvoke.RendererSetDefaultZoneFogColor(NativeObject.GetHandle(this), color);
    }
}

public partial class StaticModel
{
    public void SetModel(Model? model)
    {
        __RbfxGraphicsPInvoke.StaticModelSetModel(NativeObject.GetHandle(this), NativeObject.GetHandle(model));
    }

    public bool SetMaterial(uint index, Material? material)
    {
        return __RbfxGraphicsPInvoke.StaticModelSetMaterial(NativeObject.GetHandle(this), index, NativeObject.GetHandle(material));
    }
}

public partial class Viewport
{
    public Viewport(Context context)
        : this(__RbfxGraphicsPInvoke.ViewportCreate(NativeObject.GetHandle(context)), ownsHandle: true)
    {
    }

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

namespace Urho3D.Resource
{
public partial class ResourceCache
{
    public T? GetResource<T>(string name, bool sendEventOnFailure = true)
        where T : global::Urho3D.Core.Object, global::SharpCpp.Rbfx.Bindings.Interop.IStaticTypeInfo<T>
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        var handle = __RbfxGraphicsPInvoke.ResourceCacheGetResource(
            NativeObject.GetHandle(this),
            T.GetTypeStatic(),
            name,
            sendEventOnFailure);
        return handle == 0 ? null : global::Urho3D.Core.Object.CreateManagedWrapper(handle, ownsHandle: false) as T;
    }
}
}

namespace Urho3D
{
internal static partial class __RbfxGraphicsPInvoke
{
    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_Scene_Create", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial nint SceneCreate(nint context);

    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_Node_CreateChild", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial nint NodeCreateChild(nint self, string name);

    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_Node_CreateComponent", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial nint NodeCreateComponent(nint self, StringHash type, uint id);

    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_Viewport_Create", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial nint ViewportCreate(nint context);

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

    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_ResourceCache_GetResource", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial nint ResourceCacheGetResource(nint self, StringHash type, string name, [MarshalAs(UnmanagedType.I1)] bool sendEventOnFailure);

    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_Renderer_SetViewport", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial void RendererSetViewport(nint self, uint index, nint viewport);

    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_Renderer_SetDefaultZoneFogColor", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial void RendererSetDefaultZoneFogColor(nint self, in Color color);

    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_StaticModel_SetModel", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial void StaticModelSetModel(nint self, nint model);

    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_StaticModel_SetMaterial", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool StaticModelSetMaterial(nint self, uint index, nint material);
}
}
