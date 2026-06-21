using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using SharpCpp.Support;

public delegate void SharpCppCallback_BindFuncGetFunc(Rml.Variant value);
public delegate void SharpCppCallback_BindFuncSetFunc(Rml.Variant value);
public delegate void SharpCppCallback_BindEventCallbackEventFunc(Rml.DataModelHandle handle, Rml.Event @event, Rml.VariantList arguments);

namespace Rml
{

public unsafe partial class DataModelConstructor
{
    private static readonly object s_callbackRootsSync = new();
    private static readonly List<GCHandle> s_callbackRoots = new();

    public bool BindFunc(Ea.String name, SharpCppCallback_BindFuncGetFunc getFunc)
        => BindFunc(name, getFunc, null);

    public bool BindFunc(Ea.String name, SharpCppCallback_BindFuncGetFunc getFunc, SharpCppCallback_BindFuncSetFunc? setFunc)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(getFunc);

        var state = new BindFuncState(getFunc, setFunc);
        var handle = GCHandle.Alloc(state);
        var success = __RmlDataModelPInvoke.BindFunc(
            NativeObject.GetHandle(this),
            NativeObject.GetHandle(name),
            (nint)(delegate* unmanaged<nint, nint, void>)&InvokeBindFuncGet,
            setFunc is null ? 0 : (nint)(delegate* unmanaged<nint, nint, void>)&InvokeBindFuncSet,
            GCHandle.ToIntPtr(handle));

        RootOrRelease(handle, success);
        return success;
    }

    public bool BindEventCallback(Ea.String name, SharpCppCallback_BindEventCallbackEventFunc eventFunc)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(eventFunc);

        var handle = GCHandle.Alloc(eventFunc);
        var success = __RmlDataModelPInvoke.BindEventCallback(
            NativeObject.GetHandle(this),
            NativeObject.GetHandle(name),
            (nint)(delegate* unmanaged<nint, nint, nint, nint, void>)&InvokeBindEventCallback,
            GCHandle.ToIntPtr(handle));

        RootOrRelease(handle, success);
        return success;
    }

    public DataModelHandle? GetModelHandle()
    {
        var handle = __RmlDataModelPInvoke.GetModelHandle(NativeObject.GetHandle(this));
        return handle == 0 ? null : DataModelHandle.CreateManagedWrapper(handle, ownsHandle: true);
    }

    private static void RootOrRelease(GCHandle handle, bool success)
    {
        if (!success)
        {
            handle.Free();
            return;
        }

        lock (s_callbackRootsSync)
        {
            s_callbackRoots.Add(handle);
        }
    }

    [UnmanagedCallersOnly]
    private static void InvokeBindFuncGet(nint context, nint variant)
    {
        var state = (BindFuncState)GCHandle.FromIntPtr(context).Target!;
        state.GetFunc(Variant.CreateManagedWrapper(variant, ownsHandle: false));
    }

    [UnmanagedCallersOnly]
    private static void InvokeBindFuncSet(nint context, nint variant)
    {
        var state = (BindFuncState)GCHandle.FromIntPtr(context).Target!;
        state.SetFunc?.Invoke(Variant.CreateManagedWrapper(variant, ownsHandle: false));
    }

    [UnmanagedCallersOnly]
    private static void InvokeBindEventCallback(nint context, nint handle, nint @event, nint arguments)
    {
        var eventFunc = (SharpCppCallback_BindEventCallbackEventFunc)GCHandle.FromIntPtr(context).Target!;
        using var modelHandle = DataModelHandle.CreateManagedWrapper(handle, ownsHandle: true);
        eventFunc(
            modelHandle,
            Event.CreateManagedWrapper(@event, ownsHandle: false),
            VariantList.CreateManagedWrapper(arguments, ownsHandle: false));
    }

    private sealed record BindFuncState(SharpCppCallback_BindFuncGetFunc GetFunc, SharpCppCallback_BindFuncSetFunc? SetFunc);
}

public partial class DataModelHandle
{
    public bool IsVariableDirty(string variableName)
    {
        ArgumentNullException.ThrowIfNull(variableName);
        var bytes = Encoding.UTF8.GetBytes(variableName);
        return __RmlDataModelPInvoke.IsVariableDirty(NativeObject.GetHandle(this), bytes, (nuint)bytes.Length);
    }

    public void DirtyVariable(string variableName)
    {
        ArgumentNullException.ThrowIfNull(variableName);
        var bytes = Encoding.UTF8.GetBytes(variableName);
        __RmlDataModelPInvoke.DirtyVariable(NativeObject.GetHandle(this), bytes, (nuint)bytes.Length);
    }
}

public partial class Variant
{
    public void Set(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var bytes = Encoding.UTF8.GetBytes(value);
        __RmlDataModelPInvoke.VariantSetString(NativeObject.GetHandle(this), bytes, (nuint)bytes.Length);
    }

    public Ea.String GetString()
    {
        var handle = __RmlDataModelPInvoke.VariantGetString(NativeObject.GetHandle(this));
        return Ea.String.CreateManagedWrapper(handle, ownsHandle: true);
    }
}

public sealed class VariantList : NativeObject
{
    private VariantList(nint handle, bool ownsHandle)
        : base(handle, ownsHandle)
    {
    }

    public int Count => checked((int)__RmlDataModelPInvoke.VariantListCount(NativeObject.GetHandle(this)));

    public Variant this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            var handle = __RmlDataModelPInvoke.VariantListGet(NativeObject.GetHandle(this), (nuint)index);
            if (handle == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return Variant.CreateManagedWrapper(handle, ownsHandle: false);
        }
    }

    public static VariantList CreateManagedWrapper(nint handle, bool ownsHandle)
        => new(handle, ownsHandle);
}

internal static unsafe partial class __RmlDataModelPInvoke
{
    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rml_DataModelConstructor_BindFunc", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool BindFunc(nint self, nint name, nint getFunc, nint setFunc, nint context);

    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rml_DataModelConstructor_BindEventCallback", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool BindEventCallback(nint self, nint name, nint eventFunc, nint context);

    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rml_DataModelConstructor_GetModelHandle", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial nint GetModelHandle(nint self);

    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rml_DataModelHandle_IsVariableDirty", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool IsVariableDirty(nint self, ReadOnlySpan<byte> variableName, nuint variableNameLength);

    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rml_DataModelHandle_DirtyVariable", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial void DirtyVariable(nint self, ReadOnlySpan<byte> variableName, nuint variableNameLength);

    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rml_Variant_SetString", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial void VariantSetString(nint self, ReadOnlySpan<byte> value, nuint valueLength);

    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rml_Variant_GetString", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial nint VariantGetString(nint self);

    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rml_VariantList_Count", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial nuint VariantListCount(nint self);

    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rml_VariantList_Get", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial nint VariantListGet(nint self, nuint index);
}
}
