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

public sealed class SimplifiedOfficeModesTests
{
    private static ContractDefinition LoadContract(string fixture) =>
        ContractDefinition.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture)), fixture);

    private static (ContractStateMachine Contract, BatchCustodyStore Store, SurveyBatchArtifact Artifact) PrepareProtectedBatch(
        ContractDefinition def,
        string batchId)
    {
        var contract = new ContractStateMachine(def);
        var store = new BatchCustodyStore();
        Assert.True(contract.Accept(0.0).Succeeded);

        var pose1 = new EstimatedBodyPose(10.0, new Vector3d(500001.0, 7400001.0, 101.5), Quaterniond.Identity);
        var pose2 = new EstimatedBodyPose(20.0, new Vector3d(500003.0, 7400001.0, 101.5), Quaterniond.Identity);
        var quality = new CameraQualityMetrics(0.002, 0.2, 0.005, 0.0, 0.5, false, false, false, false, true);
        var thumb = CameraThumbnail.FromRgbBytes(
            CameraThumbnail.ExpectedWidth,
            CameraThumbnail.ExpectedHeight,
            new byte[CameraThumbnail.ExpectedWidth * CameraThumbnail.ExpectedHeight * 3]);

        var s1 = new CameraObservationSample(
            "s1", "cam_front", 0, 10.0, pose1,
            CameraMountEstimate.ForwardFacing, CameraIntrinsics.Initial,
            quality, [], [new PrivacyMaskRegion("plate_1", 10, 10, 40, 20, false)], thumb);
        var s2 = new CameraObservationSample(
            "s2", "cam_front", 1, 20.0, pose2,
            CameraMountEstimate.ForwardFacing, CameraIntrinsics.Initial,
            quality, [], [new PrivacyMaskRegion("face_2", 12, 12, 32, 32, false)], thumb);

        var features = def.RequiredFeatureIds
            .Select((id, i) => new TriangulatedFeatureEntry(
                id, "facade", TriangulationStatus.Resolved, FeatureReconstructionMethod.TwoRayTriangulation,
                GeometryFailureReason.None, new Vector3d(500000.5 + i * 0.1, 7400000, 100), 12.0, null, 2, true, ["s1", "s2"]))
            .ToArray();

        var artifact = new SurveyBatchArtifact(
            1, batchId, def.Id, 5.0, 25.0, 2, 0, CaptureStopReason.None,
            ["cam_front"],
            [SerializableCameraSample.FromDomain(s1), SerializableCameraSample.FromDomain(s2)],
            [
                new SampleFootprintObservation("s1", "commerce_facade_planes", new Bounds2DEnu(500000.0, 7400000.0, 500002.0, 7400002.0)),
                new SampleFootprintObservation("s2", "commerce_facade_planes", new Bounds2DEnu(500002.0, 7400000.0, 500004.0, 7400002.0)),
            ],
            features,
            []);

        Assert.True(contract.RecordPendingBatch(25.0, batchId).Succeeded);
        Assert.True(store.RegisterBufferedBatch(artifact, 25.0).Succeeded);
        Assert.True(store.OffloadAtomic(batchId, 30.0).Succeeded);
        Assert.True(contract.ProtectBatchAtUnloadPoint(30.0, batchId).Succeeded);
        return (contract, store, artifact);
    }

    [Fact]
    public void PreviewsApplyBasicBatchDurationProcessingSkillReductionAndArcadeFivePercentFeeRule()
    {
        var commercialDef = LoadContract("facades.json") with { Calm = false };
        var (contract, store, _) = PrepareProtectedBatch(commercialDef, "b1");

        var ledger = new CareerLedger(500);
        var arcadeRank0 = SimplifiedOfficeWorkstation.BuildCostAndTimePreview(contract, store, ledger, ProcessingMode.Arcade, processingSkillRank: 0);
        var arcadeRank2 = SimplifiedOfficeWorkstation.BuildCostAndTimePreview(contract, store, ledger, ProcessingMode.Arcade, processingSkillRank: 2);
        var techRank0 = SimplifiedOfficeWorkstation.BuildCostAndTimePreview(contract, store, ledger, ProcessingMode.Technical, processingSkillRank: 0);

        Assert.Equal(600.0, arcadeRank0.DurationSeconds, 6);
        Assert.Equal(480.0, arcadeRank2.DurationSeconds, 6); // -20%
        Assert.Equal((int)Math.Round(commercialDef.Reward.BaseCoords * 0.05, MidpointRounding.AwayFromZero), arcadeRank0.BaseServiceFeeCoords);
        Assert.Equal(arcadeRank0.BaseServiceFeeCoords, arcadeRank0.EffectiveServiceFeeCoords);
        Assert.False(arcadeRank0.LowBalanceSubsidized);
        Assert.Equal("office.mode.arcade.preview", arcadeRank0.PreviewKey);

        Assert.Equal(600.0, techRank0.DurationSeconds, 6);
        Assert.Equal(0, techRank0.EffectiveServiceFeeCoords);
        Assert.Equal("office.mode.technical.preview", techRank0.PreviewKey);

        // Calm contract + balance < 50 waives Arcade fee
        var calmDef = LoadContract("facades.json") with { Calm = true };
        var (calmContract, calmStore, _) = PrepareProtectedBatch(calmDef, "b_calm");
        var lowLedger = new CareerLedger(30);
        var subsidizedPreview = SimplifiedOfficeWorkstation.BuildCostAndTimePreview(calmContract, calmStore, lowLedger, ProcessingMode.Arcade);
        Assert.True(subsidizedPreview.LowBalanceSubsidized);
        Assert.True(subsidizedPreview.BaseServiceFeeCoords > 0);
        Assert.Equal(0, subsidizedPreview.EffectiveServiceFeeCoords);
    }

    [Fact]
    public void ArcadeAndTechnicalProduceIdenticalQualityReportOnMatchingSelections()
    {
        var def = LoadContract("facades.json") with { Calm = false };
        var surface = new SurveySurfaceReference("commerce_facade_planes", new Bounds2DEnu(500000, 7400000, 500004, 7400002));
        var reference = new EvaluationReference(def.Id, [surface], def.RequiredFeatureIds);

        // Run Arcade workflow
        var (contractArcade, storeArcade, _) = PrepareProtectedBatch(def, "b1");
        var ledgerArcade = new CareerLedger(500);
        var queueArcade = new OfficeProcessingQueue();
        var arcadeRecipe = SimplifiedOfficeWorkstation.BuildArcadeAssistedRecipe("rec_arcade", contractArcade, storeArcade, ledgerArcade);
        Assert.True(queueArcade.StartOrResumeJob("job_arcade", contractArcade, storeArcade, ledgerArcade, arcadeRecipe, 35.0).Succeeded);
        var arcadeDone = queueArcade.AdvanceJob(contractArcade, storeArcade, arcadeRecipe.CalendarDurationSeconds);
        Assert.True(arcadeDone.Succeeded);
        var arcadeReport = QualityEvaluator.Evaluate(def, reference, arcadeDone.Product!.Submission);

        // Run Technical manual workflow with the same selections, features, and confirmed privacy masks
        var (contractTech, storeTech, _) = PrepareProtectedBatch(def, "b1");
        var ledgerTech = new CareerLedger(500);
        var queueTech = new OfficeProcessingQueue();
        var masks = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["s1"] = ["plate_1"],
            ["s2"] = ["face_2"],
        };
        var techRecipe = SimplifiedOfficeWorkstation.BuildTechnicalManualRecipe(
            "rec_tech", contractTech, storeTech, ledgerTech,
            ["b1"], ["s1", "s2"], def.RequiredFeatureIds, masks);
        Assert.True(queueTech.StartOrResumeJob("job_tech", contractTech, storeTech, ledgerTech, techRecipe, 35.0).Succeeded);
        var techDone = queueTech.AdvanceJob(contractTech, storeTech, techRecipe.CalendarDurationSeconds);
        Assert.True(techDone.Succeeded);
        var techReport = QualityEvaluator.Evaluate(def, reference, techDone.Product!.Submission);

        // Technical had 0 fee; Arcade charged 5% base fee
        Assert.Equal(500 - arcadeRecipe.ServiceFeeCoords, ledgerArcade.BalanceCoords);
        Assert.Equal(500, ledgerTech.BalanceCoords);

        // Both modes must achieve exact metric parity on the same selections
        Assert.True(arcadeReport.Passed);
        Assert.Equal(arcadeReport.Passed, techReport.Passed);
        Assert.Equal(arcadeReport.UniqueSampleCount, techReport.UniqueSampleCount);
        Assert.Equal(arcadeReport.ValidSampleCount, techReport.ValidSampleCount);
        Assert.Equal(arcadeReport.MatchedFeatureCount, techReport.MatchedFeatureCount);
        Assert.Equal(arcadeReport.MatchedCheckpointCount, techReport.MatchedCheckpointCount);
        Assert.Equal(arcadeReport.FailureReasonKeys, techReport.FailureReasonKeys);
        Assert.Equal(arcadeReport.Metrics.Count, techReport.Metrics.Count);
        for (var i = 0; i < arcadeReport.Metrics.Count; i++)
        {
            Assert.Equal(arcadeReport.Metrics[i].Metric, techReport.Metrics[i].Metric);
            Assert.Equal(arcadeReport.Metrics[i].Status, techReport.Metrics[i].Status);
            Assert.Equal(arcadeReport.Metrics[i].Value, techReport.Metrics[i].Value);
            Assert.Equal(arcadeReport.Metrics[i].BooleanValue, techReport.Metrics[i].BooleanValue);
        }

        var arcadeQuote = DeliveryQuoteCalculator.Calculate(def, arcadeReport, 0.0, 400.0);
        var techQuote = DeliveryQuoteCalculator.Calculate(def, techReport, 0.0, 400.0);
        Assert.Equal(arcadeQuote.QualityScore, techQuote.QualityScore, 6);
        Assert.Equal(arcadeQuote.NetCoords, techQuote.NetCoords);
    }
}
