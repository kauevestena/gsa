using Godot;
using Gsa.Core.Geometry;
using Gsa.Core.Traffic;
using Gsa.Engine.Visuals;

namespace Gsa.Engine.Traffic;

/// <summary>MultiMesh traffic adapter rendering varied GTA III vehicles, stylized pedestrians, and ground blob shadows.</summary>
public sealed class TrafficBenchAdapter
{
    private static readonly Color[] SedanPalette =
    [
        new(0.22f, 0.36f, 0.56f),
        new(0.58f, 0.16f, 0.18f),
        new(0.22f, 0.42f, 0.28f),
        new(0.72f, 0.74f, 0.76f),
        new(0.20f, 0.22f, 0.26f),
        new(0.76f, 0.42f, 0.16f),
        new(0.78f, 0.72f, 0.58f),
    ];

    private static readonly Color[] PedJacketPalette =
    [
        new(0.24f, 0.48f, 0.72f),
        new(0.76f, 0.28f, 0.24f),
        new(0.28f, 0.58f, 0.36f),
        new(0.84f, 0.66f, 0.22f),
        new(0.46f, 0.36f, 0.62f),
        new(0.32f, 0.34f, 0.38f),
    ];

    private readonly GodotCoordinateAdapter _coordinates;
    private readonly TrafficBenchSimulator _simulator;
    private readonly MultiMeshInstance3D[] _vehiclePools = new MultiMeshInstance3D[4];
    private MultiMeshInstance3D? _pedestrianMeshNode;
    private MultiMeshInstance3D? _shadowPool;
    private double _walkTime;

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

        // Split 50 vehicles across 4 distinct GTA III low-poly silhouettes
        int[] counts = [22, 10, 10, 8]; // Sedan, Taxi, Van, Pickup
        for (var v = 0; v < 4; v++)
        {
            var mesh = GsaMeshBuilder.BuildTrafficCarMesh(v);
            mesh.SurfaceSetMaterial(0, new StandardMaterial3D
            {
                VertexColorUseAsAlbedo = true,
                Roughness = 0.52f,
            });
            var mm = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true,
                Mesh = mesh,
                InstanceCount = counts[v],
            };
            var mmi = new MultiMeshInstance3D { Name = $"TrafficVehicles_V{v}", Multimesh = mm };
            root.AddChild(mmi);
            _vehiclePools[v] = mmi;
        }

        var pedMesh = GsaMeshBuilder.BuildTrafficPedestrianMesh();
        pedMesh.SurfaceSetMaterial(0, new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            Roughness = 0.85f,
        });
        var pedMm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = pedMesh,
            InstanceCount = TrafficBenchSimulator.TargetPedestrianCount,
        };
        _pedestrianMeshNode = new MultiMeshInstance3D { Name = "TrafficPedestrians", Multimesh = pedMm };
        root.AddChild(_pedestrianMeshNode);

        // Blob ground shadows for all 80 traffic agents (GTA III signature!)
        var shadowQuad = new QuadMesh
        {
            Size = Vector2.One,
            Material = GsaRetroTextures.CreateWorldMaterial(
                "traffic_shadow_blob", "shadow_blob", Colors.White, unshaded: true, transparent: true),
        };
        var shadowMm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = shadowQuad,
            InstanceCount = TrafficBenchSimulator.TargetVehicleCount + TrafficBenchSimulator.TargetPedestrianCount,
        };
        _shadowPool = new MultiMeshInstance3D
        {
            Name = "TrafficBlobShadows",
            Multimesh = shadowMm,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        root.AddChild(_shadowPool);

        SyncVisuals();
    }

    public void StepAndSync(double dtSeconds, Vector3d? playerEnu = null)
    {
        _walkTime += dtSeconds;
        _simulator.Step(dtSeconds, playerPositionEnu: playerEnu);
        SyncVisuals();
    }

    public void SyncVisuals()
    {
        if (_vehiclePools[0] is null || _pedestrianMeshNode is null)
            return;

        var snapshots = _simulator.GetSnapshots();
        Span<int> vehicleIndices = stackalloc int[4];
        var pedIndex = 0;
        var shadowIndex = 0;

        foreach (var agent in snapshots)
        {
            var godotPos = _coordinates.ToGodot(agent.PositionEnu);
            var basis = Basis.FromEuler(new Vector3(0.0f, (float)agent.HeadingRadians, 0.0f));
            var hash = StableHash(agent.Id);

            if (agent.IsVehicle)
            {
                var pool = hash % 10 < 2 ? 1 : (hash % 10 < 4 ? 2 : (hash % 10 == 4 ? 3 : 0));
                var mm = _vehiclePools[pool].Multimesh;
                if (vehicleIndices[pool] >= mm.InstanceCount)
                {
                    pool = 0;
                    mm = _vehiclePools[0].Multimesh;
                }
                if (vehicleIndices[pool] < mm.InstanceCount)
                {
                    var idx = vehicleIndices[pool]++;
                    mm.SetInstanceTransform(idx, new Transform3D(basis, godotPos));
                    var color = pool == 1
                        ? new Color(0.95f, 0.74f, 0.14f) // Liberty City Yellow Taxi
                        : pool == 2
                            ? new Color(0.86f, 0.88f, 0.90f) // Delivery Van
                            : SedanPalette[hash % SedanPalette.Length];
                    mm.SetInstanceColor(idx, color);

                    if (_shadowPool is not null && shadowIndex < _shadowPool.Multimesh.InstanceCount)
                    {
                        var shBasis = Basis.FromEuler(new Vector3(-MathF.PI * 0.5f, (float)agent.HeadingRadians, 0f))
                            .Scaled(new Vector3(4.5f, 1f, 2.2f));
                        _shadowPool.Multimesh.SetInstanceTransform(shadowIndex++,
                            new Transform3D(shBasis, new Vector3(godotPos.X, godotPos.Y + 0.03f, godotPos.Z)));
                    }
                }
            }
            else if (pedIndex < _pedestrianMeshNode.Multimesh.InstanceCount)
            {
                var bob = agent.SpeedMetresPerSecond > 0.1
                    ? MathF.Abs(MathF.Sin((float)_walkTime * 7.5f + hash)) * 0.055f
                    : 0f;
                var idx = pedIndex++;
                _pedestrianMeshNode.Multimesh.SetInstanceTransform(
                    idx,
                    new Transform3D(basis, new Vector3(godotPos.X, godotPos.Y + bob, godotPos.Z)));
                _pedestrianMeshNode.Multimesh.SetInstanceColor(idx, PedJacketPalette[hash % PedJacketPalette.Length]);

                if (_shadowPool is not null && shadowIndex < _shadowPool.Multimesh.InstanceCount)
                {
                    var shBasis = Basis.FromEuler(new Vector3(-MathF.PI * 0.5f, 0f, 0f))
                        .Scaled(new Vector3(0.85f, 1f, 0.85f));
                    _shadowPool.Multimesh.SetInstanceTransform(shadowIndex++,
                        new Transform3D(shBasis, new Vector3(godotPos.X, godotPos.Y + 0.03f, godotPos.Z)));
                }
            }
        }

        // Hide unused pool slots cleanly below ground if distribution shifts
        for (var v = 0; v < 4; v++)
            _vehiclePools[v].Multimesh.VisibleInstanceCount = vehicleIndices[v];
        _pedestrianMeshNode.Multimesh.VisibleInstanceCount = pedIndex;
        if (_shadowPool is not null)
            _shadowPool.Multimesh.VisibleInstanceCount = shadowIndex;
    }

    private static int StableHash(string id)
    {
        unchecked
        {
            var h = 17;
            foreach (var c in id)
                h = h * 31 + c;
            return Math.Abs(h);
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

        GD.Print($"GSA_TRAFFIC_SMOKE_OK vehicles={lastSummary.ActiveVehicles} pedestrians={lastSummary.ActivePedestrianCountCompat()} " +
                 $"ticks={lastSummary.Tick} deadlock_removals={finalSummary.DeadlockRemovals} " +
                 $"office_clearance_m={lastSummary.MinOfficeExitClearanceMetres.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}");
    }
}

internal static class TrafficSummaryCompat
{
    public static int ActivePedestrianCountCompat(this TrafficStepSummary s) => s.ActivePedestrians;
}
