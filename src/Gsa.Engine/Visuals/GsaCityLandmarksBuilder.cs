using Gsa.Core.Geometry;
using Godot;

namespace Gsa.Engine.Visuals;

/// <summary>Builds iconic GTA III district architecture, bridge/viaduct/mountain driveable surfaces, harbor ships/cranes, signage, and urban landmarks across all 4 regions of São Datum.</summary>
public sealed class GsaCityLandmarksBuilder
{
    public IReadOnlyList<Vector3d> PotholeCentersEnu { get; private set; } = [];
    public Vector3d BlitzCheckpointEnu { get; } = new(500360.0, 7400116.0, 100.0);
    public Vector3d ChickenChallengeStartEnu { get; } = new(500288.0, 7400350.0, 100.1);
    public Vector3d ChickenChallengeEndEnu { get; } = new(500488.0, 7400350.0, 100.1);

    public Node3D? TrioTruckNode { get; private set; }
    public Node3D? MarathonPackNode { get; private set; }
    public Node3D? PigeonFlockNode { get; private set; }

    public double SampleDriveableElevationEnu(double east, double north)
    {
        // 1. Old Center Elevated Viaduct (HIST-07): ramps at E=[500605..500655] and E=[500705..500755], N=[7400472..7400486]
        if (north is >= 7400472.0 and <= 7400486.0)
        {
            if (east is >= 500655.0 and <= 500705.0)
                return 106.5;
            if (east is >= 500605.0 and < 500655.0)
                return 100.0 + ((east - 500605.0) / 50.0) * 6.5;
            if (east is > 500705.0 and <= 500755.0)
                return 100.0 + ((500755.0 - east) / 50.0) * 6.5;
        }

        // 2. Waterfront Callahan Bridge approach toward Pier (E=[501740..501880], N=[7400440..7400456])
        if (north is >= 7400440.0 and <= 7400456.0 && east is >= 501740.0 and <= 501880.0)
        {
            var t = 1.0 - Math.Abs(east - 501810.0) / 70.0;
            return 100.0 + Math.Clamp(t, 0.0, 1.0) * 5.2;
        }

        // 3. Serra Smooth Mountain Climb (NE quadrant E >= 501000, N >= 7401000)
        if (east >= 501000.0 && north >= 7401000.0)
        {
            var sx = Math.Clamp((east - 501000.0) / 1000.0, 0.0, 1.0);
            var sy = Math.Clamp((north - 7401000.0) / 1000.0, 0.0, 1.0);
            return 100.0 + (sx + sy) * 28.0; // Smooth up to ~156 m summit without jarring block stairs!
        }

        return 100.0;
    }

    public void BuildCityLandmarks(
        Node3D visualBase,
        GodotCoordinateAdapter coords,
        GsaAtmosphereController atmosphere)
    {
        var roadAsphaltMat = GsaRetroTextures.CreateWorldMaterial("city_road_asphalt", "asphalt", new Color(0.78f, 0.80f, 0.85f), new Vector3(0.14f, 0.14f, 0.14f), triplanar: true);
        var crosswalkMat = GsaRetroTextures.CreateWorldMaterial("city_crosswalk", "crosswalk", Colors.White, new Vector3(1f, 1f, 1f));
        var sidewalkMat = GsaRetroTextures.CreateWorldMaterial("city_sidewalk", "sidewalk", Colors.White, new Vector3(0.24f, 0.24f, 0.24f), triplanar: true);
        var concMat = GsaMeshBuilder.SolidMat(new Color(0.54f, 0.56f, 0.55f));
        var bridgeSteelMat = GsaMeshBuilder.SolidMat(new Color(0.15f, 0.42f, 0.34f), roughness: 0.55f);
        var craneRedMat = GsaMeshBuilder.SolidMat(new Color(0.78f, 0.18f, 0.14f));
        var waterMat = GsaRetroTextures.CreateWorldMaterial("city_ocean_water", "water", new Color(0.75f, 0.90f, 1.0f), new Vector3(0.08f, 0.08f, 0.08f), roughness: 0.14f, triplanar: true);

        // 1. Paved Avenue Grid with Crosswalks & Intersection Pads
        var yellowLine = GsaMeshBuilder.SolidMat(new Color(0.92f, 0.74f, 0.14f));
        for (var n = 7400120.0; n <= 7401800.0; n += 120.0)
        {
            var aveWidth = n <= 7401000.0 ? 1860f : 960f;
            var centerE = n <= 7401000.0 ? 500930.0 : 500480.0;
            visualBase.AddChild(GsaMeshBuilder.Box($"AveDeckEW_{(int)n}", new Vector3(aveWidth, 0.08f, 13.5f),
                coords.ToGodot(new Vector3d(centerE, n - 4.0, 99.60)), roadAsphaltMat));
            visualBase.AddChild(GsaMeshBuilder.Box($"AveLineEW_{(int)n}", new Vector3(aveWidth, 0.03f, 0.35f),
                coords.ToGodot(new Vector3d(centerE, n - 4.0, 99.66)), yellowLine));
        }
        for (var e = 500000.0; e <= 501800.0; e += 120.0)
        {
            var aveLen = e < 501000.0 ? 1760f : 980f;
            var centerN = e < 501000.0 ? 7400900.0 : 7400490.0;
            visualBase.AddChild(GsaMeshBuilder.Box($"AveDeckNS_{(int)e}", new Vector3(13.5f, 0.08f, aveLen),
                coords.ToGodot(new Vector3d(e, centerN, 99.60)), roadAsphaltMat));
            visualBase.AddChild(GsaMeshBuilder.Box($"AveLineNS_{(int)e}", new Vector3(0.35f, 0.03f, aveLen),
                coords.ToGodot(new Vector3d(e, centerN, 99.66)), yellowLine));
        }

        // Crosswalks at busy Old Center intersections
        foreach (var e in new[] { 500120.0, 500240.0, 500360.0, 500480.0 })
        {
            foreach (var n in new[] { 7400116.0, 7400236.0 })
            {
                visualBase.AddChild(GsaMeshBuilder.Box($"Crosswalk_{(int)e}_{(int)n}", new Vector3(3.6f, 0.05f, 12.0f),
                    coords.ToGodot(new Vector3d(e + 8.5, n, 99.67)), crosswalkMat));
            }
        }

        // 2. Old Center Landmark: Municipal City Hall Monumental Plaza (E=500450, N=7400450)
        BuildCityHall(visualBase, coords, concMat, sidewalkMat);

        // 3. Central Park Fountain, Pond, Walkways & Chicken Challenge Track (500285..500415, 7400315..7400445)
        BuildCentralParkLandmarks(visualBase, coords, concMat, sidewalkMat, waterMat, atmosphere);

        // 4. Old Center Elevated Viaduct (HIST-07, E=500605..500755, N=7400479)
        BuildViaductOverpass(visualBase, coords, concMat, roadAsphaltMat, yellowLine);

        // 5. Port District Landmarks: Gantry Cranes, Stacked Containers, Harbor Tunnel, Freight Ship!
        BuildPortDistrictLandmarks(visualBase, coords, craneRedMat, concMat, waterMat);

        // 6. Waterfront District Landmarks: Callahan Truss Bridge, Pier, Palm Walk, Docked Survey Boat!
        BuildWaterfrontDistrictLandmarks(visualBase, coords, bridgeSteelMat, concMat, sidewalkMat, atmosphere);

        // 7. Serra Mountain Paved Switchback Highway & Summit Broadcast Tower!
        BuildSerraMountainLandmarks(visualBase, coords, roadAsphaltMat, concMat, craneRedMat);

        // 8. Street Hazards: Roadwork Potholes & Cones + Police Blitz Checkpoint + Trio Elétrico / Marathon / Pigeons
        var potholes = new[]
        {
            new Vector3d(500145.0, 7399989.5, 100.0),
            new Vector3d(500290.0, 7400116.0, 100.0),
            new Vector3d(500420.0, 7400236.0, 100.0),
            new Vector3d(500180.0, 7401196.0, 100.0),
        };
        PotholeCentersEnu = potholes;
        for (var i = 0; i < potholes.Length; i++)
        {
            var hole = GsaMeshBuilder.BuildPotholeAndCones($"RoadPothole_{i}");
            hole.Position = coords.ToGodot(potholes[i]);
            visualBase.AddChild(hole);
        }

        var blitz = GsaMeshBuilder.BuildPoliceBlitzUnit("PoliceBlitzCheckpoint");
        blitz.Position = coords.ToGodot(BlitzCheckpointEnu);
        visualBase.AddChild(blitz);

        // Absurd urban event dynamic nodes (hidden until spawned by AbsurdUrbanEventsCoordinator!)
        TrioTruckNode = GsaMeshBuilder.BuildTrioEletricoTruck("AbsurdTrioEletrico");
        TrioTruckNode.Position = coords.ToGodot(new Vector3d(500240.0, 7400236.0, 100.0));
        TrioTruckNode.Visible = false;
        visualBase.AddChild(TrioTruckNode);

        MarathonPackNode = BuildMarathonPack();
        MarathonPackNode.Position = coords.ToGodot(new Vector3d(500360.0, 7400236.0, 100.0));
        MarathonPackNode.Visible = false;
        visualBase.AddChild(MarathonPackNode);

        PigeonFlockNode = BuildPigeonFlock();
        PigeonFlockNode.Position = coords.ToGodot(new Vector3d(500350.0, 7400380.0, 103.5));
        PigeonFlockNode.Visible = false;
        visualBase.AddChild(PigeonFlockNode);
    }

    private static void BuildCityHall(Node3D parent, GodotCoordinateAdapter coords, StandardMaterial3D concMat, StandardMaterial3D sidewalkMat)
    {
        var hall = new Node3D { Name = "CityHallMonument", Position = coords.ToGodot(new Vector3d(500462, 7400505, 100.0)) };
        var marble = GsaMeshBuilder.SolidMat(new Color(0.84f, 0.82f, 0.78f));
        var domeMat = GsaMeshBuilder.SolidMat(new Color(0.24f, 0.46f, 0.40f), roughness: 0.4f);

        hall.AddChild(GsaMeshBuilder.Box("PlazaDeck", new Vector3(42f, 0.35f, 36f), new Vector3(0f, 0.17f, 0f), sidewalkMat));
        hall.AddChild(GsaMeshBuilder.Box("Steps", new Vector3(26f, 0.8f, 8f), new Vector3(0f, 0.55f, 12f), concMat));
        hall.AddChild(GsaMeshBuilder.Box("MainHall", new Vector3(28f, 12f, 20f), new Vector3(0f, 6.4f, -2f), marble));
        for (var cx = -10f; cx <= 10f; cx += 4f)
            hall.AddChild(new MeshInstance3D { Position = new Vector3(cx, 6.2f, 9.5f), Mesh = new CylinderMesh { TopRadius = 0.45f, BottomRadius = 0.55f, Height = 10.5f, RadialSegments = 8 }, MaterialOverride = marble });
        hall.AddChild(GsaMeshBuilder.Box("Pediment", new Vector3(26f, 1.8f, 3.2f), new Vector3(0f, 12.2f, 9.5f), marble));
        hall.AddChild(new MeshInstance3D { Position = new Vector3(0f, 15.5f, -2f), Mesh = new CylinderMesh { TopRadius = 5.5f, BottomRadius = 6.2f, Height = 6.5f, RadialSegments = 12 }, MaterialOverride = domeMat });
        hall.AddChild(GsaMeshBuilder.Box("ClockBanner", new Vector3(14f, 1.4f, 0.4f), new Vector3(0f, 10.2f, 8.2f), GsaMeshBuilder.SolidMat(new Color(0.95f, 0.78f, 0.22f), emit: true, emissionEnergy: 0.7f)));
        parent.AddChild(hall);
    }

    private void BuildCentralParkLandmarks(
        Node3D parent,
        GodotCoordinateAdapter coords,
        StandardMaterial3D concMat,
        StandardMaterial3D sidewalkMat,
        StandardMaterial3D waterMat,
        GsaAtmosphereController atmosphere)
    {
        var park = new Node3D { Name = "CentralParkDetails", Position = coords.ToGodot(new Vector3d(500350, 7400380, 100.0)) };
        park.AddChild(GsaMeshBuilder.Box("PathNS", new Vector3(4.5f, 0.14f, 124f), new Vector3(0f, 0.08f, 0f), sidewalkMat));
        park.AddChild(GsaMeshBuilder.Box("PathEW", new Vector3(124f, 0.14f, 4.5f), new Vector3(0f, 0.08f, 0f), sidewalkMat));
        park.AddChild(new MeshInstance3D { Position = new Vector3(0f, 0.35f, 0f), Mesh = new CylinderMesh { TopRadius = 9.0f, BottomRadius = 9.5f, Height = 0.7f, RadialSegments = 16 }, MaterialOverride = concMat });
        park.AddChild(new MeshInstance3D { Position = new Vector3(0f, 0.55f, 0f), Mesh = new CylinderMesh { TopRadius = 8.2f, BottomRadius = 8.2f, Height = 0.4f, RadialSegments = 16 }, MaterialOverride = waterMat });
        park.AddChild(new MeshInstance3D { Position = new Vector3(0f, 2.2f, 0f), Mesh = new CylinderMesh { TopRadius = 1.2f, BottomRadius = 2.0f, Height = 3.2f, RadialSegments = 10 }, MaterialOverride = concMat });

        // Chicken Challenge 200 m Track Checkered Beacons (N = 7400350)
        var startBeacon = GsaMeshBuilder.BuildBeaconRing("ChickenTrackStart", new Color(0.98f, 0.38f, 0.72f), 2.2f, 1.4f);
        startBeacon.Position = coords.ToGodot(ChickenChallengeStartEnu);
        parent.AddChild(startBeacon);
        var finishBeacon = GsaMeshBuilder.BuildBeaconRing("ChickenTrackEnd", new Color(0.22f, 0.96f, 0.58f), 2.2f, 1.4f);
        finishBeacon.Position = coords.ToGodot(ChickenChallengeEndEnu);
        parent.AddChild(finishBeacon);

        // Ornamental park lamps
        foreach (var off in new Vector3[] { new(-14f, 0f, -14f), new(14f, 0f, 14f), new(-14f, 0f, 14f), new(14f, 0f, -14f) })
        {
            var (lNode, bMat, light) = GsaMeshBuilder.BuildStreetlamp($"ParkLamp_{(int)off.X}_{(int)off.Z}", withRealLight: false);
            lNode.Position = off;
            park.AddChild(lNode);
            atmosphere.RegisterStreetlamp(bMat, light);
        }
        parent.AddChild(park);
    }

    private static void BuildViaductOverpass(
        Node3D parent,
        GodotCoordinateAdapter coords,
        StandardMaterial3D concMat,
        StandardMaterial3D asphaltMat,
        StandardMaterial3D yellowMat)
    {
        var deck = new Node3D { Name = "ViaductOverpass" };
        // Elevated center span E=500655..500705, N=7400479, Z=106.5
        deck.AddChild(GsaMeshBuilder.Box("ViaductCenterSpan", new Vector3(50f, 0.8f, 14f), coords.ToGodot(new Vector3d(500680, 7400479, 106.1)), concMat));
        deck.AddChild(GsaMeshBuilder.Box("ViaductCenterRoad", new Vector3(50f, 0.06f, 12.2f), coords.ToGodot(new Vector3d(500680, 7400479, 106.52)), asphaltMat));
        deck.AddChild(GsaMeshBuilder.Box("ViaductCenterLine", new Vector3(50f, 0.04f, 0.35f), coords.ToGodot(new Vector3d(500680, 7400479, 106.55)), yellowMat));

        // West & East sloped approach ramps (7.4 deg grade)
        deck.AddChild(GsaMeshBuilder.Box("ViaductRampW", new Vector3(50.6f, 0.7f, 13.6f), coords.ToGodot(new Vector3d(500630, 7400479, 103.0)), asphaltMat, rotDeg: new Vector3(0f, 0f, 7.4f)));
        deck.AddChild(GsaMeshBuilder.Box("ViaductRampE", new Vector3(50.6f, 0.7f, 13.6f), coords.ToGodot(new Vector3d(500730, 7400479, 103.0)), asphaltMat, rotDeg: new Vector3(0f, 0f, -7.4f)));

        foreach (var px in new[] { 500658.0, 500680.0, 500702.0 })
            foreach (var pn in new[] { 7400474.5, 7400483.5 })
                deck.AddChild(new MeshInstance3D { Position = coords.ToGodot(new Vector3d(px, pn, 103.0)), Mesh = new CylinderMesh { TopRadius = 0.8f, BottomRadius = 0.9f, Height = 6.2f, RadialSegments = 10 }, MaterialOverride = concMat });

        parent.AddChild(deck);
    }

    private static void BuildPortDistrictLandmarks(
        Node3D parent,
        GodotCoordinateAdapter coords,
        StandardMaterial3D craneMat,
        StandardMaterial3D concMat,
        StandardMaterial3D waterMat)
    {
        var port = new Node3D { Name = "PortDistrictLandmarks" };
        // Harbor water basin north of N=7401820
        port.AddChild(GsaMeshBuilder.Box("HarborBasin", new Vector3(950f, 0.4f, 260f), coords.ToGodot(new Vector3d(500480, 7401930, 97.6)), waterMat));
        port.AddChild(GsaMeshBuilder.Box("QuayWall", new Vector3(920f, 2.2f, 12f), coords.ToGodot(new Vector3d(500480, 7401800, 99.1)), concMat));

        // 2 Giant Lattice Gantry Cranes + Colorful Shipping Container Stacks
        var containerColors = new Color[] { new(0.78f, 0.22f, 0.16f), new(0.16f, 0.44f, 0.72f), new(0.22f, 0.56f, 0.34f), new(0.84f, 0.58f, 0.14f) };
        for (var i = 0; i < 2; i++)
        {
            var ce = 500260.0 + i * 240.0;
            var crane = new Node3D { Position = coords.ToGodot(new Vector3d(ce, 7401790, 100.0)) };
            foreach (var offX in new[] { -8f, 8f })
            {
                crane.AddChild(GsaMeshBuilder.Box("LegF", new Vector3(1.4f, 24f, 1.4f), new Vector3(offX, 12f, 6f), craneMat));
                crane.AddChild(GsaMeshBuilder.Box("LegB", new Vector3(1.4f, 24f, 1.4f), new Vector3(offX, 12f, -6f), craneMat));
            }
            crane.AddChild(GsaMeshBuilder.Box("Boom", new Vector3(20f, 2.0f, 44f), new Vector3(0f, 23f, -10f), craneMat));
            crane.AddChild(GsaMeshBuilder.Box("Cabin", new Vector3(4.5f, 3.2f, 4.5f), new Vector3(0f, 19f, -18f), GsaMeshBuilder.SolidMat(new Color(0.95f, 0.85f, 0.22f))));
            port.AddChild(crane);
        }

        for (var s = 0; s < 14; s++)
        {
            var e = 500160.0 + (s % 7) * 58.0;
            var n = 7401680.0 + (s / 7) * 44.0;
            for (var tier = 0; tier < 2 + (s % 2); tier++)
            {
                var colMat = GsaRetroTextures.CreateWorldMaterial($"cont_{s}_{tier}", "warehouse", containerColors[(s + tier) % containerColors.Length], new Vector3(0.25f, 0.25f, 0.25f), triplanar: true);
                port.AddChild(GsaMeshBuilder.Box($"Container_{s}_{tier}", new Vector3(12f, 2.9f, 3.2f), coords.ToGodot(new Vector3d(e, n, 101.5 + tier * 3.0)), colMat));
            }
        }

        // Port Industrial Sheds & Harbor Quay Railway Spur (E2 rail teaser!)
        var wareMat = GsaRetroTextures.CreateWorldMaterial("port_shed_ware", "warehouse", new Color(0.86f, 0.88f, 0.90f), new Vector3(0.095f, 0.095f, 0.095f), triplanar: true);
        port.AddChild(GsaMeshBuilder.Box("PortTransitShedW", new Vector3(44f, 12f, 26f), coords.ToGodot(new Vector3d(500280, 7401640, 106.0)), wareMat));
        port.AddChild(GsaMeshBuilder.Box("PortTransitShedE", new Vector3(44f, 12f, 26f), coords.ToGodot(new Vector3d(500440, 7401640, 106.0)), wareMat));
        port.AddChild(GsaMeshBuilder.Box("QuayRailBed", new Vector3(820f, 0.25f, 4.2f), coords.ToGodot(new Vector3d(500480, 7401760, 100.05)), GsaMeshBuilder.SolidMat(new Color(0.32f, 0.27f, 0.22f))));
        port.AddChild(GsaMeshBuilder.Box("QuayRailA", new Vector3(820f, 0.14f, 0.18f), coords.ToGodot(new Vector3d(500480, 7401759.2, 100.22)), GsaMeshBuilder.SolidMat(new Color(0.65f, 0.67f, 0.70f))));
        port.AddChild(GsaMeshBuilder.Box("QuayRailB", new Vector3(820f, 0.14f, 0.18f), coords.ToGodot(new Vector3d(500480, 7401760.8, 100.22)), GsaMeshBuilder.SolidMat(new Color(0.65f, 0.67f, 0.70f))));

        // Moored Cargo Ship in Port Harbor
        var ship = new Node3D { Name = "CargoFreighter", Position = coords.ToGodot(new Vector3d(500360, 7401875, 99.0)) };
        ship.AddChild(GsaMeshBuilder.Box("Hull", new Vector3(62f, 7.5f, 16f), new Vector3(0f, 2.2f, 0f), GsaMeshBuilder.SolidMat(new Color(0.18f, 0.20f, 0.22f))));
        ship.AddChild(GsaMeshBuilder.Box("RedWaterline", new Vector3(63f, 2.2f, 16.4f), new Vector3(0f, -0.4f, 0f), craneMat));
        ship.AddChild(GsaMeshBuilder.Box("BridgeHouse", new Vector3(12f, 11f, 13f), new Vector3(-20f, 10.5f, 0f), GsaMeshBuilder.SolidMat(new Color(0.90f, 0.91f, 0.92f))));
        ship.AddChild(GsaMeshBuilder.Box("Funnel", new Vector3(4.5f, 5.5f, 4.5f), new Vector3(-23f, 17.5f, 0f), GsaMeshBuilder.SolidMat(new Color(0.92f, 0.62f, 0.14f))));
        port.AddChild(ship);

        // Harbor Tunnel Entrance Portal (HIST-06, E=500480, N=7401320)
        var tunnel = new Node3D { Name = "HarborTunnelPortal", Position = coords.ToGodot(new Vector3d(500480, 7401320, 100.0)) };
        tunnel.AddChild(GsaMeshBuilder.Box("PierL", new Vector3(3.5f, 7.5f, 24f), new Vector3(-9f, 3.75f, 0f), concMat));
        tunnel.AddChild(GsaMeshBuilder.Box("PierR", new Vector3(3.5f, 7.5f, 24f), new Vector3(9f, 3.75f, 0f), concMat));
        tunnel.AddChild(GsaMeshBuilder.Box("Roof", new Vector3(21.5f, 2.4f, 24f), new Vector3(0f, 7.2f, 0f), concMat));
        tunnel.AddChild(GsaMeshBuilder.Box("WarnSign", new Vector3(12f, 1.1f, 0.5f), new Vector3(0f, 6.2f, 12.2f), GsaMeshBuilder.SolidMat(new Color(0.98f, 0.68f, 0.10f), emit: true, emissionEnergy: 1.2f)));
        port.AddChild(tunnel);

        parent.AddChild(port);
    }

    private static void BuildWaterfrontDistrictLandmarks(
        Node3D parent,
        GodotCoordinateAdapter coords,
        StandardMaterial3D steelMat,
        StandardMaterial3D concMat,
        StandardMaterial3D sidewalkMat,
        GsaAtmosphereController atmosphere)
    {
        var wf = new Node3D { Name = "WaterfrontLandmarks" };
        var woodMat = GsaMeshBuilder.SolidMat(new Color(0.52f, 0.36f, 0.22f));

        // Coastal Suspension / Steel Truss Bridge leading onto Waterfront Pier (E=501740..501880, N=7400448)
        var bridgeDeckMat = GsaRetroTextures.CreateWorldMaterial("bridge_asphalt", "asphalt", new Color(0.84f, 0.86f, 0.90f), new Vector3(0.14f, 0.14f, 0.14f), triplanar: true);
        var cableMat = GsaMeshBuilder.SolidMat(new Color(0.24f, 0.28f, 0.30f));
        var yellowLine = GsaMeshBuilder.SolidMat(new Color(0.92f, 0.74f, 0.14f));
        wf.AddChild(GsaMeshBuilder.Box("BridgeRampW", new Vector3(72f, 0.8f, 14f), coords.ToGodot(new Vector3d(501775, 7400448, 102.2)), bridgeDeckMat, rotDeg: new Vector3(0f, 0f, 4.2f)));
        wf.AddChild(GsaMeshBuilder.Box("BridgeRampE", new Vector3(72f, 0.8f, 14f), coords.ToGodot(new Vector3d(501845, 7400448, 102.2)), bridgeDeckMat, rotDeg: new Vector3(0f, 0f, -4.2f)));
        wf.AddChild(GsaMeshBuilder.Box("BridgeStripeW", new Vector3(70f, 0.05f, 0.32f), coords.ToGodot(new Vector3d(501775, 7400448, 102.63)), yellowLine, rotDeg: new Vector3(0f, 0f, 4.2f)));
        wf.AddChild(GsaMeshBuilder.Box("BridgeStripeE", new Vector3(70f, 0.05f, 0.32f), coords.ToGodot(new Vector3d(501845, 7400448, 102.63)), yellowLine, rotDeg: new Vector3(0f, 0f, -4.2f)));
        wf.AddChild(GsaMeshBuilder.Box("BridgeMainSpan", new Vector3(64f, 0.9f, 14.2f), coords.ToGodot(new Vector3d(501810, 7400448, 104.8)), bridgeDeckMat));
        wf.AddChild(GsaMeshBuilder.Box("BridgeRailL", new Vector3(130f, 1.0f, 0.35f), coords.ToGodot(new Vector3d(501810, 7400441.2, 105.6)), steelMat));
        wf.AddChild(GsaMeshBuilder.Box("BridgeRailR", new Vector3(130f, 1.0f, 0.35f), coords.ToGodot(new Vector3d(501810, 7400454.8, 105.6)), steelMat));
        foreach (var tx in new[] { 501780.0, 501840.0 })
        {
            wf.AddChild(GsaMeshBuilder.Box($"BridgeTowerL_{(int)tx}", new Vector3(1.6f, 18f, 1.6f), coords.ToGodot(new Vector3d(tx, 7400441.5, 109.0)), steelMat));
            wf.AddChild(GsaMeshBuilder.Box($"BridgeTowerR_{(int)tx}", new Vector3(1.6f, 18f, 1.6f), coords.ToGodot(new Vector3d(tx, 7400454.5, 109.0)), steelMat));
            wf.AddChild(GsaMeshBuilder.Box($"BridgeCross_{(int)tx}", new Vector3(1.6f, 1.8f, 14.5f), coords.ToGodot(new Vector3d(tx, 7400448.0, 117.5)), steelMat));
            wf.AddChild(GsaMeshBuilder.Box($"BridgeBeaconL_{(int)tx}", new Vector3(0.55f, 0.55f, 0.55f), coords.ToGodot(new Vector3d(tx, 7400441.5, 118.6)), GsaMeshBuilder.SolidMat(Colors.Red, emit: true, emissionEnergy: 2.0f)));
            // Stay cables fanning down from tower top (118) to deck edges (104.8)!
            wf.AddChild(GsaMeshBuilder.Box($"StayCableLW_{(int)tx}", new Vector3(26f, 0.16f, 0.16f), coords.ToGodot(new Vector3d(tx - 11.0, 7400441.5, 111.4)), cableMat, rotDeg: new Vector3(0f, 0f, -30f)));
            wf.AddChild(GsaMeshBuilder.Box($"StayCableLE_{(int)tx}", new Vector3(26f, 0.16f, 0.16f), coords.ToGodot(new Vector3d(tx + 11.0, 7400441.5, 111.4)), cableMat, rotDeg: new Vector3(0f, 0f, 30f)));
            wf.AddChild(GsaMeshBuilder.Box($"StayCableRW_{(int)tx}", new Vector3(26f, 0.16f, 0.16f), coords.ToGodot(new Vector3d(tx - 11.0, 7400454.5, 111.4)), cableMat, rotDeg: new Vector3(0f, 0f, -30f)));
            wf.AddChild(GsaMeshBuilder.Box($"StayCableRE_{(int)tx}", new Vector3(26f, 0.16f, 0.16f), coords.ToGodot(new Vector3d(tx + 11.0, 7400454.5, 111.4)), cableMat, rotDeg: new Vector3(0f, 0f, 30f)));
        }

        // Flanking Glass Waterfront Towers near bridge approach
        var glassMat = GsaRetroTextures.CreateWorldMaterial("wf_glass_flank", "glass_tower", Colors.White, new Vector3(0.09f, 0.09f, 0.09f), triplanar: true);
        wf.AddChild(GsaMeshBuilder.Box("WfApproachTowerN", new Vector3(24f, 34f, 24f), coords.ToGodot(new Vector3d(501742, 7400488, 117.0)), glassMat));
        wf.AddChild(GsaMeshBuilder.Box("WfApproachTowerS", new Vector3(24f, 28f, 24f), coords.ToGodot(new Vector3d(501742, 7400408, 114.0)), glassMat));

        // Wooden Waterfront Survey Pier + Seawall Boardwalk (E=501890..501970, N=7400448)
        wf.AddChild(GsaMeshBuilder.Box("SeawallWalk", new Vector3(12f, 1.8f, 920f), coords.ToGodot(new Vector3d(501790, 7400490, 100.2)), sidewalkMat));
        wf.AddChild(GsaMeshBuilder.Box("SurveyPier", new Vector3(95f, 0.9f, 18f), coords.ToGodot(new Vector3d(501930, 7400448, 100.3)), woodMat));

        // Palm trees & boardwalk lamps along seawall!
        for (var n = 7400080.0; n <= 7400900.0; n += 65.0)
        {
            var tree = GsaMeshBuilder.BuildStreetTree($"PalmWater_{(int)n}", 1.25f);
            tree.Position = coords.ToGodot(new Vector3d(501785.0, n, 100.8));
            wf.AddChild(tree);
            var (lNode, bMat, lgt) = GsaMeshBuilder.BuildStreetlamp($"WfLamp_{(int)n}", withRealLight: false);
            lNode.Position = coords.ToGodot(new Vector3d(501793.0, n + 18.0, 100.8));
            wf.AddChild(lNode);
            atmosphere.RegisterStreetlamp(bMat, lgt);
        }

        // Docked Survey Boat next to Pier!
        var boat = new Node3D { Name = "DockedSurveyBoat", Position = coords.ToGodot(new Vector3d(501945, 7400468, 98.4)) };
        boat.AddChild(GsaMeshBuilder.Box("Hull", new Vector3(11.5f, 1.8f, 4.2f), new Vector3(0f, 0.6f, 0f), GsaMeshBuilder.SolidMat(new Color(0.92f, 0.94f, 0.96f))));
        boat.AddChild(GsaMeshBuilder.Box("Stripe", new Vector3(11.6f, 0.35f, 4.3f), new Vector3(0f, 1.1f, 0f), GsaMeshBuilder.SolidMat(new Color(0.14f, 0.56f, 0.72f))));
        boat.AddChild(GsaMeshBuilder.Box("Cabin", new Vector3(4.2f, 1.6f, 2.8f), new Vector3(-1.2f, 1.8f, 0f), GsaMeshBuilder.SolidMat(new Color(0.16f, 0.24f, 0.32f))));
        boat.AddChild(GsaMeshBuilder.Box("Mast", new Vector3(0.25f, 2.2f, 0.25f), new Vector3(-1.2f, 3.2f, 0f), GsaMeshBuilder.SolidMat(Colors.Yellow, emit: true)));
        wf.AddChild(boat);

        parent.AddChild(wf);
    }

    private void BuildSerraMountainLandmarks(
        Node3D parent,
        GodotCoordinateAdapter coords,
        StandardMaterial3D asphaltMat,
        StandardMaterial3D concMat,
        StandardMaterial3D towerRedMat)
    {
        var serra = new Node3D { Name = "SerraMountain" };
        var parkMat = GsaRetroTextures.CreateWorldMaterial("city_park_grass", "park_grass", Colors.White, new Vector3(0.10f, 0.10f, 0.10f), triplanar: true);

        // Smooth sloped ground plane matching SampleDriveableElevationEnu (0..28m X, 0..28m Y -> tilted hillside!)
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        const int grid = 12;
        const double size = 1000.0;
        for (var gy = 0; gy < grid; gy++)
        {
            for (var gx = 0; gx < grid; gx++)
            {
                var e0 = 501000.0 + (gx / (double)grid) * size;
                var e1 = 501000.0 + ((gx + 1) / (double)grid) * size;
                var n0 = 7401000.0 + (gy / (double)grid) * size;
                var n1 = 7401000.0 + ((gy + 1) / (double)grid) * size;
                var v00 = coords.ToGodot(new Vector3d(e0, n0, SampleDriveableElevationEnu(e0, n0) - 0.15));
                var v10 = coords.ToGodot(new Vector3d(e1, n0, SampleDriveableElevationEnu(e1, n0) - 0.15));
                var v11 = coords.ToGodot(new Vector3d(e1, n1, SampleDriveableElevationEnu(e1, n1) - 0.15));
                var v01 = coords.ToGodot(new Vector3d(e0, n1, SampleDriveableElevationEnu(e0, n1) - 0.15));
                st.AddVertex(v00); st.AddVertex(v11); st.AddVertex(v10);
                st.AddVertex(v00); st.AddVertex(v01); st.AddVertex(v11);
            }
        }
        st.GenerateNormals();
        serra.AddChild(new MeshInstance3D { Name = "SerraSlopedTerrain", Mesh = st.Commit(), MaterialOverride = parkMat });

        // Serra Summit Broadcast Tower & Observation Deck (E=501860, N=7401860)
        var sumElev = SampleDriveableElevationEnu(501860, 7401860);
        var summit = new Node3D { Name = "SerraSummitObservatory", Position = coords.ToGodot(new Vector3d(501860, 7401860, sumElev)) };
        summit.AddChild(GsaMeshBuilder.Box("OverlookPad", new Vector3(36f, 1.2f, 36f), new Vector3(0f, 0.5f, 0f), concMat));
        summit.AddChild(new MeshInstance3D { Position = new Vector3(0f, 18f, 0f), Mesh = new CylinderMesh { TopRadius = 0.35f, BottomRadius = 1.8f, Height = 36f, RadialSegments = 8 }, MaterialOverride = towerRedMat });
        summit.AddChild(new MeshInstance3D { Position = new Vector3(0f, 36.5f, 0f), Mesh = new SphereMesh { Radius = 0.7f, Height = 1.4f }, MaterialOverride = GsaMeshBuilder.SolidMat(Colors.Red, emit: true, emissionEnergy: 2.5f) });
        serra.AddChild(summit);

        // Continuous smooth paved Serra Mountain Highway up to Summit Observatory!
        var roadSt = new SurfaceTool();
        roadSt.Begin(Mesh.PrimitiveType.Triangles);
        const int roadSteps = 24;
        for (var rs = 0; rs < roadSteps; rs++)
        {
            var t0 = rs / (double)roadSteps;
            var t1 = (rs + 1) / (double)roadSteps;
            var cE0 = 501040.0 + t0 * 820.0;
            var cN0 = 7401040.0 + t0 * 820.0;
            var cE1 = 501040.0 + t1 * 820.0;
            var cN1 = 7401040.0 + t1 * 820.0;
            const double halfW = 6.5;
            var p0L = coords.ToGodot(new Vector3d(cE0 - halfW, cN0 + halfW, SampleDriveableElevationEnu(cE0 - halfW, cN0 + halfW) + 0.08));
            var p0R = coords.ToGodot(new Vector3d(cE0 + halfW, cN0 - halfW, SampleDriveableElevationEnu(cE0 + halfW, cN0 - halfW) + 0.08));
            var p1L = coords.ToGodot(new Vector3d(cE1 - halfW, cN1 + halfW, SampleDriveableElevationEnu(cE1 - halfW, cN1 + halfW) + 0.08));
            var p1R = coords.ToGodot(new Vector3d(cE1 + halfW, cN1 - halfW, SampleDriveableElevationEnu(cE1 + halfW, cN1 - halfW) + 0.08));
            roadSt.AddVertex(p0L); roadSt.AddVertex(p1R); roadSt.AddVertex(p0R);
            roadSt.AddVertex(p0L); roadSt.AddVertex(p1L); roadSt.AddVertex(p1R);
        }
        roadSt.GenerateNormals();
        serra.AddChild(new MeshInstance3D { Name = "SerraSummitHighway", Mesh = roadSt.Commit(), MaterialOverride = asphaltMat });

        // Scatter lush hillside trees flanking the climb
        for (var i = 0; i < 24; i++)
        {
            var e = 501080.0 + (i % 6) * 155.0;
            var n = 7401080.0 + (i / 6) * 210.0;
            var elev = SampleDriveableElevationEnu(e, n);
            var tree = GsaMeshBuilder.BuildStreetTree($"SerraTree_{i}", 1.35f);
            tree.Position = coords.ToGodot(new Vector3d(e, n, elev));
            serra.AddChild(tree);
        }

        parent.AddChild(serra);
    }

    private static Node3D BuildMarathonPack()
    {
        var pack = new Node3D { Name = "MarathonRunners" };
        var runnerMesh = GsaMeshBuilder.BuildTrafficPedestrianMesh();
        for (var i = 0; i < 8; i++)
        {
            var runner = new MeshInstance3D
            {
                Mesh = runnerMesh,
                Position = new Vector3((i - 4) * 2.5f, 0f, (i % 2 == 0 ? 1.2f : -1.2f)),
            };
            pack.AddChild(runner);
        }
        return pack;
    }

    private static Node3D BuildPigeonFlock()
    {
        var flock = new Node3D { Name = "PigeonFlock" };
        var birdMat = GsaMeshBuilder.SolidMat(new Color(0.84f, 0.86f, 0.88f));
        for (var i = 0; i < 14; i++)
        {
            var ang = i * (MathF.Tau / 14f);
            flock.AddChild(GsaMeshBuilder.Box($"Bird_{i}", new Vector3(0.45f, 0.14f, 0.25f), new Vector3(MathF.Cos(ang) * 5.5f, (i % 3) * 0.6f, MathF.Sin(ang) * 5.5f), birdMat));
        }
        return flock;
    }
}

