using System.Text.Json;
using System.Text.Json.Serialization;
using Gsa.Core.Geodesy;
using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Fusion;
using Gsa.Core.Sensors.Lidar;
using Gsa.Core.Traffic;
using Gsa.Core.World;
using Gsa.Missions.Content;
using Gsa.Missions.Contracts;
using Gsa.Missions.Economy;
using Gsa.Missions.Office;
using Gsa.Missions.Persistence;
using Gsa.Missions.Quality;

namespace Gsa.Missions.Campaign;

public sealed record ViaductControlHeightSubmission(
    string ControlId,
    Vector3d PositionEnu,
    double ReportedEllipsoidalHeightMetres,
    double ReportedOrthometricHeightMetres);

public sealed record ViaductVerticalReferenceCheckResult(
    bool Compliant,
    int EvaluatedControls,
    double MaxHeightResidualMetres,
    string ReasonKey);

public sealed record Act2MissionRunResult(
    string MissionId,
    LocomotionMode PlatformUsed,
    string ProductType,
    bool Passed,
    int RewardCoords,
    int ReputationDelta,
    IReadOnlyList<string> UnlockedPlatforms,
    IReadOnlyList<string> UnlockedRegions,
    bool HostileHazardsDisabled,
    string Notes);

public sealed record Act2PlaythroughReport(
    bool Passed,
    bool CalmCityEnabled,
    bool KombiUnlocked,
    bool PickupUnlocked,
    bool SerraUnlocked,
    bool Act3Unlocked,
    bool TunnelTestOverrideOpened,
    bool SwappedHeightsRejected,
    bool RainOutlierFilterPassed,
    int MovedBenchmarksDetectedCount,
    int TotalRewardCoords,
    int TotalReputationDelta,
    IReadOnlyList<string> UnlockedPlatforms,
    IReadOnlyList<string> UnlockedRegions,
    IReadOnlyList<Act2MissionRunResult> Missions);

public sealed record AdvancedTemplateDefinition(
    string TemplateId,
    string ClientId,
    string RegionId,
    string Product,
    bool CalmByDefault,
    string TitleKey,
    double TargetDensityPointsPerM2,
    IReadOnlyList<RouteWitnessCertificate> RouteVariants,
    IReadOnlyList<string> CandidateFeatureIds);

/// <summary>Executes Act 2 campaign missions HIST-05..HIST-09 with calm-mode parity, Serra unlock, viaduct h/H verification, rain filtering, and movable divisa benchmarks (T-F10-04).</summary>
public static class Act2CampaignRunner
{
    public static bool CanExecuteMissionWithPlatform(string missionId, LocomotionMode candidatePlatform)
    {
        var spec = FullCampaignCatalog.GetMission(missionId);
        var profile = PlatformProfile.ForMode(candidatePlatform);
        // Minimum platform does not force downgrading from a higher compatible platform.
        return profile.Level >= spec.MinimumPlatformLevel;
    }

    public static ViaductVerticalReferenceCheckResult ValidateViaductHeightReference(
        IReadOnlyList<ViaductControlHeightSubmission> submissions,
        double toleranceMetres = 0.02)
    {
        ArgumentNullException.ThrowIfNull(submissions);
        if (submissions.Count == 0)
            return new ViaductVerticalReferenceCheckResult(false, 0, double.PositiveInfinity, "cq.error.no_viaduct_controls");

        var maxErr = 0.0;
        var swappedDetected = false;

        foreach (var item in submissions)
        {
            var expectedLittleH = item.PositionEnu.Z;
            var expectedBigH = LocalGeoidModel.ToOrthometricHeight(item.PositionEnu);
            var ng = LocalGeoidModel.Separation(item.PositionEnu);

            var hErr = Math.Abs(item.ReportedEllipsoidalHeightMetres - expectedLittleH);
            var orthoErr = Math.Abs(item.ReportedOrthometricHeightMetres - expectedBigH);

            var swappedLittleErr = Math.Abs(item.ReportedEllipsoidalHeightMetres - expectedBigH);
            var swappedBigErr = Math.Abs(item.ReportedOrthometricHeightMetres - expectedLittleH);
            if (swappedLittleErr <= toleranceMetres && swappedBigErr <= toleranceMetres && ng > toleranceMetres * 2.0)
            {
                swappedDetected = true;
            }

            maxErr = Math.Max(maxErr, Math.Max(hErr, orthoErr));
        }

        if (swappedDetected)
            return new ViaductVerticalReferenceCheckResult(false, submissions.Count, maxErr, "cq.error.vertical_reference_h_and_orthometric_H_swapped");

        return maxErr <= toleranceMetres
            ? new ViaductVerticalReferenceCheckResult(true, submissions.Count, maxErr, "cq.viaduct_height_reference_valid")
            : new ViaductVerticalReferenceCheckResult(false, submissions.Count, maxErr, "cq.error.vertical_reference_tolerance_exceeded");
    }

    public static Act2PlaythroughReport RunAct2Playthrough(
        WorldSceneManifest serraManifest,
        bool calmCityEnabled = false)
    {
        ArgumentNullException.ThrowIfNull(serraManifest);

        var calmPolicy = new CalmPolicyCoordinator(calmCityEnabled);

        var completedMissions = new HashSet<string>(["HIST-01", "HIST-02", "HIST-03", "HIST-04"], StringComparer.Ordinal);
        var unlockedPlatforms = new HashSet<string>(["cart", "bicycle", "scooter", "car"], StringComparer.Ordinal);
        var unlockedRegions = new HashSet<string>(["old_center", "port", "waterfront"], StringComparer.Ordinal);
        var progressState = new CampaignSaveProgressState(unlockedPlatforms, unlockedRegions);

        var totalCoords = 0;
        var totalRep = 0;
        var results = new List<Act2MissionRunResult>(5);

        // HIST-05: Bus corridor 360 during 10-16h window (Car L3) -> unlocks Kombi
        var hist05 = FullCampaignCatalog.GetMission("HIST-05");
        var windowOpenAt11h = BusSurveyCorridor.IsSurveyWindow(11);
        var busPassed = CanExecuteMissionWithPlatform("HIST-05", LocomotionMode.Car) && windowOpenAt11h;
        CutsceneSkipAndStateCoordinator.SkipCutscene(hist05.Cutscene, progressState);
        completedMissions.Add("HIST-05");
        ApplyMissionResult(hist05, LocomotionMode.Car, busPassed, calmCityEnabled, calmPolicy, unlockedPlatforms, unlockedRegions, ref totalCoords, ref totalRep, results, "10-16h bus lane window & 360 capture verified");

        // HIST-06: 200 m Port Tunnel entrance by Kombi L4 (tunnel override before HIST-05 Serra lock irrelevant here)
        var hist06 = FullCampaignCatalog.GetMission("HIST-06");
        var tunnelPortal = RegionalAccessGateEvaluator.EvaluatePortalAccess(
            "portal_port_tunnel_mouth",
            new HashSet<string>(["HIST-04"], StringComparer.Ordinal),
            ownedPlatformLevel: 4,
            hasActivePortTunnelTestContract: true);
        var kombiRig = KombiSurveyRigCoordinator.ConfigureRig(PlatformProfile.Kombi, playerProgressionLevel: 4, LidarProfile.L4);
        var tunnelBudget = KombiSurveyRigCoordinator.SimulateTunnelCorridor(
            PlatformProfile.Kombi,
            LidarProfile.L4,
            [new KombiTunnelSegmentSpec("port_tunnel_200m", 200.0, 10.0, true, Passes: 2)],
            LidarProfile.L4.PrimaryRaysPerSecond);
        var hist06Passed = tunnelPortal.Allowed && kombiRig.Succeeded && tunnelBudget.Succeeded && CanExecuteMissionWithPlatform("HIST-06", LocomotionMode.Kombi);
        CutsceneSkipAndStateCoordinator.SkipCutscene(hist06.Cutscene, progressState);
        completedMissions.Add("HIST-06");
        ApplyMissionResult(hist06, LocomotionMode.Kombi, hist06Passed, calmCityEnabled, calmPolicy, unlockedPlatforms, unlockedRegions, ref totalCoords, ref totalRep, results, "200 m Port tunnel with Kombi L4 & ZUPT/mouth controls");

        // HIST-07: Viaduct 100 m + h/H check (swapped rejected, correct accepted) -> unlocks Pickup & Serra
        var hist07 = FullCampaignCatalog.GetMission("HIST-07");
        var ctrlA = new Vector3d(500620.0, 7401200.0, 112.5);
        var ctrlB = new Vector3d(500720.0, 7401200.0, 113.0);
        var swappedCheck = ValidateViaductHeightReference(
        [
            new("viaduct_ctrl_a", ctrlA, LocalGeoidModel.ToOrthometricHeight(ctrlA), ctrlA.Z),
            new("viaduct_ctrl_b", ctrlB, LocalGeoidModel.ToOrthometricHeight(ctrlB), ctrlB.Z),
        ]);
        var validCheck = ValidateViaductHeightReference(
        [
            new("viaduct_ctrl_a", ctrlA, ctrlA.Z, LocalGeoidModel.ToOrthometricHeight(ctrlA)),
            new("viaduct_ctrl_b", ctrlB, ctrlB.Z, LocalGeoidModel.ToOrthometricHeight(ctrlB)),
        ]);
        var hist07Passed = !swappedCheck.Compliant &&
                           string.Equals(swappedCheck.ReasonKey, "cq.error.vertical_reference_h_and_orthometric_H_swapped", StringComparison.Ordinal) &&
                           validCheck.Compliant &&
                           CanExecuteMissionWithPlatform("HIST-07", LocomotionMode.Kombi);
        CutsceneSkipAndStateCoordinator.SkipCutscene(hist07.Cutscene, progressState);
        completedMissions.Add("HIST-07");
        ApplyMissionResult(hist07, LocomotionMode.Kombi, hist07Passed, calmCityEnabled, calmPolicy, unlockedPlatforms, unlockedRegions, ref totalCoords, ref totalRep, results, "100 m under-viaduct scan + h/H vertical conformance verified");

        // Verify Serra portal now unlocks for Level-5 Pickup after HIST-07
        var serraPortal = RegionalAccessGateEvaluator.EvaluatePortalAccess("portal_to_serra", completedMissions, ownedPlatformLevel: 5);

        // HIST-08: 300 m rainy night avenue with Pickup 4x4 & paired LiDARs + spatiotemporal outlier filter
        var hist08 = FullCampaignCatalog.GetMission("HIST-08");
        var pickupRig = PickupFourByFourRigCoordinator.ConfigureDualLidarRig(playerProgressionLevel: 5);
        var (filterPassed, rainQualityPassed) = EvaluateRainyNightPointCloudFilter();
        var hist08Passed = serraPortal.Allowed && pickupRig.Succeeded && filterPassed && rainQualityPassed && CanExecuteMissionWithPlatform("HIST-08", LocomotionMode.Pickup);
        CutsceneSkipAndStateCoordinator.SkipCutscene(hist08.Cutscene, progressState);
        completedMissions.Add("HIST-08");
        ApplyMissionResult(hist08, LocomotionMode.Pickup, hist08Passed, calmCityEnabled, calmPolicy, unlockedPlatforms, unlockedRegions, ref totalCoords, ref totalRep, results, "300 m rainy-night Pickup scan + outlier filtering passed CQ");

        // HIST-09: Six boundary monuments in Serra with moved benchmarks + minimum Car allowed for Pickup
        var hist09 = FullCampaignCatalog.GetMission("HIST-09");
        var canUseCar = CanExecuteMissionWithPlatform("HIST-09", LocomotionMode.Car);
        var canUsePickupToo = CanExecuteMissionWithPlatform("HIST-09", LocomotionMode.Pickup);
        var cannotUseScooter = !CanExecuteMissionWithPlatform("HIST-09", LocomotionMode.Scooter);

        var sabotage = new FactionSabotageAndCountermeasuresCoordinator();
        var movedCount = 0;
        for (var i = 1; i <= 6; i++)
        {
            var marcoId = $"serra_divisa_marco_{i:00}";
            var cadPos = serraManifest.Objects.First(o => o.Id == marcoId).PositionEnu;
            sabotage.MovedBenchmarks.RegisterDisplacement(marcoId, cadPos, new Vector3d(1.8, -0.9, 0.0), eventTimeSeconds: 100.0);
            if (sabotage.MovedBenchmarks.EvaluateDiscrepancyFromPublishedCoordinateMetres(marcoId, cadPos, 150.0) > 1.5)
                movedCount++;
        }

        // Verify calm mode strips hostile pursuit without losing progression or rewards
        var sabotageOutcome = sabotage.TriggerIdempotentSabotageAttack(
            "hist09_claim_jumpers_ambush",
            zoneId: "serra_divisas",
            SabotageAttackKind.RoadblockOrInterception,
            new BatchCustodyStore(),
            timestampSeconds: 150.0,
            calmPolicy: calmPolicy);
        var calmRespected = !calmCityEnabled || (sabotageOutcome.SuppressedByCalmCity && calmPolicy.IsProtectionActive);
        var hist09Passed = canUseCar && canUsePickupToo && cannotUseScooter && movedCount == 6 && calmRespected;

        CutsceneSkipAndStateCoordinator.SkipCutscene(hist09.Cutscene, progressState);
        completedMissions.Add("HIST-09");
        ApplyMissionResult(hist09, LocomotionMode.Pickup, hist09Passed, calmCityEnabled, calmPolicy, unlockedPlatforms, unlockedRegions, ref totalCoords, ref totalRep, results, "6 Serra boundary monuments reconciled; Act 2 closed");

        var hist10 = FullCampaignCatalog.GetMission("HIST-10");
        var act3Unlocked = hist10.Preconditions.All(p => completedMissions.Contains(p));

        return new Act2PlaythroughReport(
            Passed: results.All(r => r.Passed) && act3Unlocked,
            CalmCityEnabled: calmCityEnabled,
            KombiUnlocked: unlockedPlatforms.Contains("kombi"),
            PickupUnlocked: unlockedPlatforms.Contains("pickup"),
            SerraUnlocked: unlockedRegions.Contains("serra") && serraPortal.Allowed,
            Act3Unlocked: act3Unlocked,
            TunnelTestOverrideOpened: tunnelPortal.Allowed,
            SwappedHeightsRejected: !swappedCheck.Compliant,
            RainOutlierFilterPassed: rainQualityPassed,
            MovedBenchmarksDetectedCount: movedCount,
            TotalRewardCoords: totalCoords,
            TotalReputationDelta: totalRep,
            UnlockedPlatforms: Array.AsReadOnly(unlockedPlatforms.OrderBy(x => x, StringComparer.Ordinal).ToArray()),
            UnlockedRegions: Array.AsReadOnly(unlockedRegions.OrderBy(x => x, StringComparer.Ordinal).ToArray()),
            Missions: Array.AsReadOnly(results.ToArray()));
    }

    private static void ApplyMissionResult(
        CampaignMissionFullSpec spec,
        LocomotionMode usedPlatform,
        bool passed,
        bool calmCityEnabled,
        CalmPolicyCoordinator calmPolicy,
        HashSet<string> unlockedPlatforms,
        HashSet<string> unlockedRegions,
        ref int totalCoords,
        ref int totalRep,
        List<Act2MissionRunResult> results,
        string notes)
    {
        if (calmCityEnabled && spec.CalmVariant is not null)
        {
            totalCoords += spec.CalmVariant.RewardCoords;
            totalRep += spec.CalmVariant.ReputationDelta;
            foreach (var p in spec.CalmVariant.UnlockedPlatforms) unlockedPlatforms.Add(p);
            foreach (var r in spec.CalmVariant.UnlockedRegions) unlockedRegions.Add(r);
            results.Add(new Act2MissionRunResult(
                spec.MissionId, usedPlatform, spec.CalmVariant.ProductType, passed,
                spec.CalmVariant.RewardCoords, spec.CalmVariant.ReputationDelta,
                spec.CalmVariant.UnlockedPlatforms, spec.CalmVariant.UnlockedRegions,
                calmPolicy.IsProtectionActive, notes));
            return;
        }

        totalCoords += spec.RewardCoords;
        totalRep += spec.ReputationDelta;
        foreach (var p in spec.UnlockedPlatforms) unlockedPlatforms.Add(p);
        foreach (var r in spec.UnlockedRegions) unlockedRegions.Add(r);
        results.Add(new Act2MissionRunResult(
            spec.MissionId, usedPlatform, spec.ProductType, passed,
            spec.RewardCoords, spec.ReputationDelta,
            spec.UnlockedPlatforms, spec.UnlockedRegions,
            false, notes));
    }

    private static (bool FilterPassed, bool QualityPassed) EvaluateRainyNightPointCloudFilter()
    {
        var bounds = new Bounds2DEnu(0.0, 0.0, 12.0, 2.0);
        var surface = new SurveySurfaceReference("hist08_rain_avenue", bounds, 0.0, []);
        var plane = new ReferencePlaneQualitySpec("avenue_deck", Vector3d.Zero, Vector3d.UnitZ, bounds, -0.5, 0.5);
        var qcSpec = new PointCloudQualitySpec("HIST-08", TargetCellDensityPointsPerM2: 4.0, MinCoverageFraction: 0.90, ReferencePlanes: [plane]);

        var inputs = new List<FilteredLidarSampleInput>();
        long seq = 1;
        for (var cell = 0; cell < 6; cell++)
        {
            for (var row = 0; row < 4; row++)
            {
                for (var col = 0; col < 4; col++)
                {
                    var x = cell * 2.0 + 0.25 + col * 0.5;
                    var y = 0.25 + row * 0.5;
                    inputs.Add(new FilteredLidarSampleInput(
                        new GeoreferencedLidarSample(seq++, 10.0 + seq * 0.01, new Vector3d(x, y, 0.0), 0.78f, 1, false),
                        PassIndex: ((row + col) % 2) + 1));
                }
            }
        }

        // Inject isolated airborne spurious rain drops that violate neighborhood & multi-pass consistency
        for (var r = 0; r < 8; r++)
        {
            inputs.Add(new FilteredLidarSampleInput(
                new GeoreferencedLidarSample(seq++, 15.0 + r * 0.2, new Vector3d(1.0 + r * 1.4, 1.0, 2.2), 0.14f, 1, true),
                PassIndex: 1));
        }

        var session = new PointCloudOutlierFilterSession(inputs);
        var spec = new SpuriousPointFilterSpec(RadiusMetres: 0.9, MinNeighborsWithinRadius: 3, RequireMultiPassIfSparse: false, MinIntensity: 0.25f);
        var preview = session.ApplyFilter(spec, qcSpec, [surface]);
        var qcReport = PointCloudQualityEvaluator.Evaluate(qcSpec, [surface], preview.RetainedSamples);
        return (preview.RemovedPointCount == 8 && preview.RetainedPointCount == 96, qcReport.Passed);
    }
}

/// <summary>Generates and validates post-M5 Act 2 procedural templates (PROC-ENERGIA, PROC-OBRAS, PROC-TUNEL) backed by route witness certificates (T-F10-04).</summary>
public static class Act2AdvancedProceduralGenerator
{
    public const string DefaultCityMapSha256 = "sha256_sao_datum_m6_serra_city_v1";
    public const string CurrentProfileVersion = "profiles_m6_v1";

    public static IReadOnlyList<AdvancedTemplateDefinition> Templates { get; } =
    [
        new(
            "PROC-ENERGIA",
            "power_utility",
            "serra",
            "feature_inventory",
            CalmByDefault: true,
            "proc.template.energy.title",
            TargetDensityPointsPerM2: 0.0,
            [
                new("energia_serra_foothill", "serra_power_corridor", "serra_works_surface", 4, ["camera", "gnss_rtk"], 1200.0, 45, DefaultCityMapSha256, CurrentProfileVersion),
                new("energia_serra_ridge", "serra_ridge_feeder", "serra_works_surface", 5, ["camera", "gnss_rtk"], 1500.0, 60, DefaultCityMapSha256, CurrentProfileVersion),
            ],
            ["serra_utility_pole_01", "serra_utility_pole_02", "serra_utility_pole_03", "serra_utility_pole_04", "serra_utility_pole_05", "serra_utility_pole_06", "serra_utility_pole_07", "serra_utility_pole_08"]),
        new(
            "PROC-OBRAS",
            "construction_corp",
            "serra",
            "point_cloud",
            CalmByDefault: false,
            "proc.template.works.title",
            TargetDensityPointsPerM2: 4.0,
            [
                new("obras_slope_earthworks", "serra_earthworks_cut", "serra_works_surface", 5, ["lidar", "imu", "dmi"], 1800.0, 120, DefaultCityMapSha256, CurrentProfileVersion),
                new("obras_retaining_wall", "serra_retaining_corridor", "serra_canopy_ground_surface", 5, ["lidar", "imu", "dmi"], 2100.0, 150, DefaultCityMapSha256, CurrentProfileVersion),
            ],
            ["serra_slope_cut_01", "serra_slope_cut_02", "serra_slope_cut_03", "serra_slope_cut_04", "serra_ground_plane_01", "serra_ground_plane_02"]),
        new(
            "PROC-TUNEL",
            "university",
            "port",
            "point_cloud",
            CalmByDefault: true,
            "proc.template.tunnel.title",
            TargetDensityPointsPerM2: 6.0,
            [
                new("tunel_port_mouth_200m", "port_tunnel_section", "port_tunnel_mouth_surface", 4, ["lidar", "imu", "dmi"], 1400.0, 90, DefaultCityMapSha256, CurrentProfileVersion),
                new("tunel_serra_gallery", "serra_tunnel_section", "serra_tunnel_surface", 4, ["lidar", "imu", "dmi"], 1700.0, 110, DefaultCityMapSha256, CurrentProfileVersion),
            ],
            ["tunnel_lining_rib_01", "tunnel_lining_rib_02", "tunnel_lining_rib_03", "tunnel_lining_rib_04", "tunnel_lining_rib_05", "tunnel_lining_rib_06"]),
    ];

    public static GeneratedContractOffer GenerateFromAdvancedTemplate(
        string templateId,
        ulong seed,
        bool forceCalmVariant = false,
        string currentMapSha256 = DefaultCityMapSha256,
        string currentProfileVersion = CurrentProfileVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);
        var spec = Templates.FirstOrDefault(t => string.Equals(t.TemplateId, templateId, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Unknown Act 2 procedural template '{templateId}'.", nameof(templateId));

        var variantIndex = (int)(seed % (ulong)spec.RouteVariants.Count);
        var witness = spec.RouteVariants[variantIndex];
        if (!witness.IsValidFor(currentMapSha256, currentProfileVersion))
            throw new InvalidOperationException($"Route witness certificate '{witness.VariantId}' is invalidated for current map/profile hash.");

        var minCount = Math.Min(3, spec.CandidateFeatureIds.Count);
        var maxCount = spec.CandidateFeatureIds.Count;
        var span = Math.Max(1, maxCount - minCount + 1);
        var count = minCount + (int)((seed / 11UL) % (ulong)span);
        var selectedFeatures = spec.CandidateFeatureIds
            .Select((id, idx) => (id, key: HashStep(seed, idx)))
            .OrderBy(x => x.key)
            .ThenBy(x => x.id, StringComparer.Ordinal)
            .Take(count)
            .Select(x => x.id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        var dueSeconds = Math.Max(14400.0, Math.Ceiling(witness.EstimatedRouteDurationSeconds * 4.5));
        var expiresSeconds = dueSeconds + 5400.0;
        var isCalm = spec.CalmByDefault || forceCalmVariant;
        var baseReward = (witness.RequiredPlatformLevel >= 5 ? 7000 : 4000) + selectedFeatures.Length * 120;

        ContractRequirementSpec[] reqs = spec.Product == "point_cloud"
            ? [
                new ThresholdRequirementSpec("density_coverage", "ratio", "min", 0.85, 0.96, witness.SurfaceId, 2.0, spec.TargetDensityPointsPerM2),
                new ThresholdRequirementSpec("absolute_rmse", "m", "max", 0.25, 0.08),
                new ThresholdRequirementSpec("relative_rmse", "m", "max", 0.12, 0.04),
                new PredicateRequirementSpec("privacy_redacted", true),
                new PredicateRequirementSpec("format_valid", true),
            ]
            : [
                new ThresholdRequirementSpec("feature_completeness", "ratio", "min", 0.85, 0.98),
                new ThresholdRequirementSpec("absolute_rmse", "m", "max", 0.50, 0.15),
                new ThresholdRequirementSpec("valid_image_fraction", "ratio", "min", 0.80, 0.96),
                new PredicateRequirementSpec("privacy_redacted", true),
                new PredicateRequirementSpec("format_valid", true),
            ];

        var def = new ContractDefinition(
            SchemaVersion: 1,
            Id: $"{spec.TemplateId.ToLowerInvariant().Replace('-', '_')}_{witness.VariantId}_{seed}",
            TitleKey: spec.TitleKey,
            ClientId: spec.ClientId,
            RegionId: spec.RegionId,
            AreaId: witness.AreaId,
            Product: spec.Product,
            RequiredFeatureIds: selectedFeatures,
            Requirements: reqs,
            RequiredCapabilities: witness.RequiredCapabilities,
            Risk: isCalm ? "easy" : "moderate",
            Calm: isCalm,
            Deadline: new ContractDeadlineSpec(dueSeconds, expiresSeconds, 0.05, 0.50),
            Reward: new ContractRewardSpec(baseReward, 175 + selectedFeatures.Length * 15, 2, []),
            PartialPolicy: new ContractPartialPolicySpec("disabled", []),
            DeliveryFormat: "gsa_bundle_v1",
            StoryPrerequisites: []);

        return new GeneratedContractOffer(
            spec.TemplateId,
            witness.VariantId,
            seed,
            def,
            witness,
            ExecutableWithStarterKit: false,
            (int)witness.EstimatedRouteDurationSeconds,
            witness.RequiredStorageUnits);
    }

    public static OneHundredSeedsPerTemplateReport ValidateOneHundredSeedsPerAdvancedTemplate(
        ulong baseSeed = 2000UL,
        string currentMapSha256 = DefaultCityMapSha256,
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
                var seed = baseSeed + (ulong)s * 37UL;
                var offer = GenerateFromAdvancedTemplate(template.TemplateId, seed, false, currentMapSha256, currentProfileVersion);
                totalGenerated++;

                var platformMode = offer.WitnessCertificate.RequiredPlatformLevel >= 5 ? LocomotionMode.Pickup : LocomotionMode.Kombi;
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
                            target_density_points_per_m2 = tr.TargetDensityPointsPerM2,
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
            AllPassed: totalPassed == totalGenerated && totalGenerated == 300,
            MapSha256: currentMapSha256,
            ProfileVersion: currentProfileVersion,
            ByTemplate: Array.AsReadOnly(results.ToArray()));
    }

    public static IReadOnlyList<GeneratedContractOffer> GenerateAvailableBoardWithAdvancedTemplates(
        int balanceCoords,
        int fineDebtCoords,
        int ownedPlatformLevel,
        IReadOnlySet<string> unlockedRegions,
        ulong seed)
    {
        ArgumentNullException.ThrowIfNull(unlockedRegions);
        var board = MvpProceduralContractGenerator.GenerateAvailableBoard(balanceCoords, fineDebtCoords, ownedPlatformLevel, seed).ToList();

        foreach (var template in Templates)
        {
            if (!unlockedRegions.Contains(template.RegionId))
                continue;

            var offer = GenerateFromAdvancedTemplate(template.TemplateId, seed);
            if (offer.WitnessCertificate.RequiredPlatformLevel > ownedPlatformLevel)
                continue;
            if ((balanceCoords <= 0 || fineDebtCoords > 0) && !offer.Contract.Calm)
                continue;

            board.Add(offer);
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
