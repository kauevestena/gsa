using Godot;
using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Camera;
using Gsa.Core.Sensors.Lidar;
using Gsa.Core.World;
using Gsa.Engine.World;

namespace Gsa.Engine.Sensors;

/// <summary>Godot direct-space raycast adapter for the LiDAR bench (never writes TruthSample or offers MVP shop gear).</summary>
public sealed class LidarBenchRig
{
    private readonly GodotCoordinateAdapter _coordinates;

    public LidarBenchRig(GodotCoordinateAdapter coordinates)
    {
        ArgumentNullException.ThrowIfNull(coordinates);
        _coordinates = coordinates;
    }

    public void RunVerificationSuite(WorldSceneManifest manifest, SemanticRegistry registry, PhysicsDirectSpaceState3D spaceState)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(spaceState);

        // Enforce MVP invariant: initial platforms never expose LiDAR slots or offer LiDAR profiles in shop.
        if (LidarProfile.L4.CanEquipOn(PlatformProfile.OnFoot) || LidarProfile.L4.CanEquipOn(PlatformProfile.Handcart))
            throw new InvalidOperationException("Initial MVP platforms must not unlock LiDAR slots.");
        if (LidarProfile.AllProfiles.Any(p => p.OfferedInMvpShop))
            throw new InvalidOperationException("Bench LiDAR profiles cannot be offered in MVP shop.");

        // Real Godot physics raycast bench targeting facade_01 (+North from Y=7400001 across glass/open air).
        var rigEnu = new Vector3d(500020.0, 7400001.0, 101.0);
        var yawNorth = Quaterniond.FromAxisAngle(Vector3d.UnitZ, Math.PI / 2.0);
        var trajectory = new EstimatedBodyTrajectory([
            new EstimatedBodyPose(0.0, rigEnu, yawNorth),
            new EstimatedBodyPose(1.0, rigEnu, yawNorth),
        ]);
        var mount = LidarMountEstimate.Zero;
        var trueLidarToMap = LidarBenchSimulator.ComposeLidarToMap(new RigidTransform(yawNorth, rigEnu), mount);

        var sim = new LidarBenchSimulator(LidarProfile.L4, seed: 777UL);
        Func<Vector3d, Vector3d, double, LidarRayHit?> engineRayCaster = (startEnu, dirEnu, maxRange) =>
        {
            var fromG = _coordinates.ToGodot(startEnu);
            var toG = _coordinates.ToGodot(startEnu + dirEnu * maxRange);
            using var query = PhysicsRayQueryParameters3D.Create(fromG, toG, 1);
            var hit = spaceState.IntersectRay(query);
            if (hit.Count == 0)
                return null;

            var hitPosG = hit["position"].AsVector3();
            var hitNormG = hit["normal"].AsVector3();
            var hitEnu = _coordinates.ToMap(hitPosG);
            var dist = (hitEnu - startEnu).Length;
            var collider = hit["collider"].AsGodotObject();
            var meta = registry.GetByInstanceId(collider.GetInstanceId());
            var id = meta.TryGetValue("id", out var idVar) ? idVar.AsString() : "unknown";
            var mat = meta.TryGetValue("material", out var matVar) ? matVar.AsString() : "concrete";
            // Godot normal (x=E, y=Up, z=-N) -> ENU normal (E, N, Up) = (x, -z, y)
            var normalEnu = new Vector3d(hitNormG.X, -hitNormG.Z, hitNormG.Y);
            return new LidarRayHit(dist, normalEnu, mat, id);
        };

        var ok = sim.TrySimulateScan(0.0, trueLidarToMap, trajectory, mount, isRaining: false, engineRayCaster,
            out var observation, out var stopReason, autoDrain: false);
        if (!ok || observation is null || observation.Returns.Count == 0)
            throw new InvalidOperationException($"LiDAR engine smoke produced no hits (reason={stopReason}).");

        // Verify 0.5 s backlog saturation enforcement on L4 (MaxPendingQueueScans = 5 at 10 Hz).
        for (var s = 1; s < sim.MaxPendingQueueScans; s++)
        {
            if (!sim.TrySimulateScan(s * 0.1, trueLidarToMap, trajectory, mount, false, engineRayCaster,
                out _, out _, autoDrain: false))
                throw new InvalidOperationException("Queue saturated prematurely below 0.5 s backlog.");
        }
        var saturatedOk = sim.TrySimulateScan(0.5, trueLidarToMap, trajectory, mount, false, engineRayCaster,
            out _, out var satReason, autoDrain: false);
        if (saturatedOk || satReason != "sensor_queue_saturated" || sim.DroppedScansOnSaturation < 1)
            throw new InvalidOperationException("LiDAR backlog limit 0.5 s did not trigger expected saturation.");

        GD.Print($"GSA_LIDAR_SMOKE_OK profiles=3 l4_rps={LidarProfile.L4.PrimaryRaysPerSecond} " +
                 $"l5_rps={LidarProfile.L5.PrimaryRaysPerSecond} l6_rps={LidarProfile.L6.PrimaryRaysPerSecond} " +
                 $"hits={observation.Returns.Count} saturated_drops={sim.DroppedScansOnSaturation}");
    }
}

