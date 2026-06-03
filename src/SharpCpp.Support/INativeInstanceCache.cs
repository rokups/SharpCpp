namespace SharpCpp.Support;

public interface INativeInstanceCache
{
    bool TryGetExisting(nint handle, bool ownsHandle, out NativeObject? instance);

    void Register(NativeObject instance);

    void Unregister(NativeObject instance);
}
