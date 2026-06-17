using SharpCpp.Model.Ast;

namespace SharpCpp.Rbfx.Generator;

public static class RbfxHandwrittenManagedTypeSuppressionPass
{
    private static readonly HashSet<string> SuppressedManagedTypes = new(StringComparer.Ordinal)
    {
        "Urho3D.Math.Intersection"
    };

    public static int Apply(BindingCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var projection = compilation.GetMetadata<ManagedBindingProjection>(MetadataKeys.ManagedBindingProjection);
        var suppressed = 0;

        foreach (var record in compilation.Records.Where(static record => !record.IsIgnored() && !record.GetMetadata<bool>(MetadataKeys.SuppressEmission)))
        {
            var managedNamespace = projection?.GetManagedNamespace(record, compilation) ?? record.ManagedNamespace ?? compilation.ManagedNamespace;
            if (SuppressedManagedTypes.Contains($"{managedNamespace}.{record.ManagedName}"))
            {
                record.SetMetadata(MetadataKeys.SuppressEmission, true);
                record.SetIgnoreReason("managed type is provided by handwritten rbfx binding source", overwriteExisting: false);
                suppressed++;
            }
        }

        foreach (var enumBinding in compilation.Enums.Where(static enumBinding => !enumBinding.IsIgnored() && !enumBinding.GetMetadata<bool>(MetadataKeys.SuppressEmission)))
        {
            var managedNamespace = projection?.GetManagedNamespace(enumBinding, compilation) ?? enumBinding.ManagedNamespace ?? compilation.ManagedNamespace;
            if (SuppressedManagedTypes.Contains($"{managedNamespace}.{enumBinding.ManagedName}"))
            {
                enumBinding.SetMetadata(MetadataKeys.SuppressEmission, true);
                enumBinding.SetIgnoreReason("managed type is provided by handwritten rbfx binding source", overwriteExisting: false);
                suppressed++;
            }
        }

        return suppressed;
    }
}
