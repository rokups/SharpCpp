using SharpCpp.Model.Ast;

namespace SharpCpp.Pipeline;

public static class ValidateBootstrapSurfacePass
{
    public static void Apply(BindingCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

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
                ValidateGenericRecord(record);
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

            ValidateGenericFunction(function);
        }
    }

    private static void ValidateGenericRecord(RecordBinding record)
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
            if (!ValidateInstantiationKinds(instantiation))
            {
                record.SetIgnoreReason($"generic record '{record.ManagedName}' currently supports only primitive/enum/handle concrete type arguments");
                return;
            }
        }
    }

    private static void ValidateGenericFunction(FunctionBinding function)
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
            if (!ValidateInstantiationKinds(instantiation))
            {
                function.SetIgnoreReason($"generic function '{function.ManagedName}' currently supports only primitive/enum/handle concrete type arguments");
                return;
            }
        }
    }

    private static bool ValidateInstantiationKinds(RecordGenericInstantiationBinding instantiation)
    {
        return !instantiation.TypeArguments.Any(argument => argument.Kind is not (BindingTypeKind.Primitive or BindingTypeKind.Enum or BindingTypeKind.Handle));
    }
}
