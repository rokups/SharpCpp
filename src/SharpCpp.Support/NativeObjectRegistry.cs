using System.Collections.Concurrent;

namespace SharpCpp.Support;

/// <summary>
/// Static type registry mapping native dynamic type IDs to managed wrapper factories,
/// enabling polymorphic wrapper creation when the exact derived type is only known at runtime.
/// </summary>
public abstract class NativeObjectRegistry
{
    private readonly record struct RegisteredType(
        Type ExpectedType,
        Func<nint, bool, NativeObject> CreateFactory,
        INativeInstanceCache InstanceCache);

    private static readonly ConcurrentDictionary<nuint, RegisteredType> s_types = new();

    /// <summary>
    /// Registers the given wrapper type by reading its <see cref="INativeObjectRegistration{T}"/> static abstract members.
    /// </summary>
    /// <typeparam name="T">The wrapper type to register. Must implement <see cref="INativeObjectRegistration{T}"/>.</typeparam>
    /// <exception cref="InvalidOperationException">Thrown when <c>T.RegistrationCreateFactory</c> is <see langword="null"/>.</exception>
    public static void RegisterType<T>()
        where T : NativeObject, INativeObjectRegistration<T>
        => RegisterTypeCore<T>(T.RegistrationDynamicTypeId);

    /// <summary>
    /// Registers an additional native dynamic type ID that resolves to <typeparamref name="T"/>'s
    /// registration (expected type, factory and instance cache). Generated code aliases the native
    /// director class of <typeparamref name="T"/>, which is the dynamic type of every object
    /// constructed from managed code, so a base pointer to such an object resolves to its live
    /// wrapper with a single lookup.
    /// </summary>
    /// <typeparam name="T">The wrapper type the alias resolves to.</typeparam>
    /// <param name="aliasTypeId">The additional C++ runtime type identifier.</param>
    /// <exception cref="InvalidOperationException">Thrown when <c>T.RegistrationCreateFactory</c> is <see langword="null"/>.</exception>
    public static void RegisterTypeAlias<T>(nuint aliasTypeId)
        where T : NativeObject, INativeObjectRegistration<T>
        => RegisterTypeCore<T>(aliasTypeId);

    private static void RegisterTypeCore<T>(nuint dynamicTypeId)
        where T : NativeObject, INativeObjectRegistration<T>
    {
        if (!T.RegistrationIsPolymorphic)
        {
            return;
        }

        var createFactory = T.RegistrationCreateFactory
            ?? throw new InvalidOperationException($"No managed wrapper factory is registered for '{typeof(T).FullName}'.");

        RegisterType(dynamicTypeId, T.RegistrationExpectedType, createFactory, T.RegistrationInstanceCache);
    }

    public static void RegisterInstance<T>(T instance)
        where T : NativeObject, INativeObjectRegistration<T>
    {
        ArgumentNullException.ThrowIfNull(instance);

        instance.RegisterInInstanceCache(T.RegistrationInstanceCache);
    }

    /// <summary>
    /// Registers a factory for the given native dynamic type ID.
    /// </summary>
    /// <param name="dynamicTypeId">The C++ runtime type identifier to map.</param>
    /// <param name="expectedType">The expected CLR wrapper <see cref="Type"/>.</param>
    /// <param name="createFactory">A factory delegate that creates a managed wrapper for a given native handle and ownership flag.</param>
    /// <param name="instanceCache">The per-type instance cache for tracking live wrappers.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="expectedType"/>, <paramref name="createFactory"/>, or <paramref name="instanceCache"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Primary entry point for generated P/Invoke wrappers. Resolves the most derived registered wrapper type
    /// for the given native handle via cache lookup, native metadata query, and registry resolution.
    /// </summary>
    /// <typeparam name="T">The expected wrapper type. Must implement <see cref="INativeObjectRegistration{T}"/>.</typeparam>
    /// <param name="handle">The native object pointer.</param>
    /// <param name="ownsHandle">Whether the wrapper should own the native handle.</param>
    /// <returns>A managed wrapper instance for the native object.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no factory is available for the resolved type.</exception>
    public static NativeObject Create<T>(
        nint handle,
        bool ownsHandle)
        where T : NativeObject, INativeObjectRegistration<T>
    {
        return CreateCore<T>(handle, ownsHandle, typeof(T));
    }

    /// <summary>
    /// Creates or retrieves a wrapper for a native object seen through a secondary base.
    /// The most derived registered wrapper stands for the object when it implements
    /// <paramref name="viewType"/> (the secondary base's managed interface); the cache
    /// identity is then the complete object, so the same C# instance is returned whether
    /// the object was reached through its primary or a secondary base pointer. Otherwise
    /// the <typeparamref name="T"/> wrapper is created over the subobject pointer.
    /// </summary>
    /// <typeparam name="T">The secondary base wrapper type.</typeparam>
    /// <param name="handle">The native pointer to the secondary base subobject.</param>
    /// <param name="ownsHandle">Whether the wrapper should own the native handle.</param>
    /// <param name="viewType">The managed interface every acceptable wrapper implements.</param>
    /// <returns>A managed wrapper instance for the native object.</returns>
    public static NativeObject CreateView<T>(
        nint handle,
        bool ownsHandle,
        Type viewType)
        where T : NativeObject, INativeObjectRegistration<T>
    {
        ArgumentNullException.ThrowIfNull(viewType);
        return CreateCore<T>(handle, ownsHandle, viewType);
    }

    private static NativeObject CreateCore<T>(
        nint handle,
        bool ownsHandle,
        Type viewType)
        where T : NativeObject, INativeObjectRegistration<T>
    {
        if (handle == IntPtr.Zero)
        {
            return null!;
        }

        var instanceCache = T.RegistrationInstanceCache;
        var factory = T.RegistrationCreateFactory;
        if (T.RegistrationIsPolymorphic)
        {
            // The most derived wrapper stands for the object only when it is a viewType (T, or
            // the interface of a secondary base T). A secondary base whose most derived wrapper
            // does not implement the view keeps its own wrapper over the subobject pointer.
            var metadata = T.RegistrationGetMetadata(handle).Normalize(handle);
            if (s_types.TryGetValue(metadata.DynamicTypeId, out var registeredType) &&
                registeredType.ExpectedType.IsAssignableTo(viewType))
            {
                instanceCache = registeredType.InstanceCache;
                factory = registeredType.CreateFactory;
                handle = metadata.Handle;
            }
        }

        if (instanceCache.TryGetExisting(handle, ownsHandle: false, out var existing) && existing is not null)
        {
            if (ownsHandle)
            {
                if (existing.OwnsNativeHandle)
                {
                    // The wrapper already holds a reference (a shared or intrusive object);
                    // release the one this call was handed instead of leaking it.
                    // Some families return an uncounted pointer, so nothing was handed over.
                    if (existing.OwningReturnHandsOverReference)
                    {
                        factory?.Invoke(handle, true).Dispose();
                    }
                }
                else
                {
                    existing.TryPromoteToOwningHandle();
                }
            }

            return existing;
        }

        if (factory is null)
        {
            throw new InvalidOperationException($"No managed wrapper factory is available for '{typeof(T).FullName}'.");
        }

        var instance = factory(handle, ownsHandle);
        instance.RegisterInInstanceCache(instanceCache);
        return instance;
    }
}
