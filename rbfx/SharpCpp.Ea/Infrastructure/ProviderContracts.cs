using System;

namespace Ea.Infrastructure;

/// <summary>Common identity carried by a generated EASTL provider.</summary>
public interface IProvider
{
    string ModuleId { get; }

    string ShapeKey { get; }
}

/// <summary>Operations needed by the shared <see cref="global::Ea.String"/> wrapper.</summary>
public interface IStringProvider : IProvider
{
    nint Create();

    nint Clone(nint handle) => throw new NotSupportedException("This provider does not expose cloning for the native shape.");

    void Delete(nint handle);

    ulong GetCount(nint handle);

    nint GetData(nint handle) => 0;

    byte GetAt(nint handle, ulong index);

    void Clear(nint handle);

    void Add(nint handle, byte value);
}

/// <summary>Common operations for indexable EASTL sequence containers.</summary>
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

/// <summary>Operations for <c>eastl::vector&lt;T&gt;</c>.</summary>
public interface IVectorProvider<T> : ISequenceProvider<T>
{
}

/// <summary>Operations for <c>eastl::list&lt;T&gt;</c>.</summary>
public interface IListProvider<T> : ISequenceProvider<T>
{
}

/// <summary>Operations for one native <c>eastl::array&lt;T, N&gt;</c> length.</summary>
public interface IFixedArrayProvider<T> : IProvider
{
    int Length { get; }

    nint Create();

    nint Clone(nint handle) => throw new NotSupportedException("This provider does not expose cloning for the native shape.");

    void Delete(nint handle);

    T GetAt(nint handle, nuint index);

    void SetAt(nint handle, nuint index, T value);
}

/// <summary>Operations shared by ordered and unordered EASTL maps.</summary>
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

/// <summary>Operations for <c>eastl::map&lt;TKey, TValue&gt;</c>.</summary>
public interface IMapProvider<TKey, TValue> : IDictionaryProvider<TKey, TValue>
    where TKey : notnull
{
}

/// <summary>Operations for <c>eastl::unordered_map&lt;TKey, TValue&gt;</c>.</summary>
public interface IUnorderedMapProvider<TKey, TValue> : IDictionaryProvider<TKey, TValue>
    where TKey : notnull
{
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

/// <summary>Compatibility name for older shared-array provider implementations.</summary>
public interface ISharedArrayProvider<T> : IDynamicArrayProvider<T>
{
}
