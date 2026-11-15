namespace Gsa.Core.Sensors.Gnss;

public sealed record GnssProfile
{
    public string Id { get; }
    public double BaseSigmaMetres { get; }

    public GnssProfile(string id, double baseSigmaMetres)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (!double.IsFinite(baseSigmaMetres) || baseSigmaMetres < 0)
            throw new ArgumentOutOfRangeException(nameof(baseSigmaMetres));
        (Id, BaseSigmaMetres) = (id, baseSigmaMetres);
    }

    public static GnssProfile Cellphone { get; } = new("gnss_cellphone", 3);
    public static GnssProfile Navigation { get; } = new("gnss_navigation", 1.5);
}
