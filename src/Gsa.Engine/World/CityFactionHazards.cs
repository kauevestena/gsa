using System.Text.Json;
using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Camera;
using Gsa.Core.Traffic;
using Gsa.Core.World;
using Gsa.Engine.Visuals;
using Gsa.Missions.Contracts;
using Gsa.Missions.Economy;
using Gsa.Missions.Gameplay;
using Gsa.Missions.Persistence;
using Godot;

namespace Gsa.Engine.World;

public partial class SaoDatumCityScene
{
    private CityHazardState _cityHazards = new();
    private BatchCustodyStore _cityCustody = new();
    private readonly List<CameraObservationSample> _protectedCityPhotos = [];
    private double _recentPhotoSeconds;
    private bool _cleanLensRequested, _disarmRequested, _citySafeZone;
    private string? _lastHazardWarning;
    private string _hazardNotice = "";
    private Label _hazardStatus = null!;
    private OptionButton _protectionZone = null!;
    private Node3D? _roadblockVisual, _pursuerVisual;
    private ObstacleBoxEnu? _roadblockObstacle;
    private double _pursuitCollisionCooldown;
    private Vector3d[]? _cityUnloadPositions;

    private bool NearCityUnload() => NearCityUnload(CurrentPositionEnu());
    private bool NearCityUnload(Vector3d p)
    {
        _cityUnloadPositions ??= _fullManifest.Objects.Where(o =>
            o.ObjectClass is "office_entry" or "unload_point" or "wifi_hotspot" ||
            o.Id is "office_entry" or "unload_point" or "unload_point_north").Select(o => o.PositionEnu).ToArray();
        foreach (var unload in _cityUnloadPositions)
            if (TrainingContractSession.HorizontalDistance(p, unload) <= 10) return true;
        if (IsTunnelCollection(_activeContract))
            return (p - PortTunnelSurvey.WestStop).Length <= 8 || (p - PortTunnelSurvey.MiddleUpload).Length <= 8 ||
                (p - PortTunnelSurvey.EastStop).Length <= 8;
        return false;
    }

    private void SyncCityHazardProtection()
    {
        _calmPolicy.SetActiveContractCalm(_activeContract?.Calm == true, _activeContract?.AreaId);
        _citySafeZone = NearCityUnload() || FullCityFactionPressureCoordinator.IsInsideProtectedSafeZone(
            GetRegionIdForEnu(CurrentPositionEnu()), CurrentPositionEnu(), out _);
        if (_calmPolicy.IsProtectionActive || _citySafeZone)
        {
            _cityHazards.CancelActiveThreats(); _attention.CancelPendingThreats(); _disarmRequested = false;
            if (_lastHazardWarning is not null) _hazardNotice = Text("hud.calm_protection.active");
            _lastHazardWarning = null; ClearCityThreatVisuals(); CancelCityEverydayThreats();
        }
    }

    private TerritoryEscalationOutcome StepCityFactionHazards(double dt, Vector3d pos, double speed, LocomotionMode mode)
    {
        if (_carSim is not null) _cityHazards.SetVehicleDamage(_carSim.Profile.Mode, _carSim.ConditionDamage);
        SyncCityHazardProtection();
        var zone = FactionCatalog.FindZonesContaining(GetRegionIdForEnu(pos), pos.X, pos.Y).FirstOrDefault();
        var factionsAvailable = _geopedia.IsUnlocked("geopedia_territory_attention") &&
            (mode >= LocomotionMode.Car || (mode == LocomotionMode.OnFoot && _locomotion.MountedVehicleMode >= LocomotionMode.Car));
        var multiplier = zone is null ? 1 : _cityHazards.Sabotage.ComputeEffectiveAttentionMultiplier(zone.ZoneId, _calmPolicy);
        if (_cityHazards.LegacyDisguise && multiplier > 0) multiplier = Math.Max(.25, multiplier * .5);
        var protection = _calmPolicy.IsProtectionActive || _citySafeZone || !factionsAvailable || multiplier <= 0;
        _cityHazards.Step(dt, zone?.ZoneId, protection);
        if (_rackMisaligned && _cityHazards.RackYawErrorRadians == 0) _cityHazards.RackYawErrorRadians = .01;
        _recentPhotoSeconds = Math.Max(0, _recentPhotoSeconds - dt);
        var capturing = (_lidarEnabled && mode >= LocomotionMode.Kombi) || _recentPhotoSeconds > 0;
        var outcome = _attention.Step(zone?.ZoneId, capturing && factionsAvailable, Math.Abs(speed),
            _disarmRequested || Input.IsPhysicalKeyPressed(Key.E), dt, _calmPolicy,
            attentionGainMultiplier: multiplier, suppressThreats: protection);
        if (outcome.WarningIssued)
        {
            _hazardNotice = outcome.BombWarningActive
                ? string.Format(Text("city.hazards.bomb_warning"), Math.Ceiling(outcome.BombRemainingCountdownSeconds))
                : Text("city.hazards.faction_warning");
            if (_lastHazardWarning != outcome.ReasonKey) _audio.PlayAlert(critical: outcome.BombWarningActive);
        }
        _lastHazardWarning = outcome.WarningIssued ? outcome.ReasonKey : null;
        if (outcome.BombDisarmed) { _disarmRequested = false; _hazardNotice = Text("city.hazards.disarmed"); SaveHazardChange(); }
        if (outcome.BombDetonated && zone is not null)
        {
            var result = _cityHazards.Sabotage.TriggerIdempotentSabotageAttack(_cityHazards.NextEventId(), zone.ZoneId,
                SabotageAttackKind.ParkedVehicleBomb, _cityCustody, _calendar.CalendarTimeSeconds, _calmPolicy);
            if (result.Executed) HandleCityVehicleLoss();
        }
        else if (outcome.ActionTriggered && outcome.TriggeredActionLevel is { } level && zone is not null)
            ApplyCityFactionAction(level, zone, pos);
        if (!outcome.BombWarningActive && _cityHazards.Lens.ObstructionFraction > 0 &&
            (_cleanLensRequested || Input.IsPhysicalKeyPressed(Key.E)))
        {
            if (_cityHazards.Lens.StepStoppedCleaning(dt, speed))
            { _cleanLensRequested = false; _hazardNotice = Text("city.hazards.cleaned"); SaveHazardChange(); }
        }
        else _cityHazards.Lens.StepStoppedCleaning(0, 1);
        StepCityThreatVisuals(dt, pos, speed);
        if (_carSim is not null)
        {
            _cityHazards.SetVehicleDamage(_carSim.Profile.Mode, _carSim.ConditionDamage);
            if (_carSim.IsDestroyed) HandleCityVehicleLoss();
        }
        return outcome;
    }

    private void ApplyCityFactionAction(TerritoryThreatLevel level, TerritoryZoneDefinition zone, Vector3d pos)
    {
        if (_calmPolicy.IsProtectionActive || _citySafeZone) return;
        _disguiseActive = _cityHazards.LegacyDisguise = false;
        _cityHazards.Sabotage.TriggerIdempotentSabotageAttack(_cityHazards.NextEventId(), zone.ZoneId,
            SabotageAttackKind.RoadblockOrInterception, _cityCustody, _calendar.CalendarTimeSeconds, _calmPolicy);
        if (level == TerritoryThreatLevel.Level2PaintAttack)
        {
            if (zone.ControllingFaction == FactionId.SociedadeDaTerraPlana)
            { _cityHazards.StartSpoofing(); _hazardNotice = Text("city.hazards.spoofing"); }
            else if (zone.ControllingFaction == FactionId.SindicatoDoMarcoTorto)
            { MoveCityBenchmark(zone); _hazardNotice = Text("city.hazards.benchmark_moved"); }
            else { _cityHazards.Lens.ApplySplatter(.45); _hazardNotice = Text("city.hazards.paint"); }
        }
        else if (level == TerritoryThreatLevel.Level3TirePunctureOrPursuit)
        {
            if (zone.ControllingFaction == FactionId.MapaFacilSa)
            { _cityHazards.TirePunctured = true; _hazardNotice = Text("city.hazards.puncture"); }
            else { _cityHazards.PursuitRemaining = 20; _hazardNotice = Text("city.hazards.pursuit"); }
        }
        else if (level >= TerritoryThreatLevel.Level4RoadblockOrPursuit)
        {
            _cityHazards.PursuitRemaining = 20; _cityHazards.RoadblockRemaining = 30;
            var heading = CurrentHeadingRadians();
            var block = new Vector3d(Math.Clamp(pos.X + Math.Cos(heading) * 22, 500008, 501992),
                Math.Clamp(pos.Y + Math.Sin(heading) * 22, 7400008, 7401992), pos.Z);
            _cityHazards.RoadblockPosition = block;
            if (NearCityUnload(block) || FullCityFactionPressureCoordinator.IsInsideProtectedSafeZone(GetRegionIdForEnu(block), block, out _))
            { _cityHazards.RoadblockPosition = null; _cityHazards.RoadblockRemaining = 0; }
            _hazardNotice = Text("city.hazards.roadblock");
        }
        _audio.PlayAlert(critical: true); _chaseCamera.TriggerShake(.15f, .1f); SaveHazardChange();
    }

    private void MoveCityBenchmark(TerritoryZoneDefinition zone)
    {
        var mark = _geodeticNetwork.Benchmarks.Values.Where(b => b.RegionId == zone.RegionId)
            .OrderBy(b => TrainingContractSession.HorizontalDistance(b.PhysicalPositionEnu, CurrentPositionEnu())).FirstOrDefault();
        if (mark is null) return;
        var displaced = mark.PhysicalPositionEnu + new Vector3d(.8, -.3, 0);
        _cityHazards.Sabotage.MovedBenchmarks.RegisterDisplacement(mark.BenchmarkId, mark.PublishedCadastralPositionEnu,
            displaced - mark.PublishedCadastralPositionEnu, _calendar.CalendarTimeSeconds);
        SetCityBenchmarkPhysicalPosition(mark.BenchmarkId, displaced);
    }

    private void SetCityBenchmarkPhysicalPosition(string id, Vector3d position)
    {
        var mark = _geodeticNetwork.Benchmarks[id];
        if ((position - mark.PhysicalPositionEnu).Length > 1e-9)
            _geodeticNetwork.ApplyClandestineDisplacement(id, position - mark.PhysicalPositionEnu);
        var node = _worldRoot.GetNodeOrNull<Node3D>(id);
        if (node is not null) node.Position = _coordinates.ToGodot(position);
        if (FindChild("Beacon_" + id, true, false) is Node3D beacon) beacon.Position = _coordinates.ToGodot(position);
        if (_manifestObjectsById.TryGetValue(id, out var definition)) _manifestObjectsById[id] = definition.WithPhysicalPosition(position);
        _registry.UpdatePhysicalPosition(id, position);
        for (var i = 0; i < _liveCandidates.Length; i++)
            if (_liveCandidates[i].TargetId == id) _liveCandidates[i] = _liveCandidates[i] with { CenterEnu = position };
    }

    private void RestoreCityHazardWorld()
    {
        ClearCityThreatVisuals();
        (_, _geodeticNetwork, _) = FullCityGeodeticNetwork.AssembleFullCity(_tutorialManifest);
        foreach (var mark in _geodeticNetwork.Benchmarks.Values.ToArray())
            SetCityBenchmarkPhysicalPosition(mark.BenchmarkId, mark.PublishedCadastralPositionEnu);
        foreach (var moved in _cityHazards.Sabotage.MovedBenchmarks.Records)
            if (_geodeticNetwork.Benchmarks.ContainsKey(moved.BenchmarkId)) SetCityBenchmarkPhysicalPosition(moved.BenchmarkId, moved.DisplacedEnuPosition);
        SyncCityHazardProtection();
    }

    private void ClearCityThreatVisuals()
    {
        if (_roadblockObstacle is not null)
        { _roadblockObstacle = null; _hasNearbyObstacleAnchor = false; }
        if (_roadblockVisual is not null) { RemoveChild(_roadblockVisual); _roadblockVisual.QueueFree(); _roadblockVisual = null; }
        if (_pursuerVisual is not null) { RemoveChild(_pursuerVisual); _pursuerVisual.QueueFree(); _pursuerVisual = null; }
    }

    private void StepCityThreatVisuals(double dt, Vector3d pos, double speed)
    {
        if (_cityHazards.RoadblockRemaining <= 0 && _roadblockVisual is not null)
        { RemoveChild(_roadblockVisual); _roadblockVisual.QueueFree(); _roadblockVisual = null; _roadblockObstacle = null; _hasNearbyObstacleAnchor = false; }
        if (_cityHazards.RoadblockPosition is { } block && _cityHazards.RoadblockRemaining > 0 && _roadblockVisual is null)
        {
            _roadblockVisual = new StaticBody3D { Name = "FactionRoadblock", CollisionLayer = 1, Position = _coordinates.ToGodot(block) };
            _roadblockVisual.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(4, 1.2f, 1.2f) }, Position = new Vector3(0, .6f, 0) });
            _roadblockVisual.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(4, 1.2f, 1.2f) },
                Position = new Vector3(0, .6f, 0), MaterialOverride = GsaMeshBuilder.SolidMat(new Color(1, .45f, .1f)) });
            AddChild(_roadblockVisual);
            _roadblockObstacle = new("faction_roadblock", block + new Vector3d(0, 0, .6), new(4, 1.2, 1.2), true);
            _hasNearbyObstacleAnchor = false;
        }
        if (_cityHazards.PursuitRemaining <= 0 && _pursuerVisual is not null)
        { RemoveChild(_pursuerVisual); _pursuerVisual.QueueFree(); _pursuerVisual = null; }
        if (_cityHazards.PursuitRemaining > 0)
        {
            if (_pursuerVisual is null)
            {
                _pursuerVisual = new Node3D { Name = "FactionPursuer", Position = _coordinates.ToGodot(pos - new Vector3d(Math.Cos(CurrentHeadingRadians()) * 20, Math.Sin(CurrentHeadingRadians()) * 20, 0)) };
                _pursuerVisual.AddChild(new MeshInstance3D { Mesh = GsaMeshBuilder.BuildTrafficCarMesh(3), MaterialOverride = GsaMeshBuilder.SolidMat(new Color(.65f, .14f, .19f)) });
                AddChild(_pursuerVisual);
            }
            var previous = _coordinates.ToMap(_pursuerVisual.Position);
            var desired = _coordinates.ToMap(_pursuerVisual.Position.MoveToward(_coordinates.ToGodot(pos), (float)(dt * 14)));
            _pursuerVisual.Position = _coordinates.ToGodot(ResolveObstaclePushout(previous, desired, 1, out _));
            _pursuitCollisionCooldown = Math.Max(0, _pursuitCollisionCooldown - dt);
            if (_pursuitCollisionCooldown <= 0 && _pursuerVisual.Position.DistanceTo(_coordinates.ToGodot(pos)) < 2.5)
            {
                _pursuitCollisionCooldown = 3;
                if (_carSim is not null) { _carSim.ApplyCollisionImpact(4, "faction_interception"); _rackMisaligned = true; }
                _audio.PlayCollisionThud(Math.Max(3, Math.Abs(speed))); _gimbalina.ApplyStartle(StartleEventKind.Horn);
            }
        }
    }

    private void OffloadCityData()
    {
        if (_activeContract is null || !NearCityUnload()) { _feedbackText = Text("city.hazards.upload_location"); return; }
        if (_fieldSamples.Count == 0 && _pendingLidarReturns == 0) { _feedbackText = Text("city.hazards.nothing_to_upload"); return; }
        var now = _calendar.CalendarTimeSeconds;
        _activeMachine?.AdvanceClock(now);
        if (_activeMachine?.State == ContractState.Expirado)
        {
            _feedbackText = Text("city.office.expired");
            return;
        }
        if (_activeMachine?.State is not (ContractState.Aceito or ContractState.Coletando or ContractState.Retrabalho))
        {
            _feedbackText = Text("city.office.processing_failed");
            return;
        }

        var id = _cityHazards.NextEventId();
        var photoTimes = _fieldSamples.Select(s => s.TimestampSeconds);
        // Batch/contract events use calendar time. Each LiDAR slice keeps its own physical timestamp.
        var batchTimes = _pendingLidarReturns == 0 ? photoTimes : photoTimes.Append(now);
        var sensors = _fieldSamples.Select(s => s.SensorId).Concat(_pendingLidar.Select(slice => slice.ProfileId)).Distinct().ToArray();
        var artifact = new SurveyBatchArtifact(1, id, _activeContract.Id, batchTimes.Min(), batchTimes.Max(),
            _fieldSamples.Count + CityLidarSlice.StorageUnits(_pendingLidarReturns), 0, CaptureStopReason.None,
            sensors, _fieldSamples.Select(SerializableCameraSample.FromDomain).ToArray(), [], [], [])
        {
            LidarSlices = _pendingLidarReturns > 0 ? _pendingLidar.ToArray() : null,
        };
        _cityCustody.RegisterBufferedBatch(artifact, _calendar.CalendarTimeSeconds);
        var result = _cityCustody.OffloadAtomic(id, _calendar.CalendarTimeSeconds);
        if (result.Status is not (CustodyOperationStatus.Executed or CustodyOperationStatus.IdempotentNoOp))
        { _feedbackText = Text("city.hazards.upload_failed"); return; }
        _activeMachine?.RecordPendingBatch(_calendar.CalendarTimeSeconds, id);
        _activeMachine?.ProtectBatchAtUnloadPoint(_calendar.CalendarTimeSeconds, id);
        _protectedCityPhotos.AddRange(_fieldSamples); _fieldSamples.Clear();
        _protectedLidarReturns += _pendingLidarReturns;
        ClearPendingCityLidar();
        _locomotion.RefillHandcartResources(refillAutonomy: false, clearStorage: true);
        if (_gimbalina.HasFledToSafety) _locomotion.MarkGimbalinaFled();
        _audio.PlayOffloadChime(); _feedbackText = _hazardNotice = Text("city.hazards.uploaded"); SaveHazardChange();
    }

    private void RestoreCityProtectedPhotos()
    {
        _protectedCityPhotos.Clear();
        _protectedLidarReturns = 0;
        foreach (var entry in _cityCustody.Entries.Where(e => e.Stage == BatchCustodyStage.Protected))
        {
            var batch = JsonSerializer.Deserialize<SurveyBatchArtifact>(entry.PayloadJson) ?? throw new ArgumentException("Invalid city batch.");
            _protectedCityPhotos.AddRange(batch.Samples.Select(s => s.ToDomain()));
            _protectedLidarReturns += batch.LidarSlices?.Sum(slice => slice.Returns.Count) ?? 0;
        }
    }

    private void RebuildCityObservedTargets()
    {
        _observedTargetIds.Clear();
        foreach (var sample in _fieldSamples.Concat(_protectedCityPhotos).Where(s => s.Quality.Passed))
            foreach (var feature in sample.ObservedFeatures)
                if (feature.ObjectClass != CityPhotoCoverage.ObservationClass) _observedTargetIds.Add(feature.TargetId);
    }

    private void HandleCityVehicleLoss()
    {
        var lost = _fieldSamples.Count; var lostLidar = _pendingLidarReturns; var mode = _locomotion.MountedVehicleMode;
        if (mode >= LocomotionMode.Car) _cityHazards.DisabledVehicle = mode;
        _fieldSamples.Clear(); _cityCustody.HandleVehicleLoss(_calendar.CalendarTimeSeconds);
        ClearPendingCityLidar();
        _activeMachine?.HandleFieldIncident(_calendar.CalendarTimeSeconds, FieldIncidentKind.VehicleDestroyed);
        RebuildCityObservedTargets(); _coverageSpots.Clear();
        foreach (var sample in _protectedCityPhotos) _coverageSpots.Add(sample.EstimatedPose.PositionEnu);
        _lidarPointWriteIndex = _lidarPointVisibleCount = _lidarSliceIndex = 0; _lidarCloudMesh.Multimesh.VisibleInstanceCount = 0;
        _cityHazards.CancelActiveThreats(); _attention.CancelPendingThreats(); ClearCityThreatVisuals();
        _cleanLensRequested = _disarmRequested = false; _recentPhotoSeconds = 0;
        _cityHazards.TirePunctured = false; _carSim = null; CancelCityEverydayThreats();
        var office = _manifestObjectsById["office_entry"].PositionEnu + new Vector3d(0, -4, 0);
        _locomotion.RepositionSafeAfterRecovery(office, LocomotionMode.Handcart, 0, true, true);
        _mobility.SetVisualPlatformMode(LocomotionMode.Handcart); ResetCityNavigation(); SnapCamera();
        _audio.PlayAlert(critical: true);
        _feedbackText = _hazardNotice = string.Format(Text("city.hazards.vehicle_lost"), lost, _protectedCityPhotos.Count);
        if (IsTunnelCollection(_activeContract))
            _feedbackText = _hazardNotice = $"{_hazardNotice} {string.Format(Text("city.lidar.vehicle_lost"), lostLidar, _protectedLidarReturns)}";
        SaveHazardChange();
    }

    private void SaveHazardChange() { if (!_isSandbox) SaveCityCareerState(announce: false); }
}
