using SharpCpp.Model.Ast;

namespace SharpCpp.Pipeline;

public static class BindingCompilationInheritancePass
{
    public static void Apply(BindingCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        foreach (var record in compilation.Records.ToArray())
        {
            ProjectSecondaryBaseMethods(compilation, record);
        }
    }

    private static void ProjectSecondaryBaseMethods(BindingCompilation compilation, RecordBinding record)
    {
        foreach (var secondaryBase in record.GetSecondaryBaseRecords(compilation))
        {
            foreach (var method in EnumerateProjectableMethods(compilation, secondaryBase))
            {
                if (record.Members.OfType<FunctionBinding>().Any(existing => RecordBindingExtensions.SignaturesMatch(existing, method)))
                {
                    continue;
                }

                record.Members.Add(CloneProjectedMethod(record, secondaryBase, method));
            }
        }
    }

    private static IEnumerable<FunctionBinding> EnumerateProjectableMethods(BindingCompilation compilation, RecordBinding record)
    {
        foreach (var baseRecord in record.EnumerateAllBaseRecords(compilation).Reverse().Append(record))
        {
            foreach (var method in baseRecord.Members.OfType<FunctionBinding>())
            {
                if (method.IsConstructor || method.IsStatic)
                {
                    continue;
                }

                yield return method;
            }
        }
    }

    private static FunctionBinding CloneProjectedMethod(RecordBinding targetRecord, RecordBinding dispatchRecord, FunctionBinding sourceMethod)
    {
        var projectedMethod = new FunctionBinding(
            $"projected-secondary:{targetRecord.SymbolId}:{dispatchRecord.SymbolId}:{sourceMethod.SymbolId}",
            sourceMethod.NativeName,
            sourceMethod.ManagedName,
            sourceMethod.ReturnType,
            entryPointName: $"sharpcpp_e2e_{targetRecord.NativeName}_{dispatchRecord.NativeName}_{sourceMethod.NativeName}_projected",
            cppReturnTypeName: sourceMethod.CppReturnTypeName,
            cppQualifiedName: $"{dispatchRecord.CppQualifiedName}::{sourceMethod.NativeName}")
        {
            ImportMethodName = sourceMethod.ImportMethodName,
            IsStatic = false,
            IsVirtual = sourceMethod.IsVirtual,
            IsPureVirtual = sourceMethod.IsPureVirtual,
            IsConstMethod = sourceMethod.IsConstMethod,
            NativeDispatchRecordCppTypeName = dispatchRecord.CppQualifiedName,
            OwningRecordCppTypeName = targetRecord.CppQualifiedName,
            IsConversionOperator = sourceMethod.IsConversionOperator,
            IsExplicitConversionOperator = sourceMethod.IsExplicitConversionOperator,
            OperatorToken = sourceMethod.OperatorToken,
            ReturnsOwnedHandle = sourceMethod.ReturnsOwnedHandle
        };

        if (sourceMethod.TryGetMetadata(MetadataKeys.DocumentationComment, out string? documentation) && !string.IsNullOrWhiteSpace(documentation))
        {
            projectedMethod.SetMetadata(MetadataKeys.DocumentationComment, documentation);
        }

        foreach (var parameter in sourceMethod.Parameters)
        {
            projectedMethod.Parameters.Add(new ParameterBinding(
                $"{projectedMethod.SymbolId}:{parameter.ManagedName}",
                parameter.NativeName,
                parameter.ManagedName,
                parameter.Type,
                parameter.Direction,
                parameter.AbiKind,
                parameter.CppTypeName)
            {
                ManagedDefaultValueExpression = parameter.ManagedDefaultValueExpression
            });
        }

        return projectedMethod;
    }
}
