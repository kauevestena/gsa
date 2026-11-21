using Gsa.Core.Geometry;
using Gsa.Core.Sensors.Camera;

namespace Gsa.Core.World;

public static class PortViaductSurvey
{
    public const string MissionId = "HIST-07";
    public const string AreaId = "port_viaduct";
    public const double WestEast = 500630;
    public const double EastEast = 500730;
    public const double CenterNorth = 7401200;
    public const double UndersideUp = 105.6;
    public const double SurveyHalfWidth = 2;
    public const double DeckHalfWidth = 5;
    public const double SurveySpeed = 3;
    private const double StationSpacing = 10;
    private const int LastStation = 10;

    public static Vector3d WestPad => new(WestEast - 10, CenterNorth, 100);
    public static Vector3d EastPad => new(EastEast + 10, CenterNorth, 100);
    public static Vector3d DeckCenter => new((WestEast + EastEast) / 2, CenterNorth, UndersideUp);

    public static IReadOnlyList<Vector3d> VerticalControls { get; } = Array.AsReadOnly(new[]
    {
        new Vector3d(WestEast - 8, CenterNorth + 3, 100.8),
        new Vector3d(WestEast + 50, CenterNorth + 3, 101.0),
        new Vector3d(EastEast + 8, CenterNorth + 3, 101.2),
    });

    public static IReadOnlyList<Vector3d> CheckPanels { get; } = Array.AsReadOnly(new[]
    {
        new Vector3d(WestEast + 20, CenterNorth, UndersideUp - .08),
        new Vector3d(WestEast + 50, CenterNorth, UndersideUp - .12),
        new Vector3d(WestEast + 80, CenterNorth, UndersideUp - .16),
    });

    public static bool IsSurveyLane(Vector3d p) =>
        p.X >= WestPad.X && p.X <= EastPad.X && Math.Abs(p.Y - CenterNorth) <= SurveyHalfWidth &&
        p.Z is > 98 and < 103;

    public static bool OverlapsBuilding(double east, double north, double width, double depth) =>
        east + width / 2 >= WestPad.X - 12 && east - width / 2 <= EastPad.X + 12 &&
        Math.Abs(north - CenterNorth) <= DeckHalfWidth + 6 + depth / 2;

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
            throw new ArgumentException("Invalid viaduct station.", nameof(observation));
        var pose = PortTunnelSurvey.SolveStation(observation, time, StationOrigin(observation.Station));
        var p = pose.PositionEnu;
        if (p.X < WestPad.X - .1 || p.X > EastPad.X + .1 || Math.Abs(p.Y - CenterNorth) > SurveyHalfWidth + .1 ||
            p.Z is < 97.9 or > 103.1)
            throw new ArgumentException("Inconsistent viaduct range geometry.", nameof(observation));
        return pose;
    }
}
