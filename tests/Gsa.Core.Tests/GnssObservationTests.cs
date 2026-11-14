using System.Reflection;
using System.Text.Json;
using Gsa.Core.Geometry;
using Gsa.Core.Randomness;
using Gsa.Core.Sensors.Gnss;
using Xunit;

namespace Gsa.Core.Tests;

public sealed class GnssObservationTests
{
    private static GnssEnvironment Open => new(1, Vector3d.Zero);

    [Fact]
    public void LargeEquivalentHeadingPreservesIndependentNoise()
    {
        const double angle = 1e20;
        var normalized = HeadingSimulator.WrapRadians(angle);
        var large = new HeadingSimulator(0xCE11UL).Observe(0, angle, HeadingProfile.Level0, true);
        var small = new HeadingSimulator(0xCE11UL).Observe(0, normalized, HeadingProfile.Level0, true);
        Assert.Equal(small, large);
        Assert.NotEqual(normalized, large.MeasuredRadians);
    }

    [Fact]
    public void MeasurementSynthesizesExplicitComponentsWithoutReturningThem()
    {
        var truth = new Vector3d(500010, 7400020, 103);
        var common = new Vector3d(.5, -.25, .125);
        var multipath = new Vector3d(2, -1, 0);
        var observation = new GnssSimulator(123).Observe(2, truth,
            new GnssProfile("analytic", 0), new GnssEnvironment(.5, multipath), common);
        Assert.Equal(truth + common + multipath, observation.MeasuredPositionEnu);
        Assert.Equal(GnssSolutionKind.Autonomous, observation.Solution);
        Assert.Equal(3, observation.Dop);
        Assert.Equal(0, observation.IndependentSigmaMetres);

        foreach (Type output in new[] { typeof(GnssObservation), typeof(HeadingObservation) })
            Assert.DoesNotContain(output.GetProperties(BindingFlags.Public | BindingFlags.Instance), property =>
                property.Name.Contains("Truth", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("Bias", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TunnelAndSignalLossHaveMissingMeasurementsNotZeroPositions()
    {
        var simulator = new GnssSimulator(7);
        foreach (var environment in new[] { new GnssEnvironment(.099, Vector3d.Zero), new GnssEnvironment(1, Vector3d.Zero, false) })
        {
            var observation = simulator.Observe(1, Vector3d.Zero, GnssProfile.Cellphone, environment, Vector3d.Zero);
            Assert.False(observation.IsAvailable);
            Assert.Equal(GnssSolutionKind.Unavailable, observation.Solution);
            Assert.Null(observation.MeasuredPositionEnu);
            Assert.Null(observation.IndependentSigmaMetres);
        }
        Assert.Equal(2, simulator.ExportState().NextSequence);
        Assert.True(simulator.Observe(2, Vector3d.Zero, GnssProfile.Cellphone,
            new GnssEnvironment(.1, Vector3d.Zero), Vector3d.Zero).IsAvailable);
    }

    [Fact]
    public void OutageAdvancesSequenceButDoesNotConsumeIndependentNoise()
    {
        var simulator = new GnssSimulator(991);
        var before = simulator.ExportState();
        simulator.Observe(0, Vector3d.Zero, GnssProfile.Navigation, new GnssEnvironment(0, Vector3d.Zero), Vector3d.Zero);
        var after = simulator.ExportState();
        Assert.Equal(before.IndependentNoise, after.IndependentNoise);
        Assert.Equal(before.NextSequence + 1, after.NextSequence);
        var resumed = simulator.Observe(1, Vector3d.Zero, GnssProfile.Navigation, Open, Vector3d.Zero);
        var uninterruptedNoise = new GnssSimulator(991).Observe(1, Vector3d.Zero, GnssProfile.Navigation, Open, Vector3d.Zero);
        Assert.Equal(uninterruptedNoise.MeasuredPositionEnu, resumed.MeasuredPositionEnu);
    }

    [Fact]
    public void CompleteStateRoundTripPreservesNoiseSpareAndObservationSequence()
    {
        var original = new GnssSimulator(0xA11CEUL);
        original.Observe(0, Vector3d.Zero, GnssProfile.Cellphone, Open, Vector3d.Zero);
        var state = original.ExportState();
        Assert.True(state.IndependentNoise.HasSpare);
        var restored = new GnssSimulator(JsonSerializer.Deserialize<GnssSimulatorState>(JsonSerializer.Serialize(state)));
        for (var index = 1; index <= 40; index++)
        {
            var environment = new GnssEnvironment(index % 5 == 0 ? .05 : .5, new Vector3d(.2, -.3, 0));
            Assert.Equal(original.Observe(index * .2, Vector3d.UnitX, GnssProfile.Navigation, environment, Vector3d.UnitZ),
                restored.Observe(index * .2, Vector3d.UnitX, GnssProfile.Navigation, environment, Vector3d.UnitZ));
        }
        Assert.Equal(original.ExportState(), restored.ExportState());
    }

    [Fact]
    public void HundredSeedOpenAndCanyonFixturesMatchDopVariance()
    {
        // Predeclared 100 streams x 100 samples = 10,000 observations per environment.
        // Common error and multipath are disabled to isolate the known independent sigma.
        var openValues = new List<double>(10000);
        var canyonValues = new List<double>(10000);
        for (ulong seed = 1000; seed < 1100; seed++)
        {
            var open = new GnssSimulator(seed);
            var canyon = new GnssSimulator(seed);
            for (var index = 0; index < 100; index++)
            {
                openValues.Add(open.Observe(index * .2, Vector3d.Zero, GnssProfile.Cellphone,
                    Open, Vector3d.Zero).MeasuredPositionEnu!.Value.X);
                canyonValues.Add(canyon.Observe(index * .2, Vector3d.Zero, GnssProfile.Cellphone,
                    new GnssEnvironment(.5, Vector3d.Zero), Vector3d.Zero).MeasuredPositionEnu!.Value.X);
            }
        }
        AssertNormalMoments(openValues, 3);
        AssertNormalMoments(canyonValues, 9);
        Assert.InRange(Variance(canyonValues) / Variance(openValues), 9 - 1e-10, 9 + 1e-10);
    }

    [Fact]
    public void NavigationProfileActuallyChangesMeasurementError()
    {
        var phone = new GnssSimulator(77).Observe(0, Vector3d.Zero, GnssProfile.Cellphone, Open, Vector3d.Zero);
        var navigation = new GnssSimulator(77).Observe(0, Vector3d.Zero, GnssProfile.Navigation, Open, Vector3d.Zero);
        Assert.Equal(phone.MeasuredPositionEnu!.Value / 2, navigation.MeasuredPositionEnu);
        Assert.Equal(1.5, navigation.IndependentSigmaMetres);
    }

    [Fact]
    public void StationaryPositionDoesNotSupplyAnUnreliableHeading()
    {
        Assert.True(new GnssSimulator(1).Observe(0, Vector3d.Zero, GnssProfile.Navigation, Open, Vector3d.Zero).IsAvailable);
        var heading = new HeadingSimulator(2).Observe(0, 1.234, HeadingProfile.Level0, reliable: false);
        Assert.False(heading.IsAvailable);
        Assert.Null(heading.MeasuredRadians);
        Assert.Null(heading.IndependentSigmaRadians);
        Assert.DoesNotContain(typeof(HeadingSimulator).GetMethods().SelectMany(m => m.GetParameters()),
            parameter => parameter.Name?.Contains("velocity", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void HeadingReplayRetainsSpareAndUnavailableCommands()
    {
        var first = new HeadingSimulator(0xB00CUL);
        first.Observe(0, .4, HeadingProfile.Level1, true);
        var restored = new HeadingSimulator(JsonSerializer.Deserialize<HeadingSimulatorState>(JsonSerializer.Serialize(first.ExportState())));
        for (var index = 1; index <= 20; index++)
            Assert.Equal(first.Observe(index, .4, HeadingProfile.Level1, index % 3 != 0),
                restored.Observe(index, .4, HeadingProfile.Level1, index % 3 != 0));
        Assert.Equal(first.ExportState(), restored.ExportState());
    }

    [Fact]
    public void HeadingProfilesUseKnownRadianSigmasAndIndependentNoise()
    {
        Assert.Equal(3 * Math.PI / 180, HeadingProfile.Level0.SigmaRadians);
        Assert.Equal(2 * Math.PI / 180, HeadingProfile.Level1.SigmaRadians);
        Assert.Equal(Math.PI / 180, HeadingProfile.Level2.SigmaRadians);
        var heading = new HeadingSimulator(0xCE11UL);
        var samples = Enumerable.Range(0, 10000).Select(i =>
            heading.Observe(i * .2, 0, HeadingProfile.Level0, true).MeasuredRadians!.Value).ToArray();
        AssertNormalMoments(samples, HeadingProfile.Level0.SigmaRadians);
        Assert.All(samples, value => Assert.InRange(value, -Math.PI, Math.BitDecrement(Math.PI)));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3.141592653589793, -3.141592653589793)]
    [InlineData(-3.141592653589793, -3.141592653589793)]
    [InlineData(6.283185307179586, 0)]
    public void HeadingWrappingHasExplicitHalfOpenBoundary(double value, double expected) =>
        Assert.Equal(expected, HeadingSimulator.WrapRadians(value));

    [Fact]
    public void InvalidProfilesEnvironmentTimesAndSequenceAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GnssProfile("bad", -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HeadingProfile("bad", double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GnssEnvironment(1.01, Vector3d.Zero));
        Assert.Throws<ArgumentException>(() => new GnssEnvironment(1, Vector3d.UnitZ));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GnssSimulator(0).Observe(-1, Vector3d.Zero, GnssProfile.Cellphone, Open, Vector3d.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HeadingSimulator(0).Observe(double.NaN, 0, HeadingProfile.Level0, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HeadingSimulator(0).Observe(0, double.PositiveInfinity, HeadingProfile.Level0, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GnssSimulator(new GnssSimulatorState(default, -1)));
        Assert.Throws<OverflowException>(() => new GnssSimulator(new GnssSimulatorState(default, long.MaxValue))
            .Observe(0, Vector3d.Zero, GnssProfile.Cellphone, Open, Vector3d.Zero));
    }

    internal static double Variance(IReadOnlyList<double> values)
    {
        var mean = values.Average();
        return values.Sum(value => (value - mean) * (value - mean)) / (values.Count - 1);
    }

    internal static void AssertNormalMoments(IReadOnlyList<double> values, double sigma)
    {
        Assert.InRange(Math.Abs(values.Average()), 0, 3 * sigma / Math.Sqrt(values.Count));
        Assert.InRange(Variance(values), .8 * sigma * sigma, 1.2 * sigma * sigma);
    }
}
