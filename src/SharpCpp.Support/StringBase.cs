using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace SharpCpp.Support;

public abstract class StringBase : NativeObject, IReadOnlyList<char>, IEquatable<string>
{
    protected StringBase(nint handle, bool ownsHandle)
        : base(handle, ownsHandle)
    {
    }

    protected StringBase(nint handle, bool ownsHandle, string? value)
        : base(handle, ownsHandle)
    {
        Assign(value);
    }

    protected virtual ulong GetCountCore() => throw new NotImplementedException();
    protected virtual void ClearCore() => throw new NotImplementedException();
    protected virtual byte GetAtCore(ulong index) => throw new NotImplementedException();
    protected virtual void AddCore(byte value) => throw new NotImplementedException();

    public void Assign(string? value)
    {
        ClearCore();
        foreach (var utf8Byte in Encoding.UTF8.GetBytes(value ?? string.Empty))
            AddCore(utf8Byte);
    }

    public void Assign(ReadOnlySpan<byte> value)
    {
        ClearCore();
        foreach (var utf8Byte in value)
            AddCore(utf8Byte);
    }

    public byte[] ToUtf8Bytes()
    {
        var byteCount = checked((int)GetCountCore());
        if (byteCount == 0) return Array.Empty<byte>();
        var buffer = new byte[byteCount];
        for (var index = 0; index < buffer.Length; index++)
            buffer[index] = GetAtCore((ulong)index);
        return buffer;
    }

    public ReadOnlySpan<byte> AsUtf8Span() => ToUtf8Bytes();

    public int Count => ToString().Length;

    public char this[int index] => ToString()[index];

    public bool Equals(string? other)
        => string.Equals(ToString(), other, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj switch
    {
        StringBase other => Equals(other.ToString()),
        string other => Equals(other),
        _ => false
    };

    public override int GetHashCode()
        => StringComparer.Ordinal.GetHashCode(ToString());

    public override string ToString()
    {
        var buffer = ToUtf8Bytes();
        return buffer.Length == 0 ? string.Empty : Encoding.UTF8.GetString(buffer);
    }

    public IEnumerator<char> GetEnumerator()
        => ((IEnumerable<char>)ToString()).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
