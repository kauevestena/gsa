using Gsa.Core.Geometry;
using Gsa.Core.Traffic;

namespace Gsa.Core.World;

/// <summary>The reserved survey lane and its roadside inventory in the Port and Waterfront.</summary>
public static class BusSurveyCorridor
{
    public const string AreaId = "port_waterfront_bus_corridor";
    public const string MissionId = "HIST-05";
    public const double PortLaneEast = 500900;
    public const double WaterfrontLaneNorth = 7400900;
    public const double HalfWidth = 2.25;

    public static IReadOnlyList<WorldBoxObject> Stops { get; } = Array.AsReadOnly(new WorldBoxObject[]
    {
        Stop("bus_stop_port_01", "port", PortLaneEast + 6, 7401030),
        Stop("bus_stop_port_02", "port", PortLaneEast + 6, 7401070),
        Stop("bus_stop_port_03", "port", PortLaneEast + 6, 7401110),
        Stop("bus_stop_waterfront_01", "waterfront", 501030, WaterfrontLaneNorth + 6),
        Stop("bus_stop_waterfront_02", "waterfront", 501070, WaterfrontLaneNorth + 6),
        Stop("bus_stop_waterfront_03", "waterfront", 501110, WaterfrontLaneNorth + 6),
    });

    public static IReadOnlyList<(Vector3d Start, Vector3d End)> Legs { get; } = Array.AsReadOnly(new[]
    {
        (new Vector3d(PortLaneEast, 7401130, 100), new Vector3d(PortLaneEast, WaterfrontLaneNorth, 100)),
        (new Vector3d(PortLaneEast, WaterfrontLaneNorth, 100), new Vector3d(501130, WaterfrontLaneNorth, 100)),
    });

    public static bool IsSurveyWindow(double hourOfDay) => hourOfDay >= 10 && hourOfDay < 16;

    public static bool IsSurveyWindowAt(double calendarSeconds) => IsSurveyWindow(
        calendarSeconds % WorldCalendarAndWeatherController.SecondsPerDay / WorldCalendarAndWeatherController.SecondsPerHour);

    public static bool Contains(Vector3d position) => Overlaps(position.X, position.Y, 0, 0, HalfWidth);

    public static bool OverlapsBuildings(double east, double north, double width, double depth) =>
        Overlaps(east, north, width / 2, depth / 2, 9);

    private static bool Overlaps(double east, double north, double halfWidth, double halfDepth, double margin) =>
        Legs.Any(leg =>
            east + halfWidth >= Math.Min(leg.Start.X, leg.End.X) - margin &&
            east - halfWidth <= Math.Max(leg.Start.X, leg.End.X) + margin &&
            north + halfDepth >= Math.Min(leg.Start.Y, leg.End.Y) - margin &&
            north - halfDepth <= Math.Max(leg.Start.Y, leg.End.Y) + margin);

    private static WorldBoxObject Stop(string id, string region, double east, double north) =>
        new(id, "traffic_sign", "metal", region, [AreaId], ["sign", "bus_stop"], false, false,
            new Vector3d(east, north, 102), new Vector3d(.8, .8, 1.2));
}
