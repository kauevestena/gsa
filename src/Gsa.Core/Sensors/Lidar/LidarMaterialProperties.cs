namespace Gsa.Core.Sensors.Lidar;

public sealed record LidarMaterialResponse(
    string MaterialId,
    double NormalReturnProbability,
    double TransmissionFactor);

/// <summary>Material return/transmission, incidence cosine attenuation, rain and vegetation multi-return constants.</summary>
public static class LidarMaterialProperties
{
    public const double VegetationAdvanceMetres = 0.01;
    public const int MaxReturnsPerPulse = 3;
    public const double MinResidualEnergy = 0.1;
    public const double RainRangeFactor = 0.8;
    public const double RainSpuriousRate = 0.02;

    private static readonly IReadOnlyDictionary<string, LidarMaterialResponse> Materials =
        new Dictionary<string, LidarMaterialResponse>(StringComparer.Ordinal)
        {
            ["asphalt"] = new("asphalt", 0.80, 0.00),
            ["concrete"] = new("concrete", 0.90, 0.00),
            ["metal"] = new("metal", 0.70, 0.00),
            ["glass"] = new("glass", 0.10, 0.70),
            ["water"] = new("water", 0.05, 0.00),
            ["vegetation"] = new("vegetation", 0.60, 0.40),
        };

    public static LidarMaterialResponse Get(string material)
    {
        if (string.IsNullOrWhiteSpace(material) || !Materials.TryGetValue(material, out var value))
            throw new ArgumentException($"Unsupported LiDAR material: {material}", nameof(material));
        return value;
    }

    public static IReadOnlyCollection<string> KnownMaterials => (IReadOnlyCollection<string>)Materials.Keys;

    public static double GetEffectiveRangeMetres(double nominalRangeMetres, bool isRaining)
    {
        if (!double.IsFinite(nominalRangeMetres) || nominalRangeMetres <= 0)
            throw new ArgumentOutOfRangeException(nameof(nominalRangeMetres));
        return isRaining ? nominalRangeMetres * RainRangeFactor : nominalRangeMetres;
    }

    public static double ComputeReturnProbability(string material, double cosIncidence, double residualEnergy = 1.0)
    {
        var props = Get(material);
        var clampedIncidence = Math.Max(0.1, Math.Clamp(Math.Abs(cosIncidence), 0.0, 1.0));
        return Math.Clamp(props.NormalReturnProbability * clampedIncidence * Math.Clamp(residualEnergy, 0.0, 1.0), 0.0, 1.0);
    }
}

