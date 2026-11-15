using System.Text.Json;
using System.Text.Json.Nodes;
using Gsa.Core.Geometry;
using Gsa.Core.World;
using Xunit;

namespace Gsa.Core.Tests;

public sealed class SkyVisibilityTests
{
    [Fact]
    public void UnobstructedUpperHemisphereHasFullOpenness()
    {
        var manifest = Scene();
        var cache = SkyPrecomputer.Compute(manifest);
        var cell = Assert.Single(cache.Cells);
        Assert.Equal(new Vector3d(500005, 7400005, 102), cell.CenterEnu);
        Assert.Equal(1, cell.Openness);
        cache.ValidateFor(manifest);
    }

    [Fact]
    public void RoofAndWallsBlockTheCompleteUpperHemisphere()
    {
        var manifest = Scene([
            Box("roof", 5, 5, 105, 10, 10, 1),
            Box("west", 0.1, 5, 102.5, 0.2, 10, 5),
            Box("east", 9.9, 5, 102.5, 0.2, 10, 5),
            Box("south", 5, 0.1, 102.5, 10, 0.2, 5),
            Box("north", 5, 9.9, 102.5, 10, 0.2, 5),
        ]);
        Assert.Equal(0, Assert.Single(SkyPrecomputer.Compute(manifest).Cells).Openness);
    }

    [Fact]
    public void NearbyFacadeProducesPartialRatherThanBinarySky()
    {
        var manifest = Scene([Box("facade", 5, 8, 105, 10, 0.2, 10)]);
        var openness = Assert.Single(SkyPrecomputer.Compute(manifest).Cells).Openness;
        Assert.InRange(openness, 1.0 / 32, 31.0 / 32);
        Assert.Equal(Math.Round(openness * 32), openness * 32);
    }

    [Fact]
    public void NoncollidingRoofDoesNotBlockSky()
    {
        var manifest = Scene([Box("visual_roof", 5, 5, 105, 10, 10, 1, collision: false)]);
        Assert.Equal(1, Assert.Single(SkyPrecomputer.Compute(manifest).Cells).Openness);
    }

    [Fact]
    public void CacheSurvivesJsonRoundTripAndManifestReformatting()
    {
        var manifest = Scene([Box("facade", 5, 8, 105, 10, 0.2, 10)]);
        var cache = SkyVisibilityCache.Parse(SkyPrecomputer.Compute(manifest).ToJson());
        var reformatted = WorldSceneManifest.Parse(JsonNode.Parse(SceneJson([Box("facade", 5, 8, 105, 10, 0.2, 10)]))!
            .ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        cache.ValidateFor(reformatted);
        Assert.Equal(manifest.CanonicalGeometryHash, reformatted.CanonicalGeometryHash);
        Assert.True(cache.TryGetCell(new Vector3d(500005, 7400005, 102), out var cell));
        Assert.Equal(SkyPrecomputer.Compute(manifest).Cells[0], cell);
    }

    [Theory]
    [InlineData("map_version")]
    [InlineData("geometry")]
    public void SceneRevisionOrGeometryChangeInvalidatesCache(string change)
    {
        var original = Scene();
        var cache = SkyPrecomputer.Compute(original);
        var edited = JsonNode.Parse(SceneJson())!;
        if (change == "map_version") edited["map_version"] = 2;
        else edited["objects"]![0]!["size_enu"]![2] = 0.4;
        var manifest = WorldSceneManifest.Parse(edited.ToJsonString());
        Assert.Throws<FormatException>(() => cache.ValidateFor(manifest));
    }

    [Fact]
    public void QueryUsesHalfOpenBoundsAndRejectsDifferentFloors()
    {
        var cache = SkyPrecomputer.Compute(Scene());
        Assert.True(cache.TryGetCell(new Vector3d(500000, 7400000, 102), out var corner));
        Assert.Equal(0, corner!.EastIndex);
        Assert.False(cache.TryGetCell(new Vector3d(500010, 7400005, 102), out var outside));
        Assert.Null(outside);
        Assert.False(cache.TryGetCell(new Vector3d(500005, 7400010, 102), out _));
        Assert.False(cache.TryGetCell(new Vector3d(499999, 7400005, 102), out _));
        Assert.True(cache.TryGetCell(new Vector3d(500005, 7400005, 102.5), out _));
        Assert.False(cache.TryGetCell(new Vector3d(500005, 7400005, 102.501), out _));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            cache.TryGetCell(new Vector3d(500005, 7400005, 102), out _, double.NaN));
    }

    [Fact]
    public void PartialBoundaryCellUsesCenterOfItsClippedArea()
    {
        var cache = SkyPrecomputer.Compute(Scene(width: 25));
        Assert.Equal(3, cache.EastCount);
        Assert.Equal(3, cache.Cells.Count);
        Assert.Equal(500022.5, cache.Cells[2].CenterEnu.X);
        Assert.True(cache.TryGetCell(new Vector3d(500024.9, 7400005, 102), out var last));
        Assert.Equal(2, last!.EastIndex);
        SkyVisibilityCache.Parse(cache.ToJson()).ValidateFor(Scene(width: 25));
    }

    [Theory]
    [InlineData("schema_version")]
    [InlineData("openness")]
    [InlineData("center_enu")]
    [InlineData("cells")]
    [InlineData("geometry_sha256")]
    public void CorruptOrIncompleteCacheIsRejected(string field)
    {
        var cache = SkyPrecomputer.Compute(Scene());
        var json = JsonNode.Parse(cache.ToJson())!;
        switch (field)
        {
            case "schema_version": json[field] = 2; break;
            case "openness": json["cells"]![0]![field] = 0.123; break;
            case "center_enu": json["cells"]![0]![field]![0] = 500006; break;
            case "cells": ((JsonArray)json[field]!).Clear(); break;
            case "geometry_sha256": json[field] = "not_a_hash"; break;
        }
        Assert.Throws<FormatException>(() => SkyVisibilityCache.Parse(json.ToJsonString()));
    }

    [Fact]
    public void DuplicateGridCellIsRejectedEvenWhenArrayLengthMatches()
    {
        var cache = SkyPrecomputer.Compute(Scene(width: 20));
        var json = JsonNode.Parse(cache.ToJson())!;
        json["cells"]![1] = json["cells"]![0]!.DeepClone();
        Assert.Throws<FormatException>(() => SkyVisibilityCache.Parse(json.ToJsonString()));
    }

    [Theory]
    [InlineData(0, 2, 32)]
    [InlineData(double.NaN, 2, 32)]
    [InlineData(10, -1, 32)]
    [InlineData(10, double.PositiveInfinity, 32)]
    [InlineData(10, 2, 0)]
    [InlineData(10, 2, 4097)]
    [InlineData(0.001, 2, 32)]
    public void InvalidOrExcessiveSamplingSettingsFailExplicitly(double size, double height, int count) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SkyPrecomputer.Compute(Scene(), size, height, count));

    private static WorldSceneManifest Scene(object[]? extra = null, double width = 10) =>
        WorldSceneManifest.Parse(SceneJson(extra, width));

    private static string SceneJson(object[]? extra = null, double width = 10)
    {
        var bounds = new { min = new[] { 500000.0, 7400000.0, 90 }, max = new[] { 500000 + width, 7400010.0, 120 } };
        var objects = new List<object> { Box("ground", width / 2, 5, 99.9, width, 10, 0.2) };
        objects.AddRange(extra ?? []);
        return JsonSerializer.Serialize(new
        {
            schema_version = 1, scene_id = "sky_fixture", map_version = 1, datum_id = "SAO_DATUM_LOCAL_V1",
            origin_enu = new[] { 500000.0, 7400000.0, 100 }, bounds_enu = bounds,
            cells = new[] { new { id = "cell", bounds_enu = bounds, environment_class = "test" } },
            objects,
        });
    }

    private static object Box(string id, double east, double north, double up,
        double eastSize, double northSize, double upSize, bool collision = true) => new
    {
        id, @class = "test_surface", material = "concrete", region_id = "old_center",
        surface_ids = new[] { "test_surface" }, tags = new[] { "sky_fixture" }, dynamic = false,
        position_enu = new[] { 500000 + east, 7400000 + north, up },
        size_enu = new[] { eastSize, northSize, upSize }, collision,
    };
}
