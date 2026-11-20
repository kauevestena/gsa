using Gsa.Core.Geometry;

namespace Gsa.Core.Mobility;

public enum StartleEventKind
{
    Horn = 0,
    Pothole = 1,
    Dog = 2,
    Fireworks = 3,
}

public sealed record GimbalinaStepResult(
    double Stress,
    bool Injured,
    bool HasFledToSafety,
    int SafeEscapeCount,
    double CornCooldownRemainingSeconds,
    double OscillationAmplitudeDegrees,
    double ResidualPitchRadians,
    double ResidualRollRadians,
    double OscillationAngularRatePitchRadPerSec,
    double OscillationAngularRateRollRadPerSec);

public sealed record GimbalinaSaveSnapshot(int SchemaVersion, double Stress, double CornCooldownRemainingSeconds,
    double ContinuousMaxStressSeconds, bool HasFledToSafety, int SafeEscapeCount, double OscillationPhaseRadians,
    double LastBodyPitchRadians, double LastBodyRollRadians);

/// <summary>Gimbalina stabilization, stress meter [0,100], corn recovery, and non-injurious safe escape.</summary>
public sealed class GimbalinaStabilizer
{
    public const double MinStress = 0.0;
    public const double MaxStress = 100.0;
    public const double OscillationThresholdStress = 60.0;
    public const double MaxOscillationDegrees = 5.0;
    public const double MaxHeadCompensationDegrees = 45.0;
    public const double RestRecoveryRatePerSecond = 5.0;
    public const double CornRecoveryPoints = 30.0;
    public const double CornCooldownSeconds = 10.0;
    public const double SafeEscapeHoldSeconds = 5.0;
    public const double OscillationFrequencyHz = 4.5;

    private double _oscillationPhaseRadians;

    public double Stress { get; private set; }
    public bool Injured => false;
    public double CornCooldownRemainingSeconds { get; private set; }
    public double ContinuousMaxStressSeconds { get; private set; }
    public bool HasFledToSafety { get; private set; }
    public int SafeEscapeCount { get; private set; }
    public double LastBodyPitchRadians { get; private set; }
    public double LastBodyRollRadians { get; private set; }

    public GimbalinaStabilizer(double initialStress = 0.0)
    {
        if (!double.IsFinite(initialStress) || initialStress < MinStress || initialStress > MaxStress)
            throw new ArgumentOutOfRangeException(nameof(initialStress), "Initial stress must be in [0, 100].");
        Stress = initialStress;
    }

    public GimbalinaSaveSnapshot ExportSaveSnapshot() => new(1, Stress, CornCooldownRemainingSeconds,
        ContinuousMaxStressSeconds, HasFledToSafety, SafeEscapeCount, _oscillationPhaseRadians,
        LastBodyPitchRadians, LastBodyRollRadians);

    public static GimbalinaStabilizer FromSaveSnapshot(GimbalinaSaveSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.SchemaVersion != 1 || !double.IsFinite(snapshot.Stress) || snapshot.Stress < 0 || snapshot.Stress > 100 ||
            !double.IsFinite(snapshot.CornCooldownRemainingSeconds) || snapshot.CornCooldownRemainingSeconds < 0 || snapshot.CornCooldownRemainingSeconds > CornCooldownSeconds ||
            !double.IsFinite(snapshot.ContinuousMaxStressSeconds) || snapshot.ContinuousMaxStressSeconds < 0 || snapshot.SafeEscapeCount < 0 ||
            !double.IsFinite(snapshot.OscillationPhaseRadians) || snapshot.OscillationPhaseRadians < 0 || snapshot.OscillationPhaseRadians >= 2 * Math.PI ||
            !double.IsFinite(snapshot.LastBodyPitchRadians) || !double.IsFinite(snapshot.LastBodyRollRadians) ||
            (snapshot.HasFledToSafety && (snapshot.SafeEscapeCount < 1 || snapshot.ContinuousMaxStressSeconds < SafeEscapeHoldSeconds - 1e-9 || snapshot.Stress < MaxStress)))
            throw new ArgumentException("Invalid Gimbalina snapshot.");
        return new(snapshot.Stress) { CornCooldownRemainingSeconds = snapshot.CornCooldownRemainingSeconds,
            ContinuousMaxStressSeconds = snapshot.ContinuousMaxStressSeconds, HasFledToSafety = snapshot.HasFledToSafety,
            SafeEscapeCount = snapshot.SafeEscapeCount, _oscillationPhaseRadians = snapshot.OscillationPhaseRadians,
            LastBodyPitchRadians = snapshot.LastBodyPitchRadians, LastBodyRollRadians = snapshot.LastBodyRollRadians };
    }

    public double OscillationAmplitudeDegrees =>
        Stress <= OscillationThresholdStress
            ? 0.0
            : MaxOscillationDegrees * ((Stress - OscillationThresholdStress) / (MaxStress - OscillationThresholdStress));

    public double OscillationAmplitudeRadians => OscillationAmplitudeDegrees * (Math.PI / 180.0);

    public static double GetStartleDelta(StartleEventKind kind) => kind switch
    {
        StartleEventKind.Horn => 10.0,
        StartleEventKind.Pothole => 15.0,
        StartleEventKind.Dog => 20.0,
        StartleEventKind.Fireworks => 25.0,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), "Unknown startle event kind."),
    };

    public double ApplyStartle(StartleEventKind kind)
    {
        if (HasFledToSafety)
            return Stress;
        Stress = Math.Clamp(Stress + GetStartleDelta(kind), MinStress, MaxStress);
        return Stress;
    }

    public bool TryFeedCorn()
    {
        if (HasFledToSafety || CornCooldownRemainingSeconds > 0.0)
            return false;
        Stress = Math.Clamp(Stress - CornRecoveryPoints, MinStress, MaxStress);
        if (Stress < MaxStress)
            ContinuousMaxStressSeconds = 0.0;
        CornCooldownRemainingSeconds = CornCooldownSeconds;
        return true;
    }

    public void RecoverAtOffice()
    {
        Stress = 0.0;
        ContinuousMaxStressSeconds = 0.0;
        HasFledToSafety = false;
        CornCooldownRemainingSeconds = 0.0;
        _oscillationPhaseRadians = 0.0;
    }

    public GimbalinaStepResult Step(
        double dtSeconds,
        bool isResting = false,
        double bodyPitchRadians = 0.0,
        double bodyRollRadians = 0.0)
    {
        if (!double.IsFinite(dtSeconds) || dtSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(dtSeconds), "Time step must be finite and positive.");
        if (!double.IsFinite(bodyPitchRadians) || !double.IsFinite(bodyRollRadians))
            throw new ArgumentOutOfRangeException(nameof(bodyPitchRadians), "Body angles must be finite.");

        LastBodyPitchRadians = bodyPitchRadians;
        LastBodyRollRadians = bodyRollRadians;

        if (CornCooldownRemainingSeconds > 0)
            CornCooldownRemainingSeconds = Math.Max(0.0, CornCooldownRemainingSeconds - dtSeconds);

        if (!HasFledToSafety)
        {
            if (isResting)
                Stress = Math.Max(MinStress, Stress - RestRecoveryRatePerSecond * dtSeconds);

            if (Stress >= MaxStress - 1e-9)
            {
                ContinuousMaxStressSeconds += dtSeconds;
                if (ContinuousMaxStressSeconds >= SafeEscapeHoldSeconds - 1e-9)
                {
                    HasFledToSafety = true;
                    SafeEscapeCount++;
                }
            }
            else
            {
                ContinuousMaxStressSeconds = 0.0;
            }
        }

        var omega = 2.0 * Math.PI * OscillationFrequencyHz;
        _oscillationPhaseRadians = (_oscillationPhaseRadians + omega * dtSeconds) % (2.0 * Math.PI);

        var (residualPitch, residualRoll, pitchRate, rollRate) = EvaluateAttitudeOffsets(_oscillationPhaseRadians, bodyPitchRadians, bodyRollRadians);

        return new GimbalinaStepResult(
            Stress,
            Injured,
            HasFledToSafety,
            SafeEscapeCount,
            CornCooldownRemainingSeconds,
            OscillationAmplitudeDegrees,
            residualPitch,
            residualRoll,
            pitchRate,
            rollRate);
    }

    /// <summary>Head stabilization counter-rotates up to +-45 deg; residual tilt and stress oscillation transfer to camera attitude.</summary>
    public Quaterniond ComputeEffectiveBodyToMap(Quaterniond baseBodyToMap, double exposureOffsetSeconds = 0.0,
        double bodyPitchRadians = 0.0, double bodyRollRadians = 0.0)
    {
        baseBodyToMap.EnsureValid();
        if (!double.IsFinite(exposureOffsetSeconds))
            throw new ArgumentOutOfRangeException(nameof(exposureOffsetSeconds), "Exposure offset must be finite.");
        var phase = _oscillationPhaseRadians + 2.0 * Math.PI * OscillationFrequencyHz * exposureOffsetSeconds;
        var (residualPitch, residualRoll, _, _) = EvaluateAttitudeOffsets(phase, bodyPitchRadians, bodyRollRadians);
        var pitchQuat = Quaterniond.FromAxisAngle(Vector3d.UnitY, residualPitch);
        var rollQuat = Quaterniond.FromAxisAngle(Vector3d.UnitX, residualRoll);
        return baseBodyToMap * pitchQuat * rollQuat;
    }

    private (double ResidualPitch, double ResidualRoll, double PitchRate, double RollRate) EvaluateAttitudeOffsets(
        double phase, double bodyPitchRadians, double bodyRollRadians)
    {
        var maxCompRad = MaxHeadCompensationDegrees * (Math.PI / 180.0);
        var pitchUncompensated = bodyPitchRadians - Math.Clamp(bodyPitchRadians, -maxCompRad, maxCompRad);
        var rollUncompensated = bodyRollRadians - Math.Clamp(bodyRollRadians, -maxCompRad, maxCompRad);

        var ampRad = OscillationAmplitudeRadians;
        var omega = 2.0 * Math.PI * OscillationFrequencyHz;
        var oscPitch = ampRad * Math.Sin(phase);
        var oscRoll = 0.7 * ampRad * Math.Cos(phase);
        var ratePitch = ampRad * omega * Math.Cos(phase);
        var rateRoll = -0.7 * ampRad * omega * Math.Sin(phase);
        return (pitchUncompensated + oscPitch, rollUncompensated + oscRoll, ratePitch, rateRoll);
    }
}
