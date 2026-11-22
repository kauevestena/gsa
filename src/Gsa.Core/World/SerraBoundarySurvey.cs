using Gsa.Core.Geometry;

namespace Gsa.Core.World;

public sealed record SerraBoundarySite(string Id, Vector3d PublishedPositionEnu)
{
    public Vector3d Station(int index)
    {
        if (index is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(index));
        var east = PublishedPositionEnu.X + (index == 0 ? -4 : 4);
        var north = PublishedPositionEnu.Y - 8;
        return new(east, north, SerraBoundarySurvey.CityGroundElevation(east, north));
    }
}

public sealed record BoundarySurveyShot(string MonumentId, int StationIndex, long PhysicsTick, double TimestampSeconds,
    double SlopeDistanceMetres, double AzimuthRadians, double ElevationRadians)
{
    public Vector3d RecomputePoint(SerraBoundarySite site)
    {
        if (site.Id != MonumentId || StationIndex is < 0 or > 1 || PhysicsTick <= 0 ||
            !double.IsFinite(TimestampSeconds) || TimestampSeconds < 0 ||
            SlopeDistanceMetres is not (> 0 and <= 30) ||
            AzimuthRadians is not (>= -Math.PI and <= Math.PI) ||
            ElevationRadians is not (>= -Math.PI / 2 and <= Math.PI / 2))
            throw new ArgumentException("Invalid boundary survey shot.");

        var horizontal = SlopeDistanceMetres * Math.Cos(ElevationRadians);
        return site.Station(StationIndex) + Vector3d.UnitZ * SerraBoundarySurvey.InstrumentHeight +
            new Vector3d(horizontal * Math.Cos(AzimuthRadians), horizontal * Math.Sin(AzimuthRadians), SlopeDistanceMetres * Math.Sin(ElevationRadians));
    }
}

public static class SerraBoundarySurvey
{
    public const string MissionId = "HIST-09";
    public const string AreaId = "serra_divisas";
    public const double InstrumentHeight = 1.45;
    public const double StationRadius = 1.75;
    public const double StopSpeed = .5;
    public const double DetectionLimit = 1.5;
    public const double RepeatLimit = .08;
    public const double MonumentHeight = 1.4;
    public const double RoadNorth = 7401608;

    public static IReadOnlyList<SerraBoundarySite> Sites { get; } = Array.AsReadOnly(Enumerable.Range(1, 6).Select(i =>
    {
        var e = 501260 + i * 85.0;
        var n = 7401620 + (i % 2) * 35.0;
        return new SerraBoundarySite($"serra_divisa_marco_{i:00}", new(e, n, CityGroundElevation(e, n) + MonumentHeight / 2));
    }).ToArray());

    public static IReadOnlyList<Vector3d> UploadPads { get; } = Array.AsReadOnly(new[]
    {
        new Vector3d(501305, RoadNorth, CityGroundElevation(501305, RoadNorth)),
        new Vector3d(501810, RoadNorth, CityGroundElevation(501810, RoadNorth)),
    });

    public static double CityGroundElevation(double east, double north) =>
        100 + 28 * (Math.Clamp((east - 501000) / 1000, 0, 1) + Math.Clamp((north - 7401000) / 1000, 0, 1));

    public static Vector3d DisplacedPosition(SerraBoundarySite site, int index)
    {
        var e = site.PublishedPositionEnu.X + 1.8 + index * .09;
        var n = site.PublishedPositionEnu.Y - .9 - (index % 2) * .18;
        return new(e, n, CityGroundElevation(e, n) + MonumentHeight / 2);
    }

    public static BoundarySurveyShot Observe(SerraBoundarySite site, int stationIndex, Vector3d prismPosition, long tick, double time)
    {
        var delta = prismPosition - (site.Station(stationIndex) + Vector3d.UnitZ * InstrumentHeight);
        var horizontal = Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y);
        var slope = delta.Length + .004 * Math.Sin(tick * .31);
        var azimuth = Math.IEEERemainder(Math.Atan2(delta.Y, delta.X) + .0006 * Math.Sin(tick * .23), Math.Tau);
        var elevation = Math.Atan2(delta.Z, horizontal) + .0006 * Math.Cos(tick * .37);
        return new(site.Id, stationIndex, tick, time, slope, azimuth, elevation);
    }
}
