using SharpCpp.Model.Ast;

namespace SharpCpp.Pipeline;

public static class ValidateBootstrapSurfacePass
{
    public static void Apply(BindingCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var recordsById = compilation.Records
            .GroupBy(static record => record.SymbolId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Last(), StringComparer.Ordinal);

        foreach (var record in compilation.Records)
        {
            if (record.IsIgnored() || record.GetMetadata<bool>(MetadataKeys.SuppressEmission))
            {
                continue;
            }

            if (record.Bases.Any(static baseBinding => baseBinding.IsVirtual))
            {
                record.SetIgnoreReason($"record uses virtual base inheritance, which is not supported in this bootstrap surface");
                continue;
            }

            if (record.IsGeneric)
            {
                ValidateGenericRecord(record, recordsById);
            }
        }

        foreach (var function in compilation.Functions)
        {
            if (function.IsIgnored())
            {
                continue;
            }

            if (!function.IsGeneric)
            {
                continue;
            }

            ValidateGenericFunction(function, recordsById);
        }
    }

    private static void ValidateGenericRecord(RecordBinding record, IReadOnlyDictionary<string, RecordBinding> recordsById)
    {
        if (record.SupportedGenericInstantiations.Count == 0 && !record.HasMetadata(MetadataKeys.ExplicitGenericInstantiations))
        {
            record.SetIgnoreReason($"generic record '{record.ManagedName}' has no known concrete instantiations");
            return;
        }

        if (record.IsDirector || record.IsPolymorphic || record.Bases.Count > 0)
        {
            record.SetIgnoreReason($"generic record '{record.ManagedName}' is limited to non-polymorphic, non-director, non-inheriting surface in this slice");
            return;
        }

        if (record.Members.OfType<FieldBinding>().Any())
        {
            record.SetIgnoreReason($"generic record '{record.ManagedName}' does not support field projection in this slice");
            return;
        }

        if (record.Members.OfType<FunctionBinding>().Any(static function => function.OperatorToken is not null || function.IsConversionOperator))
        {
            record.SetIgnoreReason($"generic record '{record.ManagedName}' does not support operators in this slice");
            return;
        }

        FilterUnsupportedInstantiations(record, record.SupportedGenericInstantiations, recordsById);

        if (record.SupportedGenericInstantiations.Count == 0 &&
            !record.HasMetadata(MetadataKeys.ExplicitGenericInstantiations))
        {
            record.SetIgnoreReason($"generic record '{record.ManagedName}' has no supported concrete instantiations");
        }
    }

    private static void ValidateGenericFunction(FunctionBinding function, IReadOnlyDictionary<string, RecordBinding> recordsById)
    {
        if (function.SupportedGenericInstantiations.Count == 0 && !function.HasMetadata(MetadataKeys.ExplicitGenericInstantiations))
        {
            function.SetIgnoreReason($"generic function '{function.ManagedName}' has no known concrete instantiations");
            return;
        }

        if (function.FindOwningRecord() is { IsGeneric: false } ownerRecord)
        {
            function.SetIgnoreReason($"generic method '{ownerRecord.ManagedName}.{function.ManagedName}' is not supported on non-generic records in this bootstrap surface");
            return;
        }

        FilterUnsupportedInstantiations(function.SupportedGenericInstantiations, recordsById);

        if (function.SupportedGenericInstantiations.Count == 0 &&
            !function.HasMetadata(MetadataKeys.ExplicitGenericInstantiations))
        {
            function.SetIgnoreReason($"generic function '{function.ManagedName}' has no supported concrete instantiations");
        }
    }

    private static bool ValidateInstantiationKinds(RecordGenericInstantiationBinding instantiation, IReadOnlyDictionary<string, RecordBinding> recordsById)
    {
        foreach (var argument in instantiation.TypeArguments)
        {
            if (!ValidateInstantiationArgument(argument, recordsById))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ValidateInstantiationKinds(
        RecordBinding record,
        RecordGenericInstantiationBinding instantiation,
        IReadOnlyDictionary<string, RecordBinding> recordsById)
    {
        foreach (var argument in SelectManagedVisibleTypeArguments(record, instantiation.TypeArguments))
        {
            if (!ValidateInstantiationArgument(argument, recordsById))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ValidateInstantiationArgument(BindingType argument, IReadOnlyDictionary<string, RecordBinding> recordsById)
    {
        if (argument.Kind is not (BindingTypeKind.Primitive or BindingTypeKind.Enum or BindingTypeKind.Handle))
        {
            return false;
        }

        if (HasUnresolvedNamespaceColons(argument))
        {
            return false;
        }

        if (argument.Marshaller.IsRefStruct)
        {
            return false;
        }

        if (argument.Kind == BindingTypeKind.Handle &&
            argument.RecordId is not null &&
            recordsById.TryGetValue(argument.RecordId, out var argRecord))
        {
            if (argRecord.IsIgnored() && IsFatalIgnoredGenericArgumentRecord(argRecord))
            {
                return false;
            }

            // Placeholder records without SuppressEmission are unresolvable
            // external types that should not produce bindings.
            if (argRecord.GetMetadata<bool>(MetadataKeys.PlaceholderRecord) &&
                !argRecord.GetMetadata<bool>(MetadataKeys.SuppressEmission))
            {
                return false;
            }

            // SuppressEmission placeholders that reference C++ types from
            // namespaces not included in the native build (e.g. Rml::Vector3)
            // produce broken native thunks — filter those instantiations.
            if (argRecord.GetMetadata<bool>(MetadataKeys.PlaceholderRecord) &&
                argRecord.GetMetadata<bool>(MetadataKeys.SuppressEmission) &&
                HasUnresolvedNativeNamespaceColons(argRecord))
            {
                return false;
            }
        }

        foreach (var genericArgument in SelectManagedVisibleNestedTypeArguments(argument, recordsById))
        {
            if (!ValidateInstantiationArgument(genericArgument, recordsById))
            {
                return false;
            }
        }

        return true;
    }

    private static void FilterUnsupportedInstantiations(
        IList<RecordGenericInstantiationBinding> instantiations,
        IReadOnlyDictionary<string, RecordBinding> recordsById)
    {
        for (var i = instantiations.Count - 1; i >= 0; i--)
        {
            if (!ValidateInstantiationKinds(instantiations[i], recordsById))
                instantiations.RemoveAt(i);
        }
    }

    private static void FilterUnsupportedInstantiations(
        RecordBinding record,
        IList<RecordGenericInstantiationBinding> instantiations,
        IReadOnlyDictionary<string, RecordBinding> recordsById)
    {
        for (var i = instantiations.Count - 1; i >= 0; i--)
        {
            if (!ValidateInstantiationKinds(record, instantiations[i], recordsById))
                instantiations.RemoveAt(i);
        }
    }

    private static IEnumerable<BindingType> SelectManagedVisibleNestedTypeArguments(
        BindingType type,
        IReadOnlyDictionary<string, RecordBinding> recordsById)
    {
        if (type.Kind == BindingTypeKind.Handle &&
            type.RecordId is not null &&
            recordsById.TryGetValue(type.RecordId, out var record) &&
            record.IsGeneric)
        {
            return SelectManagedVisibleTypeArguments(record, type.GenericArguments);
        }

        return type.GenericArguments;
    }

    private static IEnumerable<BindingType> SelectManagedVisibleTypeArguments(
        RecordBinding record,
        IReadOnlyList<BindingType> typeArguments)
    {
        var visibleIndices = GetManagedVisibleGenericParameterIndices(record);
        if (visibleIndices.Count == 0 || typeArguments.Count == 0)
        {
            return typeArguments;
        }

        if (typeArguments.Count == record.GenericParameters.Count)
        {
            return visibleIndices
                .Where(index => index < typeArguments.Count)
                .Select(index => typeArguments[index]);
        }

        return typeArguments;
    }

    private static bool IsFatalIgnoredGenericArgumentRecord(RecordBinding record)
    {
        if (record.GetMetadata<bool>(MetadataKeys.ForwardDeclaration))
        {
            return true;
        }

        if (record.GetMetadata<bool>(MetadataKeys.PlaceholderRecord) &&
            !record.GetMetadata<bool>(MetadataKeys.SuppressEmission))
        {
            return true;
        }

        var ignoreReason = record.GetIgnoreReason() ?? string.Empty;
        return ignoreReason.Contains("outside the configured namespace mappings", StringComparison.Ordinal) ||
            ignoreReason.Contains("internal detail namespace", StringComparison.Ordinal) ||
            ignoreReason.Contains("wrapper materialization requires a complete definition", StringComparison.Ordinal);
    }

    private static IReadOnlyList<int> GetManagedVisibleGenericParameterIndices(RecordBinding record)
    {
        var usedIndices = new HashSet<int>();
        CollectManagedFormatPlaceholderIndices(record.ManagedTypeFormat, usedIndices);
        CollectManagedFormatPlaceholderIndices(record.ManagedBaseTypeName, usedIndices);
        CollectManagedFormatPlaceholderIndices(record.ManagedBaseTypeConstraints, usedIndices);

        if (usedIndices.Count == 0)
        {
            return Enumerable.Range(0, record.GenericParameters.Count).ToArray();
        }

        return usedIndices
            .Where(index => index >= 0 && index < record.GenericParameters.Count)
            .OrderBy(static index => index)
            .ToArray();
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

    /// <summary>
    /// Returns <c>true</c> when the type or any of its generic arguments
    /// contains a C++ <c>::</c> namespace separator in its managed public
    /// type name that is NOT the valid <c>global::</c> prefix, indicating
    /// the type has not been mapped to a valid C# type.
    /// </summary>
    private static bool HasUnresolvedNamespaceColons(BindingType type)
    {
        // Generic parameters are resolved at instantiation time — nothing to
        // check at the generic-definition level.
        if (type.Kind == BindingTypeKind.GenericParameter)
        {
            return false;
        }

        // Check this type's own public name (without generic arguments).
        var publicName = type.Marshaller.PublicTypeName;
        if (publicName.Contains("::", StringComparison.Ordinal) &&
            !publicName.StartsWith("global::", StringComparison.Ordinal))
        {
            return true;
        }

        // Recurse into generic type arguments — each argument is individually
        // a BindingType that must also have a renderer-ready managed name.
        foreach (var argument in type.GenericArguments)
        {
            if (HasUnresolvedNamespaceColons(argument))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasUnresolvedNativeNamespaceColons(RecordBinding record)
    {
        var cppName = record.CppQualifiedName;
        if (!cppName.Contains("::", StringComparison.Ordinal))
            return false;

        var root = cppName[..cppName.IndexOf("::", StringComparison.Ordinal)];
        return root is not ("Urho3D" or "ea" or "eastl" or "std" or "SharpCpp");
    }
}
