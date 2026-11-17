using Gsa.Core.Sensors.Fusion;
using Gsa.Core.Sensors.Gnss;

namespace Gsa.Missions.Presentation;

public sealed record NavigationTelemetryView(string StatusKey, bool HasEstimate,
    string? SigmaEastMetres, string? SigmaNorthMetres, string? SigmaUpMetres, string? Sigma3DMetres,
    string? GnssAgeSeconds, bool BlackoutAlert, bool HighUncertaintyAlert,
    string BlackoutThresholdSeconds, string HighUncertaintyThresholdMetres);

public static class NavigationTelemetryPresenter
{
    public static string GnssStatusKey(GnssObservation? observation) => observation switch
    {
        null => "navigation.status.waiting_gnss",
        { IsAvailable: false } => "hud.gnss.unavailable",
        { Solution: GnssSolutionKind.RtkFix } => "hud.gnss.rtk_fix",
        { Solution: GnssSolutionKind.RtkFloat } => "hud.gnss.rtk_float",
        { Solution: GnssSolutionKind.Dgnss } => "hud.gnss.dgnss",
        { Dop: > 4 } => "hud.gnss.degraded",
        _ => "hud.gnss.autonomous",
    };

    public static NavigationTelemetryView Build(bool hasImu, TrajectoryEstimateSample? sample, string waitingStatusKey, string? locale)
    {
        if (!hasImu && sample is not null) throw new ArgumentException("An unavailable IMU cannot have an inertial estimate.", nameof(sample));
        string Number(double value) => PresentationNumberFormatter.Format(value, locale, 3, false);
        return new(!hasImu ? "navigation.status.no_imu" : sample is null ? waitingStatusKey : "navigation.status.running",
            sample is not null, sample is null ? null : Number(sample.PositionSigmaEnu.X),
            sample is null ? null : Number(sample.PositionSigmaEnu.Y), sample is null ? null : Number(sample.PositionSigmaEnu.Z),
            sample is null ? null : Number(sample.PositionSigma3DMetres), sample is null ? null : Number(sample.GnssSolutionAgeSeconds),
            sample?.BlackoutAlertActive ?? false, sample?.HighUncertaintyAlertActive ?? false,
            Number(SixStateTrajectoryKalmanFilter.DefaultBlackoutAlertThresholdSeconds),
            Number(SixStateTrajectoryKalmanFilter.DefaultHighUncertaintyThresholdMetres));
    }
}
