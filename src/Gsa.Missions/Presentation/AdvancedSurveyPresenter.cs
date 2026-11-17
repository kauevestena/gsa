using Gsa.Core.Sensors.Lidar;
using Gsa.Core.Traffic;
using Gsa.Missions.Contracts;

namespace Gsa.Missions.Presentation;

/// <summary>Display diagnostics only. These values cannot approve a processed product or calculate payment.</summary>
public sealed record LidarPreviewView(
    string ProfileId,
    string StatusKey,
    string NominalPrimaryRaysPerSecond,
    string ScheduledPrimaryRaysPerSecond,
    string MaximumRangeMetres,
    string RangeSigmaMetres,
    string VisiblePoints,
    string DisplayCapacity);

public sealed record TechnicalRequirementView(
    string Metric,
    string Comparison,
    string Unit,
    string Limit,
    string Excellent,
    bool? Expected,
    string? SurfaceId,
    string? CellSizeMetres,
    string? TargetDensityPointsPerM2,
    string? DeliverableId = null,
    string? AreaId = null);

public sealed record AttentionZoneView(
    string ZoneId,
    string FactionNameKey,
    TerritoryBoundsEnu Bounds,
    double AttentionScore,
    int ThreatLevel,
    double CooldownSeconds,
    bool ProtectionActive);

public static class AdvancedSurveyPresenter
{
    public static LidarPreviewView BuildLidarPreview(
        LidarProfile profile, bool enabled, int channelStep, int visiblePoints, int displayCapacity, string? locale)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (channelStep < 1) throw new ArgumentOutOfRangeException(nameof(channelStep));
        if (displayCapacity < 1) throw new ArgumentOutOfRangeException(nameof(displayCapacity));
        if (visiblePoints < 0 || visiblePoints > displayCapacity) throw new ArgumentOutOfRangeException(nameof(visiblePoints));

        var sampledChannels = (profile.TotalChannels + channelStep - 1) / channelStep;
        var scheduledRate = sampledChannels * profile.AzimuthsPerScan * profile.ScanRateHz;
        string Number(double v, int decimals = 0) => PresentationNumberFormatter.Format(v, locale, decimals, false);
        return new(profile.Id, enabled ? "survey.technical.lidar_on" : "survey.technical.lidar_off",
            Number(profile.PrimaryRaysPerSecond), Number(scheduledRate), Number(profile.MaxRangeMetres, 1),
            Number(profile.RangeSigmaMetres, 3), Number(visiblePoints), Number(displayCapacity));
    }

    /// <summary>Reads the accepted contract verbatim, including per-surface cell/density context. Does not evaluate QC.</summary>
    public static IReadOnlyList<TechnicalRequirementView> BuildRequirements(ContractDefinition? contract, string? locale)
    {
        if (contract is null) return [];
        // A rounded threshold could falsely advertise that a failing measurement passes (e.g. 0.000125 -> 0).
        string Number(double v) => double.IsFinite(v)
            ? v.ToString("R", System.Globalization.CultureInfo.GetCultureInfo(PresentationNumberFormatter.ResolveLocale(locale)))
            : throw new ArgumentOutOfRangeException(nameof(contract), "Contract thresholds must be finite.");
        TechnicalRequirementView Row(ContractRequirementSpec spec) => spec switch
        {
            ThresholdRequirementSpec t => new TechnicalRequirementView(t.Metric,
                t.Direction switch { "min" => "≥", "max" => "≤", _ => throw new ArgumentException("Unknown threshold direction.", nameof(contract)) },
                t.Unit, Number(t.Limit), Number(t.Excellent), null, t.SurfaceId,
                t.CellSizeMetres.HasValue ? Number(t.CellSizeMetres.Value) : null,
                t.TargetDensityPointsPerM2.HasValue ? Number(t.TargetDensityPointsPerM2.Value) : null),
            PredicateRequirementSpec p => new TechnicalRequirementView(p.Metric, "=", "bool",
                p.Expected ? "1" : "0", p.Expected ? "1" : "0", p.Expected, null, null, null),
            _ => throw new ArgumentException("Unknown requirement type.", nameof(contract)),
        };
        return Array.AsReadOnly(contract.Requirements.Select(Row).Concat(
            contract.PartialPolicy.Deliverables.SelectMany(d => d.Requirements.Select(spec =>
                Row(spec) with { DeliverableId = d.Id, AreaId = d.AreaId }))).ToArray());
    }

    public static IReadOnlyList<AttentionZoneView> BuildAttentionZones(
        IEnumerable<TerritoryZoneDefinition> zones, TerritoryAttentionTracker tracker, bool protectionActive)
    {
        ArgumentNullException.ThrowIfNull(zones);
        ArgumentNullException.ThrowIfNull(tracker);
        return Array.AsReadOnly(zones.OrderBy(z => z.ZoneId, StringComparer.Ordinal).Select(z =>
        {
            var attention = tracker.GetAttention(z.ZoneId);
            return new AttentionZoneView(z.ZoneId, FactionCatalog.GetFaction(z.ControllingFaction).NameKey,
                z.Bounds, attention, (int)TerritoryAttentionTracker.ClassifyThreatLevel(attention),
                tracker.GetCooldownRemaining(z.ZoneId), protectionActive);
        }).ToArray());
    }
}
