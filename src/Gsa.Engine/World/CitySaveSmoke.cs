using System.Text.Json;
using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Missions.Activities;
using Gsa.Missions.Campaign;
using Gsa.Missions.Economy;
using Gsa.Missions.Office;
using Gsa.Missions.Persistence;
using Gsa.Missions.Presentation;
using Godot;

namespace Gsa.Engine.World;

public partial class SaoDatumCityScene
{
    private bool _citySaveSmoke;
    private bool _citySaveSmokeCompleted;
    private string? _citySmokeDirectory;

    private void RunCitySaveSmoke()
    {
        static void Require(bool condition, string reason)
        {
            if (!condition) throw new InvalidOperationException("City save smoke: " + reason);
        }
        _calendar.AdvanceCalendarSeconds(2 * 86400);
        StartStoryMission(FullCampaignCatalog.GetMission("HIST-01"));
        _ledger = new CareerLedger(430, initialDebtCoords: 73);
        _fleet = FleetProgressionManager.FromSnapshot(new(1, "van", [LocomotionMode.Handcart, LocomotionMode.Van], [],
            new Dictionary<SkillTrackKind, int> { [SkillTrackKind.Calibration] = 2 }));
        SwitchPlatform(LocomotionMode.Van);
        var parked = _locomotion.ExportSaveSnapshot() with
        {
            Mode = LocomotionMode.OnFoot,
            MountedVehicleMode = LocomotionMode.Van,
            PositionEnu = new Vector3d(_locomotion.PositionEnu.X + 4, 7399992.5, 100),
            ParkedVehiclePositionEnu = new Vector3d(_locomotion.PositionEnu.X, 7399992.5, 100),
            UsedVirtualStorageUnits = 17,
            RemainingAutonomySeconds = 500,
        };
        _locomotion = LocomotionSimulator.FromSaveSnapshot(parked, CitySaveBounds);
        _carSim = null;
        _calendar.SetWeather(true, .65);
        _calendar.AdvanceCalendarSeconds(3600);
        while (!_camScheduler.IsCaptureTick(_tick)) _tick++;
        CaptureCityPhoto(CurrentPositionEnu(), CurrentHeadingRadians(), 0, _calendar.ExportSnapshot());
        Require(_fieldSamples.Count > 0, "real scene capture produced no stored observation");
        _geopedia.UnlockByConceptId("coverage");
        _collectibles.CollectProjectionCard(MvpCollectiblesCoordinator.AllSixProjectionCards[0].CardId, _geopedia);
        _campaignState.ObjectiveStates["HIST-01"] = "collecting";
        _campaignState.AppliedCutsceneIds.Add("smoke_intro");
        _campaignState.SkippedCutsceneIds.Add("smoke_intro");
        _officeMode = ProcessingMode.Technical;
        _rackMisaligned = true;
        _privacyMasksApplied = false;
        _calmPolicy.SetCalmCityEnabled(true);
        _disguiseActive = true;
        var expected = JsonSerializer.Serialize(BuildCitySaveState().City);
        var contract = JsonSerializer.Serialize(_activeMachine!.ExportSnapshot());
        SaveCareer();
        Require(_saveManager.LoadCurrent().Succeeded, "save failed: " + _saveManager.LoadCurrent().DiagnosticDetail);
        SaveCareer(); // establish a recoverable generation containing the full fixture
        _fieldSamples.Clear(); _coverageSpots.Clear(); _observedTargetIds.Clear();
        _ledger = new CareerLedger(0);
        _fleet = new FleetProgressionManager();
        _geopedia = new GeopediaCatalogCoordinator();
        _collectibles = new MvpCollectiblesCoordinator();
        _campaignState.ObjectiveStates.Clear();
        _activeContract = null; _activeMachine = null; _activeStorySpec = null;
        _lidarPointVisibleCount = 12;
        TryLoadCareer();
        Require(!_citySaveBlocked, "reload blocked: " + _citySaveStatus?.DiagnosticDetail);
        Require(JsonSerializer.Serialize(BuildCitySaveState().City) == expected, "city session changed on reload");
        Require(JsonSerializer.Serialize(_activeMachine!.ExportSnapshot()) == contract, "active contract/deadline changed");
        Require(_activeEggs.All(e => e.DayIndex == _calendar.DayIndex), "collectibles rebuilt for wrong day");
        Require(_ledger.BalanceCoords == 430 && _ledger.FineDebtCoords == 73, "ledger/debt changed");
        Require(_locomotion.Mode == LocomotionMode.OnFoot && _locomotion.MountedVehicleMode == LocomotionMode.Van, "parking/mount state lost");
        Require(_lidarPointVisibleCount == 0 && _lidarCloudMesh.Multimesh.VisibleInstanceCount == 0, "visual cache reused as acquisition");
        _tick += 30;
        _calendar.StepPhysical(.5);
        CaptureCityPhoto(CurrentPositionEnu(), CurrentHeadingRadians(), 0, _calendar.ExportSnapshot());
        Require(_fieldSamples.Count == 2 && _fieldSamples.Select(s => s.SampleId).Distinct().Count() == 2,
            "camera sequence reused after reload");
        TryLoadCareer();
        var previous = File.ReadAllBytes(_saveManager.PreviousSavePath);
        const string future = "{\"SchemaVersion\":99,\"future\":\"preserve\"}";
        File.WriteAllText(_saveManager.CurrentSavePath, future);
        TryLoadCareer();
        SaveCareer();
        Require(File.ReadAllText(_saveManager.CurrentSavePath) == future, "future save overwritten");
        Require(_citySaveBlocked && _citySaveStatus?.PreviousGenerationAvailable == true, "recovery not offered");
        RefreshTabletContents();
        Require(_recoverCitySaveButton.Visible, "recovery button hidden");
        _recoverCitySaveButton.EmitSignal(Button.SignalName.Pressed);
        Require(!_citySaveBlocked && _citySaveStatus?.Succeeded == true, "UI recovery failed");
        Require(File.ReadAllText(_citySaveStatus!.PreservedOriginalPath!) == future, "original not archived");
        Require(previous.SequenceEqual(File.ReadAllBytes(_saveManager.PreviousSavePath)), "backup changed during recovery");
        Require(JsonSerializer.Serialize(BuildCitySaveState().City) == expected, "recovered session differs");
        RunAdvancedSurveyTabletSmoke();
        RunCityNavigationSmoke();
        RunCityHazardSmoke();
        RunCityEverydaySmoke();
        RunCityDeliverySmoke();
        RunCityTrafficSmoke();
        TryLoadCareer(); // The tablet fixture changes presentation/platform inputs without writing a save.
        Require(JsonSerializer.Serialize(BuildCitySaveState().City) == expected, "tablet checks changed saved session");
        var current = File.ReadAllBytes(_saveManager.CurrentSavePath);
        _isSandbox = true;
        SaveCareer(); TryLoadCareer();
        Require(current.SequenceEqual(File.ReadAllBytes(_saveManager.CurrentSavePath)), "sandbox changed career slot");
        _isSandbox = false;
        _citySaveSmokeCompleted = true;
        _tabletPanel.Show();
        _tabletTabs.CurrentTab = 5;
        RefreshTabletContents();
        GD.Print($"GSA_CITY_SAVE_SMOKE_OK schema=2 photos={_fieldSamples.Count} debt=73 parked=van recovery=preserved sandbox=isolated");
        if (Gsa.Engine.Diagnostics.GsaScreenshotHelper.GetRequestedPath() is null) GetTree().Quit(0);
    }

    public override void _ExitTree()
    {
        DisposeCachedSectors();
        if (_citySmokeDirectory is null) return;
        try { Directory.Delete(_citySmokeDirectory, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
