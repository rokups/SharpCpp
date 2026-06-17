using SharpCpp.Model.Ast;

namespace SharpCpp.Rbfx.Generator;

public static class RbfxManagedNamespaceProjectionPass
{
    public static ManagedBindingProjection Apply(BindingCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var projection = new ManagedBindingProjection(compilation.ManagedNamespace);

        foreach (var record in compilation.Records)
        {
            projection.AddDeclaration(ResolveManagedNamespace(record.CppQualifiedName, record.HeaderIncludePath, record.ManagedNamespace ?? compilation.ManagedNamespace), record);
        }

        foreach (var enumBinding in compilation.Enums)
        {
            projection.AddDeclaration(ResolveManagedNamespace(enumBinding.CppQualifiedName, enumBinding.HeaderIncludePath, enumBinding.ManagedNamespace ?? compilation.ManagedNamespace), enumBinding);
        }

        foreach (var alias in compilation.TypeAliases)
        {
            projection.AddAlias(ResolveAliasManagedNamespace(compilation, projection, alias), alias);
        }

        compilation.SetMetadata(MetadataKeys.ManagedBindingProjection, projection);
        return projection;
    }

    private static string ResolveAliasManagedNamespace(
        BindingCompilation compilation,
        ManagedBindingProjection projection,
        BindingTypeAlias alias)
    {
        if (TryResolveTypeManagedNamespace(compilation, projection, alias.TargetType, out var targetNamespace))
        {
            return targetNamespace;
        }

        return alias.ManagedNamespace ?? compilation.ManagedNamespace;
    }

    private static bool TryResolveTypeManagedNamespace(
        BindingCompilation compilation,
        ManagedBindingProjection projection,
        BindingType type,
        out string managedNamespace)
    {
        if (type.Kind == BindingTypeKind.Handle && !string.IsNullOrWhiteSpace(type.RecordId))
        {
            var record = compilation.FindRecordBySymbolId(type.RecordId);
            if (record is not null)
            {
                managedNamespace = projection.GetManagedNamespace(record, compilation);
                return true;
            }
        }

        if (type.Kind == BindingTypeKind.Enum)
        {
            var enumBinding = compilation.Enums.FirstOrDefault(candidate =>
                string.Equals(candidate.CppQualifiedName, type.CppTypeName, StringComparison.Ordinal) ||
                string.Equals(candidate.NativeName, type.CppTypeName, StringComparison.Ordinal) ||
                string.Equals(candidate.ManagedName, type.UnaliasedManagedTypeName, StringComparison.Ordinal));
            if (enumBinding is not null)
            {
                managedNamespace = projection.GetManagedNamespace(enumBinding, compilation);
                return true;
            }
        }

        foreach (var argument in type.GenericArguments)
        {
            if (TryResolveTypeManagedNamespace(compilation, projection, argument, out managedNamespace))
            {
                return true;
            }
        }

        managedNamespace = string.Empty;
        return false;
    }

    private static string ResolveManagedNamespace(string cppQualifiedName, string? headerIncludePath, string defaultNamespace)
    {
        if (!cppQualifiedName.StartsWith("Urho3D::", StringComparison.Ordinal))
        {
            return defaultNamespace;
        }

        if (string.IsNullOrWhiteSpace(headerIncludePath))
        {
            return defaultNamespace;
        }

        var normalizedHeaderPath = headerIncludePath.Replace('\\', '/');
        const string urhoPrefix = "Urho3D/";
        if (!normalizedHeaderPath.StartsWith(urhoPrefix, StringComparison.Ordinal))
        {
            return defaultNamespace;
        }

        var parts = normalizedHeaderPath[urhoPrefix.Length..]
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length >= 2 ? $"Urho3D.{parts[0]}" : defaultNamespace;
    }
}
