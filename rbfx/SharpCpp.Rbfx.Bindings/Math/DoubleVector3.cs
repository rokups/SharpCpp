using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Urho3D.Math
{
    [StructLayout(LayoutKind.Sequential)]
    public struct DoubleVector3 : IEquatable<DoubleVector3>
    {
        public DoubleVector3(in DoubleVector2 vector, double z = 0.0)
        {
            X = vector.X;
            Y = vector.Y;
            Z = z;
        }

        public DoubleVector3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public DoubleVector3(IReadOnlyList<double> data)
        {
            X = data[0];
            Y = data[1];
            Z = data[2];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(in DoubleVector3 lhs, in DoubleVector3 rhs)
        {
            return lhs.X == rhs.X && lhs.Y == rhs.Y && lhs.Z == rhs.Z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(in DoubleVector3 lhs, in DoubleVector3 rhs)
        {
            return lhs.X != rhs.X || lhs.Y != rhs.Y || lhs.Z != rhs.Z;
        }

        public bool Equals(DoubleVector3 other)
        {
            return this == other;
        }

        public override bool Equals(object? obj)
        {
            return obj is DoubleVector3 other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(X, Y, Z);
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0} {1} {2}", X, Y, Z);
        }

        public static DoubleVector3 Parse(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? default : MathParsing.ToDoubleVector3(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public DoubleVector2 ToDoubleVector2()
        {
            return new DoubleVector2(X, Y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public DoubleVector2 ToXZ()
        {
            return new DoubleVector2(X, Z);
        }

        public double X;

        public double Y;

        public double Z;

        public static readonly DoubleVector3 Zero;

        public static readonly DoubleVector3 One = new(1.0, 1.0, 1.0);
    }
}