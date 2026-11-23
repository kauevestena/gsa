using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Camera;
using Gsa.Core.Sensors.Gnss;
using Gsa.Core.World;
using Xunit;

namespace Gsa.Core.Tests;

public sealed class M4WorldCar360AndRtkTests
{
    private static WorldSceneManifest LoadTutorialManifest()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "game", "world", "tutorial-block.json")))
            dir = dir.Parent;
        var path = Path.Combine(dir!.FullName, "game", "world", "tutorial-block.json");
        return WorldSceneManifest.Parse(File.ReadAllText(path));
    }

    [Fact]
    public void T_F02_04_ExpandedCityManifestPreservesMvpIdsCoversThreeSquareKmAndGatesPortalsByLevel()
    {
        var tutorial = LoadTutorialManifest();
        var mvp = OldCenterMvpWorldAssembler.BuildOldCenterMvpManifest(tutorial);
        var expanded = RegionalSectorCatalog.BuildExpandedCityManifest(tutorial);

        // 1 km^2 each in Old Center, Port, and Waterfront -> 300 100x100 m sectors
        Assert.Equal(300, expanded.Cells.Count);
        Assert.Equal(1.0, RegionalSectorCatalog.OldCenter.AreaSquareKilometres, 6);
        Assert.Equal(1.0, RegionalSectorCatalog.Port.AreaSquareKilometres, 6);
        Assert.Equal(1.0, RegionalSectorCatalog.Waterfront.AreaSquareKilometres, 6);

        // Continuous ring road corridor loop connects districts back to start
        Assert.Equal(
            RegionalSectorCatalog.RingRoadCorridorLoopEnu[0],
            RegionalSectorCatalog.RingRoadCorridorLoopEnu[^1]);

        // Zero ID/geometry drift on every MVP object
        var preservation = RegionalSectorCatalog.VerifyMvpTargetIdsPreserved(mvp, expanded);
        Assert.True(preservation.Preserved);
        Assert.Empty(preservation.MissingOrChangedObjectIds);
        Assert.Equal(mvp.Objects.Count, preservation.MvpObjectCount);

        // Regional objects present
        Assert.Contains(expanded.Objects, o => o.Id == "ring_sign_01");
        Assert.Contains(expanded.Objects, o => o.Id == "port_warehouse_01");
        Assert.Contains(expanded.Objects, o => o.Id == "port_tunnel_mouth_01");
        Assert.Contains(expanded.Objects, o => o.Id == "waterfront_glass_tower_01");

        // Portal access gates by campaign & platform level
        var hist03Only = new HashSet<string>(["HIST-03"], StringComparer.Ordinal);
        var hist04Set = new HashSet<string>(["HIST-03", "HIST-04"], StringComparer.Ordinal);

        Assert.False(RegionalAccessGateEvaluator.EvaluatePortalAccess("portal_center_to_port", new HashSet<string>(), 2).Allowed);
        Assert.True(RegionalAccessGateEvaluator.EvaluatePortalAccess("portal_center_to_port", hist03Only, 2).Allowed);

        var carLockedByCampaign = RegionalAccessGateEvaluator.EvaluatePortalAccess("portal_center_to_waterfront", hist03Only, 3);
        Assert.False(carLockedByCampaign.Allowed);
        Assert.Equal("world.portal.locked_by_campaign", carLockedByCampaign.ReasonKey);

        var bicycleDenied = RegionalAccessGateEvaluator.EvaluatePortalAccess("portal_center_to_waterfront", hist04Set, 1);
        Assert.False(bicycleDenied.Allowed);
        Assert.Equal("world.portal.platform_level_insufficient", bicycleDenied.ReasonKey);

        Assert.True(RegionalAccessGateEvaluator.EvaluatePortalAccess("portal_center_to_waterfront", hist04Set, 3).Allowed);

        // Port tunnel test override opens for Level 4 Kombi with active contract before HIST-05
        var tunnelOverride = RegionalAccessGateEvaluator.EvaluatePortalAccess(
            "portal_port_tunnel_mouth",
            hist04Set,
            ownedPlatformLevel: 4,
            hasActivePortTunnelTestContract: true);
        Assert.True(tunnelOverride.Allowed);
        Assert.Equal("world.portal.tunnel_test_open", tunnelOverride.ReasonKey);
    }

    [Fact]
    public void T_F03_05_AutomotivePlatformsSuspensionBrakingCorneringBumpsAndRackImpactsBehaveRealistically()
    {
        Assert.Equal(3, PlatformProfile.Car.Level);
        Assert.Equal(18.0, PlatformProfile.Car.MaxSpeedMetresPerSecond, 6);
        Assert.Equal(1600, PlatformProfile.Car.VirtualStorageCapacityUnits);
        Assert.Equal(3600.0, PlatformProfile.Car.PhysicalAutonomySeconds, 6);
        Assert.Equal(4500, PlatformProfile.Car.PriceCoords);

        // Car is forbidden from cycleways and sidewalks just like scooter+
        var sim = new LocomotionSimulator(new Vector3d(500100.0, 7400100.0, 100.0), initialMode: LocomotionMode.Car);
        var corridorCtrl = new MvpVehicleMobilityController(sim);
        Assert.False(corridorCtrl.EvaluateCorridorAccess(new CorridorAccessSpec("cycleway_1", AccessCorridorKind.NarrowAlleyOrCycleway, 1.5)).Allowed);
        Assert.False(corridorCtrl.EvaluateCorridorAccess(new CorridorAccessSpec("sidewalk_1", AccessCorridorKind.SidewalkPromenade, 3.0)).Allowed);
        Assert.True(corridorCtrl.EvaluateCorridorAccess(new CorridorAccessSpec("ring_road_1", AccessCorridorKind.UrbanRoadLane, 3.5)).Allowed);

        // Four-wheel raycast suspension + braking + cornering + bumps stay bounded & non-NaN
        var car = new CarSuspensionSimulator(new Vector3d(500600.0, 7400200.0, 100.0), 0.0, LocomotionMode.Car);
        var corner = car.StepCornering(1.5, steerInput: 0.8, throttleInput: 0.7);
        Assert.Equal(4, corner.Wheels.Count);
        Assert.All(corner.Wheels, w =>
        {
            Assert.True(double.IsFinite(w.CompressionMetres));
            Assert.True(double.IsFinite(w.NormalForceNewtons));
            Assert.True(w.InContact);
        });
        Assert.InRange(Math.Abs(corner.RollRadians), 0.0, 0.25);

        var brake = car.StepBraking(2.5, initialSpeedMetresPerSecond: 16.0);
        Assert.InRange(brake.SpeedMetresPerSecond, 0.0, 16.0);
        Assert.InRange(Math.Abs(brake.PitchRadians), 0.0, 0.25);

        var bump = car.StepOverSpeedBump(1.0, bumpHeightMetres: 0.06);
        Assert.InRange(Math.Abs(bump.PitchRadians), 0.0, 0.25);

        // Delta-v impact below 3.0 m/s does not trigger rack misalignment; >= 3.0 m/s does, and DefensiveDriving raises threshold
        var gentle = car.ApplyCollisionImpact(deltaVMetresPerSecond: 2.0, impactSource: "curb_scrape", defensiveDrivingRank: 0);
        Assert.False(gentle.TriggersRackMisalignment);

        var hard = car.ApplyCollisionImpact(deltaVMetresPerSecond: 3.4, impactSource: "pole_tap", defensiveDrivingRank: 0);
        Assert.True(hard.TriggersRackMisalignment);

        // Rank 3 defensive driving raises 3.0 * 1.3 = 3.9 m/s threshold -> 3.4 m/s no longer misaligns rack
        var bufferedBySkill = car.ApplyCollisionImpact(deltaVMetresPerSecond: 3.4, impactSource: "pole_tap_skilled", defensiveDrivingRank: 3);
        Assert.Equal(3.9, bufferedBySkill.EffectiveThresholdMetresPerSecond, 6);
        Assert.False(bufferedBySkill.TriggersRackMisalignment);
        Assert.Equal(3, car.ImpactLog.Count);
    }

    [Fact]
    public void T_F04_06_SixFacePanoramic360RigDeduplicatesSeamsExcludesOccludedTargetsAndIntegratesWithFleetRigs()
    {
        var carAgenda = PlatformCameraRigBuilder.CreateAgendaForPlatform(LocomotionMode.Car, 0.0);
        var vanAgenda = PlatformCameraRigBuilder.CreateAgendaForPlatform(LocomotionMode.Van, 0.0);
        var carDue = carAgenda.PollDueCaptures(0.0);
        var vanDue = vanAgenda.PollDueCaptures(0.0);

        Assert.Equal(6, carDue.Count); // 6 panoramic faces
        Assert.Equal(7, vanDue.Count); // 6 panoramic faces + pavement downward cam
        Assert.All(carDue, s => Assert.Equal(512, s.Profile.Intrinsics.Width));
        Assert.Contains(vanDue, s => s.SensorId == "cam_360_front" && s.Profile.Intrinsics.Width == 1024);

        var headPose = new EstimatedBodyPose(10.0, new Vector3d(500650.0, 7400400.0, 102.0), Quaterniond.Identity);
        var visible = new Dictionary<PanoramicFace, IReadOnlyList<string>>
        {
            [PanoramicFace.Front] = ["sign_01", "seam_facade_A"],
            [PanoramicFace.Right] = ["seam_facade_A", "hydrant_01"],
            [PanoramicFace.Back] = ["bus_stop_01", "occluded_sign_behind_bus"],
            [PanoramicFace.Left] = ["tree_01"],
            [PanoramicFace.Up] = [],
            [PanoramicFace.Down] = [],
        };
        var occluded = new Dictionary<PanoramicFace, IReadOnlyList<string>>
        {
            [PanoramicFace.Back] = ["occluded_sign_behind_bus"],
        };

        var sweep = Panoramic360RigSimulator.CaptureSixFaceSweep(
            sweepId: "sweep_01",
            timestampSeconds: 10.0,
            headPose: headPose,
            mode: LocomotionMode.Car,
            visibleFeaturesByFace: visible,
            occludedFeaturesByFace: occluded,
            totalRequiredTargetCount: 5);

        Assert.Equal(512, sweep.FaceResolutionPx);
        Assert.Equal(6, sweep.Faces.Count);
        Assert.True(sweep.FalseSeamCoveragePrevented);
        Assert.True(sweep.FalseOcclusionCoveragePrevented);

        // Unique valid features = {bus_stop_01, hydrant_01, seam_facade_A, sign_01, tree_01} => 5/5 (never inflated to 6 by seam or corrupted by occluded)
        Assert.Equal(5, sweep.DeduplicatedVisibleFeatureIds.Count);
        Assert.DoesNotContain("occluded_sign_behind_bus", sweep.DeduplicatedVisibleFeatureIds);
        Assert.Equal(1.0, sweep.EffectiveCoverageFraction, 6);
    }

    [Fact]
    public void T_F05_04_BaseStationSetupPreservesPastLogsAndRtkEnforcesThresholdsBaselineAndSnapshotRoundTrip()
    {
        var markPos = new Vector3d(500620.0, 7401200.0, 100.8);
        var markSpec = new SurveyMarkBaseStationSpec("survey_control_port_viaduct", "Port Viaduct Mark", markPos, 0.0, 3600.0, 0.8);
        var installer = new BaseStationInstaller();

        // Must be on foot and occupy mark for 5.0 s
        var mountedAttempt = installer.AdvanceInstallation(LocomotionMode.Car, markPos, markSpec, 5.0, 5.0, markOpenness: 0.9);
        Assert.False(mountedAttempt.Succeeded);
        Assert.Equal("gnss.base.must_be_on_foot", mountedAttempt.ReasonKey);

        var partial = installer.AdvanceInstallation(LocomotionMode.OnFoot, markPos, markSpec, 3.0, 3.0, markOpenness: 0.9);
        Assert.True(partial.Succeeded);
        Assert.False(partial.Installed);

        var complete = installer.AdvanceInstallation(LocomotionMode.OnFoot, markPos, markSpec, 2.0, 5.0, markOpenness: 0.9, initialBatterySeconds: 20.0);
        Assert.True(complete.Installed);
        Assert.NotNull(complete.BaseState);

        var baseState = complete.BaseState!;
        Assert.Equal(0.15, GnssProfile.RtkFloat.BaseSigmaMetres, 6);
        Assert.Equal(0.03, GnssProfile.RtkFix.BaseSigmaMetres, 6);
        Assert.Equal(GnssProfile.RtkFloat, GnssProfileSelector.SelectGnssProfile(PlatformProfile.Car, hasRtkUpgrade: true));

        var rtk = new RtkSolutionStateMachine(seed: 1337);
        var commonErr = new Vector3d(1.8, -1.2, 0.4);
        var roverNear = markPos + new Vector3d(400.0, 0.0, 0.0);

        // Openness < 0.4 falls back to Autonomous (never claims float/fix)
        var blocked = rtk.StepEpoch(10.0, 1.0, roverNear, commonErr, openness: 0.35, platformLevel: 3, hasMultiFrequencyProfile: true, activeBase: baseState);
        Assert.Equal(GnssSolutionKind.Autonomous, blocked.Solution);

        // Openness 0.55 enters RTK Float and cancels common error
        baseState.RecordEpochCorrection(11.0, commonErr, 0.9, dtSeconds: 1.0);
        var floatObs = rtk.StepEpoch(11.0, 1.0, roverNear, commonErr, openness: 0.55, platformLevel: 3, hasMultiFrequencyProfile: true, activeBase: baseState);
        Assert.Equal(GnssSolutionKind.RtkFloat, floatObs.Solution);
        Assert.True((floatObs.MeasuredPositionEnu!.Value - roverNear).Length < 0.8);

        // Openness >= 0.7 stabilizes after 10 s into RTK Fix (sigma 0.03 m)
        GnssObservation? fixObs = null;
        for (var i = 0; i < 10; i++)
        {
            var t = 12.0 + i;
            baseState.RecordEpochCorrection(t, commonErr, 0.9, dtSeconds: 1.0);
            fixObs = rtk.StepEpoch(t, 1.0, roverNear, commonErr, openness: 0.85, platformLevel: 3, hasMultiFrequencyProfile: true, activeBase: baseState);
        }
        Assert.NotNull(fixObs);
        Assert.Equal(GnssSolutionKind.RtkFix, fixObs!.Solution);
        Assert.True((fixObs.MeasuredPositionEnu!.Value - roverNear).Length < 0.25);

        // Beyond 2 km max RTK baseline falls back to Autonomous even at high openness
        var roverFar = markPos + new Vector3d(2500.0, 0.0, 0.0);
        var outOfRange = rtk.StepEpoch(25.0, 1.0, roverFar, commonErr, openness: 0.95, platformLevel: 3, hasMultiFrequencyProfile: true, activeBase: baseState);
        Assert.Equal(GnssSolutionKind.Autonomous, outOfRange.Solution);

        // Battery exhaustion or base removal stops live RTK while preserving previously recorded base logs and correctable log for PPK
        baseState.RecordEpochCorrection(26.0, commonErr, 0.9, dtSeconds: 10.0); // drains remaining 9s -> inactive
        Assert.False(baseState.IsActive);
        Assert.Equal(11, baseState.ExportLog().Samples.Count);

        var afterLoss = rtk.StepEpoch(27.0, 1.0, roverNear, commonErr, openness: 0.95, platformLevel: 3, hasMultiFrequencyProfile: true, activeBase: baseState);
        Assert.Equal(GnssSolutionKind.Autonomous, afterLoss.Solution);

        // Snapshot round-trip preserves solution, stabilization, RNG determinism, and PPK correctable records
        var snap = rtk.ExportSnapshot();
        var restored = RtkSolutionStateMachine.FromSnapshot(snap);
        Assert.Equal(rtk.CorrectableLog.Count, restored.CorrectableLog.Count);
        var nextOrig = rtk.StepEpoch(28.0, 1.0, roverNear, commonErr, openness: 0.9, platformLevel: 3, hasMultiFrequencyProfile: true, activeBase: null);
        var nextRest = restored.StepEpoch(28.0, 1.0, roverNear, commonErr, openness: 0.9, platformLevel: 3, hasMultiFrequencyProfile: true, activeBase: null);
        Assert.Equal(nextOrig.MeasuredPositionEnu!.Value, nextRest.MeasuredPositionEnu!.Value);
    }

    [Fact]
    public void CarSuspensionSupportsReverseGearAndWallSlideDamping()
    {
        var car = new CarSuspensionSimulator(new Vector3d(500208.0, 7399992.5, 100.0), 0.0, LocomotionMode.Car);
        for (var i = 0; i < 60; i++)
            car.StepMotion(1.0 / 60.0, throttleInput: -1.0, brakeInput: 0.0, steerInput: 0.6);

        Assert.True(car.SpeedMetresPerSecond < -1.0, $"Expected reverse speed < -1 m/s, got {car.SpeedMetresPerSecond}");
        Assert.True(car.PositionEnu.X < 500208.0);
        Assert.NotEqual(0.0, car.HeadingRadians);

        var speedBeforeSlide = car.SpeedMetresPerSecond;
        car.DampSpeedOnWallSlide(0.4);
        Assert.Equal(speedBeforeSlide * 0.4, car.SpeedMetresPerSecond, 6);

        for (var i = 0; i < 60; i++)
            car.StepMotion(1.0 / 60.0, throttleInput: 1.0, brakeInput: 0.0, steerInput: 0.0);
        Assert.True(car.SpeedMetresPerSecond > 0.0);
    }
}
