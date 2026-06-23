using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using SharpCpp.Support;

namespace Urho3D.RmlUI
{
public partial class RmlUI
{
}
}

namespace Rml
{
public partial class Context
{
    public DataModelConstructor? CreateDataModel(Ea.String name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var handle = __RmlUiConveniencePInvoke.CreateDataModel(NativeObject.GetHandle(this), NativeObject.GetHandle(name));
        return handle == 0 ? null : DataModelConstructor.CreateManagedWrapper(handle, ownsHandle: true);
    }

}

public partial class ElementDocument
{
    public void Show()
    {
        __RmlUiConveniencePInvoke.ShowDocument(NativeObject.GetHandle(this));
    }
}

internal static partial class __RmlUiConveniencePInvoke
{
    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_Rml_Context_CreateDataModel", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial nint CreateDataModel(nint self, nint name);

    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_Rml_ElementDocument_Show", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial void ShowDocument(nint self);
}
}
