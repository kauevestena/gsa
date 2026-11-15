using Gsa.Core.Geometry;
using Gsa.Core.Mobility;

namespace Gsa.Core.Sensors.Camera;

public sealed record SensorCoverageSummary(
    string SensorId,
    int SampleCount,
    int StorageUnitsConsumed,
    double FirstTimestampSeconds,
    double LastTimestampSeconds,
    double MeanConsecutiveOverlapFraction,
    bool MeetsOverlapRequirement);

public sealed record PlatformRigCoverageReport(
    LocomotionMode Mode,
    int TotalSamples,
    int TotalStorageUnitsUsed,
    int StorageCapacityUnits,
    bool UniqueSampleIdsVerified,
    bool TimestampsMonotonic,
    IReadOnlyList<SensorCoverageSummary> BySensor);

/// <summary>Builds helmet (bicycle) and front/rear (scooter) camera rigs sharing SensorCaptureAgenda without duplicating pipelines.</summary>
public static class PlatformCameraRigBuilder
{
    public static CameraMountEstimate HelmetMount { get; } =
        new(new Vector3d(0.05, 0.0, 1.55), CameraMountEstimate.ForwardFacing.CameraToBody);

    public static CameraMountEstimate ScooterFrontMount { get; } =
        new(new Vector3d(0.65, 0.0, 1.10), CameraMountEstimate.ForwardFacing.CameraToBody);

    public static CameraMountEstimate ScooterRearMount { get; } =
        new(new Vector3d(-0.65, 0.0, 1.10), Quaterniond.FromAxisAngle(Vector3d.UnitZ, Math.PI) * CameraMountEstimate.ForwardFacing.CameraToBody);

    public static CameraSensorProfile BicycleHelmetProfile { get; } = new(
        id: "camera-bicycle-helmet",
        rateHz: 2.0,
        rangeMetres: 90.0,
        intrinsics: CameraIntrinsics.Initial,
        dayExposureSeconds: 1.0 / 500.0,
        nightExposureSeconds: 1.0 / 60.0,
        readoutTimeSeconds: 0.010);

    public static CameraSensorProfile ScooterFrontProfile { get; } = new(
        id: "camera-scooter-front",
        rateHz: 2.0,
        rangeMetres: 100.0,
        intrinsics: CameraIntrinsics.Initial,
        dayExposureSeconds: 1.0 / 500.0,
        nightExposureSeconds: 1.0 / 60.0,
        readoutTimeSeconds: 0.010);

    public static CameraSensorProfile ScooterRearProfile { get; } = new(
        id: "camera-scooter-rear",
        rateHz: 2.0,
        rangeMetres: 100.0,
        intrinsics: CameraIntrinsics.Initial,
        dayExposureSeconds: 1.0 / 500.0,
        nightExposureSeconds: 1.0 / 60.0,
        readoutTimeSeconds: 0.010);

    public static SensorCaptureAgenda CreateAgendaForPlatform(
        LocomotionMode mode,
        double initialTimeSeconds = 0.0)
    {
        var capacity = PlatformProfile.ForMode(mode).VirtualStorageCapacityUnits;
        var agenda = new SensorCaptureAgenda(capacity);

        switch (mode)
        {
            case LocomotionMode.Handcart:
            case LocomotionMode.OnFoot:
                agenda.RegisterSensor("cam_front", CameraSensorProfile.Initial, CameraMountEstimate.ForwardFacing, initialTimeSeconds);
                break;

            case LocomotionMode.Bicycle:
                agenda.RegisterSensor("cam_helmet", BicycleHelmetProfile, HelmetMount, initialTimeSeconds);
                break;

            case LocomotionMode.Scooter:
                agenda.RegisterSensor("cam_scooter_front", ScooterFrontProfile, ScooterFrontMount, initialTimeSeconds);
                agenda.RegisterSensor("cam_scooter_rear", ScooterRearProfile, ScooterRearMount, initialTimeSeconds);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported locomotion mode.");
        }

        return agenda;
    }

    public static PlatformRigCoverageReport BuildCoverageReport(
        LocomotionMode mode,
        ObservationBatch batch,
        IReadOnlyDictionary<string, SurfaceFootprint2DEnu>? footprintsBySampleId = null,
        double requiredOverlap = SurfaceFootprintOverlapCalculator.DefaultMinimumOverlapFraction)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var capacity = PlatformProfile.ForMode(mode).VirtualStorageCapacityUnits;
        var uniqueIds = new HashSet<string>(StringComparer.Ordinal);
        var uniqueVerified = true;
        var monotonicAll = true;
        var summaries = new List<SensorCoverageSummary>(batch.SensorIds.Count);

        foreach (var sensorId in batch.SensorIds)
        {
            var samples = batch.Samples
                .Where(s => string.Equals(s.SensorId, sensorId, StringComparison.Ordinal))
                .ToArray();

            var prevTime = double.NegativeInfinity;
            foreach (var sample in samples)
            {
                if (!uniqueIds.Add(sample.SampleId))
                    uniqueVerified = false;
                if (sample.TimestampSeconds < prevTime)
                    monotonicAll = false;
                prevTime = sample.TimestampSeconds;
            }

            var overlaps = new List<double>();
            if (footprintsBySampleId is not null && samples.Length >= 2)
            {
                for (var i = 1; i < samples.Length; i++)
                {
                    footprintsBySampleId.TryGetValue(samples[i - 1].SampleId, out var prevFp);
                    footprintsBySampleId.TryGetValue(samples[i].SampleId, out var currFp);
                    var eval = SurfaceFootprintOverlapCalculator.EvaluateConsecutiveOverlap(
                        footprintsBySampleId.ContainsKey(samples[i - 1].SampleId) ? prevFp : null,
                        footprintsBySampleId.ContainsKey(samples[i].SampleId) ? currFp : null,
                        requiredOverlap);
                    if (eval.HasSufficientGeometry)
                        overlaps.Add(eval.OverlapFraction);
                }
            }

            var meanOverlap = overlaps.Count > 0 ? overlaps.Average() : 0.0;
            summaries.Add(new SensorCoverageSummary(
                SensorId: sensorId,
                SampleCount: samples.Length,
                StorageUnitsConsumed: samples.Length,
                FirstTimestampSeconds: samples.Length > 0 ? samples[0].TimestampSeconds : 0.0,
                LastTimestampSeconds: samples.Length > 0 ? samples[^1].TimestampSeconds : 0.0,
                MeanConsecutiveOverlapFraction: meanOverlap,
                MeetsOverlapRequirement: overlaps.Count > 0 && overlaps.All(o => o >= requiredOverlap)));
        }

        return new PlatformRigCoverageReport(
            Mode: mode,
            TotalSamples: batch.Samples.Count,
            TotalStorageUnitsUsed: batch.StorageUnitsUsed,
            StorageCapacityUnits: capacity,
            UniqueSampleIdsVerified: uniqueVerified,
            TimestampsMonotonic: monotonicAll,
            BySensor: summaries);
    }
}

