using Gsa.Core.World;
using Godot;

namespace Gsa.Engine.World;

/// <summary>Imports a versioned static cache without treating a missing cell as open sky.</summary>
public sealed class SkyCacheAdapter
{
    private readonly GodotCoordinateAdapter _coordinates = new();
    public SkyVisibilityCache Cache { get; }

    public SkyCacheAdapter(WorldSceneManifest manifest, string cacheJson)
    {
        Cache = SkyVisibilityCache.Parse(cacheJson);
        Cache.ValidateFor(manifest);
    }

    public bool TryGetOpenness(Vector3 localPosition, out double openness)
    {
        openness = default;
        if (!localPosition.IsFinite() || !Cache.TryGetCell(_coordinates.ToMap(localPosition), out var cell))
            return false;
        openness = cell!.Openness;
        return true;
    }
}
