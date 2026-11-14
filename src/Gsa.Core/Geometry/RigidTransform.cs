namespace Gsa.Core.Geometry;

/// <summary>Transforms p as translation + rotation * p, with no change of units.</summary>
public readonly record struct RigidTransform
{
    public Quaterniond Rotation { get; }
    public Vector3d Translation { get; }

    public RigidTransform(Quaterniond rotation, Vector3d translation)
    {
        rotation.EnsureValid();
        (Rotation, Translation) = (rotation, translation);
    }

    public static RigidTransform Identity => new(Quaterniond.Identity, Vector3d.Zero);

    public Vector3d Transform(Vector3d point) => Translation + Rotation.Rotate(point);

    public RigidTransform Inverse()
    {
        var inverseRotation = Rotation.Inverse();
        return new(inverseRotation, inverseRotation.Rotate(-Translation));
    }

    /// <summary>Composition applies <paramref name="right"/> first, then <paramref name="left"/>.</summary>
    public static RigidTransform operator *(RigidTransform left, RigidTransform right) =>
        new(left.Rotation * right.Rotation, left.Transform(right.Translation));
}
