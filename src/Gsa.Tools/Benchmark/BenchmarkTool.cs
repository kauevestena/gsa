using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Gsa.Core.Art;
using Gsa.Core.Diagnostics;
using Gsa.Core.World;
using Gsa.Missions.Content;

namespace Gsa.Tools.Benchmark;

public static class BenchmarkTool
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public static int Run(string[] args, TextWriter output)
    {
        if (args.Length < 4 || args[0] != "run-m1-benchmark")
        {
            WriteError(output, "", "$command",
                "Usage: gsa-tools run-m1-benchmark <world-manifest> <pilot-kit-dir> <output-json> [--warmup=30] [--duration=300] [--runs=3]");
            return 1;
        }

        var manifestPath = Path.GetFullPath(args[1]);
        var kitPath = Path.GetFullPath(args[2]);
        var outputPath = Path.GetFullPath(args[3]);

        double warmupSeconds = 30.0;
        double durationSeconds = 300.0;
        int runsPerPreset = 3;

        for (var i = 4; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith("--warmup=", StringComparison.Ordinal) &&
                double.TryParse(arg["--warmup=".Length..], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedWarmup))
            {
                warmupSeconds = parsedWarmup;
            }
            else if (arg.StartsWith("--duration=", StringComparison.Ordinal) &&
                     double.TryParse(arg["--duration=".Length..], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedDuration))
            {
                durationSeconds = parsedDuration;
            }
            else if (arg.StartsWith("--runs=", StringComparison.Ordinal) &&
                     int.TryParse(arg["--runs=".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedRuns))
            {
                runsPerPreset = parsedRuns;
            }
            else
            {
                WriteError(output, "", "$command", $"Unsupported option '{arg}'.");
                return 1;
            }
        }

        if (!File.Exists(manifestPath))
        {
            WriteError(output, manifestPath, "$manifest", "World manifest file does not exist.");
            return 2;
        }

        var artSummary = PilotArtKitValidator.ValidateDirectory(kitPath);
        if (!artSummary.Valid)
        {
            var first = artSummary.Diagnostics.FirstOrDefault();
            WriteError(output, first?.Path ?? kitPath, first?.Field ?? "$", first?.Reason ?? "Invalid pilot art kit.");
            return 2;
        }

        WorldSceneManifest manifest;
        try
        {
            manifest = WorldSceneManifest.Parse(File.ReadAllText(manifestPath));
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException)
        {
            WriteError(output, manifestPath, "$manifest", ex.Message);
            return 2;
        }

        var (godotVersion, gitCommit) = ProbeRepositoryMetadata(Path.GetDirectoryName(manifestPath));
        var options = new BenchmarkProtocolOptions(
            WarmupSeconds: warmupSeconds,
            DurationSeconds: durationSeconds,
            RunsPerPreset: runsPerPreset,
            BuildConfiguration: DetectBuildConfiguration(),
            RendererBackend: "Compatibility",
            VsyncEnabled: false,
            TargetResolution: "1920x1080",
            GodotVersion: godotVersion,
            GitCommit: gitCommit,
            HeadlessGpuUnavailable: true);

        var report = BenchmarkRunner.RunM1Benchmark(manifest, artSummary, options);

        var outDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outDir))
            Directory.CreateDirectory(outDir);
        File.WriteAllText(outputPath, JsonSerializer.Serialize(report, JsonOptions) + Environment.NewLine);

        output.WriteLine(JsonSerializer.Serialize(new
        {
            valid = report.GateStatus != "failed",
            benchmark_id = report.BenchmarkId,
            gate_status = report.GateStatus,
            reference_hardware_matched = report.Hardware.ReferenceHardwareMatched,
            gate_reasons = report.GateReasons,
            runs_executed = report.Runs.Count,
            block_assembly_ms = report.BlockAssembly.AssemblyMs,
            output = outputPath,
            diagnostics = Array.Empty<ContentDiagnostic>(),
        }, JsonOptions));

        return report.GateStatus == "failed" ? 2 : 0;
    }

    private static string DetectBuildConfiguration()
    {
#if DEBUG
        return "Debug";
#else
        return "Release";
#endif
    }

    private static (string GodotVersion, string GitCommit) ProbeRepositoryMetadata(string? startDirectory)
    {
        var godotVersion = "unknown";
        var gitCommit = "unknown";

        var dir = string.IsNullOrEmpty(startDirectory) ? null : new DirectoryInfo(startDirectory);
        while (dir is not null)
        {
            var csproj = Path.Combine(dir.FullName, "game", "Gsa.csproj");
            if (godotVersion == "unknown" && File.Exists(csproj))
            {
                try
                {
                    var sdk = XDocument.Load(csproj).Root?.Attribute("Sdk")?.Value;
                    if (!string.IsNullOrWhiteSpace(sdk) && sdk.Contains('/'))
                        godotVersion = sdk.Split('/')[1];
                }
                catch
                {
                    // Keep unknown if csproj is unreadable.
                }
            }

            var gitHead = Path.Combine(dir.FullName, ".git", "HEAD");
            if (gitCommit == "unknown" && File.Exists(gitHead))
            {
                try
                {
                    var headText = File.ReadAllText(gitHead).Trim();
                    if (headText.StartsWith("ref: ", StringComparison.Ordinal))
                    {
                        var refRel = headText["ref: ".Length..].Trim();
                        var refPath = Path.Combine(dir.FullName, ".git", refRel.Replace('/', Path.DirectorySeparatorChar));
                        if (File.Exists(refPath))
                            gitCommit = File.ReadAllText(refPath).Trim();
                    }
                    else
                    {
                        gitCommit = headText;
                    }
                }
                catch
                {
                    // Keep unknown if .git/HEAD is locked.
                }
            }

            if (godotVersion != "unknown" && gitCommit != "unknown")
                break;
            dir = dir.Parent;
        }

        return (godotVersion, gitCommit);
    }

    private static void WriteError(TextWriter output, string path, string field, string reason)
    {
        var diagnostics = new[] { new ContentDiagnostic(path, field, reason) };
        output.WriteLine(JsonSerializer.Serialize(new { valid = false, diagnostics }, JsonOptions));
    }
}

