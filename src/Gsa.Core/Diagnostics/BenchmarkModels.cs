using Gsa.Core.Sensors.Camera;

namespace Gsa.Core.Diagnostics;

public enum BenchmarkPreset
{
    DryDay,
    RainNight,
}

public sealed record BenchmarkPresetConfig(
    BenchmarkPreset Preset,
    string WireName,
    bool IsRaining,
    bool IsNight,
    int ExtraParticleTriangles,
    int ExtraDrawCalls,
    CameraEnvironmentSnapshot CameraEnvironment)
{
    public static BenchmarkPresetConfig ForPreset(BenchmarkPreset preset) => preset switch
    {
        BenchmarkPreset.DryDay => new BenchmarkPresetConfig(
            BenchmarkPreset.DryDay,
            "dry_day",
            IsRaining: false,
            IsNight: false,
            ExtraParticleTriangles: 0,
            ExtraDrawCalls: 0,
            CameraEnvironment: CameraEnvironmentSnapshot.DryDay),
        BenchmarkPreset.RainNight => new BenchmarkPresetConfig(
            BenchmarkPreset.RainNight,
            "rain_night",
            IsRaining: true,
            IsNight: true,
            // Static bench particle billboards + streetlamp cones for rain_night preset.
            ExtraParticleTriangles: 2400,
            ExtraDrawCalls: 2,
            CameraEnvironment: new CameraEnvironmentSnapshot(
                isNight: true,
                sunAngleRadians: Math.PI,
                lensObstructionFraction: 0.08,
                baseClippingFraction: 0.04)),
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, "Unsupported benchmark preset."),
    };
}

public sealed record BenchmarkAcceptanceThresholds(
    double MinMeanFps = 60.0,
    double MaxFrameTimeP95Ms = 20.0,
    double MaxFrameTimeP99Ms = 33.3,
    double MaxMemoryResidentMb = 2048.0,
    double MaxPointCloudCacheMb = 256.0,
    double MaxSecondHalfMemoryGrowthMb = 32.0,
    int MaxSilentSampleDrops = 0)
{
    public static BenchmarkAcceptanceThresholds Default { get; } = new();

    public IReadOnlyList<string> EvaluateFailures(
        double meanFps,
        double frameP95Ms,
        double frameP99Ms,
        double peakMemoryResidentMb,
        double pointCloudCacheMb,
        double secondHalfMemoryDriftMb,
        bool continuousMemoryGrowthDetected,
        int silentSampleDrops)
    {
        var failures = new List<string>();
        if (!double.IsFinite(meanFps) || meanFps < MinMeanFps)
            failures.Add($"mean_fps {meanFps:F2} < {MinMeanFps:F2}");
        if (!double.IsFinite(frameP95Ms) || frameP95Ms > MaxFrameTimeP95Ms)
            failures.Add($"frame_time_p95_ms {frameP95Ms:F3} > {MaxFrameTimeP95Ms:F3}");
        if (!double.IsFinite(frameP99Ms) || frameP99Ms > MaxFrameTimeP99Ms)
            failures.Add($"frame_time_p99_ms {frameP99Ms:F3} > {MaxFrameTimeP99Ms:F3}");
        if (!double.IsFinite(peakMemoryResidentMb) || peakMemoryResidentMb > MaxMemoryResidentMb)
            failures.Add($"memory_resident_mb {peakMemoryResidentMb:F2} > {MaxMemoryResidentMb:F2}");
        if (!double.IsFinite(pointCloudCacheMb) || pointCloudCacheMb > MaxPointCloudCacheMb)
            failures.Add($"point_cloud_cache_mb {pointCloudCacheMb:F2} > {MaxPointCloudCacheMb:F2}");
        if (!double.IsFinite(secondHalfMemoryDriftMb) ||
            secondHalfMemoryDriftMb > MaxSecondHalfMemoryGrowthMb ||
            continuousMemoryGrowthDetected)
        {
            failures.Add(
                $"second_half_memory_growth (drift={secondHalfMemoryDriftMb:F2} MiB, continuous={continuousMemoryGrowthDetected})");
        }
        if (silentSampleDrops > MaxSilentSampleDrops)
            failures.Add($"silent_sample_drops {silentSampleDrops} > {MaxSilentSampleDrops}");
        return failures;
    }
}

public sealed record BenchmarkHardwareInfo(
    string OsDescription,
    string Architecture,
    string CpuModel,
    int CpuLogicalCores,
    int? CpuPhysicalCores,
    string GpuModel,
    string GpuDriver,
    bool HasDiscreteGpu,
    double? RamTotalGb,
    string RamChannels,
    string StorageType,
    string TargetResolution,
    string RendererBackend,
    bool VsyncEnabled,
    bool ReferenceHardwareMatched,
    string ReferenceTargetSummary);

public sealed record BenchmarkToolchainInfo(
    string BuildConfiguration,
    string DotNetVersion,
    string GodotVersion,
    string GitCommit,
    bool HeadlessGpuUnavailable);

public sealed record BenchmarkCountersSnapshot(
    int VisibleTriangles,
    int DrawCalls,
    int ActiveVehicles,
    int ActivePedestrians,
    long LidarPrimaryRaysCast,
    long LidarReturnsEmitted,
    double LidarPointsPerSec,
    double SensorQueueBacklogSeconds,
    int DeadlockRemovals,
    int CameraSamplesCaptured,
    int DroppedSamples,
    int SilentSampleDrops);

public sealed record BenchmarkTimerSummary(
    double MeanMs,
    double P95Ms,
    double P99Ms,
    double MaxMs)
{
    public static BenchmarkTimerSummary Zero { get; } = new(0.0, 0.0, 0.0, 0.0);

    public static BenchmarkTimerSummary Compute(IReadOnlyList<double> samplesMs)
    {
        if (samplesMs.Count == 0)
            return Zero;

        var sorted = samplesMs.ToArray();
        Array.Sort(sorted);

        var sum = 0.0;
        for (var i = 0; i < sorted.Length; i++)
        {
            if (!double.IsFinite(sorted[i]) || sorted[i] < 0.0)
                throw new ArgumentOutOfRangeException(nameof(samplesMs), "Timer samples must be finite and nonnegative.");
            sum += sorted[i];
        }

        var mean = sum / sorted.Length;
        var p95 = sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(0.95 * sorted.Length) - 1)];
        var p99 = sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(0.99 * sorted.Length) - 1)];
        var max = sorted[^1];
        return new BenchmarkTimerSummary(
            Math.Round(mean, 4),
            Math.Round(p95, 4),
            Math.Round(p99, 4),
            Math.Round(max, 4));
    }
}

public sealed record BenchmarkRunResult(
    int RunIndex,
    string Preset,
    ulong Seed,
    double WarmupSeconds,
    double DurationSeconds,
    int MeasuredTicks,
    double BlockAssemblyMs,
    BenchmarkCountersSnapshot Counters,
    BenchmarkTimerSummary FrameCpuMs,
    BenchmarkTimerSummary FrameGpuMs,
    BenchmarkTimerSummary PhysicsStepMs,
    BenchmarkTimerSummary SensorRaycastMs,
    BenchmarkTimerSummary ThumbnailCaptureMs,
    double MeanFps,
    double MemoryStartMb,
    double MemoryMidpointMb,
    double MemoryEndMb,
    double PeakMemoryMb,
    double SecondHalfMemoryDriftMb,
    bool ContinuousMemoryGrowthDetected,
    double PointCloudCacheMb,
    bool ThresholdsMet,
    IReadOnlyList<string> FailedThresholds);

public sealed record LidarProfileRateReport(
    string ProfileId,
    double ScanRateHz,
    int Channels,
    double NominalRaysPerSecond,
    double MeasuredEffectiveRaysPerSecond,
    double MaxRangeMetres,
    double RangeSigmaMetres,
    bool OfferedInMvpShop);

public sealed record BlockAssemblyReport(
    string SceneId,
    double AssemblyMs,
    int WorldObjects,
    int FacadeCount,
    int UnloadZoneCount,
    int ArtKitModules,
    int ArtKitTotalTriangles,
    int VehicleMultiMeshTriangles,
    int PedestrianMultiMeshTriangles,
    int TotalSceneTriangles);

public sealed record BenchmarkProtocolOptions(
    double WarmupSeconds = 30.0,
    double DurationSeconds = 300.0,
    int RunsPerPreset = 3,
    ulong BaseSeed = 20261115UL,
    string BuildConfiguration = "Release",
    string RendererBackend = "Compatibility",
    bool VsyncEnabled = false,
    string TargetResolution = "1920x1080",
    string GodotVersion = "4.7.2-stable-mono",
    string GitCommit = "unknown",
    bool HeadlessGpuUnavailable = true)
{
    public void Validate()
    {
        if (!double.IsFinite(WarmupSeconds) || WarmupSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(WarmupSeconds), "Warmup seconds must be finite and nonnegative.");
        if (!double.IsFinite(DurationSeconds) || DurationSeconds <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(DurationSeconds), "Duration seconds must be finite and positive.");
        if (RunsPerPreset < 1)
            throw new ArgumentOutOfRangeException(nameof(RunsPerPreset), "RunsPerPreset must be >= 1.");
    }
}

public sealed record BenchmarkReport(
    int SchemaVersion,
    string BenchmarkId,
    string GeneratedAtUtc,
    BenchmarkToolchainInfo Toolchain,
    BenchmarkHardwareInfo Hardware,
    BenchmarkAcceptanceThresholds Thresholds,
    BlockAssemblyReport BlockAssembly,
    IReadOnlyList<LidarProfileRateReport> LidarRates,
    IReadOnlyList<BenchmarkRunResult> Runs,
    string GateStatus,
    IReadOnlyList<string> GateReasons);

