using System.Text.Json;
using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Fusion;
using Gsa.Core.Sensors.Gnss;
using Gsa.Core.Traffic;
using Xunit;

namespace Gsa.Core.Tests;

public sealed class CityHazardPhysicsTests
{
    private const string Zone = "zone_port_cimento";
    private static TerritoryAttentionTracker Armed()
    {
        var t = new TerritoryAttentionTracker();
        t.ImportSnapshot(new(1, [new(Zone, 100, 0, 0)], new(null, TerritoryThreatLevel.None, 0, Zone, 15, true, 10, 0))); return t;
    }
    [Fact]
    public void WarningAndBombTimersSurviveJsonReloadWithoutRestartOrRepeat()
    {
        var t = Armed(); t.Step(Zone, true, 0, false, 2);
        var restored = new TerritoryAttentionTracker(); restored.ImportSnapshot(JsonSerializer.Deserialize<TerritoryAttentionSnapshot>(JsonSerializer.Serialize(t.ExportSnapshot()))!);
        Assert.Equal(8, restored.Step(Zone, true, 0, false, .1).BombRemainingCountdownSeconds + .1, 8);
        Assert.True(restored.Step(Zone, true, 0, false, 7.91).BombDetonated);
        Assert.False(restored.Step(Zone, true, 0, false, .1).BombDetonated); Assert.True(restored.GetCooldownRemaining(Zone) > 29);
    }
    [Fact]
    public void DisarmProgressSurvivesReloadButMovementInterruptsItAndCalmContractsCancelBombs()
    {
        var t = Armed(); t.Step(Zone, true, 0, true, 2);
        var restored = new TerritoryAttentionTracker(); restored.ImportSnapshot(t.ExportSnapshot());
        Assert.True(restored.Step(Zone, true, 0, true, 3).BombDisarmed);
        t.Step(Zone, true, 1, true, .1); Assert.False(t.Step(Zone, true, 0, true, 3).BombDisarmed);
        var policy = new CalmPolicyCoordinator(false); policy.SetActiveContractCalm(true, "port");
        var outcome = t.Step(Zone, true, 0, false, .1, policy);
        Assert.True(outcome.SuppressedByCalmCity); Assert.False(outcome.BombWarningActive); Assert.False(t.ExportSnapshot().Pending!.BombActive);
    }
    [Fact]
    public void DisguiseScalesGainOnlyAndKeepsWarningCooldownAndBombInPhysicalSeconds()
    {
        var t = new TerritoryAttentionTracker(); t.ImportSnapshot(new(1, [new(Zone, 40, 0, 0)]));
        t.Step(Zone, true, 5, false, 1, attentionGainMultiplier: .5); Assert.Equal(40.5, t.GetAttention(Zone));
        t.Step(Zone, true, 5, false, 2, attentionGainMultiplier: .5);
        var restored = new TerritoryAttentionTracker(); restored.ImportSnapshot(t.ExportSnapshot());
        Assert.True(restored.Step(Zone, true, 5, false, 1, attentionGainMultiplier: .5).ActionTriggered);
        Assert.Equal(20, restored.Step(Zone, true, 5, false, 10, attentionGainMultiplier: .5).CooldownRemainingSeconds);
        Assert.True(Armed().Step(Zone, true, 0, false, 10, attentionGainMultiplier: .5).BombDetonated);
    }
    [Fact]
    public void InvalidPendingThreatsCannotEnterLiveState()
    {
        var t = Armed(); var good = t.ExportSnapshot(); var before = JsonSerializer.Serialize(good);
        Assert.Throws<ArgumentException>(() => t.ImportSnapshot(good with { Pending = good.Pending! with { BombCountdownSeconds = 11 } }));
        Assert.Throws<ArgumentException>(() => t.ImportSnapshot(good with { Pending = good.Pending! with { BombZoneId = "missing" } }));
        Assert.Equal(before, JsonSerializer.Serialize(t.ExportSnapshot()));
    }
    [Fact]
    public void TyreCapChangesActualMotionAndPersistedDamageDoesNotRepairTheCar()
    {
        var car = new CarSuspensionSimulator(Vector3d.Zero);
        for (var i = 0; i < 600; i++) car.StepMotion(1.0 / 60, 1, 0, 0, speedLimitMetresPerSecond: 6);
        Assert.Equal(6, car.SpeedMetresPerSecond, 6); Assert.InRange(car.PositionEnu.X, 40, 60);
        var impact = car.ApplyCollisionImpact(4); var restored = new CarSuspensionSimulator(car.PositionEnu, conditionDamage: car.ConditionDamage);
        Assert.Equal(impact.DamageApplied, restored.ConditionDamage);
        for (var i = 0; i < 180; i++) restored.StepMotion(1.0 / 60, 1, 0, 0);
        Assert.True(restored.SpeedMetresPerSecond > 6);
        Assert.Throws<ArgumentOutOfRangeException>(() => restored.StepMotion(.1, 1, 0, 0, speedLimitMetresPerSecond: -1));
    }
    [Fact]
    public void GnssInnovationGateRejectsObservedJumpWithoutResettingAgeOrChangingRawMeasurement()
    {
        var estimator = new LiveNavigationEstimator(ImuGrade.Tactical);
        static ImuObservation Imu(int i) => new(i, i * .01, .01, "ideal", new(0, 0, ImuProfile.GravityMetresPerSecondSquared), Vector3d.Zero);
        static GnssObservation Fix(int i, double east = 100) => new GnssSimulator(new GnssSimulator(1).ExportState() with { NextSequence = i })
            .Observe(i * .01, new(east, 200, 5), new("ideal", 0), new(1, Vector3d.Zero), Vector3d.Zero);
        static HeadingObservation Heading(int i) => new HeadingSimulator(new HeadingSimulator(1).ExportState() with { NextSequence = i }).Observe(i * .01, 0, new("ideal", 0), true);
        estimator.Step(Imu(1), Fix(1), Heading(1)); estimator.Step(Imu(2), Fix(2), Heading(2));
        var raw = Fix(3, 10000); estimator.Step(Imu(3), raw);
        Assert.True(estimator.LastGnssCorrectionRejected); Assert.Equal(1, estimator.RejectedGnssCorrections); Assert.Equal(1, estimator.GnssUpdatesApplied);
        Assert.Equal(.01, estimator.Current!.GnssSolutionAgeSeconds, 8); Assert.Equal(100, estimator.Current.PositionEnu.X, 7);
        Assert.Equal(10000, raw.MeasuredPositionEnu!.Value.X); estimator.Step(Imu(4), Fix(4));
        Assert.False(estimator.LastGnssCorrectionRejected); Assert.Equal(2, estimator.GnssUpdatesApplied); Assert.Equal(0, estimator.Current.GnssSolutionAgeSeconds);
    }
}
