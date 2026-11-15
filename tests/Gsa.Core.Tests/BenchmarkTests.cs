using System.Reflection;
using Gsa.Core.Art;
using Gsa.Core.Diagnostics;
using Gsa.Core.Geometry;
using Gsa.Core.Sensors.Camera;
using Gsa.Core.World;
using Xunit;

namespace Gsa.Core.Tests;

public sealed class BenchmarkTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Gsa.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    [Fact]
    public void TimerSummaryComputesExactMeanP95P99AndMax()
    {
        var samples = Enumerable.Range(1, 100).Select(i => (double)i).ToArray();
        var summary = BenchmarkTimerSummary.Compute(samples);
        Assert.Equal(50.5, summary.MeanMs, 4);
        Assert.Equal(95.0, summary.P95Ms, 4);
        Assert.Equal(99.0, summary.P99Ms, 4);
        Assert.Equal(100.0, summary.MaxMs, 4);
    }

    [Fact]
    public void RunnerMeasuresBothPresetsThreeRunsPerPresetAndReportsPendingReferenceHardware()
    {
        var root = FindRepoRoot();
        var manifest = WorldSceneManifest.Parse(File.ReadAllText(Path.Combine(root, "game", "world", "tutorial-block.json")));
        var artSummary = PilotArtKitValidator.ValidateDirectory(Path.Combine(root, "game", "world", "pilot-kit"));
        Assert.True(artSummary.Valid);

        var options = new BenchmarkProtocolOptions(
            WarmupSeconds: 1.0,
            DurationSeconds: 5.0,
            RunsPerPreset: 3,
            HeadlessGpuUnavailable: true);

        var report = BenchmarkRunner.RunM1Benchmark(manifest, artSummary, options);
        Assert.Equal(1, report.SchemaVersion);
        Assert.Equal(20, report.BlockAssembly.FacadeCount);
        Assert.True(report.BlockAssembly.UnloadZoneCount >= 1);
        Assert.Equal(9, report.BlockAssembly.ArtKitModules);
        Assert.Equal(3, report.LidarRates.Count);
        Assert.Contains(report.LidarRates, r => r.ProfileId == "lidar-bench" && r.NominalRaysPerSecond == 1000 && !r.OfferedInMvpShop);
        Assert.Contains(report.LidarRates, r => r.ProfileId == "lidar-l5" && r.NominalRaysPerSecond == 8000 && !r.OfferedInMvpShop);
        Assert.Contains(report.LidarRates, r => r.ProfileId == "lidar-l6" && r.NominalRaysPerSecond == 16000 && !r.OfferedInMvpShop);

        Assert.Equal(6, report.Runs.Count);
        Assert.Equal(3, report.Runs.Count(r => r.Preset == "dry_day"));
        Assert.Equal(3, report.Runs.Count(r => r.Preset == "rain_night"));

        foreach (var run in report.Runs)
        {
            Assert.Equal(50, run.Counters.ActiveVehicles);
            Assert.Equal(30, run.Counters.ActivePedestrians);
            Assert.Equal(0, run.Counters.SilentSampleDrops);
            Assert.True(run.Counters.VisibleTriangles > report.BlockAssembly.ArtKitTotalTriangles);
            Assert.True(run.Counters.LidarPrimaryRaysCast > 0);
            Assert.True(run.Counters.LidarPointsPerSec > 0);
            Assert.True(run.MemoryEndMb > 0);
            Assert.True(run.ThresholdsMet);
        }

        Assert.False(report.Hardware.ReferenceHardwareMatched);
        Assert.Equal("pending_reference_hardware", report.GateStatus);
    }

    [Fact]
    public void GateEvaluationDistinguishesPassedFailedAndReferenceMismatch()
    {
        var nonMatching = BenchmarkHardwareProbe.DetectHost(
            gpuModelOverride: "NVIDIA GeForce RTX 4060",
            gpuDriverOverride: "560.35",
            headless: false);
        Assert.False(nonMatching.ReferenceHardwareMatched);

        var vega7Hardware = new BenchmarkHardwareInfo(
            OsDescription: "Ubuntu 24.04.1 LTS",
            Architecture: "X64",
            CpuModel: "AMD Ryzen 5 5600G with Radeon Graphics",
            CpuLogicalCores: 12,
            CpuPhysicalCores: 6,
            GpuModel: "AMD Radeon(TM) Graphics (Radeon Vega 7)",
            GpuDriver: "Mesa 24.2",
            HasDiscreteGpu: false,
            RamTotalGb: 16.0,
            RamChannels: "dual",
            StorageType: "NVMe SSD",
            TargetResolution: "1920x1080",
            RendererBackend: "Compatibility",
            VsyncEnabled: false,
            ReferenceHardwareMatched: BenchmarkHardwareProbe.EvaluateReferenceHardwareMatch(
                cpuPhysicalCores: 6,
                cpuLogicalCores: 12,
                ramTotalGb: 16.0,
                ramChannels: "dual",
                gpuModel: "AMD Radeon(TM) Graphics (Radeon Vega 7)",
                gpuDriver: "Mesa 24.2",
                hasDiscreteGpu: false,
                headless: false),
            ReferenceTargetSummary: BenchmarkHardwareProbe.ReferenceSpecSummary);
        Assert.True(vega7Hardware.ReferenceHardwareMatched);

        var thresholds = BenchmarkAcceptanceThresholds.Default;
        var okFailures = thresholds.EvaluateFailures(62.0, 18.0, 29.0, 900.0, 128.0, 4.0, false, 0);
        Assert.Empty(okFailures);

        var badFailures = thresholds.EvaluateFailures(48.0, 24.0, 41.0, 2300.0, 300.0, 45.0, true, 2);
        Assert.Equal(7, badFailures.Count);
    }

    [Fact]
    public void DiagnosticsNamespaceNeverReferencesTruthSampleOrEvaluationReference()
    {
        var types = typeof(BenchmarkRunner).Assembly.GetTypes()
            .Where(t => string.Equals(t.Namespace, "Gsa.Core.Diagnostics", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(types);
        foreach (var type in types)
        {
            foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                var sig = member.ToString() ?? "";
                Assert.DoesNotContain("TruthSample", sig, StringComparison.Ordinal);
                Assert.DoesNotContain("EvaluationReference", sig, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void CameraCaptureSchedulerFlushReleasesBufferedSamplesWhilePreservingTotals()
    {
        var scheduler = new CameraCaptureScheduler("cam_flush_test", CameraSensorProfile.Initial);
        var pos = new Vector3d(500020, 7400001, 101);
        var yawNorth = Quaterniond.FromAxisAngle(Vector3d.UnitZ, Math.PI / 2.0);
        var pose = new EstimatedBodyPose(0.0, pos, yawNorth);
        var cam = CameraGeometry.CameraToMap(pose, CameraMountEstimate.ForwardFacing);

        for (var i = 0; i < 4; i++)
        {
            var tick = i * 30L;
            var req = new CameraCaptureRequest(tick, i * 0.5, cam, cam, cam, pose,
                CameraMountEstimate.ForwardFacing, CameraEnvironmentSnapshot.DryDay, [], (_, _, _) => false);
            Assert.True(scheduler.TryEnqueueSimulatedCapture(req, null, out _, out _, autoDrain: true));
        }

        Assert.Equal(4, scheduler.SampleCount);
        Assert.Equal(4, scheduler.TotalCapturedCount);

        var flushed = scheduler.FlushBatchManifest("batch_01");
        Assert.Equal(4, flushed.Samples.Count);
        Assert.Equal(0, scheduler.SampleCount);
        Assert.Equal(4, scheduler.TotalCapturedCount);

        var nextReq = new CameraCaptureRequest(120, 2.0, cam, cam, cam, pose,
            CameraMountEstimate.ForwardFacing, CameraEnvironmentSnapshot.DryDay, [], (_, _, _) => false);
        Assert.True(scheduler.TryEnqueueSimulatedCapture(nextReq, null, out _, out _, autoDrain: true));
        Assert.Equal(1, scheduler.SampleCount);
        Assert.Equal(5, scheduler.TotalCapturedCount);
    }
}
