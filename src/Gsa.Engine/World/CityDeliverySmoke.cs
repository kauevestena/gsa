using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Missions.Campaign;
using Gsa.Missions.Contracts;
using Gsa.Missions.Economy;
using Gsa.Missions.Office;
using Gsa.Missions.Persistence;
using Gsa.Missions.Quality;
using Godot;

namespace Gsa.Engine.World;

public partial class SaoDatumCityScene
{
    private void RunCityDeliverySmoke()
    {
        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("City delivery smoke: " + message);
        }

        var previous = _saveManager;
        var original = File.ReadAllBytes(previous.CurrentSavePath);
        _saveManager = new SaveGameManager(Path.Combine(_citySmokeDirectory!, "delivery"));
        _citySaveBlocked = false;
        _activeContract = null;
        _activeMachine = null;
        _activeStorySpec = null;
        const int initialBalance = 250;
        _ledger = new CareerLedger(initialBalance);
        _fleet = new FleetProgressionManager();
        _progression = new CareerProgressionState();
        _officeMode = ProcessingMode.Arcade;
        _rackMisaligned = false;
        _gimbalina = new GimbalinaStabilizer();
        _calendar = new(12 * 3600);
        _campaignState.ObjectiveStates.Clear();
        _campaignState.UnlockedPlatforms.Clear();
        _campaignState.UnlockedPlatforms.Add("cart");
        _campaignState.AppliedCutsceneIds.Clear();
        _campaignState.SkippedCutsceneIds.Clear();

        void PositionAndCapture(Vector3d position, double heading, LocomotionMode mode = LocomotionMode.Handcart)
        {
            _carSim = null;
            _locomotion.RepositionSafeAfterRecovery(position, mode, heading, true, false);
            UpdateSectorStreaming(position);
            _tick += 30;
            _calendar.StepPhysical(.5);
            while (!_camScheduler.IsCaptureTick(_tick)) _tick++;
            CaptureCityPhoto(position, heading, 0, _calendar.ExportSnapshot());
        }

        _locomotion.RepositionSafeAfterRecovery(_locomotion.PositionEnu, LocomotionMode.Handcart, 0, true, false);
        var story = FullCampaignCatalog.GetMission("HIST-01");
        var drainage = FullCampaignCatalog.GetMission("HIST-02");
        var sidewalks = FullCampaignCatalog.GetMission("HIST-03");
        StartStoryMission(drainage);
        Require(_activeContract is null && _feedbackText == Text("city.story.locked"), "drainage opened before the façade job");
        StartStoryMission(story);
        var facadeJob = _activeContract ?? throw new InvalidOperationException("Facade contract was not available.");
        Require(facadeJob.RequiredFeatureIds.Count == 20, "the façade contract did not require 20 targets");
        ProcessAndDeliverCityContract();
        Require(_feedbackText == Text("city.office.upload_first") && _ledger.BalanceCoords == initialBalance, "an empty submission passed");
        PositionAndCapture(new(500190, 7400000.7, 100), Math.PI / 2);
        Require(_fieldSamples.Any(s => s.Quality.Passed && s.ObservedFeatures.Any(f => f.TargetId == "facade_18")), "scene photo missed its facade");
        ProcessAndDeliverCityContract();
        Require(_feedbackText == Text("city.office.upload_first"), "the office read photos still in the field");
        void Upload(LocomotionMode mode = LocomotionMode.Handcart)
        {
            var office = _manifestObjectsById["office_entry"].PositionEnu + new Vector3d(0, -4, -1.5);
            _locomotion.RepositionSafeAfterRecovery(office, mode, 0, false, false);
            OffloadCityData();
            Require(_fieldSamples.Count == 0 && _protectedCityPhotos.Count > 0, "upload did not protect photos");
        }

        Upload();
        ProcessAndDeliverCityContract();
        Require(_activeMachine?.State == ContractState.Retrabalho && _ledger.BalanceCoords == initialBalance, "incomplete survey earned a payout");
        Require(_feedbackText == Text("city.office.missing_photo_targets"), "rework did not explain missing targets");
        for (var i = 1; i <= 20; i++)
        {
            if (i == 18) continue;
            PositionAndCapture(new(500010 + i * 10, 7400000.7, 100), Math.PI / 2);
        }
        ProcessAndDeliverCityContract();
        Require(_activeMachine?.State == ContractState.Retrabalho && _ledger.BalanceCoords == initialBalance, "unuploaded rework contributed");
        Upload();
        SaveCareer();
        TryLoadCareer();
        Require(_activeMachine?.State == ContractState.Retrabalho, "rework did not survive reload");

        _calendar.AdvanceCalendarSeconds(_activeMachine!.Snapshot!.DueAtSeconds + 3600 - _calendar.CalendarTimeSeconds);
        ProcessAndDeliverCityContract();
        var paid = _ledger.BalanceCoords;
        Require(_activeContract is null && paid == initialBalance + (int)Math.Floor(facadeJob.Reward.BaseCoords * .95),
            "valid photos did not pay the late-adjusted quote: " + CitySurveyReviewText());
        Require(CityMissionCompleted(story.MissionId), "photo mission did not progress the campaign");
        TryLoadCareer();
        Require(!AcceptCityContract(facadeJob), "settled contract could be reaccepted after reload");
        ProcessAndDeliverCityContract();
        Require(_ledger.BalanceCoords == paid &&
            _ledger.ExportState().Entries.Count(e => e.Kind == LedgerEntryKind.ContractPayout) == 1, "delivery replay paid twice");

        Require(!TryPurchaseCityPlatform(LocomotionMode.Bicycle), "the shop sold a bicycle before the drainage job");
        StartStoryMission(drainage);
        var drainageJob = _activeContract ?? throw new InvalidOperationException("Drainage contract was not available.");
        Require(drainageJob.RequiredFeatureIds.Count == 12, "the drainage contract did not require 12 drains");
        void CaptureSurveyPass(ContractDefinition definition, double eastOffset, double southOffset, LocomotionMode mode)
        {
            foreach (var id in definition.RequiredFeatureIds)
            {
                var target = _manifestObjectsById[id].PositionEnu;
                var viewpoint = new Vector3d(target.X + eastOffset, target.Y - southOffset, 100);
                PositionAndCapture(viewpoint, Math.Atan2(southOffset, -eastOffset), mode);
            }
        }

        CaptureSurveyPass(drainageJob, -1.5, 5, LocomotionMode.Handcart);
        Require(drainageJob.RequiredFeatureIds.All(id => _fieldSamples.Any(sample =>
            sample.ObservedFeatures.Any(feature => feature.TargetId == id))), "the initial drainage pass missed a drain");
        Upload();
        ProcessAndDeliverCityContract();
        Require(_activeMachine?.State == ContractState.Retrabalho && _ledger.BalanceCoords == paid &&
            _feedbackText == Text("city.office.need_parallax"), "single views passed drainage quality control");
        _calendar.AdvanceCalendarSeconds(_activeMachine!.Snapshot!.AcceptedAtSeconds + DrainageRainDelaySeconds - _calendar.CalendarTimeSeconds);
        Require(UpdateDrainageWeather() && _calendar.IsRaining, "rain did not arrive during the drainage job");
        CaptureSurveyPass(drainageJob, 1.5, 5, LocomotionMode.Handcart);
        Upload();
        ProcessAndDeliverCityContract();
        Require(_activeContract is null && CityMissionCompleted(drainage.MissionId) &&
            _lastCitySurveyReview?.GetMetric("absolute_rmse") is { SampleCount: 12, Status: MetricEvaluationStatus.Passed },
            "drainage triangulation did not pass: " + _feedbackText + " " + CitySurveyReviewText());
        TryLoadCareer();
        StartStoryMission(sidewalks);
        Require(_activeContract is null && _feedbackText == Text("city.story.requires_platform"), "the cart could accept the sidewalk job");
        var beforeBicycle = _ledger.BalanceCoords;
        Require(TryPurchaseCityPlatform(LocomotionMode.Bicycle) && _ledger.BalanceCoords < beforeBicycle &&
            _locomotion.MountedVehicleMode == LocomotionMode.Bicycle, "the completed drainage job did not unlock the bicycle");
        StartStoryMission(sidewalks);
        var sidewalkJob = _activeContract ?? throw new InvalidOperationException("Sidewalk contract was not available.");
        Require(sidewalkJob.RequiredFeatureIds.Count == 8, "the sidewalk contract did not require eight segments");
        CaptureSurveyPass(sidewalkJob, -1.5, 6, LocomotionMode.Bicycle);
        CaptureSurveyPass(sidewalkJob, 1.5, 6, LocomotionMode.Bicycle);
        Upload(LocomotionMode.Bicycle);
        ProcessAndDeliverCityContract();
        Require(_activeContract is null && CityMissionCompleted(sidewalks.MissionId) &&
            _lastCitySurveyReview?.GetMetric("relative_rmse") is { SampleCount: 16, Status: MetricEvaluationStatus.Passed },
            "sidewalk measurements did not pass: " + _feedbackText + " " + CitySurveyReviewText());
        foreach (var measurement in _lastSidewalkMeasurements)
        {
            var size = _manifestObjectsById[measurement.SegmentId].SizeEnu;
            Require(Math.Abs(measurement.WidthMetres - size.Y) < .03 && Math.Abs(measurement.CurbHeightMetres - size.Z) < .03,
                "sidewalk width or curb height differed from the modeled surfaces");
        }
        TryLoadCareer();
        Require(TryPurchaseCityPlatform(LocomotionMode.Scooter) &&
            _locomotion.MountedVehicleMode == LocomotionMode.Scooter, "the completed sidewalk job did not unlock the scooter");
        paid = _ledger.BalanceCoords;

        // A previously accepted tunnel contract still opens and retains its observations.
        var tunnel = FullCampaignCatalog.GetMission("HIST-06");
        StartStoryMission(tunnel);
        Require(_activeContract is null && _feedbackText == Text("city.office.requires_lidar"), "new tunnel job was offered without LiDAR acquisition");
        _activeContract = facadeJob with
        {
            Id = tunnel.MissionId, TitleKey = tunnel.TitleKey, Product = tunnel.ProductType,
            RegionId = "port", AreaId = "port", RequiredFeatureIds = ["survey_control_port_tunnel"],
            Requirements = [new PredicateRequirementSpec("format_valid", true)],
            Reward = facadeJob.Reward with { BaseCoords = tunnel.RewardCoords },
        };
        _activeStorySpec = tunnel;
        _activeMachine = new ContractStateMachine(_activeContract);
        _activeMachine.Accept(_calendar.CalendarTimeSeconds);
        _cityCustody = new BatchCustodyStore();
        _protectedCityPhotos.Clear();
        _lidarEnabled = false;
        PositionAndCapture(new(500754, 7401741.1, 100), 0);
        Require(_fieldSamples.Any(s => s.Quality.Passed && s.ObservedFeatures.Any(f => f.TargetId == "survey_control_port_tunnel")), "scene photo missed the tunnel mark");
        Upload();
        SaveCareer();
        TryLoadCareer();
        ProcessAndDeliverCityContract();
        Require(_activeContract?.Id == "HIST-06" && _protectedCityPhotos.Count == 1 && _ledger.BalanceCoords == paid &&
            !_campaignState.ObjectiveStates.ContainsKey(tunnel.MissionId), "the tunnel paid or unlocked progression from one photo");
        Require(_feedbackText == Text("city.office.requires_lidar"), "the office did not identify LiDAR as the missing product");
        AbandonCityContract();
        Require(_activeContract is null, "unsupported saved job could not be closed");
        _saveManager = previous;
        _citySaveBlocked = false;
        TryLoadCareer();
        Require(original.SequenceEqual(File.ReadAllBytes(previous.CurrentSavePath)), "delivery fixture modified the career save");
        GD.Print("GSA_CITY_DELIVERY_SMOKE_OK protected_only=true rework=true late_quote=true replay=once tunnel_photo=rejected facades=20 drains=12 sidewalks=8 bicycle=true scooter=true");
    }
}
