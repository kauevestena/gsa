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
    private MultiMeshInstance3D? _pedestrianBodyNode;
    private MultiMeshInstance3D? _pedestrianLegLNode;
    private MultiMeshInstance3D? _pedestrianLegRNode;
    private MultiMeshInstance3D? _pedestrianArmLNode;
    private MultiMeshInstance3D? _pedestrianArmRNode;
    private MultiMeshInstance3D? _shadowPool;
    private MultiMesh? _vehicleLamps;
    private double _walkTime;
    private bool _lightsActive;

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

        MultiMeshInstance3D AddPedPartPool(string name, ArrayMesh mesh, bool useColors)
        {
            mesh.SurfaceSetMaterial(0, GsaMeshBuilder.SolidMat(Colors.White));
            var mm = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = useColors,
                Mesh = mesh,
                InstanceCount = TrafficBenchSimulator.TargetPedestrianCount,
            };
            var mmi = new MultiMeshInstance3D { Name = name, Multimesh = mm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            root.AddChild(mmi);
            return mmi;
        }

        _pedestrianBodyNode = AddPedPartPool("TrafficPedestrians_Body", GsaMeshBuilder.BuildTrafficPedestrianBodyMesh(), true);
        _pedestrianLegLNode = AddPedPartPool("TrafficPedestrians_LegL", GsaMeshBuilder.BuildTrafficPedestrianLegMesh(isLeft: true), false);
        _pedestrianLegRNode = AddPedPartPool("TrafficPedestrians_LegR", GsaMeshBuilder.BuildTrafficPedestrianLegMesh(isLeft: false), false);
        _pedestrianArmLNode = AddPedPartPool("TrafficPedestrians_ArmL", GsaMeshBuilder.BuildTrafficPedestrianArmMesh(isLeft: true), true);
        _pedestrianArmRNode = AddPedPartPool("TrafficPedestrians_ArmR", GsaMeshBuilder.BuildTrafficPedestrianArmMesh(isLeft: false), true);

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
            var lamp = new BoxMesh
            {
                Size = new Vector3(.08f, .16f, .24f),
                Material = new StandardMaterial3D
                {
                    VertexColorUseAsAlbedo = true,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                },
            };
            _vehicleLamps = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true,
                Mesh = lamp,
                InstanceCount = 4 * TrafficBenchSimulator.TargetVehicleCount,
                VisibleInstanceCount = 0,
            };
            root.AddChild(new MultiMeshInstance3D
            {
                Name = "TrafficVehicleLamps",
                Multimesh = _vehicleLamps,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                VisibilityRangeEnd = 160f,
            });
        }

        SyncVisuals();
    }

    public void StepAndSync(double dtSeconds, Vector3d? playerEnu = null,
        Vector3d? cameraPosition = null, Vector3d? cameraForward = null, CityTrafficConditions? city = null)
    {
        _walkTime += dtSeconds;
        _lightsActive = city is { } c && (c.IsNight || c.CruiseSpeedMultiplier < 0.99);
        _simulator.Advance(dtSeconds, cameraPosition ?? (city.HasValue ? playerEnu : null), cameraForward, city);
        SyncVisuals();
    }

    public void SyncVisuals()
    {
        if (_vehiclePools[0] is null || _pedestrianBodyNode is null || _pedestrianLegLNode is null ||
            _pedestrianLegRNode is null || _pedestrianArmLNode is null || _pedestrianArmRNode is null)
            return;

        _simulator.CopyVisualAgentsTo(_visualBuffer);
        Span<int> vehicleIndices = stackalloc int[4];
        if (_agentVisualCache.Count > 2 * _visualBuffer.Count)
            _agentVisualCache.Clear();
        var warnedCars = 0;
        var pedIndex = 0;
        var shadowIndex = 0;
        var flashOn = _walkTime % .8 < .45;
        var pedShadowBasis = new Basis(new Vector3(0.90f, 0f, 0f), new Vector3(0f, 0f, -0.90f), Vector3.Up);
        var walkPhase = (float)_walkTime;
        var lampWarnColor = new Color(1.0f, 0.85f, 0.25f);
        var lampHeadColor = new Color(1.0f, 0.96f, 0.80f);
        var lampTailColor = new Color(0.98f, 0.16f, 0.14f);

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

                if (_vehicleLamps is { } lamps)
                {
                    if (agent.IsErratic && flashOn && warnedCars + 2 <= lamps.InstanceCount)
                    {
                        lamps.SetInstanceTransform(warnedCars, new Transform3D(basis, godotPos + basis * new Vector3(2.24f, .57f, .65f)));
                        lamps.SetInstanceColor(warnedCars++, lampWarnColor);
                        lamps.SetInstanceTransform(warnedCars, new Transform3D(basis, godotPos + basis * new Vector3(2.24f, .57f, -.65f)));
                        lamps.SetInstanceColor(warnedCars++, lampWarnColor);
                    }
                    else if (_lightsActive && warnedCars + 4 <= lamps.InstanceCount)
                    {
                        lamps.SetInstanceTransform(warnedCars, new Transform3D(basis, godotPos + basis * new Vector3(2.16f, .54f, .64f)));
                        lamps.SetInstanceColor(warnedCars++, lampHeadColor);
                        lamps.SetInstanceTransform(warnedCars, new Transform3D(basis, godotPos + basis * new Vector3(2.16f, .54f, -.64f)));
                        lamps.SetInstanceColor(warnedCars++, lampHeadColor);
                        lamps.SetInstanceTransform(warnedCars, new Transform3D(basis, godotPos + basis * new Vector3(-2.16f, .56f, .64f)));
                        lamps.SetInstanceColor(warnedCars++, lampTailColor);
                        lamps.SetInstanceTransform(warnedCars, new Transform3D(basis, godotPos + basis * new Vector3(-2.16f, .56f, -.64f)));
                        lamps.SetInstanceColor(warnedCars++, lampTailColor);
                    }
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
            else if (pedIndex < _pedestrianBodyNode.Multimesh.InstanceCount)
            {
                var speedF = (float)agent.SpeedMetresPerSecond;
                var phase = walkPhase * (agent.IsStartled ? 12.5f : MathF.Max(4.2f, speedF * 5.2f)) + (vis.Hash % 63) * 0.1f;
                var moving = speedF > 0.08f;
                var legSwing = moving ? MathF.Sin(phase) * (agent.IsStartled ? 0.68f : 0.54f) : 0f;
                var strideDip = moving ? -MathF.Abs(MathF.Sin(phase)) * 0.035f : 0f;
                var startleHop = agent.IsStartled ? MathF.Abs(MathF.Sin(phase * 1.4f)) * 0.12f : 0f;
                var groundY = godotPos.Y + 0.045f;
                var rootY = groundY + strideDip + startleHop;

                var bodyBasis = agent.IsStartled
                    ? basis.Rotated(Vector3.Up, MathF.Sin(phase * 1.2f) * 0.25f)
                    : basis;
                var legLBasis = bodyBasis.Rotated(bodyBasis.Z, legSwing);
                var legRBasis = bodyBasis.Rotated(bodyBasis.Z, -legSwing);
                var armLBasis = agent.IsStartled
                    ? bodyBasis.Rotated(bodyBasis.X, 1.35f + MathF.Sin(phase * 2.0f) * 0.4f)
                    : bodyBasis.Rotated(bodyBasis.Z, -legSwing * 0.72f);
                var armRBasis = agent.IsStartled
                    ? bodyBasis.Rotated(bodyBasis.X, -(1.35f + MathF.Cos(phase * 2.0f) * 0.4f))
                    : bodyBasis.Rotated(bodyBasis.Z, legSwing * 0.72f);

                var idx = pedIndex++;
                var bodyPos = new Vector3(godotPos.X, rootY, godotPos.Z);
                var hipPos = new Vector3(godotPos.X, rootY + 0.76f, godotPos.Z);
                var shoulderPos = new Vector3(godotPos.X, rootY + 1.24f, godotPos.Z);

                _pedestrianBodyNode.Multimesh.SetInstanceTransform(idx, new Transform3D(bodyBasis, bodyPos));
                _pedestrianBodyNode.Multimesh.SetInstanceColor(idx, vis.Tint);
                _pedestrianLegLNode.Multimesh.SetInstanceTransform(idx, new Transform3D(legLBasis, hipPos));
                _pedestrianLegRNode.Multimesh.SetInstanceTransform(idx, new Transform3D(legRBasis, hipPos));
                _pedestrianArmLNode.Multimesh.SetInstanceTransform(idx, new Transform3D(armLBasis, shoulderPos));
                _pedestrianArmLNode.Multimesh.SetInstanceColor(idx, vis.Tint);
                _pedestrianArmRNode.Multimesh.SetInstanceTransform(idx, new Transform3D(armRBasis, shoulderPos));
                _pedestrianArmRNode.Multimesh.SetInstanceColor(idx, vis.Tint);

                if (_shadowPool is not null && shadowIndex < _shadowPool.Multimesh.InstanceCount)
                {
                    _shadowPool.Multimesh.SetInstanceTransform(shadowIndex++,
                        new Transform3D(pedShadowBasis, new Vector3(godotPos.X, godotPos.Y + 0.09f, godotPos.Z)));
                }
            }
        }

        if (_vehicleLamps is not null)
            _vehicleLamps.VisibleInstanceCount = warnedCars;
        // Hide unused slots if the model mix changes.
        for (var v = 0; v < 4; v++)
            _vehiclePools[v].Multimesh.VisibleInstanceCount = vehicleIndices[v];
        _pedestrianBodyNode.Multimesh.VisibleInstanceCount = pedIndex;
        _pedestrianLegLNode.Multimesh.VisibleInstanceCount = pedIndex;
        _pedestrianLegRNode.Multimesh.VisibleInstanceCount = pedIndex;
        _pedestrianArmLNode.Multimesh.VisibleInstanceCount = pedIndex;
        _pedestrianArmRNode.Multimesh.VisibleInstanceCount = pedIndex;
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
