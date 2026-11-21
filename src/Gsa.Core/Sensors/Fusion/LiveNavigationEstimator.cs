using Gsa.Core.Geometry;
using Gsa.Core.Sensors.Gnss;

namespace Gsa.Core.Sensors.Fusion;

/// <summary>Causal, observation-only online filter. No pose truth, map oracle, or checkpoint input exists.</summary>
public sealed class LiveNavigationEstimator
{
    private readonly ImuGrade _grade;
    private readonly StationaryZuptDetector _zupt = new();
    private ComplementaryAttitudeEstimator? _attitude;
    private SixStateTrajectoryKalmanFilter? _filter;
    private double? _lastTime;
    private double _lastDmiTime = double.NegativeInfinity;
    private Vector3d _gyroBias;
    private GnssObservation? _previousFix;
    private string _waitingStatus = "navigation.status.waiting_gnss";
    private long _lastGnssSequence = -1, _lastHeadingSequence = -1, _lastDmiSequence;

    public LiveNavigationEstimator(ImuGrade grade)
    {
        if (!Enum.IsDefined(grade)) throw new ArgumentOutOfRangeException(nameof(grade));
        _grade = grade;
    }

    public GnssObservation? LatestGnss { get; private set; }
    public bool IsInitialized => _filter is not null;
    public TrajectoryEstimateSample? Current => _filter?.GetCurrentSample();
    public int GnssUpdatesApplied { get; private set; }
    public int DmiUpdatesApplied { get; private set; }
    public int ZuptUpdatesApplied { get; private set; }
    public double? LastZuptTimestampSeconds { get; private set; }
    public int RejectedGnssCorrections { get; private set; }
    public bool LastGnssCorrectionRejected { get; private set; }
    public string StatusKey => IsInitialized ? "navigation.status.running" : _waitingStatus;

    public void Step(ImuObservation imu, GnssObservation? gnss = null,
        HeadingObservation? heading = null, WheelDmiObservation? dmi = null)
    {
        ArgumentNullException.ThrowIfNull(imu);
        if (!double.IsFinite(imu.TimestampSeconds) || !double.IsFinite(imu.DtSeconds) || imu.DtSeconds <= 0 ||
            imu.TimestampSeconds < 0 || (_lastTime.HasValue &&
            Math.Abs(imu.TimestampSeconds - _lastTime.Value - imu.DtSeconds) > 1e-7))
            throw new ArgumentException("IMU epochs must increase by their physical sample interval.", nameof(imu));
        void SameEpoch(double timestamp)
        {
            if (Math.Abs(timestamp - imu.TimestampSeconds) > 1e-7 || !double.IsFinite(timestamp))
                throw new ArgumentException("Corrections must belong to the current IMU epoch.");
        }
        if (gnss is not null)
        {
            SameEpoch(gnss.TimestampSeconds);
            if (gnss.Sequence <= _lastGnssSequence) throw new ArgumentException("GNSS correction already consumed.", nameof(gnss));
        }
        if (heading is not null)
        {
            SameEpoch(heading.TimestampSeconds);
            if (heading.Sequence <= _lastHeadingSequence) throw new ArgumentException("Heading already consumed.", nameof(heading));
        }
        if (dmi is not null)
        {
            SameEpoch(dmi.TimestampSeconds);
            if (dmi.Sequence <= _lastDmiSequence) throw new ArgumentException("DMI correction already consumed.", nameof(dmi));
        }
        _lastTime = imu.TimestampSeconds;
        var previousFix = _previousFix;
        if (gnss is not null) { LatestGnss = gnss; _lastGnssSequence = gnss.Sequence; }
        if (gnss is { IsAvailable: true }) _previousFix = gnss;
        if (heading is not null) _lastHeadingSequence = heading.Sequence;
        if (dmi is not null) _lastDmiSequence = dmi.Sequence;

        if (_filter is null)
        {
            // Position/heading are observed; velocity comes from two fresh timed fixes. Never spawn/vehicle truth.
            if (gnss is null) return;
            if (gnss is not { IsAvailable: true, MeasuredPositionEnu: { } position })
            { _waitingStatus = "navigation.status.waiting_gnss"; return; }
            if (heading is not { IsAvailable: true, MeasuredRadians: { } yaw })
            { _waitingStatus = "navigation.status.waiting_heading"; return; }
            var interval = gnss.TimestampSeconds - (previousFix?.TimestampSeconds ?? gnss.TimestampSeconds);
            if (previousFix?.MeasuredPositionEnu is not { } previousPosition || interval <= 0 || interval > 1)
            { _waitingStatus = "navigation.status.waiting_velocity"; return; }
            var rotation = Quaterniond.FromAxisAngle(Vector3d.UnitZ, yaw);
            _attitude = new ComplementaryAttitudeEstimator(rotation, imu.TimestampSeconds);
            var sigma = Math.Max(.5, (gnss.IndependentSigmaMetres ?? 2.5) * Math.Max(1, gnss.Dop * .5));
            var previousSigma = Math.Max(.5, (previousFix.IndependentSigmaMetres ?? 2.5) * Math.Max(1, previousFix.Dop * .5));
            _filter = new SixStateTrajectoryKalmanFilter(imu.TimestampSeconds, position, (position - previousPosition) / interval, rotation,
                initialPositionSigmaMetres: sigma,
                initialVelocitySigmaMetresPerSecond: Math.Max(2, Math.Sqrt(sigma * sigma + previousSigma * previousSigma) / interval));
            GnssUpdatesApplied++;
        }
        else
        {
            var stationary = imu.TimestampSeconds - _lastDmiTime <= .051 &&
                _zupt.ContinuousStationarySeconds >= StationaryZuptDetector.RequiredContinuousDurationSeconds;
            var corrected = _gyroBias == Vector3d.Zero ? imu : imu with { AngularVelocityBody = imu.AngularVelocityBody - _gyroBias };
            var attitude = _attitude!.Step(corrected, stationary, heading);
            _filter.PredictLive(imu.DtSeconds, imu.SpecificForceBody, attitude.EstimatedBodyToMap, _grade);
            if (gnss is { IsAvailable: true })
            {
                LastGnssCorrectionRejected = !_filter.UpdateGnssPosition(gnss, rejectOutliers: true);
                if (LastGnssCorrectionRejected) RejectedGnssCorrections++; else GnssUpdatesApplied++;
            }
        }

        if (dmi is not null)
        {
            if (dmi.TimestampSeconds - _lastDmiTime > .051) _zupt.Reset();
            _lastDmiTime = dmi.TimestampSeconds;
            _filter.UpdateDmiVelocity(dmi, _attitude!.EstimatedBodyToMap);
            DmiUpdatesApplied++;
            var stop = _zupt.Evaluate(dmi.DtSeconds, dmi.MeasuredForwardSpeedMetresPerSecond, imu);
            if (stop.ZuptTriggered)
            {
                _filter.UpdateZupt(stop.VelocitySigmaMetresPerSecond);
                _gyroBias = stop.EstimatedGyroBiasRadiansPerSecond;
                ZuptUpdatesApplied++;
                LastZuptTimestampSeconds = imu.TimestampSeconds;
            }
        }
        else if (imu.TimestampSeconds - _lastDmiTime > .051) _zupt.Reset();
    }
}
