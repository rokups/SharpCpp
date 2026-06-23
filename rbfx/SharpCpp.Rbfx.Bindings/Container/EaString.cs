using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Ea;

public partial class String
{
    public String(string? value)
        : this(__EaStringPInvoke.CreateString(), ownsHandle: true)
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

internal static partial class __EaStringPInvoke
{
    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_String_Create", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial nint CreateString();
}
