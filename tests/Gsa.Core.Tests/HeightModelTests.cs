using Gsa.Core.Geodesy;
using Gsa.Core.Geometry;
using Xunit;

namespace Gsa.Core.Tests;

public sealed class HeightModelTests
{
    [Theory]
    [InlineData(500000, 7400000, 100, 10, 90)]
    [InlineData(501000, 7402000, 120, 12, 108)]
    [InlineData(499750, 7400500, 102.5, 10, 92.5)]
    [InlineData(499700, 7399600, 98.75, 9.5, 89.25)]
    [InlineData(500013.125, 7399975.5, 101.125, 10.000875, 91.124125)]
    public void AnalyticFixtureUsesAbsoluteEnuAndHMinusNg(
        double east, double north, double h, double expectedNg, double expectedH)
    {
        var position = new Vector3d(east, north, h);
        AssertNear(expectedNg, LocalGeoidModel.Separation(position));
        AssertNear(expectedH, LocalGeoidModel.ToOrthometricHeight(position));
        AssertNear(h, LocalGeoidModel.ToEllipsoidalHeight(east, north, expectedH));
    }

    [Fact]
    public void OrthometricRoundTripPreservesSubMetreHeight()
    {
        var position = new Vector3d(500081.125, 7400123.875, 103.000125);
        var orthometric = LocalGeoidModel.ToOrthometricHeight(position);
        var recovered = LocalGeoidModel.ToEllipsoidalHeight(position.X, position.Y, orthometric);
        AssertNear(position.Z, recovered);
        Assert.NotEqual(position.Z, orthometric);
    }

    [Fact]
    public void SeparationDependsOnHorizontalPositionOnly()
    {
        var horizontal = new Vector3d(500250, 7400500, -50);
        var elevated = new Vector3d(horizontal.X, horizontal.Y, 1200);
        Assert.Equal(LocalGeoidModel.Separation(horizontal), LocalGeoidModel.Separation(elevated));
        AssertNear(10.5, LocalGeoidModel.Separation(horizontal));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonfiniteScalarInputsAreRejected(double invalid)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LocalGeoidModel.ToEllipsoidalHeight(invalid, 7400000, 90));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LocalGeoidModel.ToEllipsoidalHeight(500000, invalid, 90));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LocalGeoidModel.ToEllipsoidalHeight(500000, 7400000, invalid));
    }

    [Fact]
    public void FiniteInputsCannotSilentlyProduceAnInfiniteHeight()
    {
        var extreme = new Vector3d(double.MaxValue, double.MaxValue, -double.MaxValue);
        Assert.Throws<ArgumentOutOfRangeException>(() => LocalGeoidModel.ToOrthometricHeight(extreme));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LocalGeoidModel.ToEllipsoidalHeight(double.MaxValue, double.MaxValue, double.MaxValue));
    }

    private static void AssertNear(double expected, double actual)
    {
        var tolerance = Math.Max(1e-6, Math.Abs(expected) * 1e-8);
        Assert.True(Math.Abs(expected - actual) <= tolerance,
            $"Expected {expected:R}, got {actual:R}; tolerance {tolerance:R}");
    }
}
