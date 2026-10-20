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
                !IsProjectionOfRecord(field.Type, record) &&
                record.Members.OfType<FunctionBinding>().Any(static method => method.HasMetadata(MetadataKeys.MarshallerExposedMethod)));
    }

    /// <summary>
    /// Returns true when the type is a projection over the record, for example optional&lt;T&gt;
    /// over the record T. The projection keeps the record id of T, but the field stores the
    /// projection and not a T. Thus a pointer to the field is not a pointer to a T.
    /// </summary>
    /// <remarks>
    /// The handle of the record itself is spelled as a pointer to the record. A generic argument
    /// of the record is spelled as the record. A projection is a template of a different name.
    /// The rule compares only the unqualified template names, because a namespace alias can
    /// qualify the same record differently.
    /// </remarks>
    private static bool IsProjectionOfRecord(BindingType type, RecordBinding record)
    {
        if (type.Marshaller.CppIsRecordValue || type.IsFixedArray || type.IsDynamicArray)
        {
            return false;
        }

        var spelling = type.CppTypeName.Trim();
        if (spelling.StartsWith("const ", StringComparison.Ordinal))
        {
            spelling = spelling[6..].TrimStart();
        }

        if (spelling.EndsWith('*') || spelling.EndsWith('&'))
        {
            return false;
        }

        var templateStart = spelling.IndexOf('<');
        if (templateStart < 0)
        {
            return false;
        }

        return !string.Equals(
            GetUnqualifiedTemplateName(spelling[..templateStart]),
            GetUnqualifiedTemplateName(record.CppQualifiedName),
            StringComparison.Ordinal);
    }

    private static string GetUnqualifiedTemplateName(string cppTypeName)
    {
        var name = cppTypeName;
        var templateStart = name.IndexOf('<');
        if (templateStart >= 0)
        {
            name = name[..templateStart];
        }

        name = name.Trim();
        var scopeEnd = name.LastIndexOf("::", StringComparison.Ordinal);
        return scopeEnd >= 0 ? name[(scopeEnd + 2)..].Trim() : name;
    }
}
