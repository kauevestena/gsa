using Gsa.Core.Geometry;
using Gsa.Core.Sensors.Camera;

namespace Gsa.Core.World;

public static class WaterfrontRainSurvey
{
    public const string MissionId = "HIST-08";
    public const string AreaId = "waterfront_rain_avenue";
    public const string SurfaceId = "rain_avenue";
    public const double WestEast = 501350;
    public const double EastEast = WestEast + 300;
    public const double CenterNorth = 7400836;
    public const double RoadUp = 99.65;
    public const double SurveyHalfWidth = 2;
    public const double CaptureRange = 6;
    public const double SurveySpeed = 8;
    private const double Approach = 8;
    private const double StationSpacing = 10;
    private const int LastStation = 30;

    public static IReadOnlyList<Vector3d> UploadPads { get; } = Array.AsReadOnly(new[]
    {
        new Vector3d(WestEast - Approach, CenterNorth, 100),
        new Vector3d(WestEast + 100, CenterNorth, 100),
        new Vector3d(WestEast + 200, CenterNorth, 100),
        new Vector3d(EastEast + Approach, CenterNorth, 100),
    });

    public static IReadOnlyList<Vector3d> CheckPanels { get; } = Array.AsReadOnly(new[]
    {
        new Vector3d(WestEast + 50, CenterNorth, RoadUp + .04),
        new Vector3d(WestEast + 150, CenterNorth, RoadUp + .08),
        new Vector3d(WestEast + 250, CenterNorth, RoadUp + .12),
    });

    public static WorldBoxObject RoadSurface { get; } = Surface("RainSurveyRoad",
        new((WestEast + EastEast) / 2, CenterNorth, RoadUp - .1), new(EastEast - WestEast + 30, 13.5, .2));

    public static IReadOnlyList<WorldBoxObject> PanelSurfaces { get; } = Array.AsReadOnly(CheckPanels.Select((p, i) =>
    {
        var height = p.Z - RoadUp;
        return Surface($"RainCheckPanel{i}", p - Vector3d.UnitZ * height / 2, new(2, 2, height));
    }).ToArray());

    private static WorldBoxObject Surface(string id, Vector3d position, Vector3d size) =>
        new(id, "survey_surface", "asphalt", "waterfront", [SurfaceId], [], false, true, position, size);

    public static bool IsSurveyLane(Vector3d p) =>
        p.X >= WestEast - Approach && p.X <= EastEast + Approach &&
        Math.Abs(p.Y - CenterNorth) <= SurveyHalfWidth && p.Z is > 98 and < 103;

    public static bool OverlapsBuilding(double east, double north, double width, double depth) =>
        east + width / 2 >= WestEast - Approach - 20 && east - width / 2 <= EastEast + Approach + 20 &&
        Math.Abs(north - CenterNorth) <= 10 + depth / 2;

    private static Vector3d StationOrigin(int station) =>
        new(WestEast + station * StationSpacing, CenterNorth + 3.8, 100);

    public static TunnelRangeObservation Observe(Vector3d position, double heading, long tick)
    {
        var station = Math.Clamp((int)Math.Round((position.X - WestEast) / StationSpacing), 0, LastStation);
        return PortTunnelSurvey.ObserveStation(position, heading, tick, station, StationOrigin(station)) with { Corridor = AreaId };
    }

    internal static EstimatedBodyPose Solve(TunnelRangeObservation observation, double time)
    {
        if (observation.Station is < 0 or > LastStation || observation.ZuptPortal is not null)
            throw new ArgumentException("Invalid Waterfront range station.", nameof(observation));
        var pose = PortTunnelSurvey.SolveStation(observation, time, StationOrigin(observation.Station));
        var p = pose.PositionEnu;
        if (p.X < WestEast - Approach - .1 || p.X > EastEast + Approach + .1 ||
            Math.Abs(p.Y - CenterNorth) > SurveyHalfWidth + .1 || p.Z is < 97.9 or > 103.1)
            throw new ArgumentException("Inconsistent Waterfront range geometry.", nameof(observation));
        return pose;
    }
}
