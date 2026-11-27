using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Traffic;
using Gsa.Core.World;
using Gsa.Engine.Visuals;
using Gsa.Missions.Activities;
using Gsa.Missions.Campaign;
using Gsa.Missions.Economy;
using Gsa.Missions.Gameplay;
using Gsa.Missions.Office;
using Godot;

namespace Gsa.Engine.World;

public partial class SaoDatumCityScene
{
    public static Vector3d GarageBayCenterEnu { get; } = new(500174.0, 7400034.5, 100.0);
    private static readonly Vector3d GarageTriggerEnu = new(500174.0, 7400033.2, 100.0);

    private static readonly (string RegionId, Vector3d CoronaEnu, Vector3d PayphoneEnu)[] DistrictDispatchSpots =
    [
        ("old_center", new(500236.5, 7399999.5, 100.0), new(500195.5, 7399997.6, 100.0)),
        ("port", new(500442.0, 7401134.0, 100.0), new(500436.0, 7401138.0, 100.0)),
        ("waterfront", new(501252.0, 7400134.0, 100.0), new(501246.0, 7400138.0, 100.0)),
        ("serra", new(501028.0, 7401028.0, 100.0), new(501024.0, 7401032.0, 100.0)),
    ];

    private static readonly Color[] GarageResprayPalette =
    [
        new(0.16f, 0.44f, 0.78f), // Cobalt Fleet
        new(0.92f, 0.74f, 0.14f), // Geodetic Gold
        new(0.80f, 0.24f, 0.20f), // Corsa Crimson
        new(0.14f, 0.58f, 0.40f), // Survey Emerald
        new(0.90f, 0.44f, 0.14f), // Terracotta Flame
        new(0.86f, 0.88f, 0.90f), // Pearl Silver
    ];

    private BlooperAlbumAndCosmeticsCoordinator _cosmetics = new();
    private readonly List<ObstacleBoxEnu> _garageObstacles = [];
    private readonly List<Label3D> _parkedVehicleLabels = [];
    private readonly List<(string RegionId, Vector3d PosEnu, Node3D Ring)> _payphoneNodes = [];
    private Node3D? _storyCoronaNode;
    private Label3D? _storyCoronaLabel;
    private Vector3d _activeStoryCoronaEnu;
    private CampaignMissionFullSpec? _activeDispatchMission;
    private double _garageCooldown;
    private double _payphoneRingCooldown = 2.0;
    private int _garagePaintIndex;
    private Color? _garagePaintOverride;

    private Node3D? _gpsArrowNode;
    private StandardMaterial3D? _gpsArrowMat;
    private bool _gpsArrowEnabled = true;

    private const int MaxSkidMarks = 96;
    private MultiMeshInstance3D? _skidMarksMesh;
    private int _skidWriteIndex;
    private int _skidVisibleCount;
    private Vector3d _lastSkidStampEnu;
    private bool _hasLastSkidStamp;

    private void BuildStreetInteractions(Node3D visualBase)
    {
        BuildWorkshopGarage(visualBase);
        BuildDispatchPayphonesAndStoryCorona(visualBase);
        BuildGpsWaypointArrow(visualBase);
        BuildTireSkidMarksOverlay(visualBase);
    }

    private void BuildWorkshopGarage(Node3D visualBase)
    {
        _garageObstacles.Clear();
        var garageBatch = new GsaStaticMeshBatcher();
        var wallMat = GsaRetroTextures.CreateWorldMaterial(
            "garage_wall_uv", "facade_concrete", new Color(0.88f, 0.90f, 0.92f), triplanar: false, nightWindowGlow: false);
        var roofMat = GsaMeshBuilder.SolidMat(new Color(0.18f, 0.21f, 0.24f), roughness: 0.6f);
        var trimMat = GsaMeshBuilder.SolidMat(new Color(0.14f, 0.72f, 0.48f), emit: true, emissionEnergy: 0.45f);
        var padMat = GsaMeshBuilder.SolidMat(new Color(0.14f, 0.16f, 0.18f), roughness: 0.85f);

        void AddWall(string id, Vector3d centerEnu, Vector3d sizeEnu)
        {
            var gPos = _coordinates.ToGodot(centerEnu);
            var gSize = new Vector3((float)sizeEnu.X, (float)sizeEnu.Z, (float)sizeEnu.Y);
            garageBatch.AddBox(wallMat, gPos, gSize, uvScale: 0.14f);
            _garageObstacles.Add(new ObstacleBoxEnu(id, centerEnu, sizeEnu, true));
            var body = new StaticBody3D { Name = id, Position = gPos, CollisionLayer = 1, CollisionMask = 1 };
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = gSize } });
            visualBase.AddChild(body);
        }

        // Open-front drive-in bay on North side of mid-block street (clear of facade_01..20 & walk_obstacle)
        AddWall("WorkshopWallLeft", new Vector3d(500169.6, 7400034.5, 102.1), new Vector3d(0.8, 8.2, 4.2));
        AddWall("WorkshopWallRight", new Vector3d(500178.4, 7400034.5, 102.1), new Vector3d(0.8, 8.2, 4.2));
        AddWall("WorkshopWallBack", new Vector3d(500174.0, 7400038.6, 102.1), new Vector3d(9.6, 0.8, 4.2));

        // Visual floor pad + roof + glowing safety stripe (no collider inside bay)
        garageBatch.AddBox(padMat, _coordinates.ToGodot(new Vector3d(500174.0, 7400034.5, 100.06)), new Vector3(8.0f, 0.06f, 8.2f));
        garageBatch.AddBox(roofMat, _coordinates.ToGodot(new Vector3d(500174.0, 7400034.5, 104.35)), new Vector3(9.8f, 0.35f, 8.8f));
        garageBatch.AddBox(trimMat, _coordinates.ToGodot(new Vector3d(500174.0, 7400030.1, 104.32)), new Vector3(9.84f, 0.24f, 0.18f));
        garageBatch.BuildSeparatedNodes(visualBase, "GsaWorkshopGarage", GeometryInstance3D.ShadowCastingSetting.Off);

        var garageSign = GsaMeshBuilder.BuildStorefrontSignboard("WorkshopSignboard", new Color(0.14f, 0.78f, 0.48f), "GSA WORKSHOP · PAY & SPRAY", width: 6.2f);
        garageSign.Position = _coordinates.ToGodot(new Vector3d(500174.0, 7400030.05, 103.55));
        visualBase.AddChild(garageSign);

        var ring = GsaMeshBuilder.BuildBeaconRing("Beacon_Workshop", new Color(1.0f, 0.46f, 0.18f), 2.1f, 0.85f, Text("beacon.workshop.label"));
        ring.Position = _coordinates.ToGodot(new Vector3d(GarageTriggerEnu.X, GarageTriggerEnu.Y, GetTerrainElevationEnu(GarageTriggerEnu.X, GarageTriggerEnu.Y)));
        ring.Visible = !_quickParkRun;
        visualBase.AddChild(ring);
        _cityVisuals.SpinningBeacons.Add(ring);
        _staticBeaconLabels.Add((ring, "beacon.workshop.label"));
    }

    private void BuildDispatchPayphonesAndStoryCorona(Node3D visualBase)
    {
        _payphoneNodes.Clear();
        var phoneBatch = new GsaStaticMeshBatcher();
        var poleMat = GsaMeshBuilder.SolidMat(new Color(0.24f, 0.27f, 0.31f));
        var shellMat = GsaMeshBuilder.SolidMat(new Color(0.14f, 0.62f, 0.86f), roughness: 0.42f);
        var screenMat = GsaMeshBuilder.SolidMat(new Color(0.95f, 0.85f, 0.24f), emit: true, emissionEnergy: 1.2f);

        foreach (var (regionId, _, rawPhoneEnu) in DistrictDispatchSpots)
        {
            var z = GetTerrainElevationEnu(rawPhoneEnu.X, rawPhoneEnu.Y);
            var pos = new Vector3d(rawPhoneEnu.X, rawPhoneEnu.Y, z);
            var gPos = _coordinates.ToGodot(pos);
            // Compact fiberglass orelhão / dispatch payphone booth (no collision so camera/player stay free)
            phoneBatch.AddCylinder(poleMat, gPos + new Vector3(0f, 1.05f, 0f), 0.07f, 0.09f, 2.1f, radialSegments: 7);
            phoneBatch.AddBox(shellMat, gPos + new Vector3(0f, 1.78f, 0f), new Vector3(0.64f, 0.68f, 0.52f));
            phoneBatch.AddBox(screenMat, gPos + new Vector3(0f, 1.66f, 0.04f), new Vector3(0.28f, 0.26f, 0.14f));

            var ring = GsaMeshBuilder.BuildBeaconRing($"PayphoneRing_{regionId}", new Color(0.24f, 0.66f, 1.0f), 0.95f, 0.95f, Text("beacon.payphone.label"), labelVisibilityRange: 36f);
            ring.Position = gPos;
            visualBase.AddChild(ring);
            _cityVisuals.SpinningBeacons.Add(ring);
            _staticBeaconLabels.Add((ring, "beacon.payphone.label"));
            _payphoneNodes.Add((regionId, pos, ring));
        }
        phoneBatch.BuildSeparatedNodes(visualBase, "DispatchPayphones", GeometryInstance3D.ShadowCastingSetting.Off);

        _storyCoronaNode = GsaMeshBuilder.BuildBeaconRing("StoryMissionCorona", new Color(0.96f, 0.28f, 0.82f), 1.50f, 1.35f);
        _storyCoronaLabel = new Label3D
        {
            Text = "M",
            Position = new Vector3(0f, 2.15f, 0f),
            FontSize = 46,
            PixelSize = 0.011f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Modulate = new Color(1.0f, 0.52f, 0.92f),
            OutlineModulate = Colors.Black,
            OutlineSize = 5,
            AlphaCut = Label3D.AlphaCutMode.Disabled,
            VisibilityRangeEnd = 65f,
        };
        _storyCoronaNode.AddChild(_storyCoronaLabel);
        visualBase.AddChild(_storyCoronaNode);
        _cityVisuals.SpinningBeacons.Add(_storyCoronaNode);
        _storyCoronaNode.Visible = false;
    }

    private void BuildGpsWaypointArrow(Node3D visualBase)
    {
        var arrow = new Node3D { Name = "GpsWaypointArrow", Visible = false, Scale = Vector3.One * 0.72f };
        _gpsArrowMat = GsaMeshBuilder.SolidStandardMat(new Color(1.0f, 0.78f, 0.16f), emit: true, emissionEnergy: 1.55f);
        _gpsArrowMat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        // Points toward +X locally (yaw = Atan2(dN, dE) rotated onto Godot Y)
        arrow.AddChild(GsaMeshBuilder.Box("Stem", new Vector3(0.75f, 0.12f, 0.16f), new Vector3(-0.18f, 0f, 0f), _gpsArrowMat));
        arrow.AddChild(GsaMeshBuilder.Box("WingL", new Vector3(0.55f, 0.12f, 0.14f), new Vector3(0.24f, 0f, -0.18f), _gpsArrowMat, rotDeg: new Vector3(0f, -38f, 0f)));
        arrow.AddChild(GsaMeshBuilder.Box("WingR", new Vector3(0.55f, 0.12f, 0.14f), new Vector3(0.24f, 0f, 0.18f), _gpsArrowMat, rotDeg: new Vector3(0f, 38f, 0f)));
        foreach (var child in arrow.GetChildren())
            if (child is MeshInstance3D mi) mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        visualBase.AddChild(arrow);
        _gpsArrowNode = arrow;
    }

    private void BuildTireSkidMarksOverlay(Node3D visualBase)
    {
        var quad = new QuadMesh
        {
            Size = new Vector2(0.24f, 0.85f),
            Material = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.05f, 0.05f, 0.06f, 0.62f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
        };
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = quad,
            InstanceCount = MaxSkidMarks,
            VisibleInstanceCount = 0,
        };
        _skidMarksMesh = new MultiMeshInstance3D
        {
            Name = "TireSkidMarks",
            Multimesh = mm,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityRangeEnd = 110f,
        };
        visualBase.AddChild(_skidMarksMesh);
    }

    private void StampTireSkidMarks(Vector3d carPosEnu, double headingRadians)
    {
        if (_skidMarksMesh is null)
            return;
        if (_hasLastSkidStamp && TrainingContractSession.HorizontalDistance(_lastSkidStampEnu, carPosEnu) < 0.75)
            return;

        _lastSkidStampEnu = carPosEnu;
        _hasLastSkidStamp = true;
        var mm = _skidMarksMesh.Multimesh;
        var back = new Vector3d(-Math.Cos(headingRadians), -Math.Sin(headingRadians), 0.0);
        var right = new Vector3d(Math.Sin(headingRadians), -Math.Cos(headingRadians), 0.0);
        var flatYaw = Basis.FromEuler(new Vector3(-MathF.PI * 0.5f, (float)(headingRadians - Math.PI / 2.0), 0f));

        foreach (var side in new[] { -0.78, 0.78 })
        {
            var stampEnu = carPosEnu + back * 1.15 + right * side;
            var gPos = _coordinates.ToGodot(new Vector3d(stampEnu.X, stampEnu.Y, carPosEnu.Z + 0.032));
            mm.SetInstanceTransform(_skidWriteIndex, new Transform3D(flatYaw, gPos));
            _skidWriteIndex = (_skidWriteIndex + 1) % MaxSkidMarks;
            if (_skidVisibleCount < MaxSkidMarks)
                _skidVisibleCount++;
        }
        mm.VisibleInstanceCount = _skidVisibleCount;
    }

    private static bool IsNearWorkshopGarage(Vector3d pos) =>
        TrainingContractSession.HorizontalDistance(pos, GarageTriggerEnu) <= 4.0;

    private CampaignMissionFullSpec? GetNextDispatchMission()
    {
        foreach (var spec in FullCampaignCatalog.AllMissions)
        {
            if (CityMissionCompleted(spec.MissionId) || !SupportsCityStory(spec))
                continue;
            if (_isSandbox || spec.Preconditions.All(CityMissionCompleted))
                return spec;
        }
        return null;
    }

    private Vector3d GetCoronaPositionForMission(CampaignMissionFullSpec spec)
    {
        var region = spec.RegionIds.Count > 0 ? spec.RegionIds[0] : "old_center";
        foreach (var spot in DistrictDispatchSpots)
        {
            if (spot.RegionId == region)
                return new Vector3d(spot.CoronaEnu.X, spot.CoronaEnu.Y, GetTerrainElevationEnu(spot.CoronaEnu.X, spot.CoronaEnu.Y));
        }
        return new Vector3d(500236.5, 7399999.5, 100.0);
    }

    private void StepCityStreetInteractions(
        double dt,
        Vector3d pos,
        double heading,
        double speed,
        LocomotionMode mode,
        LocomotionInput input)
    {
        _garageCooldown = Math.Max(0.0, _garageCooldown - dt);
        _payphoneRingCooldown = Math.Max(0.0, _payphoneRingCooldown - dt);

        if (_carSim is not null && mode >= LocomotionMode.Car && Math.Abs(speed) > 3.5 &&
            (_handbrakeActive || (input.Brake > 0.5 && Math.Abs(speed) > 5.5)))
        {
            StampTireSkidMarks(pos, heading);
        }

        // Story Mission Corona visibility & label update
        _activeDispatchMission = _activeContract is null && !_quickParkRun && !_citySaveSmoke ? GetNextDispatchMission() : null;
        if (_storyCoronaNode is not null && _storyCoronaLabel is not null)
        {
            if (_activeDispatchMission is { } nextSpec)
            {
                _activeStoryCoronaEnu = GetCoronaPositionForMission(nextSpec);
                _storyCoronaNode.Position = _coordinates.ToGodot(_activeStoryCoronaEnu);
                _storyCoronaNode.Visible = true;
                var nextTxt = $"[E] {nextSpec.MissionId} · {Text(nextSpec.TitleKey)}";
                if (_storyCoronaLabel.Text != nextTxt)
                    _storyCoronaLabel.Text = nextTxt;
            }
            else if (_storyCoronaNode.Visible)
            {
                _storyCoronaNode.Visible = false;
            }
        }

        // Payphone coronas visible & ringing when player has no active contract
        var showPhones = _activeContract is null && !_quickParkRun && !_citySaveSmoke;
        var nearAnyPhone = false;
        foreach (var (_, phonePos, ring) in _payphoneNodes)
        {
            if (ring.Visible != showPhones)
                ring.Visible = showPhones;
            if (showPhones && TrainingContractSession.HorizontalDistance(pos, phonePos) <= 20.0)
                nearAnyPhone = true;
        }
        if (nearAnyPhone && _payphoneRingCooldown <= 0.0)
        {
            _payphoneRingCooldown = 5.0;
            _audio.PlayPayphoneRing();
        }

        // Update 3D overhead GPS chevron arrow
        if (_gpsArrowNode is not null && _gpsArrowMat is not null)
        {
            if (!_gpsArrowEnabled || _quickParkRun || _citySaveSmoke || _chaseCamera.Mode == GsaCameraMode.SurveyView)
            {
                _gpsArrowNode.Visible = false;
            }
            else if (TryResolveGpsWaypoint(pos, out var wpEnu, out var wpColor))
            {
                var dE = wpEnu.X - pos.X;
                var dN = wpEnu.Y - pos.Y;
                var distSq = dE * dE + dN * dN;
                if (distSq < 7.5 * 7.5)
                {
                    _gpsArrowNode.Visible = false;
                }
                else
                {
                    _gpsArrowNode.Visible = true;
                    var bob = (mode >= LocomotionMode.Car ? 3.25 : 2.95) + 0.12 * Math.Sin(_tick * 0.08);
                    _gpsArrowNode.Position = _coordinates.ToGodot(pos + Vector3d.UnitZ * bob);
                    _gpsArrowNode.Rotation = new Vector3(0f, (float)Math.Atan2(dN, dE), 0f);
                    if (!_gpsArrowMat.AlbedoColor.IsEqualApprox(wpColor))
                    {
                        _gpsArrowMat.AlbedoColor = wpColor;
                        _gpsArrowMat.Emission = wpColor;
                    }
                }
            }
            else
            {
                _gpsArrowNode.Visible = false;
            }
        }
    }

    private bool TryResolveGpsWaypoint(Vector3d playerPos, out Vector3d targetEnu, out Color color)
    {
        if (_activeContract is { } active)
        {
            var hasPending = _fieldSamples.Count > 0 || _pendingLidarReturns > 0 || _pendingBoundaryShots.Count > 0;
            var hasProtected = _protectedCityPhotos.Count > 0 || _protectedLidarReturns > 0 || _protectedBoundaryShots.Count > 0;

            if (IsFinalPavementSurvey(active))
            {
                if (_pendingLidarReturns > 0 && FinalCityPavementSurvey.UploadAt(playerPos) is null)
                {
                    targetEnu = FinalCityPavementSurvey.Sectors
                        .Where(s => !_finaleUploadedSectors.Contains(s.Manifest.SectorId))
                        .OrderBy(s => TrainingContractSession.HorizontalDistance(playerPos, s.Upload))
                        .Select(s => s.Upload)
                        .FirstOrDefault(_manifestObjectsById["unload_point"].PositionEnu);
                    color = new Color(0.98f, 0.76f, 0.16f);
                    return true;
                }
                var pendingSector = FinalCityPavementSurvey.Sectors
                    .Where(s => !_finaleUploadedSectors.Contains(s.Manifest.SectorId))
                    .OrderBy(s => TrainingContractSession.HorizontalDistance(playerPos, s.Start))
                    .FirstOrDefault();
                if (pendingSector is not null)
                {
                    targetEnu = pendingSector.Start;
                    color = new Color(1.0f, 0.62f, 0.16f);
                    return true;
                }
            }
            else if (ActiveSingleCorridorSector(active) is { } singleSector)
            {
                if (_pendingLidarReturns > 0)
                {
                    targetEnu = singleSector.Upload;
                    color = new Color(0.98f, 0.76f, 0.16f);
                    return true;
                }
                if (!_finaleUploadedSectors.Contains(singleSector.Manifest.SectorId))
                {
                    targetEnu = singleSector.Start;
                    color = new Color(1.0f, 0.62f, 0.16f);
                    return true;
                }
                if (hasProtected && _manifestObjectsById.TryGetValue("office_entry", out var procOffice))
                {
                    targetEnu = procOffice.PositionEnu;
                    color = new Color(0.18f, 0.90f, 0.96f);
                    return true;
                }
            }
            else if (IsWorksSurvey(active))
            {
                targetEnu = hasPending
                    ? PortWorksSurvey.UploadPads.OrderBy(p => TrainingContractSession.HorizontalDistance(playerPos, p)).First()
                    : PortWorksSurvey.Sides[0].Start;
                color = hasPending ? new Color(0.98f, 0.76f, 0.16f) : new Color(1.0f, 0.62f, 0.16f);
                return true;
            }
            else if (IsBoundarySurvey(active))
            {
                targetEnu = hasPending
                    ? SerraBoundarySurvey.UploadPads.OrderBy(p => TrainingContractSession.HorizontalDistance(playerPos, p)).First()
                    : SerraBoundarySurvey.Sites[0].PublishedPositionEnu;
                color = hasPending ? new Color(0.98f, 0.76f, 0.16f) : new Color(1.0f, 0.62f, 0.16f);
                return true;
            }
            else if (IsRainCollection(active))
            {
                targetEnu = hasPending
                    ? WaterfrontRainSurvey.UploadPads.OrderBy(p => TrainingContractSession.HorizontalDistance(playerPos, p)).First()
                    : WaterfrontRainSurvey.UploadPads[0];
                color = hasPending ? new Color(0.98f, 0.76f, 0.16f) : new Color(1.0f, 0.62f, 0.16f);
                return true;
            }
            else if (IsViaductCollection(active))
            {
                targetEnu = PortViaductSurvey.WestPad;
                color = new Color(1.0f, 0.62f, 0.16f);
                return true;
            }
            else if (IsTunnelCollection(active))
            {
                targetEnu = PortTunnelSurvey.MiddleUpload;
                color = new Color(1.0f, 0.62f, 0.16f);
                return true;
            }

            Vector3d? nearestIncomplete = null;
            var bestDist = double.MaxValue;
            foreach (var fid in active.RequiredFeatureIds)
            {
                var coveredCells = _coveredCellsByTarget.TryGetValue(fid, out var cset) ? cset.Count : 0;
                var needCells = _cityPhotoCoverage is not null ? _cityPhotoCoverage.GetTargetCellCount(fid) : 0;
                var done = needCells > 0 ? coveredCells >= needCells : _observedTargetIds.Contains(fid);
                if (done) continue;
                Vector3d? tPos = _manifestObjectsById.TryGetValue(fid, out var fo) ? fo.PositionEnu
                    : (_geodeticNetwork.Benchmarks.TryGetValue(fid, out var bm) ? bm.PhysicalPositionEnu : null);
                if (tPos is null) continue;
                var d = TrainingContractSession.HorizontalDistance(playerPos, tPos.Value);
                if (d < bestDist)
                {
                    bestDist = d;
                    nearestIncomplete = tPos;
                }
            }

            var cap = Math.Max(1, _locomotion.CurrentProfile.VirtualStorageCapacityUnits);
            var nearFull = (_fieldSamples.Count + _locomotion.UsedVirtualStorageUnits) >= (int)(cap * 0.9);
            if (nearestIncomplete is { } tgt && !nearFull)
            {
                targetEnu = tgt;
                color = new Color(1.0f, 0.78f, 0.18f);
                return true;
            }
            if (hasPending)
            {
                targetEnu = active.RegionId == "serra"
                    ? SerraBoundarySurvey.UploadPads.OrderBy(p => TrainingContractSession.HorizontalDistance(playerPos, p)).First()
                    : _manifestObjectsById["unload_point"].PositionEnu;
                color = new Color(0.98f, 0.76f, 0.16f);
                return true;
            }
            if (hasProtected && _manifestObjectsById.TryGetValue("office_entry", out var office))
            {
                targetEnu = office.PositionEnu;
                color = new Color(0.18f, 0.90f, 0.96f);
                return true;
            }
        }

        if (_rackMisaligned || _cityHazards.TirePunctured || (_carSim?.ConditionDamage ?? 0.0) >= 45.0)
        {
            targetEnu = GarageTriggerEnu;
            color = new Color(1.0f, 0.46f, 0.18f);
            return true;
        }

        if (_activeDispatchMission is not null)
        {
            targetEnu = _activeStoryCoronaEnu;
            color = new Color(0.96f, 0.28f, 0.82f);
            return true;
        }

        targetEnu = Vector3d.Zero;
        color = Colors.White;
        return false;
    }

    private bool NearBasicUnloadPad(Vector3d pos)
    {
        foreach (var id in new[] { "unload_point", "unload_point_north" })
        {
            if (_manifestObjectsById.TryGetValue(id, out var obj) &&
                TrainingContractSession.HorizontalDistance(pos, obj.PositionEnu) <= 4.5)
                return true;
        }
        return false;
    }

    private string? FindNearbyBenchmarkId(Vector3d pos)
    {
        foreach (var bm in _geodeticNetwork.Benchmarks.Values)
        {
            if (TrainingContractSession.HorizontalDistance(pos, bm.PhysicalPositionEnu) <= 4.0)
                return bm.BenchmarkId;
        }
        return null;
    }

    private string GetLockedVehicleRequirementLabel(LocomotionMode mode)
    {
        var entry = FleetShopCatalog.GetPlatformEntry(mode);
        return entry.RequiredStoryMissionId ?? mode switch
        {
            LocomotionMode.Car => "HIST-04",
            LocomotionMode.Kombi => "HIST-05",
            LocomotionMode.Pickup => "HIST-07",
            LocomotionMode.Van => "HIST-10",
            _ => "HIST-01",
        };
    }

    private string? GetStreetContextPrompt(Vector3d pos, double speed, LocomotionMode mode)
    {
        if (IsNearWorkshopGarage(pos))
        {
            if (Math.Abs(speed) >= 2.0)
                return Text("city.garage.stop_hint");
            if (_cityHazards.DisabledVehicle is { } disabled)
            {
                var p = PlatformProfile.ForMode(disabled);
                var q = _cityHazards.Services.QuoteService(ServiceKind.InsuranceClaim, p);
                return string.Format(Text("city.garage.recover_prompt"), Text("platform." + p.Id + ".name"), q.Eligible ? q.FinalFeeCoords : p.PriceCoords);
            }
            var fee = GetWorkshopFeeCoords();
            return string.Format(Text("city.garage.prompt"), fee);
        }

        var hasPending = _fieldSamples.Count > 0 || _pendingLidarReturns > 0 || _pendingBoundaryShots.Count > 0;
        var hasProtected = _protectedCityPhotos.Count > 0 || _protectedLidarReturns > 0 || _protectedBoundaryShots.Count > 0;
        if (_activeContract is not null)
        {
            if (hasPending && NearCityUnload(pos))
                return Text("city.street.offload_prompt");
            if (hasProtected && IsNearOfficeDoor(pos))
                return Text("city.street.office_prompt");
        }

        if (IsNearOfficeDoor(pos))
        {
            if (_gimbalina.HasFledToSafety)
                return Text("city.street.office_recover_gimbalina");
            if (_activeContract is not null && hasPending)
                return Text("city.street.office_upload_first_prompt");
            return Text("city.street.office_open_prompt");
        }

        if (NearBasicUnloadPad(pos))
        {
            if (_gimbalina.HasFledToSafety)
                return Text("city.street.unload_recover_gimbalina");
            return Text(_activeContract is not null ? "city.street.unload_collect_first_prompt" : "city.street.unload_idle_prompt");
        }

        if (_activeContract is null)
        {
            if (_activeDispatchMission is { } spec && TrainingContractSession.HorizontalDistance(pos, _activeStoryCoronaEnu) <= 3.2)
            {
                return CityHasSurveyPlatform(spec.MinimumPlatformLevel)
                    ? string.Format(Text("city.street.mission_prompt"), spec.MissionId, Text(spec.TitleKey))
                    : string.Format(Text("city.street.mission_need_vehicle"), spec.MissionId, spec.MinimumPlatformLevel);
            }
            foreach (var (_, phonePos, ring) in _payphoneNodes)
            {
                if (ring.Visible && TrainingContractSession.HorizontalDistance(pos, phonePos) <= 3.2)
                    return Text("city.payphone.prompt");
            }
        }

        if (FindNearbyBenchmarkId(pos) is { } nearBm)
        {
            if (_collectibles.OccupiedLandmarkIds.Contains(nearBm))
                return Text("city.street.benchmark_occupied_prompt");
            return mode == LocomotionMode.OnFoot
                ? string.Format(Text("city.street.benchmark_holding_prompt"), _benchmarkHoldSeconds)
                : Text("city.street.benchmark_dismount_prompt");
        }

        foreach (var v in _worldParkedVehicles)
        {
            if (!v.Root.Visible || TrainingContractSession.HorizontalDistance(pos, v.PosEnu) >= 4.2) continue;
            var vehName = Text($"platform.{v.Mode.ToString().ToLowerInvariant()}.name");
            var owned = _isSandbox || _fleet.OwnedPlatforms.Contains(v.Mode);
            if (owned)
            {
                return mode == LocomotionMode.OnFoot
                    ? Text("city.board_vehicle")
                    : string.Format(Text("city.street.switch_vehicle_prompt"), vehName);
            }
            var completed = _campaignState.ObjectiveStates.Keys.Where(CityMissionCompleted).ToHashSet(StringComparer.Ordinal);
            var quote = _fleet.QuotePlatformPurchase(v.Mode, _progression, completed);
            var unlocked = quote.Unlocked && CityPlatformStoryUnlocked(v.Mode);
            return unlocked
                ? string.Format(Text("city.street.buy_vehicle"), vehName, quote.FinalPriceCoords)
                : string.Format(Text("city.street.vehicle_locked_detail"), vehName, GetLockedVehicleRequirementLabel(v.Mode));
        }

        return null;
    }

    private static bool IsNearOfficeDoor(Vector3d pos) =>
        TrainingContractSession.HorizontalDistance(pos, new Vector3d(500232.0, 7400003.5, 100.0)) <= 5.5;

    private int GetWorkshopFeeCoords()
    {
        if (_isSandbox) return 0;
        var needsWork = _rackMisaligned || _cityHazards.TirePunctured ||
                        _cityHazards.Lens.ObstructionFraction > 0.01 ||
                        (_carSim?.ConditionDamage ?? 0.0) > 1.0 ||
                        _everyday.CosmeticDamageFor(_locomotion.MountedVehicleMode) > 0 ||
                        _cityHazards.PursuitRemaining > 0;
        return needsWork ? 50 : 25;
    }

    private bool TryHandleStreetInteraction(Vector3d pos, double speed, LocomotionMode mode)
    {
        if (IsNearWorkshopGarage(pos))
        {
            if (_garageCooldown > 0.0) return true;
            if (Math.Abs(speed) >= 2.0)
            {
                _feedbackText = Text("city.garage.stop_hint");
                return true;
            }
            if (_cityHazards.DisabledVehicle is not null)
            {
                RepairCityVehicle();
                ApplyEquippedCosmeticsToRig();
                _garageCooldown = 2.0;
                return true;
            }
            var fee = GetWorkshopFeeCoords();
            if (fee > 0)
            {
                var debit = _ledger.TryDebitPurchaseOrService(
                    $"workshop_{_tick}", "workshop", _calendar.CalendarTimeSeconds, fee, "workshop", "city.garage.resprayed_title");
                if (!debit.Succeeded)
                {
                    _feedbackText = Text("city.hazards.insufficient");
                    _audio.PlayAlert();
                    return true;
                }
            }
            var vehicleMode = _locomotion.MountedVehicleMode;
            _cityHazards.TirePunctured = false;
            _everyday.ClearCosmeticDamage(vehicleMode);
            _rackMisaligned = false;
            _cityHazards.RackYawErrorRadians = 0;
            _cityHazards.Lens.StepStoppedCleaning(12.0, 0.0);
            _cityHazards.SetVehicleDamage(vehicleMode, 0);
            _locomotion.RefillHandcartResources(refillAutonomy: true, clearStorage: false);
            if (_carSim is not null)
                _carSim = new CarSuspensionSimulator(_carSim.PositionEnu, _carSim.HeadingRadians, _carSim.Profile.Mode, _carSim.UsedStorageUnits, conditionDamage: 0);
            if (!IsBoundarySurvey(_activeContract) && !IsWorksSurvey(_activeContract) && !IsFinalPavementSurvey(_activeContract))
            {
                _cityHazards.CancelActiveThreats();
                _attention.CancelPendingThreats();
                ClearCityThreatVisuals();
            }
            _garagePaintOverride = GarageResprayPalette[_garagePaintIndex++ % GarageResprayPalette.Length];
            ApplyEquippedCosmeticsToRig();
            _audio.PlayGarageRespray();
            _garageCooldown = 2.5;
            _feedbackText = Text("city.garage.resprayed_sub");
            _gtaHud.TriggerCelebrationBanner(Text("city.garage.resprayed_title"), Text("city.garage.resprayed_sub"), 3.2);
            SaveHazardChange();
            return true;
        }

        var hasPending = _fieldSamples.Count > 0 || _pendingLidarReturns > 0 || _pendingBoundaryShots.Count > 0;
        var hasProtected = _protectedCityPhotos.Count > 0 || _protectedLidarReturns > 0 || _protectedBoundaryShots.Count > 0;
        if (_activeContract is not null)
        {
            if (hasPending && NearCityUnload(pos))
            {
                OffloadCityData();
                return true;
            }
            if (hasProtected && IsNearOfficeDoor(pos))
            {
                if (IsViaductCollection(_activeContract) || (_officeMode == ProcessingMode.Technical && !_privacyMasksApplied))
                {
                    _tabletPanel.Show();
                    _tabletTabs.CurrentTab = 1;
                    RefreshTabletContents();
                }
                else
                {
                    ProcessAndDeliverCityContract();
                }
                return true;
            }
        }

        if (IsNearOfficeDoor(pos))
        {
            if (_gimbalina.HasFledToSafety)
                RecoverCityGimbalina();
            else if (_activeContract is not null && hasPending)
                _feedbackText = Text("city.office.upload_first");
            _tabletPanel.Show();
            _tabletTabs.CurrentTab = _activeContract is not null ? 1 : 0;
            RefreshTabletContents();
            return true;
        }

        if (NearBasicUnloadPad(pos))
        {
            if (_gimbalina.HasFledToSafety)
                RecoverCityGimbalina();
            else
            {
                _feedbackText = Text(_activeContract is not null ? "city.street.unload_collect_first_prompt" : "city.street.unload_idle_prompt");
                _audio.PlayAlert();
            }
            return true;
        }

        if (_activeContract is null)
        {
            if (_activeDispatchMission is { } spec && TrainingContractSession.HorizontalDistance(pos, _activeStoryCoronaEnu) <= 3.2)
            {
                StartStoryMission(spec);
                return true;
            }
            foreach (var (regionId, phonePos, ring) in _payphoneNodes)
            {
                if (!ring.Visible || TrainingContractSession.HorizontalDistance(pos, phonePos) > 3.2)
                    continue;
                if (_offeredContracts.Count == 0)
                    GenerateContractOffers();
                var match = _offeredContracts
                    .Where(o => o.Contract.RegionId == regionId &&
                                CityHasSurveyPlatform(o.WitnessCertificate.RequiredPlatformLevel) &&
                                SupportsCityContract(o.Contract))
                    .OrderByDescending(o => o.WitnessCertificate.RequiredPlatformLevel)
                    .FirstOrDefault()
                    ?? _offeredContracts
                        .Where(o => CityHasSurveyPlatform(o.WitnessCertificate.RequiredPlatformLevel) &&
                                    SupportsCityContract(o.Contract))
                        .OrderByDescending(o => o.WitnessCertificate.RequiredPlatformLevel)
                        .FirstOrDefault();
                if (match is not null && TryAcceptCityProceduralOffer(match))
                {
                    _gtaHud.ShowCutsceneLetterbox(
                        Text("city.payphone.speaker"),
                        $"{Text(match.Contract.TitleKey)} · {match.Contract.Reward.BaseCoords} C$",
                        () => _gtaHud.HideCutsceneLetterbox());
                    return true;
                }
                _feedbackText = Text("city.payphone.no_jobs");
                return true;
            }
        }

        if (FindNearbyBenchmarkId(pos) is { } nearBm)
        {
            if (_collectibles.OccupiedLandmarkIds.Contains(nearBm))
            {
                _feedbackText = Text("city.street.benchmark_occupied_prompt");
                return true;
            }
            if (mode != LocomotionMode.OnFoot && Math.Abs(speed) < LocomotionSimulator.MaxSpeedToToggleMount)
            {
                _feedbackText = Text("city.street.benchmark_dismount_prompt");
                return true;
            }
        }

        foreach (var v in _worldParkedVehicles)
        {
            if (!v.Root.Visible || TrainingContractSession.HorizontalDistance(pos, v.PosEnu) >= 4.2)
                continue;
            var owned = _isSandbox || _fleet.OwnedPlatforms.Contains(v.Mode);
            if (owned)
            {
                if (_cityHazards.IsVehicleDisabled(v.Mode) || Math.Abs(speed) >= 1.5)
                    continue;
                if (_carSim is not null) _cityHazards.SetVehicleDamage(_carSim.Profile.Mode, _carSim.ConditionDamage);
                _carSim = null;
                _locomotion.RepositionSafeAfterRecovery(v.PosEnu, v.Mode, v.Heading, refillAutonomy: true, clearStorage: false);
                SwitchPlatform(v.Mode);
                SnapCamera();
                return true;
            }

            var completed = _campaignState.ObjectiveStates.Keys.Where(CityMissionCompleted).ToHashSet(StringComparer.Ordinal);
            var quote = _fleet.QuotePlatformPurchase(v.Mode, _progression, completed);
            var unlocked = quote.Unlocked && CityPlatformStoryUnlocked(v.Mode);
            if (!unlocked)
            {
                var vehName = Text($"platform.{v.Mode.ToString().ToLowerInvariant()}.name");
                _feedbackText = string.Format(Text("city.street.vehicle_locked_detail"), vehName, GetLockedVehicleRequirementLabel(v.Mode));
                _audio.PlayAlert();
                return true;
            }
            if (!_cityHazards.IsVehicleDisabled(v.Mode) && Math.Abs(speed) < 1.5 && TryPurchaseCityPlatform(v.Mode))
            {
                if (_carSim is not null) _cityHazards.SetVehicleDamage(_carSim.Profile.Mode, _carSim.ConditionDamage);
                _carSim = null;
                _locomotion.RepositionSafeAfterRecovery(v.PosEnu, v.Mode, v.Heading, refillAutonomy: true, clearStorage: false);
                SwitchPlatform(v.Mode);
                SnapCamera();
                return true;
            }
        }

        return false;
    }

    private Color? ResolveEffectiveVehiclePaint()
    {
        if (_garagePaintOverride is { } respray)
            return respray;
        return _cosmetics.EquippedPaintId switch
        {
            "paint_cobalt_fleet" => new Color(0.16f, 0.44f, 0.78f),
            "paint_terracotta_stripe" => new Color(0.84f, 0.36f, 0.18f),
            "paint_geodetic_yellow" when _cosmetics.OwnedCosmeticIds.Count > 4 ||
                _cosmetics.EquippedHornId != BlooperAndCosmeticCatalog.DefaultHornId ||
                _cosmetics.EquippedGimbalinaCostume != GimbalinaCostumeId.SurveyorVest
                => new Color(0.92f, 0.74f, 0.16f),
            _ => null,
        };
    }

    private void ApplyEquippedCosmeticsToRig()
    {
        var paint = ResolveEffectiveVehiclePaint();
        var costume = (int)_cosmetics.EquippedGimbalinaCostume;
        _mobility.SetVisualCustomization(paint, costume);
    }

    private void SyncWorldParkedVehicleVisibility()
    {
        var activeVehicle = _locomotion?.MountedVehicleMode ?? LocomotionMode.Handcart;
        foreach (var v in _worldParkedVehicles)
            v.Root.Visible = !_quickParkRun && v.Mode != activeVehicle;
    }

    private void UpdateBenchmarkBeaconVisual(string benchmarkId, bool occupied)
    {
        if (!_benchmarkBeaconNodes.TryGetValue(benchmarkId, out var ring))
            return;
        var col = occupied ? new Color(0.20f, 0.78f, 0.68f) : new Color(0.24f, 0.92f, 0.46f);
        GsaMeshBuilder.SetBeaconColor(ring, col, occupied ? 0.07f : 0.14f);
        if (ring.GetNodeOrNull<Label3D>("BeaconLabel") is { } lbl)
            lbl.Text = Text(occupied ? "beacon.benchmark.done" : "beacon.benchmark.label");
    }

    private void RefreshBenchmarkBeaconVisuals()
    {
        foreach (var id in _benchmarkBeaconNodes.Keys)
            UpdateBenchmarkBeaconVisual(id, _collectibles.OccupiedLandmarkIds.Contains(id));
    }

    private void RefreshBeaconLabels()
    {
        foreach (var (node, key) in _staticBeaconLabels)
        {
            if (node.GetNodeOrNull<Label3D>("BeaconLabel") is { } lbl)
                lbl.Text = Text(key);
        }
        RefreshBenchmarkBeaconVisuals();
    }

    private void RefreshParkedVehicleLabels()
    {
        if (_parkedVehicleLabels.Count != _worldParkedVehicles.Count)
            return;
        var completed = _campaignState.ObjectiveStates.Keys.Where(CityMissionCompleted).ToHashSet(StringComparer.Ordinal);
        var onFoot = _locomotion?.Mode == LocomotionMode.OnFoot;
        for (var i = 0; i < _worldParkedVehicles.Count; i++)
        {
            var v = _worldParkedVehicles[i];
            var lbl = _parkedVehicleLabels[i];
            var vehName = Text($"platform.{v.Mode.ToString().ToLowerInvariant()}.name");
            var owned = _isSandbox || _fleet.OwnedPlatforms.Contains(v.Mode);
            string next;
            Color col;
            if (owned)
            {
                next = (onFoot ? "[F] " : "[E] ") + vehName;
                col = new Color(0.32f, 0.92f, 0.96f);
            }
            else
            {
                var q = _fleet.QuotePlatformPurchase(v.Mode, _progression, completed);
                var unlocked = q.Unlocked && CityPlatformStoryUnlocked(v.Mode);
                if (unlocked)
                {
                    next = $"[E] {vehName} · {q.FinalPriceCoords} C$";
                    col = new Color(0.98f, 0.82f, 0.24f);
                }
                else
                {
                    next = string.Format(Text("city.street.vehicle_locked_detail"), vehName, GetLockedVehicleRequirementLabel(v.Mode));
                    col = new Color(0.75f, 0.78f, 0.82f);
                }
            }
            if (lbl.Text != next) lbl.Text = next;
            if (!lbl.Modulate.IsEqualApprox(col)) lbl.Modulate = col;
        }
    }
}

