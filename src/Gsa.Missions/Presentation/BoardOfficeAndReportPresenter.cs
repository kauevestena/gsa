using Gsa.Missions.Contracts;
using Gsa.Missions.Office;
using Gsa.Missions.Quality;

namespace Gsa.Missions.Presentation;

public sealed record ContractRequirementRowView(
    string Metric,
    string Kind,
    string Unit,
    string Direction,
    string FormattedLimit,
    string FormattedExcellent,
    string MetricLabelKey);

public sealed record ContractDeliverableView(
    string DeliverableId,
    string FormattedWeight,
    string AreaId,
    IReadOnlyList<string> RequiredFeatureIds,
    IReadOnlyList<ContractRequirementRowView> Requirements);

/// <summary>Pre-acceptance contract card exposing all requirements, calm status, deadlines, penalties, and partial rules.</summary>
public sealed record ContractBoardCardView(
    string ContractId,
    string TitleKey,
    string ClientId,
    string RegionId,
    string AreaId,
    string Product,
    IReadOnlyList<string> RequiredCapabilities,
    string Risk,
    bool IsCalm,
    string CalmBadgeKey,
    double DueAfterSeconds,
    double ExpiresAfterSeconds,
    string FormattedDueHours,
    string FormattedExpiresHours,
    int BaseCoords,
    int BaseXp,
    int BaseReputation,
    string FormattedLatePenaltyPerHour,
    string FormattedLatePenaltyCap,
    string PartialPolicyMode,
    IReadOnlyList<ContractDeliverableView> Deliverables,
    IReadOnlyList<ContractRequirementRowView> Requirements);

public sealed record OfficeJobView(
    string ContractId,
    string? ActiveJobId,
    ProcessingJobState? JobState,
    double ProgressFraction,
    string ModeKey,
    int? Revision,
    string? RecipeHash,
    int SelectedSampleCount,
    string FormattedDurationMinutes,
    int ServiceFeeCoords,
    bool WouldExpireBeforeCompletion,
    int AppliedPrivacyMasks,
    int PendingPrivacyMasks,
    VisualIndicator StatusIndicator);

public sealed record QualityMetricRowView(
    string Metric,
    string Unit,
    string? FormattedValue,
    string? FormattedLimit,
    string? FormattedExcellent,
    string StatusKey,
    VisualIndicator Indicator,
    string? ProbableCauseKey);

public sealed record CorrectiveActionCardView(
    string AreaId,
    string ReasonKey,
    string ActionKey,
    string FormattedCenterEnu,
    IReadOnlyList<string> MissingFeatureIds,
    VisualIndicator Indicator);

/// <summary>Arcade and Technical presentations derived from the exact same immutable QualityReport and ContractDefinition.</summary>
public sealed record QualityReportView(
    string ContractId,
    ProcessingMode Mode,
    bool Passed,
    int Stars,
    VisualIndicator OverallIndicator,
    int UniqueSampleCount,
    int ValidSampleCount,
    int MatchedFeatureCount,
    int MatchedCheckpointCount,
    string? RecipeHash,
    IReadOnlyList<QualityMetricRowView> Metrics,
    IReadOnlyList<CorrectiveActionCardView> CorrectiveActionCards,
    bool CanDeliverFull,
    bool CanDeliverPartial,
    int? QuotedNetCoords,
    bool IsPartialQuote,
    string? FormattedLatePenalty,
    string PrimaryActionKey);

public static class BoardOfficeAndReportPresenter
{
    public static ContractBoardCardView BuildContractBoardCard(ContractDefinition def, string? locale)
    {
        ArgumentNullException.ThrowIfNull(def);

        var reqRows = def.Requirements.Select(r => FormatRequirementRow(r, locale)).ToArray();
        var deliverables = def.PartialPolicy.Deliverables
            .Select(d => new ContractDeliverableView(
                d.Id,
                PresentationNumberFormatter.Format(d.Weight * 100.0, locale, 0, false) + "%",
                d.AreaId,
                d.RequiredFeatureIds,
                d.Requirements.Select(r => FormatRequirementRow(r, locale)).ToArray()))
            .ToArray();

        return new ContractBoardCardView(
            def.Id,
            def.TitleKey,
            def.ClientId,
            def.RegionId,
            def.AreaId,
            def.Product,
            def.RequiredCapabilities,
            def.Risk,
            def.Calm,
            def.Calm ? "board.badge.calm" : $"board.risk.{def.Risk}",
            def.Deadline.DueAfterSeconds,
            def.Deadline.ExpiresAfterSeconds,
            PresentationNumberFormatter.Format(def.Deadline.DueAfterSeconds / 3600.0, locale, 1, false),
            PresentationNumberFormatter.Format(def.Deadline.ExpiresAfterSeconds / 3600.0, locale, 1, false),
            def.Reward.BaseCoords,
            def.Reward.BaseXp,
            def.Reward.Reputation,
            PresentationNumberFormatter.Format(def.Deadline.LatePenaltyPerHour * 100.0, locale, 1, false) + "%",
            PresentationNumberFormatter.Format(def.Deadline.LatePenaltyCap * 100.0, locale, 0, false) + "%",
            def.PartialPolicy.Mode,
            deliverables,
            reqRows);
    }

    public static OfficeJobView BuildOfficeView(
        ProcessingQueueQuote? quote,
        ProcessingJob? job,
        string? locale)
    {
        if (job is null && quote is null)
            throw new ArgumentException("At least one of quote or job must be provided.");

        var contractId = job?.Recipe.ContractId ?? quote!.ContractId;
        var mode = job?.Recipe.Mode ?? ProcessingMode.Arcade;
        var durationSeconds = job?.TotalCalendarSeconds ?? quote?.CalendarDurationSeconds ?? 0.0;
        var selectedCount = quote?.SelectedSampleCount ?? job?.Recipe.SelectedSampleIds.Count ?? 0;
        var fee = job?.ChargedFeeCoords ?? quote?.ServiceFeeCoords ?? 0;
        var wouldExpire = quote?.WouldExpireBeforeCompletion ?? job?.State == ProcessingJobState.FailedExpired;

        var appliedMasks = job?.Product?.PrivacyManifest.AppliedMaskCount ?? 0;
        var pendingMasks = job?.Product?.PrivacyManifest.PendingUnmaskedTargetIds.Count ?? 0;

        var indicator = job?.State switch
        {
            ProcessingJobState.Completed => pendingMasks == 0
                ? new VisualIndicator(StatusSeverity.Good, "semantic.good", "icon_job_done", "pattern_solid", "office.status.completed")
                : new VisualIndicator(StatusSeverity.Warning, "semantic.warning", "icon_mask_pending", "pattern_diagonal_hatch", "office.status.privacy_pending"),
            ProcessingJobState.FailedExpired => new VisualIndicator(StatusSeverity.Critical, "semantic.critical", "icon_expired", "pattern_crosshatch", "office.status.expired"),
            ProcessingJobState.Cancelled => new VisualIndicator(StatusSeverity.Neutral, "semantic.neutral", "icon_cancelled", "pattern_dots", "office.status.cancelled"),
            ProcessingJobState.Running => new VisualIndicator(StatusSeverity.Neutral, "semantic.neutral", "icon_processing_spin", "pattern_dots", "office.status.running"),
            _ => wouldExpire
                ? new VisualIndicator(StatusSeverity.Warning, "semantic.warning", "icon_clock_risk", "pattern_diagonal_hatch", "office.status.expiry_risk")
                : new VisualIndicator(StatusSeverity.Good, "semantic.good", "icon_queue_ready", "pattern_solid", "office.status.ready"),
        };

        return new OfficeJobView(
            contractId,
            job?.JobId,
            job?.State,
            job?.ProgressFraction ?? 0.0,
            mode == ProcessingMode.Arcade ? "office.mode.arcade" : "office.mode.technical",
            job?.Product?.Revision,
            job?.Product?.RecipeHash ?? quote?.RecipeHash,
            selectedCount,
            PresentationNumberFormatter.Format(durationSeconds / 60.0, locale, 1, false),
            fee,
            wouldExpire,
            appliedMasks,
            pendingMasks,
            indicator);
    }

    public static QualityReportView BuildQualityReportView(
        ContractDefinition def,
        QualityReport report,
        PaymentQuote? quote,
        ProcessingMode mode,
        string? locale,
        string? recipeHash = null)
    {
        ArgumentNullException.ThrowIfNull(def);
        ArgumentNullException.ThrowIfNull(report);
        if (!string.Equals(def.Id, report.ContractId, StringComparison.Ordinal))
            throw new ArgumentException("QualityReport contract ID does not match ContractDefinition.");

        var unitByMetric = def.Requirements
            .OfType<ThresholdRequirementSpec>()
            .ToDictionary(r => r.Metric, r => r.Unit, StringComparer.Ordinal);
        var dirByMetric = def.Requirements
            .OfType<ThresholdRequirementSpec>()
            .ToDictionary(r => r.Metric, r => r.Direction, StringComparer.Ordinal);

        var metricRows = new List<QualityMetricRowView>(report.Metrics.Count);
        foreach (var metric in report.Metrics)
        {
            var unit = unitByMetric.TryGetValue(metric.Metric, out var u) ? u : "";
            string? formattedVal = null;
            if (metric.Value.HasValue)
            {
                formattedVal = string.Equals(metric.Kind, "predicate", StringComparison.Ordinal)
                    ? (metric.BooleanValue == true ? "1" : "0")
                    : PresentationNumberFormatter.Format(metric.Value.Value, locale, 3, false);
            }

            var formattedLimit = metric.Limit.HasValue
                ? PresentationNumberFormatter.Format(metric.Limit.Value, locale, 3, false)
                : (string.Equals(metric.Kind, "predicate", StringComparison.Ordinal) ? "1" : null);
            var formattedExcellent = metric.Excellent.HasValue
                ? PresentationNumberFormatter.Format(metric.Excellent.Value, locale, 3, false)
                : null;

            var statusKey = metric.Status switch
            {
                MetricEvaluationStatus.Passed => "cq.status.passed",
                MetricEvaluationStatus.InsufficientData => "cq.status.insufficient",
                _ => "cq.status.failed",
            };

            var indicator = metric.Status switch
            {
                MetricEvaluationStatus.Passed => new VisualIndicator(StatusSeverity.Good, "semantic.good", "icon_metric_pass", "pattern_solid", statusKey),
                MetricEvaluationStatus.InsufficientData => new VisualIndicator(StatusSeverity.Warning, "semantic.warning", "icon_metric_missing", "pattern_diagonal_hatch", statusKey),
                _ => new VisualIndicator(StatusSeverity.Critical, "semantic.critical", "icon_metric_fail", "pattern_crosshatch", statusKey),
            };

            var cause = metric.Status == MetricEvaluationStatus.Passed
                ? null
                : metric.FailureReasonKey ?? $"cq.failure.{metric.Metric}";

            metricRows.Add(new QualityMetricRowView(
                metric.Metric,
                unit,
                formattedVal,
                formattedLimit,
                formattedExcellent,
                statusKey,
                indicator,
                cause));
        }

        var correctiveCards = report.RecollectionAreas
            .Select(area =>
            {
                var actionKey = MapCorrectiveActionKey(area.ReasonKey);
                var centerStr = $"{PresentationNumberFormatter.FormatCoordinate(area.CenterEnu.X, locale)}; " +
                                $"{PresentationNumberFormatter.FormatCoordinate(area.CenterEnu.Y, locale)}; " +
                                $"{PresentationNumberFormatter.FormatCoordinate(area.CenterEnu.Z, locale)}";
                return new CorrectiveActionCardView(
                    area.AreaId,
                    area.ReasonKey,
                    actionKey,
                    centerStr,
                    area.MissingFeatureIds,
                    new VisualIndicator(StatusSeverity.Warning, "semantic.warning", "icon_recollect_pin", "pattern_diagonal_hatch", actionKey));
            })
            .ToArray();

        // If report failed for privacy/format/checkpoints without spatial RecollectionAreas, surface top failure as an actionable card.
        if (!report.Passed && correctiveCards.Length == 0)
        {
            var topFailure = report.FailureReasonKeys.FirstOrDefault() ?? "cq.failure.general";
            var actionKey = MapCorrectiveActionKey(topFailure);
            correctiveCards =
            [
                new CorrectiveActionCardView(
                    def.AreaId,
                    topFailure,
                    actionKey,
                    "--",
                    [],
                    new VisualIndicator(StatusSeverity.Critical, "semantic.critical", "icon_fix_office", "pattern_crosshatch", actionKey)),
            ];
        }

        var stars = quote is { Eligible: true } ? quote.Stars : 0;
        var canDeliverFull = report.Passed && (quote is null || (!quote.IsExpired && quote.Eligible && !quote.IsPartial));
        var canDeliverPartial = !report.Passed &&
                                quote is { Eligible: true, IsPartial: true, IsExpired: false };

        var overallIndicator = canDeliverFull
            ? new VisualIndicator(StatusSeverity.Good, "semantic.good", "icon_cq_approved", "pattern_solid", "cq.overall.approved")
            : canDeliverPartial
                ? new VisualIndicator(StatusSeverity.Warning, "semantic.warning", "icon_cq_partial", "pattern_diagonal_hatch", "cq.overall.partial_available")
                : new VisualIndicator(StatusSeverity.Critical, "semantic.critical", "icon_cq_rework", "pattern_crosshatch", "cq.overall.rework_required");

        var primaryAction = canDeliverFull
            ? "cq.action.deliver_full"
            : canDeliverPartial
                ? "cq.action.choose_partial_or_recollect"
                : correctiveCards.Length > 0 ? correctiveCards[0].ActionKey : "cq.action.recollect";

        string? formattedLatePenalty = quote is { LatePenaltyFraction: > 0.0 }
            ? PresentationNumberFormatter.Format(quote.LatePenaltyFraction * 100.0, locale, 1, false) + "%"
            : null;

        return new QualityReportView(
            report.ContractId,
            mode,
            report.Passed,
            stars,
            overallIndicator,
            report.UniqueSampleCount,
            report.ValidSampleCount,
            report.MatchedFeatureCount,
            report.MatchedCheckpointCount,
            recipeHash,
            Array.AsReadOnly(metricRows.ToArray()),
            Array.AsReadOnly(correctiveCards),
            canDeliverFull,
            canDeliverPartial,
            quote is { Eligible: true } ? quote.NetCoords : null,
            quote?.IsPartial ?? false,
            formattedLatePenalty,
            primaryAction);
    }

    private static ContractRequirementRowView FormatRequirementRow(ContractRequirementSpec spec, string? locale)
    {
        if (spec is ThresholdRequirementSpec threshold)
        {
            return new ContractRequirementRowView(
                threshold.Metric,
                threshold.Kind,
                threshold.Unit,
                threshold.Direction,
                PresentationNumberFormatter.Format(threshold.Limit, locale, 2, false),
                PresentationNumberFormatter.Format(threshold.Excellent, locale, 2, false),
                $"cq.metric.{threshold.Metric}");
        }

        var predicate = (PredicateRequirementSpec)spec;
        return new ContractRequirementRowView(
            predicate.Metric,
            predicate.Kind,
            "bool",
            "eq",
            predicate.Expected ? "1" : "0",
            predicate.Expected ? "1" : "0",
            $"cq.metric.{predicate.Metric}");
    }

    private static string MapCorrectiveActionKey(string reasonKey) => reasonKey switch
    {
        "cq.failure.privacy_redacted" => "cq.action.apply_privacy_masks",
        "cq.failure.format_valid" => "cq.action.fix_bundle_format",
        "cq.failure.insufficient_checkpoints" => "cq.action.capture_checkpoints",
        "cq.failure.area_coverage" or "cq.failure.uncovered_cell" => "cq.action.recollect_uncovered_area",
        "cq.failure.missing_feature" or "cq.failure.feature_completeness" or "cq.failure.all_required_features" => "cq.action.recollect_missing_features",
        "cq.failure.valid_image_fraction" => "cq.action.recapture_blur_free_images",
        _ => "cq.action.reprocess_or_recollect",
    };
}

