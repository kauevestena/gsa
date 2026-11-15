using System.Text.Json;

namespace Gsa.Core.Sensors.Camera;

public sealed record PrivacyMaskRegion(
    string TargetId,
    double MinU,
    double MinV,
    double MaxU,
    double MaxV,
    bool Applied);

/// <summary>Compact captured image observation; contains only estimated pose, 2D features, quality, and thumbnail.</summary>
public sealed class CameraObservationSample
{
    public string SampleId { get; }
    public string SensorId { get; }
    public long Sequence { get; }
    public double TimestampSeconds { get; }
    public EstimatedBodyPose EstimatedPose { get; }
    public CameraMountEstimate MountEstimate { get; }
    public CameraIntrinsics Intrinsics { get; }
    public CameraQualityMetrics Quality { get; }
    public IReadOnlyList<ObservedFeature2D> ObservedFeatures { get; }
    public IReadOnlyList<PrivacyMaskRegion> PrivacyMasks { get; }
    public CameraThumbnail Thumbnail { get; }
    public bool FullTexturePersisted => false;

    public CameraObservationSample(
        string sampleId,
        string sensorId,
        long sequence,
        double timestampSeconds,
        EstimatedBodyPose estimatedPose,
        CameraMountEstimate mountEstimate,
        CameraIntrinsics intrinsics,
        CameraQualityMetrics quality,
        IEnumerable<ObservedFeature2D> observedFeatures,
        IEnumerable<PrivacyMaskRegion> privacyMasks,
        CameraThumbnail thumbnail)
    {
        if (string.IsNullOrWhiteSpace(sampleId))
            throw new ArgumentException("Sample ID is required.", nameof(sampleId));
        if (string.IsNullOrWhiteSpace(sensorId))
            throw new ArgumentException("Sensor ID is required.", nameof(sensorId));
        if (sequence < 0)
            throw new ArgumentOutOfRangeException(nameof(sequence), "Sequence cannot be negative.");
        if (!double.IsFinite(timestampSeconds) || timestampSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(timestampSeconds), "Timestamp must be finite and non-negative.");
        ArgumentNullException.ThrowIfNull(estimatedPose);
        ArgumentNullException.ThrowIfNull(mountEstimate);
        ArgumentNullException.ThrowIfNull(intrinsics);
        ArgumentNullException.ThrowIfNull(quality);
        ArgumentNullException.ThrowIfNull(observedFeatures);
        ArgumentNullException.ThrowIfNull(privacyMasks);
        ArgumentNullException.ThrowIfNull(thumbnail);

        SampleId = sampleId;
        SensorId = sensorId;
        Sequence = sequence;
        TimestampSeconds = timestampSeconds;
        EstimatedPose = estimatedPose;
        MountEstimate = mountEstimate;
        Intrinsics = intrinsics;
        Quality = quality;
        ObservedFeatures = Array.AsReadOnly(observedFeatures.ToArray());
        PrivacyMasks = Array.AsReadOnly(privacyMasks.ToArray());
        Thumbnail = thumbnail;
    }
}

/// <summary>Immutable capture batch manifest serializable as schema_version 1 JSON without full textures or truth.</summary>
public sealed class CameraCaptureBatchManifest
{
    public int SchemaVersion => 1;
    public string BatchId { get; }
    public string SensorId { get; }
    public string ProfileId { get; }
    public double StartTimeSeconds { get; }
    public double EndTimeSeconds { get; }
    public int StorageUnitsUsed { get; }
    public int SaturatedDroppedCount { get; }
    public IReadOnlyList<CameraObservationSample> Samples { get; }

    public CameraCaptureBatchManifest(
        string batchId,
        string sensorId,
        string profileId,
        IEnumerable<CameraObservationSample> samples,
        int storageUnitsUsed,
        int saturatedDroppedCount = 0)
    {
        if (string.IsNullOrWhiteSpace(batchId))
            throw new ArgumentException("Batch ID is required.", nameof(batchId));
        if (string.IsNullOrWhiteSpace(sensorId))
            throw new ArgumentException("Sensor ID is required.", nameof(sensorId));
        if (string.IsNullOrWhiteSpace(profileId))
            throw new ArgumentException("Profile ID is required.", nameof(profileId));
        ArgumentNullException.ThrowIfNull(samples);

        var list = samples.ToArray();
        BatchId = batchId;
        SensorId = sensorId;
        ProfileId = profileId;
        Samples = Array.AsReadOnly(list);
        StartTimeSeconds = list.Length > 0 ? list[0].TimestampSeconds : 0.0;
        EndTimeSeconds = list.Length > 0 ? list[^1].TimestampSeconds : 0.0;
        StorageUnitsUsed = storageUnitsUsed;
        SaturatedDroppedCount = saturatedDroppedCount;
    }

    public string ToJson()
    {
        var payload = new
        {
            schema_version = SchemaVersion,
            batch_id = BatchId,
            sensor_id = SensorId,
            profile_id = ProfileId,
            start_time_s = StartTimeSeconds,
            end_time_s = EndTimeSeconds,
            storage_units_used = StorageUnitsUsed,
            saturated_dropped_count = SaturatedDroppedCount,
            full_texture_persisted = false,
            samples = Samples.Select(sample => new
            {
                sample_id = sample.SampleId,
                sensor_id = sample.SensorId,
                sequence = sample.Sequence,
                timestamp_s = sample.TimestampSeconds,
                estimated_pose = new
                {
                    time_s = sample.EstimatedPose.TimeSeconds,
                    position_enu = new[]
                    {
                        sample.EstimatedPose.PositionEnu.X,
                        sample.EstimatedPose.PositionEnu.Y,
                        sample.EstimatedPose.PositionEnu.Z,
                    },
                    body_to_map = new[]
                    {
                        sample.EstimatedPose.BodyToMap.X,
                        sample.EstimatedPose.BodyToMap.Y,
                        sample.EstimatedPose.BodyToMap.Z,
                        sample.EstimatedPose.BodyToMap.W,
                    },
                },
                quality = new
                {
                    exposure_s = sample.Quality.ExposureSeconds,
                    motion_blur_px = sample.Quality.MotionBlurPixels,
                    clipping_fraction = sample.Quality.ClippingFraction,
                    lens_obstruction_fraction = sample.Quality.LensObstructionFraction,
                    noise_sigma_px = sample.Quality.NoiseSigmaPixels,
                    motion_blur_exceeded = sample.Quality.MotionBlurExceeded,
                    clipping_exceeded = sample.Quality.ClippingExceeded,
                    lens_obstruction_exceeded = sample.Quality.LensObstructionExceeded,
                    insufficient_visible_features = sample.Quality.InsufficientVisibleFeatures,
                    passed = sample.Quality.Passed,
                },
                observed_features = sample.ObservedFeatures.Select(f => new
                {
                    target_id = f.TargetId,
                    object_class = f.ObjectClass,
                    center_px = new[] { f.CenterU, f.CenterV },
                    bounds_px = new[] { f.MinU, f.MinV, f.MaxU, f.MaxV },
                    min_projected_dimension_px = f.MinProjectedDimensionPixels,
                    visible_sample_rays = f.VisibleSampleRays,
                    total_sample_rays = f.TotalSampleRays,
                    rolling_shutter_shift_px = f.RollingShutterShiftPixels,
                    requires_privacy_review = f.RequiresPrivacyReview,
                }),
                privacy_masks = sample.PrivacyMasks.Select(m => new
                {
                    target_id = m.TargetId,
                    bounds_px = new[] { m.MinU, m.MinV, m.MaxU, m.MaxV },
                    applied = m.Applied,
                }),
                thumbnail = new
                {
                    width = sample.Thumbnail.Width,
                    height = sample.Thumbnail.Height,
                    format = sample.Thumbnail.Format,
                    byte_length = sample.Thumbnail.ByteLength,
                    sha256 = sample.Thumbnail.Sha256,
                    full_texture_persisted = sample.Thumbnail.FullTexturePersisted,
                },
            }),
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }
}

