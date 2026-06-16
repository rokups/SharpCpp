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

        foreach (var instantiation in record.SupportedGenericInstantiations)
        {
            if (!ValidateInstantiationKinds(instantiation, recordsById))
            {
                record.SetIgnoreReason($"generic record '{record.ManagedName}' has unsupported concrete type arguments");
                return;
            }
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

        foreach (var instantiation in function.SupportedGenericInstantiations)
        {
            if (!ValidateInstantiationKinds(instantiation, recordsById))
            {
                function.SetIgnoreReason($"generic function '{function.ManagedName}' has unsupported concrete type arguments");
                return;
            }
        }
    }

    private static bool ValidateInstantiationKinds(RecordGenericInstantiationBinding instantiation, IReadOnlyDictionary<string, RecordBinding> recordsById)
    {
        foreach (var argument in instantiation.TypeArguments)
        {
            if (argument.Kind is not (BindingTypeKind.Primitive or BindingTypeKind.Enum or BindingTypeKind.Handle))
            {
                return false;
            }

            if (HasUnresolvedNamespaceColons(argument))
            {
                return false;
            }

            if (argument.Kind == BindingTypeKind.Handle &&
                argument.RecordId is not null &&
                recordsById.TryGetValue(argument.RecordId, out var argRecord) &&
                argRecord.GetMetadata<bool>(MetadataKeys.PlaceholderRecord) &&
                !argRecord.GetMetadata<bool>(MetadataKeys.SuppressEmission))
            {
                return false;
            }
        }

        return true;
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
}
