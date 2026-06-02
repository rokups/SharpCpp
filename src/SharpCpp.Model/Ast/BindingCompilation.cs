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

    public IReadOnlyList<RecordBinding> Records => Descendants<RecordBinding>().ToArray();

    public IReadOnlyList<EnumBinding> Enums => Descendants<EnumBinding>().ToArray();

    public IReadOnlyList<FunctionBinding> Functions => Descendants<FunctionBinding>().ToArray();
}
