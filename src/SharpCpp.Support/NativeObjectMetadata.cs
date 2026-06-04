using System.Runtime.InteropServices;

namespace SharpCpp.Support;

[StructLayout(LayoutKind.Sequential)]
public readonly struct NativeObjectMetadata
{
    public NativeObjectMetadata(nint handle, nuint dynamicTypeId)
    {
        Handle = handle;
        DynamicTypeId = dynamicTypeId;
    }

    public nint Handle { get; }

    public nuint DynamicTypeId { get; }

    public NativeObjectMetadata Normalize(nint fallbackHandle)
    {
        return Handle != IntPtr.Zero
            ? this
            : new NativeObjectMetadata(fallbackHandle, DynamicTypeId);
    }
}
