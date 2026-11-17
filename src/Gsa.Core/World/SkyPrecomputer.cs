using Gsa.Core.Geometry;

namespace Gsa.Core.World;

/// <summary>Static upper-hemisphere visibility over the exact neutral collision boxes.</summary>
public static class SkyPrecomputer
{
    public const string Algorithm = "fibonacci_hemisphere_aabb_v1";
    public const int MaximumCells = 1_000_000;

    public static SkyVisibilityCache Compute(WorldSceneManifest manifest, double cellSizeM = 10,
        double eyeHeightM = 2, int directionCount = 32)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ValidateSettings(cellSizeM, eyeHeightM, directionCount);
        var eastCount = GridCount(manifest.Bounds.Min.X, manifest.Bounds.Max.X, cellSizeM);
        var northCount = GridCount(manifest.Bounds.Min.Y, manifest.Bounds.Max.Y, cellSizeM);
        if ((long)eastCount * northCount > MaximumCells)
            throw new ArgumentOutOfRangeException(nameof(cellSizeM), "Sky grid exceeds one million cells.");
        var height = manifest.OriginEnu.Z + eyeHeightM;
        if (!double.IsFinite(height) || height < manifest.Bounds.Min.Z || height > manifest.Bounds.Max.Z)
            throw new ArgumentOutOfRangeException(nameof(eyeHeightM), "The sky sampling plane must lie within scene bounds.");

        var directions = BuildHemisphereDirections(directionCount);
        var boxes = manifest.Objects.Where(o => o.Collision && !o.Dynamic).Select(o =>
            new WorldBounds(o.PositionEnu - o.SizeEnu / 2, o.PositionEnu + o.SizeEnu / 2)).ToArray();
        var cells = new SkyCell[eastCount * northCount];
        for (var north = 0; north < northCount; north++)
        for (var east = 0; east < eastCount; east++)
        {
            var center = CellCenter(manifest.Bounds, cellSizeM, height, east, north);
            var open = 0;
            foreach (var direction in directions)
            {
                var blocked = false;
                foreach (var box in boxes)
                {
                    if (!IntersectsPositiveRay(center, direction, box)) continue;
                    blocked = true;
                    break;
                }
                if (!blocked) open++;
            }
            cells[north * eastCount + east] = new SkyCell(east, north, center, (double)open / directionCount);
        }
        return new SkyVisibilityCache(manifest.SceneId, manifest.MapVersion, manifest.DatumId,
            manifest.CanonicalGeometryHash, manifest.OriginEnu, manifest.Bounds,
            cellSizeM, eyeHeightM, directionCount, eastCount, northCount, cells);
    }

    internal static void ValidateSettings(double cellSizeM, double eyeHeightM, int directionCount)
    {
        if (!double.IsFinite(cellSizeM) || cellSizeM <= 0)
            throw new ArgumentOutOfRangeException(nameof(cellSizeM), "Cell size must be finite and positive.");
        if (!double.IsFinite(eyeHeightM) || eyeHeightM < 0)
            throw new ArgumentOutOfRangeException(nameof(eyeHeightM), "Eye height must be finite and nonnegative.");
        if (directionCount is < 1 or > 4096)
            throw new ArgumentOutOfRangeException(nameof(directionCount), "Use between 1 and 4096 sky directions.");
    }

    internal static int GridCount(double minimum, double maximum, double cellSizeM)
    {
        var count = Math.Ceiling((maximum - minimum) / cellSizeM);
        if (!double.IsFinite(count) || count < 1 || count > MaximumCells)
            throw new ArgumentOutOfRangeException(nameof(cellSizeM), "Invalid or excessive sky grid dimensions.");
        return (int)count;
    }

    internal static Vector3d CellCenter(WorldBounds bounds, double cellSizeM, double height, int east, int north)
    {
        var left = bounds.Min.X + east * cellSizeM;
        var bottom = bounds.Min.Y + north * cellSizeM;
        return new Vector3d(left + Math.Min(cellSizeM, bounds.Max.X - left) / 2,
            bottom + Math.Min(cellSizeM, bounds.Max.Y - bottom) / 2, height);
    }

    public static IReadOnlyList<Vector3d> CreateHemisphereDirections(int count)
    {
        if (count is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(count));
        return Array.AsReadOnly(BuildHemisphereDirections(count));
    }

    private static Vector3d[] BuildHemisphereDirections(int count)
    {
        var result = new Vector3d[count];
        var goldenAngle = Math.PI * (3 - Math.Sqrt(5));
        for (var index = 0; index < count; index++)
        {
            var up = (index + 0.5) / count;
            var radius = Math.Sqrt(1 - up * up);
            var azimuth = index * goldenAngle;
            result[index] = new Vector3d(radius * Math.Cos(azimuth), radius * Math.Sin(azimuth), up);
        }
        return result;
    }

    private static bool IntersectsPositiveRay(Vector3d origin, Vector3d direction, WorldBounds box)
    {
        var near = double.NegativeInfinity;
        var far = double.PositiveInfinity;
        return Slab(origin.X, direction.X, box.Min.X, box.Max.X, ref near, ref far) &&
               Slab(origin.Y, direction.Y, box.Min.Y, box.Max.Y, ref near, ref far) &&
               Slab(origin.Z, direction.Z, box.Min.Z, box.Max.Z, ref near, ref far) &&
               far >= Math.Max(near, 1e-6);
    }

    private static bool Slab(double origin, double direction, double minimum, double maximum,
        ref double near, ref double far)
    {
        if (Math.Abs(direction) < 1e-15)
            return origin >= minimum && origin <= maximum;
        var first = (minimum - origin) / direction;
        var second = (maximum - origin) / direction;
        near = Math.Max(near, Math.Min(first, second));
        far = Math.Min(far, Math.Max(first, second));
        return far >= near;
    }
}
