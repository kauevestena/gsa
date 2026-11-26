using Gsa.Core.Geometry;
using Gsa.Missions.Activities;
using Godot;

namespace Gsa.Engine.Visuals;

/// <summary>Park race furniture. The painted racing line and gate posts use the scoring coordinates.</summary>
internal static class GimbalinaCourseVisuals
{
    private static readonly (int East, string Name, string Color)[] Stalls =
        [(22, "CALDO DE CANA", "358c7c"), (102, "CAFÉ DO PARQUE", "cb6348"), (161, "OFICINA PEDAL", "487ab0")];

    public static IEnumerable<Gsa.Core.Mobility.ObstacleBoxEnu> StallObstacles() =>
        Stalls.Select(stall => new Gsa.Core.Mobility.ObstacleBoxEnu($"ParkStall_{stall.East}",
            GimbalinaChallenge.Start + new Vector3d(stall.East, -15, 1.35), new Vector3d(5.6, 3.2, 2.7)));

    public static void Build(Node3D parent, GodotCoordinateAdapter coordinates)
    {
        var course = new Node3D { Name = "GimbalinaCourse", Position = coordinates.ToGodot(GimbalinaChallenge.Start) };
        parent.AddChild(course);
        var batch = new GsaStaticMeshBatcher();
        var ink = GsaMeshBuilder.SolidStandardMat(new Color("193c41"));
        var track = GsaMeshBuilder.SolidStandardMat(new Color("426465"));
        var mint = GsaMeshBuilder.SolidStandardMat(new Color("71efcb"), emit: true, emissionEnergy: .35f);
        var yellow = GsaMeshBuilder.SolidStandardMat(new Color("ffc255"), emit: true, emissionEnergy: .45f);
        var white = GsaMeshBuilder.SolidStandardMat(new Color("fff0d5"), emit: true, emissionEnergy: .28f);
        var half = (float)GimbalinaChallenge.GateHalfWidth;
        var gates = GimbalinaChallenge.Gates;

        // A strip of painted pavement follows the slalom. It has no raised collision surface.
        Vector3 Point(Vector3d v) => coordinates.ToGodot(v) - course.Position;
        for (var i = 1; i < gates.Count; i++)
        {
            var a = Point(gates[i - 1]);
            var b = Point(gates[i]);
            var along = (b - a).Normalized();
            var across = new Vector3(-along.Z, 0, along.X);
            var rotation = new Vector3(0, -Mathf.RadToDeg(Mathf.Atan2(along.Z, along.X)), 0);
            var center = (a + b) * .5f - Vector3.Up * .19f;
            var length = a.DistanceTo(b);
            batch.AddBox(track, center, new Vector3(length + .02f, .04f, 2 * half + .7f), rotation);
            foreach (var side in new[] { -1, 1 })
                batch.AddBox(mint, center + across * (half + .25f) * side + Vector3.Up * .035f,
                    new Vector3(length, .018f, .095f), rotation);
            for (var d = 6f; d < length - 2; d += 8)
            {
                var arrow = a + along * d - Vector3.Up * .135f;
                foreach (var side in new[] { -1, 1 })
                    batch.AddBox(white, arrow + across * (.20f * side), new Vector3(.85f, .02f, .11f),
                        rotation + Vector3.Up * (31 * side));
            }
        }

        for (var i = 0; i < gates.Count; i++)
        {
            var pos = Point(gates[i]);
            var arch = i == 0 || i == gates.Count - 1;
            var accent = arch ? yellow : mint;
            var height = arch ? 4.1f : 2.8f;
            foreach (var side in new[] { -1, 1 })
            {
                var foot = pos + Vector3.Back * ((half + .18f) * side);
                batch.AddBox(ink, foot + Vector3.Up * (.5f * height), new Vector3(.24f, height, .23f));
                batch.AddBox(accent, foot + Vector3.Up * (height - .62f), new Vector3(.29f, 1.18f, .28f));
                batch.AddBox(white, foot + Vector3.Up * .56f, new Vector3(.26f, .22f, .26f));
                if (!arch)
                    batch.AddBox(accent, foot + Vector3.Up * (height - .04f) + Vector3.Back * (.44f * side),
                        new Vector3(.12f, .56f, 1.0f));
            }
            if (arch)
                batch.AddBox(ink, pos + Vector3.Up * height, new Vector3(.32f, .68f, 2 * half + .65f));
            const int squares = 10;
            for (var col = 0; col < squares; col++)
            for (var row = 0; row < (arch ? 2 : 1); row++)
                batch.AddBox((col + row) % 2 == 0 ? white : ink,
                    pos + new Vector3(row * .43f, -.095f, -half + (col + .5f) * 2 * half / squares),
                    new Vector3(.44f, .025f, 2 * half / squares));

            var label = new Label3D
            {
                Name = $"Gate{i}", Text = i == 0 ? TranslationServer.Translate("park.start").ToString() :
                    i == gates.Count - 1 ? TranslationServer.Translate("park.finish").ToString() : i.ToString(),
                Position = pos + new Vector3(arch ? -.20f : 0, arch ? height : height + .40f, 0),
                Billboard = arch ? BaseMaterial3D.BillboardModeEnum.Disabled : BaseMaterial3D.BillboardModeEnum.Enabled,
                RotationDegrees = arch ? Vector3.Up * -90 : Vector3.Zero, FontSize = 52,
                PixelSize = arch ? .009f : .018f, OutlineSize = 7, Modulate = new Color("fff4d7"),
                VisibilityRangeEnd = 95, DoubleSided = true,
            };
            course.AddChild(label);
        }
        var timber = GsaMeshBuilder.SolidStandardMat(new Color("8f664e"));
        var paving = GsaRetroTextures.CreateWorldMaterial("park_stall_paving", "sidewalk", Colors.White, triplanar: false);
        var vendorMesh = GsaMeshBuilder.BuildTrafficPedestrianMesh();
        var trees = new List<(Vector3 Position, float Scale)>();
        foreach (var stall in Stalls)
        {
            var p = new Vector3(stall.East, 0, 15);
            var paint = GsaMeshBuilder.SolidStandardMat(new Color(stall.Color));
            batch.AddBox(paving, p, new Vector3(19, .14f, 10), uvScale: .32f);
            batch.AddBox(paint, p + Vector3.Up * .53f, new Vector3(5.6f, 1.06f, 3.2f));
            batch.AddBox(timber, p + new Vector3(0, 1.10f, -1.55f), new Vector3(5.8f, .14f, .56f));
            batch.AddBox(paint, p + new Vector3(0, 1.80f, 1.52f), new Vector3(5.6f, 1.60f, .16f));
            foreach (var x in new[] { -2.66f, 2.66f })
                batch.AddBox(ink, p + new Vector3(x, 1.8f, -1.44f), new Vector3(.16f, 1.65f, .16f));
            for (var stripe = 0; stripe < 11; stripe++)
            {
                var x = (stripe - 5) * .54f;
                var fabric = stripe % 2 == 0 ? paint : white;
                batch.AddBox(fabric, p + new Vector3(x, 2.68f, -.36f), new Vector3(.54f, .17f, 4.25f), new Vector3(-6, 0, 0));
                batch.AddBox(fabric, p + new Vector3(x, 2.33f, -2.46f), new Vector3(.54f, .34f, .08f));
            }
            batch.AddBox(ink, p + new Vector3(0, 3.05f, -.6f), new Vector3(4.9f, .78f, .20f));
            course.AddChild(new Label3D
            {
                Text = stall.Name, Position = p + new Vector3(0, 3.05f, -.72f),
                RotationDegrees = Vector3.Up * 180, FontSize = 42, PixelSize = .012f,
                Modulate = new Color("fff1cb"), OutlineSize = 2, VisibilityRangeEnd = 85,
            });
            course.AddChild(new MeshInstance3D { Mesh = vendorMesh, Position = p + new Vector3(-.7f, .08f, -.45f),
                RotationDegrees = Vector3.Up * 90, VisibilityRangeEnd = 65 });
            foreach (var side in new[] { -1, 1 })
            {
                var tree = p + new Vector3(side * 6.7f, .13f, 2.8f);
                trees.Add((tree, side < 0 ? 1.24f : .92f));
                var bench = p + new Vector3(side * 5.9f, .53f, -2.9f);
                batch.AddBox(timber, bench, new Vector3(2.15f, .12f, .49f));
                foreach (var foot in new[] { -.78f, .78f })
                    batch.AddBox(ink, bench + new Vector3(foot, -.22f, 0), new Vector3(.14f, .42f, .35f));
            }
        }
        GsaMeshBuilder.BuildBatchedStreetTrees(course, "ParkMarketTrees", trees, 170);
        batch.BuildSeparatedNodes(course, "CoursePaintAndGates", GeometryInstance3D.ShadowCastingSetting.Off, 245);
    }
}
