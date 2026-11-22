using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.World;
using Gsa.Engine.Visuals;
using Gsa.Missions.Contracts;
using Gsa.Missions.Persistence;
using Gsa.Missions.Quality;
using Godot;

namespace Gsa.Engine.World;

public partial class SaoDatumCityScene
{
    private PortTunnelReview? _lastTunnelReview;

    private void ProcessTunnelDelivery(ContractStateMachine machine, double now)
    {
        var batches = ReadProtectedCityLidarBatches();
        if (batches.Length == 0)
        {
            _feedbackText = Text("city.office.requires_lidar");
            return;
        }

        // Catalog 5 protects Port observations. Only surveyed tunnel slices enter QC.
        var slices = batches.SelectMany(batch => batch.LidarSlices!).Where(slice =>
            slice.TunnelRanges is not null && !slice.RackNeedsCalibration).ToArray();
        var cloud = BuildProtectedCityCloud(slices);
        var review = PortTunnelQuality.Evaluate(machine.ContractId, batches.Select(batch => batch.BatchId).ToArray(), cloud, slices);
        _lastTunnelReview = review;
        CompleteCitySurveyReview(machine, now, review.Delivery,
            review.PortalCount < 2 ? "city.tunnel.portal_rework" : "city.tunnel.road_rework");
    }

    private string TunnelReviewText()
    {
        if (_lastTunnelReview is not { } review) return "";
        var cloud = review.Cloud;
        static string Metres(double? value) => value is { } number && double.IsFinite(number) ? number.ToString("F3") : "—";
        var report = string.Format(Text("city.tunnel.report"), review.PortalCount, cloud.CoveredUsefulCells, cloud.TotalUsefulCells,
            cloud.CoverageFraction, cloud.MeanDensityPointsPerM2, Metres(cloud.WorstPlaneRmseMetres), Metres(cloud.RelativeDistanceRmseMetres));
        if (!review.Delivery.Passed)
        {
            var sections = cloud.Cells.Where(cell => !cell.IsCovered).Select(cell =>
                (int)(cell.Cell.ClippedBounds.MinEast - PortTunnelSurvey.WestEast) / 20 * 20).Distinct();
            var gaps = string.Join(", ", sections.Select(section => $"{section}–{section + 20} m"));
            if (gaps.Length > 0) report += "\n" + string.Format(Text("city.tunnel.gaps"), gaps);
        }
        return report;
    }

    private static IEnumerable<ObstacleBoxEnu> PortTunnelWalls()
    {
        var middle = PortTunnelSurvey.MiddleUpload;
        var length = PortTunnelSurvey.EastEast - PortTunnelSurvey.WestEast;
        foreach (var side in new[] { -1, 1 })
            yield return new($"TunnelWall{side + 1}", new(middle.X, middle.Y + 5 * side, 102.5), new(length, .6, 6));
    }

    private void BuildPortTunnel(Node3D parent)
    {
        var concrete = GsaMeshBuilder.SolidMat(new Color(.36f, .4f, .43f));
        var marker = GsaMeshBuilder.SolidMat(new Color(.98f, .67f, .16f));
        var platform = GsaMeshBuilder.SolidMat(new Color(.28f, .58f, .72f));
        var middle = PortTunnelSurvey.MiddleUpload;

        void Box(string name, Vector3d center, Vector3d extent, Material material, uint collision = 0)
        {
            var size = new Vector3((float)extent.X, (float)extent.Z, (float)extent.Y);
            var body = new StaticBody3D { Name = name, Position = _coordinates.ToGodot(center), CollisionLayer = collision, CollisionMask = 0 };
            body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = material });
            if (collision != 0) body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
            parent.AddChild(body);
        }

        foreach (var wall in PortTunnelWalls()) Box(wall.Id, wall.CenterEnu, wall.SizeEnu, concrete, 1);
        Box("TunnelRoof", new(middle.X, middle.Y, 105.35), new(200, 10.6, .5), concrete, 1);
        foreach (var (point, index) in PortTunnelSurvey.Checkpoints.Select((p, i) => (p, i)))
        {
            var height = point.Z - PortTunnelSurvey.FloorUp;
            Box($"TunnelCheck{index}", point - Vector3d.UnitZ * (height / 2), new(2, 2, height), platform, SurveyLidarSurfaceLayer);
        }

        var pads = new[] { PortTunnelSurvey.WestStop, middle, PortTunnelSurvey.EastStop };
        for (var i = 0; i < pads.Length; i++)
        {
            var pad = new Vector3d(pads[i].X, pads[i].Y, PortTunnelSurvey.FloorUp + .02);
            Box($"TunnelPad{i}", pad, new(3, 3.8, .03), i == 1 ? platform : marker);
        }
        foreach (var side in new[] { -1, 1 })
            Box($"SurveyLaneEdge{side + 1}", new(middle.X, middle.Y + side * PortTunnelSurvey.RoadHalfWidth, PortTunnelSurvey.FloorUp + .015),
                new(248, .12, .025), marker);

        for (var offset = 8.0; offset < 200; offset += 16)
        {
            var lamp = new OmniLight3D { Position = _coordinates.ToGodot(new(PortTunnelSurvey.WestEast + offset, middle.Y, 104.8)),
                OmniRange = 14, LightEnergy = .75f, LightColor = new Color(.84f, .9f, 1f) };
            parent.AddChild(lamp);
        }
    }

    private TunnelRangeObservation? ObserveTunnelRanges(Vector3d position, double heading)
    {
        if (!PortTunnelSurvey.IsSurveyLane(position)) return null;
        var nav = _navigationRig?.Estimator;
        var portal = nav?.LastZuptTimestampSeconds is { } zupt && nav.Current is { } estimate && estimate.TimestampSeconds - zupt <= .061
            ? PortTunnelSurvey.PortalAt(position) : null;
        return PortTunnelSurvey.Observe(position, heading, _tick, portal);
    }
}
