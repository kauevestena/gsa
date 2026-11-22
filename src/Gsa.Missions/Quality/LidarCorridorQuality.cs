using Gsa.Core.Geometry;
using Gsa.Core.Sensors.Lidar;
using Gsa.Missions.Contracts;

namespace Gsa.Missions.Quality;

internal static class LidarCorridorQuality
{
    internal const double CoverageMinimum = .90;
    internal const double DensityMinimum = 5;
    internal const double AbsoluteLimit = .20;
    internal const double RelativeLimit = .10;
    private const double ElevationWindow = .40;

    internal static IReadOnlyList<ContractRequirementSpec> Requirements(string predicate) => Array.AsReadOnly<ContractRequirementSpec>(
    [
        new ThresholdRequirementSpec("lidar_coverage", "ratio", "min", CoverageMinimum, .98),
        new ThresholdRequirementSpec("lidar_density_points_per_m2", "pts/m2", "min", DensityMinimum, 8),
        new ThresholdRequirementSpec("plane_to_plane_rmse_m", "m", "max", AbsoluteLimit, .05),
        new ThresholdRequirementSpec("relative_checkpoint_rmse_m", "m", "max", RelativeLimit, .02),
        new PredicateRequirementSpec(predicate, true),
        new PredicateRequirementSpec("format_valid", true),
    ]);

    internal static PointCloudQualityReport Evaluate(string contractId, PointCloudOctreeAndChunkStore cloud, string surfaceId,
        Bounds2DEnu bounds, double elevation, IReadOnlyList<Vector3d> checkpoints)
    {
        var points = cloud.QueryScientificSamples(new(bounds.MinEast, bounds.MinNorth, elevation - ElevationWindow),
                new(bounds.MaxEast, bounds.MaxNorth, elevation + ElevationWindow))
            .Where(p => !p.IsSpuriousRain).ToArray();
        return EvaluatePoints(contractId, points, surfaceId, bounds, elevation, checkpoints);
    }

    internal static PointCloudQualityReport EvaluatePoints(string contractId, IReadOnlyList<GeoreferencedLidarSample> points, string surfaceId,
        Bounds2DEnu bounds, double elevation, IReadOnlyList<Vector3d> checkpoints) =>
        EvaluatePoints(contractId, points, [new(surfaceId, bounds, elevationEnuZ: elevation)], elevation, checkpoints);

    internal static PointCloudQualityReport EvaluatePoints(string contractId, IReadOnlyList<GeoreferencedLidarSample> points,
        IReadOnlyList<SurveySurfaceReference> surfaces, double elevation, IReadOnlyList<Vector3d> checkpoints)
    {
        var panels = checkpoints.Select((point, i) => new ControlCheckpointReference($"panel_{i + 1}", point)).ToArray();
        var planes = panels.Select(panel =>
        {
            var p = panel.TruePositionEnu;
            return new ReferencePlaneQualitySpec(panel.CheckpointId, p, Vector3d.UnitZ,
                new(p.X - .8, p.Y - .8, p.X + .8, p.Y + .8), elevation - ElevationWindow, elevation + ElevationWindow);
        }).ToArray();
        var observations = new List<SubmittedCheckpointEstimate>();
        foreach (var plane in planes)
        {
            var hits = points.Where(p => plane.ContainsPoint(p.MapPositionEnu)).DistinctBy(p => p.SampleId).ToArray();
            if (hits.Length < 5) continue;
            observations.Add(new(plane.PlaneId, new(plane.PointOnPlaneEnu.X, plane.PointOnPlaneEnu.Y,
                hits.Average(p => p.MapPositionEnu.Z))));
        }
        var pairs = panels.Zip(panels.Skip(1), (a, b) => new RelativeCheckpointPairReference(a.CheckpointId, b.CheckpointId,
            RelativeCheckpointMeasure.HeightDifference)).ToArray();
        var reference = new EvaluationReference(contractId, [], [], panels, pairs);
        return PointCloudQualityEvaluator.Evaluate(new(contractId, DensityMinimum, CoverageMinimum, AbsoluteLimit, RelativeLimit,
            ReferencePlanes: planes), surfaces, points, reference, observations);
    }

    internal static bool IsInElevationWindow(double sampleUp, double elevation) =>
        Math.Abs(sampleUp - elevation) <= ElevationWindow;

    internal static QualityReport Delivery(string contractId, IReadOnlyList<string> batchIds, PointCloudQualityReport cloud,
        MetricEvaluationResult gate, bool validFormat, int passedGates, int checkpointCount)
    {
        var format = Predicate("format_valid", validFormat, batchIds.Count, "cq.failure.format_valid");
        var failures = cloud.FailureReasonKeys.Concat(new[] { gate.FailureReasonKey, format.FailureReasonKey }.OfType<string>()).ToArray();
        return new(contractId, batchIds, cloud.Passed && gate.Status == MetricEvaluationStatus.Passed && validFormat,
            [.. cloud.Metrics, gate, format], failures, cloud.RecollectionAreas, [], cloud.RawSubmittedPointCount,
            cloud.UniquePointCount, passedGates, checkpointCount);
    }

    internal static MetricEvaluationResult Predicate(string metric, bool passed, int count, string failure) =>
        new(metric, "predicate", passed ? MetricEvaluationStatus.Passed : MetricEvaluationStatus.Failed,
            null, passed, null, null, true, count, passed ? null : failure);
}
