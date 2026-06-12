using System;
using System.Collections;
using System.Collections.Generic;

namespace SharpCpp.Support;

public abstract class VectorBase<T> : IndexableCollectionBase<T>
{
    protected VectorBase(nint handle, bool ownsHandle)
        : base(handle, ownsHandle)
    {
    }
}

public abstract class ListBase<T> : IndexableCollectionBase<T>
{
    protected ListBase(nint handle, bool ownsHandle)
        : base(handle, ownsHandle)
    {
    }
}

public abstract class MapBase<TKey, TValue> : DictionaryBase<TKey, TValue>
    where TKey : notnull
{
    protected MapBase(nint handle, bool ownsHandle)
        : base(handle, ownsHandle)
    {
    }
}

public abstract class UnorderedMapBase<TKey, TValue> : DictionaryBase<TKey, TValue>
    where TKey : notnull
{
    protected UnorderedMapBase(nint handle, bool ownsHandle)
        : base(handle, ownsHandle)
    {
    }
}

public abstract class IndexableCollectionBase<T> : NativeObject, IList<T>
{
    protected IndexableCollectionBase(nint handle, bool ownsHandle)
        : base(handle, ownsHandle)
    {
    }

    protected abstract ulong GetCountCore();
    protected abstract void ClearCore();
    protected abstract T GetAtCore(ulong index);
    protected abstract void SetAtCore(ulong index, T value);
    protected abstract void AddCore(T value);
    protected abstract void InsertAtCore(ulong index, T value);
    protected abstract void RemoveAtCore(ulong index);

    public int Count => checked((int)GetCountCore());

    public bool IsReadOnly => false;

    public T this[int index]
    {
        get
        {
            ValidateExistingIndex(index);
            return GetAtCore((ulong)index);
        }
        set
        {
            ValidateExistingIndex(index);
            SetAtCore((ulong)index, value);
        }
    }

    public void Add(T item)
    {
        AddCore(item);
    }

    public void Clear()
    {
        ClearCore();
    }

    public bool Contains(T item)
    {
        return IndexOf(item) >= 0;
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);

        if (arrayIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(arrayIndex));
        }

        if (array.Length - arrayIndex < Count)
        {
            throw new ArgumentException("Destination array is too small.", nameof(array));
        }

        for (var index = 0; index < Count; index++)
        {
            array[arrayIndex + index] = GetAtCore((ulong)index);
        }
    }

    public IEnumerator<T> GetEnumerator()
    {
        for (var index = 0; index < Count; index++)
        {
            yield return GetAtCore((ulong)index);
        }
    }

    public int IndexOf(T item)
    {
        var comparer = EqualityComparer<T>.Default;
        for (var index = 0; index < Count; index++)
        {
            if (comparer.Equals(GetAtCore((ulong)index), item))
            {
                return index;
            }
        }

        return -1;
    }

    public void Insert(int index, T item)
    {
        ValidateInsertIndex(index);
        InsertAtCore((ulong)index, item);
    }

    public bool Remove(T item)
    {
        var index = IndexOf(item);
        if (index < 0)
        {
            return false;
        }

        RemoveAtCore((ulong)index);
        return true;
    }

    public void RemoveAt(int index)
    {
        ValidateExistingIndex(index);
        RemoveAtCore((ulong)index);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    protected void ValidateExistingIndex(int index)
    {
        if ((uint)index >= (uint)Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    protected void ValidateInsertIndex(int index)
    {
        if (index < 0 || index > Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
    }
}

public abstract class DictionaryBase<TKey, TValue> : NativeObject, IDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>
    where TKey : notnull
{
    protected DictionaryBase(nint handle, bool ownsHandle)
        : base(handle, ownsHandle)
    {
    }

    protected abstract ulong GetCountCore();
    protected abstract void ClearCore();
    protected abstract bool ContainsKeyCore(TKey key);
    protected abstract TValue GetValueCore(TKey key);
    protected abstract void SetValueCore(TKey key, TValue value);
    protected abstract bool AddCore(TKey key, TValue value);
    protected abstract bool RemoveCore(TKey key);
    protected abstract TKey GetKeyAtCore(ulong index);
    protected abstract TValue GetValueAtCore(ulong index);

    public int Count => checked((int)GetCountCore());

    public bool IsReadOnly => false;

    public TValue this[TKey key]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(key);
            if (!ContainsKeyCore(key))
            {
                throw new KeyNotFoundException();
            }

            return GetValueCore(key);
        }
        set
        {
            ArgumentNullException.ThrowIfNull(key);
            SetValueCore(key, value);
        }
    }

    public ICollection<TKey> Keys => MaterializeKeys();

    public ICollection<TValue> Values => MaterializeValues();

    IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => MaterializeKeys();

    IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => MaterializeValues();

    public void Add(TKey key, TValue value)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!AddCore(key, value))
        {
            throw new ArgumentException("An element with the same key already exists.", nameof(key));
        }
    }

    public bool ContainsKey(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return ContainsKeyCore(key);
    }

    public bool Remove(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return RemoveCore(key);
    }

    public bool TryGetValue(TKey key, out TValue value)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!ContainsKeyCore(key))
        {
            value = default!;
            return false;
        }

        value = GetValueCore(key);
        return true;
    }

    public void Add(KeyValuePair<TKey, TValue> item)
    {
        Add(item.Key, item.Value);
    }

    public void Clear()
    {
        ClearCore();
    }

    public bool Contains(KeyValuePair<TKey, TValue> item)
    {
        return TryGetValue(item.Key, out var value) && EqualityComparer<TValue>.Default.Equals(value, item.Value);
    }

    public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);

        if (arrayIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(arrayIndex));
        }

        if (array.Length - arrayIndex < Count)
        {
            throw new ArgumentException("Destination array is too small.", nameof(array));
        }

        for (var index = 0; index < Count; index++)
        {
            array[arrayIndex + index] = new KeyValuePair<TKey, TValue>(GetKeyAtCore((ulong)index), GetValueAtCore((ulong)index));
        }
    }

    public bool Remove(KeyValuePair<TKey, TValue> item)
    {
        return Contains(item) && RemoveCore(item.Key);
    }

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
    {
        for (var index = 0; index < Count; index++)
        {
            yield return new KeyValuePair<TKey, TValue>(GetKeyAtCore((ulong)index), GetValueAtCore((ulong)index));
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    private List<TKey> MaterializeKeys()
    {
        var keys = new List<TKey>(Count);
        for (var index = 0; index < Count; index++)
        {
            keys.Add(GetKeyAtCore((ulong)index));
        }

        return keys;
    }

    private List<TValue> MaterializeValues()
    {
        var values = new List<TValue>(Count);
        for (var index = 0; index < Count; index++)
        {
            values.Add(GetValueAtCore((ulong)index));
        }

        return values;
    }
}
