using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Camera;
using Gsa.Core.World;
using Gsa.Engine.Visuals;
using Gsa.Missions.Contracts;
using Gsa.Missions.Quality;
using Godot;

namespace Gsa.Engine.World;

public partial class SaoDatumCityScene
{
    private FinalCityPavementReview? _lastFinalSurveyReview;
    private PointCloudQualityReport? _lastSerraEarthworksCloud;
    private readonly HashSet<string> _finaleUploadedSectors = new(StringComparer.Ordinal);
    private static bool IsFinalPavementSurvey(ContractDefinition? definition) =>
        definition is { Id: FinalCityPavementSurvey.MissionId, AreaId: FinalCityPavementSurvey.AreaId, Product: "pavement_profile" };

    private static FinalCityPavementSector SerraEarthworksSector => FinalCityPavementSurvey.Sectors[3];

    private static bool IsSerraEarthworksSurvey(ContractDefinition? definition) =>
        definition is { Product: "point_cloud", AreaId: "serra_earthworks_cut" or "serra_retaining_corridor" };

    private static bool TryGetProceduralPavementSector(ContractDefinition? definition, out FinalCityPavementSector sector)
    {
        if (definition is { Product: "pavement_profile" } && definition.Id != FinalCityPavementSurvey.MissionId &&
            FinalCityPavementSurvey.Sectors.FirstOrDefault(s => s.Manifest.RegionId == definition.RegionId) is { } found)
        {
            sector = found;
            return true;
        }
        sector = null!;
        return false;
    }

    private static bool IsProceduralPavementSurvey(ContractDefinition? definition) =>
        TryGetProceduralPavementSector(definition, out _);

    private static FinalCityPavementSector? ActiveSingleCorridorSector(ContractDefinition? definition)
    {
        if (IsSerraEarthworksSurvey(definition)) return SerraEarthworksSector;
        if (TryGetProceduralPavementSector(definition, out var sector)) return sector;
        return null;
    }

    private string? PendingFinaleSector => _pendingLidar.Count > 0 ? _pendingLidar[0].TunnelRanges?.Corridor : null;

    private string SingleCorridorStatus(FinalCityPavementSector sector)
    {
        var uploaded = _finaleUploadedSectors.Contains(sector.Manifest.SectorId) ? 1 : 0;
        var buffer = Text(FinalCityPavementSurvey.FindSector(PendingFinaleSector) is { } pending
            ? $"city.map.{pending.Manifest.RegionId}" : "city.finale.empty_buffer");
        return string.Format(Text("city.finale.collection"), uploaded, 1, buffer);
    }

    private string SerraEarthworksReviewText()
    {
        if (_lastSerraEarthworksCloud is not { } cloud) return "";
        static string Metres(double? value) => value is { } n && double.IsFinite(n) ? n.ToString("F3") : "—";
        return string.Format(Text("city.earthworks.report"), cloud.CoveredUsefulCells, cloud.TotalUsefulCells,
            cloud.CoverageFraction, cloud.MeanDensityPointsPerM2, Metres(cloud.WorstPlaneRmseMetres));
    }

    private string FinalSurveyStatus()
    {
        var buffer = Text(FinalCityPavementSurvey.FindSector(PendingFinaleSector) is { } pending
            ? $"city.map.{pending.Manifest.RegionId}" : "city.finale.empty_buffer");
        var status = string.Format(Text("city.finale.collection"), _finaleUploadedSectors.Count,
            FinalCityPavementSurvey.Sectors.Count, buffer);
        if (_activeMachine?.Snapshot is not { } clock) return status;
        var now = _calendar.CalendarTimeSeconds;
        return status + " · " + string.Format(Text(now <= clock.DueAtSeconds ? "city.finale.due" : "city.finale.expiry"),
            TechnicalNumber(Math.Max(0, (now <= clock.DueAtSeconds ? clock.DueAtSeconds - now : clock.ExpiresAtSeconds - now) / 3600)));
    }

    private string FinalSurveyReviewText()
    {
        if (_lastFinalSurveyReview is not { } review) return "";
        string Metric(double? value, int decimals) => value is { } v && double.IsFinite(v) ? TechnicalNumber(v, decimals) : "—";
        return string.Join("\n", review.Sectors.Select(result =>
        {
            var cloud = result.Cloud;
            var pavement = result.Pavement;
            var status = result.Passed ? "city.finale.passed" : result.Uploaded ? "city.finale.rework" : "city.finale.waiting_upload";
            return string.Format(Text("city.finale.sector_report"), Text($"city.map.{result.Sector.Manifest.RegionId}"),
                Text(status), Metric(cloud.CoverageFraction * 100, 0), Metric(cloud.MeanDensityPointsPerM2, 1),
                Metric(cloud.WorstPlaneRmseMetres, 3), Metric(pavement.SectionCoverageFraction * 100, 0),
                Metric(pavement.ElevationRmseMetres, 3), Metric(pavement.CrossSlopeRmseFraction * 100, 2),
                Metric(pavement.RutDepthRmseMillimetres, 1), Metric(pavement.IriRmseMPerKm, 2));
        }));
    }

    private void ProcessFinalPavementDelivery(ContractStateMachine machine, double now)
    {
        if (_pendingLidarReturns > 0)
        {
            _feedbackText = Text("city.finale.upload_first");
            return;
        }
        _lastFinalSurveyReview = FinalCityPavementQuality.Evaluate(ReadProtectedCityLidarBatches());
        CompleteCitySurveyReview(machine, now, _lastFinalSurveyReview.Delivery, "city.finale.review_required");
    }

    private void ProcessProceduralPavementDelivery(ContractStateMachine machine, double now, FinalCityPavementSector sector)
    {
        if (_pendingLidarReturns > 0)
        {
            _feedbackText = Text("city.finale.upload_first");
            return;
        }
        _lastFinalSurveyReview = FinalCityPavementQuality.EvaluateSingleSectorPavement(machine.ContractId, ReadProtectedCityLidarBatches(), sector);
        CompleteCitySurveyReview(machine, now, _lastFinalSurveyReview.Delivery, "city.finale.review_required");
    }

    private void ProcessSerraEarthworksDelivery(ContractStateMachine machine, double now)
    {
        if (_pendingLidarReturns > 0)
        {
            _feedbackText = Text("city.finale.upload_first");
            return;
        }
        var (cloud, delivery) = FinalCityPavementQuality.EvaluateSingleSectorEarthworksCloud(
            machine.ContractId, ReadProtectedCityLidarBatches(), SerraEarthworksSector);
        _lastSerraEarthworksCloud = cloud;
        CompleteCitySurveyReview(machine, now, delivery, "city.earthworks.review_required");
    }

    private void OffloadFinalPavementSector()
    {
        var sector = FinalCityPavementSurvey.UploadAt(CurrentPositionEnu());
        if (sector is null || PendingFinaleSector != sector.Manifest.SectorId || CurrentActiveMode() != LocomotionMode.Van ||
            Math.Abs(_lastSpeedMps) > .5)
        {
            _feedbackText = Text("city.finale.upload_first");
            return;
        }
        CommitCityOffload(finalSurveyUploadSectorId: sector.Manifest.SectorId);
    }

    private void OffloadSingleCorridorSector(FinalCityPavementSector sector)
    {
        var validMode = IsProceduralPavementSurvey(_activeContract)
            ? CurrentActiveMode() == LocomotionMode.Van
            : CurrentActiveMode() >= LocomotionMode.Pickup;
        if ((CurrentPositionEnu() - sector.Upload).Length > 5 || PendingFinaleSector != sector.Manifest.SectorId ||
            !validMode || Math.Abs(_lastSpeedMps) > .5)
        {
            _feedbackText = Text("city.finale.upload_first");
            return;
        }
        CommitCityOffload(finalSurveyUploadSectorId: sector.Manifest.SectorId);
    }

    private PavementCameraFootprintObservation? ObserveFinalPavementFrame(FinalCityPavementSector sector,
        Vector3d position, EstimatedBodyPose pose, bool isRaining)
    {
        if (_tick % FinalCityPavementSurvey.CameraTickInterval != 0) return null;
        const double cameraHeight = .8;
        const double halfBaseline = .52;
        var origin = position + Vector3d.UnitZ * cameraHeight;
        var space = GetWorld3D().DirectSpaceState;
        using var query = new PhysicsRayQueryParameters3D { CollisionMask = 1 | SurveyLidarSurfaceLayer };
        double? Depth(double offset)
        {
            var rayOrigin = origin + Vector3d.UnitY * offset;
            query.From = _coordinates.ToGodot(rayOrigin);
            query.To = _coordinates.ToGodot(rayOrigin - Vector3d.UnitZ * 2);
            var hit = space.IntersectRay(query);
            if (hit.Count == 0 || hit["collider"].AsGodotObject() is not StaticBody3D body ||
                body.Name != $"FinalRoad_{sector.Manifest.RegionId}")
                return null;
            return rayOrigin.Z - _coordinates.ToMap(hit["position"].AsVector3()).Z;
        }
        var left = Depth(-halfBaseline);
        var center = Depth(0);
        var right = Depth(halfBaseline);
        var depth = center.GetValueOrDefault();
        var offset = pose.PositionEnu.Y - sector.North;
        var chainage = pose.PositionEnu.X - sector.StartEast;
        var obstruction = Math.Clamp(_cityHazards.Lens.ObstructionFraction + (isRaining ? .15 * _calendar.RainIntensity : 0), 0, 1);
        return new($"final_camera_{_tick}", "van_pavement_down", pose.TimeSeconds, chainage, 0, offset,
            pose.PositionEnu.Z + cameraHeight - depth,
            Math.Max(0, 1000 * (depth - (left.GetValueOrDefault() + right.GetValueOrDefault()) / 2)),
            obstruction, !(left.HasValue && center.HasValue && right.HasValue));
    }

    private void BuildFinalPavementCorridors(Node3D parent)
    {
        var road = GsaMeshBuilder.SolidMat(new Color(.24f, .27f, .29f));
        var line = GsaMeshBuilder.SolidMat(new Color(.94f, .76f, .33f));
        var uplink = GsaMeshBuilder.SolidMat(new Color(.15f, .68f, .92f));

        foreach (var sector in FinalCityPavementSurvey.Sectors)
        {
            void Strip(string name, double x0, double x1, double y0, double y1, double lift, Material material, bool collidable = false)
            {
                Vector3 Vertex(double x, double y) => _coordinates.ToGodot(new(x, y, sector.SurfaceElevation(x, y) + lift));
                var a = Vertex(x0, y0);
                var b = Vertex(x0, y1);
                var c = Vertex(x1, y1);
                var d = Vertex(x1, y0);
                Vector3[] faces = [a, b, c, a, c, d];
                using var surface = new SurfaceTool();
                surface.Begin(Mesh.PrimitiveType.Triangles);
                foreach (var vertex in faces) surface.AddVertex(vertex);
                surface.GenerateNormals();
                parent.AddChild(new MeshInstance3D { Name = name + "Visual", Mesh = surface.Commit(), MaterialOverride = material });
                if (!collidable) return;
                var body = new StaticBody3D { Name = name, CollisionLayer = SurveyLidarSurfaceLayer, CollisionMask = 0 };
                body.AddChild(new CollisionShape3D { Shape = new ConcavePolygonShape3D { Data = faces, BackfaceCollision = true } });
                parent.AddChild(body);
            }
            var region = sector.Manifest.RegionId;
            var n = sector.North;
            Strip($"FinalRoad_{region}", sector.StartEast - 4, sector.EndEast + 13, n - 2, n + 2, 0, road, collidable: true);
            for (var sign = -1; sign <= 1; sign += 2)
                Strip($"FinalLane_{region}_{sign}", sector.StartEast - 3, sector.EndEast + 3,
                    n + sign * .89 - .035, n + sign * .89 + .035, .008, line);
            Strip($"FinalUpload_{region}", sector.EndEast + 6, sector.EndEast + 12, n - 1.8, n + 1.8, .009, uplink);
        }
    }
}
