using System.Reflection;
using System.Text.Json;
using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Xunit;

namespace Gsa.Core.Sensors.Camera;

public sealed class FeatureInventoryTests
{
    private static CameraObservationSample MakeObservation(
        string sampleId,
        string sensorId,
        long sequence,
        double timestamp,
        EstimatedBodyPose pose,
        CameraMountEstimate mount,
        CameraIntrinsics intrinsics,
        Vector3d trueFeatureEnu,
        string targetId = "sign_01",
        string objectClass = "traffic_sign",
        bool qualityPassed = true)
    {
        pose = new EstimatedBodyPose(timestamp, pose.PositionEnu, pose.BodyToMap);
        var projector = new CameraProjector(intrinsics);
        var trajectory = new EstimatedBodyTrajectory([pose]);
        var projection = projector.ProjectMapPoint(trueFeatureEnu, timestamp - mount.TimeOffsetSeconds, trajectory, mount);
        Assert.True(projection.IsVisible);
        var px = projection.Pixel!.Value;

        var features = new[]
        {
            new ObservedFeature2D(
                targetId,
                objectClass,
                px.U,
                px.V,
                px.U - 8.0,
                px.V - 8.0,
                px.U + 8.0,
                px.V + 8.0,
                16.0,
                5,
                5,
                0.2,
                false),
        };

        var quality = new CameraQualityMetrics(
            ExposureSeconds: 0.002,
            MotionBlurPixels: qualityPassed ? 0.4 : 6.0,
            ClippingFraction: 0.01,
            LensObstructionFraction: 0.0,
            NoiseSigmaPixels: 0.5,
            MotionBlurExceeded: !qualityPassed,
            ClippingExceeded: false,
            LensObstructionExceeded: false,
            InsufficientVisibleFeatures: false,
            Passed: qualityPassed);

        var thumb = CameraThumbnail.FromRgbBytes(256, 144, new byte[256 * 144 * 3]);
        return new CameraObservationSample(sampleId, sensorId, sequence, timestamp, pose, mount, intrinsics, quality, features, [], thumb);
    }

    [Fact]
    public void SensorCaptureAgendaCoordinatesMultipleSensorsAndStopsCleanlyAtStorageCap()
    {
        var intrinsics = CameraIntrinsics.Initial;
        var front = new CameraSensorProfile("cam_front", 2.0, 60.0, intrinsics, 0.002, 1.0 / 60, 0.01, storageUnitsPerSample: 1);
        var rear = new CameraSensorProfile("cam_rear", 1.0, 60.0, intrinsics, 0.002, 1.0 / 60, 0.01, storageUnitsPerSample: 2);

        var agenda = new SensorCaptureAgenda(virtualStorageCapacityUnits: 4);
        agenda.RegisterSensor("front", front, CameraMountEstimate.ForwardFacing, initialTimeSeconds: 0.0);
        agenda.RegisterSensor("rear", rear, CameraMountEstimate.ForwardFacing, initialTimeSeconds: 0.0);

        var slot0 = agenda.PollDueCaptures(0.0);
        Assert.Equal(2, slot0.Count);

        var pose0 = new EstimatedBodyPose(0.0, Vector3d.Zero, Quaterniond.Identity);
        Assert.True(agenda.TryRecordSample(MakeObservation("s_f0", "front", 0, 0.0, pose0, CameraMountEstimate.ForwardFacing, intrinsics, new Vector3d(10, 0, 0)), out _));
        Assert.True(agenda.TryRecordSample(MakeObservation("s_r0", "rear", 0, 0.0, pose0, CameraMountEstimate.ForwardFacing, intrinsics, new Vector3d(10, 0, 0)), out _));
        Assert.Equal(3, agenda.StorageUnitsUsed);

        var slot1 = agenda.PollDueCaptures(1.0);
        // At t=1.0, rear needs 2 units (3+2=5 > 4), so rear saturates and is dropped; front fits (3+1=4).
        var dueFront = Assert.Single(slot1);
        Assert.Equal("front", dueFront.SensorId);
        Assert.Equal(1, agenda.SaturatedDroppedCount);
        Assert.Equal(CaptureStopReason.VirtualStorageFull, agenda.StopReason);

        Assert.True(agenda.TryRecordSample(MakeObservation("s_f1", "front", 1, 1.0, pose0, CameraMountEstimate.ForwardFacing, intrinsics, new Vector3d(10, 0, 0)), out _));
        var extraSample = MakeObservation("s_f2", "front", 2, 1.5, pose0, CameraMountEstimate.ForwardFacing, intrinsics, new Vector3d(10, 0, 0));
        Assert.False(agenda.TryRecordSample(extraSample, out var stopReason));
        Assert.Equal(CaptureStopReason.VirtualStorageFull, stopReason);

        var batch = agenda.FlushBatch("batch_m2_01");
        Assert.Equal(3, batch.Samples.Count);
        Assert.Equal(4, batch.StorageUnitsUsed);
        Assert.Equal(0, agenda.BufferedSampleCount);
        Assert.Equal(0, agenda.StorageUnitsUsed);

        using var json = JsonDocument.Parse(batch.ToJson());
        Assert.False(json.RootElement.GetProperty("full_texture_persisted").GetBoolean());
        Assert.Equal("VirtualStorageFull", json.RootElement.GetProperty("stop_reason").GetString());
    }

    [Fact]
    public void TwoRayTriangulationRequiresMinimumFiveDegreesParallaxAndReconstructsPoint()
    {
        var intrinsics = new CameraIntrinsics(1280, 720, 640, 640, 640, 360, k1: 0.02, k2: -0.001);
        var mount = CameraMountEstimate.ForwardFacing;
        var target = new Vector3d(500020.0, 7400005.0, 102.0);

        var poseA = new EstimatedBodyPose(0.0, new Vector3d(500000.0, 7400000.0, 101.5), Quaterniond.Identity);
        var poseNear = new EstimatedBodyPose(0.2, new Vector3d(500000.0, 7400000.5, 101.5), Quaterniond.Identity); // ~1.4 deg parallax
        var poseWide = new EstimatedBodyPose(1.0, new Vector3d(500000.0, 7400010.0, 101.5), Quaterniond.Identity); // > 13 deg parallax

        var sampleA = MakeObservation("a", "front", 0, 0.0, poseA, mount, intrinsics, target);
        var sampleNear = MakeObservation("b_near", "front", 1, 0.2, poseNear, mount, intrinsics, target);
        var sampleWide = MakeObservation("b_wide", "front", 2, 1.0, poseWide, mount, intrinsics, target);

        // Single frame without support plane -> InsufficientGeometry.
        var single = FeatureInventoryTriangulator.BuildInventory([sampleA], FeatureConfirmationMode.Arcade);
        var singleEntry = Assert.Single(single.Entries);
        Assert.Equal(TriangulationStatus.InsufficientGeometry, singleEntry.Status);
        Assert.Equal(GeometryFailureReason.SingleFrameWithoutSupportPlane, singleEntry.FailureReason);
        Assert.Null(singleEntry.EstimatedPositionEnu);

        // Pair with < 5 deg parallax and no support plane -> InsufficientGeometry.
        var shallow = FeatureInventoryTriangulator.BuildInventory([sampleA, sampleNear], FeatureConfirmationMode.Arcade);
        var shallowEntry = Assert.Single(shallow.Entries);
        Assert.Equal(TriangulationStatus.InsufficientGeometry, shallowEntry.Status);
        Assert.Equal(GeometryFailureReason.LowParallaxAngle, shallowEntry.FailureReason);
        Assert.True(shallowEntry.MaxParallaxDegrees < 5.0);
        Assert.Null(shallowEntry.EstimatedPositionEnu);

        // Pair with >= 5 deg parallax -> Resolved accurately even with lens distortion!
        var wide = FeatureInventoryTriangulator.BuildInventory([sampleA, sampleWide], FeatureConfirmationMode.Arcade);
        var wideEntry = Assert.Single(wide.Entries);
        Assert.Equal(TriangulationStatus.Resolved, wideEntry.Status);
        Assert.Equal(FeatureReconstructionMethod.TwoRayTriangulation, wideEntry.Method);
        Assert.NotNull(wideEntry.EstimatedPositionEnu);
        Assert.True((wideEntry.EstimatedPositionEnu!.Value - target).Length < 1e-4);
        Assert.Single(wide.ResolvedConfirmedFeatures);
    }

    [Fact]
    public void SingleFrameUsesKnownSupportPlaneAboveFiveDegreesAndRejectsGrazingRay()
    {
        var intrinsics = CameraIntrinsics.Initial;
        var mount = CameraMountEstimate.ForwardFacing;
        var groundPlane = SupportPlane.HorizontalGround(100.0);

        // Point on ground 10 m ahead; camera at Z=101.5 -> ray slopes down at atan(1.5/10) ~ 8.53 deg >= 5 deg.
        var steepTarget = new Vector3d(500010.0, 7400000.0, 100.0);
        var pose = new EstimatedBodyPose(0.0, new Vector3d(500000.0, 7400000.0, 101.5), Quaterniond.Identity);
        var steepSample = MakeObservation("g_steep", "front", 0, 0.0, pose, mount, intrinsics, steepTarget, "manhole_01", "manhole");

        var steepResult = FeatureInventoryTriangulator.BuildInventory([steepSample], FeatureConfirmationMode.Arcade, defaultSupportPlane: groundPlane);
        var steepEntry = Assert.Single(steepResult.Entries);
        Assert.Equal(TriangulationStatus.Resolved, steepEntry.Status);
        Assert.Equal(FeatureReconstructionMethod.KnownSupportPlane, steepEntry.Method);
        Assert.True((steepEntry.EstimatedPositionEnu!.Value - steepTarget).Length < 1e-6);

        // Grazing ray (< 5 deg): 40 m ahead, 1.5 m high -> atan(1.5/40) ~ 2.15 deg -> rejected.
        var grazingTarget = new Vector3d(500040.0, 7400000.0, 100.0);
        var grazingSample = MakeObservation("g_flat", "front", 1, 0.5, pose, mount, intrinsics, grazingTarget, "manhole_02", "manhole");

        var grazingResult = FeatureInventoryTriangulator.BuildInventory([grazingSample], FeatureConfirmationMode.Arcade, defaultSupportPlane: groundPlane);
        var grazingEntry = Assert.Single(grazingResult.Entries);
        Assert.Equal(TriangulationStatus.InsufficientGeometry, grazingEntry.Status);
        Assert.Equal(GeometryFailureReason.LowPlaneGrazingAngle, grazingEntry.FailureReason);
        Assert.Null(grazingEntry.EstimatedPositionEnu);
    }

    [Fact]
    public void TechnicalModeRequiresExplicitConfirmationWhileArcadeConfirmsAutomatically()
    {
        var intrinsics = CameraIntrinsics.Initial;
        var mount = CameraMountEstimate.ForwardFacing;
        var groundPlane = SupportPlane.HorizontalGround(100.0);
        var pose = new EstimatedBodyPose(0.0, new Vector3d(500000.0, 7400000.0, 101.5), Quaterniond.Identity);
        var target = new Vector3d(500008.0, 7400000.0, 100.0);

        var manholeSample = MakeObservation("s1", "front", 0, 0.0, pose, mount, intrinsics, target, "manhole_01", "manhole");
        var treeSample = MakeObservation("s2", "front", 1, 0.5, pose, mount, intrinsics, target, "tree_01", "tree");

        var unconfirmedTech = FeatureInventoryTriangulator.BuildInventory(
            [manholeSample, treeSample],
            FeatureConfirmationMode.Technical,
            confirmedClassesOrTargets: new HashSet<string>(StringComparer.Ordinal) { "manhole" },
            defaultSupportPlane: groundPlane);

        Assert.Single(unconfirmedTech.ResolvedConfirmedFeatures);
        Assert.Equal("manhole_01", unconfirmedTech.ResolvedConfirmedFeatures[0].TargetId);
        var treeEntry = unconfirmedTech.Entries.Single(e => e.TargetId == "tree_01");
        Assert.Equal(TriangulationStatus.Unconfirmed, treeEntry.Status);
        Assert.Equal(GeometryFailureReason.UnconfirmedClassOrFeature, treeEntry.FailureReason);
        Assert.Null(treeEntry.EstimatedPositionEnu);

        var arcade = FeatureInventoryTriangulator.BuildInventory(
            [manholeSample, treeSample],
            FeatureConfirmationMode.Arcade,
            defaultSupportPlane: groundPlane);
        Assert.Equal(2, arcade.ResolvedConfirmedFeatures.Count);
    }

    [Fact]
    public void InventoryAndAgendaTypesNeverExposeTruthSampleOrTrueDepth()
    {
        Type[] types =
        [
            typeof(SensorCaptureAgenda),
            typeof(ObservationBatch),
            typeof(FeatureInventoryTriangulator),
            typeof(FeatureInventoryResult),
            typeof(TriangulatedFeatureEntry),
            typeof(CameraRayEnu),
        ];

        foreach (var type in types)
        {
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                Assert.DoesNotContain("Truth", prop.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("True", prop.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Depth", prop.Name, StringComparison.OrdinalIgnoreCase);
            }
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                Assert.DoesNotContain(method.GetParameters(), p => p.ParameterType.Name.Contains("Truth", StringComparison.OrdinalIgnoreCase));
        }
    }
}
