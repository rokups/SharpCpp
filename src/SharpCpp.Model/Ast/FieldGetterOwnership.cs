namespace SharpCpp.Model.Ast;

/// <summary>
/// Decides if the getter of a field returns a handle that the caller owns. The renderer and the
/// metadata exporter use this one rule, so they always agree.
/// </summary>
public static class FieldGetterOwnership
{
    /// <summary>
    /// Returns true when the native getter allocates an owned copy of the field value.
    /// </summary>
    public static bool GetterReturnsOwnedHandle(BindingCompilation compilation, FieldBinding field)
    {
        if (field.ReturnsOwnedHandle)
        {
            return true;
        }

        // A fixed-array getter returns a pointer into the record, so it never owns.
        return field.GetMetadata<bool>(MetadataKeys.OwnedValueFieldGetter) &&
            !field.Type.IsFixedArray &&
            !BorrowsRecordStorage(compilation, field);
    }

    /// <summary>
    /// Returns true when the native getter returns a pointer to the field inside the record.
    /// </summary>
    public static bool BorrowsRecordStorage(BindingCompilation compilation, FieldBinding field)
    {
        if (field.Type.Kind != BindingTypeKind.Handle || field.IsPointer)
        {
            return false;
        }

        var record = string.IsNullOrWhiteSpace(field.Type.RecordId)
            ? null
            : compilation.Records.FirstOrDefault(
                candidate => string.Equals(candidate.SymbolId, field.Type.RecordId, StringComparison.Ordinal));
        var projectsValue = field.Type.Marshaller.CppIsRecordValue &&
            record is not null &&
            record.GetMetadata<bool>(MetadataKeys.SuppressEmission);
        if (projectsValue)
        {
            return false;
        }

        return field.Type.Marshaller.CppIsRecordValue ||
            (record is not null &&
                record.Members.OfType<FunctionBinding>().Any(static method => method.HasMetadata(MetadataKeys.MarshallerExposedMethod)));
    }
}
