using System.Security.Cryptography;
using System.Text;

using SharpCpp.Metadata.Export;
using SharpCpp.Model.Ast;

namespace SharpCpp.Metadata.Import;

public sealed class BindingMetadataImportCatalog
{
    private readonly IReadOnlyList<BindingMetadataDocument> _documents;

    private BindingMetadataImportCatalog(IReadOnlyList<BindingMetadataDocument> documents, IReadOnlyList<BindingMetadataImport> imports)
    {
        _documents = documents;
        Imports = imports;
    }

    public static BindingMetadataImportCatalog Empty { get; } = new([], []);

    public IReadOnlyList<BindingMetadataImport> Imports { get; }

    public static BindingMetadataImportCatalog Load(IEnumerable<string> metadataFiles)
    {
        ArgumentNullException.ThrowIfNull(metadataFiles);

        var documents = new List<BindingMetadataDocument>();
        var imports = new List<BindingMetadataImport>();

        foreach (var metadataFile in metadataFiles)
        {
            var fullPath = Path.GetFullPath(metadataFile);
            var json = File.ReadAllText(fullPath);
            var document = BindingMetadataSerializer.Deserialize(json);
            if (document.SchemaVersion != BindingMetadataSchema.CurrentVersion)
            {
                throw new InvalidOperationException($"Imported metadata '{fullPath}' uses unsupported schema version '{document.SchemaVersion}'.");
            }

            documents.Add(document);
            imports.Add(new BindingMetadataImport(document.Source.Package, ComputeHash(json)));
        }

        return new BindingMetadataImportCatalog(documents, imports
            .OrderBy(static item => item.Package, StringComparer.Ordinal)
            .ThenBy(static item => item.Hash, StringComparer.Ordinal)
            .ToArray());
    }

    public void Seed(BindingCompilation compilation, IDictionary<string, RecordBinding> recordsByName, IDictionary<string, EnumBinding> enumsByName)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(recordsByName);
        ArgumentNullException.ThrowIfNull(enumsByName);

        foreach (var document in _documents)
        {
            foreach (var importedType in document.Types.OrderBy(static item => item.SymbolId, StringComparer.Ordinal))
            {
                switch (importedType.Kind)
                {
                    case MetadataTypeKind.Handle:
                    case MetadataTypeKind.Record:
                        SeedRecord(compilation, recordsByName, document, importedType);
                        break;

                    case MetadataTypeKind.Enum:
                        SeedEnum(compilation, enumsByName, document, importedType);
                        break;
                }
            }
        }
    }

    private static void SeedRecord(BindingCompilation compilation, IDictionary<string, RecordBinding> recordsByName, BindingMetadataDocument document, BindingMetadataType importedType)
    {
        var cppQualifiedName = GetQualifiedCppTypeName(importedType.Projection.CppType);
        if (recordsByName.ContainsKey(importedType.NativeName) || recordsByName.ContainsKey(cppQualifiedName))
        {
            return;
        }

        var managedTypeName = GetQualifiedManagedTypeName(importedType.ManagedNamespace ?? document.Source.ManagedNamespace, importedType.ManagedName);
        var managedBaseTypeName = string.IsNullOrWhiteSpace(importedType.ManagedBaseTypeName)
            ? null
            : ResolveQualifiedManagedBaseTypeName(document, importedType.ManagedBaseTypeName!);

        var record = new RecordBinding(
            $"import:{document.Source.Package}:{importedType.SymbolId}",
            importedType.NativeName,
            managedTypeName,
            cppQualifiedName)
        {
            ManagedNamespace = importedType.ManagedNamespace ?? document.Source.ManagedNamespace,
            ManagedBaseTypeName = managedBaseTypeName,
            ManagedBaseTypeConstraints = importedType.ManagedBaseTypeConstraints,
            ManagedInstanceCacheFactoryExpression = importedType.ManagedInstanceCacheFactoryExpression,
            ManagedInstanceCacheTypeName = importedType.ManagedInstanceCacheTypeName,
            EmitDefaultNativeReleaseOverride = importedType.EmitDefaultNativeReleaseOverride
        };
        record.SetMetadata(MetadataKeys.SuppressEmission, true);

        SeedRecordKeys(recordsByName, record);
        compilation.Members.Add(record);
    }

    private static void SeedEnum(BindingCompilation compilation, IDictionary<string, EnumBinding> enumsByName, BindingMetadataDocument document, BindingMetadataType importedType)
    {
        if (enumsByName.ContainsKey(importedType.NativeName) || enumsByName.ContainsKey(importedType.Projection.CppType))
        {
            return;
        }

        var enumBinding = new EnumBinding(
            $"import:{document.Source.Package}:{importedType.SymbolId}",
            importedType.NativeName,
            GetQualifiedManagedTypeName(importedType.ManagedNamespace ?? document.Source.ManagedNamespace, importedType.ManagedName),
            importedType.Projection.CppType,
            underlyingPrimitiveKind: BindingType.PrimitiveKindFromCppName(importedType.Projection.CppType))
        {
            ManagedNamespace = importedType.ManagedNamespace ?? document.Source.ManagedNamespace,
            IsFlags = importedType.IsFlags
        };
        enumBinding.SetMetadata(MetadataKeys.SuppressEmission, true);

        SeedEnumKeys(enumsByName, enumBinding);
        compilation.Members.Add(enumBinding);
    }

    private static void SeedRecordKeys(IDictionary<string, RecordBinding> recordsByName, RecordBinding record)
    {
        TryAdd(recordsByName, record.NativeName, record);
        TryAdd(recordsByName, record.CppQualifiedName, record);
    }

    private static void SeedEnumKeys(IDictionary<string, EnumBinding> enumsByName, EnumBinding enumBinding)
    {
        TryAdd(enumsByName, enumBinding.NativeName, enumBinding);
        TryAdd(enumsByName, enumBinding.CppQualifiedName, enumBinding);
    }

    private static void TryAdd<T>(IDictionary<string, T> dictionary, string key, T value)
    {
        if (string.IsNullOrWhiteSpace(key) || dictionary.ContainsKey(key))
        {
            return;
        }

        dictionary.Add(key, value);
    }

    private static string ResolveQualifiedManagedBaseTypeName(BindingMetadataDocument document, string managedBaseTypeName)
    {
        if (managedBaseTypeName.StartsWith("global::", StringComparison.Ordinal))
        {
            return managedBaseTypeName;
        }

        var baseType = document.Types.FirstOrDefault(candidate => string.Equals(candidate.ManagedName, managedBaseTypeName, StringComparison.Ordinal));
        if (baseType is not null)
        {
            return GetQualifiedManagedTypeName(baseType.ManagedNamespace ?? document.Source.ManagedNamespace, baseType.ManagedName);
        }

        return GetQualifiedManagedTypeName(document.Source.ManagedNamespace, managedBaseTypeName);
    }

    private static string GetQualifiedManagedTypeName(string? managedNamespace, string managedName)
    {
        if (managedName.StartsWith("global::", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(managedNamespace))
        {
            return managedName;
        }

        return $"global::{managedNamespace}.{managedName}";
    }

    private static string GetQualifiedCppTypeName(string cppType)
    {
        var normalized = cppType
            .Replace("const ", string.Empty, StringComparison.Ordinal)
            .Replace("&", string.Empty, StringComparison.Ordinal)
            .Replace("*", string.Empty, StringComparison.Ordinal)
            .Trim();

        return normalized;
    }

    private static string ComputeHash(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
