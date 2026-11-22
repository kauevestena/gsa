using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.World;
using Gsa.Missions.Campaign;
using Gsa.Missions.Contracts;
using Godot;

namespace Gsa.Engine.World;

public partial class SaoDatumCityScene
{
    private void RunWorksSurveySmoke()
    {
        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Works survey smoke: " + message);
        }

        var mission = FullCampaignCatalog.GetMission(PortWorksSurvey.MissionId);
        var finale = FullCampaignCatalog.GetMission("HIST-11");
        var sides = PortWorksSurvey.Sides;
        var pad = PortWorksSurvey.UploadPads[0];
        var office = _manifestObjectsById["office_entry"].PositionEnu - Vector3d.UnitY * 4;
        const LocomotionMode mode = LocomotionMode.Kombi;
        const double dt = 1.0 / 60;
        var wallet = _ledger.BalanceCoords;
        Require(CityMissionCompleted(SerraBoundarySurvey.MissionId) && !finale.Preconditions.All(CityMissionCompleted) &&
            !CityPlatformStoryUnlocked(LocomotionMode.Van), "the Van or finale opened before Obra Fantasma");
        SwitchPlatform(LocomotionMode.Scooter);
        StartStoryMission(mission);
        Require(_activeContract is null && _feedbackText == Text("city.story.requires_platform"), "a scooter took the perimeter contract");
        SwitchPlatform(mode);

        void Position(Vector3d p, double heading = 0)
        {
            _carSim = null;
            _locomotion.RepositionSafeAfterRecovery(p, mode, heading, true, false);
            UpdateSectorStreaming(p);
        }

        void Scan(Vector3d from, Vector3d to)
        {
            var delta = to - from;
            var heading = Math.Atan2(delta.Y, delta.X);
            var steps = (int)Math.Ceiling(delta.Length / (PortWorksSurvey.SurveySpeed * dt));
            _lidarEnabled = true;
            Position(from, heading);
            for (var i = 0; i <= steps; i++)
            {
                var p = from + delta * (i / (double)steps);
                _locomotion.RepositionSafeAfterRecovery(p, mode, heading, false, false);
                _tick++;
                var weather = _calendar.StepPhysical(dt);
                StepLiveLidar(dt, p, heading, mode, weather.IsRaining);
            }
            Require(_pendingLidarReturns > 0 && _lidarTicksWithoutCapacity == 0,
                $"field storage could not hold the survey (pending {_pendingLidarReturns}, dropped {_lidarTicksWithoutCapacity})");
        }

        void StepTransfer(int count)
        {
            for (var i = 0; i < count; i++)
            {
                _calendar.StepPhysical(dt);
                StepCityFactionHazards(dt, CurrentPositionEnu(), 0, mode);
            }
        }

        _calmPolicy.SetCalmCityEnabled(true);
        StartStoryMission(mission);
        Require(IsWorksSurvey(_activeContract) && _activeContract!.Calm && !_lidarEnabled, "Calm City cannot take the survey");
        var calmReward = _activeContract!.Reward.BaseCoords;
        Scan(sides[0].Start, sides[0].Start + Vector3d.UnitX * 16);
        Position(pad);
        OffloadCityData();
        StepTransfer(240);
        Require(_worksUpload is null && _protectedLidarReturns > 0 && _pursuerVisual is null && _cityHazards.PursuitRemaining == 0,
            "Calm City field upload failed or triggered pressure");
        AbandonCityContract();
        _calmPolicy.SetCalmCityEnabled(false);
        StartStoryMission(mission);
        Require(IsWorksSurvey(_activeContract) && !_activeContract!.Calm && _activeContract.Reward.BaseCoords == calmReward,
            "normal and Calm City differ in survey or reward");

        Scan(sides[0].Start, sides[0].End);
        Scan(sides[1].Start, sides[1].End);
        Position(office);
        OffloadCityData();
        ProcessAndDeliverCityContract();
        Require(_activeMachine?.State == ContractState.Retrabalho && _lastWorksReview is { Cloud.Passed: false, FieldUploads: 0 } &&
            _lastWorksReview.Sides.Count(side => side.Fraction >= .90) == 2 && _ledger.BalanceCoords == wallet,
            "an incomplete perimeter passed: " + WorksReviewText());
        Scan(sides[2].Start, sides[2].End);
        Scan(sides[3].Start, sides[3].End);
        Position(office);
        OffloadCityData();
        ProcessAndDeliverCityContract();
        Require(_activeMachine?.State == ContractState.Retrabalho && _lastWorksReview is { Cloud.Passed: true, FieldUploads: 0 } &&
            _ledger.BalanceCoords == wallet && !CityPlatformStoryUnlocked(LocomotionMode.Van),
            "office-only delivery did not hit the field upload gate: " + WorksReviewText());

        Scan(sides[0].End - Vector3d.UnitX * 20, sides[0].End);
        var pending = _pendingLidarReturns;
        var protectedBefore = _protectedLidarReturns;
        Position(pad);
        OffloadCityData();
        StepTransfer(60);
        Require(_worksUpload is { RemainingSeconds: > 0 } && _pursuerVisual is not null && !_citySafeZone &&
            _protectedLidarReturns == protectedBefore, "the exposed station lacked pressure or protected data prematurely");
        Position(pad + Vector3d.UnitX * 7);
        StepTransfer(1);
        Require(_worksUpload is null && _pendingLidarReturns == pending && _protectedLidarReturns == protectedBefore,
            "leaving the pad committed or lost pending data");

        Position(pad);
        OffloadCityData();
        StepTransfer(60);
        SaveCareer();
        TryLoadCareer();
        Require(_worksUpload is { PadIndex: 0, RemainingSeconds: > 2.9 and < 3.1 } && _cityHazards.PursuitRemaining > 0 &&
            _pendingLidarReturns == pending && _protectedLidarReturns == protectedBefore, "reload did not preserve upload progress and pressure");
        Position(pad);
        StepTransfer(180);
        Require(_worksUpload is null && _pendingLidarReturns == 0 && _protectedLidarReturns == protectedBefore + pending &&
            ReadProtectedCityLidarBatches().Count(batch => batch.WorksFieldPad == 0) == 1, "the field transfer did not commit exactly once");
        var checksums = _cityCustody.Entries.Select(batch => batch.PayloadSha256).Order().ToArray();
        HandleCityVehicleLoss();
        TryLoadCareer();
        Require(_cityCustody.Entries.Select(batch => batch.PayloadSha256).Order().SequenceEqual(checksums),
            "vehicle loss or reload changed protected scans");

        ProcessAndDeliverCityContract();
        var review = _lastWorksReview;
        Require(review is { Delivery.Passed: true, FieldUploads: 1 } && _activeContract is null && _ledger.BalanceCoords > wallet &&
            CityMissionCompleted(mission.MissionId) && CityPlatformStoryUnlocked(LocomotionMode.Van) &&
            _campaignState.UnlockedEquipment.Contains("van_workstation_rack") && finale.Preconditions.All(CityMissionCompleted),
            "the completed survey did not unlock the Van and finale prerequisite: " + WorksReviewText());
        var paid = _ledger.BalanceCoords;
        TryLoadCareer();
        ProcessAndDeliverCityContract();
        Require(_activeContract is null && _ledger.BalanceCoords == paid && CityPlatformStoryUnlocked(LocomotionMode.Van),
            "settlement replayed or the Van gate was lost");
        GD.Print(FormattableString.Invariant($"GSA_PORT_WORKS_DELIVERY_SMOKE_OK sides=4 coverage={review!.Sides.Min(side => side.Fraction):P0} density={review.Cloud.MeanDensityPointsPerM2:F1} worst_rmse_m={review.Cloud.WorstPlaneRmseMetres:F3} field_uploads={review.FieldUploads} loss=protected van=unlocked"));
    }
}
