using Gsa.Core.Geometry;
using Gsa.Core.Randomness;

namespace Gsa.Core.Sensors.Gnss;

/// <summary>Truth is accepted only by this synthesis boundary; output contains measurements only.</summary>
public sealed class GnssSimulator
{
    private readonly GaussianRandom _independentNoise;
    private long _nextSequence;

    public GnssSimulator(ulong independentNoiseSeed) => _independentNoise = new(independentNoiseSeed);

    public GnssSimulator(GnssSimulatorState state)
    {
        if (state.NextSequence < 0)
            throw new ArgumentOutOfRangeException(nameof(state));
        _independentNoise = new(state.IndependentNoise);
        _nextSequence = state.NextSequence;
    }

    public GnssSimulatorState ExportState() => new(_independentNoise.ExportState(), _nextSequence);

    public GnssObservation Observe(double timeSeconds, Vector3d truePosition, GnssProfile profile,
        GnssEnvironment environment, Vector3d sharedCommonError)
    {
        ValidateTime(timeSeconds);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(environment);
        var sigma = profile.BaseSigmaMetres * environment.Dop;
        if (!double.IsFinite(sigma))
            throw new ArgumentOutOfRangeException(nameof(profile), "Independent sigma must remain finite.");
        var sequence = _nextSequence;
        _nextSequence = checked(_nextSequence + 1);
        if (!environment.SignalAvailable || environment.Openness < 0.1)
            return new(sequence, timeSeconds, profile.Id, GnssSolutionKind.Unavailable, null, environment.Dop, null);

        var noise = sigma == 0 ? Vector3d.Zero : new Vector3d(
            sigma * _independentNoise.NextGaussian(), sigma * _independentNoise.NextGaussian(),
            sigma * _independentNoise.NextGaussian());
        var measured = truePosition + sharedCommonError + environment.MultipathBiasEnu + noise;
        return new(sequence, timeSeconds, profile.Id, GnssSolutionKind.Autonomous, measured, environment.Dop, sigma);
    }

    internal static void ValidateTime(double timeSeconds)
    {
        if (!double.IsFinite(timeSeconds) || timeSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(timeSeconds), "Physical time must be finite and nonnegative.");
    }
}
