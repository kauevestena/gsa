using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Camera;
using Gsa.Core.Traffic;
using Xunit;

namespace Gsa.Core.Tests;

public sealed class CalendarWeatherAndHazardsTests
{
    [Fact]
    public void CalendarPauseFreezesTimeAdvanceAppliesToClockRainReducesGripToZeroSixAndReloadsSnapshot()
    {
        var controller = new WorldCalendarAndWeatherController();
        Assert.Equal(8.0, controller.HourOfDay, 6);
        Assert.False(controller.IsNight);
        Assert.True(controller.IsRushHour); // 08:00 within 07:00-09:00
        Assert.Equal(1.0, controller.SurfaceGripMultiplier, 6);

        // 1 physical second = 60 calendar seconds
        controller.StepPhysical(10.0);
        Assert.Equal(28800.0 + 600.0, controller.CalendarTimeSeconds, 6);

        // Pausing completely freezes calendar clock
        controller.SetPaused(true);
        controller.StepPhysical(120.0);
        Assert.Equal(29400.0, controller.CalendarTimeSeconds, 6);

        // Advancing calendar directly updates deadline listeners and rain lowers grip to 0.6
        controller.SetPaused(false);
        double? observedDeadlineClock = null;
        controller.AdvanceCalendarSeconds(13 * 3600.0, t => observedDeadlineClock = t); // 08:10 -> 21:10 (night)
        Assert.NotNull(observedDeadlineClock);
        Assert.True(controller.IsNight);

        controller.SetWeather(isRaining: true, rainIntensity: 0.8);
        var rainSnapshot = controller.ExportSnapshot();
        Assert.True(rainSnapshot.IsRaining);
        Assert.Equal(0.6, rainSnapshot.SurfaceGripMultiplier, 6);

        // Night + weather maps directly into camera environment and dry vs rain adhesion alters locomotion braking/accel
        var camEnv = rainSnapshot.ToCameraEnvironment();
        Assert.True(camEnv.IsNight);

        var drySim = new LocomotionSimulator(Vector3d.Zero, initialMode: LocomotionMode.Scooter);
        var wetSim = new LocomotionSimulator(Vector3d.Zero, initialMode: LocomotionMode.Scooter);
        for (var i = 0; i < 30; i++)
        {
            drySim.Step(1.0 / 60.0, new LocomotionInput(1.0, 0.0, 0.0), surfaceGrip: 1.0);
            wetSim.Step(1.0 / 60.0, new LocomotionInput(1.0, 0.0, 0.0), surfaceGrip: rainSnapshot.SurfaceGripMultiplier);
        }
        Assert.True(wetSim.PositionEnu.X < drySim.PositionEnu.X);

        // Reload restores exact calendar/weather state
        var restored = new WorldCalendarAndWeatherController();
        restored.Restore(rainSnapshot);
        Assert.Equal(rainSnapshot.CalendarTimeSeconds, restored.CalendarTimeSeconds, 6);
        Assert.Equal(rainSnapshot.IsRaining, restored.IsRaining);
        Assert.Equal(rainSnapshot.SurfaceGripMultiplier, restored.SurfaceGripMultiplier, 6);
    }

    [Fact]
    public void ErraticDriversDogsBlitzAndCalmPolicyToggleEnforceAllSafetyGuarantees()
    {
        // Contextual spawn probabilities: 2% day, 8% night, 12% bar weekend, 0% calm
        Assert.Equal(0.02, ErraticDriverSimulator.ComputeSpawnProbability(new ErraticSpawnContext(false, false, false, false)), 6);
        Assert.Equal(0.08, ErraticDriverSimulator.ComputeSpawnProbability(new ErraticSpawnContext(true, false, false, false)), 6);
        Assert.Equal(0.12, ErraticDriverSimulator.ComputeSpawnProbability(new ErraticSpawnContext(true, true, false, false)), 6);
        Assert.Equal(0.00, ErraticDriverSimulator.ComputeSpawnProbability(new ErraticSpawnContext(true, true, true, false)), 6);
        Assert.Equal(0.00, ErraticDriverSimulator.ComputeSpawnProbability(new ErraticSpawnContext(true, true, false, true)), 6);

        var sample = ErraticDriverSimulator.EvaluateDriverStep(
            "drunk_1", timeSeconds: 1.5, periodSeconds: 3.0, normalizedSpeedDelta: 0.3,
            atIntersection: true, infractionRoll01: 0.15, enteredBlitzZone: true);
        Assert.True(sample.HasZigZagCue && sample.HasHornCue && sample.HasHeadlightCue);
        Assert.True(sample.CommittedIntersectionInfraction);
        Assert.True(sample.RemovedByBlitz);

        // Dog pursuit bounds: treat, >40m, and 10s timeout
        var dogTreat = new DogPursuitTracker("dog_1", initialDistanceMetres: 20.0, calmProtectionActive: false);
        Assert.True(dogTreat.Active);
        Assert.False(dogTreat.ApplyTreat().Active);
        Assert.Equal("hazard.dog.pacified_by_treat", dogTreat.EndReasonKey);

        var dogTimeout = new DogPursuitTracker("dog_2", initialDistanceMetres: 25.0, calmProtectionActive: false);
        Assert.False(dogTimeout.Step(10.0, 28.0).Active);
        Assert.Equal("hazard.dog.timeout", dogTimeout.EndReasonKey);

        // Activating Calm City mid-pursuit cancels active dogs AND purges pending hostile events so zero delayed threats land
        var calmCoordinator = new CalmPolicyCoordinator(calmCityEnabled: false);
        Assert.Equal("hud.calm_protection.inactive", calmCoordinator.ProtectionIndicatorKey);

        var activePursuit = calmCoordinator.RegisterDogEncounter("dog_mid_chase", initialDistanceMetres: 15.0);
        Assert.True(activePursuit.Active);
        Assert.True(calmCoordinator.TryQueueHostileEvent("pending_sabotage_bomb"));
        Assert.Equal(1, calmCoordinator.PendingHostileEventCount);

        calmCoordinator.SetCalmCityEnabled(true);
        Assert.True(calmCoordinator.IsProtectionActive);
        Assert.Equal("hud.calm_protection.active", calmCoordinator.ProtectionIndicatorKey);
        Assert.False(activePursuit.Active);
        Assert.Equal("hazard.dog.suppressed_by_calm", activePursuit.EndReasonKey);
        Assert.False(calmCoordinator.TryQueueHostileEvent("delayed_paint_ambush"));
        Assert.Empty(calmCoordinator.ResolveDueHostileEvents());
    }
}

