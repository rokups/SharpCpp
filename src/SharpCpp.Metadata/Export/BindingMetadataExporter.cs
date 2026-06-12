using SharpCpp.Model.Ast;

namespace SharpCpp.Metadata.Export;

public static class BindingMetadataExporter
{
    public static BindingMetadataDocument Export(BindingCompilation compilation, BindingMetadataExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        options ??= new BindingMetadataExportOptions(compilation.ProjectName);

        var types = compilation.Records
            .Where(static record => !record.IsIgnored() && !record.GetMetadata<bool>(MetadataKeys.SuppressEmission))
            .OrderBy(static record => record.ManagedName, StringComparer.Ordinal)
            .ThenBy(static record => record.SymbolId, StringComparer.Ordinal)
            .Select(CreateRecordType)
            .Concat(
                compilation.Enums
                    .Where(static @enum => !@enum.IsIgnored() && !@enum.GetMetadata<bool>(MetadataKeys.SuppressEmission))
                    .OrderBy(static @enum => @enum.ManagedName, StringComparer.Ordinal)
                    .ThenBy(static @enum => @enum.SymbolId, StringComparer.Ordinal)
                    .Select(CreateEnumType))
            .ToArray();

        var functions = compilation.Functions
            .Where(static function => function.Parent is not RecordBinding && !function.IsIgnored())
            .OrderBy(static function => function.ManagedName, StringComparer.Ordinal)
            .ThenBy(static function => function.SymbolId, StringComparer.Ordinal)
            .Select(CreateFunction)
            .ToArray();

        var imports = (options.Imports ?? Array.Empty<BindingMetadataImport>())
            .OrderBy(static importItem => importItem.Package, StringComparer.Ordinal)
            .ThenBy(static importItem => importItem.Hash, StringComparer.Ordinal)
            .ToArray();

        var fingerprint = new BindingMetadataFingerprint(
            options.Phase2ClosureHash,
            options.ClangArgsHash,
            options.AttributeVocabularyHash,
            compilation.HeaderFiles.OrderBy(static header => header, StringComparer.Ordinal).ToArray());

        return new BindingMetadataDocument(
            BindingMetadataSchema.CurrentVersion,
            options.GeneratorVersion,
            options.SupportRuntimeVersion,
            new BindingMetadataSource(options.PackageId, compilation.ManagedNamespace, compilation.NativeLibraryName),
            imports,
            fingerprint,
            types,
            functions);
    }

    private static BindingMetadataType CreateRecordType(RecordBinding record)
    {
        var kind = record.IsHandleType ? "handle" : "record";
        return new BindingMetadataType(
            record.SymbolId,
            kind,
            record.NativeName,
            record.ManagedName,
            record.ManagedNamespace,
            record.ManagedBaseTypeName,
            CreateTypeReference(record.HandleType),
            ManagedBaseTypeConstraints: record.ManagedBaseTypeConstraints,
            Members: record.Members
                .Where(static member => !member.IsIgnored())
                .Select(CreateRecordMember)
                .OrderBy(static member => member.ManagedName, StringComparer.Ordinal)
                .ToArray());
    }

    private static BindingMetadataType CreateEnumType(EnumBinding @enum)
    {
        return new BindingMetadataType(
            @enum.SymbolId,
            "enum",
            @enum.NativeName,
            @enum.ManagedName,
            @enum.ManagedNamespace,
            ManagedBaseTypeName: null,
            CreateTypeReference(BindingType.CreateEnum(@enum.NativeName, @enum.ManagedName)),
            IsFlags: @enum.IsFlags,
            Values: @enum.Values
                .OrderBy(static value => value.ManagedName, StringComparer.Ordinal)
                .Select(static value => new BindingMetadataEnumValue(value.NativeName, value.ManagedName, value.ValueExpression))
                .ToArray());
    }

    private static BindingMetadataFunction CreateFunction(FunctionBinding function)
    {
        var parameters = function.Parameters
            .OrderBy(static parameter => parameter.ManagedName, StringComparer.Ordinal)
            .ThenBy(static parameter => parameter.SymbolId, StringComparer.Ordinal)
            .Select(parameter => new BindingMetadataParameter(
                parameter.ManagedName,
                parameter.Direction.ToString().ToLowerInvariant(),
                CreateTypeReference(parameter.Type),
                parameter.ManagedDefaultValueExpression))
            .ToArray();

        return new BindingMetadataFunction(
            function.SymbolId,
            function.NativeName,
            function.ManagedName,
            function.EntryPointName,
            CreateTypeReference(function.ReturnType),
            parameters,
            function.ReturnsOwnedHandle);
    }

    private static BindingMetadataRecordMember CreateRecordMember(BindingNode member)
    {
        return member switch
        {
            FunctionBinding function => new BindingMetadataRecordMember(
                function.IsConstructor ? "constructor" : "method",
                function.NativeName,
                function.ManagedName,
                function.IsConstructor ? null : CreateTypeReference(function.ReturnType),
                function.Parameters
                    .OrderBy(static parameter => parameter.ManagedName, StringComparer.Ordinal)
                    .ThenBy(static parameter => parameter.SymbolId, StringComparer.Ordinal)
                    .Select(parameter => new BindingMetadataParameter(
                        parameter.ManagedName,
                        parameter.Direction.ToString().ToLowerInvariant(),
                        CreateTypeReference(parameter.Type),
                        parameter.ManagedDefaultValueExpression))
                    .ToArray(),
                IsReadOnly: false,
                function.IsStatic,
                function.IsConstructor,
                function.OperatorToken,
                function.IsConversionOperator,
                function.IsExplicitConversionOperator,
                function.ReturnsOwnedHandle),
            FieldBinding field => new BindingMetadataRecordMember(
                "field",
                field.NativeName,
                field.ManagedName,
                CreateTypeReference(field.Type),
                Parameters: null,
                field.IsReadOnly,
                IsStatic: false,
                IsConstructor: false),
            _ => throw new NotSupportedException($"Metadata export does not support member kind '{member.Kind}'.")
        };
    }

    private static BindingMetadataTypeReference CreateTypeReference(BindingType type)
    {
        return new BindingMetadataTypeReference(
            type.Kind.ToString().ToLowerInvariant(),
            type.CppTypeName,
            type.ManagedTypeName,
            type.Kind == BindingTypeKind.Primitive ? type.PrimitiveKind.ToString().ToLowerInvariant() : null,
            type.RecordId,
            type.MarshallerTypeName);
    }
}
