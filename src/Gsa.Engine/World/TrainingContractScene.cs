using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Camera;
using Gsa.Core.Sensors.Gnss;
using Gsa.Engine.UI;
using Gsa.Missions.Contracts;
using Gsa.Missions.Gameplay;
using Godot;
using Dictionary = Godot.Collections.Dictionary;

namespace Gsa.Engine.World;

public partial class TutorialBlockScene
{
    private TrainingContractSession _training = null!;
    private CameraCaptureScheduler _liveCamera = null!;
    private CameraVisibilityCandidate[] _liveCandidates = [];
    private GsaGtaHud _gtaHud = null!;
    private readonly System.Collections.Generic.Dictionary<string, Button> _trainingButtons = new();
    private System.Collections.Generic.Dictionary<string, Gsa.Core.World.WorldBoxObject> _manifestById = new(StringComparer.Ordinal);
    private readonly System.Collections.Generic.Dictionary<string, Label3D> _facadeStatusLabels = new(StringComparer.Ordinal);
    private readonly System.Collections.Generic.List<RadarBlip> _radarBlips = new(16);
    private long _liveTick;
    private bool _captureRequested;
    private double _hudElapsed;
    private double _trafficStressCooldown;
    private string? _smokeSaveDirectory;
    private string _lastObservation = "—";
    private string _lastFeedbackText = "";
    private string? _pendingVehiclePopup;
    private string? _pendingDistrictPopup;

    private static string Text(string key) => TranslationServer.Translate(key).ToString();

    private void InitializeTraining()
    {
        var slot = _smoke
            ? (_smokeSaveDirectory = Path.Combine(Path.GetTempPath(), "gsa-live-smoke-" + Guid.NewGuid().ToString("N")))
            : ProjectSettings.GlobalizePath("user://training-live");
        _training = new TrainingContractSession(_manifest, slot);
        var loaded = _smoke ? null : _training.Load();
        _liveCamera = new CameraCaptureScheduler("training_" + Guid.NewGuid().ToString("N"));
        _manifestById = _manifest.Objects.ToDictionary(o => o.Id, StringComparer.Ordinal);
        _liveCandidates = _manifest.Objects.Select(CameraVisibilityCandidate.FromWorldObject).ToArray();
        _liveLocomotion!.RepositionSafeAfterRecovery(TrainingContractSession.SpawnPosition, headingRadians: Math.PI);
        _lastSpeedMps = 0.0;
        BuildTrainingHud();

        foreach (var id in TrainingContractSession.TargetIds.Concat(new[] { "office_entry", "unload_point" }))
        {
            var item = _manifestById[id];
            var isTarget = id.StartsWith("facade_", StringComparison.Ordinal);
            var label = new Label3D
            {
                Text = isTarget ? $"#{id[7..]} · 0/2" : Text("training." + id),
                Position = _coordinates.ToGodot(item.PositionEnu + new Vector3d(0, isTarget ? -0.35 : 0, isTarget ? 2.85 : 2.35)),
                FontSize = isTarget ? 34 : 28,
                PixelSize = 0.006f,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                Modulate = isTarget
                    ? new Color(1f, 0.85f, 0.25f)
                    : (id == "office_entry" ? new Color(0.25f, 0.95f, 0.88f) : new Color(1f, 0.78f, 0.28f)),
                OutlineModulate = Colors.Black,
                OutlineSize = 5,
                NoDepthTest = false,
                AlphaCut = Label3D.AlphaCutMode.Disabled,
                VisibilityRangeEnd = 70f,
            };
            AddChild(label);
            if (isTarget)
                _facadeStatusLabels[id] = label;
        }

        _pendingDistrictPopup = Text("district.old_center.commerce");
        _pendingVehiclePopup = Text("vehicle.handcart.banner");
        if (loaded is { Succeeded: false } && loaded.Status != Gsa.Missions.Persistence.SaveLoadStatus.FileNotFound)
            _lastFeedbackText = Text("training.load_failed");
        else if (loaded?.Succeeded == true)
            _lastFeedbackText = Text("training.loaded");
        else
            _lastFeedbackText = Text("training.welcome");

        // Snap camera behind player immediately on frame 0
        _chaseCamera?.Snap(
            _coordinates.ToGodot(_liveLocomotion.PositionEnu),
            _liveLocomotion.HeadingRadians,
            _liveLocomotion.Mode);
        RefreshTrainingHud();
    }

    private void BuildTrainingHud()
    {
        _gtaHud = new GsaGtaHud(this);
        _gtaHud.ConfigureRadar(_manifest, 92f);

        foreach (var action in new[] { "accept", "capture", "offload", "process", "deliver", "notebook", "view", "save", "load" })
        {
            var labelKey = action == "notebook" ? "notebook.open" : "training.action." + action;
            var btn = _gtaHud.AddQuickButton(Text(labelKey), () => TrainingAction(action));
            _trainingButtons.Add(action, btn);
        }

        BuildSurveyNotebook(_gtaHud.Layer);
    }

    private void TrainingAction(string action)
    {
        try
        {
            var position = _liveLocomotion!.PositionEnu;
            bool succeeded;
            switch (action)
            {
                case "accept":
                    succeeded = _training.Accept(position);
                    if (succeeded) _audio?.PlayOffloadChime();
                    break;
                case "capture":
                    succeeded = _training.CanCapture;
                    _captureRequested = succeeded;
                    break;
                case "offload":
                    succeeded = _training.Offload(position);
                    if (succeeded)
                    {
                        _liveLocomotion.RefillHandcartResources(refillAutonomy: false);
                        _audio?.PlayOffloadChime();
                    }
                    break;
                case "process":
                    succeeded = _training.Process(position);
                    if (succeeded)
                    {
                        if (_training.Report?.Passed == true) _audio?.PlayPaymentSuccess();
                        else _audio?.PlayAlert();
                    }
                    break;
                case "deliver":
                    succeeded = _training.Deliver(position);
                    if (succeeded)
                    {
                        _audio?.PlayMissionPassedFanfare();
                        _gtaHud?.TriggerCelebrationBanner(Text("city.mission_passed"), "+150 C$ · +25 XP");
                    }
                    break;
                case "save":
                    succeeded = _training.Save().Succeeded;
                    break;
                case "load":
                    succeeded = _training.Load().Succeeded;
                    if (succeeded)
                    {
                        _captureRequested = false;
                        _liveCamera = new CameraCaptureScheduler("training_" + Guid.NewGuid().ToString("N"));
                        _liveLocomotion.RepositionSafeAfterRecovery(TrainingContractSession.SpawnPosition, headingRadians: Math.PI);
                    }
                    break;
                case "notebook":
                    _notebookPanel.Visible = !_notebookPanel.Visible;
                    succeeded = true;
                    break;
                case "view":
                    _chaseCamera?.CycleMode();
                    succeeded = true;
                    break;
                case "mount":
                    succeeded = _liveLocomotion.TryToggleMount(_liveObstacles, out _);
                    if (succeeded)
                        _pendingVehiclePopup = Text(_liveLocomotion.Mode == LocomotionMode.OnFoot ? "vehicle.on_foot.banner" : "vehicle.handcart.banner");
                    break;
                case "corn":
                    succeeded = _gimbalina.TryFeedCorn();
                    if (succeeded) _audio?.PlayOffloadChime();
                    break;
                case "horn":
                    _audio?.PlayHorn(_liveLocomotion!.Mode);
                    _trafficBench?.Simulator.StartleNearbyPedestrians(_liveLocomotion!.PositionEnu, 9.0);
                    if (_liveLocomotion!.Mode <= LocomotionMode.Bicycle)
                        _gimbalina.ApplyStartle(StartleEventKind.Horn);
                    succeeded = true;
                    break;
                case "radio":
                    _audio?.CycleRadioStation();
                    succeeded = true;
                    break;
                case "weather":
                    _calendarWeather.SetWeather(!_calendarWeather.IsRaining, 0.75);
                    succeeded = true;
                    break;
                case "time":
                    _calendarWeather.AdvanceCalendarSeconds(7200.0);
                    succeeded = true;
                    break;
                case "dither":
                    if (_atmosphere is not null)
                        _atmosphere.SetRetroDitherEnabled(!_atmosphere.RetroDitherEnabled);
                    succeeded = true;
                    break;
                default:
                    return;
            }
            if (action is "accept" or "capture" or "offload" or "process" or "deliver" or "save" or "load" or "notebook" or "view")
            {
                _lastFeedbackText = Text(succeeded ? "training.done." + action : "training.blocked");
                if (action == "process" && succeeded)
                    _lastFeedbackText = Text(_training.Report!.Passed ? "training.qc_passed" : "training.qc_rework");
            }
            RefreshTrainingHud();
        }
        catch (Exception error) when (!_smoke && error is IOException or UnauthorizedAccessException)
        {
            _lastFeedbackText = Text("training.load_failed");
            GD.PushWarning(error.Message);
        }
    }

    private bool TrainingInput(InputEvent input)
    {
        if (input is not InputEventKey { Pressed: true, Echo: false } key) return false;
        var action = key.PhysicalKeycode switch
        {
            Key.R or Key.Tab or Key.M => "notebook",
            Key.C => "capture",
            Key.V => "view",
            Key.F => "mount",
            Key.G => "corn",
            Key.H => "horn",
            Key.X => "radio",
            Key.P => "weather",
            Key.T => "time",
            Key.F5 => "save",
            Key.F9 => "load",
            Key.F10 => "dither",
            Key.E => GetContextualInteractAction(),
            _ => null,
        };
        if (action is null) return false;
        TrainingAction(action);
        return true;
    }

    private string? GetContextualInteractAction()
    {
        var pos = _liveLocomotion!.PositionEnu;
        if (_training.UnloadDistance(pos) <= 5.0 && _training.PendingCount > 0)
            return "offload";
        if (_training.OfficeDistance(pos) <= 8.0)
        {
            if (_training.Contract.State == ContractState.Disponivel) return "accept";
            if (_training.Contract.State == ContractState.ProntoEntregar) return "deliver";
            if (_training.PendingCount == 0 && _training.ProtectedCount > 0 &&
                _training.Contract.State is (ContractState.Coletando or ContractState.Retrabalho))
                return "process";
        }
        return null;
    }

    private void StepTraining(double delta, double angularVelocity)
    {
        _training.AdvanceClock(delta);
        _trafficStressCooldown = Math.Max(0.0, _trafficStressCooldown - delta);
        if (!_smoke && _liveLocomotion is not null && _trafficStressCooldown <= 0.0)
        {
            var p = _liveLocomotion.PositionEnu;
            var speed = Math.Abs(_liveLocomotion.ForwardSpeedMetresPerSecond);
            // Stepping into the active traffic lane at speed startles Gimbalina
            if ((p.Y < 7399996.6 || p.Y > 7400018.8) && speed > 1.4)
            {
                _trafficStressCooldown = 2.6;
                _gimbalina.ApplyStartle(StartleEventKind.Horn);
                _audio?.PlayHorn(LocomotionMode.Car);
            }
        }
        if (_captureRequested && _liveCamera.IsCaptureTick(_liveTick))
        {
            _captureRequested = false;
            CaptureLiveFrame(angularVelocity);
        }
        _liveTick++;

        if (_chaseCamera is not null && _liveLocomotion is not null)
        {
            _chaseCamera.Update(
                delta,
                _coordinates.ToGodot(_liveLocomotion.PositionEnu),
                _liveLocomotion.HeadingRadians,
                _liveLocomotion.ForwardSpeedMetresPerSecond,
                _liveLocomotion.Mode,
                GetWorld3D().DirectSpaceState,
                _gimbalina.Stress,
                _training.ClockSeconds);
        }

        _hudElapsed += delta;
        if (_hudElapsed >= 0.1)
        {
            _hudElapsed = 0;
            RefreshTrainingHud(_hudElapsed == 0 ? 0.1 : delta);
        }
    }

    private void CaptureLiveFrame(double angularVelocity = 0)
    {
        var body = _liveLocomotion!;
        var mount = CameraMountEstimate.ForwardFacing;
        var origin = body.PositionEnu + Vector3d.UnitZ;
        var time = _training.ClockSeconds;
        var baseBody = Quaterniond.FromAxisAngle(Vector3d.UnitZ, body.HeadingRadians);
        EstimatedBodyPose Pose(double dt) => new(time + dt, origin + body.VelocityEnu * dt,
            _gimbalina.ComputeEffectiveBodyToMap(
                Quaterniond.FromAxisAngle(Vector3d.UnitZ, body.HeadingRadians + angularVelocity * dt), dt));
        var start = CameraGeometry.CameraToMap(Pose(0), mount);
        var end = CameraGeometry.CameraToMap(Pose(CameraSensorProfile.Initial.DayExposureSeconds), mount);
        var readout = CameraGeometry.CameraToMap(Pose(CameraSensorProfile.Initial.ReadoutTimeSeconds), mount);
        bool Occluded(Vector3d fromEnu, Vector3d toEnu, string targetId)
        {
            var from = _coordinates.ToGodot(fromEnu);
            var to = _coordinates.ToGodot(toEnu);
            if (from.DistanceSquaredTo(to) < 1e-8f) return false;
            using var query = PhysicsRayQueryParameters3D.Create(from, to, 1);
            var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
            if (hit.Count == 0) return false;
            var collider = hit["collider"].AsGodotObject();
            var semantic = collider is null ? new Dictionary() : _registry.GetByInstanceId(collider.GetInstanceId());
            if (semantic.Count > 0 && semantic["id"].AsString() == targetId) return false;
            return from.DistanceTo(hit["position"].AsVector3()) < from.DistanceTo(to) - 0.01f;
        }
        var weatherSnap = _calendarWeather.ExportSnapshot();
        var envSnap = weatherSnap.ToCameraEnvironment();
        var candidates = _smoke
            ? _liveCandidates
            : _liveCandidates.Where(c => c.ObjectClass != "building_facade" ||
                (origin.Y < c.CenterEnu.Y - 1.2 && Math.Abs(origin.X - c.CenterEnu.X) <= 8.5 && Math.Sin(body.HeadingRadians) >= 0.35)).ToArray();
        if (_training.CanCapture && _liveCamera.TryEnqueueSimulatedCapture(new CameraCaptureRequest(
            _liveTick, time, start, end, readout, new EstimatedBodyPose(time, origin, baseBody), mount, envSnap,
            candidates, Occluded), body, out var sample, out _) && sample is not null)
        {
            _training.RecordObservation(sample);
            _audio?.PlayShutter();
            _lastObservation = string.Join(", ", sample.ObservedFeatures.Select(f => f.TargetId));
            if (_lastObservation.Length == 0) _lastObservation = Text("training.nothing_visible");
            if (!sample.Quality.Passed)
                _lastFeedbackText = !_smoke && _gimbalina.Stress >= 40.0 ? Text("training.stabilize_gimbalina") : Text("training.capture_blurred");
            else if (!_smoke && !sample.ObservedFeatures.Any(f => f.ObjectClass == "building_facade"))
                _lastFeedbackText = Text("training.face_storefront");
            else
                _lastFeedbackText = Text("training.capture_ok");
        }
        // Ownership moves to custody; do not retain a second unbounded thumbnail buffer.
        _liveCamera.FlushBatchManifest("live_flush");
        RefreshTrainingHud();
    }

    private void RefreshTrainingHud(double dt = 0.1)
    {
        RefreshSurveyNotebook();
        if (_gtaHud is null || _liveLocomotion is null)
            return;

        var position = _liveLocomotion.PositionEnu;
        var weather = _calendarWeather.ExportSnapshot();
        var stateKey = "training.state." + _training.Contract.State;
        var resolved = _training.ResolvedTargetIds;

        var targetsStr = string.Join(" · ", TrainingContractSession.TargetIds
            .Select(id => id[7..] + (resolved.Contains(id) ? " ✓" : " …")));
        var objDetails = $"{Text(stateKey)} · {Text("training.targets")} {targetsStr}\n" +
                         $"O: {_training.OfficeDistance(position):F0}m · D: {_training.UnloadDistance(position):F0}m · " +
                         $"{Text("training.observed")} {_lastObservation}";

        var skyOpenness = 0.85;
        if (_skyCache is not null && _skyCache.TryGetOpenness(_coordinates.ToGodot(position + Vector3d.UnitZ * 2.0), out var o))
            skyOpenness = o;
        var gnssText = skyOpenness >= 0.45 ? Text("hud.gnss.autonomous") : Text("hud.gnss.degraded");
        var gnssColor = skyOpenness >= 0.45 ? new Color(0.32f, 0.92f, 0.56f) : new Color(0.96f, 0.78f, 0.24f);

        var hours = (int)weather.HourOfDay;
        var mins = (int)((weather.HourOfDay - hours) * 60.0);
        var clockText = $"{hours:00}:{mins:00}";
        var weatherText = weather.IsRaining ? Text("hud.weather.rain") : Text("hud.weather.dry");

        _radarBlips.Clear();
        foreach (var id in TrainingContractSession.TargetIds)
        {
            var obj = _manifestById[id];
            var isDone = resolved.Contains(id);
            var obsCount = _training.Samples.Count(s => s.Quality.Passed && s.ObservedFeatures.Any(f => f.TargetId == id));
            if (_facadeStatusLabels.TryGetValue(id, out var lbl))
            {
                var nextText = isDone ? $"#{id[7..]} · ✓" : obsCount >= 1 ? $"#{id[7..]} · 1/2 (move >=2m)" : $"#{id[7..]} · 0/2";
                var nextColor = isDone ? new Color(0.28f, 0.95f, 0.50f) : obsCount >= 1 ? new Color(0.30f, 0.90f, 0.98f) : new Color(1f, 0.85f, 0.25f);
                if (lbl.Text != nextText) lbl.Text = nextText;
                if (!lbl.Modulate.IsEqualApprox(nextColor)) lbl.Modulate = nextColor;
            }
            _radarBlips.Add(new RadarBlip(id, obj.PositionEnu, isDone ? new Color(0.28f, 0.92f, 0.54f) : obsCount >= 1 ? new Color(0.30f, 0.90f, 0.98f) : new Color(1.0f, 0.80f, 0.22f), id[7..]));
        }
        var officeObj = _manifestById["office_entry"];
        var unloadObj = _manifestById["unload_point"];
        _radarBlips.Add(new RadarBlip("office_entry", officeObj.PositionEnu, new Color(0.20f, 0.90f, 0.95f), "O"));
        _radarBlips.Add(new RadarBlip("unload_point", unloadObj.PositionEnu, new Color(0.98f, 0.76f, 0.18f), "D"));
        foreach (var obj in _manifest.Objects)
        {
            if (obj.ObjectClass == "survey_control")
                _radarBlips.Add(new RadarBlip(obj.Id, obj.PositionEnu, new Color(0.38f, 0.95f, 0.45f), "▲", ClampToRim: false));
        }

        var coverageSpots = _training.Samples.Select(s => s.EstimatedPose.PositionEnu).ToArray();

        var radioTitle = _audio is not null && _audio.RadioStation > 0 ? "♫ " + Text(_audio.ActiveRadioTitleKey) : "";
        var radioSub = _audio is not null && !string.IsNullOrEmpty(_audio.CurrentRadioSubtitleKey) ? Text(_audio.CurrentRadioSubtitleKey) : "";

        _gtaHud.Radar.UpdateDynamicWorld(null, _trafficBench?.VisualAgents);
        _gtaHud.Update(
            dt,
            new GtaHudState(
                ClockText: clockText,
                WeatherText: weatherText,
                RadioTitleText: radioTitle,
                RadioSubtitleText: radioSub,
                BalanceCoords: _training.Ledger.BalanceCoords,
                Xp: _training.Progression.TotalXp,
                PlatformTitleText: Text(_liveLocomotion.Mode == LocomotionMode.OnFoot ? "platform.on_foot.name" : "platform.handcart.name"),
                GnssText: gnssText,
                GnssColor: gnssColor,
                ShowLidar: false,
                LidarActive: false,
                AutonomyFraction: _liveLocomotion.CurrentProfile.HasFiniteAutonomy
                    ? _liveLocomotion.RemainingAutonomySeconds / _liveLocomotion.CurrentProfile.PhysicalAutonomySeconds
                    : 1.0,
                StorageUsed: _training.SampleCount,
                StorageCapacity: TrainingContractSession.SampleLimit,
                ShowGimbalinaStress: true,
                GimbalinaStressFraction: _gimbalina.Stress / 100.0,
                ShowVehicleCondition: false,
                VehicleDamageFraction: 0.0,
                ShowAttentionStars: false,
                AttentionStars: 0,
                ObjectiveTitle: Text("training.title"),
                ObjectiveDetails: objDetails,
                FeedbackText: _lastFeedbackText,
                ContextPromptText: Text("hud.prompt.controls"),
                SurveyViewfinderActive: _chaseCamera?.Mode == GsaCameraMode.SurveyView,
                DistrictPopup: _pendingDistrictPopup,
                VehiclePopup: _pendingVehiclePopup),
            position,
            _liveLocomotion.HeadingRadians,
            _radarBlips,
            coverageSpots);

        _pendingDistrictPopup = null;
        _pendingVehiclePopup = null;

        _trainingButtons["save"].Disabled = _training.SaveBlocked;
        _trainingButtons["accept"].Disabled = _training.SaveBlocked || _training.Contract.State != ContractState.Disponivel || _training.OfficeDistance(position) > 8;
        _trainingButtons["capture"].Disabled = !_training.CanCapture;
        _trainingButtons["offload"].Disabled = _training.PendingCount == 0 || _training.UnloadDistance(position) > 5;
        _trainingButtons["process"].Disabled = _training.PendingCount != 0 || _training.ProtectedCount == 0 ||
            _training.OfficeDistance(position) > 8 || _training.Contract.State is not (ContractState.Coletando or ContractState.Retrabalho);
        _trainingButtons["deliver"].Disabled = _training.Contract.State != ContractState.ProntoEntregar || _training.OfficeDistance(position) > 8;
    }

    private void VerifyPlayableTraining()
    {
        void Press(string action) => _trainingButtons[action].EmitSignal(BaseButton.SignalName.Pressed);
        void CaptureAt(double east, double heading)
        {
            _liveLocomotion!.RepositionSafeAfterRecovery(new Vector3d(east, 7400000.7, 100), headingRadians: heading);
            Press("capture");
            _liveTick = ((_liveTick + 29) / 30) * 30;
            StepTraining(1.0 / 60.0, 0);
        }
        void ReturnToOffice() => _liveLocomotion!.RepositionSafeAfterRecovery(TrainingContractSession.SpawnPosition, headingRadians: Math.PI);
        try
        {
            Press("accept");
            Require(_training.Contract.State == ContractState.Aceito, "Training accept button did not accept contract.");
            CaptureAt(500228, -Math.PI / 2); // Facing away: must not manufacture required facade observations.
            Require(_training.ResolvedTargetIds.Count == 0, "Empty view reconstructed a facade.");
            ReturnToOffice(); Press("offload"); Press("process");
            Require(_training.Contract.State == ContractState.Retrabalho, "Incomplete live capture must require rework.");
            foreach (var east in new[] { 500188.0, 500191.0, 500198.0, 500201.0, 500208.0, 500211.0 })
                CaptureAt(east, Math.PI / 2);
            Require(_training.ResolvedTargetIds.Count == 3, "Real physics camera rays did not triangulate training facades.");
            ReturnToOffice(); Press("offload"); Press("process");
            Require(_training.Report?.Passed == true && _training.Contract.State == ContractState.ProntoEntregar,
                "Processed live scene observations did not pass CQ.");
            Press("save"); Press("load");
            Require(_training.Report?.Passed == true, "Reload lost live quality result.");
            Press("deliver");
            var paidBalance = _training.Ledger.BalanceCoords;
            Press("load"); Press("deliver");
            Require(_training.Contract.State == ContractState.Entregue && _training.Ledger.BalanceCoords == paidBalance,
                "Reload/delivery duplicated or lost payment.");
            VerifySurveyNotebook();
            GD.Print($"GSA_PLAYABLE_TRAINING_SMOKE_OK samples={_training.SampleCount} targets=3 rework=true reload=true balance={paidBalance}");
        }
        finally
        {
            if (_smokeSaveDirectory is not null && Directory.Exists(_smokeSaveDirectory)) Directory.Delete(_smokeSaveDirectory, true);
        }
    }
}
