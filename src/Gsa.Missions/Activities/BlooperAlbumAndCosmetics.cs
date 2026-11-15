using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Camera;
using Gsa.Missions.Economy;
using Gsa.Missions.Office;
using Gsa.Missions.Quality;

namespace Gsa.Missions.Activities;

public enum BlooperCategory
{
    PedestrianPose = 0,
    VehicleMishap = 1,
    CuriousSign = 2,
    AnimalCameo = 3,
    WeatherPhotobomb = 4,
    SelfieBlooper = 5,
}

public enum CosmeticKind
{
    VehiclePaint = 0,
    VehicleSticker = 1,
    VehicleHorn = 2,
    GimbalinaCostume = 3,
}

public enum GimbalinaCostumeId
{
    SurveyorVest = 0,
    HardHatSafety = 1,
    RainPoncho = 2,
    NightReflective = 3,
    FestiveCap = 4,
}

public enum AlbumAddStatus
{
    Added = 0,
    ReplacementConfirmationRequired = 1,
    ReplacedExisting = 2,
    AlreadyPresent = 3,
}

public sealed record CosmeticItemDefinition(
    string ItemId,
    CosmeticKind Kind,
    string TitleKey,
    int PriceCoords,
    string VisualTag,
    GimbalinaCostumeId? CostumeId = null);

public sealed record BlooperAlbumEntry(
    string EntryId,
    string SampleId,
    string TitleKey,
    BlooperCategory Category,
    string RegionId,
    double CapturedAtSeconds,
    int ThumbnailWidth,
    int ThumbnailHeight,
    string ThumbnailSha256,
    bool PrivacyMasked,
    bool IsFavorite = false);

public sealed record AlbumAddResult(
    AlbumAddStatus Status,
    BlooperAlbumEntry? Entry,
    string? ReplacedEntryId,
    string MessageKey);

public sealed record BlooperLocalExportResult(
    string EntryId,
    int Width,
    int Height,
    string BeforeThumbnailSha256,
    string AfterThumbnailSha256,
    byte[] RedactedRgbBytes,
    bool PrivacyCompliant,
    IReadOnlyList<SyntheticSensitiveRegion> DetectedRegions);

public sealed record EquippedCosmeticsState(
    string PaintId,
    string StickerId,
    string HornId,
    GimbalinaCostumeId GimbalinaCostume);

public sealed record BlooperAndCosmeticSnapshot(
    int SchemaVersion,
    IReadOnlyList<BlooperAlbumEntry> AlbumEntries,
    IReadOnlyList<string> OwnedCosmeticIds,
    EquippedCosmeticsState Equipped);

public static class BlooperAndCosmeticCatalog
{
    public const int MaxAlbumEntries = 30;
    public const string DefaultPaintId = "paint_geodetic_yellow";
    public const string DefaultStickerId = "sticker_compass_rose";
    public const string DefaultHornId = "horn_standard";
    public const string CackleHornId = "horn_cackle";

    public static IReadOnlyList<CosmeticItemDefinition> AllCosmetics { get; } =
    [
        // 3 Vehicle Paints
        new("paint_geodetic_yellow", CosmeticKind.VehiclePaint, "cosmetic.paint.geodetic_yellow", 0, "yellow"),
        new("paint_cobalt_fleet", CosmeticKind.VehiclePaint, "cosmetic.paint.cobalt_fleet", 60, "cobalt"),
        new("paint_terracotta_stripe", CosmeticKind.VehiclePaint, "cosmetic.paint.terracotta_stripe", 90, "terracotta"),

        // 3 Vehicle Stickers
        new("sticker_compass_rose", CosmeticKind.VehicleSticker, "cosmetic.sticker.compass_rose", 0, "compass"),
        new("sticker_gnss_badge", CosmeticKind.VehicleSticker, "cosmetic.sticker.gnss_badge", 40, "gnss"),
        new("sticker_chicken_mascot", CosmeticKind.VehicleSticker, "cosmetic.sticker.chicken_mascot", 75, "gimbalina"),

        // 3 Vehicle Horns (including cackle / cacarejo)
        new("horn_standard", CosmeticKind.VehicleHorn, "cosmetic.horn.standard", 0, "beep"),
        new("horn_cackle", CosmeticKind.VehicleHorn, "cosmetic.horn.cackle", 45, "cackle"),
        new("horn_air_chime", CosmeticKind.VehicleHorn, "cosmetic.horn.air_chime", 70, "chime"),

        // Gimbalina Costumes
        new("costume_surveyor_vest", CosmeticKind.GimbalinaCostume, "cosmetic.costume.surveyor_vest", 0, "vest", GimbalinaCostumeId.SurveyorVest),
        new("costume_hard_hat", CosmeticKind.GimbalinaCostume, "cosmetic.costume.hard_hat", 35, "hardhat", GimbalinaCostumeId.HardHatSafety),
        new("costume_rain_poncho", CosmeticKind.GimbalinaCostume, "cosmetic.costume.rain_poncho", 50, "poncho", GimbalinaCostumeId.RainPoncho),
        new("costume_night_reflective", CosmeticKind.GimbalinaCostume, "cosmetic.costume.night_reflective", 65, "reflective", GimbalinaCostumeId.NightReflective),
        new("costume_festive_cap", CosmeticKind.GimbalinaCostume, "cosmetic.costume.festive_cap", 80, "festive", GimbalinaCostumeId.FestiveCap),
    ];

    public static CosmeticItemDefinition GetById(string itemId) =>
        AllCosmetics.FirstOrDefault(c => string.Equals(c.ItemId, itemId, StringComparison.Ordinal))
        ?? throw new ArgumentOutOfRangeException(nameof(itemId), itemId, "Cosmetic item not found in catalog.");
}

/// <summary>Manages the 30-photo blooper album with privacy-masked export and non-scientific cosmetics.</summary>
public sealed class BlooperAlbumAndCosmeticsCoordinator
{
    private readonly List<BlooperAlbumEntry> _entries = [];
    private readonly HashSet<string> _ownedCosmeticIds = new(
        [
            BlooperAndCosmeticCatalog.DefaultPaintId,
            BlooperAndCosmeticCatalog.DefaultStickerId,
            BlooperAndCosmeticCatalog.DefaultHornId,
            "costume_surveyor_vest",
        ],
        StringComparer.Ordinal);

    public IReadOnlyList<BlooperAlbumEntry> Entries => _entries;
    public IReadOnlySet<string> OwnedCosmeticIds => _ownedCosmeticIds;
    public string EquippedPaintId { get; private set; } = BlooperAndCosmeticCatalog.DefaultPaintId;
    public string EquippedStickerId { get; private set; } = BlooperAndCosmeticCatalog.DefaultStickerId;
    public string EquippedHornId { get; private set; } = BlooperAndCosmeticCatalog.DefaultHornId;
    public GimbalinaCostumeId EquippedGimbalinaCostume { get; private set; } = GimbalinaCostumeId.SurveyorVest;

    public AlbumAddResult TrySaveBlooper(
        string entryId,
        CameraObservationSample sample,
        string titleKey,
        BlooperCategory category,
        string regionId,
        bool confirmReplacement = false,
        string? replaceEntryId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        ArgumentNullException.ThrowIfNull(sample);
        ArgumentException.ThrowIfNullOrWhiteSpace(titleKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(regionId);

        var existingSameId = _entries.FirstOrDefault(e => string.Equals(e.EntryId, entryId, StringComparison.Ordinal));
        if (existingSameId is not null)
        {
            return new AlbumAddResult(
                AlbumAddStatus.AlreadyPresent,
                existingSameId,
                null,
                "album.status.already_saved");
        }

        // Apply Arcade automatic redaction preview SHA so saved thumbnails never leak unmasked sensitive targets.
        var masked = PrivacyMaskingService.ApplyMasks(
            sample,
            confirmedTargetIds: null,
            mode: ProcessingMode.Arcade,
            out _,
            out _,
            out var afterSha);

        var candidate = new BlooperAlbumEntry(
            EntryId: entryId,
            SampleId: sample.SampleId,
            TitleKey: titleKey,
            Category: category,
            RegionId: regionId,
            CapturedAtSeconds: sample.TimestampSeconds,
            ThumbnailWidth: masked.Thumbnail.Width,
            ThumbnailHeight: masked.Thumbnail.Height,
            ThumbnailSha256: afterSha,
            PrivacyMasked: true);

        if (_entries.Count < BlooperAndCosmeticCatalog.MaxAlbumEntries)
        {
            _entries.Add(candidate);
            return new AlbumAddResult(AlbumAddStatus.Added, candidate, null, "album.status.saved");
        }

        if (!confirmReplacement || string.IsNullOrWhiteSpace(replaceEntryId))
        {
            return new AlbumAddResult(
                AlbumAddStatus.ReplacementConfirmationRequired,
                candidate,
                null,
                "album.prompt.full_confirm_replacement");
        }

        var replaceIndex = _entries.FindIndex(e => string.Equals(e.EntryId, replaceEntryId, StringComparison.Ordinal));
        if (replaceIndex < 0)
        {
            return new AlbumAddResult(
                AlbumAddStatus.ReplacementConfirmationRequired,
                candidate,
                null,
                "album.error.replace_target_not_found");
        }

        _entries[replaceIndex] = candidate;
        return new AlbumAddResult(
            AlbumAddStatus.ReplacedExisting,
            candidate,
            replaceEntryId,
            "album.status.replaced");
    }

    public bool TryRemoveBlooper(string entryId)
    {
        var idx = _entries.FindIndex(e => string.Equals(e.EntryId, entryId, StringComparison.Ordinal));
        if (idx < 0)
            return false;

        _entries.RemoveAt(idx);
        return true;
    }

    public static BlooperLocalExportResult ExportLocalAlbumThumbnail(
        string entryId,
        CameraObservationSample rawSample,
        ProcessingMode mode = ProcessingMode.Technical,
        IReadOnlySet<string>? confirmedTargetIds = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        ArgumentNullException.ThrowIfNull(rawSample);

        var detected = PrivacyMaskingService.DetectSensitiveRegions(rawSample);
        var effectiveConfirmed = confirmedTargetIds ??
            detected.Select(d => d.TargetId).ToHashSet(StringComparer.Ordinal);

        var maskedSample = PrivacyMaskingService.ApplyMasks(
            rawSample,
            effectiveConfirmed,
            mode,
            out var regions,
            out var beforeSha,
            out var afterSha);

        var compliant = regions.All(r => r.AppliedToProduct);
        return new BlooperLocalExportResult(
            EntryId: entryId,
            Width: maskedSample.Thumbnail.Width,
            Height: maskedSample.Thumbnail.Height,
            BeforeThumbnailSha256: beforeSha,
            AfterThumbnailSha256: afterSha,
            RedactedRgbBytes: maskedSample.Thumbnail.CopyRgbBytes(),
            PrivacyCompliant: compliant,
            DetectedRegions: regions);
    }

    public bool TryPurchaseCosmetic(
        string transactionId,
        double timestampSeconds,
        string itemId,
        CareerLedger ledger,
        out LedgerOperationResult? ledgerResult,
        bool autoEquip = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);
        ArgumentNullException.ThrowIfNull(ledger);

        var def = BlooperAndCosmeticCatalog.GetById(itemId);
        if (_ownedCosmeticIds.Contains(def.ItemId))
        {
            ledgerResult = null;
            if (autoEquip)
                TryEquipCosmetic(def.ItemId);
            return false;
        }

        ledgerResult = ledger.TryDebitPurchaseOrService(
            transactionId,
            $"cosmetic:{def.ItemId}",
            timestampSeconds,
            def.PriceCoords,
            "cosmetic",
            def.TitleKey);

        if (!ledgerResult.Succeeded)
            return false;

        _ownedCosmeticIds.Add(def.ItemId);
        if (autoEquip)
            TryEquipCosmetic(def.ItemId);
        return true;
    }

    public bool TryEquipCosmetic(string itemId)
    {
        var def = BlooperAndCosmeticCatalog.GetById(itemId);
        if (!_ownedCosmeticIds.Contains(def.ItemId))
            return false;

        switch (def.Kind)
        {
            case CosmeticKind.VehiclePaint:
                EquippedPaintId = def.ItemId;
                return true;
            case CosmeticKind.VehicleSticker:
                EquippedStickerId = def.ItemId;
                return true;
            case CosmeticKind.VehicleHorn:
                EquippedHornId = def.ItemId;
                return true;
            case CosmeticKind.GimbalinaCostume:
                EquippedGimbalinaCostume = def.CostumeId
                    ?? throw new InvalidOperationException($"Costume '{def.ItemId}' is missing CostumeId.");
                return true;
            default:
                return false;
        }
    }

    public EquippedCosmeticsState GetEquippedState() =>
        new(EquippedPaintId, EquippedStickerId, EquippedHornId, EquippedGimbalinaCostume);

    public static bool VerifyCosmeticScientificInvariance(
        QualityReport baselineReport,
        QualityReport reportWithCosmetics,
        PlatformProfile baselinePlatform,
        PlatformProfile platformWithCosmetics)
    {
        ArgumentNullException.ThrowIfNull(baselineReport);
        ArgumentNullException.ThrowIfNull(reportWithCosmetics);
        ArgumentNullException.ThrowIfNull(baselinePlatform);
        ArgumentNullException.ThrowIfNull(platformWithCosmetics);

        if (!FleetProgressionManager.VerifySkillsNeverAlterQualityMetrics(baselineReport, reportWithCosmetics))
            return false;

        return baselinePlatform == platformWithCosmetics;
    }

    public BlooperAndCosmeticSnapshot ExportSnapshot() =>
        new(
            SchemaVersion: 1,
            AlbumEntries: _entries.ToArray(),
            OwnedCosmeticIds: _ownedCosmeticIds.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            Equipped: GetEquippedState());

    public static BlooperAlbumAndCosmeticsCoordinator FromSnapshot(BlooperAndCosmeticSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.AlbumEntries.Count > BlooperAndCosmeticCatalog.MaxAlbumEntries)
            throw new ArgumentOutOfRangeException(nameof(snapshot), "Album cannot exceed 30 entries.");

        var coordinator = new BlooperAlbumAndCosmeticsCoordinator();
        coordinator._entries.AddRange(snapshot.AlbumEntries);
        foreach (var id in snapshot.OwnedCosmeticIds)
        {
            _ = BlooperAndCosmeticCatalog.GetById(id);
            coordinator._ownedCosmeticIds.Add(id);
        }

        if (coordinator._ownedCosmeticIds.Contains(snapshot.Equipped.PaintId))
            coordinator.EquippedPaintId = snapshot.Equipped.PaintId;
        if (coordinator._ownedCosmeticIds.Contains(snapshot.Equipped.StickerId))
            coordinator.EquippedStickerId = snapshot.Equipped.StickerId;
        if (coordinator._ownedCosmeticIds.Contains(snapshot.Equipped.HornId))
            coordinator.EquippedHornId = snapshot.Equipped.HornId;

        coordinator.EquippedGimbalinaCostume = snapshot.Equipped.GimbalinaCostume;
        return coordinator;
    }
}

