using Gsa.Core.Geometry;
using Gsa.Core.Sensors.Calibration;
using Gsa.Core.Sensors.Fusion;
using Gsa.Core.Sensors.Lidar;

namespace Gsa.Core.Mobility;

public sealed record KombiRigConfigurationResult(
    bool Succeeded,
    string ReasonKey,
    PlatformProfile Platform,
    ModularSensorRack? Rack,
    LidarProfile? EquippedLidar,
    ImuProfile EquippedImu,
    bool HasWheelDmi,
    bool HasDualAntennaHeading,
    bool SupportsPpk,
    double TotalPowerWatts,
    int TotalWeightGrams,
    int VirtualStorageCapacityUnits,
    double PhysicalAutonomySeconds,
    IReadOnlyList<string> UnlockedCapabilities);

public sealed record KombiTunnelSegmentSpec(
    string SegmentId,
    double LengthMetres,
    double TargetSpeedMetresPerSecond,
    bool IsTunnelBlackout,
    int Passes = 1);

public sealed record KombiCorridorSimulationResult(
    bool Succeeded,
    string ReasonKey,
    double TotalDistanceMetres,
    double ElapsedAutonomySeconds,
    double RemainingAutonomySeconds,
    long TotalLidarReturns,
    int PeakStorageUnitsBetweenUnloads,
    int FinalPendingStorageUnits,
    int UnloadCount,
    double TotalBlackoutSeconds,
    bool AutonomyWithinLimit,
    bool StorageWithinLimit,
    IReadOnlyList<string> UnloadAfterSegmentIds);

/// <summary>Configures the Level-4 Kombi mobile mapping rig, rack budgets, virtual storage (1000 returns/unit), and tunnel corridor planning.</summary>
public static class KombiSurveyRigCoordinator
{
    public const int LidarReturnsPerVirtualStorageUnit = 1000;
    public const double DefaultRackPowerBudgetWatts = 650.0;
    public const int DefaultRackWeightBudgetGrams = 35000;

    public static IReadOnlyList<string> Level4UnlockedCapabilities { get; } =
        Array.AsReadOnly(["lidar_l4", "dmi", "imu_tatica", "ppk"]);

    public static int ComputeVirtualStorageUnits(long lidarReturns)
    {
        if (lidarReturns < 0)
            throw new ArgumentOutOfRangeException(nameof(lidarReturns), "LiDAR return count cannot be negative.");
        if (lidarReturns == 0)
            return 0;
        return checked((int)((lidarReturns + LidarReturnsPerVirtualStorageUnit - 1L) / LidarReturnsPerVirtualStorageUnit));
    }

    public static KombiRigConfigurationResult ConfigureRig(
        PlatformProfile platform,
        int playerProgressionLevel,
        LidarProfile? requestedLidar = null,
        ImuGrade requestedImuGrade = ImuGrade.Tactical,
        bool includeWheelDmi = true,
        bool includeDualAntennaHeading = false,
        bool includePanoramic360 = true,
        double effectiveTimeSeconds = 0.0)
    {
        ArgumentNullException.ThrowIfNull(platform);
        if (playerProgressionLevel < 0)
            throw new ArgumentOutOfRangeException(nameof(playerProgressionLevel));

        var lidar = requestedLidar ?? LidarProfile.L4;
        var imu = ImuProfile.ForGrade(requestedImuGrade);

        if (platform.Level < 4 || !platform.SupportsLidarSlots)
        {
            return new KombiRigConfigurationResult(
                false, "kombi.error.platform_requires_level_4", platform, null, null, imu,
                false, false, false, 0.0, 0, platform.VirtualStorageCapacityUnits,
                platform.PhysicalAutonomySeconds, Array.Empty<string>());
        }

        if (playerProgressionLevel < lidar.Grade || !lidar.CanEquipOn(platform))
        {
            return new KombiRigConfigurationResult(
                false, $"kombi.error.lidar_l{lidar.Grade}_requires_level_{lidar.Grade}",
                platform, null, null, imu, false, false, false, 0.0, 0,
                platform.VirtualStorageCapacityUnits, platform.PhysicalAutonomySeconds,
                Array.Empty<string>());
        }

        if (includeDualAntennaHeading && (platform.Level < 5 || playerProgressionLevel < 5))
        {
            return new KombiRigConfigurationResult(
                false, "kombi.error.dual_antenna_requires_level_5",
                platform, null, null, imu, false, false, false, 0.0, 0,
                platform.VirtualStorageCapacityUnits, platform.PhysicalAutonomySeconds,
                Array.Empty<string>());
        }

        var rack = new ModularSensorRack(
            platform,
            RackCatalog.CreateDefaultSlotsForPlatform(platform),
            DefaultRackPowerBudgetWatts,
            DefaultRackWeightBudgetGrams);

        var mounts = new List<(string SensorId, RackSlotKind Kind, string SlotId, double Watts, int Grams)>
        {
            ("gnss_rover_primary", RackSlotKind.GnssAntennaPrimary, "slot_gnss_primary", 8.0, 450),
            ($"imu_{imu.Id}", RackSlotKind.ImuUnit, "slot_imu_core", 10.0, 550),
            ("onboard_pc_kombi", RackSlotKind.OnboardComputer, "slot_computer", 105.0, 4000),
            ("ssd_array_4000u", RackSlotKind.StorageBay, "slot_storage", 20.0, 1400),
            ("power_dist_kombi", RackSlotKind.PowerPack, "slot_power", 35.0, 10500),
            (lidar.Id, RackSlotKind.LidarProfilerOrScanner, "slot_lidar_a", lidar.PowerWatts, 4200),
        };

        if (includeWheelDmi)
            mounts.Add(("wheel_dmi_20hz", RackSlotKind.WheelDmi, "slot_wheel_dmi", 6.0, 380));

        if (includePanoramic360)
            mounts.Add(("cam_360_roof", RackSlotKind.Panoramic360, "slot_cam_360", 38.0, 1900));

        if (includeDualAntennaHeading)
            mounts.Add(("gnss_rover_secondary", RackSlotKind.GnssAntennaSecondary, "slot_gnss_secondary", 8.0, 450));

        foreach (var (sensorId, kind, slotId, watts, grams) in mounts)
        {
            var attempt = rack.MountOrMoveSensor(sensorId, kind, slotId, effectiveTimeSeconds, watts, grams);
            if (!attempt.Succeeded)
            {
                return new KombiRigConfigurationResult(
                    false, attempt.ReasonKey, platform, rack, null, imu,
                    includeWheelDmi, includeDualAntennaHeading, false,
                    rack.CurrentPowerLoadWatts, rack.CurrentWeightLoadGrams,
                    platform.VirtualStorageCapacityUnits, platform.PhysicalAutonomySeconds,
                    Array.Empty<string>());
            }
        }

        var unlocked = Level4UnlockedCapabilities.ToList();
        if (playerProgressionLevel >= 5 && platform.Level >= 5 && !unlocked.Contains("dual_antenna_heading"))
            unlocked.Add("dual_antenna_heading");

        return new KombiRigConfigurationResult(
            Succeeded: true,
            ReasonKey: "kombi.rig.configured",
            Platform: platform,
            Rack: rack,
            EquippedLidar: lidar,
            EquippedImu: imu,
            HasWheelDmi: includeWheelDmi,
            HasDualAntennaHeading: includeDualAntennaHeading,
            SupportsPpk: true,
            TotalPowerWatts: rack.CurrentPowerLoadWatts,
            TotalWeightGrams: rack.CurrentWeightLoadGrams,
            VirtualStorageCapacityUnits: platform.VirtualStorageCapacityUnits,
            PhysicalAutonomySeconds: platform.PhysicalAutonomySeconds,
            UnlockedCapabilities: Array.AsReadOnly(unlocked.ToArray()));
    }

    public static KombiCorridorSimulationResult SimulateTunnelCorridor(
        PlatformProfile kombiPlatform,
        LidarProfile lidar,
        IReadOnlyList<KombiTunnelSegmentSpec> segments,
        double lidarReturnsPerSecond,
        IReadOnlySet<int>? unloadAfterSegmentIndices = null,
        double unloadStopDurationSeconds = 45.0)
    {
        ArgumentNullException.ThrowIfNull(kombiPlatform);
        ArgumentNullException.ThrowIfNull(lidar);
        ArgumentNullException.ThrowIfNull(segments);
        if (segments.Count == 0)
            throw new ArgumentException("At least one corridor segment is required.", nameof(segments));
        if (!double.IsFinite(lidarReturnsPerSecond) || lidarReturnsPerSecond < 0.0)
            throw new ArgumentOutOfRangeException(nameof(lidarReturnsPerSecond));
        if (!double.IsFinite(unloadStopDurationSeconds) || unloadStopDurationSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(unloadStopDurationSeconds));

        var totalDistance = 0.0;
        var elapsedSeconds = 0.0;
        var totalReturns = 0L;
        var pendingReturns = 0L;
        var peakStorageUnits = 0;
        var unloadCount = 0;
        var blackoutSeconds = 0.0;
        var unloadSegmentIds = new List<string>();

        for (var i = 0; i < segments.Count; i++)
        {
            var seg = segments[i];
            if (string.IsNullOrWhiteSpace(seg.SegmentId))
                throw new ArgumentException("SegmentId is required.", nameof(segments));
            if (!double.IsFinite(seg.LengthMetres) || seg.LengthMetres <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(segments), "Segment length must be positive.");
            if (!double.IsFinite(seg.TargetSpeedMetresPerSecond) || seg.TargetSpeedMetresPerSecond <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(segments), "Segment speed must be positive.");
            if (seg.Passes <= 0)
                throw new ArgumentOutOfRangeException(nameof(segments), "Segment passes must be positive.");

            if (seg.TargetSpeedMetresPerSecond > kombiPlatform.MaxSpeedMetresPerSecond + 1e-6)
            {
                return BuildFailedCorridor(
                    "kombi.error.segment_speed_exceeds_platform_limit",
                    kombiPlatform, totalDistance, elapsedSeconds, totalReturns,
                    peakStorageUnits, ComputeVirtualStorageUnits(pendingReturns),
                    unloadCount, blackoutSeconds, unloadSegmentIds);
            }

            var segmentDist = seg.LengthMetres * seg.Passes;
            var segmentTime = segmentDist / seg.TargetSpeedMetresPerSecond;
            var segmentReturns = (long)Math.Round(segmentTime * lidarReturnsPerSecond);

            totalDistance += segmentDist;
            elapsedSeconds += segmentTime;
            if (seg.IsTunnelBlackout)
                blackoutSeconds += segmentTime;

            totalReturns = checked(totalReturns + segmentReturns);
            pendingReturns = checked(pendingReturns + segmentReturns);

            var currentUnits = ComputeVirtualStorageUnits(pendingReturns);
            peakStorageUnits = Math.Max(peakStorageUnits, currentUnits);

            if (currentUnits > kombiPlatform.VirtualStorageCapacityUnits)
            {
                return BuildFailedCorridor(
                    "kombi.error.storage_capacity_exceeded",
                    kombiPlatform, totalDistance, elapsedSeconds, totalReturns,
                    peakStorageUnits, currentUnits, unloadCount, blackoutSeconds, unloadSegmentIds);
            }

            if (kombiPlatform.HasFiniteAutonomy && elapsedSeconds > kombiPlatform.PhysicalAutonomySeconds + 1e-9)
            {
                return BuildFailedCorridor(
                    "kombi.error.autonomy_exhausted",
                    kombiPlatform, totalDistance, elapsedSeconds, totalReturns,
                    peakStorageUnits, currentUnits, unloadCount, blackoutSeconds, unloadSegmentIds);
            }

            if (unloadAfterSegmentIndices is not null && unloadAfterSegmentIndices.Contains(i))
            {
                elapsedSeconds += unloadStopDurationSeconds;
                if (kombiPlatform.HasFiniteAutonomy && elapsedSeconds > kombiPlatform.PhysicalAutonomySeconds + 1e-9)
                {
                    return BuildFailedCorridor(
                        "kombi.error.autonomy_exhausted",
                        kombiPlatform, totalDistance, elapsedSeconds, totalReturns,
                        peakStorageUnits, currentUnits, unloadCount, blackoutSeconds, unloadSegmentIds);
                }

                pendingReturns = 0L;
                unloadCount++;
                unloadSegmentIds.Add(seg.SegmentId);
            }
        }

        var finalUnits = ComputeVirtualStorageUnits(pendingReturns);
        var remainingAutonomy = kombiPlatform.HasFiniteAutonomy
            ? Math.Max(0.0, kombiPlatform.PhysicalAutonomySeconds - elapsedSeconds)
            : double.PositiveInfinity;

        return new KombiCorridorSimulationResult(
            Succeeded: true,
            ReasonKey: "kombi.corridor.completed",
            TotalDistanceMetres: totalDistance,
            ElapsedAutonomySeconds: elapsedSeconds,
            RemainingAutonomySeconds: remainingAutonomy,
            TotalLidarReturns: totalReturns,
            PeakStorageUnitsBetweenUnloads: peakStorageUnits,
            FinalPendingStorageUnits: finalUnits,
            UnloadCount: unloadCount,
            TotalBlackoutSeconds: blackoutSeconds,
            AutonomyWithinLimit: !kombiPlatform.HasFiniteAutonomy || elapsedSeconds <= kombiPlatform.PhysicalAutonomySeconds + 1e-9,
            StorageWithinLimit: peakStorageUnits <= kombiPlatform.VirtualStorageCapacityUnits,
            UnloadAfterSegmentIds: Array.AsReadOnly(unloadSegmentIds.ToArray()));
    }

    private static KombiCorridorSimulationResult BuildFailedCorridor(
        string reasonKey,
        PlatformProfile platform,
        double totalDistance,
        double elapsedSeconds,
        long totalReturns,
        int peakUnits,
        int pendingUnits,
        int unloadCount,
        double blackoutSeconds,
        List<string> unloadSegmentIds)
    {
        var autonomyOk = !platform.HasFiniteAutonomy || elapsedSeconds <= platform.PhysicalAutonomySeconds + 1e-9;
        var storageOk = peakUnits <= platform.VirtualStorageCapacityUnits;
        var remaining = platform.HasFiniteAutonomy
            ? Math.Max(0.0, platform.PhysicalAutonomySeconds - elapsedSeconds)
            : double.PositiveInfinity;

        return new KombiCorridorSimulationResult(
            Succeeded: false,
            ReasonKey: reasonKey,
            TotalDistanceMetres: totalDistance,
            ElapsedAutonomySeconds: elapsedSeconds,
            RemainingAutonomySeconds: remaining,
            TotalLidarReturns: totalReturns,
            PeakStorageUnitsBetweenUnloads: peakUnits,
            FinalPendingStorageUnits: pendingUnits,
            UnloadCount: unloadCount,
            TotalBlackoutSeconds: blackoutSeconds,
            AutonomyWithinLimit: autonomyOk,
            StorageWithinLimit: storageOk,
            UnloadAfterSegmentIds: Array.AsReadOnly(unloadSegmentIds.ToArray()));
    }
}

