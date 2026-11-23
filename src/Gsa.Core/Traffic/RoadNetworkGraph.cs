using Gsa.Core.Geometry;

namespace Gsa.Core.Traffic;

public enum SignalGroup
{
    AlwaysGreen = 0,
    EastWestRoad = 1,
    NorthSouthRoad = 2,
    EastWestCrosswalk = 3,
    NorthSouthCrosswalk = 4,
}

public enum SignalAspect
{
    Green = 0,
    Amber = 1,
    Red = 2,
}

public sealed record RoadLaneNode(
    string Id,
    Vector3d PositionEnu,
    bool IsIntersectionEntry = false,
    SignalGroup SignalGroup = SignalGroup.AlwaysGreen);

public sealed record TrafficExclusionZone(
    string Id,
    Vector3d CenterEnu,
    double RadiusMetres);

public sealed class RoadLaneSegment
{
    public string Id { get; }
    public string FromNodeId { get; }
    public string ToNodeId { get; }
    public Vector3d StartEnu { get; }
    public Vector3d EndEnu { get; }
    public Vector3d TangentXy { get; }
    public double LengthMetres { get; }
    public double HeadingRadians { get; }
    public bool IsVehicleLane { get; }
    public bool IsCrosswalk { get; }
    public SignalGroup SignalGroup { get; }
    public double SpeedLimitMetresPerSecond { get; }
    public IReadOnlyList<string> NextSegmentIds { get; }

    public RoadLaneSegment(
        string id,
        string fromNodeId,
        string toNodeId,
        Vector3d startEnu,
        Vector3d endEnu,
        bool isVehicleLane,
        bool isCrosswalk,
        SignalGroup signalGroup,
        double speedLimitMetresPerSecond,
        IEnumerable<string> nextSegmentIds)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Segment ID is required.", nameof(id));
        if (speedLimitMetresPerSecond <= 0 || !double.IsFinite(speedLimitMetresPerSecond))
            throw new ArgumentOutOfRangeException(nameof(speedLimitMetresPerSecond));

        var delta = new Vector3d(endEnu.X - startEnu.X, endEnu.Y - startEnu.Y, endEnu.Z - startEnu.Z);
        var length = delta.Length;
        if (length <= 1e-6)
            throw new ArgumentException($"Segment {id} has zero length.", nameof(endEnu));

        Id = id;
        FromNodeId = fromNodeId;
        ToNodeId = toNodeId;
        StartEnu = startEnu;
        EndEnu = endEnu;
        LengthMetres = length;
        TangentXy = new Vector3d(delta.X / length, delta.Y / length, delta.Z / length);
        HeadingRadians = Math.Atan2(delta.Y, delta.X);
        IsVehicleLane = isVehicleLane;
        IsCrosswalk = isCrosswalk;
        SignalGroup = signalGroup;
        SpeedLimitMetresPerSecond = speedLimitMetresPerSecond;
        NextSegmentIds = Array.AsReadOnly(nextSegmentIds.ToArray());
        if (NextSegmentIds.Count == 0)
            throw new ArgumentException($"Segment {id} must connect to at least one successor segment.", nameof(nextSegmentIds));
    }

    public Vector3d EvaluateEnu(double distanceMetres)
    {
        var clamped = Math.Clamp(distanceMetres, 0.0, LengthMetres);
        var t = clamped / LengthMetres;
        return StartEnu + (EndEnu - StartEnu) * t;
    }
}

/// <summary>Signalized multi-corridor vehicle and sidewalk/crosswalk graph around the tutorial block.</summary>
public sealed class RoadNetworkGraph
{
    public IReadOnlyDictionary<string, RoadLaneNode> Nodes { get; }
    public IReadOnlyDictionary<string, RoadLaneSegment> Segments { get; }
    public IReadOnlyList<RoadLaneSegment> VehicleSegments { get; }
    public IReadOnlyList<RoadLaneSegment> PedestrianSegments { get; }
    public IReadOnlyList<TrafficExclusionZone> ExclusionZones { get; }

    public RoadNetworkGraph(
        IEnumerable<RoadLaneNode> nodes,
        IEnumerable<RoadLaneSegment> segments,
        IEnumerable<TrafficExclusionZone> exclusionZones)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(exclusionZones);

        Nodes = nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var segmentList = segments.ToArray();
        Segments = segmentList.ToDictionary(s => s.Id, StringComparer.Ordinal);
        VehicleSegments = Array.AsReadOnly(segmentList.Where(s => s.IsVehicleLane).ToArray());
        PedestrianSegments = Array.AsReadOnly(segmentList.Where(s => !s.IsVehicleLane).ToArray());
        ExclusionZones = Array.AsReadOnly(exclusionZones.ToArray());

        ValidateGraph();
    }

    public static double DistanceXy(Vector3d a, Vector3d b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public static double DistancePointToSegmentXy(Vector3d point, Vector3d start, Vector3d end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var lenSq = dx * dx + dy * dy;
        if (lenSq <= 1e-12)
            return DistanceXy(point, start);
        var t = Math.Clamp(((point.X - start.X) * dx + (point.Y - start.Y) * dy) / lenSq, 0.0, 1.0);
        return Math.Sqrt(Math.Pow(point.X - (start.X + t * dx), 2) + Math.Pow(point.Y - (start.Y + t * dy), 2));
    }

    private void ValidateGraph()
    {
        foreach (var seg in Segments.Values)
        {
            if (!Nodes.ContainsKey(seg.FromNodeId) || !Nodes.ContainsKey(seg.ToNodeId))
                throw new InvalidOperationException($"Segment {seg.Id} references unknown node.");
            foreach (var nextId in seg.NextSegmentIds)
            {
                if (!Segments.TryGetValue(nextId, out var next))
                    throw new InvalidOperationException($"Segment {seg.Id} references unknown successor {nextId}.");
                if (next.IsVehicleLane != seg.IsVehicleLane)
                    throw new InvalidOperationException($"Segment {seg.Id} crosses modality to {nextId}.");
                if (!string.Equals(seg.ToNodeId, next.FromNodeId, StringComparison.Ordinal))
                    throw new InvalidOperationException($"Discontinuous transition {seg.Id} -> {nextId}.");
            }
            foreach (var zone in ExclusionZones)
            {
                if (DistancePointToSegmentXy(zone.CenterEnu, seg.StartEnu, seg.EndEnu) < zone.RadiusMetres)
                    throw new InvalidOperationException($"Segment {seg.Id} intrudes into protected zone {zone.Id}.");
            }
        }
    }

    public static RoadNetworkGraph CreateTutorialBlockGraph()
    {
        // Two-way East/West corridors with >= 7.5 m lane spacing (ensuring every segment >= 8 m),
        // keeping clear of building_shell_01..20 (Y=7400005.4..7400015.4), glass_sample/water_sample/walk_obstacle (Y>=7400029.9), and office_entry (500232, 7400005).
        const double z = 100.0;
        const double xOutW = 500006.0;
        const double xC_W = 500108.0;
        const double xC_E = 500116.0;
        const double xE = 500208.0;
        const double xOutE = 500220.0;
        const double yS_EB = 7400019.0;
        const double yS_WB = 7400027.0;
        const double yN_EB = 7400043.0;
        const double yN_WB = 7400051.0;

        var nodes = new List<RoadLaneNode>();
        var segments = new List<RoadLaneSegment>();

        void AddNode(string id, double x, double y, bool intersection = false, SignalGroup group = SignalGroup.AlwaysGreen) =>
            nodes.Add(new RoadLaneNode(id, new Vector3d(x, y, z), intersection, group));

        void AddSeg(string id, string from, string to, bool vehicle, bool crosswalk, SignalGroup group, double speed, params string[] next)
        {
            var a = nodes.First(n => n.Id == from);
            var b = nodes.First(n => n.Id == to);
            segments.Add(new RoadLaneSegment(id, from, to, a.PositionEnu, b.PositionEnu, vehicle, crosswalk, group, speed, next));
        }

        // Vehicle corridor nodes (every segment >= 8.0 m)
        AddNode("v_sw_eb", xOutW, yS_EB);
        AddNode("v_sc_w_eb", xC_W, yS_EB, true, SignalGroup.EastWestRoad);
        AddNode("v_sc_e_eb", xC_E, yS_EB);
        AddNode("v_se_e_eb", xE, yS_EB);
        AddNode("v_se_out_eb", xOutE, yS_EB);

        AddNode("v_se_out_wb", xOutE, yS_WB);
        AddNode("v_sc_e_wb", xC_E, yS_WB, true, SignalGroup.EastWestRoad);
        AddNode("v_sc_w_wb", xC_W, yS_WB);
        AddNode("v_sw_wb", xOutW, yS_WB);

        AddNode("v_nw_eb", xOutW, yN_EB);
        AddNode("v_nc_w_eb", xC_W, yN_EB, true, SignalGroup.EastWestRoad);
        AddNode("v_nc_e_eb", xC_E, yN_EB);
        AddNode("v_ne_out_eb", xOutE, yN_EB);

        AddNode("v_ne_out_wb", xOutE, yN_WB);
        AddNode("v_ne_wb", xE, yN_WB);
        AddNode("v_nc_e_wb", xC_E, yN_WB, true, SignalGroup.EastWestRoad);
        AddNode("v_nc_w_wb", xC_W, yN_WB);
        AddNode("v_nw_wb", xOutW, yN_WB);

        // South EB -> East U-turn or Northbound Center/East
        AddSeg("v_s_eb_1", "v_sw_eb", "v_sc_w_eb", true, false, SignalGroup.EastWestRoad, 9.5, "v_s_eb_2", "v_c_nb_w");
        AddSeg("v_s_eb_2", "v_sc_w_eb", "v_sc_e_eb", true, false, SignalGroup.AlwaysGreen, 9.5, "v_s_eb_3");
        AddSeg("v_s_eb_3", "v_sc_e_eb", "v_se_e_eb", true, false, SignalGroup.AlwaysGreen, 9.5, "v_s_eb_4", "v_e_nb");
        AddSeg("v_s_eb_4", "v_se_e_eb", "v_se_out_eb", true, false, SignalGroup.AlwaysGreen, 8.5, "v_e_uturn_s");
        AddSeg("v_e_uturn_s", "v_se_out_eb", "v_se_out_wb", true, false, SignalGroup.AlwaysGreen, 7.5, "v_s_wb_1");

        // South WB -> West Outer NB or South U-turn
        AddSeg("v_s_wb_1", "v_se_out_wb", "v_sc_e_wb", true, false, SignalGroup.EastWestRoad, 9.5, "v_s_wb_2", "v_c_nb_e");
        AddSeg("v_s_wb_2", "v_sc_e_wb", "v_sc_w_wb", true, false, SignalGroup.AlwaysGreen, 9.5, "v_s_wb_3");
        AddSeg("v_s_wb_3", "v_sc_w_wb", "v_sw_wb", true, false, SignalGroup.AlwaysGreen, 9.5, "v_w_nb_outer", "v_w_uturn_s");
        AddSeg("v_w_uturn_s", "v_sw_wb", "v_sw_eb", true, false, SignalGroup.AlwaysGreen, 7.5, "v_s_eb_1");

        // West outer NB & SB
        AddSeg("v_w_nb_outer", "v_sw_wb", "v_nw_eb", true, false, SignalGroup.AlwaysGreen, 9.0, "v_n_eb_1");
        AddSeg("v_w_sb_outer", "v_nw_wb", "v_sw_eb", true, false, SignalGroup.AlwaysGreen, 9.0, "v_s_eb_1");

        // North EB -> East U-turn or Southbound East/Center
        AddSeg("v_n_eb_1", "v_nw_eb", "v_nc_w_eb", true, false, SignalGroup.EastWestRoad, 9.5, "v_n_eb_2", "v_c_sb_w");
        AddSeg("v_n_eb_2", "v_nc_w_eb", "v_nc_e_eb", true, false, SignalGroup.AlwaysGreen, 9.5, "v_n_eb_3", "v_c_sb_e");
        AddSeg("v_n_eb_3", "v_nc_e_eb", "v_ne_out_eb", true, false, SignalGroup.AlwaysGreen, 9.5, "v_e_uturn_n");
        AddSeg("v_e_uturn_n", "v_ne_out_eb", "v_ne_out_wb", true, false, SignalGroup.AlwaysGreen, 7.5, "v_n_wb_1", "v_e_sb_outer");

        // North WB -> West Outer SB or North U-turn
        AddSeg("v_n_wb_1", "v_ne_out_wb", "v_ne_wb", true, false, SignalGroup.AlwaysGreen, 9.5, "v_n_wb_2");
        AddSeg("v_n_wb_2", "v_ne_wb", "v_nc_e_wb", true, false, SignalGroup.EastWestRoad, 9.5, "v_n_wb_3", "v_c_sb_e_wb");
        AddSeg("v_n_wb_3", "v_nc_e_wb", "v_nc_w_wb", true, false, SignalGroup.AlwaysGreen, 9.5, "v_n_wb_4");
        AddSeg("v_n_wb_4", "v_nc_w_wb", "v_nw_wb", true, false, SignalGroup.AlwaysGreen, 9.5, "v_w_sb_outer", "v_w_uturn_n");
        AddSeg("v_w_uturn_n", "v_nw_wb", "v_nw_eb", true, false, SignalGroup.AlwaysGreen, 7.5, "v_n_eb_1");

        // North-South connectors (Center & East, >= 24 m each)
        AddSeg("v_c_nb_w", "v_sc_w_eb", "v_nc_w_eb", true, false, SignalGroup.NorthSouthRoad, 8.5, "v_n_eb_2");
        AddSeg("v_c_nb_e", "v_sc_e_wb", "v_nc_e_wb", true, false, SignalGroup.NorthSouthRoad, 8.5, "v_n_wb_3");
        AddSeg("v_c_sb_w", "v_nc_w_eb", "v_sc_w_wb", true, false, SignalGroup.NorthSouthRoad, 8.5, "v_s_wb_3");
        AddSeg("v_c_sb_e", "v_nc_e_eb", "v_sc_e_eb", true, false, SignalGroup.NorthSouthRoad, 8.5, "v_s_eb_3");
        AddSeg("v_c_sb_e_wb", "v_nc_e_wb", "v_sc_e_wb", true, false, SignalGroup.NorthSouthRoad, 8.5, "v_s_wb_2");
        AddSeg("v_e_nb", "v_se_e_eb", "v_ne_wb", true, false, SignalGroup.NorthSouthRoad, 8.5, "v_n_wb_2");
        AddSeg("v_e_sb_outer", "v_ne_out_wb", "v_se_out_wb", true, false, SignalGroup.AlwaysGreen, 9.0, "v_s_wb_1");

        // Pedestrian sidewalks & marked crosswalks (clear of building_shell_01..20 MaxY=7400015.4 and office_entry 500232, 7400005)
        AddNode("p_sw", 500003.5, 7400016.5);
        AddNode("p_sc_w", 500104.0, 7400016.5, true, SignalGroup.EastWestCrosswalk);
        AddNode("p_sc_e", 500120.0, 7400016.5);
        AddNode("p_se", 500218.0, 7400016.5);
        AddNode("p_cw_mid", 500104.0, 7400028.4, true, SignalGroup.NorthSouthCrosswalk);
        AddNode("p_ce_mid", 500120.0, 7400028.4, true, SignalGroup.NorthSouthCrosswalk);
        AddNode("p_cw_n", 500104.0, 7400040.5, true, SignalGroup.NorthSouthCrosswalk);
        AddNode("p_ce_n", 500120.0, 7400040.5, true, SignalGroup.NorthSouthCrosswalk);
        AddNode("p_nw", 500003.5, 7400054.0);
        AddNode("p_nc_w", 500104.0, 7400054.0, true, SignalGroup.EastWestCrosswalk);
        AddNode("p_nc_e", 500120.0, 7400054.0);
        AddNode("p_ne", 500218.0, 7400054.0);

        AddSeg("p_s_1", "p_sw", "p_sc_w", false, false, SignalGroup.AlwaysGreen, 1.4, "p_s_cw");
        AddSeg("p_s_cw", "p_sc_w", "p_sc_e", false, true, SignalGroup.EastWestCrosswalk, 1.35, "p_s_2", "p_cross_c_e_n");
        AddSeg("p_s_2", "p_sc_e", "p_se", false, false, SignalGroup.AlwaysGreen, 1.4, "p_e_n");
        AddSeg("p_e_n", "p_se", "p_ne", false, false, SignalGroup.AlwaysGreen, 1.4, "p_n_1_rev");
        AddSeg("p_n_1_rev", "p_ne", "p_nc_e", false, false, SignalGroup.AlwaysGreen, 1.4, "p_n_cw_rev");
        AddSeg("p_n_cw_rev", "p_nc_e", "p_nc_w", false, true, SignalGroup.EastWestCrosswalk, 1.35, "p_n_2_rev", "p_cross_c_w_s");
        AddSeg("p_n_2_rev", "p_nc_w", "p_nw", false, false, SignalGroup.AlwaysGreen, 1.4, "p_w_s");
        AddSeg("p_w_s", "p_nw", "p_sw", false, false, SignalGroup.AlwaysGreen, 1.4, "p_s_1");

        AddSeg("p_cross_c_e_n", "p_sc_e", "p_ce_mid", false, true, SignalGroup.NorthSouthCrosswalk, 1.35, "p_mid_c_e_n");
        AddSeg("p_mid_c_e_n", "p_ce_mid", "p_ce_n", false, false, SignalGroup.AlwaysGreen, 1.4, "p_cross_n_e_n");
        AddSeg("p_cross_n_e_n", "p_ce_n", "p_nc_e", false, true, SignalGroup.NorthSouthCrosswalk, 1.35, "p_n_cw_rev");

        AddSeg("p_cross_c_w_s", "p_nc_w", "p_cw_n", false, true, SignalGroup.NorthSouthCrosswalk, 1.35, "p_mid_c_w_s");
        AddSeg("p_mid_c_w_s", "p_cw_n", "p_cw_mid", false, false, SignalGroup.AlwaysGreen, 1.4, "p_cross_s_w_s");
        AddSeg("p_cross_s_w_s", "p_cw_mid", "p_sc_w", false, true, SignalGroup.NorthSouthCrosswalk, 1.35, "p_s_cw");

        var exclusions = new[]
        {
            new TrafficExclusionZone("office_entry", new Vector3d(500232.0, 7400005.0, 101.5), 6.0),
        };
        return new RoadNetworkGraph(nodes, segments, exclusions);
    }
}
