using Gsa.Core.Geometry;
using Gsa.Core.Sensors.Camera;

namespace Gsa.Core.World;

public readonly record struct TunnelRanges(double Origin, double East, double North, double Up);
public sealed record TunnelRangeObservation(int Station, TunnelRanges Front, TunnelRanges Rear, int? ZuptPortal)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Corridor { get; init; }
}

/// <summary>Survey geometry and observation-only positioning for the instrumented Port tunnel.</summary>
public static class PortTunnelSurvey
{
    public const double WestEast = 500680;
    public const double EastEast = 500880;
    public const double CenterNorth = 7401820;
    public const double FloorUp = 99.575;
    public const double RoadHalfWidth = 2;
    public const double SurveySpeed = 3;
    public const double Approach = 24;
    private const double StationSpacing = 10;
    private const int LastStation = 24;

    public static Vector3d WestStop => new(WestEast - Approach, CenterNorth, 100);
    public static Vector3d EastStop => new(EastEast + Approach, CenterNorth, 100);
    public static Vector3d MiddleUpload => new((WestEast + EastEast) / 2, CenterNorth, 100);
    public static IReadOnlyList<Vector3d> Checkpoints { get; } = Array.AsReadOnly(new[]
    {
        new Vector3d(WestEast + 30, CenterNorth, FloorUp + .04),
        new Vector3d(WestEast + 100, CenterNorth, FloorUp + .08),
        new Vector3d(WestEast + 170, CenterNorth, FloorUp + .12),
    });

    public static bool IsUnderRoof(Vector3d p) => p.X > WestEast && p.X < EastEast && Math.Abs(p.Y - CenterNorth) <= 5;

    public static bool IsSurveyLane(Vector3d p) => p.X >= WestStop.X - 3 && p.X <= EastStop.X + 3 &&
        Math.Abs(p.Y - CenterNorth) <= RoadHalfWidth && Math.Abs(p.Z - FloorUp) < 3;

    public static int? PortalAt(Vector3d p, double radius = 2) =>
        (p - WestStop).Length < radius ? 0 : (p - EastStop).Length < radius ? 1 : null;

    public static bool OverlapsBuilding(double east, double north, double width, double depth) =>
        east + width / 2 >= WestStop.X - 12 && east - width / 2 <= EastStop.X + 12 &&
        Math.Abs(north - CenterNorth) <= 12 + depth / 2;

    private static Vector3d StationOrigin(int station) => new(WestStop.X + 4 + station * StationSpacing, CenterNorth + 3.8, FloorUp + .4);

    public static int NearestStation(double east) => Math.Clamp((int)Math.Round((east - WestStop.X - 4) / StationSpacing), 0, LastStation);

    /// <summary>Simulates measured ranges. The office uses only these ranges and the published station coordinates.</summary>
    public static TunnelRangeObservation Observe(Vector3d vehicle, double heading, long tick, int? stationaryPortal)
    {
        var station = NearestStation(vehicle.X);
        return ObserveStation(vehicle, heading, tick, station, StationOrigin(station), stationaryPortal);
    }

    internal static TunnelRangeObservation ObserveStation(Vector3d vehicle, double heading, long tick, int station,
        Vector3d origin, int? stationaryPortal = null)
    {
        var halfBaseline = new Vector3d(Math.Cos(heading), Math.Sin(heading), 0) * .8;
        TunnelRanges Measure(Vector3d antenna, int salt) => new(
            Range(antenna, origin, salt), Range(antenna, origin + Vector3d.UnitX * 4, salt + 1),
            Range(antenna, origin - Vector3d.UnitY * 7.6, salt + 2), Range(antenna, origin + Vector3d.UnitZ * 4, salt + 3));
        double Range(Vector3d antenna, Vector3d beacon, int salt) => (antenna - beacon).Length +
            .0015 * Math.Sin(tick * .37 + salt * 2.17);
        return new(station, Measure(vehicle + halfBaseline, 0), Measure(vehicle - halfBaseline, 4), stationaryPortal);
    }

    public static EstimatedBodyPose Solve(TunnelRangeObservation observation, double time)
    {
        if (observation.Corridor == PortViaductSurvey.AreaId) return PortViaductSurvey.Solve(observation, time);
        if (observation.Corridor == WaterfrontRainSurvey.AreaId) return WaterfrontRainSurvey.Solve(observation, time);
        if (observation.Corridor == PortWorksSurvey.AreaId) return PortWorksSurvey.Solve(observation, time);
        if (observation.Station is < 0 or > LastStation || observation.ZuptPortal is < 0 or > 1)
            throw new ArgumentException("Invalid tunnel station or portal.", nameof(observation));
        var pose = SolveStation(observation, time, StationOrigin(observation.Station));
        if (observation.Corridor is not null || Math.Abs(pose.PositionEnu.Y - CenterNorth) > RoadHalfWidth + .1 ||
            (observation.ZuptPortal is { } portal && PortalAt(pose.PositionEnu, radius: 2.05) != portal))
            throw new ArgumentException("Inconsistent tunnel range geometry.", nameof(observation));
        return pose;
    }

    internal static EstimatedBodyPose SolveStation(TunnelRangeObservation observation, double time, Vector3d origin)
    {
        Vector3d Locate(TunnelRanges ranges)
        {
            if (!IsRange(ranges.Origin) || !IsRange(ranges.East) || !IsRange(ranges.North) || !IsRange(ranges.Up))
                throw new ArgumentException("Invalid tunnel range.", nameof(observation));
            double Position(double range, double abscissa) =>
                (ranges.Origin * ranges.Origin - range * range + abscissa * abscissa) / (2 * abscissa);
            return origin + new Vector3d(Position(ranges.East, 4), Position(ranges.North, -7.6), Position(ranges.Up, 4));
        }
        static bool IsRange(double range) => range is > 0 and < 25;
        var front = Locate(observation.Front);
        var rear = Locate(observation.Rear);
        var baseline = front - rear;
        var center = (front + rear) * .5;
        if (baseline.Length is < 1.5 or > 1.7)
            throw new ArgumentException("Inconsistent range baseline.", nameof(observation));
        return new(time, center, Quaterniond.FromAxisAngle(Vector3d.UnitZ, Math.Atan2(baseline.Y, baseline.X)));
    }
}
