using System.Text.Json;
using System.Text.Json.Serialization;
using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Camera;
using Gsa.Core.Sensors.Lidar;
using Gsa.Core.Traffic;
using Gsa.Core.World;
using Gsa.Missions.Content;
using Gsa.Missions.Contracts;
using Gsa.Missions.Economy;
using Gsa.Missions.Persistence;
using Gsa.Missions.Quality;

namespace Gsa.Missions.Campaign;

public sealed record Act3CampaignPlaythroughReport(
    bool Passed,
    bool CalmCityEnabled,
    bool CutscenesSkipped,
    bool Act1Passed,
    bool Act2Passed,
    bool InterActSavesVerified,
    bool Hist10Passed,
    bool Hist11Passed,
    bool VanUnlocked,
    bool CloudFilterUnlocked,
    bool MasterSealUnlocked,
    bool OnTimeNoPenaltyVerified,
    bool LatePenaltyVerified,
    bool HardExpiryRejected,
    bool PostGameAndCutsceneIdempotent,
    int FinalBalanceCoords,
    int GlobalReputation,
    CampaignSaveProgressState FinalProgressState,
    PortWorksReview Hist10Review,
    FinalCityPavementReview Hist11Review);

/// <summary>Generates and validates PROC-PAVIMENTO municipal L6 pavement profile contracts across the 4 city regions (T-F10-05).</summary>
public static class Act3PavementProceduralGenerator
{
    public const string TemplateId = "PROC-PAVIMENTO";
    public const string ClientId = "city_hall";
    public const string Product = "pavement_profile";
    public const string TitleKey = "proc.template.pavement.title";

    private sealed record PavementRouteRegion(string RegionId, RouteWitnessCertificate Witness);

    private static readonly PavementRouteRegion[] Regions =
    [
        new("old_center", new("pavimento_old_center_corridor", "pavement_corridor_old_center", "old_center_ring_surface", 6, ["lidar", "camera"], 900.0, 160, Act2AdvancedProceduralGenerator.DefaultCityMapSha256, Act2AdvancedProceduralGenerator.CurrentProfileVersion)),
        new("port", new("pavimento_port_corridor", "pavement_corridor_port", "port_tunnel_mouth_surface", 6, ["lidar", "camera"], 1050.0, 180, Act2AdvancedProceduralGenerator.DefaultCityMapSha256, Act2AdvancedProceduralGenerator.CurrentProfileVersion)),
        new("waterfront", new("pavimento_waterfront_corridor", "pavement_corridor_waterfront", "waterfront_promenade_surface", 6, ["lidar", "camera"], 1100.0, 180, Act2AdvancedProceduralGenerator.DefaultCityMapSha256, Act2AdvancedProceduralGenerator.CurrentProfileVersion)),
        new("serra", new("pavimento_serra_corridor", "pavement_corridor_serra", "serra_works_surface", 6, ["lidar", "camera"], 1350.0, 210, Act2AdvancedProceduralGenerator.DefaultCityMapSha256, Act2AdvancedProceduralGenerator.CurrentProfileVersion)),
    ];

    public static IReadOnlyList<RouteWitnessCertificate> RouteVariants { get; } =
        Array.AsReadOnly(Regions.Select(r => r.Witness).ToArray());

    public static GeneratedContractOffer GenerateFromPavementTemplate(
        ulong seed,
        string? regionIdOverride = null,
        bool forceCalmVariant = false,
        string currentMapSha256 = Act2AdvancedProceduralGenerator.DefaultCityMapSha256,
        string currentProfileVersion = Act2AdvancedProceduralGenerator.CurrentProfileVersion)
    {
        PavementRouteRegion selected;
        if (string.IsNullOrWhiteSpace(regionIdOverride))
        {
            selected = Regions[(int)(seed % (ulong)Regions.Length)];
        }
        else
        {
            selected = Regions.FirstOrDefault(r => string.Equals(r.RegionId, regionIdOverride, StringComparison.Ordinal))
                ?? throw new ArgumentException($"Unknown region '{regionIdOverride}' for PROC-PAVIMENTO.", nameof(regionIdOverride));
        }

        var witness = selected.Witness;
        if (!witness.IsValidFor(currentMapSha256, currentProfileVersion))
            throw new InvalidOperationException($"Route witness certificate '{witness.VariantId}' is invalidated for current map/profile hash.");

        var dueSeconds = Math.Max(18000.0, Math.Ceiling(witness.EstimatedRouteDurationSeconds * 5.0));
        var expiresSeconds = dueSeconds + 7200.0;
        var isCalm = forceCalmVariant || (seed % 3UL == 0UL);
        var baseReward = 9200 + (int)(seed % 5UL) * 150;

        ContractRequirementSpec[] reqs =
        [
            new ThresholdRequirementSpec("area_coverage", "ratio", "min", 0.90, 0.98, witness.SurfaceId, 1.0),
            new ThresholdRequirementSpec("valid_image_fraction", "ratio", "min", 0.90, 0.99),
            new ThresholdRequirementSpec("absolute_rmse", "m", "max", 0.020, 0.008),
            new ThresholdRequirementSpec("relative_rmse", "m", "max", 0.015, 0.005),
            new PredicateRequirementSpec("privacy_redacted", true),
            new PredicateRequirementSpec("format_valid", true),
        ];

        var def = new ContractDefinition(
            SchemaVersion: 1,
            Id: $"proc_pavimento_{witness.VariantId}_{seed}",
            TitleKey: TitleKey,
            ClientId: ClientId,
            RegionId: selected.RegionId,
            AreaId: witness.AreaId,
            Product: Product,
            RequiredFeatureIds: [],
            Requirements: reqs,
            RequiredCapabilities: witness.RequiredCapabilities,
            Risk: isCalm ? "easy" : "moderate",
            Calm: isCalm,
            Deadline: new ContractDeadlineSpec(dueSeconds, expiresSeconds, 0.05, 0.50),
            Reward: new ContractRewardSpec(baseReward, 240, 3, []),
            PartialPolicy: new ContractPartialPolicySpec("disabled", []),
            DeliveryFormat: "gsa_bundle_v1",
            StoryPrerequisites: []);

        return new GeneratedContractOffer(
            TemplateId,
            witness.VariantId,
            seed,
            def,
            witness,
            ExecutableWithStarterKit: false,
            (int)witness.EstimatedRouteDurationSeconds,
            witness.RequiredStorageUnits);
    }

    public static OneHundredSeedsPerTemplateReport ValidateOneHundredSeedsForPavementTemplate(
        ulong baseSeed = 4000UL,
        string currentMapSha256 = Act2AdvancedProceduralGenerator.DefaultCityMapSha256,
        string currentProfileVersion = Act2AdvancedProceduralGenerator.CurrentProfileVersion)
    {
        var passedCount = 0;
        var allFleetOk = true;
        var allWitnessOk = true;
        var allSchemaOk = true;
        var vanStorage = PlatformProfile.Van.VirtualStorageCapacityUnits;

        for (var s = 0; s < 100; s++)
        {
            var seed = baseSeed + (ulong)s * 41UL;
            var offer = GenerateFromPavementTemplate(seed, null, false, currentMapSha256, currentProfileVersion);

            var storageOk = offer.EstimatedStorageUnits <= vanStorage;
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
                passedCount++;
        }

        var templateReport = new TemplateSeedValidationReport(
            TemplateId,
            SeedsTested: 100,
            SeedsPassed: passedCount,
            AllCompatibleWithFleet: allFleetOk,
            AllWitnessRoutesFeasible: allWitnessOk,
            AllSchemaValid: allSchemaOk);

        return new OneHundredSeedsPerTemplateReport(
            SchemaVersion: 1,
            TemplatesValidated: 1,
            TotalContractsGenerated: 100,
            TotalContractsPassed: passedCount,
            AllPassed: passedCount == 100,
            MapSha256: currentMapSha256,
            ProfileVersion: currentProfileVersion,
            ByTemplate: [templateReport]);
    }

    public static IReadOnlyList<GeneratedContractOffer> GenerateFullCampaignBoard(
        int balanceCoords,
        int fineDebtCoords,
        int ownedPlatformLevel,
        IReadOnlySet<string> unlockedRegions,
        ulong seed)
    {
        ArgumentNullException.ThrowIfNull(unlockedRegions);
        var board = Act2AdvancedProceduralGenerator
            .GenerateAvailableBoardWithAdvancedTemplates(balanceCoords, fineDebtCoords, ownedPlatformLevel, unlockedRegions, seed)
            .ToList();

        if (ownedPlatformLevel >= 6)
        {
            var forceCalm = balanceCoords <= 0 || fineDebtCoords > 0;
            for (var i = 0; i < Regions.Length; i++)
            {
                var regionId = Regions[i].RegionId;
                if (!unlockedRegions.Contains(regionId))
                    continue;
                board.Add(GenerateFromPavementTemplate(seed + (ulong)i, regionIdOverride: regionId, forceCalmVariant: forceCalm));
            }
        }

        return Array.AsReadOnly(board.ToArray());
    }
}

/// <summary>Executes the complete 3-act campaign (HIST-01..HIST-11) with inter-act saves, 24h/30h finale deadlines, calm-mode parity, and idempotent postgame unlock (T-F10-05).</summary>
public static class Act3AndFullCampaignRunner
{
    public static Act3CampaignPlaythroughReport RunAct3AndFullCampaignPlaythrough(
        string contentDirectory,
        string worldManifestPath,
        bool calmCityEnabled = false,
        bool skipCutscenes = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(worldManifestPath);

        // Act 1 closure (HIST-01..04)
        var act1 = Act1ClosureRunner.RunAct1Closure(contentDirectory, worldManifestPath);
        if (!act1.Passed)
            throw new InvalidOperationException("Act 1 must pass before Act 2/3 progression.");

        var tutorialManifest = WorldSceneManifest.Parse(File.ReadAllText(worldManifestPath));
        var serraManifest = SerraWorldAssembler.BuildSerraExpandedManifest(tutorialManifest);

        var progress = new CampaignSaveProgressState();
        foreach (var id in new[] { "HIST-01", "HIST-02", "HIST-03", "HIST-04" })
            ApplyCutscene(FullCampaignCatalog.GetMission(id).Cutscene, progress, skipCutscenes);

        var ledger = new CareerLedger(act1.FinalLedgerCoords);
        var progression = CareerProgressionState.FromSnapshot(new CareerProgressionSnapshot(
            SchemaVersion: 1,
            TotalXp: 720,
            new Dictionary<string, int>(StringComparer.Ordinal) { ["city_hall"] = act1.GlobalReputation },
            new Dictionary<string, int>(StringComparer.Ordinal) { ["old_center"] = act1.GlobalReputation },
            ["HIST-01", "HIST-02", "HIST-03", "HIST-04"],
            ["HIST-01", "HIST-02", "HIST-03", "HIST-04"]));
        var fleet = new FleetProgressionManager();
        var custody = new BatchCustodyStore();

        var tempDir = Path.Combine(Path.GetTempPath(), "gsa-act3-campaign-" + Guid.NewGuid().ToString("N"));
        try
        {
            var saveManager = new SaveGameManager(tempDir);

            // Save & restore between Act 1 and Act 2
            var act1SaveOk = RoundTripCampaignSave(saveManager, SaveTrigger.MissionEnded, 3600.0, ledger, progression, custody, progress, out progress);

            // Act 2 closure (HIST-05..09)
            var act2 = Act2CampaignRunner.RunAct2Playthrough(serraManifest, calmCityEnabled);
            if (!act2.Passed || !act2.Act3Unlocked)
                throw new InvalidOperationException("Act 2 must unlock Act 3.");

            foreach (var id in new[] { "HIST-05", "HIST-06", "HIST-07", "HIST-08", "HIST-09" })
                ApplyCutscene(FullCampaignCatalog.GetMission(id).Cutscene, progress, skipCutscenes);

            ledger.RecordActivityReward("act2_payout_summary", "campaign_act2", 7200.0, act2.TotalRewardCoords, "act2", "campaign.act2.summary");
            foreach (var m in act2.Missions)
            {
                var mSpec = FullCampaignCatalog.GetMission(m.MissionId);
                var mDef = BuildStoryContract(mSpec, mSpec.RegionIds[0], [new PredicateRequirementSpec("format_valid", true)], [], 21600, 27000, calmCityEnabled);
                var mQuote = new PaymentQuote(mDef.Id, true, false, false, 5, 1.0, 1.0, 0.0, 0.0,
                    m.RewardCoords, m.RewardCoords, m.RewardCoords, 240, m.ReputationDelta, [], mDef.Reward.StoryUnlocks, null);
                progression.ApplySettlement(mDef, mQuote);
            }

            // Save & restore between Act 2 and Act 3
            var act2SaveOk = RoundTripCampaignSave(saveManager, SaveTrigger.MissionEnded, 7200.0, ledger, progression, custody, progress, out progress);
            var interActSavesOk = act1SaveOk && act2SaveOk;

            // Act 3 - HIST-10 (Port Works Clandestine Perimeter, Kombi L4, unlocks Van L6)
            var hist10Spec = FullCampaignCatalog.GetMission(PortWorksSurvey.MissionId);
            var hist10Def = BuildStoryContract(hist10Spec, PortWorksSurvey.AreaId, PortWorksQuality.Requirements, ["lidar_l4"], 21600, 27000, calmCityEnabled);
            var hist10Machine = new ContractStateMachine(hist10Def);
            const double hist10Start = 10000.0;
            hist10Machine.Accept(hist10Start);

            var calmPolicy = new CalmPolicyCoordinator(calmCityEnabled);
            var sabotage = new FactionSabotageAndCountermeasuresCoordinator();
            var uploadAmbush = sabotage.TriggerIdempotentSabotageAttack(
                "hist10_upload_interception",
                PortWorksSurvey.ZoneId,
                SabotageAttackKind.RoadblockOrInterception,
                custody,
                hist10Start + 30.0,
                calmPolicy);
            var calmWorksOk = !calmCityEnabled || (uploadAmbush.SuppressedByCalmCity && calmPolicy.IsProtectionActive);

            var worksSlices = BuildPortWorksSyntheticSlices(hist10Start + 60.0);
            var worksBatchId = "batch_hist10_works_01";
            var worksArtifact = new SurveyBatchArtifact(
                1, worksBatchId, hist10Def.Id, hist10Start + 60.0, hist10Start + 180.0,
                CityLidarSlice.StorageUnits(worksSlices.Sum(s => s.Returns.Count)), 0, CaptureStopReason.None,
                ["lidar-bench"], [], [], [], [])
            {
                LidarSlices = worksSlices,
                WorksFieldPad = 0,
            };
            custody.RegisterBufferedBatch(worksArtifact, hist10Start + 180.0);
            custody.OffloadAtomic(worksBatchId, hist10Start + 190.0);
            hist10Machine.RecordPendingBatch(hist10Start + 180.0, worksBatchId);
            hist10Machine.ProtectBatchAtUnloadPoint(hist10Start + 190.0, worksBatchId);
            hist10Machine.MarkReadyToProcess(hist10Start + 200.0);
            hist10Machine.StartProcessing(hist10Start + 205.0);
            hist10Machine.CompleteProcessing(hist10Start + 210.0);

            var worksCloud = new PointCloudOctreeAndChunkStore();
            worksCloud.IngestRevision(worksSlices.SelectMany(s => s.Georeference()));
            var hist10Review = PortWorksQuality.Evaluate(hist10Def.Id, [worksBatchId], worksCloud, fieldUploads: 1);
            hist10Machine.RecordQualityEvaluation(hist10Start + 220.0, hist10Review.Delivery.Passed,
                primaryFailureKey: hist10Review.Delivery.FailureReasonKeys.FirstOrDefault());

            var hist10Settlement = ContractSettlementCoordinator.SettleDelivery(hist10Machine, ledger, progression, hist10Review.Delivery, hist10Start + 230.0);
            var hist10Passed = hist10Review.Delivery.Passed && hist10Settlement.Succeeded && calmWorksOk;
            ApplyCutscene(hist10Spec.Cutscene, progress, skipCutscenes);

            var vanUnlockedByStory = progress.UnlockedPlatforms.Contains("van");
            var vanQuote = fleet.QuotePlatformPurchase(LocomotionMode.Van, progression, new HashSet<string>(["HIST-10"], StringComparer.Ordinal));
            var vanUnlocked = vanUnlockedByStory && vanQuote.Unlocked;

            // Act 3 - HIST-11 (Final City Pavement Survey across 4 sectors, Van L6, 24h recommended / 30h hard expiry)
            var hist11Spec = FullCampaignCatalog.GetMission(FinalCityPavementSurvey.MissionId);
            var hist11Def = BuildStoryContract(hist11Spec, FinalCityPavementSurvey.AreaId, FinalCityPavementQuality.Requirements,
                ["lidar_l6", "camera"], FinalCityPavementSurvey.DueSeconds, FinalCityPavementSurvey.ExpirySeconds, calmCityEnabled);

            const double hist11Start = 20000.0;
            var hist11Batches = new List<SurveyBatchArtifact>(4);
            var hist11Machine = new ContractStateMachine(hist11Def);
            hist11Machine.Accept(hist11Start);

            for (var sIdx = 0; sIdx < FinalCityPavementSurvey.Sectors.Count; sIdx++)
            {
                var sector = FinalCityPavementSurvey.Sectors[sIdx];
                var slices = BuildPavementSectorSyntheticSlices(sector, sIdx, hist11Start + 100.0 + sIdx * 200.0);
                var batchId = $"batch_hist11_{sector.Manifest.SectorId}";
                var artifact = new SurveyBatchArtifact(
                    1, batchId, hist11Def.Id, hist11Start + 100.0 + sIdx * 200.0, hist11Start + 160.0 + sIdx * 200.0,
                    CityLidarSlice.StorageUnits(slices.Sum(s => s.Returns.Count)), 0, CaptureStopReason.None,
                    ["lidar-l6", "van_pavement_down"], [], [], [], [])
                {
                    LidarSlices = slices,
                    FinalSurveyUploadSectorId = sector.Manifest.SectorId,
                };
                custody.RegisterBufferedBatch(artifact, hist11Start + 160.0 + sIdx * 200.0);
                custody.OffloadAtomic(batchId, hist11Start + 170.0 + sIdx * 200.0);
                hist11Machine.RecordPendingBatch(hist11Start + 160.0 + sIdx * 200.0, batchId);
                hist11Machine.ProtectBatchAtUnloadPoint(hist11Start + 170.0 + sIdx * 200.0, batchId);
                hist11Batches.Add(artifact);
            }

            hist11Machine.MarkReadyToProcess(hist11Start + 1200.0);
            hist11Machine.StartProcessing(hist11Start + 1210.0);
            hist11Machine.CompleteProcessing(hist11Start + 1220.0);
            var hist11Review = FinalCityPavementQuality.Evaluate(hist11Batches);
            hist11Machine.RecordQualityEvaluation(hist11Start + 1230.0, hist11Review.Delivery.Passed,
                primaryFailureKey: hist11Review.Delivery.FailureReasonKeys.FirstOrDefault());

            // Verify 24h recommended vs 25h late penalty vs 30h hard expiry
            var onTimeQuote = DeliveryQuoteCalculator.Calculate(hist11Def, hist11Review.Delivery, hist11Start, hist11Start + FinalCityPavementSurvey.DueSeconds);
            var lateQuote = DeliveryQuoteCalculator.Calculate(hist11Def, hist11Review.Delivery, hist11Start, hist11Start + FinalCityPavementSurvey.DueSeconds + 3600.0);
            var onTimeOk = onTimeQuote.Eligible && onTimeQuote.LatePenaltyFraction == 0.0;
            var lateOk = lateQuote.Eligible && Math.Abs(lateQuote.LatePenaltyFraction - 0.05) < 1e-9;

            var expiredMachine = new ContractStateMachine(hist11Def);
            expiredMachine.Accept(hist11Start);
            expiredMachine.AdvanceClock(hist11Start + FinalCityPavementSurvey.ExpirySeconds + 1.0);
            var expiredRejected = expiredMachine.State == ContractState.Expirado &&
                !ContractSettlementCoordinator.SettleDelivery(expiredMachine, new CareerLedger(0), new CareerProgressionState(),
                    hist11Review.Delivery, hist11Start + FinalCityPavementSurvey.ExpirySeconds + 1.0).Succeeded;

            var hist11Settlement = ContractSettlementCoordinator.SettleDelivery(hist11Machine, ledger, progression, hist11Review.Delivery, hist11Start + 1300.0);
            var hist11Passed = hist11Review.Delivery.Passed && hist11Settlement.Succeeded;

            ApplyCutscene(hist11Spec.Cutscene, progress, skipCutscenes);
            var balanceBeforeReplay = ledger.BalanceCoords;
            var replaySettlement = ContractSettlementCoordinator.SettleDelivery(hist11Machine, ledger, progression, hist11Review.Delivery, hist11Start + 1310.0);
            var replayCutsceneApplied = ApplyCutscene(hist11Spec.Cutscene, progress, skipCutscenes);
            var postGameIdempotent = !replayCutsceneApplied &&
                                     (!replaySettlement.Succeeded || (replaySettlement.WasIdempotentReplay && replaySettlement.LedgerResult?.Status == LedgerCommandStatus.IdempotentNoOp)) &&
                                     ledger.BalanceCoords == balanceBeforeReplay;

            var cloudUnlocked = progress.UnlockedEquipment.Contains(FinalCityPavementSurvey.CloudFilterUnlock);
            var sealUnlocked = progress.UnlockedEquipment.Contains("master_geodesist_seal");

            var passed = act1.Passed && act2.Passed && interActSavesOk && hist10Passed && hist11Passed &&
                         vanUnlocked && cloudUnlocked && sealUnlocked &&
                         onTimeOk && lateOk && expiredRejected && postGameIdempotent;

            return new Act3CampaignPlaythroughReport(
                Passed: passed,
                CalmCityEnabled: calmCityEnabled,
                CutscenesSkipped: skipCutscenes,
                Act1Passed: act1.Passed,
                Act2Passed: act2.Passed,
                InterActSavesVerified: interActSavesOk,
                Hist10Passed: hist10Passed,
                Hist11Passed: hist11Passed,
                VanUnlocked: vanUnlocked,
                CloudFilterUnlocked: cloudUnlocked,
                MasterSealUnlocked: sealUnlocked,
                OnTimeNoPenaltyVerified: onTimeOk,
                LatePenaltyVerified: lateOk,
                HardExpiryRejected: expiredRejected,
                PostGameAndCutsceneIdempotent: postGameIdempotent,
                FinalBalanceCoords: ledger.BalanceCoords,
                GlobalReputation: progression.GlobalReputation,
                FinalProgressState: progress,
                Hist10Review: hist10Review,
                Hist11Review: hist11Review);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    private static bool ApplyCutscene(CutsceneSpec cutscene, CampaignSaveProgressState state, bool skip) =>
        skip ? CutsceneSkipAndStateCoordinator.SkipCutscene(cutscene, state)
             : CutsceneSkipAndStateCoordinator.ExecuteFull(cutscene, state);

    private static bool RoundTripCampaignSave(
        SaveGameManager manager,
        SaveTrigger trigger,
        double simTimeSeconds,
        CareerLedger ledger,
        CareerProgressionState progression,
        BatchCustodyStore custody,
        CampaignSaveProgressState progress,
        out CampaignSaveProgressState restoredProgress)
    {
        var state = new SaveGameState(
            SaveGameManager.CurrentSchemaVersion,
            trigger,
            0,
            new SaveClockState(simTimeSeconds, simTimeSeconds),
            new SaveRngState(901UL, 1UL, 42),
            ledger.ExportState(),
            progression.ExportSnapshot(),
            new SaveEquipmentMountState("car", ["handcart", "bicycle", "scooter", "car"], new Dictionary<string, string>()),
            custody.ExportSnapshot(),
            [],
            Content: SaveContentVersions.Current);

        var sv = manager.Save(state, trigger);
        var ld = manager.LoadCurrent();
        var runtime = ld.Succeeded && ld.State is not null ? SaveGameManager.RestoreRuntime(ld.State) : null;

        var progressJson = JsonSerializer.Serialize(new SaveCampaignPresentationState(
            progress.UnlockedPlatforms.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            progress.UnlockedRegions.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            progress.UnlockedEquipment.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            progress.AppliedCutsceneIds.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            progress.SkippedCutsceneIds.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            new Dictionary<string, string>(progress.ObjectiveStates, StringComparer.Ordinal)));

        var dto = JsonSerializer.Deserialize<SaveCampaignPresentationState>(progressJson)
                  ?? throw new InvalidOperationException("Campaign progress round-trip deserialization failed.");
        restoredProgress = new CampaignSaveProgressState(dto.Platforms, dto.Regions, dto.Equipment);
        foreach (var c in dto.AppliedCutscenes) restoredProgress.AppliedCutsceneIds.Add(c);
        foreach (var c in dto.SkippedCutscenes) restoredProgress.SkippedCutsceneIds.Add(c);
        foreach (var kv in dto.Objectives) restoredProgress.ObjectiveStates[kv.Key] = kv.Value;

        return sv.Succeeded && ld.Succeeded && runtime is not null &&
               runtime.Ledger.BalanceCoords == ledger.BalanceCoords &&
               restoredProgress.Equals(progress);
    }

    private static ContractDefinition BuildStoryContract(
        CampaignMissionFullSpec spec,
        string areaId,
        IReadOnlyList<ContractRequirementSpec> requirements,
        IReadOnlyList<string> capabilities,
        double dueSeconds,
        double expiresSeconds,
        bool calmCity) =>
        new(
            SchemaVersion: 1,
            Id: spec.MissionId,
            TitleKey: spec.TitleKey,
            ClientId: "city_hall",
            RegionId: spec.RegionIds[0],
            AreaId: areaId,
            Product: spec.ProductType,
            RequiredFeatureIds: [],
            Requirements: requirements,
            RequiredCapabilities: capabilities,
            Risk: calmCity ? "easy" : "moderate",
            Calm: calmCity,
            Deadline: new ContractDeadlineSpec(dueSeconds, expiresSeconds, 0.05, 0.50),
            Reward: new ContractRewardSpec(spec.RewardCoords, 120 + spec.Act * 60, spec.ReputationDelta, spec.UnlockedPlatforms.ToArray()),
            PartialPolicy: new ContractPartialPolicySpec("disabled", []),
            DeliveryFormat: "gsa_bundle_v1",
            StoryPrerequisites: spec.Preconditions.ToArray());

    private static CityLidarSlice[] BuildPortWorksSyntheticSlices(double startTime)
    {
        var slices = new List<CityLidarSlice>(40);
        long tick = 60;
        var mount = new LidarMountEstimate(Vector3d.Zero, Quaterniond.Identity, 0.0);

        foreach (var side in PortWorksSurvey.Sides)
        {
            var length = (side.End - side.Start).Length;
            var dir = (side.End - side.Start) / length;
            var perp = new Vector3d(-dir.Y, dir.X, 0.0);
            for (var d = 0.5; d < length; d += 1.0)
            {
                var center = side.Start + dir * d;
                var posePos = new Vector3d(center.X, center.Y, PortWorksSurvey.RoadUp);
                var pose = new EstimatedBodyPose(startTime + tick * 0.1, posePos, Quaterniond.Identity);
                var returns = new List<LidarMeasuredReturn>(24);

                var nearCheck = (center - side.Midpoint).Length <= 0.75;
                foreach (var ix in new[] { -0.35, -0.10, 0.10, 0.35 })
                {
                    foreach (var iy in new[] { -1.6, -1.1, -0.45, 0.45, 1.1, 1.6 })
                    {
                        var targetZ = nearCheck && Math.Abs(iy) < 0.7 ? side.PanelHeight : 0.0;
                        var offset = dir * ix + perp * iy + Vector3d.UnitZ * targetZ;
                        var range = offset.Length;
                        var az = Math.Atan2(offset.Y, offset.X);
                        var el = Math.Asin(Math.Clamp(offset.Z / range, -1.0, 1.0));
                        returns.Add(new LidarMeasuredReturn(az, el, range, 0.78, 1, 1, false, offset, posePos + offset));
                    }
                }
                slices.Add(CityLidarSlice.Capture(tick++, LidarProfile.L4.Id, pose, mount, false, returns));
            }
        }
        return slices.ToArray();
    }

    private static CityLidarSlice[] BuildPavementSectorSyntheticSlices(FinalCityPavementSector sector, int sectorIdx, double startTime)
    {
        var slices = new List<CityLidarSlice>(FinalCityPavementSurvey.Length);
        var mount = new LidarMountEstimate(Vector3d.Zero, Quaterniond.Identity, 0.0);
        const int interval = FinalCityPavementSurvey.CameraTickInterval;

        for (var st = 0; st < FinalCityPavementSurvey.Length; st++)
        {
            var tick = (long)(sectorIdx * 2000 + (st + 1) * interval);
            var time = tick / (double)CameraCaptureScheduler.PhysicsTicksPerSecond;
            var centerX = sector.StartEast + st + 0.5;
            var centerZ = sector.SurfaceElevation(centerX, sector.North);
            var posePos = new Vector3d(centerX, sector.North, centerZ);
            var pose = new EstimatedBodyPose(time, posePos, Quaterniond.Identity);

            var returns = new List<LidarMeasuredReturn>(99);
            // 9 longitudinal x 11 transverse points (with 3 inside strip 0 [-0.125, +0.125]) -> 39.6 pts/m^2 and >= 15 pts/station
            double[] yOffsets = [-1.1, -0.85, -0.6, -0.35, -0.08, 0.0, 0.08, 0.35, 0.6, 0.85, 1.1];
            for (var ix = 0; ix < 9; ix++)
            {
                var x = sector.StartEast + st + 0.06 + ix * 0.11;
                foreach (var dy in yOffsets)
                {
                    var y = sector.North + dy;
                    var z = sector.SurfaceElevation(x, y);
                    var targetPt = new Vector3d(x, y, z);
                    var delta = targetPt - posePos;
                    var range = Math.Max(0.05, delta.Length);
                    var az = Math.Atan2(delta.Y, delta.X);
                    var el = Math.Asin(Math.Clamp(delta.Z / range, -1.0, 1.0));
                    returns.Add(new LidarMeasuredReturn(az, el, range, 0.85, 1, 1, false, delta, targetPt));
                }
            }

            var frame = new PavementCameraFootprintObservation(
                SampleId: $"final_camera_{tick}",
                CameraSlotId: "van_pavement_down",
                TimestampSeconds: time,
                ChainageMetres: st + 0.5,
                TransverseStripIndex: 0,
                TransverseOffsetMetres: 0.0,
                EstimatedElevationMetres: centerZ,
                ObservedRutDepthMillimetres: 0.0,
                LensObstructionFraction: 0.0,
                IsOccluded: false);

            slices.Add(CityLidarSlice.Capture(tick, LidarProfile.L6.Id, pose, mount, false, returns) with
            {
                PavementFrame = frame,
            });
        }
        return slices.ToArray();
    }
}
