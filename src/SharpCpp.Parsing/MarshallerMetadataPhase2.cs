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

    private static readonly Regex ExposeMemberAttributeExpression = new(
        @"\[\s*\[\s*sharpcpp::expose_member\s*\(\s*(?<attr>[^)]*?)\s*\)\s*\]\s*\]",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ShapeExpression = new(
        @"shape\s*=\s*\""(?<value>[^\""]+)\""",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ExposedExpression = new(
        @"exposed\s*=\s*(?<value>true|false)",
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

                var simpleName = GetSimpleTypeName(record.ManagedTypeFormat ?? record.ManagedName);
                if (!string.Equals(record.ManagedName, simpleName, StringComparison.Ordinal))
                {
                    record.ManagedName = simpleName;
                }
            }

            var members = match.Groups["members"].Value;
            ApplyExposedMembers(record, target, members);
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

    private static string GetSimpleTypeName(string managedTypeFormat)
    {
        var lastDot = managedTypeFormat.LastIndexOf('.');
        return lastDot >= 0 ? managedTypeFormat[(lastDot + 1)..] : managedTypeFormat;
    }

    private static void ApplyExposedMembers(RecordBinding record, string marshallerCppType, string members)
    {
        var matches = ExposeMemberAttributeExpression.Matches(members);
        if (matches.Count == 0)
        {
            return;
        }

        foreach (Match match in matches)
        {
            var attrBody = match.Groups["attr"].Value;
            var shape = ShapeExpression.Match(attrBody) is { Success: true } shapeMatch
                ? shapeMatch.Groups["value"].Value
                : "method";
            var exposed = ExposedExpression.Match(attrBody) is { Success: true } exposedMatch
                ? string.Equals(exposedMatch.Groups["value"].Value, "true", StringComparison.OrdinalIgnoreCase)
                : true;

            var methodStart = match.Index + match.Length;
            var rest = members[methodStart..];
            var methodInfo = ParseExposedMethodSignature(rest);
            if (methodInfo is null)
            {
                continue;
            }

            var (methodName, cppReturnType, isConst, parameters) = methodInfo.Value;

            var managedReturnType = MapManagedPrimitiveType(cppReturnType) ?? BindingType.Void;
            var nativeName = methodName;

            var function = new FunctionBinding(
                $"method:{record.NativeName}:{nativeName}",
                nativeName,
                methodName,
                managedReturnType,
                $"sharpcpp_e2e_{SanitizeNativeEntrypointName(record.NativeName)}_{nativeName}",
                cppReturnType,
                $"{record.CppQualifiedName}::{nativeName}")
            {
                IsStatic = false,
                AccessModifier = shape == "protected_override" ? BindingAccessModifier.Protected : BindingAccessModifier.Public,
                IsConstMethod = isConst && !parameters.Any(),
                IsOverride = shape == "protected_override",
                OwningRecordCppTypeName = record.CppQualifiedName
            };
            function.SetMetadata(MetadataKeys.MarshallerExposedMethod, marshallerCppType);

            foreach (var (paramName, paramCppType) in parameters.Skip(1))
            {
                function.Parameters.Add(new ParameterBinding(
                    $"parameter:{record.NativeName}:{nativeName}:{paramName}",
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

    private static (string MethodName, string CppReturnType, bool IsConst, List<(string Name, string CppType)> Parameters)?
        ParseExposedMethodSignature(string text)
    {
        var staticMatch = Regex.Match(text, @"static\s+");
        if (!staticMatch.Success)
        {
            return null;
        }

        var afterStatic = text[(staticMatch.Index + staticMatch.Length)..];
        var parenIndex = afterStatic.IndexOf('(');
        if (parenIndex < 0)
        {
            return null;
        }

        var beforeParams = afterStatic[..parenIndex].Trim();
        var paramEnd = afterStatic.IndexOf(')', parenIndex);
        if (paramEnd < 0)
        {
            return null;
        }

        var paramsText = afterStatic[(parenIndex + 1)..paramEnd].Trim();
        var returnTypeAndName = beforeParams.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        if (returnTypeAndName.Length < 2)
        {
            return null;
        }

        var methodName = returnTypeAndName[^1];
        var isConst = returnTypeAndName.Length >= 3 && string.Equals(returnTypeAndName[^2], "const", StringComparison.Ordinal);

        var returnTypeParts = isConst
            ? returnTypeAndName[..^2]
            : returnTypeAndName[..^1];
        var cppReturnType = string.Join(" ", returnTypeParts);

        var parameters = new List<(string Name, string CppType)>();
        if (paramsText.Length > 0)
        {
            foreach (var param in SplitParameters(paramsText))
            {
                var trimmed = param.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                var paramParts = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                var paramName = paramParts[^1];
                var paramType = string.Join(" ", paramParts[..^1]);
                parameters.Add((paramName, paramType));
            }
        }

        return (methodName, cppReturnType, isConst, parameters);
    }

    private static IEnumerable<string> SplitParameters(string paramsText)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < paramsText.Length; i++)
        {
            var ch = paramsText[i];
            if (ch == '<')
            {
                depth++;
            }
            else if (ch == '>')
            {
                depth--;
            }
            else if (ch == ',' && depth == 0)
            {
                yield return paramsText[start..i];
                start = i + 1;
            }
        }

        if (start < paramsText.Length)
        {
            yield return paramsText[start..];
        }
    }

    private static BindingType? MapManagedPrimitiveType(string cppType)
    {
        var normalized = NormalizeTypeName(cppType);
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
