using System.Text.Json;
using System.Text.Json.Nodes;
using Gsa.Tools;
using Xunit;

namespace Gsa.Tools.Tests;

public sealed class ContentValidationTests
{
    [Fact]
    public void ReferenceCatalogAndFourProductsAreValid()
    {
        using var fixture = new ContentFixture();
        var result = ContentDirectoryValidator.Validate(fixture.Directory);
        Assert.Empty(result);
        using var writer = new StringWriter();
        Assert.Equal(0, ToolApplication.Run(["validate-content", fixture.Directory], writer));
        using var output = JsonDocument.Parse(writer.ToString());
        Assert.True(output.RootElement.GetProperty("valid").GetBoolean());
        Assert.Equal(0, output.RootElement.GetProperty("diagnostics").GetArrayLength());
    }

    [Fact]
    public void MathematicallyIntegralNumberTokensMatchTheIntegerSchema()
    {
        using var fixture = new ContentFixture();
        fixture.Edit("catalog.json", json =>
        {
            json["schema_version"] = JsonNode.Parse("1.0");
            json["mounting_slots"]![0]!["capacity"] = JsonNode.Parse("1.0");
        });
        fixture.Edit("profiles/camera-initial.json", json =>
        {
            json["grade"] = JsonNode.Parse("0.0");
            json["storage_bytes_per_sample"] = JsonNode.Parse("1048576.0");
            json["buffer_capacity_bytes"] = JsonNode.Parse("9223372036854775807.0");
            json["mounting_requirements"]!["count"] = JsonNode.Parse("1e0");
        });
        fixture.Edit("missions/HIST-01.json", json => json["act"] = JsonNode.Parse("1.0"));
        Assert.Empty(ContentDirectoryValidator.Validate(fixture.Directory));
    }

    [Theory]
    [InlineData("0.5")]
    [InlineData("1e-29")]
    [InlineData("1.00000000000000000000000000001")]
    public void FractionalTokensAreNeverRoundedIntoIntegers(string token)
    {
        using var fixture = new ContentFixture();
        fixture.Edit("profiles/camera-initial.json", json => json["grade"] = JsonNode.Parse(token));
        Assert.Contains(ContentDirectoryValidator.Validate(fixture.Directory), d => d.Field == "$.grade");
    }

    [Theory]
    [InlineData("9223372036854775808.0")]
    [InlineData("9.223372036854775808e18")]
    [InlineData("1e100")]
    public void MathematicallyIntegralOverflowIsStillRejected(string token)
    {
        using var fixture = new ContentFixture();
        fixture.Edit("profiles/camera-initial.json", json => json["buffer_capacity_bytes"] = JsonNode.Parse(token));
        Assert.Contains(ContentDirectoryValidator.Validate(fixture.Directory), d => d.Field == "$.buffer_capacity_bytes");
    }

    [Fact]
    public void MissingFeatureReturnsExitTwoAndExactField()
    {
        using var fixture = new ContentFixture();
        fixture.Edit("contracts/facades.json", json => json["required_feature_ids"]![0] = "missing_feature");
        using var writer = new StringWriter();
        Assert.Equal(2, ToolApplication.Run(["validate-content", fixture.Directory], writer));
        using var output = JsonDocument.Parse(writer.ToString());
        Assert.False(output.RootElement.GetProperty("valid").GetBoolean());
        Assert.Contains(output.RootElement.GetProperty("diagnostics").EnumerateArray(),
            d => d.GetProperty("path").GetString()!.EndsWith("facades.json", StringComparison.Ordinal) &&
                 d.GetProperty("field").GetString() == "$.required_feature_ids[0]" &&
                 d.GetProperty("reason").GetString()!.Contains("missing_feature", StringComparison.Ordinal));
    }

    [Fact]
    public void InvalidThresholdIsReportedByMissions()
    {
        using var fixture = new ContentFixture();
        fixture.Edit("contracts/facades.json", json => json["requirements"]![0]!["excellent"] = 0.5);
        Assert.Contains(ContentDirectoryValidator.Validate(fixture.Directory),
            d => d.Field == "$.requirements[0].excellent");
    }

    [Fact]
    public void NaNTokenAndMalformedJsonAreRejected()
    {
        using var fixture = new ContentFixture();
        fixture.Write("profiles/camera-initial.json", """{"schema_version":1,"id":"camera","rate_hz":NaN}""");
        Assert.Contains(ContentDirectoryValidator.Validate(fixture.Directory), d => d.Reason.Contains("Invalid JSON", StringComparison.Ordinal));
    }

    [Fact]
    public void FiniteJsonSyntaxWithOverflowingNumberIsRejected()
    {
        using var fixture = new ContentFixture();
        var file = fixture.Path("regions/old-center.json");
        File.WriteAllText(file, File.ReadAllText(file).Replace("500000", "1e999", StringComparison.Ordinal));
        Assert.Contains(ContentDirectoryValidator.Validate(fixture.Directory), d => d.Reason.Contains("finite", StringComparison.Ordinal));
    }

    [Fact]
    public void CollinearPolygonIsRejected()
    {
        using var fixture = new ContentFixture();
        fixture.Edit("regions/old-center.json", json => json["areas"]![0]!["polygon_enu"] =
            JsonNode.Parse("[[500001,7400001],[500002,7400002],[500003,7400003]]"));
        Assert.Contains(ContentDirectoryValidator.Validate(fixture.Directory),
            d => d.Field == "$.areas[0].polygon_enu" && d.Reason.Contains("area", StringComparison.Ordinal));
    }

    [Fact]
    public void CrossingPolygonWithNonzeroSignedAreaIsRejected()
    {
        using var fixture = new ContentFixture();
        fixture.Edit("regions/old-center.json", json => json["areas"]![0]!["polygon_enu"] =
            JsonNode.Parse("[[500001,7400001],[500004,7400004],[500001,7400004],[500003,7400001]]"));
        Assert.Contains(ContentDirectoryValidator.Validate(fixture.Directory),
            d => d.Field == "$.areas[0].polygon_enu" && d.Reason.Contains("intersecting", StringComparison.Ordinal));
    }

    [Fact]
    public void FeatureOutsideBoundsIsRejected()
    {
        using var fixture = new ContentFixture();
        fixture.Edit("regions/old-center.json", json => json["features"]![0]!["position_enu"]![2] = 1000);
        Assert.Contains(ContentDirectoryValidator.Validate(fixture.Directory),
            d => d.Field == "$.features[0].position_enu" && d.Reason.Contains("outside", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("buffer_capacity_bytes", 1, "$.buffer_capacity_bytes")]
    [InlineData("mount_count", 2, "$.mounting_requirements.count")]
    public void ProfileCannotExceedStorageOrMountingCapacity(string field, int value, string expected)
    {
        using var fixture = new ContentFixture();
        fixture.Edit("profiles/camera-initial.json", json =>
        {
            if (field == "mount_count") json["mounting_requirements"]!["count"] = value;
            else json[field] = value;
        });
        Assert.Contains(ContentDirectoryValidator.Validate(fixture.Directory), d => d.Field == expected);
    }

    [Fact]
    public void DeclaredCapabilityWithoutProfileProviderIsRejected()
    {
        using var fixture = new ContentFixture();
        fixture.Edit("catalog.json", json => ((JsonArray)json["capabilities"]!).Add("thermal_camera"));
        fixture.Edit("contracts/facades.json", json => ((JsonArray)json["required_capabilities"]!).Add("thermal_camera"));
        Assert.Contains(ContentDirectoryValidator.Validate(fixture.Directory),
            d => d.Field == "$.required_capabilities[2]" && d.Reason.Contains("no valid profile provider", StringComparison.Ordinal));
    }

    [Fact]
    public void SensorTypeCannotForgeAnotherKnownCapability()
    {
        using var fixture = new ContentFixture();
        fixture.Edit("profiles/gnss-basic.json", json => ((JsonArray)json["capabilities"]!).Add("camera"));
        Assert.Contains(ContentDirectoryValidator.Validate(fixture.Directory),
            d => d.Field == "$.capabilities" && d.Reason.Contains("requires sensor type 'camera'", StringComparison.Ordinal));
    }

    [Fact]
    public void StoryMissionCannotPointToPartialContract()
    {
        using var fixture = new ContentFixture();
        fixture.Edit("missions/HIST-02.json", json => json["objectives"]![0]!["contract_id"] = "contract_inventory_01");
        Assert.Contains(ContentDirectoryValidator.Validate(fixture.Directory),
            d => d.Field == "$.objectives[0].contract_id" && d.Reason.Contains("partial delivery", StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicateAreasAcrossRegionsAreRejected()
    {
        using var fixture = new ContentFixture();
        var node = JsonNode.Parse(File.ReadAllText(fixture.Path("regions/old-center.json")))!;
        node["id"] = "other_region";
        fixture.Write("regions/duplicate.json", node.ToJsonString());
        Assert.Contains(ContentDirectoryValidator.Validate(fixture.Directory),
            d => d.Field == "$.areas[0].id" && d.Reason.Contains("Duplicate", StringComparison.Ordinal));
    }

    [Fact]
    public void MissionPreconditionCyclesAreRejected()
    {
        using var fixture = new ContentFixture();
        fixture.Edit("missions/HIST-01.json", json => ((JsonArray)json["preconditions"]!).Add("HIST-02"));
        Assert.Contains(ContentDirectoryValidator.Validate(fixture.Directory),
            d => d.Field == "$.preconditions" && d.Reason.Contains("cycle", StringComparison.Ordinal));
    }

    [Fact]
    public void FutureSchemaVersionAndDuplicateJsonPropertyAreRejected()
    {
        using var fixture = new ContentFixture();
        fixture.Write("catalog.json", File.ReadAllText(fixture.Path("catalog.json")).Replace("\"schema_version\": 1", "\"schema_version\": 1, \"schema_version\": 2", StringComparison.Ordinal));
        var result = ContentDirectoryValidator.Validate(fixture.Directory);
        Assert.Contains(result, d => d.Field == "$.schema_version" && d.Reason.Contains("Duplicate", StringComparison.Ordinal));
        Assert.Contains(result, d => d.Field == "$.schema_version" && d.Reason.Contains("Only", StringComparison.Ordinal));
    }

    [Fact]
    public void UnknownCommandsAreUsageErrorsRatherThanImplementedPlaceholders()
    {
        using var writer = new StringWriter();
        Assert.Equal(1, ToolApplication.Run(["replay-core", "missing.json"], writer));
        using var output = JsonDocument.Parse(writer.ToString());
        Assert.Equal("$command", output.RootElement.GetProperty("diagnostics")[0].GetProperty("field").GetString());
    }

    [Fact]
    public void MissingDirectoryIsInvalidContentNotUsage()
    {
        using var fixture = new ContentFixture();
        using var writer = new StringWriter();
        Assert.Equal(2, ToolApplication.Run(["validate-content", fixture.Path("missing")], writer));
    }

    private sealed class ContentFixture : IDisposable
    {
        public string Directory { get; }

        public ContentFixture()
        {
            var repository = new DirectoryInfo(AppContext.BaseDirectory);
            while (repository is not null && !System.IO.Directory.Exists(System.IO.Path.Combine(repository.FullName, "specs")))
                repository = repository.Parent;
            if (repository is null) throw new InvalidOperationException("Cannot find repository fixtures.");
            Directory = System.IO.Path.Combine(repository.FullName, "work", "tools-tests", Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Directory);
            File.Copy(System.IO.Path.Combine(repository.FullName, "content/catalog.json"), Path("catalog.json"));
            foreach (var name in new[] { "contracts", "profiles", "regions", "missions" })
            {
                System.IO.Directory.CreateDirectory(Path(name));
                foreach (var file in System.IO.Directory.GetFiles(System.IO.Path.Combine(repository.FullName, "content", name), "*.json"))
                    File.Copy(file, Path(name + "/" + System.IO.Path.GetFileName(file)));
            }
        }

        public string Path(string relative) => System.IO.Path.Combine(Directory, relative);
        public void Write(string relative, string content) => File.WriteAllText(Path(relative), content);
        public void Edit(string relative, Action<JsonNode> change)
        {
            var node = JsonNode.Parse(File.ReadAllText(Path(relative)))!;
            change(node);
            Write(relative, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        public void Dispose() => System.IO.Directory.Delete(Directory, true);
    }
}
