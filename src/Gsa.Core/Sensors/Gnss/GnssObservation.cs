using Gsa.Core.Geometry;
using Gsa.Core.Randomness;

namespace Gsa.Core.Sensors.Gnss;

public enum GnssSolutionKind { Unavailable, Autonomous, Dgnss, RtkFloat, RtkFix }

/// <summary>Measurement-only output. Sigma describes independent noise, not total error.</summary>
public sealed record GnssObservation
{
    public long Sequence { get; }
    public double TimestampSeconds { get; }
    public string ProfileId { get; }
    public GnssSolutionKind Solution { get; }
    public Vector3d? MeasuredPositionEnu { get; }
    public double Dop { get; }
    public double? IndependentSigmaMetres { get; }
    public bool IsAvailable => Solution != GnssSolutionKind.Unavailable;

    internal GnssObservation(long sequence, double timestamp, string profileId,
        GnssSolutionKind solution, Vector3d? position, double dop, double? sigma)
    {
        (Sequence, TimestampSeconds, ProfileId, Solution, MeasuredPositionEnu, Dop, IndependentSigmaMetres) =
            (sequence, timestamp, profileId, solution, position, dop, sigma);
    }
}

public readonly record struct GnssSimulatorState(GaussianRandomState IndependentNoise, long NextSequence);
