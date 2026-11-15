using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Camera;
using Gsa.Missions.Contracts;
using Gsa.Missions.Economy;
using Gsa.Missions.Office;
using Gsa.Missions.Persistence;
using Gsa.Missions.Quality;
using Xunit;

namespace Gsa.Missions.Tests;

public sealed class OfficeProcessingTests
{
    private static ContractDefinition LoadContract(string fixture) =>
        ContractDefinition.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture)), fixture);

    private static CameraObservationSample MakeSample(string sampleId, double timestamp)
    {
        var pose = new EstimatedBodyPose(timestamp, new Vector3d(500000, 7400000, 101.5), Quaterniond.Identity);
        var quality = new CameraQualityMetrics(0.002, 0.2, 0.005, 0.0, 0.5, false, false, false, false, true);
        var thumb = CameraThumbnail.FromRgbBytes(CameraThumbnail.ExpectedWidth, CameraThumbnail.ExpectedHeight, new byte[CameraThumbnail.ExpectedWidth * CameraThumbnail.ExpectedHeight * 3]);
        return new CameraObservationSample(
            sampleId, "cam_front", 0, timestamp, pose,
            CameraMountEstimate.ForwardFacing, CameraIntrinsics.Initial,
            quality, [], [new PrivacyMaskRegion("p1", 10, 10, 40, 20, Applied: false)], thumb);
    }

    private static SurveyBatchArtifact MakeArtifact(ContractDefinition def, string batchId)
    {
        var features = def.RequiredFeatureIds
            .Select((id, i) => new TriangulatedFeatureEntry(id, "facade", TriangulationStatus.Resolved, FeatureReconstructionMethod.TwoRayTriangulation, GeometryFailureReason.None, new Vector3d(500000.5 + i * 0.1, 7400000, 100), 12.0, null, 2, true, ["s1", "s2"]))
            .ToArray();

        return new SurveyBatchArtifact(
            1, batchId, def.Id, 10.0, 30.0, 2, 0, CaptureStopReason.None,
            ["cam_front"],
            [SerializableCameraSample.FromDomain(MakeSample("s1", 15.0)), SerializableCameraSample.FromDomain(MakeSample("s2", 25.0))],
            [
                new SampleFootprintObservation("s1", "commerce_facade_planes", new Bounds2DEnu(500000.05, 7400000.05, 500001.95, 7400001.95)),
                new SampleFootprintObservation("s2", "commerce_facade_planes", new Bounds2DEnu(500002.05, 7400000.05, 500003.95, 7400001.95)),
            ],
            features,
            []);
    }

    [Fact]
    public void CancelAndReprocessPreserveRawHashesIncrementRevisionAndNeverDoubleCharge()
    {
        var def = LoadContract("facades.json");
        var contract = new ContractStateMachine(def);
        var store = new BatchCustodyStore();
        var ledger = new CareerLedger(200);
        var queue = new OfficeProcessingQueue();

        Assert.True(contract.Accept(0.0).Succeeded);
        var artifact = MakeArtifact(def, "b1");
        Assert.True(contract.RecordPendingBatch(20.0, "b1").Succeeded);
        Assert.True(store.RegisterBufferedBatch(artifact, 20.0).Succeeded);
        Assert.True(store.OffloadAtomic("b1", 25.0).Succeeded);
        Assert.True(contract.ProtectBatchAtUnloadPoint(25.0, "b1").Succeeded);

        var initialRawSha = store.AuthoritativeManifest.CommittedBatches.Single().PayloadSha256;

        // Recipe selecting only s1 -> cancelled mid-way
        var recipe1 = new ProcessingRecipe(
            1, "rec_1", def.Id, ProcessingMode.Technical,
            ["b1"], ["s1"], def.RequiredFeatureIds, new Dictionary<string, IReadOnlyList<string>>(),
            CalendarDurationSeconds: 120.0, ServiceFeeCoords: 30);

        var quote = queue.PreviewJob(contract, store, recipe1, 30.0);
        Assert.Equal(1, quote.SelectedSampleCount);
        Assert.False(quote.WouldExpireBeforeCompletion);

        var start1 = queue.StartOrResumeJob("job_1", contract, store, ledger, recipe1, 30.0);
        Assert.True(start1.Succeeded);
        Assert.Equal(170, ledger.BalanceCoords);
        Assert.Equal(ContractState.Processando, contract.State);

        // Resuming same job_1 or queuing a second distinct job while running
        var resumeSame = queue.StartOrResumeJob("job_1", contract, store, ledger, recipe1, 40.0);
        Assert.Equal(ProcessingCommandStatus.IdempotentNoOp, resumeSame.Status);
        Assert.Equal(170, ledger.BalanceCoords); // no double charge

        var busyOther = queue.StartOrResumeJob("job_other", contract, store, ledger, recipe1, 40.0);
        Assert.Equal(ProcessingCommandStatus.RejectedBusy, busyOther.Status);

        var step1 = queue.AdvanceJob(contract, store, 60.0);
        Assert.True(step1.Succeeded);
        Assert.Equal(0.5, step1.Job!.ProgressFraction, 6);

        var cancelResult = queue.CancelActiveJob(contract, 95.0);
        Assert.True(cancelResult.Succeeded);
        Assert.Equal(ProcessingJobState.Cancelled, cancelResult.Job!.State);
        Assert.Null(cancelResult.Job!.Product);
        Assert.Equal(ContractState.Coletando, contract.State);
        Assert.Empty(queue.CompletedProducts);

        // Reprocess with Arcade recipe including both s1 and s2 -> Revision 1 -> then fail CQ & reprocess Revision 2
        var recipe2 = new ProcessingRecipe(
            1, "rec_2", def.Id, ProcessingMode.Arcade,
            ["b1"], ["s1", "s2"], def.RequiredFeatureIds, new Dictionary<string, IReadOnlyList<string>>(),
            CalendarDurationSeconds: 100.0, ServiceFeeCoords: 20);

        var start2 = queue.StartOrResumeJob("job_2", contract, store, ledger, recipe2, 100.0);
        Assert.True(start2.Succeeded);
        Assert.Equal(150, ledger.BalanceCoords);

        var done2 = queue.AdvanceJob(contract, store, 100.0);
        Assert.True(done2.Succeeded);
        Assert.Equal(ProcessingJobState.Completed, done2.Job!.State);
        Assert.NotNull(done2.Product);
        Assert.Equal(1, done2.Product!.Revision);
        Assert.Equal(2, done2.Product!.Submission.Samples.Count);
        Assert.True(done2.Product!.PrivacyManifest.IsCompliant);
        Assert.Equal(ContractState.EmCQ, contract.State);

        // Fail CQ to enter Retrabalho and reprocess -> Revision 2 with preserved raw SHA
        Assert.True(contract.RecordQualityEvaluation(210.0, false, null, "cq.failure.manual_review").Succeeded);
        var recipe3 = recipe2 with { RecipeId = "rec_3", ServiceFeeCoords = 10 };
        var start3 = queue.StartOrResumeJob("job_3", contract, store, ledger, recipe3, 220.0);
        Assert.True(start3.Succeeded);
        var done3 = queue.AdvanceJob(contract, store, 100.0);
        Assert.Equal(2, done3.Product!.Revision);
        Assert.NotEqual(done2.Product!.RecipeHash, done3.Product!.RecipeHash);

        // Raw custody manifest hash unchanged across processing/cancellation/reprocessing
        Assert.Equal(initialRawSha, store.AuthoritativeManifest.CommittedBatches.Single().PayloadSha256);
    }

    [Fact]
    public void AdvancingOfficeCalendarPastExpirationFailsJobWithoutDeliverableProduct()
    {
        var def = LoadContract("facades.json");
        var contract = new ContractStateMachine(def);
        var store = new BatchCustodyStore();
        var ledger = new CareerLedger(200);
        var queue = new OfficeProcessingQueue();

        Assert.True(contract.Accept(0.0).Succeeded);
        Assert.True(contract.RecordPendingBatch(10.0, "b1").Succeeded);
        Assert.True(store.RegisterBufferedBatch(MakeArtifact(def, "b1"), 10.0).Succeeded);
        Assert.True(store.OffloadAtomic("b1", 15.0).Succeeded);
        Assert.True(contract.ProtectBatchAtUnloadPoint(15.0, "b1").Succeeded);

        var nearExpiry = def.Deadline.ExpiresAfterSeconds - 50.0;
        var recipe = new ProcessingRecipe(
            1, "rec_late", def.Id, ProcessingMode.Arcade,
            ["b1"], ["s1", "s2"], def.RequiredFeatureIds, new Dictionary<string, IReadOnlyList<string>>(),
            CalendarDurationSeconds: 120.0, ServiceFeeCoords: 0);

        var quote = queue.PreviewJob(contract, store, recipe, nearExpiry);
        Assert.True(quote.WouldExpireBeforeCompletion);

        Assert.True(queue.StartOrResumeJob("job_late", contract, store, ledger, recipe, nearExpiry).Succeeded);
        var expiredStep = queue.AdvanceJob(contract, store, 120.0);
        Assert.Equal(ProcessingCommandStatus.RejectedExpired, expiredStep.Status);
        Assert.Equal(ProcessingJobState.FailedExpired, expiredStep.Job!.State);
        Assert.Null(expiredStep.Product);
        Assert.Equal(ContractState.Expirado, contract.State);
    }
}

