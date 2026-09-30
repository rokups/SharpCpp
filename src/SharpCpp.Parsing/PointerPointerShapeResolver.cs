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
/// value count (argc, Num*, *Count*; deliberately narrower than the primitive-array heuristic, which also accepts *Length and *Size) is an input array; anything else is
/// one output value (`out string?` for char, `out T` for records). [[sharpcpp::in]] and
/// [[sharpcpp::out]] on the parameter select the array and single shapes explicitly.
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
                (index > 0 && ClangSharpHeaderParser.IsArrayCountParameter(function.Parameters[index - 1], strictName: true)) ||
                (index + 1 < function.Parameters.Count && ClangSharpHeaderParser.IsArrayCountParameter(function.Parameters[index + 1], strictName: true));
            var shape = parameter.PointeeIsConst || hasAdjacentCount
                ? PointerPointerShapes.Array
                : PointerPointerShapes.Single;
            if (CanApply(parameter, shape))
                parameter.SetMetadata(MetadataKeys.PointerPointerShape, shape);
        }
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
