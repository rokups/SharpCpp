namespace SharpCpp.Model.Ast;

/// <summary>
/// Shared computations over the free functions of owned strings (<see cref="StringFreeFunction"/>), used by
/// the renderer and by the binding metadata export/import.
/// </summary>
public static class OwnedStringFunctions
{
    /// <summary>
    /// The distinct free functions of live owned strings (returns and output parameters), in
    /// ordinal order. Each gets one native thunk and one managed import for the whole module.
    /// </summary>
    public static StringFreeFunction[] CollectFreeFunctions(BindingCompilation compilation)
    {
        var found = new Dictionary<string, StringFreeFunction>(StringComparer.Ordinal);
        foreach (var function in compilation.Functions)
        {
            if (function.IsIgnored() || function.IsSilentlyExcluded())
                continue;

            if (function.TryGetMetadata<StringFreeFunction>(MetadataKeys.StringFreeFunction, out var returnFree))
                found.TryAdd(returnFree!.NativeName, returnFree);

            foreach (var parameter in function.Parameters)
            {
                if (parameter.TryGetMetadata<StringFreeFunction>(MetadataKeys.StringFreeFunction, out var parameterFree))
                    found.TryAdd(parameterFree!.NativeName, parameterFree);
            }
        }

        return found.Values.OrderBy(static free => free.NativeName, StringComparer.Ordinal).ToArray();
    }

    /// <summary>The entry point of the native release thunk a local free gets in this module.</summary>
    public static string GetFreeEntryPointName(BindingCompilation compilation, StringFreeFunction free)
        => $"{compilation.EntryPointPrefix}_free_{SanitizeNativeEntrypointName(free.NativeName)}";

    public static string SanitizeNativeEntrypointName(string value)
    {
        var sanitized = value.Replace("::", "_", StringComparison.Ordinal).ToCharArray();
        for (var index = 0; index < sanitized.Length; ++index)
        {
            if (!char.IsAsciiLetterOrDigit(sanitized[index]) && sanitized[index] != '_')
            {
                sanitized[index] = '_';
            }
        }

        return new string(sanitized);
    }
}
