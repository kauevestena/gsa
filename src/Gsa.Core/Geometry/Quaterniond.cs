namespace Gsa.Core.Geometry;

/// <summary>A normalized quaternion (x,y,z,w); products apply right to left.</summary>
public readonly record struct Quaterniond
{
    public double X { get; }
    public double Y { get; }
    public double Z { get; }
    public double W { get; }

    public Quaterniond(double x, double y, double z, double w)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z) || !double.IsFinite(w))
            throw new ArgumentOutOfRangeException(nameof(x), "Quaternion components must be finite.");
        var scale = Math.Max(Math.Max(Math.Abs(x), Math.Abs(y)), Math.Max(Math.Abs(z), Math.Abs(w)));
        if (scale == 0)
            throw new ArgumentException("A zero quaternion does not define a rotation.", nameof(x));
        x /= scale;
        y /= scale;
        z /= scale;
        w /= scale;
        var norm = Math.Sqrt(x * x + y * y + z * z + w * w);
        (X, Y, Z, W) = (x / norm, y / norm, z / norm, w / norm);
    }

    public static Quaterniond Identity => new(0, 0, 0, 1);

    public static Quaterniond FromAxisAngle(Vector3d axis, double angleRadians)
    {
        if (!double.IsFinite(angleRadians))
            throw new ArgumentOutOfRangeException(nameof(angleRadians), "Angle must be finite.");
        var direction = axis.Normalized();
        var halfAngle = angleRadians / 2;
        var sine = Math.Sin(halfAngle);
        return new(direction.X * sine, direction.Y * sine, direction.Z * sine, Math.Cos(halfAngle));
    }

    public Vector3d Rotate(Vector3d vector)
    {
        EnsureValid();
        var imaginary = new Vector3d(X, Y, Z);
        var cross = 2 * Vector3d.Cross(imaginary, vector);
        return vector + W * cross + Vector3d.Cross(imaginary, cross);
    }

    public Quaterniond Inverse()
    {
        EnsureValid();
        return new(-X, -Y, -Z, W);
    }

    public static Quaterniond operator *(Quaterniond left, Quaterniond right)
    {
        left.EnsureValid();
        right.EnsureValid();
        return new(
            left.W * right.X + left.X * right.W + left.Y * right.Z - left.Z * right.Y,
            left.W * right.Y - left.X * right.Z + left.Y * right.W + left.Z * right.X,
            left.W * right.Z + left.X * right.Y - left.Y * right.X + left.Z * right.W,
            left.W * right.W - left.X * right.X - left.Y * right.Y - left.Z * right.Z);
    }

    internal void EnsureValid()
    {
        // Struct default construction bypasses the validating constructor.
        if (X == 0 && Y == 0 && Z == 0 && W == 0)
            throw new InvalidOperationException("An uninitialized quaternion is not a rotation; use Identity.");
    }
}
