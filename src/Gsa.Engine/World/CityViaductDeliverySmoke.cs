using System.Globalization;
using Gsa.Core.Geodesy;
using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.World;
using Gsa.Missions.Campaign;
using Gsa.Missions.Contracts;
using Godot;

namespace Gsa.Engine.World;

public partial class SaoDatumCityScene
{
    private void RunViaductDeliverySmoke()
    {
        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Port viaduct smoke: " + message);
        }

        const LocomotionMode mode = LocomotionMode.Kombi;
        var mission = FullCampaignCatalog.GetMission(PortViaductSurvey.MissionId);
        var west = PortViaductSurvey.WestPad;
        var east = PortViaductSurvey.EastPad;
        var middle = (west + east) / 2;
        var wallet = _ledger.BalanceCoords;
        Require(!CityPlatformStoryUnlocked(LocomotionMode.Pickup) && !_campaignState.UnlockedRegions.Contains("serra"),
            "viaduct rewards were available before delivery");
        StartStoryMission(mission);
        RefreshTabletContents();
        Require(IsViaductCollection(_activeContract) && !_lidarEnabled && _viaductForm.Visible,
            "contract did not start with a paused scanner and an office form");
        _carSim = null;
        _lidarEnabled = true;
        UpdateSectorStreaming(west);

        void Reposition(Vector3d p) => _locomotion.RepositionSafeAfterRecovery(p, mode, 0, false, false);
        void Drive(Vector3d from, Vector3d to)
        {
            const double dt = 1.0 / 60;
            var steps = (int)Math.Ceiling((to - from).Length / .05);
            for (var i = 0; i <= steps; i++)
            {
                var p = from + (to - from) * (i / (double)steps);
                Reposition(p);
                _tick++;
                _calendar.StepPhysical(dt);
                StepLiveLidar(dt, p, 0, mode, false);
            }
        }

        // Use comma decimals to exercise Portuguese input on an English smoke runner.
        void EnterHeights(bool swapped)
        {
            static string Metres(double value) => value.ToString("F3", CultureInfo.InvariantCulture).Replace('.', ',');
            for (var i = 0; i < PortViaductSurvey.VerticalControls.Count; i++)
            {
                var point = PortViaductSurvey.VerticalControls[i];
                var H = LocalGeoidModel.ToOrthometricHeight(point);
                _viaductHeightFields[2 * i].Text = Metres(swapped ? H : point.Z);
                _viaductHeightFields[2 * i + 1].Text = Metres(swapped ? point.Z : H);
            }
        }

        Drive(west, middle);
        Reposition(west);
        OffloadCityData();
        EnterHeights(swapped: false);
        ProcessAndDeliverCityContract();
        Require(_activeMachine?.State == ContractState.Retrabalho && _lastViaductReview is { Cloud.Passed: false, Heights.Compliant: true } &&
            _ledger.BalanceCoords == wallet, "correct heights passed with only half the surveyed underside: " + ViaductReviewText());

        Drive(middle, east);
        EnterHeights(swapped: true);
        ProcessAndDeliverCityContract();
        Require(_lastViaductReview?.Cloud.Passed == false, "pending returns entered quality control");
        OffloadCityData();
        ProcessAndDeliverCityContract();
        Require(_activeMachine?.State == ContractState.Retrabalho &&
            _lastViaductReview is { Cloud.Passed: true, Heights.ReasonKey: "cq.error.vertical_reference_h_and_orthometric_H_swapped" } &&
            _ledger.BalanceCoords == wallet && !CityPlatformStoryUnlocked(LocomotionMode.Pickup) &&
            !_campaignState.UnlockedRegions.Contains("serra"), "swapped heights did not require rework: " + ViaductReviewText());
        var protectedReturns = _protectedLidarReturns;
        var wrongDraft = _viaductHeightFields.Select(field => field.Text).ToArray();
        TryLoadCareer();
        Require(_activeMachine?.State == ContractState.Retrabalho && _protectedLidarReturns == protectedReturns &&
            wrongDraft.SequenceEqual(_viaductHeightFields.Select(field => field.Text)), "rework reload lost data or the height draft");
        EnterHeights(swapped: false);
        ProcessAndDeliverCityContract();
        var report = ViaductReviewText();
        Require(_lastViaductReview?.Delivery.Passed == true && _activeContract is null && _ledger.BalanceCoords > wallet &&
            CityMissionCompleted(mission.MissionId) && CityPlatformStoryUnlocked(LocomotionMode.Pickup) &&
            _campaignState.UnlockedPlatforms.Contains("pickup") && _campaignState.UnlockedRegions.Contains("serra"),
            "correct heights with the same cloud failed to settle or unlock the pickup and Serra: " + report);

        var paid = _ledger.BalanceCoords;
        TryLoadCareer();
        StartStoryMission(mission);
        ProcessAndDeliverCityContract();
        Require(_activeContract is null && _ledger.BalanceCoords == paid && CityMissionCompleted(mission.MissionId) &&
            _campaignState.UnlockedRegions.Contains("serra"), "settlement replayed or Serra was lost after reload");
        GD.Print("GSA_PORT_VIADUCT_DELIVERY_SMOKE_OK " + report.Replace('\n', ' '));
    }
}
