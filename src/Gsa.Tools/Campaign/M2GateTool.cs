using System.Text.Json;
using Gsa.Missions.Campaign;
using Gsa.Missions.Content;
using Gsa.Missions.Diagnostics;

namespace Gsa.Tools.Campaign;

public static class M2GateTool
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public static int Run(string[] args, TextWriter output)
    {
        if (args.Length != 5 || args[0] != "run-m2-gate")
        {
            WriteError(output, "", "$command",
                "Usage: gsa-tools run-m2-gate <content-dir> <world-manifest> <tutorial-output-json> <gate-output-json>");
            return 1;
        }

        var contentDir = Path.GetFullPath(args[1]);
        var worldPath = Path.GetFullPath(args[2]);
        var tutorialOutPath = Path.GetFullPath(args[3]);
        var gateOutPath = Path.GetFullPath(args[4]);

        if (!Directory.Exists(contentDir))
        {
            WriteError(output, contentDir, "$content", "Content directory does not exist.");
            return 2;
        }

        if (!File.Exists(worldPath))
        {
            WriteError(output, worldPath, "$world", "World manifest file does not exist.");
            return 2;
        }

        var contentDiagnostics = ContentDirectoryValidator.Validate(contentDir);
        if (contentDiagnostics.Count > 0)
        {
            output.WriteLine(JsonSerializer.Serialize(new
            {
                valid = false,
                diagnostics = contentDiagnostics,
            }, JsonOptions));
            return 2;
        }

        TutorialPlaythroughReport tutorialReport;
        M2GateReport gateReport;
        try
        {
            tutorialReport = TutorialMissionRunner.RunHist01Playthrough(contentDir, worldPath);
            gateReport = M2PlaytestAndGateValidator.BuildM2GateReport(tutorialReport);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException or InvalidOperationException or IOException)
        {
            WriteError(output, contentDir, "$m2_gate", ex.Message);
            return 2;
        }

        WriteJsonFile(tutorialOutPath, tutorialReport);
        WriteJsonFile(gateOutPath, gateReport);

        var technicalPassed = string.Equals(gateReport.TechnicalLoopStatus, "passed", StringComparison.Ordinal);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            valid = technicalPassed,
            mission_id = tutorialReport.MissionId,
            contract_id = tutorialReport.ContractId,
            final_stars = tutorialReport.FinalStars,
            technical_loop_status = gateReport.TechnicalLoopStatus,
            playtest_gate_status = gateReport.PlaytestGateStatus,
            overall_gate_status = gateReport.OverallGateStatus,
            tasks_linked = gateReport.Tasks.Count,
            tutorial_output = tutorialOutPath,
            gate_output = gateOutPath,
            diagnostics = Array.Empty<ContentDiagnostic>(),
        }, JsonOptions));

        return technicalPassed ? 0 : 2;
    }

    private static void WriteJsonFile<T>(string path, T value)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions) + Environment.NewLine);
    }

    private static void WriteError(TextWriter output, string path, string field, string reason) =>
        output.WriteLine(JsonSerializer.Serialize(new
        {
            valid = false,
            diagnostics = new[] { new ContentDiagnostic(path, field, reason) },
        }, JsonOptions));
}

