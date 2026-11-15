using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Calibration;
using Gsa.Core.Sensors.Camera;

namespace Gsa.Core.Traffic;

public sealed record ErraticSpawnContext(
    bool IsNight,
    bool NearBarOnWeekend,
    bool IsCalmContract,
    bool CalmCityEnabled);

public sealed record ErraticDriverSample(
    string AgentId,
    double TimeSeconds,
    double SteeringPerturbationRadians,
    double SpeedMultiplier,
    bool CommittedIntersectionInfraction,
    bool HasZigZagCue,
    bool HasHornCue,
    bool HasHeadlightCue,
    bool RemovedByBlitz);

/// <summary>Contextual erratic driver spawns, periodic steering/speed perturbations, intersection infractions, and blitz removal.</summary>
public static class ErraticDriverSimulator
{
    public const double DaytimeSpawnProbability = 0.02;
    public const double NighttimeSpawnProbability = 0.08;
    public const double WeekendBarSpawnProbability = 0.12;
    public const double MaxSpawnProbability = 0.12;
    public const double SteeringPerturbationAmplitudeRadians = 0.2;
    public const double IntersectionInfractionProbability = 0.20;

    public static double ComputeSpawnProbability(ErraticSpawnContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.IsCalmContract || context.CalmCityEnabled)
            return 0.0;

        if (context.NearBarOnWeekend)
            return Math.Min(MaxSpawnProbability, WeekendBarSpawnProbability);

        return context.IsNight ? NighttimeSpawnProbability : DaytimeSpawnProbability;
    }

    public static ErraticDriverSample EvaluateDriverStep(
        string agentId,
        double timeSeconds,
        double periodSeconds = 3.0,
        double normalizedSpeedDelta = 0.25,
        bool atIntersection = false,
        double infractionRoll01 = 0.5,
        bool enteredBlitzZone = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        if (!double.IsFinite(timeSeconds) || timeSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(timeSeconds));
        if (!double.IsFinite(periodSeconds) || periodSeconds <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(periodSeconds));
        if (!double.IsFinite(normalizedSpeedDelta))
            throw new ArgumentOutOfRangeException(nameof(normalizedSpeedDelta));

        var clampedPeriod = Math.Clamp(periodSeconds, 2.0, 5.0);
        var phase = 2.0 * Math.PI * timeSeconds / clampedPeriod;
        var steerNoise = SteeringPerturbationAmplitudeRadians * Math.Sin(phase);
        var speedMult = 1.0 + Math.Clamp(normalizedSpeedDelta, -0.30, 0.30);
        var infraction = atIntersection && infractionRoll01 < IntersectionInfractionProbability;

        return new ErraticDriverSample(
            AgentId: agentId,
            TimeSeconds: timeSeconds,
            SteeringPerturbationRadians: steerNoise,
            SpeedMultiplier: speedMult,
            CommittedIntersectionInfraction: infraction,
            HasZigZagCue: true,
            HasHornCue: true,
            HasHeadlightCue: true,
            RemovedByBlitz: enteredBlitzZone);
    }
}

public sealed record DogPursuitState(
    string DogId,
    bool Active,
    double ElapsedSeconds,
    double DistanceMetres,
    bool PacifiedByTreat,
    bool SuppressedByCalmPolicy,
    string? EndReasonKey);

/// <summary>Tracks stray dog pursuit (<= 30 m trigger, max 10 s duration, ends > 40 m, on treat, or via CalmPolicy).</summary>
public sealed class DogPursuitTracker
{
    public const double TriggerRadiusMetres = 30.0;
    public const double DisengageDistanceMetres = 40.0;
    public const double MaxPursuitDurationSeconds = 10.0;

    public string DogId { get; }
    public bool Active { get; private set; }
    public double ElapsedSeconds { get; private set; }
    public double DistanceMetres { get; private set; }
    public bool PacifiedByTreat { get; private set; }
    public bool SuppressedByCalmPolicy { get; private set; }
    public string? EndReasonKey { get; private set; }

    public DogPursuitTracker(string dogId, double initialDistanceMetres, bool calmProtectionActive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dogId);
        if (!double.IsFinite(initialDistanceMetres) || initialDistanceMetres < 0.0)
            throw new ArgumentOutOfRangeException(nameof(initialDistanceMetres));

        DogId = dogId;
        DistanceMetres = initialDistanceMetres;
        if (calmProtectionActive)
        {
            Active = false;
            SuppressedByCalmPolicy = true;
            EndReasonKey = "hazard.dog.suppressed_by_calm";
        }
        else if (initialDistanceMetres <= TriggerRadiusMetres)
        {
            Active = true;
        }
        else
        {
            Active = false;
            EndReasonKey = "hazard.dog.out_of_range";
        }
    }

    public DogPursuitState Step(double dtSeconds, double currentDistanceMetres)
    {
        if (!double.IsFinite(dtSeconds) || dtSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(dtSeconds));
        if (!double.IsFinite(currentDistanceMetres) || currentDistanceMetres < 0.0)
            throw new ArgumentOutOfRangeException(nameof(currentDistanceMetres));

        DistanceMetres = currentDistanceMetres;
        if (!Active)
            return ExportState();

        ElapsedSeconds += dtSeconds;
        if (currentDistanceMetres > DisengageDistanceMetres)
        {
            Active = false;
            EndReasonKey = "hazard.dog.out_of_range";
        }
        else if (ElapsedSeconds >= MaxPursuitDurationSeconds)
        {
            Active = false;
            EndReasonKey = "hazard.dog.timeout";
        }

        return ExportState();
    }

    public DogPursuitState ApplyTreat()
    {
        Active = false;
        PacifiedByTreat = true;
        EndReasonKey = "hazard.dog.pacified_by_treat";
        return ExportState();
    }

    public DogPursuitState SuppressByCalmPolicy()
    {
        Active = false;
        SuppressedByCalmPolicy = true;
        EndReasonKey = "hazard.dog.suppressed_by_calm";
        return ExportState();
    }

    public DogPursuitState ExportState() =>
        new(DogId, Active, ElapsedSeconds, DistanceMetres, PacifiedByTreat, SuppressedByCalmPolicy, EndReasonKey);
}

/// <summary>Centralized Calm City and calm-contract hazard suppressor preventing any delayed threats after activation.</summary>
public sealed class CalmPolicyCoordinator
{
    private readonly List<DogPursuitTracker> _dogPursuits = [];
    private readonly HashSet<string> _pendingHostileEventIds = new(StringComparer.Ordinal);

    public bool CalmCityEnabled { get; private set; }
    public bool ActiveContractIsCalm { get; private set; }
    public string? ReservedAreaId { get; private set; }
    public bool IsProtectionActive => CalmCityEnabled || ActiveContractIsCalm;
    public string ProtectionIndicatorKey => IsProtectionActive ? "hud.calm_protection.active" : "hud.calm_protection.inactive";
    public int PendingHostileEventCount => _pendingHostileEventIds.Count;
    public IReadOnlyList<DogPursuitTracker> DogPursuits => _dogPursuits;

    public CalmPolicyCoordinator(bool calmCityEnabled = false)
    {
        CalmCityEnabled = calmCityEnabled;
    }

    public void SetCalmCityEnabled(bool enabled)
    {
        CalmCityEnabled = enabled;
        if (IsProtectionActive)
            ClearAllActiveAndPendingHazards();
    }

    public void SetActiveContractCalm(bool isCalm, string? reservedAreaId = null)
    {
        ActiveContractIsCalm = isCalm;
        ReservedAreaId = isCalm ? reservedAreaId : null;
        if (IsProtectionActive)
            ClearAllActiveAndPendingHazards();
    }

    public DogPursuitTracker RegisterDogEncounter(string dogId, double initialDistanceMetres)
    {
        var tracker = new DogPursuitTracker(dogId, initialDistanceMetres, IsProtectionActive);
        _dogPursuits.Add(tracker);
        return tracker;
    }

    public bool TryQueueHostileEvent(string eventId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        if (IsProtectionActive)
            return false;

        return _pendingHostileEventIds.Add(eventId);
    }

    public IReadOnlyList<string> ResolveDueHostileEvents()
    {
        if (IsProtectionActive)
        {
            ClearAllActiveAndPendingHazards();
            return [];
        }

        var released = _pendingHostileEventIds.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        _pendingHostileEventIds.Clear();
        return released;
    }

    private void ClearAllActiveAndPendingHazards()
    {
        _pendingHostileEventIds.Clear();
        foreach (var dog in _dogPursuits)
            dog.SuppressByCalmPolicy();
    }
}

public enum UrbanHazardKind
{
    ErraticVehicleCollision = 0,
    PotholeImpact = 1,
    DogChase = 2,
    FireworksOrBomb = 3,
    SabotageVandalism = 4,
    RoadMudSplatter = 5,
    HornStartle = 6,
    FlanelinhaApproach = 7,
}

public sealed record UrbanHazardOutcome(
    string HazardId,
    UrbanHazardKind Kind,
    bool SuppressedByCalmPolicy,
    int TipPaidCoords,
    int CosmeticDamageAdded,
    double GimbalinaStressAfter,
    double LensObstructionAfter,
    bool RackInspectionRequired,
    RackImpactEvent? ImpactEvent,
    ImpactPerturbationSymptom? RackSymptom,
    DogPursuitState? DogState,
    string ReasonKey);

/// <summary>Integrates T-F09-05 urban hazard table across vehicle suspension, modular rack, Gimbalina stress, lens contamination, and Calm City suppression.</summary>
public sealed class UrbanHazardEncounterCoordinator
{
    public const int FlanelinhaTipCoords = 10;
    public const int FlanelinhaRefusalCosmeticDamage = 5;
    public const double DefaultSabotageSplatterObstruction = 0.45;
    public const double DefaultMudSplatterObstruction = 0.35;

    private readonly List<UrbanHazardOutcome> _history = [];

    public CalmPolicyCoordinator CalmPolicy { get; }
    public CarSuspensionSimulator Vehicle { get; }
    public ModularSensorRack Rack { get; }
    public GimbalinaStabilizer Gimbalina { get; }
    public LensContaminationTracker Lens { get; }
    public int TotalCosmeticDamagePoints { get; private set; }
    public int TotalTipsPaidCoords { get; private set; }
    public IReadOnlyList<UrbanHazardOutcome> HazardHistory => _history;

    public UrbanHazardEncounterCoordinator(
        CalmPolicyCoordinator calmPolicy,
        CarSuspensionSimulator vehicle,
        ModularSensorRack rack,
        GimbalinaStabilizer gimbalina,
        LensContaminationTracker lens)
    {
        ArgumentNullException.ThrowIfNull(calmPolicy);
        ArgumentNullException.ThrowIfNull(vehicle);
        ArgumentNullException.ThrowIfNull(rack);
        ArgumentNullException.ThrowIfNull(gimbalina);
        ArgumentNullException.ThrowIfNull(lens);

        CalmPolicy = calmPolicy;
        Vehicle = vehicle;
        Rack = rack;
        Gimbalina = gimbalina;
        Lens = lens;
    }

    public static bool IsHostileHazardKind(UrbanHazardKind kind) => kind switch
    {
        UrbanHazardKind.ErraticVehicleCollision => true,
        UrbanHazardKind.DogChase => true,
        UrbanHazardKind.FireworksOrBomb => true,
        UrbanHazardKind.SabotageVandalism => true,
        UrbanHazardKind.FlanelinhaApproach => true,
        UrbanHazardKind.PotholeImpact => false,
        UrbanHazardKind.RoadMudSplatter => false,
        UrbanHazardKind.HornStartle => false,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public UrbanHazardOutcome TriggerHazard(
        string hazardId,
        UrbanHazardKind kind,
        ulong impactSeed = 101UL,
        double deltaVMetresPerSecond = 0.0,
        double splatterObstruction = 0.0,
        double initialDogDistanceMetres = 20.0,
        bool payFlanelinhaTip = false,
        int defensiveDrivingRank = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hazardId);
        if (!double.IsFinite(deltaVMetresPerSecond) || deltaVMetresPerSecond < 0.0)
            throw new ArgumentOutOfRangeException(nameof(deltaVMetresPerSecond));
        if (!double.IsFinite(splatterObstruction) || splatterObstruction < 0.0 || splatterObstruction > 1.0)
            throw new ArgumentOutOfRangeException(nameof(splatterObstruction));

        var hostile = IsHostileHazardKind(kind);
        if (hostile && CalmPolicy.IsProtectionActive)
        {
            CalmPolicy.TryQueueHostileEvent(hazardId);
            DogPursuitState? suppressedDog = kind == UrbanHazardKind.DogChase
                ? CalmPolicy.RegisterDogEncounter(hazardId, initialDogDistanceMetres).ExportState()
                : null;

            var suppressedOutcome = new UrbanHazardOutcome(
                HazardId: hazardId,
                Kind: kind,
                SuppressedByCalmPolicy: true,
                TipPaidCoords: 0,
                CosmeticDamageAdded: 0,
                GimbalinaStressAfter: Gimbalina.Stress,
                LensObstructionAfter: Lens.ObstructionFraction,
                RackInspectionRequired: Rack.MountedSlots.Values.Any(s => s.NeedsWorkshopInspection),
                ImpactEvent: null,
                RackSymptom: null,
                DogState: suppressedDog,
                ReasonKey: "hazard.suppressed_by_calm_policy");
            _history.Add(suppressedOutcome);
            return suppressedOutcome;
        }

        RackImpactEvent? impactEvent = null;
        ImpactPerturbationSymptom? rackSymptom = null;
        DogPursuitState? dogState = null;
        var tipPaid = 0;
        var cosmeticAdded = 0;
        string reasonKey;

        switch (kind)
        {
            case UrbanHazardKind.HornStartle:
                Gimbalina.ApplyStartle(StartleEventKind.Horn);
                reasonKey = "hazard.horn_startle";
                break;

            case UrbanHazardKind.PotholeImpact:
                Gimbalina.ApplyStartle(StartleEventKind.Pothole);
                var potholeDv = deltaVMetresPerSecond > 0.0 ? deltaVMetresPerSecond : 3.2;
                impactEvent = Vehicle.ApplyCollisionImpact(potholeDv, "urban_pothole", defensiveDrivingRank);
                rackSymptom = ImpactMisalignmentIntegrator.ApplyImpactToRack(Rack, impactEvent, impactSeed);
                reasonKey = rackSymptom.InspectionRequired
                    ? "hazard.pothole_rack_misaligned"
                    : "hazard.pothole_absorbed";
                break;

            case UrbanHazardKind.ErraticVehicleCollision:
                var collisionDv = deltaVMetresPerSecond > 0.0 ? deltaVMetresPerSecond : 4.0;
                impactEvent = Vehicle.ApplyCollisionImpact(collisionDv, "erratic_vehicle_collision", defensiveDrivingRank);
                rackSymptom = ImpactMisalignmentIntegrator.ApplyImpactToRack(Rack, impactEvent, impactSeed);
                Gimbalina.ApplyStartle(StartleEventKind.Horn);
                reasonKey = rackSymptom.InspectionRequired
                    ? "hazard.erratic_collision_rack_misaligned"
                    : "hazard.erratic_collision_minor";
                break;

            case UrbanHazardKind.DogChase:
                Gimbalina.ApplyStartle(StartleEventKind.Dog);
                var tracker = CalmPolicy.RegisterDogEncounter(hazardId, initialDogDistanceMetres);
                dogState = tracker.ExportState();
                reasonKey = "hazard.dog_chase_started";
                break;

            case UrbanHazardKind.FireworksOrBomb:
                Gimbalina.ApplyStartle(StartleEventKind.Fireworks);
                if (deltaVMetresPerSecond > 0.0)
                {
                    impactEvent = Vehicle.ApplyCollisionImpact(deltaVMetresPerSecond, "fireworks_or_blast", defensiveDrivingRank);
                    rackSymptom = ImpactMisalignmentIntegrator.ApplyImpactToRack(Rack, impactEvent, impactSeed);
                }
                reasonKey = rackSymptom?.InspectionRequired == true
                    ? "hazard.fireworks_and_blast_misaligned"
                    : "hazard.fireworks_startle";
                break;

            case UrbanHazardKind.SabotageVandalism:
                Lens.ApplySplatter(splatterObstruction > 0.0 ? splatterObstruction : DefaultSabotageSplatterObstruction);
                reasonKey = "hazard.sabotage_lens_splatter";
                break;

            case UrbanHazardKind.RoadMudSplatter:
                Lens.ApplySplatter(splatterObstruction > 0.0 ? splatterObstruction : DefaultMudSplatterObstruction);
                reasonKey = "hazard.road_mud_splatter";
                break;

            case UrbanHazardKind.FlanelinhaApproach:
                if (payFlanelinhaTip)
                {
                    tipPaid = FlanelinhaTipCoords;
                    TotalTipsPaidCoords += tipPaid;
                    reasonKey = "hazard.flanelinha_paid";
                }
                else
                {
                    cosmeticAdded = FlanelinhaRefusalCosmeticDamage;
                    TotalCosmeticDamagePoints += cosmeticAdded;
                    reasonKey = "hazard.flanelinha_refused_cosmetic_damage";
                }
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }

        var outcome = new UrbanHazardOutcome(
            HazardId: hazardId,
            Kind: kind,
            SuppressedByCalmPolicy: false,
            TipPaidCoords: tipPaid,
            CosmeticDamageAdded: cosmeticAdded,
            GimbalinaStressAfter: Gimbalina.Stress,
            LensObstructionAfter: Lens.ObstructionFraction,
            RackInspectionRequired: rackSymptom?.InspectionRequired ?? Rack.MountedSlots.Values.Any(s => s.NeedsWorkshopInspection),
            ImpactEvent: impactEvent,
            RackSymptom: rackSymptom,
            DogState: dogState,
            ReasonKey: reasonKey);
        _history.Add(outcome);
        return outcome;
    }

    public bool FeedGimbalinaCorn() => Gimbalina.TryFeedCorn();

    public DogPursuitState PacifyDogWithTreat(string dogId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dogId);
        var dog = CalmPolicy.DogPursuits.FirstOrDefault(d => string.Equals(d.DogId, dogId, StringComparison.Ordinal))
            ?? throw new KeyNotFoundException($"Dog pursuit '{dogId}' not found.");
        return dog.ApplyTreat();
    }

    public bool StepStoppedLensCleaning(double dtSeconds, double platformSpeedMetresPerSecond) =>
        Lens.StepStoppedCleaning(dtSeconds, platformSpeedMetresPerSecond);

    public GimbalinaStepResult StepRest(double dtSeconds, bool isResting = true) =>
        Gimbalina.Step(dtSeconds, isResting: isResting);
}

