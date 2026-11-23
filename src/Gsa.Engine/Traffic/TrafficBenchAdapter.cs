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
    private readonly System.Collections.Generic.List<TrafficVisualAgent> _visualBuffer = new(TrafficBenchSimulator.TargetVehicleCount + TrafficBenchSimulator.TargetPedestrianCount);
    private readonly System.Collections.Generic.Dictionary<string, (int Pool, int Hash, Color Tint)> _agentVisualCache = new(StringComparer.Ordinal);
    private MultiMeshInstance3D? _pedestrianMeshNode;
    private MultiMeshInstance3D? _shadowPool;
    private MultiMesh? _warningLamps;
    private double _walkTime;

    public TrafficBenchSimulator Simulator => _simulator;
    public IReadOnlyList<TrafficVisualAgent> VisualAgents => _visualBuffer;

    public TrafficBenchAdapter(GodotCoordinateAdapter coordinates, TrafficBenchSimulator? simulator = null)
    {
        ArgumentNullException.ThrowIfNull(coordinates);
        _coordinates = coordinates;
        _simulator = simulator ?? new TrafficBenchSimulator();
    }

    public void AttachBenchMeshes(Node3D benchRoot, bool warnForErraticDrivers = false)
    {
        ArgumentNullException.ThrowIfNull(benchRoot);
        var root = new Node3D { Name = "TrafficBench" };
        benchRoot.AddChild(root);

        // A respawn can shift the mix. Every simulated car must have a visible slot.
        // Unused slots are excluded via VisibleInstanceCount.
        for (var v = 0; v < 4; v++)
        {
            var mesh = GsaMeshBuilder.BuildTrafficCarMesh(v);
            mesh.SurfaceSetMaterial(0, GsaMeshBuilder.SolidMat(Colors.White));
            var mm = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true,
                Mesh = mesh,
                InstanceCount = TrafficBenchSimulator.TargetVehicleCount,
            };
            var mmi = new MultiMeshInstance3D { Name = $"TrafficVehicles_V{v}", Multimesh = mm };
            root.AddChild(mmi);
            _vehiclePools[v] = mmi;
        }

        var pedMesh = GsaMeshBuilder.BuildTrafficPedestrianMesh();
        pedMesh.SurfaceSetMaterial(0, GsaMeshBuilder.SolidMat(Colors.White));
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

        if (warnForErraticDrivers)
        {
            var lamp = new BoxMesh { Size = new Vector3(.06f, .18f, .25f),
                Material = GsaMeshBuilder.SolidMat(new Color(1f, .92f, .67f), emit: true, emissionEnergy: 2f) };
            _warningLamps = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = lamp,
                InstanceCount = 2 * TrafficBenchSimulator.TargetVehicleCount,
                VisibleInstanceCount = 0,
            };
            root.AddChild(new MultiMeshInstance3D { Name = "ErraticHeadlamps", Multimesh = _warningLamps,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }

        SyncVisuals();
    }

    public void StepAndSync(double dtSeconds, Vector3d? playerEnu = null,
        Vector3d? cameraPosition = null, Vector3d? cameraForward = null, CityTrafficConditions? city = null)
    {
        _walkTime += dtSeconds;
        _simulator.Advance(dtSeconds, cameraPosition ?? (city.HasValue ? playerEnu : null), cameraForward, city);
        SyncVisuals();
    }

    public void SyncVisuals()
    {
        if (_vehiclePools[0] is null || _pedestrianMeshNode is null)
            return;

        _simulator.CopyVisualAgentsTo(_visualBuffer);
        Span<int> vehicleIndices = stackalloc int[4];
        if (_agentVisualCache.Count > 2 * _visualBuffer.Count)
            _agentVisualCache.Clear();
        var warnedCars = 0;
        var flashOn = _walkTime % .8 < .45;
        var pedIndex = 0;
        var shadowIndex = 0;
        var pedShadowBasis = new Basis(new Vector3(0.85f, 0f, 0f), new Vector3(0f, 0f, -0.85f), Vector3.Up);
        var walkPhase = (float)_walkTime * 7.5f;

        foreach (var agent in _visualBuffer)
        {
            var godotPos = _coordinates.ToGodot(agent.PositionEnu);
            var (sinH, cosH) = MathF.SinCos((float)agent.HeadingRadians);
            var basis = new Basis(new Vector3(cosH, 0f, -sinH), Vector3.Up, new Vector3(sinH, 0f, cosH));

            if (!_agentVisualCache.TryGetValue(agent.Id, out var vis))
            {
                var hash = StableHash(agent.Id);
                var pool = hash % 10 < 2 ? 1 : (hash % 10 < 4 ? 2 : (hash % 10 == 4 ? 3 : 0));
                var tint = agent.IsVehicle
                    ? (pool == 1
                        ? new Color(0.95f, 0.74f, 0.14f)
                        : pool == 2
                            ? new Color(0.86f, 0.88f, 0.90f)
                            : SedanPalette[hash % SedanPalette.Length])
                    : PedJacketPalette[hash % PedJacketPalette.Length];
                vis = (pool, hash, tint);
                _agentVisualCache[agent.Id] = vis;
            }

            if (agent.IsVehicle)
            {
                var pool = vis.Pool;
                var mm = _vehiclePools[pool].Multimesh;
                var idx = vehicleIndices[pool]++;
                mm.SetInstanceTransform(idx, new Transform3D(basis, godotPos));
                mm.SetInstanceColor(idx, vis.Tint);

                if (agent.IsErratic && flashOn && _warningLamps is { } lamps)
                {
                    lamps.SetInstanceTransform(warnedCars++, new Transform3D(basis, godotPos + basis * new Vector3(2.24f, .57f, .65f)));
                    lamps.SetInstanceTransform(warnedCars++, new Transform3D(basis, godotPos + basis * new Vector3(2.24f, .57f, -.65f)));
                }

                if (_shadowPool is not null && shadowIndex < _shadowPool.Multimesh.InstanceCount)
                {
                    var shBasis = new Basis(
                        new Vector3(cosH * 4.5f, 0f, -sinH * 4.5f),
                        new Vector3(-sinH * 2.2f, 0f, -cosH * 2.2f),
                        Vector3.Up);
                    _shadowPool.Multimesh.SetInstanceTransform(shadowIndex++,
                        new Transform3D(shBasis, new Vector3(godotPos.X, godotPos.Y + 0.03f, godotPos.Z)));
                }
            }
            else if (pedIndex < _pedestrianMeshNode.Multimesh.InstanceCount)
            {
                var bob = agent.IsStartled
                    ? MathF.Abs(MathF.Sin(walkPhase * 2.4f + vis.Hash)) * 0.28f
                    : (agent.SpeedMetresPerSecond > 0.1
                        ? MathF.Abs(MathF.Sin(walkPhase + vis.Hash)) * 0.055f
                        : 0f);
                var pedBasis = agent.IsStartled
                    ? basis.Rotated(Vector3.Up, MathF.Sin(walkPhase * 3.0f + vis.Hash) * 0.35f)
                    : basis;
                var idx = pedIndex++;
                _pedestrianMeshNode.Multimesh.SetInstanceTransform(
                    idx,
                    new Transform3D(pedBasis, new Vector3(godotPos.X, godotPos.Y + bob, godotPos.Z)));
                _pedestrianMeshNode.Multimesh.SetInstanceColor(idx, vis.Tint);

                if (_shadowPool is not null && shadowIndex < _shadowPool.Multimesh.InstanceCount)
                {
                    _shadowPool.Multimesh.SetInstanceTransform(shadowIndex++,
                        new Transform3D(pedShadowBasis, new Vector3(godotPos.X, godotPos.Y + 0.03f, godotPos.Z)));
                }
            }
        }

        if (_warningLamps is not null)
            _warningLamps.VisibleInstanceCount = warnedCars;
        // Hide unused slots if the model mix changes.
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
            return h & int.MaxValue;
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
