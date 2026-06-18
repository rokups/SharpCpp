using SharpCpp.Model.Ast;

namespace SharpCpp.Rbfx.Generator;

public static class RbfxSilentExclusionPass
{
    public static int Apply(BindingCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var count = 0;
        foreach (var record in compilation.Records)
        {
            if (record.GetMetadata<bool>(MetadataKeys.SuppressEmission))
            {
                continue;
            }

            if (TryGetSilentExclusionReason(record.CppQualifiedName, out var reason))
            {
                if (IsExplicitlyConfiguredRecord(record) && IsOutsideUrho3DNamespace(record.CppQualifiedName))
                {
                    continue;
                }

                record.SetSilentExclusionReason(reason);
                count++;
            }
        }

        foreach (var enumBinding in compilation.Enums)
        {
            if (TryGetSilentExclusionReason(enumBinding.CppQualifiedName, out var reason))
            {
                enumBinding.SetSilentExclusionReason(reason);
                count++;
            }
        }

        foreach (var function in compilation.Functions.Where(static function => function.Parent is not RecordBinding))
        {
            if (TryGetSilentExclusionReason(function.CppQualifiedName, out var reason))
            {
                function.SetSilentExclusionReason(reason);
                count++;
            }
        }

        return count;
    }

    private static bool IsExplicitlyConfiguredRecord(RecordBinding record)
    {
        return !string.IsNullOrWhiteSpace(record.ManagedBaseTypeName) ||
            !string.IsNullOrWhiteSpace(record.ManagedTypeFormat) ||
            !string.IsNullOrWhiteSpace(record.HandleMarshallerTypeName) ||
            record.Members.OfType<FunctionBinding>().Any(static member => member.HasMetadata(MetadataKeys.MarshallerExposedMethod));
    }

    private static bool IsOutsideUrho3DNamespace(string cppQualifiedName)
    {
        var parts = cppQualifiedName.Split("::", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length < 2 || !string.Equals(parts[0], "Urho3D", StringComparison.Ordinal);
    }

    private static bool TryGetSilentExclusionReason(string cppQualifiedName, out string reason)
    {
        if (string.IsNullOrWhiteSpace(cppQualifiedName))
        {
            reason = string.Empty;
            return false;
        }

        var parts = cppQualifiedName.Split("::", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            reason = $"declaration '{cppQualifiedName}' is outside the Urho3D namespace";
            return true;
        }

        if (!string.Equals(parts[0], "Urho3D", StringComparison.Ordinal))
        {
            reason = $"declaration namespace root '{parts[0]}' is outside the Urho3D namespace";
            return true;
        }

        if (parts.Skip(1).Take(parts.Length - 2).Any(static part =>
                string.Equals(part, "detail", StringComparison.Ordinal) ||
                string.Equals(part, "Detail", StringComparison.Ordinal)))
        {
            reason = $"declaration is in an internal rbfx detail namespace: '{cppQualifiedName}'";
            return true;
        }

        reason = string.Empty;
        return false;
    }
}
