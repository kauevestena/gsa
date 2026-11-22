using Gsa.Core.Geometry;
using Gsa.Core.Sensors.Camera;

namespace Gsa.Core.World;

/// <summary>Each survey strip is a drivable part of a full-city sector, with two independent check patches.</summary>
public sealed record FinalCityPavementSector(FinalCitySurveySectorSpec Manifest, double StartEast, double North,
    double BaseUp, double Grade, double Crossfall)
{
    public double EndEast => StartEast + FinalCityPavementSurvey.Length;
    public Vector3d Start => DrivePosition(StartEast);
    public Vector3d End => DrivePosition(EndEast);
    public Vector3d Upload => DrivePosition(EndEast + 9);
    public IReadOnlyList<double> CheckpointChainages { get; } = Array.AsReadOnly(new[] { 16.0, 48.0 });

    public double SurfaceElevation(double e, double n) => BaseUp + Grade * (e - StartEast) + Crossfall * (n - North);

    public Vector3d DrivePosition(double e) => new(e, North,
        Manifest.RegionId == "serra" ? SerraBoundarySurvey.CityGroundElevation(e, North) : 100);

    public bool ContainsLane(Vector3d p, double tolerance = 0) =>
        p.X >= StartEast - 3 - tolerance && p.X <= EndEast + 3 + tolerance &&
        Math.Abs(p.Y - North) <= FinalCityPavementSurvey.DriveHalfWidth + tolerance &&
        Math.Abs(p.Z - DrivePosition(p.X).Z) <= 1 + tolerance;
}

public static class FinalCityPavementSurvey
{
    public const string MissionId = "HIST-11";
    public const string AreaId = "sao_datum_final_corridors";
    public const string CloudFilterUnlock = "city_point_cloud_filter";
    public const int Length = 64;
    public const double HalfWidth = 1.25;
    public const double DriveHalfWidth = .85;
    public const double CaptureRange = 5;
    public const double SurveySpeed = 3;
    public const int LidarChannelStep = 2;
    public const int CameraTickInterval = CameraCaptureScheduler.PhysicsTicksPerSecond / 4;
    public const double DueSeconds = 24 * 3600;
    public const double ExpirySeconds = 30 * 3600;
    private const int StationSpacing = 8;
    private const int LastStation = Length / StationSpacing;

    public static IReadOnlyList<FinalCityPavementSector> Sectors { get; } = Array.AsReadOnly(new[]
    {
        new FinalCityPavementSector(FullCityGeodeticNetwork.FinalSurveySectors[0], 500560, 7400596, 100.06, 0, .015),
        new FinalCityPavementSector(FullCityGeodeticNetwork.FinalSurveySectors[1], 500540, 7401556, 100.06, 0, -.018),
        new FinalCityPavementSector(FullCityGeodeticNetwork.FinalSurveySectors[2], 501560, 7400716, 100.06, 0, .020),
        new FinalCityPavementSector(FullCityGeodeticNetwork.FinalSurveySectors[3], 501520, SerraBoundarySurvey.RoadNorth,
            SerraBoundarySurvey.CityGroundElevation(501520, SerraBoundarySurvey.RoadNorth) + .12, .028, .028),
    });

    public static FinalCityPavementSector? LaneAt(Vector3d p) => Sectors.FirstOrDefault(sector => sector.ContainsLane(p));

    public static FinalCityPavementSector? UploadAt(Vector3d p) => Sectors.FirstOrDefault(sector => (p - sector.Upload).Length <= 5);

    public static FinalCityPavementSector? FindSector(string? id) => Sectors.FirstOrDefault(sector => sector.Manifest.SectorId == id);

    public static bool OverlapsBuildings(double east, double north, double width, double depth) => Sectors.Any(sector =>
        east + width / 2 >= sector.StartEast - 16 && east - width / 2 <= sector.EndEast + 18 &&
        north + depth / 2 >= sector.North - 10 && north - depth / 2 <= sector.North + 10);

    private static Vector3d BeaconOrigin(FinalCityPavementSector sector, int station) =>
        sector.DrivePosition(sector.StartEast + station * StationSpacing) + new Vector3d(0, 3.8, .4);

    public static TunnelRangeObservation Observe(FinalCityPavementSector sector, Vector3d p, double heading, long tick)
    {
        var station = Math.Clamp((int)Math.Round((p.X - sector.StartEast) / StationSpacing), 0, LastStation);
        return PortTunnelSurvey.ObserveStation(p, heading, tick, station, BeaconOrigin(sector, station))
            with { Corridor = sector.Manifest.SectorId };
    }

    internal static EstimatedBodyPose Solve(FinalCityPavementSector sector, TunnelRangeObservation observation, double time)
    {
        if (observation.Station is < 0 or > LastStation || observation.ZuptPortal is not null)
            throw new ArgumentException("Invalid finale range station.", nameof(observation));
        var pose = PortTunnelSurvey.SolveStation(observation, time, BeaconOrigin(sector, observation.Station));
        if (!sector.ContainsLane(pose.PositionEnu, .05))
            throw new ArgumentException("Inconsistent finale range geometry.", nameof(observation));
        return pose;
    }
}
