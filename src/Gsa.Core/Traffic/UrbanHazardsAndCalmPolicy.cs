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

