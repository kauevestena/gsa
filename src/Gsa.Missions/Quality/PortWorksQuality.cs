using Gsa.Core.Sensors.Lidar;
using Gsa.Core.World;
using Gsa.Missions.Contracts;

namespace Gsa.Missions.Quality;

public sealed record WorksSideCoverage(string Side, int Covered, int Total)
{
    public double Fraction => Covered / (double)Total;
}

public sealed record PortWorksReview(PointCloudQualityReport Cloud, IReadOnlyList<WorksSideCoverage> Sides,
    int FieldUploads, QualityReport Delivery);

public static class PortWorksQuality
{
    private const string FieldUploadMetric = "works_field_upload";
    private const string SideCoverageMetric = "works_side_coverage";
    private const string SideGap = "city.works.side_gap";
    private const double Width = PortWorksSurvey.SurveyHalfWidth;

    private static readonly SurveySurfaceReference[] Surfaces = PortWorksSurvey.Sides.Select(side =>
    {
        var northSouth = side.Start.X == side.End.X;
        var eastMargin = northSouth ? Width : 0;
        var northMargin = northSouth ? 0 : Width;
        return new SurveySurfaceReference(side.Id, new(Math.Min(side.Start.X, side.End.X) - eastMargin,
            Math.Min(side.Start.Y, side.End.Y) - northMargin, Math.Max(side.Start.X, side.End.X) + eastMargin,
            Math.Max(side.Start.Y, side.End.Y) + northMargin), elevationEnuZ: PortWorksSurvey.RoadUp);
    }).ToArray();

    public static IReadOnlyList<ContractRequirementSpec> Requirements { get; } = Array.AsReadOnly<ContractRequirementSpec>(
        [.. LidarCorridorQuality.Requirements(FieldUploadMetric),
            new ThresholdRequirementSpec(SideCoverageMetric, "ratio", "min", LidarCorridorQuality.CoverageMinimum, .98)]);

    public static PortWorksReview Evaluate(string contractId, IReadOnlyList<string> batchIds, PointCloudOctreeAndChunkStore cloud, int fieldUploads)
    {
        const double road = PortWorksSurvey.RoadUp;
        var points = cloud.QueryScientificSamples(new(PortWorksSurvey.WestEast - Width, PortWorksSurvey.SouthNorth - Width, road - .4),
            new(PortWorksSurvey.EastEast + Width, PortWorksSurvey.NorthNorth + Width, road + .4))
            .Where(point => point.Intensity >= .22f).ToArray();
        var geometry = LidarCorridorQuality.EvaluatePoints(contractId, points, Surfaces, road,
            PortWorksSurvey.Sides.Select(side => side.Checkpoint).ToArray());
        var coverage = geometry.Cells.GroupBy(cell => cell.SurfaceId).Select(group =>
            new WorksSideCoverage(group.Key, group.Count(cell => cell.IsCovered), group.Count())).ToArray();
        var worst = coverage.Min(side => side.Fraction);
        var sidesPassed = worst >= LidarCorridorQuality.CoverageMinimum;
        var sideMetric = new MetricEvaluationResult(SideCoverageMetric, "coverage",
            sidesPassed ? MetricEvaluationStatus.Passed : MetricEvaluationStatus.Failed, worst, null,
            LidarCorridorQuality.CoverageMinimum, .98, false, coverage.Length, sidesPassed ? null : SideGap);
        geometry = geometry with
        {
            Passed = geometry.Passed && sidesPassed,
            Metrics = [.. geometry.Metrics, sideMetric],
            FailureReasonKeys = sidesPassed ? geometry.FailureReasonKeys : [.. geometry.FailureReasonKeys, SideGap],
        };
        var upload = LidarCorridorQuality.Predicate(FieldUploadMetric, fieldUploads > 0, fieldUploads, "city.works.field_required");
        return new(geometry, coverage, fieldUploads, LidarCorridorQuality.Delivery(contractId, batchIds, geometry, upload,
            batchIds.Count > 0 && points.Length > 0, fieldUploads, geometry.Planes.Count(plane => plane.SampleCount >= 5)));
    }
}
