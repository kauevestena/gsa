using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Xunit;

namespace Gsa.Core.Tests;

public sealed class BicycleAndScooterMobilityTests
{
    [Fact]
    public void BicycleAndScooterProfilesCorridorsAndResourceExhaustionStopCaptureCleanly()
    {
        var sim = new LocomotionSimulator(new Vector3d(500050.0, 7400050.0, 100.0), initialMode: LocomotionMode.Bicycle);
        var controller = new MvpVehicleMobilityController(sim);

        Assert.Equal(LocomotionMode.Bicycle, sim.Mode);
        Assert.Equal(6.0, sim.CurrentProfile.MaxSpeedMetresPerSecond, 6);
        Assert.Equal(400, sim.CurrentProfile.VirtualStorageCapacityUnits);
        Assert.Equal(2400.0, sim.RemainingAutonomySeconds, 6);

        // Accelerate along cycleway (1.2 m wide alley allowed for Bicycle, forbidden for Scooter)
        for (var i = 0; i < 120; i++)
            sim.Step(1.0 / 60.0, new LocomotionInput(1.0, 0.0, 0.0));

        Assert.InRange(sim.ForwardSpeedMetresPerSecond, 5.5, 6.01);

        var narrowAlley = new CorridorAccessSpec("alley_01", AccessCorridorKind.NarrowAlleyOrCycleway, 1.2);
        var roadLane = new CorridorAccessSpec("road_01", AccessCorridorKind.UrbanRoadLane, 3.2);
        var sidewalk = new CorridorAccessSpec("sidewalk_01", AccessCorridorKind.SidewalkPromenade, 3.5);

        Assert.True(controller.EvaluateCorridorAccess(narrowAlley).Allowed);
        Assert.True(controller.EvaluateCorridorAccess(roadLane).Allowed);
        Assert.True(controller.EvaluateCorridorAccess(sidewalk).Allowed);

        // Switch to Scooter (Level 2: 12 m/s, 800 storage, 3000 s autonomy)
        sim.SwitchVehiclePlatform(LocomotionMode.Scooter, refillAutonomy: true, clearStorage: true);
        Assert.Equal(LocomotionMode.Scooter, sim.Mode);
        Assert.Equal(12.0, sim.CurrentProfile.MaxSpeedMetresPerSecond, 6);
        Assert.Equal(800, sim.CurrentProfile.VirtualStorageCapacityUnits);
        Assert.Equal(3000.0, sim.RemainingAutonomySeconds, 6);

        Assert.False(controller.EvaluateCorridorAccess(narrowAlley).Allowed);
        Assert.Equal("mobility.access.cycleway_forbidden_for_scooter", controller.EvaluateCorridorAccess(narrowAlley).ReasonKey);
        Assert.False(controller.EvaluateCorridorAccess(sidewalk).Allowed);
        Assert.Equal("mobility.access.scooter_forbidden_on_sidewalk", controller.EvaluateCorridorAccess(sidewalk).ReasonKey);
        Assert.True(controller.EvaluateCorridorAccess(roadLane).Allowed);

        // Virtual storage exhaustion prevents capture before exceeding capacity
        Assert.True(sim.TryConsumeCaptureUnits(800, out _));
        Assert.False(sim.TryConsumeCaptureUnits(1, out var stopReason));
        Assert.Equal(CaptureStopReason.VirtualStorageFull, stopReason);

        var gaugeFull = controller.GetResourceGaugeSnapshot();
        Assert.Equal(800, gaugeFull.UsedStorageUnits);
        Assert.Equal(800, gaugeFull.MaxStorageUnits);
        Assert.Equal(1.0, gaugeFull.StorageFraction, 6);
        Assert.False(gaugeFull.CanContinueCapture);
        Assert.Equal(CaptureStopReason.VirtualStorageFull, gaugeFull.StopReason);
    }

    [Fact]
    public void HighImpactShockTriggersRackEventDestroysAt100DamageAndRepositionRecoversSafely()
    {
        var sim = new LocomotionSimulator(new Vector3d(500120.0, 7400100.0, 100.0), initialMode: LocomotionMode.Scooter);
        var controller = new MvpVehicleMobilityController(sim);

        var minorImpact = controller.RecordImpact(10.0, impactDeltaVMetresPerSecond: 2.0);
        Assert.False(minorImpact.TriggeredRackShockEvent);
        Assert.False(minorImpact.DestroyedVehicle);
        Assert.Equal(30.0, minorImpact.TotalDamagePoints, 6);

        // DeltaV >= 3.0 m/s triggers rack shock event; >= 100 cumulative damage destroys vehicle
        var lethalImpact = controller.RecordImpact(15.0, impactDeltaVMetresPerSecond: 5.0);
        Assert.True(lethalImpact.TriggeredRackShockEvent);
        Assert.True(lethalImpact.DestroyedVehicle);
        Assert.Equal(100.0, controller.VehicleDamagePoints, 6);
        Assert.True(controller.IsDestroyed);
        Assert.False(controller.GetResourceGaugeSnapshot().CanContinueCapture);

        var officePos = new Vector3d(500020.0, 7400010.0, 100.0);
        controller.ResetAfterSafeRecovery(officePos, LocomotionMode.Scooter);
        Assert.False(controller.IsDestroyed);
        Assert.Equal(0.0, controller.VehicleDamagePoints, 6);
        Assert.Equal(officePos, sim.PositionEnu);
        Assert.True(controller.GetResourceGaugeSnapshot().CanContinueCapture);
    }
}

