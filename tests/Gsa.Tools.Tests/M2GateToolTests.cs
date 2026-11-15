using System.Text.Json;
using Gsa.Tools;
using Xunit;

namespace Gsa.Tools.Tests;

public sealed class M2GateToolTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Gsa.Domain.slnf")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    [Fact]
    public void RunM2Gate_WritesTutorialAndGateReports_AndReturnsZero()
    {
        var root = FindRepoRoot();
        var contentDir = Path.Combine(root, "content");
        var worldPath = Path.Combine(root, "game", "world", "tutorial-block.json");
        var tempDir = Path.Combine(Path.GetTempPath(), $"gsa-m2-gate-test-{Guid.NewGuid():N}");
        var tutorialOut = Path.Combine(tempDir, "tutorial-report.json");
        var gateOut = Path.Combine(tempDir, "m2-gate-report.json");

        try
        {
            using var stdout = new StringWriter();
            var exitCode = ToolApplication.Run(
                ["run-m2-gate", contentDir, worldPath, tutorialOut, gateOut],
                stdout);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(tutorialOut));
            Assert.True(File.Exists(gateOut));

            using var summaryDoc = JsonDocument.Parse(stdout.ToString());
            Assert.True(summaryDoc.RootElement.GetProperty("valid").GetBoolean());
            Assert.Equal("HIST-01", summaryDoc.RootElement.GetProperty("mission_id").GetString());
            Assert.Equal("contract_hist_01", summaryDoc.RootElement.GetProperty("contract_id").GetString());
            Assert.Equal(5, summaryDoc.RootElement.GetProperty("final_stars").GetInt32());
            Assert.Equal("passed", summaryDoc.RootElement.GetProperty("technical_loop_status").GetString());
            Assert.Equal("pending_human_playtest_cohort", summaryDoc.RootElement.GetProperty("playtest_gate_status").GetString());
            Assert.Equal(15, summaryDoc.RootElement.GetProperty("tasks_linked").GetInt32());

            using var tutorialDoc = JsonDocument.Parse(File.ReadAllText(tutorialOut));
            Assert.True(tutorialDoc.RootElement.GetProperty("final_report_passed").GetBoolean());
            Assert.Equal(5, tutorialDoc.RootElement.GetProperty("final_stars").GetInt32());
            Assert.False(tutorialDoc.RootElement.GetProperty("requires_future_sensors").GetBoolean());

            using var gateDoc = JsonDocument.Parse(File.ReadAllText(gateOut));
            Assert.Equal("M2", gateDoc.RootElement.GetProperty("milestone").GetString());
            Assert.Equal(15, gateDoc.RootElement.GetProperty("tasks").GetArrayLength());
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void RunM2Gate_InvalidArgsOrMissingFiles_ReturnsExpectedExitCodes()
    {
        var root = FindRepoRoot();
        var contentDir = Path.Combine(root, "content");
        var worldPath = Path.Combine(root, "game", "world", "tutorial-block.json");
        var tempDir = Path.Combine(Path.GetTempPath(), $"gsa-m2-gate-err-{Guid.NewGuid():N}");

        try
        {
            using var badArgsOut = new StringWriter();
            Assert.Equal(1, ToolApplication.Run(["run-m2-gate", contentDir], badArgsOut));

            using var missingContentOut = new StringWriter();
            Assert.Equal(2, ToolApplication.Run(
                ["run-m2-gate", Path.Combine(tempDir, "missing-content"), worldPath, Path.Combine(tempDir, "t.json"), Path.Combine(tempDir, "g.json")],
                missingContentOut));

            using var missingWorldOut = new StringWriter();
            Assert.Equal(2, ToolApplication.Run(
                ["run-m2-gate", contentDir, Path.Combine(tempDir, "missing-world.json"), Path.Combine(tempDir, "t.json"), Path.Combine(tempDir, "g.json")],
                missingWorldOut));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}

