using Gsa.Core.Geometry;
using Gsa.Core.Sensors.Gnss;
using Xunit;

namespace Gsa.Core.Tests;

public sealed class DgnssCorrectionTests
{
    private static readonly SurveyMarkBaseStationSpec MarkM1 = new(
        "mark_pracamatriz",
        "Praça da Matriz",
        new Vector3d(500100.0, 7400100.0, 100.0),
        OperatingWindowStartSeconds: 0.0,
        OperatingWindowEndSeconds: 120.0,
        MinimumOpenness: 0.8);

    [Fact]
    public void DgnssReducesSharedErrorInOpenSkyWhilePreservingLocalMultipath()
    {
        var timestamps = Enumerable.Range(0, 60).Select(i => (double)i).ToArray();
        var baseCommon = new CommonGnssErrorProcess(seed: 101, sigmaMetres: 4.5, correlationTimeSeconds: 30.0);
        var roverCommon = new CommonGnssErrorProcess(seed: 101, sigmaMetres: 4.5, correlationTimeSeconds: 30.0);
        var baseReceiver = new GnssSimulator(independentNoiseSeed: 202);
        var baseLog = DgnssBaseSimulator.RecordSession(MarkM1, baseOpenness: 0.95, timestamps, baseCommon, baseReceiver);

        Assert.Equal(60, baseLog.Samples.Count);

        var roverTrueClean = new Vector3d(500250.0, 7400200.0, 100.5);
        var roverSim = new GnssSimulator(independentNoiseSeed: 303);
        var roverEnvClean = new GnssEnvironment(openness: 0.95, multipathBiasEnu: Vector3d.Zero, signalAvailable: true);
        var corrector = new DgnssRoverCorrector(seed: 404);

        var autoSqErrSum = 0.0;
        var dgnssSqErrSum = 0.0;
        var commonAtTen = Vector3d.Zero;

        foreach (var t in timestamps)
        {
            var common = roverCommon.At(t);
            if (Math.Abs(t - 10.0) < 1e-9)
                commonAtTen = common;

            var rawRover = roverSim.Observe(t, roverTrueClean, GnssProfile.Dgnss, roverEnvClean, common);
            var dgnssResult = corrector.ApplyCorrection(rawRover, MarkM1, baseLog);

            Assert.True(dgnssResult.DifferentialApplied);
            Assert.Equal(GnssSolutionKind.Dgnss, dgnssResult.Observation.Solution);
            Assert.True(dgnssResult.Observation.IsAvailable);

            var autoErr = (rawRover.MeasuredPositionEnu!.Value - roverTrueClean).Length;
            var dgnssErr = (dgnssResult.Observation.MeasuredPositionEnu!.Value - roverTrueClean).Length;
            autoSqErrSum += autoErr * autoErr;
            dgnssSqErrSum += dgnssErr * dgnssErr;
        }

        var autoRmse = Math.Sqrt(autoSqErrSum / timestamps.Length);
        var dgnssRmse = Math.Sqrt(dgnssSqErrSum / timestamps.Length);
        Assert.True(dgnssRmse < autoRmse * 0.4, $"Expected DGNSS RMSE ({dgnssRmse:F3}) << Autonomous ({autoRmse:F3}).");

        // Local rover canyon multipath must remain present after DGNSS correction
        var localMultipath = new Vector3d(6.0, -4.0, 0.0);
        var canyonEnv = new GnssEnvironment(openness: 0.5, multipathBiasEnu: localMultipath, signalAvailable: true);
        var canyonObs = roverSim.Observe(10.0, roverTrueClean, GnssProfile.Dgnss, canyonEnv, commonAtTen);
        var canyonDgnss = corrector.ApplyCorrection(canyonObs, MarkM1, baseLog);

        Assert.True(canyonDgnss.DifferentialApplied);
        var eastBias = canyonDgnss.Observation.MeasuredPositionEnu!.Value.X - roverTrueClean.X;
        var northBias = canyonDgnss.Observation.MeasuredPositionEnu!.Value.Y - roverTrueClean.Y;
        Assert.True(eastBias > 3.0, "Local multipath positive East bias must not be magically cancelled by DGNSS.");
        Assert.True(northBias < -1.5, "Local multipath negative North bias must not be magically cancelled by DGNSS.");
    }

    [Fact]
    public void LowOpennessBaseAndOutOfWindowQueriesFallBackToAutonomous()
    {
        var timestamps = Enumerable.Range(0, 30).Select(i => (double)i).ToArray();
        var baseReceiver = new GnssSimulator(independentNoiseSeed: 22);
        var roverSim = new GnssSimulator(independentNoiseSeed: 33);
        var corrector = new DgnssRoverCorrector(seed: 44);
        var roverEnv = new GnssEnvironment(0.9, Vector3d.Zero, signalAvailable: true);

        // Openness 0.65 < 0.80 produces zero valid base samples
        var degradedCommon = new CommonGnssErrorProcess(seed: 11, sigmaMetres: 3.0, correlationTimeSeconds: 20.0);
        var degradedLog = DgnssBaseSimulator.RecordSession(MarkM1, baseOpenness: 0.65, timestamps, degradedCommon, baseReceiver);
        Assert.Empty(degradedLog.Samples);

        var roverCommon = new CommonGnssErrorProcess(seed: 11, sigmaMetres: 3.0, correlationTimeSeconds: 20.0);
        var roverObs = roverSim.Observe(10.0, new Vector3d(500150.0, 7400120.0, 100.0), GnssProfile.Navigation, roverEnv, roverCommon.At(10.0));
        var fallbackUnavailable = corrector.ApplyCorrection(roverObs, MarkM1, degradedLog);
        Assert.False(fallbackUnavailable.DifferentialApplied);
        Assert.Equal(GnssSolutionKind.Autonomous, fallbackUnavailable.Observation.Solution);
        Assert.Equal("gnss.dgnss.base_unavailable", fallbackUnavailable.FallbackReasonKey);

        // Good base log but observation outside contracted operating window [0, 120]
        var goodBaseCommon = new CommonGnssErrorProcess(seed: 11, sigmaMetres: 3.0, correlationTimeSeconds: 20.0);
        var goodLog = DgnssBaseSimulator.RecordSession(MarkM1, baseOpenness: 0.9, timestamps, goodBaseCommon, baseReceiver);
        var lateObs = roverSim.Observe(150.0, new Vector3d(500150.0, 7400120.0, 100.0), GnssProfile.Navigation, roverEnv, roverCommon.At(150.0));
        var fallbackWindow = corrector.ApplyCorrection(lateObs, MarkM1, goodLog);
        Assert.False(fallbackWindow.DifferentialApplied);
        Assert.Equal(GnssSolutionKind.Autonomous, fallbackWindow.Observation.Solution);
        Assert.Equal("gnss.dgnss.outside_operating_window", fallbackWindow.FallbackReasonKey);
    }

    [Fact]
    public void SpatialDecorrelationScalesAtTwoCentimetresPerKilometreAndRejectsOverRangeBaseline()
    {
        var baseCommon = new CommonGnssErrorProcess(seed: 71, sigmaMetres: 2.0, correlationTimeSeconds: 20.0);
        var roverCommon = new CommonGnssErrorProcess(seed: 71, sigmaMetres: 2.0, correlationTimeSeconds: 20.0);
        var baseReceiver = new GnssSimulator(independentNoiseSeed: 72);
        var roverSim = new GnssSimulator(independentNoiseSeed: 73);
        var corrector = new DgnssRoverCorrector(seed: 74);

        var log = DgnssBaseSimulator.RecordSession(MarkM1, 0.95, [10.0, 20.0], baseCommon, baseReceiver);
        var env = new GnssEnvironment(0.95, Vector3d.Zero, signalAvailable: true);

        // 5 km baseline -> 0.10 m residual sigma
        var obs5km = roverSim.Observe(10.0, new Vector3d(505100.0, 7400100.0, 100.0), GnssProfile.Dgnss, env, roverCommon.At(10.0));
        var res5km = corrector.ApplyCorrection(obs5km, MarkM1, log, maxBaselineMetres: 10000.0);
        Assert.True(res5km.DifferentialApplied);
        Assert.Equal(0.02 * (res5km.BaselineMetres / 1000.0), res5km.ResidualCorrelatedErrorSigmaMetres, 6);
        Assert.InRange(res5km.ResidualCorrelatedErrorSigmaMetres, 0.09, 0.12);

        // 15 km baseline exceeds maxBaselineMetres 10 km
        var obs15km = roverSim.Observe(20.0, new Vector3d(515100.0, 7400100.0, 100.0), GnssProfile.Dgnss, env, roverCommon.At(20.0));
        var res15km = corrector.ApplyCorrection(obs15km, MarkM1, log, maxBaselineMetres: 10000.0);
        Assert.False(res15km.DifferentialApplied);
        Assert.Equal("gnss.dgnss.excessive_baseline_decorrelation", res15km.FallbackReasonKey);
    }
}
