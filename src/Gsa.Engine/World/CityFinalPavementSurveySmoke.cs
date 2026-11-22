using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.World;
using Gsa.Missions.Campaign;
using Gsa.Missions.Contracts;
using Gsa.Missions.Quality;
using Godot;

namespace Gsa.Engine.World;

public partial class SaoDatumCityScene
{
    private void RunFinalPavementSurveySmoke()
    {
        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Final city survey smoke: " + message);
        }

        var mission = FullCampaignCatalog.GetMission(FinalCityPavementSurvey.MissionId);
        var sectors = FinalCityPavementSurvey.Sectors;
        var office = _manifestObjectsById["office_entry"].PositionEnu - Vector3d.UnitY * 4;
        const LocomotionMode mode = LocomotionMode.Van;
        const double dt = 1.0 / 60;

        void Position(Vector3d p)
        {
            _carSim = null;
            _lastSpeedMps = 0;
            _locomotion.RepositionSafeAfterRecovery(p, mode, 0, false, false);
            UpdateSectorStreaming(p);
        }

        void Scan(FinalCityPavementSector sector)
        {
            _lidarEnabled = true;
            Position(sector.DrivePosition(sector.StartEast - 2));
            _lastSpeedMps = FinalCityPavementSurvey.SurveySpeed;
            var startTick = _tick;
            for (var x = sector.StartEast - 2; x <= sector.EndEast + 2; x += FinalCityPavementSurvey.SurveySpeed * dt)
            {
                var p = sector.DrivePosition(x);
                _locomotion.RepositionSafeAfterRecovery(p, mode, 0, false, false);
                _tick++;
                _calendar.StepPhysical(dt);
                StepLiveLidar(dt, p, 0, mode, _calendar.IsRaining);
            }
            Require(_pendingLidar.Count > 0 && _pendingLidar[0].PhysicsTick > startTick &&
                _pendingLidar.Any(slice => slice.PavementFrame is { IsOccluded: false }) && _lidarTicksWithoutCapacity == 0,
                $"missing L6 or camera evidence in {sector.Manifest.RegionId}: pending {_pendingLidarReturns}, skipped {_lidarTicksWithoutCapacity}");
        }

        SetCityPointView(true);
        Require(!_cityPointViewEnabled && _worldRoot.Visible && !FinalCloudFilterUnlocked() &&
            mission.Preconditions.All(CityMissionCompleted), "the postgame filter opened early or HIST-10 did not carry over");
        SwitchPlatform(LocomotionMode.Kombi);
        StartStoryMission(mission);
        Require(_activeContract is null && _feedbackText == Text("city.story.requires_platform"), "the Kombi took the L6 contract");
        // Model the savings from optional contracts; the fleet shop still enforces the real eligibility and price.
        var savings = PlatformProfile.Van.PriceCoords;
        var sideWork = new PaymentQuote("fixture_side_work", true, false, false, 5, 1, 1, 0, 0,
            savings, savings, savings, 0, 0, [], []);
        _ledger.RecordContractSettlement("finale_fixture_savings", sideWork.ContractId, _calendar.CalendarTimeSeconds, sideWork);
        Require(TryPurchaseCityPlatform(mode), $"the Van was unavailable after the campaign prerequisite (reputation {_progression.GlobalReputation})");
        var wallet = _ledger.BalanceCoords;

        _calmPolicy.SetCalmCityEnabled(false);
        StartStoryMission(mission);
        Require(_activeContract is { Calm: false } && _activeMachine?.Snapshot is not null, "standard finale was unavailable");
        var normal = _activeContract!;
        var schedule = _activeMachine!.Snapshot!;
        Require(schedule.DueAtSeconds - schedule.AcceptedAtSeconds == 24 * 3600 &&
            schedule.ExpiresAtSeconds - schedule.AcceptedAtSeconds == 30 * 3600, "the 24 h / 30 h policy was lost");
        Position(sectors[0].Start);
        _lidarEnabled = true;
        StepCityFactionHazards(dt, sectors[0].Start, FinalCityPavementSurvey.SurveySpeed, mode);
        Require(_cityHazards.ActiveZoneId is not null && !_calmPolicy.IsProtectionActive, "standard survey did not share the live city pressure rules");
        _calendar.AdvanceCalendarSeconds(schedule.ExpiresAtSeconds - _calendar.CalendarTimeSeconds + 1);
        Position(office);
        ProcessAndDeliverCityContract();
        Require(_activeMachine.State == ContractState.Expirado && _ledger.BalanceCoords == wallet && !FinalCloudFilterUnlocked(),
            "expired finale gave a reward or filter");
        AbandonCityContract();

        _calmPolicy.SetCalmCityEnabled(true);
        StartStoryMission(mission);
        Require(_activeContract is { Calm: true } && _activeContract.Reward.BaseCoords == normal.Reward.BaseCoords &&
            _activeContract.Requirements.SequenceEqual(normal.Requirements), "Calm City changed the engineering gates or reward");
        _gtaHud.HideCutsceneLetterbox();
        _calendar.SetWeather(isRaining: false);
        Position(sectors[0].Start);
        StepCityFactionHazards(dt, sectors[0].Start, FinalCityPavementSurvey.SurveySpeed, mode);
        Require(_calmPolicy.IsProtectionActive && _cityHazards.PursuitRemaining == 0, "Calm City retained faction pressure");

        Scan(sectors[0]);
        var pending = _pendingLidarReturns;
        Position(sectors[1].Upload);
        OffloadCityData();
        Require(_pendingLidarReturns == pending && _finaleUploadedSectors.Count == 0, "a neighbouring pad accepted the buffer");
        SaveCareer();
        TryLoadCareer();
        Require(!_lidarEnabled && _pendingLidarReturns == pending && PendingFinaleSector == sectors[0].Manifest.SectorId &&
            _calmPolicy.IsProtectionActive, "a reload changed pending evidence, Calm City, or the paused scanner");
        Position(sectors[0].Upload);
        OffloadCityData();
        Require(_pendingLidarReturns == 0 && _finaleUploadedSectors.SetEquals([sectors[0].Manifest.SectorId]),
            "the sector upload did not protect and release buffer storage");
        Position(office);
        ProcessAndDeliverCityContract();
        Require(_activeMachine?.State == ContractState.Retrabalho && _lastFinalSurveyReview?.Sectors.Count(s => s.Passed) == 1 &&
            _ledger.BalanceCoords == wallet && !FinalCloudFilterUnlocked(), "partial or invalid first sector: " + FinalSurveyReviewText());
        var digest = _lastFinalSurveyReview!.Sectors[0].Cloud.ScientificDigestSha256;
        TryLoadCareer();
        ProcessAndDeliverCityContract();
        Require(_lastFinalSurveyReview?.Sectors[0].Cloud.ScientificDigestSha256 == digest && _finaleUploadedSectors.Count == 1,
            "the protected sector changed across reload");

        foreach (var sector in sectors.Skip(1))
        {
            Scan(sector);
            Position(sector.Upload);
            OffloadCityData();
        }
        Position(office);
        var active = _activeMachine!.Snapshot!;
        _calendar.AdvanceCalendarSeconds(active.DueAtSeconds + 3600 - _calendar.CalendarTimeSeconds);
        ProcessAndDeliverCityContract();
        var report = _lastFinalSurveyReview;
        var quote = report is null ? null : DeliveryQuoteCalculator.Calculate(active.Definition, report.Delivery,
            active.AcceptedAtSeconds, _calendar.CalendarTimeSeconds);
        Require(report is { Delivery.Passed: true } && report.Sectors.All(sector => sector.Passed) &&
            quote is { Eligible: true, LatePenaltyFraction: .05 } && _ledger.BalanceCoords == wallet + quote.NetCoords &&
            _activeContract is null && _campaignState.UnlockedEquipment.Contains(FinalCityPavementSurvey.CloudFilterUnlock),
            "the protected four-sector profile did not settle with the expected penalty and unlock: " + _feedbackText + " " + FinalSurveyReviewText());
        var paid = _ledger.BalanceCoords;
        TryLoadCareer();
        RefreshTechnicalTablet();
        ProcessAndDeliverCityContract();
        Require(_cityPointViewButton.Visible && _ledger.BalanceCoords == paid && _activeContract is null &&
            CityMissionCompleted(mission.MissionId), "the filter or single payout was lost after reload");
        var protectedChecksums = _cityCustody.Entries.Select(entry => entry.PayloadSha256).ToArray();
        SetCityPointView(true);
        StepCityPointView(CurrentPositionEnu());
        Require(_cityPointView is { Visible: true, Mesh: not null } && !_worldRoot.Visible &&
            !_lidarCloudMesh.Visible && protectedChecksums.SequenceEqual(_cityCustody.Entries.Select(entry => entry.PayloadSha256)),
            "the point view did not render independently of the survey data");
        SetCityPointView(false);
        Require(_worldRoot.Visible && _cityPointView is { Visible: false }, "disabling the point view did not restore the city");
        GD.Print(FormattableString.Invariant($"GSA_FINAL_CITY_PAVEMENT_SMOKE_OK sectors=4 density_min={report!.Sectors.Min(sector => sector.Cloud.MeanDensityPointsPerM2):F1} checkpoint_rmse_max_m={report.Sectors.Max(sector => sector.Cloud.WorstPlaneRmseMetres):F3} profile_coverage_min={report.Sectors.Min(sector => sector.Pavement.SectionCoverageFraction):P0} late=true expiry=true calm=true filter=unlocked"));
    }
}
