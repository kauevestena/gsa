using System.Text.Json.Serialization;

namespace Gsa.Core.Geometry;

/// <summary>An immutable, finite vector in double precision. Map vectors use ENU axes.</summary>
public readonly record struct Vector3d
{
    public double X { get; }
    public double Y { get; }
    public double Z { get; }

    [JsonConstructor]
    public Vector3d(double x, double y, double z)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z))
            throw new ArgumentOutOfRangeException(nameof(x), "Vector components must be finite.");
        (X, Y, Z) = (x, y, z);
    }

    public static Vector3d Zero => default;
    public static Vector3d UnitX => new(1, 0, 0);
    public static Vector3d UnitY => new(0, 1, 0);
    public static Vector3d UnitZ => new(0, 0, 1);

    public double Length
    {
        get
        {
            var scale = Math.Max(Math.Abs(X), Math.Max(Math.Abs(Y), Math.Abs(Z)));
            if (scale == 0)
                return 0;
            return scale * Math.Sqrt(SquaredScaledLength(scale));
        }
    }

    public Vector3d Normalized()
    {
        var scale = Math.Max(Math.Abs(X), Math.Max(Math.Abs(Y), Math.Abs(Z)));
        if (scale == 0)
            throw new InvalidOperationException("The zero vector has no direction.");
        var inverseScaledLength = 1 / Math.Sqrt(SquaredScaledLength(scale));
        return new(X / scale * inverseScaledLength,
                   Y / scale * inverseScaledLength,
                   Z / scale * inverseScaledLength);
    }

    private double SquaredScaledLength(double scale) =>
        (X / scale) * (X / scale) + (Y / scale) * (Y / scale) + (Z / scale) * (Z / scale);

    public static double Dot(Vector3d left, Vector3d right) =>
        left.X * right.X + left.Y * right.Y + left.Z * right.Z;

    public static Vector3d Cross(Vector3d left, Vector3d right) => new(
        left.Y * right.Z - left.Z * right.Y,
        left.Z * right.X - left.X * right.Z,
        left.X * right.Y - left.Y * right.X);

    public static Vector3d operator +(Vector3d left, Vector3d right) =>
        new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    public static Vector3d operator -(Vector3d left, Vector3d right) =>
        new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    public static Vector3d operator -(Vector3d value) => new(-value.X, -value.Y, -value.Z);

    public static Vector3d operator *(Vector3d value, double scale) =>
        new(value.X * scale, value.Y * scale, value.Z * scale);

    public static Vector3d operator *(double scale, Vector3d value) => value * scale;

    public static Vector3d operator /(Vector3d value, double divisor)
    {
        if (!double.IsFinite(divisor) || divisor == 0)
            throw new ArgumentOutOfRangeException(nameof(divisor), "Divisor must be finite and nonzero.");
        return new(value.X / divisor, value.Y / divisor, value.Z / divisor);
    }
}
