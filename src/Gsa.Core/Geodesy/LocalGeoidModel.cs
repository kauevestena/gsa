using Gsa.Core.Geometry;

namespace Gsa.Core.Geodesy;

/// <summary>
/// The fictional SAO_DATUM_LOCAL_V1 height model, in metres.
/// This didactic plane is not a geographic geoid or an EPSG transformation.
/// </summary>
public static class LocalGeoidModel
{
    /// <summary>Returns Ng = 10 + 0.001(E-E0) + 0.0005(N-N0).</summary>
    public static double Separation(Vector3d position) => SeparationAt(position.X, position.Y);

    /// <summary>Converts ellipsoidal height h to orthometric height H = h - Ng.</summary>
    public static double ToOrthometricHeight(Vector3d position) =>
        RequireFinite(position.Z - Separation(position), nameof(position));

    /// <summary>Converts orthometric height H to ellipsoidal height h = H + Ng.</summary>
    public static double ToEllipsoidalHeight(double east, double north, double orthometricHeight)
    {
        RequireFinite(orthometricHeight, nameof(orthometricHeight));
        return RequireFinite(orthometricHeight + SeparationAt(east, north), nameof(orthometricHeight));
    }

    private static double SeparationAt(double east, double north)
    {
        RequireFinite(east, nameof(east));
        RequireFinite(north, nameof(north));
        var origin = LocalMapFrame.Default.Origin;
        return RequireFinite(10 + 0.001 * (east - origin.X) + 0.0005 * (north - origin.Y), nameof(east));
    }

    private static double RequireFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(parameterName, "Height model inputs and results must be finite.");
        return value;
    }
}
