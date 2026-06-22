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

    public static implicit operator String(string? value)
        => new(value);

    public static implicit operator global::System.ReadOnlySpan<byte>(String value)
    {
        global::System.ArgumentNullException.ThrowIfNull(value);
        return value.AsUtf8Span();
    }
}
}

namespace Urho3D.Core
{
public partial class Context
{
    public T? GetSubsystem<T>()
        where T : Object, global::SharpCpp.Rbfx.Bindings.Interop.IStaticTypeInfo<T>
    {
        return GetSubsystem(T.GetTypeStatic()) as T;
    }
}

public abstract partial class Object
{
    public T? GetSubsystem<T>()
        where T : Object, global::SharpCpp.Rbfx.Bindings.Interop.IStaticTypeInfo<T>
    {
        return GetSubsystem(T.GetTypeStatic()) as T;
    }
}

internal static partial class __RbfxCorePInvoke
{
    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_String_Create", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial nint CreateString();

    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_Context_GetSubsystem", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial nint ContextGetSubsystem(nint self, StringHash type);

    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_Object_GetSubsystem", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial nint ObjectGetSubsystem(nint self, StringHash type);

}
}
