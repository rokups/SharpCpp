using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using SharpCpp.Support;
using Urho3D.Core;
using Urho3D.Math;

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

public sealed class EngineParameterCollection
{
    private readonly Application _application;

    internal EngineParameterCollection(Application application)
    {
        _application = application;
    }

    public object? this[string name]
    {
        set
        {
            ArgumentNullException.ThrowIfNull(name);

            switch (value)
            {
                case bool boolValue:
                    __RbfxApplicationPInvoke.SetEngineParameterBool(NativeObject.GetHandle(_application), name, boolValue);
                    break;
                case int intValue:
                    __RbfxApplicationPInvoke.SetEngineParameterInt(NativeObject.GetHandle(_application), name, intValue);
                    break;
                case string stringValue:
                    __RbfxApplicationPInvoke.SetEngineParameterString(NativeObject.GetHandle(_application), name, stringValue);
                    break;
                default:
                    throw new NotSupportedException($"Engine parameter '{name}' does not support values of type '{value?.GetType().FullName ?? "null"}'.");
            }
        }
    }
}

public unsafe partial class Application
{
    private static readonly object s_updateCallbackRootsSync = new();
    private static readonly List<GCHandle> s_updateCallbackRoots = new();

    public Application(Context context)
        : this(__RbfxApplicationPInvoke.CreateApplication(NativeObject.GetHandle(context)), ownsHandle: true)
    {
        ConnectRbfxDirector();
    }

    public EngineParameterCollection EngineParameters => new(this);

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

    private void ConnectRbfxDirector()
    {
        var runtimeType = ((object)this).GetType();
        __RbfxApplicationPInvoke.ConnectApplicationDirector(
            DangerousGetHandle(),
            __GetApplication_GetTypePointer(runtimeType),
            __GetApplication_IsInstanceOfPointer(runtimeType),
            __GetApplication_SetupPointer(runtimeType),
            __GetApplication_StartPointer(runtimeType),
            __GetApplication_StopPointer(runtimeType));
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
    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_Application_Create", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial nint CreateApplication(nint context);

    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_Application_ConnectDirector", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial void ConnectApplicationDirector(nint self, nint getType, nint isInstanceOf, nint setup, nint start, nint stop);

    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_Application_SetEngineParameterBool", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial void SetEngineParameterBool(nint self, string name, [MarshalAs(UnmanagedType.I1)] bool value);

    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_Application_SetEngineParameterInt", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial void SetEngineParameterInt(nint self, string name, int value);

    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_Application_SetEngineParameterString", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial void SetEngineParameterString(nint self, string name, string value);

    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_Application_SubscribeToUpdate", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial void SubscribeToUpdate(nint self, nint callback, nint context);
}
