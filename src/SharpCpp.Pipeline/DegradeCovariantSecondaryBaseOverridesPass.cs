using SharpCpp.Model.Ast;

namespace SharpCpp.Pipeline;

/// <summary>
/// Degrades an override whose return type is covariant with a virtual declared in a secondary
/// base. The managed interface for that base exposes the base return type, and the secondary base
/// is not a managed base of the covariant return type, so no C# member can satisfy both: the
/// override is ignored and the inheritance pass projects the base declaration in its place.
/// Primary-chain covariant overrides are left alone because C# class covariance handles them.
/// </summary>
public static class DegradeCovariantSecondaryBaseOverridesPass
{
    public static void Apply(BindingCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var functionsByDeclarationId = new Dictionary<string, FunctionBinding>(StringComparer.Ordinal);
        foreach (var record in compilation.Records)
        {
            foreach (var function in record.Members.OfType<FunctionBinding>())
            {
                if (!string.IsNullOrWhiteSpace(function.NativeDeclarationId))
                {
                    functionsByDeclarationId.TryAdd(function.NativeDeclarationId, function);
                }
            }
        }

        foreach (var record in compilation.Records)
        {
            if (record.IsIgnored())
            {
                continue;
            }

            var reach = BuildSecondaryBaseReach(compilation, record);
            if (reach.Count == 0)
            {
                continue;
            }

            foreach (var method in record.Members.OfType<FunctionBinding>().ToArray())
            {
                if (!method.IsVirtual || !method.IsOverride || method.IsIgnored())
                {
                    continue;
                }

                var baseMethod = FindCovariantSecondaryBaseMethod(method, reach, functionsByDeclarationId);
                if (baseMethod is null)
                {
                    continue;
                }

                method.SetIgnoreReason(
                    $"overrides secondary-base virtual '{baseMethod.CppQualifiedName}' with a covariant return type ('{method.CppReturnTypeName}' instead of '{baseMethod.CppReturnTypeName}'); the managed interface exposes the base return type, so the base declaration is bound in its place");
                method.SetMetadata(MetadataKeys.CovariantSecondaryBaseOverride, true);
            }
        }
    }

    private static HashSet<RecordBinding> BuildSecondaryBaseReach(BindingCompilation compilation, RecordBinding record)
    {
        var reach = new HashSet<RecordBinding>(ReferenceEqualityComparer.Instance);
        foreach (var secondaryBase in record.GetSecondaryBaseRecords(compilation))
        {
            reach.Add(secondaryBase);
            foreach (var baseRecord in secondaryBase.EnumerateAllBaseRecords(compilation))
            {
                reach.Add(baseRecord);
            }
        }

        return reach;
    }

    private static FunctionBinding? FindCovariantSecondaryBaseMethod(
        FunctionBinding method,
        HashSet<RecordBinding> reach,
        Dictionary<string, FunctionBinding> functionsByDeclarationId)
    {
        var pending = new Stack<FunctionBinding>();
        var visited = new HashSet<FunctionBinding>(ReferenceEqualityComparer.Instance);
        pending.Push(method);
        visited.Add(method);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var declarationId in current.OverriddenNativeDeclarationIds)
            {
                // Ignored functions stay in the walk: an ignored intermediate override still links
                // the chain to the base virtual.
                if (!functionsByDeclarationId.TryGetValue(declarationId, out var overridden) || !visited.Add(overridden))
                {
                    continue;
                }

                pending.Push(overridden);

                var owner = overridden.FindOwningRecord();
                if (owner is not null &&
                    reach.Contains(owner) &&
                    IsInterfaceMember(overridden) &&
                    !ReturnTypesMatch(method.ReturnType, overridden.ReturnType))
                {
                    return overridden;
                }
            }
        }

        return null;
    }

    private static bool IsInterfaceMember(FunctionBinding function) =>
        function.AccessModifier == BindingAccessModifier.Public && !function.IsStatic && !function.IsIgnored();

    private static bool ReturnTypesMatch(BindingType left, BindingType right) =>
        left.Kind == right.Kind &&
        string.Equals(left.RecordId, right.RecordId, StringComparison.Ordinal) &&
        string.Equals(left.ManagedTypeName, right.ManagedTypeName, StringComparison.Ordinal);
}
