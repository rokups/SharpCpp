using System;

namespace Std.Infrastructure;

/// <summary>
/// Common identity carried by a provider emitted by one generated binding module.
/// </summary>
public interface IProvider
{
    /// <summary>
    /// Stable identifier for the generated binding module that owns this provider.
    /// </summary>
    string ModuleId { get; }

    /// <summary>
    /// Native shape handled by this provider. Providers for fixed arrays must include
    /// the native length in this key.
    /// </summary>
    string ShapeKey { get; }
}

/// <summary>
/// Operations needed by the shared <see cref="Std.String"/> wrapper.
/// </summary>
public interface IStringProvider : IProvider
{
    nint Create();

    nint Clone(nint handle) => throw new NotSupportedException("This provider does not expose cloning for the native shape.");

    void Delete(nint handle);

    ulong GetCount(nint handle);

    /// <summary>Returns a borrowed pointer to UTF-8 storage, or zero when unavailable.</summary>
    nint GetData(nint handle) => 0;

    byte GetAt(nint handle, ulong index);

    void Clear(nint handle);

    void Add(nint handle, byte value);
}

/// <summary>
/// Common operations for indexable native sequence containers.
/// </summary>
public interface ISequenceProvider<T> : IProvider
{
    nint Create();

    nint Clone(nint handle) => throw new NotSupportedException("This provider does not expose cloning for the native shape.");

    void Delete(nint handle);

    ulong GetCount(nint handle);

    void Clear(nint handle);

    T GetAt(nint handle, ulong index);

    void SetAt(nint handle, ulong index, T value);

    void Add(nint handle, T value);

    void InsertAt(nint handle, ulong index, T value);

    void RemoveAt(nint handle, ulong index);
}

/// <summary>Operations for a native <c>std::vector&lt;T&gt;</c>.</summary>
public interface IVectorProvider<T> : ISequenceProvider<T>
{
}

/// <summary>Operations for a native <c>std::list&lt;T&gt;</c>.</summary>
public interface IListProvider<T> : ISequenceProvider<T>
{
}

/// <summary>
/// Operations for a fixed-size native array. A provider instance handles one native length.
/// </summary>
public interface IFixedArrayProvider<T> : IProvider
{
    int Length { get; }

    nint Create();

    nint Clone(nint handle) => throw new NotSupportedException("This provider does not expose cloning for the native shape.");

    void Delete(nint handle);

    T GetAt(nint handle, nuint index);

    void SetAt(nint handle, nuint index, T value);
}

/// <summary>Operations for a dynamically sized native array-like wrapper.</summary>
public interface IDynamicArrayProvider<T> : IProvider
{
    nint Create(int length);

    nint Clone(nint handle) => throw new NotSupportedException("This provider does not expose cloning for the native shape.");

    void Delete(nint handle);

    T GetAt(nint handle, nuint index);

    void SetAt(nint handle, nuint index, T value);

    int? GetLength(nint handle) => null;
}

/// <summary>
/// Operations for ordered and unordered native associative containers.
/// </summary>
public interface IDictionaryProvider<TKey, TValue> : IProvider
    where TKey : notnull
{
    nint Create();

    nint Clone(nint handle) => throw new NotSupportedException("This provider does not expose cloning for the native shape.");

    void Delete(nint handle);

    ulong GetCount(nint handle);

    void Clear(nint handle);

    bool ContainsKey(nint handle, TKey key);

    TValue GetValue(nint handle, TKey key);

    void SetValue(nint handle, TKey key, TValue value);

    bool Add(nint handle, TKey key, TValue value);

    bool Remove(nint handle, TKey key);

    TKey GetKeyAt(nint handle, ulong index);

    TValue GetValueAt(nint handle, ulong index);
}

/// <summary>Operations for an ordered native map.</summary>
public interface IMapProvider<TKey, TValue> : IDictionaryProvider<TKey, TValue>
    where TKey : notnull
{
}

/// <summary>Operations for an unordered native map.</summary>
public interface IUnorderedMapProvider<TKey, TValue> : IDictionaryProvider<TKey, TValue>
    where TKey : notnull
{
}
