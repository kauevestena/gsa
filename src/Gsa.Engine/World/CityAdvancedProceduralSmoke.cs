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
    private void RunCityAdvancedProceduralSmoke()
    {
        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Advanced procedural smoke: " + message);
        }

        void Position(Vector3d p, LocomotionMode mode)
        {
            _carSim = null;
            _lastSpeedMps = 0;
            _locomotion.RepositionSafeAfterRecovery(p, mode, 0, false, false);
            UpdateSectorStreaming(p);
        }

        // Verify full-campaign board generation when all regions and L6 Van are unlocked
        _campaignState.UnlockedRegions.UnionWith(["old_center", "port", "waterfront", "serra"]);
        SwitchPlatform(LocomotionMode.Van);
        GenerateContractOffers();
        Require(_offeredContracts.Any(o => o.TemplateId == "PROC-TUNEL" && SupportsCityContract(o.Contract)),
            "PROC-TUNEL missing from unlocked L6 board");
        Require(_offeredContracts.Any(o => o.TemplateId == "PROC-ENERGIA" && SupportsCityContract(o.Contract)),
            "PROC-ENERGIA missing from unlocked L6 board");
        Require(_offeredContracts.Any(o => o.TemplateId == "PROC-OBRAS" && SupportsCityContract(o.Contract)),
            "PROC-OBRAS missing from unlocked L6 board");
        Require(_offeredContracts.Count(o => o.TemplateId == Act3PavementProceduralGenerator.TemplateId) == 4,
            "PROC-PAVIMENTO did not generate all 4 regional offers");

        // 1. Playable PROC-ENERGIA (Serra utility poles inventory with stereo camera + Serra upload)
        _calmPolicy.SetCalmCityEnabled(true);
        _calendar.SetWeather(isRaining: false);
        var energyOffer = BuildCityAdvancedOffer("PROC-ENERGIA", 2000UL);
        Require(energyOffer.Contract.AreaId == SerraPowerArea && energyOffer.Contract.RequiredFeatureIds.Count > 0,
            "PROC-ENERGIA did not select grounded Serra pole targets");
        SwitchPlatform(LocomotionMode.Pickup);
        Require(TryAcceptCityProceduralOffer(energyOffer), "could not accept PROC-ENERGIA");
        var energyJob = _activeContract!;
        var walletBeforeEnergy = _ledger.BalanceCoords;

        foreach (var fid in energyJob.RequiredFeatureIds)
        {
            var polePos = _manifestObjectsById[fid].PositionEnu;
            foreach (var dx in new[] { -4.0, 4.0 })
            {
                var viewPos = new Vector3d(polePos.X + dx, polePos.Y - 10.0, GetTerrainElevationEnu(polePos.X + dx, polePos.Y - 10.0));
                var heading = Math.Atan2(polePos.Y - viewPos.Y, polePos.X - viewPos.X);
                _carSim = null;
                _navigationEnvironmentAt = ResolveCityNavigationEnvironment;
                ResetCityNavigation();
                // Warm up LiveNavigationRig over 1 s of steady approach
                for (var step = 0; step < 60; step++)
                {
                    var p = viewPos - new Vector3d(Math.Cos(heading), Math.Sin(heading), 0.0) * ((59 - step) * 0.08);
                    var grounded = new Vector3d(p.X, p.Y, GetTerrainElevationEnu(p.X, p.Y));
                    _locomotion.RepositionSafeAfterRecovery(grounded, LocomotionMode.Pickup, heading, false, false);
                    _tick++;
                    _calendar.StepPhysical(1.0 / 60.0);
                    StepCityNavigation(1.0 / 60.0, grounded, heading, LocomotionMode.Pickup, false);
                }
                while (!_camScheduler.IsCaptureTick(_tick)) _tick++;
                CaptureCityPhoto(viewPos, heading, 0.0, _calendar.ExportSnapshot());
            }
        }
        Require(energyJob.RequiredFeatureIds.All(id => _observedTargetIds.Contains(id)),
            "PROC-ENERGIA missed grounded Serra pole observations: " + _feedbackText);

        var serraPad = SerraBoundarySurvey.UploadPads[0];
        Position(serraPad, LocomotionMode.Pickup);
        OffloadCityData();
        Require(_fieldSamples.Count == 0 && _protectedCityPhotos.Count > 0, "PROC-ENERGIA failed Serra pad offload");

        var officePos = _manifestObjectsById["office_entry"].PositionEnu - Vector3d.UnitY * 4;
        Position(officePos, LocomotionMode.Pickup);
        ProcessAndDeliverCityContract();
        Require(_activeContract is null && _ledger.BalanceCoords > walletBeforeEnergy &&
            _progression.SettledContractIds.Contains(energyJob.Id),
            "PROC-ENERGIA failed delivery: " + _feedbackText + " " + CitySurveyReviewText());

        // 2. Playable PROC-OBRAS (Serra earthworks L5 LiDAR scan + single-sector cloud CQ)
        _calendar.SetWeather(isRaining: false);
        var worksOffer = BuildCityAdvancedOffer("PROC-OBRAS", 2001UL);
        Require(TryAcceptCityProceduralOffer(worksOffer), "could not accept PROC-OBRAS");
        var worksJob = _activeContract!;
        var walletBeforeWorks = _ledger.BalanceCoords;
        var worksSector = SerraEarthworksSector;
        const double dt = 1.0 / 60.0;

        Position(worksSector.DrivePosition(worksSector.StartEast - 2), LocomotionMode.Pickup);
        _lidarEnabled = true;
        _lastSpeedMps = FinalCityPavementSurvey.SurveySpeed;
        for (var x = worksSector.StartEast - 2; x <= worksSector.EndEast + 2; x += FinalCityPavementSurvey.SurveySpeed * dt)
        {
            var p = worksSector.DrivePosition(x);
            _locomotion.RepositionSafeAfterRecovery(p, LocomotionMode.Pickup, 0, false, false);
            UpdateSectorStreaming(p);
            _tick++;
            _calendar.StepPhysical(dt);
            StepLiveLidar(dt, p, 0, LocomotionMode.Pickup, _calendar.IsRaining);
        }
        Require(_pendingLidarReturns > 0, "PROC-OBRAS captured zero L5 LiDAR returns");

        Position(worksSector.Upload, LocomotionMode.Pickup);
        OffloadCityData();
        Require(_pendingLidarReturns == 0 && _protectedLidarReturns > 0, "PROC-OBRAS failed sector offload");

        Position(officePos, LocomotionMode.Pickup);
        ProcessAndDeliverCityContract();
        Require(_activeContract is null && _lastSerraEarthworksCloud is { Passed: true } &&
            _ledger.BalanceCoords > walletBeforeWorks && _progression.SettledContractIds.Contains(worksJob.Id),
            "PROC-OBRAS failed delivery: " + _feedbackText + " " + SerraEarthworksReviewText());

        // 3. Playable PROC-PAVIMENTO (Waterfront L6 Van scan, mid-pass save/reload, never grants HIST-11 postgame filter)
        var hadFinaleDone = _campaignState.ObjectiveStates.TryGetValue(FinalCityPavementSurvey.MissionId, out var prevFinaleObj);
        var hadCloudUnlock = _campaignState.UnlockedEquipment.Remove(FinalCityPavementSurvey.CloudFilterUnlock);
        if (hadFinaleDone) _campaignState.ObjectiveStates.Remove(FinalCityPavementSurvey.MissionId);

        _calendar.SetWeather(isRaining: false);
        SwitchPlatform(LocomotionMode.Van);
        var pavOffer = BuildCityPavementContractOffer("waterfront", 4002UL);
        Require(TryAcceptCityProceduralOffer(pavOffer), "could not accept PROC-PAVIMENTO");
        var pavJob = _activeContract!;
        var walletBeforePav = _ledger.BalanceCoords;
        var pavSector = FinalCityPavementSurvey.Sectors[2]; // waterfront
        var midEast = pavSector.StartEast + 32.0;

        Position(pavSector.DrivePosition(pavSector.StartEast - 2), LocomotionMode.Van);
        _lidarEnabled = true;
        _lastSpeedMps = FinalCityPavementSurvey.SurveySpeed;
        for (var x = pavSector.StartEast - 2; x <= midEast; x += FinalCityPavementSurvey.SurveySpeed * dt)
        {
            var p = pavSector.DrivePosition(x);
            _locomotion.RepositionSafeAfterRecovery(p, LocomotionMode.Van, 0, false, false);
            UpdateSectorStreaming(p);
            _tick++;
            _calendar.StepPhysical(dt);
            StepLiveLidar(dt, p, 0, LocomotionMode.Van, _calendar.IsRaining);
        }
        var midPending = _pendingLidarReturns;
        Require(midPending > 0, "PROC-PAVIMENTO first half produced zero returns");

        SaveCareer();
        TryLoadCareer();
        Require(!_citySaveBlocked && _citySaveStatus?.Succeeded == true && !_lidarEnabled &&
            _pendingLidarReturns == midPending && PendingFinaleSector == pavSector.Manifest.SectorId,
            "PROC-PAVIMENTO pending buffer lost or scanner stayed active across reload");

        Position(pavSector.DrivePosition(midEast + FinalCityPavementSurvey.SurveySpeed * dt), LocomotionMode.Van);
        _lidarEnabled = true;
        _lastSpeedMps = FinalCityPavementSurvey.SurveySpeed;
        for (var x = midEast + FinalCityPavementSurvey.SurveySpeed * dt; x <= pavSector.EndEast + 2; x += FinalCityPavementSurvey.SurveySpeed * dt)
        {
            var p = pavSector.DrivePosition(x);
            _locomotion.RepositionSafeAfterRecovery(p, LocomotionMode.Van, 0, false, false);
            UpdateSectorStreaming(p);
            _tick++;
            _calendar.StepPhysical(dt);
            StepLiveLidar(dt, p, 0, LocomotionMode.Van, _calendar.IsRaining);
        }

        Position(pavSector.Upload, LocomotionMode.Van);
        OffloadCityData();
        Require(_pendingLidarReturns == 0 && _finaleUploadedSectors.Contains(pavSector.Manifest.SectorId),
            "PROC-PAVIMENTO sector offload failed");

        Position(officePos, LocomotionMode.Van);
        ProcessAndDeliverCityContract();
        Require(_activeContract is null && _lastFinalSurveyReview is { Delivery.Passed: true } &&
            _ledger.BalanceCoords > walletBeforePav && _progression.SettledContractIds.Contains(pavJob.Id),
            "PROC-PAVIMENTO failed delivery: " + _feedbackText + " " + FinalSurveyReviewText());
        Require(!FinalCloudFilterUnlocked(), "procedural PROC-PAVIMENTO unlocked the HIST-11 postgame filter");

        if (hadFinaleDone && prevFinaleObj is not null) _campaignState.ObjectiveStates[FinalCityPavementSurvey.MissionId] = prevFinaleObj;
        if (hadCloudUnlock) _campaignState.UnlockedEquipment.Add(FinalCityPavementSurvey.CloudFilterUnlock);

        GD.Print("GSA_ADVANCED_PROCEDURAL_SMOKE_OK energy=passed earthworks=passed pavement=passed filter_gated=true");
    }
}

