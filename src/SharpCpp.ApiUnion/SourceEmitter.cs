using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SharpCpp.ApiUnion;

/// <summary>
/// Emits a compilation contract and source additions from compiled, platform-specific APIs.
/// Input assemblies are never loaded for execution or changed.
/// </summary>
public sealed class SourceEmitter
{
    private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);
    private readonly SortedDictionary<string, IAssemblySymbol> assemblies = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<(string Platform, INamedTypeSymbol Type)>> types = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> aliases = new(StringComparer.Ordinal);
    private const string Throw = "throw new global::System.PlatformNotSupportedException()";

    public SourceEmitter(IReadOnlyDictionary<string, string> inputs, IReadOnlyList<string> references)
    {
        foreach (var (platform, path) in inputs.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var input = MetadataReference.CreateFromFile(path);
            var compilation = CSharpCompilation.Create("ApiUnionInspection", references:
                references.Where(reference => Path.GetFileName(reference) != Path.GetFileName(path))
                    .Select(reference => MetadataReference.CreateFromFile(reference)).Append(input),
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                    metadataImportOptions: MetadataImportOptions.All));
            var assembly = (IAssemblySymbol)compilation.GetAssemblyOrModuleSymbol(input)!;
            if (!assembly.Identity.PublicKeyToken.IsDefaultOrEmpty)
                throw new InvalidOperationException("Recompiling signed assemblies requires their signing key.");
            if (assemblies.Values.Any(other => !other.Identity.Equals(assembly.Identity)))
                throw new InvalidOperationException("All inputs must have the same assembly identity.");
            assemblies.Add(platform, assembly);
            foreach (var type in EnumerateTypes(assembly.GlobalNamespace).Where(IsVisible))
            {
                if (!types.TryGetValue(TypeKey(type), out var variants))
                    types.Add(TypeKey(type), variants = []);
                variants.Add((platform, type));
            }
        }
        if (assemblies.Count == 0)
            throw new InvalidOperationException("At least one platform input is required.");
        ValidateAndFindAliases();
    }

    public void Emit(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(Path.Combine(outputDirectory, "AssemblyIdentity.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            Name = assemblies.Values.First().Identity.Name,
            Version = assemblies.Values.First().Identity.Version.ToString(),
            Platforms = assemblies.Keys
        }));
        var reference = Header();
        foreach (var variants in types.Values.OrderBy(list => TypeKey(list[0].Type), StringComparer.Ordinal))
        {
            if (variants[0].Type.ContainingType is not null) continue;
            var namespaced = !variants[0].Type.ContainingNamespace.IsGlobalNamespace;
            if (namespaced) reference.AppendLine($"namespace {variants[0].Type.ContainingNamespace.ToDisplayString()} {{");
            WriteType(reference, variants, null, full: true);
            if (namespaced) reference.AppendLine("}");
        }
        File.WriteAllText(Path.Combine(outputDirectory, "Reference.g.cs"), reference.ToString());

        foreach (var platform in assemblies.Keys)
        {
            var stubs = Header();
            var concreteTypes = new List<string>();
            foreach (var variants in types.Values.OrderBy(list => TypeKey(list[0].Type), StringComparer.Ordinal))
            {
                var present = variants.FirstOrDefault(item => item.Platform == platform).Type;
                if (present?.IsAbstract == true && variants.Any(item => !item.Type.IsAbstract))
                    concreteTypes.Add(TypeKey(present));
                // Missing nested types are emitted inside the enclosing partial declaration.
                if (variants[0].Type.ContainingType is not null) continue;
                var namespaced = !variants[0].Type.ContainingNamespace.IsGlobalNamespace;
                if (namespaced) stubs.AppendLine($"namespace {variants[0].Type.ContainingNamespace.ToDisplayString()} {{");
                WriteType(stubs, variants, platform, full: present is null);
                if (namespaced) stubs.AppendLine("}");
            }
            Directory.CreateDirectory(Path.Combine(outputDirectory, platform));
            File.WriteAllText(Path.Combine(outputDirectory, platform, "Stubs.g.cs"), stubs.ToString());
            File.WriteAllLines(Path.Combine(outputDirectory, platform, "ConcreteTypes.txt"), concreteTypes);
        }
        File.WriteAllLines(Path.Combine(outputDirectory, "Aliases.tsv"),
            aliases.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => item.Key + "\t" + item.Value));
    }

    private void ValidateAndFindAliases()
    {
        foreach (var variants in types.Values)
        {
            var first = variants[0].Type;
            if (variants.Any(item => item.Type.TypeKind != first.TypeKind ||
                TypeName(item.Type.BaseType) != TypeName(first.BaseType) ||
                TypeName(item.Type.EnumUnderlyingType) != TypeName(first.EnumUnderlyingType)))
                throw new InvalidOperationException($"Incompatible type shape: {TypeKey(first)}.");

            var members = variants.SelectMany(item => ApiMembers(item.Type)).ToList();
            foreach (var group in members.OfType<IFieldSymbol>().Where(field => field.IsConst).GroupBy(field => field.Name))
                if (group.Select(field => Constant(field.ConstantValue, field.Type)).Distinct().Count() > 1)
                    throw new InvalidOperationException($"Platform-dependent constant: {TypeKey(first)}.{group.Key}.");
            foreach (var group in members.Where(member => member is IMethodSymbol { MethodKind: MethodKind.Ordinary } or IPropertySymbol)
                .GroupBy(CollisionKey))
            {
                var results = group.Select(ResultType).Distinct().ToList();
                if (results.Count <= 1) continue;
                foreach (var member in group)
                {
                    var suffix = ResultType(member) switch
                    {
                        "int" => "Int32", "uint" => "UInt32", "long" => "Int64", "ulong" => "UInt64",
                        "nint" => "IntPtr", "nuint" => "UIntPtr",
                        _ => throw new InvalidOperationException($"Return-only collision requires a naming policy: {CollisionKey(member)}.")
                    };
                    var name = member.Name + "As" + suffix;
                    if (members.Any(other => other.Name == name && CollisionKey(other) != CollisionKey(member)))
                        throw new InvalidOperationException($"Union alias collides with an existing member: {TypeKey(first)}.{name}.");
                    aliases[MemberKey(member)] = name;
                }
            }
        }
    }

    private void WriteType(StringBuilder output, List<(string Platform, INamedTypeSymbol Type)> variants,
        string? platform, bool full)
    {
        var type = variants[0].Type;
        var present = variants.FirstOrDefault(item => item.Platform == platform).Type;
        var grouped = variants.SelectMany(item => ApiMembers(item.Type).Select(member => (item.Platform, Member: member)))
            .GroupBy(item => MemberKey(item.Member)).ToList();
        var presentMembers = present is null ? new HashSet<string>() : ApiMembers(present).Select(MemberKey).ToHashSet();
        var interfaces = variants.SelectMany(item => item.Type.Interfaces).GroupBy(TypeName).Select(group => group.First()).ToList();
        var addedInterfaces = interfaces.Where(item => present is null || !present.Interfaces.Any(existing => TypeName(existing) == TypeName(item))).ToList();
        var nested = types.Values.Where(list => list[0].Type.ContainingType is { } parent && TypeKey(parent) == TypeKey(type)).ToList();
        var nestedOutput = new StringBuilder();
        foreach (var list in nested)
        {
            var nestedPresent = list.FirstOrDefault(item => item.Platform == platform).Type;
            WriteType(nestedOutput, list, platform, full: platform is null || nestedPresent is null);
        }
        var selected = grouped.Where(group => full || !presentMembers.Contains(group.Key) || aliases.ContainsKey(group.Key) ||
            (group.Any(item => item.Member is IPropertySymbol { SetMethod: not null }) &&
             present?.GetMembers().OfType<IPropertySymbol>().Any(property => MemberKey(property) == group.Key && property.SetMethod is null) == true) ||
            (group.Any(item => item.Member is IPropertySymbol { GetMethod: not null }) &&
             present?.GetMembers().OfType<IPropertySymbol>().Any(property => MemberKey(property) == group.Key && property.GetMethod is null) == true) ||
            (present?.GetMembers().OfType<IMethodSymbol>().Any(method => MemberKey(method) == group.Key && method.IsAbstract) == true &&
             group.Any(item => item.Member is IMethodSymbol { IsAbstract: false }))).ToList();
        if (!full && selected.Count == 0 && addedInterfaces.Count == 0 && nestedOutput.Length == 0) return;

        if (type.TypeKind == TypeKind.Delegate)
        {
            if (!full) return;
            Attributes(output, type);
            var invoke = type.DelegateInvokeMethod!;
            output.AppendLine($"{Access(type)} unsafe delegate {Return(invoke)} {Identifier(type.Name)}{TypeParameters(type.TypeParameters)}({Parameters(invoke.Parameters)}){Constraints(type.TypeParameters)};");
            return;
        }
        if (type.TypeKind == TypeKind.Enum)
        {
            var fields = grouped.Select(group => group.First().Member).OfType<IFieldSymbol>().Where(field => field.HasConstantValue).ToList();
            if (!full && fields.All(field => presentMembers.Contains(MemberKey(field)))) return;
            if (type.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == "System.FlagsAttribute"))
                output.AppendLine("[global::System.Flags]");
            Availability(output, variants.Select(item => item.Platform));
            output.AppendLine($"{Access(type)} enum {Identifier(type.Name)} : {TypeName(type.EnumUnderlyingType)} {{");
            foreach (var field in fields)
            {
                if (!full && presentMembers.Contains(MemberKey(field))) continue;
                output.AppendLine($"{Identifier(field.Name)} = {Constant(field.ConstantValue, type.EnumUnderlyingType!)},");
            }
            output.AppendLine("}");
            return;
        }

        if (platform is null || full) Availability(output, variants.Select(item => item.Platform));
        if (full) Attributes(output, type);
        var abstractType = variants.All(item => item.Type.IsAbstract) && !type.IsStatic;
        var modifiers = type.IsStatic ? "static " : type.TypeKind == TypeKind.Class
            ? (abstractType ? "abstract " : variants.All(item => item.Type.IsSealed) ? "sealed " : "")
            : (type.IsReadOnly ? "readonly " : "");
        var kind = type.TypeKind switch { TypeKind.Interface => "interface", TypeKind.Struct => "struct", _ => "class" };
        output.Append($"{Access(type)} {modifiers}unsafe {(type.IsRefLikeType ? "ref " : "")}partial {kind} {Identifier(type.Name)}{TypeParameters(type.TypeParameters)}");
        var bases = new List<string>();
        if (full && type.TypeKind == TypeKind.Class && type.BaseType is { SpecialType: not SpecialType.System_Object })
            bases.Add(TypeName(type.BaseType));
        bases.AddRange((full ? interfaces : addedInterfaces).Select(TypeName));
        if (bases.Count > 0) output.Append(" : " + string.Join(", ", bases));
        output.AppendLine(Constraints(type.TypeParameters) + " {");
        // C# otherwise invents a public constructor for a class whose real constructors
        // are all private. Structs' implicit zero-initialization is not a metadata member.
        if (full && type.TypeKind == TypeKind.Class && !type.IsStatic &&
            !grouped.Any(group => group.First().Member is IMethodSymbol { MethodKind: MethodKind.Constructor }))
            output.AppendLine($"private {Identifier(type.Name)}() {{ {Throw}; }}");

        foreach (var group in selected)
        {
            // Prefer the concrete definition and a property with both accessors.
            var member = group.Select(item => item.Member).OrderBy(item => item.IsAbstract)
                .ThenByDescending(item => item is IPropertySymbol property && property.SetMethod is not null).First();
            var supported = group.Select(item => item.Platform).Distinct().ToList();
            if (platform is null || !supported.Contains(platform)) Availability(output, supported);
            var alias = aliases.GetValueOrDefault(group.Key);
            // Abstract members already declared in an existing source type are replaced there.
            if (!full && presentMembers.Contains(group.Key) && alias is null && member is not IPropertySymbol) continue;
            WriteMember(output, member, alias, full, platform is not null && supported.Contains(platform));
        }

        output.Append(nestedOutput);
        output.AppendLine("}");
    }

    private void WriteMember(StringBuilder output, ISymbol symbol, string? alias, bool full, bool forward)
    {
        Attributes(output, symbol);
        var explicitInterface = IsVisible(symbol) ? null : symbol switch
        {
            IMethodSymbol method => method.ExplicitInterfaceImplementations.FirstOrDefault()?.ContainingType,
            IPropertySymbol property => property.ExplicitInterfaceImplementations.FirstOrDefault()?.ContainingType,
            _ => null
        };
        var prefix = explicitInterface is null ? Access(symbol) + " " : "";
        if (symbol.IsStatic && symbol is not IFieldSymbol { IsConst: true }) prefix += "static ";
        var inInterface = symbol.ContainingType.TypeKind == TypeKind.Interface;
        if (symbol.IsOverride) prefix += "override ";
        else if (symbol.IsVirtual && (!inInterface || symbol.IsStatic)) prefix += "virtual ";
        else if (symbol.IsAbstract && (!inInterface || symbol.IsStatic)) prefix += "abstract ";
        if (symbol.IsSealed && symbol.IsOverride) prefix = "sealed " + prefix;
        var name = explicitInterface is null ? Identifier(alias ?? symbol.Name) : TypeName(explicitInterface) + "." + Identifier(symbol.Name.Split('.').Last());
        var abstractMember = symbol.IsAbstract && alias is null;
        switch (symbol)
        {
            case IFieldSymbol field:
                if (!full)
                    throw new InvalidOperationException($"An absent field cannot be represented by a throwing stub: {MemberKey(field)}.");
                output.AppendLine($"{prefix}{(field.IsConst ? "const " : field.IsReadOnly ? "readonly " : "")}{TypeName(field.Type)} {name}{(field.IsConst ? " = " + Constant(field.ConstantValue, field.Type) : "")};");
                break;
            case IMethodSymbol method:
                if (method.MethodKind == MethodKind.StaticConstructor) return;
                var signature = method.MethodKind switch
                {
                    MethodKind.Constructor => $"{prefix}{Identifier(method.ContainingType.Name)}({Parameters(method.Parameters)}){BaseInitializer(method)}",
                    MethodKind.Conversion => $"{prefix}{(method.Name == "op_Implicit" ? "implicit" : "explicit")} operator {Return(method)}({Parameters(method.Parameters)})",
                    MethodKind.UserDefinedOperator => $"{prefix}{Return(method)} operator {Operator(method.Name)}({Parameters(method.Parameters)})",
                    _ => $"{prefix}{Return(method)} {name}{TypeParameters(method.TypeParameters)}({Parameters(method.Parameters, method.IsExtensionMethod)}){(method.IsOverride || explicitInterface is not null ? "" : Constraints(method.TypeParameters))}"
                };
                if (abstractMember) output.AppendLine(signature + ";");
                else if (alias is not null && forward)
                {
                    var invocation = Identifier(method.Name) + TypeParameters(method.TypeParameters) + "(" + Arguments(method.Parameters) + ")";
                    output.AppendLine(signature + " => " + (method.ReturnsByRef || method.ReturnsByRefReadonly ? "ref " : "") + invocation + ";");
                }
                else output.AppendLine(signature + " { " + Throw + "; }");
                break;
            case IPropertySymbol property:
                var propertyName = property.IsIndexer
                    ? (explicitInterface is null ? "" : TypeName(explicitInterface) + ".") + "this[" + Parameters(property.Parameters) + "]" : name;
                output.AppendLine($"{prefix}{(property.ReturnsByRefReadonly ? "ref readonly " : property.ReturnsByRef ? "ref " : "")}{TypeName(property.Type)} {propertyName} {{");
                foreach (var accessor in new[] { property.GetMethod, property.SetMethod }.Where(item => item is not null))
                {
                    var getter = accessor!.MethodKind == MethodKind.PropertyGet;
                    var access = explicitInterface is null && accessor.DeclaredAccessibility != property.DeclaredAccessibility ? Access(accessor) + " " : "";
                    var keyword = getter ? "get" : accessor.IsInitOnly ? "init" : "set";
                    if (abstractMember) output.AppendLine(access + keyword + ";");
                    else if (alias is not null && forward)
                        output.AppendLine(access + keyword + " => " + (getter ? (property.ReturnsByRef || property.ReturnsByRefReadonly ? "ref " : "") + Identifier(property.Name) : Identifier(property.Name) + " = value") + ";");
                    else output.AppendLine(access + keyword + " { " + Throw + "; }");
                }
                output.AppendLine("}");
                break;
            case IEventSymbol eventSymbol:
                output.AppendLine($"{prefix}event {TypeName(eventSymbol.Type)} {name}" + (abstractMember ? ";" : " { add { " + Throw + "; } remove { " + Throw + "; } }"));
                break;
        }
    }

    private static string BaseInitializer(IMethodSymbol constructor)
    {
        var baseType = constructor.ContainingType.BaseType;
        if (constructor.ContainingType.TypeKind != TypeKind.Class || baseType is null || baseType.SpecialType == SpecialType.System_Object) return "";
        var candidate = baseType.InstanceConstructors.Where(method => method.DeclaredAccessibility is Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal ||
                method.DeclaredAccessibility == Accessibility.Internal && SymbolEqualityComparer.Default.Equals(baseType.ContainingAssembly, constructor.ContainingAssembly))
            .OrderBy(method => method.Parameters.Length).FirstOrDefault()
            ?? throw new InvalidOperationException($"No accessible base constructor for {TypeKey(constructor.ContainingType)}: {TypeName(baseType)} ({baseType.TypeKind}); candidates: {string.Join(";", baseType.InstanceConstructors.Select(method => method.ToDisplayString() + " " + method.DeclaredAccessibility))}.");
        // Evaluate a throwing argument before invoking any native wrapper base constructor.
        return " : base(" + string.Join(", ", candidate.Parameters.Select((parameter, index) =>
            index == 0 ? $"false ? default({TypeName(parameter.Type)})! : {Throw}" : $"default({TypeName(parameter.Type)})!")) + ")";
    }

    private void Availability(StringBuilder output, IEnumerable<string> supportedPlatforms)
    {
        var supported = supportedPlatforms.ToHashSet();
        foreach (var os in assemblies.Keys.Select(OperatingSystemName).Distinct())
            if (!assemblies.Keys.Any(platform => OperatingSystemName(platform) == os && supported.Contains(platform)))
                output.AppendLine($"[global::System.Runtime.Versioning.UnsupportedOSPlatform(\"{os}\")]");
    }

    private static string OperatingSystemName(string platform) => platform.Split('-')[0] switch
    { "win" => "windows", "osx" => "macos", "browser" => "browser", var name => name };
    private static StringBuilder Header() => new("// <auto-generated />\n#nullable enable\n#pragma warning disable CS0108, CS0109, CS0114, CS0169, CS0649, CS0618, CS0672, CS1066, CS8618, CA1416\n");
    private static bool IsVisible(ISymbol symbol) => symbol.DeclaredAccessibility is Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal;
    private static IEnumerable<INamedTypeSymbol> EnumerateTypes(INamespaceOrTypeSymbol scope)
    {
        foreach (var member in scope.GetMembers())
        {
            if (member is INamespaceSymbol ns) foreach (var namespaceType in EnumerateTypes(ns)) yield return namespaceType;
            if (member is INamedTypeSymbol type)
            {
                if (IsVisible(type)) { yield return type; foreach (var nested in EnumerateTypes(type)) yield return nested; }
            }
        }
    }
    private static IEnumerable<ISymbol> ApiMembers(INamedTypeSymbol type) => type.GetMembers().Where(member =>
        member is not INamedTypeSymbol && (IsVisible(member) || member is IMethodSymbol { MethodKind: MethodKind.Constructor, DeclaredAccessibility: Accessibility.Internal } || member is IMethodSymbol { ExplicitInterfaceImplementations.Length: > 0 } ||
        member is IPropertySymbol { ExplicitInterfaceImplementations.Length: > 0 }) &&
        member is not IMethodSymbol { MethodKind: MethodKind.PropertyGet or MethodKind.PropertySet or MethodKind.EventAdd or MethodKind.EventRemove or MethodKind.Destructor } &&
        member is not IFieldSymbol { IsImplicitlyDeclared: true } &&
        member is not IMethodSymbol { MethodKind: MethodKind.Constructor, IsImplicitlyDeclared: true });
    private static string Access(ISymbol symbol) => symbol.DeclaredAccessibility switch
    { Accessibility.Public => "public", Accessibility.Protected => "protected", Accessibility.ProtectedOrInternal => "protected internal", Accessibility.Internal => "internal", _ => "private" };
    private static string TypeName(ITypeSymbol? type) => type?.ToDisplayString(TypeFormat) ?? "";
    public static string TypeKey(INamedTypeSymbol type) => type.ContainingType is { } parent
        ? TypeKey(parent) + "+" + type.MetadataName : (type.ContainingNamespace.IsGlobalNamespace ? "" : type.ContainingNamespace.ToDisplayString() + ".") + type.MetadataName;
    private static string Identifier(string name) => "@" + name;
    private static string TypeParameters(IEnumerable<ITypeParameterSymbol> parameters)
    {
        var names = parameters.Select(parameter => (parameter.Variance switch { VarianceKind.In => "in ", VarianceKind.Out => "out ", _ => "" }) + Identifier(parameter.Name)).ToList();
        return names.Count == 0 ? "" : "<" + string.Join(", ", names) + ">";
    }
    private static string Constraints(IEnumerable<ITypeParameterSymbol> parameters)
    {
        var output = new StringBuilder();
        foreach (var parameter in parameters)
        {
            var constraints = new List<string>();
            if (parameter.HasUnmanagedTypeConstraint) constraints.Add("unmanaged");
            else if (parameter.HasValueTypeConstraint) constraints.Add("struct");
            else if (parameter.HasReferenceTypeConstraint) constraints.Add("class");
            else if (parameter.HasNotNullConstraint) constraints.Add("notnull");
            constraints.AddRange(parameter.ConstraintTypes.Select(TypeName));
            if (parameter.HasConstructorConstraint && !parameter.HasValueTypeConstraint) constraints.Add("new()");
            if (constraints.Count > 0) output.Append($" where {Identifier(parameter.Name)} : {string.Join(", ", constraints)}");
        }
        return output.ToString();
    }
    private static string Parameters(IEnumerable<IParameterSymbol> parameters, bool extension = false) => string.Join(", ", parameters.Select((parameter, index) =>
        AttributeText(parameter) + (extension && index == 0 ? "this " : "") + (parameter.IsParams ? "params " : "") + Ref(parameter.RefKind) + TypeName(parameter.Type) + " " + Identifier(parameter.Name) +
        (parameter.HasExplicitDefaultValue ? " = " + Constant(parameter.ExplicitDefaultValue, parameter.Type) : "")));
    private static string Arguments(IEnumerable<IParameterSymbol> parameters) => string.Join(", ", parameters.Select(parameter =>
        (parameter.RefKind == RefKind.RefReadOnlyParameter ? "in " : Ref(parameter.RefKind)) + Identifier(parameter.Name)));
    private static string Ref(RefKind kind) => kind switch { RefKind.Ref => "ref ", RefKind.Out => "out ", RefKind.In => "in ", RefKind.RefReadOnlyParameter => "ref readonly ", _ => "" };
    private static string Return(IMethodSymbol method) => (method.ReturnsByRefReadonly ? "ref readonly " : method.ReturnsByRef ? "ref " : "") + TypeName(method.ReturnType);
    private static string ResultType(ISymbol member) => member switch { IMethodSymbol method => TypeName(method.ReturnType), IPropertySymbol property => TypeName(property.Type), IFieldSymbol field => TypeName(field.Type), IEventSymbol eventSymbol => TypeName(eventSymbol.Type), _ => "" };
    private static string CollisionKey(ISymbol member) => TypeKey(member.ContainingType) + "." + member.Name + (member switch
    {
        IMethodSymbol method => "`" + method.Arity + "(" + string.Join(",", method.Parameters.Select(parameter => Ref(parameter.RefKind) + TypeName(parameter.Type))) + ")",
        IPropertySymbol property => "(" + string.Join(",", property.Parameters.Select(parameter => TypeName(parameter.Type))) + ")", _ => ""
    });
    private static string MemberKey(ISymbol member) => member.Kind + ":" + CollisionKey(member) + ":" + ResultType(member);
    private static string Constant(object? value, ITypeSymbol type)
    {
        if (value is null) return type.IsValueType ? "default" : "null";
        var literal = value switch
        {
            string text => SymbolDisplay.FormatLiteral(text, true), char c => SymbolDisplay.FormatLiteral(c, true),
            bool b => b ? "true" : "false", float f when float.IsNaN(f) => "float.NaN", float f when float.IsPositiveInfinity(f) => "float.PositiveInfinity", float f when float.IsNegativeInfinity(f) => "float.NegativeInfinity",
            double d when double.IsNaN(d) => "double.NaN", double d when double.IsPositiveInfinity(d) => "double.PositiveInfinity", double d when double.IsNegativeInfinity(d) => "double.NegativeInfinity",
            decimal d => d.ToString(CultureInfo.InvariantCulture) + "M", float f => f.ToString("R", CultureInfo.InvariantCulture) + "F", double d => d.ToString("R", CultureInfo.InvariantCulture) + "D",
            uint n => n.ToString(CultureInfo.InvariantCulture) + "U", ulong n => n.ToString(CultureInfo.InvariantCulture) + "UL", long n => n.ToString(CultureInfo.InvariantCulture) + "L",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)!
        };
        return type.TypeKind == TypeKind.Enum ? "(" + TypeName(type) + ")" + literal : literal;
    }
    private static void Attributes(StringBuilder output, ISymbol symbol)
    {
        var text = AttributeText(symbol);
        if (text.Length > 0) output.AppendLine(text);
    }
    private static string AttributeText(ISymbol symbol) => string.Concat(symbol.GetAttributes().Where(attribute =>
        attribute.AttributeClass?.ToDisplayString() is "System.ObsoleteAttribute" or
            "System.Runtime.CompilerServices.InterpolatedStringHandlerAttribute" or
            "System.Runtime.CompilerServices.InterpolatedStringHandlerArgumentAttribute" ||
        attribute.AttributeClass?.ContainingNamespace.ToDisplayString() == "System.Diagnostics.CodeAnalysis")
        .Select(attribute => "[" + TypeName(attribute.AttributeClass) + "(" + string.Join(", ",
            attribute.ConstructorArguments.Select(AttributeConstant).Concat(attribute.NamedArguments.Select(argument => Identifier(argument.Key) + " = " + AttributeConstant(argument.Value)))) + ")] "));
    private static string AttributeConstant(TypedConstant argument) => argument.Kind switch
    {
        TypedConstantKind.Array => "new " + TypeName(argument.Type) + " { " + string.Join(", ", argument.Values.Select(AttributeConstant)) + " }",
        TypedConstantKind.Type => "typeof(" + TypeName((ITypeSymbol?)argument.Value) + ")",
        _ => Constant(argument.Value, argument.Type!)
    };
    private static string Operator(string name) => name switch
    {
        "op_Addition" => "+", "op_Subtraction" => "-", "op_Multiply" => "*", "op_Division" => "/", "op_Modulus" => "%",
        "op_Equality" => "==", "op_Inequality" => "!=", "op_LessThan" => "<", "op_GreaterThan" => ">", "op_LessThanOrEqual" => "<=", "op_GreaterThanOrEqual" => ">=",
        "op_BitwiseAnd" => "&", "op_BitwiseOr" => "|", "op_ExclusiveOr" => "^", "op_LeftShift" => "<<", "op_RightShift" => ">>", "op_UnsignedRightShift" => ">>>",
        "op_UnaryNegation" => "-", "op_UnaryPlus" => "+", "op_LogicalNot" => "!", "op_OnesComplement" => "~", "op_Increment" => "++", "op_Decrement" => "--", "op_True" => "true", "op_False" => "false",
        _ => throw new InvalidOperationException($"Unsupported operator {name}.")
    };
}
