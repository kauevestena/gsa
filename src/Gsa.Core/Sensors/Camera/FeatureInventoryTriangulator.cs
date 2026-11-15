using System.Text.Json;
using Gsa.Core.Geometry;
using Gsa.Core.Mobility;

namespace Gsa.Core.Sensors.Camera;

public enum FeatureConfirmationMode
{
    Arcade = 0,
    Technical = 1,
}

public enum TriangulationStatus
{
    Resolved = 0,
    InsufficientGeometry = 1,
    Unconfirmed = 2,
}

public enum FeatureReconstructionMethod
{
    None = 0,
    TwoRayTriangulation = 1,
    KnownSupportPlane = 2,
}

public enum GeometryFailureReason
{
    None = 0,
    QualityRejected = 1,
    SingleFrameWithoutSupportPlane = 2,
    LowParallaxAngle = 3,
    BehindCamera = 4,
    LowPlaneGrazingAngle = 5,
    UnconfirmedClassOrFeature = 6,
}

/// <summary>Known local support plane (ground, road surface, or mapped facade plane) in ENU coordinates.</summary>
public sealed record SupportPlane
{
    public Vector3d PointEnu { get; }
    public Vector3d UnitNormal { get; }

    public SupportPlane(Vector3d pointEnu, Vector3d normal)
    {
        if (!double.IsFinite(pointEnu.X) || !double.IsFinite(pointEnu.Y) || !double.IsFinite(pointEnu.Z))
            throw new ArgumentOutOfRangeException(nameof(pointEnu), "Support plane origin must be finite.");
        UnitNormal = normal.Normalized();
        PointEnu = pointEnu;
    }

    public static SupportPlane HorizontalGround(double elevationEnuZ = 100.0) =>
        new(new Vector3d(0.0, 0.0, elevationEnuZ), Vector3d.UnitZ);
}

public sealed record CameraRayEnu(
    double TimestampSeconds,
    string SensorId,
    string SampleId,
    Vector3d OriginEnu,
    Vector3d DirectionEnu);

public sealed record TriangulatedFeatureEntry(
    string TargetId,
    string ObjectClass,
    TriangulationStatus Status,
    FeatureReconstructionMethod Method,
    GeometryFailureReason FailureReason,
    Vector3d? EstimatedPositionEnu,
    double MaxParallaxDegrees,
    double? PlaneGrazingDegrees,
    int ContributingObservationCount,
    bool Confirmed,
    IReadOnlyList<string> ContributingSampleIds);

public sealed class FeatureInventoryResult
{
    public FeatureConfirmationMode ConfirmationMode { get; }
    public IReadOnlyList<TriangulatedFeatureEntry> Entries { get; }
    public IReadOnlyList<TriangulatedFeatureEntry> ResolvedConfirmedFeatures { get; }

    public FeatureInventoryResult(
        FeatureConfirmationMode confirmationMode,
        IEnumerable<TriangulatedFeatureEntry> entries)
    {
        ConfirmationMode = confirmationMode;
        Entries = Array.AsReadOnly(entries.ToArray());
        ResolvedConfirmedFeatures = Array.AsReadOnly(
            Entries.Where(e => e.Status == TriangulationStatus.Resolved && e.Confirmed && e.EstimatedPositionEnu.HasValue)
                   .ToArray());
    }
}

public sealed record ScheduledCaptureSlot(
    string SensorId,
    long Sequence,
    double ScheduledTimeSeconds,
    CameraSensorProfile Profile,
    CameraMountEstimate MountEstimate);

/// <summary>Immutable multi-sensor observation batch bounded by resource limits without truth or full textures.</summary>
public sealed class ObservationBatch
{
    public int SchemaVersion => 1;
    public string BatchId { get; }
    public double StartTimeSeconds { get; }
    public double EndTimeSeconds { get; }
    public int StorageUnitsUsed { get; }
    public int SaturatedDroppedCount { get; }
    public CaptureStopReason StopReason { get; }
    public IReadOnlyList<string> SensorIds { get; }
    public IReadOnlyList<CameraObservationSample> Samples { get; }

    public ObservationBatch(
        string batchId,
        IEnumerable<string> sensorIds,
        IEnumerable<CameraObservationSample> samples,
        int storageUnitsUsed,
        int saturatedDroppedCount,
        CaptureStopReason stopReason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(batchId);
        ArgumentNullException.ThrowIfNull(sensorIds);
        ArgumentNullException.ThrowIfNull(samples);
        if (storageUnitsUsed < 0 || saturatedDroppedCount < 0)
            throw new ArgumentOutOfRangeException(nameof(storageUnitsUsed), "Batch counters cannot be negative.");

        var orderedSamples = samples.ToArray();
        BatchId = batchId;
        SensorIds = Array.AsReadOnly(sensorIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
        Samples = Array.AsReadOnly(orderedSamples);
        StartTimeSeconds = orderedSamples.Length > 0 ? orderedSamples[0].TimestampSeconds : 0.0;
        EndTimeSeconds = orderedSamples.Length > 0 ? orderedSamples[^1].TimestampSeconds : 0.0;
        StorageUnitsUsed = storageUnitsUsed;
        SaturatedDroppedCount = saturatedDroppedCount;
        StopReason = stopReason;
    }

    public string ToJson()
    {
        var payload = new
        {
            schema_version = SchemaVersion,
            batch_id = BatchId,
            sensor_ids = SensorIds,
            start_time_s = StartTimeSeconds,
            end_time_s = EndTimeSeconds,
            storage_units_used = StorageUnitsUsed,
            saturated_dropped_count = SaturatedDroppedCount,
            stop_reason = StopReason.ToString(),
            full_texture_persisted = false,
            sample_ids = Samples.Select(s => s.SampleId),
        };
        return JsonSerializer.Serialize(payload);
    }
}

/// <summary>Coordinates multiple camera sensors with deterministic timing and virtual storage budget.</summary>
public sealed class SensorCaptureAgenda
{
    private sealed class SensorEntry
    {
        public required string SensorId { get; init; }
        public required CameraSensorProfile Profile { get; init; }
        public required CameraMountEstimate MountEstimate { get; init; }
        public double NextTriggerTimeSeconds { get; set; }
        public long NextSequence { get; set; }
    }

    private readonly Dictionary<string, SensorEntry> _sensors = new(StringComparer.Ordinal);
    private readonly List<CameraObservationSample> _bufferedSamples = [];

    public int VirtualStorageCapacityUnits { get; }
    public int StorageUnitsUsed { get; private set; }
    public int SaturatedDroppedCount { get; private set; }
    public CaptureStopReason StopReason { get; private set; } = CaptureStopReason.None;
    public int BufferedSampleCount => _bufferedSamples.Count;

    public SensorCaptureAgenda(int virtualStorageCapacityUnits)
    {
        if (virtualStorageCapacityUnits <= 0)
            throw new ArgumentOutOfRangeException(nameof(virtualStorageCapacityUnits), "Virtual storage capacity must be positive.");
        VirtualStorageCapacityUnits = virtualStorageCapacityUnits;
    }

    public void RegisterSensor(
        string sensorId,
        CameraSensorProfile profile,
        CameraMountEstimate? mountEstimate = null,
        double initialTimeSeconds = 0.0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sensorId);
        ArgumentNullException.ThrowIfNull(profile);
        if (!double.IsFinite(initialTimeSeconds) || initialTimeSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(initialTimeSeconds), "Initial trigger time must be finite and non-negative.");
        if (_sensors.ContainsKey(sensorId))
            throw new InvalidOperationException($"Sensor '{sensorId}' is already registered.");

        _sensors.Add(sensorId, new SensorEntry
        {
            SensorId = sensorId,
            Profile = profile,
            MountEstimate = mountEstimate ?? CameraMountEstimate.ForwardFacing,
            NextTriggerTimeSeconds = initialTimeSeconds,
            NextSequence = 0,
        });
    }

    public IReadOnlyList<ScheduledCaptureSlot> PollDueCaptures(double currentTimeSeconds)
    {
        if (!double.IsFinite(currentTimeSeconds) || currentTimeSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(currentTimeSeconds), "Current time must be finite and non-negative.");

        var due = new List<ScheduledCaptureSlot>();
        foreach (var entry in _sensors.Values.OrderBy(e => e.NextTriggerTimeSeconds).ThenBy(e => e.SensorId, StringComparer.Ordinal))
        {
            if (currentTimeSeconds + 1e-12 < entry.NextTriggerTimeSeconds)
                continue;

            if (StorageUnitsUsed + entry.Profile.StorageUnitsPerSample > VirtualStorageCapacityUnits)
            {
                StopReason = CaptureStopReason.VirtualStorageFull;
                SaturatedDroppedCount++;
                entry.NextTriggerTimeSeconds = currentTimeSeconds + (1.0 / entry.Profile.RateHz);
                continue;
            }

            var scheduledTime = entry.NextTriggerTimeSeconds;
            var seq = entry.NextSequence++;
            var period = 1.0 / entry.Profile.RateHz;
            while (entry.NextTriggerTimeSeconds <= currentTimeSeconds + 1e-12)
                entry.NextTriggerTimeSeconds += period;

            due.Add(new ScheduledCaptureSlot(entry.SensorId, seq, scheduledTime, entry.Profile, entry.MountEstimate));
        }
        return due;
    }

    public bool TryRecordSample(CameraObservationSample sample, out CaptureStopReason stopReason)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (!_sensors.TryGetValue(sample.SensorId, out var entry))
            throw new KeyNotFoundException($"Unknown sensor '{sample.SensorId}'.");

        if (StorageUnitsUsed + entry.Profile.StorageUnitsPerSample > VirtualStorageCapacityUnits)
        {
            StopReason = CaptureStopReason.VirtualStorageFull;
            SaturatedDroppedCount++;
            stopReason = StopReason;
            return false;
        }

        StorageUnitsUsed += entry.Profile.StorageUnitsPerSample;
        _bufferedSamples.Add(sample);
        stopReason = CaptureStopReason.None;
        return true;
    }

    public ObservationBatch FlushBatch(string batchId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(batchId);
        var ordered = _bufferedSamples
            .OrderBy(s => s.TimestampSeconds)
            .ThenBy(s => s.SensorId, StringComparer.Ordinal)
            .ThenBy(s => s.Sequence)
            .ToArray();

        var batch = new ObservationBatch(
            batchId,
            _sensors.Keys,
            ordered,
            StorageUnitsUsed,
            SaturatedDroppedCount,
            StopReason);

        _bufferedSamples.Clear();
        StorageUnitsUsed = 0;
        SaturatedDroppedCount = 0;
        StopReason = CaptureStopReason.None;
        return batch;
    }
}

/// <summary>Triangulates confirmed features into ENU without ever touching world truth or depth.</summary>
public static class FeatureInventoryTriangulator
{
    public const double MinimumParallaxDegrees = 5.0;
    public const double MinimumPlaneGrazingDegrees = 5.0;
    public const double DefaultMaxRangeMetres = 250.0;

    public static CameraRayEnu PixelToMapRay(
        CameraObservationSample sample,
        double pixelU,
        double pixelV)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (!double.IsFinite(pixelU) || !double.IsFinite(pixelV))
            throw new ArgumentOutOfRangeException(nameof(pixelU), "Pixel coordinates must be finite.");

        var intrinsics = sample.Intrinsics;
        var xd = (pixelU - intrinsics.Cx) / intrinsics.Fx;
        var yd = (pixelV - intrinsics.Cy) / intrinsics.Fy;

        var (x, y) = UndistortNormalized(xd, yd, intrinsics.K1, intrinsics.K2);
        var rayCam = new Vector3d(x, y, 1.0).Normalized();
        var cameraToMap = CameraGeometry.CameraToMap(sample.EstimatedPose, sample.MountEstimate);
        var rayMap = cameraToMap.Rotation.Rotate(rayCam).Normalized();

        return new CameraRayEnu(sample.TimestampSeconds, sample.SensorId, sample.SampleId, cameraToMap.Translation, rayMap);
    }

    public static FeatureInventoryResult BuildInventory(
        IEnumerable<CameraObservationSample> samples,
        FeatureConfirmationMode mode,
        IReadOnlySet<string>? confirmedClassesOrTargets = null,
        IReadOnlyDictionary<string, SupportPlane>? supportPlanesByTargetOrClass = null,
        SupportPlane? defaultSupportPlane = null,
        double maxRangeMetres = DefaultMaxRangeMetres)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (!double.IsFinite(maxRangeMetres) || maxRangeMetres <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxRangeMetres), "Max triangulation range must be positive.");

        var sampleList = samples.ToArray();
        var observationsByTarget = new Dictionary<string, List<(CameraObservationSample Sample, ObservedFeature2D Feature, CameraRayEnu Ray)>>(StringComparer.Ordinal);
        var targetClasses = new Dictionary<string, string>(StringComparer.Ordinal);
        var rejectedOnlyTargets = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var sample in sampleList)
        {
            if (sample is null)
                throw new ArgumentException("Samples cannot contain null entries.", nameof(samples));

            foreach (var feature in sample.ObservedFeatures)
            {
                if (!targetClasses.ContainsKey(feature.TargetId))
                    targetClasses[feature.TargetId] = feature.ObjectClass;

                if (!sample.Quality.Passed)
                {
                    rejectedOnlyTargets[feature.TargetId] = feature.ObjectClass;
                    continue;
                }

                var ray = PixelToMapRay(sample, feature.CenterU, feature.CenterV);
                if (!observationsByTarget.TryGetValue(feature.TargetId, out var list))
                {
                    list = [];
                    observationsByTarget[feature.TargetId] = list;
                }
                list.Add((sample, feature, ray));
            }
        }

        var entries = new List<TriangulatedFeatureEntry>();
        foreach (var targetId in targetClasses.Keys.Order(StringComparer.Ordinal))
        {
            var objectClass = targetClasses[targetId];
            var confirmed = IsConfirmed(mode, targetId, objectClass, confirmedClassesOrTargets);

            if (!observationsByTarget.TryGetValue(targetId, out var group) || group.Count == 0)
            {
                entries.Add(new TriangulatedFeatureEntry(
                    targetId,
                    objectClass,
                    confirmed ? TriangulationStatus.InsufficientGeometry : TriangulationStatus.Unconfirmed,
                    FeatureReconstructionMethod.None,
                    confirmed ? GeometryFailureReason.QualityRejected : GeometryFailureReason.UnconfirmedClassOrFeature,
                    null,
                    0.0,
                    null,
                    0,
                    confirmed,
                    []));
                continue;
            }

            var rays = group.Select(g => g.Ray).ToArray();
            var sampleIds = group.Select(g => g.Sample.SampleId).Distinct(StringComparer.Ordinal).ToArray();
            var supportPlane = ResolveSupportPlane(targetId, objectClass, supportPlanesByTargetOrClass, defaultSupportPlane);

            var solved = TrySolveGeometry(rays, supportPlane, maxRangeMetres, out var position, out var method,
                out var maxParallax, out var grazingDeg, out var failureReason);

            if (!confirmed)
            {
                entries.Add(new TriangulatedFeatureEntry(
                    targetId,
                    objectClass,
                    TriangulationStatus.Unconfirmed,
                    FeatureReconstructionMethod.None,
                    GeometryFailureReason.UnconfirmedClassOrFeature,
                    null,
                    maxParallax,
                    grazingDeg,
                    rays.Length,
                    false,
                    sampleIds));
                continue;
            }

            entries.Add(new TriangulatedFeatureEntry(
                targetId,
                objectClass,
                solved ? TriangulationStatus.Resolved : TriangulationStatus.InsufficientGeometry,
                solved ? method : FeatureReconstructionMethod.None,
                solved ? GeometryFailureReason.None : failureReason,
                solved ? position : null,
                maxParallax,
                grazingDeg,
                rays.Length,
                true,
                sampleIds));
        }

        return new FeatureInventoryResult(mode, entries);
    }

    private static bool IsConfirmed(
        FeatureConfirmationMode mode,
        string targetId,
        string objectClass,
        IReadOnlySet<string>? confirmedClassesOrTargets)
    {
        if (mode == FeatureConfirmationMode.Arcade)
            return confirmedClassesOrTargets is null || confirmedClassesOrTargets.Count == 0 ||
                   confirmedClassesOrTargets.Contains(objectClass) || confirmedClassesOrTargets.Contains(targetId);

        return confirmedClassesOrTargets is not null &&
               (confirmedClassesOrTargets.Contains(targetId) || confirmedClassesOrTargets.Contains(objectClass));
    }

    private static SupportPlane? ResolveSupportPlane(
        string targetId,
        string objectClass,
        IReadOnlyDictionary<string, SupportPlane>? byKey,
        SupportPlane? fallback)
    {
        if (byKey is not null)
        {
            if (byKey.TryGetValue(targetId, out var specific))
                return specific;
            if (byKey.TryGetValue(objectClass, out var byClass))
                return byClass;
        }
        return fallback;
    }

    private static bool TrySolveGeometry(
        CameraRayEnu[] rays,
        SupportPlane? supportPlane,
        double maxRangeMetres,
        out Vector3d position,
        out FeatureReconstructionMethod method,
        out double maxParallaxDegrees,
        out double? planeGrazingDegrees,
        out GeometryFailureReason failureReason)
    {
        position = default;
        method = FeatureReconstructionMethod.None;
        maxParallaxDegrees = 0.0;
        planeGrazingDegrees = null;
        failureReason = GeometryFailureReason.SingleFrameWithoutSupportPlane;

        var bestParallaxForForwardPair = 0.0;
        Vector3d? bestTriangulated = null;
        var sawBackwardPair = false;

        for (var i = 0; i < rays.Length; i++)
        {
            for (var j = i + 1; j < rays.Length; j++)
            {
                var dot = Math.Clamp(Vector3d.Dot(rays[i].DirectionEnu, rays[j].DirectionEnu), -1.0, 1.0);
                var angleDeg = Math.Acos(dot) * (180.0 / Math.PI);
                if (angleDeg > maxParallaxDegrees)
                    maxParallaxDegrees = angleDeg;

                if (angleDeg + 1e-9 < MinimumParallaxDegrees)
                    continue;

                if (TryIntersectTwoRays(rays[i], rays[j], maxRangeMetres, out var candidate, out var behindCamera))
                {
                    if (angleDeg > bestParallaxForForwardPair)
                    {
                        bestParallaxForForwardPair = angleDeg;
                        bestTriangulated = candidate;
                    }
                }
                else if (behindCamera)
                {
                    sawBackwardPair = true;
                }
            }
        }

        if (bestTriangulated.HasValue)
        {
            position = bestTriangulated.Value;
            method = FeatureReconstructionMethod.TwoRayTriangulation;
            failureReason = GeometryFailureReason.None;
            return true;
        }

        if (supportPlane is not null)
        {
            var bestGrazing = 0.0;
            Vector3d? bestPlaneHit = null;
            var planeBehind = false;

            foreach (var ray in rays)
            {
                var sinGrazing = Math.Clamp(Math.Abs(Vector3d.Dot(ray.DirectionEnu, supportPlane.UnitNormal)), 0.0, 1.0);
                var grazingDeg = Math.Asin(sinGrazing) * (180.0 / Math.PI);
                if (planeGrazingDegrees is null || grazingDeg > planeGrazingDegrees.Value)
                    planeGrazingDegrees = grazingDeg;

                if (grazingDeg + 1e-9 < MinimumPlaneGrazingDegrees)
                    continue;

                if (TryIntersectRayPlane(ray, supportPlane, maxRangeMetres, out var hit, out var behind))
                {
                    if (grazingDeg > bestGrazing)
                    {
                        bestGrazing = grazingDeg;
                        bestPlaneHit = hit;
                    }
                }
                else if (behind)
                {
                    planeBehind = true;
                }
            }

            if (bestPlaneHit.HasValue)
            {
                position = bestPlaneHit.Value;
                method = FeatureReconstructionMethod.KnownSupportPlane;
                failureReason = GeometryFailureReason.None;
                return true;
            }

            failureReason = planeBehind
                ? GeometryFailureReason.BehindCamera
                : GeometryFailureReason.LowPlaneGrazingAngle;
            return false;
        }

        if (rays.Length < 2)
        {
            failureReason = GeometryFailureReason.SingleFrameWithoutSupportPlane;
            return false;
        }

        failureReason = sawBackwardPair
            ? GeometryFailureReason.BehindCamera
            : GeometryFailureReason.LowParallaxAngle;
        return false;
    }

    private static bool TryIntersectTwoRays(
        CameraRayEnu first,
        CameraRayEnu second,
        double maxRangeMetres,
        out Vector3d point,
        out bool behindCamera)
    {
        point = default;
        behindCamera = false;

        var o1 = first.OriginEnu;
        var d1 = first.DirectionEnu;
        var o2 = second.OriginEnu;
        var d2 = second.DirectionEnu;

        var w0 = o1 - o2;
        var b = Vector3d.Dot(d1, d2);
        var d = Vector3d.Dot(d1, w0);
        var e = Vector3d.Dot(d2, w0);
        var denom = 1.0 - b * b;
        if (denom <= 1e-10)
            return false;

        var t1 = (b * e - d) / denom;
        var t2 = (e - b * d) / denom;
        if (t1 <= 0.0 || t2 <= 0.0)
        {
            behindCamera = true;
            return false;
        }
        if (t1 > maxRangeMetres || t2 > maxRangeMetres)
            return false;

        point = 0.5 * ((o1 + t1 * d1) + (o2 + t2 * d2));
        return double.IsFinite(point.X) && double.IsFinite(point.Y) && double.IsFinite(point.Z);
    }

    private static bool TryIntersectRayPlane(
        CameraRayEnu ray,
        SupportPlane plane,
        double maxRangeMetres,
        out Vector3d hit,
        out bool behindCamera)
    {
        hit = default;
        behindCamera = false;

        var denom = Vector3d.Dot(ray.DirectionEnu, plane.UnitNormal);
        if (Math.Abs(denom) <= 1e-10)
            return false;

        var t = Vector3d.Dot(plane.PointEnu - ray.OriginEnu, plane.UnitNormal) / denom;
        if (t <= 0.0)
        {
            behindCamera = true;
            return false;
        }
        if (t > maxRangeMetres)
            return false;

        hit = ray.OriginEnu + t * ray.DirectionEnu;
        return double.IsFinite(hit.X) && double.IsFinite(hit.Y) && double.IsFinite(hit.Z);
    }

    private static (double X, double Y) UndistortNormalized(double xd, double yd, double k1, double k2)
    {
        if (k1 == 0.0 && k2 == 0.0)
            return (xd, yd);

        var x = xd;
        var y = yd;
        for (var i = 0; i < 16; i++)
        {
            var r2 = x * x + y * y;
            var scale = 1.0 + k1 * r2 + k2 * r2 * r2;
            if (!double.IsFinite(scale) || Math.Abs(scale) < 1e-6)
                throw new InvalidOperationException("Radial distortion could not be inverted stably.");

            var nextX = xd / scale;
            var nextY = yd / scale;
            if (Math.Abs(nextX - x) < 1e-12 && Math.Abs(nextY - y) < 1e-12)
                return (nextX, nextY);
            x = nextX;
            y = nextY;
        }
        return (x, y);
    }
}
