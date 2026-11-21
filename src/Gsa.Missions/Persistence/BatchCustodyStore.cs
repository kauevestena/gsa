using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Camera;
using Gsa.Missions.Quality;

namespace Gsa.Missions.Persistence;

public enum BatchCustodyStage
{
    Buffered = 0,
    Offloading = 1,
    Protected = 2,
    Lost = 3,
}

public enum CustodyOperationStatus
{
    Executed = 0,
    IdempotentNoOp = 1,
    NotFound = 2,
    InvalidStage = 3,
    ChecksumMismatch = 4,
}

public sealed record SerializablePoseDto(double TimeSeconds, double East, double North, double Up, double Qx, double Qy, double Qz, double Qw);

public sealed record SerializableMountDto(double LeverX, double LeverY, double LeverZ, double Qx, double Qy, double Qz, double Qw, double TimeOffsetSeconds);

public sealed record SerializableIntrinsicsDto(int Width, int Height, double Fx, double Fy, double Cx, double Cy, double K1, double K2);

public sealed record SerializableCameraSample(
    string SampleId,
    string SensorId,
    long Sequence,
    double TimestampSeconds,
    SerializablePoseDto EstimatedPose,
    SerializableMountDto MountEstimate,
    SerializableIntrinsicsDto Intrinsics,
    CameraQualityMetrics Quality,
    IReadOnlyList<ObservedFeature2D> ObservedFeatures,
    IReadOnlyList<PrivacyMaskRegion> PrivacyMasks,
    string ThumbnailRgbBase64)
{
    public static SerializableCameraSample FromDomain(CameraObservationSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        var p = sample.EstimatedPose;
        var m = sample.MountEstimate;
        var i = sample.Intrinsics;
        return new SerializableCameraSample(
            sample.SampleId,
            sample.SensorId,
            sample.Sequence,
            sample.TimestampSeconds,
            new SerializablePoseDto(p.TimeSeconds, p.PositionEnu.X, p.PositionEnu.Y, p.PositionEnu.Z, p.BodyToMap.X, p.BodyToMap.Y, p.BodyToMap.Z, p.BodyToMap.W),
            new SerializableMountDto(m.LeverArmBody.X, m.LeverArmBody.Y, m.LeverArmBody.Z, m.CameraToBody.X, m.CameraToBody.Y, m.CameraToBody.Z, m.CameraToBody.W, m.TimeOffsetSeconds),
            new SerializableIntrinsicsDto(i.Width, i.Height, i.Fx, i.Fy, i.Cx, i.Cy, i.K1, i.K2),
            sample.Quality,
            sample.ObservedFeatures.ToArray(),
            sample.PrivacyMasks.ToArray(),
            Convert.ToBase64String(sample.Thumbnail.CopyRgbBytes()));
    }

    public CameraObservationSample ToDomain()
    {
        var pose = new EstimatedBodyPose(
            EstimatedPose.TimeSeconds,
            new Vector3d(EstimatedPose.East, EstimatedPose.North, EstimatedPose.Up),
            new Quaterniond(EstimatedPose.Qx, EstimatedPose.Qy, EstimatedPose.Qz, EstimatedPose.Qw));
        var mount = new CameraMountEstimate(
            new Vector3d(MountEstimate.LeverX, MountEstimate.LeverY, MountEstimate.LeverZ),
            new Quaterniond(MountEstimate.Qx, MountEstimate.Qy, MountEstimate.Qz, MountEstimate.Qw),
            MountEstimate.TimeOffsetSeconds);
        var intrinsics = new CameraIntrinsics(
            Intrinsics.Width, Intrinsics.Height, Intrinsics.Fx, Intrinsics.Fy,
            Intrinsics.Cx, Intrinsics.Cy, Intrinsics.K1, Intrinsics.K2);
        var thumb = CameraThumbnail.FromRgbBytes(
            CameraThumbnail.ExpectedWidth,
            CameraThumbnail.ExpectedHeight,
            Convert.FromBase64String(ThumbnailRgbBase64));

        return new CameraObservationSample(
            SampleId, SensorId, Sequence, TimestampSeconds,
            pose, mount, intrinsics, Quality, ObservedFeatures, PrivacyMasks, thumb);
    }
}

/// <summary>Canonical, checksummed survey payload owned by a single capture batch.</summary>
public sealed record SurveyBatchArtifact(
    int SchemaVersion,
    string BatchId,
    string ContractId,
    double StartTimeSeconds,
    double EndTimeSeconds,
    int StorageUnitsUsed,
    int SaturatedDroppedCount,
    CaptureStopReason StopReason,
    IReadOnlyList<string> SensorIds,
    IReadOnlyList<SerializableCameraSample> Samples,
    IReadOnlyList<SampleFootprintObservation> Footprints,
    IReadOnlyList<TriangulatedFeatureEntry> Features,
    IReadOnlyList<SubmittedCheckpointEstimate> Checkpoints)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CityLidarSlice>? LidarSlices { get; init; }

    public static SurveyBatchArtifact FromObservationBatch(
        string contractId,
        ObservationBatch batch,
        IEnumerable<SampleFootprintObservation>? footprints = null,
        IEnumerable<TriangulatedFeatureEntry>? features = null,
        IEnumerable<SubmittedCheckpointEstimate>? checkpoints = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractId);
        ArgumentNullException.ThrowIfNull(batch);

        return new SurveyBatchArtifact(
            1,
            batch.BatchId,
            contractId,
            batch.StartTimeSeconds,
            batch.EndTimeSeconds,
            batch.StorageUnitsUsed,
            batch.SaturatedDroppedCount,
            batch.StopReason,
            batch.SensorIds.ToArray(),
            batch.Samples.Select(SerializableCameraSample.FromDomain).ToArray(),
            footprints is null ? [] : footprints.ToArray(),
            features is null ? [] : features.ToArray(),
            checkpoints is null ? [] : checkpoints.ToArray());
    }

    public string ToCanonicalJson() => JsonSerializer.Serialize(this);

    public string ComputeSha256Hex() =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ToCanonicalJson()))).ToLowerInvariant();
}

public sealed record CommittedBatchManifestEntry(
    string BatchId,
    string ContractId,
    string PayloadSha256,
    double CommittedAtSeconds);

public sealed record CustodyManifest(
    int SchemaVersion,
    IReadOnlyList<CommittedBatchManifestEntry> CommittedBatches);

public sealed record BatchCustodyEntry(
    string BatchId,
    string ContractId,
    BatchCustodyStage Stage,
    string PayloadSha256,
    string PayloadJson,
    string? StagedChunkJson,
    string? StagedChunkSha256,
    double RegisteredAtSeconds,
    double? OffloadStartedAtSeconds,
    double? ProtectedAtSeconds,
    double? LostAtSeconds);

public sealed record CustodyStoreSnapshot(
    int SchemaVersion,
    CustodyManifest AuthoritativeManifest,
    IReadOnlyList<BatchCustodyEntry> Entries);

public sealed record CustodyOperationResult(
    CustodyOperationStatus Status,
    string BatchId,
    BatchCustodyStage? Stage,
    string? PayloadSha256,
    string? ReasonKey = null)
{
    public bool Succeeded => Status is CustodyOperationStatus.Executed or CustodyOperationStatus.IdempotentNoOp;
}

/// <summary>Chunk-verified atomic batch offload store insulated against mid-transfer crashes and vehicle loss.</summary>
public sealed class BatchCustodyStore
{
    private readonly Dictionary<string, BatchCustodyEntry> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CommittedBatchManifestEntry> _committedManifest = new(StringComparer.Ordinal);

    public IReadOnlyList<BatchCustodyEntry> Entries => _entries.Values.OrderBy(e => e.RegisteredAtSeconds).ThenBy(e => e.BatchId, StringComparer.Ordinal).ToArray();
    public CustodyManifest AuthoritativeManifest =>
        new(1, _committedManifest.Values.OrderBy(c => c.CommittedAtSeconds).ThenBy(c => c.BatchId, StringComparer.Ordinal).ToArray());

    public CustodyOperationResult RegisterBufferedBatch(SurveyBatchArtifact artifact, double timestampSeconds)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ValidateTime(timestampSeconds);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifact.BatchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifact.ContractId);

        if (_entries.TryGetValue(artifact.BatchId, out var existing))
        {
            return new CustodyOperationResult(
                CustodyOperationStatus.IdempotentNoOp,
                artifact.BatchId,
                existing.Stage,
                existing.PayloadSha256,
                "custody.batch.already_registered");
        }

        var json = artifact.ToCanonicalJson();
        var sha = ComputeSha256(json);
        _entries.Add(artifact.BatchId, new BatchCustodyEntry(
            artifact.BatchId,
            artifact.ContractId,
            BatchCustodyStage.Buffered,
            sha,
            json,
            null,
            null,
            timestampSeconds,
            null,
            null,
            null));

        return new CustodyOperationResult(CustodyOperationStatus.Executed, artifact.BatchId, BatchCustodyStage.Buffered, sha);
    }

    public CustodyOperationResult BeginOffload(string batchId, double timestampSeconds, string? overrideChunkJson = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(batchId);
        ValidateTime(timestampSeconds);

        if (!_entries.TryGetValue(batchId, out var entry))
            return new CustodyOperationResult(CustodyOperationStatus.NotFound, batchId, null, null, "custody.error.not_found");

        if (entry.Stage == BatchCustodyStage.Protected && _committedManifest.ContainsKey(batchId))
            return new CustodyOperationResult(CustodyOperationStatus.IdempotentNoOp, batchId, entry.Stage, entry.PayloadSha256);

        if (entry.Stage == BatchCustodyStage.Offloading)
            return new CustodyOperationResult(CustodyOperationStatus.IdempotentNoOp, batchId, entry.Stage, entry.PayloadSha256);

        if (entry.Stage != BatchCustodyStage.Buffered)
            return new CustodyOperationResult(CustodyOperationStatus.InvalidStage, batchId, entry.Stage, entry.PayloadSha256, "custody.error.invalid_stage");

        var chunkJson = overrideChunkJson ?? entry.PayloadJson;
        var chunkSha = ComputeSha256(chunkJson);
        if (!string.Equals(chunkSha, entry.PayloadSha256, StringComparison.Ordinal))
        {
            return new CustodyOperationResult(
                CustodyOperationStatus.ChecksumMismatch,
                batchId,
                entry.Stage,
                entry.PayloadSha256,
                "custody.error.checksum_mismatch");
        }

        _entries[batchId] = entry with
        {
            Stage = BatchCustodyStage.Offloading,
            StagedChunkJson = chunkJson,
            StagedChunkSha256 = chunkSha,
            OffloadStartedAtSeconds = timestampSeconds,
        };
        return new CustodyOperationResult(CustodyOperationStatus.Executed, batchId, BatchCustodyStage.Offloading, entry.PayloadSha256);
    }

    public CustodyOperationResult CommitOffload(string batchId, double timestampSeconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(batchId);
        ValidateTime(timestampSeconds);

        if (!_entries.TryGetValue(batchId, out var entry))
            return new CustodyOperationResult(CustodyOperationStatus.NotFound, batchId, null, null, "custody.error.not_found");

        if (entry.Stage == BatchCustodyStage.Protected && _committedManifest.ContainsKey(batchId))
            return new CustodyOperationResult(CustodyOperationStatus.IdempotentNoOp, batchId, entry.Stage, entry.PayloadSha256);

        if (entry.Stage != BatchCustodyStage.Offloading || entry.StagedChunkJson is null)
            return new CustodyOperationResult(CustodyOperationStatus.InvalidStage, batchId, entry.Stage, entry.PayloadSha256, "custody.error.not_offloading");

        var verifiedSha = ComputeSha256(entry.StagedChunkJson);
        if (!string.Equals(verifiedSha, entry.PayloadSha256, StringComparison.Ordinal) ||
            !string.Equals(verifiedSha, entry.StagedChunkSha256, StringComparison.Ordinal))
        {
            return new CustodyOperationResult(
                CustodyOperationStatus.ChecksumMismatch,
                batchId,
                entry.Stage,
                entry.PayloadSha256,
                "custody.error.checksum_mismatch");
        }

        _committedManifest[batchId] = new CommittedBatchManifestEntry(batchId, entry.ContractId, verifiedSha, timestampSeconds);
        _entries[batchId] = entry with
        {
            Stage = BatchCustodyStage.Protected,
            StagedChunkJson = null,
            StagedChunkSha256 = null,
            ProtectedAtSeconds = timestampSeconds,
        };
        return new CustodyOperationResult(CustodyOperationStatus.Executed, batchId, BatchCustodyStage.Protected, verifiedSha);
    }

    public CustodyOperationResult OffloadAtomic(string batchId, double timestampSeconds)
    {
        var begin = BeginOffload(batchId, timestampSeconds);
        if (!begin.Succeeded)
            return begin;
        return CommitOffload(batchId, timestampSeconds);
    }

    public int HandleVehicleLoss(double timestampSeconds)
    {
        ValidateTime(timestampSeconds);
        var lostCount = 0;
        foreach (var id in _entries.Keys.ToArray())
        {
            var entry = _entries[id];
            if (entry.Stage is BatchCustodyStage.Buffered or BatchCustodyStage.Offloading && !_committedManifest.ContainsKey(id))
            {
                _entries[id] = entry with
                {
                    Stage = BatchCustodyStage.Lost,
                    StagedChunkJson = null,
                    StagedChunkSha256 = null,
                    LostAtSeconds = timestampSeconds,
                };
                lostCount++;
            }
        }
        return lostCount;
    }

    public CustodyStoreSnapshot ExportSnapshot() =>
        new(1, AuthoritativeManifest, Entries);

    /// <summary>Reconciles entries strictly against the authoritative committed manifest after crash or reload.</summary>
    public static BatchCustodyStore RecoverAfterCrash(CustodyStoreSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var store = new BatchCustodyStore();
        var committedMap = snapshot.AuthoritativeManifest.CommittedBatches
            .GroupBy(c => c.BatchId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var entry in snapshot.Entries)
        {
            if (committedMap.TryGetValue(entry.BatchId, out var manifestEntry) &&
                string.Equals(manifestEntry.PayloadSha256, entry.PayloadSha256, StringComparison.Ordinal) &&
                string.Equals(ComputeSha256(entry.PayloadJson), entry.PayloadSha256, StringComparison.Ordinal))
            {
                store._committedManifest[entry.BatchId] = manifestEntry;
                store._entries[entry.BatchId] = entry with
                {
                    Stage = BatchCustodyStage.Protected,
                    StagedChunkJson = null,
                    StagedChunkSha256 = null,
                    ProtectedAtSeconds = entry.ProtectedAtSeconds ?? manifestEntry.CommittedAtSeconds,
                };
            }
            else if (entry.Stage == BatchCustodyStage.Lost)
            {
                store._entries[entry.BatchId] = entry with { StagedChunkJson = null, StagedChunkSha256 = null };
            }
            else
            {
                // Uncommitted Offloading (or unmanifested Protected claim) always rolls back to vehicle-bound Buffered.
                store._entries[entry.BatchId] = entry with
                {
                    Stage = BatchCustodyStage.Buffered,
                    StagedChunkJson = null,
                    StagedChunkSha256 = null,
                    OffloadStartedAtSeconds = null,
                    ProtectedAtSeconds = null,
                };
            }
        }
        return store;
    }

    public IReadOnlyList<SurveyBatchArtifact> GetProtectedArtifactsForContract(string contractId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractId);
        var result = new List<SurveyBatchArtifact>();
        foreach (var entry in Entries)
        {
            if (entry.Stage != BatchCustodyStage.Protected ||
                !_committedManifest.ContainsKey(entry.BatchId) ||
                !string.Equals(entry.ContractId, contractId, StringComparison.Ordinal))
                continue;

            var artifact = JsonSerializer.Deserialize<SurveyBatchArtifact>(entry.PayloadJson)
                           ?? throw new InvalidDataException($"Corrupt protected payload for batch '{entry.BatchId}'.");
            result.Add(artifact);
        }
        return Array.AsReadOnly(result.ToArray());
    }

    private static string ComputeSha256(string json) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();

    private static void ValidateTime(double t)
    {
        if (!double.IsFinite(t) || t < 0)
            throw new ArgumentOutOfRangeException(nameof(t), "Timestamp must be finite and non-negative.");
    }
}

