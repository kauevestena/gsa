using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Missions.Campaign;
using Gsa.Missions.Contracts;
using Gsa.Missions.Economy;
using Gsa.Missions.Office;
using Gsa.Missions.Persistence;
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
        _ledger = new CareerLedger(100);
        _progression = new CareerProgressionState();
        _officeMode = ProcessingMode.Arcade;
        _rackMisaligned = false;
        _gimbalina = new GimbalinaStabilizer();
        _calendar = new(12 * 3600);
        _campaignState.ObjectiveStates.Clear();
        _campaignState.AppliedCutsceneIds.Clear();
        _campaignState.SkippedCutsceneIds.Clear();

        void PositionAndCapture(Vector3d position, double heading)
        {
            _carSim = null;
            _locomotion.RepositionSafeAfterRecovery(position, LocomotionMode.Handcart, heading, true, false);
            _tick += 30;
            _calendar.StepPhysical(.5);
            while (!_camScheduler.IsCaptureTick(_tick)) _tick++;
            CaptureCityPhoto(position, heading, 0, _calendar.ExportSnapshot());
        }

        var story = FullCampaignCatalog.GetMission("HIST-01");
        StartStoryMission(story);
        var facadeJob = _activeContract ?? throw new InvalidOperationException("Facade contract was not available.");
        ProcessAndDeliverCityContract();
        Require(_feedbackText == Text("city.office.upload_first") && _ledger.BalanceCoords == 100, "an empty submission passed");
        PositionAndCapture(new(500190, 7400000.7, 100), Math.PI / 2);
        Require(_fieldSamples.Any(s => s.Quality.Passed && s.ObservedFeatures.Any(f => f.TargetId == "facade_18")), "scene photo missed its facade");
        ProcessAndDeliverCityContract();
        Require(_feedbackText == Text("city.office.upload_first"), "the office read photos still in the field");
        void Upload()
        {
            var office = _manifestObjectsById["office_entry"].PositionEnu + new Vector3d(0, -4, -1.5);
            _locomotion.RepositionSafeAfterRecovery(office, LocomotionMode.Handcart, 0, false, false);
            OffloadCityData();
            Require(_fieldSamples.Count == 0 && _protectedCityPhotos.Count > 0, "upload did not protect photos");
        }

        Upload();
        ProcessAndDeliverCityContract();
        Require(_activeMachine?.State == ContractState.Retrabalho && _ledger.BalanceCoords == 100, "incomplete survey earned a payout");
        Require(_feedbackText == Text("city.office.missing_photo_targets"), "rework did not explain missing targets");
        PositionAndCapture(new(500200, 7400000.7, 100), Math.PI / 2);
        PositionAndCapture(new(500210, 7400000.7, 100), Math.PI / 2);
        ProcessAndDeliverCityContract();
        Require(_activeMachine?.State == ContractState.Retrabalho && _ledger.BalanceCoords == 100, "unuploaded rework contributed");
        Upload();
        SaveCareer();
        TryLoadCareer();
        Require(_activeMachine?.State == ContractState.Retrabalho, "rework did not survive reload");

        _calendar.AdvanceCalendarSeconds(_activeMachine!.Snapshot!.DueAtSeconds + 3600 - _calendar.CalendarTimeSeconds);
        ProcessAndDeliverCityContract();
        var paid = _ledger.BalanceCoords;
        Require(_activeContract is null && paid == 100 + (int)Math.Floor(facadeJob.Reward.BaseCoords * .95), "valid photos did not pay the late-adjusted quote");
        Require(_campaignState.ObjectiveStates.ContainsKey(story.MissionId), "photo mission did not progress the campaign");
        TryLoadCareer();
        Require(!AcceptCityContract(facadeJob), "settled contract could be reaccepted after reload");
        ProcessAndDeliverCityContract();
        Require(_ledger.BalanceCoords == paid &&
            _ledger.ExportState().Entries.Count(e => e.Kind == LedgerEntryKind.ContractPayout) == 1, "delivery replay paid twice");

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
        GD.Print("GSA_CITY_DELIVERY_SMOKE_OK protected_only=true rework=true late_quote=true replay=once tunnel_photo=rejected");
    }
}
