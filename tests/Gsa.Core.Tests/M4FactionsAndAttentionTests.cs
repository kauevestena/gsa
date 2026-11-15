using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Calibration;
using Gsa.Core.Sensors.Camera;
using Gsa.Core.Traffic;
using Xunit;

namespace Gsa.Core.Tests;

public sealed class M4FactionsAndAttentionTests
{
    [Fact]
    public void T_F16_01_FiveFictionalFactionsTerritoriesCluesEmblemsAndPlanningMapAreValid()
    {
        var integrity = FactionCatalog.ValidateFictionalIntegrity();
        Assert.True(integrity.Valid, string.Join("; ", integrity.Errors));
        Assert.Equal(5, integrity.FactionCount);
        Assert.True(integrity.TerritoryCount >= 6);
        Assert.False(integrity.PlayerWeaponsAllowed);

        var ids = FactionCatalog.AllFactions.Select(f => f.Id).ToHashSet();
        Assert.Contains(FactionId.SindicatoDoMarcoTorto, ids);
        Assert.Contains(FactionId.CartelDoCimento, ids);
        Assert.Contains(FactionId.MafiaDoMapaVelho, ids);
        Assert.Contains(FactionId.SociedadeDaTerraPlana, ids);
        Assert.Contains(FactionId.MapaFacilSa, ids);

        foreach (var faction in FactionCatalog.AllFactions)
        {
            Assert.True(faction.Emblem.IsStrictlyFictional);
            Assert.False(faction.Emblem.UsesRealWorldSymbols);
            Assert.NotEmpty(faction.NarrativeClueKeys);
            Assert.NotEmpty(faction.PrimaryRegionIds);
        }

        var oldCenterZones = FactionCatalog.GetZonesForPlanningMap(["old_center"]);
        Assert.Contains(oldCenterZones, z => z.ZoneId == "zone_old_center_marco");
        var matched = FactionCatalog.FindZonesContaining("waterfront", 501200, 7400200);
        Assert.Single(matched);
        Assert.Equal(FactionId.SociedadeDaTerraPlana, matched[0].ControllingFaction);
        Assert.True(matched[0].SpoofedGnssOffsetEnuMeters.Length > 5.0);
    }

    [Fact]
    public void T_F16_02_TerritoryAttentionRatesThresholdsCooldownsBombDisarmAndCalmCityWorkDeterministically()
    {
        var tracker = new TerritoryAttentionTracker();
        var calmPolicy = new CalmPolicyCoordinator(calmCityEnabled: false);
        const string zone = "zone_port_cimento";

        // +1 pt/s during active capture -> reaches Level 1 (20) at 20s and Level 2 (40) at 40s
        for (var s = 1; s <= 19; s++)
        {
            var step = tracker.Step(zone, activeCapture: true, playerSpeedMps: 5.0, disarmInputHeld: false, dtSeconds: 1.0, calmPolicy);
            Assert.Equal(TerritoryThreatLevel.None, step.ThreatLevel);
            Assert.False(step.PlayerCanUseWeapons);
        }

        var lvl1 = tracker.Step(zone, activeCapture: true, playerSpeedMps: 5.0, disarmInputHeld: false, dtSeconds: 1.0, calmPolicy);
        Assert.Equal(20.0, lvl1.AttentionScore, 6);
        Assert.Equal(TerritoryThreatLevel.Level1SpotterWarning, lvl1.ThreatLevel);
        Assert.True(lvl1.WarningIssued);
        Assert.False(lvl1.ActionTriggered);

        for (var s = 21; s <= 40; s++)
            tracker.Step(zone, activeCapture: true, playerSpeedMps: 5.0, disarmInputHeld: false, dtSeconds: 1.0, calmPolicy);

        // At Level 2 (40+), requires >= 3 s warning before Paint action triggers, then sets 30 s cooldown
        var w1 = tracker.Step(zone, activeCapture: true, playerSpeedMps: 5.0, disarmInputHeld: false, dtSeconds: 1.0, calmPolicy);
        var w2 = tracker.Step(zone, activeCapture: true, playerSpeedMps: 5.0, disarmInputHeld: false, dtSeconds: 1.0, calmPolicy);
        Assert.False(w1.ActionTriggered);
        Assert.False(w2.ActionTriggered);
        Assert.True(w2.WarningIssued);

        var paintTriggered = tracker.Step(zone, activeCapture: true, playerSpeedMps: 5.0, disarmInputHeld: false, dtSeconds: 1.0, calmPolicy);
        Assert.True(paintTriggered.ActionTriggered);
        Assert.Equal(TerritoryThreatLevel.Level2PaintAttack, paintTriggered.TriggeredActionLevel);
        Assert.True(paintTriggered.WarningLeadTimeSeconds >= TerritoryAttentionTracker.MinimumWarningLeadTimeSeconds);
        Assert.Equal(30.0, paintTriggered.CooldownRemainingSeconds, 6);

        // Apply paint attack cleanly to UrbanHazardEncounterCoordinator
        var rack = new ModularSensorRack(PlatformProfile.Car);
        rack.MountOrMoveSensor("cam_front", RackSlotKind.RoofCamera, "slot_roof_cam_a", 0.0);
        var coordinator = new UrbanHazardEncounterCoordinator(
            calmPolicy,
            new CarSuspensionSimulator(Vector3d.Zero),
            rack,
            new GimbalinaStabilizer(),
            new LensContaminationTracker());

        var vehicleOutcome = TerritoryAttentionTracker.ApplyTriggeredActionToVehicle(paintTriggered, coordinator);
        Assert.NotNull(vehicleOutcome);
        Assert.True(coordinator.Lens.ObstructionFraction > 0.0);

        // Idle capture decay starts ONLY after 10s without active capture (-2 pt/s)
        var beforeIdle = tracker.GetAttention(zone);
        for (var s = 0; s < 9; s++)
            tracker.Step(zone, activeCapture: false, playerSpeedMps: 4.0, disarmInputHeld: false, dtSeconds: 1.0, calmPolicy);
        Assert.Equal(beforeIdle, tracker.GetAttention(zone), 6);

        tracker.Step(zone, activeCapture: false, playerSpeedMps: 4.0, disarmInputHeld: false, dtSeconds: 1.0, calmPolicy);
        Assert.Equal(beforeIdle - 2.0, tracker.GetAttention(zone), 6);

        // Save snapshot preserves threshold & cooldown across reload
        var snapshot = tracker.ExportSnapshot();
        var reloaded = new TerritoryAttentionTracker();
        reloaded.ImportSnapshot(snapshot);
        Assert.Equal(tracker.GetAttention(zone), reloaded.GetAttention(zone), 6);
        Assert.Equal(tracker.GetCooldownRemaining(zone), reloaded.GetCooldownRemaining(zone), 6);

        // Escalate to Level 5 (100), clear territory cooldown while driving, then test 15s stop -> 10s warning -> leave/disarm/Calm
        for (var s = 0; s < 70; s++)
            reloaded.Step(zone, activeCapture: true, playerSpeedMps: 5.0, disarmInputHeld: false, dtSeconds: 1.0, calmPolicy);
        Assert.Equal(100.0, reloaded.GetAttention(zone), 6);

        while (reloaded.GetCooldownRemaining(zone) > 0.0)
            reloaded.Step(zone, activeCapture: true, playerSpeedMps: 5.0, disarmInputHeld: false, dtSeconds: 1.0, calmPolicy);

        TerritoryEscalationOutcome? armedOutcome = null;
        for (var s = 1; s <= 15; s++)
            armedOutcome = reloaded.Step(zone, activeCapture: true, playerSpeedMps: 0.0, disarmInputHeld: false, dtSeconds: 1.0, calmPolicy);
        Assert.NotNull(armedOutcome);
        Assert.True(armedOutcome!.BombWarningActive);
        Assert.Equal(10.0, armedOutcome.BombRemainingCountdownSeconds, 6);

        // Hold stationary disarm for 5 s -> disarms safely without detonation or player weapons
        TerritoryEscalationOutcome? disarmOutcome = null;
        for (var s = 1; s <= 5; s++)
            disarmOutcome = reloaded.Step(zone, activeCapture: true, playerSpeedMps: 0.0, disarmInputHeld: true, dtSeconds: 1.0, calmPolicy);
        Assert.NotNull(disarmOutcome);
        Assert.True(disarmOutcome!.BombDisarmed);
        Assert.False(disarmOutcome.BombDetonated);
        Assert.False(disarmOutcome.BombWarningActive);
        Assert.False(disarmOutcome.PlayerCanUseWeapons);

        // Wait out 30s cooldown while driving, arm again, and verify Calm City cancels active bomb warning
        while (reloaded.GetCooldownRemaining(zone) > 0.0)
            reloaded.Step(zone, activeCapture: true, playerSpeedMps: 5.0, disarmInputHeld: false, dtSeconds: 1.0, calmPolicy);
        for (var s = 1; s <= 15; s++)
            armedOutcome = reloaded.Step(zone, activeCapture: true, playerSpeedMps: 0.0, disarmInputHeld: false, dtSeconds: 1.0, calmPolicy);
        Assert.True(armedOutcome!.BombWarningActive);

        var calmCancelled = reloaded.Step(zone, activeCapture: true, playerSpeedMps: 0.0, disarmInputHeld: false, dtSeconds: 1.0, new CalmPolicyCoordinator(calmCityEnabled: true));
        Assert.True(calmCancelled.SuppressedByCalmCity);
        Assert.False(calmCancelled.BombWarningActive);
        Assert.False(calmCancelled.ActionTriggered);
    }
}
