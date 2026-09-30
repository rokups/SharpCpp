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
/// array and single shapes explicitly. An array can also name its count with
/// [[sharpcpp::counted_by(name)]] (or a consumer-set <see cref="MetadataKeys.CountedBy"/>),
/// which replaces the name heuristic; the count must be an integer value parameter, and an
/// explicit or consumer-set shape other than the parser's default still wins over it.
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

            // An explicit count replaces the name heuristic; it may be anywhere in the list.
            var hasAdjacentCount = parameter.HasMetadata(MetadataKeys.CountedBy)
                ? TryGetExplicitCount(function, parameter) is not null
                : CountPairsWith(function, index, index - 1) ||
                    CountPairsWith(function, index, index + 1);
            var shape = parameter.PointeeIsConst || hasAdjacentCount
                ? PointerPointerShapes.Array
                : PointerPointerShapes.Single;
            if (CanApply(parameter, shape))
            {
                parameter.SetMetadata(MetadataKeys.PointerPointerShape, shape);
                parameter.SetMetadata(MetadataKeys.PointerPointerDefaultShape, shape);
            }
        }
    }

    /// <summary>
    /// The integer value parameter that <paramref name="parameter"/> names through
    /// <see cref="MetadataKeys.CountedBy"/>, or null when it names none or the named parameter
    /// is missing or not an integer value.
    /// </summary>
    internal static ParameterBinding? TryGetExplicitCount(FunctionBinding function, ParameterBinding parameter)
    {
        if (!parameter.TryGetMetadata<string>(MetadataKeys.CountedBy, out var name))
            return null;

        var count = function.Parameters.FirstOrDefault(candidate =>
            !ReferenceEquals(candidate, parameter) && string.Equals(candidate.NativeName, name, StringComparison.Ordinal));
        return count is not null && ClangSharpHeaderParser.IsIntegerValueParameter(count) ? count : null;
    }

    /// <summary>True when some other parameter names <paramref name="candidate"/> as its count.</summary>
    internal static bool IsExplicitCount(FunctionBinding function, ParameterBinding candidate) =>
        ClangSharpHeaderParser.IsIntegerValueParameter(candidate) &&
        function.Parameters.Any(parameter =>
            !ReferenceEquals(parameter, candidate) &&
            parameter.TryGetMetadata<string>(MetadataKeys.CountedBy, out var name) &&
            string.Equals(name, candidate.NativeName, StringComparison.Ordinal));

    /// <summary>
    /// Validates every <see cref="MetadataKeys.CountedBy"/> tag of the function. A tag whose
    /// count is missing or not an integer value, or on a parameter that is not a pointer,
    /// ignores the function with a reason. A `char**`/record `T**` becomes an input array
    /// unless it carries a shape that is not the parser's default (an explicit
    /// [[sharpcpp::in]]/[[sharpcpp::out]] or a consumer-set shape wins over the count; a
    /// consumer shape equal to the default cannot be told apart from it). Returns true when a
    /// primitive or handle pointer was made an input array.
    /// </summary>
    private static bool ApplyCountedBy(FunctionBinding function)
    {
        List<ParameterBinding>? counted = null;
        foreach (var parameter in function.Parameters)
        {
            if (!parameter.TryGetMetadata<string>(MetadataKeys.CountedBy, out var name))
                continue;

            if (TryGetExplicitCount(function, parameter) is null)
            {
                function.SetIgnoreReason(
                    $"parameter '{parameter.NativeName}' is counted by '{name}', which is not an integer value parameter of the function");
                continue;
            }

            if (!IsPointerLike(parameter))
            {
                function.SetIgnoreReason(
                    $"parameter '{parameter.NativeName}' is counted by '{name}' but is not a pointer");
                continue;
            }

            if (parameter.PointerPointer == PointerPointerKind.None)
            {
                (counted ??= []).Add(parameter);
                continue;
            }

            parameter.TryGetMetadata<string>(MetadataKeys.PointerPointerShape, out var shape);
            parameter.TryGetMetadata<string>(MetadataKeys.PointerPointerDefaultShape, out var defaultShape);
            if (shape is null || shape == defaultShape)
                parameter.SetMetadata(MetadataKeys.PointerPointerShape, PointerPointerShapes.Array);
        }

        if (counted is null)
            return false;

        ClangSharpHeaderParser.ApplyExplicitCountedInputArrays(function);
        foreach (var parameter in counted)
        {
            // A pointer with no array projection (a record `T*`, an element kind the span
            // wrapper cannot carry) is never silent.
            if (!parameter.HasMetadata(MetadataKeys.InputPointerArray))
            {
                function.SetIgnoreReason(
                    $"parameter '{parameter.NativeName}' is counted by '{parameter.GetMetadata<string>(MetadataKeys.CountedBy)}' but has no input array projection");
            }
        }

        return true;
    }

    /// <summary>
    /// Validates every <see cref="MetadataKeys.EndedBy"/> tag of the function and binds each valid
    /// `(T* begin, T* end)` pair as one input array on begin; `end` becomes a derived argument
    /// (<see cref="MetadataKeys.DerivedArgument"/>) that keeps its native position and has no
    /// managed counterpart. An invalid tag ignores the function with its own reason. Returns
    /// true when a parameter was reshaped.
    /// </summary>
    private static bool ApplyEndedBy(FunctionBinding function)
    {
        var changed = false;
        for (var index = 0; index < function.Parameters.Count; ++index)
        {
            var begin = function.Parameters[index];
            if (!begin.TryGetMetadata<string>(MetadataKeys.EndedBy, out var name))
                continue;

            var next = function.Parameters.ElementAtOrDefault(index + 1);
            if (GetEndedByProblem(function, begin, next, name!, out var end) is { } problem)
            {
                function.SetIgnoreReason($"parameter '{begin.NativeName}' is ended by '{name}' but {problem}");
                continue;
            }

            if (begin.PointerPointer != PointerPointerKind.None)
            {
                // A char** / record T** range is an input array, like a counted one; the shape
                // loop in Apply validates it (and reports a problem) for the tagged begin.
                begin.SetMetadata(MetadataKeys.PointerPointerShape, PointerPointerShapes.Array);
                ApplyShape(begin, PointerPointerShapes.Array);
            }
            else
            {
                ClangSharpHeaderParser.ApplyAdjacentInputPointerArrayMetadata(begin);
            }

            if (begin.PointerPointer == PointerPointerKind.None && !begin.HasMetadata(MetadataKeys.InputPointerArray))
            {
                function.SetIgnoreReason($"parameter '{begin.NativeName}' is ended by '{name}' but has no input array projection");
                continue;
            }

            MarkPointerRangeEnd(begin, end);
            changed = true;
        }

        return changed;
    }

    /// <summary>The first reason the tag on <paramref name="begin"/> is invalid, or null.</summary>
    private static string? GetEndedByProblem(FunctionBinding function, ParameterBinding begin, ParameterBinding? next, string name, out ParameterBinding end)
    {
        end = begin;
        if (begin.HasMetadata(MetadataKeys.DirectorLoweredPointer))
            return "the parameter was lowered to an address value for the director callback before the consumer tag was applied; tag it with [[sharpcpp::ended_by]] in the header";
        var named = function.Parameters.FirstOrDefault(candidate =>
            !ReferenceEquals(candidate, begin) && string.Equals(candidate.NativeName, name, StringComparison.Ordinal));
        if (named is null)
            return "that is not another parameter of the function";
        if (!IsPointerLike(begin))
            return "the parameter is not a pointer";
        if (!HasSamePointerFacts(begin, named))
            return "its end is not a pointer of the same type";
        if (!ReferenceEquals(next, named))
            return "its end is not the parameter directly after it";
        end = named;
        if (begin.HasMetadata(MetadataKeys.CountedBy))
            return "the parameter is also counted_by";
        if (end.HasMetadata(MetadataKeys.CountedBy) || end.HasMetadata(MetadataKeys.EndedBy))
            return "its end is itself counted_by or ended_by";
        if (HasExplicitShape(begin, allowArray: true))
            return "the parameter has an explicit single or out pointer-pointer shape";
        if (HasExplicitShape(end, allowArray: false))
            return "its end has an explicit pointer-pointer shape";

        if (function.IsGeneric || function.IsConstructor || function.Parent is RecordBinding { IsGeneric: true })
            return "ended_by on a template function, a class template member or a constructor is not supported";
        if (HasDefaultValue(begin) || HasDefaultValue(end))
            return "pointer range begin/end cannot have a default value";
        return null;
    }

    private static bool HasDefaultValue(ParameterBinding parameter) =>
        parameter.HasMetadata(MetadataKeys.NativeDefaultValueExpression) ||
        parameter.HasMetadata(MetadataKeys.UnexpressibleDefault) ||
        parameter.HasMetadata(MetadataKeys.UndefaultableParam) ||
        parameter.ManagedDefaultValueExpression is not null;

    /// <summary>
    /// True when the parameter carries a pointer-pointer shape that is not the parser's default
    /// (an explicit [[sharpcpp::in]]/[[sharpcpp::out]] or a consumer-set shape); the array shape
    /// is acceptable when <paramref name="allowArray"/> (begin already wants an array).
    /// </summary>
    private static bool HasExplicitShape(ParameterBinding parameter, bool allowArray)
    {
        parameter.TryGetMetadata<string>(MetadataKeys.PointerPointerShape, out var shape);
        parameter.TryGetMetadata<string>(MetadataKeys.PointerPointerDefaultShape, out var defaultShape);
        return shape is not null && shape != defaultShape && !(allowArray && shape == PointerPointerShapes.Array);
    }

    private static bool HasSamePointerFacts(ParameterBinding begin, ParameterBinding end) =>
        begin.AbiKind == end.AbiKind &&
        begin.PointerPointer == end.PointerPointer &&
        begin.PointeeIsConst == end.PointeeIsConst &&
        begin.PointerElementIsConst == end.PointerElementIsConst &&
        begin.Type.Kind == end.Type.Kind &&
        begin.Type.PrimitiveKind == end.Type.PrimitiveKind &&
        begin.Type.RecordId == end.Type.RecordId &&
        begin.Type.EnumId == end.Type.EnumId &&
        begin.Type.ManagedTypeName == end.Type.ManagedTypeName;

    /// <summary>Turns <paramref name="end"/> into a plain native pointer computed from begin.</summary>
    private static void MarkPointerRangeEnd(ParameterBinding begin, ParameterBinding end)
    {
        foreach (var key in new[]
        {
            MetadataKeys.PointerPointerShape, MetadataKeys.PointerPointerDefaultShape,
            MetadataKeys.InputPointerArray, MetadataKeys.WritablePointerArray,
            MetadataKeys.UnexpressibleDefault, MetadataKeys.UndefaultableParam,
            MetadataKeys.NativeDefaultValueExpression
        })
        {
            end.RemoveMetadata(key);
        }

        end.Type = begin.Type;
        end.Direction = BindingParameterDirection.In;
        end.AbiKind = BindingParameterAbiKind.Pointer;
        end.ManagedDefaultValueExpression = null;
        end.SetMetadata(MetadataKeys.DerivedArgument, DerivedArgumentKinds.PointerRangeEnd);
        end.SetMetadata(MetadataKeys.DerivedFromParameter, begin.NativeName);
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
            var changed = ApplyCountedBy(function);
            changed |= ApplyEndedBy(function);
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
