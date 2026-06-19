using SharpCpp.Model.Ast;

namespace SharpCpp.Pipeline;

public static class SilentlyExcludeConstOverloadMethodsPass
{
    public static void Apply(BindingCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        foreach (var record in compilation.Records)
        {
            foreach (var duplicateGroup in record.Members.OfType<FunctionBinding>()
                         .Where(static function => !function.IsIgnored())
                         .GroupBy(GetManagedPublicCallableSignatureKey, StringComparer.Ordinal)
                         .Where(static group => group.Any(static function => !function.IsConstMethod) &&
                             group.Any(static function => function.IsConstMethod)))
            {
                foreach (var constMethod in duplicateGroup.Where(static function => function.IsConstMethod))
                {
                    constMethod.SetSilentExclusionReason($"const overload '{constMethod.CppQualifiedName}' duplicates a non-const managed callable signature");
                }
            }
        }
    }

    private static string GetManagedPublicCallableSignatureKey(FunctionBinding function)
    {
        return string.Join(
            "|",
            function.ManagedName,
            function.IsStatic,
            function.IsConstructor,
            string.Join(
                ";",
                function.Parameters.Select(static parameter =>
                    string.Join(
                        ":",
                        parameter.Direction,
                        parameter.AbiKind,
                        parameter.Type.Kind,
                        parameter.Type.ManagedTypeName))));
    }
}
