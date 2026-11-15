using System.Diagnostics;
using System.Globalization;
using Gsa.Core.Art;
using Gsa.Core.Geometry;
using Gsa.Core.Sensors.Camera;
using Gsa.Core.Sensors.Lidar;
using Gsa.Core.Traffic;
using Gsa.Core.World;

namespace Gsa.Core.Diagnostics;

/// <summary>Executes reproducible M1 bench timing, counter, memory, and 3-run preset protocols without Godot dependencies.</summary>
public static class BenchmarkRunner
{
    public const int SimulatedVehicleTriangleEstimate = 1800;
    public const int SimulatedPedestrianTriangleEstimate = 1200;

    public static BenchmarkReport RunM1Benchmark(
        WorldSceneManifest manifest,
        ArtKitValidationSummary artKitSummary,
        BenchmarkProtocolOptions? options = null,
        BenchmarkAcceptanceThresholds? thresholds = null,
        BenchmarkHardwareInfo? hardwareOverride = null,
        BenchmarkToolchainInfo? toolchainOverride = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(artKitSummary);
        if (!artKitSummary.Valid)
            throw new ArgumentException("Pilot art kit must be valid before running M1 benchmark.", nameof(artKitSummary));

        var opts = options ?? new BenchmarkProtocolOptions();
        opts.Validate();
        var limits = thresholds ?? BenchmarkAcceptanceThresholds.Default;

        var hardware = hardwareOverride ?? BenchmarkHardwareProbe.DetectHost(
            rendererBackend: opts.RendererBackend,
            vsyncEnabled: opts.VsyncEnabled,
            targetResolution: opts.TargetResolution,
            headless: opts.HeadlessGpuUnavailable);

        var toolchain = toolchainOverride ?? new BenchmarkToolchainInfo(
            BuildConfiguration: opts.BuildConfiguration,
            DotNetVersion: Environment.Version.ToString(),
            GodotVersion: opts.GodotVersion,
            GitCommit: opts.GitCommit,
            HeadlessGpuUnavailable: opts.HeadlessGpuUnavailable);

        var blockReport = MeasureBlockAssembly(manifest, artKitSummary);
        var lidarRates = MeasureLidarProfileRates(manifest);

        var runs = new List<BenchmarkRunResult>(opts.RunsPerPreset * 2);
        var presets = new[] { BenchmarkPreset.DryDay, BenchmarkPreset.RainNight };
        for (var presetIndex = 0; presetIndex < presets.Length; presetIndex++)
        {
            var presetConfig = BenchmarkPresetConfig.ForPreset(presets[presetIndex]);
            for (var runIndex = 1; runIndex <= opts.RunsPerPreset; runIndex++)
            {
                var seed = opts.BaseSeed + (ulong)(presetIndex * 100 + runIndex);
                runs.Add(ExecuteSingleRun(
                    manifest,
                    blockReport,
                    presetConfig,
                    runIndex,
                    seed,
                    opts,
                    limits));
            }
        }

        var (gateStatus, gateReasons) = EvaluateGate(hardware, toolchain, runs);
        return new BenchmarkReport(
            SchemaVersion: 1,
            BenchmarkId: "M1-BENCH-V1",
            GeneratedAtUtc: DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            Toolchain: toolchain,
            Hardware: hardware,
            Thresholds: limits,
            BlockAssembly: blockReport,
            LidarRates: lidarRates,
            Runs: runs,
            GateStatus: gateStatus,
            GateReasons: gateReasons);
    }

    public static BlockAssemblyReport MeasureBlockAssembly(
        WorldSceneManifest manifest,
        ArtKitValidationSummary artKitSummary)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(artKitSummary);

        var sw = Stopwatch.StartNew();
        var objectIndex = new Dictionary<string, WorldBoxObject>(manifest.Objects.Count, StringComparer.Ordinal);
        var facadeCount = 0;
        var unloadCount = 0;
        foreach (var obj in manifest.Objects)
        {
            objectIndex[obj.Id] = obj;
            if (string.Equals(obj.ObjectClass, "building_facade", StringComparison.Ordinal) ||
                string.Equals(obj.ObjectClass, "facade", StringComparison.Ordinal))
                facadeCount++;
            else if (string.Equals(obj.ObjectClass, "unload_point", StringComparison.Ordinal) ||
                     string.Equals(obj.ObjectClass, "unload_zone", StringComparison.Ordinal))
                unloadCount++;
        }

        var _ = RoadNetworkGraph.CreateTutorialBlockGraph();
        sw.Stop();

        var worldBoxTris = objectIndex.Count * 12;
        var vehicleTris = TrafficBenchSimulator.TargetVehicleCount * SimulatedVehicleTriangleEstimate;
        var pedTris = TrafficBenchSimulator.TargetPedestrianCount * SimulatedPedestrianTriangleEstimate;
        var totalSceneTris = worldBoxTris + artKitSummary.TotalTriangles + vehicleTris + pedTris;

        return new BlockAssemblyReport(
            SceneId: manifest.SceneId,
            AssemblyMs: Math.Round(sw.Elapsed.TotalMilliseconds, 4),
            WorldObjects: objectIndex.Count,
            FacadeCount: facadeCount,
            UnloadZoneCount: unloadCount,
            ArtKitModules: artKitSummary.Modules.Count,
            ArtKitTotalTriangles: artKitSummary.TotalTriangles,
            VehicleMultiMeshTriangles: vehicleTris,
            PedestrianMultiMeshTriangles: pedTris,
            TotalSceneTriangles: totalSceneTris);
    }

    public static IReadOnlyList<LidarProfileRateReport> MeasureLidarProfileRates(WorldSceneManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var collisionObjects = manifest.Objects.Where(o => o.Collision).ToArray();
        var rayCaster = LidarBenchSimulator.CreateAnalyticBoxRayCaster(collisionObjects);
        var mount = LidarMountEstimate.Zero;
        var rigEnu = new Vector3d(500020.0, 7400001.0, 101.0);
        var yawNorth = Quaterniond.FromAxisAngle(Vector3d.UnitZ, Math.PI / 2.0);
        var trajectory = new EstimatedBodyTrajectory([
            new EstimatedBodyPose(0.0, rigEnu, yawNorth),
            new EstimatedBodyPose(2.0, rigEnu, yawNorth),
        ]);
        var lidarToMap = LidarBenchSimulator.ComposeLidarToMap(new RigidTransform(yawNorth, rigEnu), mount);

        var reports = new List<LidarProfileRateReport>(LidarProfile.AllProfiles.Count);
        foreach (var profile in LidarProfile.AllProfiles)
        {
            var sim = new LidarBenchSimulator(profile, seed: 20261116UL);
            // Cast 1 second of nominal scans for each profile (10 for L4, 5 for L5/L6) to measure real throughput.
            var scanCount = Math.Max(1, (int)Math.Round(profile.ScanRateHz));
            var dtPerScan = 1.0 / profile.ScanRateHz;

            var sw = Stopwatch.StartNew();
            for (var i = 0; i < scanCount; i++)
            {
                sim.TrySimulateScan(
                    i * dtPerScan,
                    lidarToMap,
                    trajectory,
                    mount,
                    isRaining: false,
                    rayCaster,
                    out _,
                    out _,
                    autoDrain: true,
                    zeroNoise: false);
            }
            sw.Stop();

            var elapsedSec = Math.Max(1e-6, sw.Elapsed.TotalSeconds);
            var effectiveRps = Math.Round(sim.TotalPrimaryRaysCast / elapsedSec, 1);

            reports.Add(new LidarProfileRateReport(
                ProfileId: profile.Id,
                ScanRateHz: profile.ScanRateHz,
                Channels: profile.TotalChannels,
                NominalRaysPerSecond: profile.PrimaryRaysPerSecond,
                MeasuredEffectiveRaysPerSecond: effectiveRps,
                MaxRangeMetres: profile.MaxRangeMetres,
                RangeSigmaMetres: profile.RangeSigmaMetres,
                OfferedInMvpShop: profile.OfferedInMvpShop));
        }

        return reports;
    }

    private static BenchmarkRunResult ExecuteSingleRun(
        WorldSceneManifest manifest,
        BlockAssemblyReport blockReport,
        BenchmarkPresetConfig preset,
        int runIndex,
        ulong seed,
        BenchmarkProtocolOptions opts,
        BenchmarkAcceptanceThresholds thresholds)
    {
        const double dt = TrafficBenchSimulator.FixedDtSeconds;
        var warmupTicks = (int)Math.Round(opts.WarmupSeconds / dt);
        var measuredTicks = Math.Max(1, (int)Math.Round(opts.DurationSeconds / dt));

        var collisionObjects = manifest.Objects.Where(o => o.Collision).ToArray();
        var cameraCandidates = manifest.Objects.Select(CameraVisibilityCandidate.FromWorldObject).ToArray();
        var cameraOccluder = CameraVisibilityEvaluator.CreateAnalyticBoxOcclusionTester(collisionObjects);
        var lidarRayCaster = LidarBenchSimulator.CreateAnalyticBoxRayCaster(collisionObjects);

        var traffic = new TrafficBenchSimulator(seed: seed, respawnAfterDeadlockRemoval: true);
        var cameraScheduler = new CameraCaptureScheduler("cam_m1_bench", CameraSensorProfile.Initial);
        var lidar = new LidarBenchSimulator(LidarProfile.L4, seed: seed + 17UL);
        var lidarMount = LidarMountEstimate.Zero;

        // 10 Hz L4 capture cadence at 60 Hz physics = every 6 ticks.
        var lidarTickInterval = Math.Max(1, (int)Math.Round(60.0 / LidarProfile.L4.ScanRateHz));

        var frameCpuSamples = new List<double>(measuredTicks);
        var physicsSamples = new List<double>(measuredTicks);
        var lidarSamples = new List<double>(Math.Max(1, measuredTicks / lidarTickInterval + 1));
        var cameraSamples = new List<double>(Math.Max(1, measuredTicks / 30 + 1));

        var startMemoryMb = 0.0;
        var midpointMemoryMb = 0.0;
        var peakMemoryMb = 0.0;
        var secondHalfCheckpointTicks = new[]
        {
            measuredTicks / 2,
            (measuredTicks * 5) / 8,
            (measuredTicks * 3) / 4,
            (measuredTicks * 7) / 8,
            measuredTicks - 1,
        };
        var secondHalfMemorySamples = new double[secondHalfCheckpointTicks.Length];
        var nextCheckpointIdx = 0;

        long runStartRays = 0;
        long runStartReturns = 0;
        var runStartDropped = 0;
        var runStartDeadlocks = 0;
        var cameraSamplesCaptured = 0;
        double maxBacklogSeconds = 0.0;
        TrafficStepSummary? lastTrafficSummary = null;

        var totalTicks = warmupTicks + measuredTicks;
        for (var tick = 0; tick < totalTicks; tick++)
        {
            var isWarmup = tick < warmupTicks;
            var measuredTick = tick - warmupTicks;

            if (!isWarmup && measuredTick == 0)
            {
                cameraScheduler.FlushBatchManifest($"bench_{preset.WireName}_run{runIndex}_warmup");
                CollectBeforeMemorySample();
                runStartRays = lidar.TotalPrimaryRaysCast;
                runStartReturns = lidar.TotalReturnsEmitted;
                runStartDropped = lidar.DroppedScansOnSaturation + cameraScheduler.SaturatedDroppedCount;
                runStartDeadlocks = traffic.DeadlockRemovals;
                startMemoryMb = ReadProcessResidentMb();
                peakMemoryMb = startMemoryMb;
            }

            var timeSeconds = tick * dt;
            var bodyPose = EvaluateFixedRoutePose(timeSeconds, manifest.Bounds);
            var camForward = bodyPose.BodyToMap.Rotate(Vector3d.UnitX);

            var frameStart = Stopwatch.GetTimestamp();

            var physStart = Stopwatch.GetTimestamp();
            lastTrafficSummary = traffic.Step(dt, bodyPose.PositionEnu, camForward, bodyPose.PositionEnu);
            var physMs = Stopwatch.GetElapsedTime(physStart).TotalMilliseconds;

            double camMs = 0.0;
            if (cameraScheduler.IsCaptureTick(tick))
            {
                var camStart = Stopwatch.GetTimestamp();
                var camTransform = CameraGeometry.CameraToMap(bodyPose, CameraMountEstimate.ForwardFacing);
                var req = new CameraCaptureRequest(
                    tick,
                    timeSeconds,
                    camTransform,
                    camTransform,
                    camTransform,
                    bodyPose,
                    CameraMountEstimate.ForwardFacing,
                    preset.CameraEnvironment,
                    cameraCandidates,
                    cameraOccluder);

                if (cameraScheduler.TryEnqueueSimulatedCapture(req, null, out _, out _, autoDrain: true))
                {
                    if (!isWarmup)
                        cameraSamplesCaptured++;
                }
                camMs = Stopwatch.GetElapsedTime(camStart).TotalMilliseconds;
            }

            double lidarMs = 0.0;
            if (tick % lidarTickInterval == 0)
            {
                var lidarStart = Stopwatch.GetTimestamp();
                var lidarToMap = LidarBenchSimulator.ComposeLidarToMap(
                    new RigidTransform(bodyPose.BodyToMap, bodyPose.PositionEnu),
                    lidarMount);
                var trajectory = new EstimatedBodyTrajectory([
                    new EstimatedBodyPose(timeSeconds - dt, bodyPose.PositionEnu, bodyPose.BodyToMap),
                    new EstimatedBodyPose(timeSeconds + dt, bodyPose.PositionEnu, bodyPose.BodyToMap),
                ]);

                lidar.TrySimulateScan(
                    timeSeconds,
                    lidarToMap,
                    trajectory,
                    lidarMount,
                    preset.IsRaining,
                    lidarRayCaster,
                    out _,
                    out _,
                    autoDrain: true,
                    zeroNoise: false);
                lidarMs = Stopwatch.GetElapsedTime(lidarStart).TotalMilliseconds;
            }

            var frameMs = Stopwatch.GetElapsedTime(frameStart).TotalMilliseconds;

            if (!isWarmup)
            {
                frameCpuSamples.Add(frameMs);
                physicsSamples.Add(physMs);
                if (camMs > 0.0)
                    cameraSamples.Add(camMs);
                if (lidarMs > 0.0)
                    lidarSamples.Add(lidarMs);

                if (cameraScheduler.SampleCount >= 60)
                    cameraScheduler.FlushBatchManifest($"bench_{preset.WireName}_run{runIndex}_t{tick}");

                var currentBacklog = Math.Max(
                    lidar.CurrentBacklogSeconds,
                    cameraScheduler.PendingQueueCount / CameraSensorProfile.Initial.RateHz);
                if (currentBacklog > maxBacklogSeconds)
                    maxBacklogSeconds = currentBacklog;

                if (measuredTick == measuredTicks / 2)
                {
                    CollectBeforeMemorySample();
                    midpointMemoryMb = ReadProcessResidentMb();
                    if (midpointMemoryMb > peakMemoryMb)
                        peakMemoryMb = midpointMemoryMb;
                }

                if (nextCheckpointIdx < secondHalfCheckpointTicks.Length &&
                    measuredTick == secondHalfCheckpointTicks[nextCheckpointIdx])
                {
                    if (measuredTick != measuredTicks / 2)
                        CollectBeforeMemorySample();
                    var sampleMb = ReadProcessResidentMb();
                    secondHalfMemorySamples[nextCheckpointIdx] = sampleMb;
                    if (sampleMb > peakMemoryMb)
                        peakMemoryMb = sampleMb;
                    nextCheckpointIdx++;
                }
            }
        }

        cameraScheduler.FlushBatchManifest($"bench_{preset.WireName}_run{runIndex}_end");
        CollectBeforeMemorySample();
        var endMemoryMb = ReadProcessResidentMb();
        if (endMemoryMb > peakMemoryMb)
            peakMemoryMb = endMemoryMb;
        if (midpointMemoryMb <= 0.0)
            midpointMemoryMb = startMemoryMb;

        var secondHalfDriftMb = Math.Round(endMemoryMb - midpointMemoryMb, 3);
        var continuousGrowth = DetectContinuousGrowth(secondHalfMemorySamples, thresholdDeltaMb: 8.0);

        var frameSummary = BenchmarkTimerSummary.Compute(frameCpuSamples);
        var physSummary = BenchmarkTimerSummary.Compute(physicsSamples);
        var lidarSummary = BenchmarkTimerSummary.Compute(lidarSamples);
        var camSummary = BenchmarkTimerSummary.Compute(cameraSamples);
        // No graphical GPU viewport in Core CLI; EngineBenchmarkHarness records live Godot GPU timings when active.
        var gpuSummary = BenchmarkTimerSummary.Zero;

        var raysInWindow = lidar.TotalPrimaryRaysCast - runStartRays;
        var returnsInWindow = lidar.TotalReturnsEmitted - runStartReturns;
        var explicitDrops = (lidar.DroppedScansOnSaturation + cameraScheduler.SaturatedDroppedCount) - runStartDropped;
        // Expected camera samples at 2 Hz in measured window vs captured + explicit saturation drops:
        var expectedCameraSamples = measuredTicks / 30;
        var silentDrops = Math.Max(0, expectedCameraSamples - cameraSamplesCaptured - Math.Max(0, explicitDrops));

        var measuredSeconds = measuredTicks * dt;
        var meanCpuFrameMs = Math.Max(1e-4, frameSummary.MeanMs);
        var meanFps = Math.Round(1000.0 / meanCpuFrameMs, 2);

        // Include world grid, art kit modules, 50 cars, 30 pedestrians, and preset particles/lighting draw calls.
        var visibleTriangles = blockReport.TotalSceneTriangles + preset.ExtraParticleTriangles;
        var drawCalls = 1 + blockReport.ArtKitModules + 2 + preset.ExtraDrawCalls;

        var counters = new BenchmarkCountersSnapshot(
            VisibleTriangles: visibleTriangles,
            DrawCalls: drawCalls,
            ActiveVehicles: lastTrafficSummary?.ActiveVehicles ?? TrafficBenchSimulator.TargetVehicleCount,
            ActivePedestrians: lastTrafficSummary?.ActivePedestrians ?? TrafficBenchSimulator.TargetPedestrianCount,
            LidarPrimaryRaysCast: raysInWindow,
            LidarReturnsEmitted: returnsInWindow,
            LidarPointsPerSec: Math.Round(returnsInWindow / measuredSeconds, 2),
            SensorQueueBacklogSeconds: Math.Round(maxBacklogSeconds, 4),
            DeadlockRemovals: traffic.DeadlockRemovals - runStartDeadlocks,
            CameraSamplesCaptured: cameraSamplesCaptured,
            DroppedSamples: Math.Max(0, explicitDrops),
            SilentSampleDrops: silentDrops);

        const double pointCloudCacheMb = 0.0;
        var failures = thresholds.EvaluateFailures(
            meanFps: meanFps,
            frameP95Ms: frameSummary.P95Ms,
            frameP99Ms: frameSummary.P99Ms,
            peakMemoryResidentMb: peakMemoryMb,
            pointCloudCacheMb: pointCloudCacheMb,
            secondHalfMemoryDriftMb: secondHalfDriftMb,
            continuousMemoryGrowthDetected: continuousGrowth,
            silentSampleDrops: silentDrops);

        return new BenchmarkRunResult(
            RunIndex: runIndex,
            Preset: preset.WireName,
            Seed: seed,
            WarmupSeconds: opts.WarmupSeconds,
            DurationSeconds: opts.DurationSeconds,
            MeasuredTicks: measuredTicks,
            BlockAssemblyMs: blockReport.AssemblyMs,
            Counters: counters,
            FrameCpuMs: frameSummary,
            FrameGpuMs: gpuSummary,
            PhysicsStepMs: physSummary,
            SensorRaycastMs: lidarSummary,
            ThumbnailCaptureMs: camSummary,
            MeanFps: meanFps,
            MemoryStartMb: Math.Round(startMemoryMb, 3),
            MemoryMidpointMb: Math.Round(midpointMemoryMb, 3),
            MemoryEndMb: Math.Round(endMemoryMb, 3),
            PeakMemoryMb: Math.Round(peakMemoryMb, 3),
            SecondHalfMemoryDriftMb: secondHalfDriftMb,
            ContinuousMemoryGrowthDetected: continuousGrowth,
            PointCloudCacheMb: pointCloudCacheMb,
            ThresholdsMet: failures.Count == 0,
            FailedThresholds: failures);
    }

    public static (string GateStatus, IReadOnlyList<string> Reasons) EvaluateGate(
        BenchmarkHardwareInfo hardware,
        BenchmarkToolchainInfo toolchain,
        IReadOnlyList<BenchmarkRunResult> runs)
    {
        ArgumentNullException.ThrowIfNull(hardware);
        ArgumentNullException.ThrowIfNull(toolchain);
        ArgumentNullException.ThrowIfNull(runs);

        var failedRuns = runs.Where(r => !r.ThresholdsMet).ToArray();
        if (failedRuns.Length > 0)
        {
            var reasons = failedRuns
                .Select(r => $"{r.Preset}#run{r.RunIndex}: {string.Join(", ", r.FailedThresholds)}")
                .ToArray();
            return ("failed", reasons);
        }

        var pendingReasons = new List<string>();
        if (!hardware.ReferenceHardwareMatched)
            pendingReasons.Add($"Host hardware ({hardware.GpuModel}, {hardware.CpuModel}) does not match reference ({hardware.ReferenceTargetSummary}).");
        if (toolchain.HeadlessGpuUnavailable)
            pendingReasons.Add("Headless CPU/domain run has no graphical GPU viewport measurement (frame_gpu_ms requires non-headless reference run).");

        return pendingReasons.Count == 0
            ? ("passed", Array.Empty<string>())
            : ("pending_reference_hardware", pendingReasons);
    }

    private static EstimatedBodyPose EvaluateFixedRoutePose(double timeSeconds, WorldBounds bounds)
    {
        // Deterministic closed perimeter loop strictly inside the manifest block bounds (~6 m/s).
        const double period = 60.0;
        var phase = (timeSeconds % period) / period;
        var angle = phase * 2.0 * Math.PI;
        var centerE = (bounds.Min.X + bounds.Max.X) * 0.5;
        var centerN = (bounds.Min.Y + bounds.Max.Y) * 0.5;
        var radiusE = Math.Max(4.0, (bounds.Max.X - bounds.Min.X) * 0.38);
        var radiusN = Math.Max(2.0, (bounds.Max.Y - bounds.Min.Y) * 0.35);
        var east = centerE + radiusE * Math.Sin(angle);
        var north = centerN + radiusN * Math.Cos(angle);
        var heading = Math.Atan2(radiusE * Math.Cos(angle), -radiusN * Math.Sin(angle));
        var rotation = Quaterniond.FromAxisAngle(Vector3d.UnitZ, heading);
        return new EstimatedBodyPose(timeSeconds, new Vector3d(east, north, 101.0), rotation);
    }

    private static bool DetectContinuousGrowth(IReadOnlyList<double> samplesMb, double thresholdDeltaMb)
    {
        if (samplesMb.Count < 4)
            return false;

        var strictlyIncreasing = true;
        for (var i = 1; i < samplesMb.Count; i++)
        {
            if (samplesMb[i] <= samplesMb[i - 1])
            {
                strictlyIncreasing = false;
                break;
            }
        }

        return strictlyIncreasing && (samplesMb[^1] - samplesMb[0]) >= thresholdDeltaMb;
    }

    private static void CollectBeforeMemorySample()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static double ReadProcessResidentMb()
    {
        using var proc = Process.GetCurrentProcess();
        return proc.WorkingSet64 / (1024.0 * 1024.0);
    }
}
