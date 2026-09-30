namespace SharpCpp.Model.Ast;

/// <summary>How the begin of an <c>ended_by</c> pointer range binds as a managed span.</summary>
public enum PointerRangeBeginKind
{
    /// <summary>Not the begin of a bound pointer range.</summary>
    None = 0,

    /// <summary>A record <c>T**</c> range: <c>ReadOnlySpan&lt;T?&gt;</c> of wrappers.</summary>
    Record = 1,

    /// <summary>A primitive, enum or address element range: a span over the raw native elements.</summary>
    Raw = 2
}

public static class ParameterBindingRangeExtensions
{
    /// <summary>
    /// Classifies <paramref name="parameter"/> as the begin of a pointer range (an input pointer
    /// tagged with <see cref="MetadataKeys.EndedBy"/>). The derived end parameter is never a begin.
    /// </summary>
    public static PointerRangeBeginKind GetPointerRangeBeginKind(this ParameterBinding parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        if (parameter.HasMetadata(MetadataKeys.DerivedArgument) ||
            !parameter.HasMetadata(MetadataKeys.EndedBy) ||
            parameter.Direction != BindingParameterDirection.In ||
            parameter.AbiKind != BindingParameterAbiKind.Pointer)
        {
            return PointerRangeBeginKind.None;
        }

        var type = parameter.Type;
        if (type.Kind == BindingTypeKind.Handle && !string.IsNullOrEmpty(type.RecordId))
        {
            return PointerRangeBeginKind.Record;
        }

        var isRawElement = type.Kind is BindingTypeKind.Primitive or BindingTypeKind.Enum ||
            type.Kind == BindingTypeKind.Handle &&
            string.Equals(type.ManagedTypeName, "global::System.IntPtr", StringComparison.Ordinal);
        return isRawElement && parameter.HasMetadata(MetadataKeys.InputPointerArray)
            ? PointerRangeBeginKind.Raw
            : PointerRangeBeginKind.None;
    }
}
