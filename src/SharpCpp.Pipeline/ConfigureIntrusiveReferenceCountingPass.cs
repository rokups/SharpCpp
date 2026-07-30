namespace SharpCpp.Pipeline;

using SharpCpp.Model.Ast;

public static class ConfigureIntrusiveReferenceCountingPass
{
    public static void Apply(
        BindingCompilation compilation,
        string addReferenceMethodName = "AddRef",
        string releaseMethodName = "Release")
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentException.ThrowIfNullOrWhiteSpace(addReferenceMethodName);
        ArgumentException.ThrowIfNullOrWhiteSpace(releaseMethodName);

        var recordsById = compilation.Records
            .GroupBy(static record => record.SymbolId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Last(), StringComparer.Ordinal);
        var referenceTargetRecordIds = compilation.Records
            .Where(record => IsReferenceTarget(
                record,
                recordsById,
                addReferenceMethodName,
                releaseMethodName,
                []))
            .Select(static record => record.SymbolId)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var record in compilation.Records)
        {
            if (referenceTargetRecordIds.Contains(record.SymbolId))
                record.SetMetadata(MetadataKeys.NativeInterfaceReleaseMethod, releaseMethodName);
        }

        foreach (var function in compilation.Functions)
        {
            if (function.ReturnType.RecordId is not null &&
                referenceTargetRecordIds.Contains(function.ReturnType.RecordId))
            {
                function.ReturnsOwnedHandle = true;
            }
        }

        foreach (var field in compilation.Records.SelectMany(static record => record.Members.OfType<FieldBinding>()))
        {
            if (field.Type.RecordId is not null && referenceTargetRecordIds.Contains(field.Type.RecordId))
                field.ReturnsOwnedHandle = true;
        }
    }

    private static bool IsReferenceTarget(
        RecordBinding record,
        IReadOnlyDictionary<string, RecordBinding> recordsById,
        string addReferenceMethodName,
        string releaseMethodName,
        HashSet<string> visited)
    {
        if (!visited.Add(record.SymbolId))
            return false;

        if (HasReferenceMethod(record, addReferenceMethodName) &&
            HasReferenceMethod(record, releaseMethodName))
        {
            return true;
        }

        foreach (var baseRecord in record.Bases)
        {
            if (recordsById.TryGetValue(baseRecord.RecordId, out var resolvedBase) &&
                IsReferenceTarget(
                    resolvedBase,
                    recordsById,
                    addReferenceMethodName,
                    releaseMethodName,
                    visited))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasReferenceMethod(RecordBinding record, string methodName) =>
        record.Members.OfType<FunctionBinding>().Any(function =>
            !function.IsStatic &&
            !function.IsConstructor &&
            function.AccessModifier == BindingAccessModifier.Public &&
            function.Parameters.Count == 0 &&
            function.ReturnType.PrimitiveKind == BindingPrimitiveKind.Void &&
            string.Equals(function.NativeName, methodName, StringComparison.Ordinal));
}
