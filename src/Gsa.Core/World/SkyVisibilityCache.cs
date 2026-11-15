using System.Text.Json;
using Gsa.Core.Geometry;
using Gsa.Core.Serialization;

namespace Gsa.Core.World;

public sealed record SkyCell(int EastIndex, int NorthIndex, Vector3d CenterEnu, double Openness);

/// <summary>A versioned immutable visibility grid; validation precedes use against a scene.</summary>
public sealed class SkyVisibilityCache
{
    private readonly SkyCell[] _cells;
    public int SchemaVersion => 1;
    public string Algorithm => SkyPrecomputer.Algorithm;
    public string SceneId { get; }
    public int MapVersion { get; }
    public string DatumId { get; }
    public string GeometryHash { get; }
    public Vector3d OriginEnu { get; }
    public WorldBounds Bounds { get; }
    public double CellSizeM { get; }
    public double EyeHeightM { get; }
    public int DirectionCount { get; }
    public int EastCount { get; }
    public int NorthCount { get; }
    public IReadOnlyList<SkyCell> Cells { get; }

    internal SkyVisibilityCache(string sceneId, int mapVersion, string datumId, string geometryHash,
        Vector3d origin, WorldBounds bounds, double cellSizeM, double eyeHeightM, int directionCount,
        int eastCount, int northCount, SkyCell[] cells)
    {
        (SceneId, MapVersion, DatumId, GeometryHash, OriginEnu, Bounds) =
            (sceneId, mapVersion, datumId, geometryHash, origin, bounds);
        (CellSizeM, EyeHeightM, DirectionCount, EastCount, NorthCount) =
            (cellSizeM, eyeHeightM, directionCount, eastCount, northCount);
        _cells = cells.ToArray();
        Cells = Array.AsReadOnly(_cells);
    }

    public void ValidateFor(WorldSceneManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (SceneId != manifest.SceneId || MapVersion != manifest.MapVersion ||
            DatumId != manifest.DatumId || GeometryHash != manifest.CanonicalGeometryHash ||
            OriginEnu != manifest.OriginEnu || Bounds != manifest.Bounds)
            throw Invalid("$", "Sky cache does not match this scene revision; regenerate it.");
    }

    public bool TryGetCell(Vector3d position, out SkyCell? cell, double maxHeightDeltaM = 0.5)
    {
        if (!double.IsFinite(maxHeightDeltaM) || maxHeightDeltaM < 0)
            throw new ArgumentOutOfRangeException(nameof(maxHeightDeltaM));
        cell = null;
        if (position.X < Bounds.Min.X || position.X >= Bounds.Max.X ||
            position.Y < Bounds.Min.Y || position.Y >= Bounds.Max.Y ||
            Math.Abs(position.Z - (OriginEnu.Z + EyeHeightM)) > maxHeightDeltaM)
            return false;
        var east = (int)Math.Floor((position.X - Bounds.Min.X) / CellSizeM);
        var north = (int)Math.Floor((position.Y - Bounds.Min.Y) / CellSizeM);
        if (east < 0 || east >= EastCount || north < 0 || north >= NorthCount)
            return false;
        cell = _cells[north * EastCount + east];
        return true;
    }

    public string ToJson()
    {
        static double[] Coordinates(Vector3d value) => [value.X, value.Y, value.Z];
        return JsonSerializer.Serialize(new
        {
            schema_version = SchemaVersion, algorithm = Algorithm, scene_id = SceneId,
            map_version = MapVersion, datum_id = DatumId, geometry_sha256 = GeometryHash,
            origin_enu = Coordinates(OriginEnu),
            bounds_enu = new { min = Coordinates(Bounds.Min), max = Coordinates(Bounds.Max) },
            cell_size_m = CellSizeM, eye_height_m = EyeHeightM, direction_count = DirectionCount,
            east_count = EastCount, north_count = NorthCount,
            cells = _cells.Select(c => new
            {
                east_index = c.EastIndex, north_index = c.NorthIndex,
                center_enu = Coordinates(c.CenterEnu), openness = c.Openness,
            }),
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    public static SkyVisibilityCache Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var parsed = JsonDocument.Parse(json);
        var root = parsed.RootElement;
        Object(root, "$", "schema_version", "algorithm", "scene_id", "map_version", "datum_id",
            "geometry_sha256", "origin_enu", "bounds_enu", "cell_size_m", "eye_height_m",
            "direction_count", "east_count", "north_count", "cells");
        if (Integer(root, "schema_version", "$") != 1)
            throw Invalid("$.schema_version", "Only schema_version 1 is supported.");
        if (Text(root, "algorithm", "$") != SkyPrecomputer.Algorithm)
            throw Invalid("$.algorithm", "Unknown sky sampling algorithm.");
        var sceneId = Text(root, "scene_id", "$");
        var mapVersion = Integer(root, "map_version", "$");
        if (mapVersion < 1) throw Invalid("$.map_version", "Map version must be positive.");
        var datum = Text(root, "datum_id", "$");
        if (datum != LocalMapFrame.DatumId) throw Invalid("$.datum_id", "Unsupported datum.");
        var hash = Text(root, "geometry_sha256", "$");
        if (hash.Length != 64 || hash.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw Invalid("$.geometry_sha256", "Expected a lowercase SHA256 value.");
        var origin = Vector(root.GetProperty("origin_enu"), "$.origin_enu");
        if (origin != LocalMapFrame.Default.Origin) throw Invalid("$.origin_enu", "Unsupported origin.");
        var boundsValue = root.GetProperty("bounds_enu");
        Object(boundsValue, "$.bounds_enu", "min", "max");
        WorldBounds bounds;
        try
        {
            bounds = new WorldBounds(Vector(boundsValue.GetProperty("min"), "$.bounds_enu.min"),
                Vector(boundsValue.GetProperty("max"), "$.bounds_enu.max"));
        }
        catch (ArgumentException error) { throw Invalid("$.bounds_enu", error.Message); }
        var cellSize = Number(root.GetProperty("cell_size_m"), "$.cell_size_m");
        var eyeHeight = Number(root.GetProperty("eye_height_m"), "$.eye_height_m");
        var directionCount = Integer(root, "direction_count", "$");
        int eastCount, northCount;
        try
        {
            SkyPrecomputer.ValidateSettings(cellSize, eyeHeight, directionCount);
            eastCount = SkyPrecomputer.GridCount(bounds.Min.X, bounds.Max.X, cellSize);
            northCount = SkyPrecomputer.GridCount(bounds.Min.Y, bounds.Max.Y, cellSize);
        }
        catch (ArgumentException error) { throw Invalid("$grid", error.Message); }
        if ((long)eastCount * northCount > SkyPrecomputer.MaximumCells)
            throw Invalid("$grid", "Sky grid exceeds one million cells.");
        if (Integer(root, "east_count", "$") != eastCount || Integer(root, "north_count", "$") != northCount)
            throw Invalid("$grid", "Grid dimensions do not match scene bounds and cell size.");
        var height = origin.Z + eyeHeight;
        if (!double.IsFinite(height) || height < bounds.Min.Z || height > bounds.Max.Z)
            throw Invalid("$.eye_height_m", "Sampling plane lies outside scene bounds.");
        var cellsValue = root.GetProperty("cells");
        if (cellsValue.ValueKind != JsonValueKind.Array || cellsValue.GetArrayLength() != eastCount * northCount)
            throw Invalid("$.cells", "Every grid cell must appear exactly once.");
        var cells = new SkyCell[eastCount * northCount];
        var index = 0;
        foreach (var value in cellsValue.EnumerateArray())
        {
            var field = $"$.cells[{index++}]";
            Object(value, field, "east_index", "north_index", "center_enu", "openness");
            var east = Integer(value, "east_index", field);
            var north = Integer(value, "north_index", field);
            if (east < 0 || east >= eastCount || north < 0 || north >= northCount)
                throw Invalid(field, "Cell indices lie outside the grid.");
            var slot = north * eastCount + east;
            if (cells[slot] is not null) throw Invalid(field, "Duplicate grid cell.");
            var center = Vector(value.GetProperty("center_enu"), field + ".center_enu");
            if (center != SkyPrecomputer.CellCenter(bounds, cellSize, height, east, north))
                throw Invalid(field + ".center_enu", "Cell center does not match the declared grid.");
            var openness = Number(value.GetProperty("openness"), field + ".openness");
            if (openness < 0 || openness > 1 ||
                Math.Abs(openness * directionCount - Math.Round(openness * directionCount)) > 1e-9)
                throw Invalid(field + ".openness", "Openness must be a fraction of the sampled directions.");
            cells[slot] = new SkyCell(east, north, center, openness);
        }
        return new SkyVisibilityCache(sceneId, mapVersion, datum, hash, origin, bounds,
            cellSize, eyeHeight, directionCount, eastCount, northCount, cells);
    }

    private static void Object(JsonElement value, string field, params string[] expected)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid(field, "Expected an object.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!seen.Add(property.Name) || !expected.Contains(property.Name, StringComparer.Ordinal))
                throw Invalid(field + "." + property.Name, "Duplicate or unknown field.");
        foreach (var name in expected)
            if (!seen.Contains(name)) throw Invalid(field + "." + name, "Required field is absent.");
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

    private static double Number(JsonElement value, string field)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) || !double.IsFinite(number))
            throw Invalid(field, "Expected a finite number.");
        return number;
    }

    private static Vector3d Vector(JsonElement value, string field)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 3)
            throw Invalid(field, "Expected three ENU components.");
        return new Vector3d(Number(value[0], field), Number(value[1], field), Number(value[2], field));
    }

    private static FormatException Invalid(string field, string message) => new(field + ": " + message);
}
