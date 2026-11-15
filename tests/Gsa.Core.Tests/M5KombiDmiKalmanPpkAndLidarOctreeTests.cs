using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Camera;
using Gsa.Core.Sensors.Fusion;
using Gsa.Core.Sensors.Gnss;
using Gsa.Core.Sensors.Lidar;
using Xunit;

namespace Gsa.Core.Tests;

public sealed class M5KombiDmiKalmanPpkAndLidarOctreeTests
{
    [Fact]
    public void T_F03_06_KombiProfileRigLimitsStorageConversionAndTunnelCorridorBudgetWorkDeterministically()
    {
        var kombi = PlatformProfile.Kombi;
        Assert.Equal(4, kombi.Level);
        Assert.Equal(16.0, kombi.MaxSpeedMetresPerSecond);
        Assert.Equal(14000, kombi.PriceCoords);
        Assert.Equal(4000, kombi.VirtualStorageCapacityUnits);
        Assert.Equal(3600.0, kombi.PhysicalAutonomySeconds);
        Assert.True(kombi.SupportsLidarSlots);

        Assert.Equal(1, KombiSurveyRigCoordinator.ComputeVirtualStorageUnits(1));
        Assert.Equal(1, KombiSurveyRigCoordinator.ComputeVirtualStorageUnits(1000));
        Assert.Equal(2, KombiSurveyRigCoordinator.ComputeVirtualStorageUnits(1001));
        Assert.Equal(4000, KombiSurveyRigCoordinator.ComputeVirtualStorageUnits(4_000_000));

        var rig = KombiSurveyRigCoordinator.ConfigureRig(
            kombi,
            playerProgressionLevel: 4,
            requestedLidar: LidarProfile.L4,
            requestedImuGrade: ImuGrade.Tactical,
            includeWheelDmi: true,
            includeDualAntennaHeading: false);

        Assert.True(rig.Succeeded, rig.ReasonKey);
        Assert.NotNull(rig.Rack);
        Assert.True(rig.TotalPowerWatts <= KombiSurveyRigCoordinator.DefaultRackPowerBudgetWatts);
        Assert.True(rig.TotalWeightGrams <= KombiSurveyRigCoordinator.DefaultRackWeightBudgetGrams);
        Assert.True(rig.HasWheelDmi);
        Assert.True(rig.SupportsPpk);
        Assert.Contains("lidar_l4", rig.UnlockedCapabilities);
        Assert.Contains("dmi", rig.UnlockedCapabilities);
        Assert.Contains("imu_tatica", rig.UnlockedCapabilities);
        Assert.Contains("ppk", rig.UnlockedCapabilities);

        var l5Blocked = KombiSurveyRigCoordinator.ConfigureRig(kombi, playerProgressionLevel: 4, requestedLidar: LidarProfile.L5);
        Assert.False(l5Blocked.Succeeded);
        Assert.Contains("level_5", l5Blocked.ReasonKey);

        var dualBlocked = KombiSurveyRigCoordinator.ConfigureRig(kombi, playerProgressionLevel: 4, includeDualAntennaHeading: true);
        Assert.False(dualBlocked.Succeeded);
        Assert.Contains("level_5", dualBlocked.ReasonKey);

        // Single-pass 3.2 km tunnel corridor at 12 m/s and 8,000 returns/s fits directly in 4000 storage units (3,734 <= 4,000) and 3600 s autonomy.
        var segments = new[]
        {
            new KombiTunnelSegmentSpec("approach_west", 1200.0, 12.0, IsTunnelBlackout: false),
            new KombiTunnelSegmentSpec("main_tunnel", 3200.0, 12.0, IsTunnelBlackout: true),
            new KombiTunnelSegmentSpec("approach_east", 1200.0, 12.0, IsTunnelBlackout: false),
        };
        var corridorOk = KombiSurveyRigCoordinator.SimulateTunnelCorridor(kombi, LidarProfile.L4, segments, lidarReturnsPerSecond: 8_000.0);
        Assert.True(corridorOk.Succeeded, corridorOk.ReasonKey);
        Assert.True(corridorOk.AutonomyWithinLimit);
        Assert.True(corridorOk.StorageWithinLimit);
        Assert.True(corridorOk.TotalBlackoutSeconds > 200.0);
        Assert.True(corridorOk.PeakStorageUnitsBetweenUnloads <= 4000);

        // High-density multi-pass exceeds 4000 units without an unload point, then succeeds cleanly with an unload stop.
        var heavySegments = new[]
        {
            new KombiTunnelSegmentSpec("tunnel_pass_a", 3600.0, 12.0, IsTunnelBlackout: true, Passes: 1),
            new KombiTunnelSegmentSpec("tunnel_pass_b", 3600.0, 12.0, IsTunnelBlackout: true, Passes: 1),
        };
        var overflow = KombiSurveyRigCoordinator.SimulateTunnelCorridor(kombi, LidarProfile.L4, heavySegments, lidarReturnsPerSecond: 12_000.0);
        Assert.False(overflow.Succeeded);
        Assert.Equal("kombi.error.storage_capacity_exceeded", overflow.ReasonKey);

        var withUnload = KombiSurveyRigCoordinator.SimulateTunnelCorridor(
            kombi, LidarProfile.L4, heavySegments, lidarReturnsPerSecond: 12_000.0,
            unloadAfterSegmentIndices: new HashSet<int> { 0 });
        Assert.True(withUnload.Succeeded, withUnload.ReasonKey);
        Assert.Equal(1, withUnload.UnloadCount);
        Assert.True(withUnload.PeakStorageUnitsBetweenUnloads <= 4000);
        Assert.True(withUnload.ElapsedAutonomySeconds <= 3600.0);
    }

    [Fact]
    public void T_F14_02_DmiNoiseScaleWetSlip25xVarianceAndZupt2SecondThreeCriteriaRejectFalseStops()
    {
        var dmi = new WheelDmiSimulator(seed: 12345UL, initialScaleErrorOverride: 0.005);
        var dryObs = new List<WheelDmiObservation>(200);
        for (var i = 0; i < 200; i++)
            dryObs.Add(dmi.Observe(i * 0.05, 0.05, trueForwardSpeedMetresPerSecond: 10.0, isWetRoad: false));

        var dryMean = dryObs.Average(o => o.MeasuredForwardSpeedMetresPerSecond);
        Assert.InRange(dryMean, 10.04, 10.06); // ~10.0 * (1 + 0.005)
        Assert.Equal(0.02, dryObs[0].LongitudinalSigmaMetresPerSecond, 6);

        var wetObs = dmi.Observe(10.0, 0.05, 10.0, isWetRoad: true, requestedWetSlipFraction: 0.20);
        Assert.True(wetObs.IsWetRoad);
        Assert.True(wetObs.VarianceInflatedForWetSlip);
        Assert.Equal(25.0 * dryObs[0].LongitudinalVarianceMetresPerSecondSquared,
            wetObs.LongitudinalVarianceMetresPerSecondSquared, 10);
        Assert.True(wetObs.MeasuredForwardSpeedMetresPerSecond < 8.5); // up to 20% slip reduction

        // ZUPT triggers only after continuous 2.0 s satisfying DMI < 0.05 m/s, gyro < 0.01 rad/s, ||f|-g| < 0.1 m/s^2.
        var zuptDetector = new StationaryZuptDetector();
        var imuSim = new ImuSimulator(
            ImuProfile.Tactical, whiteNoiseSeed: 101UL, biasSeed: 102UL,
            initialGyroBiasOverride: new Vector3d(0.0004, -0.0003, 0.0002),
            initialAccelBiasOverride: Vector3d.Zero,
            gyroScaleOverride: Vector3d.Zero,
            accelScaleOverride: Vector3d.Zero);

        var idleSpecificForce = ImuIdealKinematics.ComputeSpecificForceBody(Quaterniond.Identity, Vector3d.Zero);

        // False stop 1: brief 1.5 s pause -> never triggers ZUPT
        for (var s = 0; s < 30; s++)
        {
            var imu = imuSim.Observe(s * 0.05, 0.05, idleSpecificForce, Vector3d.Zero);
            var res = zuptDetector.Evaluate(0.05, 0.01, imu);
            Assert.False(res.ZuptTriggered);
        }

        // False stop 2: wet wheel lockup (DMI reads 0 m/s while vehicle skids/decelerates 2.5 m/s^2) -> resets and never triggers ZUPT
        var skiddingForce = ImuIdealKinematics.ComputeSpecificForceBody(Quaterniond.Identity, new Vector3d(-2.5, 0.0, 0.0));
        var lockDmi = dmi.Observe(2.0, 0.05, 8.0, isWetRoad: true, simulateWetWheelLockup: true);
        Assert.Equal(0.0, lockDmi.MeasuredForwardSpeedMetresPerSecond, 6);
        var skidImu = imuSim.Observe(2.0, 0.05, skiddingForce, Vector3d.Zero);
        var lockResult = zuptDetector.Evaluate(0.05, lockDmi.MeasuredForwardSpeedMetresPerSecond, skidImu);
        Assert.False(lockResult.SpecificForceCriterionMet);
        Assert.False(lockResult.ZuptTriggered);
        Assert.Equal(0.0, lockResult.ContinuousStationarySeconds, 6);

        // Genuine stop >= 2.0 s (40 steps at 20 Hz) -> triggers ZUPT with 0.01 m/s sigma and refines gyro bias estimate
        ZuptDetectionResult? last = null;
        for (var s = 0; s < 45; s++)
        {
            var imu = imuSim.Observe(3.0 + s * 0.05, 0.05, idleSpecificForce, Vector3d.Zero);
            var obsDmi = dmi.Observe(3.0 + s * 0.05, 0.05, 0.0);
            last = zuptDetector.Evaluate(0.05, obsDmi.MeasuredForwardSpeedMetresPerSecond, imu);
            if (s < 39)
                Assert.False(last.ZuptTriggered);
        }

        Assert.NotNull(last);
        Assert.True(last!.ZuptTriggered);
        Assert.Equal(0.01, last.VelocitySigmaMetresPerSecond, 6);
        Assert.True((last.EstimatedGyroBiasRadiansPerSecond - new Vector3d(0.0004, -0.0003, 0.0002)).Length < 0.0005);
    }

    [Fact]
    public void T_F14_03_SixStateKalmanMonotonicImuGradesDmiZupt100SeedsAndNoTruthAccess()
    {
        // Process noise Q constants match specs/parametros.md
        Assert.Equal(0.1, SixStateTrajectoryKalmanFilter.GetProcessNoisePowerDensity(ImuGrade.Mems), 10);
        Assert.Equal(0.01, SixStateTrajectoryKalmanFilter.GetProcessNoisePowerDensity(ImuGrade.Tactical), 10);
        Assert.Equal(0.001, SixStateTrajectoryKalmanFilter.GetProcessNoisePowerDensity(ImuGrade.Navigation), 10);

        // Reflection guard: estimator classes must never reference TruthSample or ground-truth types.
        foreach (var estimatorType in new[]
        {
            typeof(SixStateTrajectoryKalmanFilter),
            typeof(StationaryZuptDetector),
            typeof(DualAntennaHeadingSensor),
            typeof(PpkRtsSmootherAndControlAdjuster),
            typeof(LidarRayGeoreferencer)
        })
        {
            foreach (var member in estimatorType.GetMembers())
            {
                Assert.DoesNotContain("TruthSample", member.ToString() ?? string.Empty, StringComparison.Ordinal);
                Assert.DoesNotContain("TruePose", member.ToString() ?? string.Empty, StringComparison.Ordinal);
            }
        }

        // Covariance grows during tunnel blackout and flags blackout / uncertainty alerts
        var kf = new SixStateTrajectoryKalmanFilter(0.0, Vector3d.Zero, new Vector3d(10, 0, 0), Quaterniond.Identity,
            initialPositionSigmaMetres: 0.05, initialVelocitySigmaMetresPerSecond: 0.05);
        var sigma0 = kf.PositionSigma3DMetres;
        var idleForce = ImuIdealKinematics.ComputeSpecificForceBody(Quaterniond.Identity, Vector3d.Zero);
        for (var i = 0; i < 600; i++) // 6 s without GNSS
            kf.Predict(0.01, idleForce, Quaterniond.Identity, ImuGrade.Tactical);
        var sample = kf.GetCurrentSample();
        Assert.True(sample.PositionSigma3DMetres > sigma0 * 5.0);
        Assert.True(sample.BlackoutAlertActive);

        // 100 deterministic seeds across Mems, Tactical, Navigation: finite covariances, Navigation < Tactical < Mems, and Aided < FreeInertial
        var summary = TrajectoryFusionBenchmark.RunTunnelBlackoutSuite(
            seedCount: 100, preTunnelSeconds: 4.0, tunnelBlackoutSeconds: 25.0,
            zupStopStartOffsetSeconds: 10.0, zuptStopDurationSeconds: 3.0);

        var mems = summary[ImuGrade.Mems];
        var tact = summary[ImuGrade.Tactical];
        var nav = summary[ImuGrade.Navigation];

        Assert.True(mems.AllStatesFinite && tact.AllStatesFinite && nav.AllStatesFinite);
        Assert.True(mems.MedianAidedDriftMetres < mems.MedianFreeInertialDriftMetres);
        Assert.True(tact.MedianAidedDriftMetres < tact.MedianFreeInertialDriftMetres);
        Assert.True(nav.MedianAidedDriftMetres < nav.MedianFreeInertialDriftMetres);
        Assert.True(nav.MedianAidedDriftMetres < tact.MedianAidedDriftMetres);
        Assert.True(tact.MedianAidedDriftMetres < mems.MedianAidedDriftMetres);
    }

    [Fact]
    public void T_F14_04_PpkRtsSmootherControlIsolation3SigmaOutliersAndDualAntennaHeadingWorkCorrectly()
    {
        // Build 30 s corridor: [0..8] GNSS + common error, (8..22) tunnel blackout, [22..30] GNSS recovery
        var commonError = new CommonGnssErrorProcess(4441UL, sigmaMetres: 3.5, correlationTimeSeconds: 120.0);
        var activeBase = new InstalledBaseStationState("mark_01", Vector3d.Zero, 0.0);
        var rtk = new RtkSolutionStateMachine(seed: 5555UL);
        var imuSim = new ImuSimulator(
            ImuProfile.Mems, 601UL, 602UL,
            initialGyroBiasOverride: Vector3d.Zero,
            initialAccelBiasOverride: new Vector3d(0.06, -0.04, 0.0),
            gyroScaleOverride: Vector3d.Zero,
            accelScaleOverride: Vector3d.Zero);
        var dmiSim = new WheelDmiSimulator(seed: 603UL, initialScaleErrorOverride: 0.0);

        const double dt = 0.05; // 20 Hz IMU/DMI
        const int totalSteps = 600;
        var imuList = new List<ImuObservation>(totalSteps);
        var dmiList = new List<WheelDmiObservation>(totalSteps);
        var gnssList = new List<CorrectableGnssObservationRecord>();
        var truePositions = new Vector3d[totalSteps];

        var truePos = Vector3d.Zero;
        var idleForce = ImuIdealKinematics.ComputeSpecificForceBody(Quaterniond.Identity, Vector3d.Zero);
        for (var i = 0; i < totalSteps; i++)
        {
            var t = (i + 1) * dt;
            truePos += new Vector3d(10.0 * dt, 0.0, 0.0);
            truePositions[i] = truePos;
            imuList.Add(imuSim.Observe(t, dt, idleForce, Vector3d.Zero));
            dmiList.Add(dmiSim.Observe(t, dt, 10.0));

            if ((i + 1) % 20 == 0) // 1 Hz GNSS
            {
                var epochCommonError = commonError.At(t);
                activeBase.RecordEpochCorrection(t, epochCommonError, baseOpenness: 0.95, dtSeconds: 1.0);
                var inTunnel = t > 8.0 && t < 22.0;
                var openness = inTunnel ? 0.0 : 0.95;
                // Simulate live autonomous GNSS outside tunnel (radio link offline) while carrier PPK rover logs phase data (RtkFix grade).
                var obs = rtk.StepEpoch(t, 1.0, truePos, epochCommonError, openness, platformLevel: 4,
                    hasMultiFrequencyProfile: false, activeBase: null);
                var ppkCarrierRaw = inTunnel
                    ? Vector3d.Zero
                    : truePos + epochCommonError + new Vector3d(0.015, -0.015, 0.01);
                gnssList.Add(new CorrectableGnssObservationRecord(
                    obs.Sequence, t, ppkCarrierRaw, epochCommonError, obs.Solution, openness, 250.0,
                    inTunnel ? obs.ProfileId : GnssProfile.RtkFix.Id, obs.MeasuredPositionEnu));
            }
        }
        var baseLog = activeBase.ExportLog(samplingIntervalSeconds: 1.0);

        // 1. Missing base log -> explicit diagnostic, no fake improvement
        var missingBaseResult = PpkRtsSmootherAndControlAdjuster.ProcessTrajectory(
            Vector3d.Zero, new Vector3d(10, 0, 0), Quaterniond.Identity, ImuGrade.Mems,
            imuList, gnssList, baseStationLog: null, dmiObservations: dmiList);
        Assert.False(missingBaseResult.Succeeded);
        Assert.Equal("PPK_BASE_LOG_MISSING", missingBaseResult.DiagnosticCode);
        Assert.Equal(missingBaseResult.ForwardTrajectory[300].PositionEnu, missingBaseResult.SmoothedTrajectory[300].PositionEnu);

        // 2. Incompatible time window base log -> explicit diagnostic
        var shiftedLog = new BaseStationLog("mark_01", Vector3d.Zero, 1.0,
            new[] { new BaseStationCorrectionSample(1000.0, "mark_01", Vector3d.Zero, 0.95) });
        var badWindowResult = PpkRtsSmootherAndControlAdjuster.ProcessTrajectory(
            Vector3d.Zero, new Vector3d(10, 0, 0), Quaterniond.Identity, ImuGrade.Mems,
            imuList, gnssList, baseStationLog: shiftedLog, dmiObservations: dmiList);
        Assert.False(badWindowResult.Succeeded);
        Assert.Equal("PPK_BASE_WINDOW_INCOMPATIBLE", badWindowResult.DiagnosticCode);

        // 3. Valid base log + valid control + 3-sigma outlier control + CQ checkpoint isolation
        var controls = new[]
        {
            new SurveyControlObservation("CTRL_MID", 15.0, truePositions[299], DeclaredSigmaMetres: 0.03),
            new SurveyControlObservation("CTRL_OUTLIER", 16.0, truePositions[319] + new Vector3d(0.0, 25.0, 0.0), DeclaredSigmaMetres: 0.05),
            new SurveyControlObservation("CQ_CHECK_01", 17.0, truePositions[339] + new Vector3d(0.0, 0.5, 0.0), DeclaredSigmaMetres: 0.02, IsCheckpoint: false),
        };
        var reservedCheckpoints = new HashSet<string>(StringComparer.Ordinal) { "CQ_CHECK_01" };

        var baselinePurePpk = PpkRtsSmootherAndControlAdjuster.ProcessTrajectory(
            Vector3d.Zero, new Vector3d(10, 0, 0), Quaterniond.Identity, ImuGrade.Mems,
            imuList, gnssList, baseLog, dmiObservations: dmiList);

        var baselineNoCp = PpkRtsSmootherAndControlAdjuster.ProcessTrajectory(
            Vector3d.Zero, new Vector3d(10, 0, 0), Quaterniond.Identity, ImuGrade.Mems,
            imuList, gnssList, baseLog, dmiObservations: dmiList,
            surveyControls: controls.Take(2).ToArray());

        var ppkWithAll = PpkRtsSmootherAndControlAdjuster.ProcessTrajectory(
            Vector3d.Zero, new Vector3d(10, 0, 0), Quaterniond.Identity, ImuGrade.Mems,
            imuList, gnssList, baseLog, dmiObservations: dmiList,
            surveyControls: controls,
            reservedCqCheckpointIds: reservedCheckpoints);

        Assert.True(ppkWithAll.Succeeded);
        Assert.True(ppkWithAll.DifferentialBaseApplied);
        Assert.Equal("PPK_RTS_COMPLETED", ppkWithAll.DiagnosticCode);
        Assert.Contains(ppkWithAll.ControlOutcomes, c => c.PointId == "CTRL_MID" && c.Applied);
        Assert.Contains(ppkWithAll.ControlOutcomes, c => c.PointId == "CTRL_OUTLIER" && c.FlaggedAsOutlier && !c.Applied);
        Assert.Contains(ppkWithAll.ControlOutcomes, c => c.PointId == "CQ_CHECK_01" && c.RejectedAsCqCheckpoint && !c.Applied);

        // Passing CQ checkpoint must produce bit-for-bit identical trajectory to omitting it!
        Assert.Equal(baselineNoCp.SmoothedTrajectory[339].PositionEnu, ppkWithAll.SmoothedTrajectory[339].PositionEnu);

        // Both pure-PPK RTS smoothing and control-adjusted PPK dramatically reduce mid-tunnel blackout error vs real-time live!
        var liveErrorMidTunnel = (missingBaseResult.ForwardTrajectory[299].PositionEnu - truePositions[299]).Length;
        var purePpkErrorMidTunnel = (baselinePurePpk.SmoothedTrajectory[299].PositionEnu - truePositions[299]).Length;
        var ppkSmoothedErrorMidTunnel = (ppkWithAll.SmoothedTrajectory[299].PositionEnu - truePositions[299]).Length;
        Assert.True(purePpkErrorMidTunnel < liveErrorMidTunnel * 0.4);
        Assert.True(ppkSmoothedErrorMidTunnel < 0.25 && ppkSmoothedErrorMidTunnel < liveErrorMidTunnel * 0.2);

        // 4. Dual-antenna 1.0 m heading sensor: works at 0 m/s, flags secondary loss cleanly without fallback to truth
        var dualSensor = new DualAntennaHeadingSensor(seed: 7771UL);
        Assert.Equal(1.0, DualAntennaHeadingSensor.NominalBaselineMetres);
        var stationaryObs = dualSensor.Observe(10.0, trueYawRadians: 1.25, vehicleSpeedMetresPerSecond: 0.0, platformLevel: 5, secondaryAntennaSignalAvailable: true);
        Assert.True(stationaryObs.IsAvailable);
        Assert.False(stationaryObs.IsDegradedSingleAntenna);
        Assert.NotNull(stationaryObs.MeasuredYawRadians);
        var (fusedYaw, fusedSigma, updated) = DualAntennaHeadingSensor.FuseEstimatedYawRadians(0.0, 0.5, stationaryObs);
        Assert.True(updated);
        Assert.True(Math.Abs(fusedYaw - 1.25) < 0.05);
        Assert.True(fusedSigma < 0.02);

        var degradedObs = dualSensor.Observe(11.0, trueYawRadians: 1.25, vehicleSpeedMetresPerSecond: 10.0, platformLevel: 5, secondaryAntennaSignalAvailable: false);
        Assert.False(degradedObs.IsAvailable);
        Assert.True(degradedObs.IsDegradedSingleAntenna);
        Assert.Equal("DUAL_ANTENNA_SECONDARY_LOST", degradedObs.ReasonKey);
        Assert.Null(degradedObs.MeasuredYawRadians);
    }

    [Fact]
    public void T_F15_02_KombiProfilerPerRayGeoreferencingOcclusionSpeedSpacingOutOfWindowAndReprocessingWork()
    {
        var acq = new KombiLidarProfilerAcquisition(LidarProfile.L4, seed: 888UL);

        // Occlusion scene: closer opaque pillar at X=10, Y in [-2,2], behind it a wide facade at X=10, Y=6 (Wait: beams along Y left/right at -Y/+Y! Pillar at (5, 4, 2) blocks wall behind at (5, 9, 2))
        var scene = new[]
        {
            new LidarAnalyticBox("front_pillar", "concrete", new Vector3d(5.0, 4.0, 1.5), new Vector3d(10.0, 1.0, 6.0)),
            new LidarAnalyticBox("occluded_wall", "concrete", new Vector3d(5.0, 9.0, 1.5), new Vector3d(10.0, 1.0, 6.0)),
        };
        var rayCaster = LidarBenchSimulator.CreateAnalyticBoxRayCaster(scene);

        Func<double, double, RigidTransform> buildTruePose = (speed, t) =>
            new RigidTransform(Quaterniond.Identity, new Vector3d(speed * t, 0.0, 2.0));

        // Capture at 5 m/s vs 15 m/s over 5 scan lines
        var rawSlow = acq.SimulateContinuousSwath(0.0, scanLineCount: 5, t => buildTruePose(5.0, t), rayCaster, zeroNoise: true);
        var rawFast = acq.SimulateContinuousSwath(0.0, scanLineCount: 5, t => buildTruePose(15.0, t), rayCaster, zeroNoise: true);

        EstimatedBodyTrajectory BuildTrajectory(double speed, double endTime, Vector3d posBias, Quaterniond rotBias) =>
            new(Enumerable.Range(0, 21).Select(i =>
            {
                var t = endTime * i / 20.0;
                return new EstimatedBodyPose(t, new Vector3d(speed * t, 0.0, 2.0) + posBias, rotBias);
            }));

        var slowTraj = BuildTrajectory(5.0, 1.0, Vector3d.Zero, Quaterniond.Identity);
        var fastTraj = BuildTrajectory(15.0, 1.0, Vector3d.Zero, Quaterniond.Identity);

        var geoSlow = LidarRayGeoreferencer.GeoreferenceRawRays(rawSlow, slowTraj, LidarMountEstimate.Zero, LidarProfile.L4.ScanRateHz);
        var geoFast = LidarRayGeoreferencer.GeoreferenceRawRays(rawFast, fastTraj, LidarMountEstimate.Zero, LidarProfile.L4.ScanRateHz);

        Assert.True(geoSlow.Succeeded && geoFast.Succeeded);
        // Occluded wall at Y=9 behind pillar at Y=4 receives zero hits
        Assert.DoesNotContain(geoSlow.Samples, s => s.MapPositionEnu.Y > 7.5);
        Assert.Contains(geoSlow.Samples, s => Math.Abs(s.MapPositionEnu.Y - 3.5) < 0.25);

        // Speed-dependent longitudinal spacing (~ speed / ScanRateHz -> 3x wider at 15 m/s than 5 m/s)
        Assert.True(geoFast.EstimatedLongitudinalSpacingMetres > geoSlow.EstimatedLongitudinalSpacingMetres * 2.5);

        // Uncalibrated mount + drifted trajectory introduces real misalignment, then PPK + calibration fixes the exact same raw rays!
        var badTraj = BuildTrajectory(15.0, 1.0, new Vector3d(0.0, 2.0, 0.0), Quaterniond.Identity);
        var badMount = new LidarMountEstimate(Vector3d.Zero, Quaterniond.FromAxisAngle(Vector3d.UnitZ, 0.12), TimeOffsetSeconds: 0.02);
        var uncalibratedCloud = LidarRayGeoreferencer.GeoreferenceRawRays(rawFast, badTraj, badMount, LidarProfile.L4.ScanRateHz);
        var fixedCloud = LidarRayGeoreferencer.GeoreferenceRawRays(rawFast, fastTraj, LidarMountEstimate.Zero, LidarProfile.L4.ScanRateHz);

        var errBad = uncalibratedCloud.Samples.Average(s => Math.Abs(s.MapPositionEnu.Y - 3.5));
        var errFixed = fixedCloud.Samples.Average(s => Math.Abs(s.MapPositionEnu.Y - 3.5));
        Assert.True(errFixed < 0.1 && errBad > 1.5);

        // Out-of-window ray timestamps are strictly rejected (never extrapolated)
        var shortTraj = BuildTrajectory(15.0, 0.15, Vector3d.Zero, Quaterniond.Identity);
        var partial = LidarRayGeoreferencer.GeoreferenceRawRays(rawFast, shortTraj, LidarMountEstimate.Zero);
        Assert.True(partial.RejectedOutOfWindowRays > 0);
        Assert.Contains("RAY_TIMESTAMP_OUT_OF_TRAJECTORY_WINDOW", partial.DiagnosticCodes);
    }

    [Fact]
    public void T_F15_03_DoublePrecisionOctreeChunksSha256Lru256MiBVisualLod250kAndRevisionInvalidation()
    {
        Assert.Equal(4096, PointCloudOctreeAndChunkStore.MaxSamplesPerLeaf);
        Assert.Equal(16, PointCloudOctreeAndChunkStore.MaxOctreeDepth);
        Assert.Equal(65536, PointCloudOctreeAndChunkStore.MaxSamplesPerChunk);
        Assert.Equal(256L * 1024L * 1024L, PointCloudOctreeAndChunkStore.RamCacheBudgetBytes);
        Assert.Equal(250_000, PointCloudOctreeAndChunkStore.MaxVisualLodBudgetPoints);

        // 1. Spatially distributed 20,000 samples in ENU coordinates > 7e6 + fine 1e-6 m precision
        var samples = new List<GeoreferencedLidarSample>(20_000);
        for (var i = 0; i < 20_000; i++)
        {
            var pos = new Vector3d(
                500_000.0 + (i % 200) * 0.5 + 0.000123,
                7_400_000.0 + (i / 200) * 0.5 + 0.000456,
                12.345678);
            samples.Add(new GeoreferencedLidarSample(i + 1, i * 0.001, pos, 0.85f, 1, false, 2));
        }

        // Configure tight RAM cache budget (500 KB) while keeping 65,536/chunk cap via smaller custom chunks? Wait: use default budget or explicit chunk size:
        // Wait: 20,000 samples * 48 bytes = 960,000 bytes (< 1 MB), wait if 1 chunk is 960,000 bytes, ramBudgetBytes must >= chunkSize.
        // Test A: Default 256 MiB store for spatial octree, double precision, visual LOD <= 250k, overflow at depth 16, revision invalidation.
        var store = new PointCloudOctreeAndChunkStore();
        // Add 6,000 near-coincident points within 1e-7 m at one corner to force MaxOctreeDepth (16) overflow pagination without dropping samples!
        for (var j = 0; j < 6_000; j++)
        {
            samples.Add(new GeoreferencedLidarSample(
                100_000 + j, 50.0,
                new Vector3d(500_000.000001 + j * 1e-11, 7_400_000.000001, 12.0),
                0.9f, 1, false, 1));
        }

        var rev1 = store.IngestRevision(samples);
        Assert.Equal(1, rev1);
        var metrics = store.GetMetrics();
        Assert.Equal(26_000, metrics.TotalSamples);
        Assert.True(metrics.MaxObservedDepth <= 16);
        Assert.True(metrics.MaxLeafPrimaryBucketCount <= 4096);
        Assert.True(metrics.OverflowPageCount >= 1);
        Assert.Equal(1, metrics.ChunkCount);
        Assert.All(store.ChunkMetadata.Values, c =>
        {
            Assert.True(c.SampleCount <= 65536);
            Assert.Equal(64, c.ChecksumSha256Hex.Length);
        });

        // Double-precision query preserves exact ENU decimals and 1e-11 offsets
        var tinyRegion = store.QueryScientificSamples(
            new Vector3d(500_000.0, 7_400_000.0, 11.9),
            new Vector3d(500_000.00005, 7_400_000.00005, 12.1));
        Assert.Equal(6_000, tinyRegion.Count);
        Assert.Contains(tinyRegion, s => Math.Abs(s.MapPositionEnu.X - 500_000.000001) < 1e-9);

        // Visual LOD budget respected without touching scientific counts
        var lodPoints = store.QueryVisualLod(250_000);
        Assert.True(lodPoints.Count > 0 && lodPoints.Count <= 250_000);

        // Invalidate & rebuild revision 2
        var rev2 = store.IngestRevision(samples.Take(100));
        Assert.Equal(2, rev2);
        Assert.Equal(100, store.GetMetrics().TotalSamples);
        Assert.Equal(100, store.QueryScientificSamples(
            new Vector3d(499_999, 7_399_999, 0),
            new Vector3d(501_000, 7_401_000, 100)).Count);

        // Test B: Multi-chunk (> 65,536 samples) LRU RAM cache eviction & SHA-256 verification under RAM budget <= 256 MiB
        // 150,000 samples -> 3 chunks (65,536 + 65,536 + 18,928), largest chunk = 65536 * 48 = 3,145,728 bytes.
        // Set LRU budget to 4,000,000 bytes (fits at most 1 full chunk + 1 small chunk -> forces eviction of chunk 0 & chunk 1!).
        var bigCloud = Enumerable.Range(0, 150_000).Select(i =>
            new GeoreferencedLidarSample(
                i + 1, i * 0.0001,
                new Vector3d(500_000 + (i % 500), 7_400_000 + (i / 500), 10.0),
                0.8f, 1, false)).ToArray();

        var pagedStore = new PointCloudOctreeAndChunkStore(ramBudgetBytes: 4_000_000L);
        pagedStore.IngestRevision(bigCloud);
        var pagedMetrics = pagedStore.GetMetrics();
        Assert.Equal(3, pagedMetrics.ChunkCount);
        Assert.True(pagedMetrics.CurrentResidentRamBytes <= 4_000_000L);
        Assert.True(pagedMetrics.LruEvictionCount >= 1);

        // Evicted chunk 0 reloads transparently via backing store with SHA-256 verification
        Assert.False(pagedStore.IsChunkResidentInRam("rev001_chunk_0000"));
        var reloaded = pagedStore.QueryScientificSamples(
            new Vector3d(499_999, 7_399_999, 9.0),
            new Vector3d(500_501, 7_400_005, 11.0));
        Assert.NotEmpty(reloaded);
        Assert.True(pagedStore.GetMetrics().BackingReloadCount >= 1);

        // Tampering with backing bytes of an evicted chunk is caught deterministically by SHA-256 check!
        Assert.False(pagedStore.IsChunkResidentInRam("rev001_chunk_0001"));
        pagedStore.CorruptBackingChunkBytesForVerificationTest("rev001_chunk_0001");
        Assert.Throws<InvalidDataException>(() => pagedStore.QueryScientificSamples(
            new Vector3d(499_999, 7_400_135, 9.0),
            new Vector3d(500_501, 7_400_140, 11.0)));

        // Requesting > 250k visual LOD points is clamped strictly to 250,000
        var defaultPaged = new PointCloudOctreeAndChunkStore();
        var massiveCloud = Enumerable.Range(0, 300_000).Select(i =>
            new GeoreferencedLidarSample(
                i + 1, i * 0.0001,
                new Vector3d(500_000 + (i % 300), 7_400_000 + ((i / 300) % 300), 5.0 + (i / 90_000)),
                0.7f, 1, false)).ToArray();
        defaultPaged.IngestRevision(massiveCloud);
        var cappedVisual = defaultPaged.QueryVisualLod(500_000);
        Assert.True(cappedVisual.Count <= 250_000);
    }
}
