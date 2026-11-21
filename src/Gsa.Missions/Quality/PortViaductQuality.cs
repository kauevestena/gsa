using Gsa.Core.Sensors.Lidar;
using Gsa.Core.World;
using Gsa.Missions.Campaign;
using Gsa.Missions.Contracts;

namespace Gsa.Missions.Quality;

public sealed record PortViaductReview(PointCloudQualityReport Cloud, ViaductVerticalReferenceCheckResult Heights, QualityReport Delivery);

public static class PortViaductQuality
{
    public static IReadOnlyList<ContractRequirementSpec> Requirements { get; } = LidarCorridorQuality.Requirements("viaduct_height_reference");

    public static PortViaductReview Evaluate(string contractId, IReadOnlyList<string> batchIds, PointCloudOctreeAndChunkStore cloud,
        IReadOnlyList<ViaductControlHeightSubmission> submissions)
    {
        var bounds = new Bounds2DEnu(PortViaductSurvey.WestEast, PortViaductSurvey.CenterNorth - PortViaductSurvey.SurveyHalfWidth,
            PortViaductSurvey.EastEast, PortViaductSurvey.CenterNorth + PortViaductSurvey.SurveyHalfWidth);
        var geometry = LidarCorridorQuality.Evaluate(contractId, cloud, "viaduct_underside", bounds,
            PortViaductSurvey.UndersideUp, PortViaductSurvey.CheckPanels);
        var controls = PortViaductSurvey.VerticalControls;
        var complete = submissions.Count == controls.Count && controls.Select((position, index) =>
            submissions.Count(item => item.ControlId == ControlId(index) && item.PositionEnu == position) == 1).All(found => found);
        var heights = complete
            ? Act2CampaignRunner.ValidateViaductHeightReference(submissions)
            : new ViaductVerticalReferenceCheckResult(false, submissions.Count, double.PositiveInfinity, "city.viaduct.incomplete_heights");
        var heightGate = LidarCorridorQuality.Predicate("viaduct_height_reference", heights.Compliant, heights.EvaluatedControls, heights.ReasonKey);
        var delivery = LidarCorridorQuality.Delivery(contractId, batchIds, geometry, heightGate, batchIds.Count > 0,
            heights.Compliant ? heights.EvaluatedControls : 0, geometry.Planes.Count(plane => plane.SampleCount >= 5));
        return new(geometry, heights, delivery);
    }

    public static string ControlId(int index) => $"V{index + 1:00}";
}
