using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.World;
using Gsa.Missions.Campaign;
using Gsa.Missions.Contracts;
using Gsa.Missions.Economy;
using Gsa.Missions.Gameplay;
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
        _campaignState.UnlockedRegions.Clear();
        _campaignState.UnlockedRegions.Add("old_center");
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
        var signs = FullCampaignCatalog.GetMission("HIST-04");
        var parkEdge = new Vector3d(500600, 7400999, 100);
        var portEdge = parkEdge + Vector3d.UnitY * 2;
        var waterfrontEdge = new Vector3d(501001, 7400500, 100);
        Require(!CityMayEnterRegion(parkEdge, portEdge) && !CityMayEnterRegion(parkEdge, waterfrontEdge) &&
            !CityPlatformStoryUnlocked(LocomotionMode.Car), "Act 2 access was open at the start");
        GenerateContractOffers();
        Require(_offeredContracts.Count == 5 && _offeredContracts.All(offer => offer.ExecutableWithStarterKit && SupportsCityDelivery(offer.Contract)) &&
            _offeredContracts.Any(offer => offer.TemplateId == "PROC-PARQUE" && offer.Contract.Calm),
            "the starter board did not offer all compatible templates and a calm job");
        StartStoryMission(drainage);
        Require(_activeContract is null && _feedbackText == Text("city.story.locked"), "drainage opened before the façade job");
        StartStoryMission(story);
        var facadeJob = _activeContract ?? throw new InvalidOperationException("Facade contract was not available.");
        Require(facadeJob.RequiredFeatureIds.Count == 20, "the façade contract did not require 20 targets");
        ProcessAndDeliverCityContract();
        Require(_feedbackText == Text("city.office.upload_first") && _ledger.BalanceCoords == initialBalance, "an empty submission passed");
        PositionAndCapture(new(500190, 7400001.8, 100), Math.PI / 2);
        Require(_fieldSamples.Any(s => s.Quality.Passed && s.ObservedFeatures.Any(f => f.TargetId == "facade_18")), "scene photo missed its facade");
        ProcessAndDeliverCityContract();
        Require(_feedbackText == Text("city.office.upload_first"), "the office read photos still in the field");
        void Upload(LocomotionMode mode = LocomotionMode.Handcart)
        {
            var office = _manifestObjectsById["office_entry"].PositionEnu + new Vector3d(0, -4, -1.5);
            _carSim = null;
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
            PositionAndCapture(new(500010 + i * 10, 7400001.8, 100), Math.PI / 2);
        }
        ProcessAndDeliverCityContract();
        Require(_activeMachine?.State == ContractState.Retrabalho && _ledger.BalanceCoords == initialBalance, "unuploaded rework contributed");
        Upload();
        SaveCareer();
        TryLoadCareer();
        Require(_activeMachine?.State == ContractState.Retrabalho, "rework did not survive reload");
        ProcessAndDeliverCityContract();
        Require(_lastCitySurveyReview is { MatchedFeatureCount: 20 } &&
            _lastCitySurveyReview.GetMetric("area_coverage")?.Status == MetricEvaluationStatus.Failed &&
            _feedbackText == Text("city.office.coverage_rework"), "cropped photos passed coverage: " + CitySurveyReviewText());
        foreach (var id in facadeJob.RequiredFeatureIds)
        {
            var target = _manifestObjectsById[id].PositionEnu;
            PositionAndCapture(new(target.X, target.Y - 9, 100), Math.PI / 2);
        }
        Upload();

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
        var controls = CitySurveyInventory.PublishedControls(sidewalkJob, _manifestObjectsById);
        var adjusted = CityLocalControlAdjustment.Adjust(_fieldSamples[0], controls);
        Require(adjusted is not null && (adjusted.PositionEnu - _fieldSamples[0].EstimatedPose.PositionEnu).Length > .3 &&
            CityLocalControlAdjustment.Adjust(_fieldSamples[0], new Dictionary<string, Vector3d>()) is null,
            "the adjustment did not correct the biased pose or required no control");
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
        Require(_lastAdjustedPhotoCount == 16, "the sidewalk job bypassed the camera control adjustment");
        TryLoadCareer();
        StartStoryMission(signs);
        Require(_activeContract is null && _feedbackText == Text("city.story.requires_platform"), "the bicycle could accept HIST-04");
        Require(TryPurchaseCityPlatform(LocomotionMode.Scooter) &&
            _locomotion.MountedVehicleMode == LocomotionMode.Scooter, "the completed sidewalk job did not unlock the scooter");

        _calendar.SetWeather(false);
        StartStoryMission(signs);
        var signsJob = _activeContract ?? throw new InvalidOperationException("Sign contract was not available.");
        Require(signsJob.RequiredFeatureIds.Count == 12, "the sign contract did not require 12 signs");
        CaptureSurveyPass(signsJob, -1.5, 8, LocomotionMode.Scooter);
        Upload(LocomotionMode.Scooter);
        ProcessAndDeliverCityContract();
        Require(_activeMachine?.State == ContractState.Retrabalho && !CityPlatformStoryUnlocked(LocomotionMode.Car),
            "the sign survey unlocked the car with only one pass");
        CaptureSurveyPass(signsJob, 1.5, 8, LocomotionMode.Scooter);
        Upload(LocomotionMode.Scooter);
        ProcessAndDeliverCityContract();
        Require(_activeContract is null && CityMissionCompleted(signs.MissionId) &&
            _lastCitySurveyReview?.GetMetric("relative_rmse") is { SampleCount: 11, Status: MetricEvaluationStatus.Passed },
            "the avenue inventory did not close Act 1: " + _feedbackText + " " + CitySurveyReviewText());
        TryLoadCareer();
        Require(CityMayEnterRegion(parkEdge, portEdge) && CityMayEnterRegion(parkEdge, waterfrontEdge) &&
            !CityMayEnterRegion(portEdge, new(501001, 7401001, 100)) && CityPlatformStoryUnlocked(LocomotionMode.Car) &&
            _fleet.QuotePlatformPurchase(LocomotionMode.Car, _progression, new HashSet<string> { signs.MissionId }).Unlocked,
            "Act 1 closure access did not survive loading");

        foreach (var seed in new[] { 1000UL, 1001UL })
        {
            foreach (var template in MvpProceduralContractGenerator.Templates)
            {
                var offer = BuildCityContractOffer(template.TemplateId, seed);
                var mode = offer.WitnessCertificate.RequiredPlatformLevel switch
                {
                    0 => LocomotionMode.Handcart,
                    1 => LocomotionMode.Bicycle,
                    _ => LocomotionMode.Scooter,
                };
                SwitchPlatform(mode);
                Require(TryAcceptCityProceduralOffer(offer), $"{offer.VariantId} could not be accepted with {mode}");
                var job = offer.Contract;
                if (job.Product == "images")
                {
                    foreach (var id in job.RequiredFeatureIds)
                    {
                        var target = _manifestObjectsById[id].PositionEnu;
                        PositionAndCapture(new(target.X, target.Y - 9, 100), Math.PI / 2, mode);
                    }
                }
                else
                {
                    var distance = job.AreaId == CitySurveyInventory.SidewalkArea ? 6 : 8;
                    CaptureSurveyPass(job, -1.5, distance, mode);
                    CaptureSurveyPass(job, 1.5, distance, mode);
                }
                Upload(mode);
                ProcessAndDeliverCityContract();
                Require(_activeContract is null && _progression.SettledContractIds.Contains(job.Id),
                    $"{offer.VariantId} failed delivery: " + _feedbackText + " " + CitySurveyReviewText());
            }
        }

        var bus = FullCampaignCatalog.GetMission(BusSurveyCorridor.MissionId);
        StartStoryMission(bus);
        Require(_activeContract is null && _feedbackText == Text("city.story.requires_platform"), "a starter platform could start the bus survey");
        Require(TryPurchaseCityPlatform(LocomotionMode.Car), $"Act 1 and ten paying jobs could not fund a car (balance {_ledger.BalanceCoords})");
        TryLoadCareer();
        Require(_fleet.OwnedPlatforms.Contains(LocomotionMode.Car) && _locomotion.MountedVehicleMode == LocomotionMode.Car,
            "purchased car was lost on reload");
        Require(!CityPlatformStoryUnlocked(LocomotionMode.Kombi), "Kombi shop access did not require HIST-05");

        void AdvanceToHour(double hour)
        {
            var hoursToAdvance = (hour - _calendar.HourOfDay + 24) % 24;
            _calendar.AdvanceCalendarSeconds(hoursToAdvance * 3600);
        }

        AdvanceToHour(17);
        _calendar.SetWeather(false);
        Require(BusSurveyCorridor.IsSurveyWindow(10) && !BusSurveyCorridor.IsSurveyWindow(16),
            "the lane window did not include its opening and exclude its closing time");
        StartStoryMission(bus);
        var busContract = _activeContract ?? throw new InvalidOperationException("Bus corridor was not available.");
        var initialBusBalance = _ledger.BalanceCoords;
        var firstLane = BusSurveyCorridor.Legs[0].Start;
        var nextToLane = firstLane - Vector3d.UnitX * 4;
        Require(!CityMayEnterBusLane(nextToLane, firstLane, _calendar.ExportSnapshot()), "lane admitted a car at 17:00");
        PositionAndCapture(firstLane, -Math.PI / 2, LocomotionMode.Car);
        Require(_fieldSamples.Count == 0 && _feedbackText == Text("city.bus.window_closed"), "closed lane produced a panorama");
        AdvanceToHour(11);
        Require(CityMayEnterBusLane(nextToLane, firstLane, _calendar.ExportSnapshot()), "lane did not open at 11:00");
        PositionAndCapture(nextToLane, -Math.PI / 2, LocomotionMode.Car);
        Require(_fieldSamples.Count == 0 && _feedbackText == Text("city.bus.use_lane"), "out-of-lane capture produced a panorama");
        PositionAndCapture(firstLane, -Math.PI / 2, LocomotionMode.Car);
        Require(_fieldSamples.Count == 0 && _feedbackText == Text("city.hazards.wait_navigation"), "panorama used ideal pose before initialization");

        void CaptureBusLeg(Vector3d start, Vector3d end)
        {
            const double step = .10;
            var length = (end - start).Length;
            var direction = (end - start) / length;
            var heading = Math.Atan2(direction.Y, direction.X);
            var region = GetRegionIdForEnu(end);
            _carSim = null;
            _navigationEnvironmentAt = ResolveCityNavigationEnvironment;
            ResetCityNavigation();
            for (var index = 0; index <= Math.Round(length / step); index++)
            {
                var position = start + direction * (index * step);
                _locomotion.RepositionSafeAfterRecovery(position, LocomotionMode.Car, heading, false, false);
                _tick++;
                var weather = _calendar.StepPhysical(1.0 / 60);
                StepCityNavigation(1.0 / 60, position, heading, LocomotionMode.Car, false);
                if (!_camScheduler.IsCaptureTick(_tick) || GetRegionIdForEnu(position) != region ||
                    _navigationRig?.Estimator?.Current is not { } estimate)
                    continue;

                var before = _fieldSamples.Count;
                CaptureCityPhoto(position, heading, step * 60, weather);
                Require(_fieldSamples.Count - before == 6 && _fieldSamples.Skip(before).All(s => s.EstimatedPose.PositionEnu == estimate.PositionEnu),
                    "expected six synchronized faces registered with the observed trajectory");
            }
        }

        var portLane = BusSurveyCorridor.Legs[0];
        var waterfrontLane = BusSurveyCorridor.Legs[1];
        // Enter each survey leg from the neighborhood side to warm up the estimator before the first stop.
        CaptureBusLeg(new(portLane.Start.X, 7400997, 100), portLane.Start);
        Require(_fieldSamples.Count > 0 && _fieldSamples.Select(photo => photo.SensorId).Distinct().Count() == 6,
            "Port sweep had missing faces");
        ProcessAndDeliverCityContract();
        Require(_feedbackText == Text("city.office.upload_first"), "the office processed unprotected panoramas");
        Upload(LocomotionMode.Car);
        ProcessAndDeliverCityContract();
        Require(_activeMachine?.State == ContractState.Retrabalho && _ledger.BalanceCoords == initialBusBalance &&
            _lastCitySurveyReview?.MatchedFeatureCount == 3, "Port-only delivery did not request the other district: " + CitySurveyReviewText());

        CaptureBusLeg(new(500997, waterfrontLane.End.Y, 100), waterfrontLane.End);
        var pendingBusSamples = _fieldSamples.Select(photo => photo.SampleId).ToArray();
        SaveCareer();
        TryLoadCareer();
        Require(pendingBusSamples.Length > 0 && pendingBusSamples.SequenceEqual(_fieldSamples.Select(photo => photo.SampleId)),
            "pending panorama faces did not survive reload");
        CaptureBusLeg(new(501095, waterfrontLane.End.Y, 100), waterfrontLane.End);
        Require(_fieldSamples.Concat(_protectedCityPhotos).Select(photo => photo.SampleId).Distinct().Count() ==
            _fieldSamples.Count + _protectedCityPhotos.Count, "a reloaded sweep reused existing face IDs");
        Upload(LocomotionMode.Car);
        SaveCareer();

        _calendar.AdvanceCalendarSeconds(_activeMachine!.Snapshot!.ExpiresAtSeconds + 1 - _calendar.CalendarTimeSeconds);
        ProcessAndDeliverCityContract();
        Require(_activeMachine.State == ContractState.Expirado && _ledger.BalanceCoords == initialBusBalance,
            "the expired bus survey paid");
        LoadCityCareerState(recover: true);
        var busAcceptedAt = _activeMachine!.Snapshot!.AcceptedAtSeconds;
        _calendar.AdvanceCalendarSeconds(_activeMachine!.Snapshot!.DueAtSeconds + 3600 - _calendar.CalendarTimeSeconds);
        ProcessAndDeliverCityContract();
        paid = _ledger.BalanceCoords;
        var busQuote = _lastCitySurveyReview is { } busReview ? DeliveryQuoteCalculator.Calculate(busContract, busReview, busAcceptedAt, _calendar.CalendarTimeSeconds) : null;
        Require(CityMissionCompleted(bus.MissionId) && _lastCitySurveyReview?.GetMetric("absolute_rmse") is { SampleCount: 6, Status: MetricEvaluationStatus.Passed } &&
            busQuote is { Eligible: true, LatePenaltyFraction: .05 } && paid == initialBusBalance + busQuote.NetCoords,
            "the protected bus inventory did not settle on the late quote: " + _feedbackText + " " + CitySurveyReviewText());
        var busMeasuredRmse = _lastCitySurveyReview!.GetMetric("absolute_rmse")!.Value;
        TryLoadCareer();
        Require(CityPlatformStoryUnlocked(LocomotionMode.Kombi) &&
            _fleet.QuotePlatformPurchase(LocomotionMode.Kombi, _progression, new HashSet<string> { bus.MissionId }).Unlocked,
            "HIST-05 completion or city-hall reputation did not unlock the Kombi shop");
        StartStoryMission(bus);
        ProcessAndDeliverCityContract();
        Require(_activeContract is null && _ledger.BalanceCoords == paid, "the bus survey could pay twice after reload");

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
        GD.Print(FormattableString.Invariant($"GSA_CITY_DELIVERY_SMOKE_OK protected_only=true rework=true late_quote=true replay=once tunnel_photo=rejected facades=20 area_coverage=true drains=12 sidewalks=8 local_control=true signs=12 procedural_variants=10 act1_closure=true bicycle=true scooter=true car=true bus_360=true bus_rmse_m={busMeasuredRmse:F3} kombi_unlock=true"));
    }
}
