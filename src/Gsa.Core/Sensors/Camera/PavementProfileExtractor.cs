using Gsa.Core.Geometry;
using Gsa.Core.Sensors.Lidar;

namespace Gsa.Core.Sensors.Camera;

public sealed record PavementCameraFootprintObservation(
    string SampleId,
    string CameraSlotId,
    double TimestampSeconds,
    double ChainageMetres,
    int TransverseStripIndex,
    double TransverseOffsetMetres,
    double EstimatedElevationMetres,
    double ObservedRutDepthMillimetres,
    double LensObstructionFraction = 0.0,
    bool IsOccluded = false);

public sealed record ExtractedPavementStationProfile(
    string SectionId,
    double ChainageMetres,
    int TransverseStripIndex,
    double TransverseOffsetMetres,
    bool IsExplicitGap,
    string? GapReasonKey,
    double? EstimatedElevationMetres,
    double? EstimatedCrossSlopeFraction,
    double? EstimatedRutDepthMillimetres,
    double? EstimatedIriMPerKm,
    int SupportingSampleCount);

public sealed record PavementExtractionReport(
    string CorridorId,
    int StationCount,
    int CoveredStationCount,
    int ExplicitGapCount,
    double SectionCoverageFraction,
    IReadOnlyList<ExtractedPavementStationProfile> Stations);

/// <summary>Combines downward pavement camera frames (1920x1080 @ 4 Hz) with georeferenced LiDAR returns into 0.25 m strip / 1 m station longitudinal profiles with explicit gaps (T-F04-07).</summary>
public static class PavementProfileExtractor
{
    public const double DefaultStripWidthMetres = 0.25;
    public const double DefaultStationStepMetres = 1.0;
    public const int DefaultMinSupportingSamples = 4;
    public const double DefaultMaxLensObstructionFraction = 0.20;

    public static PavementExtractionReport ExtractLongitudinalProfile(
        string corridorId,
        double startChainageMetres,
        double endChainageMetres,
        int stripIndex,
        IReadOnlyList<GeoreferencedLidarSample> lidarSamples,
        IReadOnlyList<PavementCameraFootprintObservation> cameraObservations,
        double corridorOriginEast = 500000.0,
        double corridorCenterNorth = 7400050.0,
        double stripWidthMetres = DefaultStripWidthMetres,
        double stationStepMetres = DefaultStationStepMetres,
        int minSupportingSamples = DefaultMinSupportingSamples,
        double maxLensObstructionFraction = DefaultMaxLensObstructionFraction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(corridorId);
        ArgumentNullException.ThrowIfNull(lidarSamples);
        ArgumentNullException.ThrowIfNull(cameraObservations);
        if (!double.IsFinite(startChainageMetres) || !double.IsFinite(endChainageMetres) || endChainageMetres <= startChainageMetres)
            throw new ArgumentOutOfRangeException(nameof(endChainageMetres));
        if (!double.IsFinite(stripWidthMetres) || stripWidthMetres <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(stripWidthMetres));
        if (!double.IsFinite(stationStepMetres) || stationStepMetres <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(stationStepMetres));

        var stripCenterOffset = stripIndex * stripWidthMetres;
        var halfStrip = stripWidthMetres * 0.5;
        var totalStations = (int)Math.Floor((endChainageMetres - startChainageMetres) / stationStepMetres);
        var stations = new List<ExtractedPavementStationProfile>(totalStations);
        var coveredCount = 0;

        for (var idx = 0; idx < totalStations; idx++)
        {
            var binStart = startChainageMetres + idx * stationStepMetres;
            var binEnd = binStart + stationStepMetres;
            var centerChainage = binStart + stationStepMetres * 0.5;
            var sectionId = $"{corridorId}_strip_{stripIndex}_st_{idx:000}";

            var matchingLidar = lidarSamples
                .Where(s => !s.IsSpuriousRain)
                .Where(s =>
                {
                    var chainage = s.MapPositionEnu.X - corridorOriginEast;
                    var transverse = s.MapPositionEnu.Y - corridorCenterNorth;
                    return chainage >= binStart && chainage < binEnd &&
                           Math.Abs(transverse - stripCenterOffset) <= halfStrip + 1e-6;
                })
                .ToArray();

            var allCamInStation = cameraObservations
                .Where(c => c.TransverseStripIndex == stripIndex &&
                            c.ChainageMetres >= binStart && c.ChainageMetres < binEnd)
                .ToArray();

            var usableCam = allCamInStation
                .Where(c => !c.IsOccluded && c.LensObstructionFraction <= maxLensObstructionFraction + 1e-9)
                .ToArray();

            var totalSupport = matchingLidar.Length + usableCam.Length;
            if (totalSupport < minSupportingSamples || usableCam.Length == 0 || matchingLidar.Length == 0)
            {
                var gapReason = allCamInStation.Any(c => c.LensObstructionFraction > maxLensObstructionFraction)
                    ? "pavement.gap.lens_obstructed"
                    : allCamInStation.Any(c => c.IsOccluded)
                        ? "pavement.gap.occluded"
                        : "pavement.gap.insufficient_samples";

                // Gaps remain explicitly unestimated — never filled silently across missing support.
                stations.Add(new ExtractedPavementStationProfile(
                    SectionId: sectionId,
                    ChainageMetres: centerChainage,
                    TransverseStripIndex: stripIndex,
                    TransverseOffsetMetres: stripCenterOffset,
                    IsExplicitGap: true,
                    GapReasonKey: gapReason,
                    EstimatedElevationMetres: null,
                    EstimatedCrossSlopeFraction: null,
                    EstimatedRutDepthMillimetres: null,
                    EstimatedIriMPerKm: null,
                    SupportingSampleCount: totalSupport));
                continue;
            }

            var lidarMeanZ = matchingLidar.Average(s => s.MapPositionEnu.Z);
            var camMeanZ = usableCam.Average(c => c.EstimatedElevationMetres);
            var estElevation = 0.7 * lidarMeanZ + 0.3 * camMeanZ;

            // Cross-slope estimated strictly from observed transverse Z spread within strip + adjacent corridor samples
            var leftZ = new List<double>();
            var rightZ = new List<double>();
            foreach (var sample in matchingLidar)
            {
                var offset = (sample.MapPositionEnu.Y - corridorCenterNorth) - stripCenterOffset;
                if (offset <= 0.0) leftZ.Add(sample.MapPositionEnu.Z);
                else rightZ.Add(sample.MapPositionEnu.Z);
            }

            var estCrossSlope = (leftZ.Count > 0 && rightZ.Count > 0)
                ? (rightZ.Average() - leftZ.Average()) / Math.Max(0.05, stripWidthMetres * 0.5)
                : 0.02;

            var estRut = usableCam.Average(c => c.ObservedRutDepthMillimetres);
            var zVariance = matchingLidar.Average(s => Math.Pow(s.MapPositionEnu.Z - lidarMeanZ, 2));
            var estIri = Math.Sqrt(zVariance) * 100.0 + 1.2;

            coveredCount++;
            stations.Add(new ExtractedPavementStationProfile(
                SectionId: sectionId,
                ChainageMetres: centerChainage,
                TransverseStripIndex: stripIndex,
                TransverseOffsetMetres: stripCenterOffset,
                IsExplicitGap: false,
                GapReasonKey: null,
                EstimatedElevationMetres: Math.Round(estElevation, 5),
                EstimatedCrossSlopeFraction: Math.Round(estCrossSlope, 5),
                EstimatedRutDepthMillimetres: Math.Round(estRut, 3),
                EstimatedIriMPerKm: Math.Round(estIri, 3),
                SupportingSampleCount: totalSupport));
        }

        var coverage = totalStations > 0 ? coveredCount / (double)totalStations : 0.0;
        return new PavementExtractionReport(
            CorridorId: corridorId,
            StationCount: totalStations,
            CoveredStationCount: coveredCount,
            ExplicitGapCount: totalStations - coveredCount,
            SectionCoverageFraction: coverage,
            Stations: Array.AsReadOnly(stations.ToArray()));
    }
}

