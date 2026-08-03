using SharpCpp.Model.Ast;

namespace SharpCpp.Pipeline;

public static class LowerForwardDeclaredHandlesPass
{
    public static void Apply(BindingCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var ownedRecordIds = compilation.Records
            .Select(static record => record.SymbolId)
            .ToHashSet(StringComparer.Ordinal);
        var forwardRecordIds = compilation.Records
            .Where(static record =>
                record.GetMetadata<bool>(MetadataKeys.ForwardDeclaration) &&
                !record.GetMetadata<bool>(MetadataKeys.OpaqueNativeHandle))
            .Select(static record => record.SymbolId)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var function in compilation.Functions)
        {
            function.ReturnType = Lower(
                function.ReturnType,
                function.CppReturnTypeName,
                ownedRecordIds,
                forwardRecordIds);
            foreach (var parameter in function.Parameters)
            {
                parameter.Type = Lower(
                    parameter.Type,
                    parameter.CppTypeName,
                    ownedRecordIds,
                    forwardRecordIds);
            }
        }

        foreach (var field in compilation.Descendants<FieldBinding>())
        {
            field.Type = Lower(field.Type, field.CppTypeName, ownedRecordIds, forwardRecordIds);
        }
    }

    private static BindingType Lower(
        BindingType type,
        string cppTypeName,
        IReadOnlySet<string> ownedRecordIds,
        IReadOnlySet<string> forwardRecordIds)
    {
        if (type.Kind != BindingTypeKind.Handle ||
            type.RecordId is null ||
            ownedRecordIds.Contains(type.RecordId) && !forwardRecordIds.Contains(type.RecordId))
        {
            return type;
        }

        return BindingType.CreateConfigured(
            cppTypeName,
            BindingType.IntPtr.ManagedTypeName,
            primitiveKind: BindingPrimitiveKind.IntPtr);
    }
}
