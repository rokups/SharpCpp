using SharpCpp.Model.Ast;

namespace SharpCpp.Pipeline;

public static class BindingCompilationGenericInstantiationPass
{
    public static void Apply(BindingCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        foreach (var record in compilation.Records)
        {
            record.SupportedGenericInstantiations.Clear();
        }

        foreach (var function in compilation.Functions)
        {
            CollectRecordInstantiations(compilation, function.ReturnType);
            foreach (var parameter in function.Parameters)
            {
                CollectRecordInstantiations(compilation, parameter.Type);
            }
        }

        foreach (var field in compilation.Descendants<FieldBinding>())
        {
            CollectRecordInstantiations(compilation, field.Type);
        }
    }

    private static void CollectRecordInstantiations(BindingCompilation compilation, BindingType type)
    {
        foreach (var genericArgument in type.GenericArguments)
        {
            CollectRecordInstantiations(compilation, genericArgument);
        }

        if (type.Kind != BindingTypeKind.Handle || string.IsNullOrWhiteSpace(type.RecordId) || type.GenericArguments.Count == 0 || type.ContainsGenericParameters)
        {
            return;
        }

        var record = compilation.Records.FirstOrDefault(candidate => string.Equals(candidate.SymbolId, type.RecordId, StringComparison.Ordinal));
        if (record is null || !record.IsGeneric || record.GenericParameters.Count != type.GenericArguments.Count)
        {
            return;
        }

        if (record.SupportedGenericInstantiations.Any(existing => InstantiationsEqual(existing.TypeArguments, type.GenericArguments)))
        {
            return;
        }

        record.SupportedGenericInstantiations.Add(new RecordGenericInstantiationBinding(type.GenericArguments));
    }

    private static bool InstantiationsEqual(IReadOnlyList<BindingType> left, IReadOnlyList<BindingType> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!string.Equals(left[index].CppTypeName, right[index].CppTypeName, StringComparison.Ordinal) ||
                left[index].Kind != right[index].Kind)
            {
                return false;
            }
        }

        return true;
    }
}
