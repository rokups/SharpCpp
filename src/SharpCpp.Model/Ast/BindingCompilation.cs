namespace SharpCpp.Model.Ast;

public sealed class BindingCompilation : BindingContainerNode
{
    public BindingCompilation(
        string projectName,
        string managedNamespace,
        string managedClassName,
        string nativeLibraryName,
        IEnumerable<string> headerFiles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);
        ArgumentException.ThrowIfNullOrWhiteSpace(managedNamespace);
        ArgumentException.ThrowIfNullOrWhiteSpace(managedClassName);
        ArgumentException.ThrowIfNullOrWhiteSpace(nativeLibraryName);
        ArgumentNullException.ThrowIfNull(headerFiles);

        ProjectName = projectName;
        ManagedNamespace = managedNamespace;
        ManagedClassName = managedClassName;
        NativeLibraryName = nativeLibraryName;
        HeaderFiles = new List<string>(headerFiles);
    }

    public override BindingNodeKind Kind => BindingNodeKind.Compilation;

    public string ProjectName { get; }

    public string ManagedNamespace { get; set; }

    public string ManagedClassName { get; set; }

    public string NativeLibraryName { get; set; }

    public IList<string> HeaderFiles { get; }

    public IReadOnlyDictionary<string, string> NamespaceMappings { get; set; } = new Dictionary<string, string>();

    private IReadOnlyList<RecordBinding>? _records;
    private IReadOnlyList<EnumBinding>? _enums;
    private IReadOnlyList<FunctionBinding>? _functions;

    public IReadOnlyList<RecordBinding> Records => _records ??= Descendants<RecordBinding>().ToArray();

    public IReadOnlyList<EnumBinding> Enums => _enums ??= Descendants<EnumBinding>().ToArray();

    public IReadOnlyList<FunctionBinding> Functions => _functions ??= Descendants<FunctionBinding>().ToArray();

    /// <summary>Invalidate cached descendants (call after pipeline passes that add members).</summary>
    public void InvalidateCaches()
    {
        _records = null;
        _enums = null;
        _functions = null;
    }
}
