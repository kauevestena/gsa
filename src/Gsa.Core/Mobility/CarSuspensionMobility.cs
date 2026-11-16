using Gsa.Core.Geometry;

namespace Gsa.Core.Mobility;

public sealed record WheelRaycastState(
    string WheelId,
    double CompressionMetres,
    double NormalForceNewtons,
    bool InContact);

public sealed record RackImpactEvent(
    string EventId,
    double TimestampSeconds,
    double DeltaVMetresPerSecond,
    double EffectiveThresholdMetresPerSecond,
    bool TriggersRackMisalignment,
    double DamageApplied,
    string ImpactSource);

public sealed record CarSuspensionStepResult(
    Vector3d PositionEnu,
    double HeadingRadians,
    double PitchRadians,
    double RollRadians,
    double SpeedMetresPerSecond,
    double ConditionDamage,
    bool IsDestroyed,
    IReadOnlyList<WheelRaycastState> Wheels,
    RackImpactEvent? ImpactEvent,
    CaptureStopReason StopReason);

/// <summary>Four-wheel raycast suspension controller and delta-v rack impact emitter for automotive platforms (Levels 3-6).</summary>
public sealed class CarSuspensionSimulator
{
    public const double BaseRackImpactThresholdMetresPerSecond = 3.0;
    public const double RestSuspensionTravelMetres = 0.14;
    public const double SpringConstantNewtonsPerMetre = 28000.0;
    public const double DamagePerExcessDeltaV = 12.5;
    public const double DestroyedThreshold = 100.0;

    private readonly PlatformProfile _profile;
    private readonly double _dryGripCoefficient = 1.0;
    private double _timestampSeconds;
    private Vector3d _positionEnu;
    private double _headingRadians;
    private double _speedMetresPerSecond;
    private double _pitchRadians;
    private double _pitchRate;
    private double _rollRadians;
    private double _rollRate;
    private double _conditionDamage;
    private double _remainingAutonomySeconds;
    private int _usedStorageUnits;
    private long _impactCounter;
    private readonly List<RackImpactEvent> _impactLog = [];

    public PlatformProfile Profile => _profile;
    public double TimestampSeconds => _timestampSeconds;
    public Vector3d PositionEnu => _positionEnu;
    public double HeadingRadians => _headingRadians;
    public double SpeedMetresPerSecond => _speedMetresPerSecond;
    public double PitchRadians => _pitchRadians;
    public double RollRadians => _rollRadians;
    public double ConditionDamage => _conditionDamage;
    public bool IsDestroyed => _conditionDamage >= DestroyedThreshold;
    public double RemainingAutonomySeconds => _remainingAutonomySeconds;
    public int UsedStorageUnits => _usedStorageUnits;
    public IReadOnlyList<RackImpactEvent> ImpactLog => _impactLog;

    public void ApplyExternalPositionAdjustment(Vector3d positionEnu)
    {
        _positionEnu = positionEnu;
    }

    public CarSuspensionSimulator(
        Vector3d initialPositionEnu,
        double initialHeadingRadians = 0.0,
        LocomotionMode mode = LocomotionMode.Car,
        int usedStorageUnits = 0)
    {
        if (mode < LocomotionMode.Car)
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Automotive suspension simulator requires Level 3+ platform.");
        if (!double.IsFinite(initialHeadingRadians))
            throw new ArgumentOutOfRangeException(nameof(initialHeadingRadians));
        if (usedStorageUnits < 0)
            throw new ArgumentOutOfRangeException(nameof(usedStorageUnits));

        _profile = PlatformProfile.ForMode(mode);
        _positionEnu = initialPositionEnu;
        _headingRadians = initialHeadingRadians;
        _remainingAutonomySeconds = _profile.PhysicalAutonomySeconds;
        _usedStorageUnits = Math.Min(usedStorageUnits, _profile.VirtualStorageCapacityUnits);
    }

    public CarSuspensionStepResult StepMotion(
        double dtSeconds,
        double throttleInput,
        double brakeInput,
        double steerInput,
        double surfaceGripMultiplier = 1.0,
        double bumpHeightMetres = 0.0,
        int defensiveDrivingRank = 0)
    {
        if (!double.IsFinite(dtSeconds) || dtSeconds <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(dtSeconds));
        if (!double.IsFinite(surfaceGripMultiplier) || surfaceGripMultiplier <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(surfaceGripMultiplier));

        var grip = Math.Clamp(_dryGripCoefficient * surfaceGripMultiplier, 0.2, 1.2);
        throttleInput = Math.Clamp(throttleInput, 0.0, 1.0);
        brakeInput = Math.Clamp(brakeInput, 0.0, 1.0);
        steerInput = Math.Clamp(steerInput, -1.0, 1.0);

        if (IsDestroyed || _remainingAutonomySeconds <= 0.0)
        {
            _speedMetresPerSecond = 0.0;
            return BuildResult(bumpHeightMetres, null);
        }

        _timestampSeconds += dtSeconds;
        _remainingAutonomySeconds = Math.Max(0.0, _remainingAutonomySeconds - dtSeconds * (throttleInput > 0.0 ? 1.0 : 0.25));

        var accel = throttleInput * _profile.AccelerationMetresPerSecondSquared * grip
                  - brakeInput * _profile.DecelerationMetresPerSecondSquared * grip;
        var drag = _speedMetresPerSecond * 0.12;
        _speedMetresPerSecond = Math.Clamp(_speedMetresPerSecond + (accel - drag) * dtSeconds, 0.0, _profile.MaxSpeedMetresPerSecond);
        if (brakeInput > 0.5 && _speedMetresPerSecond < 0.05)
            _speedMetresPerSecond = 0.0;

        var steerFactor = Math.Min(1.0, _speedMetresPerSecond / 4.0);
        var yawRate = steerInput * _profile.TurnRateRadiansPerSecond * steerFactor * Math.Min(1.0, grip);
        _headingRadians += yawRate * dtSeconds;

        var forward = new Vector3d(Math.Cos(_headingRadians), Math.Sin(_headingRadians), 0.0);
        _positionEnu += forward * (_speedMetresPerSecond * dtSeconds);

        // Spring-damper pitch & roll keep braking, bumps, and cornering bounded and stable
        var targetPitch = Math.Clamp((-accel * 0.008) + bumpHeightMetres * 0.35, -0.18, 0.18);
        var targetRoll = Math.Clamp(yawRate * _speedMetresPerSecond * 0.004, -0.16, 0.16);
        _pitchRate = (_pitchRate + (targetPitch - _pitchRadians) * 42.0 * dtSeconds) * Math.Exp(-8.5 * dtSeconds);
        _pitchRadians = Math.Clamp(_pitchRadians + _pitchRate * dtSeconds, -0.22, 0.22);

        _rollRate = (_rollRate + (targetRoll - _rollRadians) * 45.0 * dtSeconds) * Math.Exp(-9.0 * dtSeconds);
        _rollRadians = Math.Clamp(_rollRadians + _rollRate * dtSeconds, -0.20, 0.20);

        RackImpactEvent? bumpImpact = null;
        if (Math.Abs(bumpHeightMetres) >= 0.08 && _speedMetresPerSecond >= 11.0)
        {
            var bumpDeltaV = Math.Abs(bumpHeightMetres) * _speedMetresPerSecond * 2.2;
            bumpImpact = ApplyCollisionImpact(bumpDeltaV, "speed_bump_overspeed", defensiveDrivingRank);
        }

        return BuildResult(bumpHeightMetres, bumpImpact);
    }

    public CarSuspensionStepResult StepBraking(double durationSeconds, double initialSpeedMetresPerSecond, double surfaceGripMultiplier = 1.0)
    {
        if (!double.IsFinite(initialSpeedMetresPerSecond) || initialSpeedMetresPerSecond < 0.0)
            throw new ArgumentOutOfRangeException(nameof(initialSpeedMetresPerSecond));

        _speedMetresPerSecond = Math.Min(initialSpeedMetresPerSecond, _profile.MaxSpeedMetresPerSecond);
        var steps = Math.Max(1, (int)Math.Ceiling(durationSeconds / (1.0 / 60.0)));
        var dt = durationSeconds / steps;
        CarSuspensionStepResult? last = null;
        for (var i = 0; i < steps; i++)
            last = StepMotion(dt, 0.0, 1.0, 0.0, surfaceGripMultiplier);
        return last!;
    }

    public CarSuspensionStepResult StepCornering(double durationSeconds, double steerInput, double throttleInput = 0.6, double surfaceGripMultiplier = 1.0)
    {
        var steps = Math.Max(1, (int)Math.Ceiling(durationSeconds / (1.0 / 60.0)));
        var dt = durationSeconds / steps;
        CarSuspensionStepResult? last = null;
        for (var i = 0; i < steps; i++)
            last = StepMotion(dt, throttleInput, 0.0, steerInput, surfaceGripMultiplier);
        return last!;
    }

    public CarSuspensionStepResult StepOverSpeedBump(double durationSeconds, double bumpHeightMetres, int defensiveDrivingRank = 0)
    {
        var steps = Math.Max(1, (int)Math.Ceiling(durationSeconds / (1.0 / 60.0)));
        var dt = durationSeconds / steps;
        CarSuspensionStepResult? last = null;
        for (var i = 0; i < steps; i++)
        {
            var pulse = i < steps / 3 ? bumpHeightMetres : 0.0;
            last = StepMotion(dt, 0.35, 0.0, 0.0, 1.0, pulse, defensiveDrivingRank);
        }
        return last!;
    }

    public RackImpactEvent ApplyCollisionImpact(
        double deltaVMetresPerSecond,
        string impactSource = "collision",
        int defensiveDrivingRank = 0)
    {
        if (!double.IsFinite(deltaVMetresPerSecond) || deltaVMetresPerSecond < 0.0)
            throw new ArgumentOutOfRangeException(nameof(deltaVMetresPerSecond));
        ArgumentException.ThrowIfNullOrWhiteSpace(impactSource);

        var clampedRank = Math.Clamp(defensiveDrivingRank, 0, 3);
        var effectiveThreshold = BaseRackImpactThresholdMetresPerSecond * (1.0 + 0.10 * clampedRank);
        var triggersMisalignment = deltaVMetresPerSecond >= effectiveThreshold;
        var damage = deltaVMetresPerSecond >= effectiveThreshold
            ? Math.Round((deltaVMetresPerSecond - effectiveThreshold + 1.0) * DamagePerExcessDeltaV, 2)
            : Math.Round(deltaVMetresPerSecond * 1.5, 2);

        _conditionDamage = Math.Clamp(_conditionDamage + damage, 0.0, DestroyedThreshold);
        _speedMetresPerSecond = Math.Max(0.0, _speedMetresPerSecond - deltaVMetresPerSecond);

        _impactCounter++;
        var ev = new RackImpactEvent(
            EventId: $"rack_imp_{_impactCounter:0000}",
            TimestampSeconds: _timestampSeconds,
            DeltaVMetresPerSecond: deltaVMetresPerSecond,
            EffectiveThresholdMetresPerSecond: effectiveThreshold,
            TriggersRackMisalignment: triggersMisalignment,
            DamageApplied: damage,
            ImpactSource: impactSource);
        _impactLog.Add(ev);
        return ev;
    }

    public bool TryRecordCaptureSample(int storageUnits = 1)
    {
        if (storageUnits <= 0)
            throw new ArgumentOutOfRangeException(nameof(storageUnits));
        if (IsDestroyed || _remainingAutonomySeconds <= 0.0 || _usedStorageUnits + storageUnits > _profile.VirtualStorageCapacityUnits)
            return false;

        _usedStorageUnits += storageUnits;
        return true;
    }

    private CarSuspensionStepResult BuildResult(double bumpHeightMetres, RackImpactEvent? impactEvent)
    {
        var baseComp = RestSuspensionTravelMetres + Math.Clamp(bumpHeightMetres * 0.25, -0.05, 0.08);
        var pitchOffset = _pitchRadians * 0.25;
        var rollOffset = _rollRadians * 0.25;

        WheelRaycastState MakeWheel(string id, double pSign, double rSign)
        {
            var comp = Math.Clamp(baseComp + pSign * pitchOffset + rSign * rollOffset, 0.0, 0.28);
            var inContact = comp > 0.005;
            var force = inContact ? Math.Round(comp * SpringConstantNewtonsPerMetre, 1) : 0.0;
            return new WheelRaycastState(id, comp, force, inContact);
        }

        WheelRaycastState[] wheels =
        [
            MakeWheel("FL", -1.0, -1.0),
            MakeWheel("FR", -1.0, +1.0),
            MakeWheel("RL", +1.0, -1.0),
            MakeWheel("RR", +1.0, +1.0),
        ];

        var stopReason = _remainingAutonomySeconds <= 0.0
            ? CaptureStopReason.AutonomyExhausted
            : _usedStorageUnits >= _profile.VirtualStorageCapacityUnits
                ? CaptureStopReason.VirtualStorageFull
                : CaptureStopReason.None;

        return new CarSuspensionStepResult(
            _positionEnu,
            _headingRadians,
            _pitchRadians,
            _rollRadians,
            _speedMetresPerSecond,
            _conditionDamage,
            IsDestroyed,
            Array.AsReadOnly(wheels),
            impactEvent,
            stopReason);
    }
}

