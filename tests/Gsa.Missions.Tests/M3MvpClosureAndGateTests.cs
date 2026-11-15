using System.Text.Json;
using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Camera;
using Gsa.Missions.Activities;
using Gsa.Missions.Campaign;
using Gsa.Missions.Contracts;
using Gsa.Missions.Diagnostics;
using Gsa.Missions.Economy;
using Gsa.Missions.Office;
using Gsa.Missions.Persistence;
using Gsa.Missions.Presentation;
using Gsa.Missions.Quality;
using Xunit;

namespace Gsa.Missions.Tests;

public sealed class M3MvpClosureAndGateTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Gsa.Domain.slnf")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    [Fact]
    public void GeopediaUnlocksAuditBilingualParityAnd150PercentScaleFit()
    {
        var root = FindRepoRoot();
        var enPoPath = Path.Combine(root, "game", "localization", "en_US.po");
        var ptPoPath = Path.Combine(root, "game", "localization", "pt_BR.po");
        var glossaryPath = Path.Combine(root, "content", "localization", "glossary.csv");

        var audit = GeopediaCatalogCoordinator.AuditBilingualCatalog(enPoPath, ptPoPath, glossaryPath);
        Assert.True(audit.Passed, $"Missing EN: [{string.Join(", ", audit.MissingInEnUs)}], Missing PT: [{string.Join(", ", audit.MissingInPtBr)}], Missing Terms: [{string.Join(", ", audit.MissingGlossaryTerms)}]");
        Assert.Empty(audit.EmptyTranslations);

        var enTranslations = GeopediaCatalogCoordinator.ParseSimplePoTranslations(enPoPath);
        var ptTranslations = GeopediaCatalogCoordinator.ParseSimplePoTranslations(ptPoPath);

        var geo = new GeopediaCatalogCoordinator();
        Assert.Equal(2, geo.UnlockForMilestone("M1"));
        Assert.Equal(4, geo.UnlockForMilestone("M2"));
        Assert.Equal(2, geo.UnlockForMilestone("M3"));
        Assert.False(geo.UnlockByConceptId("overlap"));
        Assert.True(geo.UnlockByConceptId("card_mercator"));
        Assert.False(geo.UnlockByConceptId("card_mercator"));

        var exported = geo.ExportUnlockedIds();
        var reloaded = new GeopediaCatalogCoordinator(exported);
        Assert.True(reloaded.IsUnlocked("geopedia_overlap"));

        var articleEn = reloaded.RenderEntry("geopedia_overlap", "en-US", enTranslations);
        var articlePt = reloaded.RenderEntry("geopedia_overlap", "pt-BR", ptTranslations);
        Assert.True(articleEn.Unlocked);
        Assert.NotEqual(articleEn.Title, articlePt.Title);

        var layout150 = GeopediaCatalogCoordinator.EvaluateLongScreenLayout(150.0, articlePt.Title, baseToolbarButtonCount: 4);
        Assert.True(layout150.FitsWithoutClipping);
        Assert.True(layout150.EssentialCommandsVisible);
    }

    [Fact]
    public void CollectiblesRewardOnceAcrossSaveReloadPreserveBlockedAreasAndCatalogEightLandmarks()
    {
        Assert.Equal(8, MvpCollectiblesCoordinator.AllEightLandmarksCatalog.Count);
        Assert.Equal(2, MvpCollectiblesCoordinator.AllEightLandmarksCatalog.Count(l => l.AvailableInMvp));
        Assert.Equal(6, MvpCollectiblesCoordinator.AllSixProjectionCards.Count);

        var ledger = new CareerLedger(100);
        var geo = new GeopediaCatalogCoordinator();
        var collectibles = new MvpCollectiblesCoordinator();

        var blockedSlots = new HashSet<int> { 2 };
        var dayEggs = collectibles.GetActiveDailyEggs(dayIndex: 3, temporarilyBlockedSlots: blockedSlots);
        Assert.Equal(5, dayEggs.Count);
        Assert.True(dayEggs[2].TemporarilyBlocked);

        // Temporarily blocked item cannot be collected prematurely and stays reachable once unlocked
        var blockedAttempt = collectibles.CollectGoldenEgg(dayEggs[2], 10.0, ledger);
        Assert.False(blockedAttempt.Succeeded);
        Assert.Equal("collectible.area_temporarily_blocked", blockedAttempt.ReasonKey);

        var unblockedEggs = collectibles.GetActiveDailyEggs(dayIndex: 3);
        Assert.True(collectibles.CollectGoldenEgg(unblockedEggs[2], 12.0, ledger).Succeeded);
        Assert.Equal(110, ledger.BalanceCoords);

        // Landmark requires 5s continuous occupation within 4m and unlocks historical Geopédia entry idempotently
        var zeroStonePos = new Vector3d(500250.0, 7400250.0, 100.5);
        var shortOcc = collectibles.OccupySurveyLandmark("landmark_old_center_zero_stone", 3.0, zeroStonePos, 20.0, ledger, geo);
        Assert.False(shortOcc.Succeeded);
        Assert.Equal("collectible.landmark.occupation_too_short", shortOcc.ReasonKey);

        var fullOcc = collectibles.OccupySurveyLandmark("landmark_old_center_zero_stone", 5.0, zeroStonePos, 25.0, ledger, geo);
        Assert.True(fullOcc.Succeeded);
        Assert.False(fullOcc.AlreadyClaimed);
        Assert.Equal(135, ledger.BalanceCoords);
        Assert.True(geo.IsUnlocked("geopedia_landmark_zero_stone"));

        // Collect Mercator card
        var cardRes = collectibles.CollectProjectionCard("card_mercator", geo);
        Assert.True(cardRes.Succeeded);
        Assert.True(geo.IsUnlocked("geopedia_card_mercator"));

        // Save/reload preserves all claimed items and prevents duplicate Coords
        var snapshot = collectibles.ExportSnapshot();
        var reloaded = new MvpCollectiblesCoordinator(snapshot);
        Assert.Equal(4, reloaded.GetActiveDailyEggs(dayIndex: 3).Count);
        var dupEgg = reloaded.CollectGoldenEgg(unblockedEggs[2], 50.0, ledger);
        Assert.True(dupEgg.AlreadyClaimed);
        Assert.Equal(135, ledger.BalanceCoords);
    }

    [Fact]
    public void ScientificReplayIsInvariantAcross30_60_120FpsAndRejectsFutureVersionsOrNonReplayTruth()
    {
        var root = FindRepoRoot();
        var contractJson = File.ReadAllText(Path.Combine(root, "content", "contracts", "hist-02-drainage.json"));
        var def = ContractDefinition.Parse(contractJson, "hist-02-drainage.json");
        var rgb = new byte[CameraThumbnail.ExpectedWidth * CameraThumbnail.ExpectedHeight * 3];
        var thumb = CameraThumbnail.FromRgbBytes(CameraThumbnail.ExpectedWidth, CameraThumbnail.ExpectedHeight, rgb);

        var samples = new List<SerializableCameraSample>(def.RequiredFeatureIds.Count);
        var footprints = new List<SampleFootprintObservation>(def.RequiredFeatureIds.Count);
        var features = new List<TriangulatedFeatureEntry>(def.RequiredFeatureIds.Count);
        var featLocs = new Dictionary<string, Vector3d>(StringComparer.Ordinal);
        var bounds = new Bounds2DEnu(500008.0, 7400008.0, 500232.0, 7400052.0);

        for (var i = 0; i < def.RequiredFeatureIds.Count; i++)
        {
            var fid = def.RequiredFeatureIds[i];
            var pos = new Vector3d(500020.0 + i * 16.0, 7400025.0, 100.5);
            featLocs[fid] = pos;
            var sid = $"rep_s_{i + 1:00}";
            var s = new CameraObservationSample(
                sid, "cam_initial", i, 10.0 + i * 5.0,
                new EstimatedBodyPose(10.0 + i * 5.0, pos + new Vector3d(-2.0, 0.0, 0.5), Quaterniond.Identity),
                CameraMountEstimate.ForwardFacing, CameraIntrinsics.Initial,
                new CameraQualityMetrics(0.002, 0.25, 0.005, 0.0, 0.35, false, false, false, false, true), [], [], thumb);
            samples.Add(SerializableCameraSample.FromDomain(s));
            footprints.Add(new SampleFootprintObservation(sid, "commerce_drainage_surface", bounds));
            features.Add(new TriangulatedFeatureEntry(
                fid, "manhole", TriangulationStatus.Resolved, FeatureReconstructionMethod.TwoRayTriangulation,
                GeometryFailureReason.None, pos, 4.0, null, 2, true, [sid]));
        }

        ControlCheckpointReference[] checkpoints =
        [
            new("rep_chk_01", new Vector3d(500020.0, 7400020.0, 100.5)),
            new("rep_chk_02", new Vector3d(500110.0, 7400020.0, 100.5)),
            new("rep_chk_03", new Vector3d(500200.0, 7400020.0, 100.5)),
        ];
        var submittedCheckpoints = checkpoints.Select(c => new SubmittedCheckpointEstimate(c.CheckpointId, c.TruePositionEnu)).ToArray();

        var batch = new SurveyBatchArtifact(
            1, "batch_rep_unit", def.Id, 10.0, 70.0, samples.Count, 0, CaptureStopReason.None,
            ["cam_initial"], samples, footprints, features, submittedCheckpoints);
        var evalRef = new EvaluationReference(
            def.Id, [new SurveySurfaceReference("commerce_drainage_surface", bounds, 100.5)],
            def.RequiredFeatureIds, checkpoints, [new RelativeCheckpointPairReference("rep_chk_01", "rep_chk_02")], featLocs);

        var cm = new ContractStateMachine(def);
        cm.Accept(0.0);
        cm.RecordPendingBatch(70.0, batch.BatchId);
        var store = new BatchCustodyStore();
        store.RegisterBufferedBatch(batch, 70.0);
        store.OffloadAtomic(batch.BatchId, 72.0);
        cm.ProtectBatchAtUnloadPoint(72.0, batch.BatchId);
        var queue = new OfficeProcessingQueue();
        var ledger = new CareerLedger(1000);
        var recipe = SimplifiedOfficeWorkstation.BuildArcadeAssistedRecipe("rec_rep_unit", cm, store, ledger, sourceBatchIds: [batch.BatchId]) with { CalendarDurationSeconds = 0.0 };
        var product = queue.StartOrResumeJob("job_rep_unit", cm, store, ledger, recipe, 75.0).Product!;
        var report = QualityEvaluator.Evaluate(def, evalRef, product.Submission);
        var reportJson = report.ToJson();
        var reportSha = ScientificReplayEngine.ComputeCanonicalSha256(reportJson);

        var pkg = new ReplayPackage(
            1, "gsa-mvp-0.3.0", "gsa-physics-v1", MvpProceduralContractGenerator.DefaultOldCenterMapSha256,
            ["cam_initial"], 99UL, contractJson, batch, recipe, ReplayEvaluationReferenceDto.FromDomain(evalRef),
            [
                new VisualPoseKeyframe(0.0, new Vector3d(500010.0, 7400025.0, 100.5), Quaterniond.Identity, 1.5),
                new VisualPoseKeyframe(2.0, new Vector3d(500013.0, 7400025.0, 100.5), Quaterniond.Identity, 1.5),
            ],
            [new ReplayEventRecord(10.0, "capture_started", batch.BatchId)],
            reportJson,
            reportSha);

        // Live mode cannot access didactic truth overlay
        Assert.Throws<InvalidOperationException>(() => ScientificReplayEngine.BuildDidacticTruthOverlay(pkg, isReplayMode: false));
        Assert.True(ScientificReplayEngine.BuildDidacticTruthOverlay(pkg, isReplayMode: true).ReplayModeOnly);

        HashSet<string> profs = ["cam_initial"];
        var fps30 = ScientificReplayEngine.SampleVisualTrajectoryAtFps(pkg, 30.0, "gsa-mvp-0.3.0", "gsa-physics-v1", MvpProceduralContractGenerator.DefaultOldCenterMapSha256, profs);
        var fps60 = ScientificReplayEngine.SampleVisualTrajectoryAtFps(pkg, 60.0, "gsa-mvp-0.3.0", "gsa-physics-v1", MvpProceduralContractGenerator.DefaultOldCenterMapSha256, profs);
        var fps120 = ScientificReplayEngine.SampleVisualTrajectoryAtFps(pkg, 120.0, "gsa-mvp-0.3.0", "gsa-physics-v1", MvpProceduralContractGenerator.DefaultOldCenterMapSha256, profs);

        Assert.Equal(61, fps30.VisualFrameCount);
        Assert.Equal(121, fps60.VisualFrameCount);
        Assert.Equal(241, fps120.VisualFrameCount);
        Assert.Equal(samples.Count, fps30.ScientificObservationCount);
        Assert.Equal(samples.Count, fps60.ScientificObservationCount);
        Assert.Equal(samples.Count, fps120.ScientificObservationCount);
        Assert.Equal(reportSha, fps30.ReplayedReportSha256);
        Assert.Equal(reportSha, fps60.ReplayedReportSha256);
        Assert.Equal(reportSha, fps120.ReplayedReportSha256);

        var futureVersion = ScientificReplayEngine.ExecuteScientificReplay(
            pkg with { SchemaVersion = 99 }, "gsa-mvp-0.3.0", "gsa-physics-v1", MvpProceduralContractGenerator.DefaultOldCenterMapSha256, profs);
        Assert.False(futureVersion.Succeeded);
        Assert.Equal("replay.incompatible_version", futureVersion.DiagnosticReasonKey);
    }

    [Fact]
    public void MvpCampaignRunsHist01To03ProceduralGeneratorPasses500SeedsAndM3DualGateEvaluatesCohorts()
    {
        var root = FindRepoRoot();
        var contentDir = Path.Combine(root, "content");
        var worldPath = Path.Combine(root, "game", "world", "tutorial-block.json");
        var enPoPath = Path.Combine(root, "game", "localization", "en_US.po");
        var ptPoPath = Path.Combine(root, "game", "localization", "pt_BR.po");
        var glossaryPath = Path.Combine(root, "content", "localization", "glossary.csv");

        var gateReport = M3PlaytestAndGateValidator.BuildM3GateReport(contentDir, worldPath, enPoPath, ptPoPath, glossaryPath);
        Assert.Equal("passed", gateReport.TechnicalLoopStatus);
        Assert.Equal("pending_human_playtest_cohort", gateReport.PlaytestGateStatus);
        Assert.Equal(3, gateReport.CampaignPlaythrough.Missions.Count);
        Assert.True(gateReport.CampaignPlaythrough.AllThreeMissionsPassed);
        Assert.True(gateReport.CampaignPlaythrough.SaveBetweenMissionsVerified);
        Assert.True(gateReport.CampaignPlaythrough.RainHandledWithoutInstantFail);
        Assert.True(gateReport.CampaignPlaythrough.ControlsDisjointFromCheckpoints);
        Assert.Contains("bicycle", gateReport.CampaignPlaythrough.UnlockedVehicles);
        Assert.Contains("scooter", gateReport.CampaignPlaythrough.UnlockedVehicles);

        Assert.Equal(5, gateReport.ProceduralValidation.TemplatesValidated);
        Assert.Equal(500, gateReport.ProceduralValidation.TotalContractsGenerated);
        Assert.Equal(500, gateReport.ProceduralValidation.TotalContractsPassed);
        Assert.True(gateReport.ProceduralValidation.AllPassed);
        Assert.True(gateReport.CalmContractAlwaysAvailableAtLowBalance);
        Assert.True(gateReport.ReplayFpsInvarianceVerified);
        Assert.True(gateReport.BilingualAuditPassed);
        Assert.True(gateReport.CollectiblesIdempotentVerified);
        Assert.Equal(16, gateReport.Tasks.Count);

        // Route witness invalidates cleanly on map hash change
        Assert.Throws<InvalidOperationException>(() =>
            MvpProceduralContractGenerator.GenerateFromTemplate("PROC-PARQUE", 42UL, currentMapSha256: "stale-map-sha"));

        // Human dual-cohort validation rejects insufficient student effect links and passes full valid cohorts
        var weakStudents = Enumerable.Range(1, 5)
            .Select(i => new M3PlaytestSessionRecord($"stu_{i}", M3CohortKind.Student, 25.0, true, true, LinkedObservedEffectsCount: 1, "only 1 effect", IsHumanCohortSession: true))
            .ToArray();
        Assert.Equal("failed_rework_required", M3PlaytestAndGateValidator.EvaluateCohort(M3CohortKind.Student, weakStudents).PlaytestGateStatus);

        var strongStudents = Enumerable.Range(1, 5)
            .Select(i => new M3PlaytestSessionRecord($"stu_{i}", M3CohortKind.Student, 25.0, true, i <= 4, LinkedObservedEffectsCount: i <= 4 ? 2 : 1, "linked blur & overlap", IsHumanCohortSession: true))
            .ToArray();
        var strongGamers = Enumerable.Range(1, 5)
            .Select(i => new M3PlaytestSessionRecord($"gam_{i}", M3CohortKind.Gamer, 24.0, true, i <= 4, LinkedObservedEffectsCount: 1, "smooth", IsHumanCohortSession: true))
            .ToArray();
        Assert.Equal("passed", M3PlaytestAndGateValidator.EvaluateCohort(M3CohortKind.Student, strongStudents).PlaytestGateStatus);
        Assert.Equal("passed", M3PlaytestAndGateValidator.EvaluateCohort(M3CohortKind.Gamer, strongGamers).PlaytestGateStatus);
    }
}
