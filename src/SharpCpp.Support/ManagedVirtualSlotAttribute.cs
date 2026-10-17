namespace SharpCpp.Support;

/// <summary>
/// Marks a generated virtual method with the stable key of its virtual slot.
/// The key has the form <c>{OwnerManagedType}.{ManagedName}({parameter managed types})</c>.
/// A source generator reads the key to emit <c>NativeObject.IsManagedOverride</c> for user overrides.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ManagedVirtualSlotAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance with the slot key.
    /// </summary>
    /// <param name="key">The stable key of the virtual slot.</param>
    public ManagedVirtualSlotAttribute(string key)
    {
        Key = key;
    }

    /// <summary>
    /// Gets the stable key of the virtual slot.
    /// </summary>
    public string Key { get; }
}
