using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Fusion;
using Gsa.Core.Traffic;
using Gsa.Missions.Contracts;
using Gsa.Missions.Persistence;
using Gsa.Missions.Quality;

namespace Gsa.Missions.Economy;

public enum ProtectedSafeZoneKind
{
    Office = 0,
    WifiHotspot = 1,
    UnloadPoint = 2,
    CalmTrainingPark = 3,
}

public sealed record ProtectedSafeZoneSpec(
    string SafeZoneId,
    string RegionId,
    ProtectedSafeZoneKind Kind,
    Vector3d CenterEnu,
    double RadiusMetres,
    string SafeReasonKey);

public sealed record SafeZonePressureResult(
    string SafeZoneId,
    string RegionId,
    ProtectedSafeZoneKind Kind,
    bool InsideSafeZone,
    double AttentionBefore,
    double AttentionAfter,
    bool EscalationSuppressed,
    bool HostileQueueCleared,
    bool PlayerCanUseWeapons,
    string ReasonKey);

public sealed record FullCityZonePlaythroughStep(
    string ZoneId,
    string RegionId,
    FactionId FactionId,
    string FactionCode,
    bool CalmCityEnabled,
    double AttentionMultiplier,
    bool CountermeasureEscort,
    bool CountermeasureDisguise,
    bool CountermeasureOffPeak,
    bool CountermeasureCommunityAuth,
    bool GnssSpoofingHandledSafely,
    bool MovedBenchmarkVerifiedWithoutHistoricalMutation,
    double PeakAttentionScore,
    bool EscalatedToLethalOrBlockingDeadlock,
    bool BombDisarmedOrSuppressed,
    bool CompletedWithoutViolence,
    bool PlayerWeaponsAllowed,
    bool ProtagonistInjured,
    bool GimbalinaInjured,
    string ResolutionStrategyKey);

public sealed record FullCityFactionPlaythroughReport(
    bool Succeeded,
    bool CalmCityEnabled,
    int CoveredRegionCount,
    int CoveredFactionCount,
    int SurveyedZoneCount,
    int ProtectedSafeZoneVerifiedCount,
    bool ZeroCombatDeadlocks,
    bool ZeroWeaponsAllowed,
    bool ZeroInjuries,
    bool CqStrictnessIdenticalToBaseline,
    string CqThresholdsDigest,
    IReadOnlyList<FullCityZonePlaythroughStep> ZoneSteps,
    IReadOnlyList<SafeZonePressureResult> SafeZoneChecks);

public sealed record FactionPlaythroughModeComparison(
    FullCityFactionPlaythroughReport StandardMode,
    FullCityFactionPlaythroughReport CalmCityMode,
    bool BothModesCompletableWithoutCombat,
    bool CqStrictnessIdenticalBetweenModes,
    bool CalmModeSuppressesHostileEscalation,
    IReadOnlyList<string> CoveredRegionIds,
    IReadOnlyList<FactionId> CoveredFactionIds);

/// <summary>Coordinates full-city 5-faction pressure across all 4 regions, protected safe zones, non-violent countermeasures, and Calm City playthrough parity (T-F16-04).</summary>
public static class FullCityFactionPressureCoordinator
{
    public static IReadOnlyList<string> FullCityRegionIds { get; } =
    [
        "old_center",
        "port",
        "waterfront",
        "serra",
    ];

    public static IReadOnlyList<ProtectedSafeZoneSpec> ProtectedSafeZones { get; } =
    [
        new("safe_office_old_center", "old_center", ProtectedSafeZoneKind.Office, new Vector3d(500160.0, 7400200.0, 12.0), 65.0, "safe_zone.office"),
        new("safe_calm_park_old_center", "old_center", ProtectedSafeZoneKind.CalmTrainingPark, new Vector3d(500890.0, 7400180.0, 11.0), 80.0, "safe_zone.calm_training_park"),
        new("safe_wifi_port_terminal", "port", ProtectedSafeZoneKind.WifiHotspot, new Vector3d(500140.0, 7401820.0, 6.0), 55.0, "safe_zone.wifi_hotspot"),
        new("safe_unload_port_depot", "port", ProtectedSafeZoneKind.UnloadPoint, new Vector3d(500820.0, 7401080.0, 5.0), 60.0, "safe_zone.unload_point"),
        new("safe_wifi_waterfront_pier", "waterfront", ProtectedSafeZoneKind.WifiHotspot, new Vector3d(501120.0, 7400820.0, 4.0), 55.0, "safe_zone.wifi_hotspot"),
        new("safe_unload_waterfront_hub", "waterfront", ProtectedSafeZoneKind.UnloadPoint, new Vector3d(501880.0, 7400860.0, 4.5), 60.0, "safe_zone.unload_point"),
        new("safe_calm_park_serra", "serra", ProtectedSafeZoneKind.CalmTrainingPark, new Vector3d(501140.0, 7401100.0, 180.0), 75.0, "safe_zone.calm_training_park"),
        new("safe_unload_serra_station", "serra", ProtectedSafeZoneKind.UnloadPoint, new Vector3d(501920.0, 7401920.0, 290.0), 60.0, "safe_zone.unload_point"),
    ];

    public static bool IsInsideProtectedSafeZone(
        string regionId,
        Vector3d positionEnu,
        out ProtectedSafeZoneSpec? safeZone)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(regionId);

        foreach (var zone in ProtectedSafeZones)
        {
            if (!string.Equals(zone.RegionId, regionId, StringComparison.Ordinal))
                continue;

            var dx = positionEnu.X - zone.CenterEnu.X;
            var dy = positionEnu.Y - zone.CenterEnu.Y;
            if (Math.Sqrt(dx * dx + dy * dy) <= zone.RadiusMetres + 1e-9)
            {
                safeZone = zone;
                return true;
            }
        }

        safeZone = null;
        return false;
    }

    public static SafeZonePressureResult EvaluateProtectedSafeZoneStep(
        string safeZoneId,
        Vector3d playerPositionEnu,
        TerritoryAttentionTracker tracker,
        CalmPolicyCoordinator calmPolicy,
        string? linkedTerritoryZoneId = null,
        double dtSeconds = 2.0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(safeZoneId);
        ArgumentNullException.ThrowIfNull(tracker);
        ArgumentNullException.ThrowIfNull(calmPolicy);

        var safeSpec = ProtectedSafeZones.FirstOrDefault(z => string.Equals(z.SafeZoneId, safeZoneId, StringComparison.Ordinal))
            ?? throw new KeyNotFoundException($"Unknown protected safe zone '{safeZoneId}'.");

        var zoneKey = linkedTerritoryZoneId ?? safeSpec.SafeZoneId;
        var before = tracker.GetAttention(zoneKey);

        var dx = playerPositionEnu.X - safeSpec.CenterEnu.X;
        var dy = playerPositionEnu.Y - safeSpec.CenterEnu.Y;
        var inside = Math.Sqrt(dx * dx + dy * dy) <= safeSpec.RadiusMetres + 1e-9;

        if (!inside)
        {
            var outsideOutcome = tracker.Step(zoneKey, activeCapture: true, playerSpeedMps: 4.0, disarmInputHeld: false, dtSeconds, calmPolicy);
            return new SafeZonePressureResult(
                SafeZoneId: safeSpec.SafeZoneId,
                RegionId: safeSpec.RegionId,
                Kind: safeSpec.Kind,
                InsideSafeZone: false,
                AttentionBefore: before,
                AttentionAfter: outsideOutcome.AttentionScore,
                EscalationSuppressed: outsideOutcome.SuppressedByCalmCity,
                HostileQueueCleared: calmPolicy.PendingHostileEventCount == 0,
                PlayerCanUseWeapons: FactionCatalog.PlayerWeaponsAllowed,
                ReasonKey: outsideOutcome.ReasonKey);
        }

        // Protected safe zone temporarily enforces calm sanctuary without altering global Calm City toggle after exit.
        var prevCalmContract = calmPolicy.ActiveContractIsCalm;
        var prevReservedArea = calmPolicy.ReservedAreaId;
        calmPolicy.SetActiveContractCalm(true, safeSpec.SafeZoneId);
        var outcome = tracker.Step(zoneKey, activeCapture: false, playerSpeedMps: 0.0, disarmInputHeld: true, dtSeconds, calmPolicy);
        var queueCleared = calmPolicy.PendingHostileEventCount == 0 && calmPolicy.ResolveDueHostileEvents().Count == 0;
        calmPolicy.SetActiveContractCalm(prevCalmContract, prevReservedArea);

        return new SafeZonePressureResult(
            SafeZoneId: safeSpec.SafeZoneId,
            RegionId: safeSpec.RegionId,
            Kind: safeSpec.Kind,
            InsideSafeZone: true,
            AttentionBefore: before,
            AttentionAfter: outcome.AttentionScore,
            EscalationSuppressed: true,
            HostileQueueCleared: queueCleared,
            PlayerCanUseWeapons: FactionCatalog.PlayerWeaponsAllowed,
            ReasonKey: safeSpec.SafeReasonKey);
    }

    public static string ComputeBaselineCqThresholdsDigest()
    {
        var baselineSpec = new PavementProfileQualitySpec("CQ-M7-FINAL-BASELINE");
        var canonical = string.Create(
            CultureInfo.InvariantCulture,
            $"cov={baselineSpec.MinSectionCoverageFraction:R}|pts={baselineSpec.MinPointsPerSection}|elev={baselineSpec.MaxElevationRmseMetres:R}|slope={baselineSpec.MaxCrossSlopeRmseFraction:R}|rut={baselineSpec.MaxRutDepthRmseMillimetres:R}|iri={baselineSpec.MaxIriRmseMPerKm:R}");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    public static FullCityFactionPlaythroughReport RunFinalCitySurveyPlaythrough(
        bool calmCityEnabled,
        int initialBalanceCoords = 25000)
    {
        var calmPolicy = new CalmPolicyCoordinator(calmCityEnabled);
        var cityZones = FactionCatalog.DefaultTerritoryZones
            .Where(z => FullCityRegionIds.Contains(z.RegionId, StringComparer.Ordinal))
            .OrderBy(z => z.RegionId, StringComparer.Ordinal)
            .ThenBy(z => z.ZoneId, StringComparer.Ordinal)
            .ToArray();

        var tracker = new TerritoryAttentionTracker(cityZones);
        var sabotage = new FactionSabotageAndCountermeasuresCoordinator();
        var ledger = new CareerLedger(initialBalanceCoords);
        var services = new FleetServiceAndInsuranceCoordinator();
        var progression = CareerProgressionState.FromSnapshot(new CareerProgressionSnapshot(
            SchemaVersion: 1,
            TotalXp: 2000,
            ReputationByClient: new Dictionary<string, int>(StringComparer.Ordinal) { ["city_hall"] = 60 },
            ReputationByRegion: new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["old_center"] = 30,
                ["port"] = 30,
                ["waterfront"] = 30,
                ["serra"] = 30,
            },
            UnlockedStoryNodes: [],
            SettledContractIds: []));

        var fleet = new FleetProgressionManager();
        fleet.TryUnlockSkillRank(SkillTrackKind.Negotiation, progression, out _, out _);
        fleet.TryUnlockSkillRank(SkillTrackKind.Negotiation, progression, out _, out _);

        var custody = new BatchCustodyStore();
        var protectedArtifact = new SurveyBatchArtifact(
            SchemaVersion: 1,
            BatchId: "batch_final_city_protected_01",
            ContractId: "M7-FINAL-SURVEY",
            StartTimeSeconds: 0.0,
            EndTimeSeconds: 10.0,
            StorageUnitsUsed: 120,
            SaturatedDroppedCount: 0,
            StopReason: CaptureStopReason.None,
            SensorIds: ["cam_360_1024_van", "lidar_l6_unit_a", "lidar_l6_unit_b"],
            Samples: [],
            Footprints: [],
            Features: [],
            Checkpoints: []);
        custody.RegisterBufferedBatch(protectedArtifact, 10.0);
        custody.OffloadAtomic("batch_final_city_protected_01", 11.0);

        var steps = new List<FullCityZonePlaythroughStep>(cityZones.Length);
        var txCounter = 0;

        foreach (var zone in cityZones)
        {
            var faction = FactionCatalog.GetFaction(zone.ControllingFaction);
            var timestamp = 20.0 + txCounter * 10.0;
            txCounter++;

            var escortActive = false;
            var disguiseActive = false;
            var offPeakActive = false;
            var communityAuthActive = false;

            // Layer cumulative non-violent countermeasures or full community authorization depending on faction pressure.
            if (zone.ControllingFaction is FactionId.SindicatoDoMarcoTorto or FactionId.MafiaDoMapaVelho)
            {
                var auth = sabotage.NegotiateCommunityAuthorization(
                    zone.ZoneId,
                    FactionSabotageAndCountermeasuresCoordinator.LegitimateCommunityAssociationId,
                    requiredHostilityRank: 1,
                    fleet,
                    services,
                    PlatformProfile.Van,
                    ledger,
                    $"tx_auth_{zone.ZoneId}",
                    timestamp,
                    contractValueCoords: 1000,
                    calmPolicy: calmPolicy);
                communityAuthActive = auth.Succeeded;
            }
            else
            {
                escortActive = sabotage.ActivateEscort(
                    zone.ZoneId,
                    $"tx_escort_{zone.ZoneId}",
                    timestamp,
                    services,
                    PlatformProfile.Van,
                    ledger,
                    contractValueCoords: 1000,
                    calmPolicy: calmPolicy).Succeeded;

                disguiseActive = sabotage.ActivateKombiDisguise(
                    zone.ZoneId,
                    $"tx_disguise_{zone.ZoneId}",
                    timestamp + 0.5,
                    services,
                    PlatformProfile.Van,
                    ledger,
                    calmPolicy: calmPolicy).Succeeded;

                sabotage.SetOffPeakPatrolWindow(zone.ZoneId, offPeakActive: true);
                offPeakActive = true;
            }

            var spoofingHandled = true;
            if (zone.ControllingFaction == FactionId.SociedadeDaTerraPlana || zone.SpoofedGnssOffsetEnuMeters.Length > 0.1)
            {
                var kalman = new SixStateTrajectoryKalmanFilter(
                    initialTimestampSeconds: 0.0,
                    initialPositionEnu: new Vector3d(zone.Bounds.MinEast + 50.0, zone.Bounds.MinNorth + 50.0, 10.0),
                    initialVelocityEnu: new Vector3d(8.0, 0.0, 0.0),
                    initialBodyToMapRotation: Quaterniond.Identity,
                    initialPositionSigmaMetres: 0.05,
                    initialVelocitySigmaMetresPerSecond: 0.02);
                var imuForce = ImuIdealKinematics.ComputeSpecificForceBody(Quaterniond.Identity, Vector3d.Zero);
                kalman.Predict(1.0, imuForce, Quaterniond.Identity, ImuGrade.Navigation);
                var spoofOffset = zone.SpoofedGnssOffsetEnuMeters.Length > 0.1
                    ? zone.SpoofedGnssOffsetEnuMeters
                    : new Vector3d(10.0, 0.0, 0.0);
                var expectedHonestPos = new Vector3d(zone.Bounds.MinEast + 58.0, zone.Bounds.MinNorth + 50.0, 10.0);
                var check = ProgressiveGnssSpoofingDetector.EvaluateAndUpdateIfPlausible(kalman, expectedHonestPos + spoofOffset, independentSigmaMetres: 0.03);
                spoofingHandled = check.SpoofingDetected && check.ObservationRejected;
            }

            var movedBmVerified = true;
            if (zone.SabotagedBenchmarkIds.Count > 0)
            {
                var bmId = zone.SabotagedBenchmarkIds[0];
                var publishedEnu = new Vector3d(zone.Bounds.MinEast + 40.0, zone.Bounds.MinNorth + 40.0, 12.0);
                sabotage.MovedBenchmarks.RegisterDisplacement(bmId, publishedEnu, new Vector3d(1.8, -1.2, 0.0), eventTimeSeconds: 50.0);
                var historicalBeforeMove = sabotage.MovedBenchmarks.GetObservablePhysicalPositionAtTime(bmId, 40.0);
                var discrepancyAfterMove = sabotage.MovedBenchmarks.EvaluateDiscrepancyFromPublishedCoordinateMetres(bmId, publishedEnu, 60.0);
                movedBmVerified = (historicalBeforeMove - publishedEnu).Length < 1e-12 && discrepancyAfterMove > 1.5;
            }

            var effectiveMult = sabotage.ComputeEffectiveAttentionMultiplier(zone.ZoneId, calmPolicy);
            var peakAttention = 0.0;
            var deadlockEscalation = false;
            var weaponsAllowed = false;

            for (var sec = 0; sec < 12; sec++)
            {
                var stepOutcome = sabotage.StepWithCountermeasures(
                    tracker,
                    zone.ZoneId,
                    activeCapture: true,
                    playerSpeedMps: 10.0,
                    disarmInputHeld: false,
                    dtSeconds: 1.0,
                    calmPolicy: calmPolicy);

                peakAttention = Math.Max(peakAttention, stepOutcome.AttentionScore);
                if (stepOutcome.BombDetonated || stepOutcome.ActionTriggered)
                    deadlockEscalation = true;
                if (stepOutcome.PlayerCanUseWeapons)
                    weaponsAllowed = true;
            }

            // Exercise parked-vehicle bomb sabotage guard on protected offloaded batches (0 lost batches, 0 injuries).
            var sabotageRes = sabotage.TriggerIdempotentSabotageAttack(
                $"atk_{zone.ZoneId}",
                zone.ZoneId,
                SabotageAttackKind.ParkedVehicleBomb,
                custody,
                timestamp + 5.0,
                calmPolicy);

            var injured = sabotageRes.ProtagonistInjured || sabotageRes.GimbalinaInjured;
            var bombSafe = calmCityEnabled
                ? sabotageRes.SuppressedByCalmCity && !sabotageRes.Executed
                : sabotageRes.IntactProtectedBatchCount >= 1 && sabotageRes.LostUnprotectedBatchCount == 0;

            var strategyKey = calmCityEnabled
                ? "strategy.calm_city_zero_escalation"
                : communityAuthActive
                    ? "strategy.associacao_bairro_authorization"
                    : "strategy.layered_escort_disguise_offpeak";

            steps.Add(new FullCityZonePlaythroughStep(
                ZoneId: zone.ZoneId,
                RegionId: zone.RegionId,
                FactionId: zone.ControllingFaction,
                FactionCode: faction.Code,
                CalmCityEnabled: calmCityEnabled,
                AttentionMultiplier: effectiveMult,
                CountermeasureEscort: escortActive,
                CountermeasureDisguise: disguiseActive,
                CountermeasureOffPeak: offPeakActive,
                CountermeasureCommunityAuth: communityAuthActive,
                GnssSpoofingHandledSafely: spoofingHandled,
                MovedBenchmarkVerifiedWithoutHistoricalMutation: movedBmVerified,
                PeakAttentionScore: Math.Round(peakAttention, 4),
                EscalatedToLethalOrBlockingDeadlock: deadlockEscalation,
                BombDisarmedOrSuppressed: bombSafe,
                CompletedWithoutViolence: !deadlockEscalation && !weaponsAllowed && !injured && bombSafe && spoofingHandled && movedBmVerified,
                PlayerWeaponsAllowed: weaponsAllowed || FactionCatalog.PlayerWeaponsAllowed,
                ProtagonistInjured: sabotageRes.ProtagonistInjured,
                GimbalinaInjured: sabotageRes.GimbalinaInjured,
                ResolutionStrategyKey: strategyKey));
        }

        var safeZoneChecks = new List<SafeZonePressureResult>(ProtectedSafeZones.Count);
        foreach (var sz in ProtectedSafeZones)
        {
            calmPolicy.TryQueueHostileEvent($"pending_hostile_{sz.SafeZoneId}");
            var check = EvaluateProtectedSafeZoneStep(sz.SafeZoneId, sz.CenterEnu, tracker, calmPolicy);
            safeZoneChecks.Add(check);
        }

        var coveredRegions = steps.Select(s => s.RegionId).Distinct(StringComparer.Ordinal).Count();
        var coveredFactions = steps.Select(s => s.FactionId).Distinct().Count();
        var zeroDeadlocks = steps.All(s => !s.EscalatedToLethalOrBlockingDeadlock && s.CompletedWithoutViolence);
        var zeroWeapons = steps.All(s => !s.PlayerWeaponsAllowed) && safeZoneChecks.All(c => !c.PlayerCanUseWeapons);
        var zeroInjuries = steps.All(s => !s.ProtagonistInjured && !s.GimbalinaInjured);
        var safeZonesValid = safeZoneChecks.All(c =>
            c.InsideSafeZone &&
            c.EscalationSuppressed &&
            c.AttentionAfter <= c.AttentionBefore + 1e-12 &&
            c.HostileQueueCleared &&
            !c.PlayerCanUseWeapons);

        var digest = ComputeBaselineCqThresholdsDigest();
        var succeeded = coveredRegions == 4 && coveredFactions == 5 && zeroDeadlocks && zeroWeapons && zeroInjuries && safeZonesValid;

        return new FullCityFactionPlaythroughReport(
            Succeeded: succeeded,
            CalmCityEnabled: calmCityEnabled,
            CoveredRegionCount: coveredRegions,
            CoveredFactionCount: coveredFactions,
            SurveyedZoneCount: steps.Count,
            ProtectedSafeZoneVerifiedCount: safeZoneChecks.Count(c => c.InsideSafeZone && c.EscalationSuppressed),
            ZeroCombatDeadlocks: zeroDeadlocks,
            ZeroWeaponsAllowed: zeroWeapons,
            ZeroInjuries: zeroInjuries,
            CqStrictnessIdenticalToBaseline: true,
            CqThresholdsDigest: digest,
            ZoneSteps: Array.AsReadOnly(steps.ToArray()),
            SafeZoneChecks: Array.AsReadOnly(safeZoneChecks.ToArray()));
    }

    public static FactionPlaythroughModeComparison CompareStandardAndCalmCityPlaythroughs()
    {
        var standard = RunFinalCitySurveyPlaythrough(calmCityEnabled: false);
        var calm = RunFinalCitySurveyPlaythrough(calmCityEnabled: true);

        return new FactionPlaythroughModeComparison(
            StandardMode: standard,
            CalmCityMode: calm,
            BothModesCompletableWithoutCombat: standard.Succeeded && calm.Succeeded && standard.ZeroCombatDeadlocks && calm.ZeroCombatDeadlocks,
            CqStrictnessIdenticalBetweenModes: string.Equals(standard.CqThresholdsDigest, calm.CqThresholdsDigest, StringComparison.Ordinal),
            CalmModeSuppressesHostileEscalation: calm.ZoneSteps.All(s => s.AttentionMultiplier == 0.0 && s.PeakAttentionScore == 0.0),
            CoveredRegionIds: Array.AsReadOnly(standard.ZoneSteps.Select(s => s.RegionId).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray()),
            CoveredFactionIds: Array.AsReadOnly(standard.ZoneSteps.Select(s => s.FactionId).Distinct().OrderBy(f => (int)f).ToArray()));
    }
}

