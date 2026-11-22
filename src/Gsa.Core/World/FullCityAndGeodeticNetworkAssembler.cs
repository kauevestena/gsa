using Gsa.Core.Geodesy;
using Gsa.Core.Geometry;

namespace Gsa.Core.World;

public enum GeodeticBenchmarkStatus
{
    Nominal = 0,
    ClandestinelyDisplaced = 1,
}

public sealed record GeodeticBenchmarkNode(
    string BenchmarkId,
    string RegionId,
    Vector3d PublishedCadastralPositionEnu,
    double PublishedEllipsoidalHeightMetres,
    double PublishedOrthometricHeightMetres,
    Vector3d PhysicalPositionEnu,
    GeodeticBenchmarkStatus Status,
    string HistoricalNarrativeKey);

public sealed record BenchmarkDisplacementOutcome(
    string BenchmarkId,
    Vector3d PreservedPublishedEnu,
    Vector3d UpdatedPhysicalEnu,
    double HorizontalDisplacementMetres);

public sealed record FinalCitySurveySectorSpec(
    string SectorId,
    string RegionId,
    string SurfaceId,
    WorldBounds BoundsEnu,
    double ExpectedAreaM2,
    double MinPointDensityPerM2,
    double MaxPlaneRmseMetres,
    IReadOnlyList<string> ContractCheckpointIds);

public sealed record FinalCitySurveySectorManifest(
    string SceneId,
    double TotalCityAreaM2,
    int TotalRegionCount,
    int TotalCellCount,
    IReadOnlyList<GeodeticBenchmarkNode> Benchmarks,
    IReadOnlyList<FinalCitySurveySectorSpec> SurveySectors,
    bool DistinctFromCheckpointsVerified,
    bool PriorIdsPreserved);

public sealed record FullCityTraversalWaypointResult(
    string WaypointId,
    string RegionId,
    Vector3d EnuPosition,
    double LocalFrameRoundTripErrorMetres,
    double GeoidOrthometricResidualMetres,
    int LoadedCellCount,
    bool CaptureAllowed,
    bool SuspendedByUnloadedSafetyBand);

public sealed record FullCityTraversalResult(
    bool Succeeded,
    IReadOnlyList<string> VisitedRegionIds,
    IReadOnlyList<string> OccupiedBenchmarkIds,
    double MaxLocalFrameRoundTripErrorMetres,
    double MaxGeoidHeightResidualMetres,
    IReadOnlyList<FullCityTraversalWaypointResult> Steps);

/// <summary>Integrates all four city regions (~4 km²) and the 8-benchmark primary geodetic network cleanly separated from contract checkpoints (T-F02-06).</summary>
public sealed class FullCityGeodeticNetwork
{
    private readonly Dictionary<string, GeodeticBenchmarkNode> _benchmarksById;

    public static IReadOnlyList<string> PrimaryBenchmarkIds { get; } =
    [
        "control_old_center_01",
        "control_old_center_02",
        "survey_control_port_viaduct",
        "survey_control_port_tunnel",
        "survey_control_waterfront_pier",
        "survey_control_waterfront_plaza",
        "survey_control_serra_foothill",
        "survey_control_serra_summit",
    ];

    public IReadOnlyDictionary<string, GeodeticBenchmarkNode> Benchmarks => _benchmarksById;

    private FullCityGeodeticNetwork(Dictionary<string, GeodeticBenchmarkNode> benchmarksById) =>
        _benchmarksById = benchmarksById;

    public static FullCityGeodeticNetwork BuildDefaultNetwork(WorldSceneManifest fullCityManifest)
    {
        ArgumentNullException.ThrowIfNull(fullCityManifest);

        var objectsById = fullCityManifest.Objects.ToDictionary(o => o.Id, StringComparer.Ordinal);
        var narrativeById = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["control_old_center_01"] = "geodesy.benchmark.old_center_estacao.history",
            ["control_old_center_02"] = "geodesy.benchmark.old_center_praca.history",
            ["survey_control_port_viaduct"] = "geodesy.benchmark.port_viaduct.history",
            ["survey_control_port_tunnel"] = "geodesy.benchmark.port_tunnel.history",
            ["survey_control_waterfront_pier"] = "geodesy.benchmark.waterfront_pier.history",
            ["survey_control_waterfront_plaza"] = "geodesy.benchmark.waterfront_plaza.history",
            ["survey_control_serra_foothill"] = "geodesy.benchmark.serra_foothill.history",
            ["survey_control_serra_summit"] = "geodesy.benchmark.serra_summit.history",
        };

        var regionById = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["control_old_center_01"] = "old_center",
            ["control_old_center_02"] = "old_center",
            ["survey_control_port_viaduct"] = "port",
            ["survey_control_port_tunnel"] = "port",
            ["survey_control_waterfront_pier"] = "waterfront",
            ["survey_control_waterfront_plaza"] = "waterfront",
            ["survey_control_serra_foothill"] = "serra",
            ["survey_control_serra_summit"] = "serra",
        };

        var dict = new Dictionary<string, GeodeticBenchmarkNode>(8, StringComparer.Ordinal);
        foreach (var id in PrimaryBenchmarkIds)
        {
            if (!objectsById.TryGetValue(id, out var obj))
                throw new InvalidOperationException($"Missing required primary geodetic benchmark '{id}' in city manifest.");

            var pos = obj.PositionEnu;
            var orthoH = LocalGeoidModel.ToOrthometricHeight(pos);
            dict[id] = new GeodeticBenchmarkNode(
                BenchmarkId: id,
                RegionId: regionById[id],
                PublishedCadastralPositionEnu: pos,
                PublishedEllipsoidalHeightMetres: pos.Z,
                PublishedOrthometricHeightMetres: orthoH,
                PhysicalPositionEnu: pos,
                Status: GeodeticBenchmarkStatus.Nominal,
                HistoricalNarrativeKey: narrativeById[id]);
        }

        return new FullCityGeodeticNetwork(dict);
    }

    public BenchmarkDisplacementOutcome ApplyClandestineDisplacement(
        string benchmarkId,
        Vector3d physicalDeltaEnu)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(benchmarkId);
        if (!_benchmarksById.TryGetValue(benchmarkId, out var current))
            throw new KeyNotFoundException($"Unknown primary benchmark '{benchmarkId}'.");

        // Cadastral reference remains invariant and historical capture batches are never rewritten.
        var nextPhysical = current.PhysicalPositionEnu + physicalDeltaEnu;
        _benchmarksById[benchmarkId] = current with
        {
            PhysicalPositionEnu = nextPhysical,
            Status = GeodeticBenchmarkStatus.ClandestinelyDisplaced,
        };

        var horizontalDelta = Math.Sqrt(
            Math.Pow(nextPhysical.X - current.PublishedCadastralPositionEnu.X, 2) +
            Math.Pow(nextPhysical.Y - current.PublishedCadastralPositionEnu.Y, 2));

        return new BenchmarkDisplacementOutcome(
            BenchmarkId: benchmarkId,
            PreservedPublishedEnu: current.PublishedCadastralPositionEnu,
            UpdatedPhysicalEnu: nextPhysical,
            HorizontalDisplacementMetres: Math.Round(horizontalDelta, 4));
    }

    public static IReadOnlyList<FinalCitySurveySectorSpec> FinalSurveySectors { get; } = Array.AsReadOnly(new FinalCitySurveySectorSpec[]
    {
        new("final_sector_old_center", "old_center", "old_center_ring_surface", RegionalSectorCatalog.OldCenter.BoundsEnu, 1_000_000.0, 20.0, 0.05, ["cp_final_old_01", "cp_final_old_02"]),
        new("final_sector_port", "port", "port_tunnel_mouth_surface", RegionalSectorCatalog.Port.BoundsEnu, 1_000_000.0, 20.0, 0.05, ["cp_final_port_01", "cp_final_port_02"]),
        new("final_sector_waterfront", "waterfront", "waterfront_promenade_surface", RegionalSectorCatalog.Waterfront.BoundsEnu, 1_000_000.0, 20.0, 0.05, ["cp_final_waterfront_01", "cp_final_waterfront_02"]),
        new("final_sector_serra", "serra", "serra_works_surface", RegionalSectorCatalog.SerraReserved.BoundsEnu, 1_000_000.0, 20.0, 0.05, ["cp_final_serra_01", "cp_final_serra_02"]),
    });

    public static (WorldSceneManifest FullManifest, FullCityGeodeticNetwork Network, FinalCitySurveySectorManifest SurveyManifest)
        AssembleFullCity(WorldSceneManifest tutorialManifest)
    {
        ArgumentNullException.ThrowIfNull(tutorialManifest);

        var mvpManifest = OldCenterMvpWorldAssembler.BuildOldCenterMvpManifest(tutorialManifest);
        var fullManifest = SerraWorldAssembler.BuildSerraExpandedManifest(tutorialManifest);
        var mvpPreservation = RegionalSectorCatalog.VerifyMvpTargetIdsPreserved(mvpManifest, fullManifest);
        var network = BuildDefaultNetwork(fullManifest);

        var sectors = FinalSurveySectors;

        var checkpointSet = sectors.SelectMany(s => s.ContractCheckpointIds).ToHashSet(StringComparer.Ordinal);
        var distinctFromCheckpoints = PrimaryBenchmarkIds.All(b => !checkpointSet.Contains(b));
        var totalArea = sectors.Sum(s => s.ExpectedAreaM2);

        var surveyManifest = new FinalCitySurveySectorManifest(
            SceneId: fullManifest.SceneId,
            TotalCityAreaM2: totalArea,
            TotalRegionCount: sectors.Count,
            TotalCellCount: fullManifest.Cells.Count,
            Benchmarks: Array.AsReadOnly(network.Benchmarks.Values.OrderBy(b => b.BenchmarkId, StringComparer.Ordinal).ToArray()),
            SurveySectors: sectors,
            DistinctFromCheckpointsVerified: distinctFromCheckpoints,
            PriorIdsPreserved: mvpPreservation.Preserved);

        return (fullManifest, network, surveyManifest);
    }

    /// <summary>Evaluates neutral streaming windows at benchmark positions; does not run Godot physics or verify capture custody.</summary>
    public static FullCityTraversalResult EvaluateBenchmarkStreamingWindows(
        WorldSceneManifest fullManifest,
        FullCityGeodeticNetwork network,
        double streamingActiveRadiusMetres = 600.0)
    {
        ArgumentNullException.ThrowIfNull(fullManifest);
        ArgumentNullException.ThrowIfNull(network);

        var streamer = new OldCenterSectorStreamer(
            fullManifest,
            activeRadiusMetres: streamingActiveRadiusMetres,
            safetyBandRadiusMetres: streamingActiveRadiusMetres);

        var orderedRoute = new string[]
        {
            "control_old_center_01",
            "control_old_center_02",
            "survey_control_port_viaduct",
            "survey_control_port_tunnel",
            "survey_control_serra_foothill",
            "survey_control_serra_summit",
            "survey_control_waterfront_pier",
            "survey_control_waterfront_plaza",
        };

        var frame = LocalMapFrame.Default;
        var visitedRegions = new List<string>();
        var occupiedBenchmarks = new List<string>();
        var steps = new List<FullCityTraversalWaypointResult>(orderedRoute.Length);
        var maxLocalFrameErr = 0.0;
        var maxGeoidErr = 0.0;

        foreach (var benchmarkId in orderedRoute)
        {
            var node = network.Benchmarks[benchmarkId];
            var enu = node.PhysicalPositionEnu;
            var local = frame.ToLocal(enu);
            var backToEnu = frame.ToMap(local);
            var roundTripErr = (backToEnu - enu).Length;
            maxLocalFrameErr = Math.Max(maxLocalFrameErr, roundTripErr);

            var expectedH = enu.Z - LocalGeoidModel.Separation(enu);
            var geoidResidual = Math.Abs(LocalGeoidModel.ToOrthometricHeight(enu) - expectedH);
            maxGeoidErr = Math.Max(maxGeoidErr, geoidResidual);

            var streamEval = streamer.UpdateStreamingWindow(enu);
            if (!visitedRegions.Contains(node.RegionId, StringComparer.Ordinal))
                visitedRegions.Add(node.RegionId);
            occupiedBenchmarks.Add(node.BenchmarkId);

            steps.Add(new FullCityTraversalWaypointResult(
                WaypointId: node.BenchmarkId,
                RegionId: node.RegionId,
                EnuPosition: enu,
                LocalFrameRoundTripErrorMetres: roundTripErr,
                GeoidOrthometricResidualMetres: geoidResidual,
                LoadedCellCount: streamEval.LoadedSectors.Count,
                CaptureAllowed: streamEval.CaptureAllowed,
                SuspendedByUnloadedSafetyBand: streamEval.SuspendedByUnloadedSafetyBand));
        }

        var succeeded = steps.All(s => s.CaptureAllowed && !s.SuspendedByUnloadedSafetyBand) &&
                        visitedRegions.Count == 4 &&
                        occupiedBenchmarks.Count == 8 &&
                        maxLocalFrameErr < 1e-9 &&
                        maxGeoidErr < 1e-12;

        return new FullCityTraversalResult(
            Succeeded: succeeded,
            VisitedRegionIds: Array.AsReadOnly(visitedRegions.ToArray()),
            OccupiedBenchmarkIds: Array.AsReadOnly(occupiedBenchmarks.ToArray()),
            MaxLocalFrameRoundTripErrorMetres: maxLocalFrameErr,
            MaxGeoidHeightResidualMetres: maxGeoidErr,
            Steps: Array.AsReadOnly(steps.ToArray()));
    }
}

