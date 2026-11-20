using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Fusion;
using Gsa.Core.Sensors.Gnss;
using Gsa.Core.Traffic;
using Gsa.Missions.Persistence;

namespace Gsa.Missions.Economy;

public enum SabotageAttackKind
{
    GnssSpoofingRamp = 0,
    MovedBenchmarkDiscrepancy = 1,
    RoadblockOrInterception = 2,
    ParkedVehicleBomb = 3,
}

public sealed record GnssSpoofingCheckOutcome(
    bool SpoofingDetected,
    bool ObservationRejected,
    double MaxNormalizedAxisInnovation,
    double ThreeDimensionalMahalanobisDistance,
    string ReasonKey);

public sealed record MovedBenchmarkRecord(
    string BenchmarkId,
    Vector3d HistoricalEnuPosition,
    Vector3d DisplacedEnuPosition,
    double DisplacedFromTimeSeconds);

public sealed record SabotageAttackResolutionResult(
    bool Executed,
    bool IdempotentAlreadyResolved,
    bool SuppressedByCalmCity,
    string AttackEventId,
    SabotageAttackKind Kind,
    int LostUnprotectedBatchCount,
    int IntactProtectedBatchCount,
    bool ProtagonistInjured,
    bool GimbalinaInjured,
    bool DisguiseBrokenInZone,
    string ReasonKey);

public sealed record TerritoryCountermeasureState(
    string ZoneId,
    bool EscortActive,
    bool DisguiseActive,
    bool DisguiseBrokenByAttack,
    bool OffPeakScheduleActive,
    bool CommunityAuthorizationActive);

public sealed record SabotageCoordinatorSnapshot(
    int SchemaVersion,
    IReadOnlyList<string> ResolvedAttackIds,
    IReadOnlyList<TerritoryCountermeasureState> ZoneStates,
    IReadOnlyList<MovedBenchmarkRecord> MovedBenchmarks);

/// <summary>Detects progressive GNSS spoofing via Kalman innovation residuals strictly without reading truth pose (T-F16-03).</summary>
public static class ProgressiveGnssSpoofingDetector
{
    public const double DefaultSpoofingMaxDriftMetres = 10.0;
    public const double DefaultSpoofingRampDurationSeconds = 5.0;
    public const double DefaultSigmaThreshold = 3.0;

    /// <summary>Computes progressive GNSS spoofing bias up to 10 m over 5 s (2 m/s ramp) in ENU horizontal coordinates.</summary>
    public static Vector3d ComputeSpoofingOffsetEnu(
        double elapsedSinceSpoofStartSeconds,
        double maxDriftMetres = DefaultSpoofingMaxDriftMetres,
        double rampDurationSeconds = DefaultSpoofingRampDurationSeconds)
    {
        if (elapsedSinceSpoofStartSeconds <= 0.0 || maxDriftMetres <= 0.0 || rampDurationSeconds <= 0.0)
            return Vector3d.Zero;

        var fraction = Math.Clamp(elapsedSinceSpoofStartSeconds / rampDurationSeconds, 0.0, 1.0);
        var eastOffset = maxDriftMetres * fraction;
        return new Vector3d(eastOffset, 0.0, 0.0);
    }

    /// <summary>Tests a potentially spoofed GNSS observation against predicted 6-state Kalman innovation; rejects if &gt; 3-sigma without truth access.</summary>
    public static GnssSpoofingCheckOutcome EvaluateAndUpdateIfPlausible(
        SixStateTrajectoryKalmanFilter filter,
        GnssObservation incomingGnss,
        double innovationSigmaThreshold = DefaultSigmaThreshold)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(incomingGnss);

        if (!incomingGnss.IsAvailable || incomingGnss.MeasuredPositionEnu is null)
        {
            return new GnssSpoofingCheckOutcome(
                SpoofingDetected: false,
                ObservationRejected: true,
                MaxNormalizedAxisInnovation: 0.0,
                ThreeDimensionalMahalanobisDistance: 0.0,
                ReasonKey: "gnss.unavailable");
        }

        return EvaluateAndUpdateIfPlausible(
            filter,
            incomingGnss.MeasuredPositionEnu.Value,
            incomingGnss.IndependentSigmaMetres ?? 1.5,
            incomingGnss.Dop,
            innovationSigmaThreshold);
    }

    /// <summary>Tests a GNSS position measurement (possibly offset by progressive spoofing) against Kalman innovation without truth access.</summary>
    public static GnssSpoofingCheckOutcome EvaluateAndUpdateIfPlausible(
        SixStateTrajectoryKalmanFilter filter,
        Vector3d measuredPositionEnu,
        double independentSigmaMetres = 0.03,
        double dop = 1.0,
        double innovationSigmaThreshold = DefaultSigmaThreshold)
    {
        ArgumentNullException.ThrowIfNull(filter);

        // Reuse the filter's pure innovation/Mahalanobis consistency check against predicted IMU+DMI state.
        var sigma = Math.Max(0.015, independentSigmaMetres * Math.Max(1.0, dop * 0.5));
        var posBefore = filter.PositionEnu;
        var sigmaBefore = filter.PositionSigmaEnu;

        // Compute normalized axis residual directly from predicted filter position and combined (prior + measurement) std dev.
        var residual = measuredPositionEnu - posBefore;
        var sx = Math.Sqrt(sigmaBefore.X * sigmaBefore.X + sigma * sigma);
        var sy = Math.Sqrt(sigmaBefore.Y * sigmaBefore.Y + sigma * sigma);
        var sz = Math.Sqrt(sigmaBefore.Z * sigmaBefore.Z + sigma * sigma);
        var zX = Math.Abs(residual.X) / Math.Max(1e-6, sx);
        var zY = Math.Abs(residual.Y) / Math.Max(1e-6, sy);
        var zZ = Math.Abs(residual.Z) / Math.Max(1e-6, sz);
        var maxAxisZ = Math.Max(zX, Math.Max(zY, zZ));
        var mahalanobisApprox = Math.Sqrt(zX * zX + zY * zY + zZ * zZ);

        if (maxAxisZ > innovationSigmaThreshold || !filter.UpdatePositionControlPoint(measuredPositionEnu, sigma, out var mahalanobisExact))
        {
            return new GnssSpoofingCheckOutcome(
                SpoofingDetected: true,
                ObservationRejected: true,
                MaxNormalizedAxisInnovation: maxAxisZ,
                ThreeDimensionalMahalanobisDistance: mahalanobisApprox,
                ReasonKey: "sabotage.gnss_spoofing_3sigma_detected");
        }

        return new GnssSpoofingCheckOutcome(
            SpoofingDetected: false,
            ObservationRejected: false,
            MaxNormalizedAxisInnovation: maxAxisZ,
            ThreeDimensionalMahalanobisDistance: mahalanobisExact,
            ReasonKey: "gnss.innovation_consistent");
    }
}

/// <summary>Tracks displaced physical field monuments while preserving immutable historical coordinates and contract EvaluationReference.</summary>
public sealed class MovedBenchmarkRegistry
{
    private readonly Dictionary<string, MovedBenchmarkRecord> _benchmarks = new(StringComparer.Ordinal);

    public IReadOnlyList<MovedBenchmarkRecord> Records => _benchmarks.Values.OrderBy(r => r.BenchmarkId, StringComparer.Ordinal).ToArray();

    public void RegisterDisplacement(
        string benchmarkId,
        Vector3d originalPositionEnu,
        Vector3d physicalDisplacementEnu,
        double eventTimeSeconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(benchmarkId);
        if (!double.IsFinite(eventTimeSeconds) || eventTimeSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(eventTimeSeconds));

        _benchmarks[benchmarkId] = new MovedBenchmarkRecord(
            benchmarkId,
            originalPositionEnu,
            originalPositionEnu + physicalDisplacementEnu,
            eventTimeSeconds);
    }

    public Vector3d GetObservablePhysicalPositionAtTime(string benchmarkId, double observationTimeSeconds)
    {
        if (!_benchmarks.TryGetValue(benchmarkId, out var rec))
            throw new KeyNotFoundException($"Benchmark '{benchmarkId}' is not registered.");

        return observationTimeSeconds >= rec.DisplacedFromTimeSeconds
            ? rec.DisplacedEnuPosition
            : rec.HistoricalEnuPosition;
    }

    public double EvaluateDiscrepancyFromPublishedCoordinateMetres(
        string benchmarkId,
        Vector3d publishedHistoricalCoordinateEnu,
        double observationTimeSeconds)
    {
        var observed = GetObservablePhysicalPositionAtTime(benchmarkId, observationTimeSeconds);
        return (observed - publishedHistoricalCoordinateEnu).Length;
    }
}

/// <summary>Coordinates advanced faction sabotage, custody loss isolation, non-violence, and cumulative countermeasures (T-F16-03).</summary>
public sealed class FactionSabotageAndCountermeasuresCoordinator
{
    public const double EscortAttentionMultiplier = 0.50;
    public const double DisguiseAttentionMultiplier = 0.50;
    public const double OffPeakAttentionMultiplier = 0.50;
    public const double CumulativeAttentionMultiplierFloor = 0.25;
    public const string LegitimateCommunityAssociationId = "associacao_bairro";

    private readonly HashSet<string> _resolvedAttackIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TerritoryCountermeasureState> _zoneStates = new(StringComparer.Ordinal);
    private readonly MovedBenchmarkRegistry _movedBenchmarks = new();

    public MovedBenchmarkRegistry MovedBenchmarks => _movedBenchmarks;
    public IReadOnlySet<string> ResolvedAttackIds => _resolvedAttackIds;

    public TerritoryCountermeasureState GetZoneState(string zoneId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zoneId);
        return _zoneStates.GetValueOrDefault(zoneId)
            ?? new TerritoryCountermeasureState(zoneId, false, false, false, false, false);
    }

    public ServiceExecutionResult ActivateEscort(
        string zoneId,
        string transactionId,
        double timestampSeconds,
        FleetServiceAndInsuranceCoordinator services,
        PlatformProfile platform,
        CareerLedger ledger,
        int contractValueCoords,
        CalmPolicyCoordinator? calmPolicy = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zoneId);
        ArgumentNullException.ThrowIfNull(services);

        // Calm City suppresses hostile factions so no unnecessary countermeasure fee is charged.
        if (calmPolicy?.IsProtectionActive == true)
        {
            SetState(GetZoneState(zoneId) with { EscortActive = true });
            return new ServiceExecutionResult(true, ServiceKind.SecurityEscort, null, new ServiceCostQuote(ServiceKind.SecurityEscort, true, 0, 0.0, 0, "calm.zero_fee"), "calm.zero_fee");
        }

        var exec = services.ExecuteService(transactionId, timestampSeconds, ServiceKind.SecurityEscort, platform, ledger, contractOrCargoValueCoords: contractValueCoords);
        if (exec.Succeeded)
            SetState(GetZoneState(zoneId) with { EscortActive = true });
        return exec;
    }

    public ServiceExecutionResult ActivateKombiDisguise(
        string zoneId,
        string transactionId,
        double timestampSeconds,
        FleetServiceAndInsuranceCoordinator services,
        PlatformProfile platform,
        CareerLedger ledger,
        CalmPolicyCoordinator? calmPolicy = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zoneId);
        ArgumentNullException.ThrowIfNull(services);

        if (calmPolicy?.IsProtectionActive == true)
        {
            SetState(GetZoneState(zoneId) with { DisguiseActive = true, DisguiseBrokenByAttack = false });
            return new ServiceExecutionResult(true, ServiceKind.VehicleDisguise, null, new ServiceCostQuote(ServiceKind.VehicleDisguise, true, 0, 0.0, 0, "calm.zero_fee"), "calm.zero_fee");
        }

        var exec = services.ExecuteService(transactionId, timestampSeconds, ServiceKind.VehicleDisguise, platform, ledger);
        if (exec.Succeeded)
            SetState(GetZoneState(zoneId) with { DisguiseActive = true, DisguiseBrokenByAttack = false });
        return exec;
    }

    public void SetOffPeakPatrolWindow(string zoneId, bool offPeakActive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zoneId);
        SetState(GetZoneState(zoneId) with { OffPeakScheduleActive = offPeakActive });
    }

    /// <summary>Negotiates zone authorization strictly with the legitimate neighborhood association using player negotiation skill.</summary>
    public (bool Succeeded, string ReasonKey, ServiceExecutionResult? ServiceResult) NegotiateCommunityAuthorization(
        string zoneId,
        string counterpartEntityId,
        int requiredHostilityRank,
        FleetProgressionManager progression,
        FleetServiceAndInsuranceCoordinator services,
        PlatformProfile platform,
        CareerLedger ledger,
        string transactionId,
        double timestampSeconds,
        int contractValueCoords,
        CalmPolicyCoordinator? calmPolicy = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zoneId);
        ArgumentException.ThrowIfNullOrWhiteSpace(counterpartEntityId);
        ArgumentNullException.ThrowIfNull(progression);
        ArgumentNullException.ThrowIfNull(services);

        if (!string.Equals(counterpartEntityId, LegitimateCommunityAssociationId, StringComparison.Ordinal))
            return (false, "authorization.error.bribery_not_allowed", null);

        var negotiationRank = progression.GetSkillRank(SkillTrackKind.Negotiation);
        if (negotiationRank < requiredHostilityRank)
            return (false, "authorization.error.negotiation_skill_insufficient", null);

        if (calmPolicy?.IsProtectionActive == true)
        {
            SetState(GetZoneState(zoneId) with { CommunityAuthorizationActive = true });
            return (true, "calm.zero_fee", null);
        }

        var exec = services.ExecuteService(
            transactionId,
            timestampSeconds,
            ServiceKind.ControlledZoneAuthorization,
            platform,
            ledger,
            contractOrCargoValueCoords: contractValueCoords);

        if (!exec.Succeeded)
            return (false, exec.RejectionReasonKey ?? "ledger.error.insufficient_funds", exec);

        SetState(GetZoneState(zoneId) with { CommunityAuthorizationActive = true });
        return (true, "authorization.community_granted", exec);
    }

    /// <summary>Computes effective attention gain multiplier in [0, 1] honoring multiplicative countermeasures and the 0.25x floor.</summary>
    public double ComputeEffectiveAttentionMultiplier(string zoneId, CalmPolicyCoordinator? calmPolicy = null)
    {
        if (calmPolicy?.IsProtectionActive == true)
            return 0.0;

        var state = GetZoneState(zoneId);
        if (state.CommunityAuthorizationActive)
            return 0.0;

        var mult = 1.0;
        if (state.EscortActive)
            mult *= EscortAttentionMultiplier;
        if (state.DisguiseActive && !state.DisguiseBrokenByAttack)
            mult *= DisguiseAttentionMultiplier;
        if (state.OffPeakScheduleActive)
            mult *= OffPeakAttentionMultiplier;

        return Math.Max(CumulativeAttentionMultiplierFloor, mult);
    }

    /// <summary>Advances territory attention applying countermeasures; zero attention when Calm City or community authorization is active.</summary>
    public TerritoryEscalationOutcome StepWithCountermeasures(
        TerritoryAttentionTracker tracker,
        string zoneId,
        bool activeCapture,
        double playerSpeedMps,
        bool disarmInputHeld,
        double dtSeconds,
        CalmPolicyCoordinator? calmPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        ArgumentException.ThrowIfNullOrWhiteSpace(zoneId);

        var multiplier = ComputeEffectiveAttentionMultiplier(zoneId, calmPolicy);
        return tracker.Step(zoneId, activeCapture, playerSpeedMps, disarmInputHeld, dtSeconds, calmPolicy,
            attentionGainMultiplier: multiplier, suppressThreats: multiplier <= 0);

    }

    /// <summary>Executes idempotent parked-bomb / vehicle sabotage destroying ONLY PendingField batches while preserving ProtectedOffloaded batches.</summary>
    public SabotageAttackResolutionResult TriggerIdempotentSabotageAttack(
        string attackEventId,
        string zoneId,
        SabotageAttackKind kind,
        BatchCustodyStore custodyStore,
        double timestampSeconds,
        CalmPolicyCoordinator? calmPolicy = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attackEventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(zoneId);
        ArgumentNullException.ThrowIfNull(custodyStore);

        if (calmPolicy?.IsProtectionActive == true)
        {
            return new SabotageAttackResolutionResult(
                Executed: false,
                IdempotentAlreadyResolved: false,
                SuppressedByCalmCity: true,
                AttackEventId: attackEventId,
                Kind: kind,
                LostUnprotectedBatchCount: 0,
                IntactProtectedBatchCount: custodyStore.Entries.Count(e => e.Stage == BatchCustodyStage.Protected),
                ProtagonistInjured: false,
                GimbalinaInjured: false,
                DisguiseBrokenInZone: false,
                ReasonKey: "sabotage.suppressed_by_calm_city");
        }

        if (!_resolvedAttackIds.Add(attackEventId))
        {
            return new SabotageAttackResolutionResult(
                Executed: false,
                IdempotentAlreadyResolved: true,
                SuppressedByCalmCity: false,
                AttackEventId: attackEventId,
                Kind: kind,
                LostUnprotectedBatchCount: 0,
                IntactProtectedBatchCount: custodyStore.Entries.Count(e => e.Stage == BatchCustodyStage.Protected),
                ProtagonistInjured: false,
                GimbalinaInjured: false,
                DisguiseBrokenInZone: GetZoneState(zoneId).DisguiseBrokenByAttack,
                ReasonKey: "sabotage.attack_already_resolved_idempotent");
        }

        var lostCount = 0;
        if (kind == SabotageAttackKind.ParkedVehicleBomb)
            lostCount = custodyStore.HandleVehicleLoss(timestampSeconds);

        // First attack in the zone breaks any active disguise for the remainder of the operation in that territory.
        var state = GetZoneState(zoneId);
        var disguiseBroken = state.DisguiseActive;
        if (disguiseBroken)
            SetState(state with { DisguiseBrokenByAttack = true });

        var intactCount = custodyStore.Entries.Count(e => e.Stage == BatchCustodyStage.Protected);
        return new SabotageAttackResolutionResult(
            Executed: true,
            IdempotentAlreadyResolved: false,
            SuppressedByCalmCity: false,
            AttackEventId: attackEventId,
            Kind: kind,
            LostUnprotectedBatchCount: lostCount,
            IntactProtectedBatchCount: intactCount,
            ProtagonistInjured: false,
            GimbalinaInjured: false,
            DisguiseBrokenInZone: disguiseBroken,
            ReasonKey: $"sabotage.{kind.ToString().ToLowerInvariant()}.executed");
    }

    public SabotageCoordinatorSnapshot ExportSnapshot() =>
        new(
            SchemaVersion: 1,
            ResolvedAttackIds: _resolvedAttackIds.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            ZoneStates: _zoneStates.Values.OrderBy(z => z.ZoneId, StringComparer.Ordinal).ToArray(),
            MovedBenchmarks: _movedBenchmarks.Records);

    public static FactionSabotageAndCountermeasuresCoordinator ImportSnapshot(SabotageCoordinatorSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var coordinator = new FactionSabotageAndCountermeasuresCoordinator();
        foreach (var id in snapshot.ResolvedAttackIds)
            coordinator._resolvedAttackIds.Add(id);
        foreach (var state in snapshot.ZoneStates)
            coordinator._zoneStates[state.ZoneId] = state;
        foreach (var bm in snapshot.MovedBenchmarks)
        {
            coordinator._movedBenchmarks.RegisterDisplacement(
                bm.BenchmarkId,
                bm.HistoricalEnuPosition,
                bm.DisplacedEnuPosition - bm.HistoricalEnuPosition,
                bm.DisplacedFromTimeSeconds);
        }
        return coordinator;
    }

    private void SetState(TerritoryCountermeasureState state) =>
        _zoneStates[state.ZoneId] = state;
}
