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

        ResetFunctionInstantiations(compilation);
        CollectInferredInstantiations(compilation);
    }

    /// <summary>
    /// Rebuild inferred specializations after validation has removed unsupported uses.
    /// Explicit and target-configured specializations are preserved.
    /// </summary>
    public static void RefreshInferred(BindingCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var allowedInferredInstantiations = compilation.Records.ToDictionary(
            static record => record,
            static record => record.SupportedGenericInstantiations
                .Where(static instantiation => instantiation.IsInferred)
                .ToArray());

        foreach (var record in compilation.Records)
        {
            for (var index = record.SupportedGenericInstantiations.Count - 1; index >= 0; --index)
            {
                if (record.SupportedGenericInstantiations[index].IsInferred)
                    record.SupportedGenericInstantiations.RemoveAt(index);
            }
        }

        CollectInferredInstantiations(compilation);

        // Target configuration may deliberately narrow the inferred surface. Refreshing
        // after degradation is an intersection with the still-live uses, not permission
        // to resurrect specializations that the target already removed.
        foreach (var record in compilation.Records)
        {
            var allowed = allowedInferredInstantiations[record];
            for (var index = record.SupportedGenericInstantiations.Count - 1; index >= 0; --index)
            {
                var instantiation = record.SupportedGenericInstantiations[index];
                if (instantiation.IsInferred && !allowed.Any(candidate => InstantiationShapesEqual(candidate, instantiation)))
                    record.SupportedGenericInstantiations.RemoveAt(index);
            }
        }
    }

    private static void CollectInferredInstantiations(BindingCompilation compilation)
    {
        foreach (var function in compilation.Functions)
        {
            if (function.IsIgnored() || function.IsGeneric || (function.Parent as RecordBinding)?.IsGeneric == true)
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

        foreach (var baseBinding in compilation.Records.SelectMany(static record => record.Bases))
        {
            CollectRecordInstantiations(compilation, baseBinding.Type);
        }

        foreach (var alias in compilation.TypeAliases)
        {
            CollectRecordInstantiations(compilation, alias.TargetType);
        }
        DropIncompleteNativeShapes(compilation);
    }

    private static void ResetFunctionInstantiations(BindingCompilation compilation)
    {
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
    }

    private static void CollectRecordInstantiations(BindingCompilation compilation, BindingType type)
    {
        if (type.IsCallback)
        {
            if (type.CallbackReturnType is not null)
            {
                CollectRecordInstantiations(compilation, type.CallbackReturnType);
            }

            foreach (var parameterType in type.CallbackParameterTypes)
            {
                CollectRecordInstantiations(compilation, parameterType);
            }

            return;
        }

        foreach (var genericArgument in type.GenericArguments)
        {
            CollectRecordInstantiations(compilation, genericArgument);
        }

        if (type.Kind != BindingTypeKind.Handle ||
            string.IsNullOrWhiteSpace(type.RecordId) ||
            type.GenericArguments.Count == 0 ||
            type.ContainsGenericParameters ||
            type.ContainsDependentTemplateSpelling)
        {
            return;
        }

        var record = compilation.Records.FirstOrDefault(candidate => string.Equals(candidate.SymbolId, type.RecordId, StringComparison.Ordinal));
        if (record is null || record.IsIgnored() || !record.IsGeneric)
        {
            return;
        }

        // A managed projection may hide native-only template parameters, but the native
        // specialization must still provide every argument. An incomplete AST shape is
        // not a valid C++ type and must not become a generated specialization.
        var nativeTemplateParameterCount = record.NativeTemplateParameterCount > 0
            ? record.NativeTemplateParameterCount
            : record.GenericParameters.Count;
        var requiredNativeTemplateParameterCount = record.NativeTemplateRequiredParameterCount > 0
            ? record.NativeTemplateRequiredParameterCount
            : nativeTemplateParameterCount;
        if (!type.NativeTemplateArgumentsAreAstVerified &&
            type.NativeTemplateArguments.Count < requiredNativeTemplateParameterCount ||
            type.NativeTemplateArguments.Count > nativeTemplateParameterCount)
        {
            return;
        }

        var visibleGenericParameterCount = GetVisibleGenericParameterCount(record);
        if (type.GenericArguments.Count != record.GenericParameters.Count &&
            type.GenericArguments.Count != visibleGenericParameterCount &&
            type.GenericArguments.Count >= record.GenericParameters.Count)
        {
            return;
        }

        if (type.GenericArguments.Any(argument => ContainsRecordGenericParameterReference(record, argument)))
        {
            return;
        }

        if (record.SupportedGenericInstantiations.Any(existing =>
                InstantiationsEqual(existing.TypeArguments, type.GenericArguments) &&
                (existing.NativeTemplateArguments.All(static argument => argument.Kind == BindingTemplateArgumentKind.Type) &&
                 type.NativeTemplateArguments.All(static argument => argument.Kind == BindingTemplateArgumentKind.Type) ||
                 NativeTemplateArgumentsEqual(existing.NativeTemplateArguments, type.NativeTemplateArguments))))
        {
            return;
        }

        record.SupportedGenericInstantiations.Add(new RecordGenericInstantiationBinding(
            type.GenericArguments,
            type.NativeTemplateArguments,
            isInferred: true));
    }

    /// <summary>
    /// One C++ specialization is identified by its complete argument list. A use spelled
    /// with defaulted arguments omitted (reached without AST completion) is dropped when
    /// the complete shape of the same specialization is also known, so provider shapes
    /// and entry points agree across spellings and modules.
    /// </summary>
    private static void DropIncompleteNativeShapes(BindingCompilation compilation)
    {
        foreach (var record in compilation.Records.Where(static record => record.NativeTemplateParameterCount > 0))
        {
            var instantiations = record.SupportedGenericInstantiations;
            for (var index = instantiations.Count - 1; index >= 0; --index)
            {
                var candidate = instantiations[index];
                if (candidate.IsInferred &&
                    candidate.NativeTemplateArguments.Count < record.NativeTemplateParameterCount &&
                    instantiations.Any(complete =>
                        complete.NativeTemplateArguments.Count == record.NativeTemplateParameterCount &&
                        SpecializationArgumentsEqual(complete.TypeArguments, candidate.TypeArguments)))
                {
                    instantiations.RemoveAt(index);
                }
            }
        }
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

    // An incomplete shape is reached through spellings (aliases, callback signatures) that
    // carry record arguments as handles or values inconsistently; a record argument is
    // identified by its record.
    private static bool SpecializationArgumentsEqual(IReadOnlyList<BindingType> left, IReadOnlyList<BindingType> right)
    {
        return left.Count == right.Count &&
            left.Zip(right).All(static pair =>
                pair.First.RecordId is not null || pair.Second.RecordId is not null
                    ? string.Equals(pair.First.RecordId, pair.Second.RecordId, StringComparison.Ordinal)
                    : pair.First.Kind == pair.Second.Kind &&
                        string.Equals(pair.First.CppTypeName, pair.Second.CppTypeName, StringComparison.Ordinal));
    }

    private static bool NativeTemplateArgumentsEqual(
        IReadOnlyList<BindingTemplateArgument> left,
        IReadOnlyList<BindingTemplateArgument> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; ++index)
        {
            if (left[index].Kind != right[index].Kind ||
                left[index].IntegralValue != right[index].IntegralValue ||
                !string.Equals(left[index].Type?.CppTypeName, right[index].Type?.CppTypeName, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool InstantiationShapesEqual(
        RecordGenericInstantiationBinding left,
        RecordGenericInstantiationBinding right) =>
        InstantiationsEqual(left.TypeArguments, right.TypeArguments) &&
        NativeTemplateArgumentsEqual(left.NativeTemplateArguments, right.NativeTemplateArguments);

    private static int GetVisibleGenericParameterCount(RecordBinding record)
    {
        var usedIndices = new HashSet<int>();
        CollectManagedFormatPlaceholderIndices(record.ManagedTypeFormat, usedIndices);
        CollectManagedFormatPlaceholderIndices(record.ManagedBaseTypeName, usedIndices);
        CollectManagedFormatPlaceholderIndices(record.ManagedBaseTypeConstraints, usedIndices);

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
