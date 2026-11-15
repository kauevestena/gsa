using Gsa.Core.Geometry;

namespace Gsa.Core.Traffic;

public enum AbsurdUrbanEventKind
{
    TrioEletrico = 0,
    StreetMarathon = 1,
    PigeonFlock = 2,
}

public sealed record AbsurdUrbanEventState(
    string EventId,
    AbsurdUrbanEventKind Kind,
    bool Active,
    bool AdvanceWarningIssued,
    double WarningLeadTimeSeconds,
    double DurationSeconds,
    double ElapsedSeconds,
    IReadOnlyList<string> BlockedSegmentIds,
    IReadOnlyList<string> SignpostedDetourSegmentIds,
    bool EnvironmentalSpectacleOnly,
    bool CausesDamageOrThreat,
    bool CausesHostileStress,
    double PigeonObservationClutterRate,
    string? EndedReasonKey);

public sealed record AbsurdEventStartResult(
    bool Started,
    string ReasonKey,
    AbsurdUrbanEventState? EventState);

/// <summary>Coordinates absurd city events (trio eletrico, street marathon, pigeon flock) with advance warnings, verified graph detours, &lt;=2 min caps, and CalmPolicy enforcement (T-F18-03).</summary>
public sealed class AbsurdUrbanEventsCoordinator
{
    public const double MaxEventDurationSeconds = 120.0;
    public const double DefaultWarningLeadTimeSeconds = 15.0;
    public const double DefaultPigeonClutterRate = 0.035;

    private readonly Dictionary<string, AbsurdUrbanEventState> _events = new(StringComparer.Ordinal);
    private bool _calmCityEnabled;

    public bool CalmCityEnabled => _calmCityEnabled;
    public IReadOnlyDictionary<string, AbsurdUrbanEventState> Events => _events;

    public IReadOnlySet<string> GetCurrentlyBlockedSegmentIds() =>
        _events.Values
            .Where(e => e.Active)
            .SelectMany(e => e.BlockedSegmentIds)
            .ToHashSet(StringComparer.Ordinal);

    public static bool TryFindTransitableDetour(
        RoadNetworkGraph graph,
        IReadOnlySet<string> blockedSegmentIds,
        string startSegmentId,
        string goalSegmentId,
        out IReadOnlyList<string> detourSegmentIds)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(blockedSegmentIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(startSegmentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(goalSegmentId);

        detourSegmentIds = Array.Empty<string>();
        if (!graph.Segments.ContainsKey(startSegmentId) ||
            !graph.Segments.ContainsKey(goalSegmentId) ||
            blockedSegmentIds.Contains(startSegmentId) ||
            blockedSegmentIds.Contains(goalSegmentId))
        {
            return false;
        }

        if (string.Equals(startSegmentId, goalSegmentId, StringComparison.Ordinal))
        {
            detourSegmentIds = Array.AsReadOnly([startSegmentId]);
            return true;
        }

        var parent = new Dictionary<string, string?>(StringComparer.Ordinal) { [startSegmentId] = null };
        var queue = new Queue<string>();
        queue.Enqueue(startSegmentId);

        while (queue.Count > 0)
        {
            var currentId = queue.Dequeue();
            var seg = graph.Segments[currentId];
            foreach (var nextId in seg.NextSegmentIds)
            {
                if (blockedSegmentIds.Contains(nextId) || parent.ContainsKey(nextId))
                    continue;

                parent[nextId] = currentId;
                if (string.Equals(nextId, goalSegmentId, StringComparison.Ordinal))
                {
                    var path = new List<string>();
                    string? cursor = nextId;
                    while (cursor is not null)
                    {
                        path.Add(cursor);
                        cursor = parent[cursor];
                    }
                    path.Reverse();
                    detourSegmentIds = Array.AsReadOnly(path.ToArray());
                    return true;
                }

                queue.Enqueue(nextId);
            }
        }

        return false;
    }

    public AbsurdEventStartResult TryStartEvent(
        string eventId,
        AbsurdUrbanEventKind kind,
        RoadNetworkGraph graph,
        IReadOnlyList<string> candidateBlockedSegmentIds,
        string contractStartSegmentId,
        string contractGoalSegmentId,
        double requestedDurationSeconds = MaxEventDurationSeconds,
        bool isCalmContract = false,
        bool? calmCityOverride = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(candidateBlockedSegmentIds);
        if (!double.IsFinite(requestedDurationSeconds) || requestedDurationSeconds <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(requestedDurationSeconds));

        var effectiveCalm = isCalmContract || (calmCityOverride ?? _calmCityEnabled);
        var blocked = new HashSet<string>(GetCurrentlyBlockedSegmentIds(), StringComparer.Ordinal);
        foreach (var segId in candidateBlockedSegmentIds)
            blocked.Add(segId);

        if (!TryFindTransitableDetour(graph, blocked, contractStartSegmentId, contractGoalSegmentId, out var detour))
        {
            return new AbsurdEventStartResult(false, "event.rejected.seals_unique_contract_route", null);
        }

        var duration = Math.Min(MaxEventDurationSeconds, requestedDurationSeconds);
        var clutter = kind == AbsurdUrbanEventKind.PigeonFlock ? DefaultPigeonClutterRate : 0.0;
        var state = new AbsurdUrbanEventState(
            EventId: eventId,
            Kind: kind,
            Active: true,
            AdvanceWarningIssued: true,
            WarningLeadTimeSeconds: DefaultWarningLeadTimeSeconds,
            DurationSeconds: duration,
            ElapsedSeconds: 0.0,
            BlockedSegmentIds: Array.AsReadOnly(candidateBlockedSegmentIds.ToArray()),
            SignpostedDetourSegmentIds: detour,
            EnvironmentalSpectacleOnly: effectiveCalm,
            CausesDamageOrThreat: !effectiveCalm,
            CausesHostileStress: !effectiveCalm,
            PigeonObservationClutterRate: clutter,
            EndedReasonKey: null);

        _events[eventId] = state;
        return new AbsurdEventStartResult(true, "event.started", state);
    }

    public void ApplyCalmPolicy(bool calmCityEnabled)
    {
        _calmCityEnabled = calmCityEnabled;
        if (!calmCityEnabled)
            return;

        foreach (var id in _events.Keys.ToArray())
        {
            var current = _events[id];
            if (!current.Active)
                continue;

            _events[id] = current with
            {
                EnvironmentalSpectacleOnly = true,
                CausesDamageOrThreat = false,
                CausesHostileStress = false,
            };
        }
    }

    public void StepEvents(double dtSeconds)
    {
        if (!double.IsFinite(dtSeconds) || dtSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(dtSeconds));

        foreach (var id in _events.Keys.ToArray())
        {
            var current = _events[id];
            if (!current.Active)
                continue;

            var nextElapsed = current.ElapsedSeconds + dtSeconds;
            if (nextElapsed >= current.DurationSeconds)
            {
                _events[id] = current with
                {
                    Active = false,
                    ElapsedSeconds = current.DurationSeconds,
                    CausesDamageOrThreat = false,
                    CausesHostileStress = false,
                    EndedReasonKey = "event.ended.duration_completed",
                };
            }
            else
            {
                _events[id] = current with { ElapsedSeconds = nextElapsed };
            }
        }
    }

    /// <summary>Builds a multi-corridor test road graph featuring parallel bypass lanes and a single non-bypassable contract bottleneck segment.</summary>
    public static RoadNetworkGraph CreateDetourAndBottleneckTestGraph()
    {
        var nodes = new RoadLaneNode[]
        {
            new("n_start", new Vector3d(500000, 7400000, 100)),
            new("n_main_mid", new Vector3d(500100, 7400000, 100)),
            new("n_alt_mid", new Vector3d(500100, 7400080, 100)),
            new("n_merge", new Vector3d(500200, 7400000, 100)),
            new("n_goal", new Vector3d(500300, 7400000, 100)),
        };

        var segments = new RoadLaneSegment[]
        {
            new("seg_entry", "n_start", "n_main_mid", nodes[0].PositionEnu, nodes[1].PositionEnu, true, false, SignalGroup.AlwaysGreen, 12.0, ["seg_main", "seg_detour_in"]),
            new("seg_main", "n_main_mid", "n_merge", nodes[1].PositionEnu, nodes[3].PositionEnu, true, false, SignalGroup.AlwaysGreen, 12.0, ["seg_unique_bridge"]),
            new("seg_detour_in", "n_main_mid", "n_alt_mid", nodes[1].PositionEnu, nodes[2].PositionEnu, true, false, SignalGroup.AlwaysGreen, 10.0, ["seg_detour_out"]),
            new("seg_detour_out", "n_alt_mid", "n_merge", nodes[2].PositionEnu, nodes[3].PositionEnu, true, false, SignalGroup.AlwaysGreen, 10.0, ["seg_unique_bridge"]),
            new("seg_unique_bridge", "n_merge", "n_goal", nodes[3].PositionEnu, nodes[4].PositionEnu, true, false, SignalGroup.AlwaysGreen, 12.0, ["seg_loop_back"]),
            new("seg_loop_back", "n_goal", "n_start", nodes[4].PositionEnu, nodes[0].PositionEnu, true, false, SignalGroup.AlwaysGreen, 12.0, ["seg_entry"]),
        };

        return new RoadNetworkGraph(nodes, segments, []);
    }
}

