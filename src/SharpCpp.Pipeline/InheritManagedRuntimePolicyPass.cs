using SharpCpp.Model.Ast;

namespace SharpCpp.Pipeline;

public static class InheritManagedRuntimePolicyPass
{
    public static void Apply(BindingCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        foreach (var record in compilation.Records)
        {
            for (var currentBase = record.GetPrimaryBaseRecord(compilation); currentBase is not null; currentBase = currentBase.GetPrimaryBaseRecord(compilation))
            {
                if (string.IsNullOrWhiteSpace(record.ManagedInstanceCacheFactoryExpression) &&
                    !string.IsNullOrWhiteSpace(currentBase.ManagedInstanceCacheFactoryExpression))
                {
                    record.ManagedInstanceCacheFactoryExpression = currentBase.ManagedInstanceCacheFactoryExpression;
                }

                if (string.IsNullOrWhiteSpace(record.ManagedInstanceCacheTypeName) &&
                    !string.IsNullOrWhiteSpace(currentBase.ManagedInstanceCacheTypeName))
                {
                    record.ManagedInstanceCacheTypeName = currentBase.ManagedInstanceCacheTypeName;
                }

                if (!currentBase.EmitDefaultNativeReleaseOverride)
                {
                    record.EmitDefaultNativeReleaseOverride = false;
                }
            }
        }
    }
}
