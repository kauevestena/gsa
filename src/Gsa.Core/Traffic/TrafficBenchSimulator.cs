using Gsa.Core.Geometry;
using Gsa.Core.Randomness;

namespace Gsa.Core.Traffic;

public readonly record struct TrafficVisualAgent(
    string Id,
    bool IsVehicle,
    Vector3d PositionEnu,
    double HeadingRadians,
    double SpeedMetresPerSecond);

public sealed record TrafficAgentSnapshot(
    string Id,
    bool IsVehicle,
    string SegmentId,
    double DistanceAlongSegmentMetres,
    Vector3d PositionEnu,
    double HeadingRadians,
    double SpeedMetresPerSecond,
    double StalledSeconds,
    bool OnCrosswalk,
    bool WaitingAtSignal);

public sealed record TrafficStepSummary(
    long Tick,
    double ElapsedSeconds,
    int ActiveVehicles,
    int ActivePedestrians,
    int VehiclesStoppedAtSignal,
    int PedestriansCrossing,
    int DeadlockRemovals,
    double MinOfficeExitClearanceMetres,
    double MinVehicleCenterSpacingMetres,
    bool PlayerBlocked);

/// <summary>Deterministic 60 Hz bench traffic simulator with 50 cars, 30 pedestrians, signals, and 20 s deadlock eviction.</summary>
public sealed class TrafficBenchSimulator
{
    public const int TargetVehicleCount = 50;
    public const int TargetPedestrianCount = 30;
    public const double FixedDtSeconds = 1.0 / 60.0;
    public const double SignalCycleSeconds = 24.0;
    public const double DeadlockThresholdSeconds = 20.0;
    public const double VehicleLengthMetres = 4.2;
    public const double MinVehicleBumperGapMetres = 2.0;

    private sealed class AgentState
    {
        public required string Id { get; init; }
        public required bool IsVehicle { get; init; }
        public required string SegmentId { get; set; }
        public required double DistanceAlongSegment { get; set; }
        public required double Speed { get; set; }
        public required double NominalSpeedScale { get; init; }
        public required Vector3d PositionEnu { get; set; }
        public required double HeadingRadians { get; set; }
        public double StalledSeconds { get; set; }
        public bool WaitingAtSignal { get; set; }
        public bool ForcedStall { get; set; }
    }

    private readonly RoadNetworkGraph _graph;
    private readonly GaussianRandom _rng;
    private readonly List<AgentState> _agents = [];
    private readonly List<AgentState> _deadlockRemovalScratch = [];
    private readonly List<string> _viableSegmentScratch = [];
    private readonly bool _respawnAfterDeadlockRemoval;
    private long _nextSpawnSerial;

    public long Tick { get; private set; }
    public double ElapsedSeconds => Tick * FixedDtSeconds;
    public int DeadlockRemovals { get; private set; }
    public double MinOfficeExitClearanceMetres { get; private set; } = double.PositiveInfinity;
    public bool AutoRespawnOnDeadlockRemoval => _respawnAfterDeadlockRemoval;

    public TrafficBenchSimulator(
        RoadNetworkGraph? graph = null,
        ulong seed = 20261114UL,
        bool respawnAfterDeadlockRemoval = true)
    {
        _graph = graph ?? RoadNetworkGraph.CreateTutorialBlockGraph();
        _rng = new GaussianRandom(seed);
        _respawnAfterDeadlockRemoval = respawnAfterDeadlockRemoval;
        SeedInitialAgents();
    }

    public RoadNetworkGraph Graph => _graph;

    public SignalAspect GetSignalAspect(SignalGroup group)
    {
        if (group == SignalGroup.AlwaysGreen)
            return SignalAspect.Green;

        var phase = ElapsedSeconds % SignalCycleSeconds;
        return group switch
        {
            SignalGroup.EastWestRoad => phase < 10.0 ? SignalAspect.Green
                : phase < 11.2 ? SignalAspect.Amber
                : SignalAspect.Red,
            SignalGroup.EastWestCrosswalk => phase < 10.0 ? SignalAspect.Green : SignalAspect.Red,
            SignalGroup.NorthSouthRoad => phase is >= 12.0 and < 22.0 ? SignalAspect.Green
                : phase is >= 22.0 and < 23.2 ? SignalAspect.Amber
                : SignalAspect.Red,
            SignalGroup.NorthSouthCrosswalk => phase is >= 12.0 and < 22.0 ? SignalAspect.Green : SignalAspect.Red,
            _ => SignalAspect.Red,
        };
    }

    public void CopyVisualAgentsTo(List<TrafficVisualAgent> destination)
    {
        destination.Clear();
        for (var i = 0; i < _agents.Count; i++)
        {
            var a = _agents[i];
            destination.Add(new TrafficVisualAgent(a.Id, a.IsVehicle, a.PositionEnu, a.HeadingRadians, a.Speed));
        }
    }

    public IReadOnlyList<TrafficAgentSnapshot> GetSnapshots() =>
        _agents.Select(a => new TrafficAgentSnapshot(
            a.Id,
            a.IsVehicle,
            a.SegmentId,
            a.DistanceAlongSegment,
            a.PositionEnu,
            a.HeadingRadians,
            a.Speed,
            a.StalledSeconds,
            _graph.Segments[a.SegmentId].IsCrosswalk,
            a.WaitingAtSignal)).ToArray();

    public void ForceStallForTest(string agentId, double? setStalledSeconds = null)
    {
        var agent = _agents.FirstOrDefault(a => string.Equals(a.Id, agentId, StringComparison.Ordinal))
                    ?? throw new KeyNotFoundException($"Agent {agentId} not found.");
        agent.ForcedStall = true;
        agent.Speed = 0.0;
        if (setStalledSeconds.HasValue)
            agent.StalledSeconds = setStalledSeconds.Value;
    }

    public TrafficStepSummary Step(
        double dtSeconds = FixedDtSeconds,
        Vector3d? cameraPositionEnu = null,
        Vector3d? cameraForwardEnu = null,
        Vector3d? playerPositionEnu = null)
    {
        if (!double.IsFinite(dtSeconds) || dtSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(dtSeconds));

        Tick++;
        var stoppedAtSignal = 0;
        var pedCrossing = 0;

        foreach (var agent in _agents)
        {
            StepSingleAgent(agent, dtSeconds);
            if (agent.WaitingAtSignal)
                stoppedAtSignal++;
            if (!agent.IsVehicle && _graph.Segments[agent.SegmentId].IsCrosswalk)
                pedCrossing++;
        }

        HandleOffCameraDeadlocks(dtSeconds, cameraPositionEnu, cameraForwardEnu);
        UpdateOfficeClearance();

        var minSpacing = ComputeMinimumVehicleSpacing();
        var playerBlocked = IsPlayerBlocked(playerPositionEnu);

        var activeVehicles = 0;
        var activePeds = 0;
        for (var i = 0; i < _agents.Count; i++)
        {
            if (_agents[i].IsVehicle) activeVehicles++;
            else activePeds++;
        }

        return new TrafficStepSummary(
            Tick,
            ElapsedSeconds,
            activeVehicles,
            activePeds,
            stoppedAtSignal,
            pedCrossing,
            DeadlockRemovals,
            MinOfficeExitClearanceMetres,
            minSpacing,
            playerBlocked);
    }

    private void StepSingleAgent(AgentState agent, double dt)
    {
        if (agent.ForcedStall)
        {
            agent.Speed = 0.0;
            agent.WaitingAtSignal = false;
            return;
        }

        var segment = _graph.Segments[agent.SegmentId];
        var desiredSpeed = segment.SpeedLimitMetresPerSecond * agent.NominalSpeedScale;
        var waitingAtSignal = false;

        var remainingOnSegment = segment.LengthMetres - agent.DistanceAlongSegment;
        if (agent.IsVehicle)
        {
            if (segment.SignalGroup != SignalGroup.AlwaysGreen && remainingOnSegment < 5.0)
            {
                if (GetSignalAspect(segment.SignalGroup) != SignalAspect.Green)
                {
                    desiredSpeed = 0.0;
                    waitingAtSignal = true;
                }
            }

            if (HasPedestrianOnUpcomingCrosswalk(segment, remainingOnSegment))
            {
                desiredSpeed = Math.Min(desiredSpeed, remainingOnSegment <= 4.0 ? 0.0 : 2.5);
                if (desiredSpeed == 0.0)
                    waitingAtSignal = true;
            }

            var minGapAhead = FindGapToLeadingVehicle(agent, segment);
            var desiredCenterGap = VehicleLengthMetres + MinVehicleBumperGapMetres;
            if (minGapAhead < desiredCenterGap * 1.6)
            {
                var factor = Math.Clamp((minGapAhead - desiredCenterGap) / desiredCenterGap, 0.0, 1.0);
                desiredSpeed = Math.Min(desiredSpeed, desiredSpeed * factor);
            }
        }
        else
        {
            var nextSeg = _graph.Segments[segment.NextSegmentIds[0]];
            if (nextSeg.IsCrosswalk && remainingOnSegment < 1.2 && GetSignalAspect(nextSeg.SignalGroup) != SignalAspect.Green)
            {
                desiredSpeed = 0.0;
                waitingAtSignal = true;
            }
        }

        agent.WaitingAtSignal = waitingAtSignal;
        var maxAccel = agent.IsVehicle ? 3.0 : 2.5;
        var maxDecel = agent.IsVehicle ? 6.5 : 4.0;
        if (agent.Speed < desiredSpeed)
            agent.Speed = Math.Min(desiredSpeed, agent.Speed + maxAccel * dt);
        else
            agent.Speed = Math.Max(desiredSpeed, agent.Speed - maxDecel * dt);

        var nextDistance = agent.DistanceAlongSegment + agent.Speed * dt;
        while (nextDistance >= segment.LengthMetres)
        {
            var overflow = nextDistance - segment.LengthMetres;
            var nextSegId = ChooseNextSegment(segment, agent);
            if (!CanEnterSegmentSafely(agent, nextSegId))
            {
                nextDistance = segment.LengthMetres - 0.05;
                agent.Speed = 0.0;
                break;
            }
            agent.SegmentId = nextSegId;
            segment = _graph.Segments[nextSegId];
            nextDistance = overflow;
        }

        agent.DistanceAlongSegment = Math.Max(0.0, nextDistance);
        agent.PositionEnu = segment.EvaluateEnu(agent.DistanceAlongSegment);
        agent.HeadingRadians = segment.HeadingRadians;
    }

    private string ChooseNextSegment(RoadLaneSegment current, AgentState agent)
    {
        var nextIds = current.NextSegmentIds;
        if (nextIds.Count == 1)
            return nextIds[0];

        _viableSegmentScratch.Clear();
        for (var i = 0; i < nextIds.Count; i++)
        {
            var nextId = nextIds[i];
            var group = _graph.Segments[nextId].SignalGroup;
            if (group == SignalGroup.AlwaysGreen || GetSignalAspect(group) == SignalAspect.Green)
                _viableSegmentScratch.Add(nextId);
        }

        if (_viableSegmentScratch.Count > 0)
        {
            var idx = Math.Clamp((int)(_rng.NextUniformOpenUnit() * _viableSegmentScratch.Count), 0, _viableSegmentScratch.Count - 1);
            return _viableSegmentScratch[idx];
        }

        var fallbackIdx = Math.Clamp((int)(_rng.NextUniformOpenUnit() * nextIds.Count), 0, nextIds.Count - 1);
        return nextIds[fallbackIdx];
    }

    private bool CanEnterSegmentSafely(AgentState agent, string nextSegId)
    {
        if (!agent.IsVehicle)
            return true;

        var entrySpacing = VehicleLengthMetres + MinVehicleBumperGapMetres;
        foreach (var other in _agents)
        {
            if (ReferenceEquals(other, agent) || !other.IsVehicle)
                continue;
            if (string.Equals(other.SegmentId, nextSegId, StringComparison.Ordinal) && other.DistanceAlongSegment < entrySpacing)
                return false;
        }
        return true;
    }

    private double FindGapToLeadingVehicle(AgentState agent, RoadLaneSegment segment)
    {
        var minGap = double.PositiveInfinity;
        foreach (var other in _agents)
        {
            if (ReferenceEquals(other, agent) || !other.IsVehicle)
                continue;

            if (string.Equals(other.SegmentId, agent.SegmentId, StringComparison.Ordinal))
            {
                var diff = other.DistanceAlongSegment - agent.DistanceAlongSegment;
                if (diff > 0 && diff < minGap)
                    minGap = diff;
            }
            else if (segment.NextSegmentIds.Contains(other.SegmentId, StringComparer.Ordinal))
            {
                var diff = (segment.LengthMetres - agent.DistanceAlongSegment) + other.DistanceAlongSegment;
                if (diff > 0 && diff < minGap)
                    minGap = diff;
            }
        }
        return minGap;
    }

    private bool HasPedestrianOnUpcomingCrosswalk(RoadLaneSegment vehicleSegment, double remainingOnSegment)
    {
        if (remainingOnSegment > 10.0)
            return false;
        var intersectionEnd = vehicleSegment.EndEnu;
        foreach (var other in _agents)
        {
            if (other.IsVehicle)
                continue;
            if (!_graph.Segments[other.SegmentId].IsCrosswalk)
                continue;
            if (Math.Abs(other.PositionEnu.X - intersectionEnd.X) <= 4.0 &&
                Math.Abs(other.PositionEnu.Y - intersectionEnd.Y) <= 2.2)
                return true;
        }
        return false;
    }

    private void HandleOffCameraDeadlocks(double dt, Vector3d? cameraPos, Vector3d? cameraForward)
    {
        _deadlockRemovalScratch.Clear();
        for (var i = 0; i < _agents.Count; i++)
        {
            var agent = _agents[i];
            if (agent.Speed < 0.05 && (!agent.WaitingAtSignal || agent.ForcedStall))
                agent.StalledSeconds += dt;
            else
                agent.StalledSeconds = 0.0;

            if (agent.StalledSeconds + 1e-9 >= DeadlockThresholdSeconds && IsOffCamera(agent.PositionEnu, cameraPos, cameraForward))
                _deadlockRemovalScratch.Add(agent);
        }

        for (var i = 0; i < _deadlockRemovalScratch.Count; i++)
        {
            var dead = _deadlockRemovalScratch[i];
            _agents.Remove(dead);
            DeadlockRemovals++;
            if (_respawnAfterDeadlockRemoval)
                RespawnCleanly(dead.IsVehicle);
        }
    }

    public static bool IsOffCamera(Vector3d pointEnu, Vector3d? cameraPos, Vector3d? cameraForward)
    {
        if (!cameraPos.HasValue)
            return true;

        var offset = pointEnu - cameraPos.Value;
        var distance = Math.Sqrt(offset.X * offset.X + offset.Y * offset.Y);
        if (distance > 80.0)
            return true;

        if (!cameraForward.HasValue || distance <= 1e-6)
            return distance > 20.0;

        var forwardNorm = new Vector3d(cameraForward.Value.X, cameraForward.Value.Y, 0.0);
        if (forwardNorm.Length <= 1e-6)
            return true;
        var dirXy = new Vector3d(offset.X / distance, offset.Y / distance, 0.0);
        return Vector3d.Dot(forwardNorm.Normalized(), dirXy) < 0.25;
    }

    private void RespawnCleanly(bool isVehicle)
    {
        var pool = isVehicle ? _graph.VehicleSegments : _graph.PedestrianSegments;
        foreach (var seg in pool)
        {
            if (seg.LengthMetres < 12.0)
                continue;
            var candidateDist = seg.LengthMetres * 0.5;
            var pos = seg.EvaluateEnu(candidateDist);
            if (IsSpacingClear(pos, isVehicle ? 8.0 : 1.5, isVehicle))
            {
                _nextSpawnSerial++;
                _agents.Add(new AgentState
                {
                    Id = $"{(isVehicle ? "car" : "ped")}_respawn_{_nextSpawnSerial}",
                    IsVehicle = isVehicle,
                    SegmentId = seg.Id,
                    DistanceAlongSegment = candidateDist,
                    Speed = isVehicle ? seg.SpeedLimitMetresPerSecond * 0.5 : 1.3,
                    NominalSpeedScale = 0.9,
                    PositionEnu = pos,
                    HeadingRadians = seg.HeadingRadians,
                });
                return;
            }
        }
    }

    private bool IsSpacingClear(Vector3d pos, double minRadius, bool vehiclesOnly)
    {
        foreach (var other in _agents)
        {
            if (vehiclesOnly && !other.IsVehicle)
                continue;
            if (RoadNetworkGraph.DistanceXy(other.PositionEnu, pos) < minRadius)
                return false;
        }
        return true;
    }

    private void SeedInitialAgents()
    {
        var vehicleSlots = new List<(RoadLaneSegment Seg, double Dist, Vector3d Pos)>();
        foreach (var seg in _graph.VehicleSegments.Where(s => s.LengthMetres >= 12.0))
        {
            for (var d = 5.0; d <= seg.LengthMetres - 5.0; d += 7.5)
            {
                var pos = seg.EvaluateEnu(d);
                if (vehicleSlots.All(existing => RoadNetworkGraph.DistanceXy(existing.Pos, pos) >= 7.0))
                    vehicleSlots.Add((seg, d, pos));
            }
        }

        if (vehicleSlots.Count < TargetVehicleCount)
            throw new InvalidOperationException($"Insufficient spaced vehicle slots ({vehicleSlots.Count}) for {TargetVehicleCount} vehicles.");

        for (var i = 0; i < TargetVehicleCount; i++)
        {
            var slotIndex = (i * vehicleSlots.Count) / TargetVehicleCount;
            var (seg, dist, pos) = vehicleSlots[slotIndex];
            _agents.Add(new AgentState
            {
                Id = $"car_{i:00}",
                IsVehicle = true,
                SegmentId = seg.Id,
                DistanceAlongSegment = dist,
                Speed = seg.SpeedLimitMetresPerSecond * 0.75,
                NominalSpeedScale = 0.85 + 0.15 * _rng.NextUniformOpenUnit(),
                PositionEnu = pos,
                HeadingRadians = seg.HeadingRadians,
            });
        }

        var pedSlots = new List<(RoadLaneSegment Seg, double Dist, Vector3d Pos)>();
        foreach (var seg in _graph.PedestrianSegments)
        {
            if (seg.IsCrosswalk || seg.LengthMetres < 8.0)
                continue;
            for (var d = 2.0; d <= seg.LengthMetres - 2.0; d += 5.5)
                pedSlots.Add((seg, d, seg.EvaluateEnu(d)));
        }

        for (var i = 0; i < TargetPedestrianCount; i++)
        {
            var (seg, dist, pos) = pedSlots[(i * pedSlots.Count) / TargetPedestrianCount];
            _agents.Add(new AgentState
            {
                Id = $"ped_{i:00}",
                IsVehicle = false,
                SegmentId = seg.Id,
                DistanceAlongSegment = dist,
                Speed = 1.25,
                NominalSpeedScale = 0.9 + 0.15 * _rng.NextUniformOpenUnit(),
                PositionEnu = pos,
                HeadingRadians = seg.HeadingRadians,
            });
        }
    }

    private void UpdateOfficeClearance()
    {
        var office = _graph.ExclusionZones.FirstOrDefault(z => z.Id == "office_entry");
        if (office is null)
            return;

        foreach (var agent in _agents)
        {
            var d = RoadNetworkGraph.DistanceXy(agent.PositionEnu, office.CenterEnu);
            if (d < MinOfficeExitClearanceMetres)
                MinOfficeExitClearanceMetres = d;
        }
    }

    private double ComputeMinimumVehicleSpacing()
    {
        var minSq = double.PositiveInfinity;
        for (var i = 0; i < _agents.Count; i++)
        {
            var a = _agents[i];
            if (!a.IsVehicle) continue;
            for (var j = i + 1; j < _agents.Count; j++)
            {
                var b = _agents[j];
                if (!b.IsVehicle) continue;
                var dx = a.PositionEnu.X - b.PositionEnu.X;
                var dy = a.PositionEnu.Y - b.PositionEnu.Y;
                var dSq = dx * dx + dy * dy;
                if (dSq < minSq) minSq = dSq;
            }
        }
        return double.IsPositiveInfinity(minSq) ? minSq : Math.Sqrt(minSq);
    }

    private bool IsPlayerBlocked(Vector3d? playerPos)
    {
        if (!playerPos.HasValue)
            return false;
        foreach (var agent in _agents)
        {
            if (agent.StalledSeconds >= DeadlockThresholdSeconds &&
                RoadNetworkGraph.DistanceXy(agent.PositionEnu, playerPos.Value) < 4.0)
            {
                return true;
            }
        }
        return false;
    }
}
