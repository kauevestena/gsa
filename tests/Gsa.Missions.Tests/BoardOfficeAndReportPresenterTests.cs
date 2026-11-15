using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Camera;
using Gsa.Missions.Contracts;
using Gsa.Missions.Economy;
using Gsa.Missions.Office;
using Gsa.Missions.Persistence;
using Gsa.Missions.Presentation;
using Gsa.Missions.Quality;
using Xunit;

namespace Gsa.Missions.Tests;

public sealed class BoardOfficeAndReportPresenterTests
{
    private static ContractDefinition LoadContract(string fixture) =>
        ContractDefinition.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture)), fixture);

    [Fact]
    public void BoardCardExposesAllRequirementsDeadlinesPenaltiesAndPartialRulesBeforeAcceptance()
    {
        var inventoryDef = LoadContract("inventory.json");
        var cardPt = BoardOfficeAndReportPresenter.BuildContractBoardCard(inventoryDef, "pt-BR");
        var cardEn = BoardOfficeAndReportPresenter.BuildContractBoardCard(inventoryDef, "en-US");

        Assert.Equal(inventoryDef.Id, cardPt.ContractId);
        Assert.Equal(inventoryDef.ClientId, cardPt.ClientId);
        Assert.Equal(inventoryDef.AreaId, cardPt.AreaId);
        Assert.True(cardPt.IsCalm);
        Assert.Equal("board.badge.calm", cardPt.CalmBadgeKey);
        Assert.Equal("6,0", cardPt.FormattedDueHours);
        Assert.Equal("7,5", cardPt.FormattedExpiresHours);
        Assert.Equal("5,0%", cardPt.FormattedLatePenaltyPerHour);
        Assert.Equal("50%", cardPt.FormattedLatePenaltyCap);
        Assert.Equal("5.0%", cardEn.FormattedLatePenaltyPerHour);
        Assert.Equal(5, cardPt.Requirements.Count);
        Assert.Equal("deliverables", cardPt.PartialPolicyMode);
        Assert.Equal(2, cardPt.Deliverables.Count);
    }

    [Fact]
    public void ArcadeAndTechnicalReportsPresentSameEvaluationAndOfficeViewReflectsProgress()
    {
        var def = LoadContract("facades.json");
        var contract = new ContractStateMachine(def);
        var store = new BatchCustodyStore();
        var ledger = new CareerLedger(250);
        var queue = new OfficeProcessingQueue();

        Assert.True(contract.Accept(0.0).Succeeded);
        var pose = new EstimatedBodyPose(10.0, new Vector3d(500000, 7400000, 101.5), Quaterniond.Identity);
        var quality = new CameraQualityMetrics(0.002, 0.2, 0.005, 0.0, 0.5, false, false, false, false, true);
        var thumb = CameraThumbnail.FromRgbBytes(CameraThumbnail.ExpectedWidth, CameraThumbnail.ExpectedHeight, new byte[CameraThumbnail.ExpectedWidth * CameraThumbnail.ExpectedHeight * 3]);
        var sample = new CameraObservationSample(
            "s1", "cam_front", 0, 10.0, pose,
            CameraMountEstimate.ForwardFacing, CameraIntrinsics.Initial,
            quality, [], [new PrivacyMaskRegion("plate_1", 10, 10, 40, 20, false)], thumb);

        var features = def.RequiredFeatureIds
            .Select((id, i) => new TriangulatedFeatureEntry(id, "facade", TriangulationStatus.Resolved, FeatureReconstructionMethod.TwoRayTriangulation, GeometryFailureReason.None, new Vector3d(500000.5 + i * 0.1, 7400000, 100), 12.0, null, 2, true, ["s1"]))
            .ToArray();

        var batch = new SurveyBatchArtifact(
            1, "b1", def.Id, 5.0, 15.0, 1, 0, CaptureStopReason.None,
            ["cam_front"], [SerializableCameraSample.FromDomain(sample)],
            [new SampleFootprintObservation("s1", "commerce_facade_planes", new Bounds2DEnu(500000.05, 7400000.05, 500001.95, 7400001.95))],
            features, []);

        Assert.True(contract.RecordPendingBatch(15.0, "b1").Succeeded);
        Assert.True(store.RegisterBufferedBatch(batch, 15.0).Succeeded);
        Assert.True(store.OffloadAtomic("b1", 20.0).Succeeded);
        Assert.True(contract.ProtectBatchAtUnloadPoint(20.0, "b1").Succeeded);

        // Start technical mode job without confirming plate_1 -> Privacy pending triggers failure & corrective action
        var recipe = new ProcessingRecipe(
            1, "rec_tech", def.Id, ProcessingMode.Technical,
            ["b1"], ["s1"], def.RequiredFeatureIds, new Dictionary<string, IReadOnlyList<string>>(),
            CalendarDurationSeconds: 300.0, ServiceFeeCoords: 25);

        var quotePreview = queue.PreviewJob(contract, store, recipe, 25.0);
        var preView = BoardOfficeAndReportPresenter.BuildOfficeView(quotePreview, null, "en-US");
        Assert.Equal("5.0", preView.FormattedDurationMinutes);

        var start = queue.StartOrResumeJob("job_ui_1", contract, store, ledger, recipe, 25.0);
        Assert.True(start.Succeeded);
        var done = queue.AdvanceJob(contract, store, 300.0);
        Assert.True(done.Succeeded);

        var officeDoneView = BoardOfficeAndReportPresenter.BuildOfficeView(null, done.Job, "en-US");
        Assert.Equal(ProcessingJobState.Completed, officeDoneView.JobState);
        Assert.Equal(1, officeDoneView.PendingPrivacyMasks);

        var surface = new SurveySurfaceReference("commerce_facade_planes", new Bounds2DEnu(500000, 7400000, 500004, 7400002));
        var reference = new EvaluationReference(def.Id, [surface], def.RequiredFeatureIds);
        var report = QualityEvaluator.Evaluate(def, reference, done.Product!.Submission);
        var paymentQuote = DeliveryQuoteCalculator.Calculate(def, report, 0.0, 350.0);

        var arcadeView = BoardOfficeAndReportPresenter.BuildQualityReportView(def, report, paymentQuote, ProcessingMode.Arcade, "pt-BR", done.Product.RecipeHash);
        var techView = BoardOfficeAndReportPresenter.BuildQualityReportView(def, report, paymentQuote, ProcessingMode.Technical, "en-US", done.Product.RecipeHash);

        // Both modes reflect the exact same requirements, statuses, failure causes, and eligibility
        Assert.False(arcadeView.Passed);
        Assert.False(techView.Passed);
        Assert.False(arcadeView.CanDeliverFull);
        Assert.False(techView.CanDeliverFull);
        Assert.Equal(arcadeView.Metrics.Count, techView.Metrics.Count);
        for (var i = 0; i < arcadeView.Metrics.Count; i++)
        {
            Assert.Equal(arcadeView.Metrics[i].Metric, techView.Metrics[i].Metric);
            Assert.Equal(arcadeView.Metrics[i].StatusKey, techView.Metrics[i].StatusKey);
            Assert.Equal(arcadeView.Metrics[i].ProbableCauseKey, techView.Metrics[i].ProbableCauseKey);
        }

        // Technical exposes formatted limit/value/units cleanly per locale
        var covTech = techView.Metrics.Single(m => m.Metric == "area_coverage");
        Assert.Equal("ratio", covTech.Unit);
        Assert.Equal("0.500", covTech.FormattedValue);
        Assert.Equal("0.900", covTech.FormattedLimit);
        Assert.Equal("0.980", covTech.FormattedExcellent);

        var covArcade = arcadeView.Metrics.Single(m => m.Metric == "area_coverage");
        Assert.Equal("0,500", covArcade.FormattedValue);
        Assert.NotEmpty(arcadeView.CorrectiveActionCards);
    }
}

