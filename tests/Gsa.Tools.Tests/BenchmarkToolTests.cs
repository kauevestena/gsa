using System.Text.Json;
using Gsa.Tools;
using Xunit;

namespace Gsa.Tools.Tests;

public sealed class BenchmarkToolTests
{
    [Theory]
    [InlineData("--runs=0")]
    [InlineData("--runs=21")]
    [InlineData("--runs=invalid")]
    [InlineData("--unknown")]
    public void LidarPerformanceRejectsInvalidRunCounts(string option)
    {
        using var output = new StringWriter();
        Assert.Equal(1, ToolApplication.Run(["run-lidar-performance", "unused.json", option], output));
    }

    [Fact]
    public void LidarPerformanceReportsMeasuredAllocationsAndStableScientificPayloads()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gsa-lidar-perf-{Guid.NewGuid():N}.json");
        try
        {
            using var output = new StringWriter();
            Assert.Equal(0, ToolApplication.Run(["run-lidar-performance", path, "--runs=2"], output));
            using var report = JsonDocument.Parse(File.ReadAllText(path));
            var root = report.RootElement;
            Assert.True(root.GetProperty("scientific_results_deterministic").GetBoolean());
            Assert.Equal(2, root.GetProperty("measured_runs").GetInt32());
            foreach (var scenario in root.GetProperty("scenarios").EnumerateObject())
            {
                var measurements = scenario.Value.EnumerateArray().ToArray();
                Assert.Equal(2, measurements.Length);
                Assert.Equal(measurements[0].GetProperty("sha256").GetString(), measurements[1].GetProperty("sha256").GetString());
                Assert.All(measurements, m => Assert.True(m.GetProperty("allocated_bytes").GetInt64() > 0));
            }
            var query = root.GetProperty("scenarios").GetProperty("overlapping_chunk_query")[0];
            Assert.Equal(512, query.GetProperty("count").GetInt32());
            Assert.InRange(query.GetProperty("chunk_reloads").GetInt32(), 0, 2);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Gsa.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    [Fact]
    public void RunM1BenchmarkCliGeneratesReportJsonAndRejectsMissingManifest()
    {
        var root = FindRepoRoot();
        var manifestPath = Path.Combine(root, "game", "world", "tutorial-block.json");
        var kitDir = Path.Combine(root, "game", "world", "pilot-kit");
        var outFile = Path.Combine(Path.GetTempPath(), $"gsa-m1-bench-{Guid.NewGuid():N}.json");

        try
        {
            using var okOut = new StringWriter();
            var exitOk = ToolApplication.Run(
                ["run-m1-benchmark", manifestPath, kitDir, outFile, "--warmup=1", "--duration=3", "--runs=3"],
                okOut);
            Assert.Equal(0, exitOk);
            Assert.True(File.Exists(outFile));

            using var summaryDoc = JsonDocument.Parse(okOut.ToString());
            Assert.True(summaryDoc.RootElement.GetProperty("valid").GetBoolean());
            Assert.Equal("pending_reference_hardware", summaryDoc.RootElement.GetProperty("gate_status").GetString());
            Assert.Equal(6, summaryDoc.RootElement.GetProperty("runs_executed").GetInt32());

            using var reportDoc = JsonDocument.Parse(File.ReadAllText(outFile));
            Assert.Equal("M1-BENCH-V1", reportDoc.RootElement.GetProperty("benchmark_id").GetString());
            Assert.Equal(20, reportDoc.RootElement.GetProperty("block_assembly").GetProperty("facade_count").GetInt32());
            Assert.Equal(6, reportDoc.RootElement.GetProperty("runs").GetArrayLength());

            using var errOut = new StringWriter();
            var missingManifest = Path.Combine(Path.GetTempPath(), $"gsa-missing-{Guid.NewGuid():N}.json");
            var exitErr = ToolApplication.Run(["run-m1-benchmark", missingManifest, kitDir, outFile], errOut);
            Assert.Equal(2, exitErr);
        }
        finally
        {
            if (File.Exists(outFile))
                File.Delete(outFile);
        }
    }
}

