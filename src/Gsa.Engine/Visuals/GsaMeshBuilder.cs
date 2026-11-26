using Gsa.Core.Mobility;
using Godot;

namespace Gsa.Engine.Visuals;

/// <summary>Holds animated sub-node references for any active player platform or on-foot rig.</summary>
public sealed class PlayerVisualRig
{
    public Node3D Root { get; }
    public Node3D BodyPivot { get; }
    public List<Node3D> Wheels { get; } = [];
    public List<Node3D> SteerableWheels { get; } = [];
    public Node3D? LegL { get; set; }
    public Node3D? LegR { get; set; }
    internal Node3D? BicycleCrank { get; set; }
    internal Node3D? ShinL { get; set; }
    internal Node3D? ShinR { get; set; }
    public Node3D? ArmL { get; set; }
    public Node3D? ArmR { get; set; }
    public Node3D? GimbalinaBody { get; set; }
    public Node3D? GimbalinaHead { get; set; }
    public Node3D? WingL { get; set; }
    public Node3D? WingR { get; set; }
    public Node3D? LidarSpinner { get; set; }
    public Node3D? ShadowBlob { get; set; }
    public SpotLight3D? HeadlightL { get; set; }
    public SpotLight3D? HeadlightR { get; set; }
    public Node3D? HeadlightBeam { get; set; }
    public StandardMaterial3D? HeadlightMat { get; set; }
    public StandardMaterial3D? TaillightMat { get; set; }
    public bool IsGimbalinaDashboardMascot { get; set; }

    private float _walkPhase;
    private float _wheelAngle;
    private float _lidarAngle;
    private float _bobbleTime;

    public PlayerVisualRig(Node3D root, Node3D bodyPivot)
    {
        Root = root;
        BodyPivot = bodyPivot;
    }

    public void Animate(
        double dt,
        double forwardSpeedMps,
        double steerInput,
        double bodyPitchRad,
        double bodyRollRad,
        double gimbalinaStress,
        double gimbalinaResidualPitchRad,
        double gimbalinaResidualRollRad,
        bool isNight,
        bool isBraking)
    {
        var fdt = (float)dt;
        var speed = (float)forwardSpeedMps;
        _wheelAngle -= speed * fdt * 2.8f;
        var steerAngle = (float)Math.Clamp(steerInput * 0.42, -0.45, 0.45);
        foreach (var wheel in Wheels)
        {
            var yaw = SteerableWheels.Contains(wheel) ? steerAngle : 0f;
            wheel.Rotation = new Vector3(_wheelAngle, yaw, 0f);
        }

        // Suspension / handcart pitch & roll
        BodyPivot.Rotation = new Vector3((float)bodyPitchRad, 0f, (float)bodyRollRad);

        // Walking / pedaling animation
        if (BicycleCrank is not null)
        {
            // The two pedals are half a revolution apart. Each knee follows its pedal.
            _walkPhase = Mathf.Wrap(_walkPhase + speed * fdt * 1.65f, 0, Mathf.Tau);
            BicycleCrank.Rotation = new Vector3(_walkPhase, 0, 0);
            PosePedalingLeg(LegL!, ShinL!, _walkPhase);
            PosePedalingLeg(LegR!, ShinR!, _walkPhase + Mathf.Pi);
        }
        else if (MathF.Abs(speed) > 0.08f)
        {
            _walkPhase += fdt * Math.Clamp(MathF.Abs(speed) * 3.8f, 3.5f, 14f);
            var swing = MathF.Sin(_walkPhase) * 0.55f;
            if (LegL is not null) LegL.Rotation = new Vector3(swing, 0f, 0f);
            if (LegR is not null) LegR.Rotation = new Vector3(-swing, 0f, 0f);
            if (ArmL is not null) ArmL.Rotation = new Vector3(-swing * 0.65f, 0f, 0f);
            if (ArmR is not null) ArmR.Rotation = new Vector3(swing * 0.65f, 0f, 0f);
        }
        else
        {
            if (LegL is not null) LegL.Rotation = LegL.Rotation.Lerp(Vector3.Zero, Math.Min(1f, fdt * 10f));
            if (LegR is not null) LegR.Rotation = LegR.Rotation.Lerp(Vector3.Zero, Math.Min(1f, fdt * 10f));
            if (ArmL is not null) ArmL.Rotation = ArmL.Rotation.Lerp(Vector3.Zero, Math.Min(1f, fdt * 10f));
            if (ArmR is not null) ArmR.Rotation = ArmR.Rotation.Lerp(Vector3.Zero, Math.Min(1f, fdt * 10f));
        }

        // Gimbalina head counter-stabilization (stays level up to +-45 deg while body tilts, jitters at high stress)
        if (GimbalinaBody is not null && GimbalinaHead is not null)
        {
            _bobbleTime += fdt;
            if (IsGimbalinaDashboardMascot)
            {
                var bob = MathF.Sin(_bobbleTime * 6.5f) * 0.06f * Math.Min(1f, MathF.Abs(speed) * 0.25f + 0.15f);
                GimbalinaHead.Rotation = new Vector3(bob, (float)(steerInput * -0.15), bob * 0.7f);
            }
            else
            {
                var compPitch = (float)(gimbalinaResidualPitchRad - bodyPitchRad);
                var compRoll = (float)(gimbalinaResidualRollRad - bodyRollRad);
                var stressFrac = Math.Clamp((float)(gimbalinaStress / 100.0), 0f, 1f);
                var flutter = stressFrac > 0.35f ? MathF.Sin(_bobbleTime * 24f) * 0.45f * stressFrac : 0.04f * MathF.Sin(_bobbleTime * 4f);
                if (WingL is not null) WingL.Rotation = new Vector3(0f, 0f, flutter);
                if (WingR is not null) WingR.Rotation = new Vector3(0f, 0f, -flutter);
                GimbalinaHead.Rotation = new Vector3(compPitch, (float)(-steerInput * 0.2), compRoll);
            }
        }

        if (LidarSpinner is not null)
        {
            _lidarAngle += fdt * 16.0f;
            LidarSpinner.Rotation = new Vector3(0f, _lidarAngle, 0f);
        }

        if (HeadlightL is not null) HeadlightL.Visible = isNight;
        if (HeadlightR is not null) HeadlightR.Visible = isNight;
        if (HeadlightBeam is not null) HeadlightBeam.Visible = isNight;
        if (HeadlightMat is not null)
            HeadlightMat.EmissionEnergyMultiplier = isNight ? 2.4f : 0.25f;
        if (TaillightMat is not null)
            TaillightMat.EmissionEnergyMultiplier = isBraking ? 2.6f : (isNight ? 1.1f : 0.1f);
    }
    private static void PosePedalingLeg(Node3D thigh, Node3D shin, float phase)
    {
        // Both leg segments are 0.37 m. Hip and crank are 0.48 m apart vertically
        // and 0.14 m apart fore/aft in the seated rig.
        var down = .48f + .15f * MathF.Cos(phase);
        var forward = .14f + .15f * MathF.Sin(phase);
        var bend = MathF.Acos(Math.Clamp(MathF.Sqrt(down * down + forward * forward) / .74f, 0, 1));
        thigh.Rotation = new Vector3(MathF.Atan2(forward, down) + bend, 0, 0);
        shin.Rotation = new Vector3(-2 * bend, 0, 0);
    }

}

/// <summary>Builds low-poly PS2/GTA III-era 3D vehicles, Elias + Gimbalina rigs, traffic silhouettes, and street props.</summary>
public static class GsaMeshBuilder
{
    private static readonly Dictionary<(uint ColorRgba, byte RoughnessByte, bool Emit, short EnergyCentis), Material> SolidMaterialCache = new();
    private static readonly QuadMesh UnitQuadMesh = new() { Size = Vector2.One };
    private static readonly CylinderMesh TreeTrunkMesh = new() { TopRadius = 0.18f, BottomRadius = 0.28f, Height = 2.6f, RadialSegments = 6 };
    private static readonly SphereMesh TreeCanopyLowerMesh = new() { Radius = 1.65f, Height = 2.3f, RadialSegments = 7, Rings = 4 };
    private static readonly SphereMesh TreeCanopyTopMesh = new() { Radius = 1.15f, Height = 1.7f, RadialSegments = 6, Rings = 3 };

    public static Mesh CreateWorldUvBoxMesh(Vector3 size, float uvScale = 0.1f)
    {
        var batcher = new GsaStaticMeshBatcher();
        var dummy = SolidMat(Colors.White);
        batcher.AddBox(dummy, Vector3.Zero, size, uvScale: uvScale, uvOffsetY: size.Y * 0.5f);
        var mesh = batcher.BuildCombinedArrayMesh()!;
        return mesh;
    }

    public static Mesh CreateStorefrontFacadeMesh(Vector3 size, int facadeIndex)
    {
        var batch = new GsaStaticMeshBatcher();
        AddStorefrontFacadeParts(batch, Vector3.Zero, size, facadeIndex, isCenteredLocal: true);
        return batch.BuildCombinedArrayMesh()!;
    }

    public static void AddStorefrontFacadeToBatch(
        GsaStaticMeshBatcher batch,
        Vector3 facadeCenterG,
        Vector3 facadeSizeG,
        int facadeIndex) =>
        AddStorefrontFacadeParts(batch, facadeCenterG, facadeSizeG, facadeIndex, isCenteredLocal: false);

    private static void AddStorefrontFacadeParts(
        GsaStaticMeshBatcher batch,
        Vector3 center,
        Vector3 size,
        int facadeIndex,
        bool isCenteredLocal)
    {
        // Facade manifest box is 8.0 x 2.0 x 0.2 centered at Z = -5.0 (Godot) -> front at +0.1, back at -0.1.
        // Building shell starts at Godot Z = -5.4 (-0.4 behind center), so bridge depth back to -0.45 for zero gap!
        var wallTints = new Color[]
        {
            new(0.66f, 0.36f, 0.28f), // Terracotta brick
            new(0.76f, 0.69f, 0.54f), // Sandstone cream
            new(0.46f, 0.50f, 0.54f), // Slate concrete
            new(0.72f, 0.50f, 0.34f), // Warm ochre
        };
        var accentTints = new Color[]
        {
            new(0.58f, 0.18f, 0.16f),
            new(0.16f, 0.44f, 0.34f),
            new(0.18f, 0.34f, 0.54f),
            new(0.64f, 0.42f, 0.16f),
        };

        var idx = Math.Max(0, facadeIndex - 1);
        var wallMat = SolidMat(wallTints[idx % wallTints.Length], roughness: 0.86f);
        var plinthMat = SolidMat(new Color(0.28f, 0.30f, 0.33f), roughness: 0.90f);
        var trimMat = SolidMat(accentTints[idx % accentTints.Length], roughness: 0.65f);
        var frameMat = SolidMat(new Color(0.14f, 0.16f, 0.18f), roughness: 0.55f);
        var glassMat = SolidMat(new Color(0.14f, 0.26f, 0.36f), roughness: 0.20f);
        var interiorMat = SolidMat(new Color(0.95f, 0.82f, 0.52f), emit: true, emissionEnergy: 0.22f);
        var doorMat = SolidMat(new Color(0.24f, 0.18f, 0.14f), roughness: 0.72f);

        var hy = size.Y * 0.5f;
        var yBase = isCenteredLocal ? -hy : center.Y - hy;
        var zFront = center.Z + size.Z * 0.5f;
        var zBridgeCenter = center.Z - 0.18f; // spans [-0.46, +0.10] around center.Z -> seals flush into building_shell!
        const float bridgeDepth = 0.56f;

        void Part(Material m, float lx, float bottomY, float h, float w, float d = bridgeDepth, float zOff = 0f) =>
            batch.AddBox(m, new Vector3(center.X + lx, yBase + bottomY + h * 0.5f, zBridgeCenter + zOff), new Vector3(w, h, d));

        // Main masonry piers (left, center-left, center-right, right) + upper fascia bridging Z=2.0..2.45m flush to building_shell
        Part(wallMat, -3.55f, 0f, 2.35f, 0.90f);
        Part(wallMat, -1.05f, 0f, 2.35f, 0.55f);
        Part(wallMat, 1.05f, 0f, 2.35f, 0.55f);
        Part(wallMat, 3.55f, 0f, 2.35f, 0.90f);
        Part(wallMat, 0f, 1.92f, 0.58f, 8.0f);
        Part(trimMat, 0f, 2.42f, 0.12f, 8.12f, d: 0.62f, zOff: 0.03f);

        // Low plinth base under left & right display bays
        Part(plinthMat, -2.25f, 0f, 0.42f, 1.90f);
        Part(plinthMat, 2.25f, 0f, 0.42f, 1.90f);
        Part(plinthMat, -2.25f, 0.42f, 0.08f, 1.96f, d: 0.62f, zOff: 0.03f);
        Part(plinthMat, 2.25f, 0.42f, 0.08f, 1.96f, d: 0.62f, zOff: 0.03f);

        // Left & right storefront display windows (warm interior backdrop + dark glass pane + mullions)
        foreach (var side in new[] { -2.25f, 2.25f })
        {
            Part(interiorMat, side, 0.50f, 1.42f, 1.72f, d: 0.42f, zOff: -0.05f);
            batch.AddBox(glassMat, new Vector3(center.X + side, yBase + 1.21f, zFront - 0.02f), new Vector3(1.66f, 1.32f, 0.04f));
            batch.AddBox(frameMat, new Vector3(center.X + side, yBase + 1.21f, zFront), new Vector3(0.06f, 1.36f, 0.06f));
            batch.AddBox(frameMat, new Vector3(center.X + side, yBase + 1.56f, zFront), new Vector3(1.74f, 0.06f, 0.06f));
        }

        // Central recessed shop entrance door & transom
        batch.AddBox(doorMat, new Vector3(center.X, yBase + 0.94f, zFront - 0.12f), new Vector3(1.12f, 1.88f, 0.34f));
        batch.AddBox(frameMat, new Vector3(center.X, yBase + 1.88f, zFront - 0.04f), new Vector3(1.55f, 0.08f, 0.24f));
        batch.AddBox(glassMat, new Vector3(center.X, yBase + 1.18f, zFront + 0.05f), new Vector3(0.72f, 0.78f, 0.03f));
    }

    public static void BuildBatchedStreetTrees(
        Node3D parent,
        string name,
        IReadOnlyList<(Vector3 Position, float Scale)> trees,
        float visibilityRangeEnd = 0f)
    {
        if (trees.Count == 0)
            return;

        var trunkMat = SolidMat(new Color(0.36f, 0.24f, 0.14f));
        var foliageLowMat = GsaRetroTextures.CreateWorldMaterial("tree_foliage_mat", "park_grass", new Color(0.85f, 1.0f, 0.78f));
        var foliageTopMat = GsaRetroTextures.CreateWorldMaterial("tree_foliage_top_mat", "park_grass", new Color(0.95f, 1.1f, 0.85f));
        var shadowMat = GsaRetroTextures.CreateWorldMaterial("shadow_blob_mat", "shadow_blob", Colors.White, unshaded: true, transparent: true);

        TreeTrunkMesh.Material = trunkMat;
        TreeCanopyLowerMesh.Material = foliageLowMat;
        TreeCanopyTopMesh.Material = foliageTopMat;

        var trunkMm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = TreeTrunkMesh, InstanceCount = trees.Count };
        var lowMm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = TreeCanopyLowerMesh, InstanceCount = trees.Count };
        var topMm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = TreeCanopyTopMesh, InstanceCount = trees.Count };
        var shadowMm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = UnitQuadMesh, InstanceCount = trees.Count };

        var shadowRot = Basis.FromEuler(new Vector3(-MathF.PI * 0.5f, 0f, 0f));
        for (var i = 0; i < trees.Count; i++)
        {
            var (pos, s) = trees[i];
            var sb = Basis.Identity.Scaled(new Vector3(s, s, s));
            trunkMm.SetInstanceTransform(i, new Transform3D(sb, pos + new Vector3(0f, 1.3f * s, 0f)));
            lowMm.SetInstanceTransform(i, new Transform3D(sb, pos + new Vector3(0f, 3.0f * s, 0f)));
            topMm.SetInstanceTransform(i, new Transform3D(sb, pos + new Vector3(0.2f * s, 4.1f * s, -0.1f * s)));
            shadowMm.SetInstanceTransform(i, new Transform3D(shadowRot.Scaled(new Vector3(3.0f * s, 1f, 3.0f * s)), pos + new Vector3(0f, 0.03f, 0f)));
        }

        void AddLayer(string suffix, MultiMesh mm, Material? matOverride, float visEnd)
        {
            var mmi = new MultiMeshInstance3D
            {
                Name = $"{name}_{suffix}",
                Multimesh = mm,
                MaterialOverride = matOverride,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            if (visEnd > 0f)
                mmi.VisibilityRangeEnd = visEnd;
            parent.AddChild(mmi);
        }

        AddLayer("Trunks", trunkMm, trunkMat, visibilityRangeEnd);
        AddLayer("CanopyLow", lowMm, foliageLowMat, visibilityRangeEnd);
        AddLayer("CanopyTop", topMm, foliageTopMat, visibilityRangeEnd);
        AddLayer("Shadows", shadowMm, shadowMat, visibilityRangeEnd > 0f ? MathF.Min(visibilityRangeEnd, 90f) : 90f);
    }

    public static PlayerVisualRig BuildPlayerRig(LocomotionMode mode)
    {
        var root = new Node3D { Name = $"PlayerVisual_{mode}" };
        var bodyPivot = new Node3D { Name = "BodyPivot" };
        root.AddChild(bodyPivot);
        var rig = new PlayerVisualRig(root, bodyPivot);

        var shadowSize = mode switch
        {
            LocomotionMode.OnFoot => 0.9f,
            LocomotionMode.Handcart => 1.4f,
            LocomotionMode.Bicycle or LocomotionMode.Scooter => 1.5f,
            LocomotionMode.Car => 3.9f,
            LocomotionMode.Kombi => 4.4f,
            LocomotionMode.Pickup => 4.7f,
            LocomotionMode.Van => 5.2f,
            _ => 1.4f,
        };
        var shadowWidth = mode >= LocomotionMode.Car ? 2.2f : 1.1f;
        rig.ShadowBlob = AddShadowBlob(root, shadowWidth, shadowSize);

        switch (mode)
        {
            case LocomotionMode.OnFoot:
                BuildEliasActor(bodyPivot, rig, Vector3.Zero, pushingPose: false);
                break;
            case LocomotionMode.Handcart:
                BuildHandcart(bodyPivot, rig);
                BuildEliasActor(bodyPivot, rig, new Vector3(0f, 0f, 0.72f), pushingPose: true);
                break;
            case LocomotionMode.Bicycle:
                BuildBicycle(bodyPivot, rig);
                BuildEliasActor(bodyPivot, rig, new Vector3(0f, .07f, .14f), pushingPose: true, wearingHelmet: true);
                rig.Animate(0, 0, 0, 0, 0, 0, 0, 0, false, false);
                break;
            case LocomotionMode.Scooter:
                BuildScooter(bodyPivot, rig);
                BuildEliasActor(bodyPivot, rig, new Vector3(0f, 0.30f, 0.14f), pushingPose: true, wearingHelmet: true);
                break;
            case LocomotionMode.Car:
                BuildSedanCar(bodyPivot, rig);
                break;
            case LocomotionMode.Kombi:
                BuildKombiVan(bodyPivot, rig);
                break;
            case LocomotionMode.Pickup:
                BuildPickup4x4(bodyPivot, rig);
                break;
            case LocomotionMode.Van:
                BuildEngineeringVan(bodyPivot, rig);
                break;
        }

        return rig;
    }

    private static Node3D AddShadowBlob(Node3D parent, float width, float length)
    {
        var quad = new MeshInstance3D
        {
            Name = "BlobShadow",
            Position = new Vector3(0f, 0.025f, 0f),
            RotationDegrees = new Vector3(-90f, 0f, 0f),
            Scale = new Vector3(width, length, 1f),
            Mesh = UnitQuadMesh,
            MaterialOverride = GsaRetroTextures.CreateWorldMaterial(
                "shadow_blob_mat", "shadow_blob", Colors.White, unshaded: true, transparent: true),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityRangeEnd = 80f,
        };
        parent.AddChild(quad);
        return quad;
    }

    private static void BuildEliasActor(Node3D parent, PlayerVisualRig rig, Vector3 offset, bool pushingPose, bool wearingHelmet = false)
    {
        var elias = new Node3D { Name = "EliasAnacleto", Position = offset };
        parent.AddChild(elias);

        var pantsMat = SolidMat(new Color(0.14f, 0.21f, 0.34f));
        var vestMat = SolidMat(new Color(0.95f, 0.48f, 0.08f));
        var shirtMat = SolidMat(new Color(0.22f, 0.42f, 0.68f));
        var skinMat = SolidMat(new Color(0.84f, 0.66f, 0.52f));
        var capMat = SolidMat(wearingHelmet ? new Color(0.95f, 0.78f, 0.14f) : new Color(0.18f, 0.30f, 0.45f));

        var cycling = rig.BicycleCrank is not null;
        var legLength = cycling ? .37f : .76f;
        // Legs
        var legL = new Node3D { Name = "LegL", Position = new Vector3(-0.11f, 0.78f, 0f) };
        legL.AddChild(Box("LegMesh", new Vector3(.14f, legLength, .15f), new Vector3(0, -legLength / 2, 0), pantsMat));
        elias.AddChild(legL);
        rig.LegL = legL;

        var legR = new Node3D { Name = "LegR", Position = new Vector3(0.11f, 0.78f, 0f) };
        legR.AddChild(Box("LegMesh", new Vector3(.14f, legLength, .15f), new Vector3(0, -legLength / 2, 0), pantsMat));
        elias.AddChild(legR);
        rig.LegR = legR;

        // Torso + hi-vis surveyor vest
        elias.AddChild(Box("Torso", new Vector3(0.40f, 0.54f, 0.24f), new Vector3(0f, 1.07f, 0f), vestMat));
        elias.AddChild(Box("VestStripe", new Vector3(0.41f, 0.08f, 0.25f), new Vector3(0f, 1.00f, 0f), SolidMat(new Color(0.92f, 0.96f, 0.25f))));
        elias.AddChild(Box("Backpack", new Vector3(0.28f, 0.38f, 0.14f), new Vector3(0f, 1.08f, 0.17f), SolidMat(new Color(0.24f, 0.27f, 0.30f))));

        // Arms
        var armPitch = cycling ? .99f : pushingPose ? -.95f : 0f;
        var armL = new Node3D { Name = "ArmL", Position = new Vector3(-0.26f, 1.28f, 0f), Rotation = new Vector3(armPitch, 0f, 0f) };
        armL.AddChild(Box("SleeveL", new Vector3(0.12f, 0.46f, 0.12f), new Vector3(0f, -0.22f, 0f), shirtMat));
        elias.AddChild(armL);
        if (!pushingPose) rig.ArmL = armL;

        var armR = new Node3D { Name = "ArmR", Position = new Vector3(0.26f, 1.28f, 0f), Rotation = new Vector3(armPitch, 0f, 0f) };
        armR.AddChild(Box("SleeveR", new Vector3(0.12f, 0.46f, 0.12f), new Vector3(0f, -0.22f, 0f), shirtMat));
        elias.AddChild(armR);
        if (!pushingPose) rig.ArmR = armR;

        // Head & cap/helmet
        elias.AddChild(Box("Head", new Vector3(0.25f, 0.26f, 0.25f), new Vector3(0f, 1.49f, 0f), skinMat));
        elias.AddChild(Box("Cap", new Vector3(0.28f, 0.11f, 0.29f), new Vector3(0f, 1.62f, -0.02f), capMat));
        elias.AddChild(Box("CapVisor", new Vector3(0.26f, 0.04f, 0.12f), new Vector3(0f, 1.58f, -0.18f), capMat));
        var bootMat = SolidMat(new Color(0.10f, 0.10f, 0.11f));
        Node3D AddShin(Node3D thigh)
        {
            var shin = new Node3D { Position = new Vector3(0, -.37f, 0) };
            shin.AddChild(Box("Shin", new Vector3(.12f, .37f, .14f), new Vector3(0, -.185f, 0), pantsMat));
            thigh.AddChild(shin);
            return shin;
        }
        if (cycling)
        {
            rig.ShinL = AddShin(legL);
            rig.ShinR = AddShin(legR);
        }
        var bootPosition = new Vector3(0, cycling ? -.35f : -.72f, -.04f);
        (rig.ShinL ?? legL).AddChild(Box("BootL", new Vector3(.15f, .12f, .22f), bootPosition, bootMat));
        (rig.ShinR ?? legR).AddChild(Box("BootR", new Vector3(.15f, .12f, .22f), bootPosition, bootMat));
        armL.AddChild(Box("HandL", new Vector3(0.10f, 0.10f, 0.10f), new Vector3(0f, -0.46f, 0f), skinMat));
        armR.AddChild(Box("HandR", new Vector3(0.10f, 0.10f, 0.10f), new Vector3(0f, -0.46f, 0f), skinMat));

        if (wearingHelmet)
        {
            // Helmet-mounted camera pod
            elias.AddChild(Box("HelmetCam", new Vector3(0.08f, 0.08f, 0.12f), new Vector3(0.15f, 1.62f, -0.06f), SolidMat(new Color(0.12f, 0.14f, 0.16f))));
            elias.AddChild(Box("HelmetLens", new Vector3(0.05f, 0.05f, 0.03f), new Vector3(0.15f, 1.62f, -0.13f), SolidMat(new Color(0.20f, 0.85f, 0.95f), emit: true)));
        }
    }

    private static void BuildHandcart(Node3D parent, PlayerVisualRig rig)
    {
        var cart = new Node3D { Name = "Handcart" };
        parent.AddChild(cart);

        var frameMat = SolidMat(new Color(0.25f, 0.28f, 0.30f));
        var trayMat = SolidMat(new Color(0.88f, 0.48f, 0.14f));
        var woodMat = SolidMat(new Color(0.54f, 0.36f, 0.18f));
        var tireMat = SolidMat(new Color(0.10f, 0.11f, 0.12f));

        // Wheelbarrow tray & handles
        cart.AddChild(Box("Tray", new Vector3(0.56f, 0.28f, 0.72f), new Vector3(0f, 0.56f, -0.22f), trayMat));
        cart.AddChild(Box("TrayRim", new Vector3(0.62f, 0.06f, 0.78f), new Vector3(0f, 0.70f, -0.22f), frameMat));
        cart.AddChild(Box("HandleL", new Vector3(0.05f, 0.05f, 0.96f), new Vector3(-0.24f, 0.68f, 0.16f), woodMat, rotDeg: new Vector3(-16f, 0f, 0f)));
        cart.AddChild(Box("HandleR", new Vector3(0.05f, 0.05f, 0.96f), new Vector3(0.24f, 0.68f, 0.16f), woodMat, rotDeg: new Vector3(-16f, 0f, 0f)));
        cart.AddChild(Box("LegStandL", new Vector3(0.05f, 0.42f, 0.05f), new Vector3(-0.22f, 0.23f, 0.14f), frameMat));
        cart.AddChild(Box("LegStandR", new Vector3(0.05f, 0.42f, 0.05f), new Vector3(0.22f, 0.23f, 0.14f), frameMat));

        // Front wheel (rotates around local X)
        var wheel = BuildWheelNode("Wheel_F", 0.22f, 0.10f, tireMat, new Vector3(0f, 0.22f, -0.62f));
        cart.AddChild(wheel);
        rig.Wheels.Add(wheel);

        // Gimbalina perched on front platform!
        BuildGimbalina(cart, rig, new Vector3(0f, 0.74f, -0.35f), scale: 1.0f);
    }

    private static void BuildGimbalina(Node3D parent, PlayerVisualRig rig, Vector3 pos, float scale, bool dashboardMascot = false)
    {
        var gBody = new Node3D { Name = "GimbalinaBody", Position = pos, Scale = Vector3.One * scale };
        parent.AddChild(gBody);
        rig.GimbalinaBody = gBody;
        rig.IsGimbalinaDashboardMascot = dashboardMascot;

        var featherMat = SolidMat(new Color(0.96f, 0.94f, 0.88f));
        var wingMat = SolidMat(new Color(0.90f, 0.85f, 0.76f));
        var combMat = SolidMat(new Color(0.88f, 0.14f, 0.14f));
        var beakMat = SolidMat(new Color(0.96f, 0.65f, 0.12f));
        var camMat = SolidMat(new Color(0.14f, 0.16f, 0.19f));

        // Plump body & tail
        gBody.AddChild(Box("Body", new Vector3(0.24f, 0.22f, 0.30f), new Vector3(0f, 0.11f, 0f), featherMat));
        gBody.AddChild(Box("Tail", new Vector3(0.14f, 0.14f, 0.12f), new Vector3(0f, 0.20f, 0.16f), wingMat, rotDeg: new Vector3(-28f, 0f, 0f)));

        var wingL = new Node3D { Name = "WingL", Position = new Vector3(-0.13f, 0.15f, 0f) };
        wingL.AddChild(Box("WingMesh", new Vector3(0.04f, 0.14f, 0.20f), new Vector3(-0.02f, -0.04f, 0f), wingMat));
        gBody.AddChild(wingL);
        rig.WingL = wingL;

        var wingR = new Node3D { Name = "WingR", Position = new Vector3(0.13f, 0.15f, 0f) };
        wingR.AddChild(Box("WingMesh", new Vector3(0.04f, 0.14f, 0.20f), new Vector3(0.02f, -0.04f, 0f), wingMat));
        gBody.AddChild(wingR);
        rig.WingR = wingR;

        // Independently counter-stabilized head!
        var gHead = new Node3D { Name = "GimbalinaHead", Position = new Vector3(0f, 0.24f, -0.11f) };
        gBody.AddChild(gHead);
        rig.GimbalinaHead = gHead;

        gHead.AddChild(Box("NeckHead", new Vector3(0.13f, 0.17f, 0.14f), new Vector3(0f, 0.08f, 0f), featherMat));
        gHead.AddChild(Box("Comb", new Vector3(0.04f, 0.07f, 0.11f), new Vector3(0f, 0.19f, 0f), combMat));
        gHead.AddChild(Box("Beak", new Vector3(0.05f, 0.04f, 0.08f), new Vector3(0f, 0.10f, -0.10f), beakMat));
        gHead.AddChild(Box("EyeL", new Vector3(0.02f, 0.03f, 0.03f), new Vector3(-0.07f, 0.12f, -0.04f), SolidMat(Colors.Black)));
        gHead.AddChild(Box("EyeR", new Vector3(0.02f, 0.03f, 0.03f), new Vector3(0.07f, 0.12f, -0.04f), SolidMat(Colors.Black)));

        if (!dashboardMascot)
        {
            // Action camera mounted on Gimbalina's crest
            gHead.AddChild(Box("ActionCam", new Vector3(0.09f, 0.07f, 0.09f), new Vector3(0f, 0.23f, -0.03f), camMat));
            gHead.AddChild(Box("ActionCamLens", new Vector3(0.05f, 0.05f, 0.03f), new Vector3(0f, 0.23f, -0.08f), SolidMat(new Color(0.2f, 0.9f, 1.0f), emit: true)));
        }
    }

    private static void BuildBicycle(Node3D parent, PlayerVisualRig rig)
    {
        var bike = new Node3D { Name = "CargoBicycle" };
        parent.AddChild(bike);
        var frame = SolidMat(new Color("35beaa"));
        var rubber = SolidMat(new Color("26282a"));
        var metal = SolidMat(new Color("b7cdce"));
        var oak = SolidMat(new Color("cb8b4c"));
        var batch = new GsaStaticMeshBatcher();
        void Tube(Vector3 a, Vector3 b, float diameter, Material mat)
        {
            var line = b - a;
            var rotation = new Quaternion(Vector3.Up, line.Normalized()).GetEuler() * (180f / Mathf.Pi);
            batch.AddBox(mat, (a + b) / 2, new Vector3(diameter, line.Length(), diameter), rotation);
        }
        var crank = new Vector3(0, .37f, 0);
        var saddle = new Vector3(0, .84f, .16f);
        var headset = new Vector3(0, .84f, -.47f);
        Tube(crank, saddle, .065f, frame);
        Tube(saddle, headset, .064f, frame);
        Tube(headset, crank, .079f, frame);
        foreach (var side in new[] { -.11f, .11f })
        {
            var rear = new Vector3(side, .32f, .62f);
            Tube(rear, saddle, .043f, frame);
            Tube(rear, crank, .041f, frame);
            Tube(new Vector3(side * .62f, .32f, -.68f), headset, .044f, metal);
        }
        Tube(headset, new Vector3(0, 1.10f, -.36f), .048f, metal);
        batch.AddBox(metal, new Vector3(0, 1.10f, -.36f), new Vector3(.57f, .046f, .046f));
        batch.AddBox(rubber, saddle, new Vector3(.25f, .08f, .31f));
        batch.AddBox(oak, new Vector3(0, .73f, -.78f), new Vector3(.52f, .06f, .49f));
        foreach (var y in new[] { .80f, .89f })
        {
            foreach (var x in new[] { -.245f, .245f })
                batch.AddBox(oak, new Vector3(x, y, -.78f), new Vector3(.025f, .052f, .49f));
            foreach (var z in new[] { -1.01f, -.55f })
                batch.AddBox(oak, new Vector3(0, y, z), new Vector3(.52f, .052f, .025f));
        }
        batch.BuildSeparatedNodes(bike, "Frame");

        var spokes = new GsaStaticMeshBatcher();
        for (var i = 0; i < 6; i++)
            spokes.AddBox(metal, Vector3.Zero, new Vector3(.015f, .57f, .015f), Vector3.Right * (i * 30));
        var spokeMesh = spokes.BuildCombinedArrayMesh();
        var tire = new TorusMesh { InnerRadius = .275f, OuterRadius = .328f, Rings = 20, RingSegments = 6 };
        foreach (var z in new[] { -.68f, .62f })
        {
            var wheel = new Node3D { Position = new Vector3(0, .328f, z) };
            wheel.AddChild(new MeshInstance3D { Mesh = tire, MaterialOverride = rubber, RotationDegrees = Vector3.Back * 90 });
            wheel.AddChild(new MeshInstance3D { Mesh = spokeMesh });
            bike.AddChild(wheel);
            rig.Wheels.Add(wheel);
            if (z < 0) rig.SteerableWheels.Add(wheel);
        }
        var pedals = new Node3D { Name = "PedalCrank", Position = crank };
        bike.AddChild(pedals);
        rig.BicycleCrank = pedals;
        foreach (var side in new[] { -1, 1 })
        {
            pedals.AddChild(Box("CrankArm", new Vector3(.032f, .15f, .044f), new Vector3(side * .13f, side * .075f, 0), metal));
            pedals.AddChild(Box("Pedal", new Vector3(.12f, .04f, .11f), new Vector3(side * .16f, side * .15f, 0), rubber));
        }
        BuildGimbalina(bike, rig, new Vector3(0, .76f, -.80f), scale: 1.15f);
    }

    private static void BuildScooter(Node3D parent, PlayerVisualRig rig)
    {
        var scooter = new Node3D { Name = "DeliveryScooter" };
        parent.AddChild(scooter);
        var bodyMat = SolidMat(new Color(0.84f, 0.18f, 0.16f));
        var trimMat = SolidMat(new Color(0.16f, 0.17f, 0.19f));
        var boxMat = SolidMat(new Color(0.95f, 0.74f, 0.18f));

        var wheelF = BuildWheelNode("Wheel_F", 0.24f, 0.10f, trimMat, new Vector3(0f, 0.24f, -0.65f));
        var wheelR = BuildWheelNode("Wheel_R", 0.24f, 0.10f, trimMat, new Vector3(0f, 0.24f, 0.62f));
        scooter.AddChild(wheelF);
        scooter.AddChild(wheelR);
        rig.Wheels.Add(wheelF);
        rig.Wheels.Add(wheelR);

        scooter.AddChild(Box("Deck", new Vector3(0.36f, 0.12f, 1.15f), new Vector3(0f, 0.34f, 0f), trimMat));
        scooter.AddChild(Box("FrontApron", new Vector3(0.38f, 0.66f, 0.28f), new Vector3(0f, 0.64f, -0.52f), bodyMat, rotDeg: new Vector3(12f, 0f, 0f)));
        scooter.AddChild(Box("Seat", new Vector3(0.32f, 0.16f, 0.60f), new Vector3(0f, 0.72f, 0.18f), trimMat));
        scooter.AddChild(Box("SurveyBox", new Vector3(0.44f, 0.36f, 0.42f), new Vector3(0f, 0.96f, 0.60f), boxMat));
        scooter.AddChild(Box("FrontCam", new Vector3(0.08f, 0.08f, 0.10f), new Vector3(0f, 0.95f, -0.62f), SolidMat(new Color(0.2f, 0.85f, 0.95f), emit: true)));
        scooter.AddChild(Box("RearCam", new Vector3(0.08f, 0.08f, 0.10f), new Vector3(0f, 1.05f, 0.82f), SolidMat(new Color(0.2f, 0.85f, 0.95f), emit: true)));

        AttachVehicleLights(scooter, rig, zFront: -0.67f, zRear: 0.78f, halfWidth: 0.12f, height: 0.68f);
    }

    private static void BuildSedanCar(Node3D parent, PlayerVisualRig rig)
    {
        var car = new Node3D { Name = "SurveyCar" };
        parent.AddChild(car);
        var paintMat = SolidMat(new Color(0.22f, 0.44f, 0.70f), roughness: 0.45f);
        var glassMat = SolidMat(new Color(0.10f, 0.17f, 0.24f), roughness: 0.18f);
        var bumperMat = SolidMat(new Color(0.14f, 0.15f, 0.17f));

        // Lower body, hood, cabin greenhouse, trunk, bumpers
        car.AddChild(Box("Chassis", new Vector3(1.76f, 0.56f, 3.85f), new Vector3(0f, 0.54f, 0f), paintMat));
        car.AddChild(Box("HoodSlope", new Vector3(1.68f, 0.14f, 1.10f), new Vector3(0f, 0.80f, -1.28f), paintMat, rotDeg: new Vector3(6f, 0f, 0f)));
        car.AddChild(Box("Cabin", new Vector3(1.52f, 0.54f, 1.75f), new Vector3(0f, 1.08f, 0.10f), paintMat));
        car.AddChild(Box("Windshield", new Vector3(1.44f, 0.46f, 1.80f), new Vector3(0f, 1.07f, 0.10f), glassMat));
        car.AddChild(Box("FrontBumper", new Vector3(1.80f, 0.20f, 0.20f), new Vector3(0f, 0.34f, -1.94f), bumperMat));
        car.AddChild(Box("RearBumper", new Vector3(1.80f, 0.20f, 0.20f), new Vector3(0f, 0.34f, 1.94f), bumperMat));

        AddFourWheels(car, rig, xOff: 0.84f, zFront: -1.18f, zRear: 1.18f, radius: 0.32f, width: 0.22f);
        AttachRoofRackAndSensors(car, rig, yRoof: 1.38f, has360: true, hasLidar: false, dualLidar: false, hasPavementCam: false);
        AttachVehicleLights(car, rig, zFront: -1.95f, zRear: 1.95f, halfWidth: 0.62f, height: 0.56f);
    }

    private static void BuildKombiVan(Node3D parent, PlayerVisualRig rig)
    {
        var kombi = new Node3D { Name = "SurveyKombi" };
        parent.AddChild(kombi);
        var lowerMat = SolidMat(new Color(0.88f, 0.44f, 0.12f), roughness: 0.5f);
        var upperMat = SolidMat(new Color(0.95f, 0.92f, 0.84f), roughness: 0.55f);
        var glassMat = SolidMat(new Color(0.11f, 0.18f, 0.25f), roughness: 0.2f);
        var bumperMat = SolidMat(new Color(0.75f, 0.77f, 0.79f));

        // Iconic split-window Brazilian Kombi silhouette
        kombi.AddChild(Box("LowerHull", new Vector3(1.84f, 0.76f, 4.25f), new Vector3(0f, 0.64f, 0f), lowerMat));
        kombi.AddChild(Box("UpperCabin", new Vector3(1.76f, 0.74f, 4.10f), new Vector3(0f, 1.38f, 0.04f), upperMat));
        kombi.AddChild(Box("WindowBand", new Vector3(1.79f, 0.44f, 3.85f), new Vector3(0f, 1.40f, 0.02f), glassMat));
        kombi.AddChild(Box("NoseBadge", new Vector3(0.42f, 0.36f, 0.08f), new Vector3(0f, 0.84f, -2.14f), upperMat));
        kombi.AddChild(Box("FrontBumper", new Vector3(1.88f, 0.18f, 0.18f), new Vector3(0f, 0.32f, -2.16f), bumperMat));
        kombi.AddChild(Box("RearBumper", new Vector3(1.88f, 0.18f, 0.18f), new Vector3(0f, 0.32f, 2.16f), bumperMat));

        AddFourWheels(kombi, rig, xOff: 0.88f, zFront: -1.30f, zRear: 1.30f, radius: 0.34f, width: 0.24f);
        AttachRoofRackAndSensors(kombi, rig, yRoof: 1.78f, has360: true, hasLidar: true, dualLidar: false, hasPavementCam: false);
        AttachVehicleLights(kombi, rig, zFront: -2.15f, zRear: 2.15f, halfWidth: 0.64f, height: 0.58f);
    }

    private static void BuildPickup4x4(Node3D parent, PlayerVisualRig rig)
    {
        var pickup = new Node3D { Name = "SurveyPickup4x4" };
        parent.AddChild(pickup);
        var paintMat = SolidMat(new Color(0.20f, 0.38f, 0.28f), roughness: 0.52f);
        var bedMat = SolidMat(new Color(0.14f, 0.15f, 0.16f));
        var glassMat = SolidMat(new Color(0.10f, 0.16f, 0.22f), roughness: 0.2f);
        var chromeMat = SolidMat(new Color(0.76f, 0.78f, 0.80f), roughness: 0.3f);

        pickup.AddChild(Box("FrontHood", new Vector3(1.88f, 0.66f, 1.45f), new Vector3(0f, 0.76f, -1.55f), paintMat));
        pickup.AddChild(Box("CrewCab", new Vector3(1.84f, 1.24f, 1.65f), new Vector3(0f, 1.08f, -0.15f), paintMat));
        pickup.AddChild(Box("CabGlass", new Vector3(1.86f, 0.48f, 1.45f), new Vector3(0f, 1.36f, -0.15f), glassMat));
        pickup.AddChild(Box("TruckBed", new Vector3(1.86f, 0.62f, 1.72f), new Vector3(0f, 0.76f, 1.50f), paintMat));
        pickup.AddChild(Box("BedLiner", new Vector3(1.66f, 0.16f, 1.54f), new Vector3(0f, 1.02f, 1.50f), bedMat));
        pickup.AddChild(Box("Bullbar", new Vector3(1.76f, 0.36f, 0.18f), new Vector3(0f, 0.56f, -2.32f), chromeMat));
        pickup.AddChild(Box("RollBar", new Vector3(1.68f, 0.62f, 0.12f), new Vector3(0f, 1.38f, 0.82f), bedMat));

        AddFourWheels(pickup, rig, xOff: 0.94f, zFront: -1.48f, zRear: 1.44f, radius: 0.42f, width: 0.28f);
        AttachRoofRackAndSensors(pickup, rig, yRoof: 1.74f, has360: true, hasLidar: true, dualLidar: true, hasPavementCam: false);
        AttachVehicleLights(pickup, rig, zFront: -2.29f, zRear: 2.38f, halfWidth: 0.68f, height: 0.72f);
    }

    private static void BuildEngineeringVan(Node3D parent, PlayerVisualRig rig)
    {
        var van = new Node3D { Name = "EngineeringMmsVan" };
        parent.AddChild(van);
        var whiteMat = SolidMat(new Color(0.92f, 0.94f, 0.95f), roughness: 0.42f);
        var stripeMat = SolidMat(new Color(0.08f, 0.58f, 0.62f), roughness: 0.4f);
        var amberMat = SolidMat(new Color(0.95f, 0.68f, 0.12f), emit: true);
        var glassMat = SolidMat(new Color(0.09f, 0.15f, 0.22f), roughness: 0.18f);
        var trimMat = SolidMat(new Color(0.14f, 0.16f, 0.18f));

        van.AddChild(Box("VanBody", new Vector3(2.02f, 1.62f, 4.95f), new Vector3(0f, 1.12f, 0.05f), whiteMat));
        van.AddChild(Box("HoodNose", new Vector3(1.94f, 0.80f, 0.95f), new Vector3(0f, 0.72f, -2.18f), whiteMat));
        van.AddChild(Box("LiveryStripe", new Vector3(2.05f, 0.22f, 4.85f), new Vector3(0f, 0.98f, 0.05f), stripeMat));
        van.AddChild(Box("Windshield", new Vector3(1.86f, 0.68f, 0.80f), new Vector3(0f, 1.44f, -1.90f), glassMat, rotDeg: new Vector3(24f, 0f, 0f)));
        van.AddChild(Box("SideWindows", new Vector3(2.04f, 0.48f, 1.15f), new Vector3(0f, 1.48f, -1.10f), glassMat));
        van.AddChild(Box("FrontBumper", new Vector3(2.06f, 0.24f, 0.24f), new Vector3(0f, 0.36f, -2.62f), trimMat));
        van.AddChild(Box("BeaconBar", new Vector3(1.10f, 0.12f, 0.24f), new Vector3(0f, 2.02f, -1.55f), amberMat));

        AddFourWheels(van, rig, xOff: 0.96f, zFront: -1.58f, zRear: 1.58f, radius: 0.38f, width: 0.26f);
        AttachRoofRackAndSensors(van, rig, yRoof: 1.98f, has360: true, hasLidar: true, dualLidar: true, hasPavementCam: true);
        AttachVehicleLights(van, rig, zFront: -2.64f, zRear: 2.54f, halfWidth: 0.72f, height: 0.66f);

        // Retired Gimbalina happily bobbling on the dashboard!
        BuildGimbalina(van, rig, new Vector3(0.35f, 1.16f, -1.80f), scale: 0.45f, dashboardMascot: true);
    }

    private static void AddFourWheels(Node3D parent, PlayerVisualRig rig, float xOff, float zFront, float zRear, float radius, float width)
    {
        var tireMat = SolidMat(new Color(0.11f, 0.12f, 0.13f));
        foreach (var (name, pos, front) in new (string, Vector3, bool)[]
        {
            ("Wheel_FL", new(-xOff, radius, zFront), true),
            ("Wheel_FR", new(xOff, radius, zFront), true),
            ("Wheel_RL", new(-xOff, radius, zRear), false),
            ("Wheel_RR", new(xOff, radius, zRear), false),
        })
        {
            var wheel = BuildWheelNode(name, radius, width, tireMat, pos);
            wheel.RotationOrder = EulerOrder.Yxz;
            parent.AddChild(wheel);
            rig.Wheels.Add(wheel);
            if (front) rig.SteerableWheels.Add(wheel);
        }
    }

    private static Node3D BuildWheelNode(string name, float radius, float width, Material tireMat, Vector3 pos)
    {
        var wheel = new Node3D { Name = name, Position = pos };
        var tire = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = width, RadialSegments = 12 },
            RotationDegrees = new Vector3(0f, 0f, 90f),
            MaterialOverride = tireMat,
        };
        var hub = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = radius * 0.56f, BottomRadius = radius * 0.56f, Height = width + 0.02f, RadialSegments = 8 },
            RotationDegrees = new Vector3(0f, 0f, 90f),
            MaterialOverride = SolidMat(new Color(0.70f, 0.73f, 0.76f)),
        };
        wheel.AddChild(tire);
        wheel.AddChild(hub);
        return wheel;
    }

    private static void AttachRoofRackAndSensors(
        Node3D parent,
        PlayerVisualRig rig,
        float yRoof,
        bool has360,
        bool hasLidar,
        bool dualLidar,
        bool hasPavementCam)
    {
        var rackMat = SolidMat(new Color(0.22f, 0.24f, 0.27f));
        var gnssMat = SolidMat(new Color(0.95f, 0.96f, 0.98f));
        var lidarMat = SolidMat(new Color(0.16f, 0.58f, 0.85f), roughness: 0.3f);

        parent.AddChild(Box("RoofRack", new Vector3(1.36f, 0.08f, 1.65f), new Vector3(0f, yRoof, 0.15f), rackMat));
        parent.AddChild(Box("GnssPole", new Vector3(0.08f, 0.28f, 0.08f), new Vector3(0f, yRoof + 0.16f, -0.35f), rackMat));
        parent.AddChild(new MeshInstance3D
        {
            Name = "GnssDome",
            Position = new Vector3(0f, yRoof + 0.30f, -0.35f),
            Mesh = new CylinderMesh { TopRadius = 0.14f, BottomRadius = 0.18f, Height = 0.09f, RadialSegments = 10 },
            MaterialOverride = gnssMat,
        });

        if (has360)
        {
            parent.AddChild(Box("Mast360", new Vector3(0.07f, 0.40f, 0.07f), new Vector3(0f, yRoof + 0.22f, 0.10f), rackMat));
            parent.AddChild(new MeshInstance3D
            {
                Name = "Cam360Orb",
                Position = new Vector3(0f, yRoof + 0.45f, 0.10f),
                Mesh = new SphereMesh { Radius = 0.13f, Height = 0.26f, RadialSegments = 10, Rings = 6 },
                MaterialOverride = SolidMat(new Color(0.12f, 0.82f, 0.92f), emit: true),
            });
        }

        if (hasLidar)
        {
            var spinner = new Node3D { Name = "LidarSpinner", Position = new Vector3(0f, yRoof + 0.20f, 0.65f) };
            if (dualLidar)
            {
                spinner.AddChild(new MeshInstance3D
                {
                    Position = new Vector3(-0.36f, 0.06f, 0f),
                    RotationDegrees = new Vector3(0f, 0f, 20f),
                    Mesh = new CylinderMesh { TopRadius = 0.11f, BottomRadius = 0.11f, Height = 0.14f, RadialSegments = 10 },
                    MaterialOverride = lidarMat,
                });
                spinner.AddChild(new MeshInstance3D
                {
                    Position = new Vector3(0.36f, 0.06f, 0f),
                    RotationDegrees = new Vector3(0f, 0f, -20f),
                    Mesh = new CylinderMesh { TopRadius = 0.11f, BottomRadius = 0.11f, Height = 0.14f, RadialSegments = 10 },
                    MaterialOverride = lidarMat,
                });
            }
            else
            {
                spinner.AddChild(new MeshInstance3D
                {
                    Position = new Vector3(0f, 0.06f, 0f),
                    Mesh = new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.12f, Height = 0.16f, RadialSegments = 10 },
                    MaterialOverride = lidarMat,
                });
            }
            parent.AddChild(spinner);
            rig.LidarSpinner = spinner;
        }

        if (hasPavementCam)
        {
            parent.AddChild(Box("PavementBar", new Vector3(1.65f, 0.08f, 0.12f), new Vector3(0f, yRoof - 0.18f, 2.55f), rackMat));
            parent.AddChild(Box("PavementCamL", new Vector3(0.14f, 0.12f, 0.14f), new Vector3(-0.55f, yRoof - 0.24f, 2.58f), lidarMat));
            parent.AddChild(Box("PavementCamR", new Vector3(0.14f, 0.12f, 0.14f), new Vector3(0.55f, yRoof - 0.24f, 2.58f), lidarMat));
        }
    }

    private static void AttachVehicleLights(Node3D parent, PlayerVisualRig rig, float zFront, float zRear, float halfWidth, float height)
    {
        var headMat = SolidStandardMat(new Color(1.0f, 0.96f, 0.80f), emit: true, emissionEnergy: 0.25f);
        var tailMat = SolidStandardMat(new Color(0.95f, 0.14f, 0.12f), emit: true, emissionEnergy: 0.10f);
        rig.HeadlightMat = headMat;
        rig.TaillightMat = tailMat;

        parent.AddChild(Box("HeadLampL", new Vector3(0.24f, 0.14f, 0.06f), new Vector3(-halfWidth, height, zFront), headMat));
        parent.AddChild(Box("HeadLampR", new Vector3(0.24f, 0.14f, 0.06f), new Vector3(halfWidth, height, zFront), headMat));
        parent.AddChild(Box("TailLampL", new Vector3(0.24f, 0.14f, 0.06f), new Vector3(-halfWidth, height + 0.04f, zRear), tailMat));
        parent.AddChild(Box("TailLampR", new Vector3(0.24f, 0.14f, 0.06f), new Vector3(halfWidth, height + 0.04f, zRear), tailMat));
        // Side mirrors + exhaust tailpipe for crisp GTA III silhouette!
        var mirrorMat = SolidMat(new Color(0.16f, 0.17f, 0.19f));
        var mirrorZ = zFront * 0.42f;
        parent.AddChild(Box("MirrorL", new Vector3(0.14f, 0.09f, 0.10f), new Vector3(-(halfWidth + 0.34f), height + 0.42f, mirrorZ), mirrorMat));
        parent.AddChild(Box("MirrorR", new Vector3(0.14f, 0.09f, 0.10f), new Vector3(halfWidth + 0.34f, height + 0.42f, mirrorZ), mirrorMat));
        parent.AddChild(Box("LicensePlateR", new Vector3(0.42f, 0.16f, 0.04f), new Vector3(0f, height - 0.10f, zRear), SolidMat(new Color(0.92f, 0.90f, 0.78f))));
        parent.AddChild(Box("ExhaustPipe", new Vector3(0.09f, 0.09f, 0.24f), new Vector3(-halfWidth * 0.72f, 0.22f, zRear - 0.04f), SolidMat(new Color(0.55f, 0.58f, 0.60f))));

        var spotL = new SpotLight3D
        {
            Name = "HeadlightConeL",
            Position = new Vector3(-halfWidth, height, zFront - 0.1f),
            LightColor = new Color(1.0f, 0.95f, 0.78f),
            LightEnergy = 2.6f,
            SpotRange = 28f,
            SpotAngle = 36f,
            Visible = false,
        };
        var spotR = new SpotLight3D
        {
            Name = "HeadlightConeR",
            Position = new Vector3(halfWidth, height, zFront - 0.1f),
            LightColor = new Color(1.0f, 0.95f, 0.78f),
            LightEnergy = 2.6f,
            SpotRange = 28f,
            SpotAngle = 36f,
            Visible = false,
        };
        parent.AddChild(spotL);
        parent.AddChild(spotR);
        rig.HeadlightL = spotL;
        rig.HeadlightR = spotR;

        // Lightweight GTA III projected ground headlight beam cones (smooth vertex-alpha fade)
        var beamMat = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var nearCol = new Color(1.0f, 0.95f, 0.74f, 0.24f);
        var farCol = new Color(1.0f, 0.92f, 0.68f, 0.0f);
        void AddCone(float xCenter)
        {
            var p0 = new Vector3(xCenter - 0.14f, 0.07f, zFront);
            var p1 = new Vector3(xCenter + 0.14f, 0.07f, zFront);
            var p2 = new Vector3(xCenter + 2.8f, 0.07f, zFront - 18.0f);
            var p3 = new Vector3(xCenter - 2.8f, 0.07f, zFront - 18.0f);
            st.SetColor(nearCol); st.SetNormal(Vector3.Up); st.AddVertex(p0);
            st.SetColor(nearCol); st.SetNormal(Vector3.Up); st.AddVertex(p1);
            st.SetColor(farCol);  st.SetNormal(Vector3.Up); st.AddVertex(p3);
            st.SetColor(nearCol); st.SetNormal(Vector3.Up); st.AddVertex(p1);
            st.SetColor(farCol);  st.SetNormal(Vector3.Up); st.AddVertex(p2);
            st.SetColor(farCol);  st.SetNormal(Vector3.Up); st.AddVertex(p3);
        }
        AddCone(-halfWidth);
        AddCone(halfWidth);
        var beamMesh = new MeshInstance3D
        {
            Name = "HeadlightBeamCone",
            Mesh = st.Commit(),
            MaterialOverride = beamMat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        parent.AddChild(beamMesh);
        rig.HeadlightBeam = beamMesh;
    }

    /// <summary>Builds sculpted GTA III low-poly traffic meshes (+X forward!) for MultiMeshInstance3D.</summary>
    public static ArrayMesh BuildTrafficCarMesh(int variant)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        // Remember: TrafficBenchAdapter uses +X = forward, +Y = up, +Z = right!
        // Origin Y = 0 is bottom of wheels.
        void AddBoxPart(Vector3 center, Vector3 size, Color col)
        {
            var hx = size.X * 0.5f;
            var hy = size.Y * 0.5f;
            var hz = size.Z * 0.5f;
            var p000 = center + new Vector3(-hx, -hy, -hz);
            var p001 = center + new Vector3(-hx, -hy, hz);
            var p010 = center + new Vector3(-hx, hy, -hz);
            var p011 = center + new Vector3(-hx, hy, hz);
            var p100 = center + new Vector3(hx, -hy, -hz);
            var p101 = center + new Vector3(hx, -hy, hz);
            var p110 = center + new Vector3(hx, hy, -hz);
            var p111 = center + new Vector3(hx, hy, hz);

            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
            {
                // Clockwise front-face winding matching Godot BoxMesh
                st.SetColor(col);
                st.SetNormal(normal);
                st.AddVertex(a);
                st.SetColor(col);
                st.SetNormal(normal);
                st.AddVertex(d);
                st.SetColor(col);
                st.SetNormal(normal);
                st.AddVertex(c);

                st.SetColor(col);
                st.SetNormal(normal);
                st.AddVertex(a);
                st.SetColor(col);
                st.SetNormal(normal);
                st.AddVertex(c);
                st.SetColor(col);
                st.SetNormal(normal);
                st.AddVertex(b);
            }

            Quad(p101, p100, p110, p111, Vector3.Right);   // +X front
            Quad(p000, p001, p011, p010, Vector3.Left);    // -X rear
            Quad(p011, p111, p110, p010, Vector3.Up);      // +Y top
            Quad(p000, p100, p101, p001, Vector3.Down);    // -Y bottom
            Quad(p001, p101, p111, p011, Vector3.Back);    // +Z side
            Quad(p100, p000, p010, p110, Vector3.Forward); // -Z side
        }

        var darkTire = new Color(0.14f, 0.14f, 0.15f);
        var darkGlass = new Color(0.18f, 0.25f, 0.34f);
        var bumperCol = new Color(0.22f, 0.23f, 0.25f);
        var whitePaint = Colors.White; // Multiplied by MultiMesh InstanceColor!

        // 4 wheels
        AddBoxPart(new Vector3(1.22f, 0.28f, -0.84f), new Vector3(0.56f, 0.56f, 0.22f), darkTire);
        AddBoxPart(new Vector3(1.22f, 0.28f, 0.84f), new Vector3(0.56f, 0.56f, 0.22f), darkTire);
        AddBoxPart(new Vector3(-1.22f, 0.28f, -0.84f), new Vector3(0.56f, 0.56f, 0.22f), darkTire);
        AddBoxPart(new Vector3(-1.22f, 0.28f, 0.84f), new Vector3(0.56f, 0.56f, 0.22f), darkTire);

        if (variant == 2) // Delivery Van / Kombi
        {
            AddBoxPart(new Vector3(0f, 0.72f, 0f), new Vector3(4.15f, 0.88f, 1.86f), whitePaint);
            AddBoxPart(new Vector3(-0.08f, 1.42f, 0f), new Vector3(3.92f, 0.64f, 1.76f), whitePaint);
            AddBoxPart(new Vector3(1.52f, 1.38f, 0f), new Vector3(0.75f, 0.44f, 1.62f), darkGlass);
            AddBoxPart(new Vector3(2.10f, 0.34f, 0f), new Vector3(0.14f, 0.18f, 1.90f), bumperCol);
            AddBoxPart(new Vector3(-2.10f, 0.34f, 0f), new Vector3(0.14f, 0.18f, 1.90f), bumperCol);
        }
        else if (variant == 3) // Utility Pickup
        {
            AddBoxPart(new Vector3(0f, 0.62f, 0f), new Vector3(4.25f, 0.68f, 1.88f), whitePaint);
            AddBoxPart(new Vector3(0.35f, 1.24f, 0f), new Vector3(1.55f, 0.58f, 1.68f), whitePaint);
            AddBoxPart(new Vector3(0.35f, 1.22f, 0f), new Vector3(1.62f, 0.42f, 1.56f), darkGlass);
            AddBoxPart(new Vector3(-1.25f, 1.00f, 0f), new Vector3(1.55f, 0.10f, 1.62f), bumperCol);
            AddBoxPart(new Vector3(2.14f, 0.34f, 0f), new Vector3(0.14f, 0.20f, 1.90f), bumperCol);
        }
        else // Sedan (0) or Taxi (1)
        {
            AddBoxPart(new Vector3(0f, 0.56f, 0f), new Vector3(4.10f, 0.58f, 1.84f), whitePaint);
            AddBoxPart(new Vector3(-0.14f, 1.12f, 0f), new Vector3(1.90f, 0.54f, 1.58f), whitePaint);
            AddBoxPart(new Vector3(-0.14f, 1.10f, 0f), new Vector3(1.98f, 0.40f, 1.48f), darkGlass);
            AddBoxPart(new Vector3(2.06f, 0.32f, 0f), new Vector3(0.14f, 0.18f, 1.88f), bumperCol);
            AddBoxPart(new Vector3(-2.06f, 0.32f, 0f), new Vector3(0.14f, 0.18f, 1.88f), bumperCol);
            if (variant == 1) // Taxi roof sign
                AddBoxPart(new Vector3(0.0f, 1.46f, 0f), new Vector3(0.36f, 0.16f, 0.58f), new Color(1.0f, 0.88f, 0.22f));
        }

        return st.Commit();
    }

    private static void AddSurfaceBoxPart(
        SurfaceTool st,
        Vector3 center,
        Vector3 size,
        Color col)
    {
        var hx = size.X * 0.5f;
        var hy = size.Y * 0.5f;
        var hz = size.Z * 0.5f;
        var p000 = center + new Vector3(-hx, -hy, -hz);
        var p001 = center + new Vector3(-hx, -hy, hz);
        var p010 = center + new Vector3(-hx, hy, -hz);
        var p011 = center + new Vector3(-hx, hy, hz);
        var p100 = center + new Vector3(hx, -hy, -hz);
        var p101 = center + new Vector3(hx, -hy, hz);
        var p110 = center + new Vector3(hx, hy, -hz);
        var p111 = center + new Vector3(hx, hy, hz);

        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
        {
            st.SetColor(col); st.SetNormal(normal); st.AddVertex(a);
            st.SetColor(col); st.SetNormal(normal); st.AddVertex(d);
            st.SetColor(col); st.SetNormal(normal); st.AddVertex(c);
            st.SetColor(col); st.SetNormal(normal); st.AddVertex(a);
            st.SetColor(col); st.SetNormal(normal); st.AddVertex(c);
            st.SetColor(col); st.SetNormal(normal); st.AddVertex(b);
        }

        Quad(p101, p100, p110, p111, Vector3.Right);
        Quad(p000, p001, p011, p010, Vector3.Left);
        Quad(p011, p111, p110, p010, Vector3.Up);
        Quad(p000, p100, p101, p001, Vector3.Down);
        Quad(p001, p101, p111, p011, Vector3.Back);
        Quad(p100, p000, p010, p110, Vector3.Forward);
    }

    public static ArrayMesh BuildTrafficPedestrianMesh()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        var trouser = new Color(0.18f, 0.22f, 0.30f);
        var shoe = new Color(0.10f, 0.10f, 0.12f);
        var skin = new Color(0.82f, 0.65f, 0.53f);
        AddSurfaceBoxPart(st, new Vector3(0f, 0.42f, -0.09f), new Vector3(0.14f, 0.68f, 0.14f), trouser);
        AddSurfaceBoxPart(st, new Vector3(0.03f, 0.05f, -0.09f), new Vector3(0.20f, 0.10f, 0.15f), shoe);
        AddSurfaceBoxPart(st, new Vector3(0f, 0.42f, 0.09f), new Vector3(0.14f, 0.68f, 0.14f), trouser);
        AddSurfaceBoxPart(st, new Vector3(0.03f, 0.05f, 0.09f), new Vector3(0.20f, 0.10f, 0.15f), shoe);
        AddSurfaceBoxPart(st, new Vector3(0f, 1.04f, 0f), new Vector3(0.24f, 0.56f, 0.40f), Colors.White);
        AddSurfaceBoxPart(st, new Vector3(0f, 1.04f, -0.26f), new Vector3(0.12f, 0.44f, 0.11f), Colors.White);
        AddSurfaceBoxPart(st, new Vector3(0f, 1.04f, 0.26f), new Vector3(0.12f, 0.44f, 0.11f), Colors.White);
        AddSurfaceBoxPart(st, new Vector3(0f, 0.77f, -0.26f), new Vector3(0.10f, 0.10f, 0.10f), skin);
        AddSurfaceBoxPart(st, new Vector3(0f, 0.77f, 0.26f), new Vector3(0.10f, 0.10f, 0.10f), skin);
        AddSurfaceBoxPart(st, new Vector3(0f, 1.48f, 0f), new Vector3(0.22f, 0.26f, 0.22f), skin);
        AddSurfaceBoxPart(st, new Vector3(-0.01f, 1.59f, 0f), new Vector3(0.24f, 0.09f, 0.24f), new Color(0.18f, 0.14f, 0.12f));
        return st.Commit();
    }

    public static ArrayMesh BuildTrafficPedestrianBodyMesh()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        AddSurfaceBoxPart(st, new Vector3(0f, 1.04f, 0f), new Vector3(0.24f, 0.56f, 0.40f), Colors.White);
        AddSurfaceBoxPart(st, new Vector3(0f, 1.48f, 0f), new Vector3(0.22f, 0.26f, 0.22f), new Color(0.82f, 0.65f, 0.53f));
        AddSurfaceBoxPart(st, new Vector3(-0.01f, 1.59f, 0f), new Vector3(0.24f, 0.09f, 0.24f), new Color(0.18f, 0.14f, 0.12f));
        return st.Commit();
    }

    public static ArrayMesh BuildTrafficPedestrianLegMesh(bool isLeft)
    {
        // Origin at hip pivot (Y = 0.76f above ground, local Z = +-0.09f)
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var z = isLeft ? -0.09f : 0.09f;
        AddSurfaceBoxPart(st, new Vector3(0f, -0.33f, z), new Vector3(0.14f, 0.66f, 0.14f), new Color(0.18f, 0.22f, 0.30f));
        AddSurfaceBoxPart(st, new Vector3(0.03f, -0.71f, z), new Vector3(0.21f, 0.10f, 0.15f), new Color(0.10f, 0.10f, 0.12f));
        return st.Commit();
    }

    public static ArrayMesh BuildTrafficPedestrianArmMesh(bool isLeft)
    {
        // Origin at shoulder pivot (Y = 1.24f above ground, local Z = +-0.26f)
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var z = isLeft ? -0.26f : 0.26f;
        AddSurfaceBoxPart(st, new Vector3(0f, -0.22f, z), new Vector3(0.12f, 0.44f, 0.11f), Colors.White);
        AddSurfaceBoxPart(st, new Vector3(0f, -0.47f, z), new Vector3(0.10f, 0.10f, 0.10f), new Color(0.82f, 0.65f, 0.53f));
        return st.Commit();
    }

    public static Node3D BuildStreetTree(string name, float scale = 1.0f)
    {
        var tree = new Node3D { Name = name, Scale = Vector3.One * scale };
        tree.AddChild(new MeshInstance3D
        {
            Name = "Trunk",
            Position = new Vector3(0f, 1.3f, 0f),
            Mesh = TreeTrunkMesh,
            MaterialOverride = SolidMat(new Color(0.36f, 0.24f, 0.14f)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        tree.AddChild(new MeshInstance3D
        {
            Name = "CanopyLower",
            Position = new Vector3(0f, 3.0f, 0f),
            Mesh = TreeCanopyLowerMesh,
            MaterialOverride = GsaRetroTextures.CreateWorldMaterial("tree_foliage_mat", "park_grass", new Color(0.85f, 1.0f, 0.78f)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        tree.AddChild(new MeshInstance3D
        {
            Name = "CanopyTop",
            Position = new Vector3(0.2f, 4.1f, -0.1f),
            Mesh = TreeCanopyTopMesh,
            MaterialOverride = GsaRetroTextures.CreateWorldMaterial("tree_foliage_top_mat", "park_grass", new Color(0.95f, 1.1f, 0.85f)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        AddShadowBlob(tree, 3.0f, 3.0f);
        return tree;
    }

    public static (Node3D Node, StandardMaterial3D BulbMat, OmniLight3D? Light) BuildStreetlamp(string name, bool withRealLight)
    {
        var root = new Node3D { Name = name };
        var poleMat = SolidMat(new Color(0.24f, 0.27f, 0.30f));
        var bulbMat = SolidStandardMat(new Color(1.0f, 0.88f, 0.56f), emit: true, emissionEnergy: 0.0f);

        root.AddChild(Box("Pole", new Vector3(0.14f, 5.2f, 0.14f), new Vector3(0f, 2.6f, 0f), poleMat));
        root.AddChild(Box("Arm", new Vector3(1.1f, 0.10f, 0.10f), new Vector3(0.50f, 5.15f, 0f), poleMat));
        root.AddChild(Box("Bulb", new Vector3(0.34f, 0.10f, 0.22f), new Vector3(0.98f, 5.06f, 0f), bulbMat));

        OmniLight3D? light = null;
        if (withRealLight)
        {
            light = new OmniLight3D
            {
                Name = "LampGlow",
                Position = new Vector3(0.98f, 4.85f, 0f),
                LightColor = new Color(1.0f, 0.84f, 0.52f),
                LightEnergy = 1.8f,
                OmniRange = 16f,
                Visible = false,
            };
            root.AddChild(light);
        }
        return (root, bulbMat, light);
    }

    public static (Node3D Node, StandardMaterial3D RedMat, StandardMaterial3D AmberMat, StandardMaterial3D GreenMat) BuildTrafficSignal(string name)
    {
        var root = new Node3D { Name = name };
        var poleMat = SolidMat(new Color(0.20f, 0.22f, 0.24f));
        var housingMat = SolidMat(new Color(0.11f, 0.12f, 0.13f));
        var redMat = SolidStandardMat(new Color(0.95f, 0.15f, 0.12f), emit: true, emissionEnergy: 0.1f);
        var amberMat = SolidStandardMat(new Color(0.98f, 0.72f, 0.10f), emit: true, emissionEnergy: 0.1f);
        var greenMat = SolidStandardMat(new Color(0.16f, 0.92f, 0.42f), emit: true, emissionEnergy: 1.8f);

        root.AddChild(Box("Post", new Vector3(0.16f, 4.8f, 0.16f), new Vector3(0f, 2.4f, 0f), poleMat));
        root.AddChild(Box("Cantilever", new Vector3(2.4f, 0.12f, 0.12f), new Vector3(1.1f, 4.7f, 0f), poleMat));
        root.AddChild(Box("SignalBox", new Vector3(0.40f, 1.05f, 0.34f), new Vector3(1.8f, 4.15f, 0f), housingMat));
        root.AddChild(Box("RedLight", new Vector3(0.22f, 0.22f, 0.38f), new Vector3(1.8f, 4.46f, 0f), redMat));
        root.AddChild(Box("AmberLight", new Vector3(0.22f, 0.22f, 0.38f), new Vector3(1.8f, 4.15f, 0f), amberMat));
        root.AddChild(Box("GreenLight", new Vector3(0.22f, 0.22f, 0.38f), new Vector3(1.8f, 3.84f, 0f), greenMat));
        return (root, redMat, amberMat, greenMat);
    }

    public static Node3D BuildBeaconRing(string name, Color color, float radius = 1.3f, float height = 1.0f)
    {
        var ring = new Node3D { Name = name };
        var mat = new StandardMaterial3D
        {
            AlbedoColor = new Color(color.R, color.G, color.B, 0.20f),
            EmissionEnabled = true,
            Emission = color,
            EmissionEnergyMultiplier = 0.95f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        ring.AddChild(new MeshInstance3D
        {
            Name = "Cylinder",
            Position = new Vector3(0f, height * 0.5f, 0f),
            Mesh = new CylinderMesh
            {
                TopRadius = radius,
                BottomRadius = radius,
                Height = height,
                RadialSegments = 18,
                CapTop = false,
                CapBottom = false,
            },
            MaterialOverride = mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        return ring;
    }

    public static MeshInstance3D Box(string name, Vector3 size, Vector3 pos, Material mat, Vector3? rotDeg = null)
    {
        var node = new MeshInstance3D
        {
            Name = name,
            Position = pos,
            Mesh = new BoxMesh { Size = size },
            MaterialOverride = mat,
        };
        if (rotDeg.HasValue)
            node.RotationDegrees = rotDeg.Value;
        return node;
    }

    public static StandardMaterial3D SolidStandardMat(
        Color color,
        float roughness = 0.78f,
        bool emit = false,
        float emissionEnergy = 1.2f)
    {
        var mat = new StandardMaterial3D
        {
            AlbedoColor = color,
            Roughness = roughness,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.PerVertex,
            DiffuseMode = BaseMaterial3D.DiffuseModeEnum.Lambert,
            DisableReceiveShadows = true,
            DisableSpecularOcclusion = true,
            SpecularMode = roughness < 0.35f
                ? BaseMaterial3D.SpecularModeEnum.SchlickGgx
                : BaseMaterial3D.SpecularModeEnum.Disabled,
        };
        if (emit)
        {
            mat.EmissionEnabled = true;
            mat.Emission = color;
            mat.EmissionEnergyMultiplier = emissionEnergy;
        }
        return mat;
    }

    public static Material SolidMat(
        Color color,
        float roughness = 0.78f,
        bool emit = false,
        float emissionEnergy = 1.2f,
        bool uncached = false)
    {
        var key = (
            color.ToRgba32(),
            (byte)Math.Clamp((int)MathF.Round(roughness * 255f), 0, 255),
            emit,
            emit ? (short)Math.Clamp((int)MathF.Round(emissionEnergy * 100f), short.MinValue, short.MaxValue) : (short)0);

        if (!uncached && SolidMaterialCache.TryGetValue(key, out var cached))
            return cached;

        if (uncached)
            return SolidStandardMat(color, roughness, emit, emissionEnergy);

        var mat = GsaRetroShaders.CreateSolidMaterial(color, emit, emissionEnergy);
        SolidMaterialCache[key] = mat;
        return mat;
    }

    public static Node3D BuildStorefrontSignboard(string name, Color neonColor, string labelText, float width = 3.2f)
    {
        var sign = new Node3D { Name = name };
        var frameMat = SolidMat(new Color(0.12f, 0.13f, 0.15f));
        var neonMat = SolidMat(neonColor, roughness: 0.35f, emit: true, emissionEnergy: 0.95f);
        sign.AddChild(Box("Frame", new Vector3(width + 0.16f, 0.72f, 0.18f), Vector3.Zero, frameMat));
        sign.AddChild(Box("GlowPanel", new Vector3(width, 0.56f, 0.20f), new Vector3(0f, 0f, 0.02f), neonMat));
        sign.AddChild(new Label3D
        {
            Text = labelText,
            Position = new Vector3(0f, 0f, 0.16f),
            FontSize = 36,
            PixelSize = 0.008f,
            Modulate = Colors.White,
            OutlineModulate = Colors.Black,
            OutlineSize = 4,
            AlphaCut = Label3D.AlphaCutMode.Disabled,
            VisibilityRangeEnd = 70f,
        });
        return sign;
    }

    public static Node3D BuildFireHydrant(string name)
    {
        var h = new Node3D { Name = name };
        var redMat = SolidMat(new Color(0.84f, 0.14f, 0.12f), roughness: 0.45f);
        h.AddChild(new MeshInstance3D { Position = new Vector3(0f, 0.38f, 0f), Mesh = new CylinderMesh { TopRadius = 0.13f, BottomRadius = 0.16f, Height = 0.76f, RadialSegments = 8 }, MaterialOverride = redMat });
        h.AddChild(new MeshInstance3D { Position = new Vector3(0f, 0.80f, 0f), Mesh = new SphereMesh { Radius = 0.15f, Height = 0.22f, RadialSegments = 8, Rings = 4 }, MaterialOverride = redMat });
        h.AddChild(Box("Nozzles", new Vector3(0.42f, 0.10f, 0.10f), new Vector3(0f, 0.54f, 0f), SolidMat(new Color(0.75f, 0.77f, 0.80f))));
        return h;
    }

    public static Node3D BuildTrashDumpster(string name)
    {
        var d = new Node3D { Name = name };
        var bodyMat = SolidMat(new Color(0.16f, 0.38f, 0.24f), roughness: 0.7f);
        var lidMat = SolidMat(new Color(0.12f, 0.13f, 0.14f), roughness: 0.55f);
        d.AddChild(Box("Bin", new Vector3(1.85f, 1.05f, 1.10f), new Vector3(0f, 0.56f, 0f), bodyMat));
        d.AddChild(Box("Lid", new Vector3(1.92f, 0.12f, 1.16f), new Vector3(0f, 1.12f, 0f), lidMat));
        return d;
    }

    public static Node3D BuildBusStopShelter(string name)
    {
        var bus = new Node3D { Name = name };
        var metal = SolidMat(new Color(0.24f, 0.27f, 0.32f));
        var glass = new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.72f, 0.86f, 0.45f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.2f };
        var benchMat = SolidMat(new Color(0.72f, 0.52f, 0.18f));
        bus.AddChild(Box("BackPanel", new Vector3(3.4f, 2.2f, 0.08f), new Vector3(0f, 1.15f, -0.55f), glass));
        bus.AddChild(Box("Roof", new Vector3(3.7f, 0.12f, 1.5f), new Vector3(0f, 2.32f, 0f), metal));
        bus.AddChild(Box("PostL", new Vector3(0.10f, 2.3f, 0.10f), new Vector3(-1.65f, 1.15f, -0.55f), metal));
        bus.AddChild(Box("PostR", new Vector3(0.10f, 2.3f, 0.10f), new Vector3(1.65f, 1.15f, -0.55f), metal));
        bus.AddChild(Box("Bench", new Vector3(2.4f, 0.12f, 0.46f), new Vector3(0f, 0.48f, -0.22f), benchMat));
        bus.AddChild(Box("AdLight", new Vector3(0.22f, 1.85f, 1.10f), new Vector3(1.72f, 1.15f, 0f), SolidMat(new Color(0.95f, 0.82f, 0.32f), emit: true, emissionEnergy: 0.7f)));
        return bus;
    }

    public static Node3D BuildPotholeAndCones(string name)
    {
        var p = new Node3D { Name = name };
        var craterMat = SolidMat(new Color(0.08f, 0.07f, 0.06f), roughness: 0.95f);
        var rimMat = SolidMat(new Color(0.32f, 0.31f, 0.28f), roughness: 0.9f);
        p.AddChild(new MeshInstance3D { Position = new Vector3(0f, 0.02f, 0f), Mesh = new CylinderMesh { TopRadius = 1.25f, BottomRadius = 1.35f, Height = 0.03f, RadialSegments = 12 }, MaterialOverride = rimMat });
        p.AddChild(new MeshInstance3D { Position = new Vector3(0f, 0.03f, 0f), Mesh = new CylinderMesh { TopRadius = 0.90f, BottomRadius = 0.75f, Height = 0.025f, RadialSegments = 10 }, MaterialOverride = craterMat });
        var coneMat = SolidMat(new Color(0.98f, 0.42f, 0.08f), emit: true, emissionEnergy: 0.25f);
        foreach (var off in new Vector3[] { new(-1.45f, 0.32f, -0.8f), new(1.45f, 0.32f, 0.8f) })
            p.AddChild(new MeshInstance3D { Position = off, Mesh = new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.22f, Height = 0.64f, RadialSegments = 8 }, MaterialOverride = coneMat });
        return p;
    }

    public static Node3D BuildPoliceBlitzUnit(string name)
    {
        var blitz = new Node3D { Name = name };
        var bwBlack = SolidMat(new Color(0.10f, 0.11f, 0.13f), roughness: 0.4f);
        var bwWhite = SolidMat(new Color(0.92f, 0.93f, 0.94f), roughness: 0.4f);
        var redBar = SolidMat(new Color(0.98f, 0.16f, 0.16f), emit: true, emissionEnergy: 2.2f);
        var blueBar = SolidMat(new Color(0.18f, 0.56f, 1.0f), emit: true, emissionEnergy: 2.2f);

        // Patrol cruiser parked across lane
        var car = new Node3D { Position = new Vector3(0f, 0f, 0f), RotationDegrees = new Vector3(0f, 70f, 0f) };
        car.AddChild(Box("Chassis", new Vector3(1.80f, 0.56f, 4.05f), new Vector3(0f, 0.54f, 0f), bwBlack));
        car.AddChild(Box("Doors", new Vector3(1.82f, 0.54f, 1.70f), new Vector3(0f, 0.56f, 0f), bwWhite));
        car.AddChild(Box("Cabin", new Vector3(1.56f, 0.52f, 1.70f), new Vector3(0f, 1.06f, 0f), bwWhite));
        car.AddChild(Box("SirenRed", new Vector3(0.45f, 0.14f, 0.26f), new Vector3(-0.24f, 1.38f, 0f), redBar));
        car.AddChild(Box("SirenBlue", new Vector3(0.45f, 0.14f, 0.26f), new Vector3(0.24f, 1.38f, 0f), blueBar));
        blitz.AddChild(car);

        // Barrier horses + cones
        var barrierMat = SolidMat(new Color(0.95f, 0.72f, 0.14f));
        blitz.AddChild(Box("BarrierA", new Vector3(3.0f, 0.92f, 0.24f), new Vector3(-2.4f, 0.46f, 1.2f), barrierMat));
        blitz.AddChild(Box("BarrierB", new Vector3(3.0f, 0.92f, 0.24f), new Vector3(2.4f, 0.46f, -1.2f), barrierMat));
        return blitz;
    }

    public static Node3D BuildTrioEletricoTruck(string name)
    {
        var trio = new Node3D { Name = name };
        var bodyMat = SolidMat(new Color(0.92f, 0.22f, 0.58f), roughness: 0.4f);
        var speakerMat = SolidMat(new Color(0.14f, 0.16f, 0.18f));
        var neonCyan = SolidMat(new Color(0.22f, 0.95f, 0.92f), emit: true, emissionEnergy: 1.8f);
        var neonGold = SolidMat(new Color(0.98f, 0.85f, 0.18f), emit: true, emissionEnergy: 1.8f);

        trio.AddChild(Box("TruckBase", new Vector3(2.8f, 1.2f, 8.8f), new Vector3(0f, 0.8f, 0f), bodyMat));
        trio.AddChild(Box("StageDeck", new Vector3(2.7f, 2.2f, 6.2f), new Vector3(0f, 2.3f, 0.9f), speakerMat));
        trio.AddChild(Box("SpeakerGridL", new Vector3(0.22f, 1.9f, 5.8f), new Vector3(-1.38f, 2.3f, 0.9f), neonCyan));
        trio.AddChild(Box("SpeakerGridR", new Vector3(0.22f, 1.9f, 5.8f), new Vector3(1.38f, 2.3f, 0.9f), neonGold));
        trio.AddChild(Box("TopBanner", new Vector3(2.6f, 0.55f, 0.4f), new Vector3(0f, 3.65f, -2.0f), neonGold));
        return trio;
    }
}

