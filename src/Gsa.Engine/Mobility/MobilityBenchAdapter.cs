using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.World;
using Godot;

namespace Gsa.Engine.Mobility;

/// <summary>Bridges core locomotion and remappable InputActionMap with Godot InputMap and scene physics.</summary>
public sealed class MobilityBenchAdapter
{
    public const string ActionPrefix = "gsa_";
    private const string SmokeBindingsPath = "user://input_bindings_smoke.json";

    private readonly GodotCoordinateAdapter _coordinates;
    private CharacterBody3D? _avatarBody;
    private InputActionMap _actionMap = InputActionMap.CreateDefault();

    public MobilityBenchAdapter(GodotCoordinateAdapter coordinates)
    {
        ArgumentNullException.ThrowIfNull(coordinates);
        _coordinates = coordinates;
    }

    public InputActionMap CurrentActionMap => _actionMap;

    public CharacterBody3D AttachAvatar(Node3D parent, Vector3d initialPositionEnu)
    {
        ArgumentNullException.ThrowIfNull(parent);
        var body = new CharacterBody3D
        {
            Name = "PlayerRig",
            Position = _coordinates.ToGodot(initialPositionEnu),
            CollisionLayer = 2,
            CollisionMask = 1,
        };
        body.AddChild(new MeshInstance3D
        {
            Name = "HandcartMesh",
            Position = new Vector3(0, 0.55f, 0),
            Mesh = new BoxMesh { Size = new Vector3(0.9f, 1.1f, 0.9f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.86f, 0.48f, 0.16f) },
        });
        body.AddChild(new CollisionShape3D
        {
            Name = "HandcartCollision",
            Position = new Vector3(0, 0.625f, 0),
            Shape = new BoxShape3D { Size = new Vector3(0.9f, 1.25f, 0.9f) },
        });
        parent.AddChild(body);
        _avatarBody = body;
        return body;
    }

    public void ApplyInputMap(InputActionMap actionMap)
    {
        ArgumentNullException.ThrowIfNull(actionMap);
        _actionMap = actionMap;
        foreach (var (wireName, binding) in actionMap.GetAllByWireName())
        {
            var actionName = new StringName(ActionPrefix + wireName);
            if (!InputMap.HasAction(actionName))
                InputMap.AddAction(actionName);
            InputMap.ActionEraseEvents(actionName);

            var keycode = ParseKey(binding.Keyboard);
            if (keycode != Key.None)
                InputMap.ActionAddEvent(actionName, new InputEventKey { Keycode = keycode });
        }
    }

    public static void SaveBindings(string userPath, InputActionMap actionMap)
    {
        ArgumentNullException.ThrowIfNull(actionMap);
        var resolved = ProjectSettings.GlobalizePath(userPath);
        var directory = Path.GetDirectoryName(resolved);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        using var file = Godot.FileAccess.Open(userPath, Godot.FileAccess.ModeFlags.Write)
                         ?? throw new IOException($"Cannot write input bindings to {userPath}: {Godot.FileAccess.GetOpenError()}");
        file.StoreString(actionMap.ToJson());
    }

    public static InputActionMap LoadBindings(string userPath)
    {
        if (!Godot.FileAccess.FileExists(userPath))
            throw new FileNotFoundException($"Bindings file does not exist: {userPath}");
        return InputActionMap.FromJson(Godot.FileAccess.GetFileAsString(userPath));
    }

    public LocomotionInput SampleLiveInput()
    {
        double Read(GameInputAction action) =>
            Input.IsActionPressed(ActionPrefix + InputActionMap.WireName(action)) ? 1.0 : 0.0;

        var throttle = Read(GameInputAction.Accelerate);
        var brake = Read(GameInputAction.Brake);
        var steer = Read(GameInputAction.SteerLeft) - Read(GameInputAction.SteerRight);
        var interact = Input.IsActionJustPressed(ActionPrefix + InputActionMap.WireName(GameInputAction.Interact));
        return new LocomotionInput(throttle, brake, steer, interact);
    }

    public void SyncAvatarPose(Vector3d positionEnu, double headingRadians)
    {
        if (_avatarBody is null)
            return;
        _avatarBody.Position = _coordinates.ToGodot(positionEnu);
        _avatarBody.Rotation = new Vector3(0, (float)(headingRadians - Math.PI / 2.0), 0);
    }

    public void RunVerificationSuite(WorldSceneManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        // 1. Remapping and persistence round-trip via user:// storage and Godot InputMap.
        var remapped = InputActionMap.CreateDefault()
            .RemapKeyboard(GameInputAction.Accelerate, "I")
            .RemapGamepad(GameInputAction.Accelerate, "gamepad_rb");
        SaveBindings(SmokeBindingsPath, remapped);
        var reloaded = LoadBindings(SmokeBindingsPath);
        ApplyInputMap(reloaded);
        try
        {
            Require(reloaded.GetBinding(GameInputAction.Accelerate).Keyboard == "I" &&
                    reloaded.GetBinding(GameInputAction.Accelerate).Gamepad == "gamepad_rb",
                "Remapped keyboard/gamepad binding did not persist across reload.");
            Require(InputMap.HasAction(ActionPrefix + "accelerate") &&
                    InputMap.ActionGetEvents(ActionPrefix + "accelerate").Count > 0,
                "Godot InputMap did not register remapped action.");
            var remappedInput = reloaded.EvaluateLocomotion(new HashSet<string>(StringComparer.Ordinal) { "I" });
            var oldKeyInput = reloaded.EvaluateLocomotion(new HashSet<string>(StringComparer.Ordinal) { "W" });
            Require(remappedInput.Throttle == 1.0 && oldKeyInput.Throttle == 0.0,
                "Remapped input evaluation did not switch control key.");
        }
        finally
        {
            var absPath = ProjectSettings.GlobalizePath(SmokeBindingsPath);
            if (File.Exists(absPath))
                File.Delete(absPath);
            ApplyInputMap(InputActionMap.CreateDefault());
        }

        // 2. Uphill climb, turning, obstacle collision, mount/dismount, and interactions.
        var obstacles = ObstacleBoxEnu.FromWorldObjects(manifest.Objects);
        var targets = InteractiveTargetEnu.FromWorldObjects(manifest.Objects);
        var sim = new LocomotionSimulator(
            new Vector3d(500115.0, 7400035.0, 100.0),
            initialHeadingRadians: 0.0,
            initialMode: LocomotionMode.Handcart,
            sceneBounds: manifest.Bounds);

        const double dt = 1.0 / 60.0;
        var startZ = sim.PositionEnu.Z;
        for (var i = 0; i < 90; i++)
        {
            var step = sim.Step(dt, new LocomotionInput(1.0, 0.0, 0.25), slopeGrade: 0.08, surfaceGrip: 1.0, obstacles);
            Require(step.ForwardSpeedMetresPerSecond <= PlatformProfile.Handcart.MaxSpeedMetresPerSecond + 1e-6,
                "Handcart exceeded max speed.");
        }
        Require(sim.PositionEnu.Z > startZ + 0.05 && Math.Abs(sim.HeadingRadians) > 0.1,
            "Uphill and turning maneuver did not advance climb or heading.");

        // Collide intentionally with walk_obstacle at [500125, 7400035, 100.5].
        var collisionSim = new LocomotionSimulator(
            new Vector3d(500122.0, 7400035.0, 100.0),
            initialHeadingRadians: 0.0,
            initialMode: LocomotionMode.Handcart,
            sceneBounds: manifest.Bounds);
        var sawObstacleCollision = false;
        for (var i = 0; i < 180; i++)
        {
            var step = collisionSim.Step(dt, new LocomotionInput(1.0, 0.0, 0.0), 0.0, 1.0, obstacles);
            if (step.Collided && step.CollidedObjectId == "walk_obstacle")
                sawObstacleCollision = true;
            Require(collisionSim.PositionEnu.X < 500124.0,
                "Locomotion penetrated walk_obstacle solid bounds.");
        }
        Require(sawObstacleCollision, "Handcart did not detect collision against walk_obstacle.");

        // Stop, test blocked survey control on handcart, dismount, walk on foot at <= 3 m/s, occupy control.
        var controlSim = new LocomotionSimulator(
            new Vector3d(500010.0, 7400003.0, 100.0),
            initialHeadingRadians: -Math.PI / 2.0,
            initialMode: LocomotionMode.Handcart,
            sceneBounds: manifest.Bounds);
        Require(controlSim.TryInteract(targets, obstacles).Kind == InteractionKind.BlockedMustDismount,
            "Survey control occupation must require dismounting the handcart.");
        Require(controlSim.TryToggleMount(obstacles, out _) && controlSim.Mode == LocomotionMode.OnFoot,
            "Failed to dismount handcart when stationary.");
        for (var i = 0; i < 60; i++)
        {
            var step = controlSim.Step(dt, new LocomotionInput(1.0, 0.0, 0.0), 0.0, 1.0, obstacles);
            Require(step.ForwardSpeedMetresPerSecond <= PlatformProfile.OnFoot.MaxSpeedMetresPerSecond + 1e-6,
                "On-foot speed exceeded 3.0 m/s.");
        }

        // Move back near control_old_center_01 and occupy it on foot.
        var atControl = new LocomotionSimulator(
            new Vector3d(500010.0, 7400002.2, 100.0),
            initialHeadingRadians: 0.0,
            initialMode: LocomotionMode.OnFoot,
            sceneBounds: manifest.Bounds);
        Require(atControl.TryInteract(targets, obstacles).Kind == InteractionKind.OccupySurveyControl,
            "On-foot player failed to occupy nearby survey control.");

        // Verify unload_point and office_entry interactions.
        var atUnload = new LocomotionSimulator(new Vector3d(500225.0, 7400002.2, 100.0), 0.0, LocomotionMode.Handcart, manifest.Bounds);
        Require(atUnload.TryInteract(targets, obstacles).Kind == InteractionKind.UnloadBatch,
            "Handcart failed to interact with unload_point.");

        var atOffice = new LocomotionSimulator(new Vector3d(500232.0, 7400004.0, 100.0), 0.0, LocomotionMode.OnFoot, manifest.Bounds);
        Require(atOffice.TryInteract(targets, obstacles).Kind == InteractionKind.EnterOffice,
            "Player failed to interact with office_entry.");

        // Verify resource limit stops capture explicitly.
        Require(sim.TryConsumeCaptureUnits(200, out _), "Failed to consume virtual storage capacity up to limit.");
        Require(!sim.TryConsumeCaptureUnits(1, out var stopReason) && stopReason == CaptureStopReason.VirtualStorageFull,
            "Exceeding virtual storage did not stop capture with expected cause.");

        SyncAvatarPose(sim.PositionEnu, sim.HeadingRadians);
        GD.Print($"GSA_MOBILITY_SMOKE_OK on_foot_max=3.0 handcart_max=2.0 storage_cap=200");
    }

    private static Key ParseKey(string name)
    {
        if (string.Equals(name, "Space", StringComparison.OrdinalIgnoreCase))
            return Key.Space;
        if (string.Equals(name, "Escape", StringComparison.OrdinalIgnoreCase))
            return Key.Escape;
        var parsed = OS.FindKeycodeFromString(name);
        return parsed;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
