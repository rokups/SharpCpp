using RbfxUserCode;

namespace Rbfx.Application;

internal static class Program
{
    private static void TouchImportedComponent(RotatingComponent component)
    {
        component.SetRotationSpeed(component.GetRotationSpeed() + 15.0f);
    }

    private static int Main()
    {
        _ = typeof(RotatingComponent);
        _ = (Action<RotatingComponent>)TouchImportedComponent;
        return 0;
    }
}
