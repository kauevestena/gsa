using Gsa.Core.Geometry;
using Gsa.Core.Sensors.Camera;

namespace Gsa.Core.World;

public sealed record WorksSurveySide(string Id, Vector3d Start, Vector3d End, double PanelHeight)
{
    public Vector3d Midpoint => (Start + End) / 2;
    public Vector3d Checkpoint => new(Midpoint.X, Midpoint.Y, PortWorksSurvey.RoadUp + PanelHeight);
}

public static class PortWorksSurvey
{
    public const string MissionId = "HIST-10";
    public const string AreaId = "port_clandestine_perimeter";
    public const string ZoneId = "zone_port_cimento";
    public const double WestEast = 500220;
    public const double EastEast = WestEast + 100;
    public const double SouthNorth = 7401360;
    public const double NorthNorth = SouthNorth + 100;
    public const double RoadUp = PortTunnelSurvey.FloorUp;
    public const double SurveyHalfWidth = 2;
    public const double CaptureRange = 6;
    public const double SurveySpeed = 5;
    public const double UploadDuration = 4;
    public const double UploadStopSpeed = .5;
    private const double UploadRadius = 5;
    private const int StationsPerSide = 10;

    public static IReadOnlyList<WorksSurveySide> Sides { get; } = Array.AsReadOnly(new WorksSurveySide[]
    {
        new("south", new(WestEast, SouthNorth, 100), new(EastEast, SouthNorth, 100), .04),
        new("east", new(EastEast, SouthNorth, 100), new(EastEast, NorthNorth, 100), .08),
        new("north", new(EastEast, NorthNorth, 100), new(WestEast, NorthNorth, 100), .12),
        new("west", new(WestEast, NorthNorth, 100), new(WestEast, SouthNorth, 100), .06),
    });

    public static IReadOnlyList<Vector3d> UploadPads { get; } = Array.AsReadOnly(new[]
    {
        new Vector3d(EastEast + 7, SouthNorth, 100),
        new Vector3d(WestEast - 7, NorthNorth, 100),
    });

    private static readonly Vector3d[] Stations = Sides.SelectMany(side => Enumerable.Range(0, StationsPerSide)
        .Select(i => side.Start + (side.End - side.Start) * (i / (double)StationsPerSide) + Vector3d.UnitZ * .28)).ToArray();

    public static int? UploadPadAt(Vector3d position)
    {
        for (var i = 0; i < UploadPads.Count; i++)
            if ((UploadPads[i] - position).Length <= UploadRadius) return i;
        return null;
    }

    public static bool IsSurveyLane(Vector3d p, double tolerance = 0)
    {
        const double driveHalfWidth = 3;
        var width = driveHalfWidth + tolerance;
        if (p.Z < 98 - tolerance || p.Z > 103 + tolerance ||
            p.X < WestEast - width || p.X > EastEast + width || p.Y < SouthNorth - width || p.Y > NorthNorth + width)
            return false;
        return Math.Min(Math.Min(Math.Abs(p.X - WestEast), Math.Abs(p.X - EastEast)),
            Math.Min(Math.Abs(p.Y - SouthNorth), Math.Abs(p.Y - NorthNorth))) <= width;
    }

    public static bool OverlapsBuilding(double east, double north, double width, double depth) =>
        east + width / 2 >= WestEast - 18 && east - width / 2 <= EastEast + 18 &&
        north + depth / 2 >= SouthNorth - 14 && north - depth / 2 <= NorthNorth + 14;

    public static TunnelRangeObservation Observe(Vector3d position, double heading, long tick)
    {
        var nearest = 0;
        var nearestSquared = double.PositiveInfinity;
        for (var i = 0; i < Stations.Length; i++)
        {
            var delta = position - Stations[i];
            var distance = Vector3d.Dot(delta, delta);
            if (distance >= nearestSquared) continue;
            nearest = i;
            nearestSquared = distance;
        }
        return PortTunnelSurvey.ObserveStation(position, heading, tick, nearest, Stations[nearest]) with { Corridor = AreaId };
    }

    internal static EstimatedBodyPose Solve(TunnelRangeObservation observation, double time)
    {
        if (observation.Station < 0 || observation.Station >= Stations.Length || observation.ZuptPortal is not null)
            throw new ArgumentException("Invalid works range station.", nameof(observation));
        var pose = PortTunnelSurvey.SolveStation(observation, time, Stations[observation.Station]);
        if (!IsSurveyLane(pose.PositionEnu, tolerance: .1))
            throw new ArgumentException("Inconsistent works range geometry.", nameof(observation));
        return pose;
    }
}
