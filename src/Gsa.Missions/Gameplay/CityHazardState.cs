using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Camera;
using Gsa.Missions.Economy;

namespace Gsa.Missions.Gameplay;

public sealed record CityHazardSnapshot(int SchemaVersion, double PhysicalTimeSeconds, double LensObstruction,
    bool TirePunctured, LocomotionMode? DisabledVehicle, double RackYawErrorRadians,
    string? ActiveZoneId, double SpoofElapsed, double SpoofRemaining, double PursuitRemaining,
    double RoadblockRemaining, Vector3d? RoadblockPosition, long NextEventSequence,
    bool LegacyDisguise, SabotageCoordinatorSnapshot Sabotage, ServiceInsuranceSnapshot Services,
    IReadOnlyDictionary<LocomotionMode, double>? VehicleDamage = null);

/// <summary>Physical city damage and persistent countermeasures. No quality scores or truth-based GNSS corrections.</summary>
public sealed class CityHazardState
{
    public LensContaminationTracker Lens { get; private set; } = new();
    public FactionSabotageAndCountermeasuresCoordinator Sabotage { get; private set; } = new();
    public FleetServiceAndInsuranceCoordinator Services { get; private set; } = new();
    public double PhysicalTimeSeconds { get; private set; }
    public bool TirePunctured { get; set; }
    private readonly Dictionary<LocomotionMode, double> _vehicleDamage = [];
    public LocomotionMode? DisabledVehicle
    {
        get => _vehicleDamage.Where(p => p.Value >= CarSuspensionSimulator.DestroyedThreshold)
            .OrderBy(p => p.Key).Select(p => (LocomotionMode?)p.Key).FirstOrDefault();
        set
        {
            if (value is { } mode) SetVehicleDamage(mode, CarSuspensionSimulator.DestroyedThreshold);
            else if (DisabledVehicle is { } old) SetVehicleDamage(old, 0);
        }
    }
    public double GetVehicleDamage(LocomotionMode mode) => _vehicleDamage.GetValueOrDefault(mode);
    public bool IsVehicleDisabled(LocomotionMode mode) => GetVehicleDamage(mode) >= CarSuspensionSimulator.DestroyedThreshold;
    public void SetVehicleDamage(LocomotionMode mode, double damage)
    {
        if (!Enum.IsDefined(mode) || mode < LocomotionMode.Car || !double.IsFinite(damage) || damage < 0 || damage > 100)
            throw new ArgumentException("Invalid vehicle damage.");
        if (damage == 0) _vehicleDamage.Remove(mode); else _vehicleDamage[mode] = damage;
    }
    public double RackYawErrorRadians { get; set; }
    public string? ActiveZoneId { get; private set; }
    public double SpoofElapsed { get; private set; }
    public double SpoofRemaining { get; private set; }
    public double PursuitRemaining { get; set; }
    public double RoadblockRemaining { get; set; }
    public Vector3d? RoadblockPosition { get; set; }
    public bool LegacyDisguise { get; set; }
    public long NextEventSequence { get; private set; }

    public string NextEventId() => "city_hazard_" + NextEventSequence++;
    public void StartSpoofing() { SpoofElapsed = 0; SpoofRemaining = 20; }
    public void CancelActiveThreats()
    {
        SpoofElapsed = SpoofRemaining = PursuitRemaining = RoadblockRemaining = 0;
        RoadblockPosition = null;
    }
    public void Step(double dt, string? zoneId, bool protectedNow)
    {
        if (!double.IsFinite(dt) || dt <= 0) throw new ArgumentOutOfRangeException(nameof(dt));
        PhysicalTimeSeconds += dt;
        if (zoneId != ActiveZoneId || protectedNow) CancelActiveThreats();
        ActiveZoneId = zoneId;
        if (SpoofRemaining > 0) { SpoofElapsed += dt; SpoofRemaining = Math.Max(0, SpoofRemaining - dt); }
        PursuitRemaining = Math.Max(0, PursuitRemaining - dt);
        RoadblockRemaining = Math.Max(0, RoadblockRemaining - dt);
        if (RoadblockRemaining <= 0) RoadblockPosition = null;
    }
    public void BeginContract()
    {
        CancelActiveThreats(); LegacyDisguise = false;
        // Purchases last for this job; displaced monuments and resolved attack IDs are world history.
        Sabotage = FactionSabotageAndCountermeasuresCoordinator.ImportSnapshot(Sabotage.ExportSnapshot() with { ZoneStates = [] });
    }
    public CityHazardSnapshot Export() => new(1, PhysicalTimeSeconds, Lens.ObstructionFraction, TirePunctured,
        DisabledVehicle, RackYawErrorRadians, ActiveZoneId, SpoofElapsed, SpoofRemaining, PursuitRemaining,
        RoadblockRemaining, RoadblockPosition, NextEventSequence, LegacyDisguise, Sabotage.ExportSnapshot(), Services.ExportSnapshot(),
        _vehicleDamage.OrderBy(p => p.Key).ToDictionary(p => p.Key, p => p.Value));

    public static CityHazardState Restore(CityHazardSnapshot? snapshot, bool legacyDisguise = false)
    {
        if (snapshot is null) return new() { LegacyDisguise = legacyDisguise };
        static bool Timer(double t) => double.IsFinite(t) && t >= 0;
        if (snapshot.SchemaVersion != 1 || !Timer(snapshot.PhysicalTimeSeconds) || !Timer(snapshot.SpoofElapsed) ||
            !Timer(snapshot.SpoofRemaining) || snapshot.SpoofRemaining > 20 || !Timer(snapshot.PursuitRemaining) ||
            !Timer(snapshot.RoadblockRemaining) || snapshot.PursuitRemaining > 20 || snapshot.RoadblockRemaining > 30 || snapshot.NextEventSequence < 0 ||
            !double.IsFinite(snapshot.RackYawErrorRadians) || Math.Abs(snapshot.RackYawErrorRadians) > .1 ||
            snapshot.Sabotage is null || snapshot.Services is null ||
            snapshot.Sabotage.SchemaVersion != 1 || snapshot.Sabotage.ResolvedAttackIds is null ||
            snapshot.Sabotage.ZoneStates is null || snapshot.Sabotage.MovedBenchmarks is null ||
            snapshot.Services.SchemaVersion != 1 || snapshot.Services.Claims is null ||
            snapshot.Services.InsuredSensorIds is null || snapshot.Services.PurchasedServiceReceiptIds is null ||
            (snapshot.DisabledVehicle is { } mode && (!Enum.IsDefined(mode) || mode < LocomotionMode.Car)))
            throw new ArgumentException("Invalid city hazard snapshot.");
        var state = new CityHazardState
        {
            PhysicalTimeSeconds = snapshot.PhysicalTimeSeconds, Lens = new(snapshot.LensObstruction),
            TirePunctured = snapshot.TirePunctured, DisabledVehicle = snapshot.DisabledVehicle,
            RackYawErrorRadians = snapshot.RackYawErrorRadians, ActiveZoneId = snapshot.ActiveZoneId,
            SpoofElapsed = snapshot.SpoofElapsed, SpoofRemaining = snapshot.SpoofRemaining,
            PursuitRemaining = snapshot.PursuitRemaining, RoadblockRemaining = snapshot.RoadblockRemaining,
            RoadblockPosition = snapshot.RoadblockPosition, NextEventSequence = snapshot.NextEventSequence,
            LegacyDisguise = snapshot.LegacyDisguise,
            Sabotage = FactionSabotageAndCountermeasuresCoordinator.ImportSnapshot(snapshot.Sabotage),
            Services = FleetServiceAndInsuranceCoordinator.FromSnapshot(snapshot.Services),
        };
        foreach (var (vehicle, damage) in snapshot.VehicleDamage ?? new Dictionary<LocomotionMode, double>()) state.SetVehicleDamage(vehicle, damage);
        return state;
    }
}
