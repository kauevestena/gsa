using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Gnss;

namespace Gsa.Core.Sensors.Fusion;

/// <summary>Simulator-only kinematics. This type is never accepted by LiveNavigationEstimator.</summary>
public readonly record struct NavigationTruthMotion(Vector3d PositionEnu, Vector3d VelocityEnu, double YawRadians);
public readonly record struct NavigationTrailPoint(double TimestampSeconds, Vector3d EstimatedPositionEnu);

/// <summary>Bounded live diagnostic synthesis at 100/20/5 Hz on physical time, separate from acquired product data.</summary>
public sealed class LiveNavigationRig
{
    public const int TrailCapacity = 600;
    public const double ImuIntervalSeconds = .01;
    private readonly PlatformProfile _platform;
    private readonly ImuSimulator? _imu;
    private readonly GnssSimulator _gnss;
    private readonly HeadingSimulator _heading;
    private readonly DualAntennaHeadingSensor _dualHeading;
    private readonly CommonGnssErrorProcess _common;
    private readonly WheelDmiSimulator? _dmi;
    private readonly NavigationTrailPoint[] _trail = new NavigationTrailPoint[TrailCapacity];
    private NavigationTruthMotion _previous;
    private double _frameTime;
    private long _nextSample = 1, _headingSequence;
    private int _trailWrite, _trailCount;

    public LocomotionMode Mode { get; }
    public LiveNavigationEstimator? Estimator { get; }
    public GnssObservation? LatestGnss { get; private set; }
    public double PhysicalTimeSeconds => _frameTime;
    public int ImuSamples { get; private set; }
    public int GnssEpochs { get; private set; }
    public int DmiSamples { get; private set; }
    public int TrailCount => _trailCount;

    public LiveNavigationRig(LocomotionMode mode, NavigationTruthMotion initial, ulong seed = 71117)
    {
        ValidateMotion(initial);
        Mode = mode;
        _platform = PlatformProfile.ForMode(mode);
        _previous = initial;
        _gnss = new GnssSimulator(seed ^ 0x510);
        _heading = new HeadingSimulator(seed ^ 0x511);
        _dualHeading = new DualAntennaHeadingSensor(seed ^ 0x512);
        _common = new CommonGnssErrorProcess(seed ^ 0x513, .5, 30);
        if (_platform.Level >= 3)
        {
            var grade = _platform.Level >= 6 ? ImuGrade.Navigation : _platform.Level >= 4 ? ImuGrade.Tactical : ImuGrade.Mems;
            _imu = new ImuSimulator(ImuProfile.ForGrade(grade), seed ^ 0x514, seed ^ 0x515);
            Estimator = new LiveNavigationEstimator(grade);
        }
        if (_platform.Level >= 4) _dmi = new WheelDmiSimulator(seed ^ 0x516);
    }

    public void Advance(double dtSeconds, NavigationTruthMotion next,
        Func<Vector3d, GnssEnvironment> environmentAt, bool wetRoad = false)
    {
        ArgumentNullException.ThrowIfNull(environmentAt);
        if (!double.IsFinite(dtSeconds) || dtSeconds <= 0 || dtSeconds > .25)
            throw new ArgumentOutOfRangeException(nameof(dtSeconds), "Use bounded physical steps of at most 0.25 s.");
        ValidateMotion(next);
        var endTime = _frameTime + dtSeconds;
        var acceleration = (next.VelocityEnu - _previous.VelocityEnu) / dtSeconds;
        var yawChange = HeadingSimulator.WrapRadians(next.YawRadians - _previous.YawRadians);
        while (_nextSample * ImuIntervalSeconds <= endTime + 1e-9)
        {
            if (_imu is null && _nextSample % 20 != 0) { _nextSample++; continue; }
            var time = _nextSample * ImuIntervalSeconds;
            var fraction = Math.Clamp((time - _frameTime) / dtSeconds, 0, 1);
            var position = _previous.PositionEnu * (1 - fraction) + next.PositionEnu * fraction;
            var velocity = _previous.VelocityEnu * (1 - fraction) + next.VelocityEnu * fraction;
            var yaw = _previous.YawRadians + fraction * yawChange;
            var rotation = Quaterniond.FromAxisAngle(Vector3d.UnitZ, yaw);
            var forward = rotation.Rotate(Vector3d.UnitX);
            var forwardSpeed = Vector3d.Dot(velocity, forward);
            GnssObservation? gnss = null;
            HeadingObservation? heading = null;
            WheelDmiObservation? dmi = null;

            if (_nextSample % 20 == 0)
            {
                var environment = environmentAt(position);
                // The city has no connected correction base. Never claim RTK because a car has RTK capability.
                var profile = _platform.Level >= 1 ? GnssProfile.Navigation : GnssProfile.Cellphone;
                gnss = _gnss.Observe(time, position, profile, environment, _common.At(time),
                    GnssProfileSelector.ComputeDopForOpenness(environment.Openness, _platform.Level));
                LatestGnss = gnss;
                GnssEpochs++;
                if (_platform.Level >= DualAntennaHeadingSensor.RequiredPlatformLevel)
                {
                    var dual = _dualHeading.Observe(time, yaw, velocity.Length, _platform.Level,
                        gnss.IsAvailable, environment.Openness);
                    heading = new HeadingObservation(_headingSequence++, time, "heading_dual_antenna",
                        dual.MeasuredYawRadians, dual.HeadingSigmaRadians);
                }
                else
                    heading = _heading.Observe(time, yaw, GnssProfileSelector.SelectHeadingProfile(_platform),
                        gnss.IsAvailable && forwardSpeed >= .5);
            }
            if (_dmi is not null && _nextSample % 5 == 0)
            {
                dmi = _dmi.Observe(time, .05, forwardSpeed, wetRoad, requestedWetSlipFraction: .05);
                DmiSamples++;
            }
            if (_imu is not null)
            {
                var observation = _imu.Observe(time, ImuIntervalSeconds,
                    ImuIdealKinematics.ComputeSpecificForceBody(rotation, acceleration),
                    Vector3d.UnitZ * (yawChange / dtSeconds));
                ImuSamples++;
                Estimator!.Step(observation, gnss, heading, dmi);
                if (_nextSample % 10 == 0 && Estimator.Current is { } current)
                {
                    _trail[_trailWrite] = new(time, current.PositionEnu);
                    _trailWrite = (_trailWrite + 1) % TrailCapacity;
                    _trailCount = Math.Min(TrailCapacity, _trailCount + 1);
                }
            }
            _nextSample++;
        }
        _previous = next;
        _frameTime = endTime;
    }

    public void CopyTrailTo(List<NavigationTrailPoint> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.Clear();
        for (var i = 0; i < _trailCount; i++)
            destination.Add(_trail[(_trailWrite - _trailCount + i + TrailCapacity) % TrailCapacity]);
    }

    private static void ValidateMotion(NavigationTruthMotion motion)
    {
        if (!double.IsFinite(motion.YawRadians)) throw new ArgumentOutOfRangeException(nameof(motion));
        // Vector3d's constructor already rejects non-finite positions and velocities.
    }
}
