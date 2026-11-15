using Gsa.Core.Geometry;
using Gsa.Core.Mobility;

namespace Gsa.Core.Sensors.Camera;

public sealed record CameraCaptureRequest(
    long Tick,
    double TimestampSeconds,
    RigidTransform TrueCameraToMapStart,
    RigidTransform TrueCameraToMapExposureEnd,
    RigidTransform TrueCameraToMapReadoutEnd,
    EstimatedBodyPose EstimatedPose,
    CameraMountEstimate MountEstimate,
    CameraEnvironmentSnapshot Environment,
    IReadOnlyList<CameraVisibilityCandidate> Candidates,
    Func<Vector3d, Vector3d, string, bool> IsRayOccluded,
    CameraThumbnail? EngineThumbnailOverride = null);

/// <summary>Physical-clock 2 Hz scheduler with 0.5 s backlog limit and immutable batch construction.</summary>
public sealed class CameraCaptureScheduler
{
    public const int PhysicsTicksPerSecond = 60;
    public const double MaxBacklogSeconds = 0.5;

    private readonly string _sensorId;
    private readonly CameraSensorProfile _profile;
    private readonly int _tickInterval;
    private readonly List<CameraObservationSample> _samples = new();
    private long _nextSequence;
    private int _pendingQueueCount;
    private int _saturatedDroppedCount;

    public CameraSensorProfile Profile => _profile;
    public int SampleCount => _samples.Count;
    public int TotalCapturedCount { get; private set; }
    public int PendingQueueCount => _pendingQueueCount;
    public int SaturatedDroppedCount => _saturatedDroppedCount;
    public int MaxPendingQueueSamples { get; }

    public CameraCaptureScheduler(string sensorId, CameraSensorProfile? profile = null)
    {
        if (string.IsNullOrWhiteSpace(sensorId))
            throw new ArgumentException("Sensor ID is required.", nameof(sensorId));
        _sensorId = sensorId;
        _profile = profile ?? CameraSensorProfile.Initial;
        _tickInterval = Math.Max(1, (int)Math.Round(PhysicsTicksPerSecond / _profile.RateHz));
        MaxPendingQueueSamples = Math.Max(1, (int)Math.Floor(MaxBacklogSeconds * _profile.RateHz));
    }

    public bool IsCaptureTick(long tick) => tick >= 0 && tick % _tickInterval == 0;

    public int DrainPending(int maxToDrain)
    {
        if (maxToDrain < 0)
            throw new ArgumentOutOfRangeException(nameof(maxToDrain), "Drain count cannot be negative.");
        var drained = Math.Min(_pendingQueueCount, maxToDrain);
        _pendingQueueCount -= drained;
        return drained;
    }

    public bool TryEnqueueSimulatedCapture(
        CameraCaptureRequest request,
        LocomotionSimulator? locomotion,
        out CameraObservationSample? sample,
        out string? stopReason,
        bool autoDrain = true)
    {
        ArgumentNullException.ThrowIfNull(request);
        sample = null;
        stopReason = null;

        if (!IsCaptureTick(request.Tick))
            return false;

        if (_pendingQueueCount >= MaxPendingQueueSamples)
        {
            _saturatedDroppedCount++;
            stopReason = "sensor_queue_saturated";
            return false;
        }

        if (locomotion is not null && !locomotion.TryConsumeCaptureUnits(_profile.StorageUnitsPerSample, out var storageStop))
        {
            stopReason = storageStop switch
            {
                CaptureStopReason.AutonomyExhausted => "autonomy_exhausted",
                CaptureStopReason.VirtualStorageFull => "virtual_storage_full",
                CaptureStopReason.GimbalinaFled => "gimbalina_fled",
                _ => "capture_stopped",
            };
            return false;
        }

        _pendingQueueCount++;
        try
        {
            var features = CameraVisibilityEvaluator.EvaluateVisibleFeatures(
                request.TrueCameraToMapStart,
                request.TrueCameraToMapReadoutEnd,
                _profile,
                request.Candidates,
                request.IsRayOccluded);

            var referencePoints = features
                .Select(f => request.Candidates.First(c => c.TargetId == f.TargetId).CenterEnu)
                .ToArray();

            var quality = CameraExposureMetrics.EvaluateFrameMetrics(
                _profile,
                request.Environment,
                request.TrueCameraToMapStart,
                request.TrueCameraToMapExposureEnd,
                referencePoints,
                features.Count);

            var masks = features
                .Where(f => f.RequiresPrivacyReview)
                .Select(f => new PrivacyMaskRegion(f.TargetId, f.MinU, f.MinV, f.MaxU, f.MaxV, Applied: true))
                .ToArray();

            var thumbnail = request.EngineThumbnailOverride
                            ?? CameraThumbnail.CreateSynthetic(_profile, features, request.Environment);

            var sequence = _nextSequence++;
            sample = new CameraObservationSample(
                sampleId: $"{_sensorId}_{sequence:D6}",
                sensorId: _sensorId,
                sequence: sequence,
                timestampSeconds: request.TimestampSeconds,
                estimatedPose: request.EstimatedPose,
                mountEstimate: request.MountEstimate,
                intrinsics: _profile.Intrinsics,
                quality: quality,
                observedFeatures: features,
                privacyMasks: masks,
                thumbnail: thumbnail);

            _samples.Add(sample);
            TotalCapturedCount++;
            return true;
        }
        finally
        {
            if (autoDrain)
                _pendingQueueCount = Math.Max(0, _pendingQueueCount - 1);
        }
    }

    public CameraCaptureBatchManifest BuildBatchManifest(string batchId) =>
        new(
            batchId: batchId,
            sensorId: _sensorId,
            profileId: _profile.Id,
            samples: _samples,
            storageUnitsUsed: _samples.Count * _profile.StorageUnitsPerSample,
            saturatedDroppedCount: _saturatedDroppedCount);

    /// <summary>Builds a detached batch manifest and releases in-memory buffered samples after offload.</summary>
    public CameraCaptureBatchManifest FlushBatchManifest(string batchId)
    {
        var snapshot = _samples.ToArray();
        var manifest = new CameraCaptureBatchManifest(
            batchId: batchId,
            sensorId: _sensorId,
            profileId: _profile.Id,
            samples: snapshot,
            storageUnitsUsed: snapshot.Length * _profile.StorageUnitsPerSample,
            saturatedDroppedCount: _saturatedDroppedCount);
        _samples.Clear();
        return manifest;
    }
}
