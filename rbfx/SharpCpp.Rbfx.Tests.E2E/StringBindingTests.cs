using SharpCpp.Support;

using Urho3D.UI;

using Xunit;

namespace SharpCpp.Rbfx.Tests.E2E;

public sealed class StringBindingTests
{
    [Fact]
    public void Rbfx_ea_string_wrapper_is_generated_with_ui_name_surface()
    {
        var eaStringType = typeof(global::Ea.String);
        var uiElementType = typeof(UIElement);

        Assert.True(eaStringType.IsSubclassOf(typeof(StringBase)));

        Assert.Equal(eaStringType, uiElementType.GetMethod("GetName", global::System.Type.EmptyTypes)!.ReturnType);
        Assert.Contains(
            uiElementType.GetMethods(),
            method => method.Name == "SetName" &&
                method.GetParameters() is [{ ParameterType: var parameterType }] &&
                parameterType == eaStringType);
    }
}
