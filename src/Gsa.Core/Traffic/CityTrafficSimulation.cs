using Gsa.Core.Geometry;

namespace Gsa.Core.Traffic;

public readonly record struct CityTrafficConditions(
    Vector3d PlayerPositionEnu, bool IsNight, bool IsWeekend, bool ProtectFromErraticDrivers,
    double CruiseSpeedMultiplier = 1);

public readonly record struct TrafficVehicleContact(
    string AgentId, Vector3d SafePlayerPositionEnu, double ClosingSpeedMetresPerSecond);

public sealed partial class TrafficBenchSimulator
{
    public const double ErraticWarningSeconds = 2.5;
    public const double CityBlitzRadiusMetres = 10.5;

    private readonly List<AgentState> _blitzRemovalScratch = [];
    private int _vacantVehicleSlots;
    private int _vacantPedestrianSlots;

    public int BlitzRemovals { get; private set; }

    private void UpdateCityDriver(AgentState agent, double dt, CityTrafficConditions conditions)
    {
        if (conditions.ProtectFromErraticDrivers)
        {
            if (agent.ErraticAgeSeconds.HasValue)
            {
                var lane = _graph.Segments[agent.SegmentId];
                agent.PositionEnu = lane.EvaluateEnu(agent.DistanceAlongSegment);
                agent.HeadingRadians = lane.HeadingRadians;
                agent.Speed = Math.Min(agent.Speed, lane.SpeedLimitMetresPerSecond * agent.NominalSpeedScale * conditions.CruiseSpeedMultiplier);
                agent.ErraticAgeSeconds = null;
                agent.IgnoreSignalThisApproach = null;
            }
            agent.CitySpawnClassified = true;
            return;
        }

        if (!agent.CitySpawnClassified)
        {
            agent.CitySpawnClassified = true;
            var nearBar = conditions.IsWeekend &&
                RoadNetworkGraph.DistanceXy(agent.PositionEnu, OldCenterTrafficNetwork.MusicBarEnu) <= OldCenterTrafficNetwork.BarSpawnRadiusMetres;
            var probability = ErraticDriverSimulator.ComputeSpawnProbability(new(conditions.IsNight, nearBar, false, false));
            if (_hazardRng.NextUniformOpenUnit() < probability)
            {
                agent.ErraticAgeSeconds = 0;
                agent.ErraticPeriodSeconds = 2 + 3 * _hazardRng.NextUniformOpenUnit();
            }
        }

        if (agent.ErraticAgeSeconds.HasValue)
            agent.ErraticAgeSeconds += dt;
    }

    private static ErraticDriverSample GetErraticMotion(AgentState agent)
    {
        var age = agent.ErraticAgeSeconds!.Value - ErraticWarningSeconds;
        var speedDelta = .30 * Math.Sin(age * 2 * Math.PI / agent.ErraticPeriodSeconds);
        return ErraticDriverSimulator.EvaluateDriverStep(agent.Id, age, agent.ErraticPeriodSeconds, speedDelta);
    }

    private static bool IsOutsideCityCamera(Vector3d position, Vector3d? camera, Vector3d? forward)
    {
        if (camera is not { } origin || forward is not { } direction)
            return IsOffCamera(position, camera, forward);

        // Keep the entire forward half-plane eligible for rendering. A far-off
        // vehicle can still be visible under the city's perspective camera.
        var offset = position - origin;
        return offset.X * direction.X + offset.Y * direction.Y < 0;
    }

    // Sweep the player against each moving vehicle footprint in relative coordinates.
    // The expanded footprint uses the player's horizontal collision radius.
    public TrafficVehicleContact? FindVehicleContact(Vector3d from, Vector3d to, double playerRadius, double dt)
    {
        TrafficVehicleContact? closest = null;
        var earliest = double.PositiveInfinity;
        var halfLength = VehicleLengthMetres * .5 + playerRadius;
        var halfWidth = 1.05 + playerRadius;
        var broadRadius = Math.Max(halfLength, halfWidth) * 1.5;
        foreach (var agent in _agents)
        {
            if (!agent.IsVehicle || Math.Abs(to.Z - agent.PositionEnu.Z) > 2.2)
                continue;

            var relativeStart = from - agent.PreviousPositionEnu;
            var relativeEnd = to - agent.PositionEnu;
            if (Math.Min(relativeStart.X, relativeEnd.X) > broadRadius || Math.Max(relativeStart.X, relativeEnd.X) < -broadRadius ||
                Math.Min(relativeStart.Y, relativeEnd.Y) > broadRadius || Math.Max(relativeStart.Y, relativeEnd.Y) < -broadRadius)
                continue;

            var (sin, cos) = Math.SinCos(agent.HeadingRadians);
            var forward = new Vector3d(cos, sin, 0);
            var left = new Vector3d(-sin, cos, 0);
            var x = Vector3d.Dot(relativeStart, forward);
            var y = Vector3d.Dot(relativeStart, left);
            var dx = Vector3d.Dot(relativeEnd, forward) - x;
            var dy = Vector3d.Dot(relativeEnd, left) - y;
            var startedInside = Math.Abs(x) < halfLength && Math.Abs(y) < halfWidth;
            var enter = 0.0;
            var exit = 1.0;
            var normal = Vector3d.Zero;
            if (!ClipAxis(x, dx, halfLength, forward, ref enter, ref exit, ref normal) ||
                !ClipAxis(y, dy, halfWidth, left, ref enter, ref exit, ref normal) || enter >= earliest)
                continue;

            var relativeContact = relativeStart + (relativeEnd - relativeStart) * enter;
            if (startedInside || normal == Vector3d.Zero)
            {
                if (Math.Abs(x + dx) > halfLength || Math.Abs(y + dy) > halfWidth)
                    continue; // A player already overlapping can move out.
                var penetrationX = halfLength - Math.Abs(x);
                var penetrationY = halfWidth - Math.Abs(y);
                normal = penetrationX < penetrationY ? forward * (x < 0 ? -1 : 1) : left * (y < 0 ? -1 : 1);
                relativeContact += normal * Math.Min(penetrationX, penetrationY);
            }

            var safe = agent.PositionEnu + relativeContact + normal * .025;
            var closingSpeed = Math.Max(0, Vector3d.Dot(relativeStart - relativeEnd, normal) / dt);
            earliest = enter;
            closest = new TrafficVehicleContact(agent.Id, new Vector3d(safe.X, safe.Y, to.Z), closingSpeed);
        }
        return closest;
    }

    private static bool ClipAxis(double start, double motion, double extent, Vector3d axis,
        ref double enter, ref double exit, ref Vector3d normal)
    {
        if (Math.Abs(motion) < 1e-10) return Math.Abs(start) <= extent;
        var a = (-extent - start) / motion;
        var b = (extent - start) / motion;
        var near = Math.Min(a, b);
        if (near > enter)
        {
            enter = near;
            normal = axis * (motion > 0 ? -1 : 1);
        }
        exit = Math.Min(exit, Math.Max(a, b));
        return enter <= exit;
    }
}
