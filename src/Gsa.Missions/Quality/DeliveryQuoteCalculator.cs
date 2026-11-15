using System.Text.Json;
using Gsa.Missions.Contracts;

namespace Gsa.Missions.Quality;

public enum DeliveryMode
{
    Auto = 0,
    Full = 1,
    Partial = 2,
}

public sealed record PaymentQuote
{
    public int SchemaVersion => 1;
    public string ContractId { get; }
    public bool Eligible { get; }
    public bool IsPartial { get; }
    public bool IsExpired { get; }
    public int Stars { get; }
    public double QualityScore { get; }
    public double PartialWeight { get; }
    public double LateHours { get; }
    public double LatePenaltyFraction { get; }
    public int BaseCoords { get; }
    public int GrossCoords { get; }
    public int NetCoords { get; }
    public int XpAwarded { get; }
    public int ReputationDelta { get; }
    public IReadOnlyList<string> PassedDeliverableIds { get; }
    public IReadOnlyList<string> StoryUnlocks { get; }
    public string? ReasonKey { get; }

    public PaymentQuote(
        string contractId,
        bool eligible,
        bool isPartial,
        bool isExpired,
        int stars,
        double qualityScore,
        double partialWeight,
        double lateHours,
        double latePenaltyFraction,
        int baseCoords,
        int grossCoords,
        int netCoords,
        int xpAwarded,
        int reputationDelta,
        IReadOnlyList<string> passedDeliverableIds,
        IReadOnlyList<string> storyUnlocks,
        string? reasonKey = null)
    {
        ContractId = contractId;
        Eligible = eligible;
        IsPartial = isPartial;
        IsExpired = isExpired;
        Stars = stars;
        QualityScore = qualityScore;
        PartialWeight = partialWeight;
        LateHours = lateHours;
        LatePenaltyFraction = latePenaltyFraction;
        BaseCoords = baseCoords;
        GrossCoords = grossCoords;
        NetCoords = netCoords;
        XpAwarded = xpAwarded;
        ReputationDelta = reputationDelta;
        PassedDeliverableIds = passedDeliverableIds;
        StoryUnlocks = storyUnlocks;
        ReasonKey = reasonKey;
    }

    public string ToJson()
    {
        var payload = new
        {
            schema_version = SchemaVersion,
            contract_id = ContractId,
            eligible = Eligible,
            is_partial = IsPartial,
            is_expired = IsExpired,
            stars = Stars,
            quality_score = QualityScore,
            partial_weight = PartialWeight,
            late_hours = LateHours,
            late_penalty_fraction = LatePenaltyFraction,
            base_coords = BaseCoords,
            gross_coords = GrossCoords,
            net_coords = NetCoords,
            xp_awarded = XpAwarded,
            reputation_delta = ReputationDelta,
            passed_deliverable_ids = PassedDeliverableIds,
            story_unlocks = StoryUnlocks,
            reason_key = ReasonKey,
        };
        return JsonSerializer.Serialize(payload);
    }
}

/// <summary>Computes stars, quality margins, late penalties, and rounded Coords/XP/reputation payouts.</summary>
public static class DeliveryQuoteCalculator
{
    public static PaymentQuote Calculate(
        ContractDefinition contract,
        QualityReport report,
        double acceptedAtSeconds,
        double deliveredAtSeconds,
        DeliveryMode requestedMode = DeliveryMode.Auto)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(report);
        if (!string.Equals(contract.Id, report.ContractId, StringComparison.Ordinal))
            throw new ArgumentException("QualityReport contract ID does not match ContractDefinition.");
        if (!double.IsFinite(acceptedAtSeconds) || acceptedAtSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(acceptedAtSeconds), "Acceptance time must be finite and non-negative.");
        if (!double.IsFinite(deliveredAtSeconds) || deliveredAtSeconds < acceptedAtSeconds)
            throw new ArgumentOutOfRangeException(nameof(deliveredAtSeconds), "Delivery time must be finite and at least acceptance time.");

        if (HasComplianceFailure(report))
        {
            return RejectedQuote(
                contract,
                isExpired: false,
                reputationDelta: 0,
                reasonKey: "quote.rejected.compliance");
        }

        var dueAtSeconds = acceptedAtSeconds + contract.Deadline.DueAfterSeconds;
        var expiresAtSeconds = acceptedAtSeconds + contract.Deadline.ExpiresAfterSeconds;
        if (deliveredAtSeconds > expiresAtSeconds)
        {
            return RejectedQuote(
                contract,
                isExpired: true,
                reputationDelta: -1,
                reasonKey: "quote.rejected.expired");
        }

        var lateHours = Math.Max(0.0, (deliveredAtSeconds - dueAtSeconds) / 3600.0);
        var latePenalty = Math.Clamp(
            lateHours * contract.Deadline.LatePenaltyPerHour,
            0.0,
            contract.Deadline.LatePenaltyCap);

        if (report.Passed && requestedMode != DeliveryMode.Partial)
        {
            var q = ComputeMarginAverage(contract.Requirements, report);
            var stars = MapFullStars(q);
            var qualityFactor = 0.8 + 0.2 * q;
            var grossCoords = (int)Math.Floor(contract.Reward.BaseCoords * qualityFactor * (1.0 - latePenalty));
            var xp = (int)Math.Floor(contract.Reward.BaseXp * qualityFactor);
            var reputationDelta = stars == 5 ? Math.Max(contract.Reward.Reputation + 1, 3) : contract.Reward.Reputation;

            return new PaymentQuote(
                contract.Id,
                eligible: true,
                isPartial: false,
                isExpired: false,
                stars: stars,
                qualityScore: q,
                partialWeight: 1.0,
                lateHours: lateHours,
                latePenaltyFraction: latePenalty,
                baseCoords: contract.Reward.BaseCoords,
                grossCoords: grossCoords,
                netCoords: grossCoords,
                xpAwarded: xp,
                reputationDelta: reputationDelta,
                passedDeliverableIds: report.PassedDeliverableIds,
                storyUnlocks: contract.Reward.StoryUnlocks,
                reasonKey: null);
        }

        if (requestedMode != DeliveryMode.Full &&
            contract.PartialPolicy.Mode == "deliverables" &&
            report.PassedDeliverableIds.Count > 0)
        {
            var passedSet = new HashSet<string>(report.PassedDeliverableIds, StringComparer.Ordinal);
            var passedDeliverables = contract.PartialPolicy.Deliverables.Where(d => passedSet.Contains(d.Id)).ToArray();
            var partialWeight = Math.Clamp(passedDeliverables.Sum(d => d.Weight), 0.0, 1.0);
            if (partialWeight > 0.0)
            {
                var q = ComputeDeliverableMarginAverage(passedDeliverables, report);
                var stars = Math.Min(2, Math.Max(1, (int)Math.Ceiling(2.0 * partialWeight)));
                var qualityFactor = 0.8 + 0.2 * q;
                var grossCoords = (int)Math.Floor(contract.Reward.BaseCoords * partialWeight * qualityFactor * (1.0 - latePenalty));
                var xp = (int)Math.Floor(contract.Reward.BaseXp * partialWeight * qualityFactor);

                return new PaymentQuote(
                    contract.Id,
                    eligible: true,
                    isPartial: true,
                    isExpired: false,
                    stars: stars,
                    qualityScore: q,
                    partialWeight: partialWeight,
                    lateHours: lateHours,
                    latePenaltyFraction: latePenalty,
                    baseCoords: contract.Reward.BaseCoords,
                    grossCoords: grossCoords,
                    netCoords: grossCoords,
                    xpAwarded: xp,
                    reputationDelta: 1,
                    passedDeliverableIds: passedDeliverables.Select(d => d.Id).ToArray(),
                    storyUnlocks: [],
                    reasonKey: null);
            }
        }

        return RejectedQuote(
            contract,
            isExpired: false,
            reputationDelta: 0,
            reasonKey: "quote.rejected.cq_failed");
    }

    private static bool HasComplianceFailure(QualityReport report)
    {
        if (report.FailureReasonKeys.Contains("cq.failure.privacy_redacted", StringComparer.Ordinal) ||
            report.FailureReasonKeys.Contains("cq.failure.format_valid", StringComparer.Ordinal) ||
            report.FailureReasonKeys.Contains("cq.failure.empty_submission", StringComparer.Ordinal))
        {
            return true;
        }

        var privacy = report.GetMetric("privacy_redacted");
        if (privacy is not null && privacy.Status != MetricEvaluationStatus.Passed)
            return true;

        var format = report.GetMetric("format_valid");
        if (format is not null && format.Status != MetricEvaluationStatus.Passed)
            return true;

        return false;
    }

    private static double ComputeMarginAverage(IReadOnlyList<ContractRequirementSpec> specs, QualityReport report)
    {
        var margins = new List<double>();
        foreach (var spec in specs)
        {
            if (spec is not ThresholdRequirementSpec threshold || threshold.IsBinaryLimit)
                continue;

            var measured = report.GetMetric(threshold.Metric);
            if (measured?.Value is not { } actual)
                continue;

            margins.Add(ComputeNormalizedMargin(threshold, actual));
        }

        return margins.Count == 0 ? 1.0 : margins.Average();
    }

    private static double ComputeDeliverableMarginAverage(ContractDeliverableSpec[] deliverables, QualityReport report)
    {
        var margins = new List<double>();
        foreach (var deliverable in deliverables)
        {
            foreach (var req in deliverable.Requirements)
            {
                if (req is not ThresholdRequirementSpec threshold || threshold.IsBinaryLimit)
                    continue;
                var measured = report.GetMetric(threshold.Metric);
                if (measured?.Value is not { } actual)
                    continue;
                margins.Add(ComputeNormalizedMargin(threshold, actual));
            }
        }
        return margins.Count == 0 ? 0.5 : margins.Average();
    }

    private static double ComputeNormalizedMargin(ThresholdRequirementSpec spec, double actual)
    {
        if (spec.Direction == "min")
        {
            var span = spec.Excellent - spec.Limit;
            return span <= 0.0 ? 1.0 : Math.Clamp((actual - spec.Limit) / span, 0.0, 1.0);
        }

        var maxSpan = spec.Limit - spec.Excellent;
        return maxSpan <= 0.0 ? 1.0 : Math.Clamp((spec.Limit - actual) / maxSpan, 0.0, 1.0);
    }

    private static int MapFullStars(double q)
    {
        if (q >= 1.0 - 1e-9)
            return 5;
        if (q >= 0.75 - 1e-9)
            return 4;
        if (q >= 0.50 - 1e-9)
            return 3;
        if (q >= 0.25 - 1e-9)
            return 2;
        return 1;
    }

    private static PaymentQuote RejectedQuote(ContractDefinition contract, bool isExpired, int reputationDelta, string reasonKey) =>
        new(
            contract.Id,
            eligible: false,
            isPartial: false,
            isExpired: isExpired,
            stars: 0,
            qualityScore: 0.0,
            partialWeight: 0.0,
            lateHours: 0.0,
            latePenaltyFraction: 0.0,
            baseCoords: contract.Reward.BaseCoords,
            grossCoords: 0,
            netCoords: 0,
            xpAwarded: 0,
            reputationDelta: reputationDelta,
            passedDeliverableIds: [],
            storyUnlocks: [],
            reasonKey: reasonKey);
}
