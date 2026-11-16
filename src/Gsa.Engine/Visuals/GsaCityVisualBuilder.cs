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

        // 1. Extended Asphalt Base & Surrounding Horizon Ground
        var outerGround = new MeshInstance3D
        {
            Name = "HorizonAsphaltDeck",
            Position = coords.ToGodot(new Vector3d(500120, 7400030, 99.45)),
            Mesh = new BoxMesh { Size = new Vector3(1100f, 0.2f, 1100f) },
            MaterialOverride = GsaRetroTextures.CreateWorldMaterial(
                "horizon_asphalt", "asphalt", new Color(0.85f, 0.87f, 0.90f), new Vector3(0.12f, 0.12f, 0.12f), triplanar: true),
        };
        visualRoot.AddChild(outerGround);

        BuildCommerceStreetDetails(visualRoot, manifest, coords, atmosphere, includeBeacons: true);

        // 7. Surrounding City Skyline Blocks so horizon looks like Liberty City instead of a void!
        BuildSurroundingSkyline(visualRoot, coords, minPlayE: 500000, maxPlayE: 500240, minPlayN: 7399980, maxPlayN: 7400060);
    }

    public void BuildCommerceStreetDetails(
        Node3D visualRoot,
        WorldSceneManifest manifest,
        GodotCoordinateAdapter coords,
        GsaAtmosphereController atmosphere,
        bool includeBeacons)
    {
        // 2. Raised Concrete Sidewalks & Commerce Street Promenade (in front of facades 01..20!)
        var sidewalkMat = GsaRetroTextures.CreateWorldMaterial(
            "city_sidewalk", "sidewalk", Colors.White, new Vector3(0.24f, 0.24f, 0.24f), triplanar: true);
        var curbMat = GsaMeshBuilder.SolidMat(new Color(0.42f, 0.44f, 0.46f));
        var stripeYellow = GsaMeshBuilder.SolidMat(new Color(0.92f, 0.74f, 0.14f), roughness: 0.75f);
        var stripeWhite = GsaMeshBuilder.SolidMat(new Color(0.86f, 0.88f, 0.86f), roughness: 0.75f);

        // South Commerce Promenade (Y = 7399997..7400005) where Elias walks & shoots facades
        AddDeck(visualRoot, coords, "CommercePromenade", new Vector3d(500120, 7400001.0, 99.96), new Vector3d(244, 8.2, 0.16), sidewalkMat);
        AddDeck(visualRoot, coords, "CommerceCurbS", new Vector3d(500120, 7399996.8, 99.99), new Vector3d(244, 0.35, 0.22), curbMat);

        // South Commerce Boulevard (Y = 7399981..7399996.6)
        AddRoadLaneMarkings(visualRoot, coords, xStart: 500004, xEnd: 500236, yCenter: 7399989.0, zHeight: 100.015, stripeYellow, stripeWhite);

        // Opposite (South) Sidewalk, Curb & Downtown Storefront Row so Commerce Blvd feels like a real 2-sided street canyon
        AddDeck(visualRoot, coords, "CommerceCurbSouthOpp", new Vector3d(500120, 7399981.2, 99.99), new Vector3d(244, 0.35, 0.22), curbMat);
        AddDeck(visualRoot, coords, "CommerceSouthSidewalk", new Vector3d(500120, 7399978.0, 99.96), new Vector3d(244, 6.2, 0.16), sidewalkMat);
        for (var e = 500018.0; e <= 500222.0; e += 26.0)
        {
            var bh = 11f + ((int)e % 3) * 5f;
            var style = (((int)e / 26) % 3) switch
            {
                0 => ("bldg_opp_comm", "facade_commercial"),
                1 => ("bldg_opp_res", "facade_residential"),
                _ => ("bldg_opp_conc", "facade_concrete"),
            };
            var bMat = GsaRetroTextures.CreateWorldMaterial(style.Item1, style.Item2, Colors.White, new Vector3(0.095f, 0.095f, 0.095f), triplanar: true, nightWindowGlow: true);
            var pos = coords.ToGodot(new Vector3d(e, 7399967.5, 100.0 + bh * 0.5));
            if (!includeBeacons)
            {
                var bBody = new StaticBody3D { Name = $"SouthBlock_{(int)e}", Position = pos, CollisionLayer = 1, CollisionMask = 1 };
                bBody.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(22f, bh, 14f) }, MaterialOverride = bMat });
                bBody.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(22f, bh, 14f) } });
                visualRoot.AddChild(bBody);
            }
            else
            {
                visualRoot.AddChild(GsaMeshBuilder.Box($"SouthBlock_{(int)e}", new Vector3(22f, bh, 14f), pos, bMat));
            }

            var tree = GsaMeshBuilder.BuildStreetTree($"TreeOpp_{(int)e}", 0.95f);
            tree.Position = coords.ToGodot(new Vector3d(e + 9.0, 7399979.2, 100.0));
            visualRoot.AddChild(tree);
        }

        // North Avenue corridor (Y = 7400043..7400051) + North Sidewalk
        AddRoadLaneMarkings(visualRoot, coords, xStart: 500010, xEnd: 500230, yCenter: 7400047.0, zHeight: 100.025, stripeYellow, stripeWhite);
        AddDeck(visualRoot, coords, "NorthSidewalk", new Vector3d(500120, 7400056.0, 100.02), new Vector3d(236, 5.0, 0.16), sidewalkMat);
        AddDeck(visualRoot, coords, "CentralPlazaWalk", new Vector3d(500112, 7400029.5, 100.01), new Vector3d(206, 17.5, 0.14), sidewalkMat);

        // Crosswalk stripes at main intersection (X = 500112)
        var cwMat = GsaRetroTextures.CreateWorldMaterial("crosswalk_mat", "crosswalk", Colors.White, new Vector3(1f, 1f, 1f));
        AddDeck(visualRoot, coords, "CrosswalkSouth", new Vector3d(500112, 7400041.0, 100.04), new Vector3d(3.4, 3.2, 0.03), cwMat);
        AddDeck(visualRoot, coords, "CrosswalkNorth", new Vector3d(500112, 7400053.0, 100.04), new Vector3d(3.4, 3.2, 0.03), cwMat);
        AddDeck(visualRoot, coords, "CrosswalkEast", new Vector3d(500120, 7399989.0, 100.03), new Vector3d(3.6, 14.0, 0.03), cwMat);

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

        for (var i = 1; i <= 20; i++)
        {
            var shell = manifest.Objects.FirstOrDefault(o => o.Id == $"building_shell_{i:00}");
            if (shell is null) continue;
            var centerG = coords.ToGodot(shell.PositionEnu);
            var roofY = centerG.Y + (float)(shell.SizeEnu.Z * 0.5);
            var frontZ = centerG.Z + (float)(shell.SizeEnu.Y * 0.5);

            // Roof parapet rim & Brazilian rooftop water tank ("caixa d'água") + AC units
            visualRoot.AddChild(GsaMeshBuilder.Box($"Parapet_{i:00}", new Vector3(8.2f, 0.45f, 10.2f), new Vector3(centerG.X, roofY + 0.22f, centerG.Z), roofTrimMat));
            if (i % 2 == 1)
            {
                visualRoot.AddChild(new MeshInstance3D
                {
                    Name = $"WaterTank_{i:00}",
                    Position = new Vector3(centerG.X - 2.1f, roofY + 1.05f, centerG.Z - 2.0f),
                    Mesh = new CylinderMesh { TopRadius = 0.85f, BottomRadius = 0.78f, Height = 1.25f, RadialSegments = 10 },
                    MaterialOverride = tankMat,
                });
            }
            if (i % 3 != 0)
            {
                visualRoot.AddChild(GsaMeshBuilder.Box($"AcUnit_{i:00}", new Vector3(1.2f, 0.7f, 1.0f), new Vector3(centerG.X + 2.0f, roofY + 0.65f, centerG.Z + 1.5f), acMat));
            }

            // Storefront awning on South face at Z = 102.7 (safely above 2 m facade_XX!)
            var awningMat = GsaMeshBuilder.SolidMat(awningColors[i % awningColors.Length]);
            visualRoot.AddChild(GsaMeshBuilder.Box($"Awning_{i:00}", new Vector3(7.4f, 0.22f, 1.5f), new Vector3(centerG.X, 2.75f, frontZ + 0.75f), awningMat, rotDeg: new Vector3(18f, 0f, 0f)));

            // Neon storefront signage above awnings on select shops
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
        visualRoot.AddChild(GsaMeshBuilder.Box("OfficeBuilding", new Vector3(10f, 9f, 10f), officePos,
            GsaRetroTextures.CreateWorldMaterial("bldg_office", "facade_concrete", new Color(0.88f, 0.96f, 0.94f), new Vector3(0.09f, 0.09f, 0.09f), triplanar: true, nightWindowGlow: true)));
        visualRoot.AddChild(GsaMeshBuilder.Box("OfficeCanopy", new Vector3(5.2f, 0.28f, 2.4f), coords.ToGodot(new Vector3d(500232, 7400004.5, 103.3)),
            GsaMeshBuilder.SolidMat(new Color(0.12f, 0.56f, 0.44f))));
        var officeSign = GsaMeshBuilder.BuildStorefrontSignboard("OfficeSign", new Color(0.12f, 0.68f, 0.58f), "GSA SURVEY HQ", width: 4.6f);
        officeSign.Position = coords.ToGodot(new Vector3d(500232, 7400005.2, 104.3));
        visualRoot.AddChild(officeSign);

        // Street furniture: bus shelters, fire hydrants, trash dumpsters along sidewalks!
        var busStop = GsaMeshBuilder.BuildBusStopShelter("BusStopCommerce");
        busStop.Position = coords.ToGodot(new Vector3d(500084.0, 7399998.0, 100.04));
        visualRoot.AddChild(busStop);

        foreach (var hx in new[] { 500038.0, 500158.0, 500216.0 })
        {
            var hyd = GsaMeshBuilder.BuildFireHydrant($"Hydrant_{(int)hx}");
            hyd.Position = coords.ToGodot(new Vector3d(hx, 7399997.4, 100.04));
            visualRoot.AddChild(hyd);
        }
        foreach (var dx in new[] { 500062.0, 500136.0 })
        {
            var bin = GsaMeshBuilder.BuildTrashDumpster($"Dumpster_{(int)dx}");
            bin.Position = coords.ToGodot(new Vector3d(dx, 7400003.6, 100.04));
            visualRoot.AddChild(bin);
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

        // 6. Streetlamps & Street Trees along sidewalks
        for (var x = 500016.0; x <= 500228.0; x += 28.0)
        {
            var (lampS, bulbS, lightS) = GsaMeshBuilder.BuildStreetlamp($"LampS_{(int)x}", withRealLight: Math.Abs(x - 500212) < 35);
            lampS.Position = coords.ToGodot(new Vector3d(x, 7399997.2, 100.0));
            lampS.RotationDegrees = new Vector3(0f, -90f, 0f);
            visualRoot.AddChild(lampS);
            atmosphere.RegisterStreetlamp(bulbS, lightS);

            var (lampN, bulbN, lightN) = GsaMeshBuilder.BuildStreetlamp($"LampN_{(int)x}", withRealLight: false);
            lampN.Position = coords.ToGodot(new Vector3d(x, 7400054.8, 100.0));
            lampN.RotationDegrees = new Vector3(0f, 90f, 0f);
            visualRoot.AddChild(lampN);
            atmosphere.RegisterStreetlamp(bulbN, lightN);
        }

        foreach (var tx in new[] { 500015.0, 500055.0, 500095.0, 500145.0, 500185.0, 500222.0 })
        {
            var tree = GsaMeshBuilder.BuildStreetTree($"TreeN_{(int)tx}", 0.95f);
            tree.Position = coords.ToGodot(new Vector3d(tx, 7400057.2, 100.0));
            visualRoot.AddChild(tree);
        }
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
            ("bldg_sky_comm", "facade_commercial", new Color(0.95f, 0.90f, 0.85f)),
            ("bldg_sky_res", "facade_residential", new Color(0.92f, 0.90f, 0.84f)),
            ("bldg_sky_conc", "facade_concrete", new Color(0.82f, 0.85f, 0.88f)),
            ("bldg_sky_glass", "glass_tower", new Color(0.85f, 0.92f, 0.98f)),
        };

        var rng = new Random(200103);
        const double step = 36.0;
        for (var e = minPlayE - 220.0; e <= maxPlayE + 220.0; e += step)
        {
            for (var n = minPlayN - 200.0; n <= maxPlayN + 200.0; n += step)
            {
                if (e > minPlayE - 24.0 && e < maxPlayE + 24.0 && n > minPlayN - 28.0 && n < maxPlayN + 24.0)
                    continue;
                if (rng.NextDouble() < 0.34) // Leave avenue corridors open!
                    continue;

                var w = 18f + (float)rng.NextDouble() * 10f;
                var d = 18f + (float)rng.NextDouble() * 10f;
                var h = 14f + (float)rng.NextDouble() * 34f;
                var style = styles[rng.Next(styles.Length)];
                var mat = GsaRetroTextures.CreateWorldMaterial(
                    style.Item1, style.Item2, style.Item3,
                    new Vector3(0.085f, 0.085f, 0.085f),
                    triplanar: true,
                    nightWindowGlow: style.Item2 != "glass_tower");

                var pos = coords.ToGodot(new Vector3d(e, n, 100.0 + h * 0.5 - 0.5));
                skyline.AddChild(GsaMeshBuilder.Box($"SkyBldg_{(int)e}_{(int)n}", new Vector3(w, h, d), pos, mat));
                if (h > 24f)
                {
                    var ph = 5.5f + ((int)e % 4);
                    skyline.AddChild(GsaMeshBuilder.Box($"SkyPent_{(int)e}_{(int)n}", new Vector3(w * 0.64f, ph, d * 0.64f),
                        new Vector3(pos.X, h + ph * 0.5f - 0.5f, pos.Z), mat));
                    skyline.AddChild(GsaMeshBuilder.Box($"SkyMast_{(int)e}_{(int)n}", new Vector3(0.35f, 6.5f, 0.35f),
                        new Vector3(pos.X, h + ph + 2.75f, pos.Z), GsaMeshBuilder.SolidMat(new Color(0.55f, 0.58f, 0.62f))));
                    skyline.AddChild(GsaMeshBuilder.Box($"SkyBeacon_{(int)e}_{(int)n}", new Vector3(0.55f, 0.55f, 0.55f),
                        new Vector3(pos.X, h + ph + 6.1f, pos.Z), GsaMeshBuilder.SolidMat(Colors.Red, emit: true, emissionEnergy: 1.8f)));
                }
            }
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

    private static void AddDeck(Node3D parent, GodotCoordinateAdapter coords, string name, Vector3d posEnu, Vector3d sizeEnu, StandardMaterial3D mat)
    {
        parent.AddChild(new MeshInstance3D
        {
            Name = name,
            Position = coords.ToGodot(posEnu),
            Mesh = new BoxMesh { Size = new Vector3((float)sizeEnu.X, (float)sizeEnu.Z, (float)sizeEnu.Y) },
            MaterialOverride = mat,
        });
    }

    private static void AddRoadLaneMarkings(
        Node3D parent,
        GodotCoordinateAdapter coords,
        double xStart,
        double xEnd,
        double yCenter,
        double zHeight,
        StandardMaterial3D yellowMat,
        StandardMaterial3D whiteMat)
    {
        var len = xEnd - xStart;
        var midX = (xStart + xEnd) * 0.5;
        AddDeck(parent, coords, $"DoubleYellowA_{(int)yCenter}", new Vector3d(midX, yCenter - 0.14, zHeight), new Vector3d(len, 0.12, 0.02), yellowMat);
        AddDeck(parent, coords, $"DoubleYellowB_{(int)yCenter}", new Vector3d(midX, yCenter + 0.14, zHeight), new Vector3d(len, 0.12, 0.02), yellowMat);
        for (var x = xStart + 4.0; x <= xEnd - 4.0; x += 10.0)
        {
            AddDeck(parent, coords, $"DashL_{(int)x}_{(int)yCenter}", new Vector3d(x, yCenter - 3.6, zHeight), new Vector3d(4.0, 0.12, 0.02), whiteMat);
            AddDeck(parent, coords, $"DashR_{(int)x}_{(int)yCenter}", new Vector3d(x, yCenter + 3.6, zHeight), new Vector3d(4.0, 0.12, 0.02), whiteMat);
        }
    }
}

