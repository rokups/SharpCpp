using SharpCpp.Model.Ast;

namespace SharpCpp.Rbfx.Generator;

public static class RbfxHandwrittenManagedTypeSuppressionPass
{
    private enum HandwrittenMemberKind
    {
        Constructor,
        Method,
        FieldOrProperty
    }

    private readonly record struct HandwrittenMemberSignature(
        string ParentCppQualifiedName,
        string ManagedName,
        HandwrittenMemberKind Kind,
        int ParameterCount);

    private static readonly HashSet<string> SuppressedManagedTypes = new(StringComparer.Ordinal)
    {
        "Urho3D.Math.Intersection"
    };

    private static readonly HashSet<HandwrittenMemberSignature> SuppressedManagedMembers =
    [
        new("Rml::Context", "LoadDocumentFromMemory", HandwrittenMemberKind.Method, 2),
        new("Rml::DataModelConstructor", "GetModelHandle", HandwrittenMemberKind.Method, 0),
        new("Urho3D::Application", "Application", HandwrittenMemberKind.Constructor, 1),
        new("Urho3D::Application", "EngineParameters", HandwrittenMemberKind.FieldOrProperty, 0),
        new("Urho3D::Node", "CreateComponent", HandwrittenMemberKind.Method, 2),
        new("Urho3D::Object", "Context", HandwrittenMemberKind.FieldOrProperty, 0),
        new("Urho3D::Renderer", "SetViewport", HandwrittenMemberKind.Method, 2),
        new("Urho3D::RmlUI", "LoadFont", HandwrittenMemberKind.Method, 2),
        new("Urho3D::Scene", "Scene", HandwrittenMemberKind.Constructor, 1),
        new("Urho3D::StaticModel", "SetMaterial", HandwrittenMemberKind.Method, 2),
        new("Urho3D::StaticModel", "SetModel", HandwrittenMemberKind.Method, 1),
        new("Urho3D::Viewport", "Viewport", HandwrittenMemberKind.Constructor, 1)
    ];

    public static int Apply(BindingCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var projection = compilation.GetMetadata<ManagedBindingProjection>(MetadataKeys.ManagedBindingProjection);
        var suppressed = 0;

        foreach (var record in compilation.Records.Where(static record => !record.IsIgnored() && !record.GetMetadata<bool>(MetadataKeys.SuppressEmission)))
        {
            var managedNamespace = projection?.GetManagedNamespace(record, compilation) ?? record.ManagedNamespace ?? compilation.ManagedNamespace;
            if (SuppressedManagedTypes.Contains($"{managedNamespace}.{record.ManagedName}"))
            {
                record.SetMetadata(MetadataKeys.SuppressEmission, true);
                record.SetIgnoreReason("managed type is provided by handwritten rbfx binding source", overwriteExisting: false);
                suppressed++;
            }
        }

        foreach (var enumBinding in compilation.Enums.Where(static enumBinding => !enumBinding.IsIgnored() && !enumBinding.GetMetadata<bool>(MetadataKeys.SuppressEmission)))
        {
            var managedNamespace = projection?.GetManagedNamespace(enumBinding, compilation) ?? enumBinding.ManagedNamespace ?? compilation.ManagedNamespace;
            if (SuppressedManagedTypes.Contains($"{managedNamespace}.{enumBinding.ManagedName}"))
            {
                enumBinding.SetMetadata(MetadataKeys.SuppressEmission, true);
                enumBinding.SetIgnoreReason("managed type is provided by handwritten rbfx binding source", overwriteExisting: false);
                suppressed++;
            }
        }

        foreach (var function in compilation.Functions.Where(static function => !function.IsIgnored()))
        {
            if (function.Parent is not RecordBinding parentRecord)
            {
                continue;
            }

            var kind = function.IsConstructor
                ? HandwrittenMemberKind.Constructor
                : HandwrittenMemberKind.Method;
            if (SuppressedManagedMembers.Contains(new HandwrittenMemberSignature(parentRecord.CppQualifiedName, function.ManagedName, kind, function.Parameters.Count)))
            {
                function.SetSilentExclusionReason("managed member is provided by handwritten rbfx binding source", overwriteExisting: false);
                suppressed++;
            }
        }

        foreach (var field in compilation.Records.SelectMany(static record => record.Members.OfType<FieldBinding>()).Where(static field => !field.IsIgnored()))
        {
            if (field.Parent is RecordBinding parentRecord &&
                SuppressedManagedMembers.Contains(new HandwrittenMemberSignature(parentRecord.CppQualifiedName, field.ManagedName, HandwrittenMemberKind.FieldOrProperty, 0)))
            {
                field.SetSilentExclusionReason("managed member is provided by handwritten rbfx binding source", overwriteExisting: false);
                suppressed++;
            }
        }

        return suppressed;
    }
}
