using System.Text.Json;
using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Camera;
using Gsa.Missions.Contracts;
using Gsa.Missions.Economy;
using Gsa.Missions.Gameplay;
using Gsa.Missions.Persistence;
using Godot;

namespace Gsa.Engine.World;

public partial class SaoDatumCityScene
{
    private void RunCityEverydaySmoke()
    {
        static void Require(bool condition, string reason)
        { if (!condition) throw new InvalidOperationException("City everyday smoke: " + reason); }
        var manager = _saveManager; var original = File.ReadAllBytes(manager.CurrentSavePath);
        var locale = TranslationServer.GetLocale(); var calmDefinition = _activeContract! with { Id = "encounter_calm", Calm = true };
        _saveManager = new SaveGameManager(Path.Combine(_citySmokeDirectory!, "everyday"));
        _citySaveBlocked = false; _citySaveStatus = null; _activeContract = null; _activeMachine = null; _activeStorySpec = null;
        _everyday = new(); _gimbalina = new(65); _ledger = new CareerLedger(100);
        _cityHazards = new(); _cityCustody = new(); _protectedCityPhotos.Clear(); _fieldSamples.Clear(); _observedTargetIds.Clear();
        _calmPolicy.SetCalmCityEnabled(false); _rackMisaligned = false; _calendar = new(22 * 3600);
        _fleet = FleetProgressionManager.FromSnapshot(new(1, "car", [LocomotionMode.Handcart, LocomotionMode.Bicycle, LocomotionMode.Car, LocomotionMode.Van], [], new Dictionary<SkillTrackKind, int>()));
        void Position(Vector3d p, LocomotionMode mode)
        {
            _carSim = null; _locomotion.RepositionSafeAfterRecovery(p, mode, 0, true, true); SwitchPlatform(mode); SyncCityHazardProtection();
        }
        void Capture()
        {
            _tick += 30; while (!_camScheduler.IsCaptureTick(_tick)) _tick++;
            CaptureCityPhoto(CurrentPositionEnu(), 0, 0, _calendar.ExportSnapshot());
        }
        string Photos() => JsonSerializer.Serialize(_fieldSamples.Select(SerializableCameraSample.FromDomain).ToArray());
        Require(_cityDogs.Count == 4 && _cityParking.Count == 4, "expected persistent visible actors in four districts");
        var dog = _cityDogs.First(); var p = FindCityNpcStandingPoint(dog.Value.Home + new Vector3d(6, 0, 0));
        Position(p, LocomotionMode.Bicycle); StepCityEverydayEncounters(.1, p, 0, LocomotionMode.Bicycle);
        Require(_everyday.DogActive && _gimbalina.Stress == 85, "real bicycle encounter did not apply dog startle");
        var home = dog.Value.Home;
        StepCityEverydayEncounters(1.8, p, 0, LocomotionMode.Bicycle);
        Require(_everyday.DogPositionEnu == home, "dog moved before warning elapsed");
        _gimbalina.Step(.01); Capture();
        Require(_fieldSamples.Count == 1 && _fieldSamples[0].Quality.MotionBlurPixels > 1,
            "head motion not connected to actual camera exposure");
        Require(_fieldSamples[0].Quality.ExposureSeconds == CameraSensorProfile.Initial.NightExposureSeconds,
            "night capture used day exposure");
        Require(_fieldSamples[0].EstimatedPose.BodyToMap == Quaterniond.Identity, "unobserved head truth entered estimated pose");
        var photos = Photos(); var encounter = JsonSerializer.Serialize(_everyday.Export()); var stress = _gimbalina.ExportSaveSnapshot();
        SaveCareer(); _everyday = new(); _gimbalina = new(); TryLoadCareer();
        Require(!_citySaveBlocked && JsonSerializer.Serialize(_everyday.Export()) == encounter, "dog timer changed on reload");
        Require(_gimbalina.ExportSaveSnapshot() == stress && Photos() == photos, "stress or raw observations changed on reload");
        GiveCityDogTreat(); Require(!_everyday.DogActive && _everyday.Export().Dog!.Pursuit.PacifiedByTreat, "treat callback failed");
        SaveCareer(); TryLoadCareer(); Require(!_everyday.DogActive && Photos() == photos, "load restarted treated dog or rewrote photos");

        var otherDog = _cityDogs.Skip(1).First(); Position(otherDog.Value.Home, LocomotionMode.Bicycle);
        StepCityEverydayEncounters(.1, CurrentPositionEnu(), 0, LocomotionMode.Bicycle);
        StepCityEverydayEncounters(2, CurrentPositionEnu(), 0, LocomotionMode.Bicycle);
        Require(_everyday.BicycleStumbleRemainingSeconds > 0, "contact did not cause non-injurious stumble");
        _locomotion.Step(.2, new(1, 0, 0)); var speed = _locomotion.ForwardSpeedMetresPerSecond;
        var actual = _locomotion.Step(.1, ApplyCityEverydayInput(new(1, 0, 0), LocomotionMode.Bicycle));
        Require(actual.ForwardSpeedMetresPerSecond < speed, "stumble did not brake actual bicycle motion");
        _calmPolicy.SetCalmCityEnabled(true); SyncCityHazardProtection();
        Require(!_everyday.DogActive && _everyday.BicycleStumbleRemainingSeconds == 0, "calm left dog or pending brake");
        _calmPolicy.SetCalmCityEnabled(false);

        var parking = _cityParking.First(); Position(parking.Value.Home, LocomotionMode.Car);
        StepCityEverydayEncounters(.1, CurrentPositionEnu(), 0, LocomotionMode.Car);
        Require(_everyday.Parking is { Phase: CityParkingPhase.Offered }, "real parking approach missing");
        PayCityParking(); PayCityParking(); Require(_ledger.BalanceCoords == 90 && _everyday.Parking is null, "payment duplicated or wrong fee");
        SaveCareer(); TryLoadCareer(); Require(_ledger.BalanceCoords == 90 && _everyday.Parking is null, "payment repeated on reload");
        Position(parking.Value.Home + new Vector3d(30, 0, 0), LocomotionMode.Car);
        StepCityEverydayEncounters(31, CurrentPositionEnu(), 0, LocomotionMode.Car);
        Position(parking.Value.Home, LocomotionMode.Car); StepCityEverydayEncounters(.1, CurrentPositionEnu(), 0, LocomotionMode.Car);
        RefuseCityParking(); StepCityEverydayEncounters(2, CurrentPositionEnu(), 0, LocomotionMode.Car); SaveCareer(); TryLoadCareer();
        Require(_everyday.Parking?.RetaliationRemainingSeconds == 1, "refusal warning reset by load");
        var vehicleDamage = _carSim!.ConditionDamage; var rack = _rackMisaligned;
        StepCityEverydayEncounters(1.1, CurrentPositionEnu(), 0, LocomotionMode.Car);
        StepCityEverydayEncounters(5, CurrentPositionEnu(), 0, LocomotionMode.Car);
        Require(_everyday.CosmeticDamageFor(LocomotionMode.Car) == 5, "cosmetic scratch repeated or not recorded");
        Require(_carSim.ConditionDamage == vehicleDamage && _rackMisaligned == rack && Photos() == photos,
            "cosmetic damage changed mechanical condition, calibration or raw data");

        Position(parking.Value.Home + new Vector3d(30, 0, 0), LocomotionMode.Car);
        StepCityEverydayEncounters(31, CurrentPositionEnu(), 0, LocomotionMode.Car);
        Position(parking.Value.Home, LocomotionMode.Car); StepCityEverydayEncounters(.1, CurrentPositionEnu(), 0, LocomotionMode.Car); RefuseCityParking();
        _activeContract = calmDefinition; _activeMachine = new ContractStateMachine(calmDefinition); _activeMachine.Accept(_calendar.CalendarTimeSeconds);
        SyncCityHazardProtection(); StepCityEverydayEncounters(4, CurrentPositionEnu(), 0, LocomotionMode.Car); PayCityParking();
        Require(!_calmPolicy.CalmCityEnabled && _everyday.Parking is null && _ledger.BalanceCoords == 90 &&
                _everyday.CosmeticDamageFor(LocomotionMode.Car) == 5, "calm contract permitted delayed retaliation/charge");
        Position(dog.Value.Home, LocomotionMode.Bicycle); StepCityEverydayEncounters(1, CurrentPositionEnu(), 0, LocomotionMode.Bicycle);
        Require(!_everyday.DogActive, "calm contract spawned a dog chase");

        _gimbalina = new(100); _gimbalina.Step(5.1); _locomotion.MarkGimbalinaFled(); _mobility.SetGimbalinaPresent(false);
        SaveCareer(); _gimbalina = new(); TryLoadCareer(); var count = _fieldSamples.Count; Capture();
        Require(_gimbalina.HasFledToSafety && !_gimbalina.Injured && _fieldSamples.Count == count, "save revived fled chicken or allowed capture");
        var bird = _benchRoot.GetNode<Node3D>("PlayerRig").FindChild("GimbalinaBody", true, false) as Node3D;
        Require(bird is { Visible: false }, "fled chicken remained visible in player rig");
        var office = _manifestObjectsById["office_entry"].PositionEnu + new Vector3d(0, -4, 0);
        Position(office, LocomotionMode.Bicycle); RecoverCityGimbalina();
        Require(!_gimbalina.HasFledToSafety && !_locomotion.GimbalinaFled && _gimbalina.Stress == 0 && Photos() == photos,
            "office recovery lost data or did not recover chicken");
        Require(bird!.Visible, "office recovery did not restore visible chicken");
        Capture(); Require(_fieldSamples.Count == count + 1, "capture did not resume after recovery");
        TranslationServer.SetLocale("en_US"); RefreshCityHazardLocale(); var english = _treatDogButton!.Text;
        TranslationServer.SetLocale("pt_BR"); RefreshCityHazardLocale();
        Require(_treatDogButton.Text != english && !parking.Value.Label.Text.StartsWith("city."), "encounter controls/actor labels not localized");
        _saveManager = manager; _citySaveBlocked = false; TryLoadCareer(); TranslationServer.SetLocale(locale);
        Require(original.SequenceEqual(File.ReadAllBytes(manager.CurrentSavePath)), "encounter test touched original career slot");
        GD.Print("GSA_CITY_EVERYDAY_SMOKE_OK dogs=4 parking=4 warning=2s treat=effective stumble=braking head_motion=observed night=exposure parking_fee=10 cosmetic=5 save=persistent calm=cancelled recovery=safe locales=2");
    }
}
