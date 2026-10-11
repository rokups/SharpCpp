using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpCpp.ApiUnion;

/// <summary>Merges absent accessors into existing source properties before recompilation.</summary>
public static class SourceAugmenter
{
    public static IReadOnlyList<SyntaxTree> Augment(IEnumerable<SyntaxTree> sources, SyntaxTree stubs,
        IReadOnlySet<string> concreteTypes)
    {
        var trees = sources.ToList();
        var existing = trees.SelectMany(tree => tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
            .GroupBy(TypeKey).ToDictionary(group => group.Key, group => group.ToList());
        var merged = new Dictionary<string, PropertyDeclarationSyntax>();
        var removal = new HashSet<PropertyDeclarationSyntax>();
        var enums = trees.SelectMany(tree => tree.GetRoot().DescendantNodes().OfType<EnumDeclarationSyntax>())
            .ToDictionary(TypeKey);
        var mergedEnums = new Dictionary<string, EnumDeclarationSyntax>();
        var enumRemoval = new HashSet<EnumDeclarationSyntax>();
        foreach (var addition in stubs.GetRoot().DescendantNodes().OfType<EnumDeclarationSyntax>())
        {
            if (!enums.TryGetValue(TypeKey(addition), out var original)) continue;
            mergedEnums[TypeKey(addition)] = original.WithMembers(original.Members.AddRange(addition.Members
                .Where(member => !original.Members.Any(existingMember => existingMember.Identifier.ValueText == member.Identifier.ValueText))));
            enumRemoval.Add(addition);
        }
        foreach (var type in stubs.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            if (!existing.TryGetValue(TypeKey(type), out var declarations)) continue;
            foreach (var property in type.Members.OfType<PropertyDeclarationSyntax>())
            {
                var original = declarations.SelectMany(declaration => declaration.Members.OfType<PropertyDeclarationSyntax>())
                    .FirstOrDefault(candidate => candidate.Identifier.ValueText == property.Identifier.ValueText &&
                        candidate.ExplicitInterfaceSpecifier?.ToString() == property.ExplicitInterfaceSpecifier?.ToString());
                if (original is null) continue;
                var accessors = original.AccessorList?.Accessors ?? default;
                if (original.ExpressionBody is not null)
                    accessors = SyntaxFactory.SingletonList(SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                        .WithExpressionBody(original.ExpressionBody).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)));
                var additions = property.AccessorList!.Accessors.Where(accessor => !accessors.Any(item => item.Kind() == accessor.Kind()));
                merged[TypeKey(type) + "." + original.Identifier.ValueText] = original
                    .WithExpressionBody(null).WithSemicolonToken(default)
                    .WithAccessorList(SyntaxFactory.AccessorList(accessors.AddRange(additions)));
                removal.Add(property);
            }
        }
        var rewritten = trees.Select(tree => tree.WithRootAndOptions(new OriginalRewriter(merged, mergedEnums, concreteTypes).Visit(tree.GetRoot())!, tree.Options)).ToList();
        rewritten.Add(stubs.WithRootAndOptions(stubs.GetRoot().RemoveNodes(removal.Cast<SyntaxNode>().Concat(enumRemoval), SyntaxRemoveOptions.KeepNoTrivia)!, stubs.Options));
        return rewritten;
    }

    private sealed class OriginalRewriter(IReadOnlyDictionary<string, PropertyDeclarationSyntax> properties,
        IReadOnlyDictionary<string, EnumDeclarationSyntax> enums,
        IReadOnlySet<string> concreteTypes) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitEnumDeclaration(EnumDeclarationSyntax node) => enums.GetValueOrDefault(TypeKey(node)) ?? base.VisitEnumDeclaration(node);
        public override SyntaxNode? VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            var key = TypeKey((TypeDeclarationSyntax)node.Parent!) + "." + node.Identifier.ValueText;
            return properties.GetValueOrDefault(key) ?? base.VisitPropertyDeclaration(node);
        }
        public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            var rewritten = (ClassDeclarationSyntax)base.VisitClassDeclaration(node)!;
            if (!concreteTypes.Contains(TypeKey(node))) return rewritten;
            if (node.Members.OfType<MethodDeclarationSyntax>().Any(method => method.Modifiers.Any(SyntaxKind.AbstractKeyword)))
                throw new InvalidOperationException($"Cannot make {TypeKey(node)} concrete while it has abstract methods.");
            return rewritten.WithModifiers(SyntaxFactory.TokenList(rewritten.Modifiers.Where(token => !token.IsKind(SyntaxKind.AbstractKeyword))));
        }
    }

    private static string TypeKey(BaseTypeDeclarationSyntax type)
    {
        var name = type.Identifier.ValueText + (type is TypeDeclarationSyntax { TypeParameterList.Parameters.Count: > 0 } generic ? "`" + generic.TypeParameterList.Parameters.Count : "");
        if (type.Parent is TypeDeclarationSyntax parent) return TypeKey(parent) + "+" + name;
        var namespaces = type.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(ns => ns.Name.ToString());
        var nsName = string.Join(".", namespaces);
        return (nsName.Length > 0 ? nsName + "." : "") + name;
    }
}
