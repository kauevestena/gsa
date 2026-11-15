using Gsa.Core.Mobility;
using Gsa.Missions.Contracts;
using Gsa.Missions.Quality;

namespace Gsa.Missions.Economy;

public enum SkillTrackKind
{
    DefensiveDriving = 0,
    Navigation = 1,
    Calibration = 2,
    Processing = 3,
    Negotiation = 4,
    NightShift = 5,
}

public sealed record ShopPlatformCatalogEntry(
    PlatformProfile Profile,
    string? RequiredStoryMissionId,
    string MechanicSummaryKey,
    int RequiredGlobalReputation = 0,
    string? RequiredClientId = null,
    int RequiredClientReputation = 0);

public sealed record PlatformPurchaseQuote(
    bool Unlocked,
    bool AlreadyOwned,
    PlatformProfile Profile,
    int BasePriceCoords,
    double DiscountFraction,
    int DiscountCoords,
    int FinalPriceCoords,
    string? LockReasonKey = null);

public sealed record SensorUpgradeQuote(
    bool Eligible,
    string SensorUpgradeId,
    int RequiredPlatformLevel,
    int CurrentPlatformLevel,
    int BasePriceCoords,
    double DiscountFraction,
    int FinalPriceCoords,
    string? RejectionReasonKey = null,
    string? TargetGrade = null);

public sealed record SkillTrackBenefitSummary(
    SkillTrackKind Track,
    int Rank,
    double BenefitFraction,
    string EffectKey,
    string NonScientificGuaranteeKey);

public sealed record FleetProgressionSnapshot(
    int SchemaVersion,
    string ActivePlatformId,
    IReadOnlyList<LocomotionMode> OwnedPlatforms,
    IReadOnlyList<string> OwnedSensorUpgrades,
    IReadOnlyDictionary<SkillTrackKind, int> SkillRanks);

public sealed record StarterRecoveryResult(
    LedgerOperationResult LedgerResult,
    LocomotionMode ActiveMode,
    bool LowBalanceSubsidyActive);

public static class FleetShopCatalog
{
    public const int XpPerSkillPoint = 500;
    public const int MaxSkillRank = 3;
    public const double MaxReputationDiscountFraction = 0.15;
    public const int NavigationGnssUpgradePriceCoords = 180;
    public const int LidarBasicUpgradePriceCoords = 5000;

    public static IReadOnlyList<ShopPlatformCatalogEntry> Platforms { get; } =
    [
        new(PlatformProfile.Handcart, null, "shop.platform.handcart.mechanic"),
        new(PlatformProfile.Bicycle, "HIST-02", "shop.platform.bicycle.mechanic"),
        new(PlatformProfile.Scooter, "HIST-03", "shop.platform.scooter.mechanic"),
        new(PlatformProfile.Car, null, "shop.platform.car.mechanic", RequiredGlobalReputation: 10),
        new(PlatformProfile.Kombi, null, "shop.platform.kombi.mechanic", RequiredGlobalReputation: 0, RequiredClientId: "city_hall", RequiredClientReputation: 20),
        new(PlatformProfile.Pickup, null, "shop.platform.pickup.mechanic", RequiredGlobalReputation: 40),
        new(PlatformProfile.Van, null, "shop.platform.van.mechanic", RequiredGlobalReputation: 60),
    ];

    public static ShopPlatformCatalogEntry GetPlatformEntry(LocomotionMode mode) =>
        Platforms.FirstOrDefault(p => p.Profile.Mode == mode)
        ?? throw new ArgumentOutOfRangeException(nameof(mode), mode, "Platform is not offered in the fleet shop.");
}

/// <summary>Manages MVP shop purchases, reputation discounts, XP skill tracks, and free starter recovery.</summary>
public sealed class FleetProgressionManager
{
    private readonly HashSet<LocomotionMode> _ownedPlatforms = [LocomotionMode.OnFoot, LocomotionMode.Handcart];
    private readonly HashSet<string> _ownedSensorUpgrades = new(StringComparer.Ordinal);
    private readonly Dictionary<SkillTrackKind, int> _skillRanks = new();

    public PlatformProfile ActivePlatform { get; private set; } = PlatformProfile.Handcart;
    public IReadOnlySet<LocomotionMode> OwnedPlatforms => _ownedPlatforms;
    public IReadOnlySet<string> OwnedSensorUpgrades => _ownedSensorUpgrades;

    public FleetProgressionManager()
    {
        foreach (SkillTrackKind track in Enum.GetValues<SkillTrackKind>())
            _skillRanks[track] = 0;
    }

    public static double ComputeReputationDiscountFraction(int globalReputation) =>
        Math.Clamp(Math.Max(0, globalReputation) / 1000.0, 0.0, FleetShopCatalog.MaxReputationDiscountFraction);

    public static double ComputeClientReputationDiscountFraction(
        CareerProgressionState progression,
        string? clientId = null)
    {
        ArgumentNullException.ThrowIfNull(progression);
        var effectiveRep = Math.Max(
            progression.GlobalReputation,
            string.IsNullOrWhiteSpace(clientId) ? 0 : progression.GetClientReputation(clientId));
        return ComputeReputationDiscountFraction(effectiveRep);
    }

    public int GetSkillRank(SkillTrackKind track) =>
        _skillRanks.TryGetValue(track, out var rank) ? rank : 0;

    public int GetTotalEarnedSkillPoints(CareerProgressionState progression)
    {
        ArgumentNullException.ThrowIfNull(progression);
        return Math.Max(0, progression.TotalXp) / FleetShopCatalog.XpPerSkillPoint;
    }

    public int GetSpentSkillPoints() => _skillRanks.Values.Sum();

    public int GetAvailableSkillPoints(CareerProgressionState progression)
    {
        ArgumentNullException.ThrowIfNull(progression);
        return Math.Max(0, GetTotalEarnedSkillPoints(progression) - GetSpentSkillPoints());
    }

    public double GetDefensiveDrivingReductionFraction() => 0.10 * GetSkillRank(SkillTrackKind.DefensiveDriving);

    public double GetNavigationEfficiencyFraction() => 0.10 * GetSkillRank(SkillTrackKind.Navigation);

    public double GetCalibrationFeeReductionFraction() => 0.10 * GetSkillRank(SkillTrackKind.Calibration);

    public double GetProcessingTimeReductionFraction() => 0.10 * GetSkillRank(SkillTrackKind.Processing);

    public double GetNegotiationFeeDiscountFraction() => 0.10 * GetSkillRank(SkillTrackKind.Negotiation);

    public double GetNightShiftEfficiencyFraction() => 0.10 * GetSkillRank(SkillTrackKind.NightShift);

    public IReadOnlyList<SkillTrackBenefitSummary> GetSkillTreeInspection()
    {
        return
        [
            new(SkillTrackKind.DefensiveDriving, GetSkillRank(SkillTrackKind.DefensiveDriving), GetDefensiveDrivingReductionFraction(), "skill.defensive_driving.impact_fine_reduction", "skill.guarantee.no_qc_mutation"),
            new(SkillTrackKind.Navigation, GetSkillRank(SkillTrackKind.Navigation), GetNavigationEfficiencyFraction(), "skill.navigation.autonomy_efficiency", "skill.guarantee.no_qc_mutation"),
            new(SkillTrackKind.Calibration, GetSkillRank(SkillTrackKind.Calibration), GetCalibrationFeeReductionFraction(), "skill.calibration.fee_reduction", "skill.guarantee.no_qc_mutation"),
            new(SkillTrackKind.Processing, GetSkillRank(SkillTrackKind.Processing), GetProcessingTimeReductionFraction(), "skill.processing.time_reduction", "skill.guarantee.no_qc_mutation"),
            new(SkillTrackKind.Negotiation, GetSkillRank(SkillTrackKind.Negotiation), GetNegotiationFeeDiscountFraction(), "skill.negotiation.service_discount", "skill.guarantee.no_qc_mutation"),
            new(SkillTrackKind.NightShift, GetSkillRank(SkillTrackKind.NightShift), GetNightShiftEfficiencyFraction(), "skill.night_shift.night_stamina_bonus", "skill.guarantee.no_qc_mutation"),
        ];
    }

    public static bool VerifySkillsNeverAlterQualityMetrics(
        QualityReport baselineReport,
        QualityReport reportWithSkills)
    {
        ArgumentNullException.ThrowIfNull(baselineReport);
        ArgumentNullException.ThrowIfNull(reportWithSkills);

        if (baselineReport.Passed != reportWithSkills.Passed ||
            baselineReport.UniqueSampleCount != reportWithSkills.UniqueSampleCount ||
            baselineReport.ValidSampleCount != reportWithSkills.ValidSampleCount ||
            baselineReport.MatchedFeatureCount != reportWithSkills.MatchedFeatureCount ||
            baselineReport.MatchedCheckpointCount != reportWithSkills.MatchedCheckpointCount ||
            baselineReport.Metrics.Count != reportWithSkills.Metrics.Count)
        {
            return false;
        }

        for (var i = 0; i < baselineReport.Metrics.Count; i++)
        {
            var a = baselineReport.Metrics[i];
            var b = reportWithSkills.Metrics[i];
            if (!string.Equals(a.Metric, b.Metric, StringComparison.Ordinal) ||
                a.Status != b.Status ||
                a.BooleanValue != b.BooleanValue)
            {
                return false;
            }

            if (a.Value.HasValue != b.Value.HasValue)
                return false;
            if (a.Value.HasValue && Math.Abs(a.Value.Value - b.Value!.Value) > 1e-12)
                return false;
        }

        return true;
    }

    public PlatformPurchaseQuote QuotePlatformPurchase(
        LocomotionMode mode,
        CareerProgressionState progression,
        IReadOnlySet<string> completedMissions)
    {
        ArgumentNullException.ThrowIfNull(progression);
        ArgumentNullException.ThrowIfNull(completedMissions);

        var entry = FleetShopCatalog.GetPlatformEntry(mode);
        var discountFraction = ComputeReputationDiscountFraction(progression.GlobalReputation);
        var basePrice = entry.Profile.PriceCoords;
        var discountCoords = (int)Math.Round(basePrice * discountFraction, MidpointRounding.AwayFromZero);
        var finalPrice = Math.Max(0, basePrice - discountCoords);
        var alreadyOwned = _ownedPlatforms.Contains(mode);

        var storyUnlocked = entry.RequiredStoryMissionId is null
            || completedMissions.Contains(entry.RequiredStoryMissionId)
            || progression.HasStoryUnlock(entry.RequiredStoryMissionId);

        if (!storyUnlocked)
        {
            return new PlatformPurchaseQuote(
                Unlocked: false,
                AlreadyOwned: alreadyOwned,
                Profile: entry.Profile,
                BasePriceCoords: basePrice,
                DiscountFraction: discountFraction,
                DiscountCoords: discountCoords,
                FinalPriceCoords: finalPrice,
                LockReasonKey: "shop.error.story_prerequisite_missing");
        }

        if (entry.RequiredGlobalReputation > 0 &&
            progression.GlobalReputation < entry.RequiredGlobalReputation)
        {
            return new PlatformPurchaseQuote(
                Unlocked: false,
                AlreadyOwned: alreadyOwned,
                Profile: entry.Profile,
                BasePriceCoords: basePrice,
                DiscountFraction: discountFraction,
                DiscountCoords: discountCoords,
                FinalPriceCoords: finalPrice,
                LockReasonKey: "shop.error.reputation_prerequisite_missing");
        }

        if (!string.IsNullOrWhiteSpace(entry.RequiredClientId) &&
            entry.RequiredClientReputation > 0 &&
            progression.GetClientReputation(entry.RequiredClientId) < entry.RequiredClientReputation)
        {
            return new PlatformPurchaseQuote(
                Unlocked: false,
                AlreadyOwned: alreadyOwned,
                Profile: entry.Profile,
                BasePriceCoords: basePrice,
                DiscountFraction: discountFraction,
                DiscountCoords: discountCoords,
                FinalPriceCoords: finalPrice,
                LockReasonKey: "shop.error.client_reputation_prerequisite_missing");
        }

        return new PlatformPurchaseQuote(
            Unlocked: true,
            AlreadyOwned: alreadyOwned,
            Profile: entry.Profile,
            BasePriceCoords: basePrice,
            DiscountFraction: discountFraction,
            DiscountCoords: discountCoords,
            FinalPriceCoords: finalPrice,
            LockReasonKey: null);
    }

    public bool TryPurchasePlatform(
        string transactionId,
        double timestampSeconds,
        LocomotionMode mode,
        CareerLedger ledger,
        CareerProgressionState progression,
        IReadOnlySet<string> completedMissions,
        out LedgerOperationResult? ledgerResult,
        out PlatformPurchaseQuote quote)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(progression);
        ArgumentNullException.ThrowIfNull(completedMissions);

        quote = QuotePlatformPurchase(mode, progression, completedMissions);
        ledgerResult = null;

        if (!quote.Unlocked || quote.AlreadyOwned)
            return false;

        var entry = FleetShopCatalog.GetPlatformEntry(mode);
        ledgerResult = ledger.TryDebitPurchaseOrService(
            transactionId,
            $"shop_platform:{entry.Profile.Id}",
            timestampSeconds,
            quote.FinalPriceCoords,
            "shop_platform",
            entry.MechanicSummaryKey);

        if (!ledgerResult.Succeeded)
            return false;

        _ownedPlatforms.Add(mode);
        ActivePlatform = entry.Profile;
        return true;
    }

    public bool TryEquipOwnedPlatform(LocomotionMode mode)
    {
        if (!_ownedPlatforms.Contains(mode))
            return false;

        ActivePlatform = PlatformProfile.ForMode(mode);
        return true;
    }

    public SensorUpgradeQuote QuoteSensorUpgrade(
        string sensorUpgradeId,
        int basePriceCoords,
        CareerProgressionState progression,
        bool requiresLidarSlot = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sensorUpgradeId);
        ArgumentNullException.ThrowIfNull(progression);
        if (basePriceCoords < 0)
            throw new ArgumentOutOfRangeException(nameof(basePriceCoords), "Base price cannot be negative.");

        var isLidar = requiresLidarSlot || sensorUpgradeId.Contains("lidar", StringComparison.OrdinalIgnoreCase);
        var requiredLevel = isLidar ? 4 : 0;
        var discountFraction = ComputeReputationDiscountFraction(progression.GlobalReputation);
        var discountCoords = (int)Math.Round(basePriceCoords * discountFraction, MidpointRounding.AwayFromZero);
        var finalPrice = Math.Max(0, basePriceCoords - discountCoords);

        if (ActivePlatform.Level < requiredLevel)
        {
            return new SensorUpgradeQuote(
                Eligible: false,
                SensorUpgradeId: sensorUpgradeId,
                RequiredPlatformLevel: requiredLevel,
                CurrentPlatformLevel: ActivePlatform.Level,
                BasePriceCoords: basePriceCoords,
                DiscountFraction: discountFraction,
                FinalPriceCoords: finalPrice,
                RejectionReasonKey: "shop.error.lidar_requires_level_4");
        }

        return new SensorUpgradeQuote(
            Eligible: true,
            SensorUpgradeId: sensorUpgradeId,
            RequiredPlatformLevel: requiredLevel,
            CurrentPlatformLevel: ActivePlatform.Level,
            BasePriceCoords: basePriceCoords,
            DiscountFraction: discountFraction,
            FinalPriceCoords: finalPrice,
            RejectionReasonKey: null);
    }

    public bool TryPurchaseSensorUpgrade(
        string transactionId,
        double timestampSeconds,
        string sensorUpgradeId,
        int basePriceCoords,
        CareerLedger ledger,
        CareerProgressionState progression,
        bool requiresLidarSlot,
        out SensorUpgradeQuote quote,
        out LedgerOperationResult? ledgerResult)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);
        ArgumentNullException.ThrowIfNull(ledger);

        quote = QuoteSensorUpgrade(sensorUpgradeId, basePriceCoords, progression, requiresLidarSlot);
        ledgerResult = null;
        if (!quote.Eligible || _ownedSensorUpgrades.Contains(sensorUpgradeId))
            return false;

        ledgerResult = ledger.TryDebitPurchaseOrService(
            transactionId,
            $"shop_sensor:{sensorUpgradeId}",
            timestampSeconds,
            quote.FinalPriceCoords,
            "shop_sensor",
            $"shop.sensor.{sensorUpgradeId}");

        if (!ledgerResult.Succeeded)
            return false;

        _ownedSensorUpgrades.Add(sensorUpgradeId);
        return true;
    }

    public bool TryUnlockSkillRank(
        SkillTrackKind track,
        CareerProgressionState progression,
        out int newRank,
        out string? reasonKey)
    {
        ArgumentNullException.ThrowIfNull(progression);

        var current = GetSkillRank(track);
        if (current >= FleetShopCatalog.MaxSkillRank)
        {
            newRank = current;
            reasonKey = "progression.error.track_maxed";
            return false;
        }

        if (GetAvailableSkillPoints(progression) <= 0)
        {
            newRank = current;
            reasonKey = "progression.error.insufficient_skill_points";
            return false;
        }

        newRank = current + 1;
        _skillRanks[track] = newRank;
        reasonKey = null;
        return true;
    }

    public SensorUpgradeQuote QuoteGradeUpgrade(
        string sensorUpgradeId,
        string targetGrade,
        int basePriceCoords,
        CareerProgressionState progression,
        bool requiresLidarSlot = false,
        int minimumPlatformLevel = 0,
        string? clientId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sensorUpgradeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetGrade);
        ArgumentNullException.ThrowIfNull(progression);
        if (basePriceCoords < 0)
            throw new ArgumentOutOfRangeException(nameof(basePriceCoords), "Base price cannot be negative.");
        if (minimumPlatformLevel < 0)
            throw new ArgumentOutOfRangeException(nameof(minimumPlatformLevel), "Minimum platform level cannot be negative.");

        var isLidar = requiresLidarSlot || sensorUpgradeId.Contains("lidar", StringComparison.OrdinalIgnoreCase);
        var requiredLevel = Math.Max(minimumPlatformLevel, isLidar ? 4 : 0);
        var discountFraction = ComputeClientReputationDiscountFraction(progression, clientId);
        var discountCoords = (int)Math.Round(basePriceCoords * discountFraction, MidpointRounding.AwayFromZero);
        var finalPrice = Math.Max(0, basePriceCoords - discountCoords);

        if (ActivePlatform.Level < requiredLevel)
        {
            var reasonKey = isLidar && requiredLevel >= 4
                ? "shop.error.lidar_requires_level_4"
                : "shop.error.platform_level_insufficient";
            return new SensorUpgradeQuote(
                Eligible: false,
                SensorUpgradeId: sensorUpgradeId,
                RequiredPlatformLevel: requiredLevel,
                CurrentPlatformLevel: ActivePlatform.Level,
                BasePriceCoords: basePriceCoords,
                DiscountFraction: discountFraction,
                FinalPriceCoords: finalPrice,
                RejectionReasonKey: reasonKey,
                TargetGrade: targetGrade);
        }

        return new SensorUpgradeQuote(
            Eligible: true,
            SensorUpgradeId: sensorUpgradeId,
            RequiredPlatformLevel: requiredLevel,
            CurrentPlatformLevel: ActivePlatform.Level,
            BasePriceCoords: basePriceCoords,
            DiscountFraction: discountFraction,
            FinalPriceCoords: finalPrice,
            RejectionReasonKey: null,
            TargetGrade: targetGrade);
    }

    public StarterRecoveryResult RecoverStarterKitAndEvaluateSubsidy(
        string transactionId,
        double timestampSeconds,
        CareerLedger ledger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);
        ArgumentNullException.ThrowIfNull(ledger);

        var ledgerResult = ledger.RecoverStarterKitFree(transactionId, timestampSeconds);
        _ownedPlatforms.Add(LocomotionMode.Handcart);
        ActivePlatform = PlatformProfile.Handcart;

        return new StarterRecoveryResult(
            LedgerResult: ledgerResult,
            ActiveMode: ActivePlatform.Mode,
            LowBalanceSubsidyActive: ledgerResult.BalanceCoords < CareerLedger.LowBalanceSubsidyThresholdCoords);
    }

    public FleetProgressionSnapshot ExportSnapshot() =>
        new(
            SchemaVersion: 1,
            ActivePlatformId: ActivePlatform.Id,
            OwnedPlatforms: _ownedPlatforms.OrderBy(m => (int)m).ToArray(),
            OwnedSensorUpgrades: _ownedSensorUpgrades.OrderBy(u => u, StringComparer.Ordinal).ToArray(),
            SkillRanks: new Dictionary<SkillTrackKind, int>(_skillRanks));

    public static FleetProgressionManager FromSnapshot(FleetProgressionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var manager = new FleetProgressionManager();
        foreach (var mode in snapshot.OwnedPlatforms)
            manager._ownedPlatforms.Add(mode);
        foreach (var upgrade in snapshot.OwnedSensorUpgrades)
            manager._ownedSensorUpgrades.Add(upgrade);
        foreach (var (track, rank) in snapshot.SkillRanks)
        {
            if (rank < 0 || rank > FleetShopCatalog.MaxSkillRank)
                throw new ArgumentOutOfRangeException(nameof(snapshot), $"Invalid rank {rank} for {track}.");
            manager._skillRanks[track] = rank;
        }

        var active = FleetShopCatalog.Platforms.FirstOrDefault(p => string.Equals(p.Profile.Id, snapshot.ActivePlatformId, StringComparison.Ordinal))?.Profile
            ?? PlatformProfile.Handcart;
        manager._ownedPlatforms.Add(active.Mode);
        manager.ActivePlatform = active;
        return manager;
    }
}

