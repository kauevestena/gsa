using System.Text.Json;
using System.Text.Json.Serialization;
using Gsa.Core.Geometry;
using Gsa.Core.Sensors.Camera;
using Gsa.Missions.Contracts;

namespace Gsa.Missions.Quality;

public readonly record struct Bounds2DEnu
{
    public double MinEast { get; }
    public double MinNorth { get; }
    public double MaxEast { get; }
    public double MaxNorth { get; }

    public double Width => MaxEast - MinEast;
    public double Height => MaxNorth - MinNorth;
    public double Area => Width * Height;

    [JsonConstructor]
    public Bounds2DEnu(double minEast, double minNorth, double maxEast, double maxNorth)
    {
        if (!double.IsFinite(minEast) || !double.IsFinite(minNorth) ||
            !double.IsFinite(maxEast) || !double.IsFinite(maxNorth))
            throw new ArgumentOutOfRangeException(nameof(minEast), "Bounds coordinates must be finite.");
        if (maxEast <= minEast || maxNorth <= minNorth)
            throw new ArgumentException("Bounds max coordinates must be strictly greater than min coordinates.");

        MinEast = minEast;
        MinNorth = minNorth;
        MaxEast = maxEast;
        MaxNorth = maxNorth;
    }

    public Vector3d CenterEnu(double elevationEnuZ = 100.0) =>
        new((MinEast + MaxEast) * 0.5, (MinNorth + MaxNorth) * 0.5, elevationEnuZ);

    public double IntersectionArea(Bounds2DEnu other)
    {
        var dx = Math.Min(MaxEast, other.MaxEast) - Math.Max(MinEast, other.MinEast);
        var dy = Math.Min(MaxNorth, other.MaxNorth) - Math.Max(MinNorth, other.MinNorth);
        return dx > 0.0 && dy > 0.0 ? dx * dy : 0.0;
    }

    public bool TryIntersect(Bounds2DEnu other, out Bounds2DEnu intersection)
    {
        var minE = Math.Max(MinEast, other.MinEast);
        var minN = Math.Max(MinNorth, other.MinNorth);
        var maxE = Math.Min(MaxEast, other.MaxEast);
        var maxN = Math.Min(MaxNorth, other.MaxNorth);
        if (maxE > minE && maxN > minN)
        {
            intersection = new Bounds2DEnu(minE, minN, maxE, maxN);
            return true;
        }
        intersection = default;
        return false;
    }
}

public sealed record ClippedSurfaceCell(
    string SurfaceId,
    int EastIndex,
    int NorthIndex,
    Bounds2DEnu ClippedBounds,
    double UsefulAreaM2);

public sealed record SurveySurfaceReference
{
    public string SurfaceId { get; }
    public Bounds2DEnu Bounds { get; }
    public double ElevationEnuZ { get; }
    public IReadOnlyList<Bounds2DEnu> ExclusionZones { get; }

    public SurveySurfaceReference(
        string surfaceId,
        Bounds2DEnu bounds,
        double elevationEnuZ = 100.0,
        IEnumerable<Bounds2DEnu>? exclusionZones = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(surfaceId);
        if (!double.IsFinite(elevationEnuZ))
            throw new ArgumentOutOfRangeException(nameof(elevationEnuZ), "Surface elevation must be finite.");

        SurfaceId = surfaceId;
        Bounds = bounds;
        ElevationEnuZ = elevationEnuZ;
        ExclusionZones = exclusionZones is null ? [] : Array.AsReadOnly(exclusionZones.ToArray());
    }

    public IReadOnlyList<ClippedSurfaceCell> RasterizeUsefulCells(double cellSizeMetres)
    {
        if (!double.IsFinite(cellSizeMetres) || cellSizeMetres <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(cellSizeMetres), "Cell size must be finite and positive.");

        var eastCount = (int)Math.Ceiling(Bounds.Width / cellSizeMetres);
        var northCount = (int)Math.Ceiling(Bounds.Height / cellSizeMetres);
        var cells = new List<ClippedSurfaceCell>();

        for (var iy = 0; iy < northCount; iy++)
        {
            var y0 = Bounds.MinNorth + iy * cellSizeMetres;
            var y1 = Math.Min(Bounds.MaxNorth, y0 + cellSizeMetres);
            if (y1 <= y0)
                continue;

            for (var ix = 0; ix < eastCount; ix++)
            {
                var x0 = Bounds.MinEast + ix * cellSizeMetres;
                var x1 = Math.Min(Bounds.MaxEast, x0 + cellSizeMetres);
                if (x1 <= x0)
                    continue;

                var clipped = new Bounds2DEnu(x0, y0, x1, y1);
                var usefulArea = ComputeUsefulAreaInsideCell(clipped);
                if (usefulArea > 1e-9)
                    cells.Add(new ClippedSurfaceCell(SurfaceId, ix, iy, clipped, usefulArea));
            }
        }

        if (cells.Count == 0)
            throw new ArgumentException($"Surface '{SurfaceId}' has zero useful area after clipping and exclusions.");

        return Array.AsReadOnly(cells.ToArray());
    }

    private double ComputeUsefulAreaInsideCell(Bounds2DEnu cell)
    {
        if (ExclusionZones.Count == 0)
            return cell.Area;

        var overlapping = new List<Bounds2DEnu>();
        foreach (var exclusion in ExclusionZones)
        {
            if (cell.TryIntersect(exclusion, out var overlap))
                overlapping.Add(overlap);
        }

        if (overlapping.Count == 0)
            return cell.Area;

        var xs = new SortedSet<double> { cell.MinEast, cell.MaxEast };
        var ys = new SortedSet<double> { cell.MinNorth, cell.MaxNorth };
        foreach (var rect in overlapping)
        {
            xs.Add(rect.MinEast);
            xs.Add(rect.MaxEast);
            ys.Add(rect.MinNorth);
            ys.Add(rect.MaxNorth);
        }

        var xList = xs.ToArray();
        var yList = ys.ToArray();
        var excludedArea = 0.0;

        for (var i = 0; i < xList.Length - 1; i++)
        {
            var cx = (xList[i] + xList[i + 1]) * 0.5;
            var dx = xList[i + 1] - xList[i];
            for (var j = 0; j < yList.Length - 1; j++)
            {
                var cy = (yList[j] + yList[j + 1]) * 0.5;
                var dy = yList[j + 1] - yList[j];
                if (overlapping.Any(r => cx >= r.MinEast && cx <= r.MaxEast && cy >= r.MinNorth && cy <= r.MaxNorth))
                    excludedArea += dx * dy;
            }
        }

        return Math.Max(0.0, cell.Area - excludedArea);
    }
}

public sealed record ControlCheckpointReference(string CheckpointId, Vector3d TruePositionEnu)
{
    public ControlCheckpointReference AssertValid()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(CheckpointId);
        if (!double.IsFinite(TruePositionEnu.X) || !double.IsFinite(TruePositionEnu.Y) || !double.IsFinite(TruePositionEnu.Z))
            throw new ArgumentOutOfRangeException(nameof(TruePositionEnu), "Checkpoint true ENU position must be finite.");
        return this;
    }
}

public enum RelativeCheckpointMeasure { Distance, HorizontalDistance, HeightDifference }

public sealed record RelativeCheckpointPairReference(string FirstCheckpointId, string SecondCheckpointId,
    RelativeCheckpointMeasure Measure = RelativeCheckpointMeasure.Distance)
{
    public double Evaluate(Vector3d difference) => Measure switch
    {
        RelativeCheckpointMeasure.HorizontalDistance => Math.Sqrt(difference.X * difference.X + difference.Y * difference.Y),
        RelativeCheckpointMeasure.HeightDifference => difference.Z,
        _ => difference.Length,
    };
}

/// <summary>Authoritative mission-evaluation truth kept strictly isolated in Gsa.Missions.Quality.</summary>
public sealed class EvaluationReference
{
    public string ContractId { get; }
    public IReadOnlyDictionary<string, SurveySurfaceReference> SurfacesById { get; }
    public IReadOnlyList<string> RequiredFeatureIds { get; }
    public IReadOnlyDictionary<string, Vector3d> FeatureLocationsById { get; }
    public IReadOnlyDictionary<string, ControlCheckpointReference> CheckpointsById { get; }
    public IReadOnlyList<RelativeCheckpointPairReference> RelativePairs { get; }

    public EvaluationReference(
        string contractId,
        IEnumerable<SurveySurfaceReference> surfaces,
        IEnumerable<string> requiredFeatureIds,
        IEnumerable<ControlCheckpointReference>? checkpoints = null,
        IEnumerable<RelativeCheckpointPairReference>? relativePairs = null,
        IReadOnlyDictionary<string, Vector3d>? featureLocationsById = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractId);
        ArgumentNullException.ThrowIfNull(surfaces);
        ArgumentNullException.ThrowIfNull(requiredFeatureIds);

        ContractId = contractId;
        var surfaceMap = new Dictionary<string, SurveySurfaceReference>(StringComparer.Ordinal);
        foreach (var surface in surfaces)
        {
            ArgumentNullException.ThrowIfNull(surface);
            if (!surfaceMap.TryAdd(surface.SurfaceId, surface))
                throw new ArgumentException($"Duplicate surface '{surface.SurfaceId}'.", nameof(surfaces));
        }
        SurfacesById = surfaceMap;

        var uniqueFeatures = requiredFeatureIds.Distinct(StringComparer.Ordinal).ToArray();
        RequiredFeatureIds = Array.AsReadOnly(uniqueFeatures);

        var cpMap = new Dictionary<string, ControlCheckpointReference>(StringComparer.Ordinal);
        if (checkpoints is not null)
        {
            foreach (var cp in checkpoints)
            {
                ArgumentNullException.ThrowIfNull(cp);
                cp.AssertValid();
                if (!cpMap.TryAdd(cp.CheckpointId, cp))
                    throw new ArgumentException($"Duplicate checkpoint '{cp.CheckpointId}'.", nameof(checkpoints));
            }
        }
        CheckpointsById = cpMap;

        RelativePairs = relativePairs is null ? [] : Array.AsReadOnly(relativePairs.ToArray());
        FeatureLocationsById = featureLocationsById is null
            ? new Dictionary<string, Vector3d>(StringComparer.Ordinal)
            : new Dictionary<string, Vector3d>(featureLocationsById, StringComparer.Ordinal);
    }
}

public sealed record SampleFootprintObservation(
    string SampleId,
    string SurfaceId,
    Bounds2DEnu CoveredFootprint);

public sealed record SubmittedCheckpointEstimate(
    string CheckpointId,
    Vector3d EstimatedPositionEnu);

public sealed record QualitySubmission(
    string ContractId,
    IReadOnlyList<string> BatchIds,
    string DeliveryFormat,
    int SchemaVersion,
    bool FullTexturePersisted,
    IReadOnlyList<CameraObservationSample> Samples,
    IReadOnlyList<SampleFootprintObservation> SampleFootprints,
    IReadOnlyList<TriangulatedFeatureEntry> Features,
    IReadOnlyList<SubmittedCheckpointEstimate> Checkpoints,
    bool OverlapOk = true,
    IReadOnlyList<string>? CorruptSampleErrors = null);

public enum MetricEvaluationStatus
{
    Passed = 0,
    Failed = 1,
    InsufficientData = 2,
}

public sealed record MetricEvaluationResult(
    string Metric,
    string Kind,
    MetricEvaluationStatus Status,
    double? Value,
    bool? BooleanValue,
    double? Limit,
    double? Excellent,
    bool IsBinaryRequirement,
    int SampleCount,
    string? FailureReasonKey);

public sealed record RecollectionArea(
    string AreaId,
    string ReasonKey,
    Vector3d CenterEnu,
    Vector3d ExtentsEnu,
    IReadOnlyList<string> MissingFeatureIds);

public sealed class QualityReport
{
    public int SchemaVersion => 1;
    public string ContractId { get; }
    public IReadOnlyList<string> BatchIds { get; }
    public bool Passed { get; }
    public IReadOnlyList<MetricEvaluationResult> Metrics { get; }
    public IReadOnlyList<string> FailureReasonKeys { get; }
    public IReadOnlyList<RecollectionArea> RecollectionAreas { get; }
    public IReadOnlyList<string> PassedDeliverableIds { get; }
    public int UniqueSampleCount { get; }
    public int ValidSampleCount { get; }
    public int MatchedFeatureCount { get; }
    public int MatchedCheckpointCount { get; }

    public QualityReport(
        string contractId,
        IReadOnlyList<string> batchIds,
        bool passed,
        IReadOnlyList<MetricEvaluationResult> metrics,
        IReadOnlyList<string> failureReasonKeys,
        IReadOnlyList<RecollectionArea> recollectionAreas,
        IReadOnlyList<string> passedDeliverableIds,
        int uniqueSampleCount,
        int validSampleCount,
        int matchedFeatureCount,
        int matchedCheckpointCount)
    {
        ContractId = contractId;
        BatchIds = batchIds;
        Passed = passed;
        Metrics = metrics;
        FailureReasonKeys = failureReasonKeys;
        RecollectionAreas = recollectionAreas;
        PassedDeliverableIds = passedDeliverableIds;
        UniqueSampleCount = uniqueSampleCount;
        ValidSampleCount = validSampleCount;
        MatchedFeatureCount = matchedFeatureCount;
        MatchedCheckpointCount = matchedCheckpointCount;
    }

    public MetricEvaluationResult? GetMetric(string metricName) =>
        Metrics.FirstOrDefault(m => string.Equals(m.Metric, metricName, StringComparison.Ordinal));

    public string ToJson()
    {
        var payload = new
        {
            schema_version = SchemaVersion,
            contract_id = ContractId,
            batch_ids = BatchIds,
            passed = Passed,
            metrics = Metrics.Select(m => new
            {
                metric = m.Metric,
                kind = m.Kind,
                status = ToSnakeStatus(m.Status),
                value = m.Value,
                boolean_value = m.BooleanValue,
                limit = m.Limit,
                excellent = m.Excellent,
                is_binary_requirement = m.IsBinaryRequirement,
                sample_count = m.SampleCount,
                failure_reason_key = m.FailureReasonKey,
            }),
            failure_reason_keys = FailureReasonKeys,
            recollection_areas = RecollectionAreas.Select(a => new
            {
                area_id = a.AreaId,
                reason_key = a.ReasonKey,
                center_enu = new[] { a.CenterEnu.X, a.CenterEnu.Y, a.CenterEnu.Z },
                extents_enu = new[] { a.ExtentsEnu.X, a.ExtentsEnu.Y, a.ExtentsEnu.Z },
                missing_feature_ids = a.MissingFeatureIds,
            }),
            passed_deliverable_ids = PassedDeliverableIds,
            unique_sample_count = UniqueSampleCount,
            valid_sample_count = ValidSampleCount,
            matched_feature_count = MatchedFeatureCount,
            matched_checkpoint_count = MatchedCheckpointCount,
        };
        return JsonSerializer.Serialize(payload);
    }

    private static string ToSnakeStatus(MetricEvaluationStatus status) => status switch
    {
        MetricEvaluationStatus.Passed => "passed",
        MetricEvaluationStatus.Failed => "failed",
        MetricEvaluationStatus.InsufficientData => "insufficient_data",
        _ => "failed",
    };
}

/// <summary>Evaluates images and feature-inventory deliverables against clipped grids and reference truth.</summary>
public static class QualityEvaluator
{
    public const int MinimumAbsoluteCheckpoints = 3;
    public const double DefaultCellSizeMetres = 2.0;

    public static QualityReport Evaluate(
        ContractDefinition contract,
        EvaluationReference reference,
        QualitySubmission submission)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(submission);
        if (!string.Equals(contract.Id, reference.ContractId, StringComparison.Ordinal))
            throw new ArgumentException("EvaluationReference contract ID does not match contract definition.");

        var uniqueSamples = DeduplicateSamples(submission.Samples, out var uniqueSampleLookup);
        var validSampleIds = new HashSet<string>(
            uniqueSamples.Where(s => s.Quality.Passed).Select(s => s.SampleId),
            StringComparer.Ordinal);

        var uniqueFootprintsBySurface = GroupUniqueValidFootprints(submission.SampleFootprints, validSampleIds);
        var resolvedFeatureIds = new HashSet<string>(
            submission.Features
                .Where(f => f.Confirmed && f.Status == TriangulationStatus.Resolved && f.EstimatedPositionEnu.HasValue)
                .Select(f => f.TargetId),
            StringComparer.Ordinal);

        // An image delivery can document an object without reconstructing its 3D position.
        // Inventory deliveries still need confirmed triangulation.
        if (contract.Product == "images")
        {
            resolvedFeatureIds.UnionWith(uniqueSamples
                .Where(s => s.Quality.Passed)
                .SelectMany(s => s.ObservedFeatures)
                .Select(f => f.TargetId));
        }

        var uniqueCheckpoints = DeduplicateCheckpoints(submission.Checkpoints);

        var topEval = EvaluateRequirementSet(
            contract.Requirements,
            reference.RequiredFeatureIds,
            contract,
            reference,
            submission,
            uniqueSamples,
            validSampleIds,
            uniqueFootprintsBySurface,
            resolvedFeatureIds,
            uniqueCheckpoints,
            collectRecollection: true);

        var passedDeliverables = new List<string>();
        if (contract.PartialPolicy.Mode == "deliverables")
        {
            foreach (var deliverable in contract.PartialPolicy.Deliverables)
            {
                var delEval = EvaluateRequirementSet(
                    deliverable.Requirements,
                    deliverable.RequiredFeatureIds,
                    contract,
                    reference,
                    submission,
                    uniqueSamples,
                    validSampleIds,
                    uniqueFootprintsBySurface,
                    resolvedFeatureIds,
                    uniqueCheckpoints,
                    collectRecollection: false);
                if (delEval.Passed)
                    passedDeliverables.Add(deliverable.Id);
            }
        }

        var matchedRequiredCount = reference.RequiredFeatureIds.Count(resolvedFeatureIds.Contains);
        var matchedCheckpointCount = reference.CheckpointsById.Keys.Count(uniqueCheckpoints.ContainsKey);

        return new QualityReport(
            contract.Id,
            submission.BatchIds,
            topEval.Passed,
            topEval.Metrics,
            topEval.FailureReasonKeys,
            topEval.RecollectionAreas,
            passedDeliverables,
            uniqueSamples.Count,
            validSampleIds.Count,
            matchedRequiredCount,
            matchedCheckpointCount);
    }

    private sealed record RequirementSetEvaluation(
        bool Passed,
        IReadOnlyList<MetricEvaluationResult> Metrics,
        IReadOnlyList<string> FailureReasonKeys,
        IReadOnlyList<RecollectionArea> RecollectionAreas);

    private static RequirementSetEvaluation EvaluateRequirementSet(
        IReadOnlyList<ContractRequirementSpec> requirements,
        IReadOnlyList<string> scopedFeatureIds,
        ContractDefinition contract,
        EvaluationReference reference,
        QualitySubmission submission,
        IReadOnlyList<CameraObservationSample> uniqueSamples,
        HashSet<string> validSampleIds,
        Dictionary<string, Dictionary<string, Bounds2DEnu>> uniqueFootprintsBySurface,
        HashSet<string> resolvedFeatureIds,
        Dictionary<string, Vector3d> uniqueCheckpoints,
        bool collectRecollection)
    {
        var results = new List<MetricEvaluationResult>(requirements.Count);
        var failures = new HashSet<string>(StringComparer.Ordinal);
        var recollections = new List<RecollectionArea>();
        var isEmptySubmission = uniqueSamples.Count == 0 && submission.Features.Count == 0 && submission.Checkpoints.Count == 0;

        if (isEmptySubmission)
            failures.Add("cq.failure.empty_submission");

        var missingScopedFeatures = scopedFeatureIds.Where(id => !resolvedFeatureIds.Contains(id)).ToArray();

        foreach (var req in requirements)
        {
            MetricEvaluationResult res;
            if (req is ThresholdRequirementSpec threshold)
            {
                res = EvaluateThreshold(
                    threshold,
                    scopedFeatureIds,
                    reference,
                    uniqueSamples.Count,
                    validSampleIds.Count,
                    uniqueFootprintsBySurface,
                    resolvedFeatureIds,
                    uniqueCheckpoints,
                    collectRecollection ? recollections : null);
            }
            else if (req is PredicateRequirementSpec predicate)
            {
                res = EvaluatePredicate(predicate, scopedFeatureIds, resolvedFeatureIds, contract, submission, uniqueSamples, isEmptySubmission);
            }
            else
            {
                continue;
            }

            results.Add(res);
            if (res.Status != MetricEvaluationStatus.Passed && res.FailureReasonKey is not null)
                failures.Add(res.FailureReasonKey);
        }

        if (collectRecollection && missingScopedFeatures.Length > 0 && !recollections.Any(a => a.MissingFeatureIds.Count > 0))
        {
            foreach (var missingId in missingScopedFeatures)
            {
                var center = reference.FeatureLocationsById.TryGetValue(missingId, out var known) ? known : Vector3d.Zero;
                recollections.Add(new RecollectionArea(
                    $"feature:{missingId}",
                    "cq.failure.missing_feature",
                    center,
                    new Vector3d(4.0, 4.0, 2.0),
                    [missingId]));
            }
        }

        var passed = !isEmptySubmission && results.All(r => r.Status == MetricEvaluationStatus.Passed);
        return new RequirementSetEvaluation(
            passed,
            results.ToArray(),
            failures.Order(StringComparer.Ordinal).ToArray(),
            recollections.ToArray());
    }

    private static MetricEvaluationResult EvaluateThreshold(
        ThresholdRequirementSpec spec,
        IReadOnlyList<string> scopedFeatureIds,
        EvaluationReference reference,
        int uniqueSampleCount,
        int validSampleCount,
        Dictionary<string, Dictionary<string, Bounds2DEnu>> footprintsBySurface,
        HashSet<string> resolvedFeatureIds,
        Dictionary<string, Vector3d> uniqueCheckpoints,
        List<RecollectionArea>? recollections)
    {
        switch (spec.Metric)
        {
            case "area_coverage":
            {
                var surfaceId = spec.SurfaceId ?? throw new InvalidOperationException("area_coverage requires surface_id.");
                if (!reference.SurfacesById.TryGetValue(surfaceId, out var surface))
                    throw new KeyNotFoundException($"EvaluationReference missing surface '{surfaceId}'.");

                var cellSize = spec.CellSizeMetres ?? DefaultCellSizeMetres;
                var cells = surface.RasterizeUsefulCells(cellSize);
                var totalUsefulArea = cells.Sum(c => c.UsefulAreaM2);
                footprintsBySurface.TryGetValue(surfaceId, out var surfaceFootprints);

                var coveredUsefulArea = 0.0;
                foreach (var cell in cells)
                {
                    var isCovered = surfaceFootprints is not null &&
                                    surfaceFootprints.Values.Any(fp => fp.IntersectionArea(cell.ClippedBounds) > 1e-9);
                    if (isCovered)
                    {
                        coveredUsefulArea += cell.UsefulAreaM2;
                    }
                    else if (recollections is not null)
                    {
                        recollections.Add(new RecollectionArea(
                            $"{surfaceId}:{cell.EastIndex}:{cell.NorthIndex}",
                            "cq.failure.uncovered_cell",
                            cell.ClippedBounds.CenterEnu(surface.ElevationEnuZ),
                            new Vector3d(cell.ClippedBounds.Width, cell.ClippedBounds.Height, 2.0),
                            []));
                    }
                }

                var ratio = Math.Clamp(coveredUsefulArea / totalUsefulArea, 0.0, 1.0);
                var pass = ratio + 1e-12 >= spec.Limit;
                return new MetricEvaluationResult(
                    spec.Metric,
                    spec.Kind,
                    pass ? MetricEvaluationStatus.Passed : MetricEvaluationStatus.Failed,
                    ratio,
                    null,
                    spec.Limit,
                    spec.Excellent,
                    spec.IsBinaryLimit,
                    cells.Count,
                    pass ? null : "cq.failure.area_coverage");
            }

            case "feature_completeness":
            {
                if (scopedFeatureIds.Count == 0)
                {
                    return new MetricEvaluationResult(
                        spec.Metric, spec.Kind, MetricEvaluationStatus.InsufficientData,
                        null, null, spec.Limit, spec.Excellent, spec.IsBinaryLimit, 0, "cq.failure.no_required_features");
                }

                var matched = scopedFeatureIds.Count(resolvedFeatureIds.Contains);
                var ratio = matched / (double)scopedFeatureIds.Count;
                var pass = ratio + 1e-12 >= spec.Limit;
                return new MetricEvaluationResult(
                    spec.Metric,
                    spec.Kind,
                    pass ? MetricEvaluationStatus.Passed : MetricEvaluationStatus.Failed,
                    ratio,
                    null,
                    spec.Limit,
                    spec.Excellent,
                    spec.IsBinaryLimit,
                    scopedFeatureIds.Count,
                    pass ? null : "cq.failure.feature_completeness");
            }

            case "valid_image_fraction":
            {
                var ratio = uniqueSampleCount == 0 ? 0.0 : validSampleCount / (double)uniqueSampleCount;
                var pass = uniqueSampleCount > 0 && ratio + 1e-12 >= spec.Limit;
                return new MetricEvaluationResult(
                    spec.Metric,
                    spec.Kind,
                    pass ? MetricEvaluationStatus.Passed : MetricEvaluationStatus.Failed,
                    ratio,
                    null,
                    spec.Limit,
                    spec.Excellent,
                    spec.IsBinaryLimit,
                    uniqueSampleCount,
                    pass ? null : "cq.failure.valid_image_fraction");
            }

            case "absolute_rmse":
            {
                var squaredErrors = new List<double>();
                foreach (var (cpId, cpRef) in reference.CheckpointsById)
                {
                    if (!uniqueCheckpoints.TryGetValue(cpId, out var est) || !IsFiniteVector(est))
                        continue;
                    var diff = est - cpRef.TruePositionEnu;
                    squaredErrors.Add(Vector3d.Dot(diff, diff));
                }

                if (squaredErrors.Count < MinimumAbsoluteCheckpoints)
                {
                    return new MetricEvaluationResult(
                        spec.Metric,
                        spec.Kind,
                        MetricEvaluationStatus.InsufficientData,
                        null,
                        null,
                        spec.Limit,
                        spec.Excellent,
                        false,
                        squaredErrors.Count,
                        "cq.failure.insufficient_checkpoints");
                }

                var rmse = Math.Sqrt(squaredErrors.Sum() / squaredErrors.Count);
                var pass = rmse <= spec.Limit + 1e-12;
                return new MetricEvaluationResult(
                    spec.Metric,
                    spec.Kind,
                    pass ? MetricEvaluationStatus.Passed : MetricEvaluationStatus.Failed,
                    rmse,
                    null,
                    spec.Limit,
                    spec.Excellent,
                    false,
                    squaredErrors.Count,
                    pass ? null : "cq.failure.absolute_rmse");
            }

            case "relative_rmse":
            {
                var squaredPairErrors = new List<double>();
                foreach (var pair in reference.RelativePairs)
                {
                    if (!reference.CheckpointsById.TryGetValue(pair.FirstCheckpointId, out var refA) ||
                        !reference.CheckpointsById.TryGetValue(pair.SecondCheckpointId, out var refB))
                        continue;
                    if (!uniqueCheckpoints.TryGetValue(pair.FirstCheckpointId, out var estA) ||
                        !uniqueCheckpoints.TryGetValue(pair.SecondCheckpointId, out var estB))
                        continue;
                    if (!IsFiniteVector(estA) || !IsFiniteVector(estB))
                        continue;

                    var trueDist = pair.Evaluate(refB.TruePositionEnu - refA.TruePositionEnu);
                    var estDist = pair.Evaluate(estB - estA);
                    var err = estDist - trueDist;
                    squaredPairErrors.Add(err * err);
                }

                if (squaredPairErrors.Count == 0)
                {
                    return new MetricEvaluationResult(
                        spec.Metric,
                        spec.Kind,
                        MetricEvaluationStatus.InsufficientData,
                        null,
                        null,
                        spec.Limit,
                        spec.Excellent,
                        false,
                        0,
                        "cq.failure.insufficient_checkpoints");
                }

                var relRmse = Math.Sqrt(squaredPairErrors.Sum() / squaredPairErrors.Count);
                var pass = relRmse <= spec.Limit + 1e-12;
                return new MetricEvaluationResult(
                    spec.Metric,
                    spec.Kind,
                    pass ? MetricEvaluationStatus.Passed : MetricEvaluationStatus.Failed,
                    relRmse,
                    null,
                    spec.Limit,
                    spec.Excellent,
                    false,
                    squaredPairErrors.Count,
                    pass ? null : "cq.failure.relative_rmse");
            }

            default:
                return new MetricEvaluationResult(
                    spec.Metric,
                    spec.Kind,
                    MetricEvaluationStatus.InsufficientData,
                    null,
                    null,
                    spec.Limit,
                    spec.Excellent,
                    spec.IsBinaryLimit,
                    0,
                    $"cq.failure.unsupported_metric.{spec.Metric}");
        }
    }

    private static MetricEvaluationResult EvaluatePredicate(
        PredicateRequirementSpec spec,
        IReadOnlyList<string> scopedFeatureIds,
        HashSet<string> resolvedFeatureIds,
        ContractDefinition contract,
        QualitySubmission submission,
        IReadOnlyList<CameraObservationSample> uniqueSamples,
        bool isEmptySubmission)
    {
        bool actual;
        string? failKey = $"cq.failure.{spec.Metric}";

        switch (spec.Metric)
        {
            case "privacy_redacted":
                actual = AreAllPrivacySensitiveRegionsRedacted(uniqueSamples);
                break;
            case "format_valid":
                actual = submission.SchemaVersion == 1 &&
                         !submission.FullTexturePersisted &&
                         uniqueSamples.All(s => !s.FullTexturePersisted && !s.Thumbnail.FullTexturePersisted) &&
                         string.Equals(submission.DeliveryFormat, contract.DeliveryFormat, StringComparison.Ordinal) &&
                         (submission.CorruptSampleErrors is null || submission.CorruptSampleErrors.Count == 0) &&
                         !isEmptySubmission;
                break;
            case "all_required_features":
                actual = scopedFeatureIds.Count > 0 && scopedFeatureIds.All(resolvedFeatureIds.Contains);
                break;
            case "overlap_ok":
                actual = submission.OverlapOk && !isEmptySubmission;
                break;
            default:
                actual = false;
                failKey = $"cq.failure.unsupported_metric.{spec.Metric}";
                break;
        }

        var pass = actual == spec.Expected;
        return new MetricEvaluationResult(
            spec.Metric,
            spec.Kind,
            pass ? MetricEvaluationStatus.Passed : MetricEvaluationStatus.Failed,
            actual ? 1.0 : 0.0,
            actual,
            null,
            null,
            true,
            uniqueSamples.Count,
            pass ? null : failKey);
    }

    private static bool AreAllPrivacySensitiveRegionsRedacted(IReadOnlyList<CameraObservationSample> samples)
    {
        foreach (var sample in samples)
        {
            var appliedMaskIds = new HashSet<string>(
                sample.PrivacyMasks.Where(m => m.Applied).Select(m => m.TargetId),
                StringComparer.Ordinal);

            if (sample.PrivacyMasks.Any(m => !m.Applied))
                return false;

            if (sample.ObservedFeatures.Any(f => f.RequiresPrivacyReview && !appliedMaskIds.Contains(f.TargetId)))
                return false;
        }
        return true;
    }

    private static IReadOnlyList<CameraObservationSample> DeduplicateSamples(
        IReadOnlyList<CameraObservationSample> samples,
        out Dictionary<string, CameraObservationSample> byId)
    {
        byId = new Dictionary<string, CameraObservationSample>(StringComparer.Ordinal);
        var list = new List<CameraObservationSample>(samples.Count);
        foreach (var sample in samples)
        {
            if (sample is null)
                continue;
            if (byId.TryAdd(sample.SampleId, sample))
                list.Add(sample);
        }
        return Array.AsReadOnly(list.ToArray());
    }

    private static Dictionary<string, Dictionary<string, Bounds2DEnu>> GroupUniqueValidFootprints(
        IReadOnlyList<SampleFootprintObservation> footprints,
        HashSet<string> validSampleIds)
    {
        var map = new Dictionary<string, Dictionary<string, Bounds2DEnu>>(StringComparer.Ordinal);
        foreach (var fp in footprints)
        {
            if (!validSampleIds.Contains(fp.SampleId))
                continue;
            if (!map.TryGetValue(fp.SurfaceId, out var bySample))
            {
                bySample = new Dictionary<string, Bounds2DEnu>(StringComparer.Ordinal);
                map[fp.SurfaceId] = bySample;
            }
            bySample.TryAdd(fp.SampleId, fp.CoveredFootprint);
        }
        return map;
    }

    private static Dictionary<string, Vector3d> DeduplicateCheckpoints(IReadOnlyList<SubmittedCheckpointEstimate> checkpoints)
    {
        var map = new Dictionary<string, Vector3d>(StringComparer.Ordinal);
        foreach (var cp in checkpoints)
        {
            if (!string.IsNullOrWhiteSpace(cp.CheckpointId) && IsFiniteVector(cp.EstimatedPositionEnu))
                map.TryAdd(cp.CheckpointId, cp.EstimatedPositionEnu);
        }
        return map;
    }

    private static bool IsFiniteVector(Vector3d vector) =>
        double.IsFinite(vector.X) && double.IsFinite(vector.Y) && double.IsFinite(vector.Z);
}
