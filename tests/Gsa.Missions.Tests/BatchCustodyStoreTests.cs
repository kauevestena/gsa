using System.Text.Json;
using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Camera;
using Gsa.Missions.Persistence;
using Xunit;

namespace Gsa.Missions.Tests;

public sealed class BatchCustodyStoreTests
{
    private static SurveyBatchArtifact CreateBatch(string batchId, string contractId = "contract_01")
    {
        var pose = new EstimatedBodyPose(1.0, new Vector3d(500000, 7400000, 101.5), Quaterniond.Identity);
        var quality = new CameraQualityMetrics(0.002, 0.3, 0.01, 0.0, 0.5, false, false, false, false, true);
        var sample = new CameraObservationSample(
            $"{batchId}_s1",
            "cam_front",
            0,
            1.0,
            pose,
            CameraMountEstimate.ForwardFacing,
            CameraIntrinsics.Initial,
            quality,
            [],
            [],
            CameraThumbnail.FromRgbBytes(256, 144, new byte[256 * 144 * 3]));

        var obsBatch = new ObservationBatch(batchId, ["cam_front"], [sample], 1, 0, CaptureStopReason.None);
        return SurveyBatchArtifact.FromObservationBatch(contractId, obsBatch);
    }

    [Fact]
    public void CrashDuringEachOffloadPhaseRecoversExactlyAccordingToAuthoritativeManifest()
    {
        var store = new BatchCustodyStore();
        var batch = CreateBatch("b_01");
        Assert.Equal(CustodyOperationStatus.Executed, store.RegisterBufferedBatch(batch, 10.0).Status);

        // Crash during Buffered -> stays Buffered.
        var snapBuffered = JsonSerializer.Deserialize<CustodyStoreSnapshot>(JsonSerializer.Serialize(store.ExportSnapshot()))!;
        var recoveredBuffered = BatchCustodyStore.RecoverAfterCrash(snapBuffered);
        Assert.Equal(BatchCustodyStage.Buffered, Assert.Single(recoveredBuffered.Entries).Stage);
        Assert.Empty(recoveredBuffered.AuthoritativeManifest.CommittedBatches);

        // BeginOffload -> stage is Offloading (temporary chunk exists, manifest not committed yet).
        Assert.Equal(CustodyOperationStatus.Executed, store.BeginOffload("b_01", 20.0).Status);
        Assert.Equal(BatchCustodyStage.Offloading, Assert.Single(store.Entries).Stage);
        var snapMidCrash = JsonSerializer.Deserialize<CustodyStoreSnapshot>(JsonSerializer.Serialize(store.ExportSnapshot()))!;
        var recoveredMidCrash = BatchCustodyStore.RecoverAfterCrash(snapMidCrash);
        Assert.Equal(BatchCustodyStage.Buffered, Assert.Single(recoveredMidCrash.Entries).Stage);
        Assert.Empty(recoveredMidCrash.AuthoritativeManifest.CommittedBatches);

        // CommitOffload -> authoritative manifest committed -> Protected across crash & reload.
        Assert.Equal(CustodyOperationStatus.Executed, store.CommitOffload("b_01", 30.0).Status);
        var snapCommitted = JsonSerializer.Deserialize<CustodyStoreSnapshot>(JsonSerializer.Serialize(store.ExportSnapshot()))!;
        var recoveredCommitted = BatchCustodyStore.RecoverAfterCrash(snapCommitted);
        Assert.Equal(BatchCustodyStage.Protected, Assert.Single(recoveredCommitted.Entries).Stage);
        Assert.Single(recoveredCommitted.AuthoritativeManifest.CommittedBatches);

        // Idempotent offload by batchId produces no duplicate manifest entry.
        var idempotent = recoveredCommitted.OffloadAtomic("b_01", 40.0);
        Assert.Equal(CustodyOperationStatus.IdempotentNoOp, idempotent.Status);
        Assert.Single(recoveredCommitted.AuthoritativeManifest.CommittedBatches);
    }

    [Fact]
    public void ChecksumMismatchAndVehicleLossEnforceIntegrityBoundaries()
    {
        var store = new BatchCustodyStore();
        Assert.Equal(CustodyOperationStatus.Executed, store.RegisterBufferedBatch(CreateBatch("b_safe"), 1.0).Status);
        Assert.Equal(CustodyOperationStatus.Executed, store.OffloadAtomic("b_safe", 2.0).Status);

        var corrupted = store.RegisterBufferedBatch(CreateBatch("b_pending"), 3.0);
        Assert.True(corrupted.Succeeded);

        var badChunk = store.BeginOffload("b_pending", 4.0, overrideChunkJson: "{\"corrupt\":true}");
        Assert.False(badChunk.Succeeded);
        Assert.Equal(CustodyOperationStatus.ChecksumMismatch, badChunk.Status);
        Assert.Equal(BatchCustodyStage.Buffered, store.Entries.Single(e => e.BatchId == "b_pending").Stage);

        // Vehicle destroyed loses Buffered + uncommitted Offloading, leaves Protected intact.
        Assert.Equal(1, store.HandleVehicleLoss(5.0));
        Assert.Equal(BatchCustodyStage.Protected, store.Entries.Single(e => e.BatchId == "b_safe").Stage);
        Assert.Equal(BatchCustodyStage.Lost, store.Entries.Single(e => e.BatchId == "b_pending").Stage);
    }
}

