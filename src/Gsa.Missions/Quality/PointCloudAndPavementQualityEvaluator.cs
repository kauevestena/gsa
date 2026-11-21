using System.Security.Cryptography;
using System.Text;
using Gsa.Core.Geometry;
using Gsa.Core.Sensors.Lidar;
using Gsa.Missions.Contracts;

namespace Gsa.Missions.Quality;

/// <summary>3D axis-aligned ENU box identifying known physical obstacles or planned occlusion zones in a LiDAR survey.</summary>
public sealed record SurfaceOcclusionBox3D(
    string BoxId,
    double MinEast,
    double MinNorth,
    double MinUp,
    double MaxEast,
    double MaxNorth,
    double MaxUp)
{
    public bool OverlapsPlanar(Bounds2DEnu bounds) =>
        MaxEast > bounds.MinEast && MinEast < bounds.MaxEast &&
        MaxNorth > bounds.MinNorth && MinNorth < bounds.MaxNorth;
}

/// <summary>Reference plane in contract map coordinates used for point-to-plane LiDAR residual verification.</summary>
public sealed record ReferencePlaneQualitySpec(
    string PlaneId,
    Vector3d PointOnPlaneEnu,
    Vector3d UnitNormalEnu,
    Bounds2DEnu Bounds2D,
    double MinUpMetres,
    double MaxUpMetres,
    double? MaxRmseOverrideMetres = null)
{
    public bool ContainsPoint(Vector3d point) =>
        point.X >= Bounds2D.MinEast && point.X <= Bounds2D.MaxEast &&
        point.Y >= Bounds2D.MinNorth && point.Y <= Bounds2D.MaxNorth &&
        point.Z >= MinUpMetres && point.Z <= MaxUpMetres;
}

/// <summary>Contract specification for support-based LiDAR density, coverage, plane residuals, and relative checkpoints.</summary>
public sealed record PointCloudQualitySpec(
    string ContractId,
    double TargetCellDensityPointsPerM2,
    double MinCoverageFraction = 0.90,
    double MaxPlaneRmseMetres = 0.08,
    double MaxRelativeDistanceRmseMetres = 0.08,
    double CellSizeMetres = 2.0,
    IReadOnlyList<ReferencePlaneQualitySpec>? ReferencePlanes = null,
    IReadOnlyList<SurfaceOcclusionBox3D>? KnownOcclusionObstacles = null);

public sealed record CellDensityEvaluation(
    string SurfaceId,
    int EastIndex,
    int NorthIndex,
    ClippedSurfaceCell Cell,
    int UniquePointCount,
    double DensityPointsPerM2,
    bool IsCovered,
    bool OcclusionSuspected);

public sealed record PlaneResidualEvaluation(
    string PlaneId,
    int SampleCount,
    double PointToPlaneRmseMetres,
    double MaxAbsoluteResidualMetres,
    double AllowedRmseMetres,
    bool Passed);

public sealed record PointCloudQualityReport(
    string ContractId,
    bool Passed,
    int RawSubmittedPointCount,
    int UniquePointCount,
    int TotalUsefulCells,
    int CoveredUsefulCells,
    double CoverageFraction,
    double MeanDensityPointsPerM2,
    double MinCellDensityPointsPerM2,
    double? WorstPlaneRmseMetres,
    double? RelativeDistanceRmseMetres,
    IReadOnlyList<CellDensityEvaluation> Cells,
    IReadOnlyList<PlaneResidualEvaluation> Planes,
    IReadOnlyList<MetricEvaluationResult> Metrics,
    IReadOnlyList<string> FailureReasonKeys,
    IReadOnlyList<RecollectionArea> RecollectionAreas,
    string ScientificDigestSha256);

/// <summary>Evaluates georeferenced point clouds on clipped useful cells without visual LOD leakage or duplicate inflation.</summary>
public static class PointCloudQualityEvaluator
{
    public static PointCloudQualityReport Evaluate(
        PointCloudQualitySpec spec,
        IReadOnlyList<SurveySurfaceReference> usefulSurfaces,
        IReadOnlyList<GeoreferencedLidarSample> submittedSamples,
        EvaluationReference? checkpointReference = null,
        IReadOnlyList<SubmittedCheckpointEstimate>? submittedCheckpoints = null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(usefulSurfaces);
        ArgumentNullException.ThrowIfNull(submittedSamples);
        if (!double.IsFinite(spec.TargetCellDensityPointsPerM2) || spec.TargetCellDensityPointsPerM2 <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(spec));
        if (!double.IsFinite(spec.MinCoverageFraction) || spec.MinCoverageFraction is <= 0.0 or > 1.0)
            throw new ArgumentOutOfRangeException(nameof(spec));
        if (!double.IsFinite(spec.CellSizeMetres) || spec.CellSizeMetres <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(spec));
        if (usefulSurfaces.Count == 0)
            throw new ArgumentException("At least one useful surface is required.", nameof(usefulSurfaces));

        var uniqueSamples = DeduplicateScientificSamples(submittedSamples);
        var obstacles = spec.KnownOcclusionObstacles ?? Array.Empty<SurfaceOcclusionBox3D>();

        var cellEvaluations = new List<CellDensityEvaluation>();
        var failureReasons = new HashSet<string>(StringComparer.Ordinal);
        var recollectionAreas = new List<RecollectionArea>();
        var coveredCellCount = 0;
        var totalUsefulArea = 0.0;
        var totalPointsInUsefulCells = 0;
        var minCellDensity = double.PositiveInfinity;

        foreach (var surface in usefulSurfaces)
        {
            var surfaceCells = surface.RasterizeUsefulCells(spec.CellSizeMetres);
            var eligiblePoints = uniqueSamples
                .Where(s => IsInsideSurfaceUsefulFootprint(surface, s.MapPositionEnu))
                .ToArray();

            var maxEastIdx = surfaceCells.Max(c => c.EastIndex);
            var maxNorthIdx = surfaceCells.Max(c => c.NorthIndex);

            foreach (var cell in surfaceCells)
            {
                totalUsefulArea += cell.UsefulAreaM2;
                var cellCount = CountPointsInsideCell(eligiblePoints, cell, maxEastIdx, maxNorthIdx);
                totalPointsInUsefulCells += cellCount;

                var density = cellCount / cell.UsefulAreaM2;
                minCellDensity = Math.Min(minCellDensity, density);

                var covered = cellCount > 0 && density + 1e-12 >= spec.TargetCellDensityPointsPerM2;
                if (covered)
                {
                    coveredCellCount++;
                }
                else
                {
                    var occlusionSuspected = obstacles.Any(o => o.OverlapsPlanar(cell.ClippedBounds));
                    var reasonKey = occlusionSuspected
                        ? "cq.failure.lidar_occluded_cell"
                        : "cq.failure.lidar_density";
                    failureReasons.Add(reasonKey);
                    recollectionAreas.Add(new RecollectionArea(
                        AreaId: $"{surface.SurfaceId}:c_{cell.EastIndex}_{cell.NorthIndex}",
                        ReasonKey: reasonKey,
                        CenterEnu: cell.ClippedBounds.CenterEnu(surface.ElevationEnuZ),
                        ExtentsEnu: new Vector3d(cell.ClippedBounds.Width * 0.5, cell.ClippedBounds.Height * 0.5, 2.0),
                        MissingFeatureIds: Array.Empty<string>()));
                }

                cellEvaluations.Add(new CellDensityEvaluation(
                    surface.SurfaceId,
                    cell.EastIndex,
                    cell.NorthIndex,
                    cell,
                    cellCount,
                    density,
                    covered,
                    !covered && obstacles.Any(o => o.OverlapsPlanar(cell.ClippedBounds))));
            }
        }

        if (!double.IsFinite(minCellDensity))
            minCellDensity = 0.0;

        var totalCells = cellEvaluations.Count;
        var coverageFraction = totalCells > 0 ? coveredCellCount / (double)totalCells : 0.0;
        var meanDensity = totalUsefulArea > 0.0 ? totalPointsInUsefulCells / totalUsefulArea : 0.0;

        var coveragePassed = uniqueSamples.Length > 0 && coverageFraction + 1e-12 >= spec.MinCoverageFraction;
        if (!coveragePassed)
            failureReasons.Add("cq.failure.lidar_coverage");

        var densityPassed = uniqueSamples.Length > 0 && meanDensity + 1e-12 >= spec.TargetCellDensityPointsPerM2;
        if (!densityPassed)
            failureReasons.Add("cq.failure.lidar_mean_density");

        var metrics = new List<MetricEvaluationResult>
        {
            new(
                Metric: "lidar_coverage",
                Kind: "coverage",
                Status: coveragePassed ? MetricEvaluationStatus.Passed : MetricEvaluationStatus.Failed,
                Value: coverageFraction,
                BooleanValue: null,
                Limit: spec.MinCoverageFraction,
                Excellent: Math.Min(1.0, spec.MinCoverageFraction + 0.05),
                IsBinaryRequirement: false,
                SampleCount: uniqueSamples.Length,
                FailureReasonKey: coveragePassed ? null : "cq.failure.lidar_coverage"),
            new(
                Metric: "lidar_density_points_per_m2",
                Kind: "density",
                Status: densityPassed ? MetricEvaluationStatus.Passed : MetricEvaluationStatus.Failed,
                Value: meanDensity,
                BooleanValue: null,
                Limit: spec.TargetCellDensityPointsPerM2,
                Excellent: spec.TargetCellDensityPointsPerM2 * 1.5,
                IsBinaryRequirement: false,
                SampleCount: uniqueSamples.Length,
                FailureReasonKey: densityPassed ? null : "cq.failure.lidar_mean_density"),
        };

        var planeEvaluations = EvaluateReferencePlanes(
            spec,
            uniqueSamples,
            metrics,
            failureReasons,
            recollectionAreas,
            out var worstPlaneRmse,
            out var planesPassed);

        var relativePassed = EvaluateRelativeCheckpointPairs(
            spec.MaxRelativeDistanceRmseMetres,
            checkpointReference,
            submittedCheckpoints,
            metrics,
            failureReasons,
            out var relativeRmse);

        var allPassed = coveragePassed && densityPassed && planesPassed && relativePassed;
        var digest = ComputeScientificDigest(
            spec.ContractId,
            uniqueSamples.Length,
            coverageFraction,
            meanDensity,
            worstPlaneRmse,
            relativeRmse);

        return new PointCloudQualityReport(
            ContractId: spec.ContractId,
            Passed: allPassed,
            RawSubmittedPointCount: submittedSamples.Count,
            UniquePointCount: uniqueSamples.Length,
            TotalUsefulCells: totalCells,
            CoveredUsefulCells: coveredCellCount,
            CoverageFraction: coverageFraction,
            MeanDensityPointsPerM2: meanDensity,
            MinCellDensityPointsPerM2: minCellDensity,
            WorstPlaneRmseMetres: worstPlaneRmse,
            RelativeDistanceRmseMetres: relativeRmse,
            Cells: Array.AsReadOnly(cellEvaluations.ToArray()),
            Planes: Array.AsReadOnly(planeEvaluations.ToArray()),
            Metrics: Array.AsReadOnly(metrics.ToArray()),
            FailureReasonKeys: Array.AsReadOnly(failureReasons.OrderBy(k => k, StringComparer.Ordinal).ToArray()),
            RecollectionAreas: Array.AsReadOnly(recollectionAreas.ToArray()),
            ScientificDigestSha256: digest);
    }

    /// <summary>Evaluates scientific CQ directly from an octree/chunk store, ignoring visual LOD rendering budgets.</summary>
    public static PointCloudQualityReport EvaluateFromOctreeStore(
        PointCloudQualitySpec spec,
        IReadOnlyList<SurveySurfaceReference> usefulSurfaces,
        PointCloudOctreeAndChunkStore octreeStore,
        int visualLodBudgetIgnoredForCq,
        EvaluationReference? checkpointReference = null,
        IReadOnlyList<SubmittedCheckpointEstimate>? submittedCheckpoints = null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(usefulSurfaces);
        ArgumentNullException.ThrowIfNull(octreeStore);
        if (!octreeStore.DerivedIndicesValid)
            throw new InvalidOperationException("Octree derived indices are stale; re-ingest revision before scientific CQ.");

        // Query visual LOD to prove callers/HUD can adjust rendering budget freely without mutating scientific inputs.
        if (visualLodBudgetIgnoredForCq > 0)
        {
            _ = octreeStore.QueryVisualLod(Math.Min(visualLodBudgetIgnoredForCq, PointCloudOctreeAndChunkStore.MaxVisualLodBudgetPoints));
        }

        var fullMin = new Vector3d(-1e9, -1e9, -1e9);
        var fullMax = new Vector3d(1e9, 1e9, 1e9);
        var exactSamples = octreeStore.QueryScientificSamples(fullMin, fullMax);
        return Evaluate(spec, usefulSurfaces, exactSamples, checkpointReference, submittedCheckpoints);
    }

    private static GeoreferencedLidarSample[] DeduplicateScientificSamples(
        IReadOnlyList<GeoreferencedLidarSample> submittedSamples)
    {
        var seenIds = new HashSet<long>();
        var seenSpatialKeys = new HashSet<(long TimeTick, long XMm, long YMm, long ZMm, int ReturnNo)>();
        var result = new List<GeoreferencedLidarSample>(submittedSamples.Count);

        foreach (var sample in submittedSamples)
        {
            if (sample is null)
                continue;
            if (!seenIds.Add(sample.SampleId))
                continue;

            var spatialKey = (
                (long)Math.Round(sample.PhysicalTimestampSeconds * 1_000_000_000.0),
                (long)Math.Round(sample.MapPositionEnu.X * 100_000.0),
                (long)Math.Round(sample.MapPositionEnu.Y * 100_000.0),
                (long)Math.Round(sample.MapPositionEnu.Z * 100_000.0),
                sample.ReturnNumber);
            if (!seenSpatialKeys.Add(spatialKey))
                continue;

            result.Add(sample);
        }

        return result.ToArray();
    }

    private static bool IsInsideSurfaceUsefulFootprint(SurveySurfaceReference surface, Vector3d point)
    {
        if (point.X < surface.Bounds.MinEast || point.X > surface.Bounds.MaxEast ||
            point.Y < surface.Bounds.MinNorth || point.Y > surface.Bounds.MaxNorth)
        {
            return false;
        }

        foreach (var exclusion in surface.ExclusionZones)
        {
            if (point.X >= exclusion.MinEast && point.X <= exclusion.MaxEast &&
                point.Y >= exclusion.MinNorth && point.Y <= exclusion.MaxNorth)
            {
                return false;
            }
        }

        return true;
    }

    private static int CountPointsInsideCell(
        IReadOnlyList<GeoreferencedLidarSample> points,
        ClippedSurfaceCell cell,
        int maxEastIdx,
        int maxNorthIdx)
    {
        var b = cell.ClippedBounds;
        var count = 0;
        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i].MapPositionEnu;
            var inEast = p.X >= b.MinEast && (cell.EastIndex == maxEastIdx ? p.X <= b.MaxEast : p.X < b.MaxEast);
            var inNorth = p.Y >= b.MinNorth && (cell.NorthIndex == maxNorthIdx ? p.Y <= b.MaxNorth : p.Y < b.MaxNorth);
            if (inEast && inNorth)
                count++;
        }
        return count;
    }

    private static List<PlaneResidualEvaluation> EvaluateReferencePlanes(
        PointCloudQualitySpec spec,
        IReadOnlyList<GeoreferencedLidarSample> uniqueSamples,
        List<MetricEvaluationResult> metrics,
        HashSet<string> failureReasons,
        List<RecollectionArea> recollectionAreas,
        out double? worstPlaneRmse,
        out bool planesPassed)
    {
        var planeResults = new List<PlaneResidualEvaluation>();
        worstPlaneRmse = null;
        planesPassed = true;

        var planes = spec.ReferencePlanes;
        if (planes is null || planes.Count == 0)
            return planeResults;

        foreach (var plane in planes)
        {
            var n = plane.UnitNormalEnu.Normalized();
            var allowed = plane.MaxRmseOverrideMetres ?? spec.MaxPlaneRmseMetres;
            var sumSq = 0.0;
            var maxAbs = 0.0;
            var count = 0;

            for (var i = 0; i < uniqueSamples.Count; i++)
            {
                var p = uniqueSamples[i].MapPositionEnu;
                if (!plane.ContainsPoint(p))
                    continue;
                var absRes = Math.Abs(Vector3d.Dot(n, p - plane.PointOnPlaneEnu));
                sumSq += absRes * absRes;
                if (absRes > maxAbs)
                    maxAbs = absRes;
                count++;
            }

            var rmse = count > 0 ? Math.Sqrt(sumSq / count) : double.PositiveInfinity;
            var ok = count > 0 && double.IsFinite(rmse) && rmse <= allowed + 1e-12;

            if (double.IsFinite(rmse))
                worstPlaneRmse = worstPlaneRmse.HasValue ? Math.Max(worstPlaneRmse.Value, rmse) : rmse;
            else
                worstPlaneRmse = double.PositiveInfinity;

            if (!ok)
            {
                planesPassed = false;
                var reason = count == 0 ? "cq.failure.plane_no_samples" : "cq.failure.plane_rmse_exceeded";
                failureReasons.Add(reason);
                recollectionAreas.Add(new RecollectionArea(
                    AreaId: $"plane:{plane.PlaneId}",
                    ReasonKey: reason,
                    CenterEnu: plane.Bounds2D.CenterEnu((plane.MinUpMetres + plane.MaxUpMetres) * 0.5),
                    ExtentsEnu: new Vector3d(plane.Bounds2D.Width * 0.5, plane.Bounds2D.Height * 0.5, Math.Max(0.5, (plane.MaxUpMetres - plane.MinUpMetres) * 0.5)),
                    MissingFeatureIds: Array.Empty<string>()));
            }

            planeResults.Add(new PlaneResidualEvaluation(plane.PlaneId, count, rmse, maxAbs, allowed, ok));
        }

        metrics.Add(new MetricEvaluationResult(
            Metric: "plane_to_plane_rmse_m",
            Kind: "accuracy",
            Status: planesPassed ? MetricEvaluationStatus.Passed : MetricEvaluationStatus.Failed,
            Value: worstPlaneRmse,
            BooleanValue: null,
            Limit: spec.MaxPlaneRmseMetres,
            Excellent: spec.MaxPlaneRmseMetres * 0.5,
            IsBinaryRequirement: false,
            SampleCount: planeResults.Sum(p => p.SampleCount),
            FailureReasonKey: planesPassed ? null : "cq.failure.plane_rmse_exceeded"));

        return planeResults;
    }

    private static bool EvaluateRelativeCheckpointPairs(
        double maxRelativeDistanceRmseMetres,
        EvaluationReference? reference,
        IReadOnlyList<SubmittedCheckpointEstimate>? submittedCheckpoints,
        List<MetricEvaluationResult> metrics,
        HashSet<string> failureReasons,
        out double? relativeRmse)
    {
        relativeRmse = null;
        if (reference is null || reference.RelativePairs.Count == 0)
            return true;

        var estById = (submittedCheckpoints ?? Array.Empty<SubmittedCheckpointEstimate>())
            .GroupBy(c => c.CheckpointId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().EstimatedPositionEnu, StringComparer.Ordinal);

        var sumSq = 0.0;
        var validPairs = 0;
        foreach (var pair in reference.RelativePairs)
        {
            if (!reference.CheckpointsById.TryGetValue(pair.FirstCheckpointId, out var firstRef) ||
                !reference.CheckpointsById.TryGetValue(pair.SecondCheckpointId, out var secondRef) ||
                !estById.TryGetValue(pair.FirstCheckpointId, out var firstEst) ||
                !estById.TryGetValue(pair.SecondCheckpointId, out var secondEst))
            {
                relativeRmse = double.PositiveInfinity;
                failureReasons.Add("cq.failure.relative_checkpoint_missing");
                metrics.Add(new MetricEvaluationResult(
                    Metric: "relative_checkpoint_rmse_m",
                    Kind: "relative_accuracy",
                    Status: MetricEvaluationStatus.InsufficientData,
                    Value: null,
                    BooleanValue: null,
                    Limit: maxRelativeDistanceRmseMetres,
                    Excellent: maxRelativeDistanceRmseMetres * 0.5,
                    IsBinaryRequirement: false,
                    SampleCount: validPairs,
                    FailureReasonKey: "cq.failure.relative_checkpoint_missing"));
                return false;
            }

            var trueDist = pair.Evaluate(secondRef.TruePositionEnu - firstRef.TruePositionEnu);
            var estDist = pair.Evaluate(secondEst - firstEst);
            var err = estDist - trueDist;
            sumSq += err * err;
            validPairs++;
        }

        relativeRmse = validPairs > 0 ? Math.Sqrt(sumSq / validPairs) : 0.0;
        var ok = relativeRmse.Value <= maxRelativeDistanceRmseMetres + 1e-12;
        if (!ok)
            failureReasons.Add("cq.failure.relative_accuracy_exceeded");

        metrics.Add(new MetricEvaluationResult(
            Metric: "relative_checkpoint_rmse_m",
            Kind: "relative_accuracy",
            Status: ok ? MetricEvaluationStatus.Passed : MetricEvaluationStatus.Failed,
            Value: relativeRmse,
            BooleanValue: null,
            Limit: maxRelativeDistanceRmseMetres,
            Excellent: maxRelativeDistanceRmseMetres * 0.5,
            IsBinaryRequirement: false,
            SampleCount: validPairs,
            FailureReasonKey: ok ? null : "cq.failure.relative_accuracy_exceeded"));

        return ok;
    }

    private static string ComputeScientificDigest(
        string contractId,
        int uniquePointCount,
        double coverageFraction,
        double meanDensity,
        double? worstPlaneRmse,
        double? relativeRmse)
    {
        var canonical = FormattableString.Invariant(
            $"{contractId}|{uniquePointCount}|{coverageFraction:R}|{meanDensity:R}|{(worstPlaneRmse?.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? "null")}|{(relativeRmse?.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? "null")}");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}

public sealed record PavementSectionObservation(
    string SectionId,
    double ChainageMetres,
    double TransverseOffsetMetres,
    double MeasuredElevationMetres,
    double MeasuredCrossSlopeFraction,
    double MeasuredRutDepthMillimetres,
    double MeasuredIriMPerKm,
    int PointCountInSection);

public sealed record PavementSectionTruthReference(
    string SectionId,
    double ChainageMetres,
    Vector3d CenterPositionEnu,
    double ReferenceElevationMetres,
    double ReferenceCrossSlopeFraction,
    double ReferenceRutDepthMillimetres,
    double ReferenceIriMPerKm);

public sealed record PavementProfileQualitySpec(
    string ContractId,
    double MinSectionCoverageFraction = 0.95,
    int MinPointsPerSection = 15,
    double MaxElevationRmseMetres = 0.020,
    double MaxCrossSlopeRmseFraction = 0.005,
    double MaxRutDepthRmseMillimetres = 3.0,
    double MaxIriRmseMPerKm = 0.50);

public sealed record PavementQualityEvaluationReport(
    string ContractId,
    bool Passed,
    int ExpectedSectionCount,
    int CoveredSectionCount,
    double SectionCoverageFraction,
    double? ElevationRmseMetres,
    double? CrossSlopeRmseFraction,
    double? RutDepthRmseMillimetres,
    double? IriRmseMPerKm,
    IReadOnlyList<MetricEvaluationResult> Metrics,
    IReadOnlyList<string> FailureReasonKeys,
    IReadOnlyList<RecollectionArea> RecollectionAreas);

/// <summary>Domain interface defining pavement longitudinal/transverse profile CQ checks for M5/M7 contracts.</summary>
public interface IPavementProfileEvaluator
{
    PavementQualityEvaluationReport Evaluate(
        PavementProfileQualitySpec spec,
        IReadOnlyList<PavementSectionTruthReference> referenceSections,
        IReadOnlyList<PavementSectionObservation> submittedObservations);
}

/// <summary>Evaluates longitudinal/transverse pavement elevation, cross-slope, rutting, IRI roughness, and section coverage.</summary>
public sealed class PavementProfileEvaluator : IPavementProfileEvaluator
{
    public PavementQualityEvaluationReport Evaluate(
        PavementProfileQualitySpec spec,
        IReadOnlyList<PavementSectionTruthReference> referenceSections,
        IReadOnlyList<PavementSectionObservation> submittedObservations)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(referenceSections);
        ArgumentNullException.ThrowIfNull(submittedObservations);
        if (referenceSections.Count == 0)
            throw new ArgumentException("At least one reference pavement section is required.", nameof(referenceSections));

        var obsBySectionId = submittedObservations
            .Where(o => o is not null && !string.IsNullOrWhiteSpace(o.SectionId))
            .GroupBy(o => o.SectionId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.PointCountInSection).First(), StringComparer.Ordinal);

        var failures = new HashSet<string>(StringComparer.Ordinal);
        var recollections = new List<RecollectionArea>();
        var covered = 0;

        var elevSq = 0.0;
        var slopeSq = 0.0;
        var rutSq = 0.0;
        var iriSq = 0.0;

        foreach (var truth in referenceSections)
        {
            if (!obsBySectionId.TryGetValue(truth.SectionId, out var obs) ||
                obs.PointCountInSection < spec.MinPointsPerSection)
            {
                failures.Add("cq.failure.pavement_section_sparse_or_missing");
                recollections.Add(new RecollectionArea(
                    AreaId: $"pavement:{truth.SectionId}",
                    ReasonKey: "cq.failure.pavement_section_sparse_or_missing",
                    CenterEnu: truth.CenterPositionEnu,
                    ExtentsEnu: new Vector3d(5.0, 2.5, 1.0),
                    MissingFeatureIds: [truth.SectionId]));
                continue;
            }

            covered++;
            var dElev = obs.MeasuredElevationMetres - truth.ReferenceElevationMetres;
            var dSlope = obs.MeasuredCrossSlopeFraction - truth.ReferenceCrossSlopeFraction;
            var dRut = obs.MeasuredRutDepthMillimetres - truth.ReferenceRutDepthMillimetres;
            var dIri = obs.MeasuredIriMPerKm - truth.ReferenceIriMPerKm;

            elevSq += dElev * dElev;
            slopeSq += dSlope * dSlope;
            rutSq += dRut * dRut;
            iriSq += dIri * dIri;
        }

        var coverageFrac = covered / (double)referenceSections.Count;
        var coverageOk = covered > 0 && coverageFrac + 1e-12 >= spec.MinSectionCoverageFraction;
        if (!coverageOk)
            failures.Add("cq.failure.pavement_coverage");

        double? elevRmse = covered > 0 ? Math.Sqrt(elevSq / covered) : null;
        double? slopeRmse = covered > 0 ? Math.Sqrt(slopeSq / covered) : null;
        double? rutRmse = covered > 0 ? Math.Sqrt(rutSq / covered) : null;
        double? iriRmse = covered > 0 ? Math.Sqrt(iriSq / covered) : null;

        var elevOk = elevRmse.HasValue && elevRmse.Value <= spec.MaxElevationRmseMetres + 1e-12;
        var slopeOk = slopeRmse.HasValue && slopeRmse.Value <= spec.MaxCrossSlopeRmseFraction + 1e-12;
        var rutOk = rutRmse.HasValue && rutRmse.Value <= spec.MaxRutDepthRmseMillimetres + 1e-12;
        var iriOk = iriRmse.HasValue && iriRmse.Value <= spec.MaxIriRmseMPerKm + 1e-12;

        if (!elevOk)
            failures.Add("cq.failure.pavement_elevation_rmse");
        if (!slopeOk)
            failures.Add("cq.failure.pavement_cross_slope");
        if (!rutOk)
            failures.Add("cq.failure.pavement_rut_depth");
        if (!iriOk)
            failures.Add("cq.failure.pavement_iri_roughness");

        MetricEvaluationStatus StatusOrInsufficient(bool hasVal, bool ok) =>
            !hasVal ? MetricEvaluationStatus.InsufficientData : (ok ? MetricEvaluationStatus.Passed : MetricEvaluationStatus.Failed);

        var metrics = new MetricEvaluationResult[]
        {
            new("pavement_coverage", "coverage", coverageOk ? MetricEvaluationStatus.Passed : MetricEvaluationStatus.Failed, coverageFrac, null, spec.MinSectionCoverageFraction, 1.0, false, covered, coverageOk ? null : "cq.failure.pavement_coverage"),
            new("pavement_elevation_rmse_m", "profile", StatusOrInsufficient(elevRmse.HasValue, elevOk), elevRmse, null, spec.MaxElevationRmseMetres, spec.MaxElevationRmseMetres * 0.5, false, covered, elevOk ? null : "cq.failure.pavement_elevation_rmse"),
            new("pavement_cross_slope_rmse", "profile", StatusOrInsufficient(slopeRmse.HasValue, slopeOk), slopeRmse, null, spec.MaxCrossSlopeRmseFraction, spec.MaxCrossSlopeRmseFraction * 0.5, false, covered, slopeOk ? null : "cq.failure.pavement_cross_slope"),
            new("pavement_rut_depth_rmse_mm", "profile", StatusOrInsufficient(rutRmse.HasValue, rutOk), rutRmse, null, spec.MaxRutDepthRmseMillimetres, spec.MaxRutDepthRmseMillimetres * 0.5, false, covered, rutOk ? null : "cq.failure.pavement_rut_depth"),
            new("pavement_iri_rmse_m_per_km", "profile", StatusOrInsufficient(iriRmse.HasValue, iriOk), iriRmse, null, spec.MaxIriRmseMPerKm, spec.MaxIriRmseMPerKm * 0.5, false, covered, iriOk ? null : "cq.failure.pavement_iri_roughness"),
        };

        var passed = coverageOk && elevOk && slopeOk && rutOk && iriOk;
        return new PavementQualityEvaluationReport(
            ContractId: spec.ContractId,
            Passed: passed,
            ExpectedSectionCount: referenceSections.Count,
            CoveredSectionCount: covered,
            SectionCoverageFraction: coverageFrac,
            ElevationRmseMetres: elevRmse,
            CrossSlopeRmseFraction: slopeRmse,
            RutDepthRmseMillimetres: rutRmse,
            IriRmseMPerKm: iriRmse,
            Metrics: Array.AsReadOnly(metrics),
            FailureReasonKeys: Array.AsReadOnly(failures.OrderBy(f => f, StringComparer.Ordinal).ToArray()),
            RecollectionAreas: Array.AsReadOnly(recollections.ToArray()));
    }
}
