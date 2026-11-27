using Gsa.Core.Geometry;
using Gsa.Core.Sensors.Camera;
using Xunit;

namespace Gsa.Core.Tests;

public sealed class AutoCameraTriggerTests
{
    private static CameraObservationSample MakeSample(string id, string targetId, Vector3d pos, bool passed = true, bool lensExceeded = false) =>
        new(
            sampleId: id,
            sensorId: "city_cam",
            sequence: 1,
            timestampSeconds: 1.0,
            estimatedPose: new EstimatedBodyPose(1.0, pos, Quaterniond.Identity),
            mountEstimate: CameraMountEstimate.ForwardFacing,
            intrinsics: CameraIntrinsics.Initial,
            quality: new CameraQualityMetrics(0.002, 0.2, 0.01, lensExceeded ? 0.45 : 0.0, 0.5, false, false, lensExceeded, false, passed),
            observedFeatures:
            [
                new ObservedFeature2D(targetId, "manhole", 320, 180, 300, 160, 340, 200, 40, 5, 5, 0.1, false),
            ],
            privacyMasks: [],
            thumbnail: CameraThumbnail.FromRgbBytes(256, 144, new byte[256 * 144 * 3]));

    [Fact]
    public void AutomaticModeIsDefaultAndManualModeSuppressesAllAutoTriggers()
    {
        var controller = new AutoCameraTriggerController();
        Assert.False(controller.ManualMode);

        Assert.True(controller.ShouldAutoTriggerCoverage(new Vector3d(500020, 7400001, 100), Math.PI / 2, true, 0.0));
        Assert.True(controller.ShouldAutoTriggerStereoTarget(new Vector3d(500020, 7400135, 100), ["manhole_01"]));

        controller.ManualMode = true;
        Assert.False(controller.ShouldAutoTriggerCoverage(new Vector3d(500020, 7400001, 100), Math.PI / 2, true, 0.0));
        Assert.False(controller.ShouldAutoTriggerStereoTarget(new Vector3d(500020, 7400135, 100), ["manhole_01"]));
    }

    [Fact]
    public void CoverageTriggerRequiresMovementOrTurnAfterCapture()
    {
        var controller = new AutoCameraTriggerController();
        var pos = new Vector3d(500020, 7400001, 100);
        const double heading = Math.PI / 2;

        Assert.True(controller.ShouldAutoTriggerCoverage(pos, heading, true, 0.0));
        controller.RecordCaptureOutcome(pos, heading, MakeSample("s1", "facade_01", pos), ["facade_01"]);

        // Stationary or tiny step should not spam captures
        Assert.False(controller.ShouldAutoTriggerCoverage(pos + new Vector3d(0.4, 0, 0), heading, true, 0.0));

        // Moving >= 1.75 m triggers next coverage shot
        Assert.True(controller.ShouldAutoTriggerCoverage(pos + new Vector3d(2.0, 0, 0), heading, true, 0.0));

        // Turning >= 0.24 rad also triggers next coverage shot
        Assert.True(controller.ShouldAutoTriggerCoverage(pos, heading + 0.30, true, 0.0));
    }

    [Fact]
    public void StereoTriggerEnforcesBaselineAndStopsAtTwoViews()
    {
        var controller = new AutoCameraTriggerController();
        var pos1 = new Vector3d(500020, 7400135, 100);
        var nearPos = new Vector3d(500021, 7400135, 100);
        var pos2 = new Vector3d(500022.5, 7400135, 100);
        var pos3 = new Vector3d(500025.5, 7400135, 100);

        Assert.True(controller.ShouldAutoTriggerStereoTarget(pos1, ["manhole_01"]));
        controller.RecordCaptureOutcome(pos1, Math.PI / 2, MakeSample("s1", "manhole_01", pos1), ["manhole_01"]);
        Assert.Equal(1, controller.GetValidViewCount("manhole_01"));

        // Too close (< 2.2 m) for stereo parallax
        Assert.False(controller.ShouldAutoTriggerStereoTarget(nearPos, ["manhole_01"]));

        // Separated >= 2.2 m triggers second view
        Assert.True(controller.ShouldAutoTriggerStereoTarget(pos2, ["manhole_01"]));
        controller.RecordCaptureOutcome(pos2, Math.PI / 2, MakeSample("s2", "manhole_01", pos2), ["manhole_01"]);
        Assert.Equal(2, controller.GetValidViewCount("manhole_01"));

        // Once 2 separated views exist, target no longer auto-triggers
        Assert.False(controller.ShouldAutoTriggerStereoTarget(pos3, ["manhole_01"]));
    }

    [Fact]
    public void LensObstructionBlocksAutoCaptureUntilCleaned()
    {
        var controller = new AutoCameraTriggerController();
        var pos = new Vector3d(500020, 7400001, 100);

        controller.RecordCaptureOutcome(pos, Math.PI / 2, MakeSample("dirty", "facade_01", pos, passed: false, lensExceeded: true));
        Assert.True(controller.IsBlockedByLensObstruction);

        // Even after cooldown elapses, dirty lens keeps auto-capture paused
        controller.StepCooldown(5.0, currentLensObstruction: 0.35);
        Assert.True(controller.IsBlockedByLensObstruction);
        Assert.False(controller.ShouldAutoTriggerCoverage(pos + new Vector3d(5, 0, 0), Math.PI / 2, true, 0.35));

        // Cleaning the lens clears the block
        controller.StepCooldown(0.1, currentLensObstruction: 0.0);
        Assert.False(controller.IsBlockedByLensObstruction);
        Assert.True(controller.ShouldAutoTriggerCoverage(pos + new Vector3d(5, 0, 0), Math.PI / 2, true, 0.0));
    }
}

