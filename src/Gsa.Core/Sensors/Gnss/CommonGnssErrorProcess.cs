using Gsa.Core.Geometry;
using Gsa.Core.Randomness;

namespace Gsa.Core.Sensors.Gnss;

/// <summary>Primitive replay state, including the last shared sample and every RNG component.</summary>
public readonly record struct CommonGnssErrorState(
    GaussianRandomState Noise,
    double SigmaMetres,
    double CorrelationTimeSeconds,
    bool Initialized,
    double LastTimestampSeconds,
    double ErrorEastMetres,
    double ErrorNorthMetres,
    double ErrorUpMetres);

/// <summary>
/// Stationary correlated ENU error for observation synthesis. Query it once per physical time
/// and share its result among receivers; it never belongs to a measured observation.
/// </summary>
public sealed class CommonGnssErrorProcess
{
    private readonly GaussianRandom _noise;
    private readonly double _sigma;
    private readonly double _correlationTime;
    private bool _initialized;
    private double _lastTime;
    private Vector3d _error;

    public CommonGnssErrorProcess(ulong seed, double sigmaMetres, double correlationTimeSeconds)
    {
        ValidateConfiguration(sigmaMetres, correlationTimeSeconds);
        _noise = new(seed);
        (_sigma, _correlationTime) = (sigmaMetres, correlationTimeSeconds);
    }

    public CommonGnssErrorProcess(CommonGnssErrorState state)
    {
        ValidateConfiguration(state.SigmaMetres, state.CorrelationTimeSeconds);
        GnssSimulator.ValidateTime(state.LastTimestampSeconds);
        _error = new(state.ErrorEastMetres, state.ErrorNorthMetres, state.ErrorUpMetres);
        if (!state.Initialized && (state.LastTimestampSeconds != 0 || _error != Vector3d.Zero))
            throw new ArgumentException("An uninitialized process must not contain a previous sample.", nameof(state));
        _noise = new(state.Noise);
        (_sigma, _correlationTime, _initialized, _lastTime) =
            (state.SigmaMetres, state.CorrelationTimeSeconds, state.Initialized, state.LastTimestampSeconds);
    }

    public CommonGnssErrorState ExportState() => new(_noise.ExportState(), _sigma, _correlationTime,
        _initialized, _lastTime, _error.X, _error.Y, _error.Z);

    public Vector3d At(double timeSeconds)
    {
        GnssSimulator.ValidateTime(timeSeconds);
        if (_initialized && timeSeconds < _lastTime)
            throw new ArgumentOutOfRangeException(nameof(timeSeconds), "Common error cannot be queried backwards in time.");
        if (_initialized && timeSeconds == _lastTime)
            return _error;

        if (!_initialized)
            _error = Draw(_sigma);
        else
        {
            var alpha = Math.Exp(-(timeSeconds - _lastTime) / _correlationTime);
            var innovationSigma = _sigma * Math.Sqrt(Math.Max(0, 1 - alpha * alpha));
            _error = alpha * _error + Draw(innovationSigma);
        }
        _initialized = true;
        _lastTime = timeSeconds;
        return _error;
    }

    private Vector3d Draw(double sigma) => sigma == 0 ? Vector3d.Zero : new(
        sigma * _noise.NextGaussian(), sigma * _noise.NextGaussian(), sigma * _noise.NextGaussian());

    private static void ValidateConfiguration(double sigmaMetres, double correlationTimeSeconds)
    {
        if (!double.IsFinite(sigmaMetres) || sigmaMetres < 0)
            throw new ArgumentOutOfRangeException(nameof(sigmaMetres));
        if (!double.IsFinite(correlationTimeSeconds) || correlationTimeSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(correlationTimeSeconds));
    }
}
