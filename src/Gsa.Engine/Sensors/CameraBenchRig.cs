using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Camera;
using Gsa.Core.World;
using Gsa.Engine.World;
using Godot;

namespace Gsa.Engine.Sensors;

/// <summary>Godot 2 Hz physical-clock camera rig, 256x144 SubViewport, physics ray occlusion, and Gimbalina integration.</summary>
public sealed class CameraBenchRig
{
    private readonly GodotCoordinateAdapter _coordinates;
    private SubViewport? _thumbnailViewport;
    private Camera3D? _surveyCamera;

    public CameraBenchRig(GodotCoordinateAdapter coordinates)
    {
        ArgumentNullException.ThrowIfNull(coordinates);
        _coordinates = coordinates;
    }

    public SubViewport AttachRig(Node3D parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        var viewport = new SubViewport
        {
            Name = "SurveyThumbnailViewport",
            Size = new Vector2I(CameraThumbnail.ExpectedWidth, CameraThumbnail.ExpectedHeight),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
        };
        var camera = new Camera3D
        {
            Name = "SurveyCamera",
            Fov = 58.7155f,
            Near = 0.1f,
            Far = (float)CameraSensorProfile.Initial.RangeMetres,
            Current = false,
        };
        viewport.AddChild(camera);
        parent.AddChild(viewport);
        _thumbnailViewport = viewport;
        _surveyCamera = camera;
        return viewport;
    }

    public void RunVerificationSuite(
        WorldSceneManifest manifest,
        SemanticRegistry registry,
        PhysicsDirectSpaceState3D spaceState)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(spaceState);

        Require(_thumbnailViewport is not null &&
                _thumbnailViewport.Size == new Vector2I(CameraThumbnail.ExpectedWidth, CameraThumbnail.ExpectedHeight),
            "Thumbnail SubViewport must be 256x144.");

        bool IsRayOccludedInPhysics(Vector3d originEnu, Vector3d targetEnu, string targetId)
        {
            var from = _coordinates.ToGodot(originEnu);
            var to = _coordinates.ToGodot(targetEnu);
            if (from.DistanceSquaredTo(to) < 1e-8f)
                return false;
            using var query = PhysicsRayQueryParameters3D.Create(from, to, 1);
            var hit = spaceState.IntersectRay(query);
            if (hit.Count == 0)
                return false;
            var colliderObj = hit["collider"].AsGodotObject();
            if (colliderObj is null)
                return false;
            var semantic = registry.GetByInstanceId(colliderObj.GetInstanceId());
            if (semantic.Count == 0)
                return false;
            if (semantic["id"].AsString() == targetId)
                return false;
            var hitPos = hit["position"].AsVector3();
            return from.DistanceTo(hitPos) < from.DistanceTo(to) - 0.01f;
        }

        var candidates = manifest.Objects.Select(CameraVisibilityCandidate.FromWorldObject).ToArray();
        var facingNorth = Quaterniond.FromAxisAngle(Vector3d.UnitZ, Math.PI / 2.0);
        var facingSouth = Quaterniond.FromAxisAngle(Vector3d.UnitZ, -Math.PI / 2.0);

        // 1. Verify 2 Hz physical-clock scheduling (60 Hz ticks -> captures at 0, 30, 60).
        var scheduler = new CameraCaptureScheduler("cam_front_0");
        var locomotion = new LocomotionSimulator(new Vector3d(500020.0, 7400001.0, 101.0), Math.PI / 2.0, LocomotionMode.Handcart, manifest.Bounds);
        var mount = CameraMountEstimate.ForwardFacing;

        var capturedAtTick15 = scheduler.TryEnqueueSimulatedCapture(
            new CameraCaptureRequest(
                Tick: 15,
                TimestampSeconds: 15.0 / 60.0,
                TrueCameraToMapStart: CameraGeometry.CameraToMap(new EstimatedBodyPose(0.25, locomotion.PositionEnu, facingNorth), mount),
                TrueCameraToMapExposureEnd: CameraGeometry.CameraToMap(new EstimatedBodyPose(0.252, locomotion.PositionEnu, facingNorth), mount),
                TrueCameraToMapReadoutEnd: CameraGeometry.CameraToMap(new EstimatedBodyPose(0.26, locomotion.PositionEnu, facingNorth), mount),
                EstimatedPose: new EstimatedBodyPose(0.25, locomotion.PositionEnu, facingNorth),
                MountEstimate: mount,
                Environment: CameraEnvironmentSnapshot.DryDay,
                Candidates: candidates,
                IsRayOccluded: IsRayOccludedInPhysics),
            locomotion,
            out _,
            out _);
        Require(!capturedAtTick15 && scheduler.SampleCount == 0, "2 Hz camera must not capture on tick 15.");

        var clearPos = new Vector3d(500020.0, 7400001.0, 101.0);
        var poseAt0 = new EstimatedBodyPose(0.0, clearPos, facingNorth);
        var camToMap0 = CameraGeometry.CameraToMap(poseAt0, mount);
        var capturedAtTick0 = scheduler.TryEnqueueSimulatedCapture(
            new CameraCaptureRequest(
                Tick: 0,
                TimestampSeconds: 0.0,
                TrueCameraToMapStart: camToMap0,
                TrueCameraToMapExposureEnd: camToMap0,
                TrueCameraToMapReadoutEnd: camToMap0,
                EstimatedPose: poseAt0,
                MountEstimate: mount,
                Environment: CameraEnvironmentSnapshot.DryDay,
                Candidates: candidates,
                IsRayOccluded: IsRayOccludedInPhysics),
            locomotion,
            out var clearSample,
            out _);
        Require(capturedAtTick0 && clearSample is not null, "2 Hz camera failed to capture at tick 0.");
        Require(clearSample!.ObservedFeatures.Any(f => f.TargetId == "facade_01"),
            "Unoccluded camera looking at facade_01 did not detect facade_01.");
        Require(clearSample.PrivacyMasks.Any(m => m.TargetId == "facade_01" && m.Applied),
            "Privacy mask was not generated for facade_01.");
        Require(clearSample.Thumbnail.Width == 256 && clearSample.Thumbnail.Height == 144 &&
                !clearSample.FullTexturePersisted,
            "Thumbnail must be 256x144 without retaining full texture.");

        // 2. Verify real physics occlusion: looking south at facade_01 through glass_sample (collision=true).
        var occludedOrigin = new Vector3d(500020.0, 7400035.0, 101.0);
        var occludedPose = new EstimatedBodyPose(0.5, occludedOrigin, facingSouth);
        var occludedCam = CameraGeometry.CameraToMap(occludedPose, mount);
        var capturedAtTick30 = scheduler.TryEnqueueSimulatedCapture(
            new CameraCaptureRequest(
                Tick: 30,
                TimestampSeconds: 0.5,
                TrueCameraToMapStart: occludedCam,
                TrueCameraToMapExposureEnd: occludedCam,
                TrueCameraToMapReadoutEnd: occludedCam,
                EstimatedPose: occludedPose,
                MountEstimate: mount,
                Environment: CameraEnvironmentSnapshot.DryDay,
                Candidates: candidates,
                IsRayOccluded: IsRayOccludedInPhysics),
            locomotion,
            out var occludedSample,
            out _);
        Require(capturedAtTick30 && occludedSample is not null, "Camera failed to capture at tick 30.");
        Require(!occludedSample!.ObservedFeatures.Any(f => f.TargetId == "facade_01"),
            "Physics raycast occlusion failed to suppress occluded facade_01.");
        Require(occludedSample.ObservedFeatures.Any(f => f.TargetId == "glass_sample"),
            "Occluder glass_sample was expected to be visible.");

        var batchJson = scheduler.BuildBatchManifest("bench_batch_01").ToJson();
        Require(!batchJson.Contains("truth", StringComparison.OrdinalIgnoreCase) &&
                !batchJson.Contains("depth", StringComparison.OrdinalIgnoreCase),
            "Batch manifest JSON leaked truth or depth fields.");

        // 3. Gimbalina calm vs stressed comparative replay + safe escape at stress 100 for 5 s.
        var calmSample = CaptureMovingFrame(new GimbalinaStabilizer(0.0), 0, candidates, IsRayOccludedInPhysics);
        var stressedGimbalina = new GimbalinaStabilizer(0.0);
        stressedGimbalina.ApplyStartle(StartleEventKind.Fireworks); // 25
        stressedGimbalina.ApplyStartle(StartleEventKind.Fireworks); // 50
        stressedGimbalina.ApplyStartle(StartleEventKind.Fireworks); // 75
        stressedGimbalina.ApplyStartle(StartleEventKind.Dog);       // 95
        var stressedStep = stressedGimbalina.Step(1.0 / 60.0);
        var stressedSample = CaptureMovingFrame(stressedGimbalina, 60, candidates, IsRayOccludedInPhysics);

        Require(stressedStep.OscillationAmplitudeDegrees > 4.0 && !stressedGimbalina.Injured,
            "Stressed Gimbalina must oscillate up to 5 deg without injury.");
        Require(stressedSample.Quality.MotionBlurPixels > calmSample.Quality.MotionBlurPixels + 0.1,
            "Stressed Gimbalina did not causally increase motion blur compared to calm replay.");

        // Corn recovery and 5 s continuous stress=100 safe escape.
        Require(stressedGimbalina.TryFeedCorn() && stressedGimbalina.Stress == 65.0 &&
                !stressedGimbalina.TryFeedCorn(),
            "Corn feeding must reduce stress by 30 and enforce 10 s cooldown.");

        var escapeGimbalina = new GimbalinaStabilizer(100.0);
        for (var i = 0; i < 300; i++)
            escapeGimbalina.Step(1.0 / 60.0);
        Require(escapeGimbalina.HasFledToSafety && !escapeGimbalina.Injured && escapeGimbalina.SafeEscapeCount == 1,
            "Continuous stress 100 for 5 s must trigger non-injurious safe escape.");
        escapeGimbalina.RecoverAtOffice();
        Require(!escapeGimbalina.HasFledToSafety && escapeGimbalina.Stress == 0.0 && !escapeGimbalina.Injured,
            "Safe recovery at office must restore Gimbalina without injury.");

        if (_surveyCamera is not null)
            _surveyCamera.Position = _coordinates.ToGodot(clearPos);

        GD.Print($"GSA_CAMERA_SMOKE_OK samples={scheduler.SampleCount} thumb=256x144 full_persisted=false");
        GD.Print($"GSA_GIMBALINA_SMOKE_OK calm_blur={calmSample.Quality.MotionBlurPixels:F3} " +
                 $"stressed_blur={stressedSample.Quality.MotionBlurPixels:F3} injured=false");
    }

    private static CameraObservationSample CaptureMovingFrame(
        GimbalinaStabilizer gimbalina,
        long tick,
        CameraVisibilityCandidate[] candidates,
        Func<Vector3d, Vector3d, string, bool> isRayOccluded)
    {
        var env = CameraEnvironmentSnapshot.DryDay;
        var exposure = env.IsNight ? CameraSensorProfile.Initial.NightExposureSeconds : CameraSensorProfile.Initial.DayExposureSeconds;
        var baseHeading = Math.PI / 2.0; // facing facade_01 at Y=7400005 from Y=7400001
        var baseQuat = Quaterniond.FromAxisAngle(Vector3d.UnitZ, baseHeading);
        var startPos = new Vector3d(500020.0, 7400001.0, 101.0);
        var endPos = new Vector3d(500020.0 + 1.0 * exposure, 7400001.0, 101.0);

        var startBodyQuat = gimbalina.ComputeEffectiveBodyToMap(baseQuat, 0.0);
        var endBodyQuat = gimbalina.ComputeEffectiveBodyToMap(baseQuat, exposure);
        var readoutBodyQuat = gimbalina.ComputeEffectiveBodyToMap(baseQuat, CameraSensorProfile.Initial.ReadoutTimeSeconds);

        var mount = CameraMountEstimate.ForwardFacing;
        var scheduler = new CameraCaptureScheduler("cam_gimbalina");
        var ok = scheduler.TryEnqueueSimulatedCapture(
            new CameraCaptureRequest(
                Tick: tick,
                TimestampSeconds: tick / 60.0,
                TrueCameraToMapStart: CameraGeometry.CameraToMap(new EstimatedBodyPose(0.0, startPos, startBodyQuat), mount),
                TrueCameraToMapExposureEnd: CameraGeometry.CameraToMap(new EstimatedBodyPose(exposure, endPos, endBodyQuat), mount),
                TrueCameraToMapReadoutEnd: CameraGeometry.CameraToMap(new EstimatedBodyPose(CameraSensorProfile.Initial.ReadoutTimeSeconds, endPos, readoutBodyQuat), mount),
                EstimatedPose: new EstimatedBodyPose(tick / 60.0, startPos, baseQuat),
                MountEstimate: mount,
                Environment: env,
                Candidates: candidates,
                IsRayOccluded: isRayOccluded),
            locomotion: null,
            out var sample,
            out _);
        Require(ok && sample is not null, "Gimbalina comparative capture failed.");
        return sample!;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
