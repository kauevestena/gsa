using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Traffic;
using Gsa.Core.World;
using Xunit;

namespace Gsa.Core.Tests;

public sealed class TrafficBenchTests
{
    [Fact]
    public void TutorialNetworkMaintainsOfficeExitExclusionAndValidConnections()
    {
        var graph = RoadNetworkGraph.CreateTutorialBlockGraph();
        Assert.NotEmpty(graph.VehicleSegments);
        Assert.NotEmpty(graph.PedestrianSegments);
        Assert.Contains(graph.PedestrianSegments, s => s.IsCrosswalk);
        Assert.Contains(graph.ExclusionZones, z => z.Id == "office_entry");
    }

    [Fact]
    public void FiveMinuteRunKeeps50CarsAnd30PedestriansCirculatingWithoutPermanentBlock()
    {
        var sim = new TrafficBenchSimulator(seed: 20261114UL);
        var officeExitPlayerPos = new Vector3d(500232.0, 7400005.0, 101.5);
        TrafficStepSummary summary = null!;

        var initialSnapshots = sim.GetSnapshots().ToDictionary(a => a.Id, a => a.PositionEnu);
        var movedAgents = new HashSet<string>(StringComparer.Ordinal);
        var sawSignalWait = false;
        var sawCrosswalkPedestrian = false;

        // 5 minutes at 60 Hz = 18,000 fixed steps.
        for (var tick = 0; tick < 18_000; tick++)
        {
            summary = sim.Step(TrafficBenchSimulator.FixedDtSeconds, playerPositionEnu: officeExitPlayerPos);
            if (summary.VehiclesStoppedAtSignal > 0)
                sawSignalWait = true;
            if (summary.PedestriansCrossing > 0)
                sawCrosswalkPedestrian = true;

            if (tick % 300 == 0)
            {
                foreach (var snap in sim.GetSnapshots())
                {
                    if (initialSnapshots.TryGetValue(snap.Id, out var p0) && (snap.PositionEnu - p0).Length > 5.0)
                        movedAgents.Add(snap.Id);
                }
            }
        }

        Assert.Equal(50, summary.ActiveVehicles);
        Assert.Equal(30, summary.ActivePedestrians);
        Assert.True(summary.MinOfficeExitClearanceMetres >= 6.0);
        Assert.False(summary.PlayerBlocked);
        Assert.True(sawSignalWait, "Traffic should encounter red/green signal stops during 5-minute run.");
        Assert.True(sawCrosswalkPedestrian, "Pedestrians should use signalized crosswalks.");
        Assert.True(movedAgents.Count >= 45, $"Expected sustained circulation across agents, only {movedAgents.Count} moved.");
        Assert.Equal(0, summary.DeadlockRemovals);
    }

    [Fact]
    public void OffCameraDeadlockIsRemovedOnlyAtOrAfter20Seconds()
    {
        var sim = new TrafficBenchSimulator(seed: 999UL, respawnAfterDeadlockRemoval: false);
        var cameraPos = new Vector3d(500120.0, 7400020.0, 102.0);
        var cameraForward = new Vector3d(1.0, 0.0, 0.0);

        // Put car_00 well outside 80 m camera radius and stall it.
        var stuckTarget = sim.GetSnapshots().First(a => a.IsVehicle && (a.PositionEnu - cameraPos).Length > 80.0);
        sim.ForceStallForTest(stuckTarget.Id, setStalledSeconds: 19.5);

        for (var i = 0; i < 29; i++)
        {
            var pre = sim.Step(TrafficBenchSimulator.FixedDtSeconds, cameraPos, cameraForward);
            Assert.Equal(0, pre.DeadlockRemovals);
        }

        // Crossing 20.0 s off-camera triggers clean removal.
        var after = sim.Step(TrafficBenchSimulator.FixedDtSeconds, cameraPos, cameraForward);
        Assert.Equal(1, after.DeadlockRemovals);
        Assert.DoesNotContain(sim.GetSnapshots(), s => s.Id == stuckTarget.Id);
    }

    [Fact]
    public void OnCameraStalledVehicleIsNotAbruptlyDeleted()
    {
        var sim = new TrafficBenchSimulator(seed: 101UL, respawnAfterDeadlockRemoval: false);
        var onCamTarget = sim.GetSnapshots().First(a => a.IsVehicle);
        var cameraPos = onCamTarget.PositionEnu - new Vector3d(10.0, 0.0, 0.0);
        var cameraForward = new Vector3d(1.0, 0.0, 0.0);

        sim.ForceStallForTest(onCamTarget.Id, setStalledSeconds: 25.0);
        var summary = sim.Step(TrafficBenchSimulator.FixedDtSeconds, cameraPos, cameraForward);

        Assert.Equal(0, summary.DeadlockRemovals);
        Assert.Contains(sim.GetSnapshots(), s => s.Id == onCamTarget.Id);
    }

    [Fact]
    public void TutorialRoadAndSidewalkLanesDoNotIntersectManifestBuildings()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "game", "world", "tutorial-block.json")))
            dir = dir.Parent;
        var manifest = WorldSceneManifest.Parse(File.ReadAllText(Path.Combine(dir!.FullName, "game", "world", "tutorial-block.json")));
        var obstacles = ObstacleBoxEnu.FromWorldObjects(
            manifest.Objects.Where(o => o.Collision && o.ObjectClass is not ("ground" or "unload_point" or "survey_control"))).ToArray();
        var graph = RoadNetworkGraph.CreateTutorialBlockGraph();

        foreach (var seg in graph.Segments.Values)
        {
            var clearance = seg.IsVehicleLane ? 1.1 : 0.35;
            for (var d = 0.0; d <= seg.LengthMetres; d += 0.5)
            {
                var pt = seg.EvaluateEnu(d);
                Assert.DoesNotContain(obstacles, box =>
                    pt.Z + 1.6 > box.MinZ && pt.Z + 0.05 < box.MaxZ &&
                    pt.X + clearance > box.MinX && pt.X - clearance < box.MaxX &&
                    pt.Y + clearance > box.MinY && pt.Y - clearance < box.MaxY);
            }
        }
    }

    [Fact]
    public void FindPedestrianContactStartlesPedestrianAndNotifyPausesVehicle()
    {
        var sim = new TrafficBenchSimulator(seed: 20261114UL);
        sim.Advance(TrafficBenchSimulator.FixedDtSeconds);
        var agents = new List<TrafficVisualAgent>();
        sim.CopyVisualAgentsTo(agents);

        var ped = agents.First(a => !a.IsVehicle);
        var pedHit = sim.FindPedestrianContact(
            ped.PositionEnu + new Vector3d(0.0, -3.0, 0.0),
            ped.PositionEnu + new Vector3d(0.0, 1.0, 0.0),
            playerRadius: 1.15,
            dt: 1.0 / 60.0);
        Assert.NotNull(pedHit);
        Assert.Equal(ped.Id, pedHit.Value.AgentId);

        sim.Advance(TrafficBenchSimulator.FixedDtSeconds);
        sim.CopyVisualAgentsTo(agents);
        Assert.True(agents.First(a => a.Id == ped.Id).IsStartled);

        var car = agents.First(a => a.IsVehicle && a.SpeedMetresPerSecond > 0.5);
        sim.NotifyAgentContact(car.Id, pauseSeconds: 1.0);
        for (var i = 0; i < 30; i++)
            sim.Advance(TrafficBenchSimulator.FixedDtSeconds);
        sim.CopyVisualAgentsTo(agents);
        Assert.True(agents.First(a => a.Id == car.Id).SpeedMetresPerSecond < 0.1);
    }
}
