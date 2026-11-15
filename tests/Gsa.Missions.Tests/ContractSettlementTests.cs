using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Camera;
using Gsa.Missions.Contracts;
using Gsa.Missions.Economy;
using Gsa.Missions.Persistence;
using Gsa.Missions.Quality;
using Xunit;

namespace Gsa.Missions.Tests;

public sealed class ContractSettlementTests
{
    private static ContractDefinition LoadContract(string fixture) =>
        ContractDefinition.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture)), fixture);

    private static CameraObservationSample MakeSample(string sampleId, double timestamp, bool privacyUnredacted = false)
    {
        var pose = new EstimatedBodyPose(timestamp, new Vector3d(500000, 7400000, 101.5), Quaterniond.Identity);
        var quality = new CameraQualityMetrics(0.002, 0.2, 0.005, 0.0, 0.5, false, false, false, false, true);
        var masks = new[] { new PrivacyMaskRegion("plate_01", 10, 10, 40, 20, Applied: !privacyUnredacted) };
        var thumb = CameraThumbnail.FromRgbBytes(CameraThumbnail.ExpectedWidth, CameraThumbnail.ExpectedHeight, new byte[CameraThumbnail.ExpectedWidth * CameraThumbnail.ExpectedHeight * 3]);
        return new CameraObservationSample(
            sampleId, "cam_front", 0, timestamp, pose,
            CameraMountEstimate.ForwardFacing, CameraIntrinsics.Initial,
            quality, [], masks, thumb);
    }

    private static TriangulatedFeatureEntry ResolvedFeature(string targetId, Vector3d pos) =>
        new(targetId, "facade", TriangulationStatus.Resolved, FeatureReconstructionMethod.TwoRayTriangulation,
            GeometryFailureReason.None, pos, 12.0, null, 2, Confirmed: true, ["s1", "s2"]);

    private static SurveyBatchArtifact MakeArtifact(
        string batchId,
        string contractId,
        double startTime,
        double endTime,
        IReadOnlyList<CameraObservationSample> samples,
        IReadOnlyList<SampleFootprintObservation> footprints,
        IReadOnlyList<TriangulatedFeatureEntry> features,
        IReadOnlyList<SubmittedCheckpointEstimate>? checkpoints = null) =>
        new(
            1,
            batchId,
            contractId,
            startTime,
            endTime,
            samples.Count,
            0,
            CaptureStopReason.None,
            ["cam_front"],
            samples.Select(SerializableCameraSample.FromDomain).ToArray(),
            footprints,
            features,
            checkpoints ?? []);

    [Fact]
    public void ReworkMergesUniqueProtectedBatchesSurvivesVehicleLossAndSettlesIdempotently()
    {
        var contractDef = LoadContract("facades.json");
        var surface = new SurveySurfaceReference("commerce_facade_planes", new Bounds2DEnu(500000, 7400000, 500004, 7400002));
        var reference = new EvaluationReference(contractDef.Id, [surface], contractDef.RequiredFeatureIds);

        var contract = new ContractStateMachine(contractDef);
        var store = new BatchCustodyStore();
        var ledger = new CareerLedger(250);
        var progression = new CareerProgressionState();

        Assert.True(contract.Accept(10.0).Succeeded);

        // Batch 1 (protected): covers left half [500000..500002] and features 0..9
        var half1Features = contractDef.RequiredFeatureIds.Take(10)
            .Select((id, i) => ResolvedFeature(id, new Vector3d(500000.5 + i * 0.1, 7400000, 100))).ToArray();
        var b1 = MakeArtifact(
            "b1", contractDef.Id, 10.0, 20.0,
            [MakeSample("s1", 15.0)],
            [new SampleFootprintObservation("s1", "commerce_facade_planes", new Bounds2DEnu(500000.05, 7400000.05, 500001.95, 7400001.95))],
            half1Features);

        Assert.True(contract.RecordPendingBatch(20.0, "b1").Succeeded);
        Assert.True(store.RegisterBufferedBatch(b1, 20.0).Succeeded);
        Assert.True(store.OffloadAtomic("b1", 25.0).Succeeded);
        Assert.True(contract.ProtectBatchAtUnloadPoint(25.0, "b1").Succeeded);

        var firstQc = ContractSettlementCoordinator.EvaluateProtectedBatchesAndUpdateQc(contract, store, reference, 30.0);
        Assert.False(firstQc.Report.Passed);
        Assert.Null(firstQc.Quote);
        Assert.Equal(ContractState.Retrabalho, contract.State);

        // Batch 2 (buffered ONLY): vehicle destroyed -> discarded/lost, must not erase b1 or contribute to QC
        var b2Lost = MakeArtifact(
            "b2_lost", contractDef.Id, 35.0, 40.0,
            [MakeSample("s_lost", 38.0)],
            [new SampleFootprintObservation("s_lost", "commerce_facade_planes", new Bounds2DEnu(500002.05, 7400000.05, 500003.95, 7400001.95))],
            contractDef.RequiredFeatureIds.Skip(10).Select((id, i) => ResolvedFeature(id, new Vector3d(500002.5 + i * 0.1, 7400000, 100))).ToArray());

        Assert.True(contract.RecordPendingBatch(40.0, "b2_lost").Succeeded);
        Assert.True(store.RegisterBufferedBatch(b2Lost, 40.0).Succeeded);
        Assert.Equal(1, store.HandleVehicleLoss(45.0));
        Assert.True(contract.HandleFieldIncident(45.0, FieldIncidentKind.VehicleDestroyed).Succeeded);
        Assert.Single(contract.ProtectedBatches);

        // Batch 3 (re-collected + offloaded): intentionally repeats s1 AND adds s2 + remaining features 10..19
        var half2Features = contractDef.RequiredFeatureIds.Skip(10)
            .Select((id, i) => ResolvedFeature(id, new Vector3d(500002.5 + i * 0.1, 7400000, 100))).ToArray();
        var b3 = MakeArtifact(
            "b3", contractDef.Id, 50.0, 60.0,
            [MakeSample("s1", 15.0), MakeSample("s2", 55.0)],
            [
                new SampleFootprintObservation("s1", "commerce_facade_planes", new Bounds2DEnu(500000.05, 7400000.05, 500001.95, 7400001.95)),
                new SampleFootprintObservation("s2", "commerce_facade_planes", new Bounds2DEnu(500002.05, 7400000.05, 500003.95, 7400001.95)),
            ],
            half2Features);

        Assert.True(contract.RecordPendingBatch(60.0, "b3").Succeeded);
        Assert.True(store.RegisterBufferedBatch(b3, 60.0).Succeeded);
        Assert.True(store.OffloadAtomic("b3", 65.0).Succeeded);
        Assert.True(contract.ProtectBatchAtUnloadPoint(65.0, "b3").Succeeded);

        var secondQc = ContractSettlementCoordinator.EvaluateProtectedBatchesAndUpdateQc(contract, store, reference, 70.0);
        Assert.True(secondQc.Report.Passed);
        Assert.Equal(2, secondQc.Report.UniqueSampleCount); // s1 deduplicated across b1 + b3; s_lost excluded
        Assert.Equal(ContractState.ProntoEntregar, contract.State);
        Assert.NotNull(secondQc.Quote);
        Assert.True(secondQc.Quote!.Eligible);

        var settlement1 = ContractSettlementCoordinator.SettleDelivery(contract, ledger, progression, secondQc.Report, 80.0);
        Assert.True(settlement1.Succeeded);
        Assert.False(settlement1.WasIdempotentReplay);
        Assert.Equal(ContractState.Entregue, contract.State);

        var expectedBalance = 250 + settlement1.Quote.NetCoords;
        Assert.Equal(expectedBalance, ledger.BalanceCoords);
        Assert.Equal(settlement1.Quote.XpAwarded, progression.TotalXp);
        Assert.Equal(settlement1.Quote.ReputationDelta, progression.GetClientReputation(contractDef.ClientId));
        Assert.True(progression.HasStoryUnlock("HIST-02"));

        // Repeat delivery (including past expiry timestamp) must remain an idempotent no-op with zero double credit
        var replay = ContractSettlementCoordinator.SettleDelivery(contract, ledger, progression, secondQc.Report, 50000.0);
        Assert.True(replay.Succeeded);
        Assert.True(replay.WasIdempotentReplay);
        Assert.Equal(expectedBalance, ledger.BalanceCoords);
        Assert.Equal(settlement1.Quote.XpAwarded, progression.TotalXp);
        Assert.Equal(settlement1.Quote.ReputationDelta, progression.GetClientReputation(contractDef.ClientId));
    }

    [Fact]
    public void PartialDeliveryHonorsDeliverablesAndPrivacyFailureBlocksEvenPartial()
    {
        var contractDef = LoadContract("inventory.json");
        var surface = new SurveySurfaceReference("commerce_street_facades", new Bounds2DEnu(500000, 7400000, 500002, 7400002));
        var cps = new[]
        {
            new ControlCheckpointReference("cp1", new Vector3d(0, 0, 0)),
            new ControlCheckpointReference("cp2", new Vector3d(10, 0, 0)),
            new ControlCheckpointReference("cp3", new Vector3d(0, 10, 0)),
        };
        var reference = new EvaluationReference(contractDef.Id, [surface], contractDef.RequiredFeatureIds, cps);

        var blockA = contractDef.RequiredFeatureIds.Take(10)
            .Select((id, i) => ResolvedFeature(id, new Vector3d(i, 0, 0))).ToArray();
        var estCps = cps.Select(c => new SubmittedCheckpointEstimate(c.CheckpointId, c.TruePositionEnu)).ToArray();

        // Case 1: Block A complete, privacy redacted -> partial allowed after rework failure
        var contract = new ContractStateMachine(contractDef);
        var store = new BatchCustodyStore();
        var ledger = new CareerLedger(100);
        var progression = new CareerProgressionState();

        Assert.True(contract.Accept(0.0).Succeeded);
        var batchOk = MakeArtifact("b_ok", contractDef.Id, 1.0, 5.0, [MakeSample("s1", 2.0)], [], blockA, estCps);
        Assert.True(contract.RecordPendingBatch(5.0, "b_ok").Succeeded);
        Assert.True(store.RegisterBufferedBatch(batchOk, 5.0).Succeeded);
        Assert.True(store.OffloadAtomic("b_ok", 6.0).Succeeded);
        Assert.True(contract.ProtectBatchAtUnloadPoint(6.0, "b_ok").Succeeded);

        var qc = ContractSettlementCoordinator.EvaluateProtectedBatchesAndUpdateQc(contract, store, reference, 10.0);
        Assert.False(qc.Report.Passed);
        Assert.Equal(["block_a"], qc.Report.PassedDeliverableIds);
        Assert.Equal(ContractState.Retrabalho, contract.State);
        Assert.True(contract.CanDeliverPartial);

        var partialSettlement = ContractSettlementCoordinator.SettleDelivery(contract, ledger, progression, qc.Report, 15.0, DeliveryMode.Partial);
        Assert.True(partialSettlement.Succeeded);
        Assert.True(partialSettlement.Quote.IsPartial);
        Assert.Equal(ContractState.EntregueParcial, contract.State);
        Assert.Equal(100 + partialSettlement.Quote.NetCoords, ledger.BalanceCoords);
        Assert.Empty(progression.UnlockedStoryNodes);

        // Case 2: Privacy unredacted must block partial delivery too
        var badPrivacyContract = new ContractStateMachine(contractDef);
        var badPrivacyStore = new BatchCustodyStore();
        Assert.True(badPrivacyContract.Accept(0.0).Succeeded);
        var batchPrivacyBad = MakeArtifact("b_bad", contractDef.Id, 1.0, 5.0, [MakeSample("s_leak", 2.0, privacyUnredacted: true)], [], blockA, estCps);
        Assert.True(badPrivacyContract.RecordPendingBatch(5.0, "b_bad").Succeeded);
        Assert.True(badPrivacyStore.RegisterBufferedBatch(batchPrivacyBad, 5.0).Succeeded);
        Assert.True(badPrivacyStore.OffloadAtomic("b_bad", 6.0).Succeeded);
        Assert.True(badPrivacyContract.ProtectBatchAtUnloadPoint(6.0, "b_bad").Succeeded);

        var badQc = ContractSettlementCoordinator.EvaluateProtectedBatchesAndUpdateQc(badPrivacyContract, badPrivacyStore, reference, 10.0);
        var rejected = ContractSettlementCoordinator.SettleDelivery(badPrivacyContract, new CareerLedger(100), new CareerProgressionState(), badQc.Report, 15.0, DeliveryMode.Partial);
        Assert.False(rejected.Succeeded);
        Assert.Equal("quote.rejected.compliance", rejected.ReasonKey);
        Assert.Equal(ContractState.Retrabalho, badPrivacyContract.State);
    }

    [Fact]
    public void CareerProgressionNeverDoubleCountsClientAndRegionReputationAndClampsAtZero()
    {
        var baseDef = LoadContract("facades.json");
        var clientA = baseDef with { Id = "c_a", ClientId = "client_alpha", RegionId = "old_center" };
        var clientB = baseDef with { Id = "c_b", ClientId = "client_beta", RegionId = "old_center" };
        var prog = new CareerProgressionState();

        var quoteA = new PaymentQuote("c_a", true, false, false, 4, 0.9, 1.0, 0.0, 0.0, 400, 400, 400, 100, 2, [], []);
        var quoteB = new PaymentQuote("c_b", true, false, false, 4, 0.9, 1.0, 0.0, 0.0, 400, 400, 400, 100, 2, [], []);

        Assert.True(prog.ApplySettlement(clientA, quoteA));
        Assert.True(prog.ApplySettlement(clientB, quoteB));
        Assert.False(prog.ApplySettlement(clientA, quoteA)); // idempotent

        // Region old_center gained 4 total; global must sum clients (2 + 2 = 4), NOT client + region (8).
        Assert.Equal(4, prog.GetRegionReputation("old_center"));
        Assert.Equal(2, prog.GetClientReputation("client_alpha"));
        Assert.Equal(2, prog.GetClientReputation("client_beta"));
        Assert.Equal(4, prog.GlobalReputation);

        // Negative reputation penalty clamps at 0 per entity
        var clientC = baseDef with { Id = "c_c", ClientId = "client_gamma", RegionId = "north_port" };
        var expiredQuote = new PaymentQuote("c_c", false, false, true, 0, 0.0, 0.0, 0.0, 0.0, 400, 0, 0, 0, -1, [], [], "quote.rejected.expired");
        Assert.True(prog.ApplySettlement(clientC, expiredQuote));
        Assert.Equal(0, prog.GetClientReputation("client_gamma"));
        Assert.Equal(0, prog.GetRegionReputation("north_port"));
    }
}

