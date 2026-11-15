using Gsa.Core.Geometry;

namespace Gsa.Core.Mobility;

public enum AccessCorridorKind
{
    SidewalkPromenade = 0,
    NarrowAlleyOrCycleway = 1,
    UrbanRoadLane = 2,
}

public sealed record CorridorAccessSpec(
    string CorridorId,
    AccessCorridorKind Kind,
    double ClearWidthMetres);

public sealed record CorridorAccessEvaluation(
    bool Allowed,
    LocomotionMode Mode,
    double RequiredWidthMetres,
    double ClearWidthMetres,
    string? ReasonKey);

public sealed record PlatformResourceGaugeSnapshot(
    LocomotionMode Mode,
    double RemainingAutonomySeconds,
    double MaxAutonomySeconds,
    double AutonomyFraction,
    int UsedStorageUnits,
    int MaxStorageUnits,
    double StorageFraction,
    double VehicleDamagePoints,
    bool IsDestroyed,
    bool CanContinueCapture,
    CaptureStopReason StopReason);

public sealed record VehicleImpactEvent(
    double TimestampSeconds,
    LocomotionMode Mode,
    double ImpactDeltaVMetresPerSecond,
    double DamageAddedPoints,
    double TotalDamagePoints,
    bool TriggeredRackShockEvent,
    bool DestroyedVehicle);

/// <summary>Extends MVP locomotion with bicycle/scooter corridor rules, resource gauges, damage, and safe recovery.</summary>
public sealed class MvpVehicleMobilityController
{
    public const double RackShockDeltaVThresholdMetresPerSecond = 3.0;
    public const double DestroyedDamageThresholdPoints = 100.0;
    public const double DefaultDamagePerDeltaV = 15.0;

    private readonly List<VehicleImpactEvent> _impactLog = [];

    public LocomotionSimulator Simulator { get; }
    public double VehicleDamagePoints { get; private set; }
    public bool IsDestroyed { get; private set; }
    public IReadOnlyList<VehicleImpactEvent> ImpactLog => _impactLog;

    public MvpVehicleMobilityController(LocomotionSimulator simulator)
    {
        ArgumentNullException.ThrowIfNull(simulator);
        Simulator = simulator;
    }

    public CorridorAccessEvaluation EvaluateCorridorAccess(CorridorAccessSpec corridor)
    {
        ArgumentNullException.ThrowIfNull(corridor);
        ArgumentException.ThrowIfNullOrWhiteSpace(corridor.CorridorId);
        if (!double.IsFinite(corridor.ClearWidthMetres) || corridor.ClearWidthMetres <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(corridor), "Corridor width must be finite and positive.");

        var profile = Simulator.CurrentProfile;
        var mode = Simulator.Mode;
        var requiredWidth = profile.CollisionRadiusMetres * 2.0 + 0.10;

        switch (corridor.Kind)
        {
            case AccessCorridorKind.SidewalkPromenade:
                if (mode == LocomotionMode.Scooter)
                    return new CorridorAccessEvaluation(false, mode, requiredWidth, corridor.ClearWidthMetres, "mobility.access.scooter_forbidden_on_sidewalk");
                break;

            case AccessCorridorKind.NarrowAlleyOrCycleway:
                if (mode == LocomotionMode.Scooter)
                    return new CorridorAccessEvaluation(false, mode, requiredWidth, corridor.ClearWidthMetres, "mobility.access.cycleway_forbidden_for_scooter");
                break;

            case AccessCorridorKind.UrbanRoadLane:
                if (mode == LocomotionMode.Handcart)
                    return new CorridorAccessEvaluation(false, mode, requiredWidth, corridor.ClearWidthMetres, "mobility.access.handcart_forbidden_on_road_lane");
                break;
        }

        if (corridor.ClearWidthMetres < requiredWidth)
            return new CorridorAccessEvaluation(false, mode, requiredWidth, corridor.ClearWidthMetres, "mobility.access.too_narrow");

        return new CorridorAccessEvaluation(true, mode, requiredWidth, corridor.ClearWidthMetres, null);
    }

    public VehicleImpactEvent RecordImpact(
        double timestampSeconds,
        double impactDeltaVMetresPerSecond,
        double? explicitDamagePoints = null)
    {
        if (!double.IsFinite(timestampSeconds) || timestampSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(timestampSeconds));
        if (!double.IsFinite(impactDeltaVMetresPerSecond) || impactDeltaVMetresPerSecond < 0.0)
            throw new ArgumentOutOfRangeException(nameof(impactDeltaVMetresPerSecond));

        var addedDamage = explicitDamagePoints ?? (impactDeltaVMetresPerSecond * DefaultDamagePerDeltaV);
        if (!double.IsFinite(addedDamage) || addedDamage < 0.0)
            throw new ArgumentOutOfRangeException(nameof(explicitDamagePoints));

        VehicleDamagePoints = Math.Min(DestroyedDamageThresholdPoints, VehicleDamagePoints + addedDamage);
        if (VehicleDamagePoints >= DestroyedDamageThresholdPoints)
            IsDestroyed = true;

        var ev = new VehicleImpactEvent(
            timestampSeconds,
            Simulator.Mode,
            impactDeltaVMetresPerSecond,
            addedDamage,
            VehicleDamagePoints,
            TriggeredRackShockEvent: impactDeltaVMetresPerSecond >= RackShockDeltaVThresholdMetresPerSecond,
            DestroyedVehicle: IsDestroyed);
        _impactLog.Add(ev);
        return ev;
    }

    public PlatformResourceGaugeSnapshot GetResourceGaugeSnapshot()
    {
        var profile = Simulator.CurrentProfile;
        var vehicleProfile = PlatformProfile.ForMode(Simulator.MountedVehicleMode);
        var maxAutonomy = profile.HasFiniteAutonomy
            ? profile.PhysicalAutonomySeconds
            : Math.Max(1.0, vehicleProfile.PhysicalAutonomySeconds);
        var remainingAutonomy = profile.HasFiniteAutonomy ? Simulator.RemainingAutonomySeconds : maxAutonomy;
        var autonomyFraction = Math.Clamp(remainingAutonomy / maxAutonomy, 0.0, 1.0);
        var storageFraction = Math.Clamp((double)Simulator.UsedVirtualStorageUnits / profile.VirtualStorageCapacityUnits, 0.0, 1.0);
        var stopReason = Simulator.EvaluateCaptureReadiness();
        var canContinue = !IsDestroyed && stopReason == CaptureStopReason.None;

        return new PlatformResourceGaugeSnapshot(
            Mode: Simulator.Mode,
            RemainingAutonomySeconds: Simulator.RemainingAutonomySeconds,
            MaxAutonomySeconds: vehicleProfile.PhysicalAutonomySeconds,
            AutonomyFraction: autonomyFraction,
            UsedStorageUnits: Simulator.UsedVirtualStorageUnits,
            MaxStorageUnits: profile.VirtualStorageCapacityUnits,
            StorageFraction: storageFraction,
            VehicleDamagePoints: VehicleDamagePoints,
            IsDestroyed: IsDestroyed,
            CanContinueCapture: canContinue,
            StopReason: stopReason);
    }

    public void ResetAfterSafeRecovery(Vector3d safeOfficePositionEnu, LocomotionMode? mode = null)
    {
        VehicleDamagePoints = 0.0;
        IsDestroyed = false;
        Simulator.RepositionSafeAfterRecovery(safeOfficePositionEnu, mode);
    }
}
