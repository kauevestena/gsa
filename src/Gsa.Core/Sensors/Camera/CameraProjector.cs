using Gsa.Core.Geometry;

namespace Gsa.Core.Sensors.Camera;

/// <summary>Pure projection from explicitly supplied camera geometry or estimated trajectory inputs.</summary>
public sealed class CameraProjector
{
    public CameraIntrinsics Intrinsics { get; }

    public CameraProjector(CameraIntrinsics intrinsics)
    {
        ArgumentNullException.ThrowIfNull(intrinsics);
        Intrinsics = intrinsics;
    }

    public ProjectionResult ProjectCameraPoint(Vector3d point)
    {
        if (point.Z <= 0)
            return ProjectionResult.Failure(ProjectionStatus.BehindCamera);
        var x = point.X / point.Z;
        var y = point.Y / point.Z;
        var scale = 1.0;
        if (Intrinsics.K1 != 0 || Intrinsics.K2 != 0)
        {
            var squaredRadius = x * x + y * y;
            scale = 1 + Intrinsics.K1 * squaredRadius + Intrinsics.K2 * squaredRadius * squaredRadius;
        }
        var u = Intrinsics.Fx * x * scale + Intrinsics.Cx;
        var v = Intrinsics.Fy * y * scale + Intrinsics.Cy;
        if (!double.IsFinite(u) || !double.IsFinite(v))
            return ProjectionResult.Failure(ProjectionStatus.NonFiniteProjection);
        if (u < 0 || u >= Intrinsics.Width || v < 0 || v >= Intrinsics.Height)
            return ProjectionResult.Failure(ProjectionStatus.OutsideImage);
        return ProjectionResult.Success(new PixelPoint(u, v));
    }

    public ProjectionResult ProjectMapPoint(Vector3d knownMapPoint, double sensorTimeSeconds,
                                            EstimatedBodyTrajectory trajectory, CameraMountEstimate mounting)
    {
        ArgumentNullException.ThrowIfNull(trajectory);
        ArgumentNullException.ThrowIfNull(mounting);
        if (!trajectory.TryGetCameraToMap(sensorTimeSeconds, mounting, out var cameraToMap))
            return ProjectionResult.Failure(ProjectionStatus.TrajectoryUnavailable);
        return ProjectCameraPoint(cameraToMap.Inverse().Transform(knownMapPoint));
    }
}
