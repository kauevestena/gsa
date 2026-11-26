using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.World;
using Xunit;

namespace Gsa.Core.Tests;

public sealed class MobilityTests
{
    private static readonly WorldBounds TestBounds = new(
        new Vector3d(500000, 7400000, 99.5),
        new Vector3d(500240, 7400060, 115));

    [Fact]
    public void InitialProfilesRespectNormativeLimitsAndNoLidarBeforeLevel4()
    {
        Assert.Equal(3.0, PlatformProfile.OnFoot.MaxSpeedMetresPerSecond);
        Assert.Equal(0, PlatformProfile.Handcart.Level);
        Assert.Equal(2.0, PlatformProfile.Handcart.MaxSpeedMetresPerSecond);
        Assert.Equal(200, PlatformProfile.Handcart.VirtualStorageCapacityUnits);
        Assert.Equal(1800.0, PlatformProfile.Handcart.PhysicalAutonomySeconds);
        Assert.Equal(0, PlatformProfile.Handcart.PriceCoords);
        Assert.False(PlatformProfile.OnFoot.SupportsLidarSlots);
        Assert.False(PlatformProfile.Handcart.SupportsLidarSlots);
    }

    [Fact]
    public void UphillTurnAndBrakingStayControllableWithinSpeedLimit()
    {
        var sim = new LocomotionSimulator(new Vector3d(500050, 7400020, 100), 0.0, LocomotionMode.Handcart, TestBounds);
        const double dt = 1.0 / 60.0;
        var initialZ = sim.PositionEnu.Z;

        for (var i = 0; i < 120; i++)
        {
            var step = sim.Step(dt, new LocomotionInput(1.0, 0.0, 0.35), slopeGrade: 0.10, surfaceGrip: 1.0);
            Assert.True(step.ForwardSpeedMetresPerSecond <= PlatformProfile.Handcart.MaxSpeedMetresPerSecond + 1e-9);
        }

        Assert.True(sim.PositionEnu.Z > initialZ + 0.1);
        Assert.True(sim.HeadingRadians > 0.2);

        for (var i = 0; i < 60; i++)
            sim.Step(dt, new LocomotionInput(0.0, 1.0, 0.0), slopeGrade: 0.10, surfaceGrip: 1.0);

        Assert.True(Math.Abs(sim.ForwardSpeedMetresPerSecond) < 0.05);
    }

    [Fact]
    public void ObstacleCollisionPreventsPenetrationAndReportsImpactDeltaV()
    {
        var obstacles = new[]
        {
            new ObstacleBoxEnu("walk_obstacle", new Vector3d(500125, 7400035, 100.5), new Vector3d(2, 2, 1), Collision: true),
            new ObstacleBoxEnu("tutorial_ground", new Vector3d(500120, 7400030, 99.75), new Vector3d(240, 60, 0.5), Collision: true),
        };
        var sim = new LocomotionSimulator(new Vector3d(500122, 7400035, 100.0), 0.0, LocomotionMode.Handcart, TestBounds);
        var sawCollision = false;
        var maxDeltaV = 0.0;

        for (var i = 0; i < 180; i++)
        {
            var step = sim.Step(1.0 / 60.0, new LocomotionInput(1.0, 0.0, 0.0), 0.0, 1.0, obstacles);
            if (step.Collided)
            {
                sawCollision = true;
                Assert.Equal("walk_obstacle", step.CollidedObjectId);
                maxDeltaV = Math.Max(maxDeltaV, step.ImpactDeltaVMetresPerSecond);
            }
            Assert.True(sim.PositionEnu.X < 500124.0);
        }

        Assert.True(sawCollision);
        Assert.True(maxDeltaV > 0.5);
    }

    [Fact]
    public void MountDismountRequiresStationaryVehicleAndFreeExitSpace()
    {
        var obstacles = new[]
        {
            new ObstacleBoxEnu("wall_east", new Vector3d(500051.2, 7400020, 101), new Vector3d(0.4, 6, 2), Collision: true),
            new ObstacleBoxEnu("wall_west", new Vector3d(500048.8, 7400020, 101), new Vector3d(0.4, 6, 2), Collision: true),
            new ObstacleBoxEnu("wall_north", new Vector3d(500050, 7400021.2, 101), new Vector3d(4, 0.4, 2), Collision: true),
            new ObstacleBoxEnu("wall_south", new Vector3d(500050, 7400018.8, 101), new Vector3d(4, 0.4, 2), Collision: true),
        };

        var boxed = new LocomotionSimulator(new Vector3d(500050, 7400020, 100), 0.0, LocomotionMode.Handcart, TestBounds);
        Assert.False(boxed.TryToggleMount(obstacles, out var boxedReason));
        Assert.Equal("mobility.mount.exit_blocked", boxedReason);

        var open = new LocomotionSimulator(new Vector3d(500050, 7400020, 100), 0.0, LocomotionMode.Handcart, TestBounds);
        for (var i = 0; i < 30; i++)
            open.Step(1.0 / 60.0, new LocomotionInput(1.0, 0.0, 0.0));
        Assert.False(open.TryToggleMount(null, out var movingReason));
        Assert.Equal("mobility.mount.vehicle_moving", movingReason);

        for (var i = 0; i < 60; i++)
            open.Step(1.0 / 60.0, new LocomotionInput(0.0, 1.0, 0.0));
        Assert.True(open.TryToggleMount(null, out _));
        Assert.Equal(LocomotionMode.OnFoot, open.Mode);

        // Walk away beyond 1.8 m -> cannot remount remotely.
        for (var i = 0; i < 90; i++)
            open.Step(1.0 / 60.0, new LocomotionInput(1.0, 0.0, 0.0));
        for (var i = 0; i < 60; i++)
            open.Step(1.0 / 60.0, new LocomotionInput(0.0, 1.0, 0.0));
        Assert.True(open.TryToggleMount(null, out var farReason) == false && farReason == "mobility.mount.too_far");
    }

    [Fact]
    public void ContextInteractionsEnforceOnFootControlOccupationAndTargets()
    {
        var targets = new[]
        {
            new InteractiveTargetEnu("control_old_center_01", "survey_control", new Vector3d(500010, 7400002, 100.05)),
            new InteractiveTargetEnu("unload_point", "unload_point", new Vector3d(500225, 7400002, 100.1)),
            new InteractiveTargetEnu("office_entry", "office_entry", new Vector3d(500232, 7400005, 101.5)),
        };

        var sim = new LocomotionSimulator(new Vector3d(500010, 7400002.8, 100), 0.0, LocomotionMode.Handcart, TestBounds);
        var blocked = sim.TryInteract(targets);
        Assert.Equal(InteractionKind.BlockedMustDismount, blocked.Kind);

        Assert.True(sim.TryToggleMount(null, out _));
        var onFootControl = new LocomotionSimulator(new Vector3d(500010, 7400002.8, 100), 0.0, LocomotionMode.OnFoot, TestBounds);
        Assert.Equal(InteractionKind.OccupySurveyControl, onFootControl.TryInteract(targets).Kind);

        var atUnload = new LocomotionSimulator(new Vector3d(500225, 7400002.8, 100), 0.0, LocomotionMode.Handcart, TestBounds);
        Assert.Equal(InteractionKind.UnloadBatch, atUnload.TryInteract(targets).Kind);

        var atOffice = new LocomotionSimulator(new Vector3d(500232, 7400004.0, 100), 0.0, LocomotionMode.OnFoot, TestBounds);
        Assert.Equal(InteractionKind.EnterOffice, atOffice.TryInteract(targets).Kind);
    }

    [Fact]
    public void InputRemappingPersistsAcrossJsonAndRejectsDuplicatesOrUnknownActions()
    {
        var remapped = InputActionMap.CreateDefault()
            .RemapKeyboard(GameInputAction.Accelerate, "Up")
            .RemapGamepad(GameInputAction.Capture, "gamepad_y");

        var roundTrip = InputActionMap.FromJson(remapped.ToJson());
        Assert.Equal("Up", roundTrip.GetBinding(GameInputAction.Accelerate).Keyboard);
        Assert.Equal("gamepad_y", roundTrip.GetBinding(GameInputAction.Capture).Gamepad);

        var active = new HashSet<string>(StringComparer.Ordinal) { "Up" };
        Assert.Equal(1.0, roundTrip.EvaluateLocomotion(active).Throttle);
        Assert.Equal(0.0, roundTrip.EvaluateLocomotion(new HashSet<string>(StringComparer.Ordinal) { "W" }).Throttle);

        Assert.Throws<ArgumentException>(() => remapped.RemapKeyboard(GameInputAction.Brake, "Up"));
        Assert.Throws<ArgumentException>(() => remapped.RemapGamepad(GameInputAction.Horn, "gamepad_y"));
        Assert.Throws<FormatException>(() => InputActionMap.FromJson("{\"schema_version\":2,\"actions\":{}}"));
        Assert.Throws<FormatException>(() => InputActionMap.FromJson("{\"schema_version\":1,\"actions\":{\"unknown\":{\"keyboard\":\"K\",\"gamepad\":\"G\"}}}"));
    }

    [Fact]
    public void VirtualStorageAndAutonomyExhaustionStopCaptureWithExplicitReason()
    {
        var sim = new LocomotionSimulator(new Vector3d(500050, 7400020, 100), 0.0, LocomotionMode.Handcart, TestBounds);
        Assert.True(sim.TryConsumeCaptureUnits(200, out var okReason));
        Assert.Equal(CaptureStopReason.None, okReason);
        Assert.False(sim.TryConsumeCaptureUnits(1, out var fullReason));
        Assert.Equal(CaptureStopReason.VirtualStorageFull, fullReason);

        var autonomySim = new LocomotionSimulator(new Vector3d(500050, 7400020, 100), 0.0, LocomotionMode.Handcart, sceneBounds: null);
        for (var i = 0; i < 1800 * 10; i++)
            autonomySim.Step(0.1, new LocomotionInput(1.0, 0.0, 0.0));
        Assert.Equal(0.0, autonomySim.RemainingAutonomySeconds);
        Assert.False(autonomySim.TryConsumeCaptureUnits(1, out var autonomyReason));
        Assert.Equal(CaptureStopReason.AutonomyExhausted, autonomyReason);
    }

    [Fact]
    public void SpawnWithHandcartCanWalkSouthOntoStreetStepOverUnloadPadAndDismount()
    {
        var playableBounds = new WorldBounds(
            new Vector3d(500000, 7399976, 99.5),
            new Vector3d(500240, 7400060, 115));

        var manifestItems = new[]
        {
            (ObjectClass: "ground", Box: new ObstacleBoxEnu("tutorial_ground", new Vector3d(500120, 7400030, 99.75), new Vector3d(240, 60, 0.5), Collision: true)),
            (ObjectClass: "office_entry", Box: new ObstacleBoxEnu("office_entry", new Vector3d(500232, 7400005, 101.5), new Vector3d(4, 0.2, 3), Collision: true)),
            (ObjectClass: "unload_point", Box: new ObstacleBoxEnu("unload_point", new Vector3d(500225, 7400002, 100.1), new Vector3d(2, 2, 0.2), Collision: true)),
            (ObjectClass: "survey_control", Box: new ObstacleBoxEnu("control_old_center_01", new Vector3d(500010, 7400002, 100.05), new Vector3d(0.25, 0.25, 0.1), Collision: true)),
            (ObjectClass: "survey_control", Box: new ObstacleBoxEnu("control_old_center_02", new Vector3d(500230, 7400025, 100.05), new Vector3d(0.25, 0.25, 0.1), Collision: true)),
            (ObjectClass: "building_shell", Box: new ObstacleBoxEnu("building_shell_20", new Vector3d(500210, 7400010.4, 104), new Vector3d(8, 10, 8), Collision: true)),
        };

        var obstacles = manifestItems
            .Where(o => o.ObjectClass is not ("ground" or "unload_point" or "survey_control"))
            .Select(o => o.Box)
            .ToArray();

        var sim = new LocomotionSimulator(new Vector3d(500228.0, 7400000.7, 100.0), Math.PI, LocomotionMode.Handcart, playableBounds);
        const double dt = 1.0 / 60.0;

        // Drive west across the unload_point pad without getting wedged.
        for (var i = 0; i < 240; i++)
        {
            var step = sim.Step(dt, new LocomotionInput(1.0, 0.0, 0.0), 0.0, 1.0, obstacles);
            Assert.False(step.Collided);
        }
        Assert.True(sim.PositionEnu.X < 500223.0);

        // Stop, dismount onto foot, and walk south onto the street / sidewalk.
        for (var i = 0; i < 45; i++)
            sim.Step(dt, new LocomotionInput(0.0, 1.0, 0.0), 0.0, 1.0, obstacles);
        Assert.True(sim.TryToggleMount(obstacles, out var reason), reason);
        Assert.Equal(LocomotionMode.OnFoot, sim.Mode);

        // Turn to face south (-PI / 2) and walk south onto South Commerce Blvd.
        for (var i = 0; i < 30; i++)
        {
            var steer = Math.Abs(sim.HeadingRadians + Math.PI / 2.0) > 0.08 ? 1.0 : 0.0;
            sim.Step(dt, new LocomotionInput(0.0, 0.0, steer), 0.0, 1.0, obstacles);
        }
        for (var i = 0; i < 90; i++)
            sim.Step(dt, new LocomotionInput(1.0, 0.0, 0.0), 0.0, 1.0, obstacles);

        Assert.True(sim.PositionEnu.Y < 7399998.0);
    }
}
