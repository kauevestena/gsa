using Gsa.Core.Geometry;

namespace Gsa.Core.Sensors.Camera;

/// <summary>Immutable camera-input trajectory with bounded interpolation, never extrapolation.</summary>
public sealed class EstimatedBodyTrajectory
{
    private readonly EstimatedBodyPose[] _poses;

    public EstimatedBodyTrajectory(IEnumerable<EstimatedBodyPose> poses)
    {
        ArgumentNullException.ThrowIfNull(poses);
        _poses = poses.ToArray();
        if (_poses.Length == 0)
            throw new ArgumentException("At least one estimated pose is required.", nameof(poses));
        for (var index = 0; index < _poses.Length; index++)
        {
            if (_poses[index] is null)
                throw new ArgumentException("Trajectory poses cannot be null.", nameof(poses));
            if (index > 0)
            {
                var interval = _poses[index].TimeSeconds - _poses[index - 1].TimeSeconds;
                if (!double.IsFinite(interval) || interval <= 0)
                    throw new ArgumentException("Pose times must strictly increase with finite intervals.", nameof(poses));
            }
        }
    }

    public bool TrySample(double timeSeconds, out EstimatedBodyPose? pose)
    {
        if (!double.IsFinite(timeSeconds))
            throw new ArgumentOutOfRangeException(nameof(timeSeconds), "Sample time must be finite.");
        pose = null;
        if (timeSeconds < _poses[0].TimeSeconds || timeSeconds > _poses[^1].TimeSeconds)
            return false;
        if (timeSeconds == _poses[0].TimeSeconds)
        {
            pose = _poses[0];
            return true;
        }
        if (timeSeconds == _poses[^1].TimeSeconds)
        {
            pose = _poses[^1];
            return true;
        }
        var left = 0;
        var right = _poses.Length - 1;
        while (right - left > 1)
        {
            var middle = left + (right - left) / 2;
            if (_poses[middle].TimeSeconds <= timeSeconds)
                left = middle;
            else
                right = middle;
        }
        var first = _poses[left];
        var second = _poses[right];
        var fraction = (timeSeconds - first.TimeSeconds) / (second.TimeSeconds - first.TimeSeconds);
        var position = first.PositionEnu * (1 - fraction) + second.PositionEnu * fraction;
        pose = new(timeSeconds, position, Slerp(first.BodyToMap, second.BodyToMap, fraction));
        return true;
    }

    public bool TryGetCameraToMap(double sensorTimeSeconds, CameraMountEstimate mounting,
                                  out RigidTransform cameraToMap)
    {
        ArgumentNullException.ThrowIfNull(mounting);
        if (!double.IsFinite(sensorTimeSeconds))
            throw new ArgumentOutOfRangeException(nameof(sensorTimeSeconds), "Sensor time must be finite.");
        var evaluationTime = sensorTimeSeconds + mounting.TimeOffsetSeconds;
        if (!double.IsFinite(evaluationTime))
            throw new ArgumentOutOfRangeException(nameof(sensorTimeSeconds), "Corrected time must be finite.");
        cameraToMap = default;
        if (!TrySample(evaluationTime, out var pose))
            return false;
        cameraToMap = CameraGeometry.CameraToMap(pose!, mounting);
        return true;
    }

    private static Quaterniond Slerp(Quaterniond first, Quaterniond second, double fraction)
    {
        var x = second.X;
        var y = second.Y;
        var z = second.Z;
        var w = second.W;
        var dot = first.X * x + first.Y * y + first.Z * z + first.W * w;
        if (dot < 0)
        {
            (x, y, z, w) = (-x, -y, -z, -w);
            dot = -dot;
        }
        dot = Math.Clamp(dot, -1, 1);
        if (dot > 0.9995)
            return new(first.X + fraction * (x - first.X), first.Y + fraction * (y - first.Y),
                       first.Z + fraction * (z - first.Z), first.W + fraction * (w - first.W));
        var angle = Math.Acos(dot);
        var denominator = Math.Sin(angle);
        var firstWeight = Math.Sin((1 - fraction) * angle) / denominator;
        var secondWeight = Math.Sin(fraction * angle) / denominator;
        return new(firstWeight * first.X + secondWeight * x, firstWeight * first.Y + secondWeight * y,
                   firstWeight * first.Z + secondWeight * z, firstWeight * first.W + secondWeight * w);
    }
}
