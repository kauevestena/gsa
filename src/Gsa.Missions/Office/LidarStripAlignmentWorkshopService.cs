using Gsa.Core.Geometry;
using Gsa.Core.Sensors.Calibration;
using Gsa.Core.Sensors.Camera;
using Gsa.Core.Sensors.Lidar;
using Gsa.Missions.Contracts;
using Gsa.Missions.Economy;

namespace Gsa.Missions.Office;

/// <summary>Observable reference plane correspondence constructed purely from opposing/intersecting raw LiDAR swaths.</summary>
public sealed record LidarPlaneTieObservation(
    string PlaneGroupId,
    RawLidarRayMeasurement PassARay,
    RawLidarRayMeasurement PassBRay,
    Vector3d PassBPlaneNormalSensor);

/// <summary>Observable speed-varying LiDAR return tied to a surveyed static calibration marker (for Δt solving).</summary>
public sealed record LidarTimeSyncTieObservation(
    RawLidarRayMeasurement Ray,
    Vector3d SurveyedMarkerMapPoint);

/// <summary>Versioned LiDAR mount and Δt calibration recipe produced without touching physical ground-truth mounts.</summary>
public sealed record LidarCalibrationRecipe(
    string RecipeId,
    string SlotId,
    int AppliedRevision,
    Vector3d EstimatedLeverArmBody,
    Quaterniond EstimatedSensorToBody,
    double EstimatedTimeOffsetSeconds,
    double BeforePointToPlaneRmseMetres,
    double AfterPointToPlaneRmseMetres);

public sealed record LidarWorkshopAlignmentReport(
    bool Succeeded,
    ProcessingMode Mode,
    string SlotId,
    int? NewRackRevision,
    int ChargedFeeCoords,
    int BaseFeeCoords,
    double FeeDiscountFraction,
    double BeforePointToPlaneRmseMetres,
    double AfterPointToPlaneRmseMetres,
    string ReasonKey,
    LidarCalibrationRecipe? Recipe,
    IReadOnlyList<GeoreferencedLidarSample> ReprocessedSamples);

public sealed record TechnicalLidarCalibrationSnapshot(
    int StepIndex,
    Vector3d CandidateLeverArmBody,
    Quaterniond CandidateSensorToBody,
    double CandidateTimeOffsetSeconds,
    double StripDiscrepancyRmseMetres);

/// <summary>Integrates CalibrationWorkshopAndSolvers with multi-pass LiDAR swaths (Arcade &amp; Technical parity, zero truth leakage).</summary>
public static class LidarStripAlignmentWorkshopService
{
    public const int DefaultLidarWorkshopFeeCoords = 150;

    /// <summary>Derives purely sensor-frame plane correspondences between opposing passes over observable reference planes.</summary>
    public static IReadOnlyList<ExtrinsicPlaneCorrespondence> BuildPlaneCorrespondencesFromSwaths(
        EstimatedBodyTrajectory trajectory,
        IReadOnlyList<LidarPlaneTieObservation> tieObservations)
    {
        ArgumentNullException.ThrowIfNull(trajectory);
        ArgumentNullException.ThrowIfNull(tieObservations);

        var list = new List<ExtrinsicPlaneCorrespondence>(tieObservations.Count);
        foreach (var tie in tieObservations)
        {
            if (!trajectory.TrySample(tie.PassARay.RawTimestampSeconds, out var poseA) || poseA is null ||
                !trajectory.TrySample(tie.PassBRay.RawTimestampSeconds, out var poseB) || poseB is null)
            {
                continue;
            }

            list.Add(new ExtrinsicPlaneCorrespondence(
                PlaneGroupId: tie.PlaneGroupId,
                PassABodyToMap: new RigidTransform(poseA.BodyToMap, poseA.PositionEnu),
                PassAPointSensor: tie.PassARay.LocalPointLidarMetres,
                PassBBodyToMap: new RigidTransform(poseB.BodyToMap, poseB.PositionEnu),
                PassBPointSensor: tie.PassBRay.LocalPointLidarMetres,
                PassBPlaneNormalSensor: tie.PassBPlaneNormalSensor.Normalized()));
        }

        return Array.AsReadOnly(list.ToArray());
    }

    public static LidarWorkshopAlignmentReport RunArcadeAutoStripAlignment(
        ModularSensorRack rack,
        int highestOwnedPlatformLevel,
        string slotId,
        EstimatedBodyTrajectory estimatedTrajectory,
        IReadOnlyList<RawLidarRayMeasurement> allRawRays,
        IReadOnlyList<LidarPlaneTieObservation> swathTies,
        IReadOnlyList<LidarTimeSyncTieObservation>? timeSyncTies,
        FleetProgressionManager progression,
        CareerLedger ledger,
        string transactionId,
        double timestampSeconds,
        int arcadeBaseFeeCoords = DefaultLidarWorkshopFeeCoords)
    {
        ArgumentNullException.ThrowIfNull(rack);
        ArgumentException.ThrowIfNullOrWhiteSpace(slotId);
        ArgumentNullException.ThrowIfNull(estimatedTrajectory);
        ArgumentNullException.ThrowIfNull(allRawRays);
        ArgumentNullException.ThrowIfNull(swathTies);
        ArgumentNullException.ThrowIfNull(progression);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);

        if (!ModularSensorRack.IsWorkshopAccessible(highestOwnedPlatformLevel))
        {
            return FailureReport(ProcessingMode.Arcade, slotId, "workshop.error.requires_platform_level_3");
        }
        if (!rack.MountedSlots.TryGetValue(slotId, out var slotPair))
        {
            return FailureReport(ProcessingMode.Arcade, slotId, "workshop.error.slot_not_mounted");
        }

        // Read strictly the ESTIMATED mount record; never access PhysicalTruthMount.
        var initialEst = slotPair.EstimatedCalibrationMount;
        var initialTimeOffset = initialEst.TimeOffsetSeconds;
        var correspondences = BuildTimeCorrectedPlaneCorrespondences(estimatedTrajectory, swathTies, initialTimeOffset);
        if (correspondences.Count < 6)
        {
            return FailureReport(ProcessingMode.Arcade, slotId, "calibration.extrinsics.insufficient_planes");
        }

        var beforeRmse = CalibrationWorkshopAndSolvers.ComputePointToPlaneRmse(
            initialEst.LeverArmBody,
            initialEst.SensorToBody,
            correspondences);

        var discount = progression.GetCalibrationFeeReductionFraction();
        var feeToCharge = Math.Max(0, (int)Math.Round(arcadeBaseFeeCoords * (1.0 - discount), MidpointRounding.AwayFromZero));
        if (feeToCharge > 0)
        {
            var debit = ledger.TryDebitPurchaseOrService(
                transactionId,
                $"lidar_workshop:{slotId}",
                timestampSeconds,
                feeToCharge,
                "calibration_workshop",
                "workshop.lidar.strip_alignment");
            if (!debit.Succeeded)
            {
                return new LidarWorkshopAlignmentReport(
                    Succeeded: false,
                    Mode: ProcessingMode.Arcade,
                    SlotId: slotId,
                    NewRackRevision: null,
                    ChargedFeeCoords: 0,
                    BaseFeeCoords: arcadeBaseFeeCoords,
                    FeeDiscountFraction: discount,
                    BeforePointToPlaneRmseMetres: beforeRmse,
                    AfterPointToPlaneRmseMetres: beforeRmse,
                    ReasonKey: debit.ReasonKey ?? "ledger.error.insufficient_funds",
                    Recipe: null,
                    ReprocessedSamples: Array.Empty<GeoreferencedLidarSample>());
            }
        }

        var workingLeverArm = initialEst.LeverArmBody;
        var workingSensorToBody = initialEst.SensorToBody;
        var workingTimeOffset = initialTimeOffset;

        // When both extrinsics and timing ties are present, jointly solve 7-DOF (lever arm, boresight, Δt) first
        // so cross-coupling (> 5 cm) does not block single-stage solver acceptance thresholds.
        if (timeSyncTies is { Count: >= 3 })
        {
            var joint = SolveJointExtrinsicsAndTimeOffset(
                estimatedTrajectory,
                swathTies,
                timeSyncTies,
                workingLeverArm,
                workingSensorToBody,
                workingTimeOffset);
            if (joint.Succeeded)
            {
                workingLeverArm = joint.LeverArmBody;
                workingSensorToBody = joint.SensorToBody;
                workingTimeOffset = joint.TimeOffsetSeconds;
            }
        }

        correspondences = BuildTimeCorrectedPlaneCorrespondences(estimatedTrajectory, swathTies, workingTimeOffset);
        var extResult = CalibrationWorkshopAndSolvers.CalibrateExtrinsicsPointToPlane(
            workingLeverArm,
            workingSensorToBody,
            correspondences);

        if (!extResult.Succeeded)
        {
            return new LidarWorkshopAlignmentReport(
                Succeeded: false,
                Mode: ProcessingMode.Arcade,
                SlotId: slotId,
                NewRackRevision: null,
                ChargedFeeCoords: feeToCharge,
                BaseFeeCoords: arcadeBaseFeeCoords,
                FeeDiscountFraction: discount,
                BeforePointToPlaneRmseMetres: beforeRmse,
                AfterPointToPlaneRmseMetres: beforeRmse,
                ReasonKey: extResult.ReasonKey,
                Recipe: null,
                ReprocessedSamples: Array.Empty<GeoreferencedLidarSample>());
        }

        var estTimeOffset = workingTimeOffset;
        if (timeSyncTies is { Count: >= 3 })
        {
            var syncObs = timeSyncTies
                .Select(t => new TimeSyncCorrespondence(t.Ray.RawTimestampSeconds, t.Ray.LocalPointLidarMetres, t.SurveyedMarkerMapPoint))
                .ToArray();
            var syncResult = CalibrationWorkshopAndSolvers.CalibrateTimeOffset(
                estimatedTrajectory,
                extResult.EstimatedLeverArmBody,
                extResult.EstimatedSensorToBody,
                syncObs,
                workingTimeOffset);

            if (!syncResult.Succeeded)
            {
                return new LidarWorkshopAlignmentReport(
                    Succeeded: false,
                    Mode: ProcessingMode.Arcade,
                    SlotId: slotId,
                    NewRackRevision: null,
                    ChargedFeeCoords: feeToCharge,
                    BaseFeeCoords: arcadeBaseFeeCoords,
                    FeeDiscountFraction: discount,
                    BeforePointToPlaneRmseMetres: beforeRmse,
                    AfterPointToPlaneRmseMetres: beforeRmse,
                    ReasonKey: syncResult.ReasonKey,
                    Recipe: null,
                    ReprocessedSamples: Array.Empty<GeoreferencedLidarSample>());
            }

            estTimeOffset = syncResult.EstimatedTimeOffsetSeconds;

            var correctedCorrespondences = BuildTimeCorrectedPlaneCorrespondences(estimatedTrajectory, swathTies, estTimeOffset);
            if (correctedCorrespondences.Count >= 6)
            {
                var refinedExt = CalibrationWorkshopAndSolvers.CalibrateExtrinsicsPointToPlane(
                    extResult.EstimatedLeverArmBody,
                    extResult.EstimatedSensorToBody,
                    correctedCorrespondences);
                if (refinedExt.Succeeded)
                {
                    extResult = refinedExt;
                    correspondences = correctedCorrespondences;
                }
            }
        }

        var finalRmse = CalibrationWorkshopAndSolvers.ComputePointToPlaneRmse(
            extResult.EstimatedLeverArmBody,
            extResult.EstimatedSensorToBody,
            timeSyncTies is { Count: >= 3 }
                ? BuildTimeCorrectedPlaneCorrespondences(estimatedTrajectory, swathTies, estTimeOffset)
                : correspondences);

        var updatedSlot = rack.ApplyEstimatedCalibration(
            slotId,
            extResult.EstimatedLeverArmBody,
            extResult.EstimatedSensorToBody,
            estTimeOffset,
            timestampSeconds,
            "rack.revision.lidar_strip_aligned");

        var reprocessed = ReprocessWithMount(
            allRawRays,
            estimatedTrajectory,
            extResult.EstimatedLeverArmBody,
            extResult.EstimatedSensorToBody,
            estTimeOffset);

        var recipe = new LidarCalibrationRecipe(
            RecipeId: $"lidar_cal_{slotId}_r{updatedSlot.Revision}",
            SlotId: slotId,
            AppliedRevision: updatedSlot.Revision,
            EstimatedLeverArmBody: extResult.EstimatedLeverArmBody,
            EstimatedSensorToBody: extResult.EstimatedSensorToBody,
            EstimatedTimeOffsetSeconds: estTimeOffset,
            BeforePointToPlaneRmseMetres: beforeRmse,
            AfterPointToPlaneRmseMetres: finalRmse);

        return new LidarWorkshopAlignmentReport(
            Succeeded: true,
            Mode: ProcessingMode.Arcade,
            SlotId: slotId,
            NewRackRevision: updatedSlot.Revision,
            ChargedFeeCoords: feeToCharge,
            BaseFeeCoords: arcadeBaseFeeCoords,
            FeeDiscountFraction: discount,
            BeforePointToPlaneRmseMetres: beforeRmse,
            AfterPointToPlaneRmseMetres: finalRmse,
            ReasonKey: "workshop.lidar.aligned",
            Recipe: recipe,
            ReprocessedSamples: reprocessed);
    }

    public static LidarWorkshopAlignmentReport ApplyTechnicalCalibrationParameters(
        ModularSensorRack rack,
        int highestOwnedPlatformLevel,
        string slotId,
        EstimatedBodyTrajectory estimatedTrajectory,
        IReadOnlyList<RawLidarRayMeasurement> allRawRays,
        IReadOnlyList<LidarPlaneTieObservation> swathTies,
        Vector3d manualLeverArmBody,
        Quaterniond manualSensorToBody,
        double manualTimeOffsetSeconds,
        double timestampSeconds,
        double maxAllowedRmseMetres = CalibrationWorkshopAndSolvers.MaxExtrinsicsRmseMetres)
    {
        ArgumentNullException.ThrowIfNull(rack);
        ArgumentException.ThrowIfNullOrWhiteSpace(slotId);
        ArgumentNullException.ThrowIfNull(estimatedTrajectory);
        ArgumentNullException.ThrowIfNull(allRawRays);
        ArgumentNullException.ThrowIfNull(swathTies);

        if (!ModularSensorRack.IsWorkshopAccessible(highestOwnedPlatformLevel))
            return FailureReport(ProcessingMode.Technical, slotId, "workshop.error.requires_platform_level_3");
        if (!rack.MountedSlots.TryGetValue(slotId, out var slotPair))
            return FailureReport(ProcessingMode.Technical, slotId, "workshop.error.slot_not_mounted");

        var initialEst = slotPair.EstimatedCalibrationMount;
        var beforeCorresp = BuildTimeCorrectedPlaneCorrespondences(estimatedTrajectory, swathTies, initialEst.TimeOffsetSeconds);
        var beforeRmse = CalibrationWorkshopAndSolvers.ComputePointToPlaneRmse(
            initialEst.LeverArmBody,
            initialEst.SensorToBody,
            beforeCorresp);

        var candidateCorresp = BuildTimeCorrectedPlaneCorrespondences(estimatedTrajectory, swathTies, manualTimeOffsetSeconds);
        var candidateRmse = CalibrationWorkshopAndSolvers.ComputePointToPlaneRmse(
            manualLeverArmBody,
            manualSensorToBody,
            candidateCorresp);

        if (!double.IsFinite(candidateRmse) || candidateRmse > maxAllowedRmseMetres)
        {
            return new LidarWorkshopAlignmentReport(
                Succeeded: false,
                Mode: ProcessingMode.Technical,
                SlotId: slotId,
                NewRackRevision: null,
                ChargedFeeCoords: 0,
                BaseFeeCoords: 0,
                FeeDiscountFraction: 0.0,
                BeforePointToPlaneRmseMetres: beforeRmse,
                AfterPointToPlaneRmseMetres: candidateRmse,
                ReasonKey: "calibration.extrinsics.rmse_exceeded",
                Recipe: null,
                ReprocessedSamples: Array.Empty<GeoreferencedLidarSample>());
        }

        var updatedSlot = rack.ApplyEstimatedCalibration(
            slotId,
            manualLeverArmBody,
            manualSensorToBody,
            manualTimeOffsetSeconds,
            timestampSeconds,
            "rack.revision.lidar_technical_aligned");

        var reprocessed = ReprocessWithMount(
            allRawRays,
            estimatedTrajectory,
            manualLeverArmBody,
            manualSensorToBody,
            manualTimeOffsetSeconds);

        var recipe = new LidarCalibrationRecipe(
            RecipeId: $"lidar_cal_{slotId}_r{updatedSlot.Revision}",
            SlotId: slotId,
            AppliedRevision: updatedSlot.Revision,
            EstimatedLeverArmBody: manualLeverArmBody,
            EstimatedSensorToBody: manualSensorToBody,
            EstimatedTimeOffsetSeconds: manualTimeOffsetSeconds,
            BeforePointToPlaneRmseMetres: beforeRmse,
            AfterPointToPlaneRmseMetres: candidateRmse);

        return new LidarWorkshopAlignmentReport(
            Succeeded: true,
            Mode: ProcessingMode.Technical,
            SlotId: slotId,
            NewRackRevision: updatedSlot.Revision,
            ChargedFeeCoords: 0,
            BaseFeeCoords: 0,
            FeeDiscountFraction: 0.0,
            BeforePointToPlaneRmseMetres: beforeRmse,
            AfterPointToPlaneRmseMetres: candidateRmse,
            ReasonKey: "workshop.lidar.aligned",
            Recipe: recipe,
            ReprocessedSamples: reprocessed);
    }

    public static IReadOnlyList<GeoreferencedLidarSample> ReprocessWithRecipe(
        IReadOnlyList<RawLidarRayMeasurement> rawRays,
        EstimatedBodyTrajectory trajectory,
        LidarCalibrationRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(rawRays);
        ArgumentNullException.ThrowIfNull(trajectory);
        ArgumentNullException.ThrowIfNull(recipe);
        return ReprocessWithMount(
            rawRays,
            trajectory,
            recipe.EstimatedLeverArmBody,
            recipe.EstimatedSensorToBody,
            recipe.EstimatedTimeOffsetSeconds);
    }

    private static IReadOnlyList<GeoreferencedLidarSample> ReprocessWithMount(
        IReadOnlyList<RawLidarRayMeasurement> rawRays,
        EstimatedBodyTrajectory trajectory,
        Vector3d leverArmBody,
        Quaterniond sensorToBody,
        double timeOffsetSeconds)
    {
        var mount = new LidarMountEstimate(leverArmBody, sensorToBody, timeOffsetSeconds);
        return LidarRayGeoreferencer.GeoreferenceRawRays(rawRays, trajectory, mount).Samples;
    }

    private static IReadOnlyList<ExtrinsicPlaneCorrespondence> BuildTimeCorrectedPlaneCorrespondences(
        EstimatedBodyTrajectory trajectory,
        IReadOnlyList<LidarPlaneTieObservation> swathTies,
        double timeOffsetSeconds)
    {
        var list = new List<ExtrinsicPlaneCorrespondence>(swathTies.Count);
        foreach (var tie in swathTies)
        {
            var tA = tie.PassARay.RawTimestampSeconds + timeOffsetSeconds;
            var tB = tie.PassBRay.RawTimestampSeconds + timeOffsetSeconds;
            if (!trajectory.TrySample(tA, out var poseA) || poseA is null ||
                !trajectory.TrySample(tB, out var poseB) || poseB is null)
            {
                continue;
            }

            list.Add(new ExtrinsicPlaneCorrespondence(
                PlaneGroupId: tie.PlaneGroupId,
                PassABodyToMap: new RigidTransform(poseA.BodyToMap, poseA.PositionEnu),
                PassAPointSensor: tie.PassARay.LocalPointLidarMetres,
                PassBBodyToMap: new RigidTransform(poseB.BodyToMap, poseB.PositionEnu),
                PassBPointSensor: tie.PassBRay.LocalPointLidarMetres,
                PassBPlaneNormalSensor: tie.PassBPlaneNormalSensor.Normalized()));
        }

        return Array.AsReadOnly(list.ToArray());
    }

    private static (bool Succeeded, Vector3d LeverArmBody, Quaterniond SensorToBody, double TimeOffsetSeconds) SolveJointExtrinsicsAndTimeOffset(
        EstimatedBodyTrajectory trajectory,
        IReadOnlyList<LidarPlaneTieObservation> swathTies,
        IReadOnlyList<LidarTimeSyncTieObservation> timeSyncTies,
        Vector3d initialLeverArmBody,
        Quaterniond initialSensorToBody,
        double initialTimeOffsetSeconds)
    {
        double[] start =
        [
            initialLeverArmBody.X,
            initialLeverArmBody.Y,
            initialLeverArmBody.Z,
            0.0,
            0.0,
            0.0,
            Math.Clamp(initialTimeOffsetSeconds, CalibrationWorkshopAndSolvers.MinTimeOffsetSeconds, CalibrationWorkshopAndSolvers.MaxTimeOffsetSeconds),
        ];

        bool Validate(double[] p) =>
            p.All(double.IsFinite) &&
            Math.Abs(p[0]) < 10.0 && Math.Abs(p[1]) < 10.0 && Math.Abs(p[2]) < 10.0 &&
            Math.Abs(p[3]) < 1.0 && Math.Abs(p[4]) < 1.0 && Math.Abs(p[5]) < 1.0 &&
            p[6] >= CalibrationWorkshopAndSolvers.MinTimeOffsetSeconds - 1e-4 &&
            p[6] <= CalibrationWorkshopAndSolvers.MaxTimeOffsetSeconds + 1e-4;

        Quaterniond ApplyIncremental(Quaterniond baseRot, double rx, double ry, double rz)
        {
            var w = new Vector3d(rx, ry, rz);
            var theta = w.Length;
            if (theta < 1e-12)
                return baseRot;
            return Quaterniond.FromAxisAngle(w / theta, theta) * baseRot;
        }

        double[]? Evaluate(double[] p)
        {
            var lever = new Vector3d(p[0], p[1], p[2]);
            var rot = ApplyIncremental(initialSensorToBody, p[3], p[4], p[5]);
            var dt = p[6];
            var residuals = new List<double>(swathTies.Count + timeSyncTies.Count * 3);

            foreach (var tie in swathTies)
            {
                if (!trajectory.TrySample(tie.PassARay.RawTimestampSeconds + dt, out var poseA) || poseA is null ||
                    !trajectory.TrySample(tie.PassBRay.RawTimestampSeconds + dt, out var poseB) || poseB is null)
                {
                    return null;
                }

                var xA = new RigidTransform(poseA.BodyToMap, poseA.PositionEnu).Transform(lever + rot.Rotate(tie.PassARay.LocalPointLidarMetres));
                var xB = new RigidTransform(poseB.BodyToMap, poseB.PositionEnu).Transform(lever + rot.Rotate(tie.PassBRay.LocalPointLidarMetres));
                var nMap = poseB.BodyToMap.Rotate(rot.Rotate(tie.PassBPlaneNormalSensor.Normalized())).Normalized();
                residuals.Add(Vector3d.Dot(nMap, xA - xB));
            }

            foreach (var sync in timeSyncTies)
            {
                if (!trajectory.TrySample(sync.Ray.RawTimestampSeconds + dt, out var pose) || pose is null)
                    return null;
                var predicted = new RigidTransform(pose.BodyToMap, pose.PositionEnu).Transform(lever + rot.Rotate(sync.Ray.LocalPointLidarMetres));
                var err = predicted - sync.SurveyedMarkerMapPoint;
                residuals.Add(err.X);
                residuals.Add(err.Y);
                residuals.Add(err.Z);
            }

            return residuals.ToArray();
        }

        var lm = DampedLeastSquaresSolver.Solve(start, Evaluate, Validate);
        if (lm.TerminatedWithNonFinite)
            return (false, initialLeverArmBody, initialSensorToBody, initialTimeOffsetSeconds);

        return (
            true,
            new Vector3d(lm.Parameters[0], lm.Parameters[1], lm.Parameters[2]),
            ApplyIncremental(initialSensorToBody, lm.Parameters[3], lm.Parameters[4], lm.Parameters[5]),
            lm.Parameters[6]);
    }

    private static LidarWorkshopAlignmentReport FailureReport(ProcessingMode mode, string slotId, string reasonKey) =>
        new(
            Succeeded: false,
            Mode: mode,
            SlotId: slotId,
            NewRackRevision: null,
            ChargedFeeCoords: 0,
            BaseFeeCoords: 0,
            FeeDiscountFraction: 0.0,
            BeforePointToPlaneRmseMetres: double.PositiveInfinity,
            AfterPointToPlaneRmseMetres: double.PositiveInfinity,
            ReasonKey: reasonKey,
            Recipe: null,
            ReprocessedSamples: Array.Empty<GeoreferencedLidarSample>());
}

/// <summary>Interactive Technical LiDAR calibration session with step-by-step strip residual hints and undo stack (no truth leakage).</summary>
public sealed class TechnicalLidarCalibrationSession
{
    private readonly EstimatedBodyTrajectory _trajectory;
    private readonly IReadOnlyList<LidarPlaneTieObservation> _swathTies;
    private readonly Stack<TechnicalLidarCalibrationSnapshot> _undoStack = new();

    public Vector3d CurrentLeverArmBody { get; private set; }
    public Quaterniond CurrentSensorToBody { get; private set; }
    public double CurrentTimeOffsetSeconds { get; private set; }
    public double CurrentStripDiscrepancyRmseMetres { get; private set; }
    public bool CanUndo => _undoStack.Count > 0;
    public int UndoDepth => _undoStack.Count;

    public TechnicalLidarCalibrationSession(
        ModularSensorRack rack,
        string slotId,
        EstimatedBodyTrajectory estimatedTrajectory,
        IReadOnlyList<LidarPlaneTieObservation> swathTies)
    {
        ArgumentNullException.ThrowIfNull(rack);
        ArgumentException.ThrowIfNullOrWhiteSpace(slotId);
        ArgumentNullException.ThrowIfNull(estimatedTrajectory);
        ArgumentNullException.ThrowIfNull(swathTies);
        if (!rack.MountedSlots.TryGetValue(slotId, out var pair))
            throw new KeyNotFoundException($"No sensor mounted in slot '{slotId}'.");

        _trajectory = estimatedTrajectory;
        _swathTies = swathTies;

        var init = pair.EstimatedCalibrationMount;
        CurrentLeverArmBody = init.LeverArmBody;
        CurrentSensorToBody = init.SensorToBody;
        CurrentTimeOffsetSeconds = init.TimeOffsetSeconds;
        CurrentStripDiscrepancyRmseMetres = EvaluateCurrentDiscrepancy();
    }

    public TechnicalLidarCalibrationSnapshot PreviewAndApplyCandidate(
        Vector3d candidateLeverArmBody,
        Quaterniond candidateSensorToBody,
        double candidateTimeOffsetSeconds)
    {
        _undoStack.Push(new TechnicalLidarCalibrationSnapshot(
            _undoStack.Count,
            CurrentLeverArmBody,
            CurrentSensorToBody,
            CurrentTimeOffsetSeconds,
            CurrentStripDiscrepancyRmseMetres));

        CurrentLeverArmBody = candidateLeverArmBody;
        CurrentSensorToBody = candidateSensorToBody;
        CurrentTimeOffsetSeconds = candidateTimeOffsetSeconds;
        CurrentStripDiscrepancyRmseMetres = EvaluateCurrentDiscrepancy();

        return new TechnicalLidarCalibrationSnapshot(
            _undoStack.Count,
            CurrentLeverArmBody,
            CurrentSensorToBody,
            CurrentTimeOffsetSeconds,
            CurrentStripDiscrepancyRmseMetres);
    }

    public bool TryUndo(out TechnicalLidarCalibrationSnapshot? restored)
    {
        if (_undoStack.Count == 0)
        {
            restored = null;
            return false;
        }

        restored = _undoStack.Pop();
        CurrentLeverArmBody = restored.CandidateLeverArmBody;
        CurrentSensorToBody = restored.CandidateSensorToBody;
        CurrentTimeOffsetSeconds = restored.CandidateTimeOffsetSeconds;
        CurrentStripDiscrepancyRmseMetres = restored.StripDiscrepancyRmseMetres;
        return true;
    }

    public string GetDiagnosticHintKey()
    {
        if (CurrentStripDiscrepancyRmseMetres <= 0.015)
            return "workshop.hint.residuals_converged";
        if (Math.Abs(CurrentTimeOffsetSeconds) < 1e-6 && CurrentStripDiscrepancyRmseMetres > 0.05)
            return "workshop.hint.check_boresight_or_timesync";
        return "workshop.hint.refine_lever_arm_or_boresight";
    }

    private double EvaluateCurrentDiscrepancy()
    {
        var list = new List<ExtrinsicPlaneCorrespondence>(_swathTies.Count);
        foreach (var tie in _swathTies)
        {
            if (!_trajectory.TrySample(tie.PassARay.RawTimestampSeconds + CurrentTimeOffsetSeconds, out var poseA) || poseA is null ||
                !_trajectory.TrySample(tie.PassBRay.RawTimestampSeconds + CurrentTimeOffsetSeconds, out var poseB) || poseB is null)
            {
                continue;
            }

            list.Add(new ExtrinsicPlaneCorrespondence(
                tie.PlaneGroupId,
                new RigidTransform(poseA.BodyToMap, poseA.PositionEnu),
                tie.PassARay.LocalPointLidarMetres,
                new RigidTransform(poseB.BodyToMap, poseB.PositionEnu),
                tie.PassBRay.LocalPointLidarMetres,
                tie.PassBPlaneNormalSensor.Normalized()));
        }

        return CalibrationWorkshopAndSolvers.ComputePointToPlaneRmse(
            CurrentLeverArmBody,
            CurrentSensorToBody,
            list);
    }
}
