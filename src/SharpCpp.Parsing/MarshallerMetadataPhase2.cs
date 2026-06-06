using System.Text.RegularExpressions;

using SharpCpp.Model.Ast;

namespace SharpCpp.Parsing;

public static class MarshallerMetadataPhase2
{
    private static readonly Regex ManagedTypePartialMarshallerExpression = new(
        @"template\s*<(?<templateParameters>.*?)>\s*struct\s*\[\[\s*sharpcpp::managed_type\((?<body>.*?)\)\s*\]\]\s*Marshaller\s*<\s*(?<target>.*?)\s*>\s*\{(?<members>.*?)\};",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex ManagedTypeExactMarshallerExpression = new(
        @"template\s*<>\s*struct\s*\[\[\s*sharpcpp::managed_type\((?<body>.*?)\)\s*\]\]\s*Marshaller\s*<\s*(?<target>.*?)\s*>\s*\{(?<members>.*?)\};",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex PublicTypeExpression = new(
        @"public_type\s*=\s*\""(?<value>[^\""\\]*(?:\\.[^\""\\]*)*)\""",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex MarshallerTypeExpression = new(
        @"marshaller_type\s*=\s*\""(?<value>[^\""\\]*(?:\\.[^\""\\]*)*)\""",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ManagedBaseTypeExpression = new(
        @"managed_base_type\s*=\s*\""(?<value>[^\""\\]*(?:\\.[^\""\\]*)*)\""",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static void Apply(BindingCompilation compilation, IEnumerable<string> metadataFiles)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(metadataFiles);

        var recordsByNativeName = compilation.Records.ToDictionary(static record => record.NativeName, StringComparer.Ordinal);

        foreach (var metadataFile in metadataFiles.Select(Path.GetFullPath).Distinct(StringComparer.Ordinal))
        {
            if (!File.Exists(metadataFile))
            {
                continue;
            }

            var sourceText = File.ReadAllText(metadataFile);
            ApplyMatches(recordsByNativeName, ManagedTypePartialMarshallerExpression.Matches(sourceText));
            ApplyMatches(recordsByNativeName, ManagedTypeExactMarshallerExpression.Matches(sourceText));
        }

        SyncPlaceholderRecords(compilation, recordsByNativeName);
        ApplyManagedTypeFormats(compilation, recordsByNativeName);
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

    private static void ApplyMatches(IDictionary<string, RecordBinding> recordsByNativeName, MatchCollection matches)
    {
        foreach (Match match in matches)
        {
            if (!match.Success)
            {
                continue;
            }

            var target = NormalizeTypeName(match.Groups["target"].Value);
            var recordName = GetTargetRecordName(target);
            if (string.IsNullOrWhiteSpace(recordName))
            {
                continue;
            }

            if (!recordsByNativeName.TryGetValue(recordName, out var record))
            {
                var separatorIndex = recordName.LastIndexOf("::", StringComparison.Ordinal);
                var unqualified = separatorIndex >= 0 ? recordName[(separatorIndex + 2)..] : recordName;
                if (!recordsByNativeName.TryGetValue(unqualified, out record))
                {
                    record = recordsByNativeName.FirstOrDefault(kvp =>
                        kvp.Key.StartsWith(unqualified, StringComparison.Ordinal) &&
                        (kvp.Key.Length == unqualified.Length || kvp.Key[unqualified.Length] == '<')).Value;
                }
            }

            if (record is null)
            {
                continue;
            }

            var body = match.Groups["body"].Value;
            if (PublicTypeExpression.Match(body) is { Success: true } publicTypeMatch)
            {
                var publicType = publicTypeMatch.Groups["value"].Value;
                if (record.ManagedTypeFormat is not null && !string.Equals(record.ManagedTypeFormat, publicType, StringComparison.Ordinal))
                {
                    Console.Error.WriteLine(
                        $"SharpCpp warning: record '{record.NativeName}' has [[sharpcpp::rename(\"{record.ManagedTypeFormat}\")]] " +
                        $"but marshaller declares managed_type(public_type = \"{publicType}\"). " +
                        $"The rename attribute takes precedence.");
                }
                else if (record.ManagedTypeFormat is null)
                {
                    record.ManagedTypeFormat = publicType;
                    record.ManagedName = GetManagedTypeHead(publicType);
                }
            }

            if (MarshallerTypeExpression.Match(body) is { Success: true } marshallerTypeMatch)
            {
                record.HandleMarshallerTypeName = marshallerTypeMatch.Groups["value"].Value;
            }

            if (ManagedBaseTypeExpression.Match(body) is { Success: true } baseTypeMatch)
            {
                record.ManagedBaseTypeName = baseTypeMatch.Groups["value"].Value;
                record.ManagedTypeFormat ??= record.ManagedName;

                if (string.Equals(record.ManagedBaseTypeName, "global::SharpCpp.Support.StringBase", StringComparison.Ordinal))
                {
                    record.ManagedName = "StdString";
                    record.ManagedTypeFormat = "StdString";
                    record.ManagedNamespace = null;
                    AddStringCoreMethods(record);
                }
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
        var genericStart = managedTypeFormat.IndexOf('<');
        return genericStart >= 0 ? managedTypeFormat[..genericStart] : managedTypeFormat;
    }

    private static string NormalizeTypeName(string spelling)
    {
        return Regex.Replace(spelling.Trim(), @"\s+", " ")
            .Replace(" *", "*", StringComparison.Ordinal)
            .Replace(" &", "&", StringComparison.Ordinal);
    }

    private static void AddStringCoreMethods(RecordBinding record)
    {
        var sanitizedName = record.NativeName
            .Replace("::", "_", StringComparison.Ordinal)
            .Replace("<", "_", StringComparison.Ordinal)
            .Replace(">", "_", StringComparison.Ordinal);

        var getCountCore = new FunctionBinding(
            $"method:{record.NativeName}:size",
            "size",
            "GetCountCore",
            BindingType.UInt64,
            $"sharpcpp_e2e_{sanitizedName}_size",
            "std::size_t",
            $"{record.CppQualifiedName}::size")
        {
            IsStatic = false,
            AccessModifier = BindingAccessModifier.Protected,
            IsConstMethod = true,
            IsOverride = true,
            OwningRecordCppTypeName = record.CppQualifiedName
        };
        record.Members.Add(getCountCore);

        var addCore = new FunctionBinding(
            $"method:{record.NativeName}:push_back",
            "push_back",
            "AddCore",
            BindingType.Void,
            $"sharpcpp_e2e_{sanitizedName}_push_back",
            "void",
            $"{record.CppQualifiedName}::push_back")
        {
            IsStatic = false,
            AccessModifier = BindingAccessModifier.Protected,
            IsOverride = true,
            OwningRecordCppTypeName = record.CppQualifiedName
        };
        addCore.Parameters.Add(new ParameterBinding(
            $"parameter:{record.NativeName}:push_back:value",
            "value",
            "value",
            BindingType.UInt8,
            BindingParameterDirection.In,
            BindingParameterAbiKind.Value,
            "unsigned char"));
        record.Members.Add(addCore);

        var clearCore = new FunctionBinding(
            $"method:{record.NativeName}:clear",
            "clear",
            "ClearCore",
            BindingType.Void,
            $"sharpcpp_e2e_{sanitizedName}_clear",
            "void",
            $"{record.CppQualifiedName}::clear")
        {
            IsStatic = false,
            AccessModifier = BindingAccessModifier.Protected,
            IsOverride = true,
            OwningRecordCppTypeName = record.CppQualifiedName
        };
        record.Members.Add(clearCore);
    }

    private static void ApplyManagedTypeFormats(BindingCompilation compilation, IReadOnlyDictionary<string, RecordBinding> recordsByNativeName)
    {
        foreach (var function in compilation.Functions)
        {
            ApplyManagedTypeFormat(function.ReturnType, recordsByNativeName);
            foreach (var parameter in function.Parameters)
            {
                ApplyManagedTypeFormat(parameter.Type, recordsByNativeName);
            }
        }

        foreach (var field in compilation.Descendants<FieldBinding>())
        {
            ApplyManagedTypeFormat(field.Type, recordsByNativeName);
        }

        foreach (var method in compilation.Records.SelectMany(static record => record.Members.OfType<FunctionBinding>()))
        {
            ApplyManagedTypeFormat(method.ReturnType, recordsByNativeName);
            foreach (var parameter in method.Parameters)
            {
                ApplyManagedTypeFormat(parameter.Type, recordsByNativeName);
            }
        }
    }

    private static void ApplyManagedTypeFormat(BindingType type, IReadOnlyDictionary<string, RecordBinding> recordsByNativeName)
    {
        foreach (var genericArgument in type.GenericArguments)
        {
            ApplyManagedTypeFormat(genericArgument, recordsByNativeName);
        }

        if (type.Kind != BindingTypeKind.Handle || string.IsNullOrWhiteSpace(type.RecordId))
        {
            return;
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
            return;
        }

        type.SetManagedTypeFormatOverride(record.ManagedTypeFormat ?? record.ManagedName);
    }

    private static string StripGenericArguments(string name)
    {
        var genericStart = name.IndexOf('<');
        return genericStart >= 0 ? name[..genericStart] : name;
    }
}
