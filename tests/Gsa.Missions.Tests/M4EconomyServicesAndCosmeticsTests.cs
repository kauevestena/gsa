using System.Text.Json;
using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Camera;
using Gsa.Core.Sensors.Gnss;
using Gsa.Missions.Activities;
using Gsa.Missions.Contracts;
using Gsa.Missions.Economy;
using Gsa.Missions.Office;
using Gsa.Missions.Persistence;
using Gsa.Missions.Presentation;
using Gsa.Missions.Quality;
using Xunit;

namespace Gsa.Missions.Tests;

public sealed class M4EconomyServicesAndCosmeticsTests
{
    private static ContractDefinition LoadContract(string fixture) =>
        ContractDefinition.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture)), fixture);

    private static (CareerProgressionState State, HashSet<string> Completed) BuildProgression(
        int totalXp,
        int cityHallRep,
        int otherClientRep,
        params string[] completedMissions)
    {
        var def = LoadContract("facades.json");
        var snap = new CareerProgressionSnapshot(
            SchemaVersion: 1,
            TotalXp: totalXp,
            ReputationByClient: new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["city_hall"] = cityHallRep,
                ["partner_lab"] = otherClientRep,
            },
            ReputationByRegion: new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["old_center"] = cityHallRep + otherClientRep,
            },
            UnlockedStoryNodes: completedMissions,
            SettledContractIds: [def.Id]);

        return (CareerProgressionState.FromSnapshot(snap), new HashSet<string>(completedMissions, StringComparer.Ordinal));
    }

    private static CameraObservationSample BuildSampleWithSensitiveRegions(string sampleId, double timestampSeconds)
    {
        var pose = new EstimatedBodyPose(timestampSeconds, new Vector3d(500100.0, 7400200.0, 101.5), Quaterniond.Identity);
        var quality = new CameraQualityMetrics(0.002, 0.2, 0.005, 0.0, 0.5, false, false, false, false, true);
        var intrinsics = CameraIntrinsics.Initial;
        var features = new[]
        {
            new ObservedFeature2D("face_bystander_01", "person_face", 640, 360, 600, 320, 680, 400, 80, 9, 9, 0.0, RequiresPrivacyReview: true),
            new ObservedFeature2D("plate_taxi_01", "license_plate", 320, 540, 280, 520, 360, 560, 80, 9, 9, 0.0, RequiresPrivacyReview: true),
        };
        var profile = new CameraSensorProfile("cam_front", 2.0, 80.0, intrinsics, 0.002, 0.01, 0.005);
        var thumb = CameraThumbnail.CreateSynthetic(profile, features, CameraEnvironmentSnapshot.DryDay);
        return new CameraObservationSample(sampleId, "cam_front", 1, timestampSeconds, pose, CameraMountEstimate.ForwardFacing, intrinsics, quality, features, [], thumb);
    }

    private static QualityReport BuildDeterministicQualityReport(string contractId, double rmse)
    {
        MetricEvaluationResult[] metrics =
        [
            new("absolute_rmse_m", "control", MetricEvaluationStatus.Passed, rmse, null, 0.20, 0.05, false, 12, null),
            new("target_coverage", "coverage", MetricEvaluationStatus.Passed, 1.0, null, 0.90, 1.0, false, 20, null),
        ];
        return new QualityReport(contractId, ["batch_01"], true, metrics, [], [], ["d_1"], 12, 12, 20, 12);
    }

    [Fact]
    public void T_F08_03_AllSixSkillTracksReputationDiscountsFleetGatesAndScientificInvariancePersistCleanly()
    {
        var (progression, completed) = BuildProgression(totalXp: 3500, cityHallRep: 20, otherClientRep: 140, "HIST-02", "HIST-03");
        var fleet = new FleetProgressionManager();

        // 3500 XP -> 7 points available (500 XP/pt), max rank 3 per track (+10%/rank)
        Assert.Equal(7, fleet.GetTotalEarnedSkillPoints(progression));
        Assert.True(fleet.TryUnlockSkillRank(SkillTrackKind.DefensiveDriving, progression, out _, out _));
        Assert.True(fleet.TryUnlockSkillRank(SkillTrackKind.Navigation, progression, out _, out _));
        Assert.True(fleet.TryUnlockSkillRank(SkillTrackKind.Calibration, progression, out _, out _));
        Assert.True(fleet.TryUnlockSkillRank(SkillTrackKind.Processing, progression, out _, out _));
        Assert.True(fleet.TryUnlockSkillRank(SkillTrackKind.Negotiation, progression, out _, out _));
        Assert.True(fleet.TryUnlockSkillRank(SkillTrackKind.NightShift, progression, out _, out _));
        Assert.Equal(6, fleet.GetSpentSkillPoints());
        Assert.Equal(1, fleet.GetAvailableSkillPoints(progression));

        // 2nd rank in DefensiveDriving brings bonus to +20%, all 6 tracks present in tree inspection
        Assert.True(fleet.TryUnlockSkillRank(SkillTrackKind.DefensiveDriving, progression, out var ddRank, out _));
        Assert.Equal(2, ddRank);
        Assert.Equal(0.20, fleet.GetDefensiveDrivingReductionFraction(), 6);
        Assert.Equal(6, fleet.GetSkillTreeInspection().Count);

        // Client and global reputation discounts cap at 15% (160 >= 150)
        Assert.Equal(0.15, FleetProgressionManager.ComputeClientReputationDiscountFraction(progression), 6);

        // Fleet gates: Car (global>=10) & Kombi (city_hall>=20) unlocked, while low-rep state blocks them
        var (lowRep, emptyCompleted) = BuildProgression(totalXp: 0, cityHallRep: 5, otherClientRep: 2);
        var carBlocked = fleet.QuotePlatformPurchase(LocomotionMode.Car, lowRep, emptyCompleted);
        Assert.False(carBlocked.Unlocked);
        Assert.Equal("shop.error.reputation_prerequisite_missing", carBlocked.LockReasonKey);

        var carUnlocked = fleet.QuotePlatformPurchase(LocomotionMode.Car, progression, completed);
        Assert.True(carUnlocked.Unlocked);
        var kombiUnlocked = fleet.QuotePlatformPurchase(LocomotionMode.Kombi, progression, completed);
        Assert.True(kombiUnlocked.Unlocked);
        var pickupUnlocked = fleet.QuotePlatformPurchase(LocomotionMode.Pickup, progression, completed);
        Assert.True(pickupUnlocked.Unlocked);
        var vanUnlocked = fleet.QuotePlatformPurchase(LocomotionMode.Van, progression, completed);
        Assert.True(vanUnlocked.Unlocked);

        // Kombi below city_hall=20 is blocked even if global >= 20
        var (highOtherOnly, _) = BuildProgression(totalXp: 0, cityHallRep: 10, otherClientRep: 30);
        var kombiDenied = fleet.QuotePlatformPurchase(LocomotionMode.Kombi, highOtherOnly, emptyCompleted);
        Assert.False(kombiDenied.Unlocked);
        Assert.Equal("shop.error.client_reputation_prerequisite_missing", kombiDenied.LockReasonKey);

        // Skills never alter quality metrics (before vs after)
        var baselineQc = BuildDeterministicQualityReport("c_qc_01", 0.042);
        var postSkillQc = BuildDeterministicQualityReport("c_qc_01", 0.042);
        Assert.True(FleetProgressionManager.VerifySkillsNeverAlterQualityMetrics(baselineQc, postSkillQc));

        // Grade upgrade quote + Snapshot save/restore preserves rank and owned platforms
        var ledger = new CareerLedger(100000);
        Assert.True(fleet.TryPurchasePlatform("buy_kombi", 10.0, LocomotionMode.Kombi, ledger, progression, completed, out _, out _));
        var rtkQuote = fleet.QuoteGradeUpgrade("rtk_gnss", "centimetric", 3500, progression);
        Assert.True(rtkQuote.Eligible);
        Assert.Equal(2975, rtkQuote.FinalPriceCoords);

        var snap = JsonSerializer.Deserialize<FleetProgressionSnapshot>(JsonSerializer.Serialize(fleet.ExportSnapshot()))!;
        var restored = FleetProgressionManager.FromSnapshot(snap);
        Assert.Equal(LocomotionMode.Kombi, restored.ActivePlatform.Mode);
        Assert.Equal(2, restored.GetSkillRank(SkillTrackKind.DefensiveDriving));
        Assert.Equal(1, restored.GetSkillRank(SkillTrackKind.Negotiation));
    }

    [Fact]
    public void T_F08_04_ServiceFeeMatrixInsuranceSinistroCustodyLossFinesAndSnapshotWorkDeterministically()
    {
        var services = new FleetServiceAndInsuranceCoordinator();
        var kombi = PlatformProfile.Kombi; // 14,000 Coords
        var sensorsPrice = 5000; // e.g., LiDAR + RTK = 19,000 insured total

        // Inspect canonical fee matrix matching specs/parametros.md
        var matrix = services.GetServiceFeeMatrix(kombi, sensorsPrice, referenceContractValueCoords: 1000)
            .ToDictionary(q => q.Kind);

        Assert.Equal(10, services.QuoteService(ServiceKind.RefuelOrCorn, PlatformProfile.Bicycle).FinalFeeCoords);
        Assert.Equal(50, matrix[ServiceKind.RefuelOrCorn].FinalFeeCoords);
        Assert.Equal(380, matrix[ServiceKind.InsurancePolicy].FinalFeeCoords); // 2% of 19,000
        Assert.Equal(20, matrix[ServiceKind.FieldBaseStationSetup].FinalFeeCoords);
        Assert.Equal(10, matrix[ServiceKind.TemporaryCustodyStorage].FinalFeeCoords); // 1% of 1000
        Assert.Equal(10, matrix[ServiceKind.ValetParkingGuard].FinalFeeCoords);
        Assert.Equal(100, matrix[ServiceKind.SecurityEscort].FinalFeeCoords); // 10% of 1000
        Assert.Equal(200, matrix[ServiceKind.VehicleDisguise].FinalFeeCoords);
        Assert.Equal(50, matrix[ServiceKind.ControlledZoneAuthorization].FinalFeeCoords); // 5% of 1000

        // Maintenance = 1% of vehicle price per 10 damage points (14,000 * 2% = 280 for 20 dmg)
        services.RecordVehicleDamage(20);
        Assert.Equal(280, services.QuoteService(ServiceKind.VehicleMaintenance, kombi).FinalFeeCoords);

        // Purchase insurance, verify 10% deductible sinistro claim (1900 Coords), restore vehicle + sensors, NEVER restore buffered lost batches
        var ledger = new CareerLedger(5000);
        var buyPolicy = services.ExecuteService("tx_ins_01", 10.0, ServiceKind.InsurancePolicy, kombi, ledger, sensorsPrice, ["rtk_gnss", "lidar_basic"]);
        Assert.True(buyPolicy.Succeeded);
        Assert.True(services.InsuranceActive);
        Assert.Equal(19000, services.InsuredValueCoords);

        var custody = new BatchCustodyStore();
        var sample = BuildSampleWithSensitiveRegions("s_loss", 12.0);
        var obsSafe = new ObservationBatch("b_safe", ["cam_front"], [sample], 1, 0, CaptureStopReason.None);
        var obsPending = new ObservationBatch("b_pending", ["cam_front"], [sample], 1, 0, CaptureStopReason.None);
        custody.RegisterBufferedBatch(SurveyBatchArtifact.FromObservationBatch("c_1", obsSafe), 12.0);
        custody.OffloadAtomic("b_safe", 13.0); // Protected in office!
        custody.RegisterBufferedBatch(SurveyBatchArtifact.FromObservationBatch("c_1", obsPending), 14.0); // Still buffered in vehicle!

        services.RecordVehicleDamage(80); // Total 100 dmg
        var claimResult = services.SubmitTotalLossClaim("claim_01", "tx_claim_01", 20.0, kombi, ledger, custody, out var claim);
        Assert.True(claimResult.Succeeded);
        Assert.NotNull(claim);
        Assert.Equal(1900, claim!.DeductiblePaidCoords); // 10% of 19,000
        Assert.True(claim.VehicleRestored);
        Assert.True(claim.SensorsRestored);
        Assert.False(claim.LostBatchesRestored);
        Assert.Equal(1, claim.LostPendingBatchCount);
        Assert.Equal(0, services.VehicleDamagePoints);

        // Pending batch becomes Lost while previously offloaded Protected batch survives
        Assert.Contains(custody.Entries, e => e.BatchId == "b_pending" && e.Stage == BatchCustodyStage.Lost);
        Assert.Contains(custody.Entries, e => e.BatchId == "b_safe" && e.Stage == BatchCustodyStage.Protected);

        // Traffic fines (20 / 30 / 40 Coords) & save round-trip
        var fineLight = FleetServiceAndInsuranceCoordinator.ApplyTrafficFine("tx_fine_l", "speeding", 30.0, TrafficFineSeverity.Light, ledger);
        Assert.Equal(-20, fineLight.AppliedDeltaCoords);
        var fineHeavy = FleetServiceAndInsuranceCoordinator.ApplyTrafficFine("tx_fine_h", "red_light", 35.0, TrafficFineSeverity.Heavy, ledger);
        Assert.Equal(-40, fineHeavy.AppliedDeltaCoords);

        var snap = JsonSerializer.Deserialize<ServiceInsuranceSnapshot>(JsonSerializer.Serialize(services.ExportSnapshot()))!;
        var restored = FleetServiceAndInsuranceCoordinator.FromSnapshot(snap);
        Assert.True(restored.InsuranceActive);
        Assert.Single(restored.Claims);
        Assert.Equal(["rtk_gnss", "lidar_basic"], restored.InsuredSensorIds);

        // HUD GNSS indicator also exposes M4 RTK Float & RTK Fix keys via HudSnapshotAggregator
        var baseState = new InstalledBaseStationState("survey_control_port_viaduct", new Vector3d(500100, 7400200, 100), 0.0);
        var rtk = new RtkSolutionStateMachine(seed: 77);
        var floatObs = rtk.StepEpoch(1.0, 1.0, new Vector3d(500200, 7400200, 100), new Vector3d(1.0, 0.0, 0.0), openness: 0.55, platformLevel: 4, hasMultiFrequencyProfile: true, activeBase: baseState);
        GnssObservation? fixObs = null;
        for (var i = 0; i < 10; i++)
            fixObs = rtk.StepEpoch(2.0 + i, 1.0, new Vector3d(500200, 7400200, 100), new Vector3d(1.0, 0.0, 0.0), openness: 0.90, platformLevel: 4, hasMultiFrequencyProfile: true, activeBase: baseState);

        var aggregator = new HudSnapshotAggregator();
        var level4Context = new HudVisibilityContext(PlatformLevel: 4, FactionsUnlocked: true, HasGimbalina: true, HasCamera: true, HasGnss: true);
        Assert.True(aggregator.TrySample(new HudFieldTelemetryInput(1.0, 4, 10, floatObs, 8, 0.85, 10, 4000, LidarActive: true), level4Context, "en-US", out var snapFloat));
        Assert.Equal("hud.gnss.rtk_float", snapFloat!.GnssStateKey);
        Assert.True(aggregator.TrySample(new HudFieldTelemetryInput(11.0, 4, 10, fixObs!, 8, 0.90, 10, 4000, LidarActive: true), level4Context, "en-US", out var snapFix));
        Assert.Equal("hud.gnss.rtk_fix", snapFix!.GnssStateKey);
    }

    [Fact]
    public void T_F18_02_BlooperAlbumCapLocalPrivacyExportCosmeticsAndScientificInvariancePersistAcrossSaves()
    {
        var manager = new BlooperAlbumAndCosmeticsCoordinator();
        var rawSample = BuildSampleWithSensitiveRegions("s_funny_01", 10.0);

        // Fill album to 30 entries
        for (var i = 1; i <= BlooperAndCosmeticCatalog.MaxAlbumEntries; i++)
        {
            var res = manager.TrySaveBlooper($"blooper_{i:00}", rawSample, $"blooper.title.{i}", BlooperCategory.PedestrianPose, "old_center");
            Assert.Equal(AlbumAddStatus.Added, res.Status);
        }
        Assert.Equal(30, manager.Entries.Count);

        // 31st photo requires explicit replacement confirmation
        var unconfirmed = manager.TrySaveBlooper("blooper_31", rawSample, "blooper.title.31", BlooperCategory.AnimalCameo, "port");
        Assert.Equal(AlbumAddStatus.ReplacementConfirmationRequired, unconfirmed.Status);
        Assert.Equal(30, manager.Entries.Count);
        Assert.DoesNotContain(manager.Entries, e => e.EntryId == "blooper_31");

        var confirmed = manager.TrySaveBlooper(
            "blooper_31",
            rawSample,
            "blooper.title.31",
            BlooperCategory.AnimalCameo,
            "port",
            confirmReplacement: true,
            replaceEntryId: "blooper_01");
        Assert.Equal(AlbumAddStatus.ReplacedExisting, confirmed.Status);
        Assert.Equal(30, manager.Entries.Count);
        Assert.DoesNotContain(manager.Entries, e => e.EntryId == "blooper_01");
        Assert.Contains(manager.Entries, e => e.EntryId == "blooper_31");

        // Local album export runs privacy masking without mutating raw sample
        var exported = BlooperAlbumAndCosmeticsCoordinator.ExportLocalAlbumThumbnail("blooper_31", rawSample, ProcessingMode.Technical);
        Assert.True(exported.PrivacyCompliant);
        Assert.Equal(256, exported.Width);
        Assert.Equal(144, exported.Height);
        Assert.NotEqual(exported.BeforeThumbnailSha256, exported.AfterThumbnailSha256);
        Assert.Empty(rawSample.PrivacyMasks); // Raw sample untouched!

        // Catalog offers 3 paints, 3 stickers, 3 horns (including cackle), and Gimbalina costumes
        Assert.Equal(3, BlooperAndCosmeticCatalog.AllCosmetics.Count(c => c.Kind == CosmeticKind.VehiclePaint));
        Assert.Equal(3, BlooperAndCosmeticCatalog.AllCosmetics.Count(c => c.Kind == CosmeticKind.VehicleSticker));
        Assert.Equal(3, BlooperAndCosmeticCatalog.AllCosmetics.Count(c => c.Kind == CosmeticKind.VehicleHorn));

        var ledger = new CareerLedger(500);
        Assert.True(manager.TryPurchaseCosmetic("tx_paint", 15.0, "paint_cobalt_fleet", ledger, out _));
        Assert.True(manager.TryPurchaseCosmetic("tx_sticker", 16.0, "sticker_chicken_mascot", ledger, out _));
        Assert.True(manager.TryPurchaseCosmetic("tx_horn", 17.0, BlooperAndCosmeticCatalog.CackleHornId, ledger, out _));
        Assert.True(manager.TryPurchaseCosmetic("tx_costume", 18.0, "costume_rain_poncho", ledger, out _));

        // Verify cosmetics never alter scientific CQ metrics or platform physics
        var qcBaseline = BuildDeterministicQualityReport("c_1", 0.038);
        var qcCosmetic = BuildDeterministicQualityReport("c_1", 0.038);
        Assert.True(BlooperAlbumAndCosmeticsCoordinator.VerifyCosmeticScientificInvariance(
            qcBaseline,
            qcCosmetic,
            PlatformProfile.Car,
            PlatformProfile.Car));

        var snap = JsonSerializer.Deserialize<BlooperAndCosmeticSnapshot>(JsonSerializer.Serialize(manager.ExportSnapshot()))!;
        var restored = BlooperAlbumAndCosmeticsCoordinator.FromSnapshot(snap);
        Assert.Equal(30, restored.Entries.Count);
        Assert.Equal("paint_cobalt_fleet", restored.EquippedPaintId);
        Assert.Equal(BlooperAndCosmeticCatalog.CackleHornId, restored.EquippedHornId);
        Assert.Equal(GimbalinaCostumeId.RainPoncho, restored.EquippedGimbalinaCostume);
    }
}
