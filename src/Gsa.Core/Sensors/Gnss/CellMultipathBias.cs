using System.Text;
using Gsa.Core.Geometry;
using Gsa.Core.Randomness;

namespace Gsa.Core.Sensors.Gnss;

/// <summary>Stable local horizontal bias, intentionally separate from common receiver error.</summary>
public static class CellMultipathBias
{
    public static Vector3d ForCell(ulong seed, string cellId, double amplitudeMetres, double facadeFactor = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cellId);
        if (!double.IsFinite(amplitudeMetres) || amplitudeMetres < 0)
            throw new ArgumentOutOfRangeException(nameof(amplitudeMetres));
        if (!double.IsFinite(facadeFactor) || facadeFactor < 1)
            throw new ArgumentOutOfRangeException(nameof(facadeFactor));
        var amplitude = amplitudeMetres * facadeFactor;
        if (!double.IsFinite(amplitude))
            throw new ArgumentOutOfRangeException(nameof(facadeFactor), "Amplified multipath must remain finite.");
        if (amplitude == 0)
            return Vector3d.Zero;

        var hash = 14695981039346656037UL;
        unchecked
        {
            foreach (var value in Encoding.UTF8.GetBytes(cellId))
                hash = (hash ^ value) * 1099511628211UL;
        }
        var random = new GaussianRandom(seed ^ hash);
        var angle = 2 * Math.PI * random.NextUniformOpenUnit();
        return new(amplitude * Math.Cos(angle), amplitude * Math.Sin(angle), 0);
    }
}
