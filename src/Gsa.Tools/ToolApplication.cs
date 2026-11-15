using System.Text.Json;
using Gsa.Missions.Content;

namespace Gsa.Tools;

public static class ToolApplication
{
    public static int Run(string[] args, TextWriter output)
    {
        if (args.Length > 0 && args[0] == "precompute-sky")
            return Sky.SkyTool.Run(args, output);
        if (args.Length > 0 && args[0] == "validate-art-kit")
            return Art.ArtKitTool.Run(args, output);
        if (args.Length > 0 && args[0] == "run-m1-benchmark")
            return Benchmark.BenchmarkTool.Run(args, output);
        if (args.Length > 0 && args[0] == "run-m2-gate")
            return Campaign.M2GateTool.Run(args, output);
        if (args.Length > 0 && args[0] == "run-m3-gate")
            return Campaign.M3GateTool.Run(args, output);
        if (args.Length > 0 && args[0] == "replay-core")
            return Replay.ReplayAndQualityCliTool.RunReplayCore(args, output);
        if (args.Length > 0 && args[0] == "quality-report")
            return Replay.ReplayAndQualityCliTool.RunQualityReport(args, output);

        if (args.Length != 2 || args[0] != "validate-content")
        {
            Write(output, false, [new ContentDiagnostic("", "$command",
                "Usage: gsa-tools validate-content <directory> | precompute-sky <scene-manifest> <output> | validate-art-kit <directory> | run-m1-benchmark <world-manifest> <pilot-kit-dir> <output-json> | run-m2-gate <content-dir> <world-manifest> <tutorial-output-json> <gate-output-json> | run-m3-gate <content-dir> <world-manifest> <en-us-po> <pt-br-po> <glossary-csv> <gate-output-json> | replay-core <replay-json> <output-report-json> | quality-report <report-a-json> <report-b-json> <output-comparison-json>")]);
            return 1;
        }

        var diagnostics = ContentDirectoryValidator.Validate(args[1]);
        Write(output, diagnostics.Count == 0, diagnostics);
        return diagnostics.Count == 0 ? 0 : 2;
    }

    private static void Write(TextWriter output, bool valid, IReadOnlyList<ContentDiagnostic> diagnostics) =>
        output.WriteLine(JsonSerializer.Serialize(new { valid, diagnostics },
            new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }));
}
