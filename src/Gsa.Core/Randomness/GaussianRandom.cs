namespace Gsa.Core.Randomness;

/// <summary>Complete replay state, including Box–Muller's buffered second sample.</summary>
public readonly record struct GaussianRandomState(ulong State, bool HasSpare, double Spare);

/// <summary>Explicit SplitMix64 stream and Box–Muller normals; no runtime/global RNG.</summary>
public sealed class GaussianRandom
{
    private ulong _state;
    private bool _hasSpare;
    private double _spare;

    public GaussianRandom(ulong seed) => _state = seed;

    public GaussianRandom(GaussianRandomState state)
    {
        if (!double.IsFinite(state.Spare))
            throw new ArgumentOutOfRangeException(nameof(state), "Buffered Gaussian must be finite.");
        (_state, _hasSpare, _spare) = (state.State, state.HasSpare, state.Spare);
    }

    public GaussianRandomState ExportState() => new(_state, _hasSpare, _spare);

    public ulong NextUInt64()
    {
        unchecked
        {
            _state += 0x9E3779B97F4A7C15UL;
            var value = _state;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }
    }

    public double NextUniformOpenUnit() => ((NextUInt64() >> 12) + 0.5) / 4503599627370496.0;

    public double NextGaussian()
    {
        if (_hasSpare)
        {
            _hasSpare = false;
            return _spare;
        }
        var radius = Math.Sqrt(-2 * Math.Log(NextUniformOpenUnit()));
        var angle = 2 * Math.PI * NextUniformOpenUnit();
        _spare = radius * Math.Sin(angle);
        _hasSpare = true;
        return radius * Math.Cos(angle);
    }
}
