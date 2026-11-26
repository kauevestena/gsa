using Gsa.Core.Geometry;
using Gsa.Core.Mobility;

namespace Gsa.Missions.Activities;

public enum ParkRunState { Ready, Riding, Finished, MissedGate, LeftCourse, LostPartner, Unsteady }
public enum ParkRunMedal { None, Bronze, Silver, Gold }

/// <summary>The timed, eastbound 200 m promenade. Gates are crossed by the rider's swept path.</summary>
public sealed class GimbalinaChallenge
{
    public const double HeadToleranceRadians = 5 * Math.PI / 180;
    public const double RequiredSteadyFraction = .95;
    public const double GateHalfWidth = 2.35;
    public static readonly Vector3d Start = new(500288, 7400350, 100);
    public static IReadOnlyList<Vector3d> Gates { get; } = Array.AsReadOnly(new[]
    {
        Start,
        Start + new Vector3d(46, -4.5, 0),
        Start + new Vector3d(97, 1, 0),
        Start + new Vector3d(149, -4.5, 0),
        Start + new Vector3d(200, -5, 0),
    });
    public static Vector3d Grid => Start - Vector3d.UnitX * 7;
    public static bool OverlapsCourse(double east, double north, double width, double depth) =>
        east + width / 2 > Grid.X - 8 && east - width / 2 < Gates[^1].X + 8 &&
        north + depth / 2 > Start.Y - 21 && north - depth / 2 < Start.Y + 5;

    public static bool IsEligible(LocomotionMode mode) =>
        mode is LocomotionMode.Handcart or LocomotionMode.Bicycle;

    private int _samples;
    private int _steadySamples;

    public ParkRunState State { get; private set; }
    public bool IsOver => State is not (ParkRunState.Ready or ParkRunState.Riding);
    public LocomotionMode Platform { get; private set; }
    public int NextGate { get; private set; }
    public double Seconds { get; private set; }
    public double SteadyFraction => _samples == 0 ? 1 : _steadySamples / (double)_samples;
    public bool HeadIsLevel { get; private set; } = true;
    public ParkRunMedal Medal { get; private set; }

    public static double GoldSeconds(LocomotionMode mode) =>
        Math.Ceiling(200 / PlatformProfile.ForMode(mode).MaxSpeedMetresPerSecond * 1.15);
    public static double SilverSeconds(LocomotionMode mode) =>
        Math.Ceiling(200 / PlatformProfile.ForMode(mode).MaxSpeedMetresPerSecond * 1.40);

    public void Step(double dt, Vector3d from, Vector3d to, LocomotionMode mode, GimbalinaStepResult head)
    {
        if (IsOver) return;
        var gate = Gates[NextGate];
        var crosses = from.X < gate.X && to.X >= gate.X;
        var fraction = crosses ? (gate.X - from.X) / (to.X - from.X) : 0;
        var inGate = crosses && Math.Abs(from.Y + (to.Y - from.Y) * fraction - gate.Y) <= GateHalfWidth;
        if (State == ParkRunState.Ready)
        {
            if (!inGate || !IsEligible(mode) || head.HasFledToSafety) return;
            Platform = mode;
            State = ParkRunState.Riding;
            NextGate++;
            dt *= 1 - fraction;
        }
        else
        {
            if (mode != Platform || head.HasFledToSafety) { State = ParkRunState.LostPartner; return; }
            if (Math.Abs(to.Y - Start.Y) > 13 || to.X < Grid.X - 5)
            { State = ParkRunState.LeftCourse; return; }
            if (crosses && !inGate) { State = ParkRunState.MissedGate; return; }
            if (inGate)
            {
                NextGate++;
                if (NextGate == Gates.Count) dt *= fraction;
            }
        }

        Seconds += dt;
        HeadIsLevel = Math.Abs(head.ResidualPitchRadians) <= HeadToleranceRadians &&
                      Math.Abs(head.ResidualRollRadians) <= HeadToleranceRadians;
        if (dt > 0)
        {
            _samples++;
            if (HeadIsLevel) _steadySamples++;
        }
        if (NextGate < Gates.Count) return;
        if (SteadyFraction < RequiredSteadyFraction) { State = ParkRunState.Unsteady; return; }

        State = ParkRunState.Finished;
        Medal = Seconds <= GoldSeconds(mode) ? ParkRunMedal.Gold :
                Seconds <= SilverSeconds(mode) ? ParkRunMedal.Silver : ParkRunMedal.Bronze;
    }
}
