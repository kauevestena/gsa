using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json;
using Gsa.Core.Geometry;
using Gsa.Core.Serialization;

namespace Gsa.Core.World;

/// <summary>Finite ENU bounds, expressed in metres without engine numeric types.</summary>
public readonly record struct WorldBounds
{
    public Vector3d Min { get; }
    public Vector3d Max { get; }

    public WorldBounds(Vector3d min, Vector3d max)
    {
        if (min.X >= max.X || min.Y >= max.Y || min.Z >= max.Z)
            throw new ArgumentException("World bounds must increase on all three axes.");
        (Min, Max) = (min, max);
    }

    public bool Contains(WorldBounds other) =>
        other.Min.X >= Min.X && other.Min.Y >= Min.Y && other.Min.Z >= Min.Z &&
        other.Max.X <= Max.X && other.Max.Y <= Max.Y && other.Max.Z <= Max.Z;

    public bool ContainsBox(Vector3d position, Vector3d size) =>
        position.X - size.X / 2 >= Min.X && position.X + size.X / 2 <= Max.X &&
        position.Y - size.Y / 2 >= Min.Y && position.Y + size.Y / 2 <= Max.Y &&
        position.Z - size.Z / 2 >= Min.Z && position.Z + size.Z / 2 <= Max.Z;
}

/// <summary>An immutable semantic box. Dimensions are ordered east, north, up.</summary>
public sealed class WorldBoxObject
{
    public string Id { get; }
    public string ObjectClass { get; }
    public string Material { get; }
    public string RegionId { get; }
    public IReadOnlyList<string> SurfaceIds { get; }
    public IReadOnlyList<string> Tags { get; }
    public bool Dynamic { get; }
    public bool Collision { get; }
    public Vector3d PositionEnu { get; }
    public Vector3d SizeEnu { get; }

    internal WorldBoxObject(string id, string objectClass, string material, string regionId,
        IEnumerable<string> surfaceIds, IEnumerable<string> tags, bool dynamic, bool collision,
        Vector3d positionEnu, Vector3d sizeEnu)
    {
        (Id, ObjectClass, Material, RegionId) = (id, objectClass, material, regionId);
        SurfaceIds = Array.AsReadOnly(surfaceIds.ToArray());
        Tags = Array.AsReadOnly(tags.ToArray());
        (Dynamic, Collision, PositionEnu, SizeEnu) = (dynamic, collision, positionEnu, sizeEnu);
    }
}

public sealed class WorldCellDefinition
{
    public string Id { get; }
    public WorldBounds Bounds { get; }
    public string EnvironmentClass { get; }

    internal WorldCellDefinition(string id, WorldBounds bounds, string environmentClass) =>
        (Id, Bounds, EnvironmentClass) = (id, bounds, environmentClass);
}

/// <summary>Neutral source for both render geometry and physics proxies of the M1 graybox.</summary>
public sealed class WorldSceneManifest
{
    private static readonly HashSet<string> Materials =
        new(["asphalt", "concrete", "glass", "water", "metal", "vegetation"], StringComparer.Ordinal);

    public int SchemaVersion => 1;
    public string SceneId { get; }
    public int MapVersion { get; }
    public string DatumId { get; }
    public Vector3d OriginEnu { get; }
    public WorldBounds Bounds { get; }
    public IReadOnlyList<WorldCellDefinition> Cells { get; }
    public IReadOnlyList<WorldBoxObject> Objects { get; }
    public string CanonicalGeometryHash { get; }

    private WorldSceneManifest(string sceneId, int mapVersion, string datumId, Vector3d origin,
        WorldBounds bounds, WorldCellDefinition[] cells, WorldBoxObject[] objects)
    {
        (SceneId, MapVersion, DatumId, OriginEnu, Bounds) = (sceneId, mapVersion, datumId, origin, bounds);
        Cells = Array.AsReadOnly(cells);
        Objects = Array.AsReadOnly(objects);
        CanonicalGeometryHash = ComputeCanonicalHash();
    }

    public static WorldSceneManifest Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        CheckObject(root, "$", "schema_version", "scene_id", "map_version", "datum_id", "origin_enu",
            "bounds_enu", "cells", "objects");
        if (Integer(root, "schema_version", "$") != 1)
            throw Invalid("$.schema_version", "Only schema_version 1 is supported.");
        var sceneId = Text(root, "scene_id", "$");
        var mapVersion = Integer(root, "map_version", "$");
        if (mapVersion < 1)
            throw Invalid("$.map_version", "Map version must be positive.");
        var datum = Text(root, "datum_id", "$");
        if (datum != LocalMapFrame.DatumId)
            throw Invalid("$.datum_id", "The scene must use SAO_DATUM_LOCAL_V1.");
        var origin = Vector(root.GetProperty("origin_enu"), "$.origin_enu");
        if (origin != LocalMapFrame.Default.Origin)
            throw Invalid("$.origin_enu", "The scene must preserve the documented local origin.");
        var bounds = ReadBounds(root.GetProperty("bounds_enu"), "$.bounds_enu");
        var cells = new List<WorldCellDefinition>();
        var cellIds = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var item in Items(root, "cells", "$"))
        {
            var field = $"$.cells[{index++}]";
            CheckObject(item, field, "id", "bounds_enu", "environment_class");
            var id = Text(item, "id", field);
            if (!cellIds.Add(id))
                throw Invalid(field + ".id", "Duplicate cell ID.");
            var cellBounds = ReadBounds(item.GetProperty("bounds_enu"), field + ".bounds_enu");
            if (!bounds.Contains(cellBounds))
                throw Invalid(field + ".bounds_enu", "Cell lies outside scene bounds.");
            cells.Add(new WorldCellDefinition(id, cellBounds, Text(item, "environment_class", field)));
        }
        if (cells.Count == 0)
            throw Invalid("$.cells", "At least one reference cell is required.");

        var objects = new List<WorldBoxObject>();
        var objectIds = new HashSet<string>(StringComparer.Ordinal);
        index = 0;
        foreach (var item in Items(root, "objects", "$"))
        {
            var field = $"$.objects[{index++}]";
            CheckObject(item, field, "id", "class", "material", "region_id", "surface_ids", "tags",
                "dynamic", "position_enu", "size_enu", "collision");
            var id = Text(item, "id", field);
            if (!objectIds.Add(id))
                throw Invalid(field + ".id", "Duplicate object ID.");
            var material = Text(item, "material", field);
            if (!Materials.Contains(material))
                throw Invalid(field + ".material", "Unknown material.");
            var dynamic = Boolean(item, "dynamic", field);
            if (dynamic)
                throw Invalid(field + ".dynamic", "The initial graybox supports static objects only.");
            var position = Vector(item.GetProperty("position_enu"), field + ".position_enu");
            var size = Vector(item.GetProperty("size_enu"), field + ".size_enu");
            if (size.X <= 0 || size.Y <= 0 || size.Z <= 0)
                throw Invalid(field + ".size_enu", "Box dimensions must be positive.");
            if (!bounds.ContainsBox(position, size))
                throw Invalid(field + ".position_enu", "The full box must lie inside scene bounds.");
            objects.Add(new WorldBoxObject(id, Text(item, "class", field), material,
                Text(item, "region_id", field), Strings(item, "surface_ids", field), Strings(item, "tags", field),
                dynamic, Boolean(item, "collision", field), position, size));
        }
        if (objects.Count == 0)
            throw Invalid("$.objects", "At least one semantic object is required.");
        return new WorldSceneManifest(sceneId, mapVersion, datum, origin, bounds, cells.ToArray(), objects.ToArray());
    }

    private string ComputeCanonicalHash()
    {
        static double[] Coordinates(Vector3d value) => [value.X, value.Y, value.Z];
        static object BoundsValue(WorldBounds value) => new { min = Coordinates(value.Min), max = Coordinates(value.Max) };
        var canonical = new
        {
            schema_version = SchemaVersion, scene_id = SceneId, map_version = MapVersion, datum_id = DatumId,
            origin_enu = Coordinates(OriginEnu), bounds_enu = BoundsValue(Bounds),
            cells = Cells.OrderBy(c => c.Id, StringComparer.Ordinal).Select(c => new
            {
                id = c.Id, bounds_enu = BoundsValue(c.Bounds), environment_class = c.EnvironmentClass,
            }),
            objects = Objects.OrderBy(o => o.Id, StringComparer.Ordinal).Select(o => new
            {
                id = o.Id, @class = o.ObjectClass, material = o.Material, region_id = o.RegionId,
                surface_ids = o.SurfaceIds.Order(StringComparer.Ordinal), tags = o.Tags.Order(StringComparer.Ordinal),
                dynamic = o.Dynamic, collision = o.Collision, position_enu = Coordinates(o.PositionEnu), size_enu = Coordinates(o.SizeEnu),
            }),
        };
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(canonical))).ToLowerInvariant();
    }

    private static void CheckObject(JsonElement value, string field, params string[] fields)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw Invalid(field, "Expected an object.");
        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!present.Add(property.Name))
                throw Invalid(field + "." + property.Name, "Duplicate property.");
            if (!fields.Contains(property.Name, StringComparer.Ordinal))
                throw Invalid(field + "." + property.Name, "Unknown field for schema version 1.");
        }
        foreach (var required in fields)
            if (!present.Contains(required))
                throw Invalid(field + "." + required, "Required field is absent.");
    }

    private static IEnumerable<JsonElement> Items(JsonElement parent, string property, string field)
    {
        var value = parent.GetProperty(property);
        if (value.ValueKind != JsonValueKind.Array)
            throw Invalid(field + "." + property, "Expected an array.");
        return value.EnumerateArray();
    }

    private static string Text(JsonElement parent, string property, string field)
    {
        var value = parent.GetProperty(property);
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw Invalid(field + "." + property, "Expected a nonempty string.");
        return value.GetString()!;
    }

    private static int Integer(JsonElement parent, string property, string field)
    {
        if (!JsonNumbers.TryGetInt64(parent.GetProperty(property), out var value) || value is < int.MinValue or > int.MaxValue)
            throw Invalid(field + "." + property, "Expected an exact Int32 value.");
        return (int)value;
    }

    private static bool Boolean(JsonElement parent, string property, string field)
    {
        var value = parent.GetProperty(property);
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw Invalid(field + "." + property, "Expected a boolean.");
        return value.GetBoolean();
    }

    private static string[] Strings(JsonElement parent, string property, string field)
    {
        var result = new List<string>();
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in Items(parent, property, field))
        {
            if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
                throw Invalid(field + "." + property, "IDs/tags must be nonempty strings.");
            var item = value.GetString()!;
            if (!unique.Add(item))
                throw Invalid(field + "." + property, "IDs/tags must be unique.");
            result.Add(item);
        }
        if (result.Count == 0)
            throw Invalid(field + "." + property, "At least one ID/tag is required.");
        return result.ToArray();
    }

    private static Vector3d Vector(JsonElement value, string field)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 3)
            throw Invalid(field, "Expected three finite ENU components.");
        var components = new double[3];
        var index = 0;
        foreach (var component in value.EnumerateArray())
        {
            if (component.ValueKind != JsonValueKind.Number || !component.TryGetDouble(out var number) || !double.IsFinite(number))
                throw Invalid(field, "Coordinate components must be finite numbers.");
            components[index++] = number;
        }
        return new Vector3d(components[0], components[1], components[2]);
    }

    private static WorldBounds ReadBounds(JsonElement value, string field)
    {
        CheckObject(value, field, "min", "max");
        var min = Vector(value.GetProperty("min"), field + ".min");
        var max = Vector(value.GetProperty("max"), field + ".max");
        if (min.X >= max.X || min.Y >= max.Y || min.Z >= max.Z)
            throw Invalid(field, "Bounds must increase on all axes.");
        return new WorldBounds(min, max);
    }

    private static FormatException Invalid(string field, string message) => new(field + ": " + message);
}
