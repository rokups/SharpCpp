namespace SharpCpp.Tests.Model;

using SharpCpp.Model.Ast;
using SharpCpp.Pipeline;

using Xunit;

public sealed class ConfigureIntrusiveReferenceCountingPassTests
{
    [Fact]
    public void Apply_configures_inherited_reference_targets_and_owned_returns()
    {
        var compilation = new BindingCompilation(
            "TestProject",
            "Test.Namespace",
            "Bindings",
            "Test.Native",
            ["Sample.hpp"]);
        var baseRecord = new RecordBinding("record:RefTarget", "RefTarget", "RefTarget");
        baseRecord.Members.Add(CreateReferenceMethod(baseRecord, "AddRef"));
        baseRecord.Members.Add(CreateReferenceMethod(baseRecord, "Release"));

        var targetRecord = new RecordBinding("record:Target", "Target", "Target");
        targetRecord.Bases.Add(new RecordBaseBinding(
            baseRecord.SymbolId,
            baseRecord.NativeName,
            baseRecord.ManagedName,
            baseRecord.CppQualifiedName,
            isVirtual: false));

        var createTarget = new FunctionBinding(
            "function:CreateTarget",
            "CreateTarget",
            "CreateTarget",
            targetRecord.HandleType);
        var targetField = new FieldBinding(
            "field:Target",
            "target",
            "Target",
            targetRecord.HandleType,
            "Target*",
            "Holder",
            isPointer: true);
        var holderRecord = new RecordBinding("record:Holder", "Holder", "Holder");
        holderRecord.Members.Add(targetField);

        compilation.Members.Add(baseRecord);
        compilation.Members.Add(targetRecord);
        compilation.Members.Add(createTarget);
        compilation.Members.Add(holderRecord);

        ConfigureIntrusiveReferenceCountingPass.Apply(compilation);

        Assert.Equal("Release", targetRecord.GetMetadata<string>(MetadataKeys.NativeInterfaceReleaseMethod));
        Assert.True(createTarget.ReturnsOwnedHandle);
        Assert.True(targetField.ReturnsOwnedHandle);
    }

    private static FunctionBinding CreateReferenceMethod(RecordBinding record, string name) =>
        new(
            $"method:{record.NativeName}:{name}",
            name,
            name,
            BindingType.Void)
        {
            IsStatic = false,
            OwningRecordCppTypeName = record.CppQualifiedName
        };
}
