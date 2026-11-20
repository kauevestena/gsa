using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Traffic;
using Godot;

namespace Gsa.Engine.World;

public partial class SaoDatumCityScene
{
    private void RunCityTrafficSmoke()
    {
        static void Require(bool condition, string reason)
        {
            if (!condition) throw new InvalidOperationException("City traffic smoke: " + reason);
        }

        const double dt = TrafficBenchSimulator.FixedDtSeconds;
        var original = File.ReadAllBytes(_saveManager.CurrentSavePath);
        var calmContract = _activeContract! with { Calm = true };
        _calmPolicy.SetCalmCityEnabled(false);
        _activeContract = null;
        _activeMachine = null;
        _activeStorySpec = null;
        SyncCityHazardProtection();
        var weather = _calendar.ExportSnapshot() with { IsNight = true };
        var office = new Vector3d(500228, 7400000.7, 100);
        var simulator = _traffic.Simulator;
        StepCityTraffic(dt, office, weather);

        Require(_traffic.VisualAgents.Count(a => a.IsVehicle) == 50, "wrong live traffic population");
        Require(simulator.Graph.VehicleSegments.All(s => s.StartEnu.Y >= 7400113), "vehicles are still on the tutorial graph");
        UpdateSectorStreaming(new Vector3d(500300, 7400236, 100));
        foreach (var lane in simulator.Graph.VehicleSegments)
        for (var distance = 0.0; distance <= lane.LengthMetres; distance += .75)
        {
            var point = lane.EvaluateEnu(distance);
            Require(!_cityBlockObstacles.Any(box => box.Collision && box.MinZ < point.Z + 1.5 && box.MaxZ > point.Z + .05 &&
                point.X + 1.65 > box.MinX && point.X - 1.65 < box.MaxX && point.Y + 1.65 > box.MinY && point.Y - 1.65 < box.MaxY),
                "a traffic lane intersects city geometry at " + point);
        }
        var flagged = _traffic.VisualAgents.First(a => a.IsErratic &&
            RoadNetworkGraph.DistanceXy(a.PositionEnu, OldCenterTrafficNetwork.CheckpointEnu) > 40);
        var originalHeading = flagged.HeadingRadians;
        StepCityTraffic(dt, flagged.PositionEnu, weather);
        Require(_cityTrafficWarningId is not null, "driver did not raise the local warning");
        Require(_traffic.VisualAgents.First(a => a.Id == flagged.Id).HeadingRadians == originalHeading,
            "erratic steering started before the warning");

        var source = _traffic.VisualAgents.First(a => a.IsVehicle && !a.IsErratic &&
            RoadNetworkGraph.DistanceXy(a.PositionEnu, OldCenterTrafficNetwork.CheckpointEnu) > 20);
        var forward = new Vector3d(Math.Cos(source.HeadingRadians), Math.Sin(source.HeadingRadians), 0);
        var radius = PlatformProfile.Car.CollisionRadiusMetres;
        var from = source.PositionEnu + forward * (TrafficBenchSimulator.VehicleLengthMetres / 2 + radius + .25);
        var candidate = from - forward * .8;
        var contact = ResolveCityTrafficContact(.1, LocomotionMode.Car, from, ref candidate);
        Require(contact is { } actual && actual.AgentId == source.Id && candidate != from - forward * .8,
            "the city passed through a moving vehicle");
        _carSim = new CarSuspensionSimulator(from, source.HeadingRadians, LocomotionMode.Car);
        _rackMisaligned = false;
        _cityHazards.RackYawErrorRadians = 0;
        Require(ApplyCityTrafficImpact(contact!.Value, LocomotionMode.Car) &&
            _carSim.ConditionDamage > 0 && _rackMisaligned && _cityHazards.RackYawErrorRadians != 0,
            "traffic contact did not damage the car and shift its rack");
        var damage = _carSim.ConditionDamage;
        ApplyCityTrafficImpact(contact.Value, LocomotionMode.Car);
        Require(_carSim.ConditionDamage == damage, "one ongoing contact inflicted repeated damage");
        _carSim = null;

        for (var tick = 0; tick < 171; tick++)
            StepCityTraffic(dt, flagged.PositionEnu, weather);
        var unstable = simulator.GetSnapshots().First(a => a.Id == flagged.Id);
        Require(Math.Abs(unstable.HeadingRadians - simulator.Graph.Segments[unstable.SegmentId].HeadingRadians) > .001,
            "the warned car did not develop steering variation");
        _activeContract = calmContract;
        SyncCityHazardProtection();
        StepCityTraffic(dt, unstable.PositionEnu, weather);
        Require(!_calmPolicy.CalmCityEnabled && _cityTrafficWarningId is null && _traffic.VisualAgents.All(a => !a.IsErratic) &&
            _traffic.VisualAgents.Any(a => a.Id == flagged.Id && a.SpeedMetresPerSecond > 0),
            "a calm contract failed to cancel erratic motion while preserving traffic");
        _activeContract = null;
        _calmPolicy.SetCalmCityEnabled(true);
        SyncCityHazardProtection();
        StepCityTraffic(dt, unstable.PositionEnu, weather);
        Require(_cityTrafficWarningId is null && _traffic.VisualAgents.Count(a => a.IsVehicle) == 50,
            "Calm City failed to preserve ordinary traffic");

        TryLoadCareer();
        Require(original.SequenceEqual(File.ReadAllBytes(_saveManager.CurrentSavePath)), "traffic fixture altered the save");
        GD.Print("GSA_CITY_TRAFFIC_SMOKE_OK routes=old_center cars=50 contact=resolved rack=shifted repeated_damage=blocked warning=before_steering calm=contract_and_setting");
    }
}
