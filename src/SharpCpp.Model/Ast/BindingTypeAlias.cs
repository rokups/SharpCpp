namespace SharpCpp.Model.Ast;

public sealed record BindingTypeAlias(
    string CppQualifiedName,
    string NativeName,
    string ManagedName,
    BindingType TargetType)
{
    public string? ManagedNamespace { get; set; }

    /// <summary>
    /// Declared by an imported module. Its C# alias is a global using of that module's
    /// assembly, so this module neither emits it nor names types through it.
    /// </summary>
    public bool IsImported { get; init; }
}
