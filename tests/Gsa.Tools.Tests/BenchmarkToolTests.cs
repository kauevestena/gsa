using System.Text.Json;
using Gsa.Tools;
using Xunit;

namespace Gsa.Tools.Tests;

public sealed class BenchmarkToolTests
{
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

