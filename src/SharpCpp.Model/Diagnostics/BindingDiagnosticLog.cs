using SharpCpp.Model.Ast;
using SharpCpp.Model.Logging;

namespace SharpCpp.Model.Diagnostics;

public static class BindingDiagnosticLog
{
    public static void WriteToConfiguredLog(BindingCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var writer = SharpCppLog.FileWriter;
        if (writer is null)
        {
            return;
        }

        Write(compilation, writer);
        writer.Flush();
    }

    public static void Write(BindingCompilation compilation, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(writer);

        var ignoredCount = 0;
        var warningCount = 0;

        writer.WriteLine("SharpCpp binding diagnostics:");
        foreach (var node in compilation.DescendantsAndSelf<BindingNode>())
        {
            if (node.IsIgnored())
            {
                writer.WriteLine(
                    $"{(node.IsSilentlyExcluded() ? "EXCLUDED" : "IGNORED")}\t{node.Kind}\t{FormatSource(node.Source)}\t{GetQualifiedName(node)}\t{Escape(node.GetIgnoreReason())}");
                ignoredCount++;
            }

            foreach (var warning in node.GetWarnings())
            {
                writer.WriteLine(
                    $"WARNING\t{node.Kind}\t{FormatSource(node.Source)}\t{GetQualifiedName(node)}\t{Escape(warning)}");
                warningCount++;
            }
        }

        writer.WriteLine($"SharpCpp binding diagnostics: {ignoredCount} ignored or excluded, {warningCount} warnings.");
    }

    private static string GetQualifiedName(BindingNode node)
    {
        return node switch
        {
            RecordBinding record => record.CppQualifiedName,
            EnumBinding enumBinding => enumBinding.CppQualifiedName,
            FunctionBinding function => function.CppQualifiedName,
            FieldBinding field => $"{field.OwningRecordCppTypeName}::{field.NativeName}",
            ParameterBinding parameter when parameter.Parent is FunctionBinding function =>
                $"{function.CppQualifiedName}({parameter.NativeName})",
            BindingNamedNode namedNode => GetNestedName(namedNode.Parent, namedNode.NativeName),
            BindingNamedContainerNode namedNode => GetNestedName(namedNode.Parent, namedNode.NativeName),
            BindingCompilation bindingCompilation => bindingCompilation.ProjectName,
            _ => node.Kind.ToString()
        };
    }

    private static string GetNestedName(BindingNode? parent, string nativeName)
    {
        return parent is null ? nativeName : $"{GetQualifiedName(parent)}::{nativeName}";
    }

    private static string FormatSource(BindingSourceReference? source)
    {
        return source is null ? "(unknown)" : Escape(source.ToString());
    }

    private static string Escape(string? value)
    {
        return (value ?? string.Empty)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);
    }
}
