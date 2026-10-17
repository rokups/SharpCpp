using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace SharpCpp.Support.SourceGenerator;

/// <summary>
/// Emits <c>NativeObject.IsManagedOverride</c> for each class that overrides a generated director virtual method.
/// A generated virtual method carries <c>ManagedVirtualSlotAttribute</c> with the stable key of its slot.
/// The generator reads the key from the attribute of the root overridden method. It never rebuilds the key.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ManagedOverrideGenerator : IIncrementalGenerator
{
    internal const string AttributeName = "ManagedVirtualSlotAttribute";
    internal const string AttributeNamespace = "SharpCpp.Support";

    private static readonly DiagnosticDescriptor PartialRequiredDiagnostic = new(
        id: "SCPPSG001",
        title: "Types that override native virtual methods must be partial",
        messageFormat: "Type '{0}' overrides a native virtual method and needs a generated IsManagedOverride override; mark '{1}' partial",
        category: "SharpCpp.Support.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly SymbolDisplayFormat TypeDisplayFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var models = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, _) => node is ClassDeclarationSyntax declaration && MayOverride(declaration),
            static (syntaxContext, cancellationToken) => CreateModel(syntaxContext, cancellationToken))
            .Where(static model => model is not null);

        context.RegisterSourceOutput(models, static (productionContext, model) => Execute(productionContext, model!));
    }

    // A part qualifies when it has an override method or is partial. The partial parts decide which part the generator handles.
    private static bool MayOverride(ClassDeclarationSyntax declaration)
    {
        if (declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            return true;
        }

        foreach (var member in declaration.Members)
        {
            if (member is MethodDeclarationSyntax method && method.Modifiers.Any(SyntaxKind.OverrideKeyword))
            {
                return true;
            }
        }

        return false;
    }

    private static OverrideModel? CreateModel(GeneratorSyntaxContext syntaxContext, CancellationToken cancellationToken)
    {
        var declaration = (ClassDeclarationSyntax)syntaxContext.Node;
        if (syntaxContext.SemanticModel.GetDeclaredSymbol(declaration, cancellationToken) is not INamedTypeSymbol symbol)
        {
            return null;
        }

        // All the parts of a partial type produce one model: the first declaration handles the type.
        var firstReference = symbol.DeclaringSyntaxReferences.FirstOrDefault();
        if (firstReference is null || firstReference.SyntaxTree != declaration.SyntaxTree || firstReference.Span != declaration.Span)
        {
            return null;
        }

        var keys = CollectSlotKeys(symbol);
        if (keys.Count == 0)
        {
            return null;
        }

        var nonPartial = FindNonPartialType(symbol);
        var identifier = declaration.Identifier.GetLocation();
        var lineSpan = identifier.GetLineSpan();
        var displayName = symbol.ToDisplayString(TypeDisplayFormat);
        return new OverrideModel(
            HintName: GetHintName(symbol),
            DisplayName: displayName,
            NonPartialName: nonPartial?.ToDisplayString(TypeDisplayFormat),
            Source: nonPartial is null ? Render(symbol, keys) : string.Empty,
            FilePath: lineSpan.Path,
            Span: identifier.SourceSpan,
            LineSpan: lineSpan.Span);
    }

    // The keys come from the attributes of the overridden methods of each override that the type declares.
    // The walk goes to the root overridden method. The attribute sits on the method that starts the director slot,
    // which is the root method or a method of a derived generated class that overrides a non-director root.
    private static List<string> CollectSlotKeys(INamedTypeSymbol symbol)
    {
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var member in symbol.GetMembers())
        {
            if (member is not IMethodSymbol { MethodKind: MethodKind.Ordinary, IsOverride: true } method)
            {
                continue;
            }

            for (var overridden = method.OverriddenMethod; overridden is not null; overridden = overridden.OverriddenMethod)
            {
                foreach (var attribute in overridden.OriginalDefinition.GetAttributes())
                {
                    if (IsSlotAttribute(attribute.AttributeClass) &&
                        attribute.ConstructorArguments.Length == 1 &&
                        attribute.ConstructorArguments[0].Value is string key)
                    {
                        keys.Add(key);
                    }
                }
            }
        }

        return keys.ToList();
    }

    private static bool IsSlotAttribute(INamedTypeSymbol? attributeClass) =>
        attributeClass is { Name: AttributeName, ContainingNamespace: { Name: "Support", ContainingNamespace: { Name: "SharpCpp", ContainingNamespace.IsGlobalNamespace: true } } };

    // Returns the type itself or a containing type that has no partial declaration.
    private static INamedTypeSymbol? FindNonPartialType(INamedTypeSymbol symbol)
    {
        for (INamedTypeSymbol? current = symbol; current is not null; current = current.ContainingType)
        {
            var isPartial = current.DeclaringSyntaxReferences.Any(static reference =>
                reference.GetSyntax() is TypeDeclarationSyntax declaration && declaration.Modifiers.Any(SyntaxKind.PartialKeyword));
            if (!isPartial)
            {
                return current;
            }
        }

        return null;
    }

    private static void Execute(SourceProductionContext context, OverrideModel model)
    {
        if (model.NonPartialName is not null)
        {
            var location = Location.Create(model.FilePath, model.Span, model.LineSpan);
            context.ReportDiagnostic(Diagnostic.Create(PartialRequiredDiagnostic, location, model.DisplayName, model.NonPartialName));
            return;
        }

        context.AddSource(model.HintName, SourceText.From(model.Source, Encoding.UTF8));
    }

    private static string GetHintName(INamedTypeSymbol symbol)
    {
        var name = symbol.ToDisplayString(TypeDisplayFormat);
        var builder = new StringBuilder(name.Length + 40);
        foreach (var character in name)
        {
            builder.Append(char.IsLetterOrDigit(character) || character == '.' || character == '_' ? character : '_');
        }

        // The arity separates Name<T> from Name_T and from Name<T1, T2>.
        builder.Append('.').Append(symbol.Arity).Append(".IsManagedOverride.g.cs");
        return builder.ToString();
    }

    private static string Render(INamedTypeSymbol symbol, List<string> keys)
    {
        var chain = new List<INamedTypeSymbol>();
        for (INamedTypeSymbol? current = symbol; current is not null; current = current.ContainingType)
        {
            chain.Insert(0, current);
        }

        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("#nullable enable");
        builder.AppendLine();

        var indent = string.Empty;
        var hasNamespace = !symbol.ContainingNamespace.IsGlobalNamespace;
        if (hasNamespace)
        {
            builder.Append("namespace ").AppendLine(symbol.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty));
            builder.AppendLine("{");
            indent = "    ";
        }

        foreach (var type in chain)
        {
            builder.Append(indent).Append("partial ").Append(GetKeyword(type)).Append(' ').Append(Escape(type.Name));
            if (type.TypeParameters.Length > 0)
            {
                builder.Append('<').Append(string.Join(", ", type.TypeParameters.Select(static parameter => Escape(parameter.Name)))).Append('>');
            }

            builder.AppendLine();
            builder.Append(indent).AppendLine("{");
            indent += "    ";
        }

        builder.Append(indent).AppendLine("protected override bool IsManagedOverride(string slotKey) => slotKey switch");
        builder.Append(indent).AppendLine("{");
        builder.Append(indent).Append("    ")
            .Append(string.Join(" or ", keys.Select(static key => SymbolDisplay.FormatLiteral(key, quote: true))))
            .AppendLine(" => true,");
        builder.Append(indent).AppendLine("    _ => base.IsManagedOverride(slotKey),");
        builder.Append(indent).AppendLine("};");

        for (var index = 0; index < chain.Count; index++)
        {
            indent = indent.Substring(4);
            builder.Append(indent).AppendLine("}");
        }

        if (hasNamespace)
        {
            builder.AppendLine("}");
        }

        return builder.ToString();
    }

    private static string Escape(string identifier) =>
        SyntaxFacts.GetKeywordKind(identifier) != SyntaxKind.None
            ? "@" + identifier
            : identifier;

    private static string GetKeyword(INamedTypeSymbol type) => type.TypeKind switch
    {
        TypeKind.Struct => type.IsRecord ? "record struct" : "struct",
        TypeKind.Interface => "interface",
        _ => type.IsRecord ? "record" : "class",
    };

    private sealed record OverrideModel(
        string HintName,
        string DisplayName,
        string? NonPartialName,
        string Source,
        string FilePath,
        TextSpan Span,
        LinePositionSpan LineSpan);
}
