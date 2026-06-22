using System.Reflection;

using Urho3D.Math;

namespace Urho3D.Core;

public static class ObjectReflection<T>
{
    public static string ClassName { get; } = ResolveClassName();

    public static StringHash TypeId { get; } = ResolveTypeId();

    public static TypeInfo? TypeInfo { get; } = ResolveTypeInfo();

    private static string ResolveClassName()
    {
        var method = typeof(T).GetMethod("GetTypeNameStatic", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        return method?.Invoke(null, Array.Empty<object>()) is global::Ea.String className
            ? className.ToString()
            : typeof(T).Name;
    }

    private static StringHash ResolveTypeId()
    {
        var method = typeof(T).GetMethod("GetTypeStatic", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        return method?.Invoke(null, Array.Empty<object>()) is StringHash typeId
            ? typeId
            : new StringHash(ClassName);
    }

    private static TypeInfo? ResolveTypeInfo()
    {
        var method = typeof(T).GetMethod("GetTypeInfoStatic", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        return method?.Invoke(null, Array.Empty<object>()) as TypeInfo;
    }
}
