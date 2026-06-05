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
}
