using System.Diagnostics;
using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Fusion;
using Gsa.Core.Sensors.Gnss;
using Xunit;
using Xunit.Abstractions;

namespace Gsa.Core.Tests;

public sealed class LiveNavigationTests(ITestOutputHelper output)
{
    private static readonly GnssEnvironment Open = new(1, Vector3d.Zero);
    private static readonly NavigationTruthMotion Rest = new(new(500200, 7400200, 100), Vector3d.Zero, .4);
    private static ImuObservation Imu(double time) => new((long)Math.Round(time * 100), time, .01, "ideal",
        new(0, 0, ImuProfile.GravityMetresPerSecondSquared), Vector3d.Zero);
    private static HeadingObservation Heading(double time, ulong seed = 1) =>
        new HeadingSimulator(new HeadingSimulator(seed).ExportState() with { NextSequence = (long)Math.Round(time * 100) })
            .Observe(time, .4, new HeadingProfile("ideal", 0), true);
    private static GnssObservation Fix(double time) => new GnssSimulator(new GnssSimulator(1).ExportState() with { NextSequence = (long)Math.Round(time * 100) }).Observe(time,
        new(500200, 7400200, 100), new GnssProfile("ideal", 0), Open, new(10, -5, 1));
    private static void Advance(LiveNavigationRig rig, double seconds, GnssEnvironment environment, double speed = 0)
    {
        const double dt = 1.0 / 60;
        for (var i = 0; i < (int)Math.Round(seconds * 60); i++)
        {
            var time = rig.PhysicalTimeSeconds + dt;
            var velocity = new Vector3d(speed, 0, 0);
            rig.Advance(dt, new(Rest.PositionEnu + velocity * time, velocity, speed == 0 ? Rest.YawRadians : 0), _ => environment);
        }
    }

    [Fact]
    public void OnlinePredictionMatchesHistoryPathWithCrossAxisCovariancesAndMixedUpdates()
    {
        var reference = new SixStateTrajectoryKalmanFilter(0, Rest.PositionEnu, new(2, 1, 0), Quaterniond.Identity);
        var live = new SixStateTrajectoryKalmanFilter(0, Rest.PositionEnu, new(2, 1, 0), Quaterniond.Identity);
        var gnss = new GnssSimulator(771);
        var dmi = new WheelDmiSimulator(772);
        double time = 0, maxDifference = 0;
        for (var i = 1; i <= 2000; i++)
        {
            var dt = i % 3 == 0 ? .007 : i % 3 == 1 ? .01 : .013;
            time += dt;
            var rotation = Quaterniond.FromAxisAngle(new Vector3d(1, 2, 3), .01 * i);
            var force = new Vector3d(.1 * Math.Sin(time), .1, ImuProfile.GravityMetresPerSecondSquared);
            reference.Predict(dt, force, rotation, ImuGrade.Tactical);
            live.PredictLive(dt, force, rotation, ImuGrade.Tactical);
            if (i % 20 == 0)
            {
                var fix = gnss.Observe(time, Rest.PositionEnu + new Vector3d(time * 2, time, 0), GnssProfile.Navigation, Open, Vector3d.Zero);
                reference.UpdateGnssPosition(fix); live.UpdateGnssPosition(fix);
            }
            if (i % 5 == 0)
            {
                var observation = dmi.Observe(time, .05, 2, isWetRoad: true, requestedWetSlipFraction: .1);
                reference.UpdateDmiVelocity(observation, rotation); live.UpdateDmiVelocity(observation, rotation);
            }
            if (i % 101 == 0) { reference.UpdateZupt(); live.UpdateZupt(); }
            if (i % 100 != 0) continue;
            var (x, p) = reference.ExportInternalState(); var (actual, covariance) = live.ExportInternalState();
            for (var row = 0; row < 6; row++)
            {
                maxDifference = Math.Max(maxDifference, Math.Abs(x[row] - actual[row]));
                for (var col = 0; col < 6; col++) maxDifference = Math.Max(maxDifference, Math.Abs(p[row, col] - covariance[row, col]));
            }
        }
        output.WriteLine($"mixed_updates_max_state_or_covariance_difference={maxDifference:R}");
        Assert.InRange(maxDifference, 0, 1e-9);
        Assert.Equal(reference.GnssSolutionAgeSeconds, live.GnssSolutionAgeSeconds);
    }

    [Fact]
    public void LivePredictionRemovesSteadyStateHistoryAllocations()
    {
        const int steps = 10000;
        var reference = new SixStateTrajectoryKalmanFilter(0, Rest.PositionEnu, Vector3d.Zero, Quaterniond.Identity);
        var live = new SixStateTrajectoryKalmanFilter(0, Rest.PositionEnu, Vector3d.Zero, Quaterniond.Identity);
        var force = new Vector3d(0, 0, ImuProfile.GravityMetresPerSecondSquared);
        for (var i = 0; i < 1024; i++) { reference.Predict(.01, force, Quaterniond.Identity, ImuGrade.Mems); live.PredictLive(.01, force, Quaterniond.Identity, ImuGrade.Mems); }
        var clock = Stopwatch.StartNew();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < steps; i++) reference.Predict(.01, force, Quaterniond.Identity, ImuGrade.Mems);
        var historyBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        var historyMs = clock.Elapsed.TotalMilliseconds;
        clock.Restart();
        allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < steps; i++) live.PredictLive(.01, force, Quaterniond.Identity, ImuGrade.Mems);
        var liveBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        output.WriteLine($"prediction_steps={steps} history_allocated_bytes={historyBytes} live_allocated_bytes={liveBytes} history_ms={historyMs:F3} live_ms={clock.Elapsed.TotalMilliseconds:F3}");
        Assert.True(historyBytes > 10_000_000);
        Assert.InRange(liveBytes, 0, 1024);
        Assert.Equal(reference.GetCurrentSample(), live.GetCurrentSample());
    }

    [Fact]
    public void InitializationRequiresMeasuredPositionAndHeadingAndKeepsMeasurementBias()
    {
        var estimator = new LiveNavigationEstimator(ImuGrade.Mems);
        estimator.Step(Imu(.01), new GnssSimulator(1).Observe(.01, Rest.PositionEnu, GnssProfile.Navigation,
            new GnssEnvironment(0, Vector3d.Zero), Vector3d.Zero), Heading(.01));
        Assert.Null(estimator.Current);
        estimator.Step(Imu(.02), Fix(.02));
        Assert.Equal("navigation.status.waiting_heading", estimator.StatusKey);
        Assert.Null(estimator.Current);
        var fix = Fix(.03);
        estimator.Step(Imu(.03), fix, Heading(.03));
        Assert.Equal(fix.MeasuredPositionEnu, estimator.Current!.PositionEnu);
        Assert.NotEqual(Rest.PositionEnu, estimator.Current.PositionEnu);
        Assert.Equal(1, estimator.GnssUpdatesApplied);
    }

    [Fact]
    public void InitialVelocityUsesTwoTimedMeasuredPositions()
    {
        var estimator = new LiveNavigationEstimator(ImuGrade.Mems);
        var sensor = new GnssSimulator(1);
        var profile = new GnssProfile("analytic", 0);
        estimator.Step(Imu(.01), sensor.Observe(.01, new(100, 200, 5), profile, Open, Vector3d.Zero), Heading(.01));
        Assert.Null(estimator.Current);
        estimator.Step(Imu(.02), sensor.Observe(.02, new(100.03, 200.01, 5), profile, Open, Vector3d.Zero), Heading(.02));
        var velocity = estimator.Current!.VelocityEnu;
        Assert.InRange(Math.Abs(velocity.X - 3), 0, 1e-8);
        Assert.InRange(Math.Abs(velocity.Y - 1), 0, 1e-8);
        Assert.Equal(0, velocity.Z);
        Assert.True(estimator.Current.VelocitySigmaEnu.X >= 2);
    }

    [Fact]
    public void PhysicalSensorRatesAndResultsAreIndependentOfRenderFramePartition()
    {
        var first = new LiveNavigationRig(LocomotionMode.Van, Rest, 123);
        var second = new LiveNavigationRig(LocomotionMode.Van, Rest, 123);
        Advance(first, 10, Open);
        for (var i = 0; i < 300; i++) second.Advance(1.0 / 30, Rest, _ => Open);
        Assert.Equal(1000, first.ImuSamples); Assert.Equal(200, first.DmiSamples); Assert.Equal(50, first.GnssEpochs);
        Assert.Equal(first.ImuSamples, second.ImuSamples); Assert.Equal(first.DmiSamples, second.DmiSamples);
        Assert.Equal(first.Estimator!.Current, second.Estimator!.Current);
        Assert.Equal(GnssSolutionKind.Autonomous, first.LatestGnss!.Solution);
        Assert.Equal(10, first.Estimator.Current!.TimestampSeconds, 9);
    }

    [Fact]
    public void OutageContinuesInertialPredictionAndRaisesAgeAndUncertaintyAlerts()
    {
        var rig = new LiveNavigationRig(LocomotionMode.Car, Rest with { VelocityEnu = Vector3d.UnitX, YawRadians = 0 }, 731);
        Advance(rig, 4, Open, 1);
        var before = rig.Estimator!.Current!;
        var updates = rig.Estimator.GnssUpdatesApplied;
        Advance(rig, 10, new GnssEnvironment(0, Vector3d.Zero), 1);
        var after = rig.Estimator.Current!;
        Assert.Equal(1400, rig.ImuSamples);
        Assert.Equal(updates, rig.Estimator.GnssUpdatesApplied);
        Assert.False(rig.LatestGnss!.IsAvailable);
        Assert.Null(rig.LatestGnss.MeasuredPositionEnu);
        Assert.True(after.BlackoutAlertActive);
        Assert.True(after.HighUncertaintyAlertActive);
        Assert.True(after.PositionSigma3DMetres > before.PositionSigma3DMetres);
        Assert.Equal(10, after.GnssSolutionAgeSeconds, 8);
    }

    [Fact]
    public void StationarySingleAntennaWaitsForHeadingAndLowPlatformsDoNotGainAnImu()
    {
        var car = new LiveNavigationRig(LocomotionMode.Car, Rest);
        Advance(car, 2, Open);
        Assert.Null(car.Estimator!.Current);
        Assert.Equal("navigation.status.waiting_heading", car.Estimator.StatusKey);
        var van = new LiveNavigationRig(LocomotionMode.Van, Rest);
        Advance(van, 1, Open);
        Assert.NotNull(van.Estimator!.Current);
        var handcart = new LiveNavigationRig(LocomotionMode.Handcart, Rest);
        Advance(handcart, 1, Open);
        Assert.Null(handcart.Estimator);
        Assert.Equal(0, handcart.ImuSamples);
        Assert.Equal(5, handcart.GnssEpochs);
    }

    [Fact]
    public void TrailStaysBoundedChronologicalAndIndependentOfCopiedDisplayData()
    {
        var rig = new LiveNavigationRig(LocomotionMode.Van, Rest);
        Advance(rig, 65, Open);
        Assert.Equal(LiveNavigationRig.TrailCapacity, rig.TrailCount);
        var points = new List<NavigationTrailPoint>(); rig.CopyTrailTo(points);
        Assert.Equal(600, points.Count);
        Assert.True(points[0].TimestampSeconds >= 5);
        Assert.Equal(65, points[^1].TimestampSeconds, 9);
        Assert.All(points.Zip(points.Skip(1)), pair => Assert.True(pair.Second.TimestampSeconds > pair.First.TimestampSeconds));
        points.Clear();
        Assert.Equal(600, rig.TrailCount);
        Assert.Equal(6500, rig.ImuSamples);
    }

    [Fact]
    public void StaleOrFutureCorrectionsAreRejectedBeforeChangingTheLiveState()
    {
        var estimator = new LiveNavigationEstimator(ImuGrade.Mems);
        estimator.Step(Imu(.01), Fix(.01), Heading(.01));
        Assert.Null(estimator.Current);
        Assert.Equal("navigation.status.waiting_velocity", estimator.StatusKey);
        estimator.Step(Imu(.02), Fix(.02), Heading(.02));
        var before = estimator.Current;
        Assert.Throws<ArgumentException>(() => estimator.Step(Imu(.03), Fix(.04)));
        Assert.Throws<ArgumentException>(() => estimator.Step(Imu(.03), Fix(.01)));
        Assert.Equal(before, estimator.Current);
        estimator.Step(Imu(.03));
        Assert.Equal(.03, estimator.Current!.TimestampSeconds, 9);
    }

    [Fact]
    public void ZuptRequiresContinuousObservedStationarityAndFreshDmi()
    {
        var estimator = new LiveNavigationEstimator(ImuGrade.Tactical);
        for (var i = 1; i <= 250; i++)
        {
            var time = i * .01;
            var dmi = i % 5 == 0 ? new WheelDmiObservation(i / 5, time, .05, 0, 1, .02, false, false) : null;
            estimator.Step(Imu(time), i <= 2 ? Fix(time) : null, i <= 2 ? Heading(time) : null, dmi);
            if (i < 200) Assert.Equal(0, estimator.ZuptUpdatesApplied);
        }
        Assert.True(estimator.ZuptUpdatesApplied > 0);
        var stopped = estimator.ZuptUpdatesApplied;
        for (var i = 251; i <= 300; i++) estimator.Step(Imu(i * .01));
        Assert.Equal(stopped, estimator.ZuptUpdatesApplied);
    }

    [Fact]
    public void MultiConstellationDopChangesBothObservationNoiseAndReportedDop()
    {
        var rig = new LiveNavigationRig(LocomotionMode.Van, Rest);
        Advance(rig, 1, new GnssEnvironment(.4, Vector3d.Zero));
        Assert.Equal(2.2, rig.LatestGnss!.Dop, 9);
        Assert.Equal(3.3, rig.LatestGnss.IndependentSigmaMetres!.Value, 9);
        Assert.Throws<ArgumentOutOfRangeException>(() => new GnssSimulator(1).Observe(0, Vector3d.Zero, GnssProfile.Navigation, Open, Vector3d.Zero, .5));
    }
}
