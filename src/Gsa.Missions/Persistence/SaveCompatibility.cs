using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Traffic;
using Gsa.Missions.Activities;
using Gsa.Missions.Economy;
using Gsa.Missions.Office;

namespace Gsa.Missions.Persistence;

public sealed record SaveContentVersions(
    int CatalogVersion,
    int ModelVersion,
    int WorldVersion,
    IReadOnlyList<string> DerivedCacheIds)
{
    public static SaveContentVersions Current => new(2, 1, 1, []);
}

public sealed record SaveCampaignPresentationState(
    IReadOnlyList<string> Platforms, IReadOnlyList<string> Regions, IReadOnlyList<string> Equipment,
    IReadOnlyList<string> AppliedCutscenes, IReadOnlyList<string> SkippedCutscenes,
    IReadOnlyDictionary<string, string> Objectives);

/// <summary>Engine-neutral city session; raw camera observations are not derived caches.</summary>
public sealed record SaveCitySessionState(
    LocomotionSaveSnapshot Mobility,
    FleetProgressionSnapshot Fleet,
    WeatherAndTimeSnapshot Calendar,
    IReadOnlyList<SerializableCameraSample> CameraSamples,
    IReadOnlyList<Vector3d> CoverageSpots,
    IReadOnlyList<string> ObservedTargetIds,
    IReadOnlyList<string> GeopediaEntryIds,
    CollectibleStateSnapshot Collectibles,
    SaveCampaignPresentationState Campaign,
    TerritoryAttentionSnapshot Attention,
    string? ActiveStoryMissionId,
    ProcessingMode OfficeMode,
    bool PrivacyMasksApplied,
    bool RackMisaligned,
    bool CalmCityEnabled,
    bool DisguiseActive,
    ulong NextContractSeed,
    long PhysicsTick,
    long CameraNextSequence = 0);

public sealed record SaveCompatibilityInfo(
    int SourceSchemaVersion,
    bool Migrated,
    bool DerivedCachesInvalidated,
    IReadOnlyList<string> AppliedAliases);

/// <summary>Pure v1-to-v2 conversion. No file IO, reprocessing, payments or custody transitions.</summary>
public static class SaveCompatibility
{
    public static string ResolveContentId(string id) => id switch
    {
        "cart" or "Handcart" => "handcart",
        "OnFoot" => "on_foot",
        "Bicycle" => "bicycle",
        "Scooter" => "scooter",
        "Car" => "car",
        "Kombi" => "kombi",
        "Pickup" => "pickup",
        "Van" => "van",
        "cam_basic" or "cam_initial" or "camera_initial" => "camera-initial",
        "gnss_basic" => "gnss-basic",
        _ => id,
    };

    public static (SaveGameState State, SaveCompatibilityInfo Info) Normalize(SaveGameState state, int sourceSchema)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (sourceSchema is < 1 or > SaveGameManager.CurrentSchemaVersion)
            throw new ArgumentException("Unsupported save schema.", nameof(sourceSchema));
        if (state.Clocks is null || state.Rng is null || state.Ledger is null || state.Progression is null ||
            state.Equipment is null || state.CustodyStore is null || state.Contracts is null ||
            state.Equipment.OwnedEquipmentIds is null || state.Equipment.MountedSlots is null ||
            state.CustodyStore.Entries is null || !Enum.IsDefined(state.Trigger) || state.SaveGeneration < 0 ||
            !double.IsFinite(state.Clocks.SimulationTimeSeconds) || state.Clocks.SimulationTimeSeconds < 0 ||
            !double.IsFinite(state.Clocks.CareerElapsedSeconds) || state.Clocks.CareerElapsedSeconds < 0)
            throw new ArgumentException("Required save fields are missing or invalid.", nameof(state));

        var content = state.Content ?? SaveContentVersions.Current;
        if (content.CatalogVersion is < 1 or > 2 || content.ModelVersion != 1 || content.WorldVersion != 1 || content.DerivedCacheIds is null)
            throw new ArgumentException("Unsupported content, model or world version.", nameof(state));
        var aliases = new HashSet<string>(StringComparer.Ordinal);
        string Resolve(string id)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            var resolved = ResolveContentId(id);
            if (resolved != id) aliases.Add(id + " → " + resolved);
            return resolved;
        }
        var equipment = state.Equipment with
        {
            ActivePlatformId = Resolve(state.Equipment.ActivePlatformId),
            OwnedEquipmentIds = state.Equipment.OwnedEquipmentIds.Select(Resolve).Distinct(StringComparer.Ordinal).ToArray(),
            MountedSlots = state.Equipment.MountedSlots.ToDictionary(p => p.Key, p => Resolve(p.Value), StringComparer.Ordinal),
        };
        var city = state.City;
        if (city is not null)
        {
            if (city.Fleet is null || city.Calendar is null || city.CameraSamples is null || city.CoverageSpots is null ||
                city.ObservedTargetIds is null || city.GeopediaEntryIds is null || city.Collectibles is null || city.Campaign is null ||
                city.Attention is null || !Enum.IsDefined(city.OfficeMode) || city.PhysicsTick < 0 || city.CameraNextSequence < 0)
                throw new ArgumentException("Required city save fields are missing or invalid.", nameof(state));
            if (city.Fleet.SchemaVersion != 1 || city.Calendar.SchemaVersion != 1 || city.Collectibles.SchemaVersion != 1 ||
                city.Attention.SchemaVersion != 1 || city.Fleet.OwnedPlatforms is null || city.Fleet.OwnedSensorUpgrades is null ||
                city.Fleet.SkillRanks is null || city.Fleet.OwnedPlatforms.Any(m => !Enum.IsDefined(m)) ||
                city.Fleet.SkillRanks.Keys.Any(k => !Enum.IsDefined(k)) ||
                city.Campaign.Platforms is null || city.Campaign.Regions is null || city.Campaign.Equipment is null ||
                city.Campaign.AppliedCutscenes is null || city.Campaign.SkippedCutscenes is null || city.Campaign.Objectives is null ||
                city.Collectibles.CollectedEggIds is null || city.Collectibles.CollectedCardIds is null ||
                city.Collectibles.OccupiedLandmarkIds is null || city.Attention.Zones is null)
                throw new ArgumentException("Invalid city component version or collection.", nameof(state));
            if (city.CameraSamples.Select(s => s.SampleId).Distinct(StringComparer.Ordinal).Count() != city.CameraSamples.Count ||
                city.Campaign.Objectives.Any(p => string.IsNullOrWhiteSpace(p.Key) || string.IsNullOrWhiteSpace(p.Value)))
                throw new ArgumentException("Duplicate camera IDs or invalid campaign objective.", nameof(state));
            _ = LocomotionSimulator.FromSaveSnapshot(city.Mobility);
            var fleetId = Resolve(city.Fleet.ActivePlatformId);
            if (!FleetShopCatalog.Platforms.Any(p => p.Profile.Id == fleetId))
                throw new ArgumentException("Unknown active city platform.", nameof(state));
            _ = FleetProgressionManager.FromSnapshot(city.Fleet with { ActivePlatformId = fleetId });
            new WorldCalendarAndWeatherController().Restore(city.Calendar);
            foreach (var sample in city.CameraSamples) _ = sample.ToDomain();
            if (city.CameraSamples.Any(s => s.Sequence < 0 || s.Sequence >= city.CameraNextSequence))
                throw new ArgumentException("Camera sequence would collide with an existing observation.", nameof(state));
            if (city.ActiveStoryMissionId is { } mission)
            {
                _ = Gsa.Missions.Campaign.FullCampaignCatalog.GetMission(mission);
                if (state.Contracts.Count != 1 || state.Contracts[0].CatalogDefinition.Id != mission)
                    throw new ArgumentException("Active story and saved contract do not match.", nameof(state));
            }
            city = city with { Fleet = city.Fleet with { ActivePlatformId = Resolve(city.Fleet.ActivePlatformId) } };
        }
        var invalidate = sourceSchema != SaveGameManager.CurrentSchemaVersion || content.CatalogVersion != 2 || aliases.Count > 0;
        var normalized = state with
        {
            SchemaVersion = SaveGameManager.CurrentSchemaVersion,
            Equipment = equipment,
            City = city,
            Content = content with { CatalogVersion = 2, DerivedCacheIds = invalidate ? [] : content.DerivedCacheIds.ToArray() },
        };
        // Exercise snapshot validation without publishing runtime objects or altering stored state.
        _ = SaveGameManager.RestoreRuntime(normalized);
        return (normalized, new(sourceSchema, sourceSchema != SaveGameManager.CurrentSchemaVersion,
            invalidate, aliases.Order(StringComparer.Ordinal).ToArray()));
    }
}
