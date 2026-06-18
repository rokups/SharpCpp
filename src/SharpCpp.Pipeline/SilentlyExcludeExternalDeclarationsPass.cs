using SharpCpp.Model.Ast;

namespace SharpCpp.Pipeline;

public static class SilentlyExcludeExternalDeclarationsPass
{
    public static void Apply(BindingCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        foreach (var record in compilation.Records)
        {
            if (ShouldSilentlyExcludeRecord(record))
            {
                record.SetSilentExclusionReason($"record '{record.CppQualifiedName}' is defined outside headers owned by this compilation");
            }
        }
    }

    private static bool ShouldSilentlyExcludeRecord(RecordBinding record)
    {
        if (!record.GetMetadata<bool>(MetadataKeys.PlaceholderRecord))
        {
            return false;
        }

        if (record.GetMetadata<bool>(MetadataKeys.SuppressEmission))
        {
            return false;
        }

        return !IsExplicitlyConfiguredRecord(record);
    }

    private static bool IsExplicitlyConfiguredRecord(RecordBinding record)
    {
        return !string.IsNullOrWhiteSpace(record.ManagedBaseTypeName) ||
            !string.IsNullOrWhiteSpace(record.ManagedTypeFormat) ||
            !string.IsNullOrWhiteSpace(record.HandleMarshallerTypeName) ||
            record.Members.OfType<FunctionBinding>().Any(static member => member.HasMetadata(MetadataKeys.MarshallerExposedMethod));
    }
}
