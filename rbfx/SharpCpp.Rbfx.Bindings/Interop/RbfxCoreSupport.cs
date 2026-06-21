using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using SharpCpp.Support;
using Urho3D.Math;

namespace Ea
{
public partial class String
{
    public String(string? value)
        : this(Urho3D.Core.__RbfxCorePInvoke.CreateString(), ownsHandle: true)
    {
        Assign(value);
    }
}
}

namespace Urho3D.Core
{
public partial class Context
{
    public Context()
        : this(__RbfxCorePInvoke.CreateContext(), ownsHandle: true)
    {
    }

    public T? GetSubsystem<T>()
        where T : Object, global::SharpCpp.Rbfx.Bindings.Interop.IStaticTypeInfo<T>
    {
        var handle = __RbfxCorePInvoke.ContextGetSubsystem(NativeObject.GetHandle(this), T.GetTypeStatic());
        return handle == 0 ? null : Object.CreateManagedWrapper(handle, ownsHandle: false) as T;
    }
}

public abstract partial class Object
{
    public Context? Context
    {
        get
        {
            var handle = __RbfxCorePInvoke.ObjectGetContext(NativeObject.GetHandle(this));
            return handle == 0 ? null : Context.CreateManagedWrapper(handle, ownsHandle: false);
        }
    }

    public T? GetSubsystem<T>()
        where T : Object, global::SharpCpp.Rbfx.Bindings.Interop.IStaticTypeInfo<T>
    {
        var handle = __RbfxCorePInvoke.ObjectGetSubsystem(NativeObject.GetHandle(this), T.GetTypeStatic());
        return handle == 0 ? null : CreateManagedWrapper(handle, ownsHandle: false) as T;
    }
}

internal static partial class __RbfxCorePInvoke
{
    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_String_Create", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial nint CreateString();

    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_Context_Create", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial nint CreateContext();

    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_Context_GetSubsystem", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial nint ContextGetSubsystem(nint self, StringHash type);

    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_Object_GetSubsystem", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial nint ObjectGetSubsystem(nint self, StringHash type);

    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_Object_GetContext", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial nint ObjectGetContext(nint self);
}
}
