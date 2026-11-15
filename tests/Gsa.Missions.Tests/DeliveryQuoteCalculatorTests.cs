using System.Text.Json;
using Gsa.Missions.Contracts;
using Gsa.Missions.Quality;
using Xunit;

namespace Gsa.Missions.Tests;

public sealed class DeliveryQuoteCalculatorTests
{
    private static ContractDefinition LoadContract(string fixture) =>
        ContractDefinition.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture)), fixture);

    private static QualityReport BuildReport(
        ContractDefinition contract,
        bool passed,
        IReadOnlyList<MetricEvaluationResult> metrics,
        IReadOnlyList<string>? failureKeys = null,
        IReadOnlyList<string>? passedDeliverables = null) =>
        new(
            contract.Id,
            ["b1"],
            passed,
            metrics,
            failureKeys ?? [],
            [],
            passedDeliverables ?? [],
            uniqueSampleCount: 10,
            validSampleCount: 10,
            matchedFeatureCount: contract.RequiredFeatureIds.Count,
            matchedCheckpointCount: 3);

    [Fact]
    public void BinaryCompletenessDoesNotEnterMarginAverageAndFiveStarsRequiresFullMargins()
    {
        var contract = LoadContract("facades.json");
        // Thresholds in facades.json: feature_completeness (1.0/1.0 binary), area_coverage (0.9->0.98), valid_image_fraction (0.8->0.98).
        var metrics = new[]
        {
            new MetricEvaluationResult("feature_completeness", "threshold", MetricEvaluationStatus.Passed, 1.0, null, 1.0, 1.0, IsBinaryRequirement: true, 20, null),
            new MetricEvaluationResult("area_coverage", "threshold", MetricEvaluationStatus.Passed, 0.94, null, 0.90, 0.98, IsBinaryRequirement: false, 10, null), // m = 0.5
            new MetricEvaluationResult("valid_image_fraction", "threshold", MetricEvaluationStatus.Passed, 0.89, null, 0.80, 0.98, IsBinaryRequirement: false, 10, null), // m = 0.5
            new MetricEvaluationResult("privacy_redacted", "predicate", MetricEvaluationStatus.Passed, 1.0, true, null, null, true, 10, null),
            new MetricEvaluationResult("format_valid", "predicate", MetricEvaluationStatus.Passed, 1.0, true, null, null, true, 10, null),
        };

        // q should be (0.5 + 0.5) / 2 = 0.5 (not diluted/inflated by binary 1.0 completeness).
        var quote = DeliveryQuoteCalculator.Calculate(contract, BuildReport(contract, true, metrics), acceptedAtSeconds: 0.0, deliveredAtSeconds: 1000.0);
        Assert.True(quote.Eligible);
        Assert.False(quote.IsPartial);
        Assert.Equal(0.5, quote.QualityScore, 9);
        Assert.Equal(3, quote.Stars);
        // base*(0.8 + 0.2*0.5) = base * 0.9, single floor at end.
        var expectedCoords = (int)Math.Floor(contract.Reward.BaseCoords * 0.9);
        Assert.Equal(expectedCoords, quote.NetCoords);
        Assert.Equal(contract.Reward.Reputation, quote.ReputationDelta);

        var maxMetrics = new[]
        {
            metrics[0],
            metrics[1] with { Value = 0.99 },
            metrics[2] with { Value = 0.99 },
            metrics[3],
            metrics[4],
        };
        var fiveStar = DeliveryQuoteCalculator.Calculate(contract, BuildReport(contract, true, maxMetrics), 0.0, 1000.0);
        Assert.Equal(5, fiveStar.Stars);
        Assert.Equal(1.0, fiveStar.QualityScore, 9);
        Assert.Equal(contract.Reward.BaseCoords, fiveStar.NetCoords);
        Assert.Equal(contract.Reward.Reputation + 1, fiveStar.ReputationDelta);
    }

    [Fact]
    public void LatePenaltyAppliesAfterDueDeadlineRespectsCapAndAppliesSingleFloor()
    {
        var contract = LoadContract("facades.json");
        var metrics = new[]
        {
            new MetricEvaluationResult("area_coverage", "threshold", MetricEvaluationStatus.Passed, 0.98, null, 0.90, 0.98, false, 10, null),
            new MetricEvaluationResult("valid_image_fraction", "threshold", MetricEvaluationStatus.Passed, 0.98, null, 0.80, 0.98, false, 10, null),
            new MetricEvaluationResult("privacy_redacted", "predicate", MetricEvaluationStatus.Passed, 1.0, true, null, null, true, 10, null),
            new MetricEvaluationResult("format_valid", "predicate", MetricEvaluationStatus.Passed, 1.0, true, null, null, true, 10, null),
        };

        // 1.5 hours late at 0.05/h -> penalty 0.075.
        var deliveredLate = contract.Deadline.DueAfterSeconds + 5400.0;
        var lateQuote = DeliveryQuoteCalculator.Calculate(contract, BuildReport(contract, true, metrics), 0.0, deliveredLate);
        Assert.True(lateQuote.Eligible);
        Assert.Equal(1.5, lateQuote.LateHours, 9);
        Assert.Equal(0.075, lateQuote.LatePenaltyFraction, 9);
        Assert.Equal((int)Math.Floor(contract.Reward.BaseCoords * 1.0 * (1.0 - 0.075)), lateQuote.NetCoords);

        // Beyond ExpiresAfterSeconds -> rejected expired with -1 reputation.
        var expiredQuote = DeliveryQuoteCalculator.Calculate(contract, BuildReport(contract, true, metrics), 0.0, contract.Deadline.ExpiresAfterSeconds + 1.0);
        Assert.False(expiredQuote.Eligible);
        Assert.True(expiredQuote.IsExpired);
        Assert.Equal(0, expiredQuote.NetCoords);
        Assert.Equal(-1, expiredQuote.ReputationDelta);
    }

    [Fact]
    public void PrivacyFailureBlocksPartialDeliveryEvenWhenDeliverableThresholdPassed()
    {
        var contract = LoadContract("inventory.json");
        var metricsWithUnredactedPrivacy = new[]
        {
            new MetricEvaluationResult("feature_completeness", "threshold", MetricEvaluationStatus.Failed, 0.5, null, 0.9, 1.0, false, 20, "cq.failure.feature_completeness"),
            new MetricEvaluationResult("absolute_rmse", "threshold", MetricEvaluationStatus.Passed, 0.5, null, 2.0, 0.5, false, 3, null),
            new MetricEvaluationResult("valid_image_fraction", "threshold", MetricEvaluationStatus.Passed, 1.0, null, 0.8, 0.98, false, 10, null),
            new MetricEvaluationResult("privacy_redacted", "predicate", MetricEvaluationStatus.Failed, 0.0, false, null, null, true, 10, "cq.failure.privacy_redacted"),
            new MetricEvaluationResult("format_valid", "predicate", MetricEvaluationStatus.Passed, 1.0, true, null, null, true, 10, null),
        };

        var blocked = DeliveryQuoteCalculator.Calculate(
            contract,
            BuildReport(contract, false, metricsWithUnredactedPrivacy, ["cq.failure.privacy_redacted"], ["block_a"]),
            0.0,
            100.0,
            DeliveryMode.Partial);

        Assert.False(blocked.Eligible);
        Assert.Equal(0, blocked.NetCoords);
        Assert.Equal("quote.rejected.compliance", blocked.ReasonKey);

        // Once privacy is clean, block_a partial pays half base scaled by quality multiplier, max 2 stars, no story unlock.
        var cleanMetrics = metricsWithUnredactedPrivacy.ToArray();
        cleanMetrics[3] = new MetricEvaluationResult("privacy_redacted", "predicate", MetricEvaluationStatus.Passed, 1.0, true, null, null, true, 10, null);
        var partialOk = DeliveryQuoteCalculator.Calculate(
            contract,
            BuildReport(contract, false, cleanMetrics, ["cq.failure.feature_completeness"], ["block_a"]),
            0.0,
            100.0,
            DeliveryMode.Partial);

        Assert.True(partialOk.Eligible);
        Assert.True(partialOk.IsPartial);
        Assert.InRange(partialOk.Stars, 1, 2);
        Assert.Equal(0.5, partialOk.PartialWeight, 9);
        Assert.Empty(partialOk.StoryUnlocks);
        Assert.Equal(1, partialOk.ReputationDelta);

        using var json = JsonDocument.Parse(partialOk.ToJson());
        Assert.Equal(1, json.RootElement.GetProperty("schema_version").GetInt32());
        Assert.True(json.RootElement.GetProperty("is_partial").GetBoolean());
    }
}

