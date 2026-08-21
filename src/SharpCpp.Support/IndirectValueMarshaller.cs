using System.Runtime.InteropServices;

namespace SharpCpp.Support;

public static unsafe class IndirectValueMarshaller<T> where T : unmanaged
{
    public static nint ConvertToUnmanaged(T managed)
    {
        var unmanaged = (T*)NativeMemory.Alloc((nuint)sizeof(T));
        *unmanaged = managed;
        return (nint)unmanaged;
    }

    public static T ConvertToManaged(nint unmanaged)
    {
        if (unmanaged == 0)
            throw new InvalidOperationException("SharpCpp indirect value ABI cannot be null.");

        return *(T*)unmanaged;
    }

    public static void Free(nint unmanaged) => NativeMemory.Free((void*)unmanaged);
}
