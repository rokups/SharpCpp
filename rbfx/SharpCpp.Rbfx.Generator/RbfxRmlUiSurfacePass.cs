using SharpCpp.Model.Ast;

namespace SharpCpp.Rbfx.Generator;

public static class RbfxRmlUiSurfacePass
{
    private static readonly HashSet<string> PublicRecordNames = new(StringComparer.Ordinal)
    {
        "Rml::Context",
        "Rml::DataModelConstructor",
        "Rml::DataModelHandle",
        "Rml::Element",
        "Rml::ElementDocument",
        "Rml::Event",
        "Rml::Variant",
        "Rml::VariantList"
    };

    private static readonly string[] AllowedTypeNames =
    [
        "Rml::Context",
        "Rml::DataModelConstructor",
        "Rml::DataModelHandle",
        "Rml::Element",
        "Rml::ElementDocument",
        "Rml::Event",
        "Rml::Variant",
        "Rml::VariantList",
        "Rml::String"
    ];

    public static int Apply(BindingCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        var changed = 0;

        foreach (var record in compilation.Records.Where(static record => IsRml(record.CppQualifiedName)))
        {
            record.ManagedNamespace = "Rml";
            if (!PublicRecordNames.Contains(record.CppQualifiedName) || ShouldIgnoreRecord(record))
            {
                record.SetIgnoreReason("RmlUI declaration is outside the conservative bootstrap surface", overwriteExisting: false);
                record.SetMetadata(MetadataKeys.SilentExclusion, true);
            }
            else
            {
                record.ClearIgnoreReason();
            }

            changed += PruneUnsupportedBases(record);
            changed++;
        }

        foreach (var enumBinding in compilation.Enums.Where(static enumBinding => IsRml(enumBinding.CppQualifiedName)))
        {
            enumBinding.ManagedNamespace = "Rml";
            enumBinding.SetIgnoreReason("RmlUI enum is outside the conservative bootstrap surface", overwriteExisting: false);
            enumBinding.SetMetadata(MetadataKeys.SilentExclusion, true);
            changed++;
        }

        foreach (var function in compilation.Functions.Where(static function => IsRml(function.CppQualifiedName)))
        {
            var owner = function.Parent as RecordBinding;
            if (owner is null || !PublicRecordNames.Contains(owner.CppQualifiedName))
            {
                function.SetIgnoreReason("RmlUI callable is outside the conservative bootstrap surface", overwriteExisting: false);
                function.SetMetadata(MetadataKeys.SilentExclusion, true);
                changed++;
            }
        }

        var recordsBySymbolId = compilation.Records
            .GroupBy(static record => record.SymbolId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Last(), StringComparer.Ordinal);
        var enumsBySymbolId = compilation.Enums
            .GroupBy(static enumBinding => enumBinding.SymbolId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Last(), StringComparer.Ordinal);
        foreach (var function in compilation.Functions.Where(static function => !function.IsIgnored()))
        {
            if (FunctionUsesUnsupportedRmlType(function, recordsBySymbolId, enumsBySymbolId))
            {
                function.SetSilentExclusionReason("RmlUI callable references a type outside the conservative bootstrap surface");
                changed++;
            }
        }

        foreach (var field in compilation.Records.SelectMany(static record => record.Members.OfType<FieldBinding>()).Where(static field => !field.IsIgnored()))
        {
            if (TypeUsesUnsupportedRmlType(field.Type, field.CppTypeName, recordsBySymbolId, enumsBySymbolId))
            {
                field.SetSilentExclusionReason("RmlUI field references a type outside the conservative bootstrap surface");
                changed++;
            }
        }

        foreach (var record in compilation.Records)
        {
            changed += RemoveUnsupportedGenericInstantiations(record.SupportedGenericInstantiations, recordsBySymbolId, enumsBySymbolId);
        }

        foreach (var function in compilation.Functions)
        {
            changed += RemoveUnsupportedGenericInstantiations(function.SupportedGenericInstantiations, recordsBySymbolId, enumsBySymbolId);
        }

        changed += RemoveUnsupportedTypeAliases(compilation.TypeAliases, recordsBySymbolId, enumsBySymbolId);

        return changed;
    }

    private static bool IsRml(string cppQualifiedName)
        => cppQualifiedName.StartsWith("Rml::", StringComparison.Ordinal);

    private static bool ShouldIgnoreRecord(RecordBinding record)
        => record.IsGeneric ||
            record.CppQualifiedName.Contains("::detail::", StringComparison.Ordinal) ||
            record.CppQualifiedName.Contains("::Detail::", StringComparison.Ordinal);

    private static int PruneUnsupportedBases(RecordBinding record)
    {
        var removed = 0;
        for (var index = record.Bases.Count - 1; index >= 0; --index)
        {
            if (!ContainsUnsupportedRmlType(record.Bases[index].CppQualifiedName))
            {
                continue;
            }

            record.Bases.RemoveAt(index);
            removed++;
        }

        var primaryBase = record.Bases.FirstOrDefault();
        record.ManagedBaseTypeName = primaryBase is null || ContainsUnsupportedRmlType(primaryBase.CppQualifiedName)
            ? null
            : primaryBase.ManagedName;
        return removed;
    }

    private static bool FunctionUsesUnsupportedRmlType(
        FunctionBinding function,
        IReadOnlyDictionary<string, RecordBinding> recordsBySymbolId,
        IReadOnlyDictionary<string, EnumBinding> enumsBySymbolId)
    {
        return TypeUsesUnsupportedRmlType(function.ReturnType, function.CppReturnTypeName, recordsBySymbolId, enumsBySymbolId) ||
            function.Parameters.Any(parameter => TypeUsesUnsupportedRmlType(parameter.Type, parameter.CppTypeName, recordsBySymbolId, enumsBySymbolId));
    }

    private static bool TypeUsesUnsupportedRmlType(
        BindingType type,
        string cppTypeName,
        IReadOnlyDictionary<string, RecordBinding> recordsBySymbolId,
        IReadOnlyDictionary<string, EnumBinding> enumsBySymbolId)
    {
        if (ContainsUnsupportedRmlType(cppTypeName) || ContainsUnsupportedRmlType(type.CppTypeName))
        {
            return true;
        }

        if (type.RecordId is not null && recordsBySymbolId.TryGetValue(type.RecordId, out var record) && ContainsUnsupportedRmlType(record.CppQualifiedName))
        {
            return true;
        }

        if (type.EnumId is not null && enumsBySymbolId.TryGetValue(type.EnumId, out var enumBinding) && ContainsUnsupportedRmlType(enumBinding.CppQualifiedName))
        {
            return true;
        }

        return type.GenericArguments.Any(argument => TypeUsesUnsupportedRmlType(argument, argument.CppTypeName, recordsBySymbolId, enumsBySymbolId)) ||
            (type.FixedArrayElementType is not null && TypeUsesUnsupportedRmlType(type.FixedArrayElementType, type.FixedArrayElementType.CppTypeName, recordsBySymbolId, enumsBySymbolId)) ||
            (type.DynamicArrayElementType is not null && TypeUsesUnsupportedRmlType(type.DynamicArrayElementType, type.DynamicArrayElementType.CppTypeName, recordsBySymbolId, enumsBySymbolId)) ||
            (type.CallbackReturnType is not null && TypeUsesUnsupportedRmlType(type.CallbackReturnType, type.CallbackReturnType.CppTypeName, recordsBySymbolId, enumsBySymbolId)) ||
            type.CallbackParameterTypes.Any(parameter => TypeUsesUnsupportedRmlType(parameter, parameter.CppTypeName, recordsBySymbolId, enumsBySymbolId));
    }

    private static int RemoveUnsupportedGenericInstantiations(
        IList<RecordGenericInstantiationBinding> instantiations,
        IReadOnlyDictionary<string, RecordBinding> recordsBySymbolId,
        IReadOnlyDictionary<string, EnumBinding> enumsBySymbolId)
    {
        var removed = 0;
        for (var index = instantiations.Count - 1; index >= 0; --index)
        {
            if (!instantiations[index].TypeArguments.Any(type => TypeUsesUnsupportedRmlType(type, type.CppTypeName, recordsBySymbolId, enumsBySymbolId)))
            {
                continue;
            }

            instantiations.RemoveAt(index);
            removed++;
        }

        return removed;
    }

    private static int RemoveUnsupportedTypeAliases(
        IList<BindingTypeAlias> aliases,
        IReadOnlyDictionary<string, RecordBinding> recordsBySymbolId,
        IReadOnlyDictionary<string, EnumBinding> enumsBySymbolId)
    {
        var removed = 0;
        for (var index = aliases.Count - 1; index >= 0; --index)
        {
            if (!TypeUsesUnsupportedRmlType(aliases[index].TargetType, aliases[index].TargetType.CppTypeName, recordsBySymbolId, enumsBySymbolId))
            {
                continue;
            }

            aliases.RemoveAt(index);
            removed++;
        }

        return removed;
    }

    private static bool ContainsUnsupportedRmlType(string cppTypeName)
    {
        if (cppTypeName.Contains("Style::", StringComparison.Ordinal) ||
            cppTypeName.Contains("PropertyId", StringComparison.Ordinal))
        {
            return true;
        }

        var searchIndex = 0;
        while (true)
        {
            var rmlIndex = cppTypeName.IndexOf("Rml::", searchIndex, StringComparison.Ordinal);
            if (rmlIndex < 0)
            {
                return false;
            }

            if (!AllowedTypeNames.Any(allowed => StartsWithTypeName(cppTypeName, rmlIndex, allowed)))
            {
                return true;
            }

            searchIndex = rmlIndex + "Rml::".Length;
        }
    }

    private static bool StartsWithTypeName(string value, int startIndex, string typeName)
    {
        if (!value.AsSpan(startIndex).StartsWith(typeName, StringComparison.Ordinal))
        {
            return false;
        }

        var nextIndex = startIndex + typeName.Length;
        return nextIndex >= value.Length || !IsIdentifierOrNamespaceChar(value[nextIndex]);
    }

    private static bool IsIdentifierOrNamespaceChar(char value)
        => char.IsLetterOrDigit(value) || value == '_' || value == ':';
}
