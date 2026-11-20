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
            candidate = ResolveObstaclePushout(previous, hit.SafePlayerPositionEnu, radius, out _);
        return contact;
    }

    private bool ApplyCityTrafficImpact(TrafficVehicleContact hit, LocomotionMode mode)
    {
        var alreadyTouching = hit.AgentId == _cityTrafficContactId;
        _cityTrafficContactId = hit.AgentId;
        _cityTrafficContactGrace = .45;
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
