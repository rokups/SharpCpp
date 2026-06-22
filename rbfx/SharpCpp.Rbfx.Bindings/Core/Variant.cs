namespace Urho3D.Core;

public partial class Variant
{
    public static implicit operator Variant(bool value)
        => new(value);

    public static implicit operator Variant(int value)
        => new(value);

    public static implicit operator Variant(string? value)
    {
        using var text = new global::Ea.String(value ?? string.Empty);
        return new Variant(text);
    }
}
