using System.Globalization;
using Gsa.Missions.Gameplay;
using Gsa.Core.Mobility;
using Godot;
using Gsa.Missions.Campaign;
using Gsa.Missions.Contracts;
using Gsa.Missions.Office;
using Gsa.Missions.Persistence;
using Gsa.Missions.Quality;

namespace Gsa.Engine.World;

public partial class SaoDatumCityScene
{
    private static readonly IReadOnlyList<ContractRequirementSpec> CityPhotoRequirements = Array.AsReadOnly<ContractRequirementSpec>(
    [
        new PredicateRequirementSpec("all_required_features", true),
        new ThresholdRequirementSpec("valid_image_fraction", "ratio", "min", .8, .98),
        new PredicateRequirementSpec("privacy_redacted", true),
        new PredicateRequirementSpec("format_valid", true),
    ]);

    private const double DrainageRainDelaySeconds = 3 * 3600;
    private QualityReport? _lastCitySurveyReview;
    private IReadOnlyList<SidewalkSectionMeasurement> _lastSidewalkMeasurements = [];
    private int _lastCitySurveyTargetCount;

    private static IReadOnlyList<ContractRequirementSpec> CitySurveyRequirements(string missionId) => missionId switch
    {
        "HIST-02" => [.. CityPhotoRequirements, new ThresholdRequirementSpec("absolute_rmse", "m", "max", 2, .5)],
        "HIST-03" => [.. CityPhotoRequirements, new ThresholdRequirementSpec("relative_rmse", "m", "max", .2, .05)],
        _ => CityPhotoRequirements,
    };

    private static bool SupportsCityStory(CampaignMissionFullSpec spec) => spec.MissionId is "HIST-01" or "HIST-02" or "HIST-03";

    private bool SupportsCityDelivery(ContractDefinition definition)
    {
        if (definition.RequiredFeatureIds.Count == 0 || definition.PartialPolicy.Mode != "disabled")
            return false;

        var inventory = definition.Product == "feature_inventory" && definition.Id is "HIST-02" or "HIST-03";
        if (definition.Product != "images" && !inventory)
            return false;
        if (inventory && !CitySurveyInventory.HasReference(definition, _manifestObjectsById))
            return false;

        var supported = inventory ? CitySurveyRequirements(definition.Id) : CityPhotoRequirements;
        return definition.Requirements.All(requirement =>
            supported.Any(metric => metric.Metric == requirement.Metric && metric.Kind == requirement.Kind));
    }

    private bool CityMissionCompleted(string missionId) =>
        _campaignState.ObjectiveStates.TryGetValue(missionId, out var state) && state == $"completed:{missionId}";

    private bool CityHasSurveyPlatform(CampaignMissionFullSpec spec)
    {
        var mode = _locomotion.MountedVehicleMode;
        return _fleet.OwnedPlatforms.Contains(mode) && PlatformProfile.ForMode(mode).Level >= spec.MinimumPlatformLevel;
    }

    private bool UpdateDrainageWeather()
    {
        if (_activeStorySpec?.MissionId != "HIST-02" || _activeMachine is not { IsActiveState: true, Snapshot: { } job } ||
            _calendar.IsRaining || _calendar.CalendarTimeSeconds < job.AcceptedAtSeconds + DrainageRainDelaySeconds ||
            _calendar.CalendarTimeSeconds > job.ExpiresAtSeconds)
            return false;

        _calendar.SetWeather(true, .8);
        _feedbackText = Text("city.office.rain_arrived");
        return true;
    }

    private string CitySurveyReviewText()
    {
        if (_lastCitySurveyReview is not { } report)
            return "";

        var locale = TranslationServer.GetLocale().Split('_')[0];
        var culture = CultureInfo.GetCultureInfo(locale == "pt" ? "pt-BR" : "en-US");
        var rows = new List<string>
        {
            string.Format(culture, Text("city.office.survey_review"), report.MatchedFeatureCount,
                _lastCitySurveyTargetCount, report.ValidSampleCount, report.UniqueSampleCount)
        };
        foreach (var (metric, labelKey) in new[] { ("absolute_rmse", "absolute"), ("relative_rmse", "relative") })
        {
            if (report.GetMetric(metric) is { Value: { } value, Limit: { } limit })
                rows.Add(string.Format(culture, Text("city.office.rmse_" + labelKey), value, limit));
        }
        foreach (var segment in _lastSidewalkMeasurements)
            rows.Add(string.Format(culture, Text("city.office.sidewalk_measurement"),
                segment.SegmentId, segment.WidthMetres, segment.CurbHeightMetres));
        return string.Join("\n", rows);
    }

    private static string CityOfficeUnavailableKey(string product) => product switch
    {
        "point_cloud" or "pavement_profile" => "city.office.requires_lidar",
        "feature_inventory" => "city.office.requires_inventory",
        _ => "city.office.requires_coverage",
    };

    private void ProcessAndDeliverCityContract()
    {
        if (_activeContract is not { } definition || _activeMachine is not { } machine)
        {
            _feedbackText = Text("city.office.no_contract");
            return;
        }

        var now = _calendar.CalendarTimeSeconds;
        machine.AdvanceClock(now);
        if (machine.State == ContractState.Expirado)
        {
            _feedbackText = Text("city.office.expired");
            SaveCityCareerState(SaveTrigger.Manual, announce: false);
            return;
        }

        if (!SupportsCityDelivery(definition))
        {
            _feedbackText = Text(CityOfficeUnavailableKey(definition.Product));
            return;
        }

        if (_rackMisaligned)
        {
            _feedbackText = Text("city.rack.misaligned");
            _audio.PlayAlert(critical: true);
            return;
        }

        var batchIds = machine.ProtectedBatches.Select(batch => batch.BatchId).ToHashSet(StringComparer.Ordinal);
        var artifacts = _cityCustody.GetProtectedArtifactsForContract(definition.Id)
            .Where(batch => batchIds.Contains(batch.BatchId)).ToArray();
        var samples = artifacts.SelectMany(batch => batch.Samples).ToArray();
        if (samples.Length == 0)
        {
            _feedbackText = Text("city.office.upload_first");
            return;
        }

        var isInventory = definition.Product == "feature_inventory";
        var recipeId = isInventory ? "city_inventory" : "city_images";
        var recipe = new ProcessingRecipe(
            1, recipeId, definition.Id, _officeMode,
            artifacts.Select(batch => batch.BatchId).ToArray(),
            samples.Select(sample => sample.SampleId).Distinct(StringComparer.Ordinal).ToArray(),
            [], new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal), 0, 0);

        if (_officeMode == ProcessingMode.Technical && _privacyMasksApplied)
        {
            recipe = recipe with
            {
                ConfirmedMasksBySampleId = samples.GroupBy(sample => sample.SampleId, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => (IReadOnlyList<string>)group.First().ObservedFeatures
                        .Where(feature => feature.RequiresPrivacyReview).Select(feature => feature.TargetId)
                        .Concat(group.First().PrivacyMasks.Select(mask => mask.TargetId)).Distinct(StringComparer.Ordinal).ToArray(),
                        StringComparer.Ordinal)
            };
        }

        // Processing is synchronous in the city. Only protected, selected samples enter the product.
        var job = new OfficeProcessingQueue().StartOrResumeJob(recipeId, machine, _cityCustody, _ledger, recipe, now);
        if (job.Product is not { } product)
        {
            _feedbackText = Text("city.office.processing_failed");
            return;
        }

        // Older accepted jobs retain their targets and commercial terms. They also
        // need the current evidence for their product before settlement.
        var effectiveDefinition = definition with
        {
            Requirements = definition.Requirements.Concat(CitySurveyRequirements(definition.Id)
                .Where(floor => definition.Requirements.All(rule => rule.Metric != floor.Metric))).ToArray()
        };
        var submission = product.Submission;
        _lastSidewalkMeasurements = [];
        if (isInventory)
            submission = CitySurveyInventory.Reconstruct(definition, submission, out _lastSidewalkMeasurements);
        var reference = isInventory ? CitySurveyInventory.BuildReference(definition, _manifestObjectsById)
            : new EvaluationReference(definition.Id, [], definition.RequiredFeatureIds);
        var report = QualityEvaluator.Evaluate(effectiveDefinition, reference, submission);
        _lastCitySurveyReview = report;
        _lastCitySurveyTargetCount = definition.RequiredFeatureIds.Count;
        var result = machine.RecordQualityEvaluation(now, report.Passed, report.PassedDeliverableIds, report.FailureReasonKeys.FirstOrDefault());
        if (!result.Succeeded || !report.Passed)
        {
            var reason = report.FailureReasonKeys.FirstOrDefault() switch
            {
                "cq.failure.privacy_redacted" => "city.office.review_privacy",
                "cq.failure.all_required_features" when isInventory => "city.office.need_parallax",
                "cq.failure.all_required_features" => "city.office.missing_photo_targets",
                "cq.failure.absolute_rmse" or "cq.failure.relative_rmse" or "cq.failure.insufficient_checkpoints" =>
                    "city.office.measurement_rework",
                "cq.failure.valid_image_fraction" => "city.office.invalid_photos",
                _ => "city.office.processing_failed",
            };
            _feedbackText = Text(reason);
            _audio.PlayAlert();
            SaveCityCareerState(SaveTrigger.Manual, announce: false);
            return;
        }

        var settlement = ContractSettlementCoordinator.SettleDelivery(machine, _ledger, _progression, report, now);
        if (!settlement.Succeeded)
        {
            _feedbackText = Text("city.office.processing_failed");
            return;
        }

        _geopedia.UnlockForMilestone("M2");
        if (_activeStorySpec is { } story)
            CutsceneSkipAndStateCoordinator.ExecuteFull(story.Cutscene, _campaignState);

        var quote = settlement.Quote;
        var credited = settlement.LedgerResult?.AppliedDeltaCoords ?? 0;
        var summary = string.Format(Text("city.office.payout"), quote.Stars, credited, quote.XpAwarded);
        _audio.PlayMissionPassedFanfare();
        _gtaHud.TriggerCelebrationBanner(Text("city.mission_passed"), $"{Text(definition.TitleKey)} · {summary}");
        _feedbackText = summary;
        _activeContract = null;
        _activeMachine = null;
        _activeStorySpec = null;
        _cityHazards.Services.ExpireInsurance();
        SyncCityHazardProtection();
        GenerateContractOffers();
        SaveCityCareerState(SaveTrigger.ContractSettled, announce: false);
    }
}
