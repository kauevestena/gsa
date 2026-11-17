using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Gsa.Core.Geometry;
using Gsa.Core.Sensors.Lidar;

namespace Gsa.Tools.Benchmark;

/// <summary>Repeatable CPU/allocation microbenchmarks; never a GPU or frame-rate acceptance gate.</summary>
public static class LidarPerformanceTool
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private sealed record WorkloadResult(object Payload, int Count, int ChunkReloads = 0);
    private sealed record Measurement(double ElapsedMs, long AllocatedBytes, int Count, int ChunkReloads, string Sha256);

    public static int Run(string[] args, TextWriter output)
    {
        var runs = 5;
        if (args.Length is < 2 or > 3 || args[0] != "run-lidar-performance" ||
            (args.Length == 3 && (!args[2].StartsWith("--runs=", StringComparison.Ordinal) ||
                !int.TryParse(args[2][7..], out runs) || runs is < 1 or > 20)))
        {
            output.WriteLine("Usage: gsa-tools run-lidar-performance <output-json> [--runs=1..20]");
            return 1;
        }

        // Two spatially overlapping 65,536-point chunks; the cache holds just one.
        // A narrow query crosses many octree leaves and alternates source chunks.
        var count = PointCloudOctreeAndChunkStore.MaxSamplesPerChunk * 2;
        var samples = Enumerable.Range(0, count).Select(i => new GeoreferencedLidarSample(
            i + 1, i * 0.0001,
            new Vector3d(500000 + i % 256, 7400000 + (i % 65536) / 256, 100 + (i % 5) * 0.05),
            0.8f, 1, false)).ToArray();
        var store = new PointCloudOctreeAndChunkStore(
            ramBudgetBytes: PointCloudOctreeAndChunkStore.MaxSamplesPerChunk * PointCloudOctreeAndChunkStore.SampleRecordByteSize);
        store.IngestRevision(samples);
        var residentStore = new PointCloudOctreeAndChunkStore();
        residentStore.IngestRevision(samples);
        WorkloadResult QueryCloud(PointCloudOctreeAndChunkStore source, bool fullCloud = false)
        {
            var before = source.GetMetrics().BackingReloadCount;
            var found = source.QueryScientificSamples(new(500000, 7400000, 99),
                new(fullCloud ? 500255 : 500000, 7400255, 101));
            return new(found, found.Count, source.GetMetrics().BackingReloadCount - before);
        }

        // A closed hall gives all scan directions real box intersections, with seeded
        // incidence/range noise and rain. Setup, hashing and JSON are outside timers.
        var caster = LidarBenchSimulator.CreateAnalyticBoxRayCaster(new LidarAnalyticBox[]
        {
            new("hall", "concrete", new(500000, 7400000, 102), new(30, 30, 10)),
        });
        WorkloadResult AcquireSwath()
        {
            var acquisition = new KombiLidarProfilerAcquisition(LidarProfile.L6, seed: 88421UL);
            var rays = acquisition.SimulateContinuousSwath(0, 10,
                t => new RigidTransform(Quaterniond.Identity, new(500000 + t, 7400000, 101)),
                caster, isRaining: true);
            return new(rays, rays.Count);
        }

        var scenarios = new Dictionary<string, Measurement[]>
        {
            ["overlapping_chunk_query"] = Measure(() => QueryCloud(store), runs),
            ["resident_chunk_query"] = Measure(() => QueryCloud(residentStore), runs),
            ["resident_full_cloud_query"] = Measure(() => QueryCloud(residentStore, fullCloud: true), runs),
            ["continuous_l6_rain_swath"] = Measure(AcquireSwath, runs),
        };
        var deterministic = scenarios.Values.All(measurements =>
            measurements.All(m => m.Count > 0 && m.Sha256 == measurements[0].Sha256));
        var report = new
        {
            benchmark_id = "LIDAR-CPU-V1",
            runtime = RuntimeInformation.FrameworkDescription,
            os = RuntimeInformation.OSDescription,
            processor_count = Environment.ProcessorCount,
            jit_optimization_disabled = typeof(LidarBenchSimulator).Assembly
                .GetCustomAttributes(typeof(DebuggableAttribute), false).OfType<DebuggableAttribute>()
                .Any(a => a.IsJITOptimizerDisabled),
            warmup_runs = 1,
            measured_runs = runs,
            scientific_results_deterministic = deterministic,
            scenarios,
        };
        var path = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(report, JsonOptions) + Environment.NewLine);
        output.WriteLine(JsonSerializer.Serialize(new { valid = deterministic, output = path }, JsonOptions));
        return deterministic ? 0 : 2;
    }

    private static Measurement[] Measure(Func<WorkloadResult> action, int runs)
    {
        _ = action();
        var measurements = new Measurement[runs];
        for (var i = 0; i < runs; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var started = Stopwatch.GetTimestamp();
            var result = action();
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(result.Payload)));
            measurements[i] = new(elapsed, allocated, result.Count, result.ChunkReloads, hash);
        }
        return measurements;
    }
}
