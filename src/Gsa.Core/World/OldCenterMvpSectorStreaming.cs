using Gsa.Core.Geometry;
using Gsa.Core.Geodesy;

namespace Gsa.Core.World;

public readonly record struct WorldSectorCellId(int SectorX, int SectorY)
{
    public string WireId => $"sector_{SectorX}_{SectorY}";
}

public sealed record OldCenterTargetInventorySummary(
    int FacadeCount,
    int ManholeCount,
    int SignCount,
    int SidewalkSegmentCount,
    int OfficeCount,
    int UnloadPointCount,
    int SurveyControlMarkCount,
    bool HasTrainingPark,
    bool HasWifiZone,
    bool HasCalmArea)
{
    public bool MeetsMvpRequirements =>
        FacadeCount >= 20 &&
        ManholeCount >= 12 &&
        SignCount >= 12 &&
        SidewalkSegmentCount >= 8 &&
        OfficeCount >= 1 &&
        UnloadPointCount >= 2 &&
        SurveyControlMarkCount >= 2 &&
        HasTrainingPark &&
        HasWifiZone &&
        HasCalmArea;
}

public sealed record PortalGateDefinition(
    string PortalId,
    string TargetRegionId,
    Vector3d PositionEnu,
    double TriggerRadiusMetres,
    string RequiredStoryMissionId);

public sealed record PortalAccessEvaluation(
    bool Allowed,
    string PortalId,
    string TargetRegionId,
    string ReasonKey);

public sealed record SectorStreamingEvaluation(
    bool CaptureAllowed,
    bool SuspendedByUnloadedSafetyBand,
    WorldSectorCellId PlayerSector,
    IReadOnlyList<WorldSectorCellId> LoadedSectors,
    IReadOnlyList<WorldSectorCellId> MissingSafetyBandSectors,
    IReadOnlyList<WorldBoxObject> ActiveObjects,
    string? WarningKey);

public sealed record StreamedTargetLookupResult(
    bool Available,
    bool SuspendedByStreaming,
    WorldBoxObject? TargetObject,
    string? ReasonKey);

/// <summary>Builds the 500x500 m Old Center MVP semantic scene while preserving all tutorial IDs intact.</summary>
public static class OldCenterMvpWorldAssembler
{
    public const double MvpOriginEast = 500000.0;
    public const double MvpOriginNorth = 7400000.0;
    public const double MvpExtentMetres = 500.0;
    public const double SectorSizeMetres = 100.0;
    public const double DrainageSurveyNorth = 7400140.0;
    public const double AvenueSignsNorth = 7400362.4;
    public const double SidewalkSurveyNorth = 7400310.0;

    public static bool OverlapsSurveyCorridor(double east, double north, double width, double depth)
    {
        if (east + width / 2 < 500008 || east - width / 2 > 500420)
            return false;
        const double reserve = 9;
        return Math.Abs(north - DrainageSurveyNorth) < depth / 2 + reserve ||
               Math.Abs(north - SidewalkSurveyNorth) < depth / 2 + reserve ||
               Math.Abs(north - AvenueSignsNorth) < depth / 2 + reserve;
    }

    public static WorldSceneManifest BuildOldCenterMvpManifest(WorldSceneManifest tutorialManifest)
    {
        ArgumentNullException.ThrowIfNull(tutorialManifest);

        var mvpBounds = new WorldBounds(
            new Vector3d(MvpOriginEast, MvpOriginNorth, 95.0),
            new Vector3d(MvpOriginEast + MvpExtentMetres, MvpOriginNorth + MvpExtentMetres, 140.0));

        var cells = new List<WorldCellDefinition>(25);
        for (var sy = 0; sy < 5; sy++)
        {
            for (var sx = 0; sx < 5; sx++)
            {
                var minE = MvpOriginEast + sx * SectorSizeMetres;
                var minN = MvpOriginNorth + sy * SectorSizeMetres;
                var cellBounds = new WorldBounds(
                    new Vector3d(minE, minN, 95.0),
                    new Vector3d(minE + SectorSizeMetres, minN + SectorSizeMetres, 140.0));
                cells.Add(new WorldCellDefinition($"old_center_sector_{sx}_{sy}", cellBounds, "urban"));
            }
        }

        var objectsById = new Dictionary<string, WorldBoxObject>(StringComparer.Ordinal);
        var objects = new List<WorldBoxObject>(tutorialManifest.Objects.Count + 40);
        foreach (var existing in tutorialManifest.Objects)
        {
            if (objectsById.TryAdd(existing.Id, existing))
                objects.Add(existing);
        }

        void AddObject(WorldBoxObject item)
        {
            if (objectsById.TryAdd(item.Id, item))
                objects.Add(item);
        }

        for (var i = 1; i <= 12; i++)
        {
            var e = 500020.0 + (i - 1) * 35.0;
            AddObject(new WorldBoxObject(
                $"manhole_{i:00}",
                "manhole",
                "metal",
                "old_center",
                ["commerce_drainage_surface"],
                ["drainage", "manhole", "inventory_target"],
                dynamic: false,
                collision: false,
                new Vector3d(e, DrainageSurveyNorth, 100.0),
                new Vector3d(1.0, 1.0, 0.2)));
        }

        for (var i = 1; i <= 12; i++)
        {
            var e = 500132.0 + (i - 1) * 28.0;
            AddObject(new WorldBoxObject(
                $"sign_{i:00}",
                "traffic_sign",
                "metal",
                "old_center",
                ["commerce_sign_planes"],
                ["sign", "inventory_target"],
                dynamic: false,
                collision: true,
                new Vector3d(e, AvenueSignsNorth, 101.95),
                new Vector3d(0.85, 0.14, 0.65)));
        }

        for (var i = 1; i <= 8; i++)
        {
            var e = 500040.0 + (i - 1) * 52.0;
            var segmentId = $"sidewalk_seg_{i:00}";
            const double north = SidewalkSurveyNorth;
            const double pavementElevation = 100;
            var width = 2.5 + (i % 4) * .25;
            var stepHeight = .12 + (i % 4) * .04;
            AddObject(new WorldBoxObject(
                segmentId, "sidewalk_segment", "concrete", "old_center",
                ["commerce_sidewalk_surface"], ["sidewalk", "accessibility", "local_control"],
                dynamic: false, collision: false,
                new Vector3d(e, north, pavementElevation + stepHeight / 2),
                new Vector3d(20, width, stepHeight)));

            // Targets have the same vertical offset above the points they mark.
            // The offset cancels in the step measurement. The foot and top are
            // separated along the curb so they can be distinguished in an image.
            const double targetHeight = .14;
            var edge = north - width / 2;
            AddMark("curb", new(e, edge, pavementElevation + stepHeight + targetHeight));
            AddMark("back", new(e, north + width / 2, pavementElevation + stepHeight + targetHeight));
            AddMark("foot", new(e + .55, edge - .25, pavementElevation + targetHeight));

            // Published control coordinates are distinct from the points that are measured and checked.
            AddControl("a", new(e - 1.25, edge, pavementElevation + .44));
            AddControl("b", new(e + 1.55, edge - .4, pavementElevation + .38));
            AddControl("c", new(e + .85, north + width / 2, pavementElevation + stepHeight + .68));

            void AddControl(string suffix, Vector3d position) => AddObject(new WorldBoxObject(
                segmentId + "_control_" + suffix, "sidewalk_control_mark", "metal", "old_center",
                [], ["published_control"], dynamic: false, collision: false, position, new Vector3d(.34, .045, .34)));

            void AddMark(string suffix, Vector3d position) => AddObject(new WorldBoxObject(
                segmentId + "_" + suffix, "sidewalk_survey_mark", "metal", "old_center",
                ["commerce_sidewalk_surface"], ["sidewalk_measurement"], dynamic: false, collision: false,
                position, new Vector3d(.40, .045, .32)));
        }

        AddObject(new WorldBoxObject(
            "training_park",
            "training_park",
            "vegetation",
            "old_center",
            ["old_center_park_surface"],
            ["training_park", "park", "calm_area"],
            dynamic: false,
            collision: false,
            new Vector3d(500350.0, 7400380.0, 100.2),
            new Vector3d(120.0, 120.0, 0.4)));

        for (var i = 1; i <= 8; i++)
        {
            var east = 500310.0 + ((i - 1) % 4) * 25;
            var north = i <= 4 ? 7400397.0 : 7400423.0;
            AddObject(new WorldBoxObject($"park_bench_{i:00}", "park_bench", "wood", "old_center",
                ["central_park_planes"], ["inventory_target"], dynamic: false, collision: true,
                new Vector3d(east, north, 100.65), new Vector3d(2, .25, .9)));
        }

        AddObject(new WorldBoxObject(
            "unload_point_north",
            "unload_point",
            "concrete",
            "old_center",
            ["old_center_unload_north"],
            ["unload_point", "safe_zone"],
            dynamic: false,
            collision: false,
            new Vector3d(500440.0, 7400440.0, 100.5),
            new Vector3d(4.0, 4.0, 1.0)));

        AddObject(new WorldBoxObject(
            "wifi_zone_office",
            "wifi_hotspot",
            "metal",
            "old_center",
            ["old_center_wifi_surface"],
            ["wifi", "fictional_network"],
            dynamic: false,
            collision: false,
            new Vector3d(500232.0, 7400004.0, 102.0),
            new Vector3d(8.0, 8.0, 2.0)));

        AddObject(new WorldBoxObject(
            "calm_zone_park",
            "calm_zone",
            "concrete",
            "old_center",
            ["old_center_calm_surface"],
            ["calm_area", "quiet_zone"],
            dynamic: false,
            collision: false,
            new Vector3d(500350.0, 7400460.0, 100.2),
            new Vector3d(60.0, 40.0, 0.4)));

        return new WorldSceneManifest(
            "old_center_mvp",
            Math.Max(1, tutorialManifest.MapVersion),
            LocalMapFrame.DatumId,
            LocalMapFrame.Default.Origin,
            mvpBounds,
            cells.ToArray(),
            objects.ToArray());
    }

    public static OldCenterTargetInventorySummary SummarizeInventory(WorldSceneManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var facadeCount = manifest.Objects.Count(o => string.Equals(o.ObjectClass, "building_facade", StringComparison.Ordinal));
        var manholeCount = manifest.Objects.Count(o => string.Equals(o.ObjectClass, "manhole", StringComparison.Ordinal));
        var signCount = manifest.Objects.Count(o => string.Equals(o.ObjectClass, "traffic_sign", StringComparison.Ordinal));
        var sidewalkCount = manifest.Objects.Count(o => string.Equals(o.ObjectClass, "sidewalk_segment", StringComparison.Ordinal));
        var officeCount = manifest.Objects.Count(o => string.Equals(o.ObjectClass, "office_entry", StringComparison.Ordinal));
        var unloadCount = manifest.Objects.Count(o => string.Equals(o.ObjectClass, "unload_point", StringComparison.Ordinal));
        var markCount = manifest.Objects.Count(o => string.Equals(o.ObjectClass, "survey_control", StringComparison.Ordinal));
        var hasPark = manifest.Objects.Any(o => string.Equals(o.ObjectClass, "training_park", StringComparison.Ordinal) || o.Tags.Contains("training_park"));
        var hasWifi = manifest.Objects.Any(o => string.Equals(o.ObjectClass, "wifi_hotspot", StringComparison.Ordinal) || o.Tags.Contains("wifi"));
        var hasCalm = manifest.Objects.Any(o => string.Equals(o.ObjectClass, "calm_zone", StringComparison.Ordinal) || o.Tags.Contains("calm_area"));

        return new OldCenterTargetInventorySummary(
            facadeCount,
            manholeCount,
            signCount,
            sidewalkCount,
            officeCount,
            unloadCount,
            markCount,
            hasPark,
            hasWifi,
            hasCalm);
    }
}

/// <summary>100x100 m sector streamer enforcing a capture safety band and campaign-gated regional portals.</summary>
public sealed class OldCenterSectorStreamer
{
    public const double DefaultActiveRadiusMetres = 600.0;
    public const double DefaultSafetyBandRadiusMetres = 120.0;

    private readonly WorldSceneManifest _manifest;
    private readonly double _sectorSizeMetres;
    private readonly double _activeRadiusMetres;
    private readonly double _safetyBandRadiusMetres;
    private readonly Dictionary<WorldSectorCellId, List<WorldBoxObject>> _objectsBySector = new();
    private readonly List<WorldSectorCellId> _allSectors = [];
    private readonly Dictionary<string, PortalGateDefinition> _portalsById;

    public IReadOnlyList<PortalGateDefinition> Portals { get; }

    public OldCenterSectorStreamer(
        WorldSceneManifest manifest,
        double activeRadiusMetres = DefaultActiveRadiusMetres,
        double safetyBandRadiusMetres = DefaultSafetyBandRadiusMetres)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (!double.IsFinite(activeRadiusMetres) || activeRadiusMetres <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(activeRadiusMetres));
        if (!double.IsFinite(safetyBandRadiusMetres) || safetyBandRadiusMetres <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(safetyBandRadiusMetres));

        _manifest = manifest;
        _sectorSizeMetres = OldCenterMvpWorldAssembler.SectorSizeMetres;
        _activeRadiusMetres = activeRadiusMetres;
        _safetyBandRadiusMetres = safetyBandRadiusMetres;

        var minSx = ToSectorCoord(manifest.Bounds.Min.X, manifest.Bounds.Min.X);
        var maxSx = ToSectorCoord(manifest.Bounds.Max.X - 1e-6, manifest.Bounds.Min.X);
        var minSy = ToSectorCoord(manifest.Bounds.Min.Y, manifest.Bounds.Min.Y);
        var maxSy = ToSectorCoord(manifest.Bounds.Max.Y - 1e-6, manifest.Bounds.Min.Y);

        for (var sy = minSy; sy <= maxSy; sy++)
        {
            for (var sx = minSx; sx <= maxSx; sx++)
            {
                var cell = new WorldSectorCellId(sx, sy);
                _allSectors.Add(cell);
                _objectsBySector[cell] = [];
            }
        }

        foreach (var obj in manifest.Objects)
        {
            var halfX = obj.SizeEnu.X * 0.5;
            var halfY = obj.SizeEnu.Y * 0.5;
            var objMinSx = Math.Clamp(ToSectorCoord(obj.PositionEnu.X - halfX, manifest.Bounds.Min.X), minSx, maxSx);
            var objMaxSx = Math.Clamp(ToSectorCoord(obj.PositionEnu.X + halfX, manifest.Bounds.Min.X), minSx, maxSx);
            var objMinSy = Math.Clamp(ToSectorCoord(obj.PositionEnu.Y - halfY, manifest.Bounds.Min.Y), minSy, maxSy);
            var objMaxSy = Math.Clamp(ToSectorCoord(obj.PositionEnu.Y + halfY, manifest.Bounds.Min.Y), minSy, maxSy);

            for (var sy = objMinSy; sy <= objMaxSy; sy++)
            {
                for (var sx = objMinSx; sx <= objMaxSx; sx++)
                {
                    _objectsBySector[new WorldSectorCellId(sx, sy)].Add(obj);
                }
            }
        }

        Portals =
        [
            new("portal_porto", "porto", new Vector3d(500250.0, 7400500.0, 100.0), 12.0, "HIST-04"),
            new("portal_orla", "orla", new Vector3d(500500.0, 7400250.0, 100.0), 12.0, "HIST-05"),
            new("portal_serra", "serra", new Vector3d(500500.0, 7400500.0, 100.0), 12.0, "HIST-07"),
        ];
        _portalsById = Portals.ToDictionary(p => p.PortalId, StringComparer.Ordinal);
    }

    public WorldSectorCellId GetSectorForPosition(Vector3d positionEnu) =>
        new(
            ToSectorCoord(positionEnu.X, _manifest.Bounds.Min.X),
            ToSectorCoord(positionEnu.Y, _manifest.Bounds.Min.Y));

    public SectorStreamingEvaluation UpdateStreamingWindow(
        Vector3d playerPositionEnu,
        IReadOnlySet<WorldSectorCellId>? forcedUnloadedSectors = null)
    {
        if (!double.IsFinite(playerPositionEnu.X) || !double.IsFinite(playerPositionEnu.Y) || !double.IsFinite(playerPositionEnu.Z))
            throw new ArgumentOutOfRangeException(nameof(playerPositionEnu));

        var playerSector = GetSectorForPosition(playerPositionEnu);
        var loaded = new List<WorldSectorCellId>();
        var missingSafety = new List<WorldSectorCellId>();
        var activeById = new Dictionary<string, WorldBoxObject>(StringComparer.Ordinal);

        foreach (var sector in _allSectors)
        {
            var dist = DistanceToSectorAabb2D(playerPositionEnu, sector);
            var isForcedUnloaded = forcedUnloadedSectors is not null && forcedUnloadedSectors.Contains(sector);

            if (dist <= _activeRadiusMetres && !isForcedUnloaded)
            {
                loaded.Add(sector);
                foreach (var obj in _objectsBySector[sector])
                    activeById.TryAdd(obj.Id, obj);
            }
            else if (dist <= _safetyBandRadiusMetres)
            {
                missingSafety.Add(sector);
            }
        }

        var suspended = missingSafety.Count > 0;
        var orderedActive = activeById.Values.OrderBy(o => o.Id, StringComparer.Ordinal).ToArray();
        return new SectorStreamingEvaluation(
            CaptureAllowed: !suspended,
            SuspendedByUnloadedSafetyBand: suspended,
            PlayerSector: playerSector,
            LoadedSectors: Array.AsReadOnly(loaded.ToArray()),
            MissingSafetyBandSectors: Array.AsReadOnly(missingSafety.ToArray()),
            ActiveObjects: Array.AsReadOnly(orderedActive),
            WarningKey: suspended ? "world.streaming.safety_band_unloaded" : null);
    }

    public static StreamedTargetLookupResult TryObserveTarget(string targetId, SectorStreamingEvaluation streaming)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        ArgumentNullException.ThrowIfNull(streaming);

        if (streaming.SuspendedByUnloadedSafetyBand)
        {
            return new StreamedTargetLookupResult(
                Available: false,
                SuspendedByStreaming: true,
                TargetObject: null,
                ReasonKey: streaming.WarningKey ?? "world.streaming.safety_band_unloaded");
        }

        var hit = streaming.ActiveObjects.FirstOrDefault(o => string.Equals(o.Id, targetId, StringComparison.Ordinal));
        return hit is not null
            ? new StreamedTargetLookupResult(true, false, hit, null)
            : new StreamedTargetLookupResult(false, false, null, "world.target.not_present_in_loaded_window");
    }

    public PortalAccessEvaluation EvaluatePortalAccess(string portalId, IReadOnlySet<string> completedMissions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portalId);
        ArgumentNullException.ThrowIfNull(completedMissions);

        if (!_portalsById.TryGetValue(portalId, out var portal))
            return new PortalAccessEvaluation(false, portalId, "unknown", "world.portal.unknown");

        var allowed = completedMissions.Contains(portal.RequiredStoryMissionId);
        return new PortalAccessEvaluation(
            allowed,
            portal.PortalId,
            portal.TargetRegionId,
            allowed ? "world.portal.open" : "world.portal.locked_by_campaign");
    }

    private int ToSectorCoord(double value, double origin) =>
        (int)Math.Floor((value - origin) / _sectorSizeMetres);

    private double DistanceToSectorAabb2D(Vector3d point, WorldSectorCellId sector)
    {
        var minX = _manifest.Bounds.Min.X + sector.SectorX * _sectorSizeMetres;
        var maxX = minX + _sectorSizeMetres;
        var minY = _manifest.Bounds.Min.Y + sector.SectorY * _sectorSizeMetres;
        var maxY = minY + _sectorSizeMetres;

        var dx = point.X < minX ? minX - point.X : (point.X > maxX ? point.X - maxX : 0.0);
        var dy = point.Y < minY ? minY - point.Y : (point.Y > maxY ? point.Y - maxY : 0.0);
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

