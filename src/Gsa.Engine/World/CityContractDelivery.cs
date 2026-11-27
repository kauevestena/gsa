using System.Globalization;
using Gsa.Missions.Gameplay;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Camera;
using Gsa.Core.World;
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

    private CityPhotoCoverage? _cityPhotoCoverage;
    private readonly Dictionary<string, HashSet<string>> _coveredCellsByTarget = new(StringComparer.Ordinal);
    private int _coveredCellTotal;
    private int _lastAdjustedPhotoCount;
    private IReadOnlyList<string> _lastIncompleteSurfaces = [];

    private const string FacadeArea = "commerce_street_facades";
    private const string ParkArea = "central_park_calm";
    private const string DrainageArea = "commerce_street_drainage";

    private static IReadOnlyList<ContractRequirementSpec> CitySurveyRequirements(string area, bool story) => area switch
    {
        FacadeArea => [.. CityPhotoRequirements, new ThresholdRequirementSpec("area_coverage", "ratio", "min",
            story ? .90 : .85, .98, "commerce_facade_planes", CityPhotoCoverage.CellSizeMetres)],
        ParkArea => [.. CityPhotoRequirements, new ThresholdRequirementSpec("area_coverage", "ratio", "min",
            .85, .96, "central_park_planes", CityPhotoCoverage.CellSizeMetres)],
        DrainageArea => [.. CityPhotoRequirements, new ThresholdRequirementSpec("absolute_rmse", "m", "max", 2, .5)],
        CitySurveyInventory.SidewalkArea => [.. CityPhotoRequirements, new ThresholdRequirementSpec("relative_rmse", "m", "max", .2, .05)],
        CitySurveyInventory.SignsArea => [.. CityPhotoRequirements, new ThresholdRequirementSpec("relative_rmse", "m", "max", .15, .05)],
        BusSurveyCorridor.AreaId =>
        [
            new PredicateRequirementSpec("all_required_features", true),
            new ThresholdRequirementSpec("valid_image_fraction", "ratio", "min", .9, .99),
            new PredicateRequirementSpec("privacy_redacted", true),
            new PredicateRequirementSpec("format_valid", true),
            new ThresholdRequirementSpec("absolute_rmse", "m", "max", 6, .5),
        ],
        _ => CityPhotoRequirements,
    };

    private static bool SupportsCityStory(CampaignMissionFullSpec spec) =>
        spec.MissionId is "HIST-01" or "HIST-02" or "HIST-03" or "HIST-04" or BusSurveyCorridor.MissionId or TunnelMissionId or
            PortViaductSurvey.MissionId or WaterfrontRainSurvey.MissionId or SerraBoundarySurvey.MissionId or PortWorksSurvey.MissionId or FinalCityPavementSurvey.MissionId;

    private bool SupportsCityDelivery(ContractDefinition definition)
    {
        if (definition.RequiredFeatureIds.Count == 0 || definition.PartialPolicy.Mode != "disabled" ||
            !definition.RequiredFeatureIds.All(_manifestObjectsById.ContainsKey))
            return false;

        var inventory = definition.Product == "feature_inventory" &&
            definition.AreaId is DrainageArea or CitySurveyInventory.SidewalkArea or CitySurveyInventory.SignsArea or BusSurveyCorridor.AreaId;
        var photos = definition.Product == "images" && definition.AreaId is FacadeArea or ParkArea;
        if (!photos && !inventory)
            return false;
        if (inventory && !CitySurveyInventory.HasReference(definition, _manifestObjectsById))
            return false;

        var supported = CitySurveyRequirements(definition.AreaId, definition.Id == "HIST-01");
        return definition.Requirements.All(requirement =>
            supported.Any(metric => metric.Metric == requirement.Metric && metric.Kind == requirement.Kind &&
                (metric is not ThresholdRequirementSpec allowed || requirement is ThresholdRequirementSpec actual &&
                    actual.SurfaceId == allowed.SurfaceId && actual.CellSizeMetres == allowed.CellSizeMetres)) ||
            inventory && requirement is ThresholdRequirementSpec { Metric: "feature_completeness" });
    }

    private static ContractDefinition CityEffectiveDefinition(ContractDefinition definition) => definition with
    {
        Requirements = definition.Requirements.Concat(CitySurveyRequirements(definition.AreaId, definition.Id == "HIST-01")
            .Where(floor => definition.Requirements.All(rule => rule.Metric != floor.Metric))).ToArray()
    };

    private void PrepareCityPhotoCoverage()
    {
        _cityPhotoCoverage = _activeContract is { } definition && SupportsCityDelivery(definition)
            ? CityPhotoCoverage.ForContract(CityEffectiveDefinition(definition), _manifestObjectsById)
            : null;
        RebuildCoveredSurfaceCells();
    }

    private void RebuildCoveredSurfaceCells()
    {
        _coveredCellsByTarget.Clear();
        _coveredCellTotal = 0;
        if (_cityPhotoCoverage is null) return;
        foreach (var sample in _fieldSamples.Concat(_protectedCityPhotos))
        {
            if (!sample.Quality.Passed) continue;
            AddSampleCoverageCells(sample);
        }
    }

    private void AddSampleCoverageCells(CameraObservationSample sample)
    {
        foreach (var feature in sample.ObservedFeatures)
        {
            if (feature.ObjectClass != CityPhotoCoverage.ObservationClass ||
                !feature.TargetId.StartsWith("coverage/", StringComparison.Ordinal))
                continue;
            var slash = feature.TargetId.IndexOf('/', 9);
            if (slash <= 9) continue;
            var targetId = feature.TargetId[9..slash];
            if (!_coveredCellsByTarget.TryGetValue(targetId, out var set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                _coveredCellsByTarget[targetId] = set;
            }
            if (set.Add(feature.TargetId))
                _coveredCellTotal++;
        }
    }

    private bool CityMissionCompleted(string missionId) =>
        _campaignState.ObjectiveStates.TryGetValue(missionId, out var state) && state == $"completed:{missionId}";

    private bool CityHasSurveyPlatform(int minimumLevel)
    {
        var mode = _locomotion.MountedVehicleMode;
        return _fleet.OwnedPlatforms.Contains(mode) && PlatformProfile.ForMode(mode).Level >= minimumLevel;
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
        var imageMetric = report.GetMetric("valid_image_fraction");
        var reviewedImages = imageMetric?.SampleCount ?? report.UniqueSampleCount;
        var validImages = imageMetric?.Value is { } fraction ? (int)Math.Round(fraction * reviewedImages) : report.ValidSampleCount;
        var rows = new List<string>
        {
            string.Format(culture, Text("city.office.survey_review"), report.MatchedFeatureCount,
                _lastCitySurveyTargetCount, validImages, reviewedImages)
        };
        foreach (var (metric, labelKey) in new[] { ("absolute_rmse", "absolute"), ("relative_rmse", "relative") })
        {
            if (report.GetMetric(metric) is { Value: { } value, Limit: { } limit })
                rows.Add(string.Format(culture, Text("city.office.rmse_" + labelKey), value, limit));
        }
        if (report.GetMetric("area_coverage") is { Value: { } coverage, Limit: { } minimum })
            rows.Add(string.Format(culture, Text("city.office.surface_coverage"), coverage, minimum));
        if (_lastIncompleteSurfaces.Count > 0)
            rows.Add(string.Format(culture, Text("city.office.incomplete_surfaces"), string.Join(", ", _lastIncompleteSurfaces)));
        if (report.ContractId == BusSurveyCorridor.MissionId)
        {
            var missingStops = report.RecollectionAreas.SelectMany(area => area.MissingFeatureIds).Distinct(StringComparer.Ordinal).ToArray();
            if (missingStops.Length > 0)
                rows.Add(string.Format(culture, Text("city.bus.missing_stops"), string.Join(", ", missingStops)));
        }
        if (_lastAdjustedPhotoCount > 0)
            rows.Add(string.Format(culture, Text("city.office.control_adjusted"), _lastAdjustedPhotoCount));
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

        if (IsTunnelCollection(definition))
        {
            ProcessTunnelDelivery(machine, now);
            return;
        }

        if (IsViaductCollection(definition))
        {
            ProcessViaductDelivery(machine, now);
            return;
        }

        if (IsRainCollection(definition))
        {
            ProcessRainDelivery(machine, now);
            return;
        }

        if (IsBoundarySurvey(definition))
        {
            ProcessBoundaryDelivery(machine, now);
            return;
        }

        if (IsWorksSurvey(definition))
        {
            ProcessWorksDelivery(machine, now);
            return;
        }

        if (IsFinalPavementSurvey(definition))
        {
            ProcessFinalPavementDelivery(machine, now);
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
        var selectedSamples = _pruneRejectedFramesInOffice && samples.Any(s => s.Quality.Passed)
            ? samples.Where(s => s.Quality.Passed).Select(sample => sample.SampleId).Distinct(StringComparer.Ordinal).ToArray()
            : samples.Select(sample => sample.SampleId).Distinct(StringComparer.Ordinal).ToArray();
        var recipe = new ProcessingRecipe(
            1, recipeId, definition.Id, _officeMode,
            artifacts.Select(batch => batch.BatchId).ToArray(),
            selectedSamples,
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
        var effectiveDefinition = CityEffectiveDefinition(definition);
        var submission = product.Submission;
        _lastSidewalkMeasurements = [];
        _lastAdjustedPhotoCount = 0;
        _lastIncompleteSurfaces = [];
        EvaluationReference reference;
        if (isInventory)
        {
            var allSamples = submission.Samples;
            if (definition.AreaId == BusSurveyCorridor.AreaId)
            {
                var targets = definition.RequiredFeatureIds.ToHashSet(StringComparer.Ordinal);
                submission = submission with { Samples = submission.Samples.Where(sample =>
                    sample.SensorId.StartsWith("camera-360-", StringComparison.Ordinal) &&
                    BusSurveyCorridor.IsSurveyWindowAt(sample.TimestampSeconds) &&
                    sample.ObservedFeatures.Any(feature => targets.Contains(feature.TargetId))).ToArray() };
            }
            var controls = CitySurveyInventory.PublishedControls(definition, _manifestObjectsById);
            var inventorySamples = submission.Samples;
            submission = CitySurveyInventory.Reconstruct(definition, submission, controls, out _lastSidewalkMeasurements, out _lastAdjustedPhotoCount);
            if (definition.AreaId == BusSurveyCorridor.AreaId)
            {
                // Scope the image fraction to useful faces, and check privacy and format across the full sweep.
                submission = submission with { Samples = allSamples, ImageQualitySampleIds = inventorySamples.Select(sample => sample.SampleId).ToHashSet(StringComparer.Ordinal) };
            }
            reference = CitySurveyInventory.BuildReference(definition, _manifestObjectsById);
        }
        else
        {
            if (_cityPhotoCoverage is { } coverage)
                submission = submission with { SampleFootprints = coverage.ReadFootprints(submission.Samples).ToArray() };
            reference = new(definition.Id, _cityPhotoCoverage is { } grid ? [grid.Reference] : [], definition.RequiredFeatureIds);
        }
        var report = QualityEvaluator.Evaluate(effectiveDefinition, reference, submission);
        _lastCitySurveyReview = report;
        if (_cityPhotoCoverage is { } incomplete && report.GetMetric("area_coverage")?.Status == MetricEvaluationStatus.Failed)
            _lastIncompleteSurfaces = incomplete.MissingTargets(submission.SampleFootprints);
        _lastCitySurveyTargetCount = definition.RequiredFeatureIds.Count;
        var result = machine.RecordQualityEvaluation(now, report.Passed, report.PassedDeliverableIds, report.FailureReasonKeys.FirstOrDefault());
        if (!result.Succeeded || !report.Passed)
        {
            var reason = report.FailureReasonKeys.FirstOrDefault() switch
            {
                "cq.failure.privacy_redacted" => "city.office.review_privacy",
                "cq.failure.all_required_features" when isInventory => "city.office.need_parallax",
                "cq.failure.all_required_features" => "city.office.missing_photo_targets",
                "cq.failure.area_coverage" => "city.office.coverage_rework",
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

        FinishCityDelivery(definition, machine, report, now);
    }

    private void CompleteCitySurveyReview(ContractStateMachine machine, double now, QualityReport review, string reworkMessage)
    {
        if (!machine.MarkReadyToProcess(now).Succeeded || !machine.StartProcessing(now).Succeeded ||
            !machine.CompleteProcessing(now).Succeeded ||
            !machine.RecordQualityEvaluation(now, review.Passed, primaryFailureKey: review.FailureReasonKeys.FirstOrDefault()).Succeeded)
        {
            _feedbackText = Text("city.office.processing_failed");
            return;
        }

        if (review.Passed)
            FinishCityDelivery(machine.Snapshot!.Definition, machine, review, now);
        else
        {
            _feedbackText = Text(reworkMessage);
            _audio.PlayAlert();
            SaveCityCareerState(SaveTrigger.Manual, announce: false);
        }
    }

    private void FinishCityDelivery(ContractDefinition definition, ContractStateMachine machine, QualityReport report, double now)
    {
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
        if (IsBoundarySurvey(definition) || IsWorksSurvey(definition) || IsFinalPavementSurvey(definition))
        {
            _cityHazards.CancelActiveThreats();
            ClearCityThreatVisuals();
        }
        _locomotion.ReleaseCaptureUnits(CityLidarSlice.StorageUnits(_pendingLidarReturns));
        _locomotion.ReleaseCaptureUnits(_pendingBoundaryShots.Count);
        _pendingBoundaryShots.Clear();
        ClearPendingCityLidar();
        _worksUpload = null;
        _activeContract = null;
        _cityPhotoCoverage = null;
        _activeMachine = null;
        _activeStorySpec = null;
        _cityHazards.Services.ExpireInsurance();
        SyncCityHazardProtection();
        GenerateContractOffers();
        SaveCityCareerState(SaveTrigger.ContractSettled, announce: false);
    }
}
