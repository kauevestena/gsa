using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gsa.Core.Geometry;
using Gsa.Missions.Contracts;
using Gsa.Missions.Economy;
using Gsa.Missions.Office;
using Gsa.Missions.Quality;

namespace Gsa.Missions.Persistence;

public sealed record VisualPoseKeyframe(
    double TimeSeconds,
    Vector3d PositionEnu,
    Quaterniond Attitude,
    double SpeedMetresPerSecond);

public sealed record ReplayEventRecord(
    double TimeSeconds,
    string EventKind,
    string Payload);

public sealed record ReplaySurveySurfaceDto(
    string SurfaceId,
    Bounds2DEnu Bounds,
    double ElevationEnuZ)
{
    public SurveySurfaceReference ToDomain() => new(SurfaceId, Bounds, ElevationEnuZ);

    public static ReplaySurveySurfaceDto FromDomain(SurveySurfaceReference domain) =>
        new(domain.SurfaceId, domain.Bounds, domain.ElevationEnuZ);
}

public sealed record ReplayEvaluationReferenceDto(
    string ContractId,
    IReadOnlyList<ReplaySurveySurfaceDto> Surfaces,
    IReadOnlyList<string> RequiredFeatureIds,
    IReadOnlyList<ControlCheckpointReference> Checkpoints,
    IReadOnlyList<RelativeCheckpointPairReference> RelativePairs,
    IReadOnlyDictionary<string, Vector3d> FeatureLocationsById)
{
    public EvaluationReference ToDomain() => new(
        ContractId,
        Surfaces.Select(s => s.ToDomain()),
        RequiredFeatureIds,
        Checkpoints,
        RelativePairs,
        FeatureLocationsById);

    public static ReplayEvaluationReferenceDto FromDomain(EvaluationReference domain) => new(
        domain.ContractId,
        domain.SurfacesById.Values.OrderBy(s => s.SurfaceId, StringComparer.Ordinal).Select(ReplaySurveySurfaceDto.FromDomain).ToArray(),
        domain.RequiredFeatureIds,
        domain.CheckpointsById.Values.OrderBy(c => c.CheckpointId, StringComparer.Ordinal).ToArray(),
        domain.RelativePairs,
        domain.FeatureLocationsById);
}

public sealed record ReplayPackage(
    int SchemaVersion,
    string BuildVersion,
    string ModelVersion,
    string MapSha256,
    IReadOnlyList<string> SensorProfileIds,
    ulong RunSeed,
    string ContractJson,
    SurveyBatchArtifact RecordedBatch,
    ProcessingRecipe Recipe,
    ReplayEvaluationReferenceDto EvaluationReference,
    IReadOnlyList<VisualPoseKeyframe> VisualKeyframes,
    IReadOnlyList<ReplayEventRecord> Events,
    string RecordedReportJson,
    string RecordedReportSha256);

public sealed record DidacticTruthOverlay(
    string ContractId,
    bool ReplayModeOnly,
    IReadOnlyDictionary<string, Vector3d> FeatureTruthEnu,
    IReadOnlyList<ControlCheckpointReference> CheckpointTruthEnu);

public sealed record VisualReplaySamplingResult(
    double RenderFps,
    int VisualFrameCount,
    int ScientificObservationCount,
    IReadOnlyList<VisualPoseKeyframe> InterpolatedFrames,
    string ReplayedReportSha256);

public sealed record ScientificReplayResult(
    bool Succeeded,
    bool MetricsMatched,
    int ScientificObservationCount,
    string RecordedReportSha256,
    string ReplayedReportSha256,
    string ReplayedReportJson,
    QualityReport? ReplayedReport,
    string? DiagnosticReasonKey = null);

public sealed record QualityMetricDelta(
    string Metric,
    string Kind,
    string FirstStatus,
    string SecondStatus,
    double? FirstValue,
    double? SecondValue,
    double? AbsoluteDifference,
    bool WithinTolerance);

public sealed record QualityReportComparisonResult(
    bool Equivalent,
    string FirstReportSha256,
    string SecondReportSha256,
    bool FirstPassed,
    bool SecondPassed,
    IReadOnlyList<QualityMetricDelta> MetricDeltas,
    string? DiagnosticReasonKey = null);

/// <summary>Executes scientific CQ replay, visual FPS-invariant interpolation, didactic truth overlay, and report comparison.</summary>
public static class ScientificReplayEngine
{
    public const int CurrentSchemaVersion = 1;

    public static string ComputeCanonicalSha256(string jsonOrPayload)
    {
        ArgumentNullException.ThrowIfNull(jsonOrPayload);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(jsonOrPayload))).ToLowerInvariant();
    }

    public static DidacticTruthOverlay BuildDidacticTruthOverlay(ReplayPackage package, bool isReplayMode)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (!isReplayMode)
            throw new InvalidOperationException("Didactic truth overlay is strictly forbidden during live collection; replay mode required.");

        var reference = package.EvaluationReference.ToDomain();
        return new DidacticTruthOverlay(
            reference.ContractId,
            ReplayModeOnly: true,
            FeatureTruthEnu: reference.FeatureLocationsById,
            CheckpointTruthEnu: reference.CheckpointsById.Values.OrderBy(c => c.CheckpointId, StringComparer.Ordinal).ToArray());
    }

    public static ScientificReplayResult ExecuteScientificReplay(
        ReplayPackage package,
        string expectedBuildVersion,
        string expectedModelVersion,
        string expectedMapSha256,
        ISet<string> supportedSensorProfileIds)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedBuildVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedModelVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedMapSha256);
        ArgumentNullException.ThrowIfNull(supportedSensorProfileIds);

        if (package.SchemaVersion != CurrentSchemaVersion)
        {
            return new ScientificReplayResult(
                false, false, package.RecordedBatch.Samples.Count,
                package.RecordedReportSha256, "", "", null, "replay.incompatible_version");
        }

        if (!string.Equals(package.ModelVersion, expectedModelVersion, StringComparison.Ordinal) ||
            !string.Equals(package.MapSha256, expectedMapSha256, StringComparison.Ordinal) ||
            package.SensorProfileIds.Any(id => !supportedSensorProfileIds.Contains(id)))
        {
            return new ScientificReplayResult(
                false, false, package.RecordedBatch.Samples.Count,
                package.RecordedReportSha256, "", "", null, "replay.incompatible_map_or_profile");
        }

        var definition = ContractDefinition.Parse(package.ContractJson, "replay_contract.json");
        var reference = package.EvaluationReference.ToDomain();

        var contract = new ContractStateMachine(definition);
        if (!contract.Accept(0.0).Succeeded ||
            !contract.RecordPendingBatch(Math.Max(1.0, package.RecordedBatch.EndTimeSeconds), package.RecordedBatch.BatchId).Succeeded)
        {
            return new ScientificReplayResult(
                false, false, package.RecordedBatch.Samples.Count,
                package.RecordedReportSha256, "", "", null, "replay.invalid_contract_transition");
        }

        var custody = new BatchCustodyStore();
        custody.RegisterBufferedBatch(package.RecordedBatch, Math.Max(1.0, package.RecordedBatch.EndTimeSeconds));
        custody.OffloadAtomic(package.RecordedBatch.BatchId, Math.Max(2.0, package.RecordedBatch.EndTimeSeconds + 1.0));
        contract.ProtectBatchAtUnloadPoint(Math.Max(2.0, package.RecordedBatch.EndTimeSeconds + 1.0), package.RecordedBatch.BatchId);

        var ledger = new CareerLedger(Math.Max(10000, package.Recipe.ServiceFeeCoords + 100));
        var queue = new OfficeProcessingQueue();
        var recipe = package.Recipe with { CalendarDurationSeconds = 0.0 };
        var processRes = queue.StartOrResumeJob("replay_job", contract, custody, ledger, recipe, Math.Max(3.0, package.RecordedBatch.EndTimeSeconds + 2.0));
        if (!processRes.Succeeded || processRes.Product is null)
        {
            return new ScientificReplayResult(
                false, false, package.RecordedBatch.Samples.Count,
                package.RecordedReportSha256, "", "", null, processRes.ReasonKey ?? "replay.processing_failed");
        }

        var replayedReport = QualityEvaluator.Evaluate(definition, reference, processRes.Product.Submission);
        var replayedJson = replayedReport.ToJson();
        var replayedSha = ComputeCanonicalSha256(replayedJson);

        var comparison = QualityReportComparer.CompareJson(package.RecordedReportJson, replayedJson);
        return new ScientificReplayResult(
            Succeeded: true,
            MetricsMatched: comparison.Equivalent,
            ScientificObservationCount: package.RecordedBatch.Samples.Count,
            RecordedReportSha256: package.RecordedReportSha256,
            ReplayedReportSha256: replayedSha,
            ReplayedReportJson: replayedJson,
            ReplayedReport: replayedReport,
            DiagnosticReasonKey: comparison.Equivalent ? null : comparison.DiagnosticReasonKey);
    }

    public static VisualReplaySamplingResult SampleVisualTrajectoryAtFps(
        ReplayPackage package,
        double renderFps,
        string expectedBuildVersion,
        string expectedModelVersion,
        string expectedMapSha256,
        ISet<string> supportedSensorProfileIds)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (!double.IsFinite(renderFps) || renderFps <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(renderFps));

        var sci = ExecuteScientificReplay(package, expectedBuildVersion, expectedModelVersion, expectedMapSha256, supportedSensorProfileIds);
        if (!sci.Succeeded)
            throw new InvalidOperationException($"Scientific replay failed ({sci.DiagnosticReasonKey}).");

        var frames = new List<VisualPoseKeyframe>();
        if (package.VisualKeyframes.Count == 1)
        {
            frames.Add(package.VisualKeyframes[0]);
        }
        else if (package.VisualKeyframes.Count >= 2)
        {
            var ordered = package.VisualKeyframes.OrderBy(k => k.TimeSeconds).ToArray();
            var t0 = ordered[0].TimeSeconds;
            var tEnd = ordered[^1].TimeSeconds;
            var dt = 1.0 / renderFps;
            var stepCount = Math.Max(1, (int)Math.Floor((tEnd - t0) * renderFps));

            var seg = 0;
            for (var s = 0; s <= stepCount; s++)
            {
                var t = Math.Min(tEnd, t0 + s * dt);
                while (seg + 1 < ordered.Length - 1 && ordered[seg + 1].TimeSeconds < t)
                    seg++;

                var a = ordered[seg];
                var b = ordered[seg + 1];
                var span = b.TimeSeconds - a.TimeSeconds;
                var alpha = span > 1e-12 ? Math.Clamp((t - a.TimeSeconds) / span, 0.0, 1.0) : 0.0;
                var pos = new Vector3d(
                    a.PositionEnu.X + (b.PositionEnu.X - a.PositionEnu.X) * alpha,
                    a.PositionEnu.Y + (b.PositionEnu.Y - a.PositionEnu.Y) * alpha,
                    a.PositionEnu.Z + (b.PositionEnu.Z - a.PositionEnu.Z) * alpha);
                var att = SlerpQuaternion(a.Attitude, b.Attitude, alpha);
                var spd = a.SpeedMetresPerSecond + (b.SpeedMetresPerSecond - a.SpeedMetresPerSecond) * alpha;
                frames.Add(new VisualPoseKeyframe(t, pos, att, spd));
            }
        }

        return new VisualReplaySamplingResult(
            renderFps,
            frames.Count,
            sci.ScientificObservationCount,
            Array.AsReadOnly(frames.ToArray()),
            sci.ReplayedReportSha256);
    }

    private static Quaterniond SlerpQuaternion(Quaterniond a, Quaterniond b, double t)
    {
        var dot = a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
        var (bx, by, bz, bw) = dot < 0.0
            ? (-b.X, -b.Y, -b.Z, -b.W)
            : (b.X, b.Y, b.Z, b.W);
        dot = Math.Clamp(Math.Abs(dot), 0.0, 1.0);
        if (dot > 0.9995)
        {
            return new Quaterniond(
                a.X + (bx - a.X) * t,
                a.Y + (by - a.Y) * t,
                a.Z + (bz - a.Z) * t,
                a.W + (bw - a.W) * t);
        }

        var theta0 = Math.Acos(dot);
        var theta = theta0 * t;
        var sinTheta0 = Math.Sin(theta0);
        var s0 = Math.Cos(theta) - dot * Math.Sin(theta) / sinTheta0;
        var s1 = Math.Sin(theta) / sinTheta0;
        return new Quaterniond(
            s0 * a.X + s1 * bx,
            s0 * a.Y + s1 * by,
            s0 * a.Z + s1 * bz,
            s0 * a.W + s1 * bw);
    }
}

public static class QualityReportComparer
{
    public static QualityReportComparisonResult Compare(
        QualityReport first,
        QualityReport second,
        double absoluteTolerance = 1e-6,
        double relativeTolerance = 1e-8)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        return CompareJson(first.ToJson(), second.ToJson(), absoluteTolerance, relativeTolerance);
    }

    public static QualityReportComparisonResult CompareJson(
        string firstReportJson,
        string secondReportJson,
        double absoluteTolerance = 1e-6,
        double relativeTolerance = 1e-8)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(firstReportJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(secondReportJson);

        using var docA = JsonDocument.Parse(firstReportJson);
        using var docB = JsonDocument.Parse(secondReportJson);
        var rootA = docA.RootElement;
        var rootB = docB.RootElement;

        var shaA = ScientificReplayEngine.ComputeCanonicalSha256(firstReportJson);
        var shaB = ScientificReplayEngine.ComputeCanonicalSha256(secondReportJson);
        var passedA = rootA.GetProperty("passed").GetBoolean();
        var passedB = rootB.GetProperty("passed").GetBoolean();

        var metricsA = ReadMetricMap(rootA.GetProperty("metrics"));
        var metricsB = ReadMetricMap(rootB.GetProperty("metrics"));
        var allMetricNames = metricsA.Keys.Union(metricsB.Keys, StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToArray();

        var deltas = new List<QualityMetricDelta>(allMetricNames.Length);
        var allMatch = passedA == passedB &&
                       string.Equals(rootA.GetProperty("contract_id").GetString(), rootB.GetProperty("contract_id").GetString(), StringComparison.Ordinal);

        foreach (var name in allMetricNames)
        {
            metricsA.TryGetValue(name, out var mA);
            metricsB.TryGetValue(name, out var mB);
            if (mA is null || mB is null)
            {
                var present = mA ?? mB!;
                allMatch = false;
                deltas.Add(new QualityMetricDelta(name, present.Kind, mA?.Status ?? "missing", mB?.Status ?? "missing", mA?.Value, mB?.Value, null, false));
                continue;
            }

            double? absDiff = null;
            bool within;
            if (mA.Value.HasValue && mB.Value.HasValue)
            {
                absDiff = Math.Abs(mA.Value.Value - mB.Value.Value);
                var limit = Math.Max(absoluteTolerance, relativeTolerance * Math.Max(Math.Abs(mA.Value.Value), Math.Abs(mB.Value.Value)));
                within = string.Equals(mA.Status, mB.Status, StringComparison.Ordinal) && absDiff.Value <= limit;
            }
            else
            {
                within = string.Equals(mA.Status, mB.Status, StringComparison.Ordinal) &&
                         mA.BooleanValue == mB.BooleanValue &&
                         !mA.Value.HasValue && !mB.Value.HasValue;
            }

            if (!within)
                allMatch = false;

            deltas.Add(new QualityMetricDelta(name, mA.Kind, mA.Status, mB.Status, mA.Value, mB.Value, absDiff, within));
        }

        return new QualityReportComparisonResult(
            Equivalent: allMatch,
            FirstReportSha256: shaA,
            SecondReportSha256: shaB,
            FirstPassed: passedA,
            SecondPassed: passedB,
            MetricDeltas: Array.AsReadOnly(deltas.ToArray()),
            DiagnosticReasonKey: allMatch ? null : "replay.metric_mismatch");
    }

    private sealed record ParsedMetric(string Kind, string Status, double? Value, bool? BooleanValue);

    private static Dictionary<string, ParsedMetric> ReadMetricMap(JsonElement metricsArray)
    {
        var map = new Dictionary<string, ParsedMetric>(StringComparer.Ordinal);
        foreach (var el in metricsArray.EnumerateArray())
        {
            var metric = el.GetProperty("metric").GetString() ?? "";
            var kind = el.GetProperty("kind").GetString() ?? "";
            var status = el.GetProperty("status").GetString() ?? "";
            double? val = el.TryGetProperty("value", out var vEl) && vEl.ValueKind == JsonValueKind.Number ? vEl.GetDouble() : null;
            bool? bVal = el.TryGetProperty("boolean_value", out var bEl) && bEl.ValueKind is JsonValueKind.True or JsonValueKind.False ? bEl.GetBoolean() : null;
            map[metric] = new ParsedMetric(kind, status, val, bVal);
        }
        return map;
    }
}
