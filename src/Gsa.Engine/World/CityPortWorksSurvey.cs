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
    private WorksUploadSaveState? _worksUpload;
    private PortWorksReview? _lastWorksReview;

    private static bool IsWorksSurvey(ContractDefinition? definition) =>
        definition is { Id: PortWorksSurvey.MissionId, AreaId: PortWorksSurvey.AreaId, Product: "point_cloud" };

    private int? WorksFieldPadAt(Vector3d position) =>
        IsWorksSurvey(_activeContract) ? PortWorksSurvey.UploadPadAt(position) : null;

    private void RestoreWorksSurvey(WorksUploadSaveState? transfer = null)
    {
        _worksUpload = transfer;
        _lastWorksReview = null;
    }

    private void BeginWorksUpload(int pad)
    {
        if (_worksUpload is not null) return;
        if (_pendingLidarReturns == 0)
        {
            _feedbackText = Text("city.hazards.nothing_to_upload");
            return;
        }
        if (Math.Abs(_lastSpeedMps) > PortWorksSurvey.UploadStopSpeed || CurrentActiveMode() < LocomotionMode.Kombi)
        {
            _feedbackText = Text("city.works.stop_for_upload");
            return;
        }
        var now = _calendar.CalendarTimeSeconds;
        _activeMachine?.AdvanceClock(now);
        if (_activeMachine?.State is not (ContractState.Aceito or ContractState.Coletando or ContractState.Retrabalho))
        {
            _feedbackText = Text(_activeMachine?.State == ContractState.Expirado ? "city.office.expired" : "city.office.processing_failed");
            return;
        }

        _worksUpload = new(pad, PortWorksSurvey.UploadDuration);
        _feedbackText = Text("city.works.upload_started");
        SaveHazardChange();
    }

    private void StepWorksUpload(double dt, Vector3d position, double speed, LocomotionMode mode, bool protectedNow)
    {
        if (_worksUpload is not { } transfer) return;
        if (WorksFieldPadAt(position) != transfer.PadIndex || Math.Abs(speed) > PortWorksSurvey.UploadStopSpeed ||
            mode < LocomotionMode.Kombi || _pendingLidarReturns == 0 ||
            _activeMachine is not { IsActiveState: true, Snapshot: { } job } || _calendar.CalendarTimeSeconds > job.ExpiresAtSeconds)
        {
            _worksUpload = null;
            _feedbackText = Text("city.works.upload_interrupted");
            SaveHazardChange();
            return;
        }
        if (transfer.RemainingSeconds == PortWorksSurvey.UploadDuration && !protectedNow && _cityHazards.ActiveZoneId == PortWorksSurvey.ZoneId)
        {
            _cityHazards.PursuitRemaining = 20;
            _hazardNotice = Text("city.works.interception");
            _audio.PlayAlert(critical: true);
        }

        var remaining = transfer.RemainingSeconds - dt;
        if (remaining > 1e-6)
        {
            _worksUpload = transfer with { RemainingSeconds = remaining };
            return;
        }
        _worksUpload = null;
        CommitCityOffload(transfer.PadIndex);
    }

    private void ProcessWorksDelivery(ContractStateMachine machine, double now)
    {
        var batches = ReadProtectedCityLidarBatches();
        if (batches.Length == 0)
        {
            _feedbackText = Text("city.office.upload_first");
            return;
        }
        var slices = batches.SelectMany(batch => batch.LidarSlices!).Where(IsCalibratedWorksSlice);
        var fieldUploads = batches.Count(batch => batch.WorksFieldPad.HasValue && batch.LidarSlices!.Any(IsCalibratedWorksSlice));
        var review = PortWorksQuality.Evaluate(machine.ContractId, batches.Select(batch => batch.BatchId).ToArray(),
            BuildProtectedCityCloud(slices), fieldUploads);
        _lastWorksReview = review;
        CompleteCitySurveyReview(machine, now, review.Delivery,
            fieldUploads == 0 ? "city.works.field_required" : "city.works.side_gap");
    }

    private static bool IsCalibratedWorksSlice(CityLidarSlice slice) =>
        !slice.RackNeedsCalibration && slice.TunnelRanges?.Corridor == PortWorksSurvey.AreaId;

    private string WorksCollectionStatus() => _worksUpload is { } transfer
        ? string.Format(Text("city.works.upload_progress"), Math.Ceiling(transfer.RemainingSeconds))
        : CityLidarCollectionStatus();

    private string WorksReviewText()
    {
        if (_lastWorksReview is not { } review) return "";
        static string Metres(double? value) => value is { } number && double.IsFinite(number) ? number.ToString("F3") : "—";
        var sides = review.Sides.Select(side => string.Format(Text("city.works.side_report"), Text($"city.works.side.{side.Side}"),
            side.Covered, side.Total, side.Fraction));
        return $"{string.Join(" · ", sides)}\n" + string.Format(Text("city.works.quality_report"), review.Cloud.MeanDensityPointsPerM2,
            Metres(review.Cloud.WorstPlaneRmseMetres), Metres(review.Cloud.RelativeDistanceRmseMetres), review.FieldUploads);
    }

    private void BuildWorksPerimeter(Node3D parent)
    {
        var road = GsaMeshBuilder.SolidMat(new Color(.24f, .27f, .28f));
        var paint = GsaMeshBuilder.SolidStandardMat(new Color(.98f, .67f, .22f), emit: true, emissionEnergy: .25f);
        var pad = GsaMeshBuilder.SolidStandardMat(new Color(.95f, .28f, .12f), emit: true, emissionEnergy: .3f);
        var panel = GsaMeshBuilder.SolidMat(new Color(.45f, .51f, .51f));
        var concrete = GsaMeshBuilder.SolidMat(new Color(.5f, .48f, .43f));
        var baseUp = PortWorksSurvey.RoadUp;

        void Box(string id, Vector3d p, double east, double north, double height, Material material, uint collision = 0)
        {
            var size = new Vector3((float)east, (float)height, (float)north);
            var body = new StaticBody3D { Name = id, Position = _coordinates.ToGodot(p), CollisionLayer = collision, CollisionMask = 0 };
            body.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = size },
                MaterialOverride = material,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                VisibilityRangeEnd = 420f,
            });
            if (collision != 0) body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
            parent.AddChild(body);
        }

        foreach (var side in PortWorksSurvey.Sides)
        {
            var middle = side.Midpoint;
            var northSouth = side.Start.X == side.End.X;
            var normal = northSouth ? Vector3d.UnitX : Vector3d.UnitY;
            Box($"WorksRoad_{side.Id}", new(middle.X, middle.Y, baseUp + .086), northSouth ? 7 : 108,
                northSouth ? 108 : 7, .02, road);
            for (var sign = -1; sign <= 1; sign += 2)
                Box($"WorksLane_{side.Id}_{sign}", new Vector3d(middle.X, middle.Y, baseUp + .11) + normal * sign * 2.5,
                    northSouth ? .12 : 100, northSouth ? 100 : .12, .018, paint);
            var check = side.Checkpoint;
            Box($"WorksPanel_{side.Id}", check - Vector3d.UnitZ * side.PanelHeight / 2, 2, 2, side.PanelHeight, panel, SurveyLidarSurfaceLayer);
        }
        foreach (var (position, i) in PortWorksSurvey.UploadPads.Select((point, index) => (point, index)))
            Box($"WorksUpload_{i}", new(position.X, position.Y, baseUp + .11), 4, 5, .024, pad);

        const double east = PortWorksSurvey.WestEast + 50;
        const double north = PortWorksSurvey.SouthNorth + 50;
        Box("WorksFoundation", new(east, north, 100.2), 52, 42, .8, concrete);
        foreach (var col in PortWorksColumns())
            Box(col.Id, col.CenterEnu, col.SizeEnu.X, col.SizeEnu.Y, col.SizeEnu.Z, concrete, 1);
        Box("WorksUnfinishedDeck", new(east, north, 105.9), 48, 17, .45, concrete, 1);
    }

    private static IEnumerable<ObstacleBoxEnu> PortWorksColumns()
    {
        const double east = PortWorksSurvey.WestEast + 50;
        const double north = PortWorksSurvey.SouthNorth + 50;
        foreach (var offset in new[] { -19.0, 19.0 })
            for (var x = -21; x <= 21; x += 14)
                yield return new($"WorksColumn_{x}_{offset}", new(east + x, north + offset, 103.0), new(1.1, 1.1, 6), true);
    }
}
