using Gsa.Core.Mobility;
using Gsa.Core.Geometry;
using Gsa.Core.Sensors.Camera;
using Gsa.Core.Traffic;
using Gsa.Missions.Activities;
using Gsa.Missions.Campaign;
using Gsa.Missions.Economy;
using Gsa.Missions.Gameplay;
using Gsa.Missions.Persistence;
using Gsa.Missions.Presentation;
using Godot;

namespace Gsa.Engine.World;

public partial class SaoDatumCityScene
{
    private Gsa.Core.World.WorldBounds CitySaveBounds => new(
        new Vector3d(_fullManifest.Bounds.Min.X, Math.Min(_fullManifest.Bounds.Min.Y, SouthAccessRoadMinimumNorth), _fullManifest.Bounds.Min.Z),
        _fullManifest.Bounds.Max);
    private SaveLoadResult? _citySaveStatus;
    private bool _citySaveBlocked;
    private string _citySaveNotice = "";
    private Label _citySaveLabel = null!;
    private Button _recoverCitySaveButton = null!;

    private SaveGameState BuildCitySaveState() => new(
        SchemaVersion: SaveGameManager.CurrentSchemaVersion,
        Trigger: SaveTrigger.Manual,
        SaveGeneration: 0,
        Clocks: new(_calendar.CalendarTimeSeconds, _calendar.CalendarTimeSeconds),
        Rng: new(1, 1, _fieldSamples.Count),
        Ledger: _ledger.ExportState(),
        Progression: _progression.ExportSnapshot(),
        Equipment: new(_fleet.ActivePlatform.Id, _fleet.OwnedPlatforms.Select(m => PlatformProfile.ForMode(m).Id).ToArray(),
            new Dictionary<string, string>()),
        CustodyStore: _cityCustody.ExportSnapshot(),
        Contracts: _activeMachine is null ? [] : [_activeMachine.ExportSnapshot()],
        Content: SaveContentVersions.Current,
        City: new(
            _locomotion.ExportSaveSnapshot(), _fleet.ExportSnapshot(), _calendar.ExportSnapshot(),
            _fieldSamples.Select(SerializableCameraSample.FromDomain).ToArray(), _coverageSpots.ToArray(),
            _observedTargetIds.Order(StringComparer.Ordinal).ToArray(), _geopedia.ExportUnlockedIds(), _collectibles.ExportSnapshot(),
            new(_campaignState.UnlockedPlatforms.ToArray(), _campaignState.UnlockedRegions.ToArray(),
                _campaignState.UnlockedEquipment.ToArray(), _campaignState.AppliedCutsceneIds.ToArray(),
                _campaignState.SkippedCutsceneIds.ToArray(), new Dictionary<string, string>(_campaignState.ObjectiveStates)),
            _attention.ExportSnapshot(), _activeStorySpec?.MissionId, _officeMode, _privacyMasksApplied, _rackMisaligned,
            _calmPolicy.CalmCityEnabled, _disguiseActive, _contractSeed, _tick, NextCityCameraSequence, _cityHazards.Export(), _everyday.Export(), _gimbalina.ExportSaveSnapshot())
        {
            LidarBuffer = IsTunnelCollection(_activeContract) && _activeStorySpec?.MissionId == TunnelMissionId
                ? new(_pendingLidar.ToArray(), _lidarTicksWithoutPose, _lidarTicksWithoutCapacity) : null,
        });

    private void SaveCityCareerState(SaveTrigger trigger = SaveTrigger.Manual, bool announce = true)
    {
        if (_isSandbox)
        {
            _feedbackText = Text("save.ui.sandbox_separate");
            return;
        }
        if (_citySaveBlocked)
        {
            _feedbackText = Text("save.error.current_protected");
            return;
        }
        try
        {
            var result = _saveManager.Save(BuildCitySaveState(), trigger);
            _citySaveNotice = Text(result.Succeeded ? "training.done.save" : result.ReasonKey ?? "training.load_failed");
            if (announce || !result.Succeeded) _feedbackText = _citySaveNotice;
            if (result.Succeeded) _citySaveStatus = _saveManager.LoadCurrent();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _feedbackText = _citySaveNotice = Text("save.error.write_failed");
        }
    }

    private void LoadCityCareerState(bool recover = false)
    {
        if (_isSandbox)
        {
            _feedbackText = Text("save.ui.sandbox_separate");
            return;
        }
        var result = recover ? _saveManager.RestorePreviousGeneration() : _saveManager.LoadCurrent();
        _citySaveStatus = result;
        _citySaveBlocked = !result.Succeeded && result.Status != SaveLoadStatus.FileNotFound;
        if (!result.Succeeded || result.State is null)
        {
            _feedbackText = _citySaveNotice = Text(result.Status == SaveLoadStatus.FileNotFound
                ? "city.career.welcome" : result.DiagnosticReasonKey ?? "training.load_failed");
            return;
        }
        try
        {
            // Prepare and validate before replacing any live state.
            var runtime = SaveGameManager.RestoreRuntime(result.State);
            if (runtime.Contracts.Count > 1) throw new ArgumentException("City supports one active contract.");
            var city = result.State.City;
            var calendar = new WorldCalendarAndWeatherController(result.State.Clocks.SimulationTimeSeconds);
            if (city is not null) calendar.Restore(city.Calendar);
            var fleet = city is not null ? FleetProgressionManager.FromSnapshot(city.Fleet) : RestoreLegacyCityFleet(runtime.Equipment);
            var mobility = city is not null ? LocomotionSimulator.FromSaveSnapshot(city.Mobility, CitySaveBounds)
                : new LocomotionSimulator(_locomotion.PositionEnu, _locomotion.HeadingRadians, fleet.ActivePlatform.Mode, _fullManifest.Bounds);
            var samples = city?.CameraSamples.Select(s => s.ToDomain()).ToArray() ?? [];
            var hazards = Gsa.Missions.Gameplay.CityHazardState.Restore(city?.Hazards, city?.DisguiseActive ?? false);
            var everyday = CityEverydayEncounters.Restore(city?.Encounters);
            var gimbalina = city?.Gimbalina is { } g ? GimbalinaStabilizer.FromSaveSnapshot(g) : new GimbalinaStabilizer();
            if (city?.Encounters?.Dog is { } dog && !_cityDogs.ContainsKey(dog.SiteId)) throw new ArgumentException("Unknown dog site.");
            if (everyday.Parking is { } parking && !_cityParking.ContainsKey(parking.SiteId)) throw new ArgumentException("Unknown parking site.");
            var geopedia = new GeopediaCatalogCoordinator(city?.GeopediaEntryIds);
            var collectibles = new MvpCollectiblesCoordinator(city?.Collectibles);
            var story = city?.ActiveStoryMissionId is { } id ? FullCampaignCatalog.GetMission(id) : null;
            var objectives = city?.Campaign.Objectives.ToArray() ?? [];
            var attention = new TerritoryAttentionTracker();
            if (city is not null) attention.ImportSnapshot(city.Attention);

            _ledger = runtime.Ledger;
            _progression = runtime.Progression;
            _calendar = calendar;
            _fleet = fleet;
            _locomotion = mobility;
            _carSim = mobility.Mode >= LocomotionMode.Car
                ? new CarSuspensionSimulator(mobility.PositionEnu, mobility.HeadingRadians, mobility.Mode, conditionDamage: hazards.GetVehicleDamage(mobility.Mode)) : null;
            _lastSpeedMps = 0;
            _lastCitySurveyReview = null;
            _lastTunnelReview = null;
            _lastSidewalkMeasurements = [];
            _cityTrafficContactId = _cityTrafficWarningId = null;
            _cityTrafficContactGrace = _cityTrafficWarningSilence = 0;
            _geopedia = geopedia;
            _collectibles = collectibles;
            _attention = attention;
            _cityHazards = hazards; _cityCustody = runtime.CustodyStore;
            _everyday = everyday; _gimbalina = gimbalina;
            _mobility.SetGimbalinaPresent(!gimbalina.HasFledToSafety);
            RestoreCityProtectedPhotos();
            _fieldSamples.Clear(); _fieldSamples.AddRange(samples);
            _coverageSpots.Clear(); _coverageSpots.AddRange(city?.CoverageSpots ?? []);
            _observedTargetIds.Clear(); _observedTargetIds.UnionWith(city?.ObservedTargetIds ?? []);
            _activeMachine = runtime.Contracts.SingleOrDefault();
            _activeContract = result.State.Contracts.SingleOrDefault()?.CatalogDefinition;
            _activeStorySpec = story;
            RestorePendingCityLidar(city?.LidarBuffer);
            if (IsTunnelCollection(_activeContract)) _lidarEnabled = false;
            PrepareCityPhotoCoverage();
            _camScheduler = new CameraCaptureScheduler("city_cam", initialSequence: city?.CameraNextSequence ?? 0);
            _panoramicCameras = [];
            _captureRequested = false;
            _recentPhotoSeconds = 0; _cleanLensRequested = _disarmRequested = false; _hazardNotice = ""; _lastHazardWarning = null;
            _gtaHud.HideCutsceneLetterbox();
            _officeMode = city?.OfficeMode ?? Gsa.Missions.Office.ProcessingMode.Arcade;
            _privacyMasksApplied = city?.PrivacyMasksApplied ?? true;
            _rackMisaligned = city?.RackMisaligned ?? false;
            _disguiseActive = city?.DisguiseActive ?? false;
            _calmPolicy.SetCalmCityEnabled(city?.CalmCityEnabled ?? false);
            _contractSeed = city?.NextContractSeed ?? 101;
            _tick = city?.PhysicsTick ?? 0;
            _campaignState.UnlockedPlatforms.Clear(); _campaignState.UnlockedPlatforms.UnionWith(city?.Campaign.Platforms ?? ["cart"]);
            _campaignState.UnlockedRegions.Clear(); _campaignState.UnlockedRegions.UnionWith(city?.Campaign.Regions ?? ["old_center"]);
            _campaignState.UnlockedEquipment.Clear(); _campaignState.UnlockedEquipment.UnionWith(city?.Campaign.Equipment ?? ["cam_initial", "gnss_basic"]);
            _campaignState.AppliedCutsceneIds.Clear(); _campaignState.AppliedCutsceneIds.UnionWith(city?.Campaign.AppliedCutscenes ?? []);
            _campaignState.SkippedCutsceneIds.Clear(); _campaignState.SkippedCutsceneIds.UnionWith(city?.Campaign.SkippedCutscenes ?? []);
            _campaignState.ObjectiveStates.Clear();
            foreach (var (key, value) in objectives) _campaignState.ObjectiveStates.Add(key, value);
            // These are render caches, never restored as acquired scientific samples.
            _lidarPointWriteIndex = _lidarPointVisibleCount = _lidarSliceIndex = 0;
            _lidarCloudMesh.Multimesh.VisibleInstanceCount = 0;
            ResetCityNavigation();
            _hasNearbyObstacleAnchor = false;
            RemoveChild(_collectiblesRoot);
            _collectiblesRoot.QueueFree();
            _collectibleNodes.Clear();
            BuildCollectiblesAndLandmarks();
            RefreshCollectibleNodesAfterLoad();
            _mobility.SetVisualPlatformMode(mobility.Mode);
            UpdateSectorStreaming(mobility.PositionEnu);
            RestoreCityHazardWorld(); SyncCityEverydayActors();
            SnapCamera();
            _citySaveBlocked = false;
            _feedbackText = _citySaveNotice = Text(recover ? "save.ui.recovered" :
                result.Compatibility?.Migrated == true ? "save.ui.migrated" : "training.done.load");
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            _citySaveBlocked = true;
            _feedbackText = _citySaveNotice = Text("save.error.invalid_state");
        }
    }

    private static FleetProgressionManager RestoreLegacyCityFleet(SaveEquipmentMountState equipment)
    {
        var entries = FleetShopCatalog.Platforms.Select(p => p.Profile).ToArray();
        var owned = entries.Where(p => equipment.OwnedEquipmentIds.Contains(p.Id) || p.Id == equipment.ActivePlatformId)
            .Select(p => p.Mode).ToArray();
        return FleetProgressionManager.FromSnapshot(new(1, equipment.ActivePlatformId, owned, [], new Dictionary<SkillTrackKind, int>()));
    }

    private void RefreshCollectibleNodesAfterLoad()
    {
        foreach (var id in _collectibles.CollectedEggIds.Concat(_collectibles.CollectedCardIds))
            if (_collectibleNodes.TryGetValue(id, out var node)) node.Hide();
        foreach (var egg in _activeEggs)
            if (_collectibleNodes.TryGetValue(egg.EggId, out var node)) node.Visible = !_collectibles.CollectedEggIds.Contains(egg.EggId);
        foreach (var card in MvpCollectiblesCoordinator.AllSixProjectionCards)
            if (_collectibleNodes.TryGetValue(card.CardId, out var node)) node.Visible = !_collectibles.CollectedCardIds.Contains(card.CardId);
    }
}
