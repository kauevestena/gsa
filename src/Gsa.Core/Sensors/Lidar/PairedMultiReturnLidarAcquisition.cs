using Gsa.Core.Geometry;
using Gsa.Core.Randomness;
using Gsa.Core.Sensors.Calibration;
using Gsa.Core.Sensors.Camera;
using Gsa.Core.World;

namespace Gsa.Core.Sensors.Lidar;

public sealed record PairedLidarUnitSpec(
    string SensorUnitId,
    string SlotId,
    LidarMountEstimate PhysicalTruthMount,
    LidarMountEstimate EstimatedCalibrationMount);

public sealed record PairedLidarReturnSample(
    string ReturnId,
    string ScanId,
    string SensorUnitId,
    string SlotId,
    int PulseIndex,
    int ReturnNumber,
    int TotalReturnsInPulse,
    double TimestampSeconds,
    double AzimuthRadians,
    double ElevationRadians,
    double MeasuredRangeMetres,
    double Intensity,
    double IncidenceCosine,
    bool IsSpuriousRain,
    Vector3d LocalPointSensorMetres,
    Vector3d GeoreferencedPointEnu)
{
    public GeoreferencedLidarSample ToGeoreferencedLidarSample(long sampleId) =>
        new(
            SampleId: sampleId,
            PhysicalTimestampSeconds: TimestampSeconds,
            MapPositionEnu: GeoreferencedPointEnu,
            Intensity: (float)Intensity,
            ReturnNumber: ReturnNumber,
            IsSpuriousRain: IsSpuriousRain);
}

public sealed record PairedLidarScanResult(
    string ScanId,
    double TimestampSeconds,
    string ProfileId,
    int SensorUnitCount,
    int TotalPrimaryRaysCast,
    int TotalReturnsEmitted,
    int MultiReturnPulseCount,
    int SpuriousRainReturnCount,
    IReadOnlyList<PairedLidarReturnSample> Returns);

public sealed record PairedReplayMetrics(
    string RouteId,
    bool IsRaining,
    double SpeedMetresPerSecond,
    int ScanCount,
    int PrimaryRayCount,
    int TotalReturnCount,
    int MultiReturnPulseCount,
    int FirstReturnCount,
    int SecondPlusReturnCount,
    int SpuriousRainReturnCount,
    double MeanReturnsPerPulse,
    IReadOnlyDictionary<string, int> ReturnsBySlot,
    IReadOnlyList<PairedLidarReturnSample> AllReturns);

/// <summary>Simulates two rack-mounted L5 LiDAR units with independent slot mounts, multi-return foliage penetration, rain attenuation, and incidence physics (T-F15-04).</summary>
public sealed class PairedMultiReturnLidarSimulator
{
    private readonly LidarProfile _profile;
    private readonly GaussianRandom _rng;
    private long _scanSequence;

    public LidarProfile Profile => _profile;

    public PairedMultiReturnLidarSimulator(LidarProfile? profile = null, ulong seed = 20261116UL)
    {
        _profile = profile ?? LidarProfile.L5;
        if (_profile.SensorUnits < 2)
            throw new ArgumentException("Paired LiDAR simulator requires at least two sensor units.", nameof(profile));
        _rng = new GaussianRandom(seed);
    }

    public static IReadOnlyList<(double AzimuthRadians, double ElevationRadians, Vector3d DirectionLidar)> BuildUnitScanPattern(LidarProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var channels = profile.ChannelsPerSensor;
        var azimuths = profile.AzimuthsPerScan;
        const double minElev = -15.0 * Math.PI / 180.0;
        const double maxElev = 15.0 * Math.PI / 180.0;

        var rays = new List<(double, double, Vector3d)>(channels * azimuths);
        for (var ch = 0; ch < channels; ch++)
        {
            var frac = channels == 1 ? 0.5 : (double)ch / (channels - 1);
            var elevation = minElev + frac * (maxElev - minElev);
            for (var az = 0; az < azimuths; az++)
            {
                var azimuth = 2.0 * Math.PI * az / azimuths;
                rays.Add((azimuth, elevation, LidarProfile.DirectionFromAngles(azimuth, elevation)));
            }
        }
        return Array.AsReadOnly(rays.ToArray());
    }

    public static IReadOnlyList<PairedLidarUnitSpec> ResolvePairedUnitsFromRack(
        ModularSensorRack rack,
        double timestampSeconds,
        IReadOnlyList<string>? slotIds = null)
    {
        ArgumentNullException.ThrowIfNull(rack);
        var slots = slotIds ?? ["slot_lidar_a", "slot_lidar_b"];
        var units = new List<PairedLidarUnitSpec>(slots.Count);

        foreach (var slotId in slots)
        {
            var record = rack.GetMountAtTimestamp(slotId, timestampSeconds);
            var physical = new LidarMountEstimate(
                record.PhysicalTruthMount.LeverArmBody,
                record.PhysicalTruthMount.SensorToBody,
                record.PhysicalTruthMount.TimeOffsetSeconds);
            var estimated = new LidarMountEstimate(
                record.EstimatedCalibrationMount.LeverArmBody,
                record.EstimatedCalibrationMount.SensorToBody,
                record.EstimatedCalibrationMount.TimeOffsetSeconds);
            units.Add(new PairedLidarUnitSpec(record.SensorId, slotId, physical, estimated));
        }

        return Array.AsReadOnly(units.ToArray());
    }

    public PairedLidarScanResult SimulatePairedScan(
        double timestampSeconds,
        RigidTransform trueBodyToMap,
        EstimatedBodyTrajectory trajectory,
        ModularSensorRack rack,
        bool isRaining,
        Func<Vector3d, Vector3d, double, LidarRayHit?> rayCaster,
        bool zeroNoise = false,
        IReadOnlyList<string>? slotIds = null)
    {
        ArgumentNullException.ThrowIfNull(trajectory);
        ArgumentNullException.ThrowIfNull(rack);
        ArgumentNullException.ThrowIfNull(rayCaster);

        var units = ResolvePairedUnitsFromRack(rack, timestampSeconds, slotIds);
        return SimulatePairedScanWithUnits(timestampSeconds, trueBodyToMap, trajectory, units, isRaining, rayCaster, zeroNoise);
    }

    public PairedLidarScanResult SimulatePairedScanWithUnits(
        double timestampSeconds,
        RigidTransform trueBodyToMap,
        EstimatedBodyTrajectory trajectory,
        IReadOnlyList<PairedLidarUnitSpec> units,
        bool isRaining,
        Func<Vector3d, Vector3d, double, LidarRayHit?> rayCaster,
        bool zeroNoise = false)
    {
        ArgumentNullException.ThrowIfNull(trajectory);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(rayCaster);

        _scanSequence++;
        var scanId = $"paired_scan_{_scanSequence:000000}";
        var pattern = BuildUnitScanPattern(_profile);
        var effectiveRange = LidarMaterialProperties.GetEffectiveRangeMetres(_profile.MaxRangeMetres, isRaining);

        var samples = new List<PairedLidarReturnSample>(units.Count * pattern.Count);
        var totalPrimaryRays = 0;
        var multiReturnPulses = 0;
        var spuriousCount = 0;
        var pulseIndex = 0;

        foreach (var unit in units)
        {
            var sampleTime = timestampSeconds + unit.EstimatedCalibrationMount.TimeOffsetSeconds;
            if (!trajectory.TrySample(sampleTime, out var estPose) || estPose is null)
                continue;

            var estBodyToMap = new RigidTransform(estPose.BodyToMap, estPose.PositionEnu);
            var trueSensorToMap = LidarBenchSimulator.ComposeLidarToMap(trueBodyToMap, unit.PhysicalTruthMount);
            var estSensorToMap = LidarBenchSimulator.ComposeLidarToMap(estBodyToMap, unit.EstimatedCalibrationMount);

            foreach (var (az, el, dirSensor) in pattern)
            {
                totalPrimaryRays++;
                pulseIndex++;
                var dirMap = trueSensorToMap.Rotation.Rotate(dirSensor).Normalized();

                if (isRaining && !zeroNoise && _rng.NextUniformOpenUnit() < LidarMaterialProperties.RainSpuriousRate)
                {
                    var spuriousRange = Math.Max(0.5, _rng.NextUniformOpenUnit() * effectiveRange * 0.90);
                    var localPt = dirSensor * spuriousRange;
                    samples.Add(new PairedLidarReturnSample(
                        ReturnId: $"{scanId}:{unit.SensorUnitId}:p{pulseIndex}:r1",
                        ScanId: scanId,
                        SensorUnitId: unit.SensorUnitId,
                        SlotId: unit.SlotId,
                        PulseIndex: pulseIndex,
                        ReturnNumber: 1,
                        TotalReturnsInPulse: 1,
                        TimestampSeconds: timestampSeconds,
                        AzimuthRadians: az,
                        ElevationRadians: el,
                        MeasuredRangeMetres: spuriousRange,
                        Intensity: 0.15,
                        IncidenceCosine: 1.0,
                        IsSpuriousRain: true,
                        LocalPointSensorMetres: localPt,
                        GeoreferencedPointEnu: estSensorToMap.Transform(localPt)));
                    spuriousCount++;
                    continue;
                }

                var pulseHits = TraceMultiReturnPulse(trueSensorToMap.Translation, dirMap, effectiveRange, rayCaster, zeroNoise);
                if (pulseHits.Count > 1)
                    multiReturnPulses++;

                for (var idx = 0; idx < pulseHits.Count; idx++)
                {
                    var (range, intensity, cosIncidence) = pulseHits[idx];
                    var localPt = dirSensor * range;
                    var returnNumber = idx + 1;
                    samples.Add(new PairedLidarReturnSample(
                        ReturnId: $"{scanId}:{unit.SensorUnitId}:p{pulseIndex}:r{returnNumber}",
                        ScanId: scanId,
                        SensorUnitId: unit.SensorUnitId,
                        SlotId: unit.SlotId,
                        PulseIndex: pulseIndex,
                        ReturnNumber: returnNumber,
                        TotalReturnsInPulse: pulseHits.Count,
                        TimestampSeconds: timestampSeconds,
                        AzimuthRadians: az,
                        ElevationRadians: el,
                        MeasuredRangeMetres: range,
                        Intensity: intensity,
                        IncidenceCosine: cosIncidence,
                        IsSpuriousRain: false,
                        LocalPointSensorMetres: localPt,
                        GeoreferencedPointEnu: estSensorToMap.Transform(localPt)));
                }
            }
        }

        return new PairedLidarScanResult(
            ScanId: scanId,
            TimestampSeconds: timestampSeconds,
            ProfileId: _profile.Id,
            SensorUnitCount: units.Count,
            TotalPrimaryRaysCast: totalPrimaryRays,
            TotalReturnsEmitted: samples.Count,
            MultiReturnPulseCount: multiReturnPulses,
            SpuriousRainReturnCount: spuriousCount,
            Returns: Array.AsReadOnly(samples.ToArray()));
    }

    private List<(double RangeMetres, double Intensity, double CosIncidence)> TraceMultiReturnPulse(
        Vector3d originMap,
        Vector3d dirMap,
        double effectiveMaxRange,
        Func<Vector3d, Vector3d, double, LidarRayHit?> rayCaster,
        bool zeroNoise)
    {
        var accepted = new List<(double, double, double)>(LidarMaterialProperties.MaxReturnsPerPulse);
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

            var hit = rayCaster(originMap + dirMap * currentStartDist, dirMap, remainingRange);
            if (hit is null || !double.IsFinite(hit.DistanceMetres) || hit.DistanceMetres < 0)
                break;

            var totalDist = currentStartDist + hit.DistanceMetres;
            if (totalDist > effectiveMaxRange)
                break;

            var mat = LidarMaterialProperties.Get(hit.Material);
            var isDuplicate = !seenTargets.Add(hit.TargetId);
            if (!isDuplicate)
            {
                var cosIncidence = Math.Abs(Vector3d.Dot(dirMap.Normalized(), hit.NormalMap.Normalized()));
                var prob = LidarMaterialProperties.ComputeReturnProbability(hit.Material, cosIncidence, residualEnergy);
                if (zeroNoise || _rng.NextUniformOpenUnit() <= prob)
                {
                    var measuredDist = zeroNoise
                        ? totalDist
                        : Math.Clamp(totalDist + _rng.NextGaussian() * _profile.RangeSigmaMetres, 0.05, effectiveMaxRange);
                    accepted.Add((measuredDist, Math.Round(prob, 4), Math.Round(cosIncidence, 4)));
                }
            }

            residualEnergy *= mat.TransmissionFactor;
            if (mat.TransmissionFactor <= 0 || residualEnergy < LidarMaterialProperties.MinResidualEnergy)
                break;

            currentStartDist = totalDist + LidarMaterialProperties.VegetationAdvanceMetres;
        }

        return accepted;
    }

    public PairedReplayMetrics RunSerraVegetationReplay(
        SerraPickupRouteSpec route,
        WorldSceneManifest serraManifest,
        ModularSensorRack pickupRack,
        double speedMetresPerSecond,
        bool isRaining,
        PointCloudOctreeAndChunkStore? octreeStore = null,
        bool zeroNoise = false)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(serraManifest);
        ArgumentNullException.ThrowIfNull(pickupRack);
        if (!double.IsFinite(speedMetresPerSecond) || speedMetresPerSecond <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(speedMetresPerSecond));

        var rayCaster = LidarBenchSimulator.CreateAnalyticBoxRayCaster(serraManifest.Objects);
        var scanInterval = 1.0 / _profile.ScanRateHz;
        var bodyPoses = new List<EstimatedBodyPose>();
        var scanTransforms = new List<(double Time, RigidTransform BodyToMap)>();

        var t = 0.0;
        for (var i = 0; i < route.WaypointsEnu.Count - 1; i++)
        {
            var a = route.WaypointsEnu[i];
            var b = route.WaypointsEnu[i + 1];
            var delta = b - a;
            var dist = delta.Length;
            var segDuration = dist / speedMetresPerSecond;
            var yaw = Math.Atan2(delta.Y, delta.X);
            var rot = Quaterniond.FromAxisAngle(Vector3d.UnitZ, yaw);

            var steps = Math.Max(1, (int)Math.Ceiling(segDuration / scanInterval));
            for (var s = 0; s < steps; s++)
            {
                var frac = (double)s / steps;
                var pos = a + delta * frac;
                var poseTime = t + frac * segDuration;
                bodyPoses.Add(new EstimatedBodyPose(poseTime, pos, rot));
                scanTransforms.Add((poseTime, new RigidTransform(rot, pos)));
            }
            t += segDuration;
        }
        var endPos = route.WaypointsEnu[^1];
        var endRot = bodyPoses[^1].BodyToMap;
        bodyPoses.Add(new EstimatedBodyPose(t + 1.0, endPos, endRot));

        var trajectory = new EstimatedBodyTrajectory(bodyPoses);
        var allReturns = new List<PairedLidarReturnSample>();
        var returnsBySlot = new Dictionary<string, int>(StringComparer.Ordinal);
        var primaryRays = 0;
        var multiReturnPulses = 0;
        var spuriousCount = 0;

        foreach (var (scanTime, bodyToMap) in scanTransforms)
        {
            var scan = SimulatePairedScan(scanTime, bodyToMap, trajectory, pickupRack, isRaining, rayCaster, zeroNoise);
            primaryRays += scan.TotalPrimaryRaysCast;
            multiReturnPulses += scan.MultiReturnPulseCount;
            spuriousCount += scan.SpuriousRainReturnCount;
            foreach (var r in scan.Returns)
            {
                allReturns.Add(r);
                returnsBySlot[r.SlotId] = returnsBySlot.GetValueOrDefault(r.SlotId) + 1;
            }
        }

        if (octreeStore is not null && allReturns.Count > 0)
        {
            long sampleSeq = 1;
            octreeStore.IngestRevision(allReturns.Select(r => r.ToGeoreferencedLidarSample(sampleSeq++)).ToArray());
        }

        var firstReturns = allReturns.Count(r => !r.IsSpuriousRain && r.ReturnNumber == 1);
        var secondPlusReturns = allReturns.Count(r => !r.IsSpuriousRain && r.ReturnNumber >= 2);
        var meanReturns = primaryRays > 0 ? (double)allReturns.Count / primaryRays : 0.0;

        return new PairedReplayMetrics(
            RouteId: route.RouteId,
            IsRaining: isRaining,
            SpeedMetresPerSecond: speedMetresPerSecond,
            ScanCount: scanTransforms.Count,
            PrimaryRayCount: primaryRays,
            TotalReturnCount: allReturns.Count,
            MultiReturnPulseCount: multiReturnPulses,
            FirstReturnCount: firstReturns,
            SecondPlusReturnCount: secondPlusReturns,
            SpuriousRainReturnCount: spuriousCount,
            MeanReturnsPerPulse: Math.Round(meanReturns, 4),
            ReturnsBySlot: returnsBySlot,
            AllReturns: Array.AsReadOnly(allReturns.ToArray()));
    }
}
