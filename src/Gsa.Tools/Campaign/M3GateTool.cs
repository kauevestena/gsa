using System.Text.Json;
using Gsa.Missions.Content;
using Gsa.Missions.Diagnostics;

namespace Gsa.Tools.Campaign;

/// <summary>Executes M3 MVP campaign, 500-seed procedural validation, replay invariance, bilingual audit, and dual-cohort gate report.</summary>
public static class M3GateTool
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public static int Run(string[] args, TextWriter output)
    {
        if (args.Length != 7 || args[0] != "run-m3-gate")
        {
            WriteError(output, "", "$command",
                "Usage: gsa-tools run-m3-gate <content-dir> <world-manifest> <en-us-po> <pt-br-po> <glossary-csv> <gate-output-json>");
            return 1;
        }

        var contentDir = Path.GetFullPath(args[1]);
        var worldPath = Path.GetFullPath(args[2]);
        var enPoPath = Path.GetFullPath(args[3]);
        var ptPoPath = Path.GetFullPath(args[4]);
        var glossaryPath = Path.GetFullPath(args[5]);
        var gateOutPath = Path.GetFullPath(args[6]);

        var diagnostics = ContentDirectoryValidator.Validate(contentDir);
        if (diagnostics.Count > 0)
        {
            output.WriteLine(JsonSerializer.Serialize(new { valid = false, diagnostics }, JsonOptions));
            return 2;
        }

        M3GateReport report;
        try
        {
            report = M3PlaytestAndGateValidator.BuildM3GateReport(contentDir, worldPath, enPoPath, ptPoPath, glossaryPath);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException or InvalidOperationException or IOException)
        {
            WriteError(output, contentDir, "$m3_gate", ex.Message);
            return 2;
        }

        var dir = Path.GetDirectoryName(gateOutPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(gateOutPath, JsonSerializer.Serialize(report, JsonOptions) + Environment.NewLine);

        var technicalPassed = string.Equals(report.TechnicalLoopStatus, "passed", StringComparison.Ordinal);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            valid = technicalPassed,
            milestone = report.Milestone,
            technical_loop_status = report.TechnicalLoopStatus,
            playtest_gate_status = report.PlaytestGateStatus,
            overall_gate_status = report.OverallGateStatus,
            missions_passed = report.CampaignPlaythrough.Missions.Count(m => m.Passed),
            procedural_seeds_passed = report.ProceduralValidation.TotalContractsPassed,
            calm_low_balance_available = report.CalmContractAlwaysAvailableAtLowBalance,
            replay_fps_invariant = report.ReplayFpsInvarianceVerified,
            bilingual_audit_passed = report.BilingualAuditPassed,
            collectibles_idempotent = report.CollectiblesIdempotentVerified,
            tasks_linked = report.Tasks.Count,
            gate_output = gateOutPath,
            diagnostics = Array.Empty<ContentDiagnostic>(),
        }, JsonOptions));

        return technicalPassed ? 0 : 2;
    }

    private static void WriteError(TextWriter output, string path, string field, string reason) =>
        output.WriteLine(JsonSerializer.Serialize(new
        {
            valid = false,
            diagnostics = new[] { new ContentDiagnostic(path, field, reason) },
        }, JsonOptions));
}

