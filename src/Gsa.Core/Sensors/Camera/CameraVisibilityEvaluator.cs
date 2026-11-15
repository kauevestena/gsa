using Gsa.Core.Geometry;
using Gsa.Core.World;

namespace Gsa.Core.Sensors.Camera;

public sealed record CameraVisibilityCandidate(
    string TargetId,
    string ObjectClass,
    Vector3d CenterEnu,
    Vector3d SizeEnu,
    IReadOnlyList<string> Tags)
{
    public static CameraVisibilityCandidate FromWorldObject(WorldBoxObject item) =>
        new(item.Id, item.ObjectClass, item.PositionEnu, item.SizeEnu, item.Tags);
}

public sealed record ObservedFeature2D(
    string TargetId,
    string ObjectClass,
    double CenterU,
    double CenterV,
    double MinU,
    double MinV,
    double MaxU,
    double MaxV,
    double MinProjectedDimensionPixels,
    int VisibleSampleRays,
    int TotalSampleRays,
    double RollingShutterShiftPixels,
    bool RequiresPrivacyReview);

/// <summary>Evaluates 5-ray surface visibility, 16 px threshold, and rolling shutter shift without leaking depth/truth.</summary>
public static class CameraVisibilityEvaluator
{
    public const int SampleRaysPerObject = 5;
    public const int RequiredUnoccludedRays = 3;

    public static IReadOnlyList<ObservedFeature2D> EvaluateVisibleFeatures(
        RigidTransform trueCameraToMapStart,
        RigidTransform trueCameraToMapReadoutEnd,
        CameraSensorProfile profile,
        IEnumerable<CameraVisibilityCandidate> candidates,
        Func<Vector3d, Vector3d, string, bool> isRayOccluded)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(isRayOccluded);

        var projector = new CameraProjector(profile.Intrinsics);
        var cameraOrigin = trueCameraToMapStart.Translation;
        var mapToCamStart = trueCameraToMapStart.Inverse();
        var mapToCamEnd = trueCameraToMapReadoutEnd.Inverse();

        var results = new List<ObservedFeature2D>();
        foreach (var candidate in candidates)
        {
            if (candidate.Tags.Contains("ground"))
                continue;

            var facePoints = SampleFrontFacePoints(candidate.CenterEnu, candidate.SizeEnu, cameraOrigin);
            var centerCam = mapToCamStart.Transform(candidate.CenterEnu);
            if (centerCam.Z <= 0 || centerCam.Z > profile.RangeMetres)
                continue;

            var centerProjection = projector.ProjectCameraPoint(centerCam);
            if (!centerProjection.IsVisible || centerProjection.Pixel is not { } centerPixel)
                continue;

            var unoccludedRays = 0;
            foreach (var point in facePoints)
            {
                var pCam = mapToCamStart.Transform(point);
                if (pCam.Z <= 0 || pCam.Z > profile.RangeMetres)
                    continue;
                if (!projector.ProjectCameraPoint(pCam).IsVisible)
                    continue;
                if (!isRayOccluded(cameraOrigin, point, candidate.TargetId))
                    unoccludedRays++;
            }

            if (unoccludedRays < RequiredUnoccludedRays)
                continue;

            var faceCorners = GetFrontFaceCorners(candidate.CenterEnu, candidate.SizeEnu, cameraOrigin);
            var minU = double.PositiveInfinity;
            var minV = double.PositiveInfinity;
            var maxU = double.NegativeInfinity;
            var maxV = double.NegativeInfinity;
            var allCornersInFront = true;
            foreach (var corner in faceCorners)
            {
                var cCam = mapToCamStart.Transform(corner);
                if (cCam.Z <= 0.05)
                {
                    allCornersInFront = false;
                    break;
                }
                var x = cCam.X / cCam.Z;
                var y = cCam.Y / cCam.Z;
                var u = profile.Intrinsics.Fx * x + profile.Intrinsics.Cx;
                var v = profile.Intrinsics.Fy * y + profile.Intrinsics.Cy;
                minU = Math.Min(minU, u);
                minV = Math.Min(minV, v);
                maxU = Math.Max(maxU, u);
                maxV = Math.Max(maxV, v);
            }
            if (!allCornersInFront)
                continue;

            var widthPx = maxU - minU;
            var heightPx = maxV - minV;
            var minDimPx = Math.Min(widthPx, heightPx);
            if (minDimPx < profile.MinProjectedFeaturePixels)
                continue;

            var rowFraction = Math.Clamp(centerPixel.V / profile.Intrinsics.Height, 0.0, 1.0);
            var readoutInterpolatedTransform = InterpolateLinear(trueCameraToMapStart, trueCameraToMapReadoutEnd, rowFraction);
            var pCamRow = readoutInterpolatedTransform.Inverse().Transform(candidate.CenterEnu);
            var rowEndProj = projector.ProjectCameraPoint(pCamRow);
            var rollingShift = rowEndProj.Pixel is { } shifted
                ? Math.Sqrt((shifted.U - centerPixel.U) * (shifted.U - centerPixel.U) +
                            (shifted.V - centerPixel.V) * (shifted.V - centerPixel.V))
                : 0.0;

            results.Add(new ObservedFeature2D(
                TargetId: candidate.TargetId,
                ObjectClass: candidate.ObjectClass,
                CenterU: centerPixel.U,
                CenterV: centerPixel.V,
                MinU: Math.Clamp(minU, 0.0, profile.Intrinsics.Width),
                MinV: Math.Clamp(minV, 0.0, profile.Intrinsics.Height),
                MaxU: Math.Clamp(maxU, 0.0, profile.Intrinsics.Width),
                MaxV: Math.Clamp(maxV, 0.0, profile.Intrinsics.Height),
                MinProjectedDimensionPixels: minDimPx,
                VisibleSampleRays: unoccludedRays,
                TotalSampleRays: SampleRaysPerObject,
                RollingShutterShiftPixels: rollingShift,
                RequiresPrivacyReview: candidate.Tags.Contains("privacy_review")));
        }

        return results;
    }

    /// <summary>Pure analytic ray-box occlusion tester for unit tests without Godot.</summary>
    public static Func<Vector3d, Vector3d, string, bool> CreateAnalyticBoxOcclusionTester(
        IEnumerable<WorldBoxObject> solidObjects)
    {
        var boxes = solidObjects.Where(o => o.Collision).ToArray();
        return (origin, targetPoint, targetId) =>
        {
            var delta = targetPoint - origin;
            var rayLength = delta.Length;
            if (rayLength <= 1e-6)
                return false;
            var dir = delta / rayLength;
            foreach (var box in boxes)
            {
                if (box.Id == targetId)
                    continue;
                if (TryIntersectRayBox(origin, dir, box.PositionEnu, box.SizeEnu, out var tHit) &&
                    tHit > 1e-4 && tHit < rayLength - 1e-3)
                    return true;
            }
            return false;
        };
    }

    public static bool TryIntersectRayBox(Vector3d origin, Vector3d unitDir, Vector3d center, Vector3d size, out double tHit)
    {
        tHit = 0;
        var half = size * 0.5;
        var min = center - half;
        var max = center + half;
        var tMin = 0.0;
        var tMax = double.PositiveInfinity;

        if (!UpdateSlab(origin.X, unitDir.X, min.X, max.X, ref tMin, ref tMax)) return false;
        if (!UpdateSlab(origin.Y, unitDir.Y, min.Y, max.Y, ref tMin, ref tMax)) return false;
        if (!UpdateSlab(origin.Z, unitDir.Z, min.Z, max.Z, ref tMin, ref tMax)) return false;

        tHit = tMin;
        return tMax >= tMin && tHit >= 0;
    }

    private static bool UpdateSlab(double o, double d, double min, double max, ref double tMin, ref double tMax)
    {
        if (Math.Abs(d) < 1e-12)
            return o >= min && o <= max;
        var inv = 1.0 / d;
        var t1 = (min - o) * inv;
        var t2 = (max - o) * inv;
        if (t1 > t2) (t1, t2) = (t2, t1);
        tMin = Math.Max(tMin, t1);
        tMax = Math.Min(tMax, t2);
        return tMin <= tMax;
    }

    public static Vector3d[] SampleFrontFacePoints(Vector3d center, Vector3d size, Vector3d cameraOrigin)
    {
        var corners = GetFrontFaceCorners(center, size, cameraOrigin);
        var faceCenter = (corners[0] + corners[1] + corners[2] + corners[3]) * 0.25;
        return
        [
            faceCenter,
            faceCenter + (corners[0] - faceCenter) * 0.65,
            faceCenter + (corners[1] - faceCenter) * 0.65,
            faceCenter + (corners[2] - faceCenter) * 0.65,
            faceCenter + (corners[3] - faceCenter) * 0.65,
        ];
    }

    public static Vector3d[] GetFrontFaceCorners(Vector3d center, Vector3d size, Vector3d cameraOrigin)
    {
        var half = size * 0.5;
        var diff = cameraOrigin - center;
        var nx = Math.Abs(diff.X) / Math.Max(1e-6, half.X);
        var ny = Math.Abs(diff.Y) / Math.Max(1e-6, half.Y);
        var nz = Math.Abs(diff.Z) / Math.Max(1e-6, half.Z);

        if (nx >= ny && nx >= nz)
        {
            var x = center.X + (diff.X >= 0 ? half.X : -half.X);
            return
            [
                new(x, center.Y - half.Y, center.Z - half.Z),
                new(x, center.Y + half.Y, center.Z - half.Z),
                new(x, center.Y + half.Y, center.Z + half.Z),
                new(x, center.Y - half.Y, center.Z + half.Z),
            ];
        }
        if (ny >= nx && ny >= nz)
        {
            var y = center.Y + (diff.Y >= 0 ? half.Y : -half.Y);
            return
            [
                new(center.X - half.X, y, center.Z - half.Z),
                new(center.X + half.X, y, center.Z - half.Z),
                new(center.X + half.X, y, center.Z + half.Z),
                new(center.X - half.X, y, center.Z + half.Z),
            ];
        }

        var z = center.Z + (diff.Z >= 0 ? half.Z : -half.Z);
        return
        [
            new(center.X - half.X, center.Y - half.Y, z),
            new(center.X + half.X, center.Y - half.Y, z),
            new(center.X + half.X, center.Y + half.Y, z),
            new(center.X - half.X, center.Y + half.Y, z),
        ];
    }

    private static RigidTransform InterpolateLinear(RigidTransform start, RigidTransform end, double fraction)
    {
        var pos = start.Translation * (1.0 - fraction) + end.Translation * fraction;
        var a = start.Rotation;
        var b = end.Rotation;
        var dot = a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
        var bx = dot < 0 ? -b.X : b.X;
        var by = dot < 0 ? -b.Y : b.Y;
        var bz = dot < 0 ? -b.Z : b.Z;
        var bw = dot < 0 ? -b.W : b.W;
        var q = new Quaterniond(
            a.X + fraction * (bx - a.X),
            a.Y + fraction * (by - a.Y),
            a.Z + fraction * (bz - a.Z),
            a.W + fraction * (bw - a.W));
        return new RigidTransform(q, pos);
    }
}

