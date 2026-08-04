namespace Parser_breadth;

public partial class BreadthPayload
{
    public static implicit operator nint(BreadthPayload? value)
        => global::SharpCpp.Support.NativeObject.GetHandle(value);

    public static implicit operator BreadthPayload(nint value)
        => value == 0 ? null! : CreateManagedWrapper(value, ownsHandle: false);
}
