using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Traffic;
using Godot;

namespace Gsa.Engine.World;

public partial class SaoDatumCityScene
{
    private string? _cityTrafficWarningId;
    private string? _cityTrafficContactId;
    private double _cityTrafficContactGrace;
    private double _cityTrafficWarningSilence;

    private void StepCityTraffic(double delta, Vector3d player, WeatherAndTimeSnapshot weather)
    {
        _cityTrafficContactGrace = Math.Max(0, _cityTrafficContactGrace - delta);
        if (_cityTrafficContactGrace <= 0) _cityTrafficContactId = null;
        _cityTrafficWarningSilence = Math.Max(0, _cityTrafficWarningSilence - delta);

        var camera = GetViewport().GetCamera3D();
        var cameraPosition = camera is null ? player : _coordinates.ToMap(camera.GlobalPosition);
        var cameraForward = camera is null ? (Vector3d?)null :
            _coordinates.ToMap(camera.GlobalPosition - camera.GlobalTransform.Basis.Z) - cameraPosition;
        var protectedTraffic = _calmPolicy.IsProtectionActive || _activeContract?.Calm == true;
        var conditions = new CityTrafficConditions(player, weather.IsNight, weather.IsWeekend, protectedTraffic,
            Math.Sqrt(weather.SurfaceGripMultiplier));
        var removalsBefore = _traffic.Simulator.BlitzRemovals;
        _traffic.StepAndSync(delta, player, cameraPosition, cameraForward, conditions);
        if (_traffic.Simulator.BlitzRemovals > removalsBefore &&
            RoadNetworkGraph.DistanceXy(player, OldCenterTrafficNetwork.CheckpointEnu) < 60)
            _feedbackText = Text("city.traffic.blitz_removed");

        string? nearest = null;
        var minDistanceSquared = 50.0 * 50.0;
        foreach (var agent in _traffic.VisualAgents)
        {
            if (!agent.IsErratic) continue;
            var offset = agent.PositionEnu - player;
            var distanceSquared = Vector3d.Dot(offset, offset);
            if (distanceSquared < minDistanceSquared)
            {
                minDistanceSquared = distanceSquared;
                nearest = agent.Id;
            }
        }

        if (nearest != _cityTrafficWarningId && nearest is not null && _cityTrafficWarningSilence == 0)
        {
            _audio.PlayHorn(LocomotionMode.Car);
            _cityTrafficWarningSilence = 2.5;
        }
        _cityTrafficWarningId = nearest;
    }

    private TrafficVehicleContact? ResolveCityTrafficContact(double dt, LocomotionMode mode,
        Vector3d previous, ref Vector3d candidate)
    {
        var radius = PlatformProfile.ForMode(mode).CollisionRadiusMetres;
        var contact = _traffic.Simulator.FindVehicleContact(previous, candidate, radius, dt);
        if (contact is { } hit)
        {
            _traffic.Simulator.NotifyAgentContact(hit.AgentId);
            candidate = ResolveObstaclePushout(previous, hit.SafePlayerPositionEnu, radius, out _);
            return hit;
        }

        var pedContact = _traffic.Simulator.FindPedestrianContact(previous, candidate, radius, dt);
        if (pedContact is { } pedHit)
        {
            candidate = ResolveObstaclePushout(previous, pedHit.SafePlayerPositionEnu, radius, out _);
            return pedHit;
        }

        var npcContact = FindStaticNpcContact(previous, candidate, radius, dt);
        if (npcContact is { } npcHit)
        {
            candidate = ResolveObstaclePushout(previous, npcHit.SafePlayerPositionEnu, radius, out _);
            return npcHit;
        }

        return null;
    }

    private TrafficVehicleContact? FindStaticNpcContact(Vector3d from, Vector3d to, double playerRadius, double dt)
    {
        var combined = 0.45 + playerRadius;
        var combinedSq = combined * combined;
        var segDx = to.X - from.X;
        var segDy = to.Y - from.Y;
        var segLenSq = segDx * segDx + segDy * segDy;

        TrafficVehicleContact? TestNpc(string id, Vector3d npcPos)
        {
            if (Math.Abs(to.Z - npcPos.Z) > 1.8)
                return null;
            var relFromX = from.X - npcPos.X;
            var relFromY = from.Y - npcPos.Y;
            var endDistSq = (to.X - npcPos.X) * (to.X - npcPos.X) + (to.Y - npcPos.Y) * (to.Y - npcPos.Y);
            if (relFromX * relFromX + relFromY * relFromY < combinedSq &&
                endDistSq >= relFromX * relFromX + relFromY * relFromY)
                return null;

            var t = segLenSq > 1e-10 ? Math.Clamp(-(relFromX * segDx + relFromY * segDy) / segLenSq, 0.0, 1.0) : 0.0;
            var cx = from.X + t * segDx;
            var cy = from.Y + t * segDy;
            var nx = cx - npcPos.X;
            var ny = cy - npcPos.Y;
            var distSq = nx * nx + ny * ny;
            if (distSq > combinedSq)
                return null;

            var dist = Math.Sqrt(distSq);
            var normal = dist > 1e-5
                ? new Vector3d(nx / dist, ny / dist, 0.0)
                : (segLenSq > 1e-10 ? new Vector3d(-segDx, -segDy, 0.0).Normalized() : Vector3d.UnitX);
            var safe = new Vector3d(npcPos.X + normal.X * (combined + 0.04), npcPos.Y + normal.Y * (combined + 0.04), to.Z);
            var closingSpeed = dt > 1e-9 ? Math.Max(0.0, -(segDx * normal.X + segDy * normal.Y) / dt) : 0.0;
            return new TrafficVehicleContact(id, safe, closingSpeed);
        }

        foreach (var (id, actor) in _cityParking)
        {
            if (TestNpc("npc_" + id, actor.Home) is { } hit)
                return hit;
        }
        if (!_everyday.DogActive)
        {
            foreach (var (id, actor) in _cityDogs)
            {
                if (TestNpc("npc_" + id, actor.Home) is { } hit)
                    return hit;
            }
        }
        return null;
    }

    private bool ApplyCityTrafficImpact(TrafficVehicleContact hit, LocomotionMode mode)
    {
        var alreadyTouching = hit.AgentId == _cityTrafficContactId;
        _cityTrafficContactId = hit.AgentId;
        _cityTrafficContactGrace = .45;

        if (hit.AgentId.StartsWith("ped_", StringComparison.Ordinal) ||
            hit.AgentId.StartsWith("npc_", StringComparison.Ordinal))
        {
            if (alreadyTouching || hit.ClosingSpeedMetresPerSecond < 0.75)
                return false;
            _audio.PlayCollisionThud(Math.Min(4.0, hit.ClosingSpeedMetresPerSecond));
            _chaseCamera.TriggerShake(.16f, .18f);
            _feedbackText = Text("city.traffic.pedestrian_contact");
            if (mode <= LocomotionMode.Bicycle && hit.ClosingSpeedMetresPerSecond > 1.2)
                _gimbalina.ApplyStartle(StartleEventKind.Horn);
            return false;
        }

        if (alreadyTouching || hit.ClosingSpeedMetresPerSecond < .75 || mode == LocomotionMode.OnFoot)
            return false;

        _audio.PlayCollisionThud(hit.ClosingSpeedMetresPerSecond);
        _chaseCamera.TriggerShake(.21f, .18f);
        _feedbackText = Text("city.traffic.contact");
        if (_carSim is { } car && mode >= LocomotionMode.Car)
        {
            // The player's change in velocity is smaller than the closing speed of both vehicles.
            var impact = car.ApplyCollisionImpact(hit.ClosingSpeedMetresPerSecond * .65, "city_traffic",
                _fleet.GetSkillRank(Gsa.Missions.Economy.SkillTrackKind.DefensiveDriving));
            _cityHazards.SetVehicleDamage(car.Profile.Mode, car.ConditionDamage);
            if (impact.TriggersRackMisalignment)
            {
                _rackMisaligned = true;
                if (_cityHazards.RackYawErrorRadians == 0) _cityHazards.RackYawErrorRadians = .01;
                _feedbackText = Text("city.rack.misaligned");
            }
            // The regular loss path will save the recovery and handle custody if destroyed.
            return !car.IsDestroyed;
        }
        if (mode <= LocomotionMode.Bicycle && hit.ClosingSpeedMetresPerSecond > 1.2)
            _gimbalina.ApplyStartle(StartleEventKind.Pothole);
        return false;
    }
}
