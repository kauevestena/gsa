using System.Collections.Concurrent;
using Gsa.Core.Geometry;
using Gsa.Core.Mobility;

namespace Gsa.Core.Sensors.Lidar;

/// <summary>Canonical LiDAR sensor profiles (L4..L6) matching specs/parametros.md; never offered in MVP shop.</summary>
public sealed record LidarProfile
{
    public string Id { get; }
    public int Grade { get; }
    public int SensorUnits { get; }
    public int ChannelsPerSensor { get; }
    public double ScanRateHz { get; }
    public int AzimuthsPerScan { get; }
    public double MaxRangeMetres { get; }
    public double RangeSigmaMetres { get; }
    public double PowerWatts { get; }
    public bool OfferedInMvpShop { get; }

    public int TotalChannels => SensorUnits * ChannelsPerSensor;
    public int PrimaryRaysPerScan => TotalChannels * AzimuthsPerScan;
    public int PrimaryRaysPerSecond => (int)Math.Round(PrimaryRaysPerScan * ScanRateHz);

    public LidarProfile(
        string id,
        int grade,
        int sensorUnits,
        int channelsPerSensor,
        double scanRateHz,
        int azimuthsPerScan,
        double maxRangeMetres,
        double rangeSigmaMetres,
        double powerWatts,
        bool offeredInMvpShop = false)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("LiDAR profile ID is required.", nameof(id));
        if (grade < 4)
            throw new ArgumentOutOfRangeException(nameof(grade), "LiDAR requires platform level >= 4.");
        if (sensorUnits <= 0 || channelsPerSensor <= 0 || azimuthsPerScan <= 0)
            throw new ArgumentOutOfRangeException(nameof(channelsPerSensor), "Channel and scan counts must be positive.");
        if (!double.IsFinite(scanRateHz) || scanRateHz <= 0)
            throw new ArgumentOutOfRangeException(nameof(scanRateHz));
        if (!double.IsFinite(maxRangeMetres) || maxRangeMetres <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxRangeMetres));
        if (!double.IsFinite(rangeSigmaMetres) || rangeSigmaMetres < 0)
            throw new ArgumentOutOfRangeException(nameof(rangeSigmaMetres));
        if (!double.IsFinite(powerWatts) || powerWatts < 0)
            throw new ArgumentOutOfRangeException(nameof(powerWatts));

        Id = id;
        Grade = grade;
        SensorUnits = sensorUnits;
        ChannelsPerSensor = channelsPerSensor;
        ScanRateHz = scanRateHz;
        AzimuthsPerScan = azimuthsPerScan;
        MaxRangeMetres = maxRangeMetres;
        RangeSigmaMetres = rangeSigmaMetres;
        PowerWatts = powerWatts;
        OfferedInMvpShop = offeredInMvpShop;
    }

    public static LidarProfile L4 { get; } = new(
        id: "lidar-bench",
        grade: 4,
        sensorUnits: 1,
        channelsPerSensor: 1,
        scanRateHz: 10.0,
        azimuthsPerScan: 100,
        maxRangeMetres: 50.0,
        rangeSigmaMetres: 0.02,
        powerWatts: 10.0,
        offeredInMvpShop: false);

    public static LidarProfile L5 { get; } = new(
        id: "lidar-l5",
        grade: 5,
        sensorUnits: 2,
        channelsPerSensor: 8,
        scanRateHz: 5.0,
        azimuthsPerScan: 100,
        maxRangeMetres: 80.0,
        rangeSigmaMetres: 0.01,
        powerWatts: 16.0,
        offeredInMvpShop: false);

    public static LidarProfile L6 { get; } = new(
        id: "lidar-l6",
        grade: 6,
        sensorUnits: 2,
        channelsPerSensor: 16,
        scanRateHz: 5.0,
        azimuthsPerScan: 100,
        maxRangeMetres: 120.0,
        rangeSigmaMetres: 0.005,
        powerWatts: 24.0,
        offeredInMvpShop: false);

    public static IReadOnlyList<LidarProfile> AllProfiles { get; } = Array.AsReadOnly([L4, L5, L6]);

    public bool CanEquipOn(PlatformProfile platform)
    {
        ArgumentNullException.ThrowIfNull(platform);
        return platform.SupportsLidarSlots && platform.Level >= Grade;
    }

    /// <summary>Unit ray direction in LiDAR frame (X forward, Y left, Z up).</summary>
    public static Vector3d DirectionFromAngles(double azimuthRadians, double elevationRadians)
    {
        var cosEl = Math.Cos(elevationRadians);
        return new Vector3d(
            cosEl * Math.Cos(azimuthRadians),
            cosEl * Math.Sin(azimuthRadians),
            Math.Sin(elevationRadians)).Normalized();
    }

    private static readonly ConcurrentDictionary<(int Grade, int TotalChannels, int AzimuthsPerScan), IReadOnlyList<(double AzimuthRadians, double ElevationRadians, Vector3d DirectionLidar)>> ScanPatternCache = new();

    public IReadOnlyList<(double AzimuthRadians, double ElevationRadians, Vector3d DirectionLidar)> BuildScanPattern() =>
        ScanPatternCache.GetOrAdd((Grade, TotalChannels, AzimuthsPerScan), static key => BuildUncachedScanPattern(key.Grade, key.TotalChannels, key.AzimuthsPerScan));

    private static IReadOnlyList<(double AzimuthRadians, double ElevationRadians, Vector3d DirectionLidar)> BuildUncachedScanPattern(
        int grade, int totalChannels, int azimuthsPerScan)
    {
        var rays = new List<(double, double, Vector3d)>(totalChannels * azimuthsPerScan);
        if (grade == 4 && totalChannels == 1)
        {
            // Cross-section transverse profiler sweep spanning Y-Z plane with slight forward pitch (X forward, Y left, Z up).
            for (var i = 0; i < azimuthsPerScan; i++)
            {
                var t = azimuthsPerScan == 1 ? 0.5 : (double)i / (azimuthsPerScan - 1);
                var azimuth = (t - 0.5) * Math.PI;
                var elevation = -0.15;
                rays.Add((azimuth, elevation, DirectionFromAngles(azimuth, elevation)));
            }
            return Array.AsReadOnly(rays.ToArray());
        }

        const double minElev = -15.0 * Math.PI / 180.0;
        const double maxElev = 15.0 * Math.PI / 180.0;
        for (var ch = 0; ch < totalChannels; ch++)
        {
            var chFrac = totalChannels == 1 ? 0.5 : (double)ch / (totalChannels - 1);
            var elevation = minElev + chFrac * (maxElev - minElev);
            for (var az = 0; az < azimuthsPerScan; az++)
            {
                var azimuth = 2.0 * Math.PI * az / azimuthsPerScan;
                rays.Add((azimuth, elevation, DirectionFromAngles(azimuth, elevation)));
            }
        }
        return Array.AsReadOnly(rays.ToArray());
    }
}

