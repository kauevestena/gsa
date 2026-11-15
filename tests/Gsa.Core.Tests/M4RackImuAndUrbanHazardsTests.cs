using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Calibration;
using Gsa.Core.Sensors.Camera;
using Gsa.Core.Sensors.Fusion;
using Gsa.Core.Sensors.Gnss;
using Gsa.Core.Traffic;
using Xunit;

namespace Gsa.Core.Tests;

public sealed class M4RackImuAndUrbanHazardsTests
{
    [Fact]
    public void T_F13_01_ModularRackEnforcesSlotCompatibilityCapacityImmutabilityAndRelocationDrift()
    {
        var scooter = PlatformProfile.Scooter; // Level 2: no LiDAR, no workshop, no dual antenna
        var rack = new ModularSensorRack(scooter, maxPowerBudgetWatts: 200.0, maxPayloadWeightGrams: 10000);

        Assert.False(ModularSensorRack.IsWorkshopAccessible(scooter.Level));
        Assert.True(ModularSensorRack.IsWorkshopAccessible(PlatformProfile.Car.Level));

        var wrongKind = rack.MountOrMoveSensor("cam_front", RackSlotKind.Panoramic360, "slot_roof_cam_a", 1.0);
        Assert.False(wrongKind.Succeeded);
        Assert.Equal("rack.error.slot_kind_incompatible", wrongKind.ReasonKey);

        var lidarDenied = rack.MountOrMoveSensor("lidar_1", RackSlotKind.LidarProfilerOrScanner, "slot_lidar_a", 1.0, powerWatts: 50.0, weightGrams: 2000);
        Assert.False(lidarDenied.Succeeded);
        Assert.Equal("rack.error.lidar_requires_level_4", lidarDenied.ReasonKey);

        // Mount camera in slot A (nominal X=+0.25) at t=10
        var mountA = rack.MountOrMoveSensor("cam_main", RackSlotKind.RoofCamera, "slot_roof_cam_a", 10.0, powerWatts: 20.0, weightGrams: 1000);
        Assert.True(mountA.Succeeded);
        var rev1 = mountA.NewRevision;
        Assert.False(mountA.SlotState!.EstimatedCalibrationInvalidated);

        // Relocate cam_main from slot A (X=+0.25) to slot B (X=-0.35) at t=25 -> physical changes, stale estimate retained!
        var moveB = rack.MountOrMoveSensor("cam_main", RackSlotKind.RoofCamera, "slot_roof_cam_b", 25.0, powerWatts: 20.0, weightGrams: 1000);
        Assert.True(moveB.Succeeded);
        Assert.True(moveB.SlotState!.EstimatedCalibrationInvalidated);
        Assert.True(moveB.SlotState.NeedsWorkshopInspection);
        Assert.Equal(-0.35, moveB.SlotState.PhysicalTruthMount.LeverArmBody.X, 6);
        Assert.Equal(0.25, moveB.SlotState.EstimatedCalibrationMount.LeverArmBody.X, 6);

        var discrepancy = rack.ComputeObservableDiscrepancy("slot_roof_cam_b", targetRangeMetres: 20.0, platformSpeedMetresPerSecond: 10.0);
        Assert.Equal(0.60, discrepancy.LeverArmErrorMetres, 6);

        // Immutable historical query keeps revision at t=12 intact despite later move at t=25
        var historicalAt12 = rack.GetMountAtTimestamp("slot_roof_cam_a", 12.0);
        Assert.Equal(rev1, historicalAt12.Revision);
        Assert.Equal(0.25, historicalAt12.PhysicalTruthMount.LeverArmBody.X, 6);

        // Kombi unlocks Level 4 LiDAR slot and rejects over-budget power
        var kombiRack = new ModularSensorRack(PlatformProfile.Kombi, maxPowerBudgetWatts: 100.0, maxPayloadWeightGrams: 15000);
        var lidarOk = kombiRack.MountOrMoveSensor("lidar_l4", RackSlotKind.LidarProfilerOrScanner, "slot_lidar_a", 5.0, powerWatts: 85.0, weightGrams: 3500);
        Assert.True(lidarOk.Succeeded);

        var powerExceeded = kombiRack.MountOrMoveSensor("cam_front", RackSlotKind.RoofCamera, "slot_roof_cam_a", 6.0, powerWatts: 20.0, weightGrams: 800);
        Assert.False(powerExceeded.Succeeded);
        Assert.Equal("rack.error.power_capacity_exceeded", powerExceeded.ReasonKey);
    }

    [Fact]
    public void T_F13_02_HighDeltaVCollisionDeterministicallyPerturbsRackWithinBoundsWithoutLeakingTruth()
    {
        var rack = new ModularSensorRack(PlatformProfile.Car);
        rack.MountOrMoveSensor("cam_front", RackSlotKind.RoofCamera, "slot_roof_cam_a", 1.0);
        rack.MountOrMoveSensor("imu_1", RackSlotKind.ImuUnit, "slot_imu_core", 1.0, powerWatts: 10.0, weightGrams: 500);

        var sim = new CarSuspensionSimulator(Vector3d.Zero);

        // Sub-threshold impact (< 3.0 m/s) leaves rack calibrated and undisturbed
        var minorImpact = sim.ApplyCollisionImpact(2.4, "minor_curb", defensiveDrivingRank: 0);
        var minorSymptom = ImpactMisalignmentIntegrator.ApplyImpactToRack(rack, minorImpact, seed: 42UL);
        Assert.False(minorSymptom.InspectionRequired);
        Assert.Empty(minorSymptom.AffectedSlotIds);
        Assert.Equal(0.0, rack.ComputeObservableDiscrepancy("slot_roof_cam_a", 50.0, 10.0).CombinedDiscrepancyMetres, 9);

        // Defensive driving rank 1 raises threshold to 3.3 m/s -> 3.15 m/s avoided
        var avoidedBySkill = sim.ApplyCollisionImpact(3.15, "moderate_curb", defensiveDrivingRank: 1);
        Assert.False(avoidedBySkill.TriggersRackMisalignment);

        // Severe impact (>= 3.0 m/s without skill) deterministically perturbs rack within ±0.03 m, ±0.5 deg, ±5 ms
        var beforePair = rack.MountedSlots["slot_roof_cam_a"];
        var severeImpact = sim.ApplyCollisionImpact(3.8, "barrier_impact", defensiveDrivingRank: 0);
        var severeSymptom = ImpactMisalignmentIntegrator.ApplyImpactToRack(rack, severeImpact, seed: 98765UL);

        Assert.True(severeSymptom.InspectionRequired);
        Assert.Equal(["slot_imu_core", "slot_roof_cam_a"], severeSymptom.AffectedSlotIds);
        Assert.True(rack.MountedSlots["slot_roof_cam_a"].NeedsWorkshopInspection);
        Assert.True(rack.MountedSlots["slot_roof_cam_a"].EstimatedCalibrationInvalidated);

        // Symptom carries no hidden delta numbers (public record only has EventId, Timestamp, InspectionRequired, AffectedSlotIds, Key)
        var afterPair = rack.MountedSlots["slot_roof_cam_a"];
        var leverDiff = (afterPair.PhysicalTruthMount.LeverArmBody - beforePair.PhysicalTruthMount.LeverArmBody).Length;
        Assert.True(leverDiff > 0.0);
        Assert.True(Math.Abs(afterPair.PhysicalTruthMount.LeverArmBody.X - beforePair.PhysicalTruthMount.LeverArmBody.X) <= 0.03 + 1e-12);
        Assert.True(Math.Abs(afterPair.PhysicalTruthMount.LeverArmBody.Y - beforePair.PhysicalTruthMount.LeverArmBody.Y) <= 0.03 + 1e-12);
        Assert.True(Math.Abs(afterPair.PhysicalTruthMount.LeverArmBody.Z - beforePair.PhysicalTruthMount.LeverArmBody.Z) <= 0.03 + 1e-12);
        Assert.True(Math.Abs(afterPair.PhysicalTruthMount.TimeOffsetSeconds - beforePair.PhysicalTruthMount.TimeOffsetSeconds) <= 0.005 + 1e-12);

        // Replay reproducibility check with same seed
        var replayRack = new ModularSensorRack(PlatformProfile.Car);
        replayRack.MountOrMoveSensor("cam_front", RackSlotKind.RoofCamera, "slot_roof_cam_a", 1.0);
        replayRack.MountOrMoveSensor("imu_1", RackSlotKind.ImuUnit, "slot_imu_core", 1.0, powerWatts: 10.0, weightGrams: 500);
        ImpactMisalignmentIntegrator.ApplyImpactToRack(replayRack, severeImpact, seed: 98765UL);
        Assert.Equal(
            afterPair.PhysicalTruthMount.LeverArmBody,
            replayRack.MountedSlots["slot_roof_cam_a"].PhysicalTruthMount.LeverArmBody);
        Assert.Equal(
            afterPair.PhysicalTruthMount.SensorToBody,
            replayRack.MountedSlots["slot_roof_cam_a"].PhysicalTruthMount.SensorToBody);
    }

    [Fact]
    public void T_F14_01_ImuGradesIntegrationGravityLevelingHeadingFusionAndNavVsMemsDrift()
    {
        // 1. Ideal analytical rotation: 90 deg around Z over 1.0 s at 100 Hz
        const double dt = 0.01;
        var idealSim = new ImuSimulator(ImuProfile.Ideal, 1UL, 2UL);
        var idealEst = new ComplementaryAttitudeEstimator(Quaterniond.Identity);
        for (var i = 1; i <= 100; i++)
        {
            var obs = idealSim.Observe(i * dt, dt, new Vector3d(0.0, 0.0, ImuProfile.GravityMetresPerSecondSquared), new Vector3d(0.0, 0.0, Math.PI / 2.0));
            idealEst.Step(obs, isStationary: false, headingObservation: null);
        }

        var rotatedForward = idealEst.EstimatedBodyToMap.Rotate(Vector3d.UnitX);
        Assert.InRange(rotatedForward.X, -1e-6, 1e-6);
        Assert.InRange(rotatedForward.Y, 1.0 - 1e-6, 1.0 + 1e-6);

        // 2. Stationary gravity leveling recovers an initial 10 deg roll tilt without changing yaw when stationary & |f|~g
        var tiltedInit = Quaterniond.FromAxisAngle(Vector3d.UnitX, 10.0 * Math.PI / 180.0);
        var gravEst = new ComplementaryAttitudeEstimator(tiltedInit);
        var accelWhileAccelerating = idealSim.Observe(1.01, dt, new Vector3d(3.5, 0.0, ImuProfile.GravityMetresPerSecondSquared), Vector3d.Zero);
        var skippedGrav = gravEst.Step(accelWhileAccelerating, isStationary: true);
        Assert.False(skippedGrav.GravityCorrectionApplied); // |f| differs from g by > 0.1 m/s^2

        for (var i = 1; i <= 600; i++) // 6s stationary at tau=2s -> exp(-3) ~ 0.05 of error remaining
        {
            var obs = idealSim.Observe(1.01 + i * dt, dt, new Vector3d(0.0, 0.0, ImuProfile.GravityMetresPerSecondSquared), Vector3d.Zero);
            var step = gravEst.Step(obs, isStationary: true);
            Assert.True(step.GravityCorrectionApplied);
        }
        var (rollAfter, pitchAfter, _) = ComplementaryAttitudeEstimator.ExtractRollPitchYaw(gravEst.EstimatedBodyToMap);
        Assert.True(Math.Abs(rollAfter) < 0.02);
        Assert.True(Math.Abs(pitchAfter) < 0.02);

        // 3. Heading complementary correction when valid HeadingObservation provided, skipped when unavailable
        var headingSim = new HeadingSimulator(55UL);
        var unreliableHeading = headingSim.Observe(0.1, 0.5, HeadingProfile.Level2, reliable: false);
        var yawEst = new ComplementaryAttitudeEstimator(Quaterniond.Identity);
        var obsStill = idealSim.Observe(10.0, dt, new Vector3d(0.0, 0.0, ImuProfile.GravityMetresPerSecondSquared), Vector3d.Zero);
        var noYaw = yawEst.Step(obsStill, isStationary: true, unreliableHeading);
        Assert.False(noYaw.YawCorrectionApplied);

        var targetHeading = 0.75; // rad
        for (var i = 1; i <= 500; i++)
        {
            var hObs = headingSim.Observe(10.0 + i * dt, targetHeading, new HeadingProfile("exact", 0.0), reliable: true);
            var obs = idealSim.Observe(10.0 + i * dt, dt, new Vector3d(0.0, 0.0, ImuProfile.GravityMetresPerSecondSquared), Vector3d.Zero);
            yawEst.Step(obs, isStationary: true, hObs);
        }
        var (_, _, fusedYaw) = ComplementaryAttitudeEstimator.ExtractRollPitchYaw(yawEst.EstimatedBodyToMap);
        Assert.InRange(Math.Abs(fusedYaw - targetHeading), 0.0, 0.07);

        // 4. Deterministic state save/restore AND MEMS vs Navigation drift statistics across 30 s outage
        var mems = new ImuSimulator(ImuProfile.Mems, 1001UL, 2002UL);
        var nav = new ImuSimulator(ImuProfile.Navigation, 1001UL, 2002UL);
        var savedMems = mems.ExportState();
        var restoredMems = new ImuSimulator(ImuProfile.Mems, savedMems);

        var memsEst = new ComplementaryAttitudeEstimator(Quaterniond.Identity);
        var navEst = new ComplementaryAttitudeEstimator(Quaterniond.Identity);
        for (var i = 1; i <= 3000; i++)
        {
            var t = i * dt;
            var mObs = mems.Observe(t, dt, new Vector3d(0.0, 0.0, ImuProfile.GravityMetresPerSecondSquared), Vector3d.Zero);
            var nObs = nav.Observe(t, dt, new Vector3d(0.0, 0.0, ImuProfile.GravityMetresPerSecondSquared), Vector3d.Zero);
            if (i <= 5)
            {
                var rObs = restoredMems.Observe(t, dt, new Vector3d(0.0, 0.0, ImuProfile.GravityMetresPerSecondSquared), Vector3d.Zero);
                Assert.Equal(mObs.AngularVelocityBody, rObs.AngularVelocityBody);
                Assert.Equal(mObs.SpecificForceBody, rObs.SpecificForceBody);
            }
            memsEst.Step(mObs, isStationary: false);
            navEst.Step(nObs, isStationary: false);
        }

        var memsYawErr = Math.Abs(ComplementaryAttitudeEstimator.ExtractRollPitchYaw(memsEst.EstimatedBodyToMap).YawRadians);
        var navYawErr = Math.Abs(ComplementaryAttitudeEstimator.ExtractRollPitchYaw(navEst.EstimatedBodyToMap).YawRadians);
        Assert.True(navYawErr < memsYawErr);

        // Verify ComplementaryAttitudeEstimator has no TruthSample parameter in public API
        var stepParams = typeof(ComplementaryAttitudeEstimator).GetMethod(nameof(ComplementaryAttitudeEstimator.Step))!.GetParameters();
        Assert.DoesNotContain(stepParams, p => p.ParameterType.Name.Contains("Truth", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void T_F09_05_UrbanHazardTableExecutesAllMitigationsAndCalmCitySuppression()
    {
        var calmPolicy = new CalmPolicyCoordinator(calmCityEnabled: false);
        var vehicle = new CarSuspensionSimulator(Vector3d.Zero);
        var rack = new ModularSensorRack(PlatformProfile.Car);
        rack.MountOrMoveSensor("cam_front", RackSlotKind.RoofCamera, "slot_roof_cam_a", 0.0);
        var gimbalina = new GimbalinaStabilizer();
        var lens = new LensContaminationTracker();

        var coordinator = new UrbanHazardEncounterCoordinator(calmPolicy, vehicle, rack, gimbalina, lens);

        // 1. Horn (+10) & Pothole (+15, rack misaligned at dv=3.6 m/s)
        var horn = coordinator.TriggerHazard("hz_horn_1", UrbanHazardKind.HornStartle);
        Assert.False(horn.SuppressedByCalmPolicy);
        Assert.Equal(10.0, horn.GimbalinaStressAfter, 6);

        var pothole = coordinator.TriggerHazard("hz_pothole_1", UrbanHazardKind.PotholeImpact, impactSeed: 77UL, deltaVMetresPerSecond: 3.6);
        Assert.Equal(25.0, pothole.GimbalinaStressAfter, 6);
        Assert.True(pothole.RackInspectionRequired);
        Assert.True(pothole.ImpactEvent!.TriggersRackMisalignment);

        // 2. Dog chase (+20 -> 45) & treat pacification
        var dog = coordinator.TriggerHazard("hz_dog_1", UrbanHazardKind.DogChase, initialDogDistanceMetres: 15.0);
        Assert.True(dog.DogState!.Active);
        Assert.Equal(45.0, dog.GimbalinaStressAfter, 6);
        var pacified = coordinator.PacifyDogWithTreat("hz_dog_1");
        Assert.False(pacified.Active);
        Assert.True(pacified.PacifiedByTreat);

        // 3. Fireworks (+25 -> 70) then feed corn (-30 -> 40, cooldown 10s) and rest (-5/s)
        var fireworks = coordinator.TriggerHazard("hz_fw_1", UrbanHazardKind.FireworksOrBomb);
        Assert.Equal(70.0, fireworks.GimbalinaStressAfter, 6);
        Assert.True(coordinator.FeedGimbalinaCorn());
        Assert.Equal(40.0, gimbalina.Stress, 6);
        Assert.False(coordinator.FeedGimbalinaCorn()); // cooldown active
        var restStep = coordinator.StepRest(4.0, isResting: true);
        Assert.Equal(20.0, restStep.Stress, 6);

        // 4. Sabotage paint splatter cleared only after >= 3s stopped (< 0.1 m/s)
        var splatter = coordinator.TriggerHazard("hz_paint_1", UrbanHazardKind.SabotageVandalism, splatterObstruction: 0.5);
        Assert.Equal(0.5, splatter.LensObstructionAfter, 6);
        Assert.False(coordinator.StepStoppedLensCleaning(5.0, platformSpeedMetresPerSecond: 2.0));
        Assert.Equal(0.5, lens.ObstructionFraction, 6);
        Assert.True(coordinator.StepStoppedLensCleaning(3.1, platformSpeedMetresPerSecond: 0.0));
        Assert.Equal(0.0, lens.ObstructionFraction, 6);

        // 5. Flanelinha: paying 10 Coords avoids cosmetic damage; refusing adds 5 cosmetic damage
        var flanelinhaPaid = coordinator.TriggerHazard("hz_flan_paid", UrbanHazardKind.FlanelinhaApproach, payFlanelinhaTip: true);
        Assert.Equal(10, flanelinhaPaid.TipPaidCoords);
        Assert.Equal(0, flanelinhaPaid.CosmeticDamageAdded);

        var flanelinhaRefused = coordinator.TriggerHazard("hz_flan_ref", UrbanHazardKind.FlanelinhaApproach, payFlanelinhaTip: false);
        Assert.Equal(5, flanelinhaRefused.CosmeticDamageAdded);
        Assert.Equal(5, coordinator.TotalCosmeticDamagePoints);

        // 6. Calm City active suppresses all hostile hazards while keeping non-hostile challenges
        calmPolicy.SetCalmCityEnabled(true);
        Assert.True(coordinator.TriggerHazard("hz_calm_coll", UrbanHazardKind.ErraticVehicleCollision, deltaVMetresPerSecond: 6.0).SuppressedByCalmPolicy);
        Assert.True(coordinator.TriggerHazard("hz_calm_dog", UrbanHazardKind.DogChase).SuppressedByCalmPolicy);
        Assert.True(coordinator.TriggerHazard("hz_calm_fw", UrbanHazardKind.FireworksOrBomb).SuppressedByCalmPolicy);
        Assert.True(coordinator.TriggerHazard("hz_calm_paint", UrbanHazardKind.SabotageVandalism).SuppressedByCalmPolicy);
        var calmFlan = coordinator.TriggerHazard("hz_calm_flan", UrbanHazardKind.FlanelinhaApproach, payFlanelinhaTip: false);
        Assert.True(calmFlan.SuppressedByCalmPolicy);
        Assert.Equal(0, calmFlan.CosmeticDamageAdded);

        // Non-hostile road mud splatter & horn still apply normally in Calm City
        var calmMud = coordinator.TriggerHazard("hz_calm_mud", UrbanHazardKind.RoadMudSplatter, splatterObstruction: 0.30);
        Assert.False(calmMud.SuppressedByCalmPolicy);
        Assert.Equal(0.30, calmMud.LensObstructionAfter, 6);
    }
}
