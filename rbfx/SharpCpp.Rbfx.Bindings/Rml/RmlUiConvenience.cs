using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using SharpCpp.Support;

namespace Urho3D.RmlUI
{
public partial class RmlUI
{
    public Rml.Context? RmlContext
    {
        get
        {
            var handle = Rml.__RmlUiConveniencePInvoke.GetRmlContext(NativeObject.GetHandle(this));
            return handle == 0 ? null : Rml.Context.CreateManagedWrapper(handle, ownsHandle: false);
        }
    }

    public bool IsHoveredValue => IsHovered();

    public bool LoadFont(Ea.String resourceName, bool fallback = false)
    {
        ArgumentNullException.ThrowIfNull(resourceName);
        return Rml.__RmlUiConveniencePInvoke.LoadFont(NativeObject.GetHandle(this), NativeObject.GetHandle(resourceName), fallback);
    }
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

    public ElementDocument? LoadDocumentFromMemory(Ea.String documentRml, Ea.String sourceUrl)
    {
        ArgumentNullException.ThrowIfNull(documentRml);
        ArgumentNullException.ThrowIfNull(sourceUrl);
        var handle = __RmlUiConveniencePInvoke.LoadDocumentFromMemory(
            NativeObject.GetHandle(this),
            NativeObject.GetHandle(documentRml),
            NativeObject.GetHandle(sourceUrl));
        return handle == 0 ? null : ElementDocument.CreateManagedWrapper(handle, ownsHandle: false);
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
    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_RmlUI_LoadFont", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool LoadFont(nint self, nint resourceName, [MarshalAs(UnmanagedType.I1)] bool fallback);

    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_RmlUI_GetRmlContext", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial nint GetRmlContext(nint self);

    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_Rml_Context_CreateDataModel", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial nint CreateDataModel(nint self, nint name);

    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_Rml_Context_LoadDocumentFromMemory", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial nint LoadDocumentFromMemory(nint self, nint documentRml, nint sourceUrl);

    [LibraryImport("SharpCpp.Rbfx.Native", EntryPoint = "sharpcpp_rbfx_Rml_ElementDocument_Show", StringMarshalling = StringMarshalling.Utf8)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial void ShowDocument(nint self);
}
}
