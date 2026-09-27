using Flattened_fields;

using SharpCpp.Tests.E2E.Generated;

using Xunit;

namespace SharpCpp.Tests.E2E.Records;

public sealed class FlattenedFieldTests
{
    [Fact]
    public void Nested_plain_data_flattens_into_native_access_paths()
    {
        using var pipeline = new PipelineStateDesc { GraphicsPipelineDepthStencilDescBackFaceStencilFailOp = 7 };
        Assert.Equal(7, E2EBindings.ReadPipelineStencil(pipeline));
        Assert.NotNull(typeof(GraphicsPipelineDesc).GetProperty(nameof(GraphicsPipelineDesc.DepthStencilDescBackFaceStencilFailOp)));

        using var typedefSettings = new TypedefSettings { CapabilitiesEnabled = 3 };
        Assert.Equal(3, E2EBindings.ReadTypedefSettings(typedefSettings));

        using var requested = new RequestedSettings { RequestedEnabled = 1 };
        Assert.Equal(1, E2EBindings.ReadRequestedEnabled(requested));
    }

    [Fact]
    public void Records_with_methods_stay_wrappers_inside_plain_data()
    {
        using var settings = E2EBindings.CreateSettings();
        settings.EngineSettingsTorque.AddPoint(1, 2);
        Assert.Equal(1, E2EBindings.ReadSettingsTorque(settings));
        Assert.Equal(0, settings.Friction.PointCount());
        Assert.DoesNotContain(typeof(Settings).GetProperties(), static property => property.Name.EndsWith("Points", StringComparison.Ordinal));
    }

    [Fact]
    public void Class_template_specialization_fields_flatten()
    {
        using var drawList = new DrawList { VerticesSize = 4 };
        Assert.Equal(4, E2EBindings.ReadDrawListSize(drawList));
    }

    [Fact]
    public void Static_members_and_named_alias_storage_are_not_flattened()
    {
        using var owner = new StateOwner { StateValue = 5 };
        Assert.Equal(6, E2EBindings.ReadStateOwner(owner));
        Assert.Null(typeof(StateOwner).GetProperty("StateSharedValue"));

        using var vector = new AliasedStorageVector();
        vector.Components[0] = 1;
        vector.Components[1] = 2;
        vector.Components[2] = 3;
        vector.Components[3] = 4;
        Assert.Equal(10f, E2EBindings.SumAliasedStorageVector(vector));
        Assert.Null(typeof(AliasedStorageVector).GetProperty("ValueData"));
    }

    [Fact]
    public void Protected_root_fields_flatten_as_protected_members()
    {
        using var container = new ExposedContainer(12);
        Assert.Equal(12, E2EBindings.ReadProtectedContainer(container));
    }

    [Fact]
    public void Explicit_protected_constructor_is_not_replaced_by_a_public_one()
    {
        Assert.Empty(typeof(ProtectedConstructor).GetConstructors());
        using var value = ProtectedConstructorFactory.Create(5);
        Assert.Equal(5, value.Value);
    }

    private sealed class ExposedContainer : ProtectedContainer
    {
        public ExposedContainer(int value)
        {
            DataValue = value;
        }
    }
}
