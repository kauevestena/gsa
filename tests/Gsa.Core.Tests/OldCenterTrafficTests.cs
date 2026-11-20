using System.Diagnostics;
using Gsa.Core.Geometry;
using Gsa.Core.Traffic;
using Xunit;
using Xunit.Abstractions;

namespace Gsa.Core.Tests;

public sealed class OldCenterTrafficTests(ITestOutputHelper output)
{
    [Fact]
    public void FiveMinutesWithFiftyCarsKeepsTheOfficeClearAndTrafficMoving()
    {
        var simulator = new TrafficBenchSimulator(OldCenterTrafficNetwork.Create());
        var office = simulator.Graph.ExclusionZones.Single(z => z.Id == "office_entry");
        var weather = new CityTrafficConditions(office.CenterEnu, true, true, false, Math.Sqrt(.6));
        var agents = new List<TrafficVisualAgent>();
        const double dt = TrafficBenchSimulator.FixedDtSeconds;
        var minCarCount = int.MaxValue;
        var maxCarCount = 0;
        var minOfficeClearance = double.PositiveInfinity;
        var stoppedIntervals = 0;
        var travelled = new Dictionary<string, Vector3d>();
        var duration = Stopwatch.StartNew();
        for (var i = 0; i < 300 * 60; i++)
        {
            simulator.Advance(dt, office.CenterEnu, Vector3d.UnitY, weather);
            if (i % 60 != 0) continue;
            simulator.CopyVisualAgentsTo(agents);
            var cars = agents.Where(a => a.IsVehicle).ToArray();
            minCarCount = Math.Min(minCarCount, cars.Length);
            maxCarCount = Math.Max(maxCarCount, cars.Length);
            minOfficeClearance = Math.Min(minOfficeClearance,
                agents.Min(agent => RoadNetworkGraph.DistanceXy(agent.PositionEnu, office.CenterEnu)));
            if (i > 0 && !cars.Any(a => travelled.TryGetValue(a.Id, out var position) &&
                RoadNetworkGraph.DistanceXy(position, a.PositionEnu) > 1))
                stoppedIntervals++;
            foreach (var car in cars) travelled[car.Id] = car.PositionEnu;
        }
        output.WriteLine($"300 s simulated in {duration.Elapsed.TotalMilliseconds:F0} ms, cars {minCarCount}–{maxCarCount}, office clearance {minOfficeClearance:F1} m, checkpoint removals {simulator.BlitzRemovals}.");
        Assert.Equal(50, minCarCount);
        Assert.Equal(50, maxCarCount);
        Assert.True(minOfficeClearance >= office.RadiusMetres);
        Assert.Equal(0, stoppedIntervals);
        Assert.True(simulator.BlitzRemovals > 0);
    }

    [Fact]
    public void MovingContactSweepsTheFootprintAndIgnoresAnOverpass()
    {
        var simulator = new TrafficBenchSimulator(OldCenterTrafficNetwork.Create());
        const double dt = 1.0 / 60;
        simulator.Advance(dt);
        var agent = simulator.GetSnapshots()[0];
        var forward = new Vector3d(Math.Cos(agent.HeadingRadians), Math.Sin(agent.HeadingRadians), 0);
        var from = agent.PositionEnu + forward * 4.5;
        var to = agent.PositionEnu - forward * 4.5;
        var contact = simulator.FindVehicleContact(from, to, 1, dt);
        Assert.NotNull(contact);
        Assert.Equal(agent.Id, contact.Value.AgentId);
        Assert.True(contact.Value.ClosingSpeedMetresPerSecond > agent.SpeedMetresPerSecond);
        Assert.True(Vector3d.Dot(contact.Value.SafePlayerPositionEnu - agent.PositionEnu, forward) > 3.1);
        Assert.Null(simulator.FindVehicleContact(from + Vector3d.UnitZ * 6, to + Vector3d.UnitZ * 6, 1, dt));
    }
}
