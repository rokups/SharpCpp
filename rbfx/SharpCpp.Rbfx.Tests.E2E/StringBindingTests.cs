using System.Linq;

using SharpCpp.Support;

using Urho3D;

using Xunit;

namespace SharpCpp.Rbfx.Tests.E2E;

public sealed class StringBindingTests
{
    [Fact]
    public void Rbfx_ea_string_surfaces_project_as_Ea_String_wrapper()
    {
        var bindingsAssembly = typeof(Connection).Assembly;
        var eaStringType = bindingsAssembly.GetType("Ea.String", throwOnError: true)!;

        Assert.True(eaStringType.IsSubclassOf(typeof(StringBase)));

        Assert.Equal(eaStringType, typeof(Connection).GetMethod(nameof(Connection.GetAddress), Type.EmptyTypes)!.ReturnType);
        Assert.Equal(eaStringType, typeof(Network).GetMethod(nameof(Network.GetPackageCacheDir), Type.EmptyTypes)!.ReturnType);

        Assert.Contains(
            typeof(UIElement).GetMethods(),
            method => method.Name == "SetName" &&
                method.GetParameters().Length == 1 &&
                method.GetParameters()[0].ParameterType == eaStringType);
    }
}
