using Gsa.Core.Sensors.Lidar;
using Gsa.Core.World;
using Gsa.Missions.Contracts;
using Gsa.Missions.Office;

namespace Gsa.Missions.Quality;

public sealed record WaterfrontRainReview(PointCloudQualityReport Cloud, int InputCount, int RemovedCount, bool FilterSelected, QualityReport Delivery);

public static class WaterfrontRainQuality
{
    private const string FilterMetric = "rain_filter_applied";
    private static readonly Bounds2DEnu Bounds = new(
        WaterfrontRainSurvey.WestEast, WaterfrontRainSurvey.CenterNorth - WaterfrontRainSurvey.SurveyHalfWidth,
        WaterfrontRainSurvey.EastEast, WaterfrontRainSurvey.CenterNorth + WaterfrontRainSurvey.SurveyHalfWidth);

    public static SpuriousPointFilterSpec Filter { get; } = new(.45, 2, RequireMultiPassIfSparse: false, MinIntensity: .24f);
    public static IReadOnlyList<ContractRequirementSpec> Requirements { get; } = LidarCorridorQuality.Requirements(FilterMetric);

    public static WaterfrontRainReview Evaluate(string contractId, IReadOnlyList<string> batchIds,
        IReadOnlyList<FilteredLidarSampleInput> inputs, bool applyFilter)
    {
        var retained = applyFilter ? PointCloudOutlierFilterSession.FilterBySpatiotemporalNeighborhoodAndIntensity(inputs, Filter) : inputs;
        // Footprint and height bounds define the contracted pavement, independently of the filter.
        // Both revisions use this same support. Neither consults the simulated rain label.
        var points = retained.Select(input => input.Sample).Where(p =>
            LidarCorridorQuality.IsInElevationWindow(p.MapPositionEnu.Z, WaterfrontRainSurvey.RoadUp) &&
            p.MapPositionEnu.X >= Bounds.MinEast && p.MapPositionEnu.X <= Bounds.MaxEast &&
            p.MapPositionEnu.Y >= Bounds.MinNorth && p.MapPositionEnu.Y <= Bounds.MaxNorth).ToArray();
        var cloud = LidarCorridorQuality.EvaluatePoints(contractId, points, WaterfrontRainSurvey.SurfaceId, Bounds,
            WaterfrontRainSurvey.RoadUp, WaterfrontRainSurvey.CheckPanels);
        var removed = inputs.Count - retained.Count;
        var gate = LidarCorridorQuality.Predicate(FilterMetric, applyFilter, removed, "city.rain_survey.apply_filter");
        var report = LidarCorridorQuality.Delivery(contractId, batchIds, cloud, gate, batchIds.Count > 0 && inputs.Count > 0,
            applyFilter ? 1 : 0, cloud.Planes.Count(plane => plane.SampleCount >= 5));
        return new(cloud, inputs.Count, removed, applyFilter, report);
    }
}
