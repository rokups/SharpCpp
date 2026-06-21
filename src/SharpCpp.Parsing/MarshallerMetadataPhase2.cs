using System.Text.RegularExpressions;

using SharpCpp.Model.Ast;
using Microsoft.Extensions.Logging;
using SharpCpp.Model.Logging;

namespace SharpCpp.Parsing;

/// <summary>
/// Phase 2 post-parse pass that extracts record-level metadata from
/// <c>Marshaller&lt;T&gt;</c> / <c>ReturnMarshaller&lt;T&gt;</c> specializations
/// and back-patches the <c>BindingCompilation</c>.
///
/// All Marshaller declaration discovery and member function extraction comes
/// from the ClangSharp AST (via <c>MarshallerConfigExtractor</c>). Regex is
/// only used for attribute body key=value parsing — no C++ code structure regex.
/// </summary>
public static class MarshallerMetadataPhase2
{
    private static readonly Regex PublicTypeExpression = new(
        @"public_type\s*=\s*\""(?<value>[^\""\\]*(?:\\.[^\""\\]*)*)\""",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex MarshallerTypeExpression = new(
        @"marshaller_type\s*=\s*\""(?<value>[^\""\\]*(?:\\.[^\""\\]*)*)\""",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly ILogger Logger = SharpCppLog.For(nameof(MarshallerMetadataPhase2));

    private static readonly Regex ManagedBaseTypeExpression = new(
        @"managed_base_type\s*=\s*\""(?<value>[^\""\\]*(?:\\.[^\""\\]*)*)\""",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ManagedBaseTypeConstraintsExpression = new(
        @"managed_base_type_constraints\s*=\s*\""(?<value>[^\""\\]*(?:\\.[^\""\\]*)*)\""",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex SuppressEmissionExpression = new(
        @"suppress_emission\s*=\s*true",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex OwnedHandleExpression = new(
        @"owned_handle\s*=\s*true",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ManagedNamespaceExpression = new(
        @"managed_namespace\s*=\s*\""(?<value>[^\""\\]*(?:\\.[^\""\\]*)*)\""",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Apply record-level metadata using ClangSharp AST extraction.
    /// </summary>
    public static void ApplyWithClang(
        BindingCompilation compilation,
        IEnumerable<string> marshallerFiles,
        IReadOnlyList<string> clangArgs)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(marshallerFiles);
        ArgumentNullException.ThrowIfNull(clangArgs);

        var recordsByNativeName = BuildRecordLookup(compilation.Records);

        var marshallerFilesList = marshallerFiles.Where(f => File.Exists(Path.GetFullPath(f))).ToArray();
        if (marshallerFilesList.Length == 0)
        {
            return;
        }

        var configs = MarshallerConfigExtractor.ExtractMarshallerBodies(marshallerFilesList, clangArgs);
        ApplyRecordMetadata(compilation, configs, recordsByNativeName);

        SyncPlaceholderRecords(compilation, recordsByNativeName);
        DeduplicateRecords(compilation, recordsByNativeName);
        ApplyManagedTypeFormats(compilation, recordsByNativeName);
        ApplyAliasSurfaces(compilation);
    }

    private static void ApplyRecordMetadata(
        BindingCompilation compilation,
        IReadOnlyDictionary<string, MarshallerConfigExtractor.MarshallerBodyConfig> configs,
        IDictionary<string, RecordBinding> recordsByNativeName)
    {
        foreach (var (cppTypeName, config) in configs)
        {
            var recordName = GetTargetRecordName(cppTypeName);
            if (string.IsNullOrWhiteSpace(recordName))
            {
                continue;
            }

            var record = FindRecordByName(recordsByNativeName, recordName);

            // When the record found is a generic template (e.g., BaseVector2<T>)
            // and the Marshaller config targets a concrete typedef (e.g.,
            // DoubleVector2 = BaseVector2<double>), Clang resolves the template
            // argument to the canonical type.  Skip the generic record and
            // create a synthetic one for the concrete type instead.
            if (record is not null && record.IsGeneric)
            {
                // Check cppTypeName from config — if it's a concrete (non-generic)
                // type, create a synthetic record for it.
                var configIsGeneric = cppTypeName.Contains('<', StringComparison.Ordinal);
                if (!configIsGeneric)
                {
                    record = null;
                }
            }
            if (record is null)
            {
                // Create a synthetic record when the Marshaller config
                // references a type with no RecordBinding in the compilation.
                // Gate on managed_base_type OR suppress_emission — both
                // indicate the type needs a wrapper class.
                if (!ManagedBaseTypeExpression.IsMatch(config.AttributeBody) &&
                    config.ExposedMembers.Count == 0 &&
                    !SuppressEmissionExpression.IsMatch(config.AttributeBody))
                {
                    continue;
                }

                // System header types (e.g., std::basic_string<char>) may not
                // have a RecordBinding in the compilation because the parser
                // only creates local placeholders.  Create a record so the
                // marshaller metadata can be applied and the wrapper class
                // emitted.
                // Derive a simple managed name from the native type name.
                // e.g. "std::basic_string" → "BasicString"
                var lastNs = recordName.LastIndexOf("::", StringComparison.Ordinal);
                var shortName = lastNs >= 0 ? recordName[(lastNs + 2)..] : recordName;
                var pascalName = string.Concat(
                    shortName.Split('_', StringSplitOptions.RemoveEmptyEntries)
                        .Select(static part => char.ToUpperInvariant(part[0]) + part[1..]));
                record = new RecordBinding(
                    CreateRecordSymbolId(recordName),
                    recordName,
                    pascalName,
                    config.TemplateParameterNames.Count > 0 ? recordName : cppTypeName);
                record.SetMetadata(MetadataKeys.PlaceholderRecord, false);
                record.SetMetadata(MetadataKeys.ForwardDeclaration, false);
                ApplyTemplateParameters(record, config.TemplateParameterNames);
                // ManagedNamespace will be set below from managed_namespace.
                compilation.Members.Add(record);
                IndexRecordLookup(recordsByNativeName, record);
            }

            // When a Marshaller<T> attribute provides metadata for a
            // placeholder record (e.g., std::basic_string<char> from system
            // headers whose definition is not in the owned include tree),
            // promote the placeholder to a "real" record so the renderer
            // emits its wrapper class instead of degrading it.
            if (record.GetMetadata<bool>(MetadataKeys.PlaceholderRecord))
            {
                record.SetMetadata(MetadataKeys.PlaceholderRecord, false);
                record.SetMetadata(MetadataKeys.ForwardDeclaration, false);
            }

            ApplyTemplateParameters(record, config.TemplateParameterNames);

            var body = config.AttributeBody;

            if (OwnedHandleExpression.IsMatch(body))
            {
                record.SetMetadata(MetadataKeys.MarshallerConfiguredRecord, true);
            }

            // public_type → ManagedTypeFormat / ManagedName
            if (PublicTypeExpression.Match(body) is { Success: true } publicTypeMatch)
            {
                var publicType = publicTypeMatch.Groups["value"].Value;
                // When suppress_emission is set on this record (hand-written
                // C# struct replaces the auto-generated wrapper), the
                // public_type from the Marshaller takes precedence over any
                // rename attribute — the two represent different types.
                var hasExplicitRename = record.GetMetadata<bool>(MetadataKeys.ExplicitManagedRename);
                var overrideRename = SuppressEmissionExpression.IsMatch(body) &&
                    !string.Equals(record.ManagedTypeFormat, publicType, StringComparison.Ordinal);

                if (!overrideRename &&
                    hasExplicitRename &&
                    record.ManagedTypeFormat is not null &&
                    !string.Equals(record.ManagedTypeFormat, publicType, StringComparison.Ordinal))
                {
                    Logger.LogWarning(
                        "Record '{NativeName}' has [[sharpcpp::rename(\"{ManagedTypeFormat}\")]] " +
                        "but marshaller declares managed_type(public_type = \"{PublicType}\"). " +
                        "The rename attribute takes precedence.",
                        record.NativeName, record.ManagedTypeFormat, publicType);
                }
                else if (record.ManagedTypeFormat is null || overrideRename)
                {
                    record.ManagedTypeFormat = publicType;
                    record.ManagedName = GetManagedTypeHead(publicType);
                }
            }

            // marshaller_type → HandleMarshallerTypeName
            if (MarshallerTypeExpression.Match(body) is { Success: true } marshallerTypeMatch)
            {
                record.HandleMarshallerTypeName = marshallerTypeMatch.Groups["value"].Value;
            }

            // suppress_emission = true → SuppressEmission metadata
            if (SuppressEmissionExpression.IsMatch(body))
            {
                record.SetMetadata(MetadataKeys.SuppressEmission, true);
                // Clear generic parameters so type references use the
                // simple non-generic name (e.g. "Vector3" not "Vector3<T>").
                // The hand-written C# struct replaces the auto-generated
                // generic wrapper.
                record.GenericParameters.Clear();
                record.SupportedGenericInstantiations.Clear();
            }

            // managed_namespace → ManagedNamespace
            if (ManagedNamespaceExpression.Match(body) is { Success: true } nsMatch)
            {
                record.ManagedNamespace = nsMatch.Groups["value"].Value;
            }

            // managed_base_type → ManagedBaseTypeName
            if (ManagedBaseTypeExpression.Match(body) is { Success: true } baseTypeMatch)
            {
                record.ManagedBaseTypeName = baseTypeMatch.Groups["value"].Value;
                record.ManagedTypeFormat ??= record.ManagedName;

                var simpleName = GetSimpleTypeName(record.ManagedTypeFormat ?? record.ManagedName);
                if (!string.Equals(record.ManagedName, simpleName, StringComparison.Ordinal))
                {
                    record.ManagedName = simpleName;
                }
            }

            // managed_base_type_constraints → ManagedBaseTypeConstraints
            if (ManagedBaseTypeConstraintsExpression.Match(body) is { Success: true } constraintsMatch)
            {
                record.ManagedBaseTypeConstraints = constraintsMatch.Groups["value"].Value;
            }

            if (!string.IsNullOrWhiteSpace(config.ManagedInstanceCacheFactoryExpression))
            {
                record.ManagedInstanceCacheFactoryExpression = config.ManagedInstanceCacheFactoryExpression;
            }

            if (!string.IsNullOrWhiteSpace(config.ManagedInstanceCacheTypeName))
            {
                record.ManagedInstanceCacheTypeName = config.ManagedInstanceCacheTypeName;
            }

            if (!config.EmitDefaultNativeReleaseOverride)
            {
                record.EmitDefaultNativeReleaseOverride = false;
            }

            // expose_member methods — metadata comes from AST cursors, not source regex.
            foreach (var member in config.ExposedMembers)
            {
                var managedReturnType = MapExposedMemberType(member.CppReturnType, record, recordsByNativeName) ?? BindingType.Void;

                var function = new FunctionBinding(
                    $"method:{record.NativeName}:{member.MethodName}",
                    member.MethodName,
                    member.MethodName,
                    managedReturnType,
                    $"{compilation.EntryPointPrefix}_{SanitizeNativeEntrypointName(record.NativeName)}_{member.MethodName}",
                    member.CppReturnType,
                    $"{record.CppQualifiedName}::{member.MethodName}")
                {
                    IsStatic = false,
                    AccessModifier = member.Shape == "protected_override" ? BindingAccessModifier.Protected : BindingAccessModifier.Public,
                    IsConstMethod = member.IsConst && member.Parameters.Count == 0,
                    IsOverride = member.Shape == "protected_override",
                    OwningRecordCppTypeName = record.CppQualifiedName
                };
                function.SetMetadata(MetadataKeys.MarshallerExposedMethod, cppTypeName);

                // Skip the first parameter (self-reference passed by the Marshaller infrastructure).
                foreach (var (paramName, paramCppType) in member.Parameters.Skip(1))
                {
                    function.Parameters.Add(new ParameterBinding(
                        $"parameter:{record.NativeName}:{member.MethodName}:{paramName}",
                        paramName,
                        paramName,
                        MapExposedMemberType(paramCppType, record, recordsByNativeName) ?? BindingType.Void,
                        BindingParameterDirection.In,
                        BindingParameterAbiKind.Value,
                        paramCppType));
                }

                record.Members.Add(function);
            }
        }
    }

    private static RecordBinding? FindRecordByName(
        IDictionary<string, RecordBinding> recordsByNativeName,
        string recordName)
    {
        if (recordsByNativeName.TryGetValue(recordName, out var record))
        {
            return record;
        }

        record = recordsByNativeName.Values.FirstOrDefault(candidate =>
            string.Equals(StripGenericArguments(candidate.CppQualifiedName), recordName, StringComparison.Ordinal) ||
            string.Equals(candidate.CppQualifiedName, recordName, StringComparison.Ordinal));
        if (record is not null)
        {
            return record;
        }

        var separatorIndex = recordName.LastIndexOf("::", StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            return recordsByNativeName.Values.FirstOrDefault(candidate =>
                string.Equals(candidate.CppQualifiedName, recordName, StringComparison.Ordinal) ||
                (candidate.NativeName.StartsWith(recordName, StringComparison.Ordinal) &&
                 (candidate.NativeName.Length == recordName.Length || candidate.NativeName[recordName.Length] == '<')));
        }

        var unqualified = separatorIndex >= 0 ? recordName[(separatorIndex + 2)..] : recordName;
        if (recordsByNativeName.TryGetValue(unqualified, out record))
        {
            return record;
        }

        return null;
    }

    private static Dictionary<string, RecordBinding> BuildRecordLookup(IEnumerable<RecordBinding> records)
    {
        var result = new Dictionary<string, RecordBinding>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            IndexRecordLookup(result, record);
        }

        return result;
    }

    private static void IndexRecordLookup(IDictionary<string, RecordBinding> recordsByNativeName, RecordBinding record)
    {
        if (!string.IsNullOrWhiteSpace(record.CppQualifiedName))
        {
            recordsByNativeName[record.CppQualifiedName] = record;
            recordsByNativeName[StripGenericArguments(record.CppQualifiedName)] = record;
        }

        if (!string.IsNullOrWhiteSpace(record.NativeName))
        {
            recordsByNativeName.TryAdd(record.NativeName, record);
        }
    }

    private static void SyncPlaceholderRecords(BindingCompilation compilation, IReadOnlyDictionary<string, RecordBinding> recordsByNativeName)
    {
        foreach (var placeholder in compilation.Records.Where(r => r.GetMetadata<bool>(MetadataKeys.PlaceholderRecord)).ToArray())
        {
            var realRecord = recordsByNativeName.Values.FirstOrDefault(r =>
                !r.GetMetadata<bool>(MetadataKeys.PlaceholderRecord) &&
                (string.Equals(r.CppQualifiedName, placeholder.CppQualifiedName, StringComparison.Ordinal) ||
                 string.Equals(StripGenericArguments(r.CppQualifiedName), StripGenericArguments(placeholder.CppQualifiedName), StringComparison.Ordinal)));
            if (realRecord is null)
            {
                continue;
            }

            if (ShouldKeepDistinctManagedSurface(placeholder, realRecord))
            {
                placeholder.SetMetadata(MetadataKeys.PlaceholderRecord, false);
                placeholder.SetMetadata(MetadataKeys.ForwardDeclaration, false);
                continue;
            }

            realRecord.ManagedBaseTypeName ??= placeholder.ManagedBaseTypeName;
            realRecord.ManagedBaseTypeConstraints ??= placeholder.ManagedBaseTypeConstraints;
            realRecord.ManagedTypeFormat ??= placeholder.ManagedTypeFormat;
            if (!string.Equals(realRecord.ManagedName, placeholder.ManagedName, StringComparison.Ordinal) &&
                realRecord.ManagedTypeFormat is null)
            {
                realRecord.ManagedName = placeholder.ManagedName;
            }
            realRecord.HandleMarshallerTypeName ??= placeholder.HandleMarshallerTypeName;
            realRecord.ManagedInstanceCacheFactoryExpression ??= placeholder.ManagedInstanceCacheFactoryExpression;
            realRecord.ManagedInstanceCacheTypeName ??= placeholder.ManagedInstanceCacheTypeName;
            realRecord.EmitDefaultNativeReleaseOverride &= placeholder.EmitDefaultNativeReleaseOverride;
            realRecord.HasCopyConstructor |= placeholder.HasCopyConstructor;

            foreach (var member in placeholder.Members.OfType<FunctionBinding>().ToArray())
            {
                if (!realRecord.Members.OfType<FunctionBinding>().Any(m =>
                        string.Equals(m.ManagedName, member.ManagedName, StringComparison.Ordinal)))
                {
                    placeholder.Members.Remove(member);
                    realRecord.Members.Add(member);
                }
            }

            placeholder.SetMetadata(MetadataKeys.SuppressEmission, true);
        }
    }

    private static bool ShouldKeepDistinctManagedSurface(RecordBinding placeholder, RecordBinding realRecord)
    {
        if (!IsConfiguredManagedSurface(placeholder))
        {
            return false;
        }

        var placeholderNamespace = placeholder.ManagedNamespace ?? string.Empty;
        var realNamespace = realRecord.ManagedNamespace ?? string.Empty;
        var placeholderType = placeholder.ManagedTypeFormat ?? placeholder.ManagedName;
        var realType = realRecord.ManagedTypeFormat ?? realRecord.ManagedName;
        return !string.Equals(placeholderNamespace, realNamespace, StringComparison.Ordinal) ||
            !string.Equals(placeholderType, realType, StringComparison.Ordinal);
    }

    private static bool IsConfiguredManagedSurface(RecordBinding record)
    {
        return !string.IsNullOrWhiteSpace(record.ManagedBaseTypeName) ||
            !string.IsNullOrWhiteSpace(record.ManagedBaseTypeConstraints) ||
            !string.IsNullOrWhiteSpace(record.ManagedTypeFormat) ||
            !string.IsNullOrWhiteSpace(record.ManagedNamespace) ||
            !string.IsNullOrWhiteSpace(record.HandleMarshallerTypeName) ||
            record.Members.OfType<FunctionBinding>().Any(static member => member.HasMetadata(MetadataKeys.MarshallerExposedMethod));
    }

    private static void DeduplicateRecords(BindingCompilation compilation, IDictionary<string, RecordBinding> recordsByNativeName)
    {
        var duplicateGroups = compilation.Records
            .GroupBy(static record => record.SymbolId, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .ToArray();

        if (duplicateGroups.Length == 0)
        {
            return;
        }

        foreach (var group in duplicateGroups)
        {
            var preferred = group
                .OrderByDescending(GetRecordSpecificityScore)
                .ThenByDescending(static record => record.Members.Count)
                .First();

            foreach (var duplicate in group.Where(record => !ReferenceEquals(record, preferred)))
            {
                MergeRecordInto(preferred, duplicate);
                duplicate.SetMetadata(MetadataKeys.SuppressEmission, true);
                duplicate.Detach();
            }

            IndexRecordLookup(recordsByNativeName, preferred);
        }

        compilation.InvalidateCaches();
    }

    private static int GetRecordSpecificityScore(RecordBinding record)
    {
        var score = 0;

        if (!record.GetMetadata<bool>(MetadataKeys.PlaceholderRecord))
        {
            score += 10;
        }

        if (!string.IsNullOrWhiteSpace(record.ManagedBaseTypeName))
        {
            score += 10;
        }

        if (!string.IsNullOrWhiteSpace(record.ManagedBaseTypeConstraints))
        {
            score += 5;
        }

        score += record.Members.Count;
        score += record.GenericParameters.Count;

        return score;
    }

    private static void MergeRecordInto(RecordBinding target, RecordBinding source)
    {
        target.ManagedBaseTypeName ??= source.ManagedBaseTypeName;
        target.ManagedBaseTypeConstraints ??= source.ManagedBaseTypeConstraints;
        target.ManagedTypeFormat ??= source.ManagedTypeFormat;
        target.ManagedNamespace ??= source.ManagedNamespace;
        target.HandleMarshallerTypeName ??= source.HandleMarshallerTypeName;
        target.ManagedInstanceCacheFactoryExpression ??= source.ManagedInstanceCacheFactoryExpression;
        target.ManagedInstanceCacheTypeName ??= source.ManagedInstanceCacheTypeName;
        target.EmitDefaultNativeReleaseOverride &= source.EmitDefaultNativeReleaseOverride;
        target.HasCopyConstructor |= source.HasCopyConstructor;

        if (string.IsNullOrWhiteSpace(target.CppQualifiedName) && !string.IsNullOrWhiteSpace(source.CppQualifiedName))
        {
            target.CppQualifiedName = source.CppQualifiedName;
        }

        if (target.GenericParameters.Count == 0)
        {
            foreach (var parameter in source.GenericParameters)
            {
                target.GenericParameters.Add(new BindingGenericParameter(parameter.Name, parameter.Position));
            }
        }

        foreach (var member in source.Members.OfType<BindingNode>().ToArray())
        {
            if (member is FunctionBinding sourceFunction &&
                target.Members.OfType<FunctionBinding>().Any(candidate =>
                    string.Equals(candidate.ManagedName, sourceFunction.ManagedName, StringComparison.Ordinal) &&
                    string.Equals(candidate.CppQualifiedName, sourceFunction.CppQualifiedName, StringComparison.Ordinal)))
            {
                continue;
            }

            member.Detach();
            target.Members.Add(member);
        }

        foreach (var metadata in source.Metadata)
        {
            if (string.Equals(metadata.Key, MetadataKeys.SuppressEmission, StringComparison.Ordinal))
            {
                continue;
            }

            if (!target.HasMetadata(metadata.Key))
            {
                target.SetMetadata(metadata.Key, metadata.Value);
            }
        }
    }

    private static string GetTargetRecordName(string target)
    {
        var withoutPointer = target.TrimEnd('*').Trim();
        var genericStart = withoutPointer.IndexOf('<');
        return genericStart >= 0 ? withoutPointer[..genericStart] : withoutPointer;
    }

    private static string GetManagedTypeHead(string managedTypeFormat)
    {
        // Strip C++ namespace qualifiers (::) — only the local type name
        // belongs in the managed head.  Namespace placement is handled
        // separately via ManagedNamespace.
        var lastColon = managedTypeFormat.LastIndexOf("::", StringComparison.Ordinal);
        var localName = lastColon >= 0 ? managedTypeFormat[(lastColon + 2)..] : managedTypeFormat;

        var genericStart = localName.IndexOf('<');
        return genericStart >= 0 ? localName[..genericStart] : localName;
    }

    private static string GetSimpleTypeName(string managedTypeFormat)
    {
        var typeHead = StripGenericArguments(managedTypeFormat);
        var lastDot = typeHead.LastIndexOf('.');
        return lastDot >= 0 ? typeHead[(lastDot + 1)..] : typeHead;
    }

    private static void ApplyTemplateParameters(RecordBinding record, IReadOnlyList<string> templateParameterNames)
    {
        if (templateParameterNames.Count == 0)
        {
            return;
        }

        record.CppQualifiedName = StripGenericArguments(record.CppQualifiedName);

        if (record.GenericParameters.Count > 0)
        {
            return;
        }

        foreach (var (name, index) in templateParameterNames.Select((name, index) => (name, index)))
        {
            record.GenericParameters.Add(new BindingGenericParameter(name, index));
        }
    }

    private static BindingType? MapExposedMemberType(
        string cppType,
        RecordBinding record,
        IDictionary<string, RecordBinding> recordsByNativeName)
    {
        var normalized = MarshallerConfigExtractor.NormalizeTypeName(cppType);
        var stripped = StripExposedTypeQualifiers(normalized);

        var genericParameter = record.GenericParameters.FirstOrDefault(parameter =>
            string.Equals(parameter.Name, stripped, StringComparison.Ordinal));
        if (genericParameter is not null)
        {
            return BindingType.CreateGenericParameter(genericParameter.Name);
        }

        var primitiveType = MapManagedPrimitiveType(stripped);
        if (primitiveType is not null)
        {
            return primitiveType;
        }

        var recordMatch = FindRecordByName(recordsByNativeName, StripGenericArguments(stripped));
        if (recordMatch is not null)
        {
            return BindingType.CreateHandle(
                normalized.EndsWith("*", StringComparison.Ordinal) ? normalized : $"{normalized}*",
                recordMatch.ManagedName,
                recordMatch.SymbolId,
                recordMatch.HandleMarshallerTypeName);
        }

        return null;
    }

    private static string StripExposedTypeQualifiers(string cppType)
    {
        var stripped = cppType;
        if (stripped.StartsWith("const ", StringComparison.Ordinal))
        {
            stripped = stripped["const ".Length..].TrimStart();
        }

        while (stripped.EndsWith("&", StringComparison.Ordinal) || stripped.EndsWith("*", StringComparison.Ordinal))
        {
            stripped = stripped[..^1].TrimEnd();
        }

        return stripped;
    }

    private static BindingType? MapManagedPrimitiveType(string cppType)
    {
        var normalized = MarshallerConfigExtractor.NormalizeTypeName(cppType);
        return normalized switch
        {
            "void" => BindingType.Void,
            "bool" => BindingType.Boolean,
            "char" => BindingType.Int8,
            "unsigned char" => BindingType.UInt8,
            "short" => BindingType.Int16,
            "unsigned short" => BindingType.UInt16,
            "int" => BindingType.Int32,
            "unsigned int" => BindingType.UInt32,
            "long" => BindingType.Int64,
            "unsigned long" => BindingType.UInt64,
            "long long" => BindingType.Int64,
            "unsigned long long" => BindingType.UInt64,
            "float" => BindingType.Single,
            "double" => BindingType.Double,
            "std::size_t" => BindingType.UInt64,
            _ => null
        };
    }

    private static string SanitizeNativeEntrypointName(string name)
    {
        return name
            .Replace("::", "_", StringComparison.Ordinal)
            .Replace("<", "_", StringComparison.Ordinal)
            .Replace(">", "_", StringComparison.Ordinal)
            .Replace(" ", "_", StringComparison.Ordinal);
    }

    private static void ApplyManagedTypeFormats(BindingCompilation compilation, IReadOnlyDictionary<string, RecordBinding> recordsByNativeName)
    {
        // Build alias lookup for stamping generic argument names during
        // suppressed-emission type conversion.
        var aliasByTarget = new Dictionary<BindingAliasTargetKey, string>();
        foreach (var alias in compilation.TypeAliases)
        {
            var targetKey = BindingAliasTargetKey.From(alias.TargetType);
            if (!aliasByTarget.ContainsKey(targetKey))
            {
                aliasByTarget[targetKey] = alias.ManagedName;
            }
        }

        foreach (var function in compilation.Functions)
        {
            function.ReturnType = ApplyManagedTypeFormat(function.ReturnType, recordsByNativeName, aliasByTarget);
            for (var i = 0; i < function.Parameters.Count; i++)
            {
                function.Parameters[i].Type = ApplyManagedTypeFormat(function.Parameters[i].Type, recordsByNativeName, aliasByTarget);
            }
        }

        foreach (var field in compilation.Descendants<FieldBinding>())
        {
            field.Type = ApplyManagedTypeFormat(field.Type, recordsByNativeName, aliasByTarget);
        }

        foreach (var method in compilation.Records.SelectMany(static record => record.Members.OfType<FunctionBinding>()))
        {
            method.ReturnType = ApplyManagedTypeFormat(method.ReturnType, recordsByNativeName, aliasByTarget);
            for (var i = 0; i < method.Parameters.Count; i++)
            {
                method.Parameters[i].Type = ApplyManagedTypeFormat(method.Parameters[i].Type, recordsByNativeName, aliasByTarget);
            }
        }

        // Update alias target types with record managed type formats, so the
        // generated "using Alias = TargetType;" emits correctly formatted
        // target type names (e.g. "Std.Vector<Std.String>").
        for (var i = 0; i < compilation.TypeAliases.Count; i++)
        {
            var alias = compilation.TypeAliases[i];
            var updatedTarget = ApplyManagedTypeFormat(alias.TargetType, recordsByNativeName, aliasByTarget);
            if (!ReferenceEquals(updatedTarget, alias.TargetType))
            {
                compilation.TypeAliases[i] = alias with { TargetType = updatedTarget };
            }
        }

        // Prune aliases that would produce invalid generated C#.
        for (var i = compilation.TypeAliases.Count - 1; i >= 0; i--)
        {
            var alias = compilation.TypeAliases[i];
            if (ContainsGenericParameter(alias.TargetType) ||
                !CanRenderAliasTarget(alias.TargetType, recordsByNativeName))
            {
                compilation.TypeAliases.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Stamps alias surface names on function/method parameter and return
    /// types whose underlying managed type identity matches a registered
    /// alias target.  Matching by fully-qualified <c>ManagedTypeName</c>
    /// ensures each closed generic instantiation gets its own alias.
    /// </summary>
    public static void ApplyAliasSurfaces(BindingCompilation compilation)
    {
        if (compilation.TypeAliases.Count == 0)
        {
            return;
        }

        // Build a lookup from target ManagedTypeName → alias ManagedName.
        var aliasByTarget = new Dictionary<BindingAliasTargetKey, string>();
        var aliasByCppType = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var alias in compilation.TypeAliases)
        {
            var targetKey = BindingAliasTargetKey.From(alias.TargetType);
            if (!aliasByTarget.ContainsKey(targetKey))
            {
                aliasByTarget[targetKey] = alias.ManagedName;
            }

            if (!aliasByCppType.ContainsKey(alias.TargetType.CppTypeName))
            {
                aliasByCppType[alias.TargetType.CppTypeName] = alias.ManagedName;
            }
        }

        if (aliasByTarget.Count == 0 && aliasByCppType.Count == 0)
        {
            return;
        }

        foreach (var function in compilation.Functions)
        {
            function.ReturnType = StampAlias(function.ReturnType, aliasByTarget, aliasByCppType);
            for (var i = 0; i < function.Parameters.Count; i++)
            {
                function.Parameters[i].Type = StampAlias(function.Parameters[i].Type, aliasByTarget, aliasByCppType);
            }
        }

        foreach (var method in compilation.Records.SelectMany(static record => record.Members.OfType<FunctionBinding>()))
        {
            method.ReturnType = StampAlias(method.ReturnType, aliasByTarget, aliasByCppType);
            for (var i = 0; i < method.Parameters.Count; i++)
            {
                method.Parameters[i].Type = StampAlias(method.Parameters[i].Type, aliasByTarget, aliasByCppType);
            }
        }

        // Stamp aliases on generic instantiation type arguments.
        foreach (var record in compilation.Records)
        {
            foreach (var instantiation in record.SupportedGenericInstantiations)
            {
                for (var i = 0; i < instantiation.TypeArguments.Count; i++)
                {
                    instantiation.UpdateTypeArgument(i, StampAlias(instantiation.TypeArguments[i], aliasByTarget, aliasByCppType));
                }
            }
        }
    }

    private static BindingType StampAlias(BindingType type, IReadOnlyDictionary<BindingAliasTargetKey, string> aliasByTarget)
    {
        return StampAlias(type, aliasByTarget, new Dictionary<string, string>(StringComparer.Ordinal));
    }

    private static BindingType StampAlias(
        BindingType type,
        IReadOnlyDictionary<BindingAliasTargetKey, string> aliasByTarget,
        IReadOnlyDictionary<string, string> aliasByCppType)
    {
        if (type.Kind == BindingTypeKind.Primitive && type.PrimitiveKind != BindingPrimitiveKind.Unsupported)
        {
            return type;
        }

        if (type.IsCallback)
        {
            var callbackReturnType = type.CallbackReturnType is null
                ? null
                : StampAlias(type.CallbackReturnType, aliasByTarget, aliasByCppType);
            var callbackParameterTypes = type.CallbackParameterTypes
                .Select(parameterType => StampAlias(parameterType, aliasByTarget, aliasByCppType))
                .ToArray();
            if (callbackReturnType is not null &&
                (!ReferenceEquals(callbackReturnType, type.CallbackReturnType) ||
                 !callbackParameterTypes.SequenceEqual(type.CallbackParameterTypes)))
            {
                type = type.WithCallbackSignature(callbackReturnType, callbackParameterTypes);
            }
        }
        else if (type.GenericArguments.Count > 0)
        {
            var genericArguments = type.GenericArguments
                .Select(argument => StampAlias(argument, aliasByTarget, aliasByCppType))
                .ToArray();
            if (!genericArguments.SequenceEqual(type.GenericArguments))
            {
                type = type.WithGenericArguments(genericArguments);
            }
        }

        var targetKey = BindingAliasTargetKey.From(type.WithManagedTypeFormatOverride(null));
        if ((aliasByTarget.TryGetValue(targetKey, out var aliasName) ||
                aliasByCppType.TryGetValue(type.CppTypeName, out aliasName)) &&
            !string.Equals(aliasName, type.ManagedTypeName, StringComparison.Ordinal))
        {
            type = type.WithManagedTypeFormatOverride(aliasName);
        }

        return type;
    }

    private static bool ContainsGenericParameter(BindingType type)
    {
        if (type.Kind == BindingTypeKind.GenericParameter)
        {
            return true;
        }

        foreach (var argument in type.GenericArguments)
        {
            if (ContainsGenericParameter(argument))
            {
                return true;
            }
        }

        return false;
    }

    private static bool CanRenderAliasTarget(BindingType type, IReadOnlyDictionary<string, RecordBinding> recordsByNativeName)
    {
        if (type.Kind == BindingTypeKind.GenericParameter)
        {
            return false;
        }

        if (type.Kind is BindingTypeKind.Primitive or BindingTypeKind.Enum)
        {
            return true;
        }

        if (type.Kind != BindingTypeKind.Handle || string.IsNullOrWhiteSpace(type.RecordId))
        {
            return false;
        }

        var record = FindRecordByType(type, recordsByNativeName);
        if (record is not null && type.GenericArguments.Count > 0 && !ContainsGenericParameter(type))
        {
            return true;
        }

        return record is not null &&
            (!record.GetMetadata<bool>(MetadataKeys.PlaceholderRecord) ||
                !string.IsNullOrWhiteSpace(record.ManagedTypeFormat) ||
                !string.IsNullOrWhiteSpace(record.ManagedNamespace) ||
                !string.IsNullOrWhiteSpace(record.HandleMarshallerTypeName));
    }

    private static BindingType ApplyManagedTypeFormat(BindingType type, IReadOnlyDictionary<string, RecordBinding> recordsByNativeName, IReadOnlyDictionary<BindingAliasTargetKey, string> aliasByTarget)
    {
        if (type.Kind != BindingTypeKind.Handle || string.IsNullOrWhiteSpace(type.RecordId))
        {
            return type;
        }

        var formattedGenericArguments = new List<BindingType>(type.GenericArguments.Count);
        var genericArgumentsChanged = false;
        foreach (var genericArg in type.GenericArguments)
        {
            StampAlias(genericArg, aliasByTarget);
            var formattedGenericArg = ApplyManagedTypeFormat(genericArg, recordsByNativeName, aliasByTarget);
            formattedGenericArguments.Add(formattedGenericArg);
            genericArgumentsChanged |= !ReferenceEquals(formattedGenericArg, genericArg);
        }

        if (genericArgumentsChanged)
        {
            type = WithGenericArguments(type, formattedGenericArguments);
        }

        var record = FindRecordByType(type, recordsByNativeName);

        if (record is null)
        {
            return type;
        }

        type = RebindRecordId(type, record);

        // When the backing record is suppressed (hand-written C# struct
        // replaces the auto-generated wrapper), convert the Handle type to
        // a Primitive configured type so the renderer treats it as a
        // value type rather than a NativeObject-derived handle.
        if (record.GetMetadata<bool>(MetadataKeys.SuppressEmission))
        {
            var publicName = record.ManagedTypeFormat ?? record.ManagedName;

            // Stamp the suppressed-emission name on the original handle type
            // so that when this type appears as a generic argument of an outer
            // type (e.g., Ea.Vector<IntVector3>), the outer type renders the
            // correct managed name even though the returned Configured type may
            // be discarded by recursive callers.
            type.SetManagedTypeFormatOverride(publicName);

            if (record.IsGeneric && type.GenericArguments.Count > 0)
            {
                // Stamp aliases on generic arguments so suppressed-emission
                // type conversion uses the alias name (e.g., IntVector3)
                // instead of the raw type name (BaseIntegerVector3<int>).
                foreach (var genericArg in type.GenericArguments)
                {
                    StampAlias(genericArg, aliasByTarget);
                }

                var argNames = type.GenericArguments
                    .Select(static argument => argument.ManagedTypeName)
                    .Cast<object>()
                    .ToArray();
                publicName = TryFormatManagedTypeFormat(publicName, argNames, out var formattedPublicName)
                    ? formattedPublicName
                    : $"{StripGenericArguments(publicName)}<{string.Join(", ", type.GenericArguments.Select(static argument => argument.ManagedTypeName))}>";
            }
            return BindingType.CreateConfigured(
                type.CppTypeName,
                publicName,
                record.HandleMarshallerTypeName);
        }

        type = ProjectManagedGenericArguments(type, record, recordsByNativeName);
        type.SetManagedTypeFormatOverride(GetManagedTypeFormatOverride(record));
        return type;
    }

    private static BindingType RebindRecordId(BindingType type, RecordBinding record)
    {
        if (string.Equals(type.RecordId, record.SymbolId, StringComparison.Ordinal))
        {
            return type;
        }

        var rebound = BindingType.CreateHandle(
            type.CppTypeName,
            type.Marshaller.PublicTypeName,
            record.SymbolId,
            type.MarshallerTypeName,
            type.GenericArguments,
            type.Marshaller.CppIsRecordValue);
        rebound.SetManagedTypeFormatOverride(type.ManagedTypeFormatOverride);
        return rebound;
    }

    private static BindingType ProjectManagedGenericArguments(
        BindingType type,
        RecordBinding record,
        IReadOnlyDictionary<string, RecordBinding> recordsByNativeName)
    {
        var visibleIndices = GetVisibleManagedGenericArgumentIndices(record);
        if (visibleIndices.Count == 0)
        {
            return type;
        }

        if (visibleIndices.Count == type.GenericArguments.Count && IsIdentityProjection(visibleIndices))
        {
            return type;
        }

        var sourceArguments = type.GenericArguments;
        if (visibleIndices.Any(index => index >= sourceArguments.Count) &&
            TryRecoverGenericArgumentsFromCppType(type.CppTypeName, recordsByNativeName, out var recoveredArguments) &&
            recoveredArguments.Count > sourceArguments.Count)
        {
            var mergedArguments = recoveredArguments.ToArray();
            for (var index = 0; index < type.GenericArguments.Count && index < mergedArguments.Length; index++)
            {
                mergedArguments[index] = type.GenericArguments[index];
            }

            sourceArguments = mergedArguments;
        }

        if (visibleIndices.Any(index => index >= sourceArguments.Count))
        {
            return type;
        }

        var projectedArguments = visibleIndices
            .Select(index => sourceArguments[index])
            .ToArray();
        return WithGenericArguments(type, projectedArguments);
    }

    private static bool IsIdentityProjection(IReadOnlyList<int> indices)
    {
        for (var index = 0; index < indices.Count; index++)
        {
            if (indices[index] != index)
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryRecoverGenericArgumentsFromCppType(
        string cppTypeName,
        IReadOnlyDictionary<string, RecordBinding> recordsByNativeName,
        out IReadOnlyList<BindingType> arguments)
    {
        if (!TryGetTopLevelTemplateArguments(cppTypeName.TrimEnd('*', '&').Trim(), out var argumentNames))
        {
            arguments = Array.Empty<BindingType>();
            return false;
        }

        arguments = argumentNames
            .Select(argument => MapRecoveredTemplateArgument(argument, recordsByNativeName))
            .ToArray();
        return true;
    }

    private static BindingType MapRecoveredTemplateArgument(string argument, IReadOnlyDictionary<string, RecordBinding> recordsByNativeName)
    {
        var normalized = StripCvAndIndirection(MarshallerConfigExtractor.NormalizeTypeName(argument));
        if (MapManagedPrimitiveType(normalized) is { } primitive)
        {
            return primitive;
        }

        var record = recordsByNativeName.Values.FirstOrDefault(candidate =>
            string.Equals(candidate.CppQualifiedName, StripGenericArguments(normalized), StringComparison.Ordinal) ||
            string.Equals(candidate.NativeName, StripGenericArguments(normalized), StringComparison.Ordinal) ||
            normalized.EndsWith("::" + candidate.NativeName, StringComparison.Ordinal));
        if (record is not null)
        {
            var handleCppTypeName = normalized.EndsWith("*", StringComparison.Ordinal) ? normalized : normalized + "*";
            return BindingType.CreateHandle(handleCppTypeName, record.ManagedName, record.SymbolId, record.HandleMarshallerTypeName);
        }

        return BindingType.CreateConfigured(normalized, normalized);
    }

    private static bool TryGetTopLevelTemplateArguments(string typeName, out IReadOnlyList<string> arguments)
    {
        var genericStart = typeName.IndexOf('<');
        var genericEnd = typeName.LastIndexOf('>');
        if (genericStart < 0 || genericEnd <= genericStart)
        {
            arguments = Array.Empty<string>();
            return false;
        }

        arguments = SplitTopLevelArguments(typeName[(genericStart + 1)..genericEnd]);
        return true;
    }

    private static IReadOnlyList<string> SplitTopLevelArguments(string value)
    {
        var result = new List<string>();
        var depth = 0;
        var segmentStart = 0;
        for (var index = 0; index < value.Length; index++)
        {
            switch (value[index])
            {
                case '<':
                case '(':
                case '[':
                    depth++;
                    break;
                case '>':
                case ')':
                case ']':
                    depth = Math.Max(0, depth - 1);
                    break;
                case ',' when depth == 0:
                    result.Add(MarshallerConfigExtractor.NormalizeTypeName(value[segmentStart..index]));
                    segmentStart = index + 1;
                    break;
            }
        }

        result.Add(MarshallerConfigExtractor.NormalizeTypeName(value[segmentStart..]));
        return result;
    }

    private static string StripCvAndIndirection(string typeName)
    {
        var result = typeName;
        while (result.StartsWith("const ", StringComparison.Ordinal) || result.StartsWith("volatile ", StringComparison.Ordinal))
        {
            result = result.StartsWith("const ", StringComparison.Ordinal)
                ? result[6..].TrimStart()
                : result[9..].TrimStart();
        }

        return result.TrimEnd('&').Trim();
    }

    private static IReadOnlyList<int> GetVisibleManagedGenericArgumentIndices(RecordBinding record)
    {
        var indices = new SortedSet<int>();
        CollectManagedFormatPlaceholderIndices(record.ManagedTypeFormat, indices);
        CollectManagedFormatPlaceholderIndices(record.ManagedBaseTypeName, indices);
        return indices.ToArray();
    }

    private static void CollectManagedFormatPlaceholderIndices(string? format, ISet<int> target)
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            return;
        }

        for (var index = 0; index < format.Length; index++)
        {
            if (format[index] != '{')
            {
                continue;
            }

            var closeIndex = format.IndexOf('}', index + 1);
            if (closeIndex <= index + 1)
            {
                continue;
            }

            if (int.TryParse(format[(index + 1)..closeIndex], out var placeholderIndex) &&
                placeholderIndex >= 0)
            {
                target.Add(placeholderIndex);
            }

            index = closeIndex;
        }
    }

    private static BindingType WithGenericArguments(BindingType type, IReadOnlyList<BindingType> genericArguments)
    {
        if (type.Kind != BindingTypeKind.Handle)
        {
            return type;
        }

        var clone = BindingType.CreateHandle(
            type.CppTypeName,
            type.Marshaller.PublicTypeName,
            type.RecordId,
            type.MarshallerTypeName,
            genericArguments,
            type.Marshaller.CppIsRecordValue);
        clone.SetManagedTypeFormatOverride(type.ManagedTypeFormatOverride);
        return clone;
    }

    private static string GetManagedTypeFormatOverride(RecordBinding record)
    {
        if (!string.IsNullOrWhiteSpace(record.ManagedTypeFormat))
        {
            return record.ManagedTypeFormat!;
        }

        if (!record.IsGeneric)
        {
            return record.ManagedName;
        }

        return $"{record.ManagedName}<{string.Join(", ", record.GenericParameters.Select(static parameter => $"{{{parameter.Position}}}"))}>";
    }

    private static string StripGenericArguments(string name)
    {
        var genericStart = name.IndexOf('<');
        return genericStart >= 0 ? name[..genericStart] : name;
    }

    private static bool TryFormatManagedTypeFormat(string format, object[] arguments, out string formatted)
    {
        if (!format.Contains('{', StringComparison.Ordinal))
        {
            formatted = string.Empty;
            return false;
        }

        try
        {
            formatted = string.Format(System.Globalization.CultureInfo.InvariantCulture, format, arguments);
            return true;
        }
        catch (FormatException)
        {
            formatted = string.Empty;
            return false;
        }
    }

    private static RecordBinding? FindRecordByType(BindingType type, IReadOnlyDictionary<string, RecordBinding> recordsByNativeName)
    {
        var record = recordsByNativeName.Values.FirstOrDefault(candidate => string.Equals(candidate.SymbolId, type.RecordId, StringComparison.Ordinal));
        if (record is not null || type.RecordId is null || !type.RecordId.StartsWith("record:", StringComparison.Ordinal))
        {
            return record;
        }

        var qualifiedName = StripGenericArguments(type.RecordId["record:".Length..]);
        return recordsByNativeName.Values.FirstOrDefault(candidate =>
            string.Equals(candidate.CppQualifiedName, qualifiedName, StringComparison.Ordinal) ||
            candidate.CppQualifiedName.EndsWith("::" + qualifiedName, StringComparison.Ordinal) ||
            string.Equals(candidate.NativeName, qualifiedName, StringComparison.Ordinal) ||
            string.Equals(candidate.ManagedName, qualifiedName, StringComparison.Ordinal));
    }

    private static string CreateRecordSymbolId(string cppQualifiedName)
        => $"record:{MarshallerConfigExtractor.NormalizeTypeName(cppQualifiedName)}";
}
