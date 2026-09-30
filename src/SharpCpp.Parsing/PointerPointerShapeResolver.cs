using SharpCpp.Model.Ast;

namespace SharpCpp.Parsing;

/// <summary>
/// Decides how `char**` and record `T**` parameters are bound. Both are ambiguous in C++:
/// `char**` is an array of strings or an output string, `T**` an array of pointers or an
/// output pointer. The parser tags each such parameter with a default shape
/// (<see cref="MetadataKeys.PointerPointerShape"/>); a consumer may overwrite the tag before
/// <see cref="Apply"/> runs, and the shape then reshapes the parameter from its parsed form.
/// </summary>
/// <remarks>
/// Defaults: `T* const*` is an input array; a non-const pointer-to-pointer with an adjacent
/// value count that relates to it is an input array; anything else is one output value
/// (`out string?` for char, `out T` for records). The count name set (argc, Num*, *Count*) is
/// deliberately narrower than the primitive-array heuristic, which also accepts *Length and
/// *Size, and the pairing is narrower still: `argc` pairs with `argv` or a `char**` right
/// after it, a specific count (`Num{X}`, `num_{x}`, `{x}Count`, `{x}_count`, `count{X}`) only
/// with a parameter whose stem is the same after normalisation (case-insensitive, underscores
/// dropped, a Hungarian `pp`/`p` prefix and a trailing plural `s` ignored), and a generic count
/// (`count`, `num`) only with the pointer parameter directly before it, or the one after
/// it when nothing pointer-like precedes. A count that names something else (`word_count`
/// before `spv_parsed_ir* parsed_ir`, `NumVertices` before `ppAllocation`) leaves the
/// parameter an output. [[sharpcpp::in]] and [[sharpcpp::out]] on the parameter select the
/// array and single shapes explicitly.
/// </remarks>
public static class PointerPointerShapeResolver
{
    /// <summary>Tags parameters without an explicit shape with the shape their neighbours imply.</summary>
    internal static void TagDefaultShapes(FunctionBinding function)
    {
        for (var index = 0; index < function.Parameters.Count; ++index)
        {
            var parameter = function.Parameters[index];
            if (parameter.PointerPointer == PointerPointerKind.None ||
                parameter.HasMetadata(MetadataKeys.PointerPointerShape))
            {
                continue;
            }

            var hasAdjacentCount =
                CountPairsWith(function, index, index - 1) ||
                CountPairsWith(function, index, index + 1);
            var shape = parameter.PointeeIsConst || hasAdjacentCount
                ? PointerPointerShapes.Array
                : PointerPointerShapes.Single;
            if (CanApply(parameter, shape))
                parameter.SetMetadata(MetadataKeys.PointerPointerShape, shape);
        }
    }

    /// <summary>
    /// True when the parameter at <paramref name="countIndex"/> is a value count that counts the
    /// pointer-to-pointer parameter at <paramref name="arrayIndex"/> (adjacent parameters).
    /// </summary>
    private static bool CountPairsWith(FunctionBinding function, int arrayIndex, int countIndex)
    {
        if (countIndex < 0 || countIndex >= function.Parameters.Count)
            return false;

        var array = function.Parameters[arrayIndex];
        var count = function.Parameters[countIndex];
        if (!ClangSharpHeaderParser.IsArrayCountParameter(count, strictName: true))
            return false;

        var countName = count.NativeName;
        if (countName.Equals("argc", StringComparison.OrdinalIgnoreCase))
        {
            return array.NativeName.Equals("argv", StringComparison.OrdinalIgnoreCase) ||
                array.PointerPointer == PointerPointerKind.Chars && countIndex < arrayIndex;
        }

        var countStem = GetCountStem(countName);
        if (countStem is null)
            return false;

        if (countStem.Length == 0)
        {
            // A generic count belongs to the pointer before it; only without one does it
            // belong to the parameter after it.
            var ownerIndex = countIndex > 0 && IsPointerLike(function.Parameters[countIndex - 1])
                ? countIndex - 1
                : countIndex + 1;
            return ownerIndex == arrayIndex;
        }

        return NormalizeStem(countStem) == NormalizeStem(array.NativeName);
    }

    private static bool IsPointerLike(ParameterBinding parameter) =>
        parameter.PointerPointer != PointerPointerKind.None ||
        parameter.AbiKind == BindingParameterAbiKind.Pointer ||
        parameter.Type.IsPointerType ||
        parameter.Type.Kind == BindingTypeKind.Handle;

    /// <summary>
    /// The `{x}` of `Num{X}`, `num_{x}`, `{x}Count`, `{x}_count` and `count{X}`; empty for a
    /// generic `count`/`num`; null when the name has no recognizable stem.
    /// </summary>
    private static string? GetCountStem(string name)
    {
        if (name.Equals("count", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("num", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        if (name.StartsWith("num", StringComparison.OrdinalIgnoreCase))
            return name[3..].Trim('_');
        if (name.EndsWith("count", StringComparison.OrdinalIgnoreCase))
            return name[..^5].Trim('_');
        if (name.StartsWith("count", StringComparison.OrdinalIgnoreCase))
            return name[5..].Trim('_');
        return null;
    }

    /// <summary>Case-insensitive, no underscores, no Hungarian `pp`/`p` prefix, no plural `s`.</summary>
    private static string NormalizeStem(string name)
    {
        foreach (var prefix in new[] { "pp", "p" })
        {
            if (name.Length > prefix.Length && name.StartsWith(prefix, StringComparison.Ordinal) &&
                char.IsUpper(name[prefix.Length]))
            {
                name = name[prefix.Length..];
                break;
            }
        }

        var normalized = name.Replace("_", string.Empty).ToLowerInvariant();
        return normalized.Length > 1 && normalized[^1] == 's' ? normalized[..^1] : normalized;
    }

    /// <summary>
    /// Applies every parameter's shape. A shape that cannot be honoured (an unknown value, a
    /// parameter that is not a `char**`/`T**`, or a form the shape cannot express) leaves the
    /// function ignored with a reason, so the drop reaches the diagnostic log.
    /// </summary>
    public static void Apply(BindingCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        foreach (var function in compilation.Functions)
        {
            var changed = false;
            foreach (var parameter in function.Parameters)
            {
                if (!parameter.TryGetMetadata<string>(MetadataKeys.PointerPointerShape, out var shape))
                    continue;

                if (TryGetProblem(parameter, shape) is { } problem)
                {
                    function.SetIgnoreReason(
                        $"parameter '{parameter.NativeName}' cannot use pointer-pointer shape '{shape}': {problem}");
                    continue;
                }

                ApplyShape(parameter, shape!);
                changed = true;
            }

            if (changed)
                ClangSharpHeaderParser.ApplyTrailingInputPointerArrayMetadata(function);
        }
    }

    internal static bool CanApply(ParameterBinding parameter, string shape) =>
        TryGetProblem(parameter, shape) is null;

    internal static string? TryGetProblem(ParameterBinding parameter, string? shape)
    {
        if (shape is not (PointerPointerShapes.Array or PointerPointerShapes.Single))
            return $"the value must be '{PointerPointerShapes.Array}' or '{PointerPointerShapes.Single}'";

        switch (parameter.PointerPointer)
        {
            case PointerPointerKind.Chars:
                var isString = parameter.Type.Kind == BindingTypeKind.Primitive &&
                    parameter.Type.ManagedTypeName is "string" or "global::System.String";
                var isRawPointer = parameter.Type.PrimitiveKind == BindingPrimitiveKind.IntPtr ||
                    parameter.Type.Kind == BindingTypeKind.Handle &&
                    string.IsNullOrEmpty(parameter.Type.RecordId) &&
                    parameter.Type.ManagedTypeName == BindingType.IntPtr.ManagedTypeName;
                if (shape == PointerPointerShapes.Array)
                    return isString || isRawPointer ? null : "the parameter is not projected as a string";
                if (parameter.PointeeIsConst)
                    return "the string pointer is const and cannot be written";
                return isString ? null : "the parameter is not projected as a string";

            case PointerPointerKind.Handles:
                if (parameter.Type.Kind != BindingTypeKind.Handle ||
                    string.IsNullOrEmpty(parameter.Type.RecordId) ||
                    parameter.Type.IsCallback ||
                    parameter.Type.IsFixedArray ||
                    parameter.AbiKind != BindingParameterAbiKind.Pointer)
                {
                    return "the parameter is not bound as a record pointer";
                }

                return shape == PointerPointerShapes.Single && parameter.PointeeIsConst
                    ? "the record pointer is const and cannot be written"
                    : null;

            default:
                return "the parameter is not a char** or a pointer-to-pointer to a record";
        }
    }

    private static void ApplyShape(ParameterBinding parameter, string shape)
    {
        var array = shape == PointerPointerShapes.Array;
        if (parameter.PointerPointer == PointerPointerKind.Chars)
        {
            if (array)
            {
                // The elements cross as raw pointers to NUL-terminated UTF-8 strings.
                if (parameter.Type.Kind == BindingTypeKind.Primitive && parameter.Type.PrimitiveKind != BindingPrimitiveKind.IntPtr)
                {
                    var elementCppTypeName = ClangSharpHeaderParser.NormalizeTypeName(parameter.CppTypeName);
                    parameter.Type = BindingType.CreateHandle(
                        elementCppTypeName[..elementCppTypeName.LastIndexOf('*')].TrimEnd(),
                        BindingType.IntPtr.ManagedTypeName);
                }

                parameter.Direction = BindingParameterDirection.In;
                parameter.AbiKind = BindingParameterAbiKind.Pointer;
                parameter.SetMetadata(MetadataKeys.InputPointerArray, true);
            }
            else
            {
                parameter.Direction = BindingParameterDirection.Out;
                parameter.AbiKind = BindingParameterAbiKind.Value;
                parameter.SetMetadata(MetadataKeys.BorrowedStringOutput, true);
            }

            return;
        }

        parameter.Direction = array ? BindingParameterDirection.In : BindingParameterDirection.Out;
    }
}
