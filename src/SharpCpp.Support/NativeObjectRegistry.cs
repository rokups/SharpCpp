using System.Collections.Concurrent;

namespace SharpCpp.Support;

public abstract class NativeObjectRegistry
{
    private readonly record struct RegisteredType(
        Type ExpectedType,
        Func<nint, bool, NativeObject> CreateFactory,
        INativeInstanceCache InstanceCache);

    private static readonly ConcurrentDictionary<nuint, RegisteredType> s_types = new();

    public static void RegisterType<T>()
        where T : NativeObject, INativeObjectRegistration<T>
    {
        var createFactory = T.RegistrationCreateFactory
            ?? throw new InvalidOperationException($"No managed wrapper factory is registered for '{typeof(T).FullName}'.");

        RegisterType(T.RegistrationDynamicTypeId, T.RegistrationExpectedType, createFactory, T.RegistrationInstanceCache);
    }

    public static void RegisterType(
        nuint dynamicTypeId,
        Type expectedType,
        Func<nint, bool, NativeObject> createFactory,
        INativeInstanceCache instanceCache)
    {
        ArgumentNullException.ThrowIfNull(expectedType);
        ArgumentNullException.ThrowIfNull(createFactory);
        ArgumentNullException.ThrowIfNull(instanceCache);

        s_types[dynamicTypeId] = new RegisteredType(expectedType, createFactory, instanceCache);
    }

    public static NativeObject Create<T>(
        nint handle,
        bool ownsHandle)
        where T : NativeObject, INativeObjectRegistration<T>
    {
        if (handle == IntPtr.Zero)
        {
            return null!;
        }

        var instanceCache = T.RegistrationInstanceCache;
        var factory = T.RegistrationCreateFactory;

        if (instanceCache.TryGetExisting(handle, ownsHandle, out var existing) && existing is not null)
        {
            return existing;
        }

        var metadata = T.RegistrationGetMetadata(handle).Normalize(handle);
        if (s_types.TryGetValue(metadata.DynamicTypeId, out var registeredType))
        {
            instanceCache = registeredType.InstanceCache;
            factory = registeredType.CreateFactory;
        }

        handle = metadata.Handle;

        if (instanceCache.TryGetExisting(handle, ownsHandle, out existing) && existing is not null)
        {
            return existing;
        }

        if (factory is null)
        {
            throw new InvalidOperationException($"No managed wrapper factory is available for '{typeof(T).FullName}'.");
        }

        return factory(handle, ownsHandle);
    }
}
