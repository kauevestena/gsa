using Gsa.Core.Geometry;
using Gsa.Core.Randomness;
using Gsa.Core.Sensors.Camera;
using Gsa.Core.World;

namespace Gsa.Core.Sensors.Lidar;

public sealed record LidarMountEstimate(
    Vector3d LeverArmBodyMetres,
    Quaterniond LidarToBodyRotation,
    double TimeOffsetSeconds = 0.0)
{
    public static LidarMountEstimate Zero { get; } = new(Vector3d.Zero, Quaterniond.Identity, 0.0);

    public RigidTransform ToRigidTransform() => new(LidarToBodyRotation, LeverArmBodyMetres);
}

public sealed record LidarRayHit(
    double DistanceMetres,
    Vector3d NormalMap,
    string Material,
    string TargetId);

public sealed record LidarAnalyticBox(
    string Id,
    string Material,
    Vector3d CenterEnu,
    Vector3d SizeEnu);

public sealed record LidarMeasuredReturn(
    double AzimuthRadians,
    double ElevationRadians,
    double MeasuredRangeMetres,
    double Intensity,
    int ReturnNumber,
    int TotalReturnsInPulse,
    bool IsSpuriousRain,
    Vector3d LocalPointLidarMetres,
    Vector3d GeoreferencedPointEnu);

public sealed class LidarScanObservation
{
    public string ScanId { get; }
    public string ProfileId { get; }
    public double TimestampSeconds { get; }
    public EstimatedBodyPose EstimatedPose { get; }
    public LidarMountEstimate MountEstimate { get; }
    public int PrimaryRaysCast { get; }
    public int SpuriousRainReturns { get; }
    public double QueueBacklogSeconds { get; }
    public bool QueueSaturated { get; }
    public IReadOnlyList<LidarMeasuredReturn> Returns { get; }

    public LidarScanObservation(
        string scanId,
        string profileId,
        double timestampSeconds,
        EstimatedBodyPose estimatedPose,
        LidarMountEstimate mountEstimate,
        int primaryRaysCast,
        int spuriousRainReturns,
        double queueBacklogSeconds,
        bool queueSaturated,
        IEnumerable<LidarMeasuredReturn> returns)
    {
        ScanId = scanId;
        ProfileId = profileId;
        TimestampSeconds = timestampSeconds;
        EstimatedPose = estimatedPose;
        MountEstimate = mountEstimate;
        PrimaryRaysCast = primaryRaysCast;
        SpuriousRainReturns = spuriousRainReturns;
        QueueBacklogSeconds = queueBacklogSeconds;
        QueueSaturated = queueSaturated;
        Returns = Array.AsReadOnly(returns.ToArray());
    }
}

/// <summary>Bench LiDAR raycast simulator supporting multi-return transmissive materials, rain, calibration, and 0.5 s backlog limit.</summary>
public sealed class LidarBenchSimulator
{
    public const double MaxBacklogSeconds = 0.5;

    private readonly LidarProfile _profile;
    private readonly GaussianRandom _rng;
    private int _pendingScans;
    private long _nextScanSequence;

    public LidarProfile Profile => _profile;
    public int PendingQueueScans => _pendingScans;
    public int MaxPendingQueueScans { get; }
    public double CurrentBacklogSeconds => _pendingScans / _profile.ScanRateHz;
    public long TotalPrimaryRaysCast { get; private set; }
    public long TotalReturnsEmitted { get; private set; }
    public long TotalSpuriousRainReturns { get; private set; }
    public int DroppedScansOnSaturation { get; private set; }

    public LidarBenchSimulator(LidarProfile? profile = null, ulong seed = 20261115UL)
    {
        _profile = profile ?? LidarProfile.L4;
        _rng = new GaussianRandom(seed);
        MaxPendingQueueScans = Math.Max(1, (int)Math.Floor(MaxBacklogSeconds * _profile.ScanRateHz));
    }

    public int DrainQueue(int count)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));
        var drained = Math.Min(_pendingScans, count);
        _pendingScans -= drained;
        return drained;
    }

    public static RigidTransform ComposeLidarToMap(RigidTransform bodyToMap, LidarMountEstimate mount)
    {
        ArgumentNullException.ThrowIfNull(mount);
        return bodyToMap * mount.ToRigidTransform();
    }

    public bool TrySimulateScan(
        double timestampSeconds,
        RigidTransform trueLidarToMap,
        EstimatedBodyTrajectory trajectory,
        LidarMountEstimate mountEstimate,
        bool isRaining,
        Func<Vector3d, Vector3d, double, LidarRayHit?> rayCaster,
        out LidarScanObservation? observation,
        out string? stopReason,
        bool autoDrain = true,
        bool zeroNoise = false)
    {
        ArgumentNullException.ThrowIfNull(trajectory);
        ArgumentNullException.ThrowIfNull(mountEstimate);
        ArgumentNullException.ThrowIfNull(rayCaster);

        observation = null;
        stopReason = null;

        if (_pendingScans >= MaxPendingQueueScans)
        {
            DroppedScansOnSaturation++;
            stopReason = "sensor_queue_saturated";
            return false;
        }

        var sampleTime = timestampSeconds + mountEstimate.TimeOffsetSeconds;
        if (!trajectory.TrySample(sampleTime, out var estPose) || estPose is null)
        {
            stopReason = "trajectory_unavailable";
            return false;
        }

        _pendingScans++;
        try
        {
            var estLidarToMap = ComposeLidarToMap(new RigidTransform(estPose.BodyToMap, estPose.PositionEnu), mountEstimate);
            var effectiveRange = LidarMaterialProperties.GetEffectiveRangeMetres(_profile.MaxRangeMetres, isRaining);
            var pattern = _profile.BuildScanPattern();
            var returns = new List<LidarMeasuredReturn>(pattern.Count);
            var spuriousInScan = 0;

            foreach (var (az, el, dirLidar) in pattern)
            {
                TotalPrimaryRaysCast++;
                var dirMap = trueLidarToMap.Rotation.Rotate(dirLidar).Normalized();

                if (isRaining && !zeroNoise && _rng.NextUniformOpenUnit() < LidarMaterialProperties.RainSpuriousRate)
                {
                    var spuriousRange = Math.Max(0.5, _rng.NextUniformOpenUnit() * effectiveRange * 0.95);
                    var localPt = dirLidar * spuriousRange;
                    returns.Add(new LidarMeasuredReturn(
                        az, el, spuriousRange, 0.15, 1, 1, true, localPt, estLidarToMap.Transform(localPt)));
                    spuriousInScan++;
                    TotalSpuriousRainReturns++;
                    TotalReturnsEmitted++;
                    continue;
                }

                var pulseHits = TraceMultiReturnPulse(
                    trueLidarToMap.Translation, dirMap, effectiveRange, rayCaster, zeroNoise);
                for (var idx = 0; idx < pulseHits.Count; idx++)
                {
                    var (range, intensity) = pulseHits[idx];
                    var localPt = dirLidar * range;
                    returns.Add(new LidarMeasuredReturn(
                        az, el, range, intensity, idx + 1, pulseHits.Count, false, localPt, estLidarToMap.Transform(localPt)));
                    TotalReturnsEmitted++;
                }
            }

            _nextScanSequence++;
            observation = new LidarScanObservation(
                $"lidar_scan_{_nextScanSequence:000000}",
                _profile.Id,
                timestampSeconds,
                estPose,
                mountEstimate,
                pattern.Count,
                spuriousInScan,
                CurrentBacklogSeconds,
                false,
                returns);
            return true;
        }
        finally
        {
            if (autoDrain)
                DrainQueue(1);
        }
    }

    private List<(double MeasuredRange, double Intensity)> TraceMultiReturnPulse(
        Vector3d originMap,
        Vector3d dirMap,
        double effectiveMaxRange,
        Func<Vector3d, Vector3d, double, LidarRayHit?> rayCaster,
        bool zeroNoise)
    {
        var accepted = new List<(double, double)>(LidarMaterialProperties.MaxReturnsPerPulse);
        var residualEnergy = 1.0;
        var currentStartDist = 0.0;
        var seenTargets = new HashSet<string>(StringComparer.Ordinal);

        for (var bounce = 0; bounce < LidarMaterialProperties.MaxReturnsPerPulse; bounce++)
        {
            if (residualEnergy < LidarMaterialProperties.MinResidualEnergy)
                break;

            var remainingRange = effectiveMaxRange - currentStartDist;
            if (remainingRange <= LidarMaterialProperties.VegetationAdvanceMetres)
                break;

            var startPoint = originMap + dirMap * currentStartDist;
            var hit = rayCaster(startPoint, dirMap, remainingRange);
            if (hit is null || !double.IsFinite(hit.DistanceMetres) || hit.DistanceMetres < 0)
                break;

            var totalDist = currentStartDist + hit.DistanceMetres;
            if (totalDist > effectiveMaxRange)
                break;

            var mat = LidarMaterialProperties.Get(hit.Material);
            var isDuplicateTarget = !seenTargets.Add(hit.TargetId);
            if (!isDuplicateTarget)
            {
                var cosIncidence = Math.Abs(Vector3d.Dot(dirMap.Normalized(), hit.NormalMap.Normalized()));
                var prob = LidarMaterialProperties.ComputeReturnProbability(hit.Material, cosIncidence, residualEnergy);
                if (zeroNoise || _rng.NextUniformOpenUnit() <= prob)
                {
                    var measuredDist = zeroNoise
                        ? totalDist
                        : Math.Clamp(totalDist + _rng.NextGaussian() * _profile.RangeSigmaMetres, 0.05, effectiveMaxRange);
                    accepted.Add((measuredDist, Math.Round(prob, 4)));
                }
            }

            residualEnergy *= mat.TransmissionFactor;
            if (mat.TransmissionFactor <= 0 || residualEnergy < LidarMaterialProperties.MinResidualEnergy)
                break;

            // Advance 0.01 m past the intersected surface to trace behind foliage/glass without duplicating surface.
            currentStartDist = totalDist + LidarMaterialProperties.VegetationAdvanceMetres;
        }

        return accepted;
    }

    public static Func<Vector3d, Vector3d, double, LidarRayHit?> CreateAnalyticBoxRayCaster(
        IEnumerable<WorldBoxObject> objects) =>
        CreateAnalyticBoxRayCaster(objects.Select(o => new LidarAnalyticBox(o.Id, o.Material, o.PositionEnu, o.SizeEnu)));

    public static Func<Vector3d, Vector3d, double, LidarRayHit?> CreateAnalyticBoxRayCaster(
        IEnumerable<LidarAnalyticBox> objects)
    {
        var boxes = objects.ToArray();
        return (origin, dir, maxDist) =>
        {
            var invX = Math.Abs(dir.X) > 1e-12 ? 1.0 / dir.X : double.PositiveInfinity;
            var invY = Math.Abs(dir.Y) > 1e-12 ? 1.0 / dir.Y : double.PositiveInfinity;
            var invZ = Math.Abs(dir.Z) > 1e-12 ? 1.0 / dir.Z : double.PositiveInfinity;

            LidarRayHit? best = null;
            var bestDist = maxDist;

            foreach (var box in boxes)
            {
                var hx = box.SizeEnu.X * 0.5;
                var hy = box.SizeEnu.Y * 0.5;
                var hz = box.SizeEnu.Z * 0.5;
                var min = new Vector3d(box.CenterEnu.X - hx, box.CenterEnu.Y - hy, box.CenterEnu.Z - hz);
                var max = new Vector3d(box.CenterEnu.X + hx, box.CenterEnu.Y + hy, box.CenterEnu.Z + hz);

                if (!IntersectSlab(origin.X, dir.X, invX, min.X, max.X, ref bestDist, out var tMinX, out var tMaxX, out var nSignX))
                    continue;
                if (!IntersectSlab(origin.Y, dir.Y, invY, min.Y, max.Y, ref bestDist, out var tMinY, out var tMaxY, out var nSignY))
                    continue;
                if (!IntersectSlab(origin.Z, dir.Z, invZ, min.Z, max.Z, ref bestDist, out var tMinZ, out var tMaxZ, out var nSignZ))
                    continue;

                var tEnter = Math.Max(tMinX, Math.Max(tMinY, tMinZ));
                var tExit = Math.Min(tMaxX, Math.Min(tMaxY, tMaxZ));
                if (tEnter > tExit || tExit < 0 || tEnter > bestDist)
                    continue;

                var tHit = tEnter >= 0 ? tEnter : tExit;
                if (tHit < 1e-6 || tHit > bestDist)
                    continue;

                Vector3d normal;
                if (tMinX >= tMinY && tMinX >= tMinZ)
                    normal = new Vector3d(nSignX, 0, 0);
                else if (tMinY >= tMinZ)
                    normal = new Vector3d(0, nSignY, 0);
                else
                    normal = new Vector3d(0, 0, nSignZ);

                bestDist = tHit;
                best = new LidarRayHit(tHit, normal, box.Material, box.Id);
            }

            return best;
        };
    }

    private static bool IntersectSlab(
        double originCoord, double dirCoord, double invDir,
        double minBound, double maxBound, ref double _,
        out double tMin, out double tMax, out double enterNormalSign)
    {
        if (double.IsInfinity(invDir))
        {
            if (originCoord < minBound || originCoord > maxBound)
            {
                tMin = tMax = enterNormalSign = 0;
                return false;
            }
            tMin = double.NegativeInfinity;
            tMax = double.PositiveInfinity;
            enterNormalSign = 0;
            return true;
        }

        var t1 = (minBound - originCoord) * invDir;
        var t2 = (maxBound - originCoord) * invDir;
        if (t1 <= t2)
        {
            tMin = t1;
            tMax = t2;
            enterNormalSign = -1;
        }
        else
        {
            tMin = t2;
            tMax = t1;
            enterNormalSign = 1;
        }
        return true;
    }
}
