using System.Globalization;
using System.Text;

using SharpCpp.Model.Ast;

namespace SharpCpp.Pipeline;

/// <summary>
/// Makes each distinct instantiation of a class template with the
/// <c>flatten_instantiations</c> marshaller option into its own non-generic record.
/// </summary>
/// <remarks>
/// The pass looks at record fields. Each field that holds an instantiation of such a template
/// gets a concrete record. The name is <c>public_type</c> and the managed simple names of the
/// arguments, joined with <c>_</c>:
/// <list type="bullet">
/// <item>A function-type argument adds the names of its parameters. A mutable reference adds <c>Ref</c>. No parameters gives <c>Void</c>.</item>
/// <item>A later non-default argument adds <c>By</c> and the name for a <c>Sender</c> parameter, else the parameter name and the name.</item>
/// </list>
/// One canonical C++ spelling gives one record. The record goes into the namespace of the first
/// owner (in declaration order) that uses it. The concrete record keeps the exposed members of
/// the template, and the template record is not emitted.
/// </remarks>
public static class FlattenMarshallerInstantiationsPass
{
    public static void Apply(BindingCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var templates = compilation.Records
            .Where(static record => record.GetMetadata<bool>(MetadataKeys.MarshallerFlattenInstantiations) &&
                record.GenericParameters.Count > 0)
            .ToDictionary(static record => record.SymbolId, StringComparer.Ordinal);
        if (templates.Count == 0)
        {
            return;
        }

        var usages = compilation.Records
            // A generic owner or a dependent argument gives no concrete instantiation.
            .Where(record => !templates.ContainsKey(record.SymbolId) && record.GenericParameters.Count == 0)
            .SelectMany(static record => record.Members.OfType<FieldBinding>().Select(field => (Owner: record, Field: field)))
            .Where(usage => usage.Field.Type.RecordId is { } recordId && templates.ContainsKey(recordId) &&
                !usage.Field.Type.NativeTemplateArguments.Any(static argument => argument.Type is { } type && IsDependent(type)))
            .Select(usage => (usage.Owner, usage.Field, Template: templates[usage.Field.Type.RecordId!], Key: GetCanonicalKey(templates[usage.Field.Type.RecordId!], usage.Field.Type)))
            .ToArray();
        if (usages.Length == 0)
        {
            return;
        }

        // An argument is a default when one usage omits it. Thus the name does not depend on the spelling.
        var defaultedArguments = usages
            .SelectMany(static usage => usage.Field.Type.NativeTemplateArguments
                .Select((argument, index) => (usage.Template.SymbolId, Index: index, Argument: argument))
                .Where(static item => item.Argument.IsDefaulted))
            .Select(static item => (item.SymbolId, item.Index, FormatArgumentKey(item.Argument)))
            .ToHashSet();

        var instances = usages
            .GroupBy(static usage => usage.Key, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .Select(group => (Key: group.Key, First: group.First(), Name: GetFlattenedName(compilation, group.First().Template, group.First().Field.Type, defaultedArguments)))
            .ToArray();
        var nameCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var recordsByKey = new Dictionary<string, RecordBinding>(StringComparer.Ordinal);
        foreach (var instance in instances)
        {

            // Two spellings with one name (for example int and const int&) get a number in canonical order.
            var count = nameCounts.TryGetValue(instance.Name, out var previous) ? previous + 1 : 1;
            nameCounts[instance.Name] = count;
            var name = count == 1 ? instance.Name : $"{instance.Name}{count.ToString(CultureInfo.InvariantCulture)}";
            recordsByKey[instance.Key] = CreateConcreteRecord(compilation, instance.First.Template, instance.First.Owner, instance.First.Field.Type, instance.Key, name);
        }

        foreach (var usage in usages)
        {
            usage.Field.Type = Retarget(usage.Field.Type, recordsByKey[usage.Key]);
            // The native setter moves the value. A move takes the subscribers of the source and can free a running signal.
            usage.Field.IsReadOnly = true;
        }

        foreach (var template in templates.Values)
        {
            template.SetMetadata(MetadataKeys.SuppressEmission, true);
        }

        compilation.InvalidateCaches();
    }

    private static bool IsDependent(BindingType type) =>
        type.IsGenericParameter ||
        type.CppTypeName.Contains("type-parameter-", StringComparison.Ordinal) ||
        type.CallbackParameterTypes.Any(IsDependent) ||
        (type.CallbackReturnType is { } returnType && IsDependent(returnType)) ||
        type.NativeTemplateArguments.Any(static argument => argument.Type is { } argumentType && IsDependent(argumentType));

    private static string GetCanonicalKey(RecordBinding template, BindingType type)
    {
        if (type.NativeTemplateArguments.Count == 0)
        {
            return type.CppTypeName.TrimEnd('*', '&', ' ');
        }

        return $"{template.CppQualifiedName}<{string.Join(", ", type.NativeTemplateArguments.Select(FormatArgumentKey))}>";
    }

    private static string FormatArgumentKey(BindingTemplateArgument argument) =>
        argument.Kind == BindingTemplateArgumentKind.Integral
            ? BindingTemplateArgument.FormatIntegral(argument.IntegralValue, argument.IsBoolean)
            : argument.Type!.CppTypeName;

    private static string GetFlattenedName(
        BindingCompilation compilation,
        RecordBinding template,
        BindingType type,
        IReadOnlySet<(string, int, string)> defaultedArguments)
    {
        var publicType = GetSimpleName(template.ManagedName);
        var builder = new StringBuilder(publicType);
        var arguments = type.NativeTemplateArguments;
        for (var index = 0; index < arguments.Count; ++index)
        {
            var argument = arguments[index];
            if (argument.Kind == BindingTemplateArgumentKind.Integral)
            {
                if (!defaultedArguments.Contains((template.SymbolId, index, FormatArgumentKey(argument))))
                {
                    builder.Append('_').Append(GetParameterPrefix(template, index))
                        .Append(BindingTemplateArgument.FormatIntegral(argument.IntegralValue, argument.IsBoolean).Replace('-', 'M'));
                }

                continue;
            }

            var argumentType = argument.Type!;
            if (index == 0 && argumentType.IsCallback)
            {
                var parameters = argumentType.CallbackParameterTypes;
                builder.Append('_').Append(parameters.Count == 0
                    ? "Void"
                    : string.Join("_", parameters.Select(parameter => GetManagedSimpleName(compilation, parameter))));
                continue;
            }

            if (index == 0)
            {
                builder.Append('_').Append(GetManagedSimpleName(compilation, argumentType));
                continue;
            }

            if (!defaultedArguments.Contains((template.SymbolId, index, FormatArgumentKey(argument))))
            {
                builder.Append('_').Append(GetParameterPrefix(template, index)).Append(GetManagedSimpleName(compilation, argumentType));
            }
        }

        return builder.ToString();
    }

    private static string GetParameterPrefix(RecordBinding template, int index)
    {
        // The marshaller pattern names the argument, for example `Sender` in `sig<void(Args...), Sender>`.
        var patternArguments = GetPatternArguments(template);
        var parameter = patternArguments is not null
            ? index < patternArguments.Count && IsIdentifier(patternArguments[index]) ? patternArguments[index] : null
            : template.GenericParameters.FirstOrDefault(candidate => candidate.Position == index)?.Name;
        return parameter switch
        {
            null => string.Empty,
            "Sender" => "By",
            _ => char.ToUpperInvariant(parameter[0]) + parameter[1..]
        };
    }

    private static string GetManagedSimpleName(BindingCompilation compilation, BindingType type)
    {
        var suffix = type.Marshaller.CppIsMutableReference ? "Ref" : string.Empty;
        if (type.RecordId is { } recordId &&
            compilation.Records.FirstOrDefault(record => string.Equals(record.SymbolId, recordId, StringComparison.Ordinal)) is { } record)
        {
            return GetSimpleName(record.ManagedName) + suffix;
        }

        var publicName = type.Marshaller.PublicTypeName;
        return publicName switch
        {
            "sbyte" => "SByte",
            "ushort" => "UShort",
            "uint" => "UInt",
            "ulong" => "ULong",
            "nint" => "NInt",
            "nuint" => "NUInt",
            _ => ToIdentifier(GetSimpleName(publicName))
        } + suffix;
    }

    private static string GetSimpleName(string managedName)
    {
        var name = managedName;
        var genericStart = name.IndexOf('<');
        if (genericStart >= 0)
        {
            name = name[..genericStart];
        }

        name = name.TrimEnd('?', '*');
        var lastDot = name.LastIndexOf('.');
        return lastDot >= 0 ? name[(lastDot + 1)..] : name;
    }

    private static string ToIdentifier(string name)
    {
        var builder = new StringBuilder(name.Length);
        var upper = true;
        foreach (var character in name)
        {
            if (!char.IsLetterOrDigit(character))
            {
                upper = true;
                continue;
            }

            builder.Append(upper ? char.ToUpperInvariant(character) : character);
            upper = false;
        }

        return builder.Length == 0 ? "Type" : builder.ToString();
    }

    private static RecordBinding CreateConcreteRecord(
        BindingCompilation compilation,
        RecordBinding template,
        RecordBinding owner,
        BindingType type,
        string key,
        string name)
    {
        var record = new RecordBinding(
            $"{template.SymbolId}<{key}>",
            name,
            name,
            key,
            isHandleType: true)
        {
            ManagedBaseTypeName = template.ManagedBaseTypeName,
            ManagedBaseTypeConstraints = template.ManagedBaseTypeConstraints,
            ManagedNamespace = template.ManagedNamespace,
            IsNonCopyable = true,
            IsNonAssignable = true,
            HeaderIncludePath = owner.HeaderIncludePath ?? template.HeaderIncludePath,
            Source = template.Source
        };
        record.SetMetadata(MetadataKeys.FlattenedInstantiationOf, template.SymbolId);
        if (template.GetMetadata<bool>(MetadataKeys.MarshallerConfiguredRecord))
        {
            record.SetMetadata(MetadataKeys.MarshallerConfiguredRecord, true);
        }

        var bindings = BindPattern(compilation, template, type);
        foreach (var method in template.Members.OfType<FunctionBinding>()
            .Where(static method => method.HasMetadata(MetadataKeys.MarshallerExposedMethod)))
        {
            record.Members.Add(CloneExposedMethod(method, record, name, bindings));
        }

        // The owner's container decides the module and the managed namespace of the class.
        var container = owner.Parent as BindingContainerNode ?? template.Parent as BindingContainerNode
            ?? throw new InvalidOperationException($"Record '{owner.CppQualifiedName}' has no container.");
        container.Members.Add(record);
        return record;
    }

    /// <summary>
    /// One concrete value of a marshaller template parameter. A pack has one element for each argument.
    /// </summary>
    private sealed record BoundArgument(IReadOnlyList<string> Spellings, IReadOnlyList<BindingType> Types, bool IsPack);

    private sealed record PatternBindings(
        IReadOnlyDictionary<string, BoundArgument> Arguments,
        IReadOnlyDictionary<string, RecordBinding> RecordsById);

    /// <summary>
    /// Matches the marshaller pattern (for example <c>sig&lt;void(Args...), Sender&gt;</c>) with the
    /// native arguments of one instantiation. A bare name binds one argument. <c>R(P...)</c> binds the
    /// pack <c>P</c> to the parameters of the function-type argument.
    /// </summary>
    private static PatternBindings BindPattern(BindingCompilation compilation, RecordBinding template, BindingType type)
    {
        var arguments = new Dictionary<string, BoundArgument>(StringComparer.Ordinal);
        var recordsById = compilation.Records.ToDictionary(static record => record.SymbolId, StringComparer.Ordinal);
        var patternArguments = GetPatternArguments(template);
        var nativeArguments = type.NativeTemplateArguments;
        if (patternArguments is null)
        {
            foreach (var parameter in template.GenericParameters.Where(parameter => parameter.Position < nativeArguments.Count))
            {
                if (nativeArguments[parameter.Position].Type is { } argumentType)
                {
                    arguments[parameter.Name] = new BoundArgument([argumentType.CppTypeName], [argumentType], IsPack: false);
                }
            }

            return new PatternBindings(arguments, recordsById);
        }

        for (var index = 0; index < patternArguments.Count && index < nativeArguments.Count; ++index)
        {
            var pattern = patternArguments[index];
            if (nativeArguments[index].Type is not { } argumentType)
            {
                continue;
            }

            if (IsIdentifier(pattern))
            {
                arguments[pattern] = new BoundArgument([argumentType.CppTypeName], [argumentType], IsPack: false);
                continue;
            }

            if (argumentType.IsCallback &&
                TrySplitFunctionType(pattern, out _, out var patternParameters) &&
                patternParameters.Count == 1 &&
                patternParameters[0].EndsWith("...", StringComparison.Ordinal) &&
                patternParameters[0][..^3].Trim() is var packName &&
                IsIdentifier(packName) &&
                TrySplitFunctionType(argumentType.CppTypeName, out _, out var spellings) &&
                spellings.Count == argumentType.CallbackParameterTypes.Count)
            {
                arguments[packName] = new BoundArgument(spellings, argumentType.CallbackParameterTypes, IsPack: true);
            }
        }

        return new PatternBindings(arguments, recordsById);
    }

    private static IReadOnlyList<string>? GetPatternArguments(RecordBinding template)
    {
        var pattern = template.Members.OfType<FunctionBinding>()
            .Select(static method => method.GetMetadata<string>(MetadataKeys.MarshallerExposedMethod))
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
        if (pattern is null)
        {
            return null;
        }

        var start = pattern.IndexOf('<');
        var end = pattern.LastIndexOf('>');
        return start < 0 || end <= start ? null : SplitTopLevel(pattern[(start + 1)..end]);
    }

    private static List<string> SplitTopLevel(string text)
    {
        var result = new List<string>();
        var depth = 0;
        var start = 0;
        for (var index = 0; index < text.Length; ++index)
        {
            switch (text[index])
            {
                case '<' or '(' or '[':
                    ++depth;
                    break;
                case '>' or ')' or ']':
                    --depth;
                    break;
                case ',' when depth == 0:
                    result.Add(text[start..index].Trim());
                    start = index + 1;
                    break;
            }
        }

        var last = text[start..].Trim();
        if (last.Length > 0 || result.Count > 0)
        {
            result.Add(last);
        }

        return result;
    }

    private static bool TrySplitFunctionType(string text, out string returnType, out IReadOnlyList<string> parameters)
    {
        var start = text.IndexOf('(');
        var end = text.LastIndexOf(')');
        if (start <= 0 || end <= start)
        {
            returnType = string.Empty;
            parameters = [];
            return false;
        }

        returnType = text[..start].Trim();
        var list = text[(start + 1)..end].Trim();
        parameters = list.Length == 0 || list == "void" ? [] : SplitTopLevel(list);
        return true;
    }

    private static bool IsIdentifier(string text) =>
        text.Length > 0 &&
        (char.IsLetter(text[0]) || text[0] == '_') &&
        text.All(static character => char.IsLetterOrDigit(character) || character == '_');

    private static FunctionBinding CloneExposedMethod(
        FunctionBinding method,
        RecordBinding record,
        string name,
        PatternBindings bindings)
    {
        var marshallerType = method.GetMetadata<string>(MetadataKeys.MarshallerExposedMethod) ?? string.Empty;
        string? unsupportedReason = null;
        var returnType = method.ReturnType;
        (BindingType Type, string Spelling)? expandedReturn = null;
        var returnResult = method.CppReturnTypeName is { } cppReturnType
            ? TryExpandType(cppReturnType, bindings, out expandedReturn)
            : ExpandResult.NotTemplated;
        if (returnResult == ExpandResult.Expanded)
        {
            returnType = expandedReturn!.Value.Type;
        }
        else if (returnResult == ExpandResult.Unsupported)
        {
            unsupportedReason = $"return type '{method.CppReturnTypeName}' cannot be expanded for '{record.CppQualifiedName}'";
        }

        var clone = new FunctionBinding(
            $"method:{record.CppQualifiedName}:{method.SymbolId}",
            method.NativeName,
            method.ManagedName,
            returnType,
            $"{method.EntryPointName}_{name}",
            returnResult == ExpandResult.Expanded ? returnType.CppTypeName : method.CppReturnTypeName,
            $"{record.CppQualifiedName}::{method.NativeName}")
        {
            IsStatic = method.IsStatic,
            AccessModifier = method.AccessModifier,
            IsConstMethod = method.IsConstMethod,
            IsOverride = method.IsOverride,
            OwningRecordCppTypeName = record.CppQualifiedName
        };
        // The marshaller pattern names the template. The concrete record names one instantiation.
        clone.SetMetadata(
            MetadataKeys.MarshallerExposedMethod,
            marshallerType.EndsWith('*') ? $"{record.CppQualifiedName}*"
                : marshallerType.EndsWith('&') ? $"{record.CppQualifiedName}&"
                : record.CppQualifiedName);
        if (method.TryGetMetadata(MetadataKeys.MarshallerThrowOnFalse, out string? throwOnFalseMessage))
        {
            clone.SetMetadata(MetadataKeys.MarshallerThrowOnFalse, throwOnFalseMessage);
        }

        foreach (var parameter in method.Parameters)
        {
            var cppTypeName = parameter.CppTypeName ?? parameter.Type.CppTypeName;
            if (IsPackExpansion(cppTypeName, bindings, out var pack))
            {
                // A pack parameter `Args... args` becomes one parameter for each argument: args0, args1, ...
                for (var index = 0; index < pack.Types.Count; ++index)
                {
                    // A mutable primitive reference (`bool&`) is a `ref` parameter, as for a plain function.
                    var elementType = pack.Types[index];
                    var isRef = elementType.IsCallbackRefParameter;
                    clone.Parameters.Add(CreateParameter(
                        $"{parameter.SymbolId}:{name}:{index.ToString(CultureInfo.InvariantCulture)}",
                        $"{parameter.NativeName}{index.ToString(CultureInfo.InvariantCulture)}",
                        $"{parameter.ManagedName}{index.ToString(CultureInfo.InvariantCulture)}",
                        isRef ? elementType.WithCppIsReference(false).WithCppIsMutableReference(false) : elementType,
                        pack.Spellings[index],
                        isRef ? BindingParameterDirection.Ref : BindingParameterDirection.In,
                        isRef ? BindingParameterAbiKind.Reference : BindingParameterAbiKind.Value));
                }

                continue;
            }

            var result = TryExpandType(cppTypeName, bindings, out var expanded);
            if (result == ExpandResult.Unsupported)
            {
                unsupportedReason ??= $"parameter '{parameter.NativeName}' of type '{cppTypeName}' cannot be expanded for '{record.CppQualifiedName}'";
            }

            var clonedParameter = CreateParameter(
                $"{parameter.SymbolId}:{name}",
                parameter.NativeName,
                parameter.ManagedName,
                result == ExpandResult.Expanded ? expanded!.Value.Type : parameter.Type,
                result == ExpandResult.Expanded ? expanded!.Value.Spelling : cppTypeName,
                parameter.Direction,
                parameter.AbiKind);
            if (result != ExpandResult.Expanded)
            {
                // A parameter that does not use the pattern keeps its shape: defaults, pointer-pointer data and metadata.
                clonedParameter.ManagedDefaultValueExpression = parameter.ManagedDefaultValueExpression;
                clonedParameter.UseCallbackDelegate = parameter.UseCallbackDelegate;
                clonedParameter.CallbackDelegateTypeName = parameter.CallbackDelegateTypeName;
                clonedParameter.CallbackTakesOwnership = parameter.CallbackTakesOwnership;
                clonedParameter.PointerPointer = parameter.PointerPointer;
                clonedParameter.PointeeIsConst = parameter.PointeeIsConst;
                clonedParameter.PointerElementIsConst = parameter.PointerElementIsConst;
                foreach (var (key, value) in parameter.Metadata)
                {
                    clonedParameter.SetMetadata(key, value);
                }
            }

            clone.Parameters.Add(clonedParameter);
        }

        if (unsupportedReason is not null)
        {
            clone.SetIgnoreReason(unsupportedReason);
        }

        return clone;
    }

    private static ParameterBinding CreateParameter(
        string symbolId,
        string nativeName,
        string managedName,
        BindingType type,
        string cppTypeName,
        BindingParameterDirection direction = BindingParameterDirection.In,
        BindingParameterAbiKind abiKind = BindingParameterAbiKind.Value)
    {
        return new ParameterBinding(symbolId, nativeName, managedName, type, direction, abiKind, cppTypeName)
        {
            // A callback with a ref or ref-struct parameter needs a declared delegate (see RequiresDeclaredCallbackDelegate).
            UseCallbackDelegate = type.RequiresDeclaredCallbackDelegate
        };
    }

    private enum ExpandResult
    {
        NotTemplated,
        Expanded,
        Unsupported
    }

    private static bool IsPackExpansion(string cppTypeName, PatternBindings bindings, out BoundArgument pack)
    {
        var normalized = cppTypeName.Trim();
        if (normalized.EndsWith("...", StringComparison.Ordinal) &&
            bindings.Arguments.TryGetValue(normalized[..^3].Trim(), out var bound) &&
            bound.IsPack)
        {
            pack = bound;
            return true;
        }

        pack = null!;
        return false;
    }

    /// <summary>
    /// Replaces the marshaller template parameters in one exposed-member type. Supported forms are
    /// <c>T</c>, <c>T*</c> and <c>ManagedCallback&lt;void(...)&gt;</c> with <c>T</c>, <c>T*</c> and <c>P...</c> elements.
    /// </summary>
    private static ExpandResult TryExpandType(string cppTypeName, PatternBindings bindings, out (BindingType Type, string Spelling)? expanded)
    {
        expanded = null;
        var normalized = cppTypeName.Trim();
        var callbackStart = normalized.IndexOf("ManagedCallback<", StringComparison.Ordinal);
        if (callbackStart >= 0 && normalized.EndsWith('>'))
        {
            var signature = normalized[(callbackStart + "ManagedCallback<".Length)..^1];
            if (!MentionsBinding(signature, bindings))
            {
                return ExpandResult.NotTemplated;
            }

            if (!TrySplitFunctionType(signature, out var returnTypeName, out var elements) || returnTypeName != "void")
            {
                return ExpandResult.Unsupported;
            }

            var spellings = new List<string>();
            var types = new List<BindingType>();
            foreach (var element in elements)
            {
                if (IsPackExpansion(element, bindings, out var pack))
                {
                    spellings.AddRange(pack.Spellings);
                    types.AddRange(pack.Types);
                    continue;
                }

                if (TryExpandSingle(element, bindings, out var single) != ExpandResult.Expanded)
                {
                    return ExpandResult.Unsupported;
                }

                spellings.Add(single!.Value.Spelling);
                types.Add(single.Value.Type);
            }

            var spelling = $"SharpCpp::ManagedCallback<void ({string.Join(", ", spellings)})>";
            var format = types.Count == 0
                ? "global::System.Action"
                : $"global::System.Action<{string.Join(", ", Enumerable.Range(0, types.Count).Select(static index => $"{{{index.ToString(CultureInfo.InvariantCulture)}}}"))}>";
            expanded = (BindingType.CreateCallback(spelling, format, managedMarshaller: null, BindingType.Void, types), spelling);
            return ExpandResult.Expanded;
        }

        return TryExpandSingle(normalized, bindings, out expanded);
    }

    private static ExpandResult TryExpandSingle(string cppTypeName, PatternBindings bindings, out (BindingType Type, string Spelling)? expanded)
    {
        expanded = null;
        var normalized = cppTypeName.Trim();
        var isPointer = normalized.EndsWith('*');
        var core = isPointer ? normalized[..^1].Trim() : normalized;
        if (!bindings.Arguments.TryGetValue(core, out var bound))
        {
            return MentionsBinding(normalized, bindings) ? ExpandResult.Unsupported : ExpandResult.NotTemplated;
        }

        if (bound.IsPack || bound.Types.Count != 1)
        {
            return ExpandResult.Unsupported;
        }

        var type = bound.Types[0];
        var spelling = bound.Spellings[0];
        if (!isPointer)
        {
            expanded = (type, spelling);
            return ExpandResult.Expanded;
        }

        // A pointer to a bound record (for example `Sender*`) is a handle to its wrapper.
        if (type.RecordId is not { } recordId || !bindings.RecordsById.TryGetValue(recordId, out var record))
        {
            return ExpandResult.Unsupported;
        }

        expanded = (BindingType.CreateHandle($"{spelling}*", record.ManagedName, record.SymbolId, record.HandleMarshallerTypeName), $"{spelling}*");
        return ExpandResult.Expanded;
    }

    private static bool MentionsBinding(string cppTypeName, PatternBindings bindings) =>
        bindings.Arguments.Keys.Any(name => System.Text.RegularExpressions.Regex.IsMatch(cppTypeName, $@"\b{name}\b"));

    private static BindingType Retarget(BindingType type, RecordBinding record)
    {
        var publicTypeName = GetSimpleName(type.Marshaller.PublicTypeName) is var simple &&
            type.Marshaller.PublicTypeName.IndexOf('<') is var genericStart and >= 0 &&
            type.Marshaller.PublicTypeName[..genericStart] is var head &&
            head.EndsWith(simple, StringComparison.Ordinal)
            ? head[..^simple.Length] + record.ManagedName
            : record.ManagedName;
        var retargeted = BindingType.CreateHandle(
            type.CppTypeName,
            publicTypeName,
            record.SymbolId,
            marshallerTypeName: null,
            genericArguments: null,
            type.Marshaller.CppIsRecordValue,
            type.Marshaller.CppPointerIsRecordValueProjection)
            .WithCppIsReference(type.Marshaller.CppIsReference);
        retargeted.SetManagedTypeFormatOverride(null);
        return retargeted;
    }
}
