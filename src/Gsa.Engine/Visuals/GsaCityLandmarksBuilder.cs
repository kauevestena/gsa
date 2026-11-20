using Gsa.Core.Geometry;
using Gsa.Core.Traffic;
using Godot;

namespace Gsa.Engine.Visuals;

/// <summary>Builds iconic GTA III district architecture, bridge/viaduct/mountain driveable surfaces, harbor ships/cranes, signage, and urban landmarks across all 4 regions of São Datum.</summary>
public sealed class GsaCityLandmarksBuilder
{
    public IReadOnlyList<Vector3d> PotholeCentersEnu { get; private set; } = [];
    public Vector3d BlitzCheckpointEnu => OldCenterTrafficNetwork.CheckpointEnu;
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
        var roadBatch = new GsaStaticMeshBatcher();
        var structureBatch = new GsaStaticMeshBatcher();
        var lampBulbBatch = new GsaStaticMeshBatcher();
        var treeList = new List<(Vector3 Position, float Scale)>(64);

        var roadAsphaltMat = GsaRetroTextures.CreateWorldMaterial(
            "city_road_asphalt_uv", "asphalt", new Color(0.78f, 0.80f, 0.85f), triplanar: false);
        var crosswalkMat = GsaRetroTextures.CreateWorldMaterial("city_crosswalk_uv", "crosswalk", Colors.White, triplanar: false);
        var sidewalkMat = GsaRetroTextures.CreateWorldMaterial("city_sidewalk_uv", "sidewalk", Colors.White, triplanar: false);
        var concMat = GsaMeshBuilder.SolidMat(new Color(0.54f, 0.56f, 0.55f));
        var bridgeSteelMat = GsaMeshBuilder.SolidMat(new Color(0.15f, 0.42f, 0.34f), roughness: 0.55f);
        var craneRedMat = GsaMeshBuilder.SolidMat(new Color(0.78f, 0.18f, 0.14f));
        var waterMat = GsaRetroTextures.CreateWorldMaterial(
            "city_ocean_water_uv", "water", new Color(0.75f, 0.90f, 1.0f), roughness: 0.14f, triplanar: false);
        var yellowLine = GsaMeshBuilder.SolidMat(new Color(0.92f, 0.74f, 0.14f));
        var lampPoleMat = GsaMeshBuilder.SolidMat(new Color(0.24f, 0.27f, 0.30f));
        var sharedLampBulbMat = GsaMeshBuilder.SolidStandardMat(new Color(1.0f, 0.88f, 0.56f), emit: true, emissionEnergy: 0.0f);
        atmosphere.RegisterStreetlamp(sharedLampBulbMat, null);

        // 1. Paved Avenue Grid with Crosswalks & Intersection Pads (batched into single meshes)
        for (var n = 7400120.0; n <= 7401800.0; n += 120.0)
        {
            var aveWidth = n <= 7401000.0 ? 1860f : 960f;
            var centerE = n <= 7401000.0 ? 500930.0 : 500480.0;
            roadBatch.AddBox(roadAsphaltMat, coords.ToGodot(new Vector3d(centerE, n - 4.0, 99.60)), new Vector3(aveWidth, 0.08f, 13.5f), uvScale: 0.14f);
            roadBatch.AddBox(yellowLine, coords.ToGodot(new Vector3d(centerE, n - 4.0, 99.66)), new Vector3(aveWidth, 0.03f, 0.35f));
        }
        // Commerce Blvd / North Ave center lines (N = 7400000)
        roadBatch.AddBox(yellowLine, coords.ToGodot(new Vector3d(500950, 7399996.0, 99.64)), new Vector3(1900f, 0.04f, 0.28f));

        for (var e = 500000.0; e <= 501800.0; e += 120.0)
        {
            var aveLen = e < 501000.0 ? 1760f : 980f;
            var centerN = e < 501000.0 ? 7400900.0 : 7400490.0;
            roadBatch.AddBox(roadAsphaltMat, coords.ToGodot(new Vector3d(e, centerN, 99.60)), new Vector3(13.5f, 0.08f, aveLen), uvScale: 0.14f);
            roadBatch.AddBox(yellowLine, coords.ToGodot(new Vector3d(e, centerN, 99.66)), new Vector3(0.35f, 0.03f, aveLen));
        }

        // Crosswalks at busy Old Center intersections
        foreach (var e in new[] { 500120.0, 500240.0, 500360.0, 500480.0 })
        {
            foreach (var n in new[] { 7400116.0, 7400236.0 })
                roadBatch.AddBoxNormalizedUv(crosswalkMat, coords.ToGodot(new Vector3d(e + 8.5, n, 99.67)), new Vector3(3.6f, 0.05f, 12.0f));
        }

        // 2. Old Center Landmark: Municipal City Hall Monumental Plaza (E=500450, N=7400450)
        BuildCityHall(structureBatch, roadBatch, coords, concMat, sidewalkMat);

        // 3. Central Park Fountain, Pond, Walkways & Chicken Challenge Track (500285..500415, 7400315..7400445)
        BuildCentralParkLandmarks(visualBase, roadBatch, structureBatch, lampBulbBatch, lampPoleMat, sharedLampBulbMat, coords, concMat, sidewalkMat, waterMat);

        // 4. Old Center Elevated Viaduct (HIST-07, E=500605..500755, N=7400479)
        BuildViaductOverpass(structureBatch, coords, concMat, roadAsphaltMat, yellowLine);

        // 5. Port District Landmarks: Gantry Cranes, Stacked Containers, Harbor Tunnel, Freight Ship!
        BuildPortDistrictLandmarks(roadBatch, structureBatch, coords, craneRedMat, concMat, waterMat);

        // 6. Waterfront District Landmarks: Callahan Truss Bridge, Pier, Palm Walk, Docked Survey Boat!
        BuildWaterfrontDistrictLandmarks(structureBatch, lampBulbBatch, lampPoleMat, sharedLampBulbMat, treeList, coords, bridgeSteelMat, sidewalkMat);

        // 7. Serra Mountain Paved Switchback Highway & Summit Broadcast Tower!
        BuildSerraMountainLandmarks(visualBase, structureBatch, treeList, coords, roadAsphaltMat, concMat, craneRedMat);

        // Commit batched landmarks
        roadBatch.BuildSeparatedNodes(visualBase, "CityRoadGrid", GeometryInstance3D.ShadowCastingSetting.Off);
        structureBatch.BuildSeparatedNodes(visualBase, "CityLandmarks", GeometryInstance3D.ShadowCastingSetting.Off, visibilityRangeEnd: 460f);
        lampBulbBatch.BuildSeparatedNodes(visualBase, "CityLandmarkBulbs", GeometryInstance3D.ShadowCastingSetting.Off, visibilityRangeEnd: 320f);
        GsaMeshBuilder.BuildBatchedStreetTrees(visualBase, "LandmarkTrees", treeList, visibilityRangeEnd: 380f);

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
        blitz.Position = coords.ToGodot(BlitzCheckpointEnu + new Vector3d(1, 10.0, 0));
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

    private static void BuildCityHall(
        GsaStaticMeshBatcher structureBatch,
        GsaStaticMeshBatcher roadBatch,
        GodotCoordinateAdapter coords,
        Material concMat,
        Material sidewalkMat)
    {
        var hallPos = coords.ToGodot(new Vector3d(500462, 7400505, 100.0));
        var marble = GsaMeshBuilder.SolidMat(new Color(0.84f, 0.82f, 0.78f));
        var domeMat = GsaMeshBuilder.SolidMat(new Color(0.24f, 0.46f, 0.40f), roughness: 0.4f);
        var bannerMat = GsaMeshBuilder.SolidMat(new Color(0.95f, 0.78f, 0.22f), emit: true, emissionEnergy: 0.7f);

        roadBatch.AddBox(sidewalkMat, hallPos + new Vector3(0f, 0.17f, 0f), new Vector3(42f, 0.35f, 36f), uvScale: 0.24f);
        structureBatch.AddBox(concMat, hallPos + new Vector3(0f, 0.55f, 12f), new Vector3(26f, 0.8f, 8f));
        structureBatch.AddBox(marble, hallPos + new Vector3(0f, 6.4f, -2f), new Vector3(28f, 12f, 20f));
        for (var cx = -10f; cx <= 10f; cx += 4f)
            structureBatch.AddCylinder(marble, hallPos + new Vector3(cx, 6.2f, 9.5f), 0.45f, 0.55f, 10.5f, radialSegments: 8);
        structureBatch.AddBox(marble, hallPos + new Vector3(0f, 12.2f, 9.5f), new Vector3(26f, 1.8f, 3.2f));
        structureBatch.AddCylinder(domeMat, hallPos + new Vector3(0f, 15.5f, -2f), 5.5f, 6.2f, 6.5f, radialSegments: 10);
        structureBatch.AddBox(bannerMat, hallPos + new Vector3(0f, 10.2f, 8.2f), new Vector3(14f, 1.4f, 0.4f));
    }

    private void BuildCentralParkLandmarks(
        Node3D parent,
        GsaStaticMeshBatcher roadBatch,
        GsaStaticMeshBatcher structureBatch,
        GsaStaticMeshBatcher lampBulbBatch,
        Material poleMat,
        StandardMaterial3D bulbMat,
        GodotCoordinateAdapter coords,
        Material concMat,
        Material sidewalkMat,
        Material waterMat)
    {
        var parkPos = coords.ToGodot(new Vector3d(500350, 7400380, 100.0));
        roadBatch.AddBox(sidewalkMat, parkPos + new Vector3(0f, 0.08f, 0f), new Vector3(4.5f, 0.14f, 124f), uvScale: 0.24f);
        roadBatch.AddBox(sidewalkMat, parkPos + new Vector3(0f, 0.08f, 0f), new Vector3(124f, 0.14f, 4.5f), uvScale: 0.24f);
        structureBatch.AddCylinder(concMat, parkPos + new Vector3(0f, 0.35f, 0f), 9.0f, 9.5f, 0.7f, radialSegments: 14);
        structureBatch.AddCylinder(waterMat, parkPos + new Vector3(0f, 0.55f, 0f), 8.2f, 8.2f, 0.4f, radialSegments: 14, uvScale: 0.08f);
        structureBatch.AddCylinder(concMat, parkPos + new Vector3(0f, 2.2f, 0f), 1.2f, 2.0f, 3.2f, radialSegments: 8);

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
            var p = parkPos + off;
            structureBatch.AddBox(poleMat, p + new Vector3(0f, 2.6f, 0f), new Vector3(0.14f, 5.2f, 0.14f));
            structureBatch.AddBox(poleMat, p + new Vector3(0.50f, 5.15f, 0f), new Vector3(1.1f, 0.10f, 0.10f));
            lampBulbBatch.AddBox(bulbMat, p + new Vector3(0.98f, 5.06f, 0f), new Vector3(0.34f, 0.10f, 0.22f));
        }
    }

    private static void BuildViaductOverpass(
        GsaStaticMeshBatcher structureBatch,
        GodotCoordinateAdapter coords,
        Material concMat,
        Material asphaltMat,
        Material yellowMat)
    {
        structureBatch.AddBox(concMat, coords.ToGodot(new Vector3d(500680, 7400479, 106.1)), new Vector3(50f, 0.8f, 14f));
        structureBatch.AddBox(asphaltMat, coords.ToGodot(new Vector3d(500680, 7400479, 106.52)), new Vector3(50f, 0.06f, 12.2f), uvScale: 0.14f);
        structureBatch.AddBox(yellowMat, coords.ToGodot(new Vector3d(500680, 7400479, 106.55)), new Vector3(50f, 0.04f, 0.35f));

        structureBatch.AddBox(asphaltMat, coords.ToGodot(new Vector3d(500630, 7400479, 103.0)), new Vector3(50.6f, 0.7f, 13.6f), rotDeg: new Vector3(0f, 0f, 7.4f), uvScale: 0.14f);
        structureBatch.AddBox(asphaltMat, coords.ToGodot(new Vector3d(500730, 7400479, 103.0)), new Vector3(50.6f, 0.7f, 13.6f), rotDeg: new Vector3(0f, 0f, -7.4f), uvScale: 0.14f);

        foreach (var px in new[] { 500658.0, 500680.0, 500702.0 })
            foreach (var pn in new[] { 7400474.5, 7400483.5 })
                structureBatch.AddCylinder(concMat, coords.ToGodot(new Vector3d(px, pn, 103.0)), 0.8f, 0.9f, 6.2f, radialSegments: 8);
    }

    private static void BuildPortDistrictLandmarks(
        GsaStaticMeshBatcher roadBatch,
        GsaStaticMeshBatcher structureBatch,
        GodotCoordinateAdapter coords,
        Material craneMat,
        Material concMat,
        Material waterMat)
    {
        roadBatch.AddBox(waterMat, coords.ToGodot(new Vector3d(500480, 7401930, 97.6)), new Vector3(950f, 0.4f, 260f), uvScale: 0.08f);
        structureBatch.AddBox(concMat, coords.ToGodot(new Vector3d(500480, 7401800, 99.1)), new Vector3(920f, 2.2f, 12f));

        var craneCabinMat = GsaMeshBuilder.SolidMat(new Color(0.95f, 0.85f, 0.22f));
        for (var i = 0; i < 2; i++)
        {
            var cp = coords.ToGodot(new Vector3d(500260.0 + i * 240.0, 7401790, 100.0));
            foreach (var offX in new[] { -8f, 8f })
            {
                structureBatch.AddBox(craneMat, cp + new Vector3(offX, 12f, 6f), new Vector3(1.4f, 24f, 1.4f));
                structureBatch.AddBox(craneMat, cp + new Vector3(offX, 12f, -6f), new Vector3(1.4f, 24f, 1.4f));
            }
            structureBatch.AddBox(craneMat, cp + new Vector3(0f, 23f, -10f), new Vector3(20f, 2.0f, 44f));
            structureBatch.AddBox(craneCabinMat, cp + new Vector3(0f, 19f, -18f), new Vector3(4.5f, 3.2f, 4.5f));
        }

        // Single vertex-tinted material for all 35 shipping containers!
        var containerColors = new Color[] { new(0.78f, 0.22f, 0.16f), new(0.16f, 0.44f, 0.72f), new(0.22f, 0.56f, 0.34f), new(0.84f, 0.58f, 0.14f) };
        var containerMat = GsaRetroTextures.CreateWorldMaterial("cont_ware_uv", "warehouse", Colors.White, triplanar: false, vertexColorAlbedo: true);
        for (var s = 0; s < 14; s++)
        {
            var e = 500160.0 + (s % 7) * 58.0;
            var n = 7401680.0 + (s / 7) * 44.0;
            for (var tier = 0; tier < 2 + (s % 2); tier++)
            {
                structureBatch.AddBox(containerMat, coords.ToGodot(new Vector3d(e, n, 101.5 + tier * 3.0)),
                    new Vector3(12f, 2.9f, 3.2f), uvScale: 0.25f, vertexColor: containerColors[(s + tier) % containerColors.Length]);
            }
        }

        var wareMat = GsaRetroTextures.CreateWorldMaterial("port_shed_ware_uv", "warehouse", new Color(0.86f, 0.88f, 0.90f), triplanar: false);
        structureBatch.AddBox(wareMat, coords.ToGodot(new Vector3d(500280, 7401640, 106.0)), new Vector3(44f, 12f, 26f), uvScale: 0.095f);
        structureBatch.AddBox(wareMat, coords.ToGodot(new Vector3d(500440, 7401640, 106.0)), new Vector3(44f, 12f, 26f), uvScale: 0.095f);
        roadBatch.AddBox(GsaMeshBuilder.SolidMat(new Color(0.32f, 0.27f, 0.22f)), coords.ToGodot(new Vector3d(500480, 7401760, 100.05)), new Vector3(820f, 0.25f, 4.2f));
        var railSteel = GsaMeshBuilder.SolidMat(new Color(0.65f, 0.67f, 0.70f));
        roadBatch.AddBox(railSteel, coords.ToGodot(new Vector3d(500480, 7401759.2, 100.22)), new Vector3(820f, 0.14f, 0.18f));
        roadBatch.AddBox(railSteel, coords.ToGodot(new Vector3d(500480, 7401760.8, 100.22)), new Vector3(820f, 0.14f, 0.18f));

        // Moored Cargo Ship
        var shipPos = coords.ToGodot(new Vector3d(500360, 7401875, 99.0));
        structureBatch.AddBox(GsaMeshBuilder.SolidMat(new Color(0.18f, 0.20f, 0.22f)), shipPos + new Vector3(0f, 2.2f, 0f), new Vector3(62f, 7.5f, 16f));
        structureBatch.AddBox(craneMat, shipPos + new Vector3(0f, -0.4f, 0f), new Vector3(63f, 2.2f, 16.4f));
        structureBatch.AddBox(GsaMeshBuilder.SolidMat(new Color(0.90f, 0.91f, 0.92f)), shipPos + new Vector3(-20f, 10.5f, 0f), new Vector3(12f, 11f, 13f));
        structureBatch.AddBox(GsaMeshBuilder.SolidMat(new Color(0.92f, 0.62f, 0.14f)), shipPos + new Vector3(-23f, 17.5f, 0f), new Vector3(4.5f, 5.5f, 4.5f));

        // Harbor Tunnel Entrance Portal
        var tunPos = coords.ToGodot(new Vector3d(500480, 7401320, 100.0));
        structureBatch.AddBox(concMat, tunPos + new Vector3(-9f, 3.75f, 0f), new Vector3(3.5f, 7.5f, 24f));
        structureBatch.AddBox(concMat, tunPos + new Vector3(9f, 3.75f, 0f), new Vector3(3.5f, 7.5f, 24f));
        structureBatch.AddBox(concMat, tunPos + new Vector3(0f, 7.2f, 0f), new Vector3(21.5f, 2.4f, 24f));
        structureBatch.AddBox(GsaMeshBuilder.SolidMat(new Color(0.98f, 0.68f, 0.10f), emit: true, emissionEnergy: 1.2f), tunPos + new Vector3(0f, 6.2f, 12.2f), new Vector3(12f, 1.1f, 0.5f));
    }

    private static void BuildWaterfrontDistrictLandmarks(
        GsaStaticMeshBatcher structureBatch,
        GsaStaticMeshBatcher lampBulbBatch,
        Material poleMat,
        StandardMaterial3D bulbMat,
        List<(Vector3 Position, float Scale)> treeList,
        GodotCoordinateAdapter coords,
        Material steelMat,
        Material sidewalkMat)
    {
        var woodMat = GsaMeshBuilder.SolidMat(new Color(0.52f, 0.36f, 0.22f));
        var bridgeDeckMat = GsaRetroTextures.CreateWorldMaterial("bridge_asphalt_uv", "asphalt", new Color(0.84f, 0.86f, 0.90f), triplanar: false);
        var cableMat = GsaMeshBuilder.SolidMat(new Color(0.24f, 0.28f, 0.30f));
        var yellowLine = GsaMeshBuilder.SolidMat(new Color(0.92f, 0.74f, 0.14f));
        var redBeaconMat = GsaMeshBuilder.SolidMat(Colors.Red, emit: true, emissionEnergy: 2.0f);

        structureBatch.AddBox(bridgeDeckMat, coords.ToGodot(new Vector3d(501775, 7400448, 102.2)), new Vector3(72f, 0.8f, 14f), rotDeg: new Vector3(0f, 0f, 4.2f), uvScale: 0.14f);
        structureBatch.AddBox(bridgeDeckMat, coords.ToGodot(new Vector3d(501845, 7400448, 102.2)), new Vector3(72f, 0.8f, 14f), rotDeg: new Vector3(0f, 0f, -4.2f), uvScale: 0.14f);
        structureBatch.AddBox(yellowLine, coords.ToGodot(new Vector3d(501775, 7400448, 102.63)), new Vector3(70f, 0.05f, 0.32f), rotDeg: new Vector3(0f, 0f, 4.2f));
        structureBatch.AddBox(yellowLine, coords.ToGodot(new Vector3d(501845, 7400448, 102.63)), new Vector3(70f, 0.05f, 0.32f), rotDeg: new Vector3(0f, 0f, -4.2f));
        structureBatch.AddBox(bridgeDeckMat, coords.ToGodot(new Vector3d(501810, 7400448, 104.8)), new Vector3(64f, 0.9f, 14.2f), uvScale: 0.14f);
        structureBatch.AddBox(steelMat, coords.ToGodot(new Vector3d(501810, 7400441.2, 105.6)), new Vector3(130f, 1.0f, 0.35f));
        structureBatch.AddBox(steelMat, coords.ToGodot(new Vector3d(501810, 7400454.8, 105.6)), new Vector3(130f, 1.0f, 0.35f));

        foreach (var tx in new[] { 501780.0, 501840.0 })
        {
            structureBatch.AddBox(steelMat, coords.ToGodot(new Vector3d(tx, 7400441.5, 109.0)), new Vector3(1.6f, 18f, 1.6f));
            structureBatch.AddBox(steelMat, coords.ToGodot(new Vector3d(tx, 7400454.5, 109.0)), new Vector3(1.6f, 18f, 1.6f));
            structureBatch.AddBox(steelMat, coords.ToGodot(new Vector3d(tx, 7400448.0, 117.5)), new Vector3(1.6f, 1.8f, 14.5f));
            structureBatch.AddBox(redBeaconMat, coords.ToGodot(new Vector3d(tx, 7400441.5, 118.6)), new Vector3(0.55f, 0.55f, 0.55f));
            structureBatch.AddBox(cableMat, coords.ToGodot(new Vector3d(tx - 11.0, 7400441.5, 111.4)), new Vector3(26f, 0.16f, 0.16f), rotDeg: new Vector3(0f, 0f, -30f));
            structureBatch.AddBox(cableMat, coords.ToGodot(new Vector3d(tx + 11.0, 7400441.5, 111.4)), new Vector3(26f, 0.16f, 0.16f), rotDeg: new Vector3(0f, 0f, 30f));
            structureBatch.AddBox(cableMat, coords.ToGodot(new Vector3d(tx - 11.0, 7400454.5, 111.4)), new Vector3(26f, 0.16f, 0.16f), rotDeg: new Vector3(0f, 0f, -30f));
            structureBatch.AddBox(cableMat, coords.ToGodot(new Vector3d(tx + 11.0, 7400454.5, 111.4)), new Vector3(26f, 0.16f, 0.16f), rotDeg: new Vector3(0f, 0f, 30f));
        }

        var glassMat = GsaRetroTextures.CreateWorldMaterial("wf_glass_flank_uv", "glass_tower", Colors.White, triplanar: false);
        structureBatch.AddBox(glassMat, coords.ToGodot(new Vector3d(501742, 7400488, 117.0)), new Vector3(24f, 34f, 24f), uvScale: 0.09f);
        structureBatch.AddBox(glassMat, coords.ToGodot(new Vector3d(501742, 7400408, 114.0)), new Vector3(24f, 28f, 24f), uvScale: 0.09f);

        structureBatch.AddBox(sidewalkMat, coords.ToGodot(new Vector3d(501790, 7400490, 100.2)), new Vector3(12f, 1.8f, 920f), uvScale: 0.24f);
        structureBatch.AddBox(woodMat, coords.ToGodot(new Vector3d(501930, 7400448, 100.3)), new Vector3(95f, 0.9f, 18f));

        for (var n = 7400080.0; n <= 7400900.0; n += 65.0)
        {
            treeList.Add((coords.ToGodot(new Vector3d(501785.0, n, 100.8)), 1.25f));
            var lp = coords.ToGodot(new Vector3d(501793.0, n + 18.0, 100.8));
            structureBatch.AddBox(poleMat, lp + new Vector3(0f, 2.6f, 0f), new Vector3(0.14f, 5.2f, 0.14f));
            structureBatch.AddBox(poleMat, lp + new Vector3(0.50f, 5.15f, 0f), new Vector3(1.1f, 0.10f, 0.10f));
            lampBulbBatch.AddBox(bulbMat, lp + new Vector3(0.98f, 5.06f, 0f), new Vector3(0.34f, 0.10f, 0.22f));
        }

        // Docked Survey Boat next to Pier
        var boatPos = coords.ToGodot(new Vector3d(501945, 7400468, 98.4));
        structureBatch.AddBox(GsaMeshBuilder.SolidMat(new Color(0.92f, 0.94f, 0.96f)), boatPos + new Vector3(0f, 0.6f, 0f), new Vector3(11.5f, 1.8f, 4.2f));
        structureBatch.AddBox(GsaMeshBuilder.SolidMat(new Color(0.14f, 0.56f, 0.72f)), boatPos + new Vector3(0f, 1.1f, 0f), new Vector3(11.6f, 0.35f, 4.3f));
        structureBatch.AddBox(GsaMeshBuilder.SolidMat(new Color(0.16f, 0.24f, 0.32f)), boatPos + new Vector3(-1.2f, 1.8f, 0f), new Vector3(4.2f, 1.6f, 2.8f));
        structureBatch.AddBox(GsaMeshBuilder.SolidMat(Colors.Yellow, emit: true), boatPos + new Vector3(-1.2f, 3.2f, 0f), new Vector3(0.25f, 2.2f, 0.25f));
    }

    private void BuildSerraMountainLandmarks(
        Node3D parent,
        GsaStaticMeshBatcher structureBatch,
        List<(Vector3 Position, float Scale)> treeList,
        GodotCoordinateAdapter coords,
        Material asphaltMat,
        Material concMat,
        Material towerRedMat)
    {
        var serra = new Node3D { Name = "SerraMountain" };
        var parkMat = GsaRetroTextures.CreateWorldMaterial("city_park_grass_uv", "park_grass", Colors.White, triplanar: false);

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
                Vector2 UvAt(Vector3 v) => new(v.X * 0.10f, v.Z * 0.10f);
                st.SetUV(UvAt(v00)); st.AddVertex(v00);
                st.SetUV(UvAt(v11)); st.AddVertex(v11);
                st.SetUV(UvAt(v10)); st.AddVertex(v10);
                st.SetUV(UvAt(v00)); st.AddVertex(v00);
                st.SetUV(UvAt(v01)); st.AddVertex(v01);
                st.SetUV(UvAt(v11)); st.AddVertex(v11);
            }
        }
        st.GenerateNormals();
        serra.AddChild(new MeshInstance3D
        {
            Name = "SerraSlopedTerrain",
            Mesh = st.Commit(),
            MaterialOverride = parkMat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });

        var sumElev = SampleDriveableElevationEnu(501860, 7401860);
        var sumPos = coords.ToGodot(new Vector3d(501860, 7401860, sumElev));
        structureBatch.AddBox(concMat, sumPos + new Vector3(0f, 0.5f, 0f), new Vector3(36f, 1.2f, 36f));
        structureBatch.AddCylinder(towerRedMat, sumPos + new Vector3(0f, 18f, 0f), 0.35f, 1.8f, 36f, radialSegments: 7);
        structureBatch.AddBox(GsaMeshBuilder.SolidMat(Colors.Red, emit: true, emissionEnergy: 2.5f), sumPos + new Vector3(0f, 36.5f, 0f), new Vector3(1.1f, 1.1f, 1.1f));

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
            roadSt.SetUV(new Vector2(0f, (float)t0 * 80f)); roadSt.AddVertex(p0L);
            roadSt.SetUV(new Vector2(1.8f, (float)t1 * 80f)); roadSt.AddVertex(p1R);
            roadSt.SetUV(new Vector2(1.8f, (float)t0 * 80f)); roadSt.AddVertex(p0R);
            roadSt.SetUV(new Vector2(0f, (float)t0 * 80f)); roadSt.AddVertex(p0L);
            roadSt.SetUV(new Vector2(0f, (float)t1 * 80f)); roadSt.AddVertex(p1L);
            roadSt.SetUV(new Vector2(1.8f, (float)t1 * 80f)); roadSt.AddVertex(p1R);
        }
        roadSt.GenerateNormals();
        serra.AddChild(new MeshInstance3D
        {
            Name = "SerraSummitHighway",
            Mesh = roadSt.Commit(),
            MaterialOverride = asphaltMat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });

        for (var i = 0; i < 24; i++)
        {
            var e = 501080.0 + (i % 6) * 155.0;
            var n = 7401080.0 + (i / 6) * 210.0;
            var elev = SampleDriveableElevationEnu(e, n);
            treeList.Add((coords.ToGodot(new Vector3d(e, n, elev)), 1.35f));
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
