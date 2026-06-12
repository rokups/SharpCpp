using SharpCpp.Model.Ast;

namespace SharpCpp.Pipeline;

public static class BindingCompilationGenericInstantiationPass
{
    public static void Apply(BindingCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        foreach (var record in compilation.Records)
        {
            record.SupportedGenericInstantiations.Clear();

            if (record.IsIgnored())
            {
                continue;
            }

            if (record.TryGetMetadata(MetadataKeys.ExplicitGenericInstantiations, out GenericInstantiationMetadata? explicitInstantiations))
            {
                foreach (var instantiation in explicitInstantiations!.Instantiations)
                {
                    record.SupportedGenericInstantiations.Add(instantiation);
                }
            }
        }

        foreach (var function in compilation.Functions)
        {
            function.SupportedGenericInstantiations.Clear();

            if (function.IsIgnored())
            {
                continue;
            }

            if (function.TryGetMetadata(MetadataKeys.ExplicitGenericInstantiations, out GenericInstantiationMetadata? explicitInstantiations))
            {
                foreach (var instantiation in explicitInstantiations!.Instantiations)
                {
                    function.SupportedGenericInstantiations.Add(instantiation);
                }
            }
        }

        foreach (var function in compilation.Functions)
        {
            if (function.IsIgnored())
            {
                continue;
            }

            CollectRecordInstantiations(compilation, function.ReturnType);
            foreach (var parameter in function.Parameters)
            {
                CollectRecordInstantiations(compilation, parameter.Type);
            }
        }

        foreach (var field in compilation.Descendants<FieldBinding>())
        {
            if (field.IsIgnored())
            {
                continue;
            }

            CollectRecordInstantiations(compilation, field.Type);
        }
    }

    private static void CollectRecordInstantiations(BindingCompilation compilation, BindingType type)
    {
        foreach (var genericArgument in type.GenericArguments)
        {
            CollectRecordInstantiations(compilation, genericArgument);
        }

        if (type.Kind != BindingTypeKind.Handle || string.IsNullOrWhiteSpace(type.RecordId) || type.GenericArguments.Count == 0 || type.ContainsGenericParameters)
        {
            return;
        }

        var record = compilation.Records.FirstOrDefault(candidate => string.Equals(candidate.SymbolId, type.RecordId, StringComparison.Ordinal));
        if (record is null || record.IsIgnored() || !record.IsGeneric)
        {
            return;
        }

        var visibleGenericParameterCount = GetVisibleGenericParameterCount(record);
        if (type.GenericArguments.Count != record.GenericParameters.Count &&
            type.GenericArguments.Count != visibleGenericParameterCount)
        {
            return;
        }

        if (type.GenericArguments.Any(argument => ContainsRecordGenericParameterReference(record, argument)))
        {
            return;
        }

        if (record.SupportedGenericInstantiations.Any(existing => InstantiationsEqual(existing.TypeArguments, type.GenericArguments)))
        {
            return;
        }

        record.SupportedGenericInstantiations.Add(new RecordGenericInstantiationBinding(type.GenericArguments));
    }

    private static bool InstantiationsEqual(IReadOnlyList<BindingType> left, IReadOnlyList<BindingType> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!string.Equals(left[index].CppTypeName, right[index].CppTypeName, StringComparison.Ordinal) ||
                left[index].Kind != right[index].Kind)
            {
                return false;
            }
        }

        return true;
    }

    private static int GetVisibleGenericParameterCount(RecordBinding record)
    {
        var usedIndices = new HashSet<int>();
        CollectManagedFormatPlaceholderIndices(record.ManagedTypeFormat, usedIndices);
        CollectManagedFormatPlaceholderIndices(record.ManagedBaseTypeName, usedIndices);

        return usedIndices.Count == 0
            ? record.GenericParameters.Count
            : usedIndices.Count(index => index >= 0 && index < record.GenericParameters.Count);
    }

    private static bool ContainsRecordGenericParameterReference(RecordBinding record, BindingType type)
    {
        if (type.IsGenericParameter &&
            record.GenericParameters.Any(parameter => string.Equals(parameter.Name, type.GenericParameterName, StringComparison.Ordinal)))
        {
            return true;
        }

        var trimmedTypeName = type.CppTypeName.Trim().TrimEnd('*', '&').Trim();
        if (record.GenericParameters.Any(parameter => string.Equals(parameter.Name, trimmedTypeName, StringComparison.Ordinal)))
        {
            return true;
        }

        return type.GenericArguments.Any(argument => ContainsRecordGenericParameterReference(record, argument));
    }

    private static void CollectManagedFormatPlaceholderIndices(string? format, ISet<int> target)
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            return;
        }

        for (var index = 0; index < format.Length; index++)
        {
            if (format[index] != '{')
            {
                continue;
            }

            var closeIndex = format.IndexOf('}', index + 1);
            if (closeIndex <= index + 1)
            {
                continue;
            }

            if (int.TryParse(format[(index + 1)..closeIndex], out var placeholderIndex))
            {
                target.Add(placeholderIndex);
            }

            index = closeIndex;
        }
    }
}
