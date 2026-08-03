using SharpCpp.Model.Ast;

namespace SharpCpp.Pipeline;

public static class LowerForwardDeclaredHandlesPass
{
    public static void Apply(BindingCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var forwardRecordIds = compilation.Records
            .Where(static record =>
                record.GetMetadata<bool>(MetadataKeys.ForwardDeclaration) &&
                !record.GetMetadata<bool>(MetadataKeys.OpaqueNativeHandle))
            .Select(static record => record.SymbolId)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var function in compilation.Functions)
        {
            function.ReturnType = Lower(function.ReturnType, forwardRecordIds);
            foreach (var parameter in function.Parameters)
            {
                parameter.Type = Lower(parameter.Type, forwardRecordIds);
            }
        }

        foreach (var field in compilation.Descendants<FieldBinding>())
        {
            field.Type = Lower(field.Type, forwardRecordIds);
        }
    }

    private static BindingType Lower(BindingType type, IReadOnlySet<string> forwardRecordIds)
    {
        if (type.Kind != BindingTypeKind.Handle ||
            type.RecordId is null ||
            !forwardRecordIds.Contains(type.RecordId))
        {
            return type;
        }

        return BindingType.CreateConfigured(
            type.CppTypeName,
            BindingType.IntPtr.ManagedTypeName,
            primitiveKind: BindingPrimitiveKind.IntPtr);
    }
}
