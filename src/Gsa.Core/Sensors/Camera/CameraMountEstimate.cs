using Gsa.Core.Geometry;

namespace Gsa.Core.Sensors.Camera;

/// <summary>Estimated camera-to-body calibration, distinct from physical mounting truth.</summary>
public sealed class CameraMountEstimate
{
    public Vector3d LeverArmBody { get; }
    public Quaterniond CameraToBody { get; }
    public double TimeOffsetSeconds { get; }

    public CameraMountEstimate(Vector3d leverArmBody, Quaterniond cameraToBody, double timeOffsetSeconds = 0)
    {
        cameraToBody.EnsureValid();
        if (!double.IsFinite(timeOffsetSeconds))
            throw new ArgumentOutOfRangeException(nameof(timeOffsetSeconds), "Time offset must be finite.");
        (LeverArmBody, CameraToBody, TimeOffsetSeconds) = (leverArmBody, cameraToBody, timeOffsetSeconds);
    }

    /// <summary>Camera right/down/forward mapped to body front/left/up.</summary>
    public static CameraMountEstimate ForwardFacing { get; } =
        new(Vector3d.Zero, new Quaterniond(-0.5, 0.5, -0.5, 0.5));
}

public static class CameraGeometry
{
    /// <summary>p_map = r_body + R_body_to_map * (lever_arm + R_camera_to_body * p_camera).</summary>
    public static RigidTransform CameraToMap(EstimatedBodyPose pose, CameraMountEstimate mounting)
    {
        ArgumentNullException.ThrowIfNull(pose);
        ArgumentNullException.ThrowIfNull(mounting);
        return new RigidTransform(pose.BodyToMap, pose.PositionEnu) *
               new RigidTransform(mounting.CameraToBody, mounting.LeverArmBody);
    }
}
