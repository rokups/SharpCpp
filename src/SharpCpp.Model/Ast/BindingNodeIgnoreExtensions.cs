namespace SharpCpp.Model.Ast;

public static class BindingNodeIgnoreExtensions
{
    public static bool IsIgnored(this BindingNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.HasMetadata(MetadataKeys.IgnoreReason);
    }

    public static string? GetIgnoreReason(this BindingNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.GetMetadata<string>(MetadataKeys.IgnoreReason);
    }

    public static void SetIgnoreReason(this BindingNode node, string reason, bool overwriteExisting = false)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (!overwriteExisting && node.HasMetadata(MetadataKeys.IgnoreReason))
        {
            return;
        }

        node.SetMetadata(MetadataKeys.IgnoreReason, reason);
    }

    public static IReadOnlyList<string> GetWarnings(this BindingNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.GetMetadata<IReadOnlyList<string>>(MetadataKeys.Warnings) ?? Array.Empty<string>();
    }

    public static void SetWarning(this BindingNode node, string warning)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentException.ThrowIfNullOrWhiteSpace(warning);

        var existing = node.GetMetadata<List<string>>(MetadataKeys.Warnings);
        if (existing is null)
        {
            existing = new List<string>();
            node.SetMetadata(MetadataKeys.Warnings, existing);
        }

        existing.Add(warning);
    }
}
