using SharpCpp.Model.Ast;

namespace SharpCpp.Pipeline;

public static class BindingCompilationInheritancePass
{
    private static int s_projectedMethodCounter;

    public static void Apply(BindingCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        foreach (var record in compilation.Records.ToArray())
        {
            if (record.IsIgnored())
            {
                continue;
            }

            ProjectSecondaryBaseMethods(compilation, record);
        }
    }

    private static void ProjectSecondaryBaseMethods(BindingCompilation compilation, RecordBinding record)
    {
        var primaryBase = record.Bases.FirstOrDefault(static candidate => !candidate.IsVirtual);
        foreach (var secondaryBaseBinding in record.Bases.Where(baseBinding => !baseBinding.IsVirtual && !ReferenceEquals(baseBinding, primaryBase)))
        {
            var secondaryBase = compilation.FindRecordBySymbolId(secondaryBaseBinding.RecordId);
            if (secondaryBase is null || secondaryBase.IsIgnored())
            {
                continue;
            }

            if (secondaryBase.UsesVirtualBaseInheritance())
            {
                WarnVirtualBaseNotProjected(record, secondaryBase);
                continue;
            }

            foreach (var method in EnumerateProjectableMethods(compilation, record, secondaryBase))
            {
                if (method.IsIgnored())
                {
                    continue;
                }

                if (record.Members.OfType<FunctionBinding>().Any(existing => !existing.IsIgnored() && RecordBindingExtensions.SignaturesMatch(existing, method)))
                {
                    continue;
                }

                var projected = CloneProjectedMethod(compilation, record, secondaryBase, secondaryBaseBinding.CppQualifiedName, method);
                var covariantOverride = FindCovariantOverride(compilation, record, secondaryBase, method);
                if (covariantOverride is not null && projected.IsVirtual)
                {
                    // A director could not reproduce the covariant native override, so the base
                    // declaration is bound non-virtually.
                    projected.IsVirtual = false;
                    projected.IsPureVirtual = false;
                    projected.SetMetadata(MetadataKeys.CovariantProjectedBaseMethod, true);
                    // Name the member through its declaring record: the covariant override would
                    // otherwise win the lookup through the dispatch record and not convert.
                    projected.NativeDeclaringRecordCppTypeName ??= ResolveDeclaringRecordCppTypeName(method, secondaryBaseBinding.CppQualifiedName);
                    record.SetWarning($"'{method.CppQualifiedName}' is bound non-virtually on '{record.CppQualifiedName}' because '{covariantOverride.CppQualifiedName}' overrides it with a covariant return type");
                }

                record.Members.Add(projected);
            }
        }
    }

    // Yields the secondary base's own methods first, then those of its non-virtual bases
    // nearest-first, so the most-derived declaration of a method is projected and the hidden ones
    // are deduplicated away. A record that uses virtual-base inheritance is ignored later by
    // ValidateBootstrapSurfacePass, so nothing is projected through it (its methods would be
    // clones of declarations the record's own view does not have, and a member pointer to a
    // virtual base's method does not convert); it is warned about on the target record instead.
    // Clones projected into a base record by an earlier iteration are skipped: the declaration they
    // were cloned from is reachable through the base chain, and a clone of a clone would lose
    // CovariantProjectedBaseMethod (it is not copied), so SecondaryBaseViewPass would retype its
    // return to the I{Base} view while the interface member keeps the declared type (CS0738).
    // GetRenderableInterfaceMethods still enumerates base-first with the later declaration
    // winning; the two must keep agreeing on which declaration a member comes from.
    private static IEnumerable<FunctionBinding> EnumerateProjectableMethods(BindingCompilation compilation, RecordBinding targetRecord, RecordBinding record)
    {
        var records = new List<RecordBinding> { record };
        CollectNonVirtualBases(compilation, targetRecord, record, records, new HashSet<string>(StringComparer.Ordinal) { record.SymbolId });

        foreach (var baseRecord in records)
        {
            foreach (var method in baseRecord.Members.OfType<FunctionBinding>())
            {
                if (method.IsConstructor || method.IsStatic || method.OperatorToken is not null || method.IsConversionOperator ||
                    method.HasMetadata(MetadataKeys.ProjectedSecondaryBaseMethod))
                {
                    continue;
                }

                yield return method;
            }
        }
    }

    private static void CollectNonVirtualBases(
        BindingCompilation compilation,
        RecordBinding targetRecord,
        RecordBinding record,
        List<RecordBinding> results,
        HashSet<string> visited)
    {
        foreach (var baseBinding in record.Bases.Where(static baseBinding => !baseBinding.IsVirtual))
        {
            var baseRecord = compilation.FindRecordBySymbolId(baseBinding.RecordId);
            if (baseRecord is null || !visited.Add(baseRecord.SymbolId))
            {
                continue;
            }

            if (baseRecord.UsesVirtualBaseInheritance())
            {
                WarnVirtualBaseNotProjected(targetRecord, baseRecord);
                continue;
            }

            // An ignored base contributes no methods, but the bases behind it are still reachable.
            if (!baseRecord.IsIgnored())
            {
                results.Add(baseRecord);
            }

            CollectNonVirtualBases(compilation, targetRecord, baseRecord, results, visited);
        }
    }

    private static void WarnVirtualBaseNotProjected(RecordBinding record, RecordBinding skippedBase)
    {
        var warning = $"members of base record '{skippedBase.CppQualifiedName}' are not projected onto '{record.CppQualifiedName}' because '{skippedBase.CppQualifiedName}' uses virtual base inheritance";
        if (!record.GetWarnings().Contains(warning))
        {
            record.SetWarning(warning);
        }
    }

    private static FunctionBinding? FindCovariantOverride(
        BindingCompilation compilation,
        RecordBinding targetRecord,
        RecordBinding secondaryBase,
        FunctionBinding method)
    {
        foreach (var candidateRecord in secondaryBase.EnumerateAllBaseRecords(compilation).Prepend(secondaryBase).Prepend(targetRecord))
        {
            foreach (var candidate in candidateRecord.Members.OfType<FunctionBinding>())
            {
                if (candidate.HasMetadata(MetadataKeys.CovariantSecondaryBaseOverride) &&
                    RecordBindingExtensions.SignaturesMatch(candidate, method))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    internal static string ResolveDeclaringRecordCppTypeName(FunctionBinding sourceMethod, string dispatchRecordCppTypeName)
    {
        if (!string.IsNullOrWhiteSpace(sourceMethod.NativeDeclaringRecordCppTypeName))
        {
            return sourceMethod.NativeDeclaringRecordCppTypeName;
        }

        if (!string.IsNullOrWhiteSpace(sourceMethod.NativeDispatchRecordCppTypeName))
        {
            return sourceMethod.NativeDispatchRecordCppTypeName;
        }

        var owner = sourceMethod.FindOwningRecord();
        return owner is null || owner.IsGeneric ? dispatchRecordCppTypeName : owner.CppQualifiedName;
    }

    internal static FunctionBinding CloneProjectedMethod(
        BindingCompilation compilation,
        RecordBinding targetRecord,
        RecordBinding dispatchRecord,
        string dispatchRecordCppTypeName,
        FunctionBinding sourceMethod)
    {
        var entrypointName = $"{compilation.EntryPointPrefix}_{targetRecord.NativeName}_{dispatchRecord.NativeName}_{sourceMethod.NativeName}_projected_{++s_projectedMethodCounter}";
        var projectedMethod = new FunctionBinding(
            $"projected-secondary:{targetRecord.SymbolId}:{dispatchRecord.SymbolId}:{sourceMethod.SymbolId}",
            sourceMethod.NativeName,
            sourceMethod.ManagedName,
            sourceMethod.ReturnType,
            entryPointName: entrypointName,
            cppReturnTypeName: sourceMethod.CppReturnTypeName,
            cppQualifiedName: $"{dispatchRecord.CppQualifiedName}::{sourceMethod.NativeName}")
        {
            ImportMethodName = sourceMethod.ImportMethodName,
            IsStatic = false,
            IsVirtual = sourceMethod.IsVirtual,
            IsPureVirtual = sourceMethod.IsPureVirtual && targetRecord.IsAbstract,
            IsConstMethod = sourceMethod.IsConstMethod,
            NativeDispatchRecordCppTypeName = dispatchRecordCppTypeName,
            NativeDeclaringRecordCppTypeName = sourceMethod.NativeDeclaringRecordCppTypeName,
            OwningRecordCppTypeName = targetRecord.CppQualifiedName,
            IsConversionOperator = sourceMethod.IsConversionOperator,
            IsExplicitConversionOperator = sourceMethod.IsExplicitConversionOperator,
            OperatorToken = sourceMethod.OperatorToken,
            ReturnsOwnedHandle = sourceMethod.ReturnsOwnedHandle,
            ReturnPointerDepth = sourceMethod.ReturnPointerDepth
        };

        projectedMethod.SetMetadata(MetadataKeys.ProjectedSecondaryBaseMethod, true);

        if (sourceMethod.TryGetMetadata(MetadataKeys.DocumentationComment, out string? documentation) && !string.IsNullOrWhiteSpace(documentation))
        {
            projectedMethod.SetMetadata(MetadataKeys.DocumentationComment, documentation);
        }

        foreach (var parameter in sourceMethod.Parameters)
        {
            var projectedParameter = new ParameterBinding(
                $"{projectedMethod.SymbolId}:{parameter.ManagedName}",
                parameter.NativeName,
                parameter.ManagedName,
                parameter.Type,
                parameter.Direction,
                parameter.AbiKind,
                parameter.CppTypeName)
            {
                ManagedDefaultValueExpression = parameter.ManagedDefaultValueExpression,
                UseCallbackDelegate = parameter.UseCallbackDelegate,
                CallbackDelegateTypeName = parameter.CallbackDelegateTypeName,
                CallbackTakesOwnership = parameter.CallbackTakesOwnership,
                PointerPointer = parameter.PointerPointer,
                PointeeIsConst = parameter.PointeeIsConst,
                PointerElementIsConst = parameter.PointerElementIsConst
            };
            foreach (var key in ProjectedParameterFlagKeys)
            {
                if (parameter.HasMetadata(key))
                    projectedParameter.SetMetadata(key, true);
            }

            foreach (var key in ProjectedParameterStringKeys)
            {
                if (parameter.TryGetMetadata<string>(key, out var value))
                    projectedParameter.SetMetadata(key, value);
            }

            projectedMethod.Parameters.Add(projectedParameter);
        }

        foreach (var instantiation in sourceMethod.OwnedReturnRecordInstantiations)
        {
            projectedMethod.OwnedReturnRecordInstantiations.Add(instantiation);
        }

        return projectedMethod;
    }

    // Parameter metadata a projected method keeps: pointer-array projections and the pointer
    // range pair (begin's EndedBy, end's DerivedArgument), which the renderer needs to bind
    // the span and hide the end pointer.
    private static readonly string[] ProjectedParameterFlagKeys =
    [
        MetadataKeys.BorrowedStringOutput,
        MetadataKeys.InputPointerArray,
        MetadataKeys.RawStringPointerArray,
        MetadataKeys.NullTerminatedStringArray,
        MetadataKeys.WritablePointerArray
    ];

    private static readonly string[] ProjectedParameterStringKeys =
    [
        MetadataKeys.CountedBy,
        MetadataKeys.StringArrayCount,
        MetadataKeys.EndedBy,
        MetadataKeys.DerivedArgument,
        MetadataKeys.DerivedFromParameter
    ];
}
