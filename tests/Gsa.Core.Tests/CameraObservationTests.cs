using System.Reflection;
using System.Text.Json;
using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Camera;
using Gsa.Core.World;
using Xunit;

namespace Gsa.Core.Tests;

public sealed class CameraObservationTests
{
    private static WorldSceneManifest LoadTutorialBlockManifest()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var json = File.ReadAllText(Path.Combine(root, "game", "world", "tutorial-block.json"));
        return WorldSceneManifest.Parse(json);
    }

    [Fact]
    public void ProfileAndSchedulerRunAtTwoHertzWithHalfSecondBacklogLimit()
    {
        var profile = CameraSensorProfile.Initial;
        Assert.Equal(2.0, profile.RateHz);
        Assert.Equal(1280, profile.Intrinsics.Width);
        Assert.Equal(720, profile.Intrinsics.Height);
        Assert.Equal(256, profile.ThumbnailWidth);
        Assert.Equal(144, profile.ThumbnailHeight);
        Assert.Equal(1.0 / 500.0, profile.DayExposureSeconds, 12);
        Assert.Equal(1.0 / 60.0, profile.NightExposureSeconds, 12);
        Assert.Equal(0.010, profile.ReadoutTimeSeconds, 12);

        var scheduler = new CameraCaptureScheduler("cam_0", profile);
        Assert.True(scheduler.IsCaptureTick(0));
        Assert.False(scheduler.IsCaptureTick(1));
        Assert.False(scheduler.IsCaptureTick(29));
        Assert.True(scheduler.IsCaptureTick(30));
        Assert.True(scheduler.IsCaptureTick(60));
        Assert.Equal(1, scheduler.MaxPendingQueueSamples);

        var pos = new Vector3d(500020, 7400001, 101);
        var facingNorth = Quaterniond.FromAxisAngle(Vector3d.UnitZ, Math.PI / 2.0);
        var pose = new EstimatedBodyPose(0.0, pos, facingNorth);
        var cam = CameraGeometry.CameraToMap(pose, CameraMountEstimate.ForwardFacing);
        var req = new CameraCaptureRequest(0, 0.0, cam, cam, cam, pose, CameraMountEstimate.ForwardFacing,
            CameraEnvironmentSnapshot.DryDay, [], (_, _, _) => false);

        Assert.True(scheduler.TryEnqueueSimulatedCapture(req, null, out _, out _, autoDrain: false));
        var req2 = req with { Tick = 30, TimestampSeconds = 0.5 };
        Assert.False(scheduler.TryEnqueueSimulatedCapture(req2, null, out _, out var saturatedReason, autoDrain: false));
        Assert.Equal("sensor_queue_saturated", saturatedReason);
        Assert.Equal(1, scheduler.SaturatedDroppedCount);

        Assert.Equal(1, scheduler.DrainPending(1));
        Assert.True(scheduler.TryEnqueueSimulatedCapture(req2 with { Tick = 60, TimestampSeconds = 1.0 }, null, out _, out _, autoDrain: true));
    }

    [Fact]
    public void FiveRayVisibilityRespectsOcclusionAndSixteenPixelMinimum()
    {
        var manifest = LoadTutorialBlockManifest();
        var candidates = manifest.Objects.Select(CameraVisibilityCandidate.FromWorldObject).ToArray();
        var tester = CameraVisibilityEvaluator.CreateAnalyticBoxOcclusionTester(manifest.Objects);
        var mount = CameraMountEstimate.ForwardFacing;

        // Clear view of facade_01 from 4 m south.
        var clearPose = new EstimatedBodyPose(0.0, new Vector3d(500020, 7400001, 101),
            Quaterniond.FromAxisAngle(Vector3d.UnitZ, Math.PI / 2.0));
        var clearCam = CameraGeometry.CameraToMap(clearPose, mount);
        var clearVisible = CameraVisibilityEvaluator.EvaluateVisibleFeatures(
            clearCam, clearCam, CameraSensorProfile.Initial, candidates, tester);
        var facade01 = Assert.Single(clearVisible, f => f.TargetId == "facade_01");
        Assert.True(facade01.VisibleSampleRays >= 3);
        Assert.True(facade01.MinProjectedDimensionPixels >= 16.0);
        Assert.True(facade01.RequiresPrivacyReview);

        // Occluded view of facade_01 from north of glass_sample (collision=true).
        var occludedPose = new EstimatedBodyPose(0.5, new Vector3d(500020, 7400035, 101),
            Quaterniond.FromAxisAngle(Vector3d.UnitZ, -Math.PI / 2.0));
        var occludedCam = CameraGeometry.CameraToMap(occludedPose, mount);
        var occludedVisible = CameraVisibilityEvaluator.EvaluateVisibleFeatures(
            occludedCam, occludedCam, CameraSensorProfile.Initial, candidates, tester);
        Assert.DoesNotContain(occludedVisible, f => f.TargetId == "facade_01");
        Assert.Contains(occludedVisible, f => f.TargetId == "glass_sample");

        // Small object at far distance (< 16 px) is filtered out.
        var tinyCandidate = new CameraVisibilityCandidate(
            "tiny_sign", "sign", new Vector3d(500020, 7400051, 101), new Vector3d(0.5, 0.1, 0.5), ["feature"]);
        var tinyVisible = CameraVisibilityEvaluator.EvaluateVisibleFeatures(
            clearCam, clearCam, CameraSensorProfile.Initial, [tinyCandidate], (_, _, _) => false);
        Assert.Empty(tinyVisible);
    }

    [Fact]
    public void MotionBlurTwoInstantProjectionMatchesAnalyticalFormulaWithinTenthOfPixel()
    {
        var intrinsics = CameraIntrinsics.Initial; // fx=640
        var projector = new CameraProjector(intrinsics);
        var target = new Vector3d(500020, 7400021, 101); // depth Z=20m from Y=7400001
        const double lateralSpeed = 10.0;
        const double exposure = 0.010; // 10 ms
        var expectedBlur = CameraExposureMetrics.ComputeAnalyticalBlurPixels(lateralSpeed, exposure, 20.0, intrinsics.Fx);
        // gsd = 20 / 640 = 0.03125 m/px; 10 * 0.01 / 0.03125 = 3.2 px
        Assert.Equal(3.2, expectedBlur, 6);

        var facingNorth = Quaterniond.FromAxisAngle(Vector3d.UnitZ, Math.PI / 2.0);
        var startCam = CameraGeometry.CameraToMap(
            new EstimatedBodyPose(0.0, new Vector3d(500020, 7400001, 101), facingNorth),
            CameraMountEstimate.ForwardFacing);
        var endCam = CameraGeometry.CameraToMap(
            new EstimatedBodyPose(exposure, new Vector3d(500020 + lateralSpeed * exposure, 7400001, 101), facingNorth),
            CameraMountEstimate.ForwardFacing);

        var twoInstantBlur = CameraExposureMetrics.ComputeTwoInstantBlurPixels(projector, target, startCam, endCam);
        Assert.True(Math.Abs(twoInstantBlur - expectedBlur) < 0.1,
            $"Expected {expectedBlur}, got {twoInstantBlur}");
    }

    [Fact]
    public void QualityFlagsDetectGlareObstructionNightNoiseAndRollingShutter()
    {
        var profile = CameraSensorProfile.Initial;
        var facingNorth = Quaterniond.FromAxisAngle(Vector3d.UnitZ, Math.PI / 2.0);
        var mount = CameraMountEstimate.ForwardFacing;
        var startCam = CameraGeometry.CameraToMap(new EstimatedBodyPose(0, new Vector3d(500020, 7400001, 101), facingNorth), mount);
        var readoutEndCam = CameraGeometry.CameraToMap(new EstimatedBodyPose(profile.ReadoutTimeSeconds, new Vector3d(500020.2, 7400001, 101), facingNorth), mount);

        var candidate = new CameraVisibilityCandidate(
            "facade_01", "building_facade", new Vector3d(500020, 7400005, 101), new Vector3d(8, 0.2, 2), ["feature", "privacy_review"]);
        var visible = CameraVisibilityEvaluator.EvaluateVisibleFeatures(startCam, readoutEndCam, profile, [candidate], (_, _, _) => false);
        var feat = Assert.Single(visible);
        Assert.True(feat.RollingShutterShiftPixels > 10.0);

        var glareEnv = new CameraEnvironmentSnapshot(isNight: true, sunAngleRadians: 3.0 * Math.PI / 180.0, lensObstructionFraction: 0.30);
        var glareMetrics = CameraExposureMetrics.EvaluateFrameMetrics(profile, glareEnv, startCam, readoutEndCam, [candidate.CenterEnu], visible.Count);
        Assert.False(glareMetrics.Passed);
        Assert.True(glareMetrics.ClippingExceeded);
        Assert.True(glareMetrics.LensObstructionExceeded);
        Assert.True(glareMetrics.MotionBlurExceeded);
        Assert.True(glareMetrics.NoiseSigmaPixels > 1.0);
    }

    [Fact]
    public void CaptureSamplesContainThumbnail256x144WithoutFullTextureOrTruthFields()
    {
        var manifest = LoadTutorialBlockManifest();
        var candidates = manifest.Objects.Select(CameraVisibilityCandidate.FromWorldObject).ToArray();
        var tester = CameraVisibilityEvaluator.CreateAnalyticBoxOcclusionTester(manifest.Objects);
        var scheduler = new CameraCaptureScheduler("cam_front_0");
        var pose = new EstimatedBodyPose(0.0, new Vector3d(500020, 7400001, 101),
            Quaterniond.FromAxisAngle(Vector3d.UnitZ, Math.PI / 2.0));
        var cam = CameraGeometry.CameraToMap(pose, CameraMountEstimate.ForwardFacing);

        var ok = scheduler.TryEnqueueSimulatedCapture(
            new CameraCaptureRequest(0, 0.0, cam, cam, cam, pose, CameraMountEstimate.ForwardFacing,
                CameraEnvironmentSnapshot.DryDay, candidates, tester),
            null,
            out var sample,
            out _);
        Assert.True(ok);
        Assert.NotNull(sample);
        Assert.Equal(256, sample!.Thumbnail.Width);
        Assert.Equal(144, sample.Thumbnail.Height);
        Assert.Equal(256 * 144 * 3, sample.Thumbnail.ByteLength);
        Assert.False(sample.FullTexturePersisted);
        Assert.False(sample.Thumbnail.FullTexturePersisted);
        Assert.Contains(sample.PrivacyMasks, m => m.TargetId == "facade_01" && m.Applied);

        var batchJson = scheduler.BuildBatchManifest("batch_01").ToJson();
        using var doc = JsonDocument.Parse(batchJson);
        Assert.False(doc.RootElement.GetProperty("full_texture_persisted").GetBoolean());

        // Reflection guard: observation DTOs never expose TruthSample, depth, or true map coordinates.
        Type[] dtoTypes =
        [
            typeof(CameraObservationSample),
            typeof(ObservedFeature2D),
            typeof(PrivacyMaskRegion),
            typeof(CameraThumbnail),
            typeof(CameraCaptureBatchManifest),
        ];
        foreach (var type in dtoTypes)
        {
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                Assert.DoesNotContain("Truth", prop.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Depth", prop.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("True", prop.Name, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}

