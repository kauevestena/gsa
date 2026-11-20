using Gsa.Core.Geometry;
using Gsa.Core.Sensors.Camera;

namespace Gsa.Missions.Gameplay;

/// <summary>Fits camera translation to surveyed control from image rays with fixed attitude.</summary>
public static class CityLocalControlAdjustment
{
    public static EstimatedBodyPose? Adjust(CameraObservationSample sample, IReadOnlyDictionary<string, Vector3d> controls)
    {
        var view = CameraGeometry.CameraToMap(sample.EstimatedPose, sample.MountEstimate);
        var inverse = view.Inverse();
        var rows = new List<(Vector3d Point, double X, double Y)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var feature in sample.ObservedFeatures)
        {
            if (!controls.TryGetValue(feature.TargetId, out var control) || !seen.Add(feature.TargetId)) continue;
            var ray = FeatureInventoryTriangulator.PixelToMapRay(sample, feature.CenterU, feature.CenterV);
            var direction = inverse.Rotation.Rotate(ray.DirectionEnu);
            if (direction.Z > 1e-6)
                rows.Add((inverse.Transform(control), direction.X / direction.Z, direction.Y / direction.Z));
        }

        if (rows.Count < 3) return null;
        var meanX = rows.Average(row => row.X);
        var meanY = rows.Average(row => row.Y);
        // In camera axes tx - x*tz = X - x*Z, and ty - y*tz = Y - y*Z.
        // Centering the equations removes tx/ty so the least-squares depth shift is scalar.
        var numerator = 0.0;
        var denominator = 0.0;
        foreach (var (point, x, y) in rows)
        {
            numerator += (x - meanX) * (point.X - x * point.Z) + (y - meanY) * (point.Y - y * point.Z);
            denominator += (x - meanX) * (x - meanX) + (y - meanY) * (y - meanY);
        }

        if (denominator < .0025) return null;
        var tz = -numerator / denominator;
        var correction = new Vector3d(
            rows.Average(row => row.Point.X - row.X * (row.Point.Z - tz)),
            rows.Average(row => row.Point.Y - row.Y * (row.Point.Z - tz)), tz);
        if (!double.IsFinite(correction.Length) || correction.Length > 3) return null;
        foreach (var (point, x, y) in rows)
        {
            var adjusted = point - correction;
            if (adjusted.Z <= 0 || Math.Abs(adjusted.X / adjusted.Z - x) * sample.Intrinsics.Fx > 3 ||
                Math.Abs(adjusted.Y / adjusted.Z - y) * sample.Intrinsics.Fy > 3)
                return null;
        }

        var pose = sample.EstimatedPose;
        return new(pose.TimeSeconds, pose.PositionEnu + view.Rotation.Rotate(correction), pose.BodyToMap);
    }
}
