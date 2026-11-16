using Gsa.Core.Geodesy;
using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Traffic;
using Gsa.Core.World;
using Gsa.Engine.Art;
using Gsa.Engine.Audio;
using Gsa.Engine.Diagnostics;
using Gsa.Engine.Localization;
using Gsa.Engine.Mobility;
using Gsa.Engine.Sensors;
using Gsa.Engine.Traffic;
using Gsa.Engine.Visuals;
using Gsa.Missions.Campaign;
using Gsa.Missions.Gameplay;
using Godot;
using Dictionary = Godot.Collections.Dictionary;

namespace Gsa.Engine.World;

/// <summary>Renders and collides against one neutral, versioned definition of the tutorial block with GTA III presentation.</summary>
public partial class TutorialBlockScene : Node3D
{
    private const string ManifestPath = "res://world/tutorial-block.json";
    private const string SkyCachePath = "res://world/tutorial-sky.json";
    private readonly GodotCoordinateAdapter _coordinates = new();
    private readonly SemanticRegistry _registry = new();
    private WorldSceneManifest _manifest = null!;
    private Node3D? _worldRoot;
    private Node3D? _visualCityRoot;
    private Node3D? _benchRoot;
    private SkyCacheAdapter? _skyCache;
    private MobilityBenchAdapter? _mobilityBench;
    private CameraBenchRig? _cameraBench;
    private TrafficBenchAdapter? _trafficBench;
    private LidarBenchRig? _lidarBench;
    private LocomotionSimulator? _liveLocomotion;
    private GimbalinaStabilizer _gimbalina = new();
    private WorldCalendarAndWeatherController _calendarWeather = new();
    private GsaAtmosphereController? _atmosphere;
    private GsaCityVisualBuilder? _cityVisuals;
    private GsaThirdPersonCamera? _chaseCamera;
    private GsaProceduralAudio? _audio;
    private ObstacleBoxEnu[] _liveObstacles = [];
    private double _lastSpeedMps;
    private int _renderFrames;
    private bool _smoke;
    private bool _reloaded;
    private int _physicsFrames;
    private string _originalSnapshot = "";
    private GsaPerfProbe? _perfProbe;

    public bool HasSkyCache => _skyCache is not null;
    public string GeometryHash => _manifest.CanonicalGeometryHash;
    public int ColliderCount => _registry.ColliderCount;

    public Dictionary GetSemanticObject(string id) => _registry.GetById(id);
    public string[] GetSemanticIds() => _registry.GetIds();

    public override void _Ready()
    {
        _perfProbe = GsaPerfProbe.TryCreateFromArgs(this, "tutorial_block");
        _smoke = OS.GetCmdlineUserArgs().Any(arg => arg is "--smoke-test" or "--bench-smoke");
        LocalizationService.EnsureCatalogsLoaded();
        try
        {
            _manifest = WorldSceneManifest.Parse(Godot.FileAccess.GetFileAsString(ManifestPath));
            if (Godot.FileAccess.FileExists(SkyCachePath))
                _skyCache = new SkyCacheAdapter(_manifest, Godot.FileAccess.GetFileAsString(SkyCachePath));
            BuildWorld();
            BuildInspectionView();
            BuildBenchSystems();
            InitializeTraining();
            SetPhysicsProcess(true);
        }
        catch (Exception error)
        {
            Fail(error);
        }
    }

    public override void _Process(double delta) => _perfProbe?.StepFrame(this);

    public override void _PhysicsProcess(double delta)
    {
        if (!_smoke)
        {
            StepInteractiveBench(delta);
            return;
        }
        // Query the direct space only inside the physics callback, after server registration.
        if (++_physicsFrames < 3)
            return;
        try
        {
            VerifyWorld();
            if (!_reloaded)
            {
                _originalSnapshot = Snapshot();
                RebuildWorld();
                _reloaded = true;
                _physicsFrames = 0;
                return;
            }
            Require(Snapshot() == _originalSnapshot, "Reload changed semantic identities, metadata or transforms.");
            VerifySky();
            _mobilityBench!.RunVerificationSuite(_manifest);
            _cameraBench!.RunVerificationSuite(_manifest, _registry, GetWorld3D().DirectSpaceState);
            _trafficBench!.RunVerificationSuite();
            _lidarBench!.RunVerificationSuite(_manifest, _registry, GetWorld3D().DirectSpaceState);
            PilotKitBenchVerifier.RunVerificationSuite();
            EngineBenchmarkHarness.RunVerificationSuite(this, _benchRoot!, _manifest!, _registry!, GetWorld3D().DirectSpaceState);
            var m2Report = TutorialMissionRunner.RunDefaultHist01InMemoryPlaythrough(_manifest!);
            if (!m2Report.FinalReportPassed || m2Report.FinalStars != 5)
                throw new InvalidOperationException("M2 tutorial loop smoke verification failed.");
            GD.Print($"GSA_M2_LOOP_SMOKE_OK mission={m2Report.MissionId} contract={m2Report.ContractId} stars={m2Report.FinalStars}");
            VerifyPlayableTraining();
            FullCityEnginePrecisionVerifier.Run(_manifest);
            SetPhysicsProcess(false);
            GD.Print($"GSA_WORLD_SMOKE_OK objects={_registry.Count} colliders={_registry.ColliderCount} " +
                     $"facades=20 geometry_sha256={GeometryHash}");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            Fail(error);
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_chaseCamera is not null && _chaseCamera.HandleInput(@event))
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        if (TrainingInput(@event))
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        if (@event.IsActionPressed("ui_cancel"))
        {
            GetViewport().SetInputAsHandled();
            if (!_training.SaveBlocked) _training.Save();
            var result = GetTree().ChangeSceneToFile("res://Bootstrap.tscn");
            if (result != Error.Ok)
                Fail(new InvalidOperationException($"Cannot return to bootstrap: {result}"));
        }
    }

    private void BuildWorld()
    {
        _registry.Clear();
        _worldRoot = new Node3D { Name = "WorldGeometry" };
        AddChild(_worldRoot);
        foreach (var item in _manifest.Objects)
        {
            var size = new Vector3((float)item.SizeEnu.X, (float)item.SizeEnu.Z, (float)item.SizeEnu.Y);
            if (!size.IsFinite() || size.X <= 0 || size.Y <= 0 || size.Z <= 0)
                throw new ArgumentOutOfRangeException(nameof(item), "Box dimensions exceed engine precision/range.");
            Node3D node = item.Collision
                ? new StaticBody3D { CollisionLayer = 1, CollisionMask = 1 }
                : new Node3D();
            node.Name = item.Id;
            node.Position = _coordinates.ToGodot(item.PositionEnu);
            _worldRoot.AddChild(node);
            _registry.Register(item, node);
            node.AddChild(new MeshInstance3D
            {
                Name = "Geometry",
                Mesh = CreateBoxMesh(item, size),
                MaterialOverride = CreateMaterial(item),
                CastShadow = item.ObjectClass is "ground" or "survey_control" or "unload_point" || item.Material is "glass" or "water"
                    ? GeometryInstance3D.ShadowCastingSetting.Off
                    : GeometryInstance3D.ShadowCastingSetting.On,
            });
            if (item.Collision)
                node.AddChild(new CollisionShape3D { Name = "Collision", Shape = new BoxShape3D { Size = size } });
        }
    }

    private void RebuildWorld()
    {
        if (_worldRoot is not null)
        {
            RemoveChild(_worldRoot);
            _worldRoot.QueueFree();
        }
        BuildWorld();
    }

    private void BuildInspectionView()
    {
        _atmosphere = new GsaAtmosphereController(this, enableRetroDither: false);
        _visualCityRoot = new Node3D { Name = "CityVisuals" };
        AddChild(_visualCityRoot);
        _cityVisuals = new GsaCityVisualBuilder();
        _cityVisuals.BuildTutorialBlockVisuals(_visualCityRoot, _manifest, _coordinates, _atmosphere);

        var camera = new Camera3D
        {
            Name = "InspectionCamera",
            Position = new Vector3(108, 4.5f, 6.5f),
            Fov = 58f,
            Near = 0.1f,
            Far = 550f,
            Current = true,
        };
        AddChild(camera);
        camera.LookAt(new Vector3(108, 1.4f, -4f), Vector3.Up);
        _chaseCamera = new GsaThirdPersonCamera(camera);
    }

    private void BuildBenchSystems()
    {
        _benchRoot = new Node3D { Name = "BenchSystems" };
        AddChild(_benchRoot);

        _mobilityBench = new MobilityBenchAdapter(_coordinates);
        _mobilityBench.ApplyInputMap(InputActionMap.CreateDefault());
        var startEnu = new Vector3d(500120.0, 7400020.0, 100.0);
        _mobilityBench.AttachAvatar(_benchRoot, startEnu, LocomotionMode.Handcart);

        _cameraBench = new CameraBenchRig(_coordinates);
        _cameraBench.AttachRig(_benchRoot);

        _trafficBench = new TrafficBenchAdapter(_coordinates);
        _trafficBench.AttachBenchMeshes(_benchRoot);

        _lidarBench = new LidarBenchRig(_coordinates);
        _audio = new GsaProceduralAudio(this);

        _liveObstacles = ObstacleBoxEnu.FromWorldObjects(_manifest.Objects).ToArray();
        _liveLocomotion = new LocomotionSimulator(startEnu, Math.PI / 2.0, LocomotionMode.Handcart, _manifest.Bounds);
    }

    private void StepInteractiveBench(double delta)
    {
        if (_mobilityBench is null || _liveLocomotion is null || delta <= 0)
            return;
        GsaScreenshotHelper.TryCaptureOnFrame(this, ++_renderFrames, targetFrame: 18);

        var weather = _calendarWeather.StepPhysical(delta);
        var input = _mobilityBench.SampleLiveInput(_liveLocomotion.ForwardSpeedMetresPerSecond);
        var previousHeading = _liveLocomotion.HeadingRadians;
        var result = _liveLocomotion.Step(delta, input, 0.0, weather.SurfaceGripMultiplier, _liveObstacles);
        if (input.InteractPressed)
            _liveLocomotion.TryInteract(InteractiveTargetEnu.FromWorldObjects(_manifest.Objects), _liveObstacles);

        if (result.Collided && result.ImpactDeltaVMetresPerSecond > 1.2)
        {
            _gimbalina.ApplyStartle(StartleEventKind.Pothole);
            _audio?.PlayAlert();
        }
        var gimbalinaStep = _gimbalina.Step(delta, isResting: Math.Abs(result.ForwardSpeedMetresPerSecond) < 0.1);
        var accelPitch = Math.Clamp((_lastSpeedMps - result.ForwardSpeedMetresPerSecond) * 0.04, -0.14, 0.14);
        var turnRoll = Math.Clamp(input.Steer * Math.Abs(result.ForwardSpeedMetresPerSecond) * 0.03, -0.14, 0.14);
        _lastSpeedMps = result.ForwardSpeedMetresPerSecond;

        _mobilityBench.SetVisualPlatformMode(_liveLocomotion.Mode);
        _mobilityBench.SyncAvatarPose(
            result.PositionEnu,
            result.HeadingRadians,
            dtSeconds: delta,
            forwardSpeedMps: result.ForwardSpeedMetresPerSecond,
            steerInput: input.Steer,
            bodyPitchRadians: accelPitch,
            bodyRollRadians: turnRoll,
            gimbalinaStress: gimbalinaStep.Stress,
            gimbalinaResidualPitchRad: gimbalinaStep.ResidualPitchRadians,
            gimbalinaResidualRollRad: gimbalinaStep.ResidualRollRadians,
            isNight: weather.IsNight,
            isBraking: input.Brake > 0.1);

        if (_liveLocomotion.Mode == LocomotionMode.OnFoot)
            _mobilityBench.SyncParkedVehicle(_benchRoot!, _liveLocomotion.MountedVehicleMode, _liveLocomotion.HandcartPositionEnu, _liveLocomotion.HandcartHeadingRadians);
        else
            _mobilityBench.SyncParkedVehicle(_benchRoot!, null, Vector3d.Zero, 0.0);

        _trafficBench?.StepAndSync(delta, result.PositionEnu);
        _cityVisuals?.UpdateAnimatedProps(delta, _trafficBench?.Simulator);

        var focusGodot = _coordinates.ToGodot(result.PositionEnu);
        _atmosphere?.Update(weather, focusGodot, delta);
        _audio?.Step(delta, gimbalinaStep.Stress, _liveLocomotion.Mode <= LocomotionMode.Bicycle, weather.IsRaining);

        var angleDelta = Math.Atan2(Math.Sin(result.HeadingRadians - previousHeading), Math.Cos(result.HeadingRadians - previousHeading));
        StepTraining(delta, angleDelta / delta);
    }

    private static Mesh CreateBoxMesh(WorldBoxObject item, Vector3 size) => item.ObjectClass switch
    {
        "ground" => GsaMeshBuilder.CreateWorldUvBoxMesh(size, 0.12f),
        "building_shell" => GsaMeshBuilder.CreateWorldUvBoxMesh(size, 0.095f),
        "building_facade" => GsaMeshBuilder.CreateWorldUvBoxMesh(size, 0.14f),
        _ when item.Material == "glass" => GsaMeshBuilder.CreateWorldUvBoxMesh(size, 0.10f),
        _ when item.Material == "water" => GsaMeshBuilder.CreateWorldUvBoxMesh(size, 0.08f),
        _ => new BoxMesh { Size = size },
    };

    private static StandardMaterial3D CreateMaterial(WorldBoxObject item)
    {
        if (item.ObjectClass == "ground")
        {
            return GsaRetroTextures.CreateWorldMaterial(
                "world_ground_asphalt_uv", "asphalt", new Color(0.88f, 0.90f, 0.92f),
                Vector3.One, roughness: 0.9f, triplanar: false, vertexShaded: false);
        }
        if (item.ObjectClass == "building_shell")
        {
            var idx = Math.Abs(item.Id.GetHashCode()) % 3;
            var (key, tex) = idx switch
            {
                0 => ("bldg_shell_comm_uv", "facade_commercial"),
                1 => ("bldg_shell_res_uv", "facade_residential"),
                _ => ("bldg_shell_conc_uv", "facade_concrete"),
            };
            return GsaRetroTextures.CreateWorldMaterial(
                key, tex, Colors.White, Vector3.One, roughness: 0.85f, triplanar: false, nightWindowGlow: true);
        }
        if (item.ObjectClass == "building_facade")
        {
            return GsaRetroTextures.CreateWorldMaterial(
                "bldg_target_facade_uv", "facade_commercial", new Color(1.0f, 0.95f, 0.84f),
                Vector3.One, roughness: 0.82f, triplanar: false);
        }
        if (item.Material == "glass")
        {
            return GsaRetroTextures.CreateWorldMaterial(
                "world_glass_mat_uv", "glass_tower", new Color(0.55f, 0.88f, 0.98f, 0.52f),
                Vector3.One, roughness: 0.16f, triplanar: false, transparent: true);
        }
        if (item.Material == "water")
        {
            return GsaRetroTextures.CreateWorldMaterial(
                "world_water_mat_uv", "water", new Color(0.35f, 0.72f, 0.95f, 0.78f),
                Vector3.One, roughness: 0.12f, triplanar: false, transparent: true, vertexShaded: false);
        }
        if (item.ObjectClass == "office_entry")
            return GsaMeshBuilder.SolidMat(new Color(0.14f, 0.62f, 0.45f), emit: true, emissionEnergy: 0.35f);
        if (item.ObjectClass == "survey_control")
            return GsaMeshBuilder.SolidMat(new Color(0.96f, 0.77f, 0.23f), emit: true, emissionEnergy: 0.45f);

        var color = item.Material switch
        {
            "asphalt" => new Color(0.19f, 0.22f, 0.24f),
            "concrete" => new Color(0.62f, 0.64f, 0.61f),
            "metal" => new Color(0.63f, 0.68f, 0.71f),
            "vegetation" => new Color(0.22f, 0.45f, 0.23f),
            _ => throw new InvalidOperationException($"Unknown material: {item.Material}"),
        };
        return GsaMeshBuilder.SolidMat(color, roughness: 0.88f);
    }

    private void VerifyWorld()
    {
        Require(_worldRoot is not null && _registry.Count == _manifest.Objects.Count,
            "The world omitted semantic objects.");
        var facades = _manifest.Objects.Where(item => item.ObjectClass == "building_facade").ToArray();
        Require(facades.Length == 20, "The tutorial block must contain twenty facades.");
        for (var index = 1; index <= 20; index++)
        {
            var expected = $"facade_{index:00}";
            var definition = facades.Single(item => item.Id == expected);
            var metadata = _registry.GetById(expected);
            Require(metadata["id"].AsString() == expected && metadata["material"].AsString() == "concrete",
                "Facade identity or material changed.");
            Require(definition.Tags.Contains("feature") && definition.Tags.Contains("privacy_review") &&
                    definition.SurfaceIds.Contains("commerce_facade_planes"), "Facade semantic tags are incomplete.");
            var node = _worldRoot!.GetNode<Node3D>(expected);
            Require(node.Position == _coordinates.ToGodot(definition.PositionEnu), "Scene position differs from ENU source.");
            Require(_coordinates.ToMap(node.Position) == definition.PositionEnu, "Scene ENU round-trip failed.");
        }
        Require(_registry.ColliderCount == _manifest.Objects.Count(item => item.Collision),
            "Collider count does not match the manifest.");
        Require(_worldRoot!.GetChildren().OfType<StaticBody3D>().Count() == _registry.ColliderCount,
            "Scene has missing or unregistered colliders.");
        foreach (var node in _worldRoot.GetChildren().OfType<StaticBody3D>())
        {
            Require(node.HasMeta("semantic_id") && node.HasMeta("semantic_class") &&
                    node.HasMeta("semantic_material") && node.HasMeta("surface_ids") && node.HasMeta("semantic_tags"),
                "A collider lacks semantic metadata.");
            Require(_registry.GetByInstanceId(node.GetInstanceId()).Count > 0, "Collider instance ID is not registered.");
        }
        foreach (var material in new[] { "concrete", "glass", "water" })
            Require(_manifest.Objects.Any(item => item.Material == material), "A required material sample is missing.");
        Require(_registry.GetById("office_entry").Count > 0 && _registry.GetById("unload_point").Count > 0 &&
                _manifest.Objects.Count(item => item.ObjectClass == "survey_control") == 2,
            "Office, unloading point or survey controls are missing.");

        var sample = new Vector3d(500100, 7400200, 103);
        Require(Math.Abs(LocalGeoidModel.Separation(sample) - 10.2) < 1e-9 &&
                Math.Abs(LocalGeoidModel.ToOrthometricHeight(sample) - 92.8) < 1e-9,
            "Local height model differs from the documented datum.");

        var mutation = _registry.GetById("facade_01");
        mutation["material"] = "mutated_by_caller";
        mutation["tags"].AsGodotArray<string>().Clear();
        Require(_registry.GetById("facade_01")["material"].AsString() == "concrete" &&
                _registry.GetById("facade_01")["tags"].AsGodotArray<string>().Count > 0,
            "Semantic snapshot exposed shared mutable state.");

        using var query = PhysicsRayQueryParameters3D.Create(
            _coordinates.ToGodot(new Vector3d(500020, 7400001, 101)),
            _coordinates.ToGodot(new Vector3d(500020, 7400010, 101)), 1);
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        Require(hit.Count > 0, "Real physics ray missed the first facade.");
        var collider = hit["collider"].AsGodotObject();
        Require(_registry.GetByInstanceId(collider.GetInstanceId())["id"].AsString() == "facade_01",
            "Physics hit did not resolve to the stable facade semantic ID.");
    }

    private void VerifySky()
    {
        Require(_skyCache is not null && _skyCache.Cache.Cells.Count > 0, "Sky cache was not prepared for the scene.");
        var cell = _skyCache!.Cache.Cells[0];
        Require(_skyCache.TryGetOpenness(_coordinates.ToGodot(cell.CenterEnu), out var openness) &&
                Math.Abs(openness - cell.Openness) < 1e-12,
            "Sky cache import/query disagrees with the neutral precomputation.");
        GD.Print($"GSA_SKY_SMOKE_OK geometry_sha256={GeometryHash}");
    }

    private string Snapshot() => string.Join("\n", _registry.GetIds().Select(id =>
        id + ":" + Json.Stringify(_registry.GetById(id)) + ":" + _worldRoot!.GetNode<Node3D>(id).Position)) +
        "\n" + GeometryHash + ":" + _registry.ColliderCount;

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private void Fail(Exception error)
    {
        SetPhysicsProcess(false);
        GD.PushError(error.ToString());
        GetTree().Quit(1);
    }
}
