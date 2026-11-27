using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Camera;
using Gsa.Core.Traffic;
using Gsa.Core.World;
using Gsa.Engine.Visuals;
using Gsa.Missions.Contracts;
using Gsa.Missions.Economy;
using Gsa.Missions.Gameplay;
using Gsa.Missions.Quality;
using Godot;

namespace Gsa.Engine.World;

public partial class SaoDatumCityScene
{
    private sealed class CommerceStreetCar
    {
        public required string Id { get; init; }
        public required Node3D Node { get; init; }
        public required double LaneNorth { get; init; }
        public required int DirectionEast { get; init; } // +1 eastbound, -1 westbound
        public required double MinEast { get; init; }
        public required double MaxEast { get; init; }
        public required double BaseSpeedMps { get; init; }
        public required bool IsErratic { get; init; }
        public double East { get; set; }
        public double HeadingRadians { get; set; }
        public Vector3d PositionEnu { get; set; }
        public Vector3d PreviousPositionEnu { get; set; }
        public double PauseSeconds { get; set; }
        public double PhaseOffset { get; init; }
    }

    private sealed class StreetDogActor
    {
        public required string Id { get; init; }
        public required Vector3d HomeEnu { get; init; }
        public required Node3D Node { get; init; }
        public required Label3D Label { get; init; }
        public Vector3d PositionEnu { get; set; }
        public bool Active { get; set; }
        public double ElapsedSeconds { get; set; }
        public double CooldownSeconds { get; set; }
        public bool AppliedContact { get; set; }
        public double BarkCooldownSeconds { get; set; }
    }

    private readonly AutoCameraTriggerController _autoCamera = new();
    private bool _pruneRejectedFramesInOffice;
    private double _lastAngularVelocityRadPerSec;
    private double _joltBlurRemainingSeconds;

    private readonly List<CommerceStreetCar> _commerceCars = [];
    private readonly List<StreetDogActor> _streetDogs = [];
    private double _mudCooldownSeconds;
    private double _streetDogStumbleSeconds;
    private double _earlySpotterAttentionSeconds;
    private double _earlySpotterCooldownSeconds;
    private bool _earlySpotterWarningShown;
    private string? _activeEarlySpotterId;
    private double _trioHornCooldownSeconds;
    private double _pigeonClutterSeconds;
    private double _pigeonCooldownSeconds;
    private double _commerceTrafficContactGrace;
    private double _commerceHornCooldown;
    private Vector3d? _activeMarathonCenterEnu;

    private void BuildCityEarlyHazardActors(Node3D visualBase)
    {
        _commerceCars.Clear();
        _streetDogs.Clear();

        var carSpecs = new (double East, double North, int Dir, double Speed, bool Erratic, int ColorIdx, double Phase)[]
        {
            (500028, 7399986.6, +1, 8.2, false, 0, 0.0),
            (500084, 7399986.6, +1, 8.5, false, 1, 0.7),
            (500142, 7399986.6, +1, 9.2, true,  3, 1.3),
            (500196, 7399986.6, +1, 8.0, false, 2, 2.0),
            (500046, 7399991.8, -1, 8.4, false, 2, 0.4),
            (500112, 7399991.8, -1, 8.8, false, 0, 1.5),
            (500178, 7399991.8, -1, 9.4, true,  3, 2.6),
            (500216, 7399991.8, -1, 8.1, false, 1, 3.1),
            (500038, 7400045.2, +1, 8.6, false, 1, 0.9),
            (500138, 7400045.2, +1, 8.3, false, 2, 1.8),
            (500078, 7400048.8, -1, 8.7, false, 0, 2.3),
            (500188, 7400048.8, -1, 8.4, false, 1, 0.2),
        };

        var hazardColors = new Color[]
        {
            new(0.24f, 0.48f, 0.74f),
            new(0.78f, 0.78f, 0.80f),
            new(0.18f, 0.56f, 0.42f),
            new(0.92f, 0.28f, 0.16f),
        };

        var trafficRoot = new Node3D { Name = "CommerceBoulevardTraffic" };
        visualBase.AddChild(trafficRoot);

        for (var i = 0; i < carSpecs.Length; i++)
        {
            var s = carSpecs[i];
            var heading = s.Dir > 0 ? 0.0 : Math.PI;
            var pos = new Vector3d(s.East, s.North, 100.0);
            var carNode = new Node3D { Name = $"CommerceCar_{i}", Position = _coordinates.ToGodot(pos) };
            carNode.AddChild(new MeshInstance3D
            {
                Mesh = GsaMeshBuilder.BuildTrafficCarMesh(s.ColorIdx),
                MaterialOverride = GsaMeshBuilder.SolidMat(hazardColors[s.ColorIdx], roughness: 0.45f),
            });
            if (s.Erratic)
            {
                carNode.AddChild(GsaMeshBuilder.Box("ErraticBeacon", new Vector3(0.35f, 0.16f, 0.35f), new Vector3(0f, 1.56f, 0f),
                    GsaMeshBuilder.SolidMat(new Color(1.0f, 0.62f, 0.12f), emit: true, emissionEnergy: 2.0f)));
            }
            carNode.Rotation = new Vector3(0f, (float)(heading - Math.PI * 0.5), 0f);
            trafficRoot.AddChild(carNode);
            _commerceCars.Add(new CommerceStreetCar
            {
                Id = $"commerce_car_{i}",
                Node = carNode,
                LaneNorth = s.North,
                DirectionEast = s.Dir,
                MinEast = 500008.0,
                MaxEast = 500234.0,
                BaseSpeedMps = s.Speed,
                IsErratic = s.Erratic,
                East = s.East,
                HeadingRadians = heading,
                PositionEnu = pos,
                PreviousPositionEnu = pos,
                PhaseOffset = s.Phase,
            });
        }

        var dogRoot = new Node3D { Name = "StreetAlleyDogs" };
        visualBase.AddChild(dogRoot);
        var dogSpots = new (string Id, Vector3d Pos)[]
        {
            ("street_dog_commerce", new(500096.0, 7400002.0, 100.0)),
            ("street_dog_drainage", new(500172.0, 7400138.0, 100.0)),
            ("street_dog_sidewalk", new(500120.0, 7400306.5, 100.0)),
        };

        foreach (var (id, rawPos) in dogSpots)
        {
            var home = new Vector3d(rawPos.X, rawPos.Y, GetTerrainElevationEnu(rawPos.X, rawPos.Y));
            var dogNode = new Node3D { Name = "StreetDog_" + id, Position = _coordinates.ToGodot(home) };
            void AddPart(Vector3 pos, Vector3 size, Color color) => dogNode.AddChild(new MeshInstance3D
            {
                Position = pos,
                Mesh = new BoxMesh { Size = size },
                MaterialOverride = GsaMeshBuilder.SolidMat(color),
            });
            var tan = new Color(0.54f, 0.34f, 0.16f);
            AddPart(new(0f, 0.36f, 0f), new(0.64f, 0.32f, 0.28f), tan);
            AddPart(new(0.36f, 0.50f, 0f), new(0.24f, 0.26f, 0.24f), tan);
            AddPart(new(0.50f, 0.45f, 0f), new(0.15f, 0.11f, 0.16f), new Color(0.16f, 0.11f, 0.08f));
            foreach (var lx in new[] { -0.20f, 0.20f })
                foreach (var lz in new[] { -0.10f, 0.10f })
                    AddPart(new(lx, 0.15f, lz), new(0.08f, 0.28f, 0.08f), tan);
            var label = new Label3D
            {
                Text = Text("city.encounters.street_dog_label"),
                Position = new Vector3(0f, 1.35f, 0f),
                FontSize = 32,
                PixelSize = 0.0055f,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                Modulate = Colors.White,
                OutlineModulate = Colors.Black,
                OutlineSize = 4,
                AlphaCut = Label3D.AlphaCutMode.Disabled,
                VisibilityRangeEnd = 34f,
            };
            dogNode.AddChild(label);
            dogRoot.AddChild(dogNode);
            _streetDogs.Add(new StreetDogActor
            {
                Id = id,
                HomeEnu = home,
                Node = dogNode,
                Label = label,
                PositionEnu = home,
            });
        }
    }

    private void StepCommerceBoulevardTraffic(double dt, Vector3d playerPos, WeatherAndTimeSnapshot weather)
    {
        _commerceTrafficContactGrace = Math.Max(0.0, _commerceTrafficContactGrace - dt);
        _commerceHornCooldown = Math.Max(0.0, _commerceHornCooldown - dt);
        var calm = _calmPolicy.IsProtectionActive;
        var span = 500234.0 - 500008.0;
        var time = _tick * (1.0 / 60.0);

        foreach (var car in _commerceCars)
        {
            car.PreviousPositionEnu = car.PositionEnu;
            if (car.PauseSeconds > 0.0)
            {
                car.PauseSeconds = Math.Max(0.0, car.PauseSeconds - dt);
                continue;
            }

            var erraticActive = car.IsErratic && !calm;
            var speedMult = erraticActive ? 1.0 + 0.24 * Math.Sin(time * 2.1 + car.PhaseOffset) : 1.0;
            if (weather.IsRaining) speedMult *= 0.85;
            var speed = car.BaseSpeedMps * speedMult;

            // Slow/stop if player is directly in the car's lane path unless erratic
            var aheadDist = (playerPos.X - car.East) * car.DirectionEast;
            if (!erraticActive && aheadDist > 0.0 && aheadDist < 9.0 && Math.Abs(playerPos.Y - car.LaneNorth) < 2.2)
                speed *= 0.25;

            car.East += car.DirectionEast * speed * dt;
            if (car.East > car.MaxEast) car.East -= span;
            else if (car.East < car.MinEast) car.East += span;

            var weave = erraticActive ? 1.15 * Math.Sin(time * 2.4 + car.PhaseOffset) : 0.0;
            var north = car.LaneNorth + weave;
            var yawOffset = erraticActive ? 0.14 * Math.Cos(time * 2.4 + car.PhaseOffset) * car.DirectionEast : 0.0;
            car.HeadingRadians = (car.DirectionEast > 0 ? 0.0 : Math.PI) + yawOffset;
            car.PositionEnu = new Vector3d(car.East, north, 100.0);
            car.Node.Position = _coordinates.ToGodot(car.PositionEnu);
            car.Node.Rotation = new Vector3(0f, (float)(car.HeadingRadians - Math.PI * 0.5), 0f);

            if (erraticActive && _commerceHornCooldown <= 0.0 &&
                TrainingContractSession.HorizontalDistance(playerPos, car.PositionEnu) < 22.0)
            {
                _commerceHornCooldown = 5.5;
                _audio.PlayHorn(LocomotionMode.Car);
                if (string.IsNullOrEmpty(_hazardNotice))
                    _feedbackText = Text("city.traffic.erratic_warning");
            }
        }
    }

    private bool ResolveCommerceTrafficContact(double dt, LocomotionMode mode, Vector3d prevPos, ref Vector3d candidatePos)
    {
        if (_commerceCars.Count == 0 || mode == LocomotionMode.OnFoot)
            return false;

        var nearSouth = candidatePos.Y >= 7399982.0 && candidatePos.Y <= 7399996.5;
        var nearNorth = candidatePos.Y >= 7400041.5 && candidatePos.Y <= 7400053.5;
        if (!nearSouth && !nearNorth)
            return false;

        var radius = PlatformProfile.ForMode(mode).CollisionRadiusMetres;
        var halfLen = 2.0 + radius;
        var halfWid = 1.0 + radius;

        foreach (var car in _commerceCars)
        {
            if (Math.Abs(candidatePos.X - car.PositionEnu.X) > halfLen ||
                Math.Abs(candidatePos.Y - car.PositionEnu.Y) > halfWid)
                continue;

            var dx = candidatePos.X - car.PositionEnu.X;
            var dy = candidatePos.Y - car.PositionEnu.Y;
            var penX = halfLen - Math.Abs(dx);
            var penY = halfWid - Math.Abs(dy);
            if (penY <= penX)
                candidatePos = new Vector3d(candidatePos.X, car.PositionEnu.Y + (dy >= 0 ? halfWid + 0.05 : -(halfWid + 0.05)), candidatePos.Z);
            else
                candidatePos = new Vector3d(car.PositionEnu.X + (dx >= 0 ? halfLen + 0.05 : -(halfLen + 0.05)), candidatePos.Y, candidatePos.Z);

            car.PauseSeconds = 1.4;
            if (_commerceTrafficContactGrace <= 0.0)
            {
                _commerceTrafficContactGrace = 0.75;
                var closing = Math.Max(3.0, car.BaseSpeedMps * 0.65);
                _audio.PlayCollisionThud(closing);
                _audio.PlayHorn(LocomotionMode.Car);
                _chaseCamera.TriggerShake(0.26f, 0.24f);
                SpawnImpactSparks(candidatePos, closing);
                _joltBlurRemainingSeconds = Math.Max(_joltBlurRemainingSeconds, 0.85);
                _feedbackText = Text("city.traffic.contact");
                if (mode <= LocomotionMode.Bicycle)
                    _gimbalina.ApplyStartle(StartleEventKind.Pothole);
                else if (_carSim is not null)
                {
                    var imp = _carSim.ApplyCollisionImpact(closing * 0.55, car.Id, _fleet.GetSkillRank(SkillTrackKind.DefensiveDriving));
                    if (imp.TriggersRackMisalignment)
                    {
                        _rackMisaligned = true;
                        if (_cityHazards.RackYawErrorRadians == 0) _cityHazards.RackYawErrorRadians = 0.01;
                        _feedbackText = Text("city.rack.misaligned");
                    }
                }
            }
            return true;
        }
        return false;
    }

    private bool IsOccludedByCommerceCar(Vector3d fromEnu, Vector3d toEnu)
    {
        var delta = toEnu - fromEnu;
        var len = delta.Length;
        if (len <= 1e-4) return false;
        var dir = delta / len;
        var carSize = new Vector3d(4.0, 1.9, 1.55);

        foreach (var car in _commerceCars)
        {
            var center = new Vector3d(car.PositionEnu.X, car.PositionEnu.Y, car.PositionEnu.Z + 0.8);
            if (CameraVisibilityEvaluator.TryIntersectRayBox(fromEnu, dir, center, carSize, out var tHit) &&
                tHit > 0.1 && tHit < len - 0.1)
                return true;
        }
        if (_activeMarathonCenterEnu is { } mCenter)
        {
            if (CameraVisibilityEvaluator.TryIntersectRayBox(fromEnu, dir, mCenter + Vector3d.UnitZ * 0.9, new Vector3d(3.2, 3.2, 1.8), out var mHit) &&
                mHit > 0.1 && mHit < len - 0.1)
                return true;
        }
        return false;
    }

    private bool HasActiveStreetDogEncounter() => _streetDogs.Any(d => d.Active);

    private bool TryPacifyStreetDog()
    {
        var active = _streetDogs.FirstOrDefault(d => d.Active);
        if (active is null) return false;
        active.Active = false;
        active.CooldownSeconds = 35.0;
        active.Node.Position = _coordinates.ToGodot(active.HomeEnu);
        active.PositionEnu = active.HomeEnu;
        active.Label.Modulate = Colors.White;
        _audio.PlayOffloadChime();
        _feedbackText = _hazardNotice = Text("city.encounters.treated");
        return true;
    }

    private LocomotionInput ApplyStreetDogAndMarathonSlowdown(LocomotionInput input, LocomotionMode mode, Vector3d pos)
    {
        if (_streetDogStumbleSeconds > 0.0 && mode <= LocomotionMode.Scooter)
            return new(Math.Min(input.Throttle, 0.25), Math.Max(input.Brake, 0.4), input.Steer, input.InteractPressed);
        if (_activeMarathonCenterEnu is { } mc && mode <= LocomotionMode.Scooter &&
            TrainingContractSession.HorizontalDistance(pos, mc) < 4.2)
            return new(Math.Min(input.Throttle, 0.35), input.Brake, input.Steer, input.InteractPressed);
        return input;
    }

    private void StepEarlyStreetAndSpotterHazards(double dt, Vector3d pos, double speed, LocomotionMode mode, WeatherAndTimeSnapshot weather)
    {
        _mudCooldownSeconds = Math.Max(0.0, _mudCooldownSeconds - dt);
        _streetDogStumbleSeconds = Math.Max(0.0, _streetDogStumbleSeconds - dt);
        _earlySpotterCooldownSeconds = Math.Max(0.0, _earlySpotterCooldownSeconds - dt);
        _trioHornCooldownSeconds = Math.Max(0.0, _trioHornCooldownSeconds - dt);
        _pigeonCooldownSeconds = Math.Max(0.0, _pigeonCooldownSeconds - dt);

        var protectedNow = _calmPolicy.IsProtectionActive || _citySafeZone;

        // 1. Mud Puddles (splash camera lens if driven/pushed through fast)
        if (_mudCooldownSeconds <= 0.0 && mode != LocomotionMode.OnFoot)
        {
            var mudThreshold = mode switch
            {
                LocomotionMode.Handcart => 1.25,
                LocomotionMode.Bicycle => 2.7,
                LocomotionMode.Scooter => 2.7,
                _ => 3.8,
            };
            if (weather.IsRaining) mudThreshold *= 0.8;
            if (Math.Abs(speed) >= mudThreshold)
            {
                foreach (var puddle in _landmarks.MudPuddleCentersEnu)
                {
                    if (TrainingContractSession.HorizontalDistance(pos, puddle) < 1.85)
                    {
                        _mudCooldownSeconds = 8.0;
                        _cityHazards.Lens.ApplySplatter(0.28);
                        _audio.PlayCollisionThud(2.2);
                        _audio.PlayAlert();
                        _chaseCamera.TriggerShake(0.18f, 0.16f);
                        _feedbackText = _hazardNotice = Text("city.hazard.mud_splatter");
                        break;
                    }
                }
            }
        }

        // 2. Early Street Alley Dogs (OnFoot / Handcart / Bicycle / Scooter)
        foreach (var dog in _streetDogs)
        {
            dog.CooldownSeconds = Math.Max(0.0, dog.CooldownSeconds - dt);
            dog.BarkCooldownSeconds = Math.Max(0.0, dog.BarkCooldownSeconds - dt);

            if (protectedNow || mode >= LocomotionMode.Car || _gimbalina.HasFledToSafety)
            {
                if (dog.Active)
                {
                    dog.Active = false;
                    dog.Node.Position = _coordinates.ToGodot(dog.HomeEnu);
                    dog.PositionEnu = dog.HomeEnu;
                    dog.Label.Modulate = Colors.White;
                }
                continue;
            }

            var distToHome = TrainingContractSession.HorizontalDistance(pos, dog.HomeEnu);
            if (!dog.Active && dog.CooldownSeconds <= 0.0 && distToHome <= 15.0 && !_streetDogs.Any(other => other.Active))
            {
                dog.Active = true;
                dog.ElapsedSeconds = 0.0;
                dog.AppliedContact = false;
                dog.PositionEnu = dog.HomeEnu;
                dog.BarkCooldownSeconds = 1.6;
                _audio.PlayDogBark();
                _feedbackText = _hazardNotice = Text("city.hazard.street_dog_warning");
                dog.Label.Modulate = new Color(1.0f, 0.65f, 0.18f);
            }

            if (!dog.Active)
                continue;

            dog.ElapsedSeconds += dt;
            var distToPlayer = TrainingContractSession.HorizontalDistance(pos, dog.PositionEnu);
            if (distToPlayer > 24.0 || dog.ElapsedSeconds >= 10.0)
            {
                dog.Active = false;
                dog.CooldownSeconds = 35.0;
                dog.Node.Position = _coordinates.ToGodot(dog.HomeEnu);
                dog.PositionEnu = dog.HomeEnu;
                dog.Label.Modulate = Colors.White;
                continue;
            }

            if (dog.BarkCooldownSeconds <= 0.0)
            {
                dog.BarkCooldownSeconds = 2.1;
                _audio.PlayDogBark();
            }

            if (dog.ElapsedSeconds >= 2.5)
            {
                var toPlayer = new Vector3d(pos.X - dog.PositionEnu.X, pos.Y - dog.PositionEnu.Y, 0.0);
                var len = toPlayer.Length;
                if (len > 0.8)
                {
                    var stepTarget = dog.PositionEnu + (toPlayer / len) * (2.6 * dt);
                    var resolved = ResolveObstaclePushout(dog.PositionEnu, stepTarget, 0.35, out _);
                    dog.PositionEnu = new Vector3d(resolved.X, resolved.Y, GetTerrainElevationEnu(resolved.X, resolved.Y));
                    dog.Node.Position = _coordinates.ToGodot(dog.PositionEnu);
                    dog.Node.Rotation = new Vector3(0f, (float)Math.Atan2(toPlayer.Y, toPlayer.X), 0f);
                }

                if (!dog.AppliedContact && len < 1.65)
                {
                    dog.AppliedContact = true;
                    _streetDogStumbleSeconds = 1.2;
                    _joltBlurRemainingSeconds = Math.Max(_joltBlurRemainingSeconds, 0.9);
                    _gimbalina.ApplyStartle(StartleEventKind.Dog);
                    _chaseCamera.TriggerShake(0.22f, 0.22f);
                    _audio.PlayDogBark();
                    _audio.PlayCollisionThud(1.8);
                    _feedbackText = _hazardNotice = Text("city.hazard.street_dog_startle");
                }
            }
        }

        // 3. Early Faction Spotters (warn, then slap flyer on lens if lingering near them while collecting)
        var collecting = _activeContract is not null &&
                         _activeMachine?.State is (ContractState.Aceito or ContractState.Coletando or ContractState.Retrabalho);
        (string Id, FactionId Faction, Vector3d PositionEnu)? nearestSpotter = null;
        var minSpotterDist = 15.0;
        if (!protectedNow && collecting)
        {
            foreach (var sp in _landmarks.EarlySpottersEnu)
            {
                var d = TrainingContractSession.HorizontalDistance(pos, sp.PositionEnu);
                if (d < minSpotterDist)
                {
                    minSpotterDist = d;
                    nearestSpotter = sp;
                }
            }
        }

        if (nearestSpotter is { } spotter && _earlySpotterCooldownSeconds <= 0.0)
        {
            _activeEarlySpotterId = spotter.Id;
            _earlySpotterAttentionSeconds += dt;
            if (_earlySpotterAttentionSeconds >= 1.0 && !_earlySpotterWarningShown)
            {
                _earlySpotterWarningShown = true;
                _audio.PlayAlert();
                _feedbackText = _hazardNotice = Text("city.hazard.spotter_warning");
            }
            if (_earlySpotterAttentionSeconds >= 4.2 && minSpotterDist <= 14.0)
            {
                _earlySpotterAttentionSeconds = 0.0;
                _earlySpotterWarningShown = false;
                _earlySpotterCooldownSeconds = 35.0;
                _cityHazards.Lens.ApplySplatter(0.35);
                _audio.PlayAlert(critical: true);
                _chaseCamera.TriggerShake(0.18f, 0.14f);
                _feedbackText = _hazardNotice = Text("city.hazard.spotter_flyer");
            }
        }
        else
        {
            _earlySpotterAttentionSeconds = Math.Max(0.0, _earlySpotterAttentionSeconds - 1.5 * dt);
            if (_earlySpotterAttentionSeconds <= 0.1)
            {
                _earlySpotterWarningShown = false;
                _activeEarlySpotterId = null;
            }
        }

        foreach (var sp in _landmarks.EarlySpottersEnu)
        {
            var labelText = sp.Id == _activeEarlySpotterId && _earlySpotterWarningShown ? "⚠ !" : null;
            if (labelText is not null && _cityBaseVisuals?.FindChild(sp.Id, true, false) is Node3D spNode &&
                TrainingContractSession.HorizontalDistance(pos, sp.PositionEnu) <= 22.0)
            {
                var toPlayer = pos - sp.PositionEnu;
                spNode.Rotation = new Vector3(0f, (float)Math.Atan2(toPlayer.Y, toPlayer.X), 0f);
            }
        }
    }

    private void StepProximityAbsurdEventEffects(double dt, Vector3d pos, LocomotionMode mode, AbsurdUrbanEventState? activeEv)
    {
        _activeMarathonCenterEnu = null;
        if (_calmPolicy.IsProtectionActive || activeEv is null)
        {
            _pigeonClutterSeconds = 0.0;
            return;
        }

        var onSouthCorridor = pos.Y < 7400090.0;
        if (activeEv.Kind == AbsurdUrbanEventKind.TrioEletrico && _landmarks.TrioTruckNode is { Visible: true } trio)
        {
            var trioPos = onSouthCorridor
                ? new Vector3d(500118.0 + Math.Sin(_tick * 0.015) * 68.0, 7399989.5, 100.0)
                : new Vector3d(500240.0 + Math.Sin(_tick * 0.02) * 35.0, 7400236.0, 100.0);
            trio.Position = _coordinates.ToGodot(trioPos);
            if (TrainingContractSession.HorizontalDistance(pos, trioPos) < 13.0 && _trioHornCooldownSeconds <= 0.0)
            {
                _trioHornCooldownSeconds = 3.5;
                _gimbalina.ApplyStartle(StartleEventKind.Horn);
                _audio.PlayHorn(LocomotionMode.Van);
                _chaseCamera.TriggerShake(0.14f, 0.12f);
                _feedbackText = Text("city.event.trio");
            }
        }
        else if (activeEv.Kind == AbsurdUrbanEventKind.StreetMarathon && _landmarks.MarathonPackNode is { Visible: true } pack)
        {
            var packPos = onSouthCorridor
                ? new Vector3d(500120.0 + Math.Cos(_tick * 0.018) * 62.0, 7399993.5, 100.0)
                : new Vector3d(500360.0, 7400236.0 + Math.Sin(_tick * 0.03) * 28.0, 100.0);
            pack.Position = _coordinates.ToGodot(packPos);
            _activeMarathonCenterEnu = packPos;
        }
        else if (activeEv.Kind == AbsurdUrbanEventKind.PigeonFlock && _landmarks.PigeonFlockNode is { Visible: true } flock)
        {
            var flockPos = onSouthCorridor
                ? new Vector3d(Math.Clamp(pos.X, 500060.0, 500190.0), 7399996.0, 102.6)
                : new Vector3d(500350.0, 7400380.0, 103.5);
            flock.Position = _coordinates.ToGodot(flockPos);
            if (TrainingContractSession.HorizontalDistance(pos, flockPos) <= 5.5 && _pigeonCooldownSeconds <= 0.0)
            {
                _pigeonClutterSeconds += dt;
                if (_pigeonClutterSeconds >= 1.6)
                {
                    _pigeonClutterSeconds = 0.0;
                    _pigeonCooldownSeconds = 25.0;
                    _cityHazards.Lens.ApplySplatter(0.16);
                    _audio.PlayAlert();
                    _feedbackText = Text("city.event.pigeons");
                }
            }
            else
            {
                _pigeonClutterSeconds = Math.Max(0.0, _pigeonClutterSeconds - dt);
            }
        }
    }

    private static double PotholeTriggerSpeedMps(LocomotionMode mode) => mode switch
    {
        LocomotionMode.Handcart => 1.15,
        LocomotionMode.Bicycle => 2.5,
        LocomotionMode.Scooter => 4.0,
        _ => 4.5,
    };

    private bool ShouldAutoCaptureThisTick(Vector3d pos, double heading, double speed, WeatherAndTimeSnapshot weather)
    {
        if (_autoCamera.ManualMode || _gtaHud.IsCutsceneVisible || _tabletPanel.Visible || _quickParkRun || _citySaveSmoke)
            return false;
        if (_activeContract is not { } contract || _activeMachine?.State is not (ContractState.Aceito or ContractState.Coletando or ContractState.Retrabalho))
            return false;
        if (_gimbalina.HasFledToSafety)
            return false;

        if (IsCityLidarCollection(contract) || IsFinalPavementSurvey(contract) || IsProceduralPavementSurvey(contract))
            return false;

        if (IsBoundarySurvey(contract))
        {
            if (Math.Abs(speed) > SerraBoundarySurvey.StopSpeed)
                return false;
            foreach (var (site, station, pad) in BoundaryStations())
            {
                if ((pos - pad).Length <= SerraBoundarySurvey.StationRadius &&
                    !_pendingBoundaryShots.Concat(_protectedBoundaryShots).Any(s => s.MonumentId == site.Id && s.StationIndex == station))
                    return true;
            }
            return false;
        }

        if (contract.AreaId == BusSurveyCorridor.AreaId)
        {
            if (CurrentActiveMode() < LocomotionMode.Car || !BusSurveyCorridor.IsSurveyWindow(weather.HourOfDay) ||
                !BusSurveyCorridor.Contains(pos) || _navigationRig?.Estimator?.Current is null)
                return false;

            var visibleStops = contract.RequiredFeatureIds
                .Where(fid => _manifestObjectsById.TryGetValue(fid, out var fo) &&
                              TrainingContractSession.HorizontalDistance(pos, fo.PositionEnu) <= 26.0)
                .ToArray();
            return visibleStops.Length > 0 && _autoCamera.ShouldAutoTriggerStereoTarget(
                pos, visibleStops, requiredViewsPerTarget: 2, minBaselineMetres: 4.5, _cityHazards.Lens.ObstructionFraction);
        }

        if (!SupportsCityDelivery(contract))
            return false;

        if (CurrentActiveMode() >= LocomotionMode.Car && _navigationRig?.Estimator?.Current is null)
            return false;

        var camPose = new EstimatedBodyPose(
            weather.CalendarTimeSeconds,
            pos + Vector3d.UnitZ * 1.4,
            Quaterniond.FromAxisAngle(Vector3d.UnitZ, heading));
        var camToMap = CameraGeometry.CameraToMap(camPose, CameraMountEstimate.ForwardFacing);
        var mapToCam = camToMap.Inverse();
        var projector = new CameraProjector(CameraSensorProfile.Initial.Intrinsics);

        if (contract.Product == "images")
        {
            var hasIncompleteTarget = false;
            foreach (var fid in contract.RequiredFeatureIds)
            {
                var coveredCells = _coveredCellsByTarget.TryGetValue(fid, out var cset) ? cset.Count : 0;
                var needCells = _cityPhotoCoverage is not null ? _cityPhotoCoverage.GetTargetCellCount(fid) : 0;
                var done = needCells > 0 ? coveredCells >= needCells : _observedTargetIds.Contains(fid);
                if (done) continue;
                if (!_manifestObjectsById.TryGetValue(fid, out var obj)) continue;
                if (TrainingContractSession.HorizontalDistance(pos, obj.PositionEnu) > 30.0) continue;
                if (IsTargetInFrustum(mapToCam, projector, obj.PositionEnu))
                {
                    hasIncompleteTarget = true;
                    break;
                }
            }
            return _autoCamera.ShouldAutoTriggerCoverage(pos, heading, hasIncompleteTarget, _cityHazards.Lens.ObstructionFraction);
        }

        if (contract.Product == "feature_inventory")
        {
            var isSidewalk = contract.AreaId == CitySurveyInventory.SidewalkArea;
            var visibleTargets = new List<string>(4);
            foreach (var fid in contract.RequiredFeatureIds)
            {
                if (!_manifestObjectsById.TryGetValue(fid, out var obj)) continue;
                if (TrainingContractSession.HorizontalDistance(pos, obj.PositionEnu) > 28.0) continue;
                if (isSidewalk)
                {
                    var marksVisible = new[] { fid + "_curb", fid + "_back", fid + "_foot" }
                        .All(mid => _manifestObjectsById.TryGetValue(mid, out var mo) && IsTargetInFrustum(mapToCam, projector, mo.PositionEnu));
                    var controlsVisible = new[] { fid + "_control_a", fid + "_control_b", fid + "_control_c" }
                        .Count(cid => _manifestObjectsById.TryGetValue(cid, out var co) && IsTargetInFrustum(mapToCam, projector, co.PositionEnu));
                    if (marksVisible && controlsVisible >= 2)
                        visibleTargets.Add(fid);
                }
                else if (IsTargetInFrustum(mapToCam, projector, obj.PositionEnu))
                {
                    visibleTargets.Add(fid);
                }
            }
            return visibleTargets.Count > 0 && _autoCamera.ShouldAutoTriggerStereoTarget(
                pos, visibleTargets, requiredViewsPerTarget: 2, minBaselineMetres: AutoCameraTriggerController.MinStereoBaselineMetres,
                currentLensObstruction: _cityHazards.Lens.ObstructionFraction);
        }

        return false;
    }

    private static bool IsTargetInFrustum(RigidTransform mapToCam, CameraProjector projector, Vector3d targetEnu)
    {
        var pCam = mapToCam.Transform(targetEnu);
        if (pCam.Z <= 0.3 || pCam.Z > CameraSensorProfile.Initial.RangeMetres)
            return false;
        var proj = projector.ProjectCameraPoint(pCam);
        if (!proj.IsVisible || proj.Pixel is not { } px)
            return false;
        var w = projector.Intrinsics.Width;
        var h = projector.Intrinsics.Height;
        return px.U >= w * 0.05 && px.U <= w * 0.95 && px.V >= h * 0.05 && px.V <= h * 0.95;
    }
}

