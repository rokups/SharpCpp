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

        ApplyManagedTypeFormats(compilation, recordsByNativeName);
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
            if (string.IsNullOrWhiteSpace(recordName) || !recordsByNativeName.TryGetValue(recordName, out var record))
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
        }
    }

    private static string GetTargetRecordName(string target)
    {
        var withoutPointer = target.TrimEnd('*').Trim();
        var genericStart = withoutPointer.IndexOf('<');
        var head = genericStart >= 0 ? withoutPointer[..genericStart] : withoutPointer;
        var separatorIndex = head.LastIndexOf("::", StringComparison.Ordinal);
        return separatorIndex >= 0 ? head[(separatorIndex + 2)..] : head;
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
        if (record is null)
        {
            return;
        }

        type.SetManagedTypeFormatOverride(record.ManagedTypeFormat ?? record.ManagedName);
    }
}
