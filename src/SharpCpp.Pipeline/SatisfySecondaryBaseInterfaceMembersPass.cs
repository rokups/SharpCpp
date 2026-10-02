using SharpCpp.Model.Ast;

namespace SharpCpp.Pipeline;

/// <summary>
/// Gives a record the secondary-base interface members its own declarations hide with another
/// return type. A derived record that redeclares a non-virtual method of a secondary base with
/// the same name and parameters but a different return type keeps only the hiding declaration
/// (the inheritance pass deduplicates by name and parameters), while the base's managed
/// interface declares the base return type, so no public C# member could implement it.
/// </summary>
/// <remarks>
/// The record gets a non-virtual clone of the hidden base declaration, tagged
/// <see cref="MetadataKeys.ExplicitInterfaceImplementation"/> with the interface record that
/// declares the member, and rendered as an explicit interface implementation that calls the base
/// declaration through its declaring record. Nothing is cloned, and the record is warned
/// instead, when the interface is generic, the member is an accessor property, its parameters
/// need convenience overloads, or the base subobject is not reached through exactly one path.
/// </remarks>
public static class SatisfySecondaryBaseInterfaceMembersPass
{
    public static void Apply(BindingCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var added = false;
        foreach (var record in compilation.Records.ToArray())
        {
            if (!record.IsIgnored())
                added |= SatisfyInterfaceMembers(compilation, record);
        }

        if (added)
            compilation.InvalidateCaches();
    }

    private static bool SatisfyInterfaceMembers(BindingCompilation compilation, RecordBinding record)
    {
        var added = false;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var direct in record.GetSecondaryBaseRecords(compilation))
        {
            foreach (var interfaceRecord in CollectReach(direct, compilation))
            {
                if (!visited.Add(interfaceRecord.SymbolId) || interfaceRecord.IsIgnored())
                    continue;

                foreach (var interfaceMethod in interfaceRecord.GetRenderableInterfaceMethods(compilation))
                {
                    // Members the interface inherits are implemented for the interface that declares them.
                    if (interfaceRecord.TryFindDeclaringInterfaceRecord(compilation, interfaceMethod, out _, out _))
                        continue;

                    added |= Satisfy(compilation, record, direct, interfaceRecord, interfaceMethod);
                }
            }
        }

        return added;
    }

    private static IEnumerable<RecordBinding> CollectReach(RecordBinding direct, BindingCompilation compilation)
    {
        yield return direct;
        foreach (var reached in SecondaryBaseViewPass.CollectInterfaceReach(direct, compilation))
            yield return reached;
    }

    private static bool Satisfy(
        BindingCompilation compilation,
        RecordBinding record,
        RecordBinding direct,
        RecordBinding interfaceRecord,
        FunctionBinding interfaceMethod)
    {
        if (record.Members.OfType<FunctionBinding>().Any(existing =>
                string.Equals(existing.GetMetadata<string>(MetadataKeys.ExplicitInterfaceImplementation), interfaceRecord.SymbolId, StringComparison.Ordinal) &&
                RecordBindingExtensions.ManagedSignaturesMatch(existing, interfaceMethod)))
        {
            return false;
        }

        var candidates = record.Members.OfType<FunctionBinding>()
            .Where(candidate => !candidate.IsConstructor && !candidate.IsStatic && !candidate.IsIgnored() &&
                                candidate.AccessModifier == BindingAccessModifier.Public &&
                                !candidate.HasMetadata(MetadataKeys.ExplicitInterfaceImplementation) &&
                                RecordBindingExtensions.ManagedSignaturesMatch(candidate, interfaceMethod))
            .ToArray();
        if (candidates.Length == 0 || candidates.Any(candidate => ReturnTypesMatch(candidate.ReturnType, interfaceMethod.ReturnType)))
            return false;

        var owner = interfaceMethod.FindOwningRecord();
        var reason = GetUnsupportedReason(compilation, record, interfaceRecord, interfaceMethod, owner);
        if (reason is not null)
        {
            record.SetWarning(
                $"'{candidates[0].CppQualifiedName}' hides '{interfaceMethod.CppQualifiedName}' with a different return type, " +
                $"so '{record.ManagedName}' cannot implement '{interfaceRecord.GetManagedInterfaceName()}.{interfaceMethod.ManagedName}' because {reason}");
            return false;
        }

        var directBinding = record.Bases.First(baseBinding => string.Equals(baseBinding.RecordId, direct.SymbolId, StringComparison.Ordinal));
        var dispatchName = string.IsNullOrWhiteSpace(directBinding.CppQualifiedName) ? direct.CppQualifiedName : directBinding.CppQualifiedName;
        var clone = BindingCompilationInheritancePass.CloneProjectedMethod(compilation, record, direct, dispatchName, interfaceMethod);
        clone.IsVirtual = false;
        clone.IsPureVirtual = false;
        clone.NativeDeclaringRecordCppTypeName ??= BindingCompilationInheritancePass.ResolveDeclaringRecordCppTypeName(interfaceMethod, dispatchName);
        clone.SetMetadata(MetadataKeys.ExplicitInterfaceImplementation, interfaceRecord.SymbolId);
        record.Members.Add(clone);
        return true;
    }

    private static string? GetUnsupportedReason(
        BindingCompilation compilation,
        RecordBinding record,
        RecordBinding interfaceRecord,
        FunctionBinding interfaceMethod,
        RecordBinding? owner)
    {
        if (interfaceRecord.IsGeneric || interfaceRecord.ManagedName.Contains('<', StringComparison.Ordinal))
            return $"'{interfaceRecord.CppQualifiedName}' is generic";

        if (interfaceMethod.HasMetadata(MetadataKeys.AccessorProperty))
            return $"'{interfaceMethod.ManagedName}' is rendered as an accessor property";

        if (interfaceMethod.Parameters.Any(static parameter =>
                parameter.HasMetadata(MetadataKeys.InputPointerArray) ||
                parameter.HasMetadata(MetadataKeys.UnexpressibleDefault) ||
                parameter.HasMetadata(MetadataKeys.UndefaultableParam)))
        {
            return $"'{interfaceMethod.ManagedName}' has parameters that need convenience overloads";
        }

        if (owner is null || owner.IsGeneric ||
            SecondaryBaseViewPass.CountBasePaths(record, owner.SymbolId, compilation, 0) != 1)
        {
            return "its declaring record is not reached through exactly one non-virtual base path";
        }

        return null;
    }

    private static bool ReturnTypesMatch(BindingType left, BindingType right) =>
        left.Kind == right.Kind &&
        string.Equals(left.RecordId, right.RecordId, StringComparison.Ordinal) &&
        string.Equals(left.ManagedTypeName, right.ManagedTypeName, StringComparison.Ordinal);
}
