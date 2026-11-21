using Gsa.Core.Geometry;

namespace Gsa.Core.World;

public sealed record RegionDefinitionSpec(
    string RegionId,
    string DisplayNameKey,
    WorldBounds BoundsEnu,
    double AreaSquareKilometres,
    int SectorsPerAxis,
    IReadOnlyList<string> SurveyMarkIds,
    bool HasRingRoadConnection,
    bool HasAccessibleTunnelMouth);

public sealed record ExpandedPortalDefinition(
    string PortalId,
    string SourceRegionId,
    string TargetRegionId,
    Vector3d PositionEnu,
    double TriggerRadiusMetres,
    string RequiredStoryMissionId,
    int MinimumPlatformLevel = 0,
    bool TunnelTestOverrideAllowed = false);

public sealed record ExpandedPortalAccessResult(
    bool Allowed,
    string PortalId,
    string TargetRegionId,
    string ReasonKey);

public sealed record MvpIdPreservationResult(
    bool Preserved,
    int MvpObjectCount,
    IReadOnlyList<string> MissingOrChangedObjectIds);

/// <summary>Expands Old Center to 1 km², adds Port and Waterfront districts, ring road, and story/tunnel-gated portals while preserving MVP target IDs.</summary>
public static class RegionalSectorCatalog
{
    public const double MvpBaseEast = OldCenterMvpWorldAssembler.MvpOriginEast;
    public const double MvpBaseNorth = OldCenterMvpWorldAssembler.MvpOriginNorth;
    public const double RegionExtentMetres = 1000.0;

    public static RegionDefinitionSpec OldCenter { get; } = new(
        "old_center",
        "region.old_center.name",
        new WorldBounds(
            new Vector3d(MvpBaseEast, MvpBaseNorth, 90.0),
            new Vector3d(MvpBaseEast + RegionExtentMetres, MvpBaseNorth + RegionExtentMetres, 150.0)),
        AreaSquareKilometres: 1.0,
        SectorsPerAxis: 10,
        SurveyMarkIds: ["survey_control_east", "survey_control_west"],
        HasRingRoadConnection: true,
        HasAccessibleTunnelMouth: false);

    public static RegionDefinitionSpec Port { get; } = new(
        "port",
        "region.port.name",
        new WorldBounds(
            new Vector3d(MvpBaseEast, MvpBaseNorth + RegionExtentMetres, 90.0),
            new Vector3d(MvpBaseEast + RegionExtentMetres, MvpBaseNorth + 2.0 * RegionExtentMetres, 150.0)),
        AreaSquareKilometres: 1.0,
        SectorsPerAxis: 10,
        SurveyMarkIds: ["survey_control_port_viaduct", "survey_control_port_tunnel"],
        HasRingRoadConnection: true,
        HasAccessibleTunnelMouth: true);

    public static RegionDefinitionSpec Waterfront { get; } = new(
        "waterfront",
        "region.waterfront.name",
        new WorldBounds(
            new Vector3d(MvpBaseEast + RegionExtentMetres, MvpBaseNorth, 90.0),
            new Vector3d(MvpBaseEast + 2.0 * RegionExtentMetres, MvpBaseNorth + RegionExtentMetres, 150.0)),
        AreaSquareKilometres: 1.0,
        SectorsPerAxis: 10,
        SurveyMarkIds: ["survey_control_waterfront_pier", "survey_control_waterfront_plaza"],
        HasRingRoadConnection: true,
        HasAccessibleTunnelMouth: false);

    public static RegionDefinitionSpec SerraReserved { get; } = new(
        "serra",
        "region.serra.name",
        new WorldBounds(
            new Vector3d(MvpBaseEast + RegionExtentMetres, MvpBaseNorth + RegionExtentMetres, 90.0),
            new Vector3d(MvpBaseEast + 2.0 * RegionExtentMetres, MvpBaseNorth + 2.0 * RegionExtentMetres, 260.0)),
        AreaSquareKilometres: 1.0,
        SectorsPerAxis: 10,
        SurveyMarkIds: ["survey_control_serra_foothill", "survey_control_serra_summit"],
        HasRingRoadConnection: true,
        HasAccessibleTunnelMouth: true);

    public static IReadOnlyList<RegionDefinitionSpec> ActOneExpandedRegions { get; } = [OldCenter, Port, Waterfront];

    public static IReadOnlyList<RegionDefinitionSpec> FullCityFootprint { get; } = [OldCenter, Port, Waterfront, SerraReserved];

    public static IReadOnlyList<Vector3d> RingRoadCorridorLoopEnu { get; } =
    [
        new(500850.0, 7400150.0, 100.0),
        new(501200.0, 7400200.0, 99.0),
        new(501450.0, 7400850.0, 99.5),
        new(500850.0, 7401150.0, 102.0),
        new(500650.0, 7400850.0, 101.0),
        new(500850.0, 7400150.0, 100.0),
    ];

    public static IReadOnlyList<ExpandedPortalDefinition> Portals { get; } =
    [
        new("portal_center_to_port", "old_center", "port", new Vector3d(500300.0, 7401000.0, 100.0), 15.0, "HIST-03", MinimumPlatformLevel: 2),
        new("portal_center_to_waterfront", "old_center", "waterfront", new Vector3d(501000.0, 7400450.0, 100.0), 15.0, "HIST-04", MinimumPlatformLevel: 3),
        new("portal_port_to_waterfront_ring", "port", "waterfront", new Vector3d(500990.0, 7401050.0, 101.0), 18.0, "HIST-04", MinimumPlatformLevel: 3),
        new("portal_port_tunnel_mouth", "port", "port_tunnel_test_section", new Vector3d(500920.0, 7401820.0, 105.0), 14.0, "HIST-05", MinimumPlatformLevel: 4, TunnelTestOverrideAllowed: true),
        new("portal_to_serra", "waterfront", "serra", new Vector3d(501500.0, 7401000.0, 105.0), 18.0, "HIST-07", MinimumPlatformLevel: 5),
    ];

    public static WorldSceneManifest BuildExpandedCityManifest(WorldSceneManifest tutorialManifest)
    {
        ArgumentNullException.ThrowIfNull(tutorialManifest);
        var mvp = OldCenterMvpWorldAssembler.BuildOldCenterMvpManifest(tutorialManifest);

        var combinedBounds = new WorldBounds(
            new Vector3d(MvpBaseEast, MvpBaseNorth, 90.0),
            new Vector3d(MvpBaseEast + 2.0 * RegionExtentMetres, MvpBaseNorth + 2.0 * RegionExtentMetres, 150.0));

        var cells = new List<WorldCellDefinition>(mvp.Cells);
        var seenCells = new HashSet<string>(mvp.Cells.Select(c => c.Id), StringComparer.Ordinal);

        foreach (var region in ActOneExpandedRegions)
        {
            for (var sy = 0; sy < region.SectorsPerAxis; sy++)
            {
                for (var sx = 0; sx < region.SectorsPerAxis; sx++)
                {
                    var minE = region.BoundsEnu.Min.X + sx * OldCenterMvpWorldAssembler.SectorSizeMetres;
                    var minN = region.BoundsEnu.Min.Y + sy * OldCenterMvpWorldAssembler.SectorSizeMetres;
                    var cellId = $"{region.RegionId}_sector_{sx}_{sy}";
                    if (!seenCells.Add(cellId))
                        continue;

                    cells.Add(new WorldCellDefinition(
                        cellId,
                        new WorldBounds(
                            new Vector3d(minE, minN, region.BoundsEnu.Min.Z),
                            new Vector3d(minE + OldCenterMvpWorldAssembler.SectorSizeMetres, minN + OldCenterMvpWorldAssembler.SectorSizeMetres, region.BoundsEnu.Max.Z)),
                        region.RegionId == "waterfront" ? "waterfront_avenue" : "urban"));
                }
            }
        }

        var objects = new List<WorldBoxObject>(mvp.Objects);
        var seenIds = new HashSet<string>(mvp.Objects.Select(o => o.Id), StringComparer.Ordinal);

        WorldBoxObject[] extraObjects =
        [
            new("ring_sign_01", "traffic_sign", "metal", "old_center", ["old_center_ring_surface"], ["sign", "ring_road"], false, true, new Vector3d(500600.0, 7400120.0, 102.0), new Vector3d(0.6, 0.2, 2.4)),
            new("ring_sign_02", "traffic_sign", "metal", "old_center", ["old_center_ring_surface"], ["sign", "ring_road"], false, true, new Vector3d(500850.0, 7400420.0, 102.0), new Vector3d(0.6, 0.2, 2.4)),
            new("ring_sign_03", "traffic_sign", "metal", "old_center", ["old_center_ring_surface"], ["sign", "ring_road"], false, true, new Vector3d(500760.0, 7400720.0, 102.0), new Vector3d(0.6, 0.2, 2.4)),
            new("ring_sign_04", "traffic_sign", "metal", "old_center", ["old_center_ring_surface"], ["sign", "ring_road"], false, true, new Vector3d(500560.0, 7400880.0, 102.0), new Vector3d(0.6, 0.2, 2.4)),
            new("port_warehouse_01", "warehouse_facade", "metal", "port", ["port_warehouse_planes"], ["warehouse", "multipath_risk"], false, true, new Vector3d(500160.0, 7401180.0, 104.0), new Vector3d(28.0, 18.0, 9.0)),
            new("port_warehouse_02", "warehouse_facade", "concrete", "port", ["port_warehouse_planes"], ["warehouse"], false, true, new Vector3d(500320.0, 7401280.0, 104.0), new Vector3d(26.0, 16.0, 8.0)),
            new("port_rail_sign_01", "traffic_sign", "metal", "port", ["port_viaduct_surface"], ["sign", "rail_crossing"], false, true, new Vector3d(500680.0, 7401320.0, 103.0), new Vector3d(0.8, 0.2, 2.6)),
            new("port_tunnel_mouth_01", "tunnel_portal", "concrete", "port", ["port_tunnel_mouth_surface"], ["tunnel_mouth", "gnss_denied"], false, true, new Vector3d(500920.0, 7401820.0, 105.0), new Vector3d(12.0, 6.0, 6.5)),
            new("survey_control_port_viaduct", "survey_control", "concrete", "port", ["port_viaduct_surface"], ["control", "landmark"], false, true, new Vector3d(500620.0, 7401200.0, 100.8), new Vector3d(0.8, 0.8, 0.5)),
            new("survey_control_port_tunnel", "survey_control", "concrete", "port", ["port_tunnel_mouth_surface"], ["control", "landmark"], false, true, new Vector3d(500760.0, 7401740.0, 101.2), new Vector3d(0.8, 0.8, 0.5)),
            new("waterfront_promenade_01", "promenade_curb", "concrete", "waterfront", ["waterfront_promenade_surface"], ["promenade", "water_adjacent"], false, true, new Vector3d(501180.0, 7400220.0, 98.5), new Vector3d(40.0, 3.0, 0.6)),
            new("waterfront_glass_tower_01", "commercial_tower", "glass", "waterfront", ["waterfront_glass_planes"], ["glass_facade", "privacy_review"], false, true, new Vector3d(501680.0, 7400360.0, 112.0), new Vector3d(24.0, 20.0, 28.0)),
            new("waterfront_glass_tower_02", "commercial_tower", "glass", "waterfront", ["waterfront_glass_planes"], ["glass_facade", "privacy_review"], false, true, new Vector3d(501820.0, 7400640.0, 114.0), new Vector3d(24.0, 20.0, 32.0)),
            new("survey_control_waterfront_pier", "survey_control", "concrete", "waterfront", ["waterfront_promenade_surface"], ["control", "landmark"], false, true, new Vector3d(501320.0, 7400310.0, 98.8), new Vector3d(0.8, 0.8, 0.5)),
            new("survey_control_waterfront_plaza", "survey_control", "concrete", "waterfront", ["waterfront_glass_planes"], ["control", "landmark"], false, true, new Vector3d(501610.0, 7400520.0, 100.2), new Vector3d(0.8, 0.8, 0.5)),
        ];

        foreach (var obj in extraObjects.Concat(BusSurveyCorridor.Stops))
        {
            if (seenIds.Add(obj.Id))
                objects.Add(obj);
        }

        return new WorldSceneManifest(
            sceneId: "sao_datum_act1_city",
            mapVersion: mvp.MapVersion + 1,
            datumId: mvp.DatumId,
            origin: mvp.OriginEnu,
            bounds: combinedBounds,
            cells: cells.ToArray(),
            objects: objects.ToArray());
    }

    public static MvpIdPreservationResult VerifyMvpTargetIdsPreserved(
        WorldSceneManifest mvpManifest,
        WorldSceneManifest expandedManifest)
    {
        ArgumentNullException.ThrowIfNull(mvpManifest);
        ArgumentNullException.ThrowIfNull(expandedManifest);

        var byId = expandedManifest.Objects.ToDictionary(o => o.Id, StringComparer.Ordinal);
        var drifted = new List<string>();
        foreach (var item in mvpManifest.Objects)
        {
            if (!byId.TryGetValue(item.Id, out var candidate))
            {
                drifted.Add(item.Id);
                continue;
            }

            if (!string.Equals(item.ObjectClass, candidate.ObjectClass, StringComparison.Ordinal) ||
                !string.Equals(item.Material, candidate.Material, StringComparison.Ordinal) ||
                item.PositionEnu != candidate.PositionEnu)
            {
                drifted.Add(item.Id);
            }
        }

        return new MvpIdPreservationResult(
            Preserved: drifted.Count == 0,
            MvpObjectCount: mvpManifest.Objects.Count,
            MissingOrChangedObjectIds: Array.AsReadOnly(drifted.ToArray()));
    }
}

public static class RegionalAccessGateEvaluator
{
    public static ExpandedPortalAccessResult EvaluatePortalAccess(
        string portalId,
        IReadOnlySet<string> completedStoryMissions,
        int ownedPlatformLevel,
        bool hasActivePortTunnelTestContract = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portalId);
        ArgumentNullException.ThrowIfNull(completedStoryMissions);

        var portal = RegionalSectorCatalog.Portals.FirstOrDefault(p => string.Equals(p.PortalId, portalId, StringComparison.Ordinal));
        if (portal is null)
            return new ExpandedPortalAccessResult(false, portalId, "unknown", "world.portal.unknown");

        // Port tunnel test mouth opens to Level 4 Kombi with a Port tunnel contract before full Serra unlock
        if (portal.TunnelTestOverrideAllowed && ownedPlatformLevel >= 4 && hasActivePortTunnelTestContract)
            return new ExpandedPortalAccessResult(true, portal.PortalId, portal.TargetRegionId, "world.portal.tunnel_test_open");

        if (!completedStoryMissions.Contains(portal.RequiredStoryMissionId))
            return new ExpandedPortalAccessResult(false, portal.PortalId, portal.TargetRegionId, "world.portal.locked_by_campaign");

        if (ownedPlatformLevel < portal.MinimumPlatformLevel)
            return new ExpandedPortalAccessResult(false, portal.PortalId, portal.TargetRegionId, "world.portal.platform_level_insufficient");

        return new ExpandedPortalAccessResult(true, portal.PortalId, portal.TargetRegionId, "world.portal.open");
    }
}
