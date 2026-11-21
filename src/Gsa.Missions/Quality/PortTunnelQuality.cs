using Gsa.Core.Geometry;
using Gsa.Core.Sensors.Lidar;
using Gsa.Core.World;
using Gsa.Missions.Contracts;
using Gsa.Missions.Persistence;

namespace Gsa.Missions.Quality;

public sealed record PortTunnelReview(PointCloudQualityReport Cloud, int PortalCount, QualityReport Delivery);

public static class PortTunnelQuality
{
    public const double CoverageMinimum = .90;
    public const double DensityMinimum = 5;
    public const double AbsoluteLimit = .20;
    public const double RelativeLimit = .10;
    private const double FloorWindow = .40;

    public static IReadOnlyList<ContractRequirementSpec> Requirements { get; } = Array.AsReadOnly<ContractRequirementSpec>(
    [
        new ThresholdRequirementSpec("lidar_coverage", "ratio", "min", CoverageMinimum, .98),
        new ThresholdRequirementSpec("lidar_density_points_per_m2", "pts/m2", "min", DensityMinimum, 8),
        new ThresholdRequirementSpec("plane_to_plane_rmse_m", "m", "max", AbsoluteLimit, .05),
        new ThresholdRequirementSpec("relative_checkpoint_rmse_m", "m", "max", RelativeLimit, .02),
        new PredicateRequirementSpec("tunnel_portal_stops", true),
        new PredicateRequirementSpec("format_valid", true),
    ]);

    public static PortTunnelReview Evaluate(string contractId, IReadOnlyList<string> batchIds,
        PointCloudOctreeAndChunkStore cloud, IReadOnlyList<CityLidarSlice> slices)
    {
        var road = new Bounds2DEnu(PortTunnelSurvey.WestEast, PortTunnelSurvey.CenterNorth - PortTunnelSurvey.RoadHalfWidth,
            PortTunnelSurvey.EastEast, PortTunnelSurvey.CenterNorth + PortTunnelSurvey.RoadHalfWidth);
        var floor = PortTunnelSurvey.FloorUp;
        var points = cloud.QueryScientificSamples(new(road.MinEast, road.MinNorth, floor - FloorWindow),
                new(road.MaxEast, road.MaxNorth, floor + FloorWindow))
            .Where(p => !p.IsSpuriousRain).ToArray();

        var panels = PortTunnelSurvey.Checkpoints.Select((point, i) => new ControlCheckpointReference($"panel_{i + 1}", point)).ToArray();
        var planes = panels.Select(panel =>
        {
            var p = panel.TruePositionEnu;
            return new ReferencePlaneQualitySpec(panel.CheckpointId, p, Vector3d.UnitZ,
                new(p.X - .8, p.Y - .8, p.X + .8, p.Y + .8), floor - FloorWindow, floor + FloorWindow);
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
        var report = PointCloudQualityEvaluator.Evaluate(new(contractId, DensityMinimum, CoverageMinimum, AbsoluteLimit, RelativeLimit,
            ReferencePlanes: planes), [new("tunnel_road", road, elevationEnuZ: floor)], points, reference, observations);

        var portalCount = slices.Select(slice => slice.TunnelRanges?.ZuptPortal).OfType<int>().Distinct().Count();
        var validFormat = batchIds.Count > 0 && slices.Count > 0;
        var stops = new MetricEvaluationResult("tunnel_portal_stops", "predicate",
            portalCount == 2 ? MetricEvaluationStatus.Passed : MetricEvaluationStatus.Failed, null, portalCount == 2,
            null, null, true, portalCount, portalCount == 2 ? null : "cq.failure.tunnel_portal_stops");
        var format = new MetricEvaluationResult("format_valid", "predicate",
            validFormat ? MetricEvaluationStatus.Passed : MetricEvaluationStatus.Failed, null, validFormat,
            null, null, true, batchIds.Count, validFormat ? null : "cq.failure.format_valid");
        var failures = report.FailureReasonKeys.Concat(new[] { stops.FailureReasonKey, format.FailureReasonKey }.OfType<string>()).ToArray();
        var delivery = new QualityReport(contractId, batchIds, report.Passed && portalCount == 2 && validFormat,
            [.. report.Metrics, stops, format], failures, report.RecollectionAreas, [], report.RawSubmittedPointCount,
            report.UniquePointCount, portalCount, observations.Count);
        return new(report, portalCount, delivery);
    }
}
