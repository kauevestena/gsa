using Gsa.Core.Geometry;
using Gsa.Core.Randomness;
using Gsa.Core.Sensors.Camera;
using Gsa.Core.Sensors.Gnss;

namespace Gsa.Core.Sensors.Fusion;

/// <summary>Raw 20 Hz wheel DMI observation; never exposes true speed, true slip, or true scale error.</summary>
public sealed record WheelDmiObservation(
    long Sequence,
    double TimestampSeconds,
    double DtSeconds,
    double MeasuredForwardSpeedMetresPerSecond,
    double NominalScaleFactor,
    double LongitudinalSigmaMetresPerSecond,
    bool IsWetRoad,
    bool VarianceInflatedForWetSlip)
{
    public double LongitudinalVarianceMetresPerSecondSquared =>
        LongitudinalSigmaMetresPerSecond * LongitudinalSigmaMetresPerSecond;
}

public readonly record struct WheelDmiSimulatorState(
    GaussianRandomState NoiseRng,
    double TrueScaleError,
    long NextSequence);

/// <summary>Simulates a 20 Hz wheel distance measurement indicator with 0.02 m/s sigma, 0.005 initial scale error, and wet-road slip (25x variance inflation).</summary>
public sealed class WheelDmiSimulator
{
    public const double NominalRateHz = 20.0;
    public const double NominalDtSeconds = 1.0 / NominalRateHz;
    public const double BaseSpeedSigmaMetresPerSecond = 0.02;
    public const double NominalInitialScaleError = 0.005;
    public const double MaxWetSlipFraction = 0.20;
    public const double WetLongitudinalVarianceMultiplier = 25.0;

    private readonly GaussianRandom _noiseRng;
    private readonly double _trueScaleError;
    private long _nextSequence = 1;

    public WheelDmiSimulator(ulong seed = 5051UL, double? initialScaleErrorOverride = null)
    {
        _noiseRng = new GaussianRandom(seed);
        if (initialScaleErrorOverride.HasValue)
        {
            if (!double.IsFinite(initialScaleErrorOverride.Value) || Math.Abs(initialScaleErrorOverride.Value) > 0.25)
                throw new ArgumentOutOfRangeException(nameof(initialScaleErrorOverride));
            _trueScaleError = initialScaleErrorOverride.Value;
        }
        else
        {
            // Deterministic signed initial calibration error near normative 0.005.
            _trueScaleError = (_noiseRng.NextGaussian() >= 0 ? 1.0 : -1.0) * NominalInitialScaleError;
        }
    }

    public WheelDmiSimulator(WheelDmiSimulatorState state)
    {
        if (state.NextSequence < 1)
            throw new ArgumentOutOfRangeException(nameof(state));
        _noiseRng = new GaussianRandom(state.NoiseRng);
        _trueScaleError = state.TrueScaleError;
        _nextSequence = state.NextSequence;
    }

    public WheelDmiSimulatorState ExportState() => new(_noiseRng.ExportState(), _trueScaleError, _nextSequence);

    public WheelDmiObservation Observe(
        double timestampSeconds,
        double dtSeconds,
        double trueForwardSpeedMetresPerSecond,
        bool isWetRoad = false,
        double requestedWetSlipFraction = 0.0,
        bool simulateWetWheelLockup = false)
    {
        if (!double.IsFinite(timestampSeconds) || timestampSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(timestampSeconds));
        if (!double.IsFinite(dtSeconds) || dtSeconds <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(dtSeconds));
        if (!double.IsFinite(trueForwardSpeedMetresPerSecond))
            throw new ArgumentOutOfRangeException(nameof(trueForwardSpeedMetresPerSecond));
        if (!double.IsFinite(requestedWetSlipFraction) || requestedWetSlipFraction < 0.0)
            throw new ArgumentOutOfRangeException(nameof(requestedWetSlipFraction));

        var effectiveSlip = isWetRoad ? Math.Min(MaxWetSlipFraction, requestedWetSlipFraction) : 0.0;
        var scaledSpeed = trueForwardSpeedMetresPerSecond * (1.0 + _trueScaleError);

        // Lockup models wheel skidding on wet pavement (wheel stops spinning while vehicle still translates).
        var physicalWheelSpeed = isWetRoad && simulateWetWheelLockup
            ? 0.0
            : scaledSpeed * (1.0 - effectiveSlip);

        var whiteNoise = simulateWetWheelLockup
            ? 0.0
            : BaseSpeedSigmaMetresPerSecond * _noiseRng.NextGaussian();

        var measuredSpeed = physicalWheelSpeed + whiteNoise;
        var sigmaMultiplier = isWetRoad ? Math.Sqrt(WetLongitudinalVarianceMultiplier) : 1.0;
        var longitudinalSigma = BaseSpeedSigmaMetresPerSecond * sigmaMultiplier;

        var seq = _nextSequence++;
        return new WheelDmiObservation(
            seq,
            timestampSeconds,
            dtSeconds,
            measuredSpeed,
            NominalScaleFactor: 1.0,
            longitudinalSigma,
            isWetRoad,
            isWetRoad);
    }
}

public sealed record ZuptDetectionResult(
    bool ZuptTriggered,
    double ContinuousStationarySeconds,
    double VelocitySigmaMetresPerSecond,
    bool SpeedCriterionMet,
    bool GyroCriterionMet,
    bool SpecificForceCriterionMet,
    Vector3d EstimatedGyroBiasRadiansPerSecond,
    Vector3d EstimatedAccelSpecificForceBodyMean);

/// <summary>Detects sustained stationary stops (2.0 s across DMI &lt; 0.05 m/s, gyro &lt; 0.01 rad/s, ||f|-g| &lt; 0.1 m/s^2) strictly from sensor observations.</summary>
public sealed class StationaryZuptDetector
{
    public const double RequiredContinuousDurationSeconds = 2.0;
    public const double MaxStationaryDmiSpeedMetresPerSecond = 0.05;
    public const double MaxStationaryGyroNormRadiansPerSecond = 0.01;
    public const double MaxStationaryGravityDeltaMetresPerSecondSquared = 0.1;
    public const double ZuptVelocitySigmaMetresPerSecond = 0.01;

    private double _accumulatedSeconds;
    private int _sampleCount;
    private Vector3d _gyroSum;
    private Vector3d _accelSum;

    public Vector3d EstimatedGyroBiasRadiansPerSecond { get; private set; }
    public Vector3d LastStationaryAccelMean { get; private set; }
    public double ContinuousStationarySeconds => _accumulatedSeconds;

    public ZuptDetectionResult Evaluate(
        double dtSeconds,
        double observedDmiSpeedMetresPerSecond,
        ImuObservation imuObservation)
    {
        if (!double.IsFinite(dtSeconds) || dtSeconds <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(dtSeconds));
        if (!double.IsFinite(observedDmiSpeedMetresPerSecond))
            throw new ArgumentOutOfRangeException(nameof(observedDmiSpeedMetresPerSecond));
        ArgumentNullException.ThrowIfNull(imuObservation);

        var speedOk = Math.Abs(observedDmiSpeedMetresPerSecond) < MaxStationaryDmiSpeedMetresPerSecond;
        var gyroOk = imuObservation.AngularVelocityBody.Length < MaxStationaryGyroNormRadiansPerSecond;
        var forceMismatch = Math.Abs(imuObservation.SpecificForceBody.Length - ImuProfile.GravityMetresPerSecondSquared);
        var forceOk = forceMismatch < MaxStationaryGravityDeltaMetresPerSecondSquared;

        if (speedOk && gyroOk && forceOk)
        {
            _accumulatedSeconds += dtSeconds;
            _sampleCount++;
            _gyroSum += imuObservation.AngularVelocityBody;
            _accelSum += imuObservation.SpecificForceBody;

            if (_accumulatedSeconds + 1e-9 >= RequiredContinuousDurationSeconds)
            {
                EstimatedGyroBiasRadiansPerSecond = _gyroSum / _sampleCount;
                LastStationaryAccelMean = _accelSum / _sampleCount;
                return new ZuptDetectionResult(
                    true,
                    _accumulatedSeconds,
                    ZuptVelocitySigmaMetresPerSecond,
                    speedOk,
                    gyroOk,
                    forceOk,
                    EstimatedGyroBiasRadiansPerSecond,
                    LastStationaryAccelMean);
            }
        }
        else
        {
            _accumulatedSeconds = 0.0;
            _sampleCount = 0;
            _gyroSum = Vector3d.Zero;
            _accelSum = Vector3d.Zero;
        }

        return new ZuptDetectionResult(
            false,
            _accumulatedSeconds,
            ZuptVelocitySigmaMetresPerSecond,
            speedOk,
            gyroOk,
            forceOk,
            EstimatedGyroBiasRadiansPerSecond,
            LastStationaryAccelMean);
    }

    public void Reset()
    {
        _accumulatedSeconds = 0.0;
        _sampleCount = 0;
        _gyroSum = Vector3d.Zero;
        _accelSum = Vector3d.Zero;
    }
}

public sealed record TrajectoryEstimateSample(
    double TimestampSeconds,
    Vector3d PositionEnu,
    Vector3d VelocityEnu,
    Quaterniond BodyToMapRotation,
    Vector3d PositionSigmaEnu,
    Vector3d VelocitySigmaEnu,
    double GnssSolutionAgeSeconds,
    bool BlackoutAlertActive,
    bool HighUncertaintyAlertActive)
{
    public double PositionSigma3DMetres =>
        Math.Sqrt(PositionSigmaEnu.X * PositionSigmaEnu.X +
                  PositionSigmaEnu.Y * PositionSigmaEnu.Y +
                  PositionSigmaEnu.Z * PositionSigmaEnu.Z);

    public EstimatedBodyPose ToEstimatedBodyPose() =>
        new(TimestampSeconds, PositionEnu, BodyToMapRotation);
}

internal sealed class KalmanStepTransitionRecord
{
    public double TimestampSeconds { get; }
    public double[] StateFiltered { get; }
    public double[,] CovFiltered { get; }
    public Quaterniond BodyToMapRotation { get; }
    public double GnssAgeSeconds { get; }
    public bool BlackoutAlert { get; }
    public double[,]? FToNext { get; set; }
    public double[]? StatePredictedNext { get; set; }
    public double[,]? CovPredictedNext { get; set; }

    public KalmanStepTransitionRecord(
        double timestampSeconds,
        double[] stateFiltered,
        double[,] covFiltered,
        Quaterniond bodyToMapRotation,
        double gnssAgeSeconds,
        bool blackoutAlert)
    {
        TimestampSeconds = timestampSeconds;
        StateFiltered = stateFiltered;
        CovFiltered = covFiltered;
        BodyToMapRotation = bodyToMapRotation;
        GnssAgeSeconds = gnssAgeSeconds;
        BlackoutAlert = blackoutAlert;
    }
}

/// <summary>Compact fixed-size double-precision linear algebra utilities for 6x6 Kalman and RTS smoothing.</summary>
internal static class MatrixOps6
{
    public static double[,] Identity(int n)
    {
        var m = new double[n, n];
        for (var i = 0; i < n; i++)
            m[i, i] = 1.0;
        return m;
    }

    public static double[] CloneVec(double[] v)
    {
        var r = new double[v.Length];
        Array.Copy(v, r, v.Length);
        return r;
    }

    public static double[,] CloneMat(double[,] m)
    {
        var rows = m.GetLength(0);
        var cols = m.GetLength(1);
        var r = new double[rows, cols];
        Array.Copy(m, r, m.Length);
        return r;
    }

    public static double[,] Multiply(double[,] a, double[,] b)
    {
        var r = a.GetLength(0);
        var k = a.GetLength(1);
        var c = b.GetLength(1);
        var res = new double[r, c];
        for (var i = 0; i < r; i++)
        {
            for (var p = 0; p < k; p++)
            {
                var aip = a[i, p];
                if (aip == 0.0) continue;
                for (var j = 0; j < c; j++)
                    res[i, j] += aip * b[p, j];
            }
        }
        return res;
    }

    public static double[,] MultiplyTransposeB(double[,] a, double[,] b)
    {
        var r = a.GetLength(0);
        var k = a.GetLength(1);
        var c = b.GetLength(0);
        var res = new double[r, c];
        for (var i = 0; i < r; i++)
        {
            for (var j = 0; j < c; j++)
            {
                var sum = 0.0;
                for (var p = 0; p < k; p++)
                    sum += a[i, p] * b[j, p];
                res[i, j] = sum;
            }
        }
        return res;
    }

    public static double[] MultiplyVec(double[,] a, double[] x)
    {
        var r = a.GetLength(0);
        var c = a.GetLength(1);
        var res = new double[r];
        for (var i = 0; i < r; i++)
        {
            var sum = 0.0;
            for (var j = 0; j < c; j++)
                sum += a[i, j] * x[j];
            res[i] = sum;
        }
        return res;
    }

    public static double[,] Add(double[,] a, double[,] b)
    {
        var r = a.GetLength(0);
        var c = a.GetLength(1);
        var res = new double[r, c];
        for (var i = 0; i < r; i++)
            for (var j = 0; j < c; j++)
                res[i, j] = a[i, j] + b[i, j];
        return res;
    }

    public static double[,] Subtract(double[,] a, double[,] b)
    {
        var r = a.GetLength(0);
        var c = a.GetLength(1);
        var res = new double[r, c];
        for (var i = 0; i < r; i++)
            for (var j = 0; j < c; j++)
                res[i, j] = a[i, j] - b[i, j];
        return res;
    }

    public static double[,] SymmetrizeAndFloor(double[,] m, double minDiag = 1e-12)
    {
        var n = m.GetLength(0);
        var res = new double[n, n];
        for (var i = 0; i < n; i++)
        {
            for (var j = i; j < n; j++)
            {
                var v = 0.5 * (m[i, j] + m[j, i]);
                if (!double.IsFinite(v))
                    throw new InvalidOperationException("Covariance matrix became non-finite.");
                if (i == j && v < minDiag)
                    v = minDiag;
                res[i, j] = v;
                res[j, i] = v;
            }
        }
        return res;
    }

    public static double[,] Invert(double[,] matrix)
    {
        var n = matrix.GetLength(0);
        var aug = new double[n, 2 * n];
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < n; j++)
                aug[i, j] = matrix[i, j];
            aug[i, n + i] = 1.0;
        }

        for (var col = 0; col < n; col++)
        {
            var pivotRow = col;
            var maxAbs = Math.Abs(aug[col, col]);
            for (var r = col + 1; r < n; r++)
            {
                var abs = Math.Abs(aug[r, col]);
                if (abs > maxAbs)
                {
                    maxAbs = abs;
                    pivotRow = r;
                }
            }

            if (maxAbs < 1e-18 || !double.IsFinite(maxAbs))
                throw new InvalidOperationException("Matrix is singular or ill-conditioned.");

            if (pivotRow != col)
            {
                for (var c = 0; c < 2 * n; c++)
                    (aug[col, c], aug[pivotRow, c]) = (aug[pivotRow, c], aug[col, c]);
            }

            var invPivot = 1.0 / aug[col, col];
            for (var c = 0; c < 2 * n; c++)
                aug[col, c] *= invPivot;

            for (var r = 0; r < n; r++)
            {
                if (r == col) continue;
                var factor = aug[r, col];
                if (factor == 0.0) continue;
                for (var c = 0; c < 2 * n; c++)
                    aug[r, c] -= factor * aug[col, c];
            }
        }

        var inv = new double[n, n];
        for (var i = 0; i < n; i++)
            for (var j = 0; j < n; j++)
                inv[i, j] = aug[i, n + j];
        return inv;
    }

    public static double[,] Rotate3x3(Quaterniond bodyToMap, Vector3d diagStdDevs)
    {
        var ex = bodyToMap.Rotate(Vector3d.UnitX);
        var ey = bodyToMap.Rotate(Vector3d.UnitY);
        var ez = bodyToMap.Rotate(Vector3d.UnitZ);

        var r = new double[,]
        {
            { ex.X, ey.X, ez.X },
            { ex.Y, ey.Y, ez.Y },
            { ex.Z, ey.Z, ez.Z },
        };

        var d = new double[,]
        {
            { diagStdDevs.X * diagStdDevs.X, 0.0, 0.0 },
            { 0.0, diagStdDevs.Y * diagStdDevs.Y, 0.0 },
            { 0.0, 0.0, diagStdDevs.Z * diagStdDevs.Z },
        };

        return MultiplyTransposeB(Multiply(r, d), r);
    }
}

/// <summary>6-state ENU trajectory Kalman filter [px, py, pz, vx, vy, vz] fusing IMU, GNSS, DMI, and ZUPT without truth access.</summary>
public sealed class SixStateTrajectoryKalmanFilter
{
    public const double DefaultBlackoutAlertThresholdSeconds = 5.0;
    public const double DefaultHighUncertaintyThresholdMetres = 2.0;

    private readonly double[] _x = new double[6];
    private double[,] _p = new double[6, 6];

    public double CurrentTimestampSeconds { get; private set; }
    public double GnssSolutionAgeSeconds { get; private set; }
    public Quaterniond EstimatedBodyToMapRotation { get; private set; } = Quaterniond.Identity;
    public double BlackoutAlertThresholdSeconds { get; }
    public double HighUncertaintyThresholdMetres { get; }

    public Vector3d PositionEnu => new(_x[0], _x[1], _x[2]);
    public Vector3d VelocityEnu => new(_x[3], _x[4], _x[5]);
    public Vector3d PositionSigmaEnu => new(Math.Sqrt(_p[0, 0]), Math.Sqrt(_p[1, 1]), Math.Sqrt(_p[2, 2]));
    public Vector3d VelocitySigmaEnu => new(Math.Sqrt(_p[3, 3]), Math.Sqrt(_p[4, 4]), Math.Sqrt(_p[5, 5]));
    public double PositionSigma3DMetres => PositionSigmaEnu.Length;
    public bool BlackoutAlertActive => GnssSolutionAgeSeconds >= BlackoutAlertThresholdSeconds;
    public bool HighUncertaintyAlertActive => PositionSigma3DMetres >= HighUncertaintyThresholdMetres;

    public SixStateTrajectoryKalmanFilter(
        double initialTimestampSeconds,
        Vector3d initialPositionEnu,
        Vector3d initialVelocityEnu,
        Quaterniond initialBodyToMapRotation,
        double initialPositionSigmaMetres = 0.5,
        double initialVelocitySigmaMetresPerSecond = 0.2,
        double blackoutAlertThresholdSeconds = DefaultBlackoutAlertThresholdSeconds,
        double highUncertaintyThresholdMetres = DefaultHighUncertaintyThresholdMetres)
    {
        if (!double.IsFinite(initialTimestampSeconds) || initialTimestampSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(initialTimestampSeconds));
        if (!double.IsFinite(initialPositionSigmaMetres) || initialPositionSigmaMetres <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(initialPositionSigmaMetres));
        if (!double.IsFinite(initialVelocitySigmaMetresPerSecond) || initialVelocitySigmaMetresPerSecond <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(initialVelocitySigmaMetresPerSecond));

        initialBodyToMapRotation.EnsureValid();
        CurrentTimestampSeconds = initialTimestampSeconds;
        EstimatedBodyToMapRotation = initialBodyToMapRotation;
        BlackoutAlertThresholdSeconds = blackoutAlertThresholdSeconds;
        HighUncertaintyThresholdMetres = highUncertaintyThresholdMetres;

        _x[0] = initialPositionEnu.X;
        _x[1] = initialPositionEnu.Y;
        _x[2] = initialPositionEnu.Z;
        _x[3] = initialVelocityEnu.X;
        _x[4] = initialVelocityEnu.Y;
        _x[5] = initialVelocityEnu.Z;

        var posVar = initialPositionSigmaMetres * initialPositionSigmaMetres;
        var velVar = initialVelocitySigmaMetresPerSecond * initialVelocitySigmaMetresPerSecond;
        for (var i = 0; i < 3; i++)
            _p[i, i] = posVar;
        for (var i = 3; i < 6; i++)
            _p[i, i] = velVar;
    }

    public static double GetProcessNoisePowerDensity(ImuGrade grade) => grade switch
    {
        ImuGrade.Ideal => 1e-5,
        ImuGrade.Mems => 0.1,
        ImuGrade.Tactical => 0.01,
        ImuGrade.Navigation => 0.001,
        _ => throw new ArgumentOutOfRangeException(nameof(grade)),
    };

    public (double[,] F, double[] XPred, double[,] PPred) Predict(
        double dtSeconds,
        Vector3d measuredSpecificForceBody,
        Quaterniond estimatedBodyToMapRotation,
        ImuGrade imuGrade)
    {
        if (!double.IsFinite(dtSeconds) || dtSeconds <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(dtSeconds));
        estimatedBodyToMapRotation.EnsureValid();

        EstimatedBodyToMapRotation = estimatedBodyToMapRotation;
        var accelMap = estimatedBodyToMapRotation.Rotate(measuredSpecificForceBody) + ImuIdealKinematics.GravityEnu;

        // x_k+1 = F x_k + B u_k
        var halfDt2 = 0.5 * dtSeconds * dtSeconds;
        _x[0] += _x[3] * dtSeconds + halfDt2 * accelMap.X;
        _x[1] += _x[4] * dtSeconds + halfDt2 * accelMap.Y;
        _x[2] += _x[5] * dtSeconds + halfDt2 * accelMap.Z;
        _x[3] += dtSeconds * accelMap.X;
        _x[4] += dtSeconds * accelMap.Y;
        _x[5] += dtSeconds * accelMap.Z;

        var f = MatrixOps6.Identity(6);
        f[0, 3] = dtSeconds;
        f[1, 4] = dtSeconds;
        f[2, 5] = dtSeconds;

        var q = GetProcessNoisePowerDensity(imuGrade);
        var dt2 = dtSeconds * dtSeconds;
        var dt3 = dt2 * dtSeconds;
        var qPos = q * (dt3 / 3.0);
        var qCross = q * (dt2 / 2.0);
        var qVel = q * dtSeconds;

        var Q = new double[6, 6];
        for (var i = 0; i < 3; i++)
        {
            Q[i, i] = qPos;
            Q[i, i + 3] = qCross;
            Q[i + 3, i] = qCross;
            Q[i + 3, i + 3] = qVel;
        }

        var fp = MatrixOps6.Multiply(f, _p);
        var fpFt = MatrixOps6.MultiplyTransposeB(fp, f);
        _p = MatrixOps6.SymmetrizeAndFloor(MatrixOps6.Add(fpFt, Q));

        CurrentTimestampSeconds += dtSeconds;
        GnssSolutionAgeSeconds += dtSeconds;

        return (f, MatrixOps6.CloneVec(_x), MatrixOps6.CloneMat(_p));
    }

    public bool UpdateGnssPosition(GnssObservation observation, double? explicitSigmaOverrideMetres = null)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (!observation.IsAvailable || observation.MeasuredPositionEnu is null)
            return false;

        var baseSigma = explicitSigmaOverrideMetres
            ?? (observation.IndependentSigmaMetres ?? 2.5) * Math.Max(1.0, observation.Dop * 0.5);
        var sigma = Math.Max(0.015, baseSigma);
        var r = MatrixOps6.Identity(3);
        var v = sigma * sigma;
        r[0, 0] = v;
        r[1, 1] = v;
        r[2, 2] = v;

        ApplyLinear3x6Update(isPositionMeasurement: true, observation.MeasuredPositionEnu.Value, r);
        GnssSolutionAgeSeconds = 0.0;
        return true;
    }

    public bool UpdatePositionControlPoint(Vector3d knownPositionEnu, double declaredSigmaMetres, out double mahalanobisDistance)
    {
        if (!double.IsFinite(declaredSigmaMetres) || declaredSigmaMetres <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(declaredSigmaMetres));

        var r = new double[,]
        {
            { declaredSigmaMetres * declaredSigmaMetres, 0.0, 0.0 },
            { 0.0, declaredSigmaMetres * declaredSigmaMetres, 0.0 },
            { 0.0, 0.0, declaredSigmaMetres * declaredSigmaMetres },
        };

        var residual = new[]
        {
            knownPositionEnu.X - _x[0],
            knownPositionEnu.Y - _x[1],
            knownPositionEnu.Z - _x[2],
        };

        var s = ComputeInnovationCovariance(isPositionMeasurement: true, r);
        var sInv = MatrixOps6.Invert(s);
        var sInvRes = MatrixOps6.MultiplyVec(sInv, residual);
        var d2 = Math.Max(0.0, residual[0] * sInvRes[0] + residual[1] * sInvRes[1] + residual[2] * sInvRes[2]);
        mahalanobisDistance = Math.Sqrt(d2);

        // Reject 3-sigma outliers (per-axis or 3D Mahalanobis) before mutating state
        var axisOutlier = false;
        for (var i = 0; i < 3; i++)
        {
            if (Math.Abs(residual[i]) / Math.Sqrt(s[i, i]) > 3.0)
                axisOutlier = true;
        }
        if (axisOutlier || mahalanobisDistance > 3.76)
            return false;

        ApplyLinear3x6UpdateWithPrecomputed(isPositionMeasurement: true, residual, r, sInv);
        return true;
    }

    public void UpdateDmiVelocity(
        WheelDmiObservation observation,
        Quaterniond estimatedBodyToMapRotation,
        double lateralRollingSigmaMetresPerSecond = 0.08,
        double verticalRollingSigmaMetresPerSecond = 0.05)
    {
        ArgumentNullException.ThrowIfNull(observation);
        estimatedBodyToMapRotation.EnsureValid();

        EstimatedBodyToMapRotation = estimatedBodyToMapRotation;
        var correctedForward = observation.MeasuredForwardSpeedMetresPerSecond / Math.Max(0.5, observation.NominalScaleFactor);
        var measuredVelMap = estimatedBodyToMapRotation.Rotate(new Vector3d(correctedForward, 0.0, 0.0));

        // Longitudinal covariance reflects 25x inflation on wet pavement; lateral/vertical enforce non-holonomic constraint.
        var bodySigmas = new Vector3d(
            observation.LongitudinalSigmaMetresPerSecond,
            lateralRollingSigmaMetresPerSecond,
            verticalRollingSigmaMetresPerSecond);
        var r = MatrixOps6.Rotate3x3(estimatedBodyToMapRotation, bodySigmas);

        ApplyLinear3x6Update(isPositionMeasurement: false, measuredVelMap, r);
    }

    public void UpdateZupt(double velocitySigmaMetresPerSecond = StationaryZuptDetector.ZuptVelocitySigmaMetresPerSecond)
    {
        if (!double.IsFinite(velocitySigmaMetresPerSecond) || velocitySigmaMetresPerSecond <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(velocitySigmaMetresPerSecond));

        var v = velocitySigmaMetresPerSecond * velocitySigmaMetresPerSecond;
        var r = new double[,]
        {
            { v, 0.0, 0.0 },
            { 0.0, v, 0.0 },
            { 0.0, 0.0, v },
        };

        ApplyLinear3x6Update(isPositionMeasurement: false, Vector3d.Zero, r);
    }

    public TrajectoryEstimateSample GetCurrentSample() =>
        new(
            CurrentTimestampSeconds,
            PositionEnu,
            VelocityEnu,
            EstimatedBodyToMapRotation,
            PositionSigmaEnu,
            VelocitySigmaEnu,
            GnssSolutionAgeSeconds,
            BlackoutAlertActive,
            HighUncertaintyAlertActive);

    public (double[] State, double[,] Covariance) ExportInternalState() =>
        (MatrixOps6.CloneVec(_x), MatrixOps6.CloneMat(_p));

    private double[,] ComputeInnovationCovariance(bool isPositionMeasurement, double[,] r)
    {
        var offset = isPositionMeasurement ? 0 : 3;
        var s = new double[3, 3];
        for (var i = 0; i < 3; i++)
            for (var j = 0; j < 3; j++)
                s[i, j] = _p[offset + i, offset + j] + r[i, j];
        return s;
    }

    private void ApplyLinear3x6Update(bool isPositionMeasurement, Vector3d measurement, double[,] r)
    {
        var offset = isPositionMeasurement ? 0 : 3;
        var residual = new[]
        {
            measurement.X - _x[offset],
            measurement.Y - _x[offset + 1],
            measurement.Z - _x[offset + 2],
        };
        var s = ComputeInnovationCovariance(isPositionMeasurement, r);
        var sInv = MatrixOps6.Invert(s);
        ApplyLinear3x6UpdateWithPrecomputed(isPositionMeasurement, residual, r, sInv);
    }

    private void ApplyLinear3x6UpdateWithPrecomputed(
        bool isPositionMeasurement,
        double[] residual,
        double[,] r,
        double[,] sInv)
    {
        var h = new double[3, 6];
        var offset = isPositionMeasurement ? 0 : 3;
        h[0, offset] = 1.0;
        h[1, offset + 1] = 1.0;
        h[2, offset + 2] = 1.0;

        // K = P H^T S^-1 (6x3)
        var pHt = MatrixOps6.MultiplyTransposeB(_p, h);
        var k = MatrixOps6.Multiply(pHt, sInv);

        // x = x + K residual
        var dx = MatrixOps6.MultiplyVec(k, residual);
        for (var i = 0; i < 6; i++)
        {
            if (!double.IsFinite(dx[i]))
                throw new InvalidOperationException("Kalman state update produced non-finite value.");
            _x[i] += dx[i];
        }

        // Joseph form: P = (I - K H) P (I - K H)^T + K R K^T
        var kH = MatrixOps6.Multiply(k, h);
        var iMinusKH = MatrixOps6.Subtract(MatrixOps6.Identity(6), kH);
        var term1 = MatrixOps6.MultiplyTransposeB(MatrixOps6.Multiply(iMinusKH, _p), iMinusKH);
        var term2 = MatrixOps6.MultiplyTransposeB(MatrixOps6.Multiply(k, r), k);
        _p = MatrixOps6.SymmetrizeAndFloor(MatrixOps6.Add(term1, term2));
    }
}

public sealed record TunnelSeedBenchmarkSummary(
    ImuGrade Grade,
    int SeedCount,
    double MedianFreeInertialDriftMetres,
    double MedianAidedDriftMetres,
    double MaxAidedFinalSigma3DMetres,
    bool AllStatesFinite);

/// <summary>Runs deterministic 100-seed tunnel blackout experiments comparing IMU grades and free-inertial vs IMU+DMI+ZUPT aiding.</summary>
public static class TrajectoryFusionBenchmark
{
    public static IReadOnlyDictionary<ImuGrade, TunnelSeedBenchmarkSummary> RunTunnelBlackoutSuite(
        int seedCount = 100,
        double preTunnelSeconds = 5.0,
        double tunnelBlackoutSeconds = 40.0,
        double zupStopStartOffsetSeconds = 18.0,
        double zuptStopDurationSeconds = 3.0)
    {
        if (seedCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(seedCount));

        var results = new Dictionary<ImuGrade, TunnelSeedBenchmarkSummary>();
        foreach (var grade in new[] { ImuGrade.Mems, ImuGrade.Tactical, ImuGrade.Navigation })
        {
            var freeDrifts = new List<double>(seedCount);
            var aidedDrifts = new List<double>(seedCount);
            var maxSigma = 0.0;
            var allFinite = true;

            for (var s = 0; s < seedCount; s++)
            {
                var baseSeed = 900000UL + (ulong)(int)grade * 10000UL + (ulong)s * 17UL;
                var (freeDrift, aidedDrift, sigma) = RunSingleSeed(
                    grade, baseSeed, preTunnelSeconds, tunnelBlackoutSeconds,
                    zupStopStartOffsetSeconds, zuptStopDurationSeconds);

                if (!double.IsFinite(freeDrift) || !double.IsFinite(aidedDrift) || !double.IsFinite(sigma))
                    allFinite = false;

                freeDrifts.Add(freeDrift);
                aidedDrifts.Add(aidedDrift);
                maxSigma = Math.Max(maxSigma, sigma);
            }

            freeDrifts.Sort();
            aidedDrifts.Sort();
            var mid = seedCount / 2;
            results[grade] = new TunnelSeedBenchmarkSummary(
                grade,
                seedCount,
                freeDrifts[mid],
                aidedDrifts[mid],
                maxSigma,
                allFinite);
        }

        return results;
    }

    private static (double FreeDrift, double AidedDrift, double AidedSigma) RunSingleSeed(
        ImuGrade grade,
        ulong seed,
        double preTunnelSeconds,
        double tunnelSeconds,
        double stopOffset,
        double stopDuration)
    {
        var profile = ImuProfile.ForGrade(grade);
        var imuSim = new ImuSimulator(profile, seed + 1, seed + 2);
        var dmiSim = new WheelDmiSimulator(seed + 3);
        var gnssSim = new GnssSimulator(seed + 4);
        var zuptDetector = new StationaryZuptDetector();

        var dtImu = 0.01;
        var totalTime = preTunnelSeconds + tunnelSeconds;
        var steps = (int)Math.Round(totalTime / dtImu);

        var freeFilter = new SixStateTrajectoryKalmanFilter(
            0.0, Vector3d.Zero, new Vector3d(10.0, 0.0, 0.0), Quaterniond.Identity,
            initialPositionSigmaMetres: 0.1, initialVelocitySigmaMetresPerSecond: 0.05);
        var aidedFilter = new SixStateTrajectoryKalmanFilter(
            0.0, Vector3d.Zero, new Vector3d(10.0, 0.0, 0.0), Quaterniond.Identity,
            initialPositionSigmaMetres: 0.1, initialVelocitySigmaMetresPerSecond: 0.05);

        var freeEstRot = Quaterniond.Identity;
        var aidedEstRot = Quaterniond.Identity;
        var truePos = Vector3d.Zero;
        const double cruiseSpeed = 10.0;
        Vector3d finalTruePos = Vector3d.Zero;

        for (var step = 1; step <= steps; step++)
        {
            var t = step * dtImu;
            var inTunnel = t > preTunnelSeconds;
            var tunnelTime = t - preTunnelSeconds;
            var stopped = inTunnel && tunnelTime >= stopOffset && tunnelTime < stopOffset + stopDuration;

            var speed = stopped ? 0.0 : cruiseSpeed;
            var trueVel = new Vector3d(speed, 0.0, 0.0);
            truePos += trueVel * dtImu;
            finalTruePos = truePos;

            var idealForce = ImuIdealKinematics.ComputeSpecificForceBody(Quaterniond.Identity, Vector3d.Zero);
            var imuObs = imuSim.Observe(t, dtImu, idealForce, Vector3d.Zero);

            var freeDeltaAngle = imuObs.AngularVelocityBody.Length * dtImu;
            if (freeDeltaAngle > 1e-12)
                freeEstRot = freeEstRot * Quaterniond.FromAxisAngle(imuObs.AngularVelocityBody.Normalized(), freeDeltaAngle);

            var compensatedOmega = imuObs.AngularVelocityBody - zuptDetector.EstimatedGyroBiasRadiansPerSecond;
            var aidedDeltaAngle = compensatedOmega.Length * dtImu;
            if (aidedDeltaAngle > 1e-12)
                aidedEstRot = aidedEstRot * Quaterniond.FromAxisAngle(compensatedOmega.Normalized(), aidedDeltaAngle);

            freeFilter.Predict(dtImu, imuObs.SpecificForceBody, freeEstRot, grade);
            aidedFilter.Predict(dtImu, imuObs.SpecificForceBody, aidedEstRot, grade);

            // 20 Hz DMI and ZUPT check (every 5 IMU steps)
            if (step % 5 == 0)
            {
                var dmiObs = dmiSim.Observe(t, 0.05, speed);
                aidedFilter.UpdateDmiVelocity(dmiObs, aidedEstRot);

                var zupt = zuptDetector.Evaluate(0.05, dmiObs.MeasuredForwardSpeedMetresPerSecond, imuObs);
                if (zupt.ZuptTriggered)
                    aidedFilter.UpdateZupt(zupt.VelocitySigmaMetresPerSecond);
            }

            // 1 Hz GNSS outside tunnel (every 100 IMU steps)
            if (!inTunnel && step % 100 == 0)
            {
                var env = new GnssEnvironment(0.95, Vector3d.Zero, true);
                var gnss = gnssSim.Observe(t, truePos, GnssProfile.RtkFix, env, Vector3d.Zero);
                freeFilter.UpdateGnssPosition(gnss);
                aidedFilter.UpdateGnssPosition(gnss);
            }
        }

        return (
            (freeFilter.PositionEnu - finalTruePos).Length,
            (aidedFilter.PositionEnu - finalTruePos).Length,
            aidedFilter.PositionSigma3DMetres);
    }
}

public sealed record DualAntennaHeadingObservation(
    double TimestampSeconds,
    bool IsAvailable,
    bool IsDegradedSingleAntenna,
    double? MeasuredYawRadians,
    double? HeadingSigmaRadians,
    double BaselineMetres,
    string ReasonKey);

/// <summary>Dual-antenna GNSS heading (1.0 m baseline, Level 5+) supporting stationary yaw observation with single-antenna degradation handling.</summary>
public sealed class DualAntennaHeadingSensor
{
    public const double NominalBaselineMetres = 1.0;
    public const int RequiredPlatformLevel = 5;
    public const double BaseHeadingSigmaRadians = 0.5 * Math.PI / 180.0;

    private readonly GaussianRandom _rng;

    public DualAntennaHeadingSensor(ulong seed = 7771UL)
    {
        _rng = new GaussianRandom(seed);
    }

    public DualAntennaHeadingObservation Observe(
        double timestampSeconds,
        double trueYawRadians,
        double vehicleSpeedMetresPerSecond,
        int platformLevel,
        bool secondaryAntennaSignalAvailable,
        double openness = 1.0)
    {
        if (!double.IsFinite(timestampSeconds) || timestampSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(timestampSeconds));
        if (!double.IsFinite(trueYawRadians))
            throw new ArgumentOutOfRangeException(nameof(trueYawRadians));
        if (!double.IsFinite(vehicleSpeedMetresPerSecond))
            throw new ArgumentOutOfRangeException(nameof(vehicleSpeedMetresPerSecond));

        if (platformLevel < RequiredPlatformLevel)
        {
            return new DualAntennaHeadingObservation(
                timestampSeconds, false, false, null, null, NominalBaselineMetres, "DUAL_ANTENNA_REQUIRES_LEVEL_5");
        }

        if (!secondaryAntennaSignalAvailable || openness < RtkSolutionStateMachine.MinimumOpennessForSignal)
        {
            return new DualAntennaHeadingObservation(
                timestampSeconds, false, true, null, null, NominalBaselineMetres, "DUAL_ANTENNA_SECONDARY_LOST");
        }

        // Works independently of linear velocity (including stationary 0 m/s) along the 1.0 m baseline.
        _ = vehicleSpeedMetresPerSecond;
        var sigma = BaseHeadingSigmaRadians / Math.Max(0.4, Math.Min(1.0, openness));
        var measured = HeadingSimulator.WrapRadians(trueYawRadians + sigma * _rng.NextGaussian());
        return new DualAntennaHeadingObservation(
            timestampSeconds, true, false, measured, sigma, NominalBaselineMetres, "DUAL_ANTENNA_OK");
    }

    /// <summary>Fuses a valid dual-antenna heading measurement into an estimated yaw angle without accessing truth.</summary>
    public static (double EstimatedYawRadians, double YawSigmaRadians, bool Updated) FuseEstimatedYawRadians(
        double priorEstimatedYawRadians,
        double priorYawSigmaRadians,
        DualAntennaHeadingObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (!observation.IsAvailable || observation.MeasuredYawRadians is null || observation.HeadingSigmaRadians is null)
            return (HeadingSimulator.WrapRadians(priorEstimatedYawRadians), priorYawSigmaRadians, false);

        var innovation = HeadingSimulator.WrapRadians(observation.MeasuredYawRadians.Value - priorEstimatedYawRadians);
        var priorVar = priorYawSigmaRadians * priorYawSigmaRadians;
        var measVar = observation.HeadingSigmaRadians.Value * observation.HeadingSigmaRadians.Value;
        var gain = priorVar / (priorVar + measVar);
        var updatedYaw = HeadingSimulator.WrapRadians(priorEstimatedYawRadians + gain * innovation);
        var updatedSigma = Math.Sqrt((1.0 - gain) * priorVar);
        return (updatedYaw, updatedSigma, true);
    }
}

public sealed record SurveyControlObservation(
    string PointId,
    double TimestampSeconds,
    Vector3d KnownPositionEnu,
    double DeclaredSigmaMetres,
    bool IsCheckpoint = false);

public sealed record ControlPointAdjustmentOutcome(
    string PointId,
    bool Applied,
    bool RejectedAsCqCheckpoint,
    bool FlaggedAsOutlier,
    double MahalanobisDistance,
    string ReasonKey);

public sealed record PpkProcessingResult(
    bool Succeeded,
    bool DifferentialBaseApplied,
    string DiagnosticCode,
    IReadOnlyList<TrajectoryEstimateSample> ForwardTrajectory,
    IReadOnlyList<TrajectoryEstimateSample> SmoothedTrajectory,
    IReadOnlyList<ControlPointAdjustmentOutcome> ControlOutcomes);

/// <summary>Post-processed kinematic (PPK) GNSS base common-error removal, control point adjustment, and 6-state Rauch-Tung-Striebel backward smoother.</summary>
public static class PpkRtsSmootherAndControlAdjuster
{
    public const double DefaultMaxStalenessSeconds = 2.0;
    public const double DefaultMaxBaselineMetres = 10000.0;
    public const double PpkBaselineDecorrelationMetresPerKm = 0.005;

    public static PpkProcessingResult ProcessTrajectory(
        Vector3d initialPositionEnu,
        Vector3d initialVelocityEnu,
        Quaterniond initialBodyToMapRotation,
        ImuGrade imuGrade,
        IReadOnlyList<ImuObservation> imuObservations,
        IReadOnlyList<CorrectableGnssObservationRecord> roverGnssRecords,
        BaseStationLog? baseStationLog,
        IReadOnlyList<(double TimestampSeconds, Quaterniond BodyToMapRotation)>? estimatedRotations = null,
        IReadOnlyList<WheelDmiObservation>? dmiObservations = null,
        IReadOnlyList<SurveyControlObservation>? surveyControls = null,
        IReadOnlySet<string>? reservedCqCheckpointIds = null,
        double maxStalenessSeconds = DefaultMaxStalenessSeconds,
        double maxBaselineMetres = DefaultMaxBaselineMetres)
    {
        ArgumentNullException.ThrowIfNull(imuObservations);
        ArgumentNullException.ThrowIfNull(roverGnssRecords);
        if (imuObservations.Count == 0)
            throw new ArgumentException("At least one IMU observation is required.", nameof(imuObservations));

        var orderedImu = imuObservations.OrderBy(i => i.TimestampSeconds).ToArray();
        var gnssByIdx = MapByNearestImuStep(orderedImu, roverGnssRecords, r => r.TimestampSeconds);
        var dmiByIdx = dmiObservations is not null
            ? MapByNearestImuStep(orderedImu, dmiObservations, d => d.TimestampSeconds)
            : new Dictionary<int, WheelDmiObservation>();
        var controlByIdx = surveyControls is not null
            ? MapListByNearestImuStep(orderedImu, surveyControls, c => c.TimestampSeconds)
            : new Dictionary<int, List<SurveyControlObservation>>();
        var rotByIdx = estimatedRotations is not null
            ? MapRotationsByStep(orderedImu, estimatedRotations, initialBodyToMapRotation)
            : Array.Empty<Quaterniond>();

        // 1. Evaluate raw live/real-time forward pass first (baseline when PPK is unavailable)
        var (liveSamples, _, _) = RunForwardPass(
            initialPositionEnu, initialVelocityEnu, initialBodyToMapRotation, imuGrade,
            orderedImu, rotByIdx, gnssByIdx, dmiByIdx, controlByIdx, reservedCqCheckpointIds,
            baseStationLog: null, applyDifferentialBase: false, maxStalenessSeconds, maxBaselineMetres);

        // 2. Validate PPK base station log presence & coverage
        if (baseStationLog is null || baseStationLog.Samples.Count == 0)
        {
            return new PpkProcessingResult(
                Succeeded: false,
                DifferentialBaseApplied: false,
                DiagnosticCode: "PPK_BASE_LOG_MISSING",
                ForwardTrajectory: liveSamples,
                SmoothedTrajectory: liveSamples,
                ControlOutcomes: Array.Empty<ControlPointAdjustmentOutcome>());
        }

        var availableRecords = roverGnssRecords
            .Where(r => r.LiveSolutionKind != GnssSolutionKind.Unavailable &&
                        r.Openness >= RtkSolutionStateMachine.MinimumOpennessForSignal)
            .ToArray();

        if (availableRecords.Length == 0)
        {
            return new PpkProcessingResult(
                Succeeded: false,
                DifferentialBaseApplied: false,
                DiagnosticCode: "PPK_NO_AVAILABLE_ROVER_EPOCHS",
                ForwardTrajectory: liveSamples,
                SmoothedTrajectory: liveSamples,
                ControlOutcomes: Array.Empty<ControlPointAdjustmentOutcome>());
        }

        var validBaseMatches = availableRecords.Count(r =>
            baseStationLog.TryInterpolateCorrection(r.TimestampSeconds, maxStalenessSeconds, out _) &&
            (r.BaselineMetres < 0.0 || r.BaselineMetres <= maxBaselineMetres));

        var baseMinT = baseStationLog.Samples[0].TimestampSeconds;
        var baseMaxT = baseStationLog.Samples[^1].TimestampSeconds;
        var roverMinT = availableRecords.Min(r => r.TimestampSeconds);
        var roverMaxT = availableRecords.Max(r => r.TimestampSeconds);

        if (validBaseMatches == 0 ||
            baseMinT > roverMinT + maxStalenessSeconds ||
            baseMaxT < roverMaxT - maxStalenessSeconds)
        {
            return new PpkProcessingResult(
                Succeeded: false,
                DifferentialBaseApplied: false,
                DiagnosticCode: "PPK_BASE_WINDOW_INCOMPATIBLE",
                ForwardTrajectory: liveSamples,
                SmoothedTrajectory: liveSamples,
                ControlOutcomes: Array.Empty<ControlPointAdjustmentOutcome>());
        }

        // 3. Forward PPK pass + Control Points (rejecting CQ checkpoints and 3-sigma outliers)
        var (ppkForwardSamples, records, controlOutcomes) = RunForwardPass(
            initialPositionEnu, initialVelocityEnu, initialBodyToMapRotation, imuGrade,
            orderedImu, rotByIdx, gnssByIdx, dmiByIdx, controlByIdx, reservedCqCheckpointIds,
            baseStationLog, applyDifferentialBase: true, maxStalenessSeconds, maxBaselineMetres);

        // 4. Backward Rauch-Tung-Striebel (RTS) 6-state smoother recursion
        var n = records.Count;
        var xSmooth = new double[n][];
        var pSmooth = new double[n][,];
        xSmooth[n - 1] = MatrixOps6.CloneVec(records[n - 1].StateFiltered);
        pSmooth[n - 1] = MatrixOps6.CloneMat(records[n - 1].CovFiltered);

        for (var k = n - 2; k >= 0; k--)
        {
            var fk = records[k].FToNext!;
            var xPredNext = records[k].StatePredictedNext!;
            var pPredNext = records[k].CovPredictedNext!;
            var xFilt = records[k].StateFiltered;
            var pFilt = records[k].CovFiltered;

            var pPredInv = MatrixOps6.Invert(pPredNext);
            // C_k = P_{k|k} F_k^T P_{k+1|k}^-1
            var ck = MatrixOps6.Multiply(MatrixOps6.MultiplyTransposeB(pFilt, fk), pPredInv);

            var dxNext = new double[6];
            for (var i = 0; i < 6; i++)
                dxNext[i] = xSmooth[k + 1][i] - xPredNext[i];

            var corr = MatrixOps6.MultiplyVec(ck, dxNext);
            var xk = new double[6];
            for (var i = 0; i < 6; i++)
                xk[i] = xFilt[i] + corr[i];
            xSmooth[k] = xk;

            var dpNext = MatrixOps6.Subtract(pSmooth[k + 1], pPredNext);
            var ckDpCkT = MatrixOps6.MultiplyTransposeB(MatrixOps6.Multiply(ck, dpNext), ck);
            pSmooth[k] = MatrixOps6.SymmetrizeAndFloor(MatrixOps6.Add(pFilt, ckDpCkT));
        }

        var smoothedSamples = new List<TrajectoryEstimateSample>(n);
        for (var k = 0; k < n; k++)
        {
            var x = xSmooth[k];
            var p = pSmooth[k];
            var rec = records[k];
            var posSig = new Vector3d(Math.Sqrt(p[0, 0]), Math.Sqrt(p[1, 1]), Math.Sqrt(p[2, 2]));
            var velSig = new Vector3d(Math.Sqrt(p[3, 3]), Math.Sqrt(p[4, 4]), Math.Sqrt(p[5, 5]));
            smoothedSamples.Add(new TrajectoryEstimateSample(
                rec.TimestampSeconds,
                new Vector3d(x[0], x[1], x[2]),
                new Vector3d(x[3], x[4], x[5]),
                rec.BodyToMapRotation,
                posSig,
                velSig,
                rec.GnssAgeSeconds,
                rec.BlackoutAlert,
                posSig.Length >= SixStateTrajectoryKalmanFilter.DefaultHighUncertaintyThresholdMetres));
        }

        return new PpkProcessingResult(
            Succeeded: true,
            DifferentialBaseApplied: true,
            DiagnosticCode: "PPK_RTS_COMPLETED",
            ForwardTrajectory: ppkForwardSamples,
            SmoothedTrajectory: Array.AsReadOnly(smoothedSamples.ToArray()),
            ControlOutcomes: Array.AsReadOnly(controlOutcomes.ToArray()));
    }

    private static (IReadOnlyList<TrajectoryEstimateSample> Samples, List<KalmanStepTransitionRecord> Transitions, List<ControlPointAdjustmentOutcome> Controls) RunForwardPass(
        Vector3d initialPositionEnu,
        Vector3d initialVelocityEnu,
        Quaterniond initialBodyToMapRotation,
        ImuGrade imuGrade,
        ImuObservation[] orderedImu,
        Quaterniond[] rotByIdx,
        Dictionary<int, CorrectableGnssObservationRecord> gnssByIdx,
        Dictionary<int, WheelDmiObservation> dmiByIdx,
        Dictionary<int, List<SurveyControlObservation>> controlByIdx,
        IReadOnlySet<string>? reservedCqCheckpointIds,
        BaseStationLog? baseStationLog,
        bool applyDifferentialBase,
        double maxStalenessSeconds,
        double maxBaselineMetres)
    {
        var filter = new SixStateTrajectoryKalmanFilter(
            orderedImu[0].TimestampSeconds,
            initialPositionEnu,
            initialVelocityEnu,
            initialBodyToMapRotation,
            initialPositionSigmaMetres: 0.25,
            initialVelocitySigmaMetresPerSecond: 0.15);

        var zuptDetector = new StationaryZuptDetector();
        var samples = new List<TrajectoryEstimateSample>(orderedImu.Length);
        var transitions = new List<KalmanStepTransitionRecord>(orderedImu.Length);
        var controlOutcomes = new List<ControlPointAdjustmentOutcome>();

        for (var idx = 0; idx < orderedImu.Length; idx++)
        {
            var imu = orderedImu[idx];
            var rot = rotByIdx.Length == orderedImu.Length ? rotByIdx[idx] : initialBodyToMapRotation;
            var (f, xPred, pPred) = filter.Predict(imu.DtSeconds, imu.SpecificForceBody, rot, imuGrade);

            if (transitions.Count > 0)
            {
                transitions[^1].FToNext = f;
                transitions[^1].StatePredictedNext = xPred;
                transitions[^1].CovPredictedNext = pPred;
            }

            if (dmiByIdx.TryGetValue(idx, out var dmi))
            {
                filter.UpdateDmiVelocity(dmi, rot);
                var zupt = zuptDetector.Evaluate(dmi.DtSeconds, dmi.MeasuredForwardSpeedMetresPerSecond, imu);
                if (zupt.ZuptTriggered)
                    filter.UpdateZupt(zupt.VelocitySigmaMetresPerSecond);
            }

            if (gnssByIdx.TryGetValue(idx, out var rawRecord) &&
                rawRecord.LiveSolutionKind != GnssSolutionKind.Unavailable &&
                rawRecord.Openness >= RtkSolutionStateMachine.MinimumOpennessForSignal)
            {
                if (applyDifferentialBase && baseStationLog is not null &&
                    baseStationLog.TryInterpolateCorrection(rawRecord.TimestampSeconds, maxStalenessSeconds, out var baseCorr) &&
                    (rawRecord.BaselineMetres < 0.0 || rawRecord.BaselineMetres <= maxBaselineMetres))
                {
                    var correctedPos = rawRecord.UncorrectedRoverPositionEnu + baseCorr;
                    var baselineKm = Math.Max(0.0, rawRecord.BaselineMetres) / 1000.0;
                    var isCarrierPpkGrade = string.Equals(rawRecord.ProfileId, GnssProfile.RtkFix.Id, StringComparison.Ordinal) ||
                                            string.Equals(rawRecord.ProfileId, GnssProfile.RtkFloat.Id, StringComparison.Ordinal) ||
                                            string.Equals(rawRecord.ProfileId, "gnss_ppk", StringComparison.Ordinal);
                    var baseCodeOrPhaseSigma = isCarrierPpkGrade
                        ? GnssProfile.RtkFix.BaseSigmaMetres
                        : 0.35;
                    var ppkSigma = (baseCodeOrPhaseSigma + PpkBaselineDecorrelationMetresPerKm * baselineKm)
                                   / Math.Max(0.5, rawRecord.Openness);
                    var ppkObs = new GnssObservation(
                        rawRecord.Sequence, rawRecord.TimestampSeconds, "gnss_ppk",
                        GnssSolutionKind.RtkFix, correctedPos, 1.0, ppkSigma);
                    filter.UpdateGnssPosition(ppkObs, explicitSigmaOverrideMetres: ppkSigma);
                }
                else if (rawRecord.LiveMeasuredPositionEnu is not null)
                {
                    var liveProfile = rawRecord.LiveSolutionKind switch
                    {
                        GnssSolutionKind.RtkFix => GnssProfile.RtkFix,
                        GnssSolutionKind.RtkFloat => GnssProfile.RtkFloat,
                        GnssSolutionKind.Dgnss => GnssProfile.Dgnss,
                        _ => GnssProfile.Navigation,
                    };
                    var liveObs = new GnssObservation(
                        rawRecord.Sequence, rawRecord.TimestampSeconds, rawRecord.ProfileId,
                        rawRecord.LiveSolutionKind, rawRecord.LiveMeasuredPositionEnu, 1.2, liveProfile.BaseSigmaMetres);
                    filter.UpdateGnssPosition(liveObs);
                }
            }

            if (controlByIdx.TryGetValue(idx, out var controls))
            {
                foreach (var control in controls)
                {
                    var isReservedCheckpoint = control.IsCheckpoint ||
                                               (reservedCqCheckpointIds?.Contains(control.PointId) == true);
                    if (isReservedCheckpoint)
                    {
                        controlOutcomes.Add(new ControlPointAdjustmentOutcome(
                            control.PointId, false, true, false, 0.0, "CQ_CHECKPOINT_FORBIDDEN_IN_ESTIMATION"));
                        continue;
                    }

                    var accepted = filter.UpdatePositionControlPoint(
                        control.KnownPositionEnu, control.DeclaredSigmaMetres, out var mDist);
                    controlOutcomes.Add(accepted
                        ? new ControlPointAdjustmentOutcome(control.PointId, true, false, false, mDist, "CONTROL_APPLIED")
                        : new ControlPointAdjustmentOutcome(control.PointId, false, false, true, mDist, "CONTROL_OUTLIER_3SIGMA"));
                }
            }

            var sample = filter.GetCurrentSample();
            samples.Add(sample);
            var (xFilt, pFilt) = filter.ExportInternalState();
            transitions.Add(new KalmanStepTransitionRecord(
                imu.TimestampSeconds, xFilt, pFilt, rot, sample.GnssSolutionAgeSeconds, sample.BlackoutAlertActive));
        }

        return (Array.AsReadOnly(samples.ToArray()), transitions, controlOutcomes);
    }

    private static Dictionary<int, T> MapByNearestImuStep<T>(
        ImuObservation[] orderedImu,
        IReadOnlyList<T> items,
        Func<T, double> getTimestamp)
    {
        var map = new Dictionary<int, T>();
        foreach (var item in items)
        {
            var idx = FindNearestStepIndex(orderedImu, getTimestamp(item));
            map[idx] = item;
        }
        return map;
    }

    private static Dictionary<int, List<T>> MapListByNearestImuStep<T>(
        ImuObservation[] orderedImu,
        IReadOnlyList<T> items,
        Func<T, double> getTimestamp)
    {
        var map = new Dictionary<int, List<T>>();
        foreach (var item in items)
        {
            var idx = FindNearestStepIndex(orderedImu, getTimestamp(item));
            if (!map.TryGetValue(idx, out var list))
            {
                list = [];
                map[idx] = list;
            }
            list.Add(item);
        }
        return map;
    }

    private static Quaterniond[] MapRotationsByStep(
        ImuObservation[] orderedImu,
        IReadOnlyList<(double TimestampSeconds, Quaterniond BodyToMapRotation)> rotations,
        Quaterniond fallback)
    {
        if (rotations.Count == orderedImu.Length)
            return rotations.Select(r => r.BodyToMapRotation).ToArray();

        var result = new Quaterniond[orderedImu.Length];
        if (rotations.Count == 0)
        {
            Array.Fill(result, fallback);
            return result;
        }

        var orderedRot = rotations.OrderBy(r => r.TimestampSeconds).ToArray();
        var cursor = 0;
        for (var i = 0; i < orderedImu.Length; i++)
        {
            var t = orderedImu[i].TimestampSeconds;
            while (cursor + 1 < orderedRot.Length &&
                   Math.Abs(orderedRot[cursor + 1].TimestampSeconds - t) <= Math.Abs(orderedRot[cursor].TimestampSeconds - t))
            {
                cursor++;
            }
            result[i] = orderedRot[cursor].BodyToMapRotation;
        }
        return result;
    }

    private static int FindNearestStepIndex(ImuObservation[] orderedImu, double targetTimestamp)
    {
        var lo = 0;
        var hi = orderedImu.Length - 1;
        while (lo < hi)
        {
            var mid = lo + (hi - lo) / 2;
            if (orderedImu[mid].TimestampSeconds < targetTimestamp)
                lo = mid + 1;
            else
                hi = mid;
        }
        if (lo > 0 && Math.Abs(orderedImu[lo - 1].TimestampSeconds - targetTimestamp) <
                     Math.Abs(orderedImu[lo].TimestampSeconds - targetTimestamp))
        {
            return lo - 1;
        }
        return lo;
    }
}
