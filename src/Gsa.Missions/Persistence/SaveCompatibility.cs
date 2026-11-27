using Gsa.Core.Geometry;
using System.Text.Json.Serialization;
using Gsa.Core.Mobility;
using Gsa.Core.Traffic;
using Gsa.Core.World;
using Gsa.Missions.Activities;
using Gsa.Missions.Economy;
using Gsa.Missions.Office;
using Gsa.Missions.Gameplay;

namespace Gsa.Missions.Persistence;

public sealed record SaveContentVersions(
    int CatalogVersion,
    int ModelVersion,
    int WorldVersion,
    IReadOnlyList<string> DerivedCacheIds)
{
    public const int CurrentCatalogVersion = 11;
    public static SaveContentVersions Current => new(CurrentCatalogVersion, 1, 1, []);
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
    long CameraNextSequence = 0, CityHazardSnapshot? Hazards = null,
    CityEverydaySnapshot? Encounters = null, GimbalinaSaveSnapshot? Gimbalina = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CityLidarBufferSnapshot? LidarBuffer { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? ViaductHeights { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool RainSurveyFilterEnabled { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BoundarySurveySaveState? BoundarySurvey { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WorksUploadSaveState? WorksUpload { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BlooperAndCosmeticSnapshot? Cosmetics { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ManualCameraCapture { get; init; }
}

public sealed record CityLidarBufferSnapshot(IReadOnlyList<CityLidarSlice> Pending, int SkippedNoPose, int SkippedCapacity);
public sealed record BoundarySurveySaveState(IReadOnlyList<BoundarySurveyShot> Pending, bool PursuitTriggered);
public sealed record WorksUploadSaveState(int PadIndex, double RemainingSeconds);

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
        const int currentCatalog = SaveContentVersions.CurrentCatalogVersion;
        if (content.CatalogVersion < 1 || content.CatalogVersion > currentCatalog || content.ModelVersion != 1 || content.WorldVersion != 1 || content.DerivedCacheIds is null)
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
            _ = CityHazardState.Restore(city.Hazards, city.DisguiseActive);
            if (city.Cosmetics is not null) _ = BlooperAlbumAndCosmeticsCoordinator.FromSnapshot(city.Cosmetics);
            _ = CityEverydayEncounters.Restore(city.Encounters);
            if (city.Gimbalina is not null) _ = GimbalinaStabilizer.FromSaveSnapshot(city.Gimbalina);
            new TerritoryAttentionTracker().ImportSnapshot(city.Attention);
            city = city with { Fleet = city.Fleet with { ActivePlatformId = Resolve(city.Fleet.ActivePlatformId) } };
        }
        var invalidate = sourceSchema != SaveGameManager.CurrentSchemaVersion || content.CatalogVersion != currentCatalog || aliases.Count > 0;
        var normalized = state with
        {
            SchemaVersion = SaveGameManager.CurrentSchemaVersion,
            Equipment = equipment,
            City = city,
            Content = content with { CatalogVersion = currentCatalog, DerivedCacheIds = invalidate ? [] : content.DerivedCacheIds.ToArray() },
        };
        // Exercise snapshot validation without publishing runtime objects or altering stored state.
        var runtime = SaveGameManager.RestoreRuntime(normalized);
        if (city is not null)
        {
            var ids = city.CameraSamples.Select(s => s.SampleId).ToHashSet(StringComparer.Ordinal);
            var lidarTicks = new HashSet<long>();
            var lidarReturns = 0;
            void CheckLidar(IEnumerable<CityLidarSlice> slices, int limit, string? missionId)
            {
                foreach (var slice in slices)
                {
                    if (slice is null || !lidarTicks.Add(slice.PhysicsTick))
                        throw new ArgumentException("Duplicate or missing LiDAR slice.");
                    slice.Validate(city.PhysicsTick);
                    var corridor = slice.TunnelRanges?.Corridor;
                    if (!(missionId switch
                    {
                        "HIST-06" => corridor is null,
                        PortViaductSurvey.MissionId => corridor == PortViaductSurvey.AreaId,
                        WaterfrontRainSurvey.MissionId => corridor == WaterfrontRainSurvey.AreaId && slice.ProfileId == "lidar-l5",
                        PortWorksSurvey.MissionId => corridor == PortWorksSurvey.AreaId && slice.ProfileId == "lidar-bench",
                        FinalCityPavementSurvey.MissionId => FinalCityPavementSurvey.FindSector(corridor) is not null && slice.ProfileId == "lidar-l6",
                        _ => false,
                    }))
                        throw new ArgumentException("City LiDAR observations do not match the contract.");
                    lidarReturns += slice.Returns.Count;
                    if (lidarReturns > limit) throw new ArgumentException("City LiDAR storage exceeds its limit.");
                }
            }
            if (city.LidarBuffer is { } lidar)
            {
                if (lidar.Pending is null || lidar.Pending.Count > CityLidarSlice.MaxBufferedReturns || lidar.SkippedNoPose < 0 || lidar.SkippedCapacity < 0 ||
                    city.ActiveStoryMissionId is not ("HIST-06" or PortViaductSurvey.MissionId or WaterfrontRainSurvey.MissionId or PortWorksSurvey.MissionId or FinalCityPavementSurvey.MissionId))
                    throw new ArgumentException("Invalid pending city LiDAR buffer.");
                CheckLidar(lidar.Pending, CityLidarSlice.MaxBufferedReturns, city.ActiveStoryMissionId);
                if (city.ActiveStoryMissionId == FinalCityPavementSurvey.MissionId &&
                    lidar.Pending.Select(slice => slice.TunnelRanges!.Corridor).Distinct(StringComparer.Ordinal).Skip(1).Any())
                    throw new ArgumentException("The finale buffer contains multiple sectors.");
                if (CityLidarSlice.StorageUnits(lidarReturns) > city.Mobility.UsedVirtualStorageUnits)
                    throw new ArgumentException("Pending city LiDAR storage is unaccounted for.");
            }
            if (city.ViaductHeights is { } heights && (city.ActiveStoryMissionId != PortViaductSurvey.MissionId ||
                heights.Count != 2 * PortViaductSurvey.VerticalControls.Count || heights.Any(value => value is null || value.Length > 16)))
                throw new ArgumentException("Invalid viaduct height draft.");
            if (city.RainSurveyFilterEnabled && city.ActiveStoryMissionId != WaterfrontRainSurvey.MissionId)
                throw new ArgumentException("Invalid Waterfront rain filter selection.");
            if (city.WorksUpload is { } transfer && (city.ActiveStoryMissionId != PortWorksSurvey.MissionId ||
                city.LidarBuffer?.Pending.Count is not > 0 || transfer.PadIndex < 0 || transfer.PadIndex >= PortWorksSurvey.UploadPads.Count ||
                transfer.RemainingSeconds is not (> 0 and <= PortWorksSurvey.UploadDuration)))
                throw new ArgumentException("Invalid works field upload state.");
            var boundaryTicks = new HashSet<long>();
            void CheckBoundaryShots(IEnumerable<BoundarySurveyShot> shots)
            {
                foreach (var shot in shots)
                {
                    if (shot is null || !boundaryTicks.Add(shot.PhysicsTick) || shot.PhysicsTick > city.PhysicsTick ||
                        shot.TimestampSeconds > state.Clocks.SimulationTimeSeconds ||
                        SerraBoundarySurvey.Sites.FirstOrDefault(site => site.Id == shot.MonumentId) is not { } target)
                        throw new ArgumentException("Invalid boundary observation identity.");
                    _ = shot.RecomputePoint(target);
                }
            }
            if (city.BoundarySurvey is { } boundary)
            {
                if (city.ActiveStoryMissionId != SerraBoundarySurvey.MissionId || boundary.Pending is null ||
                    boundary.Pending.Count > city.Mobility.UsedVirtualStorageUnits || boundary.Pending.Count > SerraBoundarySurvey.Sites.Count * 2 ||
                    boundary.Pending.Select(shot => (shot?.MonumentId, shot?.StationIndex)).Distinct().Count() != boundary.Pending.Count)
                    throw new ArgumentException("Invalid pending boundary survey.");
                CheckBoundaryShots(boundary.Pending);
            }
            foreach (var entry in runtime.CustodyStore.Entries.Where(e => e.Stage == BatchCustodyStage.Protected))
            {
                var batch = System.Text.Json.JsonSerializer.Deserialize<SurveyBatchArtifact>(entry.PayloadJson)
                    ?? throw new ArgumentException("Invalid protected city batch.");
                if (batch.LidarSlices is { } slices)
                {
                    CheckLidar(slices, CityLidarSlice.MaxContractReturns, batch.ContractId);
                }
                if (batch.ContractId == FinalCityPavementSurvey.MissionId || batch.FinalSurveyUploadSectorId is not null)
                {
                    if (batch.ContractId != FinalCityPavementSurvey.MissionId ||
                        FinalCityPavementSurvey.FindSector(batch.FinalSurveyUploadSectorId) is null ||
                        batch.LidarSlices is not { Count: > 0 and <= CityLidarSlice.MaxBufferedReturns } || batch.Samples.Count != 0 ||
                        !batch.LidarSlices.Any(slice => slice.Returns.Count > 0) ||
                        batch.LidarSlices.Any(slice => slice.TunnelRanges?.Corridor != batch.FinalSurveyUploadSectorId))
                        throw new ArgumentException("Invalid protected finale sector evidence.");
                }
                if (batch.WorksFieldPad is { } pad && (batch.ContractId != PortWorksSurvey.MissionId ||
                    batch.LidarSlices is not { Count: > 0 } || pad < 0 || pad >= PortWorksSurvey.UploadPads.Count))
                    throw new ArgumentException("Invalid works field upload evidence.");
                if (batch.BoundaryShots is { } boundaryShots)
                {
                    if (batch.ContractId != SerraBoundarySurvey.MissionId || boundaryShots.Count is < 1 ||
                        boundaryShots.Count > SerraBoundarySurvey.Sites.Count * 2 ||
                        boundaryShots.Count > batch.StorageUnitsUsed ||
                        boundaryShots.Any(shot => shot is null || shot.TimestampSeconds < batch.StartTimeSeconds || shot.TimestampSeconds > batch.EndTimeSeconds))
                        throw new ArgumentException("Invalid protected boundary survey.");
                    CheckBoundaryShots(boundaryShots);
                }
                foreach (var sample in batch.Samples)
                {
                    _ = sample.ToDomain();
                    if (!ids.Add(sample.SampleId) || sample.Sequence < 0 || sample.Sequence >= city.CameraNextSequence)
                        throw new ArgumentException("Protected camera ID/sequence conflicts with city acquisition.");
                }
            }
        }
        return (normalized, new(sourceSchema, sourceSchema != SaveGameManager.CurrentSchemaVersion,
            invalidate, aliases.Order(StringComparer.Ordinal).ToArray()));
    }
}
