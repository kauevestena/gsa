using Gsa.Core.Randomness;

namespace Gsa.Core.Sensors.Gnss;

public sealed record HeadingProfile
{
    public string Id { get; }
    public double SigmaRadians { get; }

    public HeadingProfile(string id, double sigmaRadians)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (!double.IsFinite(sigmaRadians) || sigmaRadians < 0)
            throw new ArgumentOutOfRangeException(nameof(sigmaRadians));
        (Id, SigmaRadians) = (id, sigmaRadians);
    }

    public static HeadingProfile Level0 { get; } = new("heading_level_0", 3 * Math.PI / 180);
    public static HeadingProfile Level1 { get; } = new("heading_level_1", 2 * Math.PI / 180);
    public static HeadingProfile Level2 { get; } = new("heading_level_2", Math.PI / 180);
}

public sealed record HeadingObservation
{
    public long Sequence { get; }
    public double TimestampSeconds { get; }
    public string ProfileId { get; }
    public double? MeasuredRadians { get; }
    public double? IndependentSigmaRadians { get; }
    public bool IsAvailable => MeasuredRadians.HasValue;

    internal HeadingObservation(long sequence, double time, string profile, double? measured, double? sigma)
    {
        (Sequence, TimestampSeconds, ProfileId, MeasuredRadians, IndependentSigmaRadians) =
            (sequence, time, profile, measured, sigma);
    }
}

public readonly record struct HeadingSimulatorState(GaussianRandomState IndependentNoise, long NextSequence);

/// <summary>No velocity input exists: an unreliable heading stays unavailable even when position is valid.</summary>
public sealed class HeadingSimulator
{
    private readonly GaussianRandom _noise;
    private long _nextSequence;

    public HeadingSimulator(ulong headingNoiseSeed) => _noise = new(headingNoiseSeed);

    public HeadingSimulator(HeadingSimulatorState state)
    {
        if (state.NextSequence < 0)
            throw new ArgumentOutOfRangeException(nameof(state));
        _noise = new(state.IndependentNoise);
        _nextSequence = state.NextSequence;
    }

    public HeadingSimulatorState ExportState() => new(_noise.ExportState(), _nextSequence);

    public HeadingObservation Observe(double timeSeconds, double trueHeadingRadians, HeadingProfile profile, bool reliable)
    {
        GnssSimulator.ValidateTime(timeSeconds);
        ArgumentNullException.ThrowIfNull(profile);
        if (!double.IsFinite(trueHeadingRadians))
            throw new ArgumentOutOfRangeException(nameof(trueHeadingRadians));
        var sequence = _nextSequence;
        _nextSequence = checked(_nextSequence + 1);
        if (!reliable)
            return new(sequence, timeSeconds, profile.Id, null, null);
        var measured = WrapRadians(trueHeadingRadians) +
            (profile.SigmaRadians == 0 ? 0 : profile.SigmaRadians * _noise.NextGaussian());
        if (!double.IsFinite(measured))
            throw new ArgumentOutOfRangeException(nameof(profile), "Heading noise overflowed.");
        return new(sequence, timeSeconds, profile.Id, WrapRadians(measured), profile.SigmaRadians);
    }

    public static double WrapRadians(double angle)
    {
        if (!double.IsFinite(angle))
            throw new ArgumentOutOfRangeException(nameof(angle));
        var wrapped = angle % (2 * Math.PI);
        if (wrapped >= Math.PI) wrapped -= 2 * Math.PI;
        if (wrapped < -Math.PI) wrapped += 2 * Math.PI;
        return wrapped;
    }
}
