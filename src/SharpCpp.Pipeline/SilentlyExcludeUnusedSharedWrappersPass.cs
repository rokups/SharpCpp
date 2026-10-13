using SharpCpp.Model.Ast;

namespace SharpCpp.Pipeline;

/// <summary>
/// Silently excludes non-generic shared managed wrapper records (for example
/// <c>std::basic_string</c>) when nothing else in the compilation's surface refers to them.
/// Without this every module would emit the wrapper's providers, marshaller, P/Invokes and
/// native exports even if it never touches the type.
/// </summary>
/// <remarks>
/// A wrapper counts as referenced when a type reachable from any non-ignored declaration
/// (callables, fields, bases, generic instantiations, type aliases), other than the wrapper
/// itself, carries its record identity. Reachability is followed through generic arguments,
/// native template arguments, callback signatures and array element types. The rule is
/// deliberately conservative; run it once the surface is final.
/// </remarks>
public static class SilentlyExcludeUnusedSharedWrappersPass
{
    public static void Apply(BindingCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        MarkUsedImportedHandleRecords(compilation);

        var candidates = compilation.Records
            .Where(IsCandidate)
            .ToArray();
        if (candidates.Length == 0)
        {
            return;
        }

        var references = CollectReferences(compilation);
        foreach (var candidate in candidates)
        {
            var referenced = references.Any(reference =>
                string.Equals(reference.RecordId, candidate.SymbolId, StringComparison.Ordinal) &&
                !IsWithin(reference.Owner, candidate));
            if (!referenced)
            {
                candidate.SetSilentExclusionReason($"shared wrapper record '{candidate.CppQualifiedName}' is not used by this compilation");
            }
        }
    }

    /// <summary>
    /// Sets <see cref="MetadataKeys.ImportedHandleMarshaller"/> on each imported handle record
    /// that a callable or a field of this module uses as a handle type. The renderer emits a
    /// marshaller for these records only. Members of imported records do not count.
    /// </summary>
    private static void MarkUsedImportedHandleRecords(BindingCompilation compilation)
    {
        var importedById = compilation.Records
            .Where(static record => record.GetMetadata<bool>(MetadataKeys.ImportedHandleRecord))
            .GroupBy(static record => record.SymbolId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);
        if (importedById.Count == 0)
        {
            return;
        }

        var references = new List<Reference>();
        void Add(BindingType? type, BindingNode owner)
        {
            if (type is not null)
            {
                Walk(type, owner, references, new HashSet<BindingType>(ReferenceEqualityComparer.Instance));
            }
        }

        foreach (var function in compilation.Functions.Where(IsLocalActive))
        {
            Add(function.ReturnType, function);
            foreach (var parameter in function.Parameters)
            {
                Add(parameter.Type, function);
            }
        }

        foreach (var field in compilation.Descendants<FieldBinding>().Where(IsLocalActive))
        {
            Add(field.Type, field);
        }

        foreach (var reference in references)
        {
            if (importedById.TryGetValue(reference.RecordId, out var record))
            {
                record.SetMetadata(MetadataKeys.ImportedHandleMarshaller, true);
            }
        }
    }

    private static bool IsLocalActive(BindingNode node)
    {
        return IsActive(node) &&
            !node.Ancestors().Any(static ancestor =>
                ancestor is RecordBinding record && record.GetMetadata<bool>(MetadataKeys.SuppressEmission));
    }

    private static bool IsCandidate(RecordBinding record)
    {
        return !record.GetMetadata<bool>(MetadataKeys.SuppressEmission) &&
            !record.IsGeneric &&
            !record.IsIgnored() &&
            !string.IsNullOrWhiteSpace(record.GetMetadata<string>(MetadataKeys.ManagedWrapperAssembly));
    }

    private static bool IsWithin(BindingNode? owner, RecordBinding record)
    {
        return owner is not null && (ReferenceEquals(owner, record) || owner.Ancestors().Any(ancestor => ReferenceEquals(ancestor, record)));
    }

    private static bool IsActive(BindingNode node)
    {
        return !node.IsIgnored() && !node.Ancestors().Any(static ancestor => ancestor.IsIgnored());
    }

    private readonly record struct Reference(string RecordId, BindingNode? Owner);

    private static List<Reference> CollectReferences(BindingCompilation compilation)
    {
        var references = new List<Reference>();

        void Add(BindingType? type, BindingNode? owner)
        {
            if (type is not null)
            {
                Walk(type, owner, references, new HashSet<BindingType>(ReferenceEqualityComparer.Instance));
            }
        }

        foreach (var function in compilation.Functions.Where(IsActive))
        {
            Add(function.ReturnType, function);
            foreach (var parameter in function.Parameters)
            {
                Add(parameter.Type, function);
            }

            foreach (var instantiation in function.SupportedGenericInstantiations)
            {
                foreach (var argument in instantiation.TypeArguments)
                {
                    Add(argument, function);
                }
            }
        }

        foreach (var field in compilation.Descendants<FieldBinding>().Where(IsActive))
        {
            Add(field.Type, field);
        }

        foreach (var record in compilation.Records.Where(IsActive))
        {
            foreach (var baseBinding in record.Bases)
            {
                Add(baseBinding.Type, record);
                references.Add(new Reference(baseBinding.RecordId, record));
            }

            foreach (var instantiation in record.SupportedGenericInstantiations)
            {
                foreach (var argument in instantiation.TypeArguments)
                {
                    Add(argument, record);
                }
            }
        }

        foreach (var alias in compilation.TypeAliases)
        {
            Add(alias.TargetType, null);
        }

        return references;
    }

    private static void Walk(BindingType type, BindingNode? owner, List<Reference> references, HashSet<BindingType> visited)
    {
        if (!visited.Add(type))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(type.RecordId))
        {
            references.Add(new Reference(type.RecordId!, owner));
        }

        foreach (var argument in type.GenericArguments)
        {
            Walk(argument, owner, references, visited);
        }

        foreach (var argument in type.NativeTemplateArguments)
        {
            if (argument.Type is not null)
            {
                Walk(argument.Type, owner, references, visited);
            }
        }

        foreach (var parameterType in type.CallbackParameterTypes)
        {
            Walk(parameterType, owner, references, visited);
        }

        if (type.CallbackReturnType is not null)
        {
            Walk(type.CallbackReturnType, owner, references, visited);
        }

        if (type.FixedArrayElementType is not null)
        {
            Walk(type.FixedArrayElementType, owner, references, visited);
        }

        if (type.DynamicArrayElementType is not null)
        {
            Walk(type.DynamicArrayElementType, owner, references, visited);
        }
    }
}
