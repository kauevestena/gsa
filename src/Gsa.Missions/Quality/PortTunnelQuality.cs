using Gsa.Core.Geometry;
using Gsa.Core.Sensors.Lidar;
using Gsa.Core.World;
using Gsa.Missions.Contracts;
using Gsa.Missions.Persistence;

namespace Gsa.Missions.Quality;

public sealed record PortTunnelReview(PointCloudQualityReport Cloud, int PortalCount, QualityReport Delivery);

public static class PortTunnelQuality
{
    public static IReadOnlyList<ContractRequirementSpec> Requirements { get; } = LidarCorridorQuality.Requirements("tunnel_portal_stops");

    public static PortTunnelReview Evaluate(string contractId, IReadOnlyList<string> batchIds,
        PointCloudOctreeAndChunkStore cloud, IReadOnlyList<CityLidarSlice> slices)
    {
        var road = new Bounds2DEnu(PortTunnelSurvey.WestEast, PortTunnelSurvey.CenterNorth - PortTunnelSurvey.RoadHalfWidth,
            PortTunnelSurvey.EastEast, PortTunnelSurvey.CenterNorth + PortTunnelSurvey.RoadHalfWidth);
        var floor = PortTunnelSurvey.FloorUp;
        var report = LidarCorridorQuality.Evaluate(contractId, cloud, "tunnel_road", road, floor, PortTunnelSurvey.Checkpoints);

        var portalCount = slices.Select(slice => slice.TunnelRanges?.ZuptPortal).OfType<int>().Distinct().Count();
        var validFormat = batchIds.Count > 0 && slices.Count > 0;
        var stops = LidarCorridorQuality.Predicate("tunnel_portal_stops", portalCount == 2, portalCount, "cq.failure.tunnel_portal_stops");
        var delivery = LidarCorridorQuality.Delivery(contractId, batchIds, report, stops, validFormat, portalCount,
            report.Planes.Count(plane => plane.SampleCount >= 5));
        return new(report, portalCount, delivery);
    }
}
