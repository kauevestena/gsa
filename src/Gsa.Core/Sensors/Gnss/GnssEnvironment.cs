using Gsa.Core.Geometry;

namespace Gsa.Core.Sensors.Gnss;

/// <summary>Simulator inputs; a cell bias is never part of a measured observation.</summary>
public sealed record GnssEnvironment
{
    public double Openness { get; }
    public Vector3d MultipathBiasEnu { get; }
    public bool SignalAvailable { get; }
    public double Dop => 1 + 4 * (1 - Openness);

    public GnssEnvironment(double openness, Vector3d multipathBiasEnu, bool signalAvailable = true)
    {
        if (!double.IsFinite(openness) || openness < 0 || openness > 1)
            throw new ArgumentOutOfRangeException(nameof(openness));
        if (multipathBiasEnu.Z != 0)
            throw new ArgumentException("The cell multipath model is horizontal.", nameof(multipathBiasEnu));
        (Openness, MultipathBiasEnu, SignalAvailable) = (openness, multipathBiasEnu, signalAvailable);
    }
}
