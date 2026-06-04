using SharpCpp.Model.Ast;

namespace SharpCpp.Pipeline;

public static class ValidateBootstrapSurfacePass
{
    public static void Apply(BindingCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        foreach (var record in compilation.Records)
        {
            if (record.Bases.Any(static baseBinding => baseBinding.IsVirtual))
            {
                throw new NotSupportedException($"Record '{record.ManagedName}' uses virtual base inheritance, which is not supported in this bootstrap surface.");
            }

            if (record.IsGeneric)
            {
                ValidateGenericRecord(record);
            }
        }

        foreach (var function in compilation.Functions)
        {
            if (!function.IsGeneric)
            {
                continue;
            }

            ValidateGenericFunction(function);
        }
    }

    private static void ValidateGenericRecord(RecordBinding record)
    {
        if (record.IsDirector || record.IsPolymorphic || record.Bases.Count > 0)
        {
            throw new NotSupportedException($"Generic record '{record.ManagedName}' is limited to non-polymorphic, non-director, non-inheriting surface in this slice.");
        }

        if (record.Members.OfType<FieldBinding>().Any())
        {
            throw new NotSupportedException($"Generic record '{record.ManagedName}' does not support field projection in this slice.");
        }

        if (record.Members.OfType<FunctionBinding>().Any(static function => function.OperatorToken is not null || function.IsConversionOperator))
        {
            throw new NotSupportedException($"Generic record '{record.ManagedName}' does not support operators in this slice.");
        }

        foreach (var instantiation in record.SupportedGenericInstantiations)
        {
            ValidateInstantiationKinds(instantiation, $"Generic record '{record.ManagedName}'");
        }
    }

    private static void ValidateGenericFunction(FunctionBinding function)
    {
        if (function.FindOwningRecord() is { IsGeneric: false } ownerRecord)
        {
            throw new NotSupportedException($"Generic method '{ownerRecord.ManagedName}.{function.ManagedName}' is not supported on non-generic records in this bootstrap surface.");
        }

        foreach (var instantiation in function.SupportedGenericInstantiations)
        {
            ValidateInstantiationKinds(instantiation, $"Generic function '{function.ManagedName}'");
        }
    }

    private static void ValidateInstantiationKinds(RecordGenericInstantiationBinding instantiation, string surfaceName)
    {
        if (instantiation.TypeArguments.Any(argument => argument.Kind is not (BindingTypeKind.Primitive or BindingTypeKind.Enum or BindingTypeKind.Handle)))
        {
            throw new NotSupportedException($"{surfaceName} currently supports only primitive/enum/handle concrete type arguments.");
        }
    }
}
