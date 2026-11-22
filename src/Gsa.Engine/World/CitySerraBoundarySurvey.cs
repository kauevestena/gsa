using Gsa.Core.Geometry;
using Gsa.Core.World;
using Gsa.Engine.Visuals;
using Gsa.Missions.Contracts;
using Gsa.Missions.Persistence;
using Gsa.Missions.Quality;
using Godot;

namespace Gsa.Engine.World;

public partial class SaoDatumCityScene
{
    private readonly List<BoundarySurveyShot> _pendingBoundaryShots = [];
    private readonly List<BoundarySurveyShot> _protectedBoundaryShots = [];
    private bool _boundaryPursuitTriggered;
    private SerraBoundaryReview? _lastBoundaryReview;

    private static bool IsBoundarySurvey(ContractDefinition? definition) =>
        definition is { Id: SerraBoundarySurvey.MissionId, Product: "feature_inventory", AreaId: SerraBoundarySurvey.AreaId };

    private void BeginBoundarySurvey()
    {
        var existing = _cityHazards.Sabotage.MovedBenchmarks.Records.Select(record => record.BenchmarkId).ToHashSet(StringComparer.Ordinal);
        foreach (var (site, index) in SerraBoundarySurvey.Sites.Select((item, i) => (item, i)))
        {
            if (existing.Contains(site.Id)) continue;
            var physical = SerraBoundarySurvey.DisplacedPosition(site, index);
            _cityHazards.Sabotage.MovedBenchmarks.RegisterDisplacement(site.Id, site.PublishedPositionEnu,
                physical - site.PublishedPositionEnu, _calendar.CalendarTimeSeconds);
            SetCitySurveyMarkPhysicalPosition(site.Id, physical);
        }
    }

    private void RestoreBoundarySurvey(BoundarySurveySaveState? state = null)
    {
        _lastBoundaryReview = null;
        _boundaryPursuitTriggered = state?.PursuitTriggered == true;
        _pendingBoundaryShots.Clear();
        _pendingBoundaryShots.AddRange(state?.Pending ?? []);
    }

    private void StepBoundaryPursuit(bool protectedNow)
    {
        if (protectedNow || _boundaryPursuitTriggered || !IsBoundarySurvey(_activeContract) ||
            _activeMachine is not { IsActiveState: true, Snapshot: { } job } ||
            _calendar.CalendarTimeSeconds > job.ExpiresAtSeconds || _cityHazards.ActiveZoneId != "zone_serra_divisas_marco")
            return;
        if (!_pendingBoundaryShots.Concat(_protectedBoundaryShots).GroupBy(shot => shot.MonumentId)
            .Any(group => group.Any(shot => shot.StationIndex == 0) && group.Any(shot => shot.StationIndex == 1))) return;

        _boundaryPursuitTriggered = true;
        _cityHazards.PursuitRemaining = 20;
        _hazardNotice = Text("city.boundary.pursuit");
        _audio.PlayAlert(critical: true);
        SaveHazardChange();
    }

    private void CaptureBoundaryShot(Vector3d position, double speed)
    {
        if (_activeMachine is not { IsActiveState: true, Snapshot: { } job } || _calendar.CalendarTimeSeconds > job.ExpiresAtSeconds)
        {
            _feedbackText = Text("city.office.expired");
            return;
        }
        if (!CityHasSurveyPlatform(3) || _cityHazards.IsVehicleDisabled(_locomotion.MountedVehicleMode))
        {
            _feedbackText = Text("city.story.requires_platform");
            return;
        }
        if (Math.Abs(speed) > SerraBoundarySurvey.StopSpeed)
        {
            _feedbackText = Text("city.hazards.stop_first");
            return;
        }

        foreach (var (site, station, pad) in BoundaryStations())
        {
            if ((position - pad).Length > SerraBoundarySurvey.StationRadius) continue;
            var physical = _manifestObjectsById[site.Id].PositionEnu;
            var instrument = pad + Vector3d.UnitZ * SerraBoundarySurvey.InstrumentHeight;
            using var ray = PhysicsRayQueryParameters3D.Create(_coordinates.ToGodot(instrument), _coordinates.ToGodot(physical), 1);
            var hit = GetWorld3D().DirectSpaceState.IntersectRay(ray);
            if (hit.Count > 0 && (hit["collider"].AsGodotObject() is not { } collider ||
                !_registry.TryGetFastMetadata(collider.GetInstanceId(), out var id, out _) || id != site.Id))
            {
                _feedbackText = Text("city.boundary.station_occluded");
                return;
            }

            var slot = _pendingBoundaryShots.FindIndex(shot => shot.MonumentId == site.Id && shot.StationIndex == station);
            if (slot < 0 && !_locomotion.TryConsumeCaptureUnits(1, out _))
            {
                _feedbackText = Text("city.boundary.storage");
                return;
            }
            var observation = SerraBoundarySurvey.Observe(site, station, physical, _tick, _calendar.CalendarTimeSeconds);
            if (slot < 0) _pendingBoundaryShots.Add(observation);
            else _pendingBoundaryShots[slot] = observation;
            _coverageSpots.Add(pad);
            _recentPhotoSeconds = .6;
            _audio.PlayShutter();
            _feedbackText = string.Format(Text("city.boundary.shot_recorded"), site.Id[^2..], station == 0 ? "A" : "B");
            return;
        }
        _feedbackText = Text("city.boundary.use_station");
    }

    private void ProcessBoundaryDelivery(ContractStateMachine machine, double now)
    {
        var allowed = machine.ProtectedBatches.Select(batch => batch.BatchId).ToHashSet(StringComparer.Ordinal);
        var batches = _cityCustody.GetProtectedArtifactsForContract(machine.ContractId)
            .Where(batch => allowed.Contains(batch.BatchId) && batch.BoundaryShots is { Count: > 0 }).ToArray();
        if (batches.Length == 0)
        {
            _feedbackText = Text("city.office.upload_first");
            return;
        }
        var review = SerraBoundaryQuality.Evaluate(machine.ContractId, batches.Select(batch => batch.BatchId).ToArray(),
            batches.SelectMany(batch => batch.BoundaryShots!).ToArray());
        _lastBoundaryReview = review;
        CompleteCitySurveyReview(machine, now, review.Delivery, review.Delivery.FailureReasonKeys.FirstOrDefault() ?? "city.boundary.recollect");
    }

    private string BoundaryCollectionStatus()
    {
        var pairs = _pendingBoundaryShots.Concat(_protectedBoundaryShots).GroupBy(shot => shot.MonumentId)
            .Count(group => group.Select(shot => shot.StationIndex).Distinct().Count() == 2);
        return string.Format(Text("city.boundary.capture_status"), pairs, _pendingBoundaryShots.Count, _protectedBoundaryShots.Count);
    }

    private string BoundaryReviewText() => _lastBoundaryReview is { } review
        ? string.Join("\n", review.Monuments.Select(mark =>
        {
            if (!mark.HasStationA || !mark.HasStationB)
                return string.Format(Text("city.boundary.missing_station"), mark.MonumentId[^2..],
                    mark.HasStationA ? "B" : mark.HasStationB ? "A" : "A + B");
            return string.Format(Text("city.boundary.result"), mark.MonumentId[^2..], mark.HorizontalDisplacementMetres,
                mark.RepeatErrorMetres, Text(mark.Displaced ? "city.boundary.moved" : "city.boundary.unconfirmed"));
        })) : "";

    private static IEnumerable<(SerraBoundarySite Site, int Station, Vector3d Position)> BoundaryStations()
    {
        foreach (var site in SerraBoundarySurvey.Sites)
            for (var station = 0; station < 2; station++)
                yield return (site, station, site.Station(station));
    }

    private void BuildBoundaryAccess(Node3D parent)
    {
        var road = GsaMeshBuilder.SolidMat(new Color(.28f, .27f, .25f));
        var stationMat = GsaMeshBuilder.SolidStandardMat(new Color(.95f, .6f, .17f), emit: true, emissionEnergy: .23f);
        var uploadMat = GsaMeshBuilder.SolidStandardMat(new Color(.18f, .63f, .88f), emit: true, emissionEnergy: .23f);

        void Carpet(string id, double west, double south, double east, double north, Material material, double lift)
        {
            var surface = new SurfaceTool();
            surface.Begin(Mesh.PrimitiveType.Triangles);
            Vector3 Vertex(double x, double y) => _coordinates.ToGodot(new(x, y, SerraBoundarySurvey.CityGroundElevation(x, y) + lift));
            var sw = Vertex(west, south);
            var se = Vertex(east, south);
            var nw = Vertex(west, north);
            var ne = Vertex(east, north);
            foreach (var vertex in new[] { sw, ne, se, sw, nw, ne }) surface.AddVertex(vertex);
            surface.GenerateNormals();
            parent.AddChild(new MeshInstance3D { Name = id, Mesh = surface.Commit(), MaterialOverride = material });
        }

        var north = SerraBoundarySurvey.RoadNorth;
        Carpet("SerraBoundaryServiceRoad", 501295, north - 4, 501820, north + 4, road, .017);
        foreach (var site in SerraBoundarySurvey.Sites)
        {
            var pos = site.PublishedPositionEnu;
            Carpet($"Approach_{site.Id}", pos.X - 6, north + 3.9, pos.X + 6, pos.Y - 4, road, .026);
        }
        foreach (var (site, station, pad) in BoundaryStations())
        {
            Carpet($"Station_{site.Id}_{station}", pad.X - 1.55, pad.Y - 1.55, pad.X + 1.55, pad.Y + 1.55, stationMat, .052);
            parent.AddChild(new Label3D
            {
                Text = $"{site.Id[^2..]}{(station == 0 ? "A" : "B")}", Position = _coordinates.ToGodot(pad + Vector3d.UnitZ * .9),
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 48, PixelSize = .012f, VisibilityRangeEnd = 48,
            });
        }
        foreach (var (pad, index) in SerraBoundarySurvey.UploadPads.Select((point, i) => (point, i)))
            Carpet($"BoundaryUpload{index}", pad.X - 2, pad.Y - 2, pad.X + 2, pad.Y + 2, uploadMat, .052);
    }
}
