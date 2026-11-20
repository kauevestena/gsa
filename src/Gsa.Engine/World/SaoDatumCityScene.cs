using Gsa.Core.Geodesy;
using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Camera;
using Gsa.Core.Sensors.Gnss;
using Gsa.Core.Sensors.Lidar;
using Gsa.Core.Traffic;
using Gsa.Core.World;
using Gsa.Engine.Audio;
using Gsa.Engine.Diagnostics;
using Gsa.Engine.Localization;
using Gsa.Engine.Mobility;
using Gsa.Engine.Sensors;
using Gsa.Engine.Traffic;
using Gsa.Engine.UI;
using Gsa.Engine.Visuals;
using Gsa.Missions.Activities;
using Gsa.Missions.Campaign;
using Gsa.Missions.Contracts;
using Gsa.Missions.Economy;
using Gsa.Missions.Gameplay;
using Gsa.Missions.Office;
using Gsa.Missions.Persistence;
using Gsa.Missions.Presentation;
using Gsa.Missions.Quality;
using Godot;
using Dictionary = Godot.Collections.Dictionary;

namespace Gsa.Engine.World;

/// <summary>Full 4 km² São Datum open-world GTA III city supporting Career progression and Free Roam Sandbox.</summary>
public partial class SaoDatumCityScene : Node3D
{
    public const string SandboxModeKey = "gsa_launch_mode"; // "career" or "sandbox"

    private const string TutorialManifestPath = "res://world/tutorial-block.json";
    private const string SkyCachePath = "res://world/tutorial-sky.json";
    private const int MaxLidarDisplayPoints = 18000;
    private const double SouthAccessRoadMinimumNorth = 7399982.0;

    private readonly GodotCoordinateAdapter _coordinates = new();
    private readonly SemanticRegistry _registry = new();
    private WorldSceneManifest _tutorialManifest = null!;
    private WorldSceneManifest _fullManifest = null!;
    private FullCityGeodeticNetwork _geodeticNetwork = null!;
    private OldCenterSectorStreamer _streamer = null!;
    private SkyCacheAdapter? _skyCache;

    private Node3D _worldRoot = null!;
    private Node3D _streamedBlocksRoot = null!;
    private Node3D _collectiblesRoot = null!;
    private Node3D _benchRoot = null!;
    private readonly HashSet<WorldSectorCellId> _builtSectors = [];
    private readonly Dictionary<WorldSectorCellId, Node3D> _sectorNodes = new();
    private readonly Dictionary<WorldSectorCellId, List<ObstacleBoxEnu>> _sectorObstacles = new();
    private readonly Dictionary<string, Node3D> _collectibleNodes = new();
    private Dictionary<string, WorldBoxObject> _manifestObjectsById = new(StringComparer.Ordinal);
    private WorldSectorCellId? _lastStreamingSector;

    private GsaAtmosphereController _atmosphere = null!;
    private GsaCityVisualBuilder _cityVisuals = null!;
    private GsaCityLandmarksBuilder _landmarks = new();
    private GsaThirdPersonCamera _chaseCamera = null!;
    private GsaProceduralAudio _audio = null!;
    private GsaGtaHud _gtaHud = null!;

    private MobilityBenchAdapter _mobility = null!;
    private TrafficBenchAdapter _traffic = null!;
    private LocomotionSimulator _locomotion = null!;
    private CarSuspensionSimulator? _carSim;
    private GimbalinaStabilizer _gimbalina = new();
    private WorldCalendarAndWeatherController _calendar = new();
    private CalmPolicyCoordinator _calmPolicy = new();
    private TerritoryAttentionTracker _attention = new();
    private readonly AbsurdUrbanEventsCoordinator _absurdEvents = new();
    private readonly RoadNetworkGraph _eventRoadGraph = AbsurdUrbanEventsCoordinator.CreateDetourAndBottleneckTestGraph();
    private readonly CampaignSaveProgressState _campaignState = new();
    private readonly List<(LocomotionMode Mode, Vector3d PosEnu, double Heading, Node3D Root)> _worldParkedVehicles = [];
    private CampaignMissionFullSpec? _activeStorySpec;
    private bool _disguiseActive;
    private double _hazardCooldown;
    private double _nextAbsurdEventTimer = 18.0;
    private bool _chickenChallengeActive;
    private double _chickenChallengeTime;
    private int _chickenChallengeSamples;
    private int _chickenChallengeSteady;
    private LidarBenchSimulator _lidarSim = new(LidarProfile.L4);
    private CameraCaptureScheduler _camScheduler = new("city_cam");

    // Live 3D LiDAR point cloud MultiMesh ring buffer
    private MultiMeshInstance3D _lidarCloudMesh = null!;
    private int _lidarPointWriteIndex;
    private int _lidarPointVisibleCount;
    private int _lidarSliceIndex;
    private readonly List<LidarMeasuredReturn> _lidarSliceScratch = new(320);
    private bool _lidarEnabled = true;

    // Career, Contracts, Economy, Collectibles & Save State
    private bool _isSandbox;
    private CareerLedger _ledger = new(250);
    private CareerProgressionState _progression = new();
    private FleetProgressionManager _fleet = new();
    private GeopediaCatalogCoordinator _geopedia = new();
    private MvpCollectiblesCoordinator _collectibles = new();
    private IReadOnlyList<GoldenEggSpot> _activeEggs = [];
    private readonly List<CameraObservationSample> _fieldSamples = [];
    private readonly List<Vector3d> _coverageSpots = [];
    private readonly HashSet<string> _observedTargetIds = new(StringComparer.Ordinal);
    private ContractDefinition? _activeContract;
    private ContractStateMachine? _activeMachine;
    private ProcessingMode _officeMode = ProcessingMode.Arcade;
    private bool _rackMisaligned;
    private bool _privacyMasksApplied = true;
    private double _benchmarkHoldSeconds;
    private string? _holdingBenchmarkId;

    private ObstacleBoxEnu[] _manifestObstacles = [];
    private CameraVisibilityCandidate[] _liveCandidates = [];
    private readonly List<ObstacleBoxEnu> _cityBlockObstacles = [];
    private readonly List<ObstacleBoxEnu> _nearbyObstacles = new(32);
    private Vector3d _nearbyObstacleAnchor;
    private bool _hasNearbyObstacleAnchor;
    private PhysicsRayQueryParameters3D? _lidarRayQuery;
    private long _tick;
    private double _hudElapsed;
    private double _lastSpeedMps;
    private string _feedbackText = "";
    private string _lastRegionId = "";
    private string? _pendingDistrictPopup;
    private string? _pendingVehiclePopup;
    private bool _captureRequested;
    private SaveGameManager _saveManager = null!;
    private GsaPerfProbe? _perfProbe;

    private static string Text(string key) => TranslationServer.Translate(key).ToString();

    public override void _Ready()
    {
        try
        {
            LocalizationService.EnsureCatalogsLoaded();
            _isSandbox = OS.HasEnvironment("GSA_SANDBOX") ||
                         (GetTree().Root.HasMeta(SandboxModeKey) && GetTree().Root.GetMeta(SandboxModeKey).AsString() == "sandbox");
            _perfProbe = GsaPerfProbe.TryCreateFromArgs(this, _isSandbox ? "sao_datum_sandbox" : "sao_datum_city");

            _tutorialManifest = WorldSceneManifest.Parse(Godot.FileAccess.GetFileAsString(TutorialManifestPath));
            if (Godot.FileAccess.FileExists(SkyCachePath))
                _skyCache = new SkyCacheAdapter(_tutorialManifest, Godot.FileAccess.GetFileAsString(SkyCachePath));

            (_fullManifest, _geodeticNetwork, _) = FullCityGeodeticNetwork.AssembleFullCity(_tutorialManifest);
            _manifestObjectsById = _fullManifest.Objects.ToDictionary(o => o.Id, StringComparer.Ordinal);
            _streamer = new OldCenterSectorStreamer(_fullManifest, activeRadiusMetres: 380.0, safetyBandRadiusMetres: 140.0);

            _citySaveSmoke = OS.GetCmdlineUserArgs().Contains("--city-save-smoke");
            if (_citySaveSmoke) _isSandbox = false;
            var saveDir = _citySaveSmoke
                ? (_citySmokeDirectory = Path.Combine(Path.GetTempPath(), "gsa-city-save-smoke-" + Guid.NewGuid().ToString("N")))
                : ProjectSettings.GlobalizePath(_isSandbox ? "user://sandbox-session" : "user://career-main");
            _saveManager = new SaveGameManager(saveDir);

            BuildCityEnvironment();
            BuildSemanticWorld();
            BuildCollectiblesAndLandmarks();
            BuildPlayerAndTraffic();
            BuildCityEverydayActors();
            BuildLidarPointCloudOverlay();
            BuildHudAndTablet();

            if (_isSandbox)
            {
                SetupSandboxMode();
            }
            else
            {
                TryLoadCareer();
            }

            UpdateSectorStreaming(_locomotion.PositionEnu);
            SnapCamera();
            SetPhysicsProcess(true);
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    private void SetupSandboxMode()
    {
        _ledger = new CareerLedger(99500);
        foreach (var mode in new[] { LocomotionMode.Bicycle, LocomotionMode.Scooter, LocomotionMode.Car, LocomotionMode.Kombi, LocomotionMode.Pickup, LocomotionMode.Van })
        {
            _fleet.TryPurchasePlatform($"sandbox_{mode}", 0.0, mode, new CareerLedger(999999),
                CareerProgressionState.FromSnapshot(new CareerProgressionSnapshot(1, 50000,
                    new Dictionary<string, int> { ["city_hall"] = 100 },
                    new Dictionary<string, int> { ["old_center"] = 100 },
                    ["HIST-01", "HIST-02", "HIST-03", "HIST-04", "HIST-05", "HIST-07", "HIST-10"], [])),
                new HashSet<string>(["HIST-01", "HIST-02", "HIST-03", "HIST-04", "HIST-05", "HIST-07", "HIST-10"]),
                out _, out _);
        }
        var streetSpawn = new Vector3d(500208.0, 7399992.5, 100.0);
        _locomotion.RepositionSafeAfterRecovery(streetSpawn, LocomotionMode.Car, Math.PI, refillAutonomy: true, clearStorage: false);
        SwitchPlatform(LocomotionMode.Car);
        if (OS.GetCmdlineUserArgs().Contains("--sandbox-kombi-lidar"))
        {
            SwitchPlatform(LocomotionMode.Kombi);
            _calendar.SetWeather(true, 0.75);
        }
        else if (OS.GetCmdlineUserArgs().Contains("--sandbox-van-night"))
        {
            SwitchPlatform(LocomotionMode.Van);
            _calendar.AdvanceCalendarSeconds(14 * 3600.0); // 22:00 night
        }
        else if (OS.GetCmdlineUserArgs().Contains("--sandbox-waterfront"))
        {
            _carSim = null;
            _locomotion.RepositionSafeAfterRecovery(new Vector3d(501715.0, 7400448.0, 100.0), LocomotionMode.Pickup, 0.0, refillAutonomy: true, clearStorage: false);
            SwitchPlatform(LocomotionMode.Pickup);
            _calendar.AdvanceCalendarSeconds(10 * 3600.0); // 18:00 golden sunset
        }
        else if (OS.GetCmdlineUserArgs().Contains("--sandbox-port"))
        {
            _carSim = null;
            _locomotion.RepositionSafeAfterRecovery(new Vector3d(500360.0, 7401620.0, 100.0), LocomotionMode.Kombi, Math.PI * 0.5, refillAutonomy: true, clearStorage: false);
            SwitchPlatform(LocomotionMode.Kombi);
        }
        else if (OS.GetCmdlineUserArgs().Contains("--sandbox-serra"))
        {
            _carSim = null;
            var sPos = new Vector3d(501460.0, 7401460.0, GetTerrainElevationEnu(501460.0, 7401460.0));
            _locomotion.RepositionSafeAfterRecovery(sPos, LocomotionMode.Pickup, Math.PI * 0.25, refillAutonomy: true, clearStorage: false);
            SwitchPlatform(LocomotionMode.Pickup);
        }
        _feedbackText = Text("city.sandbox.welcome");
    }

    private void BuildCityEnvironment()
    {
        _atmosphere = new GsaAtmosphereController(this, enableRetroDither: false);
        _cityVisuals = new GsaCityVisualBuilder();

        var visualBase = new Node3D { Name = "CityBaseVisuals" };
        AddChild(visualBase);

        // Full 4 km² terrain & regional decks (world-scale UVs, zero triplanar shader cost)
        var cityGround = new MeshInstance3D
        {
            Name = "CityAsphaltBase",
            Position = _coordinates.ToGodot(new Vector3d(501000, 7401000, 99.45)),
            Mesh = GsaMeshBuilder.CreateWorldUvBoxMesh(new Vector3(2400f, 0.25f, 2400f), 0.12f),
            MaterialOverride = GsaRetroTextures.CreateWorldMaterial(
                "city_base_asphalt_uv", "asphalt", new Color(0.84f, 0.86f, 0.90f), triplanar: false),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        visualBase.AddChild(cityGround);

        // Central Park grass deck (500260..500420, 7400280..7400440)
        var parkMat = GsaRetroTextures.CreateWorldMaterial("city_park_grass_uv", "park_grass", Colors.White, triplanar: false);
        visualBase.AddChild(new MeshInstance3D
        {
            Name = "CentralParkLawn",
            Position = _coordinates.ToGodot(new Vector3d(500350, 7400380, 99.78)),
            Mesh = GsaMeshBuilder.CreateWorldUvBoxMesh(new Vector3(130f, 0.3f, 130f), 0.10f),
            MaterialOverride = parkMat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        var parkTrees = new List<(Vector3 Position, float Scale)>(25);
        for (var px = 500300.0; px <= 500400.0; px += 25.0)
        {
            for (var py = 7400330.0; py <= 7400430.0; py += 25.0)
                parkTrees.Add((_coordinates.ToGodot(new Vector3d(px, py, 99.9)), 1.15f));
        }
        GsaMeshBuilder.BuildBatchedStreetTrees(visualBase, "ParkTrees", parkTrees);

        // Waterfront Ocean Bay (East side E > 501850) & Promenade boardwalk
        var oceanMat = GsaRetroTextures.CreateWorldMaterial(
            "city_ocean_water_uv", "water", new Color(0.75f, 0.90f, 1.0f), roughness: 0.14f, triplanar: false);
        visualBase.AddChild(new MeshInstance3D
        {
            Name = "OceanBay",
            Position = _coordinates.ToGodot(new Vector3d(502250, 7400500, 97.6)),
            Mesh = GsaMeshBuilder.CreateWorldUvBoxMesh(new Vector3(950f, 0.4f, 1400f), 0.08f),
            MaterialOverride = oceanMat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });

        // Serra Mountain Smooth Highway, City Hall Monument, Central Park Fountain, Viaduct, Port Cranes/Ship, Waterfront Bridge/Pier & Hazards!
        _landmarks.BuildCityLandmarks(visualBase, _coordinates, _atmosphere);

        // Commerce Street promenade, storefronts, curbs, signals, streetlamps & water tanks
        _cityVisuals.BuildCommerceStreetDetails(visualBase, _fullManifest, _coordinates, _atmosphere, includeBeacons: false);
        BuildWorldParkedVehicles(visualBase);

        // Glowing GTA III Beacons for Office, Unload, and 8 Geodetic Benchmarks
        foreach (var id in new[] { "office_entry", "unload_point", "unload_point_north" })
        {
            var obj = _fullManifest.Objects.FirstOrDefault(o => o.Id == id);
            if (obj is null) continue;
            var col = id == "office_entry" ? new Color(0.15f, 0.88f, 0.85f) : new Color(0.98f, 0.76f, 0.16f);
            var ring = GsaMeshBuilder.BuildBeaconRing($"Beacon_{id}", col, 1.4f, 1.0f);
            ring.Position = _coordinates.ToGodot(new Vector3d(obj.PositionEnu.X, obj.PositionEnu.Y - (id == "office_entry" ? 2.4 : 0.0), obj.PositionEnu.Z));
            visualBase.AddChild(ring);
            _cityVisuals.SpinningBeacons.Add(ring);
        }

        foreach (var bm in _geodeticNetwork.Benchmarks.Values)
        {
            var ring = GsaMeshBuilder.BuildBeaconRing($"Beacon_{bm.BenchmarkId}", new Color(0.24f, 0.92f, 0.46f), 0.9f, 0.95f);
            ring.Position = _coordinates.ToGodot(bm.PhysicalPositionEnu);
            visualBase.AddChild(ring);
            _cityVisuals.SpinningBeacons.Add(ring);
        }

        _streamedBlocksRoot = new Node3D { Name = "StreamedCitySectors" };
        AddChild(_streamedBlocksRoot);

        var camera = new Camera3D
        {
            Name = "CityCamera",
            Fov = 58f,
            Near = 0.1f,
            Far = 600f,
            Current = true,
        };
        AddChild(camera);
        _chaseCamera = new GsaThirdPersonCamera(camera);
    }

    private void BuildSemanticWorld()
    {
        _worldRoot = new Node3D { Name = "WorldGeometry" };
        AddChild(_worldRoot);
        foreach (var item in _fullManifest.Objects)
        {
            if (item.ObjectClass is "ground" or "training_park" or "calm_zone" or "wifi_hotspot")
                continue; // Handled by full city base decks cleanly (non-solid zone markers omitted)

            var size = new Vector3((float)item.SizeEnu.X, (float)item.SizeEnu.Z, (float)item.SizeEnu.Y);
            if (!size.IsFinite() || size.X <= 0 || size.Y <= 0 || size.Z <= 0)
                continue;

            Node3D node = item.Collision
                ? new StaticBody3D { CollisionLayer = 1, CollisionMask = 1 }
                : new Node3D();
            node.Name = item.Id;
            node.Position = _coordinates.ToGodot(item.PositionEnu);
            _worldRoot.AddChild(node);
            _registry.Register(item, node);

            var matKey = item.ObjectClass switch
            {
                "commercial_tower" => ("bldg_tower_glass_uv", "glass_tower", Colors.White, false),
                "warehouse_facade" => ("bldg_warehouse_uv", "warehouse", Colors.White, false),
                "building_shell" => ("bldg_city_comm_uv", "facade_commercial", Colors.White, true),
                "building_facade" => ("bldg_city_facade_uv", "facade_commercial", new Color(1.0f, 0.94f, 0.82f), true),
                _ => ("", "", Colors.White, false),
            };
            var textured = matKey.Item1.Length > 0;
            var material = textured
                ? GsaRetroTextures.CreateWorldMaterial(matKey.Item1, matKey.Item2, matKey.Item3, triplanar: false, nightWindowGlow: matKey.Item4)
                : GsaMeshBuilder.SolidMat(item.Material switch
                {
                    "glass" => new Color(0.24f, 0.68f, 0.82f, 0.52f),
                    "water" => new Color(0.10f, 0.42f, 0.68f, 0.72f),
                    "vegetation" => new Color(0.22f, 0.48f, 0.24f),
                    "metal" => new Color(0.62f, 0.68f, 0.72f),
                    _ => new Color(0.58f, 0.60f, 0.58f),
                });

            node.AddChild(new MeshInstance3D
            {
                Name = "Geometry",
                Mesh = textured ? GsaMeshBuilder.CreateWorldUvBoxMesh(size, 0.095f) : new BoxMesh { Size = size },
                MaterialOverride = material,
                CastShadow = item.ObjectClass is "survey_control" or "unload_point" or "manhole" || item.Material is "glass" or "water"
                    ? GeometryInstance3D.ShadowCastingSetting.Off
                    : GeometryInstance3D.ShadowCastingSetting.On,
            });
            if (item.Collision)
                node.AddChild(new CollisionShape3D { Name = "Collision", Shape = new BoxShape3D { Size = size } });
        }

        _manifestObstacles = ObstacleBoxEnu.FromWorldObjects(
            _fullManifest.Objects.Where(o => o.ObjectClass is not ("ground" or "training_park" or "calm_zone" or "wifi_hotspot"))).ToArray();
        _liveCandidates = _fullManifest.Objects.Select(CameraVisibilityCandidate.FromWorldObject).ToArray();
    }

    private void BuildCollectiblesAndLandmarks()
    {
        _collectiblesRoot = new Node3D { Name = "Collectibles" };
        AddChild(_collectiblesRoot);

        var eggMat = GsaMeshBuilder.SolidMat(new Color(0.98f, 0.80f, 0.16f), roughness: 0.25f, emit: true, emissionEnergy: 0.9f);
        var cardMat = GsaMeshBuilder.SolidMat(new Color(0.24f, 0.82f, 0.96f), roughness: 0.25f, emit: true, emissionEnergy: 0.9f);

        _activeEggs = _collectibles.GetActiveDailyEggs(_calendar.DayIndex);
        foreach (var egg in _activeEggs)
        {
            var n = new Node3D { Name = egg.EggId, Position = _coordinates.ToGodot(egg.PositionEnu + new Vector3d(0, 0, 0.8)) };
            n.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.35f, Height = 0.92f }, MaterialOverride = eggMat });
            _collectiblesRoot.AddChild(n);
            _collectibleNodes[egg.EggId] = n;
        }
        foreach (var card in MvpCollectiblesCoordinator.AllSixProjectionCards)
        {
            var n = new Node3D { Name = card.CardId, Position = _coordinates.ToGodot(card.PositionEnu + new Vector3d(0, 0, 1.0)) };
            n.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.6f, 0.8f, 0.08f) }, MaterialOverride = cardMat });
            _collectiblesRoot.AddChild(n);
            _collectibleNodes[card.CardId] = n;
        }
    }

    private void BuildPlayerAndTraffic()
    {
        _benchRoot = new Node3D { Name = "PlayerAndTraffic" };
        AddChild(_benchRoot);

        _mobility = new MobilityBenchAdapter(_coordinates);
        _mobility.ApplyInputMap(InputActionMap.CreateDefault());
        var spawn = new Vector3d(500228.0, 7400000.7, 100.0);
        _mobility.AttachAvatar(_benchRoot, spawn, LocomotionMode.Handcart);
        _locomotion = new LocomotionSimulator(spawn, Math.PI, LocomotionMode.Handcart, _fullManifest.Bounds);

        _traffic = new TrafficBenchAdapter(_coordinates, new TrafficBenchSimulator(OldCenterTrafficNetwork.Create()));
        _traffic.AttachBenchMeshes(_benchRoot, warnForErraticDrivers: true);
        _cityVisuals.AddOldCenterTrafficSignals(_benchRoot, _coordinates, _traffic.Simulator.Graph);
        var venue = new Label3D { Name = "OldCenterMusicBar", Text = Text("city.traffic.music_bar"),
            Position = _coordinates.ToGodot(OldCenterTrafficNetwork.MusicBarEnu + Vector3d.UnitZ * 2.6),
            FontSize = 42, PixelSize = .009f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled };
        _benchRoot.AddChild(venue);
        _audio = new GsaProceduralAudio(this);
    }

    private void BuildLidarPointCloudOverlay()
    {
        var pointMesh = BuildLidarPointMarkerMesh();
        pointMesh.SurfaceSetMaterial(0, new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        });
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = pointMesh,
            InstanceCount = MaxLidarDisplayPoints,
            VisibleInstanceCount = 0,
        };
        _lidarCloudMesh = new MultiMeshInstance3D
        {
            Name = "LiveLidarPointCloud",
            Multimesh = mm,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_lidarCloudMesh);
    }

    private static WorldSectorCellId GetStreamingSectorCell(Vector3d enu) => new(
        (int)Math.Floor((enu.X - OldCenterMvpWorldAssembler.MvpOriginEast) / OldCenterMvpWorldAssembler.SectorSizeMetres),
        (int)Math.Floor((enu.Y - OldCenterMvpWorldAssembler.MvpOriginNorth) / OldCenterMvpWorldAssembler.SectorSizeMetres));

    private void UpdateSectorStreaming(Vector3d playerEnu)
    {
        _lastStreamingSector = GetStreamingSectorCell(playerEnu);
        var eval = _streamer.UpdateStreamingWindow(playerEnu);
        var wanted = eval.LoadedSectors.ToHashSet();
        var changed = false;

        // Unload far sectors cleanly
        foreach (var existing in _builtSectors.ToArray())
        {
            if (wanted.Contains(existing))
                continue;
            if (_sectorNodes.TryGetValue(existing, out var node))
            {
                _streamedBlocksRoot.RemoveChild(node);
                node.QueueFree();
                _sectorNodes.Remove(existing);
            }
            _sectorObstacles.Remove(existing);
            _builtSectors.Remove(existing);
            changed = true;
        }

        // Stream procedural neighborhood city blocks inside newly entered 100x100 m sectors!
        foreach (var sector in wanted)
        {
            if (_builtSectors.Contains(sector))
                continue;
            _builtSectors.Add(sector);
            var sectorObstacles = new List<ObstacleBoxEnu>(4);
            var sectorNode = BuildProceduralCitySector(sector, sectorObstacles);
            _sectorNodes[sector] = sectorNode;
            _sectorObstacles[sector] = sectorObstacles;
            _streamedBlocksRoot.AddChild(sectorNode);
            changed = true;
        }

        if (changed || _cityBlockObstacles.Count == 0)
        {
            _cityBlockObstacles.Clear();
            _cityBlockObstacles.AddRange(_manifestObstacles);
            foreach (var list in _sectorObstacles.Values)
                _cityBlockObstacles.AddRange(list);
            _hasNearbyObstacleAnchor = false;
        }
    }

    private Node3D BuildProceduralCitySector(WorldSectorCellId sector, List<ObstacleBoxEnu> obstaclesOut)
    {
        var minE = OldCenterMvpWorldAssembler.MvpOriginEast + sector.SectorX * OldCenterMvpWorldAssembler.SectorSizeMetres;
        var minN = OldCenterMvpWorldAssembler.MvpOriginNorth + sector.SectorY * OldCenterMvpWorldAssembler.SectorSizeMetres;
        var sectorCenterEnu = new Vector3d(minE + 50.0, minN + 50.0, 100.0);
        var sectorOriginG = _coordinates.ToGodot(sectorCenterEnu);
        var node = new Node3D { Name = sector.WireId, Position = sectorOriginG };

        // Leave the tutorial Commerce Street block, Central Park, City Hall/Viaduct, Waterfront Pier bridge lane, Port Harbor quay, Serra mountain, and Ocean free of generic blocks
        if ((sector.SectorX <= 2 && sector.SectorY == 0) ||
            (sector.SectorX is >= 2 and <= 4 && sector.SectorY is >= 2 and <= 4) ||
            (sector.SectorX is >= 4 and <= 7 && sector.SectorY is >= 4 and <= 5) ||
            (sector.SectorX >= 17 && sector.SectorY is 4 or 5) ||
            (sector.SectorY >= 17 && sector.SectorX < 10) ||
            (sector.SectorX >= 10 && sector.SectorY >= 10) ||
            sector.SectorX >= 18)
        {
            return node;
        }

        var rng = new Random(sector.SectorX * 7919 + sector.SectorY * 104729 + 31);
        var isPort = sector.SectorY >= 10;
        var isWaterfront = sector.SectorX >= 10 && sector.SectorY < 10;

        var sectorBatch = new GsaStaticMeshBatcher();
        var antennaMat = GsaMeshBuilder.SolidMat(new Color(0.46f, 0.48f, 0.52f));
        var tankMat = GsaMeshBuilder.SolidMat(new Color(0.18f, 0.44f, 0.72f));
        StaticBody3D? sectorCollider = null;

        // Place 2x2 sub-block buildings with 24 m avenue corridors clear along sector edges!
        foreach (var (ox, oy) in new (double, double)[] { (26, 26), (74, 26), (26, 74), (74, 74) })
        {
            if (rng.NextDouble() < 0.18) continue;
            var e = minE + ox;
            var n = minN + oy;
            var w = 24f + (float)rng.NextDouble() * 8f;
            var d = 24f + (float)rng.NextDouble() * 8f;
            var h = isWaterfront ? 18f + (float)rng.NextDouble() * 28f
                  : isPort ? 8f + (float)rng.NextDouble() * 10f
                  : 10f + (float)rng.NextDouble() * 16f;
            // City sectors are 100 m wide and the painted avenues repeat every
            // 120 m, so sector-edge setbacks alone do not keep this grid clear.
            if (OldCenterTrafficNetwork.OverlapsAvenueGrid(e, n, w, d))
                continue;
            var groundZ = GetTerrainElevationEnu(e, n);
            var centerEnu = new Vector3d(e, n, groundZ + h * 0.5);
            var localPos = _coordinates.ToGodot(centerEnu) - sectorOriginG;
            var bldgName = $"Bldg_{sector.SectorX}_{sector.SectorY}_{(int)ox}_{(int)oy}";

            obstaclesOut.Add(new ObstacleBoxEnu(bldgName, centerEnu, new Vector3d(w, d, h), true));

            sectorCollider ??= new StaticBody3D { Name = "SectorColliders", CollisionLayer = 1, CollisionMask = 1 };
            sectorCollider.AddChild(new CollisionShape3D
            {
                Name = bldgName,
                Position = localPos,
                Shape = new BoxShape3D { Size = new Vector3(w, h, d) },
            });

            var style = isPort ? ("bldg_sec_ware_uv", "warehouse", false)
                      : isWaterfront ? ("bldg_sec_glass_uv", "glass_tower", false)
                      : rng.Next(3) switch
                      {
                          0 => ("bldg_sec_comm_uv", "facade_commercial", true),
                          1 => ("bldg_sec_res_uv", "facade_residential", true),
                          _ => ("bldg_sec_conc_uv", "facade_concrete", true),
                      };
            var bMat = GsaRetroTextures.CreateWorldMaterial(style.Item1, style.Item2, Colors.White, triplanar: false, nightWindowGlow: style.Item3);
            sectorBatch.AddBox(bMat, localPos, new Vector3(w, h, d), uvScale: 0.095f);

            // Sculpted Art-Deco stepped upper setback & rooftop penthouse / water tank to avoid flat box silhouettes!
            if (h > 15f && !isPort)
            {
                var ph = 5f + (float)rng.NextDouble() * 7f;
                sectorBatch.AddBox(bMat, localPos + new Vector3(0f, h * 0.5f + ph * 0.5f, 0f), new Vector3(w * 0.64f, ph, d * 0.64f), uvScale: 0.095f);
                sectorBatch.AddBox(antennaMat, localPos + new Vector3(0f, h * 0.5f + ph + 2.75f, 0f), new Vector3(0.35f, 5.5f, 0.35f));
            }
            else if (!isPort)
            {
                sectorBatch.AddBox(tankMat, localPos + new Vector3(-w * 0.25f, h * 0.5f + 0.9f, 0f), new Vector3(2.4f, 1.8f, 2.4f));
            }
        }

        // Add sector street tree directly into the sector batch
        var treeLocal = _coordinates.ToGodot(new Vector3d(minE + 10.0, minN + 12.0, GetTerrainElevationEnu(minE + 10.0, minN + 12.0))) - sectorOriginG;
        sectorBatch.AddCylinder(GsaMeshBuilder.SolidMat(new Color(0.34f, 0.22f, 0.13f)), treeLocal + new Vector3(0f, 1.35f, 0f), 0.16f, 0.22f, 2.7f, radialSegments: 6);
        sectorBatch.AddBox(GsaMeshBuilder.SolidMat(new Color(0.18f, 0.44f, 0.18f)), treeLocal + new Vector3(0f, 3.25f, 0f), new Vector3(2.8f, 2.2f, 2.8f));

        if (sectorCollider is not null)
            node.AddChild(sectorCollider);
        var batchMesh = sectorBatch.BuildSingleMeshInstance("SectorMesh", GeometryInstance3D.ShadowCastingSetting.Off, visibilityRangeEnd: 420f);
        if (batchMesh is not null)
            node.AddChild(batchMesh);

        return node;
    }

    public double GetTerrainElevationEnu(double east, double north) =>
        _landmarks.SampleDriveableElevationEnu(east, north);

    private void BuildWorldParkedVehicles(Node3D visualBase)
    {
        var spots = new (LocomotionMode Mode, Vector3d Pos, double Heading)[]
        {
            (LocomotionMode.Bicycle, new Vector3d(500285, 7400338, 100.0), 0.0),
            (LocomotionMode.Scooter, new Vector3d(500116, 7400038, 100.0), Math.PI * 0.5),
            (LocomotionMode.Car, new Vector3d(500220, 7400022, 100.0), Math.PI),
            (LocomotionMode.Kombi, new Vector3d(500450, 7401140, 100.0), 0.0),
            (LocomotionMode.Pickup, new Vector3d(501035, 7401035, 100.0), Math.PI * 0.25),
            (LocomotionMode.Van, new Vector3d(501260, 7400140, 100.0), Math.PI),
        };
        foreach (var (mode, pos, heading) in spots)
        {
            var rig = GsaMeshBuilder.BuildPlayerRig(mode);
            rig.Root.Name = $"ParkedWorld_{mode}";
            rig.Root.Position = _coordinates.ToGodot(pos);
            rig.Root.Rotation = new Vector3(0f, (float)(heading - Math.PI / 2.0), 0f);
            var ring = GsaMeshBuilder.BuildBeaconRing("BoardRing", new Color(0.24f, 0.88f, 0.96f), 1.8f, 0.55f);
            rig.Root.AddChild(ring);
            visualBase.AddChild(rig.Root);
            _worldParkedVehicles.Add((mode, pos, heading, rig.Root));
        }
    }

    public string GetRegionIdForEnu(Vector3d enu)
    {
        if (enu.X >= 501000.0 && enu.Y >= 7401000.0) return "serra";
        if (enu.Y >= 7401000.0) return "port";
        if (enu.X >= 501000.0) return "waterfront";
        return "old_center";
    }

    private string GetDistrictBannerKey(Vector3d enu) => GetRegionIdForEnu(enu) switch
    {
        "port" => "district.port",
        "waterfront" => "district.waterfront",
        "serra" => "district.serra",
        _ => (enu.Y < 7400100.0 && enu.X < 500300.0) ? "district.old_center.commerce" : "district.old_center",
    };

    public override void _Process(double delta) => _perfProbe?.StepFrame(this);

    public override void _PhysicsProcess(double delta)
    {
        if (delta <= 0) return;
        _tick++;

        if (_citySaveSmoke && !_citySaveSmokeCompleted)
        {
            if (_tick >= 2)
            {
                try { RunCitySaveSmoke(); }
                catch (Exception error)
                {
                    GD.PushError(error.ToString());
                    SetPhysicsProcess(false);
                    GetTree().Quit(1);
                }
            }
            return;
        }

        var weather = _calendar.StepPhysical(delta);
        var activePos = CurrentPositionEnu();
        var activeHeading = CurrentHeadingRadians();
        var previousPlayerPosition = activePos;
        var activeMode = CurrentActiveMode();
        var activeSpeed = 0.0;
        var activePitch = 0.0;
        var activeRoll = 0.0;
        var saveTrafficImpact = false;
        StepCityTraffic(delta, activePos, weather);

        var input = _mobility.SampleLiveInput(_carSim?.SpeedMetresPerSecond ?? _locomotion.ForwardSpeedMetresPerSecond);
        input = ApplyCityEverydayInput(input, activeMode);
        var regionId = GetRegionIdForEnu(activePos);
        if (regionId != _lastRegionId)
        {
            _lastRegionId = regionId;
            _pendingDistrictPopup = Text(GetDistrictBannerKey(activePos));
        }

        if (_carSim is not null && _locomotion.Mode >= LocomotionMode.Car)
        {
            var terrainZBefore = GetTerrainElevationEnu(_carSim.PositionEnu.X, _carSim.PositionEnu.Y);
            var prevCarPos = _carSim.PositionEnu;
            var carStep = _carSim.StepMotion(
                delta,
                Math.Max(0.0, input.Throttle),
                input.Brake + (input.Throttle < 0.0 ? -input.Throttle : 0.0),
                input.Steer,
                weather.SurfaceGripMultiplier,
                0.0,
                _fleet.GetSkillRank(SkillTrackKind.DefensiveDriving),
                speedLimitMetresPerSecond: _cityHazards.TirePunctured ? 6 : double.PositiveInfinity);

            var resolvedPos = ResolveObstaclePushout(prevCarPos, carStep.PositionEnu, 1.25, out var hitId);
            if (hitId is not null && carStep.SpeedMetresPerSecond > 3.2)
            {
                _audio.PlayCollisionThud(carStep.SpeedMetresPerSecond);
                _chaseCamera.TriggerShake(0.32f, 0.32f);
                var imp = _carSim.ApplyCollisionImpact(carStep.SpeedMetresPerSecond * 0.65, hitId, _fleet.GetSkillRank(SkillTrackKind.DefensiveDriving));
                if (imp.TriggersRackMisalignment)
                {
                    _rackMisaligned = true;
                    _feedbackText = Text("city.rack.misaligned");
                    _audio.PlayAlert(critical: true);
                }
            }

            var trafficContact = ResolveCityTrafficContact(delta, activeMode, prevCarPos, ref resolvedPos);
            if (trafficContact is { } vehicleHit)
            {
                saveTrafficImpact = ApplyCityTrafficImpact(vehicleHit, activeMode);
                _carSim.StopAtExternalContact();
            }

            var terrainZ = GetTerrainElevationEnu(resolvedPos.X, resolvedPos.Y);
            var finalCarPos = new Vector3d(
                Math.Clamp(resolvedPos.X, 500005, 501995),
                Math.Clamp(resolvedPos.Y, SouthAccessRoadMinimumNorth, 7401995),
                terrainZ);
            _carSim.ApplyExternalPositionAdjustment(finalCarPos);

            // Keep LocomotionSimulator synced with Car position for smooth dismount/remount
            _locomotion.RepositionSafeAfterRecovery(finalCarPos, activeMode, carStep.HeadingRadians, refillAutonomy: false, clearStorage: false);
            // Update car's internal position back if clamped/elevated
            activePos = finalCarPos;
            activeHeading = carStep.HeadingRadians;
            activeSpeed = _carSim.SpeedMetresPerSecond;
            activePitch = carStep.PitchRadians + Math.Clamp((terrainZ - terrainZBefore) * 0.4, -0.25, 0.25);
            activeRoll = carStep.RollRadians;
        }
        else
        {
            var step = _locomotion.Step(delta, input, 0.0, weather.SurfaceGripMultiplier, GetNearbyObstacles(activePos),
                p => GetTerrainElevationEnu(p.X, p.Y));
            activePos = step.PositionEnu;
            activeHeading = step.HeadingRadians;
            activeSpeed = step.ForwardSpeedMetresPerSecond;
            var contact = ResolveCityTrafficContact(delta, activeMode, previousPlayerPosition, ref activePos);
            if (contact is { } trafficHit)
            {
                activePos = new Vector3d(activePos.X, activePos.Y, GetTerrainElevationEnu(activePos.X, activePos.Y));
                _locomotion.StopAtExternalContact(activePos);
                activeSpeed = 0;
                ApplyCityTrafficImpact(trafficHit, activeMode);
            }
            if (step.Collided && step.ImpactDeltaVMetresPerSecond > 1.2)
                _gimbalina.ApplyStartle(StartleEventKind.Pothole);
            activePitch = Math.Clamp((_lastSpeedMps - activeSpeed) * 0.04, -0.14, 0.14);
            activeRoll = Math.Clamp(input.Steer * Math.Abs(activeSpeed) * 0.03, -0.14, 0.14);
        }

        if (saveTrafficImpact) SaveHazardChange();
        _lastSpeedMps = activeSpeed;
        StepCityEverydayEncounters(delta, activePos, activeSpeed, activeMode);
        var gStep = _gimbalina.Step(delta, isResting: Math.Abs(activeSpeed) < 0.1,
            bodyPitchRadians: activePitch, bodyRollRadians: activeRoll);
        if (gStep.HasFledToSafety) _locomotion.MarkGimbalinaFled();
        _mobility.SetGimbalinaPresent(!gStep.HasFledToSafety);

        _mobility.SetVisualPlatformMode(activeMode);
        _mobility.SyncAvatarPose(activePos, activeHeading, delta, activeSpeed, input.Steer, activePitch, activeRoll,
            gStep.Stress, gStep.ResidualPitchRadians, gStep.ResidualRollRadians, weather.IsNight, input.Brake > 0.1);

        if (activeMode == LocomotionMode.OnFoot)
            _mobility.SyncParkedVehicle(_benchRoot, _locomotion.MountedVehicleMode, _locomotion.HandcartPositionEnu, _locomotion.HandcartHeadingRadians);
        else
            _mobility.SyncParkedVehicle(_benchRoot, null, Vector3d.Zero, 0.0);

        if (_lastStreamingSector != GetStreamingSectorCell(activePos))
            UpdateSectorStreaming(activePos);

        _cityVisuals.UpdateAnimatedProps(delta, _traffic.Simulator);
        _atmosphere.Update(weather, _coordinates.ToGodot(activePos), delta);
        _audio.Step(delta, gStep.Stress, activeMode <= LocomotionMode.Bicycle, weather.IsRaining, activeSpeed, activeMode, input.Brake > 0.1);

        // Step world hazards, police blitz, absurd events & Chicken Challenge (200 m track)
        StepCityHazardsAndEvents(delta, activePos, activeSpeed, activeMode, gStep);

        // Rotate collectibles & check proximity pickup / benchmark occupation
        StepCollectiblesAndBenchmarks(delta, activePos, activeMode);

        // Live LiDAR continuous rotating sweep on L4+ platforms!
        StepCityNavigation(delta, activePos, activeHeading, activeMode, weather.IsRaining);
        StepLiveLidar(delta, activePos, activeHeading, activeMode, weather.IsRaining);

        // Camera capture tick
        if (_captureRequested && _camScheduler.IsCaptureTick(_tick))
        {
            _captureRequested = false;
            CaptureCityPhoto(activePos, activeHeading, activeSpeed, weather);
        }

        var attn = StepCityFactionHazards(delta, activePos, activeSpeed, activeMode);

        _chaseCamera.Update(delta, _coordinates.ToGodot(activePos), activeHeading, activeSpeed, activeMode,
            GetWorld3D().DirectSpaceState, gStep.Stress, _calendar.CalendarTimeSeconds);

        _hudElapsed += delta;
        if (_hudElapsed >= 0.1)
        {
            _hudElapsed = 0;
            RefreshCityHud(0.1, activePos, activeHeading, activeMode, weather, gStep.Stress, attn);
            if (_tabletPanel.Visible) RefreshCityHazardStatus();
            if (_tabletPanel.Visible && _tabletTabs.CurrentTab is MapTabletTab or TechnicalTabletTab)
                RefreshTechnicalTablet();
        }
        GsaScreenshotHelper.TryCaptureOnFrame(this, (int)_tick, targetFrame: 35);
    }

    private IReadOnlyList<ObstacleBoxEnu> GetNearbyObstacles(Vector3d pos)
    {
        if (!_hasNearbyObstacleAnchor ||
            Math.Abs(pos.X - _nearbyObstacleAnchor.X) > 20.0 ||
            Math.Abs(pos.Y - _nearbyObstacleAnchor.Y) > 20.0)
        {
            _nearbyObstacles.Clear();
            const double radius = 48.0;
            foreach (var box in _cityBlockObstacles)
            {
                if (!box.Collision) continue;
                if (pos.X + radius >= box.MinX && pos.X - radius <= box.MaxX &&
                    pos.Y + radius >= box.MinY && pos.Y - radius <= box.MaxY)
                {
                    _nearbyObstacles.Add(box);
                }
            }
            if (_roadblockObstacle is { } roadblock) _nearbyObstacles.Add(roadblock);
            _nearbyObstacleAnchor = pos;
            _hasNearbyObstacleAnchor = true;
        }
        return _nearbyObstacles;
    }

    private Vector3d ResolveObstaclePushout(Vector3d prevPos, Vector3d candidate, double radius, out string? hitId)
    {
        hitId = null;
        var cur = candidate;
        foreach (var box in GetNearbyObstacles(cur))
        {
            if (!box.Collision) continue;
            if (cur.X + radius > box.MinX && cur.X - radius < box.MaxX &&
                cur.Y + radius > box.MinY && cur.Y - radius < box.MaxY &&
                cur.Z + 1.8 > box.MinZ && cur.Z < box.MaxZ)
            {
                hitId = box.Id;
                return prevPos;
            }
        }
        return cur;
    }

    private void StepCityHazardsAndEvents(
        double dt,
        Vector3d pos,
        double speedMps,
        LocomotionMode mode,
        GimbalinaStepResult gStep)
    {
        _hazardCooldown = Math.Max(0.0, _hazardCooldown - dt);

        // 1. Pothole jolts & rack impact
        if (_hazardCooldown <= 0.0 && Math.Abs(speedMps) > 4.5 && mode != LocomotionMode.OnFoot)
        {
            foreach (var hole in _landmarks.PotholeCentersEnu)
            {
                if (TrainingContractSession.HorizontalDistance(pos, hole) < 2.1)
                {
                    _hazardCooldown = 3.0;
                    _gimbalina.ApplyStartle(StartleEventKind.Pothole);
                    _audio.PlayCollisionThud(Math.Abs(speedMps) * 0.5);
                    _chaseCamera.TriggerShake(0.28f, 0.30f);
                    if (mode >= LocomotionMode.Car && Math.Abs(speedMps) > 9.0 && _carSim is not null)
                    {
                        var imp = _carSim.ApplyCollisionImpact(3.4, "pothole", _fleet.GetSkillRank(SkillTrackKind.DefensiveDriving));
                        if (imp.TriggersRackMisalignment)
                            _rackMisaligned = true;
                    }
                    _feedbackText = Text("city.hazard.pothole");
                    break;
                }
            }
        }

        // 2. Police Blitz speeding enforcement
        if (_hazardCooldown <= 0.0 &&
            Math.Abs(speedMps) > 12.0 &&
            TrainingContractSession.HorizontalDistance(pos, _landmarks.BlitzCheckpointEnu) < 10.5)
        {
            _hazardCooldown = 12.0;
            _ledger.ApplyTrafficFine($"blitz_{_tick}", "police_blitz", _calendar.CalendarTimeSeconds, 20, "city.hazard.blitz");
            _audio.PlayAlert(critical: true);
            _feedbackText = Text("city.hazard.blitz");
        }

        // 3. Absurd Urban Events (Trio Elétrico, Street Marathon, Pigeon Flock) respecting Calm City
        _absurdEvents.ApplyCalmPolicy(_calmPolicy.IsProtectionActive);
        _absurdEvents.StepEvents(dt);

        if (!_calmPolicy.IsProtectionActive && !_absurdEvents.Events.Values.Any(e => e.Active))
        {
            _nextAbsurdEventTimer -= dt;
            if (_nextAbsurdEventTimer <= 0.0)
            {
                _nextAbsurdEventTimer = 55.0;
                var kind = (AbsurdUrbanEventKind)((_tick / 100) % 3);
                var started = _absurdEvents.TryStartEvent(
                    $"evt_{_tick}", kind, _eventRoadGraph, ["seg_main"], "seg_entry", "seg_unique_bridge", 35.0,
                    isCalmContract: _activeContract?.Calm == true);
                if (started.Started)
                {
                    _feedbackText = Text(kind switch
                    {
                        AbsurdUrbanEventKind.TrioEletrico => "city.event.trio",
                        AbsurdUrbanEventKind.StreetMarathon => "city.event.marathon",
                        _ => "city.event.pigeons",
                    });
                }
            }
        }

        var activeEv = _absurdEvents.Events.Values.FirstOrDefault(e => e.Active);
        if (_landmarks.TrioTruckNode is not null)
        {
            _landmarks.TrioTruckNode.Visible = activeEv?.Kind == AbsurdUrbanEventKind.TrioEletrico;
            if (_landmarks.TrioTruckNode.Visible)
                _landmarks.TrioTruckNode.Position = _coordinates.ToGodot(new Vector3d(500240.0 + Math.Sin(_tick * 0.02) * 35.0, 7400236.0, 100.0));
        }
        if (_landmarks.MarathonPackNode is not null)
        {
            _landmarks.MarathonPackNode.Visible = activeEv?.Kind == AbsurdUrbanEventKind.StreetMarathon;
            if (_landmarks.MarathonPackNode.Visible)
                _landmarks.MarathonPackNode.Position = _coordinates.ToGodot(new Vector3d(500360.0, 7400236.0 + Math.Sin(_tick * 0.03) * 28.0, 100.0));
        }
        if (_landmarks.PigeonFlockNode is not null)
        {
            _landmarks.PigeonFlockNode.Visible = activeEv?.Kind == AbsurdUrbanEventKind.PigeonFlock;
            if (_landmarks.PigeonFlockNode.Visible)
                _landmarks.PigeonFlockNode.RotateY((float)dt * 1.4f);
        }

        // 4. Chicken Challenge (200 m Track along Central Park N=7400350, E=500288 -> E=500488)
        if (!_chickenChallengeActive && mode <= LocomotionMode.Bicycle &&
            TrainingContractSession.HorizontalDistance(pos, _landmarks.ChickenChallengeStartEnu) < 4.5)
        {
            _chickenChallengeActive = true;
            _chickenChallengeTime = 0.0;
            _chickenChallengeSamples = 0;
            _chickenChallengeSteady = 0;
            _audio.PlayOffloadChime();
            _feedbackText = Text("city.challenge.start");
        }
        else if (_chickenChallengeActive)
        {
            _chickenChallengeTime += dt;
            _chickenChallengeSamples++;
            if (Math.Abs(gStep.ResidualPitchRadians) <= 0.088 && Math.Abs(gStep.ResidualRollRadians) <= 0.088)
                _chickenChallengeSteady++;

            if (pos.X >= _landmarks.ChickenChallengeEndEnu.X && Math.Abs(pos.Y - _landmarks.ChickenChallengeEndEnu.Y) < 15.0)
            {
                _chickenChallengeActive = false;
                var steadyFrac = _chickenChallengeSamples > 0 ? (double)_chickenChallengeSteady / _chickenChallengeSamples : 0.0;
                if (steadyFrac >= 0.95)
                {
                    _ledger.RecordActivityReward($"chicken_win_{_tick}", "chicken_challenge", _calendar.CalendarTimeSeconds, 100, "challenge", "city.challenge.passed");
                    _audio.PlayMissionPassedFanfare();
                    _gtaHud.TriggerCelebrationBanner(Text("city.challenge.passed"), $"{steadyFrac:P0} · {_chickenChallengeTime:F1}s");
                }
                else
                {
                    _audio.PlayAlert();
                    _feedbackText = $"{steadyFrac:P0} (<95%)";
                }
            }
            else if (TrainingContractSession.HorizontalDistance(pos, _landmarks.ChickenChallengeStartEnu) > 260.0)
            {
                _chickenChallengeActive = false;
            }
        }
    }

    private void StepCollectiblesAndBenchmarks(double dt, Vector3d pos, LocomotionMode mode)
    {
        foreach (var (id, node) in _collectibleNodes)
        {
            node.RotateY((float)dt * 2.2f);
            if (!node.Visible) continue;
            if (_coordinates.ToMap(node.Position) is var itemPos &&
                TrainingContractSession.HorizontalDistance(pos, itemPos) < 2.2)
            {
                node.Visible = false;
                if (id.StartsWith("egg_", StringComparison.Ordinal))
                {
                    var spot = _activeEggs.FirstOrDefault(e => e.EggId == id);
                    if (spot is not null)
                    {
                        var outcome = _collectibles.CollectGoldenEgg(spot, _calendar.CalendarTimeSeconds, _ledger);
                        if (outcome.Succeeded && !outcome.AlreadyClaimed)
                        {
                            _audio.PlayPaymentSuccess();
                            _feedbackText = $"{Text("collectible.egg.reward")} (+{outcome.CoordsCredited} C$)";
                        }
                    }
                }
                else if (id.StartsWith("card_", StringComparison.Ordinal))
                {
                    var outcome = _collectibles.CollectProjectionCard(id, _geopedia);
                    if (outcome.Succeeded && !outcome.AlreadyClaimed)
                    {
                        var card = MvpCollectiblesCoordinator.AllSixProjectionCards.First(c => c.CardId == id);
                        _audio.PlayOffloadChime();
                        _feedbackText = Text(card.ProjectionNameKey);
                    }
                }
            }
        }

        if (mode == LocomotionMode.OnFoot)
        {
            string? nearBm = null;
            foreach (var bm in _geodeticNetwork.Benchmarks.Values)
            {
                if (TrainingContractSession.HorizontalDistance(pos, bm.PhysicalPositionEnu) <= 4.0)
                {
                    nearBm = bm.BenchmarkId;
                    break;
                }
            }
            if (nearBm is not null && !_collectibles.OccupiedLandmarkIds.Contains(nearBm))
            {
                if (_holdingBenchmarkId != nearBm)
                {
                    _holdingBenchmarkId = nearBm;
                    _benchmarkHoldSeconds = 0.0;
                }
                _benchmarkHoldSeconds += dt;
                if (_benchmarkHoldSeconds >= 5.0)
                {
                    _collectibles.OccupySurveyLandmark(nearBm, _benchmarkHoldSeconds, pos, _calendar.CalendarTimeSeconds, _ledger, _geopedia);
                    _audio.PlayPaymentSuccess();
                    _feedbackText = $"{Text("city.benchmark.occupied")} (+25 C$)";
                }
            }
            else
            {
                _holdingBenchmarkId = null;
                _benchmarkHoldSeconds = 0.0;
            }
        }
    }

    private static ArrayMesh BuildLidarPointMarkerMesh()
    {
        const float r = 0.12f;
        var v0 = new Vector3(0f, r, 0f);
        var v1 = new Vector3(0f, -r * 0.7f, r);
        var v2 = new Vector3(r * 0.866f, -r * 0.7f, -r * 0.5f);
        var v3 = new Vector3(-r * 0.866f, -r * 0.7f, -r * 0.5f);

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = new[]
        {
            v0, v2, v1,
            v0, v3, v2,
            v0, v1, v3,
            v1, v2, v3,
        };
        arrays[(int)Mesh.ArrayType.Color] = new[]
        {
            Colors.White, Colors.White, Colors.White,
            Colors.White, Colors.White, Colors.White,
            Colors.White, Colors.White, Colors.White,
            Colors.White, Colors.White, Colors.White,
        };
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    private void StepLiveLidar(double dt, Vector3d pos, double heading, LocomotionMode mode, bool isRaining)
    {
        if (!_lidarEnabled || mode < LocomotionMode.Kombi)
            return;

        var profile = CityLidarProfile(mode);
        if (_lidarSim.Profile.Id != profile.Id)
        {
            _lidarSim = new LidarBenchSimulator(profile);
            _lidarSliceIndex = 0;
        }

        // Distribute one 360-degree revolution cleanly across physics ticks matching ScanRateHz (eliminates burst CPU spikes)
        var sliceCount = Math.Max(1, (int)Math.Round(60.0 / profile.ScanRateHz));
        var sliceIdx = _lidarSliceIndex % sliceCount;
        _lidarSliceIndex++;

        var lidarOrigin = pos + new Vector3d(0, 0, mode == LocomotionMode.Van ? 2.4 : 2.0);
        var yaw = Quaterniond.FromAxisAngle(Vector3d.UnitZ, heading);
        var pose = new EstimatedBodyPose(_calendar.CalendarTimeSeconds, lidarOrigin, yaw);
        var traj = new EstimatedBodyTrajectory([pose, new EstimatedBodyPose(_calendar.CalendarTimeSeconds + 1.0, lidarOrigin, yaw)]);
        var lidarToMap = LidarBenchSimulator.ComposeLidarToMap(new RigidTransform(yaw, lidarOrigin), LidarMountEstimate.Zero);
        var space = GetWorld3D().DirectSpaceState;
        _lidarRayQuery ??= new PhysicsRayQueryParameters3D { CollisionMask = 1 };

        LidarRayHit? CastRay(Vector3d startEnu, Vector3d dirEnu, double maxRange)
        {
            _lidarRayQuery.From = _coordinates.ToGodot(startEnu);
            _lidarRayQuery.To = _coordinates.ToGodot(startEnu + dirEnu * maxRange);
            var hit = space.IntersectRay(_lidarRayQuery);
            if (hit.Count == 0) return null;
            var hitPosG = hit["position"].AsVector3();
            var hitNormG = hit["normal"].AsVector3();
            var dist = (_coordinates.ToMap(hitPosG) - startEnu).Length;
            var colObj = hit["collider"].AsGodotObject();
            string id = "city_block", mat = "concrete";
            if (colObj is not null && _registry.TryGetFastMetadata(colObj.GetInstanceId(), out var regId, out var regMat))
            {
                id = regId;
                mat = regMat;
            }
            return new LidarRayHit(dist, new Vector3d(hitNormG.X, -hitNormG.Z, hitNormG.Y), mat, id);
        }

        var channelStep = CityLidarChannelStep(profile);
        if (_lidarSim.TrySimulateScanSlice(_calendar.CalendarTimeSeconds, lidarToMap, traj, LidarMountEstimate.Zero,
            isRaining, CastRay, sliceIdx, sliceCount, _lidarSliceScratch, channelStep))
        {
            var mm = _lidarCloudMesh.Multimesh;
            var maxPointsPerSlice = Math.Max(1, 480 / sliceCount);
            var stepStride = Math.Max(1, _lidarSliceScratch.Count / maxPointsPerSlice);
            for (var i = 0; i < _lidarSliceScratch.Count; i += stepStride)
            {
                var ret = _lidarSliceScratch[i];
                var gp = _coordinates.ToGodot(ret.GeoreferencedPointEnu);
                var hNorm = Math.Clamp((gp.Y - 0.0f) / 18f, 0f, 1f);
                var color = ret.IsSpuriousRain
                    ? new Color(0.6f, 0.8f, 1.0f)
                    : new Color(0.15f + 0.80f * hNorm, 0.92f - 0.35f * hNorm, 0.35f + 0.55f * (1f - hNorm));
                mm.SetInstanceTransform(_lidarPointWriteIndex, new Transform3D(Basis.Identity, gp));
                mm.SetInstanceColor(_lidarPointWriteIndex, color);
                _lidarPointWriteIndex = (_lidarPointWriteIndex + 1) % MaxLidarDisplayPoints;
                if (_lidarPointVisibleCount < MaxLidarDisplayPoints)
                    _lidarPointVisibleCount++;
            }
            mm.VisibleInstanceCount = _lidarPointVisibleCount;
        }
    }

    private void CaptureCityPhoto(Vector3d pos, double heading, double speed, WeatherAndTimeSnapshot weather)
    {
        var usesGimbalina = CurrentActiveMode() is LocomotionMode.Handcart or LocomotionMode.Bicycle ||
            (CurrentActiveMode() == LocomotionMode.OnFoot && _locomotion.MountedVehicleMode <= LocomotionMode.Bicycle);
        if (_gimbalina.HasFledToSafety) { _feedbackText = Text("city.encounters.chicken_safe"); return; }
        var navigation = _navigationRig?.Estimator?.Current;
        if (CurrentActiveMode() >= LocomotionMode.Car && navigation is null)
        { _feedbackText = Text("city.hazards.wait_navigation"); return; }
        var mount = CameraMountEstimate.ForwardFacing;
        var origin = pos + Vector3d.UnitZ * 1.4;
        var t = _calendar.CalendarTimeSeconds;
        var bodyQuat = Quaterniond.FromAxisAngle(Vector3d.UnitZ, heading +
            (CurrentActiveMode() >= LocomotionMode.Car ? _cityHazards.RackYawErrorRadians : 0));
        EstimatedBodyPose Pose(double dt) => new(t + dt, origin + new Vector3d(Math.Cos(heading) * speed * dt, Math.Sin(heading) * speed * dt, 0),
            usesGimbalina ? _gimbalina.ComputeEffectiveBodyToMap(bodyQuat, dt, _gimbalina.LastBodyPitchRadians, _gimbalina.LastBodyRollRadians) : bodyQuat);
        var start = CameraGeometry.CameraToMap(Pose(0), mount);
        var end = CameraGeometry.CameraToMap(Pose(weather.IsNight ? CameraSensorProfile.Initial.NightExposureSeconds : CameraSensorProfile.Initial.DayExposureSeconds), mount);
        var readout = CameraGeometry.CameraToMap(Pose(CameraSensorProfile.Initial.ReadoutTimeSeconds), mount);

        bool Occluded(Vector3d fromEnu, Vector3d toEnu, string targetId)
        {
            var from = _coordinates.ToGodot(fromEnu);
            var to = _coordinates.ToGodot(toEnu);
            if (from.DistanceSquaredTo(to) < 1e-8f) return false;
            using var query = PhysicsRayQueryParameters3D.Create(from, to, 1);
            var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
            if (hit.Count == 0) return false;
            var collider = hit["collider"].AsGodotObject();
            var semantic = collider is null ? new Dictionary() : _registry.GetByInstanceId(collider.GetInstanceId());
            if (semantic.Count > 0 && semantic["id"].AsString() == targetId) return false;
            return from.DistanceTo(hit["position"].AsVector3()) < from.DistanceTo(to) - 0.01f;
        }

        var estimatedPose = CurrentActiveMode() >= LocomotionMode.Car && navigation is not null
            ? new EstimatedBodyPose(t, navigation.PositionEnu + Vector3d.UnitZ * 1.4, navigation.BodyToMapRotation) : new EstimatedBodyPose(t, origin, bodyQuat);
        var environment = weather.ToCameraEnvironment();
        environment = new CameraEnvironmentSnapshot(environment.IsNight, environment.SunAngleRadians,
            Math.Max(environment.LensObstructionFraction, _cityHazards.Lens.ObstructionFraction), environment.BaseClippingFraction);
        if (_camScheduler.TryEnqueueSimulatedCapture(new CameraCaptureRequest(
            _tick, t, start, end, readout, estimatedPose, mount, environment,
            _liveCandidates, Occluded), _locomotion, out var sample, out _) && sample is not null)
        {
            _fieldSamples.Add(sample);
            _recentPhotoSeconds = .6;
            _coverageSpots.Add(pos);
            if (sample.Quality.Passed)
                foreach (var f in sample.ObservedFeatures) _observedTargetIds.Add(f.TargetId);
            _audio.PlayShutter();
            _feedbackText = sample.ObservedFeatures.Count > 0
                ? $"{Text("training.observed")} {string.Join(", ", sample.ObservedFeatures.Select(f => f.TargetId))}"
                : Text("training.nothing_visible");
        }
        _camScheduler.FlushBatchManifest("city_flush");
    }

    private Vector3d CurrentPositionEnu() =>
        _carSim is not null && _locomotion.Mode >= LocomotionMode.Car ? _carSim.PositionEnu : _locomotion.PositionEnu;

    private double CurrentHeadingRadians() =>
        _carSim is not null && _locomotion.Mode >= LocomotionMode.Car ? _carSim.HeadingRadians : _locomotion.HeadingRadians;

    private LocomotionMode CurrentActiveMode() => _locomotion.Mode;

    private void SwitchPlatform(LocomotionMode targetMode)
    {
        if (targetMode == LocomotionMode.OnFoot)
            return;

        if (_cityHazards.IsVehicleDisabled(targetMode))
        { _feedbackText = Text("city.hazards.disabled_vehicle"); return; }
        var pos = CurrentPositionEnu();
        var heading = CurrentHeadingRadians();
        if (_carSim is not null) _cityHazards.SetVehicleDamage(_carSim.Profile.Mode, _carSim.ConditionDamage);
        _locomotion.SwitchVehiclePlatform(targetMode, refillAutonomy: true, clearStorage: false);
        if (targetMode >= LocomotionMode.Car)
            _carSim = new CarSuspensionSimulator(pos, heading, targetMode, conditionDamage: _cityHazards.GetVehicleDamage(targetMode));
        else
            _carSim = null;

        _mobility.SetVisualPlatformMode(targetMode);
        _geopedia.UnlockAdvancedForPlatformLevel(PlatformProfile.ForMode(targetMode).Level);
        ResetCityNavigation();
        _pendingVehiclePopup = Text($"platform.{targetMode.ToString().ToLowerInvariant()}.name");
        _audio.PlayOffloadChime();
    }

    private void SnapCamera()
    {
        _chaseCamera.Snap(_coordinates.ToGodot(CurrentPositionEnu()), CurrentHeadingRadians(), CurrentActiveMode());
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_chaseCamera.HandleInput(@event))
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        if (@event is InputEventKey { Pressed: true, Echo: false } key)
        {
            switch (key.PhysicalKeycode)
            {
                case Key.E when _everyday.DogActive:
                    GiveCityDogTreat(); GetViewport().SetInputAsHandled(); return;
                case Key.E when _everyday.Parking is not null:
                    _tabletPanel.Show(); _tabletTabs.CurrentTab = 1; RefreshTabletContents();
                    GetViewport().SetInputAsHandled(); return;
                case Key.R or Key.Tab or Key.M:
                    ToggleTablet();
                    GetViewport().SetInputAsHandled();
                    return;
                case Key.C:
                    _captureRequested = true;
                    GetViewport().SetInputAsHandled();
                    return;
                case Key.V:
                    _chaseCamera.CycleMode();
                    GetViewport().SetInputAsHandled();
                    return;
                case Key.Space:
                    if (_gtaHud.IsCutsceneVisible)
                    {
                        _gtaHud.HideCutsceneLetterbox();
                        GetViewport().SetInputAsHandled();
                        return;
                    }
                    break;
                case Key.F:
                    if (_locomotion.Mode == LocomotionMode.OnFoot)
                    {
                        var curPos = CurrentPositionEnu();
                        var nearby = _worldParkedVehicles.FirstOrDefault(v =>
                            !_cityHazards.IsVehicleDisabled(v.Mode) &&
                            TrainingContractSession.HorizontalDistance(curPos, v.PosEnu) < 4.2 &&
                            (_isSandbox || _fleet.OwnedPlatforms.Contains(v.Mode) || _campaignState.UnlockedPlatforms.Contains(v.Mode.ToString().ToLowerInvariant())));
                        if (nearby.Root is not null)
                        {
                            if (_carSim is not null) _cityHazards.SetVehicleDamage(_carSim.Profile.Mode, _carSim.ConditionDamage);
                            _carSim = null;
                            _locomotion.RepositionSafeAfterRecovery(nearby.PosEnu, nearby.Mode, nearby.Heading, refillAutonomy: true, clearStorage: false);
                            SwitchPlatform(nearby.Mode);
                            SnapCamera();
                            GetViewport().SetInputAsHandled();
                            return;
                        }
                    }
                    if (_locomotion.TryToggleMount(_cityBlockObstacles, out _))
                    {
                        var mode = _locomotion.Mode;
                        if (mode < LocomotionMode.Car && mode != LocomotionMode.OnFoot) _carSim = null;
                        else if (mode >= LocomotionMode.Car && _carSim is null) _carSim = new CarSuspensionSimulator(_locomotion.PositionEnu, _locomotion.HeadingRadians, mode, conditionDamage: _cityHazards.GetVehicleDamage(mode));
                        _pendingVehiclePopup = Text(mode == LocomotionMode.OnFoot ? "vehicle.on_foot.banner" : $"platform.{mode.ToString().ToLowerInvariant()}.name");
                    }
                    GetViewport().SetInputAsHandled();
                    return;
                case Key.B:
                    CycleOwnedPlatform();
                    GetViewport().SetInputAsHandled();
                    return;
                case Key.G:
                    if (_gimbalina.TryFeedCorn()) _audio.PlayOffloadChime();
                    GetViewport().SetInputAsHandled();
                    return;
                case Key.H:
                    _audio.PlayHorn(CurrentActiveMode());
                    if (CurrentActiveMode() <= LocomotionMode.Bicycle) _gimbalina.ApplyStartle(StartleEventKind.Horn);
                    GetViewport().SetInputAsHandled();
                    return;
                case Key.X:
                    _audio.CycleRadioStation();
                    GetViewport().SetInputAsHandled();
                    return;
                case Key.L:
                    _lidarEnabled = !_lidarEnabled;
                    GetViewport().SetInputAsHandled();
                    return;
                case Key.P:
                    _calendar.SetWeather(!_calendar.IsRaining, 0.75);
                    GetViewport().SetInputAsHandled();
                    return;
                case Key.T:
                    _calendar.AdvanceCalendarSeconds(7200.0);
                    GetViewport().SetInputAsHandled();
                    return;
                case Key.F10:
                    _atmosphere.SetRetroDitherEnabled(!_atmosphere.RetroDitherEnabled);
                    GetViewport().SetInputAsHandled();
                    return;
                case Key.F5:
                    SaveCareer();
                    GetViewport().SetInputAsHandled();
                    return;
                case Key.F9:
                    TryLoadCareer();
                    GetViewport().SetInputAsHandled();
                    return;
            }
        }
        if (@event.IsActionPressed("ui_cancel"))
        {
            GetViewport().SetInputAsHandled();
            if (!_isSandbox) SaveCareer();
            GetTree().ChangeSceneToFile("res://Bootstrap.tscn");
        }
    }

    private void CycleOwnedPlatform()
    {
        LocomotionMode[] order =
        [
            LocomotionMode.Handcart, LocomotionMode.Bicycle, LocomotionMode.Scooter,
            LocomotionMode.Car, LocomotionMode.Kombi, LocomotionMode.Pickup, LocomotionMode.Van,
        ];
        var current = _locomotion.MountedVehicleMode;
        var idx = Array.IndexOf(order, current);
        for (var step = 1; step <= order.Length; step++)
        {
            var candidate = order[(idx + step) % order.Length];
            if (_fleet.OwnedPlatforms.Contains(candidate))
            {
                SwitchPlatform(candidate);
                return;
            }
        }
    }
}
