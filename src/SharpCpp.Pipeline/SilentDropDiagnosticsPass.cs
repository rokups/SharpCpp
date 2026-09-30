using SharpCpp.Model.Ast;

namespace SharpCpp.Pipeline;

/// <summary>
/// Records in the diagnostic log the renderer-side decisions that leave a declaration out of the
/// generated output without any other trace. The renderer must stay pure, so this pass evaluates
/// the same predicates on the final surface and attaches a warning to the affected field.
/// </summary>
/// <remarks>
/// Covers fields skipped because their managed name clashes with a method or accessor property of
/// the same record, and the counted-array accessor (<c>Get{Field}At</c>) that is not generated
/// when that name is already taken. Run it once the surface is final.
/// </remarks>
public static class SilentDropDiagnosticsPass
{
    public static void Apply(BindingCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        foreach (var record in compilation.DescendantsAndSelf<RecordBinding>().ToArray())
        {
            if (record.IsIgnored() || record.GetMetadata<bool>(MetadataKeys.SuppressEmission))
            {
                continue;
            }

            foreach (var field in record.Members.OfType<FieldBinding>())
            {
                if (field.IsIgnored())
                {
                    continue;
                }

                if (record.HasOwnManagedNonFieldMemberNamed(field.ManagedName))
                {
                    field.SetWarning($"field not rendered: managed name '{field.ManagedName}' clashes with a member of the same name");
                    continue;
                }

                var accessorName = $"Get{field.ManagedName}At";
                if (field.AccessModifier == BindingAccessModifier.Public &&
                    field.PointerDepth > 0 &&
                    record.HasOwnManagedNonFieldMemberNamed(accessorName) &&
                    HasCountFieldCandidate(record, field))
                {
                    field.SetWarning($"counted-array accessor '{accessorName}' not generated: the name clashes with a member of the same name");
                }
            }
        }
    }

    private static bool HasCountFieldCandidate(RecordBinding record, FieldBinding field) =>
        record.Members.OfType<FieldBinding>().Any(candidate =>
            !ReferenceEquals(candidate, field) &&
            !candidate.IsIgnored() &&
            (candidate.NativeName.StartsWith("Num", StringComparison.Ordinal) ||
             candidate.NativeName.StartsWith("count", StringComparison.Ordinal) ||
             candidate.NativeName.EndsWith("Count", StringComparison.Ordinal)));
}
