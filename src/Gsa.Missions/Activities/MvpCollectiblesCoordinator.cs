using Gsa.Core.Geometry;
using Gsa.Missions.Economy;
using Gsa.Missions.Presentation;

namespace Gsa.Missions.Activities;

public sealed record GoldenEggSpot(
    string EggId,
    int DayIndex,
    int SlotIndex,
    Vector3d PositionEnu,
    int RewardCoords,
    bool TemporarilyBlocked);

public sealed record SurveyLandmarkDefinition(
    string LandmarkId,
    string RegionId,
    string TitleKey,
    Vector3d PositionEnu,
    bool AvailableInMvp,
    string GeopediaEntryId,
    int RewardCoords);

public sealed record ProjectionCardDefinition(
    string CardId,
    string ProjectionNameKey,
    string ApplicationKey,
    string DistortionKey,
    Vector3d PositionEnu,
    string GeopediaEntryId);

public sealed record CollectibleStateSnapshot(
    int SchemaVersion,
    IReadOnlyList<string> CollectedEggIds,
    IReadOnlyList<string> OccupiedLandmarkIds,
    IReadOnlyList<string> CollectedCardIds);

public sealed record CollectibleRewardOutcome(
    bool Succeeded,
    bool AlreadyClaimed,
    string ItemId,
    int CoordsCredited,
    string? UnlockedGeopediaId,
    string? ReasonKey = null);

/// <summary>Manages MVP golden eggs, 8-landmark catalog (2 MVP active), and 6 projection cards with idempotent rewards & save persistence.</summary>
public sealed class MvpCollectiblesCoordinator
{
    public const int MaxDailyEggs = 5;
    public const int EggRewardCoords = 10;
    public const double LandmarkOccupationSeconds = 5.0;
    public const double LandmarkOccupationRadiusMetres = 4.0;
    public const int LandmarkRewardCoords = 25;

    private static readonly Vector3d[] EggCandidateOffsetsEnu =
    [
        new(500045.0, 7400035.0, 100.0),
        new(500155.0, 7400080.0, 100.0),
        new(500265.0, 7400145.0, 100.0),
        new(500340.0, 7400270.0, 100.0),
        new(500410.0, 7400380.0, 100.0),
        new(500110.0, 7400420.0, 100.0),
        new(500290.0, 7400310.0, 100.0),
    ];

    public static IReadOnlyList<SurveyLandmarkDefinition> AllEightLandmarksCatalog { get; } =
    [
        new("landmark_old_center_zero_stone", "old_center", "collectible.landmark.zero_stone.title", new(500250.0, 7400250.0, 100.5), true, "geopedia_landmark_zero_stone", LandmarkRewardCoords),
        new("landmark_old_center_clock_tower", "old_center", "collectible.landmark.clock_tower.title", new(500120.0, 7400180.0, 100.5), true, "geopedia_landmark_clock_tower", LandmarkRewardCoords),
        new("landmark_port_customs_pillar", "porto", "collectible.landmark.port_pillar.title", new(500680.0, 7400120.0, 100.0), false, "geopedia_datum_projection", LandmarkRewardCoords),
        new("landmark_port_lighthouse_bench", "porto", "collectible.landmark.port_lighthouse.title", new(500760.0, 7400040.0, 101.0), false, "geopedia_heights", LandmarkRewardCoords),
        new("landmark_waterfront_tide_gauge", "orla", "collectible.landmark.waterfront_tide.title", new(500150.0, 7399720.0, 99.5), false, "geopedia_heights", LandmarkRewardCoords),
        new("landmark_waterfront_promenade_obelisk", "orla", "collectible.landmark.waterfront_obelisk.title", new(500420.0, 7399680.0, 100.0), false, "geopedia_datum_projection", LandmarkRewardCoords),
        new("landmark_hills_triangulation_beacon", "serra", "collectible.landmark.hills_beacon.title", new(500460.0, 7400820.0, 145.0), false, "geopedia_heights", LandmarkRewardCoords),
        new("landmark_hills_viaduct_monolith", "serra", "collectible.landmark.hills_monolith.title", new(500610.0, 7400950.0, 142.0), false, "geopedia_control_vs_check", LandmarkRewardCoords),
    ];

    public static IReadOnlyList<ProjectionCardDefinition> AllSixProjectionCards { get; } =
    [
        new("card_mercator", "collectible.card.mercator.title", "collectible.card.mercator.app", "collectible.card.mercator.distortion", new(500075.0, 7400125.0, 100.0), "geopedia_card_mercator"),
        new("card_robinson", "collectible.card.robinson.title", "collectible.card.robinson.app", "collectible.card.robinson.distortion", new(500190.0, 7400220.0, 100.0), "geopedia_card_robinson"),
        new("card_gall_peters", "collectible.card.gall_peters.title", "collectible.card.gall_peters.app", "collectible.card.gall_peters.distortion", new(500315.0, 7400190.0, 100.0), "geopedia_card_gall_peters"),
        new("card_utm", "collectible.card.utm.title", "collectible.card.utm.app", "collectible.card.utm.distortion", new(500405.0, 7400310.0, 100.0), "geopedia_card_utm"),
        new("card_lambert", "collectible.card.lambert.title", "collectible.card.lambert.app", "collectible.card.lambert.distortion", new(500260.0, 7400430.0, 100.0), "geopedia_card_lambert"),
        new("card_stereographic", "collectible.card.stereographic.title", "collectible.card.stereographic.app", "collectible.card.stereographic.distortion", new(500130.0, 7400385.0, 100.0), "geopedia_card_stereographic"),
    ];

    private readonly HashSet<string> _collectedEggIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _occupiedLandmarkIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _collectedCardIds = new(StringComparer.Ordinal);

    public IReadOnlySet<string> CollectedEggIds => _collectedEggIds;
    public IReadOnlySet<string> OccupiedLandmarkIds => _occupiedLandmarkIds;
    public IReadOnlySet<string> CollectedCardIds => _collectedCardIds;

    public MvpCollectiblesCoordinator(CollectibleStateSnapshot? restored = null)
    {
        if (restored is null)
            return;

        foreach (var id in restored.CollectedEggIds) _collectedEggIds.Add(id);
        foreach (var id in restored.OccupiedLandmarkIds) _occupiedLandmarkIds.Add(id);
        foreach (var id in restored.CollectedCardIds) _collectedCardIds.Add(id);
    }

    public IReadOnlyList<GoldenEggSpot> GetActiveDailyEggs(
        int dayIndex,
        ISet<int>? temporarilyBlockedSlots = null)
    {
        if (dayIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(dayIndex));

        var list = new List<GoldenEggSpot>(MaxDailyEggs);
        for (var slot = 0; slot < MaxDailyEggs; slot++)
        {
            var eggId = $"egg_day_{dayIndex:000}_slot_{slot:00}";
            if (_collectedEggIds.Contains(eggId))
                continue;

            var idx = (dayIndex * 3 + slot) % EggCandidateOffsetsEnu.Length;
            var blocked = temporarilyBlockedSlots?.Contains(slot) == true;
            list.Add(new GoldenEggSpot(eggId, dayIndex, slot, EggCandidateOffsetsEnu[idx], EggRewardCoords, blocked));
        }

        return Array.AsReadOnly(list.ToArray());
    }

    public CollectibleRewardOutcome CollectGoldenEgg(
        GoldenEggSpot spot,
        double timestampSeconds,
        CareerLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(spot);
        ArgumentNullException.ThrowIfNull(ledger);

        if (spot.TemporarilyBlocked)
            return new CollectibleRewardOutcome(false, false, spot.EggId, 0, null, "collectible.area_temporarily_blocked");

        if (!_collectedEggIds.Add(spot.EggId))
            return new CollectibleRewardOutcome(true, true, spot.EggId, 0, null, "collectible.already_claimed");

        var res = ledger.RecordActivityReward(
            $"collectible_egg:{spot.EggId}",
            spot.EggId,
            timestampSeconds,
            spot.RewardCoords,
            "collectible_egg",
            "collectible.egg.reward");

        return new CollectibleRewardOutcome(true, res.Status == LedgerCommandStatus.IdempotentNoOp, spot.EggId, res.AppliedDeltaCoords, null);
    }

    public CollectibleRewardOutcome OccupySurveyLandmark(
        string landmarkId,
        double occupationDurationSeconds,
        Vector3d playerPositionEnu,
        double timestampSeconds,
        CareerLedger ledger,
        GeopediaCatalogCoordinator geopedia,
        bool areaTemporarilyBlocked = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(landmarkId);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(geopedia);

        var landmark = AllEightLandmarksCatalog.FirstOrDefault(l => string.Equals(l.LandmarkId, landmarkId, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Unknown survey landmark '{landmarkId}'.", nameof(landmarkId));

        if (!landmark.AvailableInMvp)
            return new CollectibleRewardOutcome(false, false, landmarkId, 0, null, "collectible.region_locked_until_expansion");

        if (areaTemporarilyBlocked)
            return new CollectibleRewardOutcome(false, false, landmarkId, 0, null, "collectible.area_temporarily_blocked");

        var horizontalDist = Math.Sqrt(
            Math.Pow(playerPositionEnu.X - landmark.PositionEnu.X, 2) +
            Math.Pow(playerPositionEnu.Y - landmark.PositionEnu.Y, 2));
        if (horizontalDist > LandmarkOccupationRadiusMetres)
            return new CollectibleRewardOutcome(false, false, landmarkId, 0, null, "collectible.landmark.too_far");

        if (occupationDurationSeconds + 1e-9 < LandmarkOccupationSeconds)
            return new CollectibleRewardOutcome(false, false, landmarkId, 0, null, "collectible.landmark.occupation_too_short");

        if (!_occupiedLandmarkIds.Add(landmarkId))
        {
            geopedia.UnlockByEntryId(landmark.GeopediaEntryId);
            return new CollectibleRewardOutcome(true, true, landmarkId, 0, null, "collectible.already_claimed");
        }

        geopedia.UnlockByEntryId(landmark.GeopediaEntryId);
        var res = ledger.RecordActivityReward(
            $"collectible_landmark:{landmarkId}",
            landmarkId,
            timestampSeconds,
            landmark.RewardCoords,
            "collectible_landmark",
            landmark.TitleKey);

        return new CollectibleRewardOutcome(
            true,
            res.Status == LedgerCommandStatus.IdempotentNoOp,
            landmarkId,
            res.AppliedDeltaCoords,
            landmark.GeopediaEntryId);
    }

    public CollectibleRewardOutcome CollectProjectionCard(
        string cardId,
        GeopediaCatalogCoordinator geopedia,
        bool areaTemporarilyBlocked = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cardId);
        ArgumentNullException.ThrowIfNull(geopedia);

        var card = AllSixProjectionCards.FirstOrDefault(c => string.Equals(c.CardId, cardId, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Unknown projection card '{cardId}'.", nameof(cardId));

        if (areaTemporarilyBlocked)
            return new CollectibleRewardOutcome(false, false, cardId, 0, null, "collectible.area_temporarily_blocked");

        if (!_collectedCardIds.Add(cardId))
        {
            geopedia.UnlockByEntryId(card.GeopediaEntryId);
            return new CollectibleRewardOutcome(true, true, cardId, 0, null, "collectible.already_claimed");
        }

        geopedia.UnlockByEntryId(card.GeopediaEntryId);
        return new CollectibleRewardOutcome(true, false, cardId, 0, card.GeopediaEntryId);
    }

    public CollectibleStateSnapshot ExportSnapshot() =>
        new(
            SchemaVersion: 1,
            CollectedEggIds: _collectedEggIds.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            OccupiedLandmarkIds: _occupiedLandmarkIds.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            CollectedCardIds: _collectedCardIds.OrderBy(id => id, StringComparer.Ordinal).ToArray());
}

