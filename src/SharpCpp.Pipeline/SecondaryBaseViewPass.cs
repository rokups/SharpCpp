using SharpCpp.Model.Ast;

namespace SharpCpp.Pipeline;

/// <summary>
/// Types secondary-base pointer and reference parameters and returns as the base's managed
/// interface (<c>I{Base}</c>) so a derived wrapper (which can only inherit its primary base in
/// C#) can be passed, and a returned base pointer resolves to the most derived wrapper.
/// </summary>
/// <remarks>
/// A secondary base record is eligible when its interface is rendered with members, it is a
/// plain (non-generic, non-shared) wrapper, and every record implementing its interface reaches
/// it through exactly one base path, so the native <c>static_cast</c> from the derived record
/// to the base subobject is unambiguous. Eligible records carry
/// <see cref="MetadataKeys.SecondaryBaseView"/>. Parameters of eligible records are retyped
/// always; returns only when the base is polymorphic, because only then can the complete
/// object (and its most derived wrapper) be recovered from a subobject pointer. Returns of a
/// non-polymorphic secondary base keep the base class type. Virtual methods, constructors,
/// operators, accessor properties and base methods projected non-virtually past a covariant
/// override keep their declared types.
/// </remarks>
public static class SecondaryBaseViewPass
{
    public static void Apply(BindingCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var eligible = FindEligibleRecords(compilation);
        if (eligible.Count > 0)
            RetypeSecondaryBaseViews(compilation, eligible);

        // After retyping: return types are compared as the interfaces will render them.
        WarnOnRedeclaredInterfaceMethods(compilation);
    }

    // An interface inherits members its interface bases already expose instead of redeclaring them
    // (which would hide them, CS0108), so a redeclaration with another return type is dropped.
    private static void WarnOnRedeclaredInterfaceMethods(BindingCompilation compilation)
    {
        var interfaceRecordIds = compilation.Records
            .SelectMany(record => record.GetSecondaryBaseRecords(compilation))
            .Select(static record => record.SymbolId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var record in compilation.Records.Where(record => interfaceRecordIds.Contains(record.SymbolId) && !record.IsIgnored()))
        {
            foreach (var method in record.GetRenderableInterfaceMethods(compilation))
            {
                if (!record.TryFindDeclaringInterfaceRecord(compilation, method, out _, out var declaring))
                    continue;

                var inherited = declaring.GetRenderableInterfaceMethods(compilation)
                    .First(candidate => RecordBindingExtensions.ManagedSignaturesMatch(method, candidate));
                if (!ReturnTypesMatch(method.ReturnType, inherited.ReturnType))
                {
                    record.SetWarning(
                        $"'{method.CppQualifiedName}' redeclares '{inherited.ManagedName}' with a covariant return type; " +
                        "the interface exposes the base return type");
                }
            }
        }
    }

    private static bool ReturnTypesMatch(BindingType left, BindingType right)
        => left.Kind == right.Kind &&
           left.Marshaller.ManagedIsSecondaryBaseView == right.Marshaller.ManagedIsSecondaryBaseView &&
           string.Equals(left.ManagedTypeName, right.ManagedTypeName, StringComparison.Ordinal);

    private static void RetypeSecondaryBaseViews(BindingCompilation compilation, Dictionary<string, RecordBinding> eligible)
    {
        foreach (var record in eligible.Values)
            record.SetMetadata(MetadataKeys.SecondaryBaseView, true);

        foreach (var function in compilation.Functions)
        {
            if (!CanRetype(function))
                continue;

            foreach (var parameter in function.Parameters)
            {
                if (parameter.Direction == BindingParameterDirection.In && IsViewCandidate(parameter.Type, parameter.CppTypeName, eligible, out _))
                    parameter.Type = parameter.Type.WithSecondaryBaseView(true);
            }

            if (IsViewCandidate(function.ReturnType, function.CppReturnTypeName, eligible, out var returnRecord) && returnRecord.IsPolymorphic)
                function.ReturnType = function.ReturnType.WithSecondaryBaseView(true);
        }
    }

    private static Dictionary<string, RecordBinding> FindEligibleRecords(BindingCompilation compilation)
    {
        var records = compilation.Records.Where(static record => !record.IsIgnored()).ToArray();
        var candidates = new Dictionary<string, RecordBinding>(StringComparer.Ordinal);
        var rejected = new Dictionary<string, (RecordBinding Record, string Reason)>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            foreach (var secondary in record.GetSecondaryBaseRecords(compilation))
            {
                if (secondary.IsIgnored() || candidates.ContainsKey(secondary.SymbolId) || rejected.ContainsKey(secondary.SymbolId))
                    continue;

                var reason = GetIneligibleBaseReason(secondary, compilation);
                if (reason is null)
                    candidates.Add(secondary.SymbolId, secondary);
                else
                    rejected.Add(secondary.SymbolId, (secondary, reason));
            }
        }

        foreach (var record in records)
        {
            foreach (var reached in CollectInterfaceReach(record, compilation))
            {
                if (!candidates.TryGetValue(reached.SymbolId, out var candidate))
                    continue;

                string? reason = null;
                if (!IsPlainRecord(record))
                    reason = $"it is implemented by '{record.CppQualifiedName}', which is not a plain generated wrapper";
                else if (CountBasePaths(record, reached.SymbolId, compilation, 0) != 1)
                    reason = $"'{record.CppQualifiedName}' reaches it through a virtual or more than one base path, so the base subobject cast is ambiguous";

                if (reason is not null)
                {
                    candidates.Remove(reached.SymbolId);
                    rejected.Add(reached.SymbolId, (candidate, reason));
                }
            }
        }

        foreach (var (record, reason) in rejected.Values)
        {
            record.SetWarning(
                $"secondary base '{record.CppQualifiedName}' keeps class-typed pointer/reference parameters and returns " +
                $"(a derived wrapper cannot be passed as '{record.ManagedName}', and returns do not resolve to the most derived wrapper) because {reason}");
        }

        return candidates;
    }

    private static string? GetIneligibleBaseReason(RecordBinding secondary, BindingCompilation compilation)
    {
        if (!IsPlainRecord(secondary) || secondary.ManagedName.Contains('<', StringComparison.Ordinal))
            return "it is generic, shared from another module, or a native interface wrapper";

        return secondary.GetRenderableInterfaceMethods(compilation).Count == 0
            ? "its managed interface has no rendered members"
            : null;
    }

    private static bool IsPlainRecord(RecordBinding record)
        => !record.IsIgnored() &&
           !record.IsGeneric &&
           string.IsNullOrWhiteSpace(record.GetMetadata<string>(MetadataKeys.ManagedWrapperAssembly)) &&
           !record.GetMetadata<bool>(MetadataKeys.NativeInterfaceWrapper);

    private static IEnumerable<RecordBinding> CollectInterfaceReach(RecordBinding record, BindingCompilation compilation)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<RecordBinding>(record.GetSecondaryBaseRecords(compilation));
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!visited.Add(current.SymbolId))
                continue;

            yield return current;
            foreach (var next in current.GetSecondaryBaseRecords(compilation))
                pending.Push(next);
        }
    }

    private static int CountBasePaths(RecordBinding record, string targetId, BindingCompilation compilation, int depth)
    {
        if (depth > 64)
            return 2;

        var count = 0;
        foreach (var baseBinding in record.Bases)
        {
            if (string.Equals(baseBinding.RecordId, targetId, StringComparison.Ordinal))
            {
                // A virtual base is reached through the vtable; keep it out of the view.
                count += baseBinding.IsVirtual ? 2 : 1;
                continue;
            }

            var baseRecord = compilation.FindRecordBySymbolId(baseBinding.RecordId);
            if (baseRecord is not null)
                count += CountBasePaths(baseRecord, targetId, compilation, depth + 1);
        }

        return count;
    }

    private static bool CanRetype(FunctionBinding function)
    {
        if (function.IsIgnored() || function.IsConstructor || function.IsVirtual || function.IsOverride ||
            function.IsGeneric || function.IsConversionOperator || function.OperatorToken is not null ||
            function.HasMetadata(MetadataKeys.AccessorProperty) || function.HasMetadata(MetadataKeys.CovariantProjectedBaseMethod))
        {
            return false;
        }

        var owner = function.FindOwningRecord();
        return owner is null || IsPlainRecord(owner);
    }

    // The type must name the plain record itself (its default managed surface, no alias or
    // generic projection) and cross as a pointer or reference: a by-value record would copy
    // (and slice) the base, so it keeps the base class type. A record reference is mapped to
    // the record value type, so the declared C++ spelling decides pointer/reference-ness.
    private static bool IsViewCandidate(BindingType type, string? cppTypeName, Dictionary<string, RecordBinding> eligible, out RecordBinding record)
    {
        record = null!;
        return type.Kind == BindingTypeKind.Handle &&
            type.RecordId is not null &&
            IsPointerOrReference(cppTypeName) &&
            type.AliasCppQualifiedName is null &&
            type.GenericArguments.Count == 0 &&
            !type.IsCallback &&
            !type.IsFixedArray &&
            eligible.TryGetValue(type.RecordId, out record!) &&
            (type.ManagedTypeFormatOverride is null ||
             string.Equals(type.ManagedTypeFormatOverride, record.ManagedTypeFormat ?? record.ManagedName, StringComparison.Ordinal));
    }

    private static bool IsPointerOrReference(string? cppTypeName)
    {
        var trimmed = cppTypeName?.TrimEnd() ?? string.Empty;
        if (trimmed.EndsWith("const", StringComparison.Ordinal))
            trimmed = trimmed[..^"const".Length].TrimEnd();

        if (!trimmed.EndsWith('*') && !trimmed.EndsWith('&'))
            return false;

        // A pointer to a pointer (or a reference to one) is an out/array shape, not a view.
        var pointee = trimmed[..^1].TrimEnd();
        return !pointee.EndsWith('*') && !pointee.EndsWith('&');
    }
}
