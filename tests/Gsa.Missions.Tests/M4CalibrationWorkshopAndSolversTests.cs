using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Calibration;
using Gsa.Core.Sensors.Camera;
using Gsa.Missions.Contracts;
using Gsa.Missions.Economy;
using Gsa.Missions.Office;
using Xunit;

namespace Gsa.Missions.Tests;

public sealed class M4CalibrationWorkshopAndSolversTests
{
    private static List<CalibrationBoardPose> SynthesizeBoardPoses(
        CameraIntrinsics trueIntrinsics,
        bool includeValidationPose = true)
    {
        var boardPoints = new List<Vector3d>();
        for (var ix = -2; ix <= 2; ix++)
        {
            for (var iy = -1; iy <= 1; iy++)
                boardPoints.Add(new Vector3d(ix * 0.25, iy * 0.25, 0.0));
        }

        (string Id, RigidTransform Pose, bool Holdout)[] configs =
        [
            ("pose_train_1", new RigidTransform(Quaterniond.FromAxisAngle(Vector3d.UnitY, 0.12), new Vector3d(-0.1, 0.05, 3.0)), false),
            ("pose_train_2", new RigidTransform(Quaterniond.FromAxisAngle(Vector3d.UnitX, -0.15) * Quaterniond.FromAxisAngle(Vector3d.UnitZ, 0.18), new Vector3d(0.35, -0.15, 3.5)), false),
            ("pose_train_3", new RigidTransform(Quaterniond.FromAxisAngle(Vector3d.UnitY, -0.22) * Quaterniond.FromAxisAngle(Vector3d.UnitX, 0.10), new Vector3d(-0.30, 0.25, 2.7)), false),
            ("pose_holdout", new RigidTransform(Quaterniond.FromAxisAngle(Vector3d.UnitZ, -0.20) * Quaterniond.FromAxisAngle(Vector3d.UnitY, 0.18), new Vector3d(0.20, 0.15, 3.2)), true),
        ];

        var projector = new CameraProjector(trueIntrinsics);
        var result = new List<CalibrationBoardPose>();
        foreach (var (id, boardToCam, holdout) in configs)
        {
            if (holdout && !includeValidationPose)
                continue;
            var obs = new List<BoardCornerObservation>();
            foreach (var pt in boardPoints)
            {
                var proj = projector.ProjectCameraPoint(boardToCam.Transform(pt));
                Assert.True(proj.IsVisible);
                obs.Add(new BoardCornerObservation(pt, proj.Pixel!.Value.U, proj.Pixel!.Value.V));
            }
            result.Add(new CalibrationBoardPose(id, boardToCam, obs, holdout));
        }
        return result;
    }

    private static (List<ExtrinsicPlaneCorrespondence> Train, List<ExtrinsicPlaneCorrespondence> Holdout) SynthesizeTwoPassPlanes(
        Vector3d trueLeverArm,
        Quaterniond trueSensorToBody,
        bool flatZeroRotationPasses = false,
        bool onlyTwoParallelPlanes = false)
    {
        var trueCamToBody = new RigidTransform(trueSensorToBody, trueLeverArm);
        var trueBodyToCam = trueCamToBody.Inverse();

        // 3 non-parallel planes: ground (Z=0), east facade (X=20), south angled facade
        (string Group, Vector3d PointMap, Vector3d NormalMap)[] planes = onlyTwoParallelPlanes
            ? [
                ("wall_1", new Vector3d(20.0, 2.0, 2.0), -Vector3d.UnitX),
                ("wall_2", new Vector3d(-20.0, 3.0, 2.5), Vector3d.UnitX),
                ("wall_3", new Vector3d(15.0, 4.0, 1.5), -Vector3d.UnitX),
              ]
            : [
                ("ground_plane", new Vector3d(5.0, 2.0, 0.0), Vector3d.UnitZ),
                ("east_wall", new Vector3d(20.0, 3.0, 2.5), -Vector3d.UnitX),
                ("north_angled_wall", new Vector3d(4.0, 25.0, 3.0), new Vector3d(0.15, -0.95, -0.27).Normalized()),
              ];

        (RigidTransform PoseA, RigidTransform PoseB)[] passPairs = flatZeroRotationPasses
            ? [
                (new RigidTransform(Quaterniond.Identity, new Vector3d(0, 0, 0)),
                 new RigidTransform(Quaterniond.Identity, new Vector3d(2, -1, 0))),
                (new RigidTransform(Quaterniond.Identity, new Vector3d(3, 1, 0)),
                 new RigidTransform(Quaterniond.Identity, new Vector3d(1, 2, 0))),
              ]
            : [
                (new RigidTransform(Quaterniond.FromAxisAngle(Vector3d.UnitZ, 0.05) * Quaterniond.FromAxisAngle(Vector3d.UnitY, 0.04), new Vector3d(1.0, 0.5, 0.2)),
                 new RigidTransform(Quaterniond.FromAxisAngle(Vector3d.UnitZ, 0.75) * Quaterniond.FromAxisAngle(Vector3d.UnitX, -0.06), new Vector3d(4.0, -1.5, 0.1))),
                (new RigidTransform(Quaterniond.FromAxisAngle(Vector3d.UnitZ, -0.40) * Quaterniond.FromAxisAngle(Vector3d.UnitX, 0.05), new Vector3d(2.0, 3.0, 0.3)),
                 new RigidTransform(Quaterniond.FromAxisAngle(Vector3d.UnitZ, 1.25) * Quaterniond.FromAxisAngle(Vector3d.UnitY, -0.07), new Vector3d(6.0, 1.0, 0.0))),
              ];

        var train = new List<ExtrinsicPlaneCorrespondence>();
        var holdout = new List<ExtrinsicPlaneCorrespondence>();

        var sampleIdx = 0;
        foreach (var (planeId, basePtMap, normalMap) in planes)
        {
            for (var p = 0; p < passPairs.Length; p++)
            {
                var (poseA, poseB) = passPairs[p];
                var tangent1 = Math.Abs(normalMap.Z) < 0.9
                    ? Vector3d.Cross(normalMap, Vector3d.UnitZ).Normalized()
                    : Vector3d.UnitX;
                var tangent2 = Vector3d.Cross(normalMap, tangent1).Normalized();

                for (var s = 0; s < 3; s++)
                {
                    var ptA = basePtMap + tangent1 * (0.4 * s) + tangent2 * (0.25 * (p + 1));
                    var ptB = basePtMap - tangent1 * (0.3 * s) + tangent2 * (0.35 * (p + 0.5));

                    var sensorPtA = (poseA * trueCamToBody).Inverse().Transform(ptA);
                    var sensorPtB = (poseB * trueCamToBody).Inverse().Transform(ptB);
                    var sensorNormalB = (poseB.Rotation * trueSensorToBody).Inverse().Rotate(normalMap).Normalized();

                    var item = new ExtrinsicPlaneCorrespondence(planeId, poseA, sensorPtA, poseB, sensorPtB, sensorNormalB);
                    if (sampleIdx % 6 == 5)
                        holdout.Add(item);
                    else
                        train.Add(item);
                    sampleIdx++;
                }
            }
        }

        return (train, holdout);
    }

    private static (EstimatedBodyTrajectory Trajectory, List<TimeSyncCorrespondence> Samples) SynthesizeTimeSyncData(
        double trueTimeOffsetSeconds,
        Vector3d leverArmBody,
        Quaterniond sensorToBody,
        bool constantVelocity)
    {
        var poses = new List<EstimatedBodyPose>();
        for (var i = 0; i <= 100; i++)
        {
            var t = i * 0.1;
            var pos = constantVelocity
                ? new Vector3d(8.0 * t, 0.0, 0.0)
                : new Vector3d(3.0 * t + 0.8 * t * t, 0.5 * Math.Sin(0.4 * t), 0.0);
            var rot = constantVelocity
                ? Quaterniond.Identity
                : Quaterniond.FromAxisAngle(Vector3d.UnitZ, 0.08 * t);
            poses.Add(new EstimatedBodyPose(t, pos, rot));
        }

        var trajectory = new EstimatedBodyTrajectory(poses);
        var trueMount = new CameraMountEstimate(leverArmBody, sensorToBody, trueTimeOffsetSeconds);
        Vector3d[] targets =
        [
            new(15.0, 4.0, 2.0),
            new(28.0, -3.0, 1.5),
            new(45.0, 6.0, 3.0),
            new(65.0, 2.0, 2.5),
            new(90.0, -5.0, 1.8),
        ];

        var samples = new List<TimeSyncCorrespondence>();
        for (var k = 0; k < targets.Length; k++)
        {
            var rawTime = 1.5 + k * 1.4;
            Assert.True(trajectory.TryGetCameraToMap(rawTime, trueMount, out var sensorToMap));
            var sensorPoint = sensorToMap.Inverse().Transform(targets[k]);
            samples.Add(new TimeSyncCorrespondence(rawTime, sensorPoint, targets[k]));
        }

        return (trajectory, samples);
    }

    [Fact]
    public void T_F13_03_IntrinsicsLmConvergesWithinHeldOutThresholdAndRejectsInsufficientPoses()
    {
        var truth = new CameraIntrinsics(1280, 720, 680.0, 675.0, 648.0, 355.0, -0.14, 0.03);
        var guess = new CameraIntrinsics(1280, 720, 620.0, 620.0, 635.0, 365.0, 0.0, 0.0);

        var poses = SynthesizeBoardPoses(truth, includeValidationPose: true);
        var res = CalibrationWorkshopAndSolvers.CalibrateCameraIntrinsics(guess, poses);

        Assert.True(res.Succeeded);
        Assert.True(res.IterationsExecuted <= DampedLeastSquaresSolver.MaxIterations);
        Assert.True(res.ValidationHeldOutRmsePixels <= CalibrationWorkshopAndSolvers.MaxIntrinsicsHeldOutRmsePixels);
        Assert.InRange(res.EstimatedIntrinsics.Fx, 678.0, 682.0);
        Assert.InRange(res.EstimatedIntrinsics.K1, -0.15, -0.13);

        // Missing held-out validation pose or fewer than 3 training poses fails safely without revealing truth
        var noHoldoutPoses = SynthesizeBoardPoses(truth, includeValidationPose: false);
        var missingHoldoutRes = CalibrationWorkshopAndSolvers.CalibrateCameraIntrinsics(guess, noHoldoutPoses);
        Assert.False(missingHoldoutRes.Succeeded);
        Assert.Equal("calibration.intrinsics.insufficient_poses", missingHoldoutRes.ReasonKey);
        Assert.Equal(guess.Fx, missingHoldoutRes.EstimatedIntrinsics.Fx);
    }

    [Fact]
    public void T_F13_03_ExtrinsicsRequiresThreeNonParallelPlanesAndObservabilityWithinThreshold()
    {
        var trueLeverArm = new Vector3d(0.28, -0.02, 1.57);
        var trueRot = Quaterniond.FromAxisAngle(Vector3d.UnitY, 0.015) *
                      Quaterniond.FromAxisAngle(Vector3d.UnitX, -0.012) *
                      CameraMountEstimate.ForwardFacing.CameraToBody;

        var (train, holdout) = SynthesizeTwoPassPlanes(trueLeverArm, trueRot);
        var perturbedLeverArm = trueLeverArm + new Vector3d(0.025, -0.02, 0.018);
        var perturbedRot = Quaterniond.FromAxisAngle(Vector3d.UnitZ, 0.01) * trueRot;

        var ok = CalibrationWorkshopAndSolvers.CalibrateExtrinsicsPointToPlane(perturbedLeverArm, perturbedRot, train, holdout);
        Assert.True(ok.Succeeded);
        Assert.True(ok.DistinctPlaneGroupCount >= 3);
        Assert.True(ok.ValidationHeldOutRmseMetres <= CalibrationWorkshopAndSolvers.MaxExtrinsicsRmseMetres);
        Assert.True((ok.EstimatedLeverArmBody - trueLeverArm).Length < 0.01);

        // Coplanar / parallel planes rejected without returning truth
        var (parallelTrain, parallelHoldout) = SynthesizeTwoPassPlanes(trueLeverArm, trueRot, onlyTwoParallelPlanes: true);
        var rejectedParallel = CalibrationWorkshopAndSolvers.CalibrateExtrinsicsPointToPlane(perturbedLeverArm, perturbedRot, parallelTrain, parallelHoldout);
        Assert.False(rejectedParallel.Succeeded);
        Assert.Equal("calibration.extrinsics.coplanar_or_parallel_normals", rejectedParallel.ReasonKey);
        Assert.Equal(perturbedLeverArm, rejectedParallel.EstimatedLeverArmBody);

        // Constant orientation (no rotation between passes) cannot separate lever arm cleanly -> rejected
        var (flatTrain, flatHoldout) = SynthesizeTwoPassPlanes(trueLeverArm, trueRot, flatZeroRotationPasses: true);
        var rejectedFlat = CalibrationWorkshopAndSolvers.CalibrateExtrinsicsPointToPlane(perturbedLeverArm, perturbedRot, flatTrain, flatHoldout);
        Assert.False(rejectedFlat.Succeeded);
        Assert.Equal("calibration.extrinsics.unobservable_motion_or_geometry", rejectedFlat.ReasonKey);
    }

    [Fact]
    public void T_F13_03_TimeSyncRequiresMotionVariationAndRefinesToSubMillisecondWithinBounds()
    {
        const double trueOffset = 0.0374;
        var lever = new Vector3d(0.25, 0.0, 1.55);
        var rot = CameraMountEstimate.ForwardFacing.CameraToBody;

        var (varTraj, varSamples) = SynthesizeTimeSyncData(trueOffset, lever, rot, constantVelocity: false);
        var syncResult = CalibrationWorkshopAndSolvers.CalibrateTimeOffset(varTraj, lever, rot, varSamples);

        Assert.True(syncResult.Succeeded);
        Assert.InRange(Math.Abs(syncResult.EstimatedTimeOffsetSeconds - trueOffset), 0.0, 0.00025);
        Assert.True(syncResult.ResidualRmseMetres <= CalibrationWorkshopAndSolvers.MaxTimeOffsetResidualRmseMetres);

        // Constant velocity cannot isolate time offset from longitudinal lever arm -> rejected
        var (constTraj, constSamples) = SynthesizeTimeSyncData(trueOffset, lever, rot, constantVelocity: true);
        var constResult = CalibrationWorkshopAndSolvers.CalibrateTimeOffset(constTraj, lever, rot, constSamples);
        Assert.False(constResult.Succeeded);
        Assert.Equal("calibration.timesync.constant_velocity_unobservable", constResult.ReasonKey);
    }

    [Fact]
    public void T_F13_03_WorkshopGateArcadeFeesCalibrationDiscountAndTechnicalUndoFlow()
    {
        var rack = new ModularSensorRack(PlatformProfile.Car);
        rack.MountOrMoveSensor("cam_front", RackSlotKind.RoofCamera, "slot_roof_cam_a", 0.0);
        var slotState = rack.MountedSlots["slot_roof_cam_a"];
        var physMount = slotState.PhysicalTruthMount;

        // Perturb estimated mount far from truth (say +2cm lever arm error) so pre-workshop discrepancy exceeds 0
        var (train, holdout) = SynthesizeTwoPassPlanes(physMount.LeverArmBody, physMount.SensorToBody);
        var (syncTraj, syncSamples) = SynthesizeTimeSyncData(0.0185, physMount.LeverArmBody, physMount.SensorToBody, constantVelocity: false);

        rack.ApplyEstimatedCalibration(
            "slot_roof_cam_a",
            physMount.LeverArmBody + new Vector3d(0.025, -0.02, 0.015),
            Quaterniond.FromAxisAngle(Vector3d.UnitY, 0.012) * physMount.SensorToBody,
            0.0,
            1.0,
            "test.initial_misaligned");

        // Prepare matching physical time offset on rack via impact perturbation? Even cleaner: test Technical + Arcade equivalence
        var manager = new FleetProgressionManager();
        var progression = CareerProgressionState.FromSnapshot(new CareerProgressionSnapshot(
            SchemaVersion: 1,
            TotalXp: 600,
            ReputationByClient: new Dictionary<string, int>(StringComparer.Ordinal),
            ReputationByRegion: new Dictionary<string, int>(StringComparer.Ordinal),
            UnlockedStoryNodes: [],
            SettledContractIds: []));
        Assert.True(manager.TryUnlockSkillRank(SkillTrackKind.Calibration, progression, out var rank, out _));
        Assert.Equal(1, rank);

        var ledger = new CareerLedger(500);

        // Level < 3 (Scooter) blocked from offline workshop
        var blocked = CalibrationWorkshopAndSolvers.ExecuteWorkshopExtrinsicsAndSync(
            rack, highestOwnedPlatformLevel: 2, "slot_roof_cam_a", ProcessingMode.Arcade,
            manager, ledger, "tx_blocked", 5.0, train, holdout);
        Assert.False(blocked.Succeeded);
        Assert.Equal("workshop.error.requires_platform_level_3", blocked.ReasonKey);
        Assert.Equal(500, ledger.BalanceCoords);

        // Arcade workshop (Car level 3): executes full solver and bills 100 * (1 - 0.10) = 90 Coords
        var arcadeRes = CalibrationWorkshopAndSolvers.ExecuteWorkshopExtrinsicsAndSync(
            rack, highestOwnedPlatformLevel: 3, "slot_roof_cam_a", ProcessingMode.Arcade,
            manager, ledger, "tx_arcade_1", 6.0, train, holdout, syncTraj, syncSamples, arcadeBaseFeeCoords: 100);
        Assert.True(arcadeRes.Succeeded);
        Assert.Equal(90, arcadeRes.ChargedFeeCoords);
        Assert.Equal(410, ledger.BalanceCoords);
        Assert.InRange(arcadeRes.TimeSyncResult!.EstimatedTimeOffsetSeconds, 0.0183, 0.0187);

        // Technical workshop session: undo, hints, and gating when residuals exceed threshold
        rack.ApplyEstimatedCalibration(
            "slot_roof_cam_a",
            physMount.LeverArmBody + new Vector3d(0.15, 0.0, 0.0),
            physMount.SensorToBody,
            0.0,
            7.0,
            "test.bad_estimate");

        var session = new WorkshopAdjustmentSession(rack, "slot_roof_cam_a", holdout, syncTraj, syncSamples);
        Assert.True(session.CurrentValidationRmseMetres > CalibrationWorkshopAndSolvers.MaxExtrinsicsRmseMetres);
        Assert.Equal("workshop.hint.adjust_lever_arm_or_boresight", session.GetDiagnosticHintKey(rank));

        var deniedCert = session.TryCommitCertification(highestOwnedPlatformLevel: 3, timestampSeconds: 8.0);
        Assert.False(deniedCert.Succeeded);
        Assert.Equal("workshop.technical.residuals_above_threshold", deniedCert.ReasonKey);

        // Adjust to valid estimates -> verify Undo reverts cleanly -> re-apply and certify free of Arcade fee
        session.ApplyManualAdjustment(physMount.LeverArmBody, physMount.SensorToBody, 0.0185);
        Assert.True(session.CanUndo);
        Assert.True(session.CurrentValidationRmseMetres < 1e-6);

        Assert.True(session.TryUndo(out _));
        Assert.True(session.CurrentValidationRmseMetres > CalibrationWorkshopAndSolvers.MaxExtrinsicsRmseMetres);

        session.ApplyManualAdjustment(physMount.LeverArmBody, physMount.SensorToBody, 0.0185);
        var certified = session.TryCommitCertification(highestOwnedPlatformLevel: 3, timestampSeconds: 9.0);
        Assert.True(certified.Succeeded);
        Assert.Equal(0, certified.ChargedFeeCoords);
        Assert.Equal(ProcessingMode.Technical, certified.Mode);
        Assert.Equal(410, ledger.BalanceCoords);
    }
}
