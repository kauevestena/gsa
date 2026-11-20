using Gsa.Core.World;
using Godot;
using Dictionary = Godot.Collections.Dictionary;

namespace Gsa.Engine.World;

/// <summary>Scene-owned semantic identities; snapshots never expose the stored domain collections.</summary>
public sealed class SemanticRegistry
{
    private readonly System.Collections.Generic.Dictionary<string, WorldBoxObject> _objects = new(StringComparer.Ordinal);
    private readonly System.Collections.Generic.Dictionary<ulong, string> _instanceIds = new();

    public int Count => _objects.Count;
    public int ColliderCount => _objects.Values.Count(item => item.Collision);

    public void Register(WorldBoxObject definition, Node3D node)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(node);
        if (!_objects.TryAdd(definition.Id, definition))
            throw new InvalidOperationException($"Duplicate semantic object: {definition.Id}");
        _instanceIds.Add(node.GetInstanceId(), definition.Id);
        node.SetMeta("semantic_id", definition.Id);
        node.SetMeta("semantic_class", definition.ObjectClass);
        node.SetMeta("semantic_material", definition.Material);
        node.SetMeta("region_id", definition.RegionId);
        node.SetMeta("surface_ids", new Godot.Collections.Array<string>(definition.SurfaceIds));
        node.SetMeta("semantic_tags", new Godot.Collections.Array<string>(definition.Tags));
        node.SetMeta("dynamic", definition.Dynamic);
    }

    public void UpdatePhysicalPosition(string id, Gsa.Core.Geometry.Vector3d position)
    {
        if (_objects.TryGetValue(id, out var item)) _objects[id] = item.WithPhysicalPosition(position);
    }

    public void Clear()
    {
        _objects.Clear();
        _instanceIds.Clear();
    }

    public string[] GetIds() => _objects.Keys.Order(StringComparer.Ordinal).ToArray();

    public Dictionary GetById(string id) => _objects.TryGetValue(id, out var item) ? Snapshot(item) : new();

    public Dictionary GetByInstanceId(ulong instanceId) =>
        _instanceIds.TryGetValue(instanceId, out var id) ? GetById(id) : new();

    public bool TryGetFastMetadata(ulong instanceId, out string id, out string material)
    {
        if (_instanceIds.TryGetValue(instanceId, out var objId) && _objects.TryGetValue(objId, out var item))
        {
            id = item.Id;
            material = item.Material;
            return true;
        }
        id = string.Empty;
        material = string.Empty;
        return false;
    }

    private static Dictionary Snapshot(WorldBoxObject item) => new()
    {
        ["id"] = item.Id,
        ["class"] = item.ObjectClass,
        ["material"] = item.Material,
        ["region_id"] = item.RegionId,
        ["surface_ids"] = new Godot.Collections.Array<string>(item.SurfaceIds),
        ["tags"] = new Godot.Collections.Array<string>(item.Tags),
        ["dynamic"] = item.Dynamic,
        ["collision"] = item.Collision,
        ["position_enu"] = new Godot.Collections.Array<double>(
            [item.PositionEnu.X, item.PositionEnu.Y, item.PositionEnu.Z]),
        ["size_enu"] = new Godot.Collections.Array<double>([item.SizeEnu.X, item.SizeEnu.Y, item.SizeEnu.Z]),
    };
}
