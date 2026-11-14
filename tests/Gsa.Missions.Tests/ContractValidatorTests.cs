using System.Text.Json;
using System.Text.Json.Nodes;
using Gsa.Missions.Content;
using Xunit;

namespace Gsa.Missions.Tests;

public class ContractValidatorTests
{
    private static JsonObject Load(string fixture = "facades.json") =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture)))!.AsObject();

    private static IReadOnlyList<ContentDiagnostic> Validate(JsonObject contract)
    {
        using var document = JsonDocument.Parse(contract.ToJsonString());
        return ContractValidator.Validate(document.RootElement, "fixture.json");
    }

    private static void HasError(JsonObject contract, string field) =>
        Assert.Contains(Validate(contract), error => error.Path == "fixture.json" && error.Field == field);

    [Theory]
    [InlineData("facades.json")]
    [InlineData("inventory.json")]
    [InlineData("point-cloud.json")]
    [InlineData("pavement.json")]
    [InlineData("normative-facades.json")]
    public void FourProductsAndNormativeExampleAreAccepted(string fixture) => Assert.Empty(Validate(Load(fixture)));

    [Fact]
    public void EmptyObjectReportsConcreteMissingFields()
    {
        var errors = Validate(new JsonObject());
        Assert.Contains(errors, error => error.Field == "$.schema_version");
        Assert.Contains(errors, error => error.Field == "$.product");
        Assert.Contains(errors, error => error.Field == "$.deadline");
        Assert.Contains(errors, error => error.Field == "$.partial_policy");
    }

    [Fact]
    public void NonObjectDocumentIsRejectedWithoutThrowing()
    {
        using var document = JsonDocument.Parse("[]");
        Assert.Equal("$", Assert.Single(ContractValidator.Validate(document.RootElement, "array.json")).Field);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("title_key")]
    [InlineData("client_id")]
    [InlineData("region_id")]
    [InlineData("area_id")]
    [InlineData("required_feature_ids")]
    [InlineData("requirements")]
    [InlineData("required_capabilities")]
    [InlineData("risk")]
    [InlineData("calm")]
    [InlineData("reward")]
    [InlineData("story_prerequisites")]
    [InlineData("delivery_format")]
    public void RequiredContractFieldCannotBeOmitted(string property)
    {
        var contract = Load();
        contract.Remove(property);
        HasError(contract, "$." + property);
    }

    [Theory]
    [InlineData("product", "meshes")]
    [InlineData("risk", "extreme")]
    [InlineData("delivery_format", "las")]
    public void UnknownEnumValuesAreRejected(string property, string value)
    {
        var contract = Load();
        contract[property] = value;
        HasError(contract, "$." + property);
    }

    [Fact]
    public void FutureSchemaAndUnknownFieldsAreRejected()
    {
        var contract = Load();
        contract["schema_version"] = 2;
        contract["skip_quality_check"] = true;
        HasError(contract, "$.schema_version");
        HasError(contract, "$.skip_quality_check");
    }

    [Theory]
    [InlineData("unit")]
    [InlineData("excellent")]
    [InlineData("limit")]
    [InlineData("direction")]
    [InlineData("surface_id")]
    [InlineData("cell_size_m")]
    public void QuantitativeRequirementsNeedUnitsLimitsAndSpatialSupport(string property)
    {
        var contract = Load();
        contract["requirements"]![0]!.AsObject().Remove(property);
        HasError(contract, "$.requirements[0]." + property);
    }

    [Theory]
    [InlineData(0.89)]
    [InlineData(0.90)]
    [InlineData(1.01)]
    public void MinimumExcellenceMustBeBetterAndWithinRatioBounds(double excellent)
    {
        var contract = Load();
        contract["requirements"]![0]!["excellent"] = excellent;
        HasError(contract, "$.requirements[0].excellent");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(-1)]
    public void MaximumExcellenceMustBeBetterAndNonNegative(double excellent)
    {
        var contract = Load("inventory.json");
        contract["requirements"]![1]!["excellent"] = excellent;
        HasError(contract, "$.requirements[1].excellent");
    }

    [Fact]
    public void OverflowingJsonNumberIsRejectedAsNonFinite()
    {
        var contract = Load();
        contract["requirements"]![0]!["limit"] = JsonNode.Parse("1e999");
        HasError(contract, "$.requirements[0].limit");
    }

    [Fact]
    public void WrongUnitDirectionAndKindCannotChangeMetricSemantics()
    {
        var contract = Load();
        contract["requirements"]![0]!["unit"] = "m";
        contract["requirements"]![0]!["direction"] = "max";
        contract["requirements"]![1]!["kind"] = "threshold";
        HasError(contract, "$.requirements[0].unit");
        HasError(contract, "$.requirements[0].direction");
        HasError(contract, "$.requirements[1].kind");
    }

    [Fact]
    public void ImageMetricCannotBeAppliedToPointCloud()
    {
        var contract = Load("point-cloud.json");
        contract["requirements"]![1] = JsonNode.Parse("""
            {"kind":"threshold","metric":"valid_image_fraction","unit":"ratio","direction":"min","limit":0.8,"excellent":0.98}
            """);
        HasError(contract, "$.requirements[1].metric");
    }

    [Fact]
    public void UnknownAndRepeatedMetricsAreRejected()
    {
        var contract = Load();
        contract["requirements"]!.AsArray().Add(contract["requirements"]![0]!.DeepClone());
        contract["requirements"]![2]!["metric"] = "free_quality_bonus";
        HasError(contract, "$.requirements[2].metric");
        HasError(contract, "$.requirements[5].metric");
    }

    [Fact]
    public void OneHundredPercentCompletenessRequiresPredicate()
    {
        var contract = Load("inventory.json");
        contract["requirements"]![0]!["limit"] = 1;
        contract["requirements"]![0]!["excellent"] = 1;
        HasError(contract, "$.requirements[0].excellent");
    }

    [Fact]
    public void PrivacyAndFormatRemainImplicitGlobalInvariants()
    {
        var contract = Load();
        contract["requirements"]!.AsArray().RemoveAt(4);
        contract["requirements"]!.AsArray().RemoveAt(3);
        Assert.Empty(Validate(contract));
        contract["requirements"]!.AsArray().Add(JsonNode.Parse("""
            {"kind":"predicate","metric":"privacy_redacted","expected":false}
            """));
        HasError(contract, "$.requirements[3].expected");
    }

    [Fact]
    public void ProductRequiresActualAcquisitionCapabilities()
    {
        var contract = Load("pavement.json");
        contract["required_capabilities"] = new JsonArray("gnss_basic", "lidar");
        HasError(contract, "$.required_capabilities");
    }

    [Theory]
    [InlineData("due_after_s", 0)]
    [InlineData("expires_after_s", 21600)]
    [InlineData("expires_after_s", 100)]
    [InlineData("late_penalty_per_hour", -0.1)]
    [InlineData("late_penalty_cap", 1.1)]
    public void DeadlinesAndPenaltyRangesAreCoherent(string property, double value)
    {
        var contract = Load();
        contract["deadline"]![property] = value;
        HasError(contract, "$.deadline." + property);
    }

    [Theory]
    [InlineData("base_coords", -1)]
    [InlineData("base_coords", 10.5)]
    [InlineData("base_xp", -1)]
    [InlineData("reputation", -1)]
    public void RewardsMustBeNonNegativeIntegers(string property, double value)
    {
        var contract = Load();
        contract["reward"]![property] = value;
        HasError(contract, "$.reward." + property);
    }

    [Theory]
    [InlineData("1.0", "400.0")]
    [InlineData("1e0", "4e2")]
    [InlineData("1.00", "9223372036854775807.0")]
    public void MathematicallyIntegralJsonNumbersAreAcceptedRegardlessOfRepresentation(string version, string reward)
    {
        var contract = Load();
        contract["schema_version"] = JsonNode.Parse(version);
        contract["reward"]!["base_coords"] = JsonNode.Parse(reward);
        Assert.Empty(Validate(contract));
    }

    [Theory]
    [InlineData("9223372036854775808")]
    [InlineData("9223372036854775808.0")]
    [InlineData("1e100")]
    [InlineData("400.25")]
    [InlineData("400.000000000000000000000000000000001")]
    [InlineData("1e-29")]
    public void IntegerFieldsRejectFractionsAndValuesOutsideInt64(string reward)
    {
        var contract = Load();
        contract["reward"]!["base_coords"] = JsonNode.Parse(reward);
        HasError(contract, "$.reward.base_coords");
    }

    [Theory]
    [InlineData("2147483648.0")]
    [InlineData("1.5")]
    public void SchemaVersionRejectsFractionsAndValuesOutsideInt32(string version)
    {
        var contract = Load();
        contract["schema_version"] = JsonNode.Parse(version);
        HasError(contract, "$.schema_version");
    }

    [Theory]
    [InlineData("0e9999999999999999999999999999999", 0L)]
    [InlineData("-0.000e-9999999999999999999999999999999", 0L)]
    [InlineData("-9223372036854775808.0", long.MinValue)]
    [InlineData("9223372036854775807.0", long.MaxValue)]
    [InlineData("0.0001e4", 1L)]
    [InlineData("4.00e+2", 400L)]
    public void ExactIntegerReaderSupportsZeroLargeExponentsAndSignedBoundaries(string number, long expected)
    {
        using var document = JsonDocument.Parse(number);
        Assert.True(JsonNumbers.TryGetInt64(document.RootElement, out var actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("1e9999999999999999999999999999999")]
    [InlineData("1e-9999999999999999999999999999999")]
    [InlineData("1e2147483647")]
    [InlineData("-9223372036854775809.0")]
    [InlineData("0.000000000000000000000000000000000001")]
    public void ExactIntegerReaderRejectsNonZeroFractionsOrUnrepresentableMagnitudes(string number)
    {
        using var document = JsonDocument.Parse(number);
        Assert.False(JsonNumbers.TryGetInt64(document.RootElement, out _));
    }

    [Fact]
    public void RequiredIdListsCannotContainDuplicates()
    {
        var contract = Load();
        contract["required_feature_ids"]!.AsArray().Add("facade_01");
        HasError(contract, "$.required_feature_ids[20]");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.5)]
    [InlineData(0.4)]
    public void PartialWeightsMustBePositiveAndSumToOne(double firstWeight)
    {
        var contract = Load("inventory.json");
        contract["partial_policy"]!["deliverables"]![0]!["weight"] = firstWeight;
        Assert.Contains(Validate(contract), error => error.Field.StartsWith("$.partial_policy.deliverables", StringComparison.Ordinal));
    }

    [Fact]
    public void PartialWeightToleranceAcceptsFloatingPointRepresentation()
    {
        var contract = Load("inventory.json");
        contract["partial_policy"]!["deliverables"]![0]!["weight"] = 0.5000000002;
        Assert.Empty(Validate(contract));
    }

    [Fact]
    public void DeliverableIdsAndFeaturesCannotBeChargedTwice()
    {
        var contract = Load("inventory.json");
        var second = contract["partial_policy"]!["deliverables"]![1]!;
        second["id"] = "block_a";
        second["required_feature_ids"]!.AsArray().Add("facade_01");
        HasError(contract, "$.partial_policy.deliverables[1].id");
        HasError(contract, "$.partial_policy.deliverables[1].required_feature_ids");
    }

    [Fact]
    public void PartialCannotChargeFeatureOutsideGlobalContract()
    {
        var contract = Load("inventory.json");
        contract["partial_policy"]!["deliverables"]![1]!["required_feature_ids"]!.AsArray().Add("feature_outside_contract");
        HasError(contract, "$.partial_policy.deliverables[1].required_feature_ids");
    }

    [Fact]
    public void PartialCannotDisablePrivacy()
    {
        var contract = Load("inventory.json");
        contract["partial_policy"]!["deliverables"]![0]!["requirements"]!.AsArray().Add(JsonNode.Parse("""
            {"kind":"predicate","metric":"privacy_redacted","expected":false}
            """));
        HasError(contract, "$.partial_policy.deliverables[0].requirements[2].expected");
    }

    [Fact]
    public void DisabledPartialCannotContainHiddenDeliverables()
    {
        var contract = Load();
        contract["partial_policy"]!["deliverables"] = new JsonArray();
        HasError(contract, "$.partial_policy.deliverables");
    }

    [Fact]
    public void DuplicateJsonPropertiesAreRejectedInsteadOfUsingLastValue()
    {
        var json = Load().ToJsonString().Replace("\"product\":\"images\"", "\"product\":\"point_cloud\",\"product\":\"images\"", StringComparison.Ordinal);
        using var document = JsonDocument.Parse(json);
        Assert.Contains(ContractValidator.Validate(document.RootElement, "duplicate.json"), error => error.Field == "$.product" && error.Reason.Contains("Duplicate", StringComparison.Ordinal));
    }

    [Fact]
    public void DiagnosticsSerializeWithInvariantSnakeCaseFieldNames()
    {
        var serialized = JsonSerializer.Serialize(new ContentDiagnostic("content/example.json", "$.requirements[0].unit", "Missing unit."));
        Assert.Contains("\"path\":", serialized);
        Assert.Contains("\"field\":", serialized);
        Assert.Contains("\"reason\":", serialized);
        Assert.DoesNotContain("\"Path\":", serialized);
    }
}
