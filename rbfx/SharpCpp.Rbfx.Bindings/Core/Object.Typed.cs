namespace Urho3D.Core;

public partial class EventHandler
{
    internal void TransferToNativeOwner()
    {
        DisposeFromNativeRuntimeCore();
    }
}

public abstract partial class Object
{
    public T? GetSubsystem<T>()
        where T : Object, global::SharpCpp.Rbfx.Bindings.Interop.IStaticTypeInfo<T>
    {
        return GetSubsystem(T.GetTypeStatic()) as T;
    }

    public void SubscribeToEvent(
        global::Urho3D.Math.StringHash eventType,
        global::System.Action<global::Urho3D.Core.Object, global::Urho3D.Math.StringHash, VariantMap> handler)
    {
        global::System.ArgumentNullException.ThrowIfNull(handler);

        var eventHandler = new EventHandler(this, handler);
        SubscribeToEventManual(eventType, eventHandler);
        eventHandler.TransferToNativeOwner();
    }

    public void SubscribeToEvent(
        global::Urho3D.Core.Object sender,
        global::Urho3D.Math.StringHash eventType,
        global::System.Action<global::Urho3D.Core.Object, global::Urho3D.Math.StringHash, VariantMap> handler)
    {
        global::System.ArgumentNullException.ThrowIfNull(sender);
        global::System.ArgumentNullException.ThrowIfNull(handler);

        var eventHandler = new EventHandler(this, handler);
        SubscribeToEventManual(sender, eventType, eventHandler);
        eventHandler.TransferToNativeOwner();
    }
}
