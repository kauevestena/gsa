using Gsa.Core.Geometry;

namespace Gsa.Core.Traffic;

public static class OldCenterTrafficNetwork
{
    public static Vector3d CheckpointEnu { get; } = new(500360, 7400116, 100);
    public static Vector3d MusicBarEnu { get; } = new(500311, 7400126, 100);
    public const double BarSpawnRadiusMetres = 85;

    private const double AvenueClearance = 7.75;

    public static bool OverlapsAvenueGrid(double east, double north, double width, double depth)
    {
        var minEast = east - width / 2;
        var maxEast = east + width / 2;
        var minNorth = north - depth / 2;
        var maxNorth = north + depth / 2;
        if (minEast >= 500480 + AvenueClearance || maxEast <= 500120 - AvenueClearance ||
            minNorth >= 7400356 + AvenueClearance || maxNorth <= 7400116 - AvenueClearance)
            return false;
        for (var e = 500120; e <= 500480; e += 120)
            if (minEast < e + AvenueClearance && maxEast > e - AvenueClearance)
                return true;
        for (var n = 7400116; n <= 7400356; n += 120)
            if (minNorth < n + AvenueClearance && maxNorth > n - AvenueClearance)
                return true;
        return false;
    }

    // Two directions on the paved avenue perimeter. The 2.4 m offset keeps each
    // direction on its right side of the yellow line, including across junctions.
    public static RoadNetworkGraph Create()
    {
        var tutorial = RoadNetworkGraph.CreateTutorialBlockGraph();
        var segments = tutorial.PedestrianSegments.ToList();
        var pedestrianNodeIds = segments.SelectMany(segment => new[] { segment.FromNodeId, segment.ToNodeId })
            .ToHashSet(StringComparer.Ordinal);
        var nodes = tutorial.Nodes.Values.Where(node => pedestrianNodeIds.Contains(node.Id)).ToList();

        Vector3d[] centers =
        [
            new(500120, 7400116, 100), new(500240, 7400116, 100),
            CheckpointEnu, new(500480, 7400116, 100),
            new(500480, 7400236, 100), new(500480, 7400356, 100),
            new(500360, 7400356, 100), new(500240, 7400356, 100),
            new(500120, 7400356, 100), new(500120, 7400236, 100),
        ];
        AddDirection("avenue_eastbound", centers);
        AddDirection("avenue_westbound", centers.Reverse().ToArray());
        return new RoadNetworkGraph(nodes, segments, tutorial.ExclusionZones);

        void AddDirection(string id, Vector3d[] route)
        {
            var lane = new RoadLaneNode[route.Length];
            for (var i = 0; i < route.Length; i++)
            {
                var previous = route[(i + route.Length - 1) % route.Length];
                var next = route[(i + 1) % route.Length];
                var incoming = RightOf((route[i] - previous).Normalized());
                var outgoing = RightOf((next - route[i]).Normalized());
                var offset = incoming == outgoing ? incoming : incoming + outgoing;
                lane[i] = new RoadLaneNode(id + "_node_" + i, route[i] + offset * 2.4, true);
                nodes.Add(lane[i]);
            }

            for (var i = 0; i < lane.Length; i++)
            {
                var next = (i + 1) % lane.Length;
                var from = lane[i];
                var to = lane[next];
                var group = from.PositionEnu.X == to.PositionEnu.X ? SignalGroup.NorthSouthRoad : SignalGroup.EastWestRoad;
                segments.Add(new RoadLaneSegment(id + "_" + i, from.Id, to.Id,
                    from.PositionEnu, to.PositionEnu, true, false, group, 10.5, [id + "_" + next]));
            }
        }

        static Vector3d RightOf(Vector3d direction) => new(direction.Y, -direction.X, 0);
    }
}
