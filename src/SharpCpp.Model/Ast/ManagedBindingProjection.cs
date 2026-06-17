namespace SharpCpp.Model.Ast;

public sealed class ManagedBindingProjection
{
    private readonly Dictionary<string, ManagedNamespaceProjection> _namespacesByFullName = new(StringComparer.Ordinal);
    private readonly Dictionary<BindingNode, ManagedNamespaceProjection> _declarationNamespaces = new();
    private readonly Dictionary<string, ManagedNamespaceProjection> _aliasNamespaces = new(StringComparer.Ordinal);

    public ManagedBindingProjection(string defaultNamespace)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultNamespace);

        DefaultNamespace = defaultNamespace;
        Root = new ManagedNamespaceProjection(string.Empty, string.Empty, null);
    }

    public string DefaultNamespace { get; }

    public ManagedNamespaceProjection Root { get; }

    public IReadOnlyCollection<ManagedNamespaceProjection> Namespaces => _namespacesByFullName.Values;

    public ManagedDeclarationProjection AddDeclaration(string managedNamespace, BindingNode originalNode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(managedNamespace);
        ArgumentNullException.ThrowIfNull(originalNode);

        var namespaceNode = GetOrCreateNamespace(managedNamespace);
        var declaration = namespaceNode.AddDeclaration(originalNode);
        _declarationNamespaces[originalNode] = namespaceNode;
        return declaration;
    }

    public ManagedAliasProjection AddAlias(string managedNamespace, BindingTypeAlias alias)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(managedNamespace);
        ArgumentNullException.ThrowIfNull(alias);

        var namespaceNode = GetOrCreateNamespace(managedNamespace);
        var aliasProjection = namespaceNode.AddAlias(alias);
        _aliasNamespaces[GetAliasKey(alias)] = namespaceNode;
        return aliasProjection;
    }

    public bool TryGetManagedNamespace(BindingNode node, out string managedNamespace)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (_declarationNamespaces.TryGetValue(node, out var namespaceNode))
        {
            managedNamespace = namespaceNode.FullName;
            return true;
        }

        managedNamespace = string.Empty;
        return false;
    }

    public bool TryGetManagedNamespace(BindingTypeAlias alias, out string managedNamespace)
    {
        ArgumentNullException.ThrowIfNull(alias);

        if (_aliasNamespaces.TryGetValue(GetAliasKey(alias), out var namespaceNode))
        {
            managedNamespace = namespaceNode.FullName;
            return true;
        }

        managedNamespace = string.Empty;
        return false;
    }

    public string GetManagedNamespace(RecordBinding record, BindingCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(compilation);

        return TryGetManagedNamespace(record, out var managedNamespace)
            ? managedNamespace
            : record.ManagedNamespace ?? compilation.ManagedNamespace;
    }

    public string GetManagedNamespace(EnumBinding enumBinding, BindingCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(enumBinding);
        ArgumentNullException.ThrowIfNull(compilation);

        return TryGetManagedNamespace(enumBinding, out var managedNamespace)
            ? managedNamespace
            : enumBinding.ManagedNamespace ?? compilation.ManagedNamespace;
    }

    public string GetManagedNamespace(BindingTypeAlias alias, BindingCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(alias);
        ArgumentNullException.ThrowIfNull(compilation);

        return TryGetManagedNamespace(alias, out var managedNamespace)
            ? managedNamespace
            : alias.ManagedNamespace ?? compilation.ManagedNamespace;
    }

    private ManagedNamespaceProjection GetOrCreateNamespace(string fullName)
    {
        if (_namespacesByFullName.TryGetValue(fullName, out var existing))
        {
            return existing;
        }

        var parts = fullName.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            throw new ArgumentException("Managed namespace must contain at least one segment.", nameof(fullName));
        }

        var current = Root;
        var currentName = string.Empty;
        foreach (var part in parts)
        {
            currentName = currentName.Length == 0 ? part : $"{currentName}.{part}";
            if (!_namespacesByFullName.TryGetValue(currentName, out var child))
            {
                child = current.AddNamespace(part, currentName);
                _namespacesByFullName.Add(currentName, child);
            }

            current = child;
        }

        return current;
    }

    private static string GetAliasKey(BindingTypeAlias alias)
    {
        return $"{alias.CppQualifiedName}\u001f{alias.NativeName}\u001f{alias.ManagedName}";
    }
}

public sealed class ManagedNamespaceProjection
{
    private readonly List<ManagedNamespaceProjection> _namespaces = new();
    private readonly List<ManagedDeclarationProjection> _declarations = new();
    private readonly List<ManagedAliasProjection> _aliases = new();

    internal ManagedNamespaceProjection(string name, string fullName, ManagedNamespaceProjection? parent)
    {
        Name = name;
        FullName = fullName;
        Parent = parent;
    }

    public string Name { get; }

    public string FullName { get; }

    public ManagedNamespaceProjection? Parent { get; }

    public IReadOnlyList<ManagedNamespaceProjection> Namespaces => _namespaces;

    public IReadOnlyList<ManagedDeclarationProjection> Declarations => _declarations;

    public IReadOnlyList<ManagedAliasProjection> Aliases => _aliases;

    public IEnumerable<ManagedNamespaceProjection> DescendantsAndSelf()
    {
        yield return this;

        foreach (var child in _namespaces)
        {
            foreach (var descendant in child.DescendantsAndSelf())
            {
                yield return descendant;
            }
        }
    }

    internal ManagedNamespaceProjection AddNamespace(string name, string fullName)
    {
        var child = new ManagedNamespaceProjection(name, fullName, this);
        _namespaces.Add(child);
        return child;
    }

    internal ManagedDeclarationProjection AddDeclaration(BindingNode originalNode)
    {
        var declaration = new ManagedDeclarationProjection(this, originalNode);
        _declarations.Add(declaration);
        return declaration;
    }

    internal ManagedAliasProjection AddAlias(BindingTypeAlias alias)
    {
        var projection = new ManagedAliasProjection(this, alias);
        _aliases.Add(projection);
        return projection;
    }
}

public sealed class ManagedDeclarationProjection
{
    internal ManagedDeclarationProjection(ManagedNamespaceProjection parentNamespace, BindingNode originalNode)
    {
        ParentNamespace = parentNamespace;
        OriginalNode = originalNode;
    }

    public ManagedNamespaceProjection ParentNamespace { get; }

    public BindingNode OriginalNode { get; }
}

public sealed class ManagedAliasProjection
{
    internal ManagedAliasProjection(ManagedNamespaceProjection parentNamespace, BindingTypeAlias alias)
    {
        ParentNamespace = parentNamespace;
        Alias = alias;
    }

    public ManagedNamespaceProjection ParentNamespace { get; }

    public BindingTypeAlias Alias { get; }
}
