using Gsa.Core.Geodesy;
using Gsa.Core.Geometry;

namespace Gsa.Core.World;

public enum SerraSurfaceType
{
    Asphalt = 0,
    DirtTrack = 1,
    SteepDirtSlope = 2,
    DenseCanopyCorridor = 3,
    TunnelGalleryConnection = 4,
}

public sealed record SerraTerrainCellSpec(
    string CellId,
    WorldBounds BoundsEnu,
    SerraSurfaceType SurfaceType,
    double SlopeGradePercent,
    double SlopeAngleRadians,
    double DryGripMultiplier,
    double WetGripMultiplier,
    double EllipsoidalHeightMetres,
    double OrthometricHeightMetres,
    bool HasTransmissiveCanopy,
    bool IsTunnelInterior);

public sealed record SerraPickupRouteSpec(
    string RouteId,
    string AreaId,
    bool IsCalmSafe,
    string? CalmAlternativeRouteId,
    IReadOnlyList<string> OrderedCellIds,
    IReadOnlyList<Vector3d> WaypointsEnu,
    double MaxSlopeGradePercent,
    double TotalLengthMetres,
    bool ConnectsToTunnelMouth,
    IReadOnlyList<string> TargetFeatureIds);

public sealed record SerraStreamingCaptureSafetyResult(
    bool CaptureAllowed,
    bool CaptureSuspendedWithWarning,
    string? WarningKey,
    IReadOnlyList<string> MissingCellIds);

/// <summary>Produces the 1 km² Serra region (completing the 4 km² city footprint) with slopes, dirt, transmissive vegetation, tunnel gallery, and Pickup routes with calm alternatives (T-F02-05).</summary>
public static class SerraWorldAssembler
{
    public const int SerraSectorsPerAxis = 10;
    public const double SerraMaxHeightMetres = 260.0;

    public static IReadOnlyList<SerraPickupRouteSpec> PickupRoutes { get; } = BuildPickupRoutes();

    public static WorldSceneManifest BuildSerraExpandedManifest(WorldSceneManifest tutorialManifest)
    {
        ArgumentNullException.ThrowIfNull(tutorialManifest);
        var act1City = RegionalSectorCatalog.BuildExpandedCityManifest(tutorialManifest);
        var serra = RegionalSectorCatalog.SerraReserved;

        var fullBounds = new WorldBounds(
            new Vector3d(RegionalSectorCatalog.MvpBaseEast, RegionalSectorCatalog.MvpBaseNorth, 90.0),
            new Vector3d(
                RegionalSectorCatalog.MvpBaseEast + 2.0 * RegionalSectorCatalog.RegionExtentMetres,
                RegionalSectorCatalog.MvpBaseNorth + 2.0 * RegionalSectorCatalog.RegionExtentMetres,
                SerraMaxHeightMetres));

        var cells = new List<WorldCellDefinition>(act1City.Cells);
        var seenCells = new HashSet<string>(act1City.Cells.Select(c => c.Id), StringComparer.Ordinal);
        var terrainMap = BuildSerraTerrainMap();

        for (var sy = 0; sy < SerraSectorsPerAxis; sy++)
        {
            for (var sx = 0; sx < SerraSectorsPerAxis; sx++)
            {
                var cellId = $"serra_sector_{sx}_{sy}";
                if (!seenCells.Add(cellId))
                    continue;

                var spec = terrainMap[cellId];
                var envClass = spec.SurfaceType switch
                {
                    SerraSurfaceType.TunnelGalleryConnection => "serra_tunnel_connector",
                    SerraSurfaceType.DenseCanopyCorridor => "serra_canopy_slope",
                    SerraSurfaceType.SteepDirtSlope => "serra_steep_dirt",
                    SerraSurfaceType.DirtTrack => "serra_dirt_track",
                    _ => "serra_mountain_road",
                };
                cells.Add(new WorldCellDefinition(cellId, spec.BoundsEnu, envClass));
            }
        }

        var objects = new List<WorldBoxObject>(act1City.Objects);
        var seenIds = new HashSet<string>(act1City.Objects.Select(o => o.Id), StringComparer.Ordinal);

        var serraObjects = new List<WorldBoxObject>
        {
            new("survey_control_serra_foothill", "survey_control", "concrete", "serra", ["serra_foothill_surface"], ["control", "landmark"], false, true, new Vector3d(501080.0, 7401120.0, 110.5), new Vector3d(0.8, 0.8, 0.5)),
            new("survey_control_serra_summit", "survey_control", "concrete", "serra", ["serra_summit_surface"], ["control", "landmark"], false, true, new Vector3d(501880.0, 7401880.0, 212.0), new Vector3d(0.8, 0.8, 0.5)),
            new("serra_tunnel_gallery_01", "tunnel_gallery", "concrete", "serra", ["serra_tunnel_surface"], ["tunnel_gallery", "gnss_denied"], false, false, new Vector3d(501000.0, 7401820.0, 108.0), new Vector3d(160.0, 10.0, 7.0)),
            new("serra_tunnel_east_portal_01", "tunnel_portal", "concrete", "serra", ["serra_tunnel_surface"], ["tunnel_mouth", "serra_connection"], false, true, new Vector3d(501120.0, 7401820.0, 118.0), new Vector3d(12.0, 6.0, 7.0)),
            new("serra_rock_obstacle_01", "rock_outcrop", "concrete", "serra", ["serra_foothill_surface"], ["rock", "slope_hazard"], false, true, new Vector3d(501350.0, 7401285.0, 132.0), new Vector3d(6.0, 5.0, 4.0)),
            new("serra_rock_obstacle_02", "rock_outcrop", "concrete", "serra", ["serra_summit_surface"], ["rock", "slope_hazard"], false, true, new Vector3d(501640.0, 7401540.0, 168.0), new Vector3d(8.0, 6.0, 4.5)),
            new("serra_rock_obstacle_03", "rock_outcrop", "concrete", "serra", ["serra_boundary_surface"], ["rock", "calm_avoidance"], false, true, new Vector3d(501790.0, 7401760.0, 192.0), new Vector3d(7.0, 6.0, 4.0)),
        };

        for (var i = 1; i <= 6; i++)
        {
            var e = 500935.0 + i * 25.0;
            serraObjects.Add(new WorldBoxObject(
                $"tunnel_lining_rib_{i:00}",
                "tunnel_rib",
                "concrete",
                "serra",
                ["serra_tunnel_surface"],
                ["tunnel_feature", "structural_rib"],
                false,
                true,
                new Vector3d(e, 7401820.0, 112.0),
                new Vector3d(1.5, 9.0, 0.8)));

            var treeE = 501180.0 + i * 65.0;
            var treeN = 7401320.0;
            serraObjects.Add(new WorldBoxObject(
                $"serra_ground_plane_{i:00}",
                "terrain_slab",
                "concrete",
                "serra",
                ["serra_canopy_ground_surface"],
                ["ground", "under_canopy"],
                false,
                true,
                new Vector3d(treeE, treeN, 134.0),
                new Vector3d(20.0, 20.0, 2.0)));

            serraObjects.Add(new WorldBoxObject(
                $"serra_canopy_{i:00}",
                "tree_canopy",
                "vegetation",
                "serra",
                ["serra_canopy_foliage_surface"],
                ["vegetation", "multi_return"],
                false,
                false,
                new Vector3d(treeE, treeN - 12.0, 141.0),
                new Vector3d(12.0, 4.0, 8.0)));

            serraObjects.Add(new WorldBoxObject(
                $"serra_canopy_wall_{i:00}",
                "retaining_slope",
                "concrete",
                "serra",
                ["serra_canopy_ground_surface"],
                ["ground", "under_canopy"],
                false,
                true,
                new Vector3d(treeE, treeN - 24.0, 141.0),
                new Vector3d(18.0, 4.0, 12.0)));

            var boundarySite = SerraBoundarySurvey.Sites[i - 1];
            serraObjects.Add(new WorldBoxObject(
                boundarySite.Id,
                "boundary_monument",
                "concrete",
                "serra",
                ["serra_boundary_surface"],
                ["boundary_divisa", "hist_09_target"],
                false,
                true,
                boundarySite.PublishedPositionEnu,
                new Vector3d(1.0, 1.0, SerraBoundarySurvey.MonumentHeight)));

            serraObjects.Add(new WorldBoxObject(
                $"serra_slope_cut_{i:00}",
                "slope_cut",
                "asphalt",
                "serra",
                ["serra_works_surface"],
                ["earthworks", "slope"],
                false,
                true,
                new Vector3d(501150.0 + i * 75.0, 7401460.0, 140.0 + i * 5.0),
                new Vector3d(16.0, 6.0, 5.0)));
        }

        for (var i = 1; i <= 8; i++)
        {
            var poleE = 501120.0 + i * 55.0;
            const double poleN = 7401180.0;
            var groundZ = SerraBoundarySurvey.CityGroundElevation(poleE, poleN);
            serraObjects.Add(new WorldBoxObject(
                $"serra_utility_pole_{i:00}",
                "utility_pole",
                "metal",
                "serra",
                ["serra_power_corridor"],
                ["power_line", "energy_template"],
                false,
                true,
                new Vector3d(poleE, poleN, groundZ + 2.1),
                new Vector3d(0.7, 0.7, 4.2)));
        }

        foreach (var obj in serraObjects)
        {
            if (seenIds.Add(obj.Id))
                objects.Add(obj);
        }

        return new WorldSceneManifest(
            sceneId: "sao_datum_m6_serra_city",
            mapVersion: act1City.MapVersion + 1,
            datumId: act1City.DatumId,
            origin: act1City.OriginEnu,
            bounds: fullBounds,
            cells: cells.ToArray(),
            objects: objects.ToArray());
    }

    public static IReadOnlyDictionary<string, SerraTerrainCellSpec> BuildSerraTerrainMap()
    {
        var serra = RegionalSectorCatalog.SerraReserved;
        var map = new Dictionary<string, SerraTerrainCellSpec>(SerraSectorsPerAxis * SerraSectorsPerAxis, StringComparer.Ordinal);

        for (var sy = 0; sy < SerraSectorsPerAxis; sy++)
        {
            for (var sx = 0; sx < SerraSectorsPerAxis; sx++)
            {
                var minE = serra.BoundsEnu.Min.X + sx * OldCenterMvpWorldAssembler.SectorSizeMetres;
                var minN = serra.BoundsEnu.Min.Y + sy * OldCenterMvpWorldAssembler.SectorSizeMetres;
                var cellId = $"serra_sector_{sx}_{sy}";

                var (surfaceType, gradePercent, dryGrip, wetGrip, hasCanopy, isTunnel) =
                    (sx, sy) switch
                    {
                        (_, 8) when sx <= 1 => (SerraSurfaceType.TunnelGalleryConnection, 4.0, 0.92, 0.82, false, true),
                        (_, 3) => (SerraSurfaceType.DenseCanopyCorridor, 10.0, 0.82, 0.58, true, false),
                        _ when (sx + sy) >= 9 && sy >= 4 => (SerraSurfaceType.SteepDirtSlope, 16.0, 0.78, 0.52, false, false),
                        _ when sx >= 2 || sy >= 2 => (SerraSurfaceType.DirtTrack, 8.0, 0.85, 0.62, false, false),
                        _ => (SerraSurfaceType.Asphalt, 5.0, 0.96, 0.84, false, false),
                    };

                var elevation = 110.0 + (sx + sy) * 6.5;
                var bounds = new WorldBounds(
                    new Vector3d(minE, minN, serra.BoundsEnu.Min.Z),
                    new Vector3d(
                        minE + OldCenterMvpWorldAssembler.SectorSizeMetres,
                        minN + OldCenterMvpWorldAssembler.SectorSizeMetres,
                        SerraMaxHeightMetres));

                var center = new Vector3d(minE + 50.0, minN + 50.0, elevation);
                var orthoH = LocalGeoidModel.ToOrthometricHeight(center);
                var slopeAngle = Math.Atan(gradePercent / 100.0);

                map[cellId] = new SerraTerrainCellSpec(
                    CellId: cellId,
                    BoundsEnu: bounds,
                    SurfaceType: surfaceType,
                    SlopeGradePercent: gradePercent,
                    SlopeAngleRadians: slopeAngle,
                    DryGripMultiplier: dryGrip,
                    WetGripMultiplier: wetGrip,
                    EllipsoidalHeightMetres: elevation,
                    OrthometricHeightMetres: orthoH,
                    HasTransmissiveCanopy: hasCanopy,
                    IsTunnelInterior: isTunnel);
            }
        }

        return map;
    }

    public static SerraStreamingCaptureSafetyResult EvaluateStreamingCaptureSafety(
        IReadOnlySet<string> loadedCellIds,
        IEnumerable<string> requiredRayCellIds)
    {
        ArgumentNullException.ThrowIfNull(loadedCellIds);
        ArgumentNullException.ThrowIfNull(requiredRayCellIds);

        var missing = requiredRayCellIds
            .Where(id => !string.IsNullOrWhiteSpace(id) && !loadedCellIds.Contains(id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        if (missing.Length == 0)
            return new SerraStreamingCaptureSafetyResult(true, false, null, Array.Empty<string>());

        return new SerraStreamingCaptureSafetyResult(
            CaptureAllowed: false,
            CaptureSuspendedWithWarning: true,
            WarningKey: "world.streaming.cell_not_loaded_capture_suspended",
            MissingCellIds: Array.AsReadOnly(missing));
    }

    private static IReadOnlyList<SerraPickupRouteSpec> BuildPickupRoutes() =>
    [
        new(
            RouteId: "serra_foothill_dirt_route",
            AreaId: "serra_foothill",
            IsCalmSafe: false,
            CalmAlternativeRouteId: "serra_foothill_calm_route",
            OrderedCellIds: ["serra_sector_0_0", "serra_sector_1_1", "serra_sector_2_2", "serra_sector_3_2", "serra_sector_4_4", "serra_sector_5_5"],
            WaypointsEnu: [
                new(501050, 7401050, 110), new(501150, 7401150, 118), new(501250, 7401250, 126),
                new(501350, 7401250, 134), new(501450, 7401450, 150), new(501550, 7401550, 166),
            ],
            MaxSlopeGradePercent: 16.0,
            TotalLengthMetres: 820.0,
            ConnectsToTunnelMouth: false,
            TargetFeatureIds: ["serra_slope_cut_01", "serra_slope_cut_02", "serra_utility_pole_01", "serra_utility_pole_02"]),
        new(
            RouteId: "serra_foothill_calm_route",
            AreaId: "serra_foothill",
            IsCalmSafe: true,
            CalmAlternativeRouteId: null,
            OrderedCellIds: ["serra_sector_0_0", "serra_sector_1_0", "serra_sector_1_1", "serra_sector_2_1", "serra_sector_3_2", "serra_sector_4_3"],
            WaypointsEnu: [
                new(501050, 7401050, 110), new(501150, 7401050, 114), new(501150, 7401150, 118),
                new(501250, 7401150, 122), new(501350, 7401250, 130), new(501450, 7401350, 138),
            ],
            MaxSlopeGradePercent: 8.0,
            TotalLengthMetres: 780.0,
            ConnectsToTunnelMouth: false,
            TargetFeatureIds: ["serra_slope_cut_01", "serra_slope_cut_02", "serra_utility_pole_01", "serra_utility_pole_02"]),
        new(
            RouteId: "serra_ridge_canopy_route",
            AreaId: "serra_canopy_ridge",
            IsCalmSafe: false,
            CalmAlternativeRouteId: "serra_ridge_calm_route",
            OrderedCellIds: ["serra_sector_1_3", "serra_sector_2_3", "serra_sector_3_3", "serra_sector_4_3", "serra_sector_5_3"],
            WaypointsEnu: [
                new(501150, 7401320, 136), new(501250, 7401320, 138), new(501350, 7401320, 142),
                new(501450, 7401320, 146), new(501550, 7401320, 150),
            ],
            MaxSlopeGradePercent: 10.0,
            TotalLengthMetres: 420.0,
            ConnectsToTunnelMouth: false,
            TargetFeatureIds: ["serra_ground_plane_01", "serra_ground_plane_02", "serra_ground_plane_03", "serra_canopy_01", "serra_canopy_02", "serra_canopy_03"]),
        new(
            RouteId: "serra_ridge_calm_route",
            AreaId: "serra_canopy_ridge",
            IsCalmSafe: true,
            CalmAlternativeRouteId: null,
            OrderedCellIds: ["serra_sector_1_2", "serra_sector_2_3", "serra_sector_3_3", "serra_sector_4_3", "serra_sector_5_2"],
            WaypointsEnu: [
                new(501150, 7401250, 132), new(501250, 7401320, 138), new(501350, 7401320, 142),
                new(501450, 7401320, 146), new(501550, 7401250, 148),
            ],
            MaxSlopeGradePercent: 8.0,
            TotalLengthMetres: 460.0,
            ConnectsToTunnelMouth: false,
            TargetFeatureIds: ["serra_ground_plane_01", "serra_ground_plane_02", "serra_ground_plane_03", "serra_canopy_01", "serra_canopy_02", "serra_canopy_03"]),
        new(
            RouteId: "serra_boundary_divisas_route",
            AreaId: "serra_divisas",
            IsCalmSafe: false,
            CalmAlternativeRouteId: "serra_boundary_calm_route",
            OrderedCellIds: ["serra_sector_3_6", "serra_sector_4_6", "serra_sector_5_6", "serra_sector_6_6", "serra_sector_7_6"],
            WaypointsEnu: [
                new(501345, 7401655, 169), new(501430, 7401620, 173), new(501515, 7401655, 177),
                new(501600, 7401620, 181), new(501770, 7401620, 189),
            ],
            MaxSlopeGradePercent: 16.0,
            TotalLengthMetres: 560.0,
            ConnectsToTunnelMouth: false,
            TargetFeatureIds: [
                "serra_divisa_marco_01", "serra_divisa_marco_02", "serra_divisa_marco_03",
                "serra_divisa_marco_04", "serra_divisa_marco_05", "serra_divisa_marco_06",
            ]),
        new(
            RouteId: "serra_boundary_calm_route",
            AreaId: "serra_divisas",
            IsCalmSafe: true,
            CalmAlternativeRouteId: null,
            OrderedCellIds: ["serra_sector_2_5", "serra_sector_3_5", "serra_sector_4_5", "serra_sector_5_5", "serra_sector_6_5"],
            WaypointsEnu: [
                new(501345, 7401580, 165), new(501430, 7401580, 168), new(501515, 7401580, 171),
                new(501600, 7401580, 174), new(501770, 7401580, 178),
            ],
            MaxSlopeGradePercent: 8.0,
            TotalLengthMetres: 580.0,
            ConnectsToTunnelMouth: false,
            TargetFeatureIds: [
                "serra_divisa_marco_01", "serra_divisa_marco_02", "serra_divisa_marco_03",
                "serra_divisa_marco_04", "serra_divisa_marco_05", "serra_divisa_marco_06",
            ]),
        new(
            RouteId: "serra_tunnel_connector_route",
            AreaId: "serra_tunnel",
            IsCalmSafe: true,
            CalmAlternativeRouteId: null,
            OrderedCellIds: ["port_sector_9_8", "serra_sector_0_8", "serra_sector_1_8"],
            WaypointsEnu: [new(500920, 7401820, 105), new(501020, 7401820, 111), new(501120, 7401820, 118)],
            MaxSlopeGradePercent: 4.0,
            TotalLengthMetres: 205.0,
            ConnectsToTunnelMouth: true,
            TargetFeatureIds: ["port_tunnel_mouth_01", "serra_tunnel_east_portal_01", "tunnel_lining_rib_01", "tunnel_lining_rib_02", "tunnel_lining_rib_03"]),
    ];
}
