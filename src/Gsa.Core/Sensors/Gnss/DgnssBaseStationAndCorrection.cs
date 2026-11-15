using Gsa.Core.Geometry;
using Gsa.Core.Randomness;

namespace Gsa.Core.Sensors.Gnss;

public sealed record SurveyMarkBaseStationSpec(
    string MarkId,
    string Name,
    Vector3d KnownPositionEnu,
    double OperatingWindowStartSeconds,
    double OperatingWindowEndSeconds,
    double MinimumOpenness = 0.8);

public sealed record BaseStationCorrectionSample(
    double TimestampSeconds,
    string BaseMarkId,
    Vector3d CorrectionEnu,
    double BaseOpenness);

public sealed class BaseStationLog
{
    private readonly List<BaseStationCorrectionSample> _samples;

    public string BaseMarkId { get; }
    public Vector3d KnownPositionEnu { get; }
    public double SamplingIntervalSeconds { get; }
    public IReadOnlyList<BaseStationCorrectionSample> Samples => _samples;

    public BaseStationLog(
        string baseMarkId,
        Vector3d knownPositionEnu,
        double samplingIntervalSeconds,
        IEnumerable<BaseStationCorrectionSample> samples)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseMarkId);
        ArgumentNullException.ThrowIfNull(samples);
        if (!double.IsFinite(samplingIntervalSeconds) || samplingIntervalSeconds <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(samplingIntervalSeconds));

        BaseMarkId = baseMarkId;
        KnownPositionEnu = knownPositionEnu;
        SamplingIntervalSeconds = samplingIntervalSeconds;
        _samples = samples
            .OrderBy(s => s.TimestampSeconds)
            .ToList();
    }

    public bool TryInterpolateCorrection(
        double timestampSeconds,
        double maxStalenessSeconds,
        out Vector3d correctionEnu)
    {
        correctionEnu = Vector3d.Zero;
        if (_samples.Count == 0)
            return false;

        BaseStationCorrectionSample? before = null;
        BaseStationCorrectionSample? after = null;

        foreach (var sample in _samples)
        {
            if (Math.Abs(sample.TimestampSeconds - timestampSeconds) <= 1e-9)
            {
                correctionEnu = sample.CorrectionEnu;
                return true;
            }
            if (sample.TimestampSeconds < timestampSeconds)
                before = sample;
            else
            {
                after = sample;
                break;
            }
        }

        if (before is not null && after is not null)
        {
            var span = after.TimestampSeconds - before.TimestampSeconds;
            if (span <= maxStalenessSeconds * 2.0 && span > 0.0)
            {
                var t = (timestampSeconds - before.TimestampSeconds) / span;
                correctionEnu = before.CorrectionEnu + (after.CorrectionEnu - before.CorrectionEnu) * t;
                return true;
            }
        }

        var nearest = before ?? after!;
        if (Math.Abs(nearest.TimestampSeconds - timestampSeconds) <= maxStalenessSeconds)
        {
            correctionEnu = nearest.CorrectionEnu;
            return true;
        }

        return false;
    }
}

public sealed record DgnssCorrectionResult(
    GnssObservation Observation,
    bool DifferentialApplied,
    double BaselineMetres,
    double ResidualCorrelatedErrorSigmaMetres,
    string? FallbackReasonKey = null);

/// <summary>Records simultaneous base station correction logs over validated geodetic marks (openness >= 0.8).</summary>
public static class DgnssBaseSimulator
{
    public const double RequiredBaseMinimumOpenness = 0.8;

    public static BaseStationLog RecordSession(
        SurveyMarkBaseStationSpec spec,
        double baseOpenness,
        IReadOnlyList<double> timestampsSeconds,
        CommonGnssErrorProcess commonError,
        GnssSimulator baseReceiver,
        double samplingIntervalSeconds = 1.0,
        Vector3d baseMultipathEnu = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(timestampsSeconds);
        ArgumentNullException.ThrowIfNull(commonError);
        ArgumentNullException.ThrowIfNull(baseReceiver);

        var env = new GnssEnvironment(baseOpenness, baseMultipathEnu, signalAvailable: true);
        var samples = new List<BaseStationCorrectionSample>(timestampsSeconds.Count);

        foreach (var t in timestampsSeconds)
        {
            var common = commonError.At(t);
            var obs = baseReceiver.Observe(t, spec.KnownPositionEnu, GnssProfile.Dgnss, env, common);
            var withinWindow = t >= spec.OperatingWindowStartSeconds && t <= spec.OperatingWindowEndSeconds;
            var meetsOpenness = baseOpenness >= Math.Max(RequiredBaseMinimumOpenness, spec.MinimumOpenness);
            if (!withinWindow || !meetsOpenness || !obs.IsAvailable || obs.MeasuredPositionEnu is null)
                continue;

            var correction = spec.KnownPositionEnu - obs.MeasuredPositionEnu.Value;
            samples.Add(new BaseStationCorrectionSample(t, spec.MarkId, correction, baseOpenness));
        }

        return new BaseStationLog(spec.MarkId, spec.KnownPositionEnu, samplingIntervalSeconds, samples);
    }
}

/// <summary>Applies post-processed base differential corrections while preserving local multipath and 0.02 m/km baseline decorrelation.</summary>
public sealed class DgnssRoverCorrector
{
    public const double SpatialDecorrelationMetresPerKm = 0.02;
    public const double DefaultMaxBaselineMetres = 10000.0;

    private readonly GaussianRandom _spatialResidualNoise;

    public DgnssRoverCorrector(ulong seed = 7919)
    {
        _spatialResidualNoise = new(seed);
    }

    public DgnssCorrectionResult ApplyCorrection(
        GnssObservation roverObservation,
        SurveyMarkBaseStationSpec baseSpec,
        BaseStationLog baseLog,
        double maxStalenessSeconds = 2.0,
        double maxBaselineMetres = DefaultMaxBaselineMetres)
    {
        ArgumentNullException.ThrowIfNull(roverObservation);
        ArgumentNullException.ThrowIfNull(baseSpec);
        ArgumentNullException.ThrowIfNull(baseLog);
        if (!string.Equals(baseSpec.MarkId, baseLog.BaseMarkId, StringComparison.Ordinal))
            throw new ArgumentException("Base station spec ID does not match log.", nameof(baseLog));

        if (!roverObservation.IsAvailable || roverObservation.MeasuredPositionEnu is null)
        {
            return new DgnssCorrectionResult(
                roverObservation,
                DifferentialApplied: false,
                BaselineMetres: 0.0,
                ResidualCorrelatedErrorSigmaMetres: 0.0,
                FallbackReasonKey: "gnss.dgnss.rover_unavailable");
        }

        var roverPos = roverObservation.MeasuredPositionEnu.Value;
        var dx = roverPos.X - baseSpec.KnownPositionEnu.X;
        var dy = roverPos.Y - baseSpec.KnownPositionEnu.Y;
        var baselineMetres = Math.Sqrt(dx * dx + dy * dy);
        var residualSigma = SpatialDecorrelationMetresPerKm * (baselineMetres / 1000.0);

        var t = roverObservation.TimestampSeconds;
        if (t < baseSpec.OperatingWindowStartSeconds || t > baseSpec.OperatingWindowEndSeconds)
        {
            return new DgnssCorrectionResult(
                roverObservation,
                DifferentialApplied: false,
                BaselineMetres: baselineMetres,
                ResidualCorrelatedErrorSigmaMetres: residualSigma,
                FallbackReasonKey: "gnss.dgnss.outside_operating_window");
        }

        if (!baseLog.TryInterpolateCorrection(t, maxStalenessSeconds, out var baseCorrection))
        {
            return new DgnssCorrectionResult(
                roverObservation,
                DifferentialApplied: false,
                BaselineMetres: baselineMetres,
                ResidualCorrelatedErrorSigmaMetres: residualSigma,
                FallbackReasonKey: "gnss.dgnss.base_unavailable");
        }

        if (baselineMetres > maxBaselineMetres)
        {
            return new DgnssCorrectionResult(
                roverObservation,
                DifferentialApplied: false,
                BaselineMetres: baselineMetres,
                ResidualCorrelatedErrorSigmaMetres: residualSigma,
                FallbackReasonKey: "gnss.dgnss.excessive_baseline_decorrelation");
        }

        var residualDecorrelation = residualSigma == 0.0
            ? Vector3d.Zero
            : new Vector3d(
                residualSigma * _spatialResidualNoise.NextGaussian(),
                residualSigma * _spatialResidualNoise.NextGaussian(),
                residualSigma * _spatialResidualNoise.NextGaussian());

        // Differential correction cancels the shared epoch common error; local rover multipath and independent noise stay untouched.
        var correctedPos = roverPos + baseCorrection + residualDecorrelation;
        var dgnssObs = new GnssObservation(
            roverObservation.Sequence,
            roverObservation.TimestampSeconds,
            GnssProfile.Dgnss.Id,
            GnssSolutionKind.Dgnss,
            correctedPos,
            roverObservation.Dop,
            roverObservation.IndependentSigmaMetres);

        return new DgnssCorrectionResult(
            dgnssObs,
            DifferentialApplied: true,
            BaselineMetres: baselineMetres,
            ResidualCorrelatedErrorSigmaMetres: residualSigma,
            FallbackReasonKey: null);
    }
}

