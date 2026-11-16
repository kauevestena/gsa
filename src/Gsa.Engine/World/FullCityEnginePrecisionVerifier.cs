using Gsa.Core.Geometry;
using Gsa.Core.World;
using Godot;

namespace Gsa.Engine.World;

/// <summary>Measures the actual double-to-Godot-float boundary separately from neutral-frame algebra.</summary>
public static class FullCityEnginePrecisionVerifier
{
    // At ~2 km from the fixed origin, float quantization is sub-millimetric, not nanometric.
    public const double MaximumEngineRoundTripErrorMetres = 0.0005;

    public static void Run(WorldSceneManifest tutorial)
    {
        var (_, network, _) = FullCityGeodeticNetwork.AssembleFullCity(tutorial);
        var adapter = new GodotCoordinateAdapter();
        var points = network.Benchmarks.Values.SelectMany(node => new[]
        {
            node.PhysicalPositionEnu,
            node.PhysicalPositionEnu + new Vector3d(0.123456789, 0.765432109, 0.0123456789),
        }).ToArray();
        var worst = 0.0;
        foreach (var point in points)
        {
            Vector3 engineVector = adapter.ToGodot(point);
            var reconstructed = adapter.ToMap(engineVector);
            var error = (point - reconstructed).Length;
            if (!double.IsFinite(error) || error > MaximumEngineRoundTripErrorMetres)
                throw new InvalidOperationException($"Godot float round-trip error {error:R} m exceeds city budget.");
            worst = Math.Max(worst, error);
        }
        if (worst <= 1e-9)
            throw new InvalidOperationException("Fractional city probes did not exercise float quantization.");
        GD.Print(FormattableString.Invariant($"GSA_CITY_ENGINE_PRECISION_SMOKE_OK points={points.Length} max_error_m={worst:R} budget_m={MaximumEngineRoundTripErrorMetres:R}"));
    }
}
