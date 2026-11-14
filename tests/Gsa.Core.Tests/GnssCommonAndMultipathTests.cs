using System.Text.Json;
using Gsa.Core.Geometry;
using Gsa.Core.Sensors.Gnss;
using Xunit;

namespace Gsa.Core.Tests;

public sealed class GnssCommonAndMultipathTests
{
    [Fact]
    public void MultipleReceiversShareOneSampleWithoutAdvancingCommonProcess()
    {
        var process = new CommonGnssErrorProcess(1234, .5, 30);
        var first = process.At(10);
        var state = process.ExportState();
        Assert.Equal(first, process.At(10));
        Assert.Equal(state, process.ExportState());
        var profile = new GnssProfile("no_independent_noise", 0);
        var environment = new GnssEnvironment(1, Vector3d.Zero);
        var a = new GnssSimulator(1).Observe(10, Vector3d.Zero, profile, environment, first);
        var b = new GnssSimulator(2).Observe(10, Vector3d.Zero, profile, environment, process.At(10));
        Assert.Equal(a.MeasuredPositionEnu, b.MeasuredPositionEnu);
    }

    [Fact]
    public void CommonProcessJsonStateRestoresTheEntireFutureSeries()
    {
        var original = new CommonGnssErrorProcess(0x515151UL, .5, 30);
        original.At(0);
        original.At(.2);
        var state = JsonSerializer.Deserialize<CommonGnssErrorState>(JsonSerializer.Serialize(original.ExportState()));
        var restored = new CommonGnssErrorProcess(state);
        for (var index = 2; index < 200; index++)
            Assert.Equal(original.At(index * .2), restored.At(index * .2));
        Assert.Equal(original.ExportState(), restored.ExportState());
    }

    [Fact]
    public void UninitializedCommonStateCanAlsoBePersistedBeforeFirstSample()
    {
        var original = new CommonGnssErrorProcess(9, .5, 30);
        var state = JsonSerializer.Deserialize<CommonGnssErrorState>(JsonSerializer.Serialize(original.ExportState()));
        Assert.Equal(original.At(5), new CommonGnssErrorProcess(state).At(5));
    }

    [Fact]
    public void CommonErrorChangesSmoothlyAcrossOneHundredSeeds()
    {
        double squareDisplacement = 0;
        for (ulong seed = 0; seed < 100; seed++)
        {
            var process = new CommonGnssErrorProcess(seed, .5, 30);
            var initial = process.At(0);
            var next = process.At(.01);
            squareDisplacement += Vector3d.Dot(next - initial, next - initial);
        }
        // The OU model predicts RMS displacement about .0224 m; independent .5 m draws
        // at every step would be about 1.22 m and fail this bound clearly.
        Assert.InRange(Math.Sqrt(squareDisplacement / 100), .005, .1);
    }

    [Fact]
    public void CommonStationarySigmaHasExpectedMomentsAtIndependentTimeGaps()
    {
        var process = new CommonGnssErrorProcess(0x515151UL, .5, 30);
        // Gaps of 100 correlation times make dependence negligible for this mean/variance fixture.
        var samples = Enumerable.Range(0, 10000).Select(index => process.At(index * 3000d).X).ToArray();
        GnssObservationTests.AssertNormalMoments(samples, .5);
    }

    [Fact]
    public void CommonZeroSigmaDoesNotConsumeItsRandomStream()
    {
        var process = new CommonGnssErrorProcess(5, 0, 30);
        var noise = process.ExportState().Noise;
        Assert.Equal(Vector3d.Zero, process.At(0));
        Assert.Equal(Vector3d.Zero, process.At(20));
        Assert.Equal(noise, process.ExportState().Noise);
    }

    [Fact]
    public void CommonInvalidTimeAndStateFailWithoutGoingBackwards()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CommonGnssErrorProcess(0, -1, 30));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CommonGnssErrorProcess(0, .5, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CommonGnssErrorProcess(0, double.NaN, 30));
        var process = new CommonGnssErrorProcess(0, .5, 30);
        var initial = process.ExportState();
        Assert.Throws<ArgumentException>(() => new CommonGnssErrorProcess(initial with { ErrorEastMetres = 1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => process.At(double.NaN));
        process.At(2);
        var state = process.ExportState();
        Assert.Throws<ArgumentOutOfRangeException>(() => process.At(1));
        Assert.Equal(state, process.ExportState());
    }

    [Fact]
    public void CellBiasIsStableHorizontalAndAmplifiesWithoutChangingDirection()
    {
        var normal = CellMultipathBias.ForCell(0xC311UL, "célula_01", 2);
        Assert.Equal(normal, CellMultipathBias.ForCell(0xC311UL, "célula_01", 2));
        Assert.Equal(0, normal.Z);
        Assert.InRange(normal.Length, 2 - 1e-12, 2 + 1e-12);
        var amplified = CellMultipathBias.ForCell(0xC311UL, "célula_01", 2, 3);
        Assert.InRange((amplified - 3 * normal).Length, 0, 1e-12);
        Assert.NotEqual(normal, CellMultipathBias.ForCell(0xC311UL, "célula_02", 2));
        Assert.NotEqual(normal, CellMultipathBias.ForCell(0xC312UL, "célula_01", 2));
    }

    [Fact]
    public void CellBiasDoesNotDependOnQueryOrder()
    {
        var first = CellMultipathBias.ForCell(4, "São Datum/水", 1.25, 2);
        CellMultipathBias.ForCell(4, "unrelated_cell", 5, 8);
        Assert.Equal(first, CellMultipathBias.ForCell(4, "São Datum/水", 1.25, 2));
        Assert.Equal(Vector3d.Zero, CellMultipathBias.ForCell(4, "São Datum/水", 0));
    }

    [Fact]
    public void CellBiasRejectsNonfiniteNegativeOrReducingConfigurations()
    {
        Assert.Throws<ArgumentException>(() => CellMultipathBias.ForCell(0, " ", 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => CellMultipathBias.ForCell(0, "cell", -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => CellMultipathBias.ForCell(0, "cell", 1, .9));
        Assert.Throws<ArgumentOutOfRangeException>(() => CellMultipathBias.ForCell(0, "cell", 1, double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => CellMultipathBias.ForCell(0, "cell", double.MaxValue, 2));
    }
}
