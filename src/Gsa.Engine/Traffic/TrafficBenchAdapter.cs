using Godot;
using Gsa.Core.Geometry;
using Gsa.Core.Traffic;

namespace Gsa.Engine.Traffic;

/// <summary>MultiMesh traffic bench adapter keeping 50 cars + 30 pedestrians outside semantic _worldRoot.</summary>
public sealed class TrafficBenchAdapter
{
    private readonly GodotCoordinateAdapter _coordinates;
    private readonly TrafficBenchSimulator _simulator;
    private MultiMeshInstance3D? _vehicleMeshNode;
    private MultiMeshInstance3D? _pedestrianMeshNode;

    public TrafficBenchSimulator Simulator => _simulator;

    public TrafficBenchAdapter(GodotCoordinateAdapter coordinates, TrafficBenchSimulator? simulator = null)
    {
        ArgumentNullException.ThrowIfNull(coordinates);
        _coordinates = coordinates;
        _simulator = simulator ?? new TrafficBenchSimulator();
    }

    public void AttachBenchMeshes(Node3D benchRoot)
    {
        ArgumentNullException.ThrowIfNull(benchRoot);
        var root = new Node3D { Name = "TrafficBench" };
        benchRoot.AddChild(root);

        _vehicleMeshNode = CreateAgentMultiMesh("TrafficVehicles", TrafficBenchSimulator.TargetVehicleCount,
            new BoxMesh { Size = new Vector3(4.2f, 1.45f, 1.9f) },
            new Color(0.86f, 0.34f, 0.18f));
        root.AddChild(_vehicleMeshNode);

        _pedestrianMeshNode = CreateAgentMultiMesh("TrafficPedestrians", TrafficBenchSimulator.TargetPedestrianCount,
            new BoxMesh { Size = new Vector3(0.45f, 1.70f, 0.45f) },
            new Color(0.24f, 0.62f, 0.85f));
        root.AddChild(_pedestrianMeshNode);

        SyncVisuals();
    }

    public void StepAndSync(double dtSeconds, Vector3d? playerEnu = null)
    {
        _simulator.Step(dtSeconds, playerPositionEnu: playerEnu);
        SyncVisuals();
    }

    public void SyncVisuals()
    {
        if (_vehicleMeshNode is null || _pedestrianMeshNode is null)
            return;

        var snapshots = _simulator.GetSnapshots();
        var vehicleIndex = 0;
        var pedestrianIndex = 0;

        foreach (var agent in snapshots)
        {
            var godotPos = _coordinates.ToGodot(agent.PositionEnu);
            var heightOffset = agent.IsVehicle ? 0.725f : 0.85f;
            var basis = Basis.FromEuler(new Vector3(0.0f, (float)agent.HeadingRadians, 0.0f));
            var transform = new Transform3D(basis, new Vector3(godotPos.X, godotPos.Y + heightOffset, godotPos.Z));

            if (agent.IsVehicle && vehicleIndex < _vehicleMeshNode.Multimesh.InstanceCount)
            {
                _vehicleMeshNode.Multimesh.SetInstanceTransform(vehicleIndex, transform);
                vehicleIndex++;
            }
            else if (!agent.IsVehicle && pedestrianIndex < _pedestrianMeshNode.Multimesh.InstanceCount)
            {
                _pedestrianMeshNode.Multimesh.SetInstanceTransform(pedestrianIndex, transform);
                pedestrianIndex++;
            }
        }
    }

    public void RunVerificationSuite()
    {
        var benchSim = new TrafficBenchSimulator();
        TrafficStepSummary lastSummary = null!;
        for (var tick = 0; tick < 180; tick++)
            lastSummary = benchSim.Step(TrafficBenchSimulator.FixedDtSeconds);

        if (lastSummary.ActiveVehicles != TrafficBenchSimulator.TargetVehicleCount)
            throw new InvalidOperationException($"Expected 50 vehicles, found {lastSummary.ActiveVehicles}.");
        if (lastSummary.ActivePedestrians != TrafficBenchSimulator.TargetPedestrianCount)
            throw new InvalidOperationException($"Expected 30 pedestrians, found {lastSummary.ActivePedestrians}.");
        if (lastSummary.MinOfficeExitClearanceMetres < 6.0)
            throw new InvalidOperationException($"Office entry clearance violated: {lastSummary.MinOfficeExitClearanceMetres:F2} m.");

        // Verify 20 s off-camera deadlock eviction counter increments and keeps active vehicle target.
        var deadlockSim = new TrafficBenchSimulator();
        deadlockSim.ForceStallForTest("car_00", setStalledSeconds: 19.95);
        deadlockSim.Step(TrafficBenchSimulator.FixedDtSeconds);
        deadlockSim.Step(TrafficBenchSimulator.FixedDtSeconds);
        var finalSummary = deadlockSim.Step(TrafficBenchSimulator.FixedDtSeconds);
        if (finalSummary.DeadlockRemovals < 1 || finalSummary.ActiveVehicles != TrafficBenchSimulator.TargetVehicleCount)
            throw new InvalidOperationException("Off-camera deadlock removal verification failed.");

        GD.Print($"GSA_TRAFFIC_SMOKE_OK vehicles={lastSummary.ActiveVehicles} pedestrians={lastSummary.ActivePedestrians} " +
                 $"ticks={lastSummary.Tick} deadlock_removals={finalSummary.DeadlockRemovals} " +
                 $"office_clearance_m={lastSummary.MinOfficeExitClearanceMetres.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}");
    }

    private static MultiMeshInstance3D CreateAgentMultiMesh(string name, int count, BoxMesh mesh, Color albedo)
    {
        mesh.Material = new StandardMaterial3D { AlbedoColor = albedo, Roughness = 0.85f };
        var multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = mesh,
            InstanceCount = count,
        };
        return new MultiMeshInstance3D { Name = name, Multimesh = multiMesh };
    }
}

