using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using SharpCpp.Support;

namespace Urho3D.Engine;

public static class EngineDefs
{
    public const string Borderless = "Borderless";
    public const string EngineAutoLoadScripts = "EngineAutoLoadScripts";
    public const string FullScreen = "FullScreen";
    public const string ResourcePrefixPaths = "ResourcePrefixPaths";
    public const string WindowHeight = "WindowHeight";
    public const string WindowTitle = "WindowTitle";
    public const string WindowWidth = "WindowWidth";
}

public unsafe partial class Application
{
    private static readonly object s_updateCallbackRootsSync = new();
    private static readonly List<GCHandle> s_updateCallbackRoots = new();

    public void SubscribeToUpdate(Action<float> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var handle = GCHandle.Alloc(handler);
        __RbfxApplicationPInvoke.SubscribeToUpdate(
            NativeObject.GetHandle(this),
            (nint)(delegate* unmanaged<nint, float, void>)&InvokeUpdateCallback,
            GCHandle.ToIntPtr(handle));

        lock (s_updateCallbackRootsSync)
        {
            s_updateCallbackRoots.Add(handle);
        }
    }

    protected void SetEngineParameter(string name, bool value)
    {
        ArgumentNullException.ThrowIfNull(name);
        __RbfxApplicationPInvoke.SetEngineParameterBool(NativeObject.GetHandle(this), name, value);
    }

    protected void SetEngineParameter(string name, int value)
    {
        ArgumentNullException.ThrowIfNull(name);
        __RbfxApplicationPInvoke.SetEngineParameterInt(NativeObject.GetHandle(this), name, value);
    }

    protected void SetEngineParameter(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(name);
        __RbfxApplicationPInvoke.SetEngineParameterString(NativeObject.GetHandle(this), name, value);
    }

    [UnmanagedCallersOnly]
    private static void InvokeUpdateCallback(nint context, float timeStep)
    {
        var handler = (Action<float>)GCHandle.FromIntPtr(context).Target!;
        handler(timeStep);
    }
}

internal static partial class __RbfxApplicationPInvoke
{
    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_Application_SubscribeToUpdate", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial void SubscribeToUpdate(nint self, nint callback, nint context);

    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_Application_SetEngineParameterBool", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial void SetEngineParameterBool(nint self, string name, [MarshalAs(UnmanagedType.I1)] bool value);

    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_Application_SetEngineParameterInt", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial void SetEngineParameterInt(nint self, string name, int value);

    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_Application_SetEngineParameterString", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial void SetEngineParameterString(nint self, string name, string value);
}
