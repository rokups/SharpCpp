using SharpCpp.Model.Ast;

namespace SharpCpp.Pipeline;

public static class DegradeUnsupportedSurfacePass
{
    public static void Apply(BindingCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var changed = true;
        while (changed)
        {
            changed = false;
            var recordsById = compilation.Records.ToDictionary(static record => record.SymbolId, StringComparer.Ordinal);
            var allowedNamespaceRoots = compilation.NamespaceMappings.Keys.ToHashSet(StringComparer.Ordinal);

            foreach (var duplicateGroup in compilation.Functions
                         .Where(static function => function.Parent is not RecordBinding && !function.IsIgnored() && function.Parent?.IsIgnored() != true)
                         .GroupBy(GetCallableSignatureKey, StringComparer.Ordinal)
                         .Where(static group => group.Count() > 1))
            {
                foreach (var function in duplicateGroup)
                {
                    function.SetIgnoreReason($"callable signature '{duplicateGroup.Key}' is declared multiple times in this bootstrap surface");
                    changed = true;
                }
            }

            foreach (var duplicateGroup in compilation.Functions
                          .Where(static function => function.Parent is not RecordBinding && !function.IsIgnored() && function.Parent?.IsIgnored() != true)
                          .GroupBy(GetManagedCallableSignatureKey, StringComparer.Ordinal)
                          .Where(static group => group.Count() > 1))
            {
                foreach (var function in duplicateGroup)
                {
                    function.SetIgnoreReason($"managed callable signature '{duplicateGroup.Key}' is declared multiple times in this bootstrap surface");
                    changed = true;
                }
            }

            foreach (var record in compilation.Records)
            {
                var duplicateGroups = record.Members.OfType<FunctionBinding>()
                    .Where(static function => !function.IsIgnored())
                    .GroupBy(GetManagedPublicCallableSignatureKey, StringComparer.Ordinal)
                    .Where(static group => group.Count() > 1)
                    .ToArray();

                foreach (var duplicateGroup in duplicateGroups)
                {
                    var kept = duplicateGroup
                        .OrderBy(static function => function.IsConstMethod)
                        .ThenBy(static function => function.SymbolId, StringComparer.Ordinal)
                        .First();

                    foreach (var function in duplicateGroup.Where(function => !ReferenceEquals(function, kept)))
                    {
                        function.SetIgnoreReason($"managed callable signature '{duplicateGroup.Key}' is declared multiple times in this bootstrap surface");
                        changed = true;
                    }
                }
            }

            foreach (var record in compilation.Records)
            {
                if (record.IsIgnored())
                {
                    continue;
                }

                if (record.GetMetadata<bool>(MetadataKeys.SuppressEmission))
                {
                    continue;
                }

                if (TryGetNamespaceExclusionReason(record.CppQualifiedName, allowedNamespaceRoots, out var namespaceReason))
                {
                    record.SetIgnoreReason(namespaceReason);
                    changed = true;
                    continue;
                }

                if (record.GetMetadata<bool>(MetadataKeys.ForwardDeclaration))
                {
                    record.SetIgnoreReason("record is only forward-declared; wrapper materialization requires a complete definition");
                    changed = true;
                    continue;
                }

                if (TryGetUnsupportedRecordReason(record, compilation, recordsById, out var recordReason))
                {
                    record.SetIgnoreReason(recordReason);
                    changed = true;
                }
            }

            foreach (var @enum in compilation.Enums)
            {
                if (@enum.IsIgnored())
                {
                    continue;
                }

                if (@enum.GetMetadata<bool>(MetadataKeys.SuppressEmission))
                {
                    continue;
                }

                if (TryGetNamespaceExclusionReason(@enum.CppQualifiedName, allowedNamespaceRoots, out var namespaceReason))
                {
                    @enum.SetIgnoreReason(namespaceReason);
                    changed = true;
                }
            }

            foreach (var function in compilation.Functions)
            {
                if (function.IsIgnored() || function.Parent?.IsIgnored() == true)
                {
                    continue;
                }

                if (TryGetNamespaceExclusionReason(function.CppQualifiedName, allowedNamespaceRoots, out var namespaceReason))
                {
                    function.SetIgnoreReason(namespaceReason);
                    changed = true;
                    continue;
                }

                if (TryGetUnsupportedFunctionReason(function, recordsById, out var reason))
                {
                    function.SetIgnoreReason(reason);
                    changed = true;
                }
            }

            foreach (var field in compilation.Descendants<FieldBinding>())
            {
                if (field.IsIgnored() || field.Parent?.IsIgnored() == true)
                {
                    continue;
                }

                if (TryGetUnsupportedFieldReason(field, recordsById, out var reason))
                {
                    field.SetIgnoreReason(reason);
                    changed = true;
                }
            }
        }
    }

    private static bool TryGetUnsupportedFunctionReason(
        FunctionBinding function,
        IReadOnlyDictionary<string, RecordBinding> recordsById,
        out string reason)
    {
        if (TryGetUnsupportedTypeReason(function.ReturnType, function.CppReturnTypeName, recordsById, disallowNonPointerHandles: true, out reason))
        {
            reason = $"return type is unsupported in this bootstrap surface: {reason}";
            return true;
        }

        foreach (var parameter in function.Parameters)
        {
            if (TryGetUnsupportedTypeReason(parameter.Type, parameter.CppTypeName, recordsById, disallowNonPointerHandles: false, out reason))
            {
                reason = $"parameter '{parameter.ManagedName}' is unsupported in this bootstrap surface: {reason}";
                return true;
            }
        }

        if (function.IsGeneric &&
            !function.ReturnType.ContainsGenericParameters &&
            function.Parameters.All(static parameter => !parameter.Type.ContainsGenericParameters))
        {
            reason = $"generic function '{function.ManagedName}' does not expose its template parameters through supported ABI positions";
            return true;
        }

        if (!function.IsConstructor &&
            function.NativeName.StartsWith("operator", StringComparison.Ordinal) &&
            function.OperatorToken is null &&
            !function.IsConversionOperator)
        {
            reason = $"operator '{function.NativeName}' is not supported in this bootstrap surface";
            return true;
        }

        if (function.OperatorToken is not null && !IsSupportedOperatorShape(function))
        {
            reason = $"operator shape '{function.ManagedName}' is not supported in this bootstrap surface";
            return true;
        }

        if (function.Parent is RecordBinding owningRecord &&
            function.OperatorToken == "!=" &&
            !HasCompanionOperator(owningRecord, function, "=="))
        {
            reason = $"operator '{function.ManagedName}' does not have the companion comparison operator required by C#";
            return true;
        }

        if (function.Parent is RecordBinding owningRecord2 &&
            function.OperatorToken == "==" &&
            !IsSelfComparisonOperator(owningRecord2, function) &&
            !HasCompanionOperator(owningRecord2, function, "!="))
        {
            reason = $"operator '{function.ManagedName}' does not have the companion comparison operator required by C#";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private static bool TryGetUnsupportedRecordReason(
        RecordBinding record,
        BindingCompilation compilation,
        IReadOnlyDictionary<string, RecordBinding> recordsById,
        out string reason)
    {
        // Records with no user-defined members are template utility types
        // (metafunctions like IndexInTuple) that shouldn't be wrapped.
        // Their only members would be the synthetic wrapper boilerplate (Handle,
        // constructors, P/Invoke) which is never useful without API methods.
        if (!record.Members.OfType<FunctionBinding>().Any(static m => !m.IsConstructor && !m.IsStatic && !m.HasMetadata(MetadataKeys.IgnoreReason)) &&
            !record.Members.OfType<FieldBinding>().Any() &&
            !record.Members.OfType<FunctionBinding>().Any(static m => m.IsConstructor && !m.HasMetadata(MetadataKeys.IgnoreReason)))
        {
            reason = $"record '{record.CppQualifiedName}' has no user-defined API surface (it is likely a template utility type)";
            return true;
        }

        var unavailableBases = new List<RecordBaseBinding>();

        foreach (var baseBinding in record.Bases)
        {
            if (!recordsById.TryGetValue(baseBinding.RecordId, out var baseRecord) || baseRecord.IsIgnored())
            {
                unavailableBases.Add(baseBinding);
            }
        }

        foreach (var baseBinding in unavailableBases)
        {
            record.SetWarning($"dropping base '{baseBinding.CppQualifiedName}' from record '{record.CppQualifiedName}' because the base record is not available for a coherent managed hierarchy; the derived record will still be generated without that base");
            record.Bases.Remove(baseBinding);

            if (string.Equals(record.ManagedBaseTypeName, baseBinding.ManagedName, StringComparison.Ordinal))
            {
                record.ManagedBaseTypeName = null;
            }
        }

        if (!string.IsNullOrWhiteSpace(record.ManagedBaseTypeName) &&
            !record.ManagedBaseTypeName.Contains('.', StringComparison.Ordinal) &&
            !compilation.Records.Any(candidate => !candidate.IsIgnored() && string.Equals(candidate.ManagedName, record.ManagedBaseTypeName, StringComparison.Ordinal)))
        {
            record.SetWarning($"dropping managed base type '{record.ManagedBaseTypeName}' from record '{record.ManagedName}' because the managed base type is not available for a coherent managed hierarchy; the derived record will still be generated without that base");
            record.ManagedBaseTypeName = null;
        }

        if (record.GetSecondaryBaseRecords(compilation).Any(static baseRecord =>
                baseRecord.IsIgnored() ||
                baseRecord.Members.OfType<FunctionBinding>().Any(static function =>
                    function.OperatorToken is not null ||
                    function.IsConversionOperator)))
        {
            reason = "secondary-base interfaces with comparison operators are not yet supported coherently";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private static bool TryGetUnsupportedFieldReason(
        FieldBinding field,
        IReadOnlyDictionary<string, RecordBinding> recordsById,
        out string reason)
    {
        if (TryGetUnsupportedTypeReason(field.Type, field.CppTypeName, recordsById, disallowNonPointerHandles: true, out reason))
        {
            reason = $"field type is unsupported in this bootstrap surface: {reason}";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private static bool TryGetUnsupportedTypeReason(
        BindingType type,
        string rawCppTypeName,
        IReadOnlyDictionary<string, RecordBinding> recordsById,
        bool disallowNonPointerHandles,
        out string reason)
    {
        if (type.Kind == BindingTypeKind.Handle &&
            !string.IsNullOrWhiteSpace(type.RecordId) &&
            recordsById.TryGetValue(type.RecordId, out var record) &&
            record.IsIgnored())
        {
            reason = $"record '{record.CppQualifiedName}' is ignored: {record.GetIgnoreReason() ?? "unsupported record surface"}";
            return true;
        }

        if (type.Kind == BindingTypeKind.Handle &&
            !string.IsNullOrWhiteSpace(type.RecordId) &&
            !recordsById.ContainsKey(type.RecordId))
        {
            reason = $"external handle type '{rawCppTypeName}' is not owned by this compilation";
            return true;
        }

        if (type.Kind == BindingTypeKind.Primitive && type.PrimitiveKind == BindingPrimitiveKind.Unsupported)
        {
            if (string.Equals(type.ManagedTypeName, rawCppTypeName, StringComparison.Ordinal))
            {
                reason = $"configured marshaller-backed type '{rawCppTypeName}' is not yet supported";
                return true;
            }
        }

        if (type.ManagedTypeName.Contains("::", StringComparison.Ordinal) &&
            !type.ManagedTypeName.StartsWith("global::", StringComparison.Ordinal))
        {
            reason = $"managed type name '{type.ManagedTypeName}' is not yet renderer-ready C#";
            return true;
        }

        if (type.ManagedTypeName.Contains(' ', StringComparison.Ordinal) ||
            type.ManagedTypeName.Contains("><", StringComparison.Ordinal))
        {
            reason = $"managed type name '{type.ManagedTypeName}' is not yet renderer-ready C#";
            return true;
        }

        if (type.ManagedTypeName.StartsWith("BaseVector", StringComparison.Ordinal) ||
            type.ManagedTypeName.StartsWith("BaseIntegerVector", StringComparison.Ordinal))
        {
            reason = $"managed type name '{type.ManagedTypeName}' is not yet renderer-ready C#";
            return true;
        }

        if (type.Kind == BindingTypeKind.Handle &&
            recordsById.TryGetValue(type.RecordId ?? string.Empty, out var managedRecord) &&
            (managedRecord.ManagedName.StartsWith("BaseVector", StringComparison.Ordinal) ||
             managedRecord.ManagedName.StartsWith("BaseIntegerVector", StringComparison.Ordinal)))
        {
            reason = $"managed type name '{managedRecord.ManagedName}' is not yet renderer-ready C#";
            return true;
        }

        if (disallowNonPointerHandles && type.Kind == BindingTypeKind.Handle && !rawCppTypeName.Contains('*', StringComparison.Ordinal))
        {
            reason = $"record value/reference type '{rawCppTypeName}' is not yet supported";
            return true;
        }

        if (rawCppTypeName.Contains('<', StringComparison.Ordinal) && !IsSupportedGenericHandleType(type, recordsById))
        {
            reason = $"template/container type '{rawCppTypeName}' is not yet supported";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private static bool IsSupportedGenericHandleType(BindingType type, IReadOnlyDictionary<string, RecordBinding> recordsById)
    {
        return type.Kind == BindingTypeKind.Handle &&
               !string.IsNullOrWhiteSpace(type.RecordId) &&
               recordsById.TryGetValue(type.RecordId, out var record) &&
               record.IsGeneric;
    }

    private static bool IsSupportedOperatorShape(FunctionBinding function)
    {
        return function switch
        {
            { OperatorToken: "+", Parameters.Count: 0 } => true,
            { OperatorToken: "+", Parameters.Count: 1 } => true,
            { OperatorToken: "-", Parameters.Count: 0 } => true,
            { OperatorToken: "!", Parameters.Count: 0 } => true,
            { OperatorToken: "==" or "!=" or "<" or ">", Parameters.Count: 1 } => true,
            { IsConversionOperator: true, Parameters.Count: 0 } => true,
            _ => false
        };
    }

    private static bool HasCompanionOperator(RecordBinding record, FunctionBinding function, string companionToken)
    {
        return record.Members.OfType<FunctionBinding>().Any(candidate =>
            candidate.OperatorToken == companionToken &&
            candidate.Parameters.Count == function.Parameters.Count &&
            candidate.Parameters.Select(static parameter => parameter.CppTypeName)
                .SequenceEqual(function.Parameters.Select(static parameter => parameter.CppTypeName), StringComparer.Ordinal));
    }

    private static bool IsSelfComparisonOperator(RecordBinding record, FunctionBinding function)
    {
        if (function.Parameters.Count != 1)
        {
            return false;
        }

        var normalizedParameterType = NormalizeComparisonType(function.Parameters[0].CppTypeName);
        var normalizedRecordType = NormalizeComparisonType(record.CppQualifiedName);
        return string.Equals(normalizedParameterType, normalizedRecordType, StringComparison.Ordinal);
    }

    private static string NormalizeComparisonType(string value)
    {
        return value
            .Replace("const ", string.Empty, StringComparison.Ordinal)
            .Replace("&", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
    }

    private static bool TryGetNamespaceExclusionReason(
        string cppQualifiedName,
        IReadOnlySet<string> allowedNamespaceRoots,
        out string reason)
    {
        if (allowedNamespaceRoots.Count == 0)
        {
            reason = string.Empty;
            return false;
        }

        var separatorIndex = cppQualifiedName.IndexOf("::", StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            reason = $"global declaration '{cppQualifiedName}' is outside the configured namespace mappings for this compilation";
            return true;
        }

        var namespaceRoot = cppQualifiedName[..separatorIndex];
        if (allowedNamespaceRoots.Contains(namespaceRoot))
        {
            // Check for internal detail namespaces — a universal C++ convention
            // indicating implementation details that should not be wrapped.
            if (cppQualifiedName.Contains("::detail::", StringComparison.Ordinal) ||
                cppQualifiedName.Contains("::Detail::", StringComparison.Ordinal) ||
                cppQualifiedName.EndsWith("::detail", StringComparison.Ordinal) ||
                cppQualifiedName.EndsWith("::Detail", StringComparison.Ordinal))
            {
                reason = $"declaration is in an internal detail namespace: '{cppQualifiedName}'";
                return true;
            }

            reason = string.Empty;
            return false;
        }

        reason = $"declaration namespace root '{namespaceRoot}' is outside the configured namespace mappings for this compilation";
        return true;
    }

    private static string GetCallableSignatureKey(FunctionBinding function)
    {
        return string.Join(
            "|",
            function.CppQualifiedName,
            function.IsStatic,
            function.IsConstructor,
            string.Join(";", function.Parameters.Select(static parameter => parameter.CppTypeName)));
    }

    private static string GetManagedCallableSignatureKey(FunctionBinding function)
    {
        return string.Join(
            "|",
            function.ManagedName,
            function.IsStatic,
            function.IsConstructor,
            string.Join(";", function.Parameters.Select(static parameter => parameter.CppTypeName)));
    }

    private static string GetManagedPublicCallableSignatureKey(FunctionBinding function)
    {
        return string.Join(
            "|",
            function.ManagedName,
            function.IsStatic,
            function.IsConstructor,
            string.Join(
                ";",
                function.Parameters.Select(static parameter =>
                    string.Join(
                        ":",
                        parameter.Direction,
                        parameter.AbiKind,
                        parameter.Type.Kind,
                        parameter.Type.ManagedTypeName))));
    }

}
