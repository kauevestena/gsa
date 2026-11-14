using System.Reflection;
using Gsa.Core.Geometry;
using Gsa.Core.Sensors.Camera;
using Xunit;

namespace Gsa.Core.Tests;

public sealed class CameraGeometryTests
{
    private static void Near(double expected, double actual, double tolerance = 1e-10) =>
        Assert.True(Math.Abs(expected - actual) <= tolerance,
            $"Expected {expected:R}, got {actual:R}; tolerance {tolerance:R}");

    private static void Near(Vector3d expected, Vector3d actual)
    {
        Near(expected.X, actual.X, Math.Max(1e-6, Math.Abs(expected.X) * 1e-8));
        Near(expected.Y, actual.Y, Math.Max(1e-6, Math.Abs(expected.Y) * 1e-8));
        Near(expected.Z, actual.Z, Math.Max(1e-6, Math.Abs(expected.Z) * 1e-8));
    }

    private static PixelPoint Pixel(ProjectionResult result)
    {
        Assert.Equal(ProjectionStatus.Visible, result.Status);
        Assert.True(result.IsVisible);
        Assert.True(result.Pixel.HasValue);
        return result.Pixel.Value;
    }

    private static EstimatedBodyTrajectory StraightTrajectory() => new([
        new EstimatedBodyPose(0, Vector3d.Zero, Quaterniond.Identity),
        new EstimatedBodyPose(1, new Vector3d(10, 0, 0), Quaterniond.Identity),
    ]);

    [Fact]
    public void InitialProfileHasNormativeResolutionAndNinetyDegreeFieldOfView()
    {
        var camera = CameraIntrinsics.Initial;
        Assert.Equal(1280, camera.Width);
        Assert.Equal(720, camera.Height);
        Near(640, camera.Fx);
        Near(640, camera.Fy);
        Near(640, camera.Cx);
        Near(360, camera.Cy);
        Assert.Equal(0, camera.K1);
        Assert.Equal(0, camera.K2);
    }

    [Theory]
    [InlineData(0, 0, 10, 640, 360)]
    [InlineData(1, 0.5, 10, 704, 392)]
    [InlineData(-1, -0.5, 10, 576, 328)]
    public void PinholeUsesRightDownForwardCameraAxes(double x, double y, double z, double u, double v)
    {
        var result = new CameraProjector(CameraIntrinsics.Initial).ProjectCameraPoint(new(x, y, z));
        var pixel = Pixel(result);
        Near(u, pixel.U);
        Near(v, pixel.V);
    }

    [Fact]
    public void RadialDistortionMatchesIndependentNormalizedFormula()
    {
        var projector = new CameraProjector(new CameraIntrinsics(2000, 2000, 1000, 800, 1000, 1000, 0.1, 0.01));
        var pixel = Pixel(projector.ProjectCameraPoint(new Vector3d(2, 1, 10)));
        // x=.2, y=.1, r²=.05, scale=1+.1*.05+.01*.0025=1.005025.
        Near(1201.005, pixel.U);
        Near(1080.402, pixel.V);
    }

    [Fact]
    public void DistortionCanMoveAnOtherwiseVisiblePixelOutsideTheImage()
    {
        var point = new Vector3d(0.5, 0, 1);
        Assert.True(new CameraProjector(CameraIntrinsics.Initial).ProjectCameraPoint(point).IsVisible);
        var distorted = new CameraProjector(new CameraIntrinsics(1280, 720, 640, 640, 640, 360, 10));
        var result = distorted.ProjectCameraPoint(point);
        Assert.Equal(ProjectionStatus.OutsideImage, result.Status);
        Assert.Null(result.Pixel);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void TargetOnOrBehindCameraPlaneHasNoMeasurement(double depth)
    {
        var result = new CameraProjector(CameraIntrinsics.Initial).ProjectCameraPoint(new(1, 2, depth));
        Assert.Equal(ProjectionStatus.BehindCamera, result.Status);
        Assert.False(result.IsVisible);
        Assert.Null(result.Pixel);
    }

    [Theory]
    [InlineData(-5, -4, true)]
    [InlineData(4.999, 3.999, true)]
    [InlineData(5, 0, false)]
    [InlineData(0, 4, false)]
    [InlineData(-5.001, 0, false)]
    [InlineData(0, -4.001, false)]
    public void PixelSupportIsHalfOpen(double x, double y, bool visible)
    {
        var projector = new CameraProjector(new CameraIntrinsics(10, 8, 1, 1, 5, 4));
        var result = projector.ProjectCameraPoint(new(x, y, 1));
        Assert.Equal(visible, result.IsVisible);
        Assert.Equal(visible, result.Pixel.HasValue);
        if (!visible)
            Assert.Equal(ProjectionStatus.OutsideImage, result.Status);
    }

    [Fact]
    public void FiniteInputsWhoseProjectionOverflowsDoNotYieldANonfinitePixel()
    {
        var result = new CameraProjector(CameraIntrinsics.Initial)
            .ProjectCameraPoint(new(double.MaxValue, 0, double.Epsilon));
        Assert.Equal(ProjectionStatus.NonFiniteProjection, result.Status);
        Assert.False(result.IsVisible);
        Assert.Null(result.Pixel);
        Assert.False(default(ProjectionResult).IsVisible);
    }

    [Fact]
    public void ForwardMountMapsTheDocumentedCameraAxesToBodyAxes()
    {
        var rotation = CameraMountEstimate.ForwardFacing.CameraToBody;
        Near(-Vector3d.UnitY, rotation.Rotate(Vector3d.UnitX));
        Near(-Vector3d.UnitZ, rotation.Rotate(Vector3d.UnitY));
        Near(Vector3d.UnitX, rotation.Rotate(Vector3d.UnitZ));
    }

    [Fact]
    public void ExtrinsicsRotateLeverArmAndSensorPointBeforeAddingMapPosition()
    {
        var pose = new EstimatedBodyPose(0, new(100, 200, 30),
            Quaterniond.FromAxisAngle(Vector3d.UnitZ, Math.PI / 2));
        var mount = new CameraMountEstimate(new(0.1, 0.2, 0.3), CameraMountEstimate.ForwardFacing.CameraToBody);
        var cameraPoint = new Vector3d(1, 2, 10);
        var transform = CameraGeometry.CameraToMap(pose, mount);
        // Camera→body (10,-1,-2), +lever=(10.1,-.8,-1.7), Rz90=(.8,10.1,-1.7).
        Near(new Vector3d(100.8, 210.1, 28.3), transform.Transform(cameraPoint));
        Near(cameraPoint, transform.Inverse().Transform(transform.Transform(cameraPoint)));
    }

    [Fact]
    public void RotatedBodyAndForwardFacingMountProjectAnEnuTargetCorrectly()
    {
        var origin = new Vector3d(500000, 7400000, 100);
        var trajectory = new EstimatedBodyTrajectory([
            new EstimatedBodyPose(0, origin, Quaterniond.FromAxisAngle(Vector3d.UnitZ, Math.PI / 2)),
        ]);
        var result = new CameraProjector(CameraIntrinsics.Initial)
            .ProjectMapPoint(origin + new Vector3d(1, 10, -1), 0, trajectory, CameraMountEstimate.ForwardFacing);
        var pixel = Pixel(result);
        Near(704, pixel.U);
        Near(424, pixel.V);
    }

    [Fact]
    public void DecimetreLeverArmCreatesPredictedGeometricAndPixelDisplacement()
    {
        var pose = new EstimatedBodyPose(0, Vector3d.Zero, Quaterniond.Identity);
        var mount = new CameraMountEstimate(new(0.1, 0, 0), Quaterniond.Identity);
        var transform = CameraGeometry.CameraToMap(pose, mount);
        Near(new Vector3d(0.1, 0, 100), transform.Transform(new(0, 0, 100)));
        var trajectory = new EstimatedBodyTrajectory([pose]);
        var pixel = Pixel(new CameraProjector(CameraIntrinsics.Initial)
            .ProjectMapPoint(new(0, 0, 100), 0, trajectory, mount));
        Near(639.36, pixel.U);
        Near(360, pixel.V);
    }

    [Fact]
    public void MilliradianBoresightAtOneHundredMetresMatchesSineAndPixelFormula()
    {
        var pose = new EstimatedBodyPose(0, Vector3d.Zero, Quaterniond.Identity);
        var mount = new CameraMountEstimate(Vector3d.Zero,
            Quaterniond.FromAxisAngle(Vector3d.UnitY, 0.001));
        var mapped = CameraGeometry.CameraToMap(pose, mount).Transform(new(0, 0, 100));
        Near(100 * Math.Sin(0.001), mapped.X);
        Near(100 * Math.Cos(0.001), mapped.Z);
        Near(0.1, mapped.X, 1e-6);
        var pixel = Pixel(new CameraProjector(CameraIntrinsics.Initial)
            .ProjectMapPoint(new(0, 0, 100), 0, new([pose]), mount));
        Near(640 - 640 * Math.Tan(0.001), pixel.U);
        Near(360, pixel.V);
    }

    [Fact]
    public void PositiveTimeOffsetSamplesALaterEstimatedPoseRatherThanMovingTheTarget()
    {
        var trajectory = StraightTrajectory();
        var noOffset = new CameraMountEstimate(Vector3d.Zero, Quaterniond.Identity);
        var offset = new CameraMountEstimate(Vector3d.Zero, Quaterniond.Identity, 0.02);
        Assert.True(trajectory.TryGetCameraToMap(0.5, offset, out var transform));
        Near(new Vector3d(5.2, 0, 0), transform.Translation);
        var projector = new CameraProjector(CameraIntrinsics.Initial);
        var before = Pixel(projector.ProjectMapPoint(new(5, 0, 100), 0.5, trajectory, noOffset));
        var after = Pixel(projector.ProjectMapPoint(new(5, 0, 100), 0.5, trajectory, offset));
        Near(640, before.U);
        Near(638.72, after.U);
        Near(0.2, transform.Translation.X - 5);
    }

    [Fact]
    public void TrajectoryInterpolatesPositionLinearlyAndAttitudeBySlerp()
    {
        var trajectory = new EstimatedBodyTrajectory([
            new EstimatedBodyPose(2, new(10, 20, 30), Quaterniond.Identity),
            new EstimatedBodyPose(4, new(30, 40, 50), Quaterniond.FromAxisAngle(Vector3d.UnitZ, Math.PI / 2)),
        ]);
        Assert.True(trajectory.TrySample(3, out var pose));
        Assert.NotNull(pose);
        Near(new Vector3d(20, 30, 40), pose.PositionEnu);
        Near(new Vector3d(Math.Sqrt(0.5), Math.Sqrt(0.5), 0), pose.BodyToMap.Rotate(Vector3d.UnitX));
    }

    [Fact]
    public void SlerpTakesTheShortPathAcrossThePiBoundary()
    {
        var trajectory = new EstimatedBodyTrajectory([
            new EstimatedBodyPose(0, Vector3d.Zero, Quaterniond.FromAxisAngle(Vector3d.UnitZ, 179 * Math.PI / 180)),
            new EstimatedBodyPose(1, Vector3d.Zero, Quaterniond.FromAxisAngle(Vector3d.UnitZ, -179 * Math.PI / 180)),
        ]);
        Assert.True(trajectory.TrySample(0.5, out var pose));
        Near(-Vector3d.UnitX, pose!.BodyToMap.Rotate(Vector3d.UnitX));
    }

    [Fact]
    public void AntipodalQuaternionsRepresentTheSameInterpolatedAttitude()
    {
        var rotation = Quaterniond.FromAxisAngle(new(1, 2, 3), 0.8);
        var antipodal = new Quaterniond(-rotation.X, -rotation.Y, -rotation.Z, -rotation.W);
        var trajectory = new EstimatedBodyTrajectory([
            new EstimatedBodyPose(0, Vector3d.Zero, rotation),
            new EstimatedBodyPose(1, Vector3d.Zero, antipodal),
        ]);
        Assert.True(trajectory.TrySample(0.5, out var pose));
        Near(rotation.Rotate(new(2, 3, 4)), pose!.BodyToMap.Rotate(new(2, 3, 4)));
    }

    [Fact]
    public void TrajectoryCopiesInputCollectionAndRetainsExactEndpoints()
    {
        var first = new EstimatedBodyPose(0, Vector3d.Zero, Quaterniond.Identity);
        var last = new EstimatedBodyPose(1, Vector3d.UnitX, Quaterniond.Identity);
        var source = new[] { first, last };
        var trajectory = new EstimatedBodyTrajectory(source);
        source[0] = new(0, new(99, 99, 99), Quaterniond.Identity);
        Assert.True(trajectory.TrySample(0, out var start));
        Assert.True(trajectory.TrySample(1, out var end));
        Assert.Same(first, start);
        Assert.Same(last, end);
    }

    [Theory]
    [InlineData(-0.001)]
    [InlineData(1.001)]
    public void OutOfIntervalTrajectoryDoesNotExtrapolate(double time)
    {
        var trajectory = StraightTrajectory();
        Assert.False(trajectory.TrySample(time, out var pose));
        Assert.Null(pose);
        var result = new CameraProjector(CameraIntrinsics.Initial).ProjectMapPoint(
            new(0, 0, 100), time, trajectory, new(Vector3d.Zero, Quaterniond.Identity));
        Assert.Equal(ProjectionStatus.TrajectoryUnavailable, result.Status);
        Assert.Null(result.Pixel);
    }

    [Fact]
    public void CorrectedTimeOutsideTrajectoryStaysUnavailable()
    {
        var result = new CameraProjector(CameraIntrinsics.Initial).ProjectMapPoint(
            new(5, 0, 100), 0.5, StraightTrajectory(), new(Vector3d.Zero, Quaterniond.Identity, 2));
        Assert.Equal(ProjectionStatus.TrajectoryUnavailable, result.Status);
        Assert.Null(result.Pixel);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonfiniteConfigurationAndTimesAreRejected(double invalid)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CameraIntrinsics.FromHorizontalFieldOfView(1280, 720, invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraIntrinsics(1280, 720, invalid, 640, 640, 360));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraIntrinsics(1280, 720, 640, 640, 640, 360, invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PixelPoint(invalid, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EstimatedBodyPose(invalid, Vector3d.Zero, Quaterniond.Identity));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraMountEstimate(Vector3d.Zero, Quaterniond.Identity, invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => StraightTrajectory().TrySample(invalid, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => StraightTrajectory().TryGetCameraToMap(
            invalid, CameraMountEstimate.ForwardFacing, out _));
    }

    [Fact]
    public void InvalidIntrinsicsPoseAndTrajectoryConfigurationsFailExplicitly()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraIntrinsics(0, 720, 640, 640, 640, 360));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraIntrinsics(1280, -1, 640, 640, 640, 360));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraIntrinsics(1280, 720, 0, 640, 640, 360));
        Assert.Throws<ArgumentOutOfRangeException>(() => CameraIntrinsics.FromHorizontalFieldOfView(1280, 720, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CameraIntrinsics.FromHorizontalFieldOfView(1280, 720, Math.PI));
        Assert.Throws<ArgumentNullException>(() => new CameraProjector(null!));
        Assert.Throws<InvalidOperationException>(() => new CameraMountEstimate(Vector3d.Zero, default));
        Assert.Throws<InvalidOperationException>(() => new EstimatedBodyPose(0, Vector3d.Zero, default));
        Assert.Throws<ArgumentException>(() => new EstimatedBodyTrajectory([]));
        Assert.Throws<ArgumentNullException>(() => new EstimatedBodyTrajectory(null!));
        Assert.Throws<ArgumentException>(() => new EstimatedBodyTrajectory([null!]));
        var pose = new EstimatedBodyPose(0, Vector3d.Zero, Quaterniond.Identity);
        Assert.Throws<ArgumentException>(() => new EstimatedBodyTrajectory([pose, pose]));
        Assert.Throws<ArgumentException>(() => new EstimatedBodyTrajectory([
            new EstimatedBodyPose(1, Vector3d.Zero, Quaterniond.Identity), pose]));
    }

    [Fact]
    public void OverflowingTimeCorrectionIsRejectedInsteadOfLookingUpAnUnrelatedPose()
    {
        var mounting = new CameraMountEstimate(Vector3d.Zero, Quaterniond.Identity, double.MaxValue);
        Assert.Throws<ArgumentOutOfRangeException>(() => StraightTrajectory()
            .TryGetCameraToMap(double.MaxValue, mounting, out _));
    }

    [Fact]
    public void EstimatedCameraApiCannotConsumeATruthSampleOrReturnTrueDepth()
    {
        foreach (var type in new[] { typeof(CameraProjector), typeof(CameraGeometry), typeof(EstimatedBodyTrajectory) })
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                Assert.DoesNotContain(method.GetParameters(), parameter => parameter.ParameterType.Name.Contains("Truth", StringComparison.Ordinal));
        }
        var mapProjection = typeof(CameraProjector).GetMethod(nameof(CameraProjector.ProjectMapPoint))!;
        Assert.Contains(mapProjection.GetParameters(), p => p.ParameterType == typeof(EstimatedBodyTrajectory));
        Assert.Contains(mapProjection.GetParameters(), p => p.ParameterType == typeof(CameraMountEstimate));
        Assert.DoesNotContain(typeof(ProjectionResult).GetProperties(), property =>
            property.Name.Contains("Depth", StringComparison.Ordinal) || property.Name.Contains("Position", StringComparison.Ordinal) ||
            property.Name.Contains("Truth", StringComparison.Ordinal));
        Assert.Equal(new[] { "U", "V" }, typeof(PixelPoint).GetProperties().Select(p => p.Name).Order().ToArray());
    }
}
