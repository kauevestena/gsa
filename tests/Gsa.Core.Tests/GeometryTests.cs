using Gsa.Core.Geometry;
using Xunit;

namespace Gsa.Core.Tests;

public sealed class GeometryTests
{
    private static void AssertNear(Vector3d expected, Vector3d actual)
    {
        AssertNear(expected.X, actual.X);
        AssertNear(expected.Y, actual.Y);
        AssertNear(expected.Z, actual.Z);
    }

    private static void AssertNear(double expected, double actual)
    {
        var tolerance = Math.Max(1e-6, Math.Abs(expected) * 1e-8);
        Assert.True(Math.Abs(expected - actual) <= tolerance,
            $"Expected {expected:R}, got {actual:R}; tolerance {tolerance:R}");
    }

    [Fact]
    public void IdentityPreservesAPointWithNonzeroComponents()
    {
        var point = new Vector3d(37.625, -91.75, 0.125);
        AssertNear(point, RigidTransform.Identity.Transform(point));
        AssertNear(point, Quaterniond.Identity.Rotate(point));
    }

    [Fact]
    public void PositiveNinetyDegreesAboutUpRotatesEastToNorth()
    {
        var rotation = Quaterniond.FromAxisAngle(Vector3d.UnitZ, Math.PI / 2);
        AssertNear(new Vector3d(0, 12, 3), rotation.Rotate(new Vector3d(12, 0, 3)));
    }

    [Fact]
    public void RigidTransformAddsLeverArmAfterRotation()
    {
        var rotation = Quaterniond.FromAxisAngle(Vector3d.UnitZ, Math.PI / 2);
        var transform = new RigidTransform(rotation, new Vector3d(20, 30, 4));
        AssertNear(new Vector3d(20, 32, 5), transform.Transform(new Vector3d(2, 0, 1)));
    }

    [Fact]
    public void InverseRecoversPointAfterNontrivialRotationAndTranslation()
    {
        var transform = new RigidTransform(
            Quaterniond.FromAxisAngle(new Vector3d(1, 2, -3), 1.137), new Vector3d(17, -23, 0.6));
        var point = new Vector3d(-5.4, 20.8, 0.125);
        AssertNear(point, transform.Inverse().Transform(transform.Transform(point)));
        AssertNear(point, (transform.Inverse() * transform).Transform(point));
    }

    [Fact]
    public void QuaternionCompositionAppliesRightHandRotationFirst()
    {
        var aroundUp = Quaterniond.FromAxisAngle(Vector3d.UnitZ, Math.PI / 2);
        var aroundEast = Quaterniond.FromAxisAngle(Vector3d.UnitX, Math.PI / 2);
        var point = Vector3d.UnitX;
        AssertNear(Vector3d.UnitZ, (aroundEast * aroundUp).Rotate(point));
        AssertNear(Vector3d.UnitY, (aroundUp * aroundEast).Rotate(point));
    }

    [Fact]
    public void TransformCompositionRotatesInnerTranslation()
    {
        var outer = new RigidTransform(
            Quaterniond.FromAxisAngle(Vector3d.UnitZ, Math.PI / 2), new Vector3d(10, 20, 30));
        var inner = new RigidTransform(Quaterniond.Identity, new Vector3d(2, 0, 0));
        AssertNear(new Vector3d(10, 23, 30), (outer * inner).Transform(Vector3d.UnitX));
    }

    [Fact]
    public void LocalFrameMapsOriginAndEachPositiveEnuAxis()
    {
        var frame = LocalMapFrame.Default;
        Assert.Equal(new Vector3d(500000, 7400000, 100), frame.Origin);
        Assert.Equal(Vector3d.Zero, frame.ToLocal(frame.Origin));
        Assert.Equal(Vector3d.UnitX, frame.ToLocal(frame.Origin + Vector3d.UnitX));
        Assert.Equal(-Vector3d.UnitZ, frame.ToLocal(frame.Origin + Vector3d.UnitY));
        Assert.Equal(Vector3d.UnitY, frame.ToLocal(frame.Origin + Vector3d.UnitZ));
    }

    [Fact]
    public void LocalFramePreservesSubMillimetreOffsetsAtLargeMapCoordinates()
    {
        var frame = LocalMapFrame.Default;
        var map = new Vector3d(500000.000125, 7400000.000375, 100.000625);
        var local = frame.ToLocal(map);
        // Compare small local components independently; map-scale relative tolerance cannot hide loss.
        AssertNear(new Vector3d(0.000125, 0.000625, -0.000375), local);
        Assert.True(local.X != 0 && local.Y != 0 && local.Z != 0);
        Assert.Equal(map, frame.ToMap(local));
    }

    [Fact]
    public void LocalFrameRoundTripWorksWithCustomOriginAndNegativeOffsets()
    {
        var frame = new LocalMapFrame(new Vector3d(-500000, -7400000, -100));
        var local = new Vector3d(-13.25, 0.0625, 47.125);
        var map = frame.ToMap(local);
        AssertNear(new Vector3d(-500013.25, -7400047.125, -99.9375), map);
        AssertNear(local, frame.ToLocal(map));
    }

    [Fact]
    public void QuaternionNormalizesFiniteNonUnitInputWithoutOverflow()
    {
        var rotation = new Quaterniond(0, 0, double.MaxValue, double.MaxValue);
        AssertNear(Vector3d.UnitY, rotation.Rotate(Vector3d.UnitX));
        AssertNear(Vector3d.UnitX, rotation.Inverse().Rotate(rotation.Rotate(Vector3d.UnitX)));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFiniteVectorsAndRotationsAreRejected(double invalid)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Vector3d(invalid, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Vector3d(0, invalid, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Vector3d(0, 0, invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Quaterniond(0, 0, 0, invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => Quaterniond.FromAxisAngle(Vector3d.UnitZ, invalid));
    }

    [Fact]
    public void UndefinedRotationsAndDirectionsFailExplicitly()
    {
        Assert.Throws<ArgumentException>(() => new Quaterniond(0, 0, 0, 0));
        Assert.Throws<InvalidOperationException>(() => default(Quaterniond).Rotate(Vector3d.UnitX));
        Assert.Throws<InvalidOperationException>(() => new RigidTransform(default, Vector3d.Zero));
        Assert.Throws<InvalidOperationException>(() => Quaterniond.FromAxisAngle(Vector3d.Zero, 0.2));
    }
}
