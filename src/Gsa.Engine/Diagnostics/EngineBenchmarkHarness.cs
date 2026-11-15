using System.Diagnostics;
using Godot;
using Gsa.Core.Diagnostics;
using Gsa.Core.Geometry;
using Gsa.Core.Sensors.Camera;
using Gsa.Core.Sensors.Lidar;
using Gsa.Core.Traffic;
using Gsa.Core.World;
using Gsa.Engine.World;

namespace Gsa.Engine.Diagnostics;

/// <summary>Measures live Godot scene geometry, memory, raycast/traffic step timings, and hardware capabilities under _benchRoot.</summary>
public static class EngineBenchmarkHarness
{
    public static void RunVerificationSuite(
        Node3D sceneRoot,
        Node3D benchRoot,
        WorldSceneManifest manifest,
        SemanticRegistry registry,
        PhysicsDirectSpaceState3D spaceState)
    {
        ArgumentNullException.ThrowIfNull(sceneRoot);
        ArgumentNullException.ThrowIfNull(benchRoot);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(spaceState);

        var probeNode = new Node3D { Name = "BenchmarkProbe" };
        benchRoot.AddChild(probeNode);
        try
        {
            var assemblySw = Stopwatch.StartNew();
            var (meshCount, liveTriangles, drawCallNodes) = CountLiveGeometry(sceneRoot);
            assemblySw.Stop();

            if (liveTriangles <= 0 || meshCount <= 0)
                throw new InvalidOperationException($"Benchmark probe expected non-empty live geometry (meshes={meshCount}, tris={liveTriangles}).");

            var coordinates = new GodotCoordinateAdapter(new LocalMapFrame(manifest.OriginEnu));
            var startStaticMemMb = OS.GetStaticMemoryUsage() / (1024.0 * 1024.0);
            var startRssMb = ReadProcessRssMb();

            var dryMeanMs = MeasureIntegratedBenchBurst(
                BenchmarkPresetConfig.ForPreset(BenchmarkPreset.DryDay),
                coordinates,
                manifest,
                registry,
                spaceState);

            var rainMeanMs = MeasureIntegratedBenchBurst(
                BenchmarkPresetConfig.ForPreset(BenchmarkPreset.RainNight),
                coordinates,
                manifest,
                registry,
                spaceState);

            var endStaticMemMb = OS.GetStaticMemoryUsage() / (1024.0 * 1024.0);
            var endRssMb = ReadProcessRssMb();
            var memDeltaMb = endRssMb - startRssMb;

            var adapterName = RenderingServer.GetVideoAdapterName();
            var adapterVendor = RenderingServer.GetVideoAdapterVendor();
            var displayName = DisplayServer.GetName();
            var isHeadless = string.Equals(displayName, "headless", StringComparison.OrdinalIgnoreCase);

            var hardware = BenchmarkHardwareProbe.DetectHost(
                rendererBackend: "Compatibility",
                vsyncEnabled: false,
                targetResolution: "1920x1080",
                gpuModelOverride: string.IsNullOrWhiteSpace(adapterName) ? null : $"{adapterVendor} {adapterName}".Trim(),
                gpuDriverOverride: isHeadless ? "headless_no_driver" : "godot_rendering_server",
                cpuModelOverride: OS.GetProcessorName(),
                headless: isHeadless);

            var gateStatus = hardware.ReferenceHardwareMatched ? "passed_probe" : "pending_reference_hardware";
            var renderDrawCalls = (int)Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);

            GD.Print(
                $"GSA_BENCHMARK_SMOKE_OK meshes={meshCount} draw_nodes={drawCallNodes} rs_draw_calls={renderDrawCalls} " +
                $"tris={liveTriangles} vehicles={TrafficBenchSimulator.TargetVehicleCount} peds={TrafficBenchSimulator.TargetPedestrianCount} " +
                $"assembly_ms={assemblySw.Elapsed.TotalMilliseconds:F3} dry_step_ms={dryMeanMs:F4} rain_step_ms={rainMeanMs:F4} " +
                $"rss_mem_mb={endRssMb:F2} static_mem_mb={endStaticMemMb:F2} rss_delta_mb={memDeltaMb:F3} display={displayName} " +
                $"ref_matched={hardware.ReferenceHardwareMatched.ToString().ToLowerInvariant()} gate={gateStatus}");
        }
        finally
        {
            probeNode.QueueFree();
        }
    }

    private static double MeasureIntegratedBenchBurst(
        BenchmarkPresetConfig preset,
        GodotCoordinateAdapter coordinates,
        WorldSceneManifest manifest,
        SemanticRegistry registry,
        PhysicsDirectSpaceState3D spaceState)
    {
        var traffic = new TrafficBenchSimulator(seed: preset.IsRaining ? 20261202UL : 20261201UL);
        var cameraScheduler = new CameraCaptureScheduler("cam_engine_probe", CameraSensorProfile.Initial);
        var lidar = new LidarBenchSimulator(LidarProfile.L4, seed: preset.IsRaining ? 302UL : 301UL);
        var lidarMount = LidarMountEstimate.Zero;
        var candidates = manifest.Objects.Select(CameraVisibilityCandidate.FromWorldObject).ToArray();
        var rigEnu = new Vector3d(500020.0, 7400001.0, 101.0);
        var yawNorth = Quaterniond.FromAxisAngle(Vector3d.UnitZ, Math.PI / 2.0);
        var bodyPose = new EstimatedBodyPose(0.0, rigEnu, yawNorth);
        var camTransform = CameraGeometry.CameraToMap(bodyPose, CameraMountEstimate.ForwardFacing);
        var lidarToMap = LidarBenchSimulator.ComposeLidarToMap(new RigidTransform(yawNorth, rigEnu), lidarMount);
        var trajectory = new EstimatedBodyTrajectory([
            bodyPose,
            new EstimatedBodyPose(2.0, rigEnu, yawNorth),
        ]);

        Func<Vector3d, Vector3d, double, LidarRayHit?> godotRayCaster = (startEnu, dirEnu, maxRange) =>
        {
            var fromG = coordinates.ToGodot(startEnu);
            var toG = coordinates.ToGodot(startEnu + dirEnu * maxRange);
            using var query = PhysicsRayQueryParameters3D.Create(fromG, toG, 1);
            var hit = spaceState.IntersectRay(query);
            if (hit.Count == 0)
                return null;

            var hitPosG = hit["position"].AsVector3();
            var hitNormG = hit["normal"].AsVector3();
            var hitEnu = coordinates.ToMap(hitPosG);
            var dist = (hitEnu - startEnu).Length;
            var collider = hit["collider"].AsGodotObject();
            var meta = registry.GetByInstanceId(collider.GetInstanceId());
            var id = meta.TryGetValue("id", out var idVar) ? idVar.AsString() : "unknown";
            var mat = meta.TryGetValue("material", out var matVar) ? matVar.AsString() : "concrete";
            var normalEnu = new Vector3d(hitNormG.X, -hitNormG.Z, hitNormG.Y);
            return new LidarRayHit(dist, normalEnu, mat, id);
        };

        var samples = new List<double>(60);
        const double dt = TrafficBenchSimulator.FixedDtSeconds;
        for (var tick = 0; tick < 60; tick++)
        {
            var start = Stopwatch.GetTimestamp();
            var t = tick * dt;
            var summary = traffic.Step(dt, rigEnu, Vector3d.UnitY, rigEnu);
            if (summary.ActiveVehicles != TrafficBenchSimulator.TargetVehicleCount ||
                summary.ActivePedestrians != TrafficBenchSimulator.TargetPedestrianCount)
            {
                throw new InvalidOperationException("Engine benchmark traffic counts drifted from target 50/30.");
            }

            if (cameraScheduler.IsCaptureTick(tick))
            {
                var req = new CameraCaptureRequest(
                    tick,
                    t,
                    camTransform,
                    camTransform,
                    camTransform,
                    bodyPose,
                    CameraMountEstimate.ForwardFacing,
                    preset.CameraEnvironment,
                    candidates,
                    (_, _, _) => false);
                cameraScheduler.TryEnqueueSimulatedCapture(req, null, out _, out _, autoDrain: true);
            }

            if (tick % 6 == 0)
            {
                lidar.TrySimulateScan(
                    t,
                    lidarToMap,
                    trajectory,
                    lidarMount,
                    preset.IsRaining,
                    godotRayCaster,
                    out _,
                    out _,
                    autoDrain: true,
                    zeroNoise: false);
            }

            samples.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }

        return BenchmarkTimerSummary.Compute(samples).MeanMs;
    }

    private static (int MeshCount, int Triangles, int DrawCallNodes) CountLiveGeometry(Node root)
    {
        var meshes = 0;
        var triangles = 0;
        var drawCallNodes = 0;
        var stack = new Stack<Node>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            for (var i = 0; i < node.GetChildCount(); i++)
                stack.Push(node.GetChild(i));

            if (node is MeshInstance3D meshInstance && meshInstance.Mesh is not null)
            {
                meshes++;
                drawCallNodes++;
                triangles += meshInstance.Mesh.GetFaces().Length / 3;
            }
            else if (node is MultiMeshInstance3D multi && multi.Multimesh?.Mesh is not null)
            {
                var instances = multi.Multimesh.VisibleInstanceCount >= 0
                    ? multi.Multimesh.VisibleInstanceCount
                    : multi.Multimesh.InstanceCount;
                meshes += Math.Max(0, instances);
                drawCallNodes++;
                triangles += (multi.Multimesh.Mesh.GetFaces().Length / 3) * Math.Max(0, instances);
            }
        }

        return (meshes, triangles, drawCallNodes);
    }

    private static double ReadProcessRssMb()
    {
        using var proc = Process.GetCurrentProcess();
        return proc.WorkingSet64 / (1024.0 * 1024.0);
    }
}
