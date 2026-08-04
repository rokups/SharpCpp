namespace SharpCpp.Model.Diagnostics;

public sealed record BindingSourceReference(string FilePath, int Line, int Column)
{
    public override string ToString() => Line <= 0 ? FilePath : $"{FilePath}({Line},{Column})";
}
