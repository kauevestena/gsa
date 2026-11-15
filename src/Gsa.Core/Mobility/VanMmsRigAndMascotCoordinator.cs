using Gsa.Core.Geometry;
using Gsa.Core.Sensors.Calibration;
using Gsa.Core.Sensors.Fusion;
using Gsa.Core.Sensors.Lidar;

namespace Gsa.Core.Mobility;

public sealed record GimbalinaDashboardMascotState(
    bool IsRetiredDashboardMascot,
    bool Injured,
    double Stress,
    bool FeedsStabilizationErrorToSensors,
    bool DashboardBobbleVisualOnly,
    string RetirementNoteKey);

public sealed record VanRigConfigurationResult(
    bool Succeeded,
    string ReasonKey,
    PlatformProfile Platform,
    ModularSensorRack? Rack,
    LidarProfile EquippedLidarProfile,
    IReadOnlyList<string> LidarSlotIds,
    IReadOnlyList<string> PavementCameraSlotIds,
    bool HasPanoramic360,
    bool HasDualAntennaHeading,
    bool HasWheelDmi,
    ImuProfile EquippedImu,
    GimbalinaDashboardMascotState MascotState,
    double TotalPowerWatts,
    int TotalWeightGrams,
    int VirtualStorageCapacityUnits,
    double PhysicalAutonomySeconds);

public sealed record VanRouteReplayResult(
    bool RouteCompleted,
    CaptureStopReason StopReason,
    string? StopReasonKey,
    double DistanceMetres,
    double ElapsedSeconds,
    double PeakSpeedMetresPerSecond,
    int ConsumedStorageUnits,
    double RemainingAutonomySeconds,
    bool RemappedCaptureControlWorked,
    GimbalinaDashboardMascotState MascotState);

/// <summary>Configures Level-6 engineering MMS Van with dual L6 LiDARs, downward pavement cameras, full suite, and retired mascot Gimbalina (T-F03-08).</summary>
public static class VanMmsRigCoordinator
{
    public const double VanRackPowerBudgetWatts = 1000.0;
    public const int VanRackWeightBudgetGrams = 60000;

    public static GimbalinaDashboardMascotState CreateRetiredMascotState() =>
        new(
            IsRetiredDashboardMascot: true,
            Injured: false,
            Stress: 0.0,
            FeedsStabilizationErrorToSensors: false,
            DashboardBobbleVisualOnly: true,
            RetirementNoteKey: "mascot.gimbalina.van_retired_happy");

    public static VanRigConfigurationResult ConfigureEngineeringVanRig(
        int playerProgressionLevel,
        ImuGrade imuGrade = ImuGrade.Navigation,
        double effectiveTimeSeconds = 0.0)
    {
        var platform = PlatformProfile.Van;
        var lidar = LidarProfile.L6;
        var imu = ImuProfile.ForGrade(imuGrade);
        var mascot = CreateRetiredMascotState();

        if (playerProgressionLevel < platform.Level)
        {
            return new VanRigConfigurationResult(
                false, "van.error.platform_requires_level_6", platform, null, lidar,
                [], [], false, false, false, imu, mascot, 0.0, 0,
                platform.VirtualStorageCapacityUnits, platform.PhysicalAutonomySeconds);
        }

        var rack = new ModularSensorRack(
            platform,
            RackCatalog.CreateDefaultSlotsForPlatform(platform),
            VanRackPowerBudgetWatts,
            VanRackWeightBudgetGrams);

        var mounts = new (string SensorId, RackSlotKind Kind, string SlotId, double Watts, int Grams)[]
        {
            ("gnss_van_primary", RackSlotKind.GnssAntennaPrimary, "slot_gnss_primary", 8.0, 450),
            ("gnss_van_secondary", RackSlotKind.GnssAntennaSecondary, "slot_gnss_secondary", 8.0, 450),
            ($"imu_{imu.Id}", RackSlotKind.ImuUnit, "slot_imu_core", 10.0, 550),
            ("wheel_dmi_van_20hz", RackSlotKind.WheelDmi, "slot_wheel_dmi", 6.0, 380),
            ("workstation_van_eng", RackSlotKind.OnboardComputer, "slot_computer", 120.0, 4500),
            ("ssd_raid_16000u", RackSlotKind.StorageBay, "slot_storage", 25.0, 1500),
            ("power_pack_van", RackSlotKind.PowerPack, "slot_power", 80.0, 12000),
            ("cam_360_1024_van", RackSlotKind.Panoramic360, "slot_cam_360", 40.0, 2100),
            ("cam_pavement_down_left", RackSlotKind.PavementCamera, "slot_pavement_cam_a", 30.0, 1400),
            ("cam_pavement_down_right", RackSlotKind.PavementCamera, "slot_pavement_cam_b", 30.0, 1400),
            ("lidar_l6_unit_a", RackSlotKind.LidarProfilerOrScanner, "slot_lidar_a", lidar.PowerWatts, 4300),
            ("lidar_l6_unit_b", RackSlotKind.LidarProfilerOrScanner, "slot_lidar_b", lidar.PowerWatts, 4300),
        };

        foreach (var (sensorId, kind, slotId, watts, grams) in mounts)
        {
            var attempt = rack.MountOrMoveSensor(sensorId, kind, slotId, effectiveTimeSeconds, watts, grams);
            if (!attempt.Succeeded)
            {
                return new VanRigConfigurationResult(
                    false, attempt.ReasonKey, platform, rack, lidar,
                    [], [], false, false, false, imu, mascot,
                    rack.CurrentPowerLoadWatts, rack.CurrentWeightLoadGrams,
                    platform.VirtualStorageCapacityUnits, platform.PhysicalAutonomySeconds);
            }
        }

        return new VanRigConfigurationResult(
            Succeeded: true,
            ReasonKey: "van.rig.configured",
            Platform: platform,
            Rack: rack,
            EquippedLidarProfile: lidar,
            LidarSlotIds: Array.AsReadOnly(["slot_lidar_a", "slot_lidar_b"]),
            PavementCameraSlotIds: Array.AsReadOnly(["slot_pavement_cam_a", "slot_pavement_cam_b"]),
            HasPanoramic360: true,
            HasDualAntennaHeading: true,
            HasWheelDmi: true,
            EquippedImu: imu,
            MascotState: mascot,
            TotalPowerWatts: rack.CurrentPowerLoadWatts,
            TotalWeightGrams: rack.CurrentWeightLoadGrams,
            VirtualStorageCapacityUnits: platform.VirtualStorageCapacityUnits,
            PhysicalAutonomySeconds: platform.PhysicalAutonomySeconds);
    }

    public static bool VerifyFleetPreservesPriorPlatforms(IReadOnlySet<LocomotionMode> ownedPlatforms)
    {
        ArgumentNullException.ThrowIfNull(ownedPlatforms);
        return ownedPlatforms.Contains(LocomotionMode.Handcart) &&
               ownedPlatforms.Contains(LocomotionMode.Bicycle) &&
               ownedPlatforms.Contains(LocomotionMode.Scooter) &&
               ownedPlatforms.Contains(LocomotionMode.Car) &&
               ownedPlatforms.Contains(LocomotionMode.Kombi) &&
               ownedPlatforms.Contains(LocomotionMode.Pickup) &&
               ownedPlatforms.Contains(LocomotionMode.Van);
    }

    public static VanRouteReplayResult RunInstrumentedRouteReplay(
        double routeDistanceMetres,
        double targetSpeedMetresPerSecond,
        int storageUnitsPerSecond,
        int initialUsedStorageUnits = 0,
        double initialUsedAutonomySeconds = 0.0,
        InputActionMap? inputMap = null)
    {
        if (!double.IsFinite(routeDistanceMetres) || routeDistanceMetres <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(routeDistanceMetres));
        if (!double.IsFinite(targetSpeedMetresPerSecond) || targetSpeedMetresPerSecond <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(targetSpeedMetresPerSecond));

        var profile = PlatformProfile.Van;
        var map = (inputMap ?? InputActionMap.CreateDefault()).RemapKeyboard(GameInputAction.Capture, "F8");
        var captureRemappedOk = string.Equals(map.GetBinding(GameInputAction.Capture).Keyboard, "F8", StringComparison.Ordinal);

        var clampedSpeed = Math.Min(targetSpeedMetresPerSecond, profile.MaxSpeedMetresPerSecond);
        var dt = 0.5;
        var remainingDist = routeDistanceMetres;
        var coveredDist = 0.0;
        var elapsed = 0.0;
        var remainingAutonomy = Math.Max(0.0, profile.PhysicalAutonomySeconds - initialUsedAutonomySeconds);
        var usedStorage = initialUsedStorageUnits;
        var mascot = CreateRetiredMascotState();

        while (remainingDist > 1e-6)
        {
            if (remainingAutonomy <= 0.0)
            {
                return new VanRouteReplayResult(
                    false, CaptureStopReason.AutonomyExhausted, "van.capture_stopped.autonomy_exhausted",
                    Math.Round(coveredDist, 3), Math.Round(elapsed, 3), clampedSpeed,
                    usedStorage, 0.0, captureRemappedOk, mascot);
            }

            if (usedStorage >= profile.VirtualStorageCapacityUnits)
            {
                return new VanRouteReplayResult(
                    false, CaptureStopReason.VirtualStorageFull, "van.capture_stopped.virtual_storage_full",
                    Math.Round(coveredDist, 3), Math.Round(elapsed, 3), clampedSpeed,
                    usedStorage, Math.Round(remainingAutonomy, 3), captureRemappedOk, mascot);
            }

            var stepTime = Math.Min(dt, remainingDist / clampedSpeed);
            stepTime = Math.Min(stepTime, remainingAutonomy);
            var stepStorage = (int)Math.Ceiling(storageUnitsPerSecond * stepTime);
            if (usedStorage + stepStorage > profile.VirtualStorageCapacityUnits)
            {
                usedStorage = profile.VirtualStorageCapacityUnits;
                return new VanRouteReplayResult(
                    false, CaptureStopReason.VirtualStorageFull, "van.capture_stopped.virtual_storage_full",
                    Math.Round(coveredDist, 3), Math.Round(elapsed, 3), clampedSpeed,
                    usedStorage, Math.Round(remainingAutonomy, 3), captureRemappedOk, mascot);
            }

            var stepDist = clampedSpeed * stepTime;
            coveredDist += stepDist;
            remainingDist = Math.Max(0.0, remainingDist - stepDist);
            elapsed += stepTime;
            remainingAutonomy = Math.Max(0.0, remainingAutonomy - stepTime);
            usedStorage += stepStorage;
        }

        return new VanRouteReplayResult(
            true, CaptureStopReason.None, null,
            Math.Round(coveredDist, 3), Math.Round(elapsed, 3), clampedSpeed,
            usedStorage, Math.Round(remainingAutonomy, 3), captureRemappedOk, mascot);
    }
}

