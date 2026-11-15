using Gsa.Core.Geometry;
using Gsa.Core.Sensors.Calibration;
using Gsa.Core.Sensors.Fusion;
using Gsa.Core.Sensors.Lidar;
using Gsa.Core.World;

namespace Gsa.Core.Mobility;

public sealed record PickupRigConfigurationResult(
    bool Succeeded,
    string ReasonKey,
    PlatformProfile Platform,
    ModularSensorRack? Rack,
    LidarProfile EquippedLidarProfile,
    IReadOnlyList<string> LidarSlotIds,
    ImuProfile EquippedImu,
    bool HasWheelDmi,
    bool HasDualAntennaHeading,
    double TotalPowerWatts,
    int TotalWeightGrams,
    int VirtualStorageCapacityUnits,
    double PhysicalAutonomySeconds);

public sealed record PickupSlopeStepState(
    Vector3d PositionEnu,
    double SpeedMetresPerSecond,
    double WheelSlipRatio,
    double PitchRadians,
    bool StalledOnSlope,
    bool BlockedByObstacle,
    RackImpactEvent? ImpactEvent,
    CaptureStopReason StopReason);

public sealed record PickupSlopeReplayResult(
    bool RouteCompleted,
    bool IsWet,
    bool FourWheelDriveEngaged,
    double ElapsedSeconds,
    double DistanceCoveredMetres,
    double AverageSpeedMetresPerSecond,
    double PeakWheelSlipRatio,
    double MaxPitchRadians,
    int ObstaclePenetrationCount,
    int ObstacleAvoidanceManeuvers,
    double RemainingAutonomySeconds,
    int ImpactEventCount,
    string ReasonKey);

/// <summary>Configures Level-5 4x4 Pickup dual-LiDAR rig and simulates dry/wet slope traction, 4x4 grip, and solid obstacle collision protection (T-F03-07).</summary>
public static class PickupFourByFourRigCoordinator
{
    public const double PickupRackPowerBudgetWatts = 800.0;
    public const int PickupRackWeightBudgetGrams = 45000;

    public static PickupRigConfigurationResult ConfigureDualLidarRig(
        int playerProgressionLevel,
        ImuGrade imuGrade = ImuGrade.Navigation,
        double effectiveTimeSeconds = 0.0)
    {
        var platform = PlatformProfile.Pickup;
        var lidar = LidarProfile.L5;
        var imu = ImuProfile.ForGrade(imuGrade);

        if (playerProgressionLevel < platform.Level)
        {
            return new PickupRigConfigurationResult(
                false, "pickup.error.platform_requires_level_5", platform, null, lidar, [],
                imu, false, false, 0.0, 0, platform.VirtualStorageCapacityUnits, platform.PhysicalAutonomySeconds);
        }

        var rack = new ModularSensorRack(
            platform,
            RackCatalog.CreateDefaultSlotsForPlatform(platform),
            PickupRackPowerBudgetWatts,
            PickupRackWeightBudgetGrams);

        var mounts = new (string SensorId, RackSlotKind Kind, string SlotId, double Watts, int Grams)[]
        {
            ("gnss_rover_primary", RackSlotKind.GnssAntennaPrimary, "slot_gnss_primary", 8.0, 450),
            ("gnss_rover_secondary", RackSlotKind.GnssAntennaSecondary, "slot_gnss_secondary", 8.0, 450),
            ($"imu_{imu.Id}", RackSlotKind.ImuUnit, "slot_imu_core", 10.0, 550),
            ("wheel_dmi_20hz", RackSlotKind.WheelDmi, "slot_wheel_dmi", 6.0, 380),
            ("onboard_pc_pickup", RackSlotKind.OnboardComputer, "slot_computer", 110.0, 4200),
            ("ssd_array_8000u", RackSlotKind.StorageBay, "slot_storage", 22.0, 1500),
            ("power_dist_pickup", RackSlotKind.PowerPack, "slot_power", 40.0, 11000),
            ("cam_adv_1080p_roof", RackSlotKind.RoofCamera, "slot_roof_cam_a", 20.0, 950),
            ("lidar_l5_unit_a", RackSlotKind.LidarProfilerOrScanner, "slot_lidar_a", lidar.PowerWatts, 4100),
            ("lidar_l5_unit_b", RackSlotKind.LidarProfilerOrScanner, "slot_lidar_b", lidar.PowerWatts, 4100),
        };

        foreach (var (sensorId, kind, slotId, watts, grams) in mounts)
        {
            var attempt = rack.MountOrMoveSensor(sensorId, kind, slotId, effectiveTimeSeconds, watts, grams);
            if (!attempt.Succeeded)
            {
                return new PickupRigConfigurationResult(
                    false, attempt.ReasonKey, platform, rack, lidar, [], imu,
                    false, false, rack.CurrentPowerLoadWatts, rack.CurrentWeightLoadGrams,
                    platform.VirtualStorageCapacityUnits, platform.PhysicalAutonomySeconds);
            }
        }

        return new PickupRigConfigurationResult(
            true,
            "pickup.rig.ready",
            platform,
            rack,
            lidar,
            Array.AsReadOnly(["slot_lidar_a", "slot_lidar_b"]),
            imu,
            HasWheelDmi: true,
            HasDualAntennaHeading: true,
            TotalPowerWatts: rack.CurrentPowerLoadWatts,
            TotalWeightGrams: rack.CurrentWeightLoadGrams,
            VirtualStorageCapacityUnits: platform.VirtualStorageCapacityUnits,
            PhysicalAutonomySeconds: platform.PhysicalAutonomySeconds);
    }
}

public sealed class PickupFourByFourSlopeSimulator
{
    public const double GravityMetresPerSecondSquared = 9.81;
    public const double FourWheelDriveTractionMultiplier = 1.65;
    public const double TwoWheelDriveTractionMultiplier = 0.48;

    private readonly PlatformProfile _profile = PlatformProfile.Pickup;
    private Vector3d _positionEnu;
    private double _speedMetresPerSecond;
    private double _remainingAutonomySeconds;
    private int _usedStorageUnits;
    private int _impactCounter;
    private readonly List<RackImpactEvent> _impactLog = [];

    public Vector3d PositionEnu => _positionEnu;
    public double SpeedMetresPerSecond => _speedMetresPerSecond;
    public double RemainingAutonomySeconds => _remainingAutonomySeconds;
    public int UsedStorageUnits => _usedStorageUnits;
    public IReadOnlyList<RackImpactEvent> ImpactLog => _impactLog;

    public PickupFourByFourSlopeSimulator(Vector3d initialPositionEnu, int usedStorageUnits = 0)
    {
        if (usedStorageUnits < 0 || usedStorageUnits > _profile.VirtualStorageCapacityUnits)
            throw new ArgumentOutOfRangeException(nameof(usedStorageUnits));

        _positionEnu = initialPositionEnu;
        _remainingAutonomySeconds = _profile.PhysicalAutonomySeconds;
        _usedStorageUnits = usedStorageUnits;
    }

    public static bool IntersectsBox(Vector3d centerPoint, double radiusMetres, WorldBoxObject obstacle)
    {
        if (!obstacle.Collision)
            return false;

        var hx = obstacle.SizeEnu.X * 0.5 + radiusMetres;
        var hy = obstacle.SizeEnu.Y * 0.5 + radiusMetres;
        var minX = obstacle.PositionEnu.X - hx;
        var maxX = obstacle.PositionEnu.X + hx;
        var minY = obstacle.PositionEnu.Y - hy;
        var maxY = obstacle.PositionEnu.Y + hy;
        return centerPoint.X >= minX && centerPoint.X <= maxX && centerPoint.Y >= minY && centerPoint.Y <= maxY;
    }

    public PickupSlopeStepState StepAlongDirection(
        double dtSeconds,
        Vector3d unitDirectionEnu,
        double throttleInput,
        double slopeAngleRadians,
        double baseSurfaceGripMultiplier,
        bool fourWheelDriveEngaged,
        IReadOnlyList<WorldBoxObject> obstacles,
        int defensiveDrivingRank = 0)
    {
        if (!double.IsFinite(dtSeconds) || dtSeconds <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(dtSeconds));
        if (!double.IsFinite(baseSurfaceGripMultiplier) || baseSurfaceGripMultiplier <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(baseSurfaceGripMultiplier));
        ArgumentNullException.ThrowIfNull(obstacles);

        if (_remainingAutonomySeconds <= 0.0)
        {
            _speedMetresPerSecond = 0.0;
            return new PickupSlopeStepState(_positionEnu, 0.0, 0.0, slopeAngleRadians, false, false, null, CaptureStopReason.AutonomyExhausted);
        }

        throttleInput = Math.Clamp(throttleInput, 0.0, 1.0);
        _remainingAutonomySeconds = Math.Max(0.0, _remainingAutonomySeconds - dtSeconds * (throttleInput > 0.0 ? 1.0 : 0.25));

        var driveFactor = fourWheelDriveEngaged ? FourWheelDriveTractionMultiplier : TwoWheelDriveTractionMultiplier;
        var effectiveGrip = baseSurfaceGripMultiplier * driveFactor;
        var maxTractiveAccel = _profile.AccelerationMetresPerSecondSquared * effectiveGrip;
        var slopeResistanceAccel = GravityMetresPerSecondSquared * Math.Sin(slopeAngleRadians);

        var requestedAccel = throttleInput * _profile.AccelerationMetresPerSecondSquared * (0.4 + 0.6 * Math.Min(1.2, baseSurfaceGripMultiplier));
        var slipRatio = Math.Clamp(1.0 - Math.Min(1.0, effectiveGrip / Math.Max(0.25, 1.0 + Math.Abs(Math.Sin(slopeAngleRadians)) * 2.5)), 0.01, 0.95);

        var stalled = maxTractiveAccel < slopeResistanceAccel * 1.02;
        var netAccel = stalled
            ? -(slopeResistanceAccel - maxTractiveAccel)
            : Math.Min(requestedAccel, maxTractiveAccel) - slopeResistanceAccel * 0.45 - _speedMetresPerSecond * 0.14;

        var surfaceCapSpeed = _profile.MaxSpeedMetresPerSecond * Math.Clamp(baseSurfaceGripMultiplier * (fourWheelDriveEngaged ? 1.05 : 0.85), 0.35, 1.0);
        var nextSpeed = Math.Clamp(_speedMetresPerSecond + netAccel * dtSeconds, 0.0, surfaceCapSpeed);

        var candidatePos = _positionEnu + unitDirectionEnu.Normalized() * (nextSpeed * dtSeconds);
        foreach (var obs in obstacles)
        {
            if (IntersectsBox(candidatePos, _profile.CollisionRadiusMetres, obs))
            {
                var deltaV = Math.Abs(nextSpeed);
                _speedMetresPerSecond = 0.0;
                RackImpactEvent? ev = null;
                if (deltaV >= 0.5)
                {
                    var effThreshold = CarSuspensionSimulator.BaseRackImpactThresholdMetresPerSecond * (1.0 + 0.10 * Math.Clamp(defensiveDrivingRank, 0, 3));
                    _impactCounter++;
                    ev = new RackImpactEvent(
                        $"pickup_imp_{_impactCounter:0000}",
                        _profile.PhysicalAutonomySeconds - _remainingAutonomySeconds,
                        deltaV,
                        effThreshold,
                        deltaV >= effThreshold,
                        deltaV >= effThreshold ? Math.Round((deltaV - effThreshold + 1.0) * 8.0, 2) : Math.Round(deltaV, 2),
                        obs.Id);
                    _impactLog.Add(ev);
                }

                return new PickupSlopeStepState(_positionEnu, 0.0, slipRatio, slopeAngleRadians, stalled, true, ev, CaptureStopReason.None);
            }
        }

        _positionEnu = candidatePos;
        _speedMetresPerSecond = nextSpeed;
        var stopReason = _remainingAutonomySeconds <= 0.0
            ? CaptureStopReason.AutonomyExhausted
            : _usedStorageUnits >= _profile.VirtualStorageCapacityUnits
                ? CaptureStopReason.VirtualStorageFull
                : CaptureStopReason.None;

        return new PickupSlopeStepState(_positionEnu, _speedMetresPerSecond, slipRatio, slopeAngleRadians, stalled && _speedMetresPerSecond <= 0.05, false, null, stopReason);
    }

    public static PickupSlopeReplayResult RunSerraSlopeReplay(
        SerraPickupRouteSpec route,
        IReadOnlyDictionary<string, SerraTerrainCellSpec> terrainByCell,
        IReadOnlyList<WorldBoxObject> obstacles,
        bool isWet,
        bool fourWheelDriveEngaged = true,
        double timeStepSeconds = 0.1)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(terrainByCell);
        ArgumentNullException.ThrowIfNull(obstacles);
        if (route.WaypointsEnu.Count < 2)
            throw new ArgumentException("Route must define at least two waypoints.", nameof(route));

        var sim = new PickupFourByFourSlopeSimulator(route.WaypointsEnu[0]);
        var collisionBoxes = obstacles
            .Where(o => o.Collision && !string.Equals(o.ObjectClass, "terrain_slab", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var elapsed = 0.0;
        var distance = 0.0;
        var peakSlip = 0.0;
        var maxPitch = 0.0;
        var penetrationCount = 0;
        var avoidanceCount = 0;
        var completedWaypoints = true;
        var reasonKey = "pickup.replay.completed";

        for (var i = 0; i < route.WaypointsEnu.Count - 1; i++)
        {
            var target = route.WaypointsEnu[i + 1];
            var cellId = route.OrderedCellIds[Math.Min(i, route.OrderedCellIds.Count - 1)];
            var cellSpec = terrainByCell.TryGetValue(cellId, out var found)
                ? found
                : terrainByCell.Values.First();

            var grip = isWet ? cellSpec.WetGripMultiplier : cellSpec.DryGripMultiplier;
            var segRemaining = (target - sim.PositionEnu).Length;
            Vector3d? activeDodgeTarget = null;
            var dodgeTicksRemaining = 0;
            var dodgeAttempt = 0;
            var stallTicks = 0;

            while (segRemaining > 2.0 && elapsed < 300.0)
            {
                var steeringTarget = activeDodgeTarget ?? target;
                var dir = (steeringTarget - sim.PositionEnu).Normalized();
                var prevPos = sim.PositionEnu;
                var step = sim.StepAlongDirection(timeStepSeconds, dir, throttleInput: 1.0, cellSpec.SlopeAngleRadians, grip, fourWheelDriveEngaged, collisionBoxes);
                elapsed += timeStepSeconds;
                distance += (step.PositionEnu - prevPos).Length;
                peakSlip = Math.Max(peakSlip, step.WheelSlipRatio);
                maxPitch = Math.Max(maxPitch, Math.Abs(step.PitchRadians));

                if (collisionBoxes.Any(obs => IntersectsBox(step.PositionEnu, 0.1, obs)))
                    penetrationCount++;

                if (step.BlockedByObstacle)
                {
                    avoidanceCount++;
                    dodgeAttempt++;
                    if (avoidanceCount > 24)
                    {
                        completedWaypoints = false;
                        reasonKey = "pickup.replay.blocked_by_obstacle";
                        break;
                    }

                    var toTarget = (target - sim.PositionEnu).Normalized();
                    var planarPerp = new Vector3d(-toTarget.Y, toTarget.X, 0.0);
                    if (planarPerp.Length < 1e-6)
                        planarPerp = new Vector3d(0.0, 1.0, 0.0);
                    planarPerp = planarPerp.Normalized();

                    var sideSign = (dodgeAttempt % 2 == 1) ? 1.0 : -1.0;
                    var lateralDist = 14.0 * ((dodgeAttempt + 1) / 2);
                    activeDodgeTarget = sim.PositionEnu + planarPerp * (sideSign * lateralDist) + toTarget * 18.0;
                    dodgeTicksRemaining = 35;
                    continue;
                }

                if (activeDodgeTarget.HasValue)
                {
                    dodgeTicksRemaining--;
                    if (dodgeTicksRemaining <= 0 || (activeDodgeTarget.Value - step.PositionEnu).Length < 3.0)
                        activeDodgeTarget = null;
                }

                if (step.StalledOnSlope)
                {
                    stallTicks++;
                    if (stallTicks >= 20)
                    {
                        completedWaypoints = false;
                        reasonKey = "pickup.replay.stalled_insufficient_traction";
                        break;
                    }
                }
                else
                {
                    stallTicks = 0;
                }

                segRemaining = (target - step.PositionEnu).Length;
                if (step.StopReason != CaptureStopReason.None)
                {
                    completedWaypoints = false;
                    reasonKey = "pickup.replay.resource_exhausted";
                    break;
                }
            }

            if (!completedWaypoints)
                break;
        }

        var avgSpeed = elapsed > 1e-6 ? distance / elapsed : 0.0;
        return new PickupSlopeReplayResult(
            RouteCompleted: completedWaypoints,
            IsWet: isWet,
            FourWheelDriveEngaged: fourWheelDriveEngaged,
            ElapsedSeconds: Math.Round(elapsed, 3),
            DistanceCoveredMetres: Math.Round(distance, 3),
            AverageSpeedMetresPerSecond: Math.Round(avgSpeed, 3),
            PeakWheelSlipRatio: Math.Round(peakSlip, 4),
            MaxPitchRadians: Math.Round(maxPitch, 4),
            ObstaclePenetrationCount: penetrationCount,
            ObstacleAvoidanceManeuvers: avoidanceCount,
            RemainingAutonomySeconds: Math.Round(sim.RemainingAutonomySeconds, 3),
            ImpactEventCount: sim.ImpactLog.Count,
            ReasonKey: reasonKey);
    }
}
