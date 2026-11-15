using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Camera;
using Xunit;

namespace Gsa.Core.Tests;

public sealed class CameraEnvironmentOverlapAndRigsTests
{
    private static CameraObservationSample BuildSampleForSlot(
        ScheduledCaptureSlot slot,
        string sampleId,
        EstimatedBodyPose pose,
        CameraEnvironmentSnapshot env,
        double moveSpeedMetresPerSecond = 2.0)
    {
        var dtExposure = env.IsNight ? slot.Profile.NightExposureSeconds : slot.Profile.DayExposureSeconds;
        var camStart = CameraGeometry.CameraToMap(pose, slot.MountEstimate);
        var endPose = new EstimatedBodyPose(pose.TimeSeconds + dtExposure,
            pose.PositionEnu + new Vector3d(moveSpeedMetresPerSecond * dtExposure, 0.0, 0.0),
            pose.BodyToMap);
        var camEnd = CameraGeometry.CameraToMap(endPose, slot.MountEstimate);
        var quality = CameraExposureMetrics.EvaluateFrameMetrics(
            slot.Profile, env, camStart, camEnd,
            [camStart.Transform(new Vector3d(0.0, 0.0, 12.0))], visibleFeatureCount: 1);

        var thumb = CameraThumbnail.FromRgbBytes(
            CameraThumbnail.ExpectedWidth,
            CameraThumbnail.ExpectedHeight,
            new byte[CameraThumbnail.ExpectedWidth * CameraThumbnail.ExpectedHeight * 3]);

        return new CameraObservationSample(
            sampleId, slot.SensorId, slot.Sequence, slot.ScheduledTimeSeconds,
            pose, slot.MountEstimate, slot.Profile.Intrinsics, quality, [], [], thumb);
    }

    [Fact]
    public void RainNightGlareRollingShutterAndStoppedLensCleaningBehaveCausallyAndHighSpeedLowersOverlap()
    {
        var tracker = new LensContaminationTracker();
        tracker.AccumulateRain(dtSeconds: 4.0, rainIntensity: 1.0);
        Assert.InRange(tracker.ObstructionFraction, 0.30, 0.35);

        // Moving >= 0.1 m/s does not clean lens
        Assert.False(tracker.StepStoppedCleaning(4.0, speedMetresPerSecond: 1.5));
        Assert.InRange(tracker.ObstructionFraction, 0.30, 0.35);

        // Staying stopped < 0.1 m/s for 3 s cleans lens to 0
        Assert.False(tracker.StepStoppedCleaning(2.0, speedMetresPerSecond: 0.0));
        Assert.True(tracker.StepStoppedCleaning(1.0, speedMetresPerSecond: 0.0));
        Assert.Equal(0.0, tracker.ObstructionFraction, 6);

        // Night + glare < 10 deg + moving fast increases blur/clipping/noise and rolling shutter skew
        var nightGlare = new CameraEnvironmentSnapshot(isNight: true, sunAngleRadians: 5.0 * Math.PI / 180.0, lensObstructionFraction: 0.05);
        var camStart = RigidTransform.Identity;
        var camFast = new RigidTransform(Quaterniond.Identity, new Vector3d(0.25, 0.0, 0.0));
        var metricsFast = CameraExposureMetrics.EvaluateFrameMetrics(
            CameraSensorProfile.Initial, nightGlare, camStart, camFast,
            [new Vector3d(0.0, 0.0, 10.0)], visibleFeatureCount: 1);
        Assert.True(metricsFast.NoiseSigmaPixels > 1.0);
        Assert.True(metricsFast.ClippingExceeded);
        Assert.True(metricsFast.MotionBlurExceeded);
        Assert.False(metricsFast.Passed);

        var skewFast = RollingShutterMetrics.ComputeRollingShutterSkewPixels(450.0, CameraSensorProfile.Initial);
        var skewSlow = RollingShutterMetrics.ComputeRollingShutterSkewPixels(80.0, CameraSensorProfile.Initial);
        Assert.True(skewFast > skewSlow);

        // Missing geometry must fail overlap explicitly, never return valid zero-error overlap
        var missingGeom = SurfaceFootprintOverlapCalculator.EvaluateConsecutiveOverlap(null, new SurfaceFootprint2DEnu(0, 0, 10, 10));
        Assert.False(missingGeom.HasSufficientGeometry);
        Assert.False(missingGeom.MeetsMinimumRequirement);
        Assert.Equal("camera.overlap.insufficient_geometry", missingGeom.DiagnosticReasonKey);

        // High speed step reduces consecutive footprint overlap vs low speed at same 2 Hz cadence
        var pose0 = new EstimatedBodyPose(0.0, new Vector3d(0.0, 0.0, 0.0), Quaterniond.Identity);
        var poseSlow = new EstimatedBodyPose(0.5, new Vector3d(1.0, 0.0, 0.0), Quaterniond.Identity); // 2 m/s * 0.5 s = 1 m step
        var poseFast = new EstimatedBodyPose(0.5, new Vector3d(6.0, 0.0, 0.0), Quaterniond.Identity); // 12 m/s * 0.5 s = 6 m step

        Assert.True(SurfaceFootprintOverlapCalculator.TryEstimateSupportFootprint(pose0, CameraMountEstimate.ForwardFacing, CameraSensorProfile.Initial, 8.0, out var fp0));
        Assert.True(SurfaceFootprintOverlapCalculator.TryEstimateSupportFootprint(poseSlow, CameraMountEstimate.ForwardFacing, CameraSensorProfile.Initial, 8.0, out var fpSlow));
        Assert.True(SurfaceFootprintOverlapCalculator.TryEstimateSupportFootprint(poseFast, CameraMountEstimate.ForwardFacing, CameraSensorProfile.Initial, 8.0, out var fpFast));

        var overlapSlow = SurfaceFootprintOverlapCalculator.EvaluateConsecutiveOverlap(fp0, fpSlow);
        var overlapFast = SurfaceFootprintOverlapCalculator.EvaluateConsecutiveOverlap(fp0, fpFast);
        Assert.True(overlapSlow.MeetsMinimumRequirement);
        Assert.True(overlapFast.OverlapFraction < overlapSlow.OverlapFraction);
    }

    [Fact]
    public void ScooterDualFrontAndRearRigGeneratesUniqueSampleIdsValidTimestampsAndRespectsStorage()
    {
        var agenda = PlatformCameraRigBuilder.CreateAgendaForPlatform(LocomotionMode.Scooter, initialTimeSeconds: 0.0);
        Assert.Equal(PlatformProfile.Scooter.VirtualStorageCapacityUnits, agenda.VirtualStorageCapacityUnits);

        var footprints = new Dictionary<string, SurfaceFootprint2DEnu>(StringComparer.Ordinal);
        var sampleSerial = 0;

        // Capture 4 ticks at 2 Hz (t = 0.0, 0.5, 1.0, 1.5) while riding slowly (1.0 m/s)
        for (var tickIndex = 0; tickIndex < 4; tickIndex++)
        {
            var t = tickIndex * 0.5;
            var due = agenda.PollDueCaptures(t);
            Assert.Equal(2, due.Count);

            var pose = new EstimatedBodyPose(t, new Vector3d(t * 1.0, 0.0, 0.0), Quaterniond.Identity);
            foreach (var slot in due)
            {
                var sampleId = $"{slot.SensorId}_{++sampleSerial:000}";
                var sample = BuildSampleForSlot(slot, sampleId, pose, CameraEnvironmentSnapshot.DryDay, 1.0);
                Assert.True(agenda.TryRecordSample(sample, out _));

                Assert.True(SurfaceFootprintOverlapCalculator.TryEstimateSupportFootprint(
                    pose, slot.MountEstimate, slot.Profile, 8.0, out var fp));
                footprints[sampleId] = fp;
            }
        }

        var batch = agenda.FlushBatch("scooter_batch_1");
        var report = PlatformCameraRigBuilder.BuildCoverageReport(LocomotionMode.Scooter, batch, footprints);

        Assert.Equal(LocomotionMode.Scooter, report.Mode);
        Assert.Equal(8, report.TotalSamples);
        Assert.Equal(8, report.TotalStorageUnitsUsed);
        Assert.True(report.UniqueSampleIdsVerified);
        Assert.True(report.TimestampsMonotonic);
        Assert.Equal(2, report.BySensor.Count);
        Assert.All(report.BySensor, s =>
        {
            Assert.Equal(4, s.SampleCount);
            Assert.True(s.MeetsOverlapRequirement);
        });
    }
}
