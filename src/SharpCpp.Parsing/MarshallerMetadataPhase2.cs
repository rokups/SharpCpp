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

    private static readonly Regex SuppressEmissionExpression = new(
        @"suppress_emission\s*=\s*true",
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

        var recordsByNativeName = compilation.Records.ToDictionary(static record => record.NativeName, StringComparer.Ordinal);

        var marshallerFilesList = marshallerFiles.Where(f => File.Exists(Path.GetFullPath(f))).ToArray();
        if (marshallerFilesList.Length == 0)
        {
            return;
        }

        var configs = MarshallerConfigExtractor.ExtractMarshallerBodies(marshallerFilesList, clangArgs);
        ApplyRecordMetadata(compilation, configs, recordsByNativeName);

        SyncPlaceholderRecords(compilation, recordsByNativeName);
        ApplyManagedTypeFormats(compilation, recordsByNativeName);
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
            if (record is null)
            {
                // Create a synthetic record when the Marshaller config
                // references a type with no RecordBinding in the compilation.
                // Gate on managed_base_type OR suppress_emission — both
                // indicate the type needs a wrapper class.
                if (!ManagedBaseTypeExpression.IsMatch(config.AttributeBody) &&
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
                    $"record:{recordName}",
                    recordName,
                    pascalName,
                    cppTypeName)
                {
                    HeaderIncludePath = $"foreign/{recordName}.hpp"
                };
                record.SetMetadata(MetadataKeys.PlaceholderRecord, false);
                record.SetMetadata(MetadataKeys.ForwardDeclaration, false);
                // ManagedNamespace will be set below from managed_namespace.
                compilation.Members.Add(record);
                recordsByNativeName[recordName] = record;
            }

            // When a Marshaller<T> attribute provides metadata for a
            // placeholder record (e.g., std::basic_string<char> from system
            // headers whose definition is not in the owned include tree),
            // promote the placeholder to a "real" record so the renderer
            // emits its wrapper class instead of degrading it.
            if (record.HasMetadata(MetadataKeys.PlaceholderRecord))
            {
                record.SetMetadata(MetadataKeys.PlaceholderRecord, false);
                record.SetMetadata(MetadataKeys.ForwardDeclaration, false);
                // Assign a header include path so the renderer places this
                // record in a valid output file (system headers aren't in
                // the owned include tree, so they'd get no output file).
                if (string.IsNullOrWhiteSpace(record.HeaderIncludePath) ||
                    !record.HeaderIncludePath.Contains('/', StringComparison.Ordinal))
                {
                    record.HeaderIncludePath = $"foreign/{record.NativeName}.hpp";
                }
            }

            var body = config.AttributeBody;

            // public_type → ManagedTypeFormat / ManagedName
            if (PublicTypeExpression.Match(body) is { Success: true } publicTypeMatch)
            {
                var publicType = publicTypeMatch.Groups["value"].Value;
                // When suppress_emission is set on this record (hand-written
                // C# struct replaces the auto-generated wrapper), the
                // public_type from the Marshaller takes precedence over any
                // rename attribute — the two represent different types.
                var overrideRename = SuppressEmissionExpression.IsMatch(body) &&
                    !string.Equals(record.ManagedTypeFormat, publicType, StringComparison.Ordinal);

                if (!overrideRename &&
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

            // expose_member methods — metadata comes from AST cursors, not source regex.
            foreach (var member in config.ExposedMembers)
            {
                var managedReturnType = MapManagedPrimitiveType(member.CppReturnType) ?? BindingType.Void;

                var function = new FunctionBinding(
                    $"method:{record.NativeName}:{member.MethodName}",
                    member.MethodName,
                    member.MethodName,
                    managedReturnType,
                    $"sharpcpp_e2e_{SanitizeNativeEntrypointName(record.NativeName)}_{member.MethodName}",
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
                        MapManagedPrimitiveType(paramCppType) ?? BindingType.Void,
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

        var separatorIndex = recordName.LastIndexOf("::", StringComparison.Ordinal);
        var unqualified = separatorIndex >= 0 ? recordName[(separatorIndex + 2)..] : recordName;
        if (recordsByNativeName.TryGetValue(unqualified, out record))
        {
            return record;
        }

        return recordsByNativeName.FirstOrDefault(kvp =>
            kvp.Key.StartsWith(unqualified, StringComparison.Ordinal) &&
            (kvp.Key.Length == unqualified.Length || kvp.Key[unqualified.Length] == '<')).Value;
    }

    private static void SyncPlaceholderRecords(BindingCompilation compilation, IReadOnlyDictionary<string, RecordBinding> recordsByNativeName)
    {
        foreach (var placeholder in compilation.Records.Where(r => r.HasMetadata(MetadataKeys.PlaceholderRecord)).ToArray())
        {
            var realRecord = recordsByNativeName.Values.FirstOrDefault(r =>
                !r.HasMetadata(MetadataKeys.PlaceholderRecord) &&
                string.Equals(r.CppQualifiedName, placeholder.CppQualifiedName, StringComparison.Ordinal));
            if (realRecord is null)
            {
                continue;
            }

            realRecord.ManagedBaseTypeName ??= placeholder.ManagedBaseTypeName;
            realRecord.ManagedTypeFormat ??= placeholder.ManagedTypeFormat;
            if (!string.Equals(realRecord.ManagedName, placeholder.ManagedName, StringComparison.Ordinal) &&
                realRecord.ManagedTypeFormat is null)
            {
                realRecord.ManagedName = placeholder.ManagedName;
            }
            realRecord.HandleMarshallerTypeName ??= placeholder.HandleMarshallerTypeName;

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

    private static string GetTargetRecordName(string target)
    {
        var withoutPointer = target.TrimEnd('*').Trim();
        var genericStart = withoutPointer.IndexOf('<');
        return genericStart >= 0 ? withoutPointer[..genericStart] : withoutPointer;
    }

    private static string GetManagedTypeHead(string managedTypeFormat)
    {
        var genericStart = managedTypeFormat.IndexOf('<');
        return genericStart >= 0 ? managedTypeFormat[..genericStart] : managedTypeFormat;
    }

    private static string GetSimpleTypeName(string managedTypeFormat)
    {
        var lastDot = managedTypeFormat.LastIndexOf('.');
        return lastDot >= 0 ? managedTypeFormat[(lastDot + 1)..] : managedTypeFormat;
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
        foreach (var function in compilation.Functions)
        {
            function.ReturnType = ApplyManagedTypeFormat(function.ReturnType, recordsByNativeName);
            for (var i = 0; i < function.Parameters.Count; i++)
            {
                function.Parameters[i].Type = ApplyManagedTypeFormat(function.Parameters[i].Type, recordsByNativeName);
            }
        }

        foreach (var field in compilation.Descendants<FieldBinding>())
        {
            field.Type = ApplyManagedTypeFormat(field.Type, recordsByNativeName);
        }

        foreach (var method in compilation.Records.SelectMany(static record => record.Members.OfType<FunctionBinding>()))
        {
            method.ReturnType = ApplyManagedTypeFormat(method.ReturnType, recordsByNativeName);
            for (var i = 0; i < method.Parameters.Count; i++)
            {
                method.Parameters[i].Type = ApplyManagedTypeFormat(method.Parameters[i].Type, recordsByNativeName);
            }
        }
    }

    private static BindingType ApplyManagedTypeFormat(BindingType type, IReadOnlyDictionary<string, RecordBinding> recordsByNativeName)
    {
        if (type.Kind != BindingTypeKind.Handle || string.IsNullOrWhiteSpace(type.RecordId))
        {
            return type;
        }

        // Recurse into generic arguments (in-place mutation via override,
        // since GenericArguments is IReadOnlyList and can't be reassigned).
        foreach (var genericArg in type.GenericArguments)
        {
            var _ = ApplyManagedTypeFormat(genericArg, recordsByNativeName);
        }

        var record = recordsByNativeName.Values.FirstOrDefault(candidate => string.Equals(candidate.SymbolId, type.RecordId, StringComparison.Ordinal));
        if (record is null && type.RecordId.StartsWith("record:", StringComparison.Ordinal))
        {
            var qualifiedName = StripGenericArguments(type.RecordId["record:".Length..]);
            record = recordsByNativeName.Values.FirstOrDefault(candidate =>
                string.Equals(candidate.CppQualifiedName, qualifiedName, StringComparison.Ordinal) ||
                candidate.CppQualifiedName.EndsWith("::" + qualifiedName, StringComparison.Ordinal) ||
                string.Equals(candidate.NativeName, qualifiedName, StringComparison.Ordinal) ||
                string.Equals(candidate.ManagedName, qualifiedName, StringComparison.Ordinal));
        }

        if (record is null)
        {
            return type;
        }

        // When the backing record is suppressed (hand-written C# struct
        // replaces the auto-generated wrapper), convert the Handle type to
        // a Primitive configured type so the renderer treats it as a
        // value type rather than a NativeObject-derived handle.
        if (record.GetMetadata<bool>(MetadataKeys.SuppressEmission))
        {
            return BindingType.CreateConfigured(
                type.CppTypeName,
                record.ManagedTypeFormat ?? record.ManagedName,
                record.HandleMarshallerTypeName);
        }

        type.SetManagedTypeFormatOverride(record.ManagedTypeFormat ?? record.ManagedName);
        return type;
    }

    private static string StripGenericArguments(string name)
    {
        var genericStart = name.IndexOf('<');
        return genericStart >= 0 ? name[..genericStart] : name;
    }
}
