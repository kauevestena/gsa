using Gsa.Core.Geometry;
using Gsa.Core.Randomness;
using Gsa.Core.Sensors.Gnss;

namespace Gsa.Core.Sensors.Fusion;

public enum ImuGrade
{
    Ideal = 0,
    Mems = 1,
    Tactical = 2,
    Navigation = 3,
}

public sealed record ImuProfile(
    ImuGrade Grade,
    string Id,
    double SamplingRateHz,
    double GyroNoiseSigmaRadiansPerSecond,
    double AccelNoiseSigmaMetresPerSecondSquared,
    double GyroInitialBiasSigmaRadiansPerSecond,
    double AccelInitialBiasSigmaMetresPerSecondSquared,
    double GyroBiasWalkPerSqrtSecond,
    double AccelBiasWalkPerSqrtSecond,
    double ScaleFactorErrorSigma)
{
    public const double NominalSamplingRateHz = 100.0;
    public const double GravityMetresPerSecondSquared = 9.80665;

    public static ImuProfile Ideal { get; } = new(
        ImuGrade.Ideal, "imu_ideal", NominalSamplingRateHz,
        0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0);

    public static ImuProfile Mems { get; } = new(
        ImuGrade.Mems, "imu_mems", NominalSamplingRateHz,
        GyroNoiseSigmaRadiansPerSecond: 0.005,
        AccelNoiseSigmaMetresPerSecondSquared: 0.1,
        GyroInitialBiasSigmaRadiansPerSecond: 0.002,
        AccelInitialBiasSigmaMetresPerSecondSquared: 0.05,
        GyroBiasWalkPerSqrtSecond: 0.002 * 0.01,
        AccelBiasWalkPerSqrtSecond: 0.05 * 0.01,
        ScaleFactorErrorSigma: 0.001);

    public static ImuProfile Tactical { get; } = new(
        ImuGrade.Tactical, "imu_tactical", NominalSamplingRateHz,
        GyroNoiseSigmaRadiansPerSecond: 0.001,
        AccelNoiseSigmaMetresPerSecondSquared: 0.03,
        GyroInitialBiasSigmaRadiansPerSecond: 0.0005,
        AccelInitialBiasSigmaMetresPerSecondSquared: 0.01,
        GyroBiasWalkPerSqrtSecond: 0.0005 * 0.01,
        AccelBiasWalkPerSqrtSecond: 0.01 * 0.01,
        ScaleFactorErrorSigma: 0.0003);

    public static ImuProfile Navigation { get; } = new(
        ImuGrade.Navigation, "imu_navigation", NominalSamplingRateHz,
        GyroNoiseSigmaRadiansPerSecond: 0.0002,
        AccelNoiseSigmaMetresPerSecondSquared: 0.01,
        GyroInitialBiasSigmaRadiansPerSecond: 0.0001,
        AccelInitialBiasSigmaMetresPerSecondSquared: 0.003,
        GyroBiasWalkPerSqrtSecond: 0.0001 * 0.01,
        AccelBiasWalkPerSqrtSecond: 0.003 * 0.01,
        ScaleFactorErrorSigma: 0.0001);

    public static ImuProfile ForGrade(ImuGrade grade) => grade switch
    {
        ImuGrade.Ideal => Ideal,
        ImuGrade.Mems => Mems,
        ImuGrade.Tactical => Tactical,
        ImuGrade.Navigation => Navigation,
        _ => throw new ArgumentOutOfRangeException(nameof(grade))
    };
}

/// <summary>Raw IMU measurement observation only; does not expose synthetic bias/scale/truth to estimators.</summary>
public sealed record ImuObservation(
    long Sequence,
    double TimestampSeconds,
    double DtSeconds,
    string ProfileId,
    Vector3d SpecificForceBody,
    Vector3d AngularVelocityBody);

public readonly record struct ImuSimulatorState(
    GaussianRandomState WhiteNoiseRng,
    GaussianRandomState WalkRng,
    Vector3d GyroBias,
    Vector3d AccelBias,
    Vector3d GyroScaleError,
    Vector3d AccelScaleError,
    long NextSequence);

public static class ImuIdealKinematics
{
    public static Vector3d GravityEnu { get; } = new(0.0, 0.0, -ImuProfile.GravityMetresPerSecondSquared);

    /// <summary>Specific force measured by an accelerometer fixed in the body frame under ENU orientation.</summary>
    public static Vector3d ComputeSpecificForceBody(Quaterniond bodyToMap, Vector3d linearAccelerationEnu)
    {
        bodyToMap.EnsureValid();
        return bodyToMap.Inverse().Rotate(linearAccelerationEnu - GravityEnu);
    }
}

/// <summary>Synthesizes IMU measurements (100 Hz) with scale factor, evolving bias random walk, and Gaussian sample noise.</summary>
public sealed class ImuSimulator
{
    private readonly GaussianRandom _whiteNoise;
    private readonly GaussianRandom _walkNoise;
    private Vector3d _gyroBias;
    private Vector3d _accelBias;
    private readonly Vector3d _gyroScaleError;
    private readonly Vector3d _accelScaleError;
    private long _nextSequence;

    public ImuProfile Profile { get; }

    public ImuSimulator(
        ImuProfile profile,
        ulong whiteNoiseSeed,
        ulong biasSeed,
        Vector3d? initialGyroBiasOverride = null,
        Vector3d? initialAccelBiasOverride = null,
        Vector3d? gyroScaleOverride = null,
        Vector3d? accelScaleOverride = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Profile = profile;
        _whiteNoise = new GaussianRandom(whiteNoiseSeed);
        _walkNoise = new GaussianRandom(biasSeed);

        _gyroBias = initialGyroBiasOverride ?? SampleTriplet(_walkNoise, profile.GyroInitialBiasSigmaRadiansPerSecond);
        _accelBias = initialAccelBiasOverride ?? SampleTriplet(_walkNoise, profile.AccelInitialBiasSigmaMetresPerSecondSquared);
        _gyroScaleError = gyroScaleOverride ?? SampleTriplet(_walkNoise, profile.ScaleFactorErrorSigma);
        _accelScaleError = accelScaleOverride ?? SampleTriplet(_walkNoise, profile.ScaleFactorErrorSigma);
    }

    public ImuSimulator(ImuProfile profile, ImuSimulatorState state)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (state.NextSequence < 0)
            throw new ArgumentOutOfRangeException(nameof(state));

        Profile = profile;
        _whiteNoise = new GaussianRandom(state.WhiteNoiseRng);
        _walkNoise = new GaussianRandom(state.WalkRng);
        _gyroBias = state.GyroBias;
        _accelBias = state.AccelBias;
        _gyroScaleError = state.GyroScaleError;
        _accelScaleError = state.AccelScaleError;
        _nextSequence = state.NextSequence;
    }

    public ImuSimulatorState ExportState() =>
        new(
            _whiteNoise.ExportState(),
            _walkNoise.ExportState(),
            _gyroBias,
            _accelBias,
            _gyroScaleError,
            _accelScaleError,
            _nextSequence);

    public ImuObservation Observe(
        double timestampSeconds,
        double dtSeconds,
        Vector3d idealSpecificForceBody,
        Vector3d idealAngularVelocityBody)
    {
        if (!double.IsFinite(timestampSeconds) || timestampSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(timestampSeconds));
        if (!double.IsFinite(dtSeconds) || dtSeconds <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(dtSeconds));

        var sqrtDt = Math.Sqrt(dtSeconds);
        _gyroBias += SampleTriplet(_walkNoise, Profile.GyroBiasWalkPerSqrtSecond * sqrtDt);
        _accelBias += SampleTriplet(_walkNoise, Profile.AccelBiasWalkPerSqrtSecond * sqrtDt);

        var gyroMeas = ApplySensorModel(
            idealAngularVelocityBody,
            _gyroScaleError,
            _gyroBias,
            SampleTriplet(_whiteNoise, Profile.GyroNoiseSigmaRadiansPerSecond));

        var accelMeas = ApplySensorModel(
            idealSpecificForceBody,
            _accelScaleError,
            _accelBias,
            SampleTriplet(_whiteNoise, Profile.AccelNoiseSigmaMetresPerSecondSquared));

        var seq = _nextSequence;
        _nextSequence = checked(_nextSequence + 1);

        return new ImuObservation(
            seq,
            timestampSeconds,
            dtSeconds,
            Profile.Id,
            accelMeas,
            gyroMeas);
    }

    private static Vector3d ApplySensorModel(Vector3d ideal, Vector3d scaleError, Vector3d bias, Vector3d whiteNoise) =>
        new(
            (1.0 + scaleError.X) * ideal.X + bias.X + whiteNoise.X,
            (1.0 + scaleError.Y) * ideal.Y + bias.Y + whiteNoise.Y,
            (1.0 + scaleError.Z) * ideal.Z + bias.Z + whiteNoise.Z);

    private static Vector3d SampleTriplet(GaussianRandom rng, double sigma) =>
        sigma == 0.0
            ? Vector3d.Zero
            : new Vector3d(sigma * rng.NextGaussian(), sigma * rng.NextGaussian(), sigma * rng.NextGaussian());
}

public sealed record AttitudeEstimateSnapshot(
    double TimestampSeconds,
    Quaterniond EstimatedBodyToMap,
    double RollRadians,
    double PitchRadians,
    double YawRadians,
    bool GravityCorrectionApplied,
    bool YawCorrectionApplied);

/// <summary>Estimates BodyToMap attitude via gyro integration and complementary gravity/heading fusion without truth inputs.</summary>
public sealed class ComplementaryAttitudeEstimator
{
    public const double ComplementaryTimeConstantSeconds = 2.0;
    public const double StationaryGravityToleranceMetresPerSecondSquared = 0.1;

    public Quaterniond EstimatedBodyToMap { get; private set; }
    public double LastTimestampSeconds { get; private set; }
    public int GravityCorrectionsApplied { get; private set; }
    public int YawCorrectionsApplied { get; private set; }

    public ComplementaryAttitudeEstimator(Quaterniond initialBodyToMap, double initialTimestampSeconds = 0.0)
    {
        initialBodyToMap.EnsureValid();
        if (!double.IsFinite(initialTimestampSeconds) || initialTimestampSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(initialTimestampSeconds));

        EstimatedBodyToMap = Normalize(initialBodyToMap);
        LastTimestampSeconds = initialTimestampSeconds;
    }

    public static double ComputeComplementaryGain(double dtSeconds)
    {
        if (!double.IsFinite(dtSeconds) || dtSeconds <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(dtSeconds));
        return 1.0 - Math.Exp(-dtSeconds / ComplementaryTimeConstantSeconds);
    }

    public AttitudeEstimateSnapshot Step(
        ImuObservation observation,
        bool isStationary,
        HeadingObservation? headingObservation = null)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (!double.IsFinite(observation.DtSeconds) || observation.DtSeconds <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(observation));

        // 1. Propagate orientation via body-frame gyro integration
        var deltaAngleVec = observation.AngularVelocityBody * observation.DtSeconds;
        var angle = deltaAngleVec.Length;
        if (angle > 1e-12)
        {
            var deltaQ = Quaterniond.FromAxisAngle(deltaAngleVec * (1.0 / angle), angle);
            EstimatedBodyToMap = Normalize(EstimatedBodyToMap * deltaQ);
        }

        var gain = ComputeComplementaryGain(observation.DtSeconds);
        var gravityApplied = false;
        var yawApplied = false;

        // 2. Stationary roll/pitch correction using specific force magnitude ~= local gravity
        if (isStationary)
        {
            var accelMag = observation.SpecificForceBody.Length;
            if (accelMag > 1e-3 &&
                Math.Abs(accelMag - ImuProfile.GravityMetresPerSecondSquared) < StationaryGravityToleranceMetresPerSecondSquared)
            {
                var estimatedUpMap = EstimatedBodyToMap.Rotate(observation.SpecificForceBody).Normalized();
                var dot = Math.Clamp(Vector3d.Dot(estimatedUpMap, Vector3d.UnitZ), -1.0, 1.0);
                var tiltErrorAngle = Math.Acos(dot);
                var tiltAxis = Vector3d.Cross(estimatedUpMap, Vector3d.UnitZ);

                if (tiltAxis.Length > 1e-10 && tiltErrorAngle > 1e-10)
                {
                    var corr = Quaterniond.FromAxisAngle(tiltAxis.Normalized(), gain * tiltErrorAngle);
                    EstimatedBodyToMap = Normalize(corr * EstimatedBodyToMap);
                }
                gravityApplied = true;
                GravityCorrectionsApplied++;
            }
        }

        // 3. Heading yaw complementary correction from valid GNSS/dual-antenna heading
        if (headingObservation is { IsAvailable: true, MeasuredRadians: { } measuredHeading })
        {
            var forwardMap = EstimatedBodyToMap.Rotate(Vector3d.UnitX);
            var horizLen = Math.Sqrt(forwardMap.X * forwardMap.X + forwardMap.Y * forwardMap.Y);
            if (horizLen > 1e-6)
            {
                var estimatedYaw = Math.Atan2(forwardMap.Y, forwardMap.X);
                var yawError = HeadingSimulator.WrapRadians(measuredHeading - estimatedYaw);
                var yawCorr = Quaterniond.FromAxisAngle(Vector3d.UnitZ, gain * yawError);
                EstimatedBodyToMap = Normalize(yawCorr * EstimatedBodyToMap);
                yawApplied = true;
                YawCorrectionsApplied++;
            }
        }

        LastTimestampSeconds = observation.TimestampSeconds;
        var (roll, pitch, yaw) = ExtractRollPitchYaw(EstimatedBodyToMap);
        return new AttitudeEstimateSnapshot(
            observation.TimestampSeconds,
            EstimatedBodyToMap,
            roll,
            pitch,
            yaw,
            gravityApplied,
            yawApplied);
    }

    public static (double RollRadians, double PitchRadians, double YawRadians) ExtractRollPitchYaw(Quaterniond bodyToMap)
    {
        bodyToMap.EnsureValid();
        // ZYX intrinsic Euler angles matching ENU (X-East, Y-North, Z-Up)
        var x = bodyToMap.X;
        var y = bodyToMap.Y;
        var z = bodyToMap.Z;
        var w = bodyToMap.W;

        var sinRoll = 2.0 * (w * x + y * z);
        var cosRoll = 1.0 - 2.0 * (x * x + y * y);
        var roll = Math.Atan2(sinRoll, cosRoll);

        var sinPitch = Math.Clamp(2.0 * (w * y - z * x), -1.0, 1.0);
        var pitch = Math.Asin(sinPitch);

        var sinYaw = 2.0 * (w * z + x * y);
        var cosYaw = 1.0 - 2.0 * (y * y + z * z);
        var yaw = Math.Atan2(sinYaw, cosYaw);

        return (roll, pitch, yaw);
    }

    private static Quaterniond Normalize(Quaterniond q)
    {
        var norm = Math.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W);
        return new Quaterniond(q.X / norm, q.Y / norm, q.Z / norm, q.W / norm);
    }
}

