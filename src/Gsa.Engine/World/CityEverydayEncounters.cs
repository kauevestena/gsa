using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Traffic;
using Gsa.Engine.Visuals;
using Gsa.Missions.Economy;
using Gsa.Missions.Gameplay;
using Godot;

namespace Gsa.Engine.World;

public partial class SaoDatumCityScene
{
    private CityEverydayEncounters _everyday = new();
    private readonly Dictionary<string, (Vector3d Home, Node3D Node, Label3D Label)> _cityDogs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (Vector3d Home, Node3D Node, Label3D Label)> _cityParking = new(StringComparer.Ordinal);
    private Func<Vector3d, Vector3d, Vector3d>? _resolveDogMotion;
    private Label? _everydayStatus;
    private Button? _treatDogButton, _payParkingButton, _refuseParkingButton, _recoverChickenButton;
    private double _dogBarkCooldown;
    private string? _lastActiveDogSite;

    private void BuildCityEverydayActors()
    {
        var root = new Node3D { Name = "CityEverydayEncounters" }; AddChild(root);
        var proposed = new (string Id, Vector3d Position)[]
        {
            ("old_center", new(500285, 7400354, 100)), ("port", new(500450, 7401156, 100)),
            ("waterfront", new(501260, 7400156, 100)), ("serra", new(501035, 7401051, 100)),
        };
        foreach (var (id, proposedPosition) in proposed)
        {
            var home = FindCityNpcStandingPoint(proposedPosition);
            var dog = new Node3D { Name = "Dog_" + id, Position = _coordinates.ToGodot(home) };
            void Part(Vector3 pos, Vector3 size, Color color) => dog.AddChild(new MeshInstance3D {
                Position = pos, Mesh = new BoxMesh { Size = size }, MaterialOverride = GsaMeshBuilder.SolidMat(color) });
            var brown = new Color(.48f, .29f, .13f);
            Part(new(0, .4f, 0), new(.7f, .35f, .3f), brown);
            Part(new(.4f, .55f, 0), new(.28f, .3f, .28f), brown);
            Part(new(.56f, .5f, 0), new(.17f, .12f, .18f), new Color(.18f, .12f, .08f));
            foreach (var x in new[] { -.23f, .23f }) foreach (var z in new[] { -.11f, .11f })
                Part(new(x, .16f, z), new(.09f, .3f, .09f), brown);
            Part(new(.35f, .76f, -.11f), new(.1f, .2f, .07f), brown);
            Part(new(.35f, .76f, .11f), new(.1f, .2f, .07f), brown);
            var label = CityNpcLabel(); dog.AddChild(label); root.AddChild(dog);
            _cityDogs.Add("dog_" + id, (home, dog, label));
        }
        foreach (var (mode, pos, _, _) in _worldParkedVehicles.Where(v => v.Mode >= LocomotionMode.Car))
        {
            var home = FindCityNpcStandingPoint(pos + new Vector3d(0, 7, 0));
            var npc = new Node3D { Name = "ParkingAttendant_" + mode, Position = _coordinates.ToGodot(home) };
            npc.AddChild(new MeshInstance3D { Mesh = GsaMeshBuilder.BuildTrafficPedestrianMesh(),
                MaterialOverride = GsaMeshBuilder.SolidMat(new Color(1, .55f, .12f)) });
            var label = CityNpcLabel(); npc.AddChild(label); root.AddChild(npc);
            _cityParking.Add("parking_" + mode.ToString().ToLowerInvariant(), (home, npc, label));
        }
        RefreshCityEverydayLocale();
    }

    private Label3D CityNpcLabel() => new() { Visible = !_quickParkRun, Position = new(0, 1.9f, 0), FontSize = 38,
        PixelSize = .006f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Modulate = Colors.White };

    private Vector3d FindCityNpcStandingPoint(Vector3d proposed)
    {
        for (var ring = 0; ring <= 6; ring++)
        for (var i = 0; i < (ring == 0 ? 1 : 16); i++)
        {
            var x = proposed.X + Math.Cos(i * Math.PI / 8) * ring * 2;
            var y = proposed.Y + Math.Sin(i * Math.PI / 8) * ring * 2;
            var p = new Vector3d(x, y, GetTerrainElevationEnu(x, y));
            if (IsCityEverydaySafe(p)) continue;
            if (_cityBlockObstacles.Concat(_manifestObstacles).Any(o => o.Collision &&
                p.X + .6 > o.MinX && p.X - .6 < o.MaxX && p.Y + .6 > o.MinY && p.Y - .6 < o.MaxY && p.Z + 1.8 > o.MinZ && p.Z < o.MaxZ)) continue;
            return p;
        }
        throw new InvalidOperationException("No clear pavement for city encounter at " + proposed);
    }

    private bool IsCityEverydaySafe(Vector3d p) => NearCityUnload(p) ||
        FullCityFactionPressureCoordinator.IsInsideProtectedSafeZone(GetRegionIdForEnu(p), p, out _);
    private Vector3d CityEncounterVehiclePosition() => CurrentActiveMode() == LocomotionMode.OnFoot
        ? _locomotion.HandcartPositionEnu : CurrentPositionEnu();
    private bool CanSeeCityNpc(Vector3d from, Vector3d to)
    {
        if ((from - to).Length < .1) return true;
        using var ray = PhysicsRayQueryParameters3D.Create(_coordinates.ToGodot(from + Vector3d.UnitZ * .7),
            _coordinates.ToGodot(to + Vector3d.UnitZ * .7), 1);
        return GetWorld3D().DirectSpaceState.IntersectRay(ray).Count == 0;
    }

    private void CancelCityEverydayThreats()
    {
        if (!_everyday.DogActive && _everyday.Parking is null && _everyday.BicycleStumbleRemainingSeconds <= 0) return;
        _everyday.SuppressHostileEncounters(); _dogBarkCooldown = 0; SyncCityEverydayActors();
    }

    private LocomotionInput ApplyCityEverydayInput(LocomotionInput input, LocomotionMode mode) =>
        mode == LocomotionMode.Bicycle && _everyday.BicycleStumbleRemainingSeconds > 0 ? new(0, 1, 0) : input;

    private void StepCityEverydayEncounters(double dt, Vector3d position, double speed, LocomotionMode mode)
    {
        SyncCityHazardProtection();
        var protectedNow = _calmPolicy.IsProtectionActive || _citySafeZone;
        if (!protectedNow && mode == LocomotionMode.Bicycle && !_gimbalina.HasFledToSafety && !_everyday.DogActive)
        {
            foreach (var (id, actor) in _cityDogs)
            {
                if (CityEverydayEncounters.Distance(actor.Home, position) > DogPursuitTracker.TriggerRadiusMetres ||
                    !CanSeeCityNpc(actor.Home, position)) continue;
                if (_everyday.TryStartDog(id, actor.Home, position, mode, false))
                {
                    _gimbalina.ApplyStartle(StartleEventKind.Dog); _audio.PlayDogBark(); _dogBarkCooldown = 2;
                    _feedbackText = Text("city.encounters.dog_warning"); SaveHazardChange(); break;
                }
            }
        }
        var vehicle = CityEncounterVehiclePosition(); var vehicleMode = _locomotion.MountedVehicleMode;
        if (!protectedNow && _everyday.Parking is null && Math.Abs(speed) < .1)
            foreach (var (id, actor) in _cityParking)
            {
                if (CityEverydayEncounters.Distance(actor.Home, position) > 12 || !CanSeeCityNpc(actor.Home, position)) continue;
                if (_everyday.TryOfferParking(id, actor.Home, vehicle, vehicleMode, speed, false))
                { _audio.PlayAlert(); _feedbackText = Text("city.encounters.parking_offer"); SaveHazardChange(); break; }
            }
        _resolveDogMotion ??= ResolveCityDogMotion;
        var result = _everyday.Step(dt, position, mode, vehicle, vehicleMode, protectedNow, _resolveDogMotion);
        if (result.DogContact) { _chaseCamera.TriggerShake(.15f, .15f); _audio.PlayCollisionThud(1.2); _feedbackText = Text("city.encounters.stumble"); }
        if (result.CosmeticScratch) { _audio.PlayAlert(); _feedbackText = Text("city.encounters.scratched"); }
        if (result.DogContact || result.DogEnded || result.CosmeticScratch) SaveHazardChange();
        _dogBarkCooldown = Math.Max(0, _dogBarkCooldown - dt);
        if (_everyday.DogActive && _dogBarkCooldown <= 0) { _audio.PlayDogBark(); _dogBarkCooldown = 2; }
        SyncCityEverydayActors();
    }

    private Vector3d ResolveCityDogMotion(Vector3d from, Vector3d to)
    {
        var resolved = ResolveObstaclePushout(from, to, .35, out _);
        return new(resolved.X, resolved.Y, GetTerrainElevationEnu(resolved.X, resolved.Y));
    }

    private void SyncCityEverydayActors()
    {
        var activeSite = _everyday.DogActive ? _everyday.DogSiteId : null;
        if (_lastActiveDogSite is { } old && old != activeSite && _cityDogs.TryGetValue(old, out var previous))
        { previous.Node.Position = _coordinates.ToGodot(previous.Home); previous.Label.Modulate = Colors.White; }
        if (activeSite is { } id && _cityDogs.TryGetValue(id, out var actor))
        {
            actor.Node.Position = _coordinates.ToGodot(_everyday.DogPositionEnu);
            var target = CurrentPositionEnu() - _everyday.DogPositionEnu;
            actor.Node.Rotation = new(0, (float)Math.Atan2(target.Y, target.X), 0);
            actor.Label.Modulate = new Color(1, .65f, .18f);
        }
        _lastActiveDogSite = activeSite;
    }

    private void GiveCityDogTreat()
    {
        SyncCityHazardProtection();
        if (_everyday.GiveTreat(_calmPolicy.IsProtectionActive || _citySafeZone))
        { _feedbackText = Text("city.encounters.treated"); _audio.PlayOffloadChime(); SyncCityEverydayActors(); SaveHazardChange(); }
    }
    private void PayCityParking()
    {
        SyncCityHazardProtection();
        _feedbackText = Text(_everyday.PayParking(_ledger, _calendar.CalendarTimeSeconds, _calmPolicy.IsProtectionActive || _citySafeZone)
            ? "city.encounters.parking_paid" : "city.encounters.payment_failed"); SaveHazardChange();
    }
    private void RefuseCityParking()
    {
        SyncCityHazardProtection();
        if (_everyday.RefuseParking(_calmPolicy.IsProtectionActive || _citySafeZone))
        { _feedbackText = Text("city.encounters.refusal_warning"); SaveHazardChange(); }
    }
    private void RecoverCityGimbalina()
    {
        if (!NearCityUnload()) { _feedbackText = Text("city.encounters.recover_location"); return; }
        _gimbalina.RecoverAtOffice(); _locomotion.RefillHandcartResources(false, false);
        _mobility.SetGimbalinaPresent(true); _feedbackText = Text("city.encounters.recovered"); SaveHazardChange();
    }

    private void BuildCityEverydayControls(VBoxContainer tab)
    {
        _everydayStatus = TechnicalLabel(); tab.AddChild(_everydayStatus);
        Button Add(string key, Action action)
        {
            var b = new Button { Text = Text(key), FocusMode = Control.FocusModeEnum.None };
            _hazardButtons.Add((b, key)); b.Pressed += () => { action(); RefreshTabletContents(); }; tab.AddChild(b); return b;
        }
        _treatDogButton = Add("city.encounters.treat", GiveCityDogTreat);
        _payParkingButton = Add("city.encounters.pay", PayCityParking);
        _refuseParkingButton = Add("city.encounters.refuse", RefuseCityParking);
        _recoverChickenButton = Add("city.encounters.recover", RecoverCityGimbalina);
        RefreshCityEverydayStatus();
    }
    private string CityEverydayPrompt() => _everyday.DogActive
        ? string.Format(Text("city.encounters.dog_active"), Math.Ceiling(DogPursuitTracker.MaxPursuitDurationSeconds - _everyday.DogElapsedSeconds))
        : _everyday.Parking is { Phase: CityParkingPhase.Refused } p
            ? string.Format(Text("city.encounters.refusal_countdown"), Math.Ceiling(p.RetaliationRemainingSeconds))
            : _everyday.Parking is not null ? Text("city.encounters.parking_offer") : "";
    private void RefreshCityEverydayStatus()
    {
        if (_everydayStatus is null) return;
        _everydayStatus.Text = CityEverydayPrompt() + "\n" + string.Format(Text("city.encounters.cosmetic"),
            _everyday.CosmeticDamageFor(_locomotion.MountedVehicleMode));
        if (_gimbalina.HasFledToSafety) _everydayStatus.Text += "\n" + Text("city.encounters.chicken_safe");
        _treatDogButton!.Disabled = !_everyday.DogActive;
        _payParkingButton!.Disabled = _refuseParkingButton!.Disabled = _everyday.Parking?.Phase != CityParkingPhase.Offered;
        _recoverChickenButton!.Disabled = !_gimbalina.HasFledToSafety && _gimbalina.Stress <= 0;
    }
    private void RefreshCityEverydayLocale()
    {
        foreach (var actor in _cityDogs.Values) actor.Label.Text = Text("city.encounters.dog_label");
        foreach (var actor in _cityParking.Values) actor.Label.Text = Text("city.encounters.parking_label");
        RefreshBeaconLabels();
        RefreshParkedVehicleLabels();
        RefreshCityEverydayStatus();
    }
}
