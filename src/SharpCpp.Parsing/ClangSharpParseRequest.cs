namespace SharpCpp.Parsing;

public sealed record ClangSharpParseRequest(
    string ProjectName,
    string ManagedNamespace,
    string ManagedClassName,
    string NativeLibraryName,
    string HeaderFilePath,
    string HeaderIncludePath,
    IReadOnlyList<string> ClangArguments,
    string EntryPointPrefix);
