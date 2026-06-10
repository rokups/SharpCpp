using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Urho3D.Math
{
    [StructLayout(LayoutKind.Sequential)]
    public struct DoubleVector2 : IEquatable<DoubleVector2>
    {
        public DoubleVector2(double x, double y)
        {
            X = x;
            Y = y;
        }

        public DoubleVector2(IReadOnlyList<double> data)
        {
            X = data[0];
            Y = data[1];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(in DoubleVector2 lhs, in DoubleVector2 rhs)
        {
            return lhs.X == rhs.X && lhs.Y == rhs.Y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(in DoubleVector2 lhs, in DoubleVector2 rhs)
        {
            return lhs.X != rhs.X || lhs.Y != rhs.Y;
        }

        public bool Equals(DoubleVector2 other)
        {
            return this == other;
        }

        public override bool Equals(object? obj)
        {
            return obj is DoubleVector2 other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(X, Y);
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0} {1}", X, Y);
        }

        public static DoubleVector2 Parse(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? default : MathParsing.ToDoubleVector2(value);
        }

        public double X;

        public double Y;

        public static readonly DoubleVector2 Zero;

        public static readonly DoubleVector2 One = new(1.0, 1.0);
    }
}