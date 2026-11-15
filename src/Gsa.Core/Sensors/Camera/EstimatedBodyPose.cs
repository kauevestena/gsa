using Gsa.Core.Geometry;

namespace Gsa.Core.Sensors.Camera;

/// <summary>An explicit estimated trajectory sample; no world-truth type is accepted.</summary>
public sealed record EstimatedBodyPose
{
    public double TimeSeconds { get; }
    public Vector3d PositionEnu { get; }
    public Quaterniond BodyToMap { get; }

    public EstimatedBodyPose(double timeSeconds, Vector3d positionEnu, Quaterniond bodyToMap)
    {
        if (!double.IsFinite(timeSeconds))
            throw new ArgumentOutOfRangeException(nameof(timeSeconds), "Pose time must be finite.");
        bodyToMap.EnsureValid();
        (TimeSeconds, PositionEnu, BodyToMap) = (timeSeconds, positionEnu, bodyToMap);
    }
}
