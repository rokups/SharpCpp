using System.Reflection;

using Urho3D.Math;

namespace Urho3D.Core;

public static class ObjectReflection<T>
{
    public static StringHash TypeId { get; } = ResolveTypeId();

    private static StringHash ResolveTypeId()
    {
        var method = typeof(T).GetMethod("GetTypeStatic", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        return method?.Invoke(null, Array.Empty<object>()) is StringHash typeId
            ? typeId
            : new StringHash(typeof(T).Name);
    }
}
