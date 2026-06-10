using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Urho3D.Math;

namespace Urho3D.Interop;

/// ABI struct for Vector3 return type.
/// Layout-compatible with C# Vector3 and C++ Urho3D::Vector3.
[StructLayout(LayoutKind.Sequential)]
public struct Vector3Abi
{
    public float X;
    public float Y;
    public float Z;
}

/// Custom marshaller that converts Vector3Abi ↔ Vector3 for return values.
/// Consumed by .NET 8+ LibraryImport source generator via [return: MarshalUsing].
[CustomMarshaller(typeof(Vector3), MarshalMode.ManagedToUnmanagedOut, typeof(Vector3ReturnMarshaller))]
public static class Vector3ReturnMarshaller
{
    public static Vector3Abi ConvertToUnmanaged(Vector3 managed)
    {
        return new Vector3Abi { X = managed.X, Y = managed.Y, Z = managed.Z };
    }

    public static Vector3 ConvertToManaged(Vector3Abi unmanaged)
    {
        return new Vector3(unmanaged.X, unmanaged.Y, unmanaged.Z);
    }
}
