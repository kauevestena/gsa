using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.World;
using Gsa.Missions.Campaign;
using Gsa.Missions.Contracts;
using Godot;

namespace Gsa.Engine.World;

public partial class SaoDatumCityScene
{
    private void RunBoundarySurveySmoke()
    {
        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Boundary survey smoke: " + message);
        }

        var mission = FullCampaignCatalog.GetMission(SerraBoundarySurvey.MissionId);
        var next = FullCampaignCatalog.GetMission("HIST-10");
        var sites = SerraBoundarySurvey.Sites;
        var pads = SerraBoundarySurvey.UploadPads;
        var wallet = _ledger.BalanceCoords;
        Require(CityMissionCompleted(WaterfrontRainSurvey.MissionId) && !next.Preconditions.All(CityMissionCompleted),
            "campaign gate opened before the Act 2 closer");

        SwitchPlatform(LocomotionMode.Scooter);
        StartStoryMission(mission);
        Require(_activeContract is null && _feedbackText == Text("city.story.requires_platform"), "a scooter took the job");

        void Position(Vector3d position, LocomotionMode mode)
        {
            _carSim = null;
            _locomotion.RepositionSafeAfterRecovery(position, mode, 0, true, false);
            UpdateSectorStreaming(position);
        }

        void Shot(SerraBoundarySite site, int station, LocomotionMode mode, double speed = 0)
        {
            var pos = site.Station(station);
            Position(pos, mode);
            _tick += 30;
            _calendar.StepPhysical(.5);
            CaptureCityPhoto(pos, 0, speed, _calendar.ExportSnapshot());
            StepCityFactionHazards(1.0 / 60, pos, speed, mode);
            if (speed <= SerraBoundarySurvey.StopSpeed)
                Require(_pendingBoundaryShots.Any(shot => shot.MonumentId == site.Id && shot.StationIndex == station),
                    $"station {site.Id}/{station} could not record: {_feedbackText}");
        }

        SwitchPlatform(LocomotionMode.Pickup);
        _calmPolicy.SetCalmCityEnabled(true);
        StartStoryMission(mission);
        Require(IsBoundarySurvey(_activeContract) && _activeContract!.Calm && _activeContract.RequiredFeatureIds.Count == 6,
            "Calm City did not receive the six monuments");
        var calmReward = _activeContract!.Reward;
        var physical = sites.Select(site => _manifestObjectsById[site.Id].PositionEnu).ToArray();
        Shot(sites[0], 0, LocomotionMode.Pickup);
        Shot(sites[0], 1, LocomotionMode.Pickup);
        Require(!_boundaryPursuitTriggered && _pursuerVisual is null && _cityHazards.PursuitRemaining == 0,
            "Calm City triggered pursuit");
        Require(_locomotion.UsedVirtualStorageUnits == 2, "station occupancy did not reserve storage");
        AbandonCityContract();
        Require(_locomotion.UsedVirtualStorageUnits == 0, "abandonment did not release unuploaded storage");

        _calmPolicy.SetCalmCityEnabled(false);
        SwitchPlatform(LocomotionMode.Car);
        StartStoryMission(mission);
        Require(IsBoundarySurvey(_activeContract) && !_activeContract!.Calm && _activeContract.Reward.BaseCoords == calmReward.BaseCoords &&
            _activeContract.Reward.Reputation == calmReward.Reputation && _activeContract.Reward.BaseXp == calmReward.BaseXp &&
            physical.SequenceEqual(sites.Select(site => _manifestObjectsById[site.Id].PositionEnu)),
            "retry changed the monuments or Calm City rewards");
        Shot(sites[0], 0, LocomotionMode.Car, speed: 2);
        Require(_pendingBoundaryShots.Count == 0 && _feedbackText == Text("city.hazards.stop_first"), "a moving occupation was accepted");
        Shot(sites[0], 0, LocomotionMode.Car);
        Shot(sites[0], 0, LocomotionMode.Car);
        Require(_pendingBoundaryShots.Count == 1 && _locomotion.UsedVirtualStorageUnits == 1 && !_boundaryPursuitTriggered,
            "duplicate station counted as a pair or double-booked storage");
        Shot(sites[0], 1, LocomotionMode.Car);
        Require(_boundaryPursuitTriggered && _pursuerVisual is not null && _cityHazards.PursuitRemaining > 0,
            "normal route did not create a pursuer after the first pair");
        TryLoadCareer();
        Require(_pendingBoundaryShots.Count == 2 && _boundaryPursuitTriggered && _cityHazards.PursuitRemaining > 0 &&
            physical.SequenceEqual(sites.Select(site => _manifestObjectsById[site.Id].PositionEnu)),
            "reload lost an unuploaded pair, pursuit, or displaced world positions");

        Position(pads[0], LocomotionMode.Car);
        StepCityFactionHazards(1.0 / 60, pads[0], 0, LocomotionMode.Car);
        Require(_cityHazards.PursuitRemaining == 0 && _pursuerVisual is null, "upload point did not stop pursuit");
        OffloadCityData();
        var firstBatch = _cityCustody.Entries.Single().PayloadSha256;
        Shot(sites[1], 0, LocomotionMode.Car);
        Shot(sites[1], 1, LocomotionMode.Car);
        ProcessAndDeliverCityContract();
        Require(_lastBoundaryReview?.Delivery.ValidSampleCount == 2 && _activeMachine?.State == ContractState.Retrabalho &&
            _ledger.BalanceCoords == wallet && !CityMissionCompleted(mission.MissionId), "pending observations entered the office or partial work paid");
        HandleCityVehicleLoss();
        TryLoadCareer();
        Require(_pendingBoundaryShots.Count == 0 && _protectedBoundaryShots.Count == 2 && _boundaryPursuitTriggered &&
            _cityCustody.Entries.Single().PayloadSha256 == firstBatch,
            "vehicle loss or reload changed the protected batch");

        SwitchPlatform(LocomotionMode.Pickup);
        foreach (var site in sites.Skip(1))
        {
            Shot(site, 0, LocomotionMode.Pickup);
            Shot(site, 1, LocomotionMode.Pickup);
        }
        Position(pads[^1], LocomotionMode.Pickup);
        OffloadCityData();
        ProcessAndDeliverCityContract();
        var review = _lastBoundaryReview;
        Require(review is { Delivery.Passed: true, Delivery.ValidSampleCount: 12 } && review.Monuments.All(mark => mark.Displaced) &&
            _activeContract is null && CityMissionCompleted(mission.MissionId) && next.Preconditions.All(CityMissionCompleted) &&
            _campaignState.UnlockedEquipment.Contains("act2_closure_seal") && _ledger.BalanceCoords > wallet &&
            _cityCustody.Entries.Any(entry => entry.PayloadSha256 == firstBatch), "boundary review did not close Act 2: " + BoundaryReviewText());
        var paid = _ledger.BalanceCoords;
        TryLoadCareer();
        StartStoryMission(mission);
        ProcessAndDeliverCityContract();
        Require(_activeContract is null && _ledger.BalanceCoords == paid && CityMissionCompleted(mission.MissionId),
            "settlement replayed or progression was lost");
        GD.Print(FormattableString.Invariant($"GSA_SERRA_BOUNDARY_DELIVERY_SMOKE_OK monuments=6 readings=12 max_repeat_m={review!.Monuments.Max(mark => mark.RepeatErrorMetres):F3} displacement_m={review.Monuments.Min(mark => mark.HorizontalDisplacementMetres):F2}..{review.Monuments.Max(mark => mark.HorizontalDisplacementMetres):F2} pursuit=active calm=suppressed loss=protected act2=closed"));
    }
}
