using System.Runtime.InteropServices;

namespace SharpCpp.Support;

// The native side allocates the same memory with SharpCpp::AllocateTaskMemory, so both sides share
// one process-wide heap even when the managed runtime and the native module use different CRTs.
public static unsafe class IndirectValueMarshaller<T> where T : unmanaged
{
    public static nint ConvertToUnmanaged(T managed)
    {
        var unmanaged = (T*)Marshal.AllocCoTaskMem(sizeof(T));
        *unmanaged = managed;
        return (nint)unmanaged;
    }

    public static T ConvertToManaged(nint unmanaged)
    {
        if (unmanaged == 0)
            throw new InvalidOperationException("SharpCpp indirect value ABI cannot be null.");

        return *(T*)unmanaged;
    }

    public static void Free(nint unmanaged) => Marshal.FreeCoTaskMem(unmanaged);
}
