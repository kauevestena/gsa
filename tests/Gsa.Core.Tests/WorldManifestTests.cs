using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gsa.Core.Geometry;
using Gsa.Core.World;
using Xunit;

namespace Gsa.Core.Tests;

public sealed class WorldManifestTests
{
    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null &&
                   !File.Exists(Path.Combine(directory.FullName, "game", "world", "tutorial-block.json")))
                directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("Tutorial block fixture not found.");
        }
    }

    private static string ReadFixture() =>
        File.ReadAllText(Path.Combine(RepositoryRoot, "game", "world", "tutorial-block.json"));

    private static JsonObject FixtureNode() => JsonNode.Parse(ReadFixture())!.AsObject();

    [Fact]
    public void TutorialContainsTwentyStableFacadeTargetsAndAllRequiredSemantics()
    {
        var manifest = WorldSceneManifest.Parse(ReadFixture());
        var facades = manifest.Objects.Where(item => item.ObjectClass == "building_facade")
            .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        Assert.Equal(Enumerable.Range(1, 20).Select(index => $"facade_{index:00}"), facades.Select(item => item.Id));

        using var catalog = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(RepositoryRoot, "content", "regions", "old-center.json")));
        foreach (var target in catalog.RootElement.GetProperty("features").EnumerateArray())
        {
            var id = target.GetProperty("id").GetString();
            var expected = target.GetProperty("position_enu").EnumerateArray().Select(n => n.GetDouble()).ToArray();
            var facade = Assert.Single(facades, candidate => candidate.Id == id);
            Assert.Equal(new Vector3d(expected[0], expected[1], expected[2]), facade.PositionEnu);
            Assert.Equal("concrete", facade.Material);
            Assert.Contains("privacy_review", facade.Tags);
            Assert.Contains("commerce_facade_planes", facade.SurfaceIds);
            Assert.True(facade.Collision);
        }

        Assert.Contains(manifest.Objects, item => item.Id == "office_entry" && item.Tags.Contains("entrance"));
        Assert.Contains(manifest.Objects, item => item.Id == "unload_point" && item.Tags.Contains("unload_point"));
        Assert.Equal(2, manifest.Objects.Count(item => item.ObjectClass == "survey_control"));
        Assert.Contains(manifest.Objects, item => item.Material == "glass" && item.Collision);
        Assert.Contains(manifest.Objects, item => item.Material == "water" && !item.Collision);
        Assert.All(manifest.Objects, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Id));
            Assert.False(string.IsNullOrWhiteSpace(item.ObjectClass));
            Assert.False(string.IsNullOrWhiteSpace(item.RegionId));
            Assert.NotEmpty(item.SurfaceIds);
            Assert.NotEmpty(item.Tags);
        });
    }

    [Fact]
    public void CanonicalHashIgnoresJsonFormattingAndUnorderedObjectLists()
    {
        var original = WorldSceneManifest.Parse(ReadFixture());
        var node = FixtureNode();
        var objects = node["objects"]!.AsArray();
        node["objects"] = new JsonArray(objects.Reverse().Select(item => item!.DeepClone()).ToArray());
        var reversedProperties = new JsonObject(node.Reverse()
            .Select(property => KeyValuePair.Create(property.Key, property.Value?.DeepClone())));
        var compact = WorldSceneManifest.Parse(reversedProperties.ToJsonString());
        Assert.Equal(original.CanonicalGeometryHash, compact.CanonicalGeometryHash);
        Assert.Equal(64, compact.CanonicalGeometryHash.Length);
    }

    [Theory]
    [InlineData("position")]
    [InlineData("size")]
    [InlineData("material")]
    [InlineData("tag")]
    [InlineData("map_version")]
    public void CanonicalHashChangesWhenCaptureRelevantDataChanges(string change)
    {
        var node = FixtureNode();
        var original = WorldSceneManifest.Parse(node.ToJsonString());
        var facade = node["objects"]!.AsArray().First(item => item!["id"]!.GetValue<string>() == "facade_01")!;
        switch (change)
        {
            case "position": facade["position_enu"]![0] = 500020.25; break;
            case "size": facade["size_enu"]![0] = 7.5; break;
            case "material": facade["material"] = "glass"; break;
            case "tag": facade["tags"]!.AsArray().Add("additional_capture_tag"); break;
            case "map_version": node["map_version"] = 2; break;
        }
        Assert.NotEqual(original.CanonicalGeometryHash,
            WorldSceneManifest.Parse(node.ToJsonString()).CanonicalGeometryHash);
    }

    [Fact]
    public void ReadOnlyManifestCollectionsCannotBeUsedToChangeAParsedScene()
    {
        var manifest = WorldSceneManifest.Parse(ReadFixture());
        var hash = manifest.CanonicalGeometryHash;
        var first = manifest.Objects[0];
        Assert.Throws<NotSupportedException>(() => ((IList<WorldBoxObject>)manifest.Objects).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<WorldCellDefinition>)manifest.Cells).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<string>)first.Tags)[0] = "caller_mutation");
        Assert.Throws<NotSupportedException>(() => ((IList<string>)first.SurfaceIds).Clear());
        Assert.Equal(hash, manifest.CanonicalGeometryHash);
        Assert.NotEmpty(first.Tags);
        Assert.NotEmpty(first.SurfaceIds);
    }

    [Theory]
    [InlineData("missing_objects", "$.objects")]
    [InlineData("future_version", "$.schema_version")]
    [InlineData("duplicate_object", "Duplicate object ID")]
    [InlineData("duplicate_cell", "Duplicate cell ID")]
    [InlineData("unknown_material", ".material")]
    [InlineData("zero_size", ".size_enu")]
    [InlineData("outside_bounds", ".position_enu")]
    [InlineData("empty_tags", ".tags")]
    [InlineData("duplicate_tags", ".tags")]
    [InlineData("wrong_origin", "$.origin_enu")]
    [InlineData("unknown_field", ".unexpected")]
    [InlineData("dynamic_object", ".dynamic")]
    public void InvalidSceneDefinitionsAreRejectedAtTheirField(string change, string expectedDiagnostic)
    {
        var node = FixtureNode();
        var first = node["objects"]![0]!;
        switch (change)
        {
            case "missing_objects": node.Remove("objects"); break;
            case "future_version": node["schema_version"] = 2; break;
            case "duplicate_object": node["objects"]!.AsArray().Add(first.DeepClone()); break;
            case "duplicate_cell": node["cells"]!.AsArray().Add(node["cells"]![0]!.DeepClone()); break;
            case "unknown_material": first["material"] = "unknown_material"; break;
            case "zero_size": first["size_enu"]![0] = 0; break;
            case "outside_bounds": first["position_enu"]![0] = 500241; break;
            case "empty_tags": first["tags"] = new JsonArray(); break;
            case "duplicate_tags": first["tags"]!.AsArray().Add(first["tags"]![0]!.GetValue<string>()); break;
            case "wrong_origin": node["origin_enu"]![0] = 500001; break;
            case "unknown_field": first["unexpected"] = true; break;
            case "dynamic_object": first["dynamic"] = true; break;
        }
        Assert.Contains(expectedDiagnostic, Assert.Throws<FormatException>(() =>
            WorldSceneManifest.Parse(node.ToJsonString())).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OverflowingCoordinateNumberAndMalformedJsonAreRejected()
    {
        var node = FixtureNode();
        node["objects"]![0]!["position_enu"]![0] = JsonNode.Parse("1e999");
        Assert.Contains("finite", Assert.Throws<FormatException>(() =>
            WorldSceneManifest.Parse(node.ToJsonString())).Message, StringComparison.Ordinal);
        Assert.ThrowsAny<JsonException>(() => WorldSceneManifest.Parse("{"));
    }

    [Fact]
    public void DuplicateJsonPropertyCannotSilentlyReplaceMapVersion()
    {
        var json = FixtureNode().ToJsonString();
        json = json.Insert(1, "\"map_version\":999,");
        Assert.Contains("Duplicate property", Assert.Throws<FormatException>(() =>
            WorldSceneManifest.Parse(json)).Message, StringComparison.Ordinal);
    }
}
