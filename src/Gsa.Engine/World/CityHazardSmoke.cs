using System.Text.Json;
using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Camera;
using Gsa.Core.Traffic;
using Gsa.Missions.Economy;
using Gsa.Missions.Gameplay;
using Gsa.Missions.Persistence;
using Godot;

namespace Gsa.Engine.World;

public partial class SaoDatumCityScene
{
    private void RunCityHazardSmoke()
    {
        static void Require(bool condition, string reason)
        { if (!condition) throw new InvalidOperationException("City hazard smoke: " + reason); }
        var manager = _saveManager; var originalBytes = File.ReadAllBytes(manager.CurrentSavePath);
        var locale = TranslationServer.GetLocale();
        var definition = _activeContract! with { Id = "hazard_smoke", Calm = false };
        _saveManager = new SaveGameManager(Path.Combine(_citySmokeDirectory!, "hazards"));
        _citySaveBlocked = false; _citySaveStatus = null;
        _activeContract = null; _activeMachine = null; _activeStorySpec = null;
        _cityHazards = new(); _attention = new(); _ledger = new CareerLedger(10000);
        _calendar = new WorldCalendarAndWeatherController(43200); _calmPolicy.SetCalmCityEnabled(false);
        _fleet = FleetProgressionManager.FromSnapshot(new(1, "kombi", [LocomotionMode.Handcart, LocomotionMode.Car, LocomotionMode.Kombi, LocomotionMode.Van], [],
            new Dictionary<SkillTrackKind, int> { [SkillTrackKind.Negotiation] = 1 }));
        _geopedia.UnlockAdvancedForPlatformLevel(4); Require(AcceptCityContract(definition), "job rejected");
        Vector3d Center(TerritoryZoneDefinition zone) => new((zone.Bounds.MinEast + zone.Bounds.MaxEast) / 2,
            (zone.Bounds.MinNorth + zone.Bounds.MaxNorth) / 2, 100);
        void Position(Vector3d p, LocomotionMode vehicle, bool onFoot = false)
        {
            _carSim = null; _locomotion.RepositionSafeAfterRecovery(p, vehicle, 0, true, true); SwitchPlatform(vehicle);
            if (onFoot) { Require(_locomotion.TryToggleMount([], out _), "cannot dismount"); _carSim = null; }
            SyncCityHazardProtection();
        }
        void Capture()
        {
            _tick += 30; while (!_camScheduler.IsCaptureTick(_tick)) _tick++;
            _calendar.StepPhysical(.5); var count = _fieldSamples.Count;
            CaptureCityPhoto(CurrentPositionEnu(), 0, 0, _calendar.ExportSnapshot());
            Require(_fieldSamples.Count == count + 1, "scheduled capture missing");
        }
        var office = _manifestObjectsById["office_entry"].PositionEnu + new Vector3d(0, -4, 0);
        Position(office, LocomotionMode.Kombi, true); Capture(); OffloadCityData();
        Require(_fieldSamples.Count == 0 && _protectedCityPhotos.Count == 1, "upload did not protect data");
        var safePayload = _cityCustody.Entries.Single(e => e.Stage == BatchCustodyStage.Protected).PayloadJson;
        var safePhoto = JsonSerializer.Serialize(SerializableCameraSample.FromDomain(_protectedCityPhotos[0]));
        Require(!AcceptCityContract(definition) && _protectedCityPhotos.Count == 1, "new job erased active acquisition");
        var mafia = FactionCatalog.GetZone("zone_old_center_ring"); var pos = Center(mafia);
        Position(pos, LocomotionMode.Kombi, true); Require(!_citySafeZone, "fixture is protected");
        _attention.ImportSnapshot(new(1, [new(mafia.ZoneId, 40, 0, 0)]));
        for (var i = 0; i < 34; i++) StepCityFactionHazards(.1, pos, 0, LocomotionMode.OnFoot);
        Require(_cityHazards.Lens.ObstructionFraction > .4, "warning did not cause paint");
        Capture(); var dirty = _fieldSamples[^1];
        Require(dirty.Quality.LensObstructionExceeded && !dirty.Quality.Passed, "paint did not invalidate camera quality");
        _cleanLensRequested = true; StepCityFactionHazards(2, pos, 0, LocomotionMode.OnFoot);
        StepCityFactionHazards(.1, pos, 1, LocomotionMode.OnFoot); StepCityFactionHazards(2, pos, 0, LocomotionMode.OnFoot);
        Require(_cityHazards.Lens.ObstructionFraction > 0, "moving cleaning accumulated");
        StepCityFactionHazards(1.1, pos, 0, LocomotionMode.OnFoot);
        Require(_cityHazards.Lens.ObstructionFraction == 0 && dirty.Quality.LensObstructionExceeded, "cleaning rewrote old observation");
        _protectionZone.Select(_planningZones.ToList().FindIndex(z => z.ZoneId == mafia.ZoneId));
        var balance = _ledger.BalanceCoords; BuyCityProtection(ServiceKind.VehicleDisguise); BuyCityProtection(ServiceKind.VehicleDisguise);
        Require(_ledger.BalanceCoords == balance - 200, "disguise free/double charged");
        Require(_cityHazards.Sabotage.ComputeEffectiveAttentionMultiplier(mafia.ZoneId) == .5, "disguise is not half attention");
        TranslationServer.SetLocale("en_US"); RefreshCityHazardLocale(); var english = _hazardButtons[0].Button.Text;
        TranslationServer.SetLocale("pt_BR"); RefreshCityHazardLocale();
        Require(english != _hazardButtons[0].Button.Text && !_hazardButtons[0].Button.Text.StartsWith("city."), "controls did not translate live");
        var rival = FactionCatalog.GetZone("zone_port_mapafacil"); pos = Center(rival); Position(pos, LocomotionMode.Kombi);
        ApplyCityFactionAction(TerritoryThreatLevel.Level3TirePunctureOrPursuit, rival, pos);
        Require(_cityHazards.TirePunctured && _cityHazards.PursuitRemaining == 0, "puncture combined with unavoidable pursuit");
        for (var i = 0; i < 600; i++) _carSim!.StepMotion(1.0 / 60, 1, 0, 0, speedLimitMetresPerSecond: _cityHazards.TirePunctured ? 6 : double.PositiveInfinity);
        Require(Math.Abs(_carSim!.SpeedMetresPerSecond - 6) < 1e-6, "puncture did not cap actual motion");
        Position(pos, LocomotionMode.Kombi); balance = _ledger.BalanceCoords; RepairCityVehicle();
        Require(!_cityHazards.TirePunctured && _ledger.BalanceCoords == balance - 50, "paid tyre repair failed");
        _carSim!.ApplyCollisionImpact(4); var damage = _carSim.ConditionDamage;
        SwitchPlatform(LocomotionMode.Car); SwitchPlatform(LocomotionMode.Kombi);
        Require(_carSim!.ConditionDamage == damage, "switch repaired damage");
        pos = Center(mafia); Position(pos, LocomotionMode.Kombi);
        ApplyCityFactionAction(TerritoryThreatLevel.Level4RoadblockOrPursuit, mafia, pos); StepCityThreatVisuals(.1, pos, 0);
        Require(_roadblockVisual is StaticBody3D && _roadblockObstacle is not null && _pursuerVisual is not null, "actors not instantiated");
        var block = _cityHazards.RoadblockPosition!.Value;
        Require((ResolveObstaclePushout(block - Vector3d.UnitX * 4, block, 1, out _) - block).Length > .5, "barrier has no collision");
        _calmPolicy.SetCalmCityEnabled(true); SyncCityHazardProtection();
        Require(_roadblockVisual is null && _pursuerVisual is null && _roadblockObstacle is null, "calm left hostile actors");
        _calmPolicy.SetCalmCityEnabled(false); SyncCityHazardProtection();
        var marco = FactionCatalog.GetZone("zone_old_center_marco"); pos = Center(marco); Position(pos, LocomotionMode.Kombi, true);
        var published = _geodeticNetwork.Benchmarks.Values.Where(b => b.RegionId == marco.RegionId)
            .ToDictionary(b => b.BenchmarkId, b => b.PublishedCadastralPositionEnu);
        ApplyCityFactionAction(TerritoryThreatLevel.Level2PaintAttack, marco, pos);
        var moved = _cityHazards.Sabotage.MovedBenchmarks.Records.Single();
        Require(_geodeticNetwork.Benchmarks[moved.BenchmarkId].PublishedCadastralPositionEnu == published[moved.BenchmarkId], "published coordinate changed");
        Require(_manifestObjectsById[moved.BenchmarkId].PositionEnu == moved.DisplacedEnuPosition, "physical definition not moved");
        Require(_worldRoot.GetNode<Node3D>(moved.BenchmarkId).Position.DistanceTo(_coordinates.ToGodot(moved.DisplacedEnuPosition)) < .001,
            "actual physical node not moved");
        Require(JsonSerializer.Serialize(SerializableCameraSample.FromDomain(_protectedCityPhotos[0])) == safePhoto, "sabotage rewrote acquired photo");
        var flat = FactionCatalog.GetZone("zone_waterfront_terra_plana"); pos = Center(flat); Position(pos, LocomotionMode.Kombi, true);
        _cityHazards.Step(.1, flat.ZoneId, false); var beforeEnvironment = ResolveCityNavigationEnvironment(pos);
        ApplyCityFactionAction(TerritoryThreatLevel.Level2PaintAttack, flat, pos); _cityHazards.Step(5, flat.ZoneId, false);
        Require(Math.Abs((ResolveCityNavigationEnvironment(pos).MultipathBiasEnu - beforeEnvironment.MultipathBiasEnu).X - 10) < 1e-7,
            "spoof ramp not in actual GNSS environment");
        Position(pos, LocomotionMode.Van);
        for (var i = 0; i < 181; i++) StepCityNavigation(1.0 / 60, pos, 0, LocomotionMode.Van, false);
        Require(_navigationRig?.Estimator?.Current is not null, "navigation failed to initialize"); Capture();
        Require((_fieldSamples[^1].EstimatedPose.PositionEnu - (pos + Vector3d.UnitZ * 1.4)).Length > 1, "vehicle photo used truth");
        _navigationEnvironmentAt = _ => new(1, new Vector3d(10000, 0, 0));
        for (var i = 0; i < 12; i++) StepCityNavigation(1.0 / 60, pos, 0, LocomotionMode.Van, false);
        RefreshTechnicalTablet();
        Require(_navigationRig!.Estimator!.RejectedGnssCorrections > 0 && _technicalStatus.Text.Contains(Text("navigation.alert.inconsistent_fix")),
            "GNSS jump not rejected/explained");
        _navigationEnvironmentAt = ResolveCityNavigationEnvironment;
        pos = Center(mafia); Position(pos, LocomotionMode.Kombi); BuyCityInsurance(); balance = _ledger.BalanceCoords;
        Require(_fieldSamples.Count > 0, "no pending data for loss"); _cityHazards.Step(.1, mafia.ZoneId, false);
        _attention.ImportSnapshot(new(1, [new(mafia.ZoneId, 100, 0, 0)], new(null, TerritoryThreatLevel.None, 0, mafia.ZoneId, 15, true, 6, 0)));
        _lidarEnabled = true; SaveCareer(); TryLoadCareer();
        Require(!_citySaveBlocked && _attention.ExportSnapshot().Pending!.BombCountdownSeconds == 6, "save reset bomb countdown");
        for (var i = 0; i < 61; i++) StepCityFactionHazards(.1, CurrentPositionEnu(), 0, CurrentActiveMode());
        Require(_fieldSamples.Count == 0 && _protectedCityPhotos.Count == 1, "loss did not preserve only uploaded data");
        Require(_cityCustody.Entries.Single(e => e.Stage == BatchCustodyStage.Protected).PayloadJson == safePayload, "uploaded payload changed");
        Require(_cityHazards.IsVehicleDisabled(LocomotionMode.Kombi) && CurrentActiveMode() == LocomotionMode.Handcart, "recovery handcart missing");
        SwitchPlatform(LocomotionMode.Kombi); Require(CurrentActiveMode() == LocomotionMode.Handcart, "lost vehicle drivable");
        SaveCareer(); TryLoadCareer();
        Require(_cityHazards.IsVehicleDisabled(LocomotionMode.Kombi) && _protectedCityPhotos.Count == 1, "reload resurrected vehicle/data");
        Require(_cityHazards.Sabotage.MovedBenchmarks.Records.Count == 1, "reload lost displaced mark");
        RepairCityVehicle();
        Require(CurrentActiveMode() == LocomotionMode.Kombi && !_cityHazards.IsVehicleDisabled(LocomotionMode.Kombi), "insurance recovery failed");
        Require(_ledger.BalanceCoords == balance - 1400 && _fieldSamples.Count == 0, "wrong deductible or data resurrected");
        AbandonCityContract(); Require(AcceptCityContract(definition with { Id = "hazard_calm_smoke", Calm = true }), "calm job rejected");
        Position(Center(mafia), LocomotionMode.Kombi);
        _attention.ImportSnapshot(new(1, [new(mafia.ZoneId, 100, 0, 0)], new(null, TerritoryThreatLevel.None, 0, mafia.ZoneId, 15, true, 1, 0)));
        balance = _ledger.BalanceCoords; var calm = StepCityFactionHazards(2, CurrentPositionEnu(), 0, CurrentActiveMode());
        BuyCityProtection(ServiceKind.SecurityEscort);
        Require(!_calmPolicy.CalmCityEnabled && calm.SuppressedByCalmCity && !calm.BombDetonated && _ledger.BalanceCoords == balance,
            "calm contract did not suppress threats/fees");
        Require(_cityHazards.Sabotage.MovedBenchmarks.Records.Count == 1, "job erased physical sabotage");
        _lidarPointVisibleCount = 500; ProcessAndDeliverCityContract(); Require(_activeContract is not null, "preview approved empty delivery");
        AbandonCityContract(); Require(AcceptCityContract(definition), "standard job rejected after calm job");
        Position(Center(mafia), LocomotionMode.Kombi);
        _cityHazards.Step(.1, mafia.ZoneId, false); _cityHazards.LegacyDisguise = true;
        ApplyCityFactionAction(TerritoryThreatLevel.Level4RoadblockOrPursuit, mafia, CurrentPositionEnu());
        StepCityThreatVisuals(.1, CurrentPositionEnu(), 0);
        _cityHazards.LegacyDisguise = true;
        _protectionZone.Select(_planningZones.ToList().FindIndex(z => z.ZoneId == mafia.ZoneId));
        balance = _ledger.BalanceCoords;
        var authorizationFee = _cityHazards.Services.QuoteService(ServiceKind.ControlledZoneAuthorization, PlatformProfile.Kombi,
            contractOrCargoValueCoords: definition.Reward.BaseCoords).FinalFeeCoords;
        BuyCityProtection(ServiceKind.ControlledZoneAuthorization); BuyCityProtection(ServiceKind.ControlledZoneAuthorization);
        Require(_ledger.BalanceCoords == balance - authorizationFee && _roadblockVisual is null && _pursuerVisual is null,
            "authorization did not cancel actors or charged twice");
        _attention.ImportSnapshot(new(1, [new(mafia.ZoneId, 100, 0, 0)], new(null, TerritoryThreatLevel.None, 0, mafia.ZoneId, 15, true, 1, 0)));
        var authorized = StepCityFactionHazards(2, CurrentPositionEnu(), 0, CurrentActiveMode());
        Require(authorized.SuppressedByCalmCity && !authorized.BombDetonated && _cityHazards.PursuitRemaining == 0,
            "legacy disguise floor overrode zero-attention authorization");
        ClearCityThreatVisuals(); _saveManager = manager; _citySaveBlocked = false; TryLoadCareer(); TranslationServer.SetLocale(locale);
        Require(originalBytes.SequenceEqual(File.ReadAllBytes(manager.CurrentSavePath)), "fixture wrote original career");
        GD.Print("GSA_CITY_HAZARDS_SMOKE_OK paint=quality_failure clean=3s puncture=motion roadblock=collision spoof=observed_pose bomb=loss upload=preserved insurance=deductible calm=cancelled authorization=cancelled save=persistent locale=live");
    }
}
