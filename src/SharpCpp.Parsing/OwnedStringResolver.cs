using SharpCpp.Model.Ast;

namespace SharpCpp.Parsing;

/// <summary>
/// Validates <see cref="MetadataKeys.FreedBy"/> tags: an output `char**` parameter or a
/// `char*`/`const char*` return whose native string the caller must release with a named free
/// function. A valid tag gets a <see cref="StringFreeFunction"/> record (the only thing the
/// renderer reads) and a return is retyped to a raw address so the wrapper copies the text before
/// freeing it. An invalid tag ignores the function with a logged reason. Runs after
/// <see cref="PointerPointerShapeResolver.Apply"/>, which decides whether a `char**` is an output.
/// </summary>
public static class OwnedStringResolver
{
    public static void Apply(BindingCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        foreach (var function in compilation.Functions)
        {
            if (function.IsIgnored())
                continue;

            var returnTagged = function.TryGetMetadata<string>(MetadataKeys.FreedBy, out var returnFree);
            var anyParameterTagged = function.Parameters.Any(static parameter => parameter.HasMetadata(MetadataKeys.FreedBy));
            if (!returnTagged && !anyParameterTagged)
                continue;

            if (function.Parent is RecordBinding || function.IsVirtual || function.IsGeneric)
            {
                function.SetIgnoreReason("freed_by is supported on free functions only");
                continue;
            }

            var resolved = new List<(ParameterBinding? Parameter, StringFreeFunction Free)>();
            string? problem = null;

            foreach (var parameter in function.Parameters)
            {
                if (!parameter.TryGetMetadata<string>(MetadataKeys.FreedBy, out var name))
                    continue;

                var subject = $"parameter '{parameter.NativeName}'";
                if (TryResolveFreeFunction(compilation, name, subject, out var free, out problem))
                {
                    if (!parameter.GetMetadata<bool>(MetadataKeys.BorrowedStringOutput))
                    {
                        problem = $"{subject} is freed by '{name}' but is not an output string";
                    }
                    else
                    {
                        resolved.Add((parameter, free!));
                        continue;
                    }
                }

                break;
            }

            if (problem is null && returnTagged)
            {
                const string subject = "return value";
                if (function.Parameters.Any(static parameter => RequiresSpecialWrapper(parameter)))
                {
                    function.SetIgnoreReason(
                        $"{subject} is freed by '{returnFree}', which is not supported together with default-valued, pointer-array, derived-argument or record-pointer input parameters");
                    continue;
                }

                if (TryResolveFreeFunction(compilation, returnFree, subject, out var free, out problem))
                {
                    if (!function.GetMetadata<bool>(MetadataKeys.CharPointerReturn))
                        problem = $"{subject} is freed by '{returnFree}' but does not return a char pointer";
                    else
                        resolved.Add((null, free!));
                }
            }

            if (problem is not null)
            {
                function.SetIgnoreReason(problem);
                continue;
            }

            foreach (var (parameter, free) in resolved)
            {
                if (parameter is not null)
                {
                    parameter.SetMetadata(MetadataKeys.StringFreeFunction, free);
                    continue;
                }

                function.SetMetadata(MetadataKeys.StringFreeFunction, free);
                // The wrapper copies the text itself, so the import returns the raw address
                // instead of a borrowed span.
                function.ReturnType = BindingType.CreateConfigured(
                    function.CppReturnTypeName,
                    BindingType.IntPtr.ManagedTypeName,
                    primitiveKind: BindingPrimitiveKind.IntPtr);
            }
        }
    }

    /// <summary>
    /// Parameters that make the renderer use a dedicated wrapper writer (default-value overloads,
    /// pointer-array spans) that has no owned-return branch.
    /// </summary>
    private static bool RequiresSpecialWrapper(ParameterBinding parameter) =>
        PointerPointerShapeResolver.HasDefaultValue(parameter) ||
        parameter.HasMetadata(MetadataKeys.InputPointerArray) ||
        parameter.HasMetadata(MetadataKeys.DerivedArgument) ||
        (parameter.Direction == BindingParameterDirection.In &&
         parameter.AbiKind == BindingParameterAbiKind.Pointer &&
         parameter.Type.Kind == BindingTypeKind.Handle);

    // A `void*` parameter is bound as a plain address (IntPtr primitive), which IsPointerLike does
    // not recognize.
    private static bool TakesPointer(ParameterBinding parameter) =>
        PointerPointerShapeResolver.IsPointerLike(parameter) ||
        parameter.Type.PrimitiveKind == BindingPrimitiveKind.IntPtr;

    private static bool TryResolveFreeFunction(
        BindingCompilation compilation,
        string? name,
        string subject,
        out StringFreeFunction? free,
        out string? problem)
    {
        free = null;
        problem = null;

        var candidates = compilation.Functions
            .Where(candidate => string.Equals(candidate.NativeName, name, StringComparison.Ordinal) &&
                                candidate.Parent is not RecordBinding &&
                                !candidate.IsGeneric)
            .ToArray();
        if (candidates.Length == 0)
        {
            // A free function of a dependency module: its exporting module validated it and
            // published the release thunk, so it is used as exported.
            var imported = compilation.ImportedStringFreeFunctions
                .Where(candidate => string.Equals(candidate.NativeName, name, StringComparison.Ordinal))
                .Distinct()
                .ToArray();
            if (imported.Length == 1)
            {
                free = imported[0];
                return true;
            }

            problem = imported.Length > 1
                ? $"{subject} is freed by '{name}', which is exported by more than one imported module"
                : $"{subject} is freed by '{name}', which is not a free function of the compilation or of an imported module";
            return false;
        }

        if (candidates.Length > 1)
        {
            problem = $"{subject} is freed by '{name}', which is overloaded";
            return false;
        }

        var candidate = candidates[0];
        if (candidate.Parameters.Count != 1 || !TakesPointer(candidate.Parameters[0]))
        {
            problem = $"{subject} is freed by '{name}', which does not take one pointer parameter";
            return false;
        }

        free = new StringFreeFunction(candidate.NativeName, candidate.CppQualifiedName, candidate.Parameters[0].CppTypeName);
        return true;
    }
}
