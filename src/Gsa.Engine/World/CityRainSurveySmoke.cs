using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.World;
using Gsa.Missions.Campaign;
using Gsa.Missions.Contracts;
using Gsa.Missions.Economy;
using Gsa.Missions.Persistence;
using Godot;

namespace Gsa.Engine.World;

public partial class SaoDatumCityScene
{
    private void RunRainSurveySmoke()
    {
        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Rain survey smoke: " + message);
        }

        var mission = FullCampaignCatalog.GetMission(WaterfrontRainSurvey.MissionId);
        const LocomotionMode mode = LocomotionMode.Pickup;
        var pads = WaterfrontRainSurvey.UploadPads;
        StartStoryMission(mission);
        Require(_activeContract is null && _feedbackText == Text("city.story.requires_platform"), "a Kombi could take the rainy scan");
        var fleet = _fleet.ExportSnapshot();
        _fleet = FleetProgressionManager.FromSnapshot(fleet with { OwnedPlatforms = fleet.OwnedPlatforms.Append(mode).ToArray() });
        SwitchPlatform(mode);
        var wallet = _ledger.BalanceCoords;

        _calmPolicy.SetCalmCityEnabled(false);
        StartStoryMission(mission);
        Require(_activeContract is { Calm: false } && _calendar.IsRaining && Math.Abs(_calendar.HourOfDay - 21) < 1e-6,
            "normal acceptance did not start the storm at 21:00");
        var normalJob = _activeContract!;
        AbandonCityContract();
        _calmPolicy.SetCalmCityEnabled(true);
        StartStoryMission(mission);
        RefreshTabletContents();
        Require(_activeContract is { Calm: true } calm && calm.Product == normalJob.Product && calm.Reward.BaseCoords == normalJob.Reward.BaseCoords &&
            calm.Reward.BaseXp == normalJob.Reward.BaseXp && calm.Reward.Reputation == normalJob.Reward.Reputation &&
            calm.Reward.StoryUnlocks.SequenceEqual(normalJob.Reward.StoryUnlocks) &&
            _rainFilterButton.Visible && !_lidarEnabled && _calendar.IsRaining && _calendar.IsNight,
            "Calm City changed the weather, product, or reward");
        ToggleCityRain();
        Require(_calendar.IsRaining, "weather toggle bypassed the job conditions");

        _carSim = null;
        _lidarEnabled = true;
        UpdateSectorStreaming(pads[0]);
        const double dt = 1.0 / 60;
        _tick++;
        StepLiveLidar(dt, pads[0], 0, mode, false);
        Require(_pendingLidarReturns == 0, "dry scan entered the rainy contract");

        void Drive(Vector3d from, Vector3d to)
        {
            var steps = (int)Math.Ceiling((to - from).Length / (WaterfrontRainSurvey.SurveySpeed * dt));
            for (var i = 1; i <= steps; i++)
            {
                var p = from + (to - from) * (i / (double)steps);
                _locomotion.RepositionSafeAfterRecovery(p, mode, 0, false, false);
                _tick++;
                var weather = _calendar.StepPhysical(dt);
                StepLiveLidar(dt, p, 0, mode, weather.IsRaining);
            }
            Require(_pendingLidarReturns > 0 && _lidarTicksWithoutCapacity == 0,
                $"scan could not fit between marked pads (pending {_pendingLidarReturns}, dropped ticks {_lidarTicksWithoutCapacity})");
        }

        Drive(pads[0], pads[1]);
        Require(_pendingLidar.Any(slice => slice.Returns.Any(point => point.Rain)), "wet raycasts generated no rain returns");
        OffloadCityData();
        ToggleRainSurveyFilter();
        ProcessAndDeliverCityContract();
        Require(_activeMachine?.State == ContractState.Retrabalho && _lastRainSurveyReview is { FilterSelected: true, Cloud.Passed: false } &&
            _ledger.BalanceCoords == wallet, "a filtered partial survey paid: " + RainSurveyReviewText());

        Drive(pads[1], pads[2]);
        var firstUpload = _protectedLidarReturns;
        ProcessAndDeliverCityContract();
        Require(_lastRainSurveyReview?.InputCount == firstUpload, "unuploaded returns changed the office input");
        OffloadCityData();
        Drive(pads[2], pads[3]);
        OffloadCityData();
        ToggleRainSurveyFilter();
        ProcessAndDeliverCityContract();
        Require(_activeMachine?.State == ContractState.Retrabalho && _lastRainSurveyReview?.FilterSelected == false &&
            _lastRainSurveyReview.Delivery.FailureReasonKeys.Contains("city.rain_survey.apply_filter") &&
            _ledger.BalanceCoords == wallet, "the unfiltered delivery bypassed the filter gate: " + RainSurveyReviewText());

        var checksums = _cityCustody.Entries.Select(batch => batch.PayloadSha256).Order().ToArray();
        ToggleRainSurveyFilter();
        TryLoadCareer();
        Require(_activeMachine?.State == ContractState.Retrabalho && _calendar.IsRaining && _calendar.IsNight && _rainSurveyFilterEnabled &&
            _cityCustody.Entries.Select(batch => batch.PayloadSha256).Order().SequenceEqual(checksums), "reload lost the storm, recipe, or uploads");
        ProcessAndDeliverCityContract();
        var report = RainSurveyReviewText();
        Require(_lastRainSurveyReview is { Delivery.Passed: true, RemovedCount: > 0 } && _activeContract is null &&
            CityMissionCompleted(mission.MissionId) && _ledger.BalanceCoords > wallet &&
            _cityCustody.Entries.Select(batch => batch.PayloadSha256).Order().SequenceEqual(checksums),
            "filtered survey did not settle from the same protected data: " + report);
        var paid = _ledger.BalanceCoords;
        TryLoadCareer();
        StartStoryMission(mission);
        ProcessAndDeliverCityContract();
        Require(_activeContract is null && _ledger.BalanceCoords == paid && CityMissionCompleted(mission.MissionId),
            "settlement replayed after reload");
        GD.Print("GSA_WATERFRONT_RAIN_DELIVERY_SMOKE_OK " + report.Replace('\n', ' '));
    }
}
