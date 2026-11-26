using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Missions.Activities;
using Xunit;

namespace Gsa.Missions.Tests;

public sealed class GimbalinaChallengeTests
{
    private static readonly GimbalinaStepResult Level = new GimbalinaStabilizer().Step(1.0 / 60);
    private static readonly Vector3d East = Vector3d.UnitX;

    private static GimbalinaChallenge Start(LocomotionMode mode = LocomotionMode.Bicycle)
    {
        var run = new GimbalinaChallenge();
        // Reach the start line at the very end of the tick. Waiting and the run-up are untimed.
        run.Step(2, GimbalinaChallenge.Grid, GimbalinaChallenge.Start, mode, Level);
        Assert.Equal(ParkRunState.Riding, run.State);
        Assert.Equal(0, run.Seconds);
        return run;
    }

    [Theory]
    [InlineData(0, 35, ParkRunMedal.Gold)]
    [InlineData(5, 42, ParkRunMedal.Silver)]
    [InlineData(5, 55, ParkRunMedal.Bronze)]
    [InlineData(6, 35, ParkRunMedal.None)]
    public void MedalRequiresOrderedGatesAndAtLeast95PercentOfActualHeadSamples(int badSamples, int seconds, ParkRunMedal medal)
    {
        var run = Start();
        var previous = GimbalinaChallenge.Start;
        var outside = Level with { ResidualPitchRadians = GimbalinaChallenge.HeadToleranceRadians + .0001 };
        var atLimit = Level with { ResidualRollRadians = -GimbalinaChallenge.HeadToleranceRadians };
        for (var n = 1; n <= 100; n++)
        {
            var leg = (n - 1) / 25;
            var a = GimbalinaChallenge.Gates[leg];
            var b = GimbalinaChallenge.Gates[leg + 1];
            var pos = a + (b - a) * ((n - leg * 25) / 25d);
            run.Step(seconds / 100d, previous, pos, LocomotionMode.Bicycle, n <= badSamples ? outside : atLimit);
            previous = pos;
        }
        Assert.Equal(medal, run.Medal);
        Assert.Equal(medal == ParkRunMedal.None ? ParkRunState.Unsteady : ParkRunState.Finished, run.State);
        Assert.Equal(1 - badSamples / 100d, run.SteadyFraction, 8);
        Assert.Equal(seconds, run.Seconds, 8);
        run.Step(20, previous, previous + East, LocomotionMode.Bicycle, outside);
        Assert.Equal(seconds, run.Seconds, 8);
    }

    [Fact]
    public void GateUsesIntersectionNotEndpointAndClockStartsAndStopsAtTheLine()
    {
        var run = new GimbalinaChallenge();
        var start = GimbalinaChallenge.Start;
        run.Step(1, start + East, start - East, LocomotionMode.Bicycle, Level);
        Assert.Equal(ParkRunState.Ready, run.State);
        run.Step(1, start - 2 * East, start + 2 * East, LocomotionMode.Bicycle, Level);
        var previous = start + 2 * East;
        foreach (var gate in GimbalinaChallenge.Gates.Skip(1).SkipLast(1))
        {
            // Only the crossing lies inside the gate.
            run.Step(6, previous, gate - East - 1.5 * Vector3d.UnitY, LocomotionMode.Bicycle, Level);
            run.Step(1, gate - East - 1.5 * Vector3d.UnitY, gate + East + 2.8 * Vector3d.UnitY, LocomotionMode.Bicycle, Level);
            previous = gate + East + 2.8 * Vector3d.UnitY;
        }
        var finish = GimbalinaChallenge.Gates[^1];
        run.Step(6, previous, finish - East, LocomotionMode.Bicycle, Level);
        run.Step(1, finish - East, finish + 3 * East, LocomotionMode.Bicycle, Level);
        Assert.Equal(ParkRunState.Finished, run.State);
        Assert.Equal(.5 + 3 * 7 + 6 + .25, run.Seconds, 8);
    }

    [Fact]
    public void ShortcutsLostPartnerAndVehicleChangesEndWithoutAMedal()
    {
        var start = GimbalinaChallenge.Start;
        var run = Start();
        run.Step(.1, start, GimbalinaChallenge.Gates[^1], LocomotionMode.Bicycle, Level);
        Assert.Equal(ParkRunState.MissedGate, run.State);
        run = Start();
        run.Step(.1, start, start + 14 * Vector3d.UnitY, LocomotionMode.Bicycle, Level);
        Assert.Equal(ParkRunState.LeftCourse, run.State);
        run = Start();
        run.Step(.1, start, start + East, LocomotionMode.Car, Level);
        Assert.Equal(ParkRunState.LostPartner, run.State);
        run = Start();
        run.Step(.1, start, start + East, LocomotionMode.Bicycle, Level with { HasFledToSafety = true });
        Assert.Equal(ParkRunState.LostPartner, run.State);
        Assert.Equal(ParkRunMedal.None, run.Medal);
    }
}
