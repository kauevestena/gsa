using Gsa.Core.Geometry;
using Gsa.Core.Sensors.Calibration;
using Gsa.Core.Sensors.Camera;
using Gsa.Missions.Economy;

namespace Gsa.Missions.Office;

public sealed record LevenbergMarquardtResult(
    bool Converged,
    bool TerminatedWithNonFinite,
    int IterationsExecuted,
    int AcceptedSteps,
    double InitialCost,
    double FinalCost,
    double InitialRms,
    double FinalRms,
    double FinalLambda,
    double[] Parameters,
    string ReasonKey);

/// <summary>Finite-difference Levenberg-Marquardt nonlinear least-squares solver (max 50 iters, lambda0=1e-3).</summary>
public static class DampedLeastSquaresSolver
{
    public const int MaxIterations = 50;
    public const double FiniteDifferenceRelativeStep = 1e-6;
    public const double FiniteDifferenceMinStep = 1e-8;
    public const double InitialDampingLambda = 1e-3;
    public const double LambdaIncreaseFactor = 10.0;
    public const double LambdaDecreaseFactor = 10.0;
    public const double ConvergenceRelativeCostTolerance = 1e-8;
    public const double MaxDampingLambda = 1e12;

    public static LevenbergMarquardtResult Solve(
        double[] initialParameters,
        Func<double[], double[]?> residualEvaluator,
        Func<double[], bool>? parameterValidator = null,
        int maxIterations = MaxIterations)
    {
        ArgumentNullException.ThrowIfNull(initialParameters);
        ArgumentNullException.ThrowIfNull(residualEvaluator);
        if (initialParameters.Length == 0)
            throw new ArgumentException("At least one parameter is required.", nameof(initialParameters));
        if (maxIterations <= 0 || maxIterations > MaxIterations)
            throw new ArgumentOutOfRangeException(nameof(maxIterations), $"Max iterations must be in [1, {MaxIterations}].");

        var parameters = (double[])initialParameters.Clone();
        if (parameters.Any(p => !double.IsFinite(p)) || (parameterValidator is not null && !parameterValidator(parameters)))
            throw new ArgumentException("Initial parameters must be finite and satisfy validator constraints.", nameof(initialParameters));

        var residuals = residualEvaluator(parameters);
        if (residuals is null || residuals.Length == 0 || residuals.Any(r => !double.IsFinite(r)))
            throw new ArgumentException("Initial residual vector must be non-empty and finite.", nameof(initialParameters));

        var initialCost = ComputeHalfSumSquares(residuals);
        var initialRms = ComputeRms(residuals);
        var currentCost = initialCost;
        var lambda = InitialDampingLambda;
        var iterations = 0;
        var acceptedSteps = 0;
        var reasonKey = "calibration.lm.max_iterations_reached";
        var converged = false;

        var m = residuals.Length;
        var k = parameters.Length;

        for (var iter = 0; iter < maxIterations; iter++)
        {
            iterations = iter + 1;

            if (currentCost <= 1e-24)
            {
                converged = true;
                reasonKey = "calibration.lm.converged_zero_cost";
                break;
            }

            if (!TryBuildFiniteDifferenceJacobian(parameters, residuals, residualEvaluator, parameterValidator, out var jacobian))
            {
                return new LevenbergMarquardtResult(
                    Converged: false,
                    TerminatedWithNonFinite: true,
                    IterationsExecuted: iterations,
                    AcceptedSteps: acceptedSteps,
                    InitialCost: initialCost,
                    FinalCost: currentCost,
                    InitialRms: initialRms,
                    FinalRms: ComputeRms(residuals),
                    FinalLambda: lambda,
                    Parameters: parameters,
                    ReasonKey: "calibration.lm.non_finite_jacobian");
            }

            BuildNormalEquations(jacobian, residuals, m, k, out var jtj, out var jtr);
            var stepAcceptedInIter = false;
            var nonFiniteStep = false;

            // Try damping factors within remaining/inner retries without blowing outer maxIterations
            while (lambda <= MaxDampingLambda)
            {
                var damped = new double[k, k];
                var rhs = new double[k];
                for (var i = 0; i < k; i++)
                {
                    rhs[i] = -jtr[i];
                    for (var j = 0; j < k; j++)
                        damped[i, j] = jtj[i, j];
                    var scale = Math.Max(1.0, jtj[i, i]);
                    damped[i, i] += lambda * scale;
                }

                if (!TrySolveLinearSystem(damped, rhs, out var delta) || delta.Any(d => !double.IsFinite(d)))
                {
                    lambda *= LambdaIncreaseFactor;
                    continue;
                }

                var candidate = new double[k];
                for (var j = 0; j < k; j++)
                    candidate[j] = parameters[j] + delta[j];

                if (candidate.Any(c => !double.IsFinite(c)) || (parameterValidator is not null && !parameterValidator(candidate)))
                {
                    lambda *= LambdaIncreaseFactor;
                    continue;
                }

                var nextResiduals = residualEvaluator(candidate);
                if (nextResiduals is null || nextResiduals.Length != m || nextResiduals.Any(r => !double.IsFinite(r)))
                {
                    nonFiniteStep = true;
                    lambda *= LambdaIncreaseFactor;
                    continue;
                }

                var nextCost = ComputeHalfSumSquares(nextResiduals);
                if (!double.IsFinite(nextCost))
                {
                    nonFiniteStep = true;
                    lambda *= LambdaIncreaseFactor;
                    continue;
                }

                if (nextCost < currentCost)
                {
                    var relImprovement = Math.Abs(currentCost - nextCost) / Math.Max(1.0, currentCost);
                    parameters = candidate;
                    residuals = nextResiduals;
                    currentCost = nextCost;
                    acceptedSteps++;
                    lambda = Math.Max(1e-12, lambda / LambdaDecreaseFactor);
                    stepAcceptedInIter = true;

                    if (relImprovement < ConvergenceRelativeCostTolerance)
                    {
                        converged = true;
                        reasonKey = "calibration.lm.converged_relative_cost";
                    }
                    break;
                }

                lambda *= LambdaIncreaseFactor;
            }

            if (converged)
                break;

            if (!stepAcceptedInIter)
            {
                reasonKey = nonFiniteStep
                    ? "calibration.lm.damping_non_finite"
                    : "calibration.lm.damping_exhausted";
                break;
            }
        }

        return new LevenbergMarquardtResult(
            Converged: converged,
            TerminatedWithNonFinite: false,
            IterationsExecuted: iterations,
            AcceptedSteps: acceptedSteps,
            InitialCost: initialCost,
            FinalCost: currentCost,
            InitialRms: initialRms,
            FinalRms: ComputeRms(residuals),
            FinalLambda: lambda,
            Parameters: parameters,
            ReasonKey: reasonKey);
    }

    public static bool TryComputeNormalEquations(
        double[] parameters,
        Func<double[], double[]?> residualEvaluator,
        Func<double[], bool>? parameterValidator,
        out double[,] jtj,
        out double[] residuals)
    {
        jtj = new double[0, 0];
        residuals = residualEvaluator(parameters) ?? [];
        if (residuals.Length == 0 || residuals.Any(r => !double.IsFinite(r)))
            return false;

        if (!TryBuildFiniteDifferenceJacobian(parameters, residuals, residualEvaluator, parameterValidator, out var jacobian))
            return false;

        BuildNormalEquations(jacobian, residuals, residuals.Length, parameters.Length, out jtj, out _);
        return true;
    }

    private static bool TryBuildFiniteDifferenceJacobian(
        double[] parameters,
        double[] baseResiduals,
        Func<double[], double[]?> residualEvaluator,
        Func<double[], bool>? parameterValidator,
        out double[,] jacobian)
    {
        var m = baseResiduals.Length;
        var k = parameters.Length;
        jacobian = new double[m, k];
        var perturbedPlus = (double[])parameters.Clone();
        var perturbedMinus = (double[])parameters.Clone();

        for (var j = 0; j < k; j++)
        {
            var step = Math.Max(FiniteDifferenceMinStep, FiniteDifferenceRelativeStep * Math.Abs(parameters[j]));
            perturbedPlus[j] = parameters[j] + step;
            perturbedMinus[j] = parameters[j] - step;

            var plusValid = parameterValidator is null || parameterValidator(perturbedPlus);
            var minusValid = parameterValidator is null || parameterValidator(perturbedMinus);

            var rPlus = plusValid ? residualEvaluator(perturbedPlus) : null;
            var rMinus = minusValid ? residualEvaluator(perturbedMinus) : null;

            perturbedPlus[j] = parameters[j];
            perturbedMinus[j] = parameters[j];

            if (rPlus is not null && rPlus.Length == m && rPlus.All(double.IsFinite) &&
                rMinus is not null && rMinus.Length == m && rMinus.All(double.IsFinite))
            {
                var inv2Step = 0.5 / step;
                for (var i = 0; i < m; i++)
                    jacobian[i, j] = (rPlus[i] - rMinus[i]) * inv2Step;
            }
            else if (rPlus is not null && rPlus.Length == m && rPlus.All(double.IsFinite))
            {
                var invStep = 1.0 / step;
                for (var i = 0; i < m; i++)
                    jacobian[i, j] = (rPlus[i] - baseResiduals[i]) * invStep;
            }
            else if (rMinus is not null && rMinus.Length == m && rMinus.All(double.IsFinite))
            {
                var invStep = 1.0 / step;
                for (var i = 0; i < m; i++)
                    jacobian[i, j] = (baseResiduals[i] - rMinus[i]) * invStep;
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    private static void BuildNormalEquations(double[,] jacobian, double[] residuals, int m, int k, out double[,] jtj, out double[] jtr)
    {
        jtj = new double[k, k];
        jtr = new double[k];

        for (var i = 0; i < m; i++)
        {
            var ri = residuals[i];
            for (var c = 0; c < k; c++)
            {
                var jic = jacobian[i, c];
                jtr[c] += jic * ri;
                for (var d = c; d < k; d++)
                    jtj[c, d] += jic * jacobian[i, d];
            }
        }

        for (var c = 0; c < k; c++)
        {
            for (var d = 0; d < c; d++)
                jtj[c, d] = jtj[d, c];
        }
    }

    public static bool TrySolveLinearSystem(double[,] matrix, double[] rhs, out double[] solution)
    {
        var n = rhs.Length;
        solution = new double[n];
        var a = (double[,])matrix.Clone();
        var b = (double[])rhs.Clone();

        for (var col = 0; col < n; col++)
        {
            var pivotRow = col;
            var bestAbs = Math.Abs(a[col, col]);
            for (var row = col + 1; row < n; row++)
            {
                var candidateAbs = Math.Abs(a[row, col]);
                if (candidateAbs > bestAbs)
                {
                    bestAbs = candidateAbs;
                    pivotRow = row;
                }
            }

            if (bestAbs < 1e-14 || !double.IsFinite(bestAbs))
                return false;

            if (pivotRow != col)
            {
                for (var j = col; j < n; j++)
                    (a[col, j], a[pivotRow, j]) = (a[pivotRow, j], a[col, j]);
                (b[col], b[pivotRow]) = (b[pivotRow], b[col]);
            }

            var pivot = a[col, col];
            for (var row = col + 1; row < n; row++)
            {
                var factor = a[row, col] / pivot;
                if (factor == 0.0)
                    continue;
                for (var j = col; j < n; j++)
                    a[row, j] -= factor * a[col, j];
                b[row] -= factor * b[col];
            }
        }

        for (var row = n - 1; row >= 0; row--)
        {
            var sum = b[row];
            for (var j = row + 1; j < n; j++)
                sum -= a[row, j] * solution[j];
            solution[row] = sum / a[row, row];
        }

        return true;
    }

    public static double[]? ComputePivotSpectrum(double[,] matrix)
    {
        var n = matrix.GetLength(0);
        if (matrix.GetLength(1) != n)
            return null;

        var a = (double[,])matrix.Clone();
        var pivots = new double[n];

        for (var col = 0; col < n; col++)
        {
            var pivotRow = col;
            var bestAbs = Math.Abs(a[col, col]);
            for (var row = col + 1; row < n; row++)
            {
                var candidateAbs = Math.Abs(a[row, col]);
                if (candidateAbs > bestAbs)
                {
                    bestAbs = candidateAbs;
                    pivotRow = row;
                }
            }

            pivots[col] = bestAbs;
            if (bestAbs < 1e-16 || !double.IsFinite(bestAbs))
                return pivots;

            if (pivotRow != col)
            {
                for (var j = col; j < n; j++)
                    (a[col, j], a[pivotRow, j]) = (a[pivotRow, j], a[col, j]);
            }

            var pivot = a[col, col];
            for (var row = col + 1; row < n; row++)
            {
                var factor = a[row, col] / pivot;
                for (var j = col; j < n; j++)
                    a[row, j] -= factor * a[col, j];
            }
        }

        return pivots;
    }

    private static double ComputeHalfSumSquares(double[] residuals)
    {
        var sum = 0.0;
        for (var i = 0; i < residuals.Length; i++)
            sum += residuals[i] * residuals[i];
        return 0.5 * sum;
    }

    private static double ComputeRms(double[] residuals)
    {
        var sum = 0.0;
        for (var i = 0; i < residuals.Length; i++)
            sum += residuals[i] * residuals[i];
        return Math.Sqrt(sum / residuals.Length);
    }
}

public sealed record BoardCornerObservation(
    Vector3d PointInBoardFrame,
    double ObservedU,
    double ObservedV);

public sealed record CalibrationBoardPose(
    string PoseId,
    RigidTransform BoardToCamera,
    IReadOnlyList<BoardCornerObservation> Observations,
    bool IsValidationHoldout);

public sealed record IntrinsicsCalibrationResult(
    bool Succeeded,
    CameraIntrinsics EstimatedIntrinsics,
    int TrainingPoseCount,
    int ValidationPoseCount,
    double TrainingRmsePixels,
    double ValidationHeldOutRmsePixels,
    int IterationsExecuted,
    string ReasonKey);

public sealed record ExtrinsicPlaneCorrespondence(
    string PlaneGroupId,
    RigidTransform PassABodyToMap,
    Vector3d PassAPointSensor,
    RigidTransform PassBBodyToMap,
    Vector3d PassBPointSensor,
    Vector3d PassBPlaneNormalSensor);

public sealed record ExtrinsicsCalibrationResult(
    bool Succeeded,
    Vector3d EstimatedLeverArmBody,
    Quaterniond EstimatedSensorToBody,
    double TrainingPointToPlaneRmseMetres,
    double ValidationHeldOutRmseMetres,
    double PlaneNormalDeterminant,
    int DistinctPlaneGroupCount,
    int IterationsExecuted,
    string ReasonKey);

public sealed record TimeSyncCorrespondence(
    double RawSensorTimeSeconds,
    Vector3d PointSensor,
    Vector3d KnownTargetMapPoint);

public sealed record TimeOffsetCalibrationResult(
    bool Succeeded,
    double EstimatedTimeOffsetSeconds,
    double CoarseOffsetSeconds,
    double ResidualRmseMetres,
    double SpeedSpanMetresPerSecond,
    double RotationSpanRadians,
    int IterationsEvaluated,
    string ReasonKey);

public sealed record WorkshopCalibrationResult(
    bool Succeeded,
    ProcessingMode Mode,
    string SlotId,
    int ChargedFeeCoords,
    int BaseFeeCoords,
    double FeeDiscountFraction,
    int? NewRackRevision,
    double ValidationMetric,
    string ReasonKey,
    IntrinsicsCalibrationResult? IntrinsicsResult = null,
    ExtrinsicsCalibrationResult? ExtrinsicsResult = null,
    TimeOffsetCalibrationResult? TimeSyncResult = null);

public sealed record WorkshopAdjustmentSnapshot(
    int StepIndex,
    Vector3d LeverArmBody,
    Quaterniond SensorToBody,
    double TimeOffsetSeconds,
    double ValidationRmseMetres);

/// <summary>Implements intrinsics, extrinsics, time-sync calibration solvers and Arcade/Technical workshop execution.</summary>
public static class CalibrationWorkshopAndSolvers
{
    public const int MinimumIntrinsicsTrainingPoses = 3;
    public const int MinimumIntrinsicsValidationPoses = 1;
    public const double MaxIntrinsicsHeldOutRmsePixels = 1.5;

    public const int MinimumExtrinsicsDistinctPlanes = 3;
    public const double MinPlaneNormalDeterminant = 1e-4;
    public const double MinJacobianPivotThreshold = 1e-6;
    public const double MaxExtrinsicsRmseMetres = 0.05;

    public const double MinTimeOffsetSeconds = -0.100;
    public const double MaxTimeOffsetSeconds = 0.100;
    public const double CoarseTimeStepSeconds = 0.001;
    public const double FineTimeStepSeconds = 0.0001;
    public const double FineSearchHalfWindowSeconds = 0.002;
    public const double MaxTimeOffsetResidualRmseMetres = 0.02;

    public const int DefaultArcadeWorkshopFeeCoords = 100;

    public static IntrinsicsCalibrationResult CalibrateCameraIntrinsics(
        CameraIntrinsics initialGuess,
        IReadOnlyList<CalibrationBoardPose> poses)
    {
        ArgumentNullException.ThrowIfNull(initialGuess);
        ArgumentNullException.ThrowIfNull(poses);

        var trainPoses = poses.Where(p => !p.IsValidationHoldout).ToArray();
        var holdoutPoses = poses.Where(p => p.IsValidationHoldout).ToArray();

        if (trainPoses.Length < MinimumIntrinsicsTrainingPoses || holdoutPoses.Length < MinimumIntrinsicsValidationPoses)
        {
            return new IntrinsicsCalibrationResult(
                Succeeded: false,
                EstimatedIntrinsics: initialGuess,
                TrainingPoseCount: trainPoses.Length,
                ValidationPoseCount: holdoutPoses.Length,
                TrainingRmsePixels: double.PositiveInfinity,
                ValidationHeldOutRmsePixels: double.PositiveInfinity,
                IterationsExecuted: 0,
                ReasonKey: "calibration.intrinsics.insufficient_poses");
        }

        if (!AreTrainingPosesDistinct(trainPoses))
        {
            return new IntrinsicsCalibrationResult(
                Succeeded: false,
                EstimatedIntrinsics: initialGuess,
                TrainingPoseCount: trainPoses.Length,
                ValidationPoseCount: holdoutPoses.Length,
                TrainingRmsePixels: double.PositiveInfinity,
                ValidationHeldOutRmsePixels: double.PositiveInfinity,
                IterationsExecuted: 0,
                ReasonKey: "calibration.intrinsics.duplicate_training_poses");
        }

        var trainPointCount = trainPoses.Sum(p => p.Observations.Count);
        var holdoutPointCount = holdoutPoses.Sum(p => p.Observations.Count);
        if (trainPointCount < 6 || holdoutPointCount < 1)
        {
            return new IntrinsicsCalibrationResult(
                Succeeded: false,
                EstimatedIntrinsics: initialGuess,
                TrainingPoseCount: trainPoses.Length,
                ValidationPoseCount: holdoutPoses.Length,
                TrainingRmsePixels: double.PositiveInfinity,
                ValidationHeldOutRmsePixels: double.PositiveInfinity,
                IterationsExecuted: 0,
                ReasonKey: "calibration.intrinsics.insufficient_points");
        }

        double[] start =
        [
            initialGuess.Fx,
            initialGuess.Fy,
            initialGuess.Cx,
            initialGuess.Cy,
            initialGuess.K1,
            initialGuess.K2
        ];

        bool ValidateParams(double[] p) =>
            p[0] > 10.0 && p[0] < 50000.0 &&
            p[1] > 10.0 && p[1] < 50000.0 &&
            p[2] > -initialGuess.Width && p[2] < 2.0 * initialGuess.Width &&
            p[3] > -initialGuess.Height && p[3] < 2.0 * initialGuess.Height &&
            Math.Abs(p[4]) <= 2.0 &&
            Math.Abs(p[5]) <= 2.0;

        double[]? EvaluateResiduals(double[] p)
        {
            var list = new double[trainPointCount * 2];
            var idx = 0;
            for (var poseIdx = 0; poseIdx < trainPoses.Length; poseIdx++)
            {
                var pose = trainPoses[poseIdx];
                for (var ptIdx = 0; ptIdx < pose.Observations.Count; ptIdx++)
                {
                    var obs = pose.Observations[ptIdx];
                    var camPt = pose.BoardToCamera.Transform(obs.PointInBoardFrame);
                    if (camPt.Z <= 0.01)
                        return null;
                    var (uPred, vPred) = ProjectNormalizedWithParams(camPt, p[0], p[1], p[2], p[3], p[4], p[5]);
                    if (!double.IsFinite(uPred) || !double.IsFinite(vPred))
                        return null;
                    list[idx++] = uPred - obs.ObservedU;
                    list[idx++] = vPred - obs.ObservedV;
                }
            }
            return list;
        }

        var lm = DampedLeastSquaresSolver.Solve(start, EvaluateResiduals, ValidateParams);
        var estimated = new CameraIntrinsics(
            initialGuess.Width,
            initialGuess.Height,
            lm.Parameters[0],
            lm.Parameters[1],
            lm.Parameters[2],
            lm.Parameters[3],
            lm.Parameters[4],
            lm.Parameters[5]);

        var trainRmse = ComputeIntrinsicsRmsePixels(estimated, trainPoses);
        var holdoutRmse = ComputeIntrinsicsRmsePixels(estimated, holdoutPoses);

        var ok = !lm.TerminatedWithNonFinite &&
                 double.IsFinite(holdoutRmse) &&
                 holdoutRmse <= MaxIntrinsicsHeldOutRmsePixels;

        return new IntrinsicsCalibrationResult(
            Succeeded: ok,
            EstimatedIntrinsics: ok ? estimated : initialGuess,
            TrainingPoseCount: trainPoses.Length,
            ValidationPoseCount: holdoutPoses.Length,
            TrainingRmsePixels: trainRmse,
            ValidationHeldOutRmsePixels: holdoutRmse,
            IterationsExecuted: lm.IterationsExecuted,
            ReasonKey: ok ? "calibration.intrinsics.succeeded" : "calibration.intrinsics.heldout_rmse_exceeded");
    }

    public static double ComputeIntrinsicsRmsePixels(CameraIntrinsics intrinsics, IReadOnlyList<CalibrationBoardPose> poses)
    {
        ArgumentNullException.ThrowIfNull(intrinsics);
        ArgumentNullException.ThrowIfNull(poses);

        var sumSq = 0.0;
        var count = 0;
        foreach (var pose in poses)
        {
            foreach (var obs in pose.Observations)
            {
                var camPt = pose.BoardToCamera.Transform(obs.PointInBoardFrame);
                if (camPt.Z <= 0.01)
                    return double.PositiveInfinity;
                var (u, v) = ProjectNormalizedWithParams(
                    camPt,
                    intrinsics.Fx,
                    intrinsics.Fy,
                    intrinsics.Cx,
                    intrinsics.Cy,
                    intrinsics.K1,
                    intrinsics.K2);
                if (!double.IsFinite(u) || !double.IsFinite(v))
                    return double.PositiveInfinity;
                var du = u - obs.ObservedU;
                var dv = v - obs.ObservedV;
                sumSq += du * du + dv * dv;
                count++;
            }
        }

        return count == 0 ? double.PositiveInfinity : Math.Sqrt(sumSq / count);
    }

    public static ExtrinsicsCalibrationResult CalibrateExtrinsicsPointToPlane(
        Vector3d initialLeverArmBody,
        Quaterniond initialSensorToBody,
        IReadOnlyList<ExtrinsicPlaneCorrespondence> trainingCorrespondences,
        IReadOnlyList<ExtrinsicPlaneCorrespondence>? validationCorrespondences = null)
    {
        EnsureValidQuaternion(initialSensorToBody, nameof(initialSensorToBody));
        ArgumentNullException.ThrowIfNull(trainingCorrespondences);

        var distinctPlanes = trainingCorrespondences
            .Select(c => c.PlaneGroupId)
            .Distinct(StringComparer.Ordinal)
            .Count();

        if (trainingCorrespondences.Count < 6 || distinctPlanes < MinimumExtrinsicsDistinctPlanes)
        {
            return new ExtrinsicsCalibrationResult(
                Succeeded: false,
                EstimatedLeverArmBody: initialLeverArmBody,
                EstimatedSensorToBody: initialSensorToBody,
                TrainingPointToPlaneRmseMetres: double.PositiveInfinity,
                ValidationHeldOutRmseMetres: double.PositiveInfinity,
                PlaneNormalDeterminant: 0.0,
                DistinctPlaneGroupCount: distinctPlanes,
                IterationsExecuted: 0,
                ReasonKey: "calibration.extrinsics.insufficient_planes");
        }

        var normalDet = ComputePlaneNormalDeterminant(trainingCorrespondences, initialSensorToBody);
        if (normalDet < MinPlaneNormalDeterminant)
        {
            return new ExtrinsicsCalibrationResult(
                Succeeded: false,
                EstimatedLeverArmBody: initialLeverArmBody,
                EstimatedSensorToBody: initialSensorToBody,
                TrainingPointToPlaneRmseMetres: double.PositiveInfinity,
                ValidationHeldOutRmseMetres: double.PositiveInfinity,
                PlaneNormalDeterminant: normalDet,
                DistinctPlaneGroupCount: distinctPlanes,
                IterationsExecuted: 0,
                ReasonKey: "calibration.extrinsics.coplanar_or_parallel_normals");
        }

        double[] start = [initialLeverArmBody.X, initialLeverArmBody.Y, initialLeverArmBody.Z, 0.0, 0.0, 0.0];

        bool ValidateParams(double[] p) =>
            p.All(double.IsFinite) &&
            Math.Abs(p[0]) < 10.0 && Math.Abs(p[1]) < 10.0 && Math.Abs(p[2]) < 10.0 &&
            Math.Abs(p[3]) < 1.0 && Math.Abs(p[4]) < 1.0 && Math.Abs(p[5]) < 1.0;

        double[]? EvaluateResiduals(double[] p) =>
            ComputePointToPlaneResiduals(p, initialSensorToBody, trainingCorrespondences);

        // Verify 6-DOF observability across normal equations before declaring solvable
        if (!DampedLeastSquaresSolver.TryComputeNormalEquations(start, EvaluateResiduals, ValidateParams, out var initialJtj, out _) ||
            !IsJacobianWellConditioned(initialJtj, trainingCorrespondences.Count))
        {
            return new ExtrinsicsCalibrationResult(
                Succeeded: false,
                EstimatedLeverArmBody: initialLeverArmBody,
                EstimatedSensorToBody: initialSensorToBody,
                TrainingPointToPlaneRmseMetres: double.PositiveInfinity,
                ValidationHeldOutRmseMetres: double.PositiveInfinity,
                PlaneNormalDeterminant: normalDet,
                DistinctPlaneGroupCount: distinctPlanes,
                IterationsExecuted: 0,
                ReasonKey: "calibration.extrinsics.unobservable_motion_or_geometry");
        }

        var lm = DampedLeastSquaresSolver.Solve(start, EvaluateResiduals, ValidateParams);
        var estLeverArm = new Vector3d(lm.Parameters[0], lm.Parameters[1], lm.Parameters[2]);
        var estRot = ComposeIncrementalRotation(initialSensorToBody, lm.Parameters[3], lm.Parameters[4], lm.Parameters[5]);

        var trainRmse = ComputePointToPlaneRmse(estLeverArm, estRot, trainingCorrespondences);
        var evalSet = validationCorrespondences is { Count: > 0 } ? validationCorrespondences : trainingCorrespondences;
        var heldOutRmse = ComputePointToPlaneRmse(estLeverArm, estRot, evalSet);

        var ok = !lm.TerminatedWithNonFinite &&
                 double.IsFinite(heldOutRmse) &&
                 heldOutRmse <= MaxExtrinsicsRmseMetres;

        return new ExtrinsicsCalibrationResult(
            Succeeded: ok,
            EstimatedLeverArmBody: ok ? estLeverArm : initialLeverArmBody,
            EstimatedSensorToBody: ok ? estRot : initialSensorToBody,
            TrainingPointToPlaneRmseMetres: trainRmse,
            ValidationHeldOutRmseMetres: heldOutRmse,
            PlaneNormalDeterminant: normalDet,
            DistinctPlaneGroupCount: distinctPlanes,
            IterationsExecuted: lm.IterationsExecuted,
            ReasonKey: ok ? "calibration.extrinsics.succeeded" : "calibration.extrinsics.rmse_exceeded");
    }

    public static double ComputePointToPlaneRmse(
        Vector3d leverArmBody,
        Quaterniond sensorToBody,
        IReadOnlyList<ExtrinsicPlaneCorrespondence> correspondences)
    {
        EnsureValidQuaternion(sensorToBody, nameof(sensorToBody));
        ArgumentNullException.ThrowIfNull(correspondences);
        if (correspondences.Count == 0)
            return double.PositiveInfinity;

        var sumSq = 0.0;
        for (var i = 0; i < correspondences.Count; i++)
        {
            var r = EvaluateSinglePointToPlaneResidual(correspondences[i], leverArmBody, sensorToBody);
            if (!double.IsFinite(r))
                return double.PositiveInfinity;
            sumSq += r * r;
        }
        return Math.Sqrt(sumSq / correspondences.Count);
    }

    public static TimeOffsetCalibrationResult CalibrateTimeOffset(
        EstimatedBodyTrajectory trajectory,
        Vector3d leverArmBody,
        Quaterniond sensorToBody,
        IReadOnlyList<TimeSyncCorrespondence> correspondences,
        double initialOffsetSeconds = 0.0)
    {
        ArgumentNullException.ThrowIfNull(trajectory);
        EnsureValidQuaternion(sensorToBody, nameof(sensorToBody));
        ArgumentNullException.ThrowIfNull(correspondences);
        if (!double.IsFinite(initialOffsetSeconds))
            throw new ArgumentOutOfRangeException(nameof(initialOffsetSeconds));

        if (correspondences.Count < 3)
        {
            return new TimeOffsetCalibrationResult(
                Succeeded: false,
                EstimatedTimeOffsetSeconds: initialOffsetSeconds,
                CoarseOffsetSeconds: initialOffsetSeconds,
                ResidualRmseMetres: double.PositiveInfinity,
                SpeedSpanMetresPerSecond: 0.0,
                RotationSpanRadians: 0.0,
                IterationsEvaluated: 0,
                ReasonKey: "calibration.timesync.insufficient_samples");
        }

        var (speedSpan, rotationSpan) = MeasureTrajectoryVariation(trajectory, correspondences);
        if (speedSpan < 1.0 && rotationSpan < 0.15)
        {
            return new TimeOffsetCalibrationResult(
                Succeeded: false,
                EstimatedTimeOffsetSeconds: initialOffsetSeconds,
                CoarseOffsetSeconds: initialOffsetSeconds,
                ResidualRmseMetres: double.PositiveInfinity,
                SpeedSpanMetresPerSecond: speedSpan,
                RotationSpanRadians: rotationSpan,
                IterationsEvaluated: 0,
                ReasonKey: "calibration.timesync.constant_velocity_unobservable");
        }

        var evals = 0;
        var bestCoarseOffset = double.NaN;
        var bestCoarseRmse = double.PositiveInfinity;

        for (var stepMs = -100; stepMs <= 100; stepMs++)
        {
            var candidate = stepMs * CoarseTimeStepSeconds;
            var rmse = ComputeTimeOffsetRmse(trajectory, leverArmBody, sensorToBody, correspondences, candidate);
            evals++;
            if (double.IsFinite(rmse) && rmse < bestCoarseRmse)
            {
                bestCoarseRmse = rmse;
                bestCoarseOffset = candidate;
            }
        }

        if (!double.IsFinite(bestCoarseOffset))
        {
            return new TimeOffsetCalibrationResult(
                Succeeded: false,
                EstimatedTimeOffsetSeconds: initialOffsetSeconds,
                CoarseOffsetSeconds: initialOffsetSeconds,
                ResidualRmseMetres: double.PositiveInfinity,
                SpeedSpanMetresPerSecond: speedSpan,
                RotationSpanRadians: rotationSpan,
                IterationsEvaluated: evals,
                ReasonKey: "calibration.timesync.outside_trajectory_bounds");
        }

        var bestFineOffset = bestCoarseOffset;
        var bestFineRmse = bestCoarseRmse;

        for (var sub = -20; sub <= 20; sub++)
        {
            var candidate = Math.Clamp(bestCoarseOffset + sub * FineTimeStepSeconds, MinTimeOffsetSeconds, MaxTimeOffsetSeconds);
            var rmse = ComputeTimeOffsetRmse(trajectory, leverArmBody, sensorToBody, correspondences, candidate);
            evals++;
            if (double.IsFinite(rmse) && rmse < bestFineRmse)
            {
                bestFineRmse = rmse;
                bestFineOffset = candidate;
            }
        }

        var ok = double.IsFinite(bestFineRmse) && bestFineRmse <= MaxTimeOffsetResidualRmseMetres;
        return new TimeOffsetCalibrationResult(
            Succeeded: ok,
            EstimatedTimeOffsetSeconds: ok ? bestFineOffset : initialOffsetSeconds,
            CoarseOffsetSeconds: bestCoarseOffset,
            ResidualRmseMetres: bestFineRmse,
            SpeedSpanMetresPerSecond: speedSpan,
            RotationSpanRadians: rotationSpan,
            IterationsEvaluated: evals,
            ReasonKey: ok ? "calibration.timesync.succeeded" : "calibration.timesync.rmse_exceeded");
    }

    public static double ComputeTimeOffsetRmse(
        EstimatedBodyTrajectory trajectory,
        Vector3d leverArmBody,
        Quaterniond sensorToBody,
        IReadOnlyList<TimeSyncCorrespondence> correspondences,
        double candidateOffsetSeconds)
    {
        ArgumentNullException.ThrowIfNull(trajectory);
        EnsureValidQuaternion(sensorToBody, nameof(sensorToBody));
        ArgumentNullException.ThrowIfNull(correspondences);

        if (correspondences.Count == 0 || !double.IsFinite(candidateOffsetSeconds))
            return double.PositiveInfinity;

        var mount = new CameraMountEstimate(leverArmBody, sensorToBody, candidateOffsetSeconds);
        var sumSq = 0.0;
        for (var i = 0; i < correspondences.Count; i++)
        {
            var c = correspondences[i];
            if (!trajectory.TryGetCameraToMap(c.RawSensorTimeSeconds, mount, out var sensorToMap))
                return double.PositiveInfinity;

            var predictedMap = sensorToMap.Transform(c.PointSensor);
            var err = predictedMap - c.KnownTargetMapPoint;
            sumSq += err.X * err.X + err.Y * err.Y + err.Z * err.Z;
        }
        return Math.Sqrt(sumSq / correspondences.Count);
    }

    public static WorkshopCalibrationResult ExecuteWorkshopExtrinsicsAndSync(
        ModularSensorRack rack,
        int highestOwnedPlatformLevel,
        string slotId,
        ProcessingMode mode,
        FleetProgressionManager progression,
        CareerLedger ledger,
        string transactionId,
        double timestampSeconds,
        IReadOnlyList<ExtrinsicPlaneCorrespondence> extrinsicTraining,
        IReadOnlyList<ExtrinsicPlaneCorrespondence>? extrinsicValidation = null,
        EstimatedBodyTrajectory? syncTrajectory = null,
        IReadOnlyList<TimeSyncCorrespondence>? syncCorrespondences = null,
        int arcadeBaseFeeCoords = DefaultArcadeWorkshopFeeCoords)
    {
        ArgumentNullException.ThrowIfNull(rack);
        ArgumentException.ThrowIfNullOrWhiteSpace(slotId);
        ArgumentNullException.ThrowIfNull(progression);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);
        if (arcadeBaseFeeCoords < 0)
            throw new ArgumentOutOfRangeException(nameof(arcadeBaseFeeCoords));

        if (!ModularSensorRack.IsWorkshopAccessible(highestOwnedPlatformLevel))
        {
            return new WorkshopCalibrationResult(
                Succeeded: false,
                Mode: mode,
                SlotId: slotId,
                ChargedFeeCoords: 0,
                BaseFeeCoords: 0,
                FeeDiscountFraction: 0.0,
                NewRackRevision: null,
                ValidationMetric: double.PositiveInfinity,
                ReasonKey: "workshop.error.requires_platform_level_3");
        }

        if (!rack.MountedSlots.TryGetValue(slotId, out var pair))
        {
            return new WorkshopCalibrationResult(
                Succeeded: false,
                Mode: mode,
                SlotId: slotId,
                ChargedFeeCoords: 0,
                BaseFeeCoords: 0,
                FeeDiscountFraction: 0.0,
                NewRackRevision: null,
                ValidationMetric: double.PositiveInfinity,
                ReasonKey: "workshop.error.slot_not_mounted");
        }

        var discountFraction = progression.GetCalibrationFeeReductionFraction();
        var baseFee = mode == ProcessingMode.Arcade ? arcadeBaseFeeCoords : 0;
        var feeToCharge = mode == ProcessingMode.Arcade
            ? Math.Max(0, (int)Math.Round(baseFee * (1.0 - discountFraction), MidpointRounding.AwayFromZero))
            : 0;

        if (feeToCharge > 0)
        {
            var debit = ledger.TryDebitPurchaseOrService(
                transactionId,
                $"workshop_cal:{slotId}",
                timestampSeconds,
                feeToCharge,
                "calibration_workshop",
                "workshop.calibration.arcade_fee");

            if (!debit.Succeeded)
            {
                return new WorkshopCalibrationResult(
                    Succeeded: false,
                    Mode: mode,
                    SlotId: slotId,
                    ChargedFeeCoords: 0,
                    BaseFeeCoords: baseFee,
                    FeeDiscountFraction: discountFraction,
                    NewRackRevision: null,
                    ValidationMetric: double.PositiveInfinity,
                    ReasonKey: "workshop.error.insufficient_funds");
            }
        }

        var initialEst = pair.EstimatedCalibrationMount;
        var extResult = CalibrateExtrinsicsPointToPlane(
            initialEst.LeverArmBody,
            initialEst.SensorToBody,
            extrinsicTraining,
            extrinsicValidation);

        if (!extResult.Succeeded)
        {
            return new WorkshopCalibrationResult(
                Succeeded: false,
                Mode: mode,
                SlotId: slotId,
                ChargedFeeCoords: feeToCharge,
                BaseFeeCoords: baseFee,
                FeeDiscountFraction: discountFraction,
                NewRackRevision: null,
                ValidationMetric: extResult.ValidationHeldOutRmseMetres,
                ReasonKey: extResult.ReasonKey,
                ExtrinsicsResult: extResult);
        }

        var finalOffset = initialEst.TimeOffsetSeconds;
        TimeOffsetCalibrationResult? syncResult = null;
        if (syncTrajectory is not null && syncCorrespondences is not null)
        {
            syncResult = CalibrateTimeOffset(
                syncTrajectory,
                extResult.EstimatedLeverArmBody,
                extResult.EstimatedSensorToBody,
                syncCorrespondences,
                initialEst.TimeOffsetSeconds);

            if (!syncResult.Succeeded)
            {
                return new WorkshopCalibrationResult(
                    Succeeded: false,
                    Mode: mode,
                    SlotId: slotId,
                    ChargedFeeCoords: feeToCharge,
                    BaseFeeCoords: baseFee,
                    FeeDiscountFraction: discountFraction,
                    NewRackRevision: null,
                    ValidationMetric: syncResult.ResidualRmseMetres,
                    ReasonKey: syncResult.ReasonKey,
                    ExtrinsicsResult: extResult,
                    TimeSyncResult: syncResult);
            }

            finalOffset = syncResult.EstimatedTimeOffsetSeconds;
        }

        var updated = rack.ApplyEstimatedCalibration(
            slotId,
            extResult.EstimatedLeverArmBody,
            extResult.EstimatedSensorToBody,
            finalOffset,
            timestampSeconds,
            mode == ProcessingMode.Arcade ? "workshop.revision.arcade" : "workshop.revision.technical");

        return new WorkshopCalibrationResult(
            Succeeded: true,
            Mode: mode,
            SlotId: slotId,
            ChargedFeeCoords: feeToCharge,
            BaseFeeCoords: baseFee,
            FeeDiscountFraction: discountFraction,
            NewRackRevision: updated.Revision,
            ValidationMetric: extResult.ValidationHeldOutRmseMetres,
            ReasonKey: "workshop.calibration.succeeded",
            ExtrinsicsResult: extResult,
            TimeSyncResult: syncResult);
    }

    public static WorkshopCalibrationResult ExecuteWorkshopIntrinsics(
        int highestOwnedPlatformLevel,
        string slotId,
        ProcessingMode mode,
        FleetProgressionManager progression,
        CareerLedger ledger,
        string transactionId,
        double timestampSeconds,
        CameraIntrinsics initialGuess,
        IReadOnlyList<CalibrationBoardPose> poses,
        int arcadeBaseFeeCoords = DefaultArcadeWorkshopFeeCoords)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotId);
        ArgumentNullException.ThrowIfNull(progression);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);

        if (!ModularSensorRack.IsWorkshopAccessible(highestOwnedPlatformLevel))
        {
            return new WorkshopCalibrationResult(
                false, mode, slotId, 0, 0, 0.0, null, double.PositiveInfinity,
                "workshop.error.requires_platform_level_3");
        }

        var discountFraction = progression.GetCalibrationFeeReductionFraction();
        var baseFee = mode == ProcessingMode.Arcade ? arcadeBaseFeeCoords : 0;
        var feeToCharge = mode == ProcessingMode.Arcade
            ? Math.Max(0, (int)Math.Round(baseFee * (1.0 - discountFraction), MidpointRounding.AwayFromZero))
            : 0;

        if (feeToCharge > 0)
        {
            var debit = ledger.TryDebitPurchaseOrService(
                transactionId, $"workshop_intrinsics:{slotId}", timestampSeconds,
                feeToCharge, "calibration_workshop", "workshop.intrinsics.arcade_fee");
            if (!debit.Succeeded)
            {
                return new WorkshopCalibrationResult(
                    false, mode, slotId, 0, baseFee, discountFraction, null, double.PositiveInfinity,
                    "workshop.error.insufficient_funds");
            }
        }

        var intrResult = CalibrateCameraIntrinsics(initialGuess, poses);
        return new WorkshopCalibrationResult(
            Succeeded: intrResult.Succeeded,
            Mode: mode,
            SlotId: slotId,
            ChargedFeeCoords: feeToCharge,
            BaseFeeCoords: baseFee,
            FeeDiscountFraction: discountFraction,
            NewRackRevision: null,
            ValidationMetric: intrResult.ValidationHeldOutRmsePixels,
            ReasonKey: intrResult.ReasonKey,
            IntrinsicsResult: intrResult);
    }

    private static (double U, double V) ProjectNormalizedWithParams(
        Vector3d camPt,
        double fx,
        double fy,
        double cx,
        double cy,
        double k1,
        double k2)
    {
        var x = camPt.X / camPt.Z;
        var y = camPt.Y / camPt.Z;
        var r2 = x * x + y * y;
        var scale = 1.0 + k1 * r2 + k2 * r2 * r2;
        return (fx * x * scale + cx, fy * y * scale + cy);
    }

    private static bool AreTrainingPosesDistinct(IReadOnlyList<CalibrationBoardPose> trainPoses)
    {
        for (var i = 0; i < trainPoses.Count; i++)
        {
            for (var j = i + 1; j < trainPoses.Count; j++)
            {
                var tDiff = (trainPoses[i].BoardToCamera.Translation - trainPoses[j].BoardToCamera.Translation).Length;
                var relQ = trainPoses[i].BoardToCamera.Rotation * trainPoses[j].BoardToCamera.Rotation.Inverse();
                var angle = 2.0 * Math.Acos(Math.Clamp(Math.Abs(relQ.W), 0.0, 1.0));
                if (tDiff < 0.05 && angle < 0.03)
                    return false;
            }
        }
        return true;
    }

    private static double ComputePlaneNormalDeterminant(
        IReadOnlyList<ExtrinsicPlaneCorrespondence> correspondences,
        Quaterniond sensorToBody)
    {
        var m = new double[3, 3];
        var n = correspondences.Count;
        for (var i = 0; i < n; i++)
        {
            var c = correspondences[i];
            var nMap = c.PassBBodyToMap.Rotation.Rotate(sensorToBody.Rotate(c.PassBPlaneNormalSensor)).Normalized();
            double[] vec = [nMap.X, nMap.Y, nMap.Z];
            for (var r = 0; r < 3; r++)
                for (var col = 0; col < 3; col++)
                    m[r, col] += vec[r] * vec[col] / n;
        }

        return
            m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1]) -
            m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0]) +
            m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);
    }

    private static bool IsJacobianWellConditioned(double[,] jtj, int sampleCount)
    {
        var normalized = new double[6, 6];
        for (var r = 0; r < 6; r++)
            for (var c = 0; c < 6; c++)
                normalized[r, c] = jtj[r, c] / sampleCount;

        var pivots = DampedLeastSquaresSolver.ComputePivotSpectrum(normalized);
        if (pivots is null)
            return false;

        foreach (var pivot in pivots)
        {
            if (pivot < MinJacobianPivotThreshold)
                return false;
        }
        return true;
    }

    private static double[]? ComputePointToPlaneResiduals(
        double[] parameters,
        Quaterniond baseRotation,
        IReadOnlyList<ExtrinsicPlaneCorrespondence> correspondences)
    {
        var leverArm = new Vector3d(parameters[0], parameters[1], parameters[2]);
        var candidateRot = ComposeIncrementalRotation(baseRotation, parameters[3], parameters[4], parameters[5]);
        var res = new double[correspondences.Count];

        for (var i = 0; i < correspondences.Count; i++)
        {
            var r = EvaluateSinglePointToPlaneResidual(correspondences[i], leverArm, candidateRot);
            if (!double.IsFinite(r))
                return null;
            res[i] = r;
        }
        return res;
    }

    private static double EvaluateSinglePointToPlaneResidual(
        ExtrinsicPlaneCorrespondence c,
        Vector3d leverArmBody,
        Quaterniond sensorToBody)
    {
        var xA = c.PassABodyToMap.Transform(leverArmBody + sensorToBody.Rotate(c.PassAPointSensor));
        var xB = c.PassBBodyToMap.Transform(leverArmBody + sensorToBody.Rotate(c.PassBPointSensor));
        var nMap = c.PassBBodyToMap.Rotation.Rotate(sensorToBody.Rotate(c.PassBPlaneNormalSensor)).Normalized();
        return Vector3d.Dot(nMap, xA - xB);
    }

    private static Quaterniond ComposeIncrementalRotation(Quaterniond baseRotation, double rx, double ry, double rz)
    {
        var omega = new Vector3d(rx, ry, rz);
        var theta = omega.Length;
        if (theta < 1e-12)
            return baseRotation;
        var dq = Quaterniond.FromAxisAngle(omega * (1.0 / theta), theta);
        return NormalizeQuaternion(dq * baseRotation);
    }

    internal static void EnsureValidQuaternion(Quaterniond q, string paramName)
    {
        if (!double.IsFinite(q.X) || !double.IsFinite(q.Y) || !double.IsFinite(q.Z) || !double.IsFinite(q.W))
            throw new ArgumentOutOfRangeException(paramName, "Quaternion components must be finite.");
        var normSq = q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W;
        if (Math.Abs(normSq - 1.0) > 1e-4)
            throw new ArgumentOutOfRangeException(paramName, "Quaternion must be normalized.");
    }

    private static Quaterniond NormalizeQuaternion(Quaterniond q)
    {
        var norm = Math.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W);
        return new Quaterniond(q.X / norm, q.Y / norm, q.Z / norm, q.W / norm);
    }

    private static (double SpeedSpan, double RotationSpan) MeasureTrajectoryVariation(
        EstimatedBodyTrajectory trajectory,
        IReadOnlyList<TimeSyncCorrespondence> correspondences)
    {
        const double probeDt = 0.05;
        var speeds = new List<double>();
        Quaterniond? firstRot = null;
        var maxAngle = 0.0;

        foreach (var c in correspondences)
        {
            var t = c.RawSensorTimeSeconds;
            if (!trajectory.TrySample(t, out var centerPose) || centerPose is null)
                continue;

            if (firstRot is null)
                firstRot = centerPose.BodyToMap;
            else
            {
                var rel = centerPose.BodyToMap * firstRot.Value.Inverse();
                var ang = 2.0 * Math.Acos(Math.Clamp(Math.Abs(rel.W), 0.0, 1.0));
                if (ang > maxAngle)
                    maxAngle = ang;
            }

            if (trajectory.TrySample(t - probeDt, out var prevPose) && prevPose is not null &&
                trajectory.TrySample(t + probeDt, out var nextPose) && nextPose is not null)
            {
                speeds.Add((nextPose.PositionEnu - prevPose.PositionEnu).Length / (2.0 * probeDt));
            }
            else if (trajectory.TrySample(t + probeDt, out var nextOnly) && nextOnly is not null)
            {
                speeds.Add((nextOnly.PositionEnu - centerPose.PositionEnu).Length / probeDt);
            }
            else if (trajectory.TrySample(t - probeDt, out var prevOnly) && prevOnly is not null)
            {
                speeds.Add((centerPose.PositionEnu - prevOnly.PositionEnu).Length / probeDt);
            }
        }

        var speedSpan = speeds.Count >= 2 ? speeds.Max() - speeds.Min() : 0.0;
        return (speedSpan, maxAngle);
    }
}

/// <summary>Technical interactive calibration bench session with observable residuals, hints, undo, and strict truth isolation.</summary>
public sealed class WorkshopAdjustmentSession
{
    private readonly ModularSensorRack _rack;
    private readonly string _slotId;
    private readonly IReadOnlyList<ExtrinsicPlaneCorrespondence> _validationCorrespondences;
    private readonly EstimatedBodyTrajectory? _syncTrajectory;
    private readonly IReadOnlyList<TimeSyncCorrespondence>? _syncCorrespondences;
    private readonly Stack<WorkshopAdjustmentSnapshot> _history = new();

    public Vector3d CurrentLeverArmBody { get; private set; }
    public Quaterniond CurrentSensorToBody { get; private set; }
    public double CurrentTimeOffsetSeconds { get; private set; }
    public double CurrentValidationRmseMetres { get; private set; }
    public bool CanUndo => _history.Count > 0;
    public int HistoryDepth => _history.Count;

    public WorkshopAdjustmentSession(
        ModularSensorRack rack,
        string slotId,
        IReadOnlyList<ExtrinsicPlaneCorrespondence> validationCorrespondences,
        EstimatedBodyTrajectory? syncTrajectory = null,
        IReadOnlyList<TimeSyncCorrespondence>? syncCorrespondences = null)
    {
        ArgumentNullException.ThrowIfNull(rack);
        ArgumentException.ThrowIfNullOrWhiteSpace(slotId);
        ArgumentNullException.ThrowIfNull(validationCorrespondences);
        if (validationCorrespondences.Count == 0)
            throw new ArgumentException("At least one validation correspondence is required.", nameof(validationCorrespondences));
        if (!rack.MountedSlots.TryGetValue(slotId, out var pair))
            throw new KeyNotFoundException($"No sensor mounted in slot '{slotId}'.");

        _rack = rack;
        _slotId = slotId;
        _validationCorrespondences = validationCorrespondences;
        _syncTrajectory = syncTrajectory;
        _syncCorrespondences = syncCorrespondences;

        CurrentLeverArmBody = pair.EstimatedCalibrationMount.LeverArmBody;
        CurrentSensorToBody = pair.EstimatedCalibrationMount.SensorToBody;
        CurrentTimeOffsetSeconds = pair.EstimatedCalibrationMount.TimeOffsetSeconds;
        CurrentValidationRmseMetres = EvaluateCurrentValidationRmse();
    }

    public WorkshopAdjustmentSnapshot ApplyManualAdjustment(
        Vector3d candidateLeverArmBody,
        Quaterniond candidateSensorToBody,
        double candidateTimeOffsetSeconds)
    {
        CalibrationWorkshopAndSolvers.EnsureValidQuaternion(candidateSensorToBody, nameof(candidateSensorToBody));
        if (!double.IsFinite(candidateTimeOffsetSeconds))
            throw new ArgumentOutOfRangeException(nameof(candidateTimeOffsetSeconds));

        _history.Push(new WorkshopAdjustmentSnapshot(
            _history.Count,
            CurrentLeverArmBody,
            CurrentSensorToBody,
            CurrentTimeOffsetSeconds,
            CurrentValidationRmseMetres));

        CurrentLeverArmBody = candidateLeverArmBody;
        CurrentSensorToBody = candidateSensorToBody;
        CurrentTimeOffsetSeconds = candidateTimeOffsetSeconds;
        CurrentValidationRmseMetres = EvaluateCurrentValidationRmse();

        return new WorkshopAdjustmentSnapshot(
            _history.Count,
            CurrentLeverArmBody,
            CurrentSensorToBody,
            CurrentTimeOffsetSeconds,
            CurrentValidationRmseMetres);
    }

    public bool TryUndo(out WorkshopAdjustmentSnapshot? restored)
    {
        if (_history.Count == 0)
        {
            restored = null;
            return false;
        }

        restored = _history.Pop();
        CurrentLeverArmBody = restored.LeverArmBody;
        CurrentSensorToBody = restored.SensorToBody;
        CurrentTimeOffsetSeconds = restored.TimeOffsetSeconds;
        CurrentValidationRmseMetres = restored.ValidationRmseMetres;
        return true;
    }

    public string GetDiagnosticHintKey(int calibrationSkillRank)
    {
        if (calibrationSkillRank <= 0)
            return "workshop.hint.basic_check_residuals";
        if (CurrentValidationRmseMetres > CalibrationWorkshopAndSolvers.MaxExtrinsicsRmseMetres)
            return "workshop.hint.adjust_lever_arm_or_boresight";
        if (_syncTrajectory is not null && _syncCorrespondences is not null)
        {
            var syncRmse = CalibrationWorkshopAndSolvers.ComputeTimeOffsetRmse(
                _syncTrajectory,
                CurrentLeverArmBody,
                CurrentSensorToBody,
                _syncCorrespondences,
                CurrentTimeOffsetSeconds);
            if (syncRmse > CalibrationWorkshopAndSolvers.MaxTimeOffsetResidualRmseMetres)
                return "workshop.hint.refine_time_offset_on_acceleration";
        }
        return "workshop.hint.residuals_within_tolerance";
    }

    public WorkshopCalibrationResult TryCommitCertification(
        int highestOwnedPlatformLevel,
        double timestampSeconds)
    {
        if (!ModularSensorRack.IsWorkshopAccessible(highestOwnedPlatformLevel))
        {
            return new WorkshopCalibrationResult(
                false, ProcessingMode.Technical, _slotId, 0, 0, 0.0, null, CurrentValidationRmseMetres,
                "workshop.error.requires_platform_level_3");
        }

        if (!double.IsFinite(CurrentValidationRmseMetres) ||
            CurrentValidationRmseMetres > CalibrationWorkshopAndSolvers.MaxExtrinsicsRmseMetres)
        {
            return new WorkshopCalibrationResult(
                false, ProcessingMode.Technical, _slotId, 0, 0, 0.0, null, CurrentValidationRmseMetres,
                "workshop.technical.residuals_above_threshold");
        }

        if (_syncTrajectory is not null && _syncCorrespondences is not null)
        {
            var syncRmse = CalibrationWorkshopAndSolvers.ComputeTimeOffsetRmse(
                _syncTrajectory,
                CurrentLeverArmBody,
                CurrentSensorToBody,
                _syncCorrespondences,
                CurrentTimeOffsetSeconds);
            if (!double.IsFinite(syncRmse) ||
                syncRmse > CalibrationWorkshopAndSolvers.MaxTimeOffsetResidualRmseMetres)
            {
                return new WorkshopCalibrationResult(
                    false, ProcessingMode.Technical, _slotId, 0, 0, 0.0, null, syncRmse,
                    "workshop.technical.timesync_above_threshold");
            }
        }

        var updated = _rack.ApplyEstimatedCalibration(
            _slotId,
            CurrentLeverArmBody,
            CurrentSensorToBody,
            CurrentTimeOffsetSeconds,
            timestampSeconds,
            "workshop.revision.technical_manual");

        return new WorkshopCalibrationResult(
            Succeeded: true,
            Mode: ProcessingMode.Technical,
            SlotId: _slotId,
            ChargedFeeCoords: 0,
            BaseFeeCoords: 0,
            FeeDiscountFraction: 0.0,
            NewRackRevision: updated.Revision,
            ValidationMetric: CurrentValidationRmseMetres,
            ReasonKey: "workshop.technical.certified");
    }

    private double EvaluateCurrentValidationRmse() =>
        CalibrationWorkshopAndSolvers.ComputePointToPlaneRmse(
            CurrentLeverArmBody,
            CurrentSensorToBody,
            _validationCorrespondences);
}
