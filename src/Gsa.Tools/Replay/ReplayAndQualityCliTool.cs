using System.Text.Json;
using Gsa.Missions.Content;
using Gsa.Missions.Persistence;

namespace Gsa.Tools.Replay;

/// <summary>Implements `replay-core` and `quality-report` CLI subcommands with SHA-256 report verification.</summary>
public static class ReplayAndQualityCliTool
{
    public const string CurrentBuildVersion = "gsa-mvp-0.3.0";
    public const string CurrentModelVersion = "gsa-physics-v1";
    public const string DefaultOldCenterMapSha256 = "old-center-mvp-v1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private static readonly HashSet<string> SupportedProfiles = new(StringComparer.Ordinal)
    {
        "cam_initial",
        "cam_helmet",
        "cam_scooter_front",
        "cam_scooter_rear",
        "camera-initial",
        "camera-bicycle-helmet",
        "camera-scooter-front",
        "camera-scooter-rear",
        "gnss-initial",
        "gnss-nav",
        "dgnss-base",
    };

    public static int RunReplayCore(string[] args, TextWriter output)
    {
        if (args.Length != 3 || args[0] != "replay-core")
        {
            WriteError(output, "", "$command", "Usage: gsa-tools replay-core <replay-json> <output-report-json>");
            return 1;
        }

        var replayPath = Path.GetFullPath(args[1]);
        var outputPath = Path.GetFullPath(args[2]);
        if (!File.Exists(replayPath))
        {
            WriteError(output, replayPath, "$replay", "Replay package file does not exist.");
            return 2;
        }

        ReplayPackage? package;
        try
        {
            package = JsonSerializer.Deserialize<ReplayPackage>(File.ReadAllText(replayPath));
            if (package is null)
                throw new JsonException("Replay JSON deserialized to null.");
        }
        catch (Exception ex) when (ex is JsonException or IOException or ArgumentException)
        {
            WriteError(output, replayPath, "$replay", ex.Message);
            return 2;
        }

        var result = ScientificReplayEngine.ExecuteScientificReplay(
            package,
            CurrentBuildVersion,
            CurrentModelVersion,
            package.MapSha256,
            SupportedProfiles);

        var outDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outDir))
            Directory.CreateDirectory(outDir);

        if (result.Succeeded)
            File.WriteAllText(outputPath, result.ReplayedReportJson + Environment.NewLine);

        output.WriteLine(JsonSerializer.Serialize(new
        {
            valid = result.Succeeded && result.MetricsMatched,
            metrics_matched = result.MetricsMatched,
            observation_count = result.ScientificObservationCount,
            recorded_report_sha256 = result.RecordedReportSha256,
            replayed_report_sha256 = result.ReplayedReportSha256,
            diagnostic_reason_key = result.DiagnosticReasonKey,
            output_report = outputPath,
            diagnostics = result.Succeeded ? Array.Empty<ContentDiagnostic>() : [new ContentDiagnostic(replayPath, "$replay", result.DiagnosticReasonKey ?? "replay.failed")],
        }, JsonOptions));

        return result.Succeeded && result.MetricsMatched ? 0 : 2;
    }

    public static int RunQualityReport(string[] args, TextWriter output)
    {
        if (args.Length != 4 || args[0] != "quality-report")
        {
            WriteError(output, "", "$command", "Usage: gsa-tools quality-report <report-a-json> <report-b-json> <output-comparison-json>");
            return 1;
        }

        var pathA = Path.GetFullPath(args[1]);
        var pathB = Path.GetFullPath(args[2]);
        var outPath = Path.GetFullPath(args[3]);
        if (!File.Exists(pathA) || !File.Exists(pathB))
        {
            WriteError(output, !File.Exists(pathA) ? pathA : pathB, "$report", "Input quality report file does not exist.");
            return 2;
        }

        QualityReportComparisonResult comparison;
        try
        {
            comparison = QualityReportComparer.CompareJson(File.ReadAllText(pathA), File.ReadAllText(pathB));
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or FormatException or IOException)
        {
            WriteError(output, pathA, "$report", ex.Message);
            return 2;
        }

        var outDir = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(outDir))
            Directory.CreateDirectory(outDir);

        File.WriteAllText(outPath, JsonSerializer.Serialize(comparison, JsonOptions) + Environment.NewLine);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            valid = comparison.Equivalent,
            equivalent = comparison.Equivalent,
            first_report_sha256 = comparison.FirstReportSha256,
            second_report_sha256 = comparison.SecondReportSha256,
            metrics_compared = comparison.MetricDeltas.Count,
            diagnostic_reason_key = comparison.DiagnosticReasonKey,
            output_comparison = outPath,
            diagnostics = comparison.Equivalent ? Array.Empty<ContentDiagnostic>() : [new ContentDiagnostic(outPath, "$comparison", comparison.DiagnosticReasonKey ?? "replay.metric_mismatch")],
        }, JsonOptions));

        return comparison.Equivalent ? 0 : 2;
    }

    private static void WriteError(TextWriter output, string path, string field, string reason) =>
        output.WriteLine(JsonSerializer.Serialize(new
        {
            valid = false,
            diagnostics = new[] { new ContentDiagnostic(path, field, reason) },
        }, JsonOptions));
}

