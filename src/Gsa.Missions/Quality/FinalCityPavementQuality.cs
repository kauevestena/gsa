using Gsa.Core.Geometry;
using Gsa.Core.Sensors.Camera;
using Gsa.Core.Sensors.Lidar;
using Gsa.Core.World;
using Gsa.Missions.Contracts;
using Gsa.Missions.Persistence;

namespace Gsa.Missions.Quality;

public sealed record FinalCitySectorReview(FinalCityPavementSector Sector, bool Uploaded,
    PointCloudQualityReport Cloud, PavementQualityEvaluationReport Pavement)
{
    public bool Passed => Uploaded && Cloud.Passed && Pavement.Passed;
}

public sealed record FinalCityPavementReview(IReadOnlyList<FinalCitySectorReview> Sectors, QualityReport Delivery);

public static class FinalCityPavementQuality
{
    private const string UploadMetric = "final_sector_uploads";
    private static readonly PavementProfileQualitySpec ProfileSpec = new(FinalCityPavementSurvey.MissionId);

    public static IReadOnlyList<ContractRequirementSpec> SerraEarthworksRequirements { get; } = Array.AsReadOnly<ContractRequirementSpec>(
    [
        new ThresholdRequirementSpec("lidar_coverage", "ratio", "min", .85, .96),
        new ThresholdRequirementSpec("lidar_density_points_per_m2", "pts/m2", "min", 12, 22),
        new ThresholdRequirementSpec("plane_to_plane_rmse_m", "m", "max", .05, .025),
        new PredicateRequirementSpec("format_valid", true),
    ]);

    public static IReadOnlyList<ContractRequirementSpec> Requirements { get; } = Array.AsReadOnly<ContractRequirementSpec>(
    [
        new ThresholdRequirementSpec("lidar_coverage", "ratio", "min", .9, .98),
        new ThresholdRequirementSpec("lidar_density_points_per_m2", "pts/m2", "min", 20, 30),
        new ThresholdRequirementSpec("plane_to_plane_rmse_m", "m", "max", .05, .025),
        new ThresholdRequirementSpec("pavement_coverage", "ratio", "min", ProfileSpec.MinSectionCoverageFraction, 1),
        new ThresholdRequirementSpec("pavement_elevation_rmse_m", "m", "max", ProfileSpec.MaxElevationRmseMetres, .01),
        new ThresholdRequirementSpec("pavement_cross_slope_rmse", "ratio", "max", ProfileSpec.MaxCrossSlopeRmseFraction, .0025),
        new ThresholdRequirementSpec("pavement_rut_depth_rmse_mm", "mm", "max", ProfileSpec.MaxRutDepthRmseMillimetres, 1.5),
        new ThresholdRequirementSpec("pavement_iri_rmse_m_per_km", "m/km", "max", ProfileSpec.MaxIriRmseMPerKm, .25),
        new PredicateRequirementSpec(UploadMetric, true),
        new PredicateRequirementSpec("format_valid", true),
    ]);

    public static FinalCityPavementReview Evaluate(IReadOnlyList<SurveyBatchArtifact> batches)
    {
        var id = FinalCityPavementSurvey.MissionId;
        var sectors = FinalCityPavementSurvey.Sectors.Select(sector => EvaluateSector(id, sector,
            batches.Where(batch => batch.FinalSurveyUploadSectorId == sector.Manifest.SectorId)
                .SelectMany(batch => batch.LidarSlices ?? []).ToArray())).ToArray();

        var passedSectors = sectors.Where(sector => sector.Passed).ToArray();
        var uploadCount = sectors.Count(sector => sector.Uploaded);
        var uploads = LidarCorridorQuality.Predicate(UploadMetric, uploadCount == sectors.Length, uploadCount, "city.finale.upload_required");
        var format = LidarCorridorQuality.Predicate("format_valid", batches.Count > 0, batches.Count, "cq.failure.format_valid");

        var metrics = sectors.SelectMany(sector => sector.Cloud.Metrics.Concat(sector.Pavement.Metrics))
            .GroupBy(metric => metric.Metric)
            .Select(group => group.OrderBy(metric => metric.Status == MetricEvaluationStatus.Passed)
                .ThenBy(metric => metric.Metric is "lidar_coverage" or "lidar_density_points_per_m2" or "pavement_coverage"
                    ? metric.Value : -metric.Value).First()).ToArray();

        var failures = sectors.SelectMany(sector => sector.Cloud.FailureReasonKeys.Concat(sector.Pavement.FailureReasonKeys))
            .Concat(new[] { uploads.FailureReasonKey, format.FailureReasonKey }.OfType<string>())
            .Distinct(StringComparer.Ordinal).ToArray();
        var recollect = sectors.SelectMany(sector => sector.Cloud.RecollectionAreas.Concat(sector.Pavement.RecollectionAreas)).ToArray();
        var delivery = new QualityReport(id, batches.Select(batch => batch.BatchId).ToArray(), passedSectors.Length == sectors.Length,
            [.. metrics, uploads, format], failures, recollect, passedSectors.Select(sector => sector.Sector.Manifest.SectorId).ToArray(),
            sectors.Sum(sector => sector.Cloud.RawSubmittedPointCount), sectors.Sum(sector => sector.Cloud.UniquePointCount),
            passedSectors.Length, sectors.Sum(sector => sector.Cloud.Planes.Count(plane => plane.Passed)));
        return new(sectors, delivery);
    }

    public static FinalCityPavementReview EvaluateSingleSectorPavement(
        string contractId,
        IReadOnlyList<SurveyBatchArtifact> batches,
        FinalCityPavementSector sector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractId);
        ArgumentNullException.ThrowIfNull(batches);
        ArgumentNullException.ThrowIfNull(sector);

        var uploaded = batches.Where(batch => batch.FinalSurveyUploadSectorId == sector.Manifest.SectorId)
            .SelectMany(batch => batch.LidarSlices ?? []).ToArray();
        var review = EvaluateSector(contractId, sector, uploaded);
        var upload = LidarCorridorQuality.Predicate(UploadMetric, review.Uploaded, review.Uploaded ? 1 : 0, "city.finale.upload_required");
        var format = LidarCorridorQuality.Predicate("format_valid", batches.Count > 0 && review.Uploaded, batches.Count, "cq.failure.format_valid");
        var failures = review.Cloud.FailureReasonKeys.Concat(review.Pavement.FailureReasonKeys)
            .Concat(new[] { upload.FailureReasonKey, format.FailureReasonKey }.OfType<string>())
            .Distinct(StringComparer.Ordinal).ToArray();
        var delivery = new QualityReport(
            contractId,
            batches.Select(b => b.BatchId).ToArray(),
            review.Passed && batches.Count > 0,
            [.. review.Cloud.Metrics, .. review.Pavement.Metrics, upload, format],
            failures,
            [.. review.Cloud.RecollectionAreas, .. review.Pavement.RecollectionAreas],
            review.Passed ? [sector.Manifest.SectorId] : [],
            review.Cloud.RawSubmittedPointCount,
            review.Cloud.UniquePointCount,
            review.Passed ? 1 : 0,
            review.Cloud.Planes.Count(p => p.Passed));
        return new([review], delivery);
    }

    public static (PointCloudQualityReport Cloud, QualityReport Delivery) EvaluateSingleSectorEarthworksCloud(
        string contractId,
        IReadOnlyList<SurveyBatchArtifact> batches,
        FinalCityPavementSector sector,
        double minDensityPointsPerM2 = 12.0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractId);
        ArgumentNullException.ThrowIfNull(batches);
        ArgumentNullException.ThrowIfNull(sector);

        var slices = batches
            .Where(batch => batch.FinalSurveyUploadSectorId == sector.Manifest.SectorId)
            .SelectMany(batch => batch.LidarSlices ?? [])
            .Where(slice => !slice.RackNeedsCalibration)
            .ToArray();
        var points = slices.SelectMany(slice => slice.Georeference())
            .Where(sample => !sample.IsSpuriousRain && Math.Abs(sample.MapPositionEnu.Z -
                sector.SurfaceElevation(sample.MapPositionEnu.X, sample.MapPositionEnu.Y)) <= .4).ToArray();
        var west = sector.StartEast;
        var east = sector.EndEast;
        var north = sector.North;
        const double halfWidth = FinalCityPavementSurvey.HalfWidth;
        var footprint = new Bounds2DEnu(west, north - halfWidth, east, north + halfWidth);
        var normal = new Vector3d(-sector.Grade, -sector.Crossfall, 1).Normalized();
        var planes = sector.CheckpointChainages.Select((chainage, index) =>
        {
            var x = west + chainage;
            var z = sector.SurfaceElevation(x, north);
            return new ReferencePlaneQualitySpec(sector.Manifest.ContractCheckpointIds[index], new(x, north, z), normal,
                new(x - 1, north - halfWidth, x + 1, north + halfWidth), z - .4, z + .4);
        }).ToArray();
        var cloud = PointCloudQualityEvaluator.Evaluate(
            new(contractId, minDensityPointsPerM2, MinCoverageFraction: 0.85,
                MaxPlaneRmseMetres: sector.Manifest.MaxPlaneRmseMetres, ReferencePlanes: planes),
            [new(sector.Manifest.SurfaceId, footprint, elevationEnuZ: sector.BaseUp)],
            points, new EvaluationReference(contractId, [], [], [], []), []);
        var format = LidarCorridorQuality.Predicate("format_valid", slices.Length > 0 && points.Length > 0, batches.Count, "cq.failure.format_valid");
        var failures = cloud.FailureReasonKeys.Concat(new[] { format.FailureReasonKey }.OfType<string>())
            .Distinct(StringComparer.Ordinal).ToArray();
        var delivery = new QualityReport(
            contractId,
            batches.Select(b => b.BatchId).ToArray(),
            cloud.Passed && format.Status == MetricEvaluationStatus.Passed,
            [.. cloud.Metrics, format],
            failures,
            cloud.RecollectionAreas,
            cloud.Passed ? [sector.Manifest.SectorId] : [],
            cloud.RawSubmittedPointCount,
            cloud.UniquePointCount,
            cloud.Passed ? 1 : 0,
            cloud.Planes.Count(p => p.Passed));
        return (cloud, delivery);
    }

    private static FinalCitySectorReview EvaluateSector(string contractId, FinalCityPavementSector sector, CityLidarSlice[] uploadedSlices)
    {
        var slices = uploadedSlices.Where(slice => !slice.RackNeedsCalibration).ToArray();
        var points = slices.SelectMany(slice => slice.Georeference())
            .Where(sample => !sample.IsSpuriousRain && Math.Abs(sample.MapPositionEnu.Z -
                sector.SurfaceElevation(sample.MapPositionEnu.X, sample.MapPositionEnu.Y)) <= .4).ToArray();
        var west = sector.StartEast;
        var east = sector.EndEast;
        var north = sector.North;
        const double halfWidth = FinalCityPavementSurvey.HalfWidth;
        var footprint = new Bounds2DEnu(west, north - halfWidth, east, north + halfWidth);
        var normal = new Vector3d(-sector.Grade, -sector.Crossfall, 1).Normalized();
        var planes = sector.CheckpointChainages.Select((chainage, index) =>
        {
            var x = west + chainage;
            var z = sector.SurfaceElevation(x, north);
            return new ReferencePlaneQualitySpec(sector.Manifest.ContractCheckpointIds[index], new(x, north, z), normal,
                new(x - 1, north - halfWidth, x + 1, north + halfWidth), z - .4, z + .4);
        }).ToArray();
        var cloud = PointCloudQualityEvaluator.Evaluate(new(contractId, sector.Manifest.MinPointDensityPerM2,
            MaxPlaneRmseMetres: sector.Manifest.MaxPlaneRmseMetres, ReferencePlanes: planes),
            [new(sector.Manifest.SurfaceId, footprint, elevationEnuZ: sector.BaseUp)],
            points, new EvaluationReference(contractId, [], [], [], []), []);

        var frames = slices.Where(slice => slice.PavementFrame is not null).Select(slice => slice.PavementFrame!).ToArray();
        var extracted = PavementProfileExtractor.ExtractLongitudinalProfile(sector.Manifest.SectorId, 0,
            FinalCityPavementSurvey.Length, 0, points, frames, west, north);
        var stationPoints = points.Where(sample => sample.MapPositionEnu.X >= west && sample.MapPositionEnu.X < east &&
            Math.Abs(sample.MapPositionEnu.Y - north) <= halfWidth)
            .ToLookup(sample => (int)(sample.MapPositionEnu.X - west), sample => sample.MapPositionEnu);
        var truth = extracted.Stations.Select(station =>
        {
            var x = west + station.ChainageMetres;
            var z = sector.SurfaceElevation(x, north);
            return new PavementSectionTruthReference(station.SectionId, station.ChainageMetres, new(x, north, z),
                z, sector.Crossfall, 0, 1.2);
        }).ToArray();
        var observations = new List<PavementSectionObservation>();
        foreach (var station in extracted.Stations.Where(station => !station.IsExplicitGap))
        {
            var samples = stationPoints[(int)station.ChainageMetres].ToArray();
            if (samples.Length < ProfileSpec.MinPointsPerSection) continue;
            if (FitSurface(samples) is not { } fit) continue;
            var centerZ = fit.Center.Z + fit.Grade * (west + station.ChainageMetres - fit.Center.X) +
                fit.Crossfall * (north - fit.Center.Y);
            // Discount the nominal vertical component of scanner noise before computing the simulated roughness proxy.
            const double noiseFloorMetres = .004;
            var roughness = Math.Sqrt(Math.Max(0, fit.Rmse * fit.Rmse - noiseFloorMetres * noiseFloorMetres));
            observations.Add(new(station.SectionId, station.ChainageMetres, 0, centerZ, fit.Crossfall,
                station.EstimatedRutDepthMillimetres!.Value, 1.2 + 100 * roughness, samples.Length));
        }
        var pavement = new PavementProfileEvaluator().Evaluate(
            new PavementProfileQualitySpec(contractId, ProfileSpec.MinSectionCoverageFraction, ProfileSpec.MinPointsPerSection,
                ProfileSpec.MaxElevationRmseMetres, ProfileSpec.MaxCrossSlopeRmseFraction, ProfileSpec.MaxRutDepthRmseMillimetres, ProfileSpec.MaxIriRmseMPerKm),
            truth, observations);
        return new(sector, uploadedSlices.Length > 0, cloud, pavement);
    }

    private static (Vector3d Center, double Grade, double Crossfall, double Rmse)? FitSurface(IReadOnlyList<Vector3d> points)
    {
        var center = new Vector3d(points.Average(point => point.X), points.Average(point => point.Y), points.Average(point => point.Z));
        double xx = 0, xy = 0, yy = 0, xz = 0, yz = 0;
        foreach (var point in points)
        {
            var p = point - center;
            xx += p.X * p.X;
            xy += p.X * p.Y;
            yy += p.Y * p.Y;
            xz += p.X * p.Z;
            yz += p.Y * p.Z;
        }
        var determinant = xx * yy - xy * xy;
        if (determinant <= 1e-8) return null;
        var grade = (xz * yy - yz * xy) / determinant;
        var crossfall = (yz * xx - xz * xy) / determinant;
        var rmse = Math.Sqrt(points.Average(point =>
        {
            var p = point - center;
            var residual = p.Z - grade * p.X - crossfall * p.Y;
            return residual * residual;
        }));
        return (center, grade, crossfall, rmse);
    }
}
