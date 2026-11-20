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

    private static bool SupportsCityDelivery(ContractDefinition contract) =>
        contract.Product == "images" && contract.RequiredFeatureIds.Count > 0 &&
        contract.PartialPolicy.Mode == "disabled" &&
        contract.Requirements.All(requirement => CityPhotoRequirements.Any(supported =>
            supported.Metric == requirement.Metric && supported.Kind == requirement.Kind));

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

        var recipe = new ProcessingRecipe(
            1, "city_images", definition.Id, _officeMode,
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
        var job = new OfficeProcessingQueue().StartOrResumeJob("city_images", machine, _cityCustody, _ledger, recipe, now);
        if (job.Product is not { } product)
        {
            _feedbackText = Text("city.office.processing_failed");
            return;
        }

        // Pre-upgrade story saves only specified a delivery format. Apply the photo evidence
        // floor at the office without rewriting their accepted terms or batch payloads.
        var effectiveDefinition = definition with
        {
            Requirements = definition.Requirements.Concat(CityPhotoRequirements
                .Where(floor => definition.Requirements.All(rule => rule.Metric != floor.Metric))).ToArray()
        };
        var reference = new EvaluationReference(definition.Id, [], definition.RequiredFeatureIds);
        var report = QualityEvaluator.Evaluate(effectiveDefinition, reference, product.Submission);
        var result = machine.RecordQualityEvaluation(now, report.Passed, report.PassedDeliverableIds, report.FailureReasonKeys.FirstOrDefault());
        if (!result.Succeeded || !report.Passed)
        {
            var reason = report.FailureReasonKeys.FirstOrDefault() switch
            {
                "cq.failure.privacy_redacted" => "city.office.review_privacy",
                "cq.failure.all_required_features" => "city.office.missing_photo_targets",
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
