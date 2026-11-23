using Gsa.Core.Geometry;
using Gsa.Core.Traffic;
using Gsa.Core.World;
using Gsa.Engine.Visuals;
using Gsa.Missions.Contracts;
using Gsa.Missions.Office;
using Gsa.Missions.Quality;
using Godot;

namespace Gsa.Engine.World;

public partial class SaoDatumCityScene
{
    private WaterfrontRainReview? _lastRainSurveyReview;
    private bool _rainSurveyFilterEnabled;
    private Button _rainFilterButton = null!;

    private static bool IsRainCollection(ContractDefinition? definition) =>
        definition is { Id: WaterfrontRainSurvey.MissionId, Product: "point_cloud" };

    private void BeginRainSurveyWeather()
    {
        const double startHour = 21;
        // The contract expires before dawn. Keep the same stamp across save/reload.
        var hoursUntilStart = (startHour - _calendar.HourOfDay + 24) % 24;
        _calendar.AdvanceCalendarSeconds(hoursUntilStart * WorldCalendarAndWeatherController.SecondsPerHour);
        _calendar.SetWeather(true, .8);
    }

    private void ToggleCityRain()
    {
        if (IsRainCollection(_activeContract) && _activeMachine is { } machine)
        {
            machine.AdvanceClock(_calendar.CalendarTimeSeconds);
            if (machine.IsActiveState)
            {
                _feedbackText = Text("city.rain_survey.weather_locked");
                return;
            }
        }
        _calendar.SetWeather(!_calendar.IsRaining, .75);
    }

    private void RestoreRainSurveyOffice(bool filter = false)
    {
        _lastRainSurveyReview = null;
        _rainSurveyFilterEnabled = filter;
    }

    private void ToggleRainSurveyFilter()
    {
        if (!IsRainCollection(_activeContract)) return;
        _rainSurveyFilterEnabled = !_rainSurveyFilterEnabled;
        SaveHazardChange();
    }

    private void ProcessRainDelivery(ContractStateMachine machine, double now)
    {
        var batches = ReadProtectedCityLidarBatches();
        if (batches.Length == 0)
        {
            _feedbackText = Text("city.office.requires_lidar");
            return;
        }

        var inputs = batches.SelectMany((batch, pass) => batch.LidarSlices!
            .Where(slice => slice.TunnelRanges?.Corridor == WaterfrontRainSurvey.AreaId && !slice.RackNeedsCalibration)
            .SelectMany(slice => slice.Georeference())
            .Select(point => new FilteredLidarSampleInput(point, pass))).ToArray();
        var review = WaterfrontRainQuality.Evaluate(machine.ContractId, batches.Select(batch => batch.BatchId).ToArray(), inputs, _rainSurveyFilterEnabled);
        _lastRainSurveyReview = review;
        CompleteCitySurveyReview(machine, now, review.Delivery,
            review.FilterSelected ? "city.rain_survey.cloud_rework" : "city.rain_survey.apply_filter");
    }

    private string RainSurveyReviewText()
    {
        if (_lastRainSurveyReview is not { } review) return "";
        var cloud = review.Cloud;
        static string Metres(double? value) => value is { } number && double.IsFinite(number) ? number.ToString("F3") : "—";
        var report = string.Format(Text("city.rain_survey.report"), cloud.CoveredUsefulCells, cloud.TotalUsefulCells,
            cloud.CoverageFraction, cloud.MeanDensityPointsPerM2, Metres(cloud.WorstPlaneRmseMetres), Metres(cloud.RelativeDistanceRmseMetres),
            Text(review.FilterSelected ? "city.rain_survey.filtered" : "city.rain_survey.unfiltered"),
            review.InputCount, review.RemovedCount, review.InputCount - review.RemovedCount);
        if (!cloud.Passed)
        {
            var sections = cloud.Cells.Where(cell => !cell.IsCovered).Select(cell =>
                (int)(cell.Cell.ClippedBounds.MinEast - WaterfrontRainSurvey.WestEast) / 25 * 25).Distinct();
            var gaps = string.Join(", ", sections.Select(section => $"{section}–{section + 25} m"));
            if (gaps.Length > 0) report += "\n" + string.Format(Text("city.rain_survey.gaps"), gaps);
        }
        return report;
    }

    private void BuildRainSurveyAvenue(Node3D parent)
    {
        var orange = GsaMeshBuilder.SolidStandardMat(new Color(.99f, .58f, .14f), emit: true, emissionEnergy: .3f);
        var blue = GsaMeshBuilder.SolidStandardMat(new Color(.19f, .58f, .8f), emit: true, emissionEnergy: .3f);
        var panels = GsaMeshBuilder.SolidMat(new Color(.27f, .31f, .33f));
        var road = WaterfrontRainSurvey.RoadUp;
        var north = WaterfrontRainSurvey.CenterNorth;
        var length = WaterfrontRainSurvey.EastEast - WaterfrontRainSurvey.WestEast;
        var center = (WaterfrontRainSurvey.WestEast + WaterfrontRainSurvey.EastEast) / 2;

        void Box(string id, Vector3d p, Vector3 size, Material? material, WorldBoxObject? surface = null)
        {
            var node = new StaticBody3D { Name = id, Position = _coordinates.ToGodot(p), CollisionLayer = surface is not null ? SurveyLidarSurfaceLayer : 0, CollisionMask = 0 };
            if (material is not null)
                node.AddChild(new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = size },
                    MaterialOverride = material,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    VisibilityRangeEnd = 420f,
                });
            if (surface is not null)
            {
                node.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
                _registry.Register(surface, node);
            }
            parent.AddChild(node);
        }

        void SurfaceBox(WorldBoxObject surface, Material? material)
        {
            var size = surface.SizeEnu;
            Box(surface.Id, surface.PositionEnu, new((float)size.X, (float)size.Z, (float)size.Y), material, surface);
        }

        SurfaceBox(WaterfrontRainSurvey.RoadSurface, null);
        foreach (var side in new[] { -1, 1 })
            Box($"RainSurveyLane{side}", new(center, north + side * WaterfrontRainSurvey.SurveyHalfWidth, road + .035),
                new((float)length + 16, .025f, .13f), orange);
        foreach (var (p, i) in WaterfrontRainSurvey.UploadPads.Select((point, index) => (point, index)))
            Box($"RainUpload{i}", new(p.X, p.Y, road + .035), new(3, .025f, 3.8f), blue);
        foreach (var panel in WaterfrontRainSurvey.PanelSurfaces)
            SurfaceBox(panel, panels);
    }
}
