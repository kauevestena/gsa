using Gsa.Core.Geometry;
using Gsa.Core.Traffic;
using Gsa.Core.World;
using Godot;

namespace Gsa.Engine.Visuals;

public sealed record AnimatedTrafficSignal(
    Node3D Root,
    SignalGroup Group,
    StandardMaterial3D RedMat,
    StandardMaterial3D AmberMat,
    StandardMaterial3D GreenMat);

/// <summary>Builds GTA III urban streetscapes, storefront architecture, traffic signals, skyline blocks, and beacon rings.</summary>
public sealed class GsaCityVisualBuilder
{
    public List<AnimatedTrafficSignal> Signals { get; } = [];
    public List<Node3D> SpinningBeacons { get; } = [];

    public void Clear()
    {
        Signals.Clear();
        SpinningBeacons.Clear();
    }

    public void BuildTutorialBlockVisuals(
        Node3D visualRoot,
        WorldSceneManifest manifest,
        GodotCoordinateAdapter coords,
        GsaAtmosphereController atmosphere)
    {
        Clear();

        // 1. Extended Asphalt Base & Surrounding Horizon Ground (world UVs, no triplanar, no shadow cast)
        var groundBatch = new GsaStaticMeshBatcher();
        var horizonAsphaltMat = GsaRetroTextures.CreateWorldMaterial(
            "horizon_asphalt_uv", "asphalt", new Color(0.85f, 0.87f, 0.90f),
            triplanar: false);
        groundBatch.AddBox(horizonAsphaltMat, coords.ToGodot(new Vector3d(500120, 7400030, 99.45)),
            new Vector3(1100f, 0.2f, 1100f), uvScale: 0.12f);
        groundBatch.BuildSeparatedNodes(visualRoot, "HorizonDeck", GeometryInstance3D.ShadowCastingSetting.Off);

        BuildCommerceStreetDetails(visualRoot, manifest, coords, atmosphere, includeBeacons: true);

        // 7. Surrounding City Skyline Blocks batched into 4 spatial quadrants
        BuildSurroundingSkyline(visualRoot, coords, minPlayE: 500000, maxPlayE: 500240, minPlayN: 7399980, maxPlayN: 7400060);
    }

    public void BuildCommerceStreetDetails(
        Node3D visualRoot,
        WorldSceneManifest manifest,
        GodotCoordinateAdapter coords,
        GsaAtmosphereController atmosphere,
        bool includeBeacons)
    {
        var roadBatch = new GsaStaticMeshBatcher();
        var bldgBatch = new GsaStaticMeshBatcher();
        var treeList = new List<(Vector3 Position, float Scale)>(20);

        var sidewalkMat = GsaRetroTextures.CreateWorldMaterial(
            "city_sidewalk_uv", "sidewalk", Colors.White, triplanar: false);
        var curbMat = GsaMeshBuilder.SolidMat(new Color(0.42f, 0.44f, 0.46f));
        var stripeYellow = GsaMeshBuilder.SolidMat(new Color(0.92f, 0.74f, 0.14f), roughness: 0.75f);
        var stripeWhite = GsaMeshBuilder.SolidMat(new Color(0.86f, 0.88f, 0.86f), roughness: 0.75f);

        void AddRoadBox(Vector3d posEnu, Vector3d sizeEnu, Material mat, float uvScale = 0.24f) =>
            roadBatch.AddBox(mat, coords.ToGodot(posEnu), new Vector3((float)sizeEnu.X, (float)sizeEnu.Z, (float)sizeEnu.Y), uvScale: uvScale);

        // South Commerce Promenade (Y = 7399997..7400005) where Elias walks & shoots facades
        AddRoadBox(new Vector3d(500120, 7400001.0, 99.96), new Vector3d(244, 8.2, 0.16), sidewalkMat, 0.24f);
        AddRoadBox(new Vector3d(500120, 7399996.8, 99.99), new Vector3d(244, 0.35, 0.22), curbMat);

        // South Commerce Boulevard (Y = 7399981..7399996.6)
        AddRoadLaneMarkings(roadBatch, coords, xStart: 500004, xEnd: 500236, yCenter: 7399989.0, zHeight: 100.015, stripeYellow, stripeWhite);

        // Opposite (South) Sidewalk, Curb & Downtown Storefront Row
        AddRoadBox(new Vector3d(500120, 7399981.2, 99.99), new Vector3d(244, 0.35, 0.22), curbMat);
        AddRoadBox(new Vector3d(500120, 7399978.0, 99.96), new Vector3d(244, 6.2, 0.16), sidewalkMat, 0.24f);

        for (var e = 500018.0; e <= 500222.0; e += 26.0)
        {
            var bh = 11f + ((int)e % 3) * 5f;
            var style = (((int)e / 26) % 3) switch
            {
                0 => ("bldg_opp_comm_uv", "facade_commercial"),
                1 => ("bldg_opp_res_uv", "facade_residential"),
                _ => ("bldg_opp_conc_uv", "facade_concrete"),
            };
            var bMat = GsaRetroTextures.CreateWorldMaterial(style.Item1, style.Item2, Colors.White, triplanar: false, nightWindowGlow: true);
            var pos = coords.ToGodot(new Vector3d(e, 7399967.5, 100.0 + bh * 0.5));
            bldgBatch.AddBox(bMat, pos, new Vector3(22f, bh, 14f), uvScale: 0.095f);
            if (!includeBeacons)
            {
                var bBody = new StaticBody3D { Name = $"SouthBlock_{(int)e}", Position = pos, CollisionLayer = 1, CollisionMask = 1 };
                bBody.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(22f, bh, 14f) } });
                visualRoot.AddChild(bBody);
            }

            treeList.Add((coords.ToGodot(new Vector3d(e + 9.0, 7399979.2, 100.0)), 0.95f));
        }

        // North Avenue corridor (Y = 7400043..7400051) + North Sidewalk
        AddRoadLaneMarkings(roadBatch, coords, xStart: 500010, xEnd: 500230, yCenter: 7400047.0, zHeight: 100.025, stripeYellow, stripeWhite);
        AddRoadBox(new Vector3d(500120, 7400056.0, 100.02), new Vector3d(236, 5.0, 0.16), sidewalkMat, 0.24f);
        AddRoadBox(new Vector3d(500112, 7400029.5, 100.01), new Vector3d(206, 17.5, 0.14), sidewalkMat, 0.24f);

        // Crosswalk stripes at main intersection (X = 500112)
        var cwMat = GsaRetroTextures.CreateWorldMaterial("crosswalk_mat_uv", "crosswalk", Colors.White, triplanar: false);
        roadBatch.AddBoxNormalizedUv(cwMat, coords.ToGodot(new Vector3d(500112, 7400041.0, 100.04)), new Vector3(3.4f, 0.03f, 3.2f));
        roadBatch.AddBoxNormalizedUv(cwMat, coords.ToGodot(new Vector3d(500112, 7400053.0, 100.04)), new Vector3(3.4f, 0.03f, 3.2f));
        roadBatch.AddBoxNormalizedUv(cwMat, coords.ToGodot(new Vector3d(500120, 7399989.0, 100.03)), new Vector3(3.6f, 0.03f, 14.0f));

        // 3. Architectural Storefront Dressings on building_shell_01..20 (strictly above Z=102.5 so facade_01..20 rays stay clean!)
        var awningColors = new[]
        {
            new Color(0.78f, 0.22f, 0.18f),
            new Color(0.16f, 0.48f, 0.36f),
            new Color(0.20f, 0.38f, 0.68f),
            new Color(0.84f, 0.54f, 0.14f),
        };
        var tankMat = GsaMeshBuilder.SolidMat(new Color(0.18f, 0.44f, 0.72f), roughness: 0.45f);
        var roofTrimMat = GsaMeshBuilder.SolidMat(new Color(0.32f, 0.34f, 0.36f));
        var acMat = GsaMeshBuilder.SolidMat(new Color(0.76f, 0.77f, 0.75f));

        var shellsById = new Dictionary<string, WorldBoxObject>(StringComparer.Ordinal);
        foreach (var obj in manifest.Objects)
        {
            if (obj.Id.StartsWith("building_shell_", StringComparison.Ordinal))
                shellsById[obj.Id] = obj;
        }

        for (var i = 1; i <= 20; i++)
        {
            if (!shellsById.TryGetValue($"building_shell_{i:00}", out var shell))
                continue;
            var centerG = coords.ToGodot(shell.PositionEnu);
            var roofY = centerG.Y + (float)(shell.SizeEnu.Z * 0.5);
            var frontZ = centerG.Z + (float)(shell.SizeEnu.Y * 0.5);

            bldgBatch.AddBox(roofTrimMat, new Vector3(centerG.X, roofY + 0.22f, centerG.Z), new Vector3(8.2f, 0.45f, 10.2f));
            if (i % 2 == 1)
                bldgBatch.AddCylinder(tankMat, new Vector3(centerG.X - 2.1f, roofY + 1.05f, centerG.Z - 2.0f), 0.85f, 0.78f, 1.25f, radialSegments: 8);
            if (i % 3 != 0)
                bldgBatch.AddBox(acMat, new Vector3(centerG.X + 2.0f, roofY + 0.65f, centerG.Z + 1.5f), new Vector3(1.2f, 0.7f, 1.0f));

            var awningMat = GsaMeshBuilder.SolidMat(awningColors[i % awningColors.Length]);
            bldgBatch.AddBox(awningMat, new Vector3(centerG.X, 2.75f, frontZ + 0.75f), new Vector3(7.4f, 0.22f, 1.5f), rotDeg: new Vector3(18f, 0f, 0f));

            if (i % 4 == 0)
            {
                var signTitles = new[] { "PIZZA DATUM", "BODEGA RTK", "GEO-TECH", "AUTO SOM", "MAPAFACIL" };
                var signColors = new Color[]
                {
                    new(0.88f, 0.20f, 0.14f),
                    new(0.14f, 0.62f, 0.82f),
                    new(0.86f, 0.64f, 0.12f),
                    new(0.18f, 0.72f, 0.42f),
                    new(0.68f, 0.22f, 0.76f),
                };
                var idx = (i / 4) % signTitles.Length;
                var sign = GsaMeshBuilder.BuildStorefrontSignboard($"ShopSign_{i:00}", signColors[idx], signTitles[idx], width: 4.2f);
                sign.Position = new Vector3(centerG.X, 3.65f, frontZ + 0.22f);
                visualRoot.AddChild(sign);
            }
        }

        // Office building behind office_entry (500232, 7400005)
        var officePos = coords.ToGodot(new Vector3d(500232, 7400010.8, 104.5));
        bldgBatch.AddBox(
            GsaRetroTextures.CreateWorldMaterial("bldg_office_uv", "facade_concrete", new Color(0.88f, 0.96f, 0.94f), triplanar: false, nightWindowGlow: true),
            officePos, new Vector3(10f, 9f, 10f), uvScale: 0.09f);
        bldgBatch.AddBox(
            GsaMeshBuilder.SolidMat(new Color(0.12f, 0.56f, 0.44f)),
            coords.ToGodot(new Vector3d(500232, 7400004.5, 103.3)), new Vector3(5.2f, 0.28f, 2.4f));
        var officeSign = GsaMeshBuilder.BuildStorefrontSignboard("OfficeSign", new Color(0.12f, 0.68f, 0.58f), "GSA SURVEY HQ", width: 4.6f);
        officeSign.Position = coords.ToGodot(new Vector3d(500232, 7400005.2, 104.3));
        visualRoot.AddChild(officeSign);

        // Street furniture: bus shelter, fire hydrants, trash dumpsters
        var busStop = GsaMeshBuilder.BuildBusStopShelter("BusStopCommerce");
        busStop.Position = coords.ToGodot(new Vector3d(500084.0, 7399998.0, 100.04));
        visualRoot.AddChild(busStop);

        var hydrantRed = GsaMeshBuilder.SolidMat(new Color(0.84f, 0.14f, 0.12f), roughness: 0.45f);
        var hydrantSilver = GsaMeshBuilder.SolidMat(new Color(0.75f, 0.77f, 0.80f));
        foreach (var hx in new[] { 500038.0, 500158.0, 500216.0 })
        {
            var hp = coords.ToGodot(new Vector3d(hx, 7399997.4, 100.04));
            bldgBatch.AddCylinder(hydrantRed, hp + new Vector3(0f, 0.38f, 0f), 0.13f, 0.16f, 0.76f, radialSegments: 7);
            bldgBatch.AddBox(hydrantSilver, hp + new Vector3(0f, 0.54f, 0f), new Vector3(0.42f, 0.10f, 0.10f));
        }
        var binBody = GsaMeshBuilder.SolidMat(new Color(0.16f, 0.38f, 0.24f), roughness: 0.7f);
        var binLid = GsaMeshBuilder.SolidMat(new Color(0.12f, 0.13f, 0.14f), roughness: 0.55f);
        foreach (var dx in new[] { 500062.0, 500136.0 })
        {
            var dp = coords.ToGodot(new Vector3d(dx, 7400003.6, 100.04));
            bldgBatch.AddBox(binBody, dp + new Vector3(0f, 0.56f, 0f), new Vector3(1.85f, 1.05f, 1.10f));
            bldgBatch.AddBox(binLid, dp + new Vector3(0f, 1.12f, 0f), new Vector3(1.92f, 0.12f, 1.16f));
        }

        // 4. GTA III Glowing Spinning Beacon Rings on Key Interactive Points
        if (includeBeacons)
        {
            AddBeacon(visualRoot, coords, "BeaconOffice", new Vector3d(500232, 7400002.6, 100.05), new Color(0.15f, 0.88f, 0.85f), 1.35f);
            AddBeacon(visualRoot, coords, "BeaconUnload", new Vector3d(500225, 7400002.0, 100.05), new Color(0.98f, 0.76f, 0.16f), 1.25f);
            AddBeacon(visualRoot, coords, "BeaconCtrl1", new Vector3d(500010, 7400002.0, 100.05), new Color(0.24f, 0.92f, 0.46f), 0.75f);
            AddBeacon(visualRoot, coords, "BeaconCtrl2", new Vector3d(500230, 7400025.0, 100.05), new Color(0.24f, 0.92f, 0.46f), 0.75f);
        }

        // 5. Traffic Signals synced to TrafficBenchSimulator
        AddSignal(visualRoot, coords, "SignalCenterEW", new Vector3d(500105, 7400040.5, 100.0), 0f, SignalGroup.EastWestRoad);
        AddSignal(visualRoot, coords, "SignalCenterNS", new Vector3d(500119, 7400053.5, 100.0), 90f, SignalGroup.NorthSouthRoad);
        AddSignal(visualRoot, coords, "SignalEastEW", new Vector3d(500208, 7400040.5, 100.0), 0f, SignalGroup.EastWestRoad);

        // 6. Streetlamps (batched poles/arms + shared bulb material) & batched Street Trees
        var poleMat = GsaMeshBuilder.SolidMat(new Color(0.24f, 0.27f, 0.30f));
        var sharedBulbMat = GsaMeshBuilder.SolidStandardMat(new Color(1.0f, 0.88f, 0.56f), emit: true, emissionEnergy: 0.0f);
        atmosphere.RegisterStreetlamp(sharedBulbMat, null);
        var bulbBatch = new GsaStaticMeshBatcher();

        for (var x = 500016.0; x <= 500228.0; x += 28.0)
        {
            var posS = coords.ToGodot(new Vector3d(x, 7399997.2, 100.0));
            // Yaw -90 deg: local +X -> world +Z, local +Z -> world -X
            bldgBatch.AddBox(poleMat, posS + new Vector3(0f, 2.6f, 0f), new Vector3(0.14f, 5.2f, 0.14f));
            bldgBatch.AddBox(poleMat, posS + new Vector3(0f, 5.15f, 0.50f), new Vector3(0.10f, 0.10f, 1.1f));
            bulbBatch.AddBox(sharedBulbMat, posS + new Vector3(0f, 5.06f, 0.98f), new Vector3(0.22f, 0.10f, 0.34f));
            if (Math.Abs(x - 500212) < 35)
            {
                var lightS = new OmniLight3D
                {
                    Name = $"LampGlowS_{(int)x}",
                    Position = posS + new Vector3(0f, 4.85f, 0.98f),
                    LightColor = new Color(1.0f, 0.84f, 0.52f),
                    LightEnergy = 1.8f,
                    OmniRange = 16f,
                    Visible = false,
                };
                visualRoot.AddChild(lightS);
                atmosphere.RegisterStreetlamp(sharedBulbMat, lightS);
            }

            var posN = coords.ToGodot(new Vector3d(x, 7400054.8, 100.0));
            // Yaw +90 deg: local +X -> world -Z
            bldgBatch.AddBox(poleMat, posN + new Vector3(0f, 2.6f, 0f), new Vector3(0.14f, 5.2f, 0.14f));
            bldgBatch.AddBox(poleMat, posN + new Vector3(0f, 5.15f, -0.50f), new Vector3(0.10f, 0.10f, 1.1f));
            bulbBatch.AddBox(sharedBulbMat, posN + new Vector3(0f, 5.06f, -0.98f), new Vector3(0.22f, 0.10f, 0.34f));
        }

        foreach (var tx in new[] { 500015.0, 500055.0, 500095.0, 500145.0, 500185.0, 500222.0 })
            treeList.Add((coords.ToGodot(new Vector3d(tx, 7400057.2, 100.0)), 0.95f));

        roadBatch.BuildSeparatedNodes(visualRoot, "CommerceRoads", GeometryInstance3D.ShadowCastingSetting.Off);
        bldgBatch.BuildSeparatedNodes(visualRoot, "CommerceProps", GeometryInstance3D.ShadowCastingSetting.On);
        bulbBatch.BuildSeparatedNodes(visualRoot, "CommerceLampBulbs", GeometryInstance3D.ShadowCastingSetting.Off);
        GsaMeshBuilder.BuildBatchedStreetTrees(visualRoot, "CommerceTrees", treeList);
    }

    public void BuildSurroundingSkyline(
        Node3D visualRoot,
        GodotCoordinateAdapter coords,
        double minPlayE,
        double maxPlayE,
        double minPlayN,
        double maxPlayN)
    {
        var skyline = new Node3D { Name = "SkylineBackdrop" };
        visualRoot.AddChild(skyline);

        var styles = new[]
        {
            ("bldg_sky_comm_uv", "facade_commercial", new Color(0.95f, 0.90f, 0.85f)),
            ("bldg_sky_res_uv", "facade_residential", new Color(0.92f, 0.90f, 0.84f)),
            ("bldg_sky_conc_uv", "facade_concrete", new Color(0.82f, 0.85f, 0.88f)),
            ("bldg_sky_glass_uv", "glass_tower", new Color(0.85f, 0.92f, 0.98f)),
        };

        // Split into 4 spatial quadrants so frustum culling discards off-screen skyline chunks cleanly
        var quadrants = new[]
        {
            new GsaStaticMeshBatcher(),
            new GsaStaticMeshBatcher(),
            new GsaStaticMeshBatcher(),
            new GsaStaticMeshBatcher(),
        };
        var midE = (minPlayE + maxPlayE) * 0.5;
        var midN = (minPlayN + maxPlayN) * 0.5;
        var mastMat = GsaMeshBuilder.SolidMat(new Color(0.55f, 0.58f, 0.62f));
        var beaconMat = GsaMeshBuilder.SolidMat(Colors.Red, emit: true, emissionEnergy: 1.8f);

        var rng = new Random(200103);
        const double step = 36.0;
        for (var e = minPlayE - 220.0; e <= maxPlayE + 220.0; e += step)
        {
            for (var n = minPlayN - 200.0; n <= maxPlayN + 200.0; n += step)
            {
                if (e > minPlayE - 24.0 && e < maxPlayE + 24.0 && n > minPlayN - 28.0 && n < maxPlayN + 24.0)
                    continue;
                if (rng.NextDouble() < 0.34)
                    continue;

                var w = 18f + (float)rng.NextDouble() * 10f;
                var d = 18f + (float)rng.NextDouble() * 10f;
                var h = 14f + (float)rng.NextDouble() * 34f;
                var style = styles[rng.Next(styles.Length)];
                var mat = GsaRetroTextures.CreateWorldMaterial(
                    style.Item1, style.Item2, style.Item3,
                    triplanar: false,
                    nightWindowGlow: style.Item2 != "glass_tower");

                var qIdx = (e >= midE ? 1 : 0) + (n >= midN ? 2 : 0);
                var batch = quadrants[qIdx];
                var pos = coords.ToGodot(new Vector3d(e, n, 100.0 + h * 0.5 - 0.5));
                batch.AddBox(mat, pos, new Vector3(w, h, d), uvScale: 0.085f);
                if (h > 24f)
                {
                    var ph = 5.5f + ((int)e % 4);
                    batch.AddBox(mat, new Vector3(pos.X, h + ph * 0.5f - 0.5f, pos.Z), new Vector3(w * 0.64f, ph, d * 0.64f), uvScale: 0.085f);
                    batch.AddBox(mastMat, new Vector3(pos.X, h + ph + 2.75f, pos.Z), new Vector3(0.35f, 6.5f, 0.35f));
                    batch.AddBox(beaconMat, new Vector3(pos.X, h + ph + 6.1f, pos.Z), new Vector3(0.55f, 0.55f, 0.55f));
                }
            }
        }

        for (var q = 0; q < quadrants.Length; q++)
        {
            if (quadrants[q].BuildSingleMeshInstance($"SkylineQ{q}", GeometryInstance3D.ShadowCastingSetting.Off, visibilityRangeEnd: 420f) is { } node)
                skyline.AddChild(node);
        }
    }

    public void UpdateAnimatedProps(double dt, TrafficBenchSimulator? traffic)
    {
        var fdt = (float)dt;
        foreach (var beacon in SpinningBeacons)
            beacon.RotateY(fdt * 1.8f);

        if (traffic is null)
            return;

        foreach (var sig in Signals)
        {
            var aspect = traffic.GetSignalAspect(sig.Group);
            sig.RedMat.EmissionEnergyMultiplier = aspect == SignalAspect.Red ? 2.4f : 0.08f;
            sig.AmberMat.EmissionEnergyMultiplier = aspect == SignalAspect.Amber ? 2.4f : 0.08f;
            sig.GreenMat.EmissionEnergyMultiplier = aspect == SignalAspect.Green ? 2.2f : 0.08f;
        }
    }

    private void AddBeacon(Node3D parent, GodotCoordinateAdapter coords, string name, Vector3d posEnu, Color color, float radius)
    {
        var beacon = GsaMeshBuilder.BuildBeaconRing(name, color, radius, 1.0f);
        beacon.Position = coords.ToGodot(posEnu);
        parent.AddChild(beacon);
        SpinningBeacons.Add(beacon);
    }

    private void AddSignal(Node3D parent, GodotCoordinateAdapter coords, string name, Vector3d posEnu, float yawDeg, SignalGroup group)
    {
        var (node, red, amber, green) = GsaMeshBuilder.BuildTrafficSignal(name);
        node.Position = coords.ToGodot(posEnu);
        node.RotationDegrees = new Vector3(0f, yawDeg, 0f);
        parent.AddChild(node);
        Signals.Add(new AnimatedTrafficSignal(node, group, red, amber, green));
    }

    private static void AddRoadLaneMarkings(
        GsaStaticMeshBatcher batch,
        GodotCoordinateAdapter coords,
        double xStart,
        double xEnd,
        double yCenter,
        double zHeight,
        Material yellowMat,
        Material whiteMat)
    {
        var len = (float)(xEnd - xStart);
        var midX = (xStart + xEnd) * 0.5;
        batch.AddBox(yellowMat, coords.ToGodot(new Vector3d(midX, yCenter - 0.14, zHeight)), new Vector3(len, 0.02f, 0.12f));
        batch.AddBox(yellowMat, coords.ToGodot(new Vector3d(midX, yCenter + 0.14, zHeight)), new Vector3(len, 0.02f, 0.12f));
        for (var x = xStart + 4.0; x <= xEnd - 4.0; x += 10.0)
        {
            batch.AddBox(whiteMat, coords.ToGodot(new Vector3d(x, yCenter - 3.6, zHeight)), new Vector3(4.0f, 0.02f, 0.12f));
            batch.AddBox(whiteMat, coords.ToGodot(new Vector3d(x, yCenter + 3.6, zHeight)), new Vector3(4.0f, 0.02f, 0.12f));
        }
    }
}
