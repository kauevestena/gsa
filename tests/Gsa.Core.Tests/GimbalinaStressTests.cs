using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Camera;
using Gsa.Core.World;
using Xunit;

namespace Gsa.Core.Tests;

public sealed class GimbalinaStressTests
{
    [Fact]
    public void StartleEventsCornAndRestFollowExactParametersAndNeverInjure()
    {
        var gimbalina = new GimbalinaStabilizer();
        Assert.False(gimbalina.Injured);
        Assert.Equal(0.0, gimbalina.Stress);

        Assert.Equal(10.0, gimbalina.ApplyStartle(StartleEventKind.Horn));
        Assert.Equal(25.0, gimbalina.ApplyStartle(StartleEventKind.Pothole));
        Assert.Equal(45.0, gimbalina.ApplyStartle(StartleEventKind.Dog));
        Assert.Equal(70.0, gimbalina.ApplyStartle(StartleEventKind.Fireworks));
        Assert.False(gimbalina.Injured);

        Assert.True(gimbalina.TryFeedCorn());
        Assert.Equal(40.0, gimbalina.Stress, 9);
        Assert.Equal(10.0, gimbalina.CornCooldownRemainingSeconds, 9);
        Assert.False(gimbalina.TryFeedCorn());

        // Advance 10 seconds without resting: cooldown reaches 0, corn feeds again.
        for (var i = 0; i < 600; i++)
            gimbalina.Step(1.0 / 60.0, isResting: false);
        Assert.Equal(0.0, gimbalina.CornCooldownRemainingSeconds, 6);
        Assert.True(gimbalina.TryFeedCorn());
        Assert.Equal(10.0, gimbalina.Stress, 9);

        // Resting recovers 5 stress/s -> 2 seconds clears 10 stress.
        for (var i = 0; i < 120; i++)
            gimbalina.Step(1.0 / 60.0, isResting: true);
        Assert.Equal(0.0, gimbalina.Stress, 9);
        Assert.False(gimbalina.Injured);
    }

    [Fact]
    public void StressThreshold60ControlsLinearOscillationUpTo5DegreesAndHeadCounterRotates45Degrees()
    {
        var calm = new GimbalinaStabilizer(60.0);
        Assert.Equal(0.0, calm.OscillationAmplitudeDegrees, 9);

        var eighty = new GimbalinaStabilizer(80.0);
        Assert.Equal(2.5, eighty.OscillationAmplitudeDegrees, 9);

        var max = new GimbalinaStabilizer(100.0);
        Assert.Equal(5.0, max.OscillationAmplitudeDegrees, 9);

        // Body tilted 30 deg is fully compensated by +-45 deg counter-rotation when stress <= 60.
        var compensated = calm.Step(1.0 / 60.0, bodyPitchRadians: 30.0 * Math.PI / 180.0, bodyRollRadians: -20.0 * Math.PI / 180.0);
        Assert.Equal(0.0, compensated.ResidualPitchRadians, 9);
        Assert.Equal(0.0, compensated.ResidualRollRadians, 9);

        // Body tilted 50 deg leaves 5 deg residual beyond 45 deg limit.
        var excess = calm.Step(1.0 / 60.0, bodyPitchRadians: 50.0 * Math.PI / 180.0);
        Assert.Equal(5.0 * Math.PI / 180.0, excess.ResidualPitchRadians, 9);
    }

    [Fact]
    public void CalmVsStressedReplayCausesMeasurableBlurIncreaseOnSameSegment()
    {
        var target = new Vector3d(500020, 7400005, 101);
        var candidate = new CameraVisibilityCandidate("facade_01", "building_facade", target, new Vector3d(8, 0.2, 2), ["feature", "privacy_review"]);
        var baseHeading = Math.PI / 2.0;
        var baseQuat = Quaterniond.FromAxisAngle(Vector3d.UnitZ, baseHeading);
        var startPos = new Vector3d(500020, 7400001, 101);
        var exposure = CameraSensorProfile.Initial.DayExposureSeconds;
        var endPos = new Vector3d(500020 + 1.0 * exposure, 7400001, 101);

        double CaptureBlur(GimbalinaStabilizer stabilizer)
        {
            var startCam = CameraGeometry.CameraToMap(
                new EstimatedBodyPose(0, startPos, stabilizer.ComputeEffectiveBodyToMap(baseQuat, 0.0)),
                CameraMountEstimate.ForwardFacing);
            var endCam = CameraGeometry.CameraToMap(
                new EstimatedBodyPose(exposure, endPos, stabilizer.ComputeEffectiveBodyToMap(baseQuat, exposure)),
                CameraMountEstimate.ForwardFacing);
            var scheduler = new CameraCaptureScheduler("cam_replay");
            var ok = scheduler.TryEnqueueSimulatedCapture(
                new CameraCaptureRequest(0, 0.0, startCam, endCam, endCam,
                    new EstimatedBodyPose(0, startPos, baseQuat),
                    CameraMountEstimate.ForwardFacing,
                    CameraEnvironmentSnapshot.DryDay,
                    [candidate],
                    (_, _, _) => false),
                null,
                out var sample,
                out _);
            Assert.True(ok);
            return sample!.Quality.MotionBlurPixels;
        }

        var calmBlur = CaptureBlur(new GimbalinaStabilizer(0.0));
        var stressed = new GimbalinaStabilizer(95.0);
        stressed.Step(1.0 / 60.0);
        var stressedBlur = CaptureBlur(stressed);

        Assert.True(stressedBlur > calmBlur + 0.1, $"Expected stressed blur {stressedBlur} > calm blur {calmBlur}");
    }

    [Fact]
    public void ContinuousMaxStressForFiveSecondsTriggersNonInjuriousSafeEscapeAndOfficeRecovery()
    {
        var gimbalina = new GimbalinaStabilizer(100.0);
        for (var i = 0; i < 299; i++)
        {
            var step = gimbalina.Step(1.0 / 60.0);
            Assert.False(step.HasFledToSafety);
            Assert.False(step.Injured);
        }

        var escapeStep = gimbalina.Step(1.0 / 60.0);
        Assert.True(escapeStep.HasFledToSafety);
        Assert.False(escapeStep.Injured);
        Assert.Equal(1, escapeStep.SafeEscapeCount);

        var locomotion = new LocomotionSimulator(new Vector3d(500050, 7400020, 100));
        locomotion.MarkGimbalinaFled();
        Assert.Equal(CaptureStopReason.GimbalinaFled, locomotion.EvaluateCaptureReadiness());

        gimbalina.RecoverAtOffice();
        Assert.False(gimbalina.HasFledToSafety);
        Assert.Equal(0.0, gimbalina.Stress);
        Assert.False(gimbalina.Injured);
    }
}

