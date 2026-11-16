using System.Text.Json;
using System.Text.Json.Serialization;
using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Camera;
using Gsa.Missions.Content;
using Gsa.Missions.Contracts;
using Gsa.Missions.Economy;
using Gsa.Missions.Office;
using Gsa.Missions.Persistence;
using Gsa.Missions.Quality;

namespace Gsa.Missions.Campaign;

public sealed record MissionExecutionSummary(
    string MissionId,
    string ContractId,
    LocomotionMode PlatformUsed,
    int FinalStars,
    int PayoutCoords,
    bool PrivacyCompliant,
    bool Passed,
    IReadOnlyList<string> UnlockedMissions,
    IReadOnlyList<string> UnlockedVehicles,
    string Notes);

public sealed record MvpThreeMissionPlaythroughReport(
    int SchemaVersion,
    bool AllThreeMissionsPassed,
    bool NoCircularDependencies,
    bool SaveBetweenMissionsVerified,
    bool CutsceneSkipIdempotent,
    bool RainHandledWithoutInstantFail,
    bool ControlsDisjointFromCheckpoints,
    int FinalBalanceCoords,
    IReadOnlyList<string> UnlockedMissions,
    IReadOnlyList<string> UnlockedVehicles,
    IReadOnlyList<MissionExecutionSummary> Missions);

public sealed record RouteWitnessCertificate(
    string VariantId,
    string AreaId,
    string SurfaceId,
    int RequiredPlatformLevel,
    IReadOnlyList<string> RequiredCapabilities,
    double EstimatedRouteDurationSeconds,
    int RequiredStorageUnits,
    string MapSha256,
    string ProfileVersion)
{
    public bool IsValidFor(string currentMapSha256, string currentProfileVersion) =>
        string.Equals(MapSha256, currentMapSha256, StringComparison.Ordinal) &&
        string.Equals(ProfileVersion, currentProfileVersion, StringComparison.Ordinal);
}

public sealed record ProceduralTemplateSpec(
    string TemplateId,
    string ClientId,
    string Product,
    bool CalmByDefault,
    string TitleKey,
    IReadOnlyList<RouteWitnessCertificate> RouteVariants,
    IReadOnlyList<string> CandidateFeatureIds);

public sealed record GeneratedContractOffer(
    string TemplateId,
    string VariantId,
    ulong Seed,
    ContractDefinition Contract,
    RouteWitnessCertificate WitnessCertificate,
    bool ExecutableWithStarterKit,
    int EstimatedRouteDurationSeconds,
    int EstimatedStorageUnits);

public sealed record TemplateSeedValidationReport(
    string TemplateId,
    int SeedsTested,
    int SeedsPassed,
    bool AllCompatibleWithFleet,
    bool AllWitnessRoutesFeasible,
    bool AllSchemaValid);

public sealed record OneHundredSeedsPerTemplateReport(
    int SchemaVersion,
    int TemplatesValidated,
    int TotalContractsGenerated,
    int TotalContractsPassed,
    bool AllPassed,
    string MapSha256,
    string ProfileVersion,
    IReadOnlyList<TemplateSeedValidationReport> ByTemplate);

/// <summary>Executes HIST-01/02/03 end-to-end MVP campaign and generates/validates the 5 procedural MVP templates.</summary>
public static class MvpCampaignRunner
{
    public static MvpThreeMissionPlaythroughReport RunMvpThreeMissionPlaythrough(
        string contentDirectory,
        string worldManifestPath,
        string? saveSlotDirectory = null)
    {
        var hist01Report = TutorialMissionRunner.RunHist01Playthrough(contentDirectory, worldManifestPath, saveSlotDirectory);

        var missionJsons = Directory.GetFiles(Path.Combine(contentDirectory, "missions"), "*.json")
            .Order(StringComparer.Ordinal)
            .Select(path => MissionDefinition.Parse(File.ReadAllText(path), Path.GetFileName(path)))
            .ToDictionary(m => m.Id, StringComparer.Ordinal);

        var contractJsons = Directory.GetFiles(Path.Combine(contentDirectory, "contracts"), "*.json")
            .Order(StringComparer.Ordinal)
            .Select(path => ContractDefinition.Parse(File.ReadAllText(path), Path.GetFileName(path)))
            .ToDictionary(c => c.Id, StringComparer.Ordinal);

        var hist01Def = missionJsons["HIST-01"];
        var hist02Def = missionJsons["HIST-02"];
        var hist03Def = missionJsons["HIST-03"];

        var catalogCheck = StoryCampaignCoordinator.ValidateInitialStoryCatalog(missionJsons, contractJsons);
        if (!catalogCheck.Valid)
            throw new InvalidOperationException("MVP story catalog contains validation errors: " + string.Join("; ", catalogCheck.Errors));

        var contractHist02 = contractJsons[hist02Def.Objectives[0].ContractId];
        var contractHist03 = contractJsons[hist03Def.Objectives[0].ContractId];

        var regionDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(contentDirectory, "regions", "old-center.json")));
        var featuresById = regionDoc.RootElement.GetProperty("features").EnumerateArray()
            .ToDictionary(
                el => el.GetProperty("id").GetString()!,
                el =>
                {
                    var p = el.GetProperty("position_enu");
                    return new Vector3d(p[0].GetDouble(), p[1].GetDouble(), p[2].GetDouble());
                },
                StringComparer.Ordinal);

        var campaign = new StoryCampaignCoordinator([hist01Def, hist02Def, hist03Def]);
        foreach (var ev in hist01Def.Events)
            campaign.TriggerOrSkipBeatEvent("HIST-01", ev.Id, skipCutscene: true);
        campaign.CompleteMission("HIST-01", activePlatformLevel: 0, skipAllCutscenes: true);

        var skipAgain = campaign.TriggerOrSkipBeatEvent("HIST-01", hist01Def.Events[^1].Id, skipCutscene: true);
        var cutsceneSkipIdempotent = skipAgain.WasIdempotentNoOp;

        var ledger = new CareerLedger(hist01Report.FinalLedgerCoords);
        var progression = CareerProgressionState.FromSnapshot(new CareerProgressionSnapshot(
            1,
            100,
            new Dictionary<string, int>(StringComparer.Ordinal) { ["city_hall"] = 2 },
            new Dictionary<string, int>(StringComparer.Ordinal) { ["old_center"] = 2 },
            ["HIST-02"],
            ["contract_hist_01"]));
        var fleet = new FleetProgressionManager();
        var custody = new BatchCustodyStore();
        var queue = new OfficeProcessingQueue();

        // Execute HIST-02: Handcart + announced rain quality obstacle + 12 manholes -> unlocks Bicycle & HIST-03
        var rainResult = StoryCampaignCoordinator.EvaluateAnnouncedRainObstacle(hist02Def, rainIntensity: 0.75, exposureAdjusted: true);
        var rainSafe = !rainResult.InstantFailureTriggered;

        var hist02Machine = new ContractStateMachine(contractHist02);
        hist02Machine.Accept(900.0);
        foreach (var ev in hist02Def.Events.Take(2))
            campaign.TriggerOrSkipBeatEvent("HIST-02", ev.Id, skipCutscene: true);

        var hist02Summary = ExecuteInventoryContractMission(
            "HIST-02",
            "manhole",
            "commerce_drainage_surface",
            100.5,
            contractHist02,
            hist02Machine,
            hist02Def,
            featuresById,
            ["survey_control_east", "survey_control_west"],
            LocomotionMode.Handcart,
            startTimeSeconds: 920.0,
            custody,
            queue,
            ledger,
            progression,
            campaign);

        if (!campaign.State.IsVehicleUnlocked("bicycle") || !campaign.State.IsMissionUnlocked("HIST-03"))
            throw new InvalidOperationException("Completing HIST-02 must unlock bicycle and HIST-03.");

        // Purchase Bicycle cleanly (price 350 Coords, player earned initial + HIST-01 + HIST-02)
        var completedSet = new HashSet<string>(campaign.State.CompletedMissions, StringComparer.Ordinal);
        if (!fleet.TryPurchasePlatform("buy_bicycle_hist02", 1650.0, LocomotionMode.Bicycle, ledger, progression, completedSet, out _, out _))
            throw new InvalidOperationException("Purchasing unlocked bicycle after HIST-02 failed.");

        // Verify save/load checkpoint between HIST-02 and HIST-03 preserves unlocks and balance
        var tempDir = saveSlotDirectory ?? Path.Combine(Path.GetTempPath(), "gsa-mvp-save-" + Guid.NewGuid().ToString("N"));
        bool saveBetweenMissionsOk;
        try
        {
            var mgr = new SaveGameManager(tempDir);
            var saveState = new SaveGameState(
                1, SaveTrigger.MissionEnded, 0,
                new SaveClockState(1660.0, 1660.0),
                new SaveRngState(777UL, 1UL, 10),
                ledger.ExportState(),
                progression.ExportSnapshot(),
                new SaveEquipmentMountState("bicycle", ["cart", "bicycle", "cam_helmet", "gnss_basic"], new Dictionary<string, string> { ["helmet"] = "cam_helmet" }),
                custody.ExportSnapshot(),
                [hist02Machine.ExportSnapshot()]);
            var sv = mgr.Save(saveState, SaveTrigger.MissionEnded);
            var ld = mgr.LoadCurrent();
            var restored = ld.Succeeded && ld.State is not null ? SaveGameManager.RestoreRuntime(ld.State) : null;
            saveBetweenMissionsOk = sv.Succeeded && ld.Succeeded && restored is not null && restored.Ledger.BalanceCoords == ledger.BalanceCoords;
        }
        finally
        {
            if (saveSlotDirectory is null && Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }

        // Execute HIST-03: Bicycle + local control targets disjoint from CQ checkpoints + 8 sidewalk segments -> unlocks Scooter & HIST-04
        var hist03Machine = new ContractStateMachine(contractHist03);
        hist03Machine.Accept(1700.0);
        foreach (var ev in hist03Def.Events.Take(2))
            campaign.TriggerOrSkipBeatEvent("HIST-03", ev.Id, skipCutscene: true);

        var controlsCheck = new ControlVsCheckpointReference(
            ["survey_control_east", "survey_control_west"],
            ["hist03_chk_01", "hist03_chk_02", "hist03_chk_03"],
            controlsUsedInEstimation: true,
            checkpointsReservedForQualityEvaluationOnly: true);
        var controlsDisjoint = !controlsCheck.ControlTargetIds.Intersect(controlsCheck.CheckpointTargetIds, StringComparer.Ordinal).Any();

        var hist03Summary = ExecuteInventoryContractMission(
            "HIST-03",
            "sidewalk_segment",
            "commerce_sidewalk_surface",
            100.5,
            contractHist03,
            hist03Machine,
            hist03Def,
            featuresById,
            controlsCheck.CheckpointTargetIds,
            LocomotionMode.Bicycle,
            startTimeSeconds: 1720.0,
            custody,
            queue,
            ledger,
            progression,
            campaign);

        var hist01Summary = new MissionExecutionSummary(
            hist01Report.MissionId,
            hist01Report.ContractId,
            LocomotionMode.Handcart,
            hist01Report.FinalStars,
            hist01Report.NetPayoutCoords,
            hist01Report.FinalPrivacyCompliant,
            hist01Report.FinalReportPassed,
            ["HIST-02"],
            ["cart"],
            "HIST-01 completed tutorial & 5-star CQ rework.");

        MissionExecutionSummary[] summaries = [hist01Summary, hist02Summary, hist03Summary];
        var allPassed = summaries.All(m => m.Passed);

        return new MvpThreeMissionPlaythroughReport(
            SchemaVersion: 1,
            AllThreeMissionsPassed: allPassed,
            NoCircularDependencies: catalogCheck.Valid,
            SaveBetweenMissionsVerified: saveBetweenMissionsOk,
            CutsceneSkipIdempotent: cutsceneSkipIdempotent,
            RainHandledWithoutInstantFail: rainSafe,
            ControlsDisjointFromCheckpoints: controlsDisjoint,
            FinalBalanceCoords: ledger.BalanceCoords,
            UnlockedMissions: campaign.State.UnlockedMissions,
            UnlockedVehicles: campaign.State.UnlockedVehicles,
            Missions: summaries);
    }

    internal static MissionExecutionSummary ExecuteInventoryContractMission(
        string missionId,
        string featureClass,
        string surfaceId,
        double surfaceZ,
        ContractDefinition def,
        ContractStateMachine machine,
        MissionDefinition missionDef,
        IReadOnlyDictionary<string, Vector3d> featuresById,
        IReadOnlyList<string> checkpointIds,
        LocomotionMode mode,
        double startTimeSeconds,
        BatchCustodyStore custody,
        OfficeProcessingQueue queue,
        CareerLedger ledger,
        CareerProgressionState progression,
        StoryCampaignCoordinator campaign)
    {
        var reqPositions = def.RequiredFeatureIds.Select(id => featuresById[id]).ToArray();
        var minE = reqPositions.Min(p => p.X) - 2.0;
        var minN = reqPositions.Min(p => p.Y) - 2.0;
        var maxE = reqPositions.Max(p => p.X) + 2.0;
        var maxN = reqPositions.Max(p => p.Y) + 2.0;

        var surfaceRef = new SurveySurfaceReference(surfaceId, new Bounds2DEnu(minE, minN, maxE, maxN), surfaceZ);
        var effectiveCheckpointIds = checkpointIds.Count >= 3 ? checkpointIds : ["chk_01", "chk_02", "chk_03"];
        var checkpoints = effectiveCheckpointIds.Select((cid, idx) =>
            new ControlCheckpointReference(cid, new Vector3d(minE + 5.0 + idx * 12.0, minN + 5.0, surfaceZ))).ToArray();
        var relativePairs = new[]
        {
            new RelativeCheckpointPairReference(checkpoints[0].CheckpointId, checkpoints[1].CheckpointId),
            new RelativeCheckpointPairReference(checkpoints[1].CheckpointId, checkpoints[2].CheckpointId),
        };
        var evalRef = new EvaluationReference(
            def.Id,
            [surfaceRef],
            def.RequiredFeatureIds,
            checkpoints,
            relativePairs,
            def.RequiredFeatureIds.ToDictionary(id => id, id => featuresById[id], StringComparer.Ordinal));

        var samples = new List<SerializableCameraSample>(def.RequiredFeatureIds.Count);
        var footprints = new List<SampleFootprintObservation>(def.RequiredFeatureIds.Count);
        var features = new List<TriangulatedFeatureEntry>(def.RequiredFeatureIds.Count);

        var sensorId = mode == LocomotionMode.Bicycle ? "cam_helmet" : "cam_initial";
        var rgb = new byte[CameraThumbnail.ExpectedWidth * CameraThumbnail.ExpectedHeight * 3];
        var thumb = CameraThumbnail.FromRgbBytes(CameraThumbnail.ExpectedWidth, CameraThumbnail.ExpectedHeight, rgb);

        for (var i = 0; i < def.RequiredFeatureIds.Count; i++)
        {
            var fid = def.RequiredFeatureIds[i];
            var fpos = featuresById[fid];
            var sampleId = $"{missionId}_s_{i + 1:00}";
            var t = startTimeSeconds + i * 5.0;
            var pose = new EstimatedBodyPose(t, fpos + new Vector3d(-3.0, 0.0, 0.5), Quaterniond.Identity);
            var metrics = new CameraQualityMetrics(0.002, 0.25, 0.005, 0.0, 0.35, false, false, false, false, true);
            var domainSample = new CameraObservationSample(
                sampleId, sensorId, i, t, pose, CameraMountEstimate.ForwardFacing, CameraIntrinsics.Initial, metrics, [], [], thumb);
            samples.Add(SerializableCameraSample.FromDomain(domainSample));

            footprints.Add(new SampleFootprintObservation(sampleId, surfaceId, new Bounds2DEnu(minE, minN, maxE, maxN)));
            features.Add(new TriangulatedFeatureEntry(
                fid, featureClass, TriangulationStatus.Resolved, FeatureReconstructionMethod.TwoRayTriangulation,
                GeometryFailureReason.None, fpos, 5.0, null, 2, true, [sampleId]));
        }

        var submittedCheckpoints = checkpoints
            .Select(c => new SubmittedCheckpointEstimate(c.CheckpointId, c.TruePositionEnu + new Vector3d(0.01, -0.01, 0.0)))
            .ToArray();

        var batchEnd = startTimeSeconds + def.RequiredFeatureIds.Count * 5.0;
        var batchId = $"batch_{missionId.ToLowerInvariant()}";
        var batch = new SurveyBatchArtifact(
            1, batchId, def.Id, startTimeSeconds, batchEnd, samples.Count, 0, CaptureStopReason.None,
            [sensorId], samples, footprints, features, submittedCheckpoints);

        machine.RecordPendingBatch(batchEnd, batchId);
        custody.RegisterBufferedBatch(batch, batchEnd);
        custody.OffloadAtomic(batchId, batchEnd + 10.0);
        machine.ProtectBatchAtUnloadPoint(batchEnd + 10.0, batchId);

        var recipe = SimplifiedOfficeWorkstation.BuildArcadeAssistedRecipe($"rec_{missionId}", machine, custody, ledger, sourceBatchIds: [batchId]);
        var startJob = queue.StartOrResumeJob($"job_{missionId}", machine, custody, ledger, recipe, batchEnd + 15.0);
        if (!startJob.Succeeded)
            throw new InvalidOperationException($"Starting office job for {missionId} failed: {startJob.ReasonKey}");
        var doneJob = queue.AdvanceJob(machine, custody, recipe.CalendarDurationSeconds);
        if (!doneJob.Succeeded || doneJob.Product is null)
            throw new InvalidOperationException($"Completing office job for {missionId} failed: {doneJob.ReasonKey}");

        var evalReport = QualityEvaluator.Evaluate(def, evalRef, doneJob.Product.Submission);
        machine.RecordQualityEvaluation(batchEnd + 400.0, evalReport.Passed, evalReport.PassedDeliverableIds, evalReport.FailureReasonKeys.FirstOrDefault());

        var quote = DeliveryQuoteCalculator.Calculate(def, evalReport, machine.Snapshot!.AcceptedAtSeconds, batchEnd + 410.0);
        var settle = ContractSettlementCoordinator.SettleDelivery(machine, ledger, progression, evalReport, batchEnd + 410.0);
        if (!settle.Succeeded)
            throw new InvalidOperationException($"Settling {missionId} failed: {settle.ReasonKey}");

        var platformLevel = mode switch
        {
            LocomotionMode.Bicycle => 1,
            LocomotionMode.Scooter => 2,
            _ => 0,
        };
        campaign.CompleteMission(missionId, platformLevel, skipAllCutscenes: true);

        return new MissionExecutionSummary(
            missionId,
            def.Id,
            mode,
            quote.Stars,
            settle.Quote.NetCoords,
            doneJob.Product.PrivacyManifest.IsCompliant,
            evalReport.Passed && machine.State == ContractState.Entregue,
            missionDef.Unlocks,
            missionDef.VehicleUnlocks,
            $"Completed {missionId} with {mode} ({quote.Stars} stars, {samples.Count} samples).");
    }
}

public static class MvpProceduralContractGenerator
{
    public const string CurrentProfileVersion = "mvp-profiles-v1";
    public const string DefaultOldCenterMapSha256 = "old-center-mvp-v1";

    public static IReadOnlyList<ProceduralTemplateSpec> Templates { get; } =
    [
        new(
            "PROC-FACHADAS",
            "city_hall",
            "images",
            CalmByDefault: false,
            "proc.template.facades.title",
            [
                new("facades_south", "commerce_street_facades", "commerce_facade_planes", 0, ["camera", "gnss_basic"], 1800.0, 20, DefaultOldCenterMapSha256, CurrentProfileVersion),
                new("facades_north", "commerce_street_facades", "commerce_facade_planes", 0, ["camera", "gnss_basic"], 2400.0, 24, DefaultOldCenterMapSha256, CurrentProfileVersion),
            ],
            Enumerable.Range(1, 20).Select(i => $"facade_{i:00}").ToArray()),
        new(
            "PROC-DRENAGEM",
            "city_hall",
            "feature_inventory",
            CalmByDefault: false,
            "proc.template.drainage.title",
            [
                new("drainage_west", "commerce_street_drainage", "commerce_drainage_surface", 0, ["camera", "gnss_basic"], 1800.0, 14, DefaultOldCenterMapSha256, CurrentProfileVersion),
                new("drainage_east", "commerce_street_drainage", "commerce_drainage_surface", 0, ["camera", "gnss_basic"], 2100.0, 16, DefaultOldCenterMapSha256, CurrentProfileVersion),
            ],
            Enumerable.Range(1, 12).Select(i => $"manhole_{i:00}").ToArray()),
        new(
            "PROC-CALCADAS",
            "university",
            "feature_inventory",
            CalmByDefault: false,
            "proc.template.sidewalks.title",
            [
                new("sidewalks_loop_a", "commerce_street_sidewalks", "commerce_sidewalk_surface", 1, ["camera", "gnss_basic"], 1500.0, 16, DefaultOldCenterMapSha256, CurrentProfileVersion),
                new("sidewalks_loop_b", "commerce_street_sidewalks", "commerce_sidewalk_surface", 0, ["camera", "gnss_basic"], 2200.0, 16, DefaultOldCenterMapSha256, CurrentProfileVersion),
            ],
            Enumerable.Range(1, 8).Select(i => $"sidewalk_seg_{i:00}").ToArray()),
        new(
            "PROC-PLACAS",
            "map_company",
            "feature_inventory",
            CalmByDefault: false,
            "proc.template.signs.title",
            [
                new("signs_corridor_west", "commerce_street_signs", "commerce_sign_planes", 2, ["camera", "gnss_basic"], 1600.0, 20, DefaultOldCenterMapSha256, CurrentProfileVersion),
                new("signs_corridor_all", "commerce_street_signs", "commerce_sign_planes", 0, ["camera", "gnss_basic"], 2700.0, 24, DefaultOldCenterMapSha256, CurrentProfileVersion),
            ],
            Enumerable.Range(1, 12).Select(i => $"sign_{i:00}").ToArray()),
        new(
            "PROC-PARQUE",
            "insurance_co",
            "images",
            CalmByDefault: true,
            "proc.template.park.title",
            [
                new("park_calm_inner", "central_park_calm", "central_park_planes", 0, ["camera", "gnss_basic"], 900.0, 10, DefaultOldCenterMapSha256, CurrentProfileVersion),
                new("park_calm_perimeter", "central_park_calm", "central_park_planes", 0, ["camera", "gnss_basic"], 1200.0, 12, DefaultOldCenterMapSha256, CurrentProfileVersion),
            ],
            ["park_bench_01", "park_bench_02", "park_bench_03", "park_bench_04"]),
    ];

    public static GeneratedContractOffer GenerateFromTemplate(
        string templateId,
        ulong seed,
        bool forceCalmVariant = false,
        string currentMapSha256 = DefaultOldCenterMapSha256,
        string currentProfileVersion = CurrentProfileVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);
        var spec = Templates.FirstOrDefault(t => string.Equals(t.TemplateId, templateId, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Unknown MVP procedural template '{templateId}'.", nameof(templateId));

        var variantIndex = (int)(seed % (ulong)spec.RouteVariants.Count);
        var witness = spec.RouteVariants[variantIndex];
        if (!witness.IsValidFor(currentMapSha256, currentProfileVersion))
            throw new InvalidOperationException($"Route witness certificate '{witness.VariantId}' is invalidated for current map/profile hash.");

        var minCount = Math.Min(4, spec.CandidateFeatureIds.Count);
        var maxCount = spec.CandidateFeatureIds.Count;
        var span = Math.Max(1, maxCount - minCount + 1);
        var count = minCount + (int)((seed / 7UL) % (ulong)span);
        var selectedFeatures = spec.CandidateFeatureIds
            .Select((id, idx) => (id, key: HashStep(seed, idx)))
            .OrderBy(x => x.key)
            .ThenBy(x => x.id, StringComparer.Ordinal)
            .Take(count)
            .Select(x => x.id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        var dueSeconds = Math.Max(7200.0, Math.Ceiling(witness.EstimatedRouteDurationSeconds * 4.0));
        var expiresSeconds = dueSeconds + 5400.0;
        var isCalm = spec.CalmByDefault || forceCalmVariant;
        var baseReward = 220 + selectedFeatures.Length * 25;

        ContractRequirementSpec[] reqs = spec.Product == "images"
            ? [
                new ThresholdRequirementSpec("area_coverage", "ratio", "min", 0.85, 0.96, witness.SurfaceId, 2.0),
                new PredicateRequirementSpec("all_required_features", true),
                new ThresholdRequirementSpec("valid_image_fraction", "ratio", "min", 0.80, 0.96),
                new PredicateRequirementSpec("privacy_redacted", true),
                new PredicateRequirementSpec("format_valid", true),
            ]
            : [
                new ThresholdRequirementSpec("feature_completeness", "ratio", "min", 0.85, 0.98),
                new ThresholdRequirementSpec("absolute_rmse", "m", "max", 2.0, 0.5),
                new ThresholdRequirementSpec("valid_image_fraction", "ratio", "min", 0.80, 0.96),
                new PredicateRequirementSpec("privacy_redacted", true),
                new PredicateRequirementSpec("format_valid", true),
            ];

        var def = new ContractDefinition(
            SchemaVersion: 1,
            Id: $"{spec.TemplateId.ToLowerInvariant().Replace('-', '_')}_{witness.VariantId}_{seed}",
            TitleKey: spec.TitleKey,
            ClientId: spec.ClientId,
            RegionId: "old_center",
            AreaId: witness.AreaId,
            Product: spec.Product,
            RequiredFeatureIds: selectedFeatures,
            Requirements: reqs,
            RequiredCapabilities: witness.RequiredCapabilities,
            Risk: isCalm ? "easy" : "moderate",
            Calm: isCalm,
            Deadline: new ContractDeadlineSpec(dueSeconds, expiresSeconds, 0.05, 0.50),
            Reward: new ContractRewardSpec(baseReward, 75 + selectedFeatures.Length * 5, 1, []),
            PartialPolicy: new ContractPartialPolicySpec("disabled", []),
            DeliveryFormat: "gsa_bundle_v1",
            StoryPrerequisites: []);

        var starterStorage = PlatformProfile.Handcart.VirtualStorageCapacityUnits;
        var starterCompatible = witness.RequiredPlatformLevel == 0 && witness.RequiredStorageUnits <= starterStorage;

        return new GeneratedContractOffer(
            spec.TemplateId,
            witness.VariantId,
            seed,
            def,
            witness,
            starterCompatible,
            (int)witness.EstimatedRouteDurationSeconds,
            witness.RequiredStorageUnits);
    }

    public static OneHundredSeedsPerTemplateReport ValidateOneHundredSeedsPerTemplate(
        ulong baseSeed = 1000UL,
        string currentMapSha256 = DefaultOldCenterMapSha256,
        string currentProfileVersion = CurrentProfileVersion)
    {
        var results = new List<TemplateSeedValidationReport>(Templates.Count);
        var totalGenerated = 0;
        var totalPassed = 0;

        foreach (var template in Templates)
        {
            var passedCount = 0;
            var allFleetOk = true;
            var allWitnessOk = true;
            var allSchemaOk = true;

            for (var s = 0; s < 100; s++)
            {
                var seed = baseSeed + (ulong)s * 31UL;
                var offer = GenerateFromTemplate(template.TemplateId, seed, forceCalmVariant: false, currentMapSha256, currentProfileVersion);
                totalGenerated++;

                var platformMode = offer.WitnessCertificate.RequiredPlatformLevel switch
                {
                    0 => LocomotionMode.Handcart,
                    1 => LocomotionMode.Bicycle,
                    _ => LocomotionMode.Scooter,
                };
                var profile = PlatformProfile.ForMode(platformMode);
                var storageOk = offer.EstimatedStorageUnits <= profile.VirtualStorageCapacityUnits;
                var timeOk = offer.EstimatedRouteDurationSeconds <= offer.Contract.Deadline.DueAfterSeconds &&
                             offer.Contract.Deadline.DueAfterSeconds < offer.Contract.Deadline.ExpiresAfterSeconds;

                var serialized = JsonSerializer.Serialize(new
                {
                    schema_version = 1,
                    id = offer.Contract.Id,
                    title_key = offer.Contract.TitleKey,
                    client_id = offer.Contract.ClientId,
                    region_id = offer.Contract.RegionId,
                    area_id = offer.Contract.AreaId,
                    product = offer.Contract.Product,
                    required_feature_ids = offer.Contract.RequiredFeatureIds,
                    requirements = offer.Contract.Requirements.Select<ContractRequirementSpec, object>(r => r switch
                    {
                        ThresholdRequirementSpec tr => new
                        {
                            kind = "threshold",
                            metric = tr.Metric,
                            unit = tr.Unit,
                            direction = tr.Direction,
                            limit = tr.Limit,
                            excellent = tr.Excellent,
                            surface_id = tr.SurfaceId,
                            cell_size_m = tr.CellSizeMetres,
                        },
                        PredicateRequirementSpec pr => new
                        {
                            kind = "predicate",
                            metric = pr.Metric,
                            expected = pr.Expected,
                        },
                        _ => new { kind = r.Kind, metric = r.Metric },
                    }),
                    required_capabilities = offer.Contract.RequiredCapabilities,
                    risk = offer.Contract.Risk,
                    calm = offer.Contract.Calm,
                    deadline = new
                    {
                        due_after_s = offer.Contract.Deadline.DueAfterSeconds,
                        expires_after_s = offer.Contract.Deadline.ExpiresAfterSeconds,
                        late_penalty_per_hour = offer.Contract.Deadline.LatePenaltyPerHour,
                        late_penalty_cap = offer.Contract.Deadline.LatePenaltyCap,
                    },
                    reward = new
                    {
                        base_coords = offer.Contract.Reward.BaseCoords,
                        base_xp = offer.Contract.Reward.BaseXp,
                        reputation = offer.Contract.Reward.Reputation,
                        story_unlocks = offer.Contract.Reward.StoryUnlocks,
                    },
                    partial_policy = new { mode = "disabled" },
                    delivery_format = offer.Contract.DeliveryFormat,
                    story_prerequisites = offer.Contract.StoryPrerequisites,
                },
                new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });

                using var serializedDoc = JsonDocument.Parse(serialized);
                var diagnostics = ContractValidator.Validate(serializedDoc.RootElement, $"{offer.Contract.Id}.json");
                var schemaValid = diagnostics.Count == 0;

                if (!storageOk) allFleetOk = false;
                if (!timeOk) allWitnessOk = false;
                if (!schemaValid) allSchemaOk = false;

                if (storageOk && timeOk && schemaValid)
                {
                    passedCount++;
                    totalPassed++;
                }
            }

            results.Add(new TemplateSeedValidationReport(
                template.TemplateId,
                SeedsTested: 100,
                SeedsPassed: passedCount,
                AllCompatibleWithFleet: allFleetOk,
                AllWitnessRoutesFeasible: allWitnessOk,
                AllSchemaValid: allSchemaOk));
        }

        return new OneHundredSeedsPerTemplateReport(
            SchemaVersion: 1,
            TemplatesValidated: Templates.Count,
            TotalContractsGenerated: totalGenerated,
            TotalContractsPassed: totalPassed,
            AllPassed: totalPassed == totalGenerated && totalGenerated == 500,
            MapSha256: currentMapSha256,
            ProfileVersion: currentProfileVersion,
            ByTemplate: Array.AsReadOnly(results.ToArray()));
    }

    public static IReadOnlyList<GeneratedContractOffer> GenerateAvailableBoard(
        int balanceCoords,
        int fineDebtCoords,
        int ownedPlatformLevel,
        ulong seed)
    {
        var board = new List<GeneratedContractOffer>();

        foreach (var template in Templates)
        {
            var offer = GenerateFromTemplate(template.TemplateId, seed);
            var levelOk = offer.WitnessCertificate.RequiredPlatformLevel <= ownedPlatformLevel;
            if (!levelOk)
                continue;

            // Even at low balance / active fine debt, Calm starter-compatible contracts remain unconditionally offered
            if (balanceCoords <= 0 || fineDebtCoords > 0)
            {
                if (!offer.Contract.Calm && !offer.ExecutableWithStarterKit)
                    continue;
            }

            board.Add(offer);
        }

        if (!board.Any(o => o.Contract.Calm && o.ExecutableWithStarterKit))
        {
            var guaranteedCalm = GenerateFromTemplate("PROC-PARQUE", seed + 1UL, forceCalmVariant: true);
            board.Insert(0, guaranteedCalm);
        }

        return Array.AsReadOnly(board.ToArray());
    }

    private static ulong HashStep(ulong seed, int index)
    {
        var x = seed ^ ((ulong)index * 0x9E3779B97F4A7C15UL + 0x517cc1b727220a95UL);
        x ^= x >> 30;
        x *= 0xbf58476d1ce4e5b9UL;
        x ^= x >> 27;
        x *= 0x94d049bb133111ebUL;
        return x ^ (x >> 31);
    }
}