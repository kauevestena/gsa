using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Camera;
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
    private Camera3D _playerCamera = null!;
    private Label _trainingStatus = null!;
    private Label _trainingFeedback = null!;
    private Label _trainingTargets = null!;
    private readonly System.Collections.Generic.Dictionary<string, Button> _trainingButtons = new();
    private long _liveTick;
    private bool _captureRequested;
    private bool _surveyView;
    private double _hudElapsed;
    private string? _smokeSaveDirectory;
    private string _lastObservation = "—";

    private static string Text(string key) => TranslationServer.Translate(key).ToString();

    private void InitializeTraining()
    {
        var slot = _smoke
            ? (_smokeSaveDirectory = Path.Combine(Path.GetTempPath(), "gsa-live-smoke-" + Guid.NewGuid().ToString("N")))
            : ProjectSettings.GlobalizePath("user://training-live");
        _training = new TrainingContractSession(_manifest, slot);
        var loaded = _smoke ? null : _training.Load();
        _liveCamera = new CameraCaptureScheduler("training_" + Guid.NewGuid().ToString("N"));
        _liveCandidates = _manifest.Objects.Select(CameraVisibilityCandidate.FromWorldObject).ToArray();
        _liveLocomotion!.RepositionSafeAfterRecovery(TrainingContractSession.SpawnPosition, headingRadians: Math.PI);
        _playerCamera = GetNode<Camera3D>("InspectionCamera");
        BuildTrainingHud();
        foreach (var id in TrainingContractSession.TargetIds.Concat(new[] { "office_entry", "unload_point" }))
        {
            var item = _manifest.Objects.Single(o => o.Id == id);
            var label = new Label3D
            {
                Text = id.StartsWith("facade_", StringComparison.Ordinal) ? id[7..] : Text("training." + id),
                Position = _coordinates.ToGodot(item.PositionEnu + new Vector3d(0, 0, 3)),
                FontSize = 64,
                PixelSize = 0.015f,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                Modulate = new Color(1, 0.85f, 0.3f),
                NoDepthTest = true,
            };
            AddChild(label);
        }
        if (loaded is { Succeeded: false } && loaded.Status != Gsa.Missions.Persistence.SaveLoadStatus.FileNotFound)
            _trainingFeedback.Text = Text("training.load_failed");
        else if (loaded?.Succeeded == true)
            _trainingFeedback.Text = Text("training.loaded");
        RefreshTrainingHud();
    }

    private void BuildTrainingHud()
    {
        var canvas = new CanvasLayer { Name = "TrainingHud" };
        AddChild(canvas);
        var panel = new PanelContainer { Position = new Vector2(12, 12), CustomMinimumSize = new Vector2(330, 0), AnchorBottom = 1, OffsetBottom = -12 };
        canvas.AddChild(panel);
        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 10);
        panel.AddChild(margin);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        margin.AddChild(scroll);
        var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(box);
        Label AddLabel(string value, int fontSize = 14)
        {
            var label = new Label { Text = value, CustomMinimumSize = new Vector2(310, 0),
                AutowrapMode = TextServer.AutowrapMode.WordSmart };
            label.AddThemeFontSizeOverride("font_size", fontSize);
            box.AddChild(label);
            return label;
        }
        AddLabel(Text("training.title"), 20);
        AddLabel(Text("training.instructions"));
        _trainingStatus = AddLabel("");
        _trainingTargets = AddLabel("");
        foreach (var action in new[] { "accept", "capture", "offload", "process", "deliver", "save", "load", "view" })
        {
            var button = new Button { Text = Text("training.action." + action), FocusMode = Control.FocusModeEnum.None };
            button.Pressed += () => TrainingAction(action);
            _trainingButtons.Add(action, button);
            box.AddChild(button);
        }
        _trainingFeedback = AddLabel(Text("training.welcome"));
        AddLabel(Text("training.controls"));
    }

    private void TrainingAction(string action)
    {
        try
        {
            var position = _liveLocomotion!.PositionEnu;
            bool succeeded;
            switch (action)
            {
                case "accept": succeeded = _training.Accept(position); break;
                case "capture":
                    succeeded = _training.CanCapture;
                    _captureRequested = succeeded;
                    break;
                case "offload":
                    succeeded = _training.Offload(position);
                    if (succeeded) _liveLocomotion.RefillHandcartResources(refillAutonomy: false);
                    break;
                case "process": succeeded = _training.Process(position); break;
                case "deliver": succeeded = _training.Deliver(position); break;
                case "save": succeeded = _training.Save().Succeeded; break;
                case "load":
                    succeeded = _training.Load().Succeeded;
                    if (succeeded)
                    {
                        _captureRequested = false;
                        _liveCamera = new CameraCaptureScheduler("training_" + Guid.NewGuid().ToString("N"));
                        _liveLocomotion.RepositionSafeAfterRecovery(TrainingContractSession.SpawnPosition, headingRadians: Math.PI);
                    }
                    break;
                case "view": _surveyView = !_surveyView; succeeded = true; break;
                default: return;
            }
            _trainingFeedback.Text = Text(succeeded ? "training.done." + action : "training.blocked");
            if (action == "process" && succeeded)
                _trainingFeedback.Text = Text(_training.Report!.Passed ? "training.qc_passed" : "training.qc_rework");
            RefreshTrainingHud();
        }
        catch (Exception error) when (!_smoke && error is IOException or UnauthorizedAccessException)
        {
            _trainingFeedback.Text = Text("training.load_failed");
            GD.PushWarning(error.Message);
        }
    }

    private bool TrainingInput(InputEvent input)
    {
        if (input is not InputEventKey { Pressed: true, Echo: false } key) return false;
        var action = key.PhysicalKeycode switch { Key.C => "capture", Key.V => "view", Key.F5 => "save", Key.F9 => "load", _ => null };
        if (action is null) return false;
        TrainingAction(action);
        return true;
    }

    private void StepTraining(double delta, double angularVelocity)
    {
        _training.AdvanceClock(delta);
        if (_captureRequested && _liveCamera.IsCaptureTick(_liveTick))
        {
            _captureRequested = false;
            CaptureLiveFrame(angularVelocity);
        }
        _liveTick++;
        var at = _coordinates.ToGodot(_liveLocomotion!.PositionEnu);
        if (_surveyView)
        {
            _playerCamera.Position = at + Vector3.Up;
            var direction = new Vector3((float)Math.Cos(_liveLocomotion.HeadingRadians), 0, -(float)Math.Sin(_liveLocomotion.HeadingRadians));
            _playerCamera.LookAt(_playerCamera.Position + direction, Vector3.Up);
        }
        else
        {
            _playerCamera.Position = at + new Vector3(0, 22, 18);
            _playerCamera.LookAt(at, Vector3.Up);
        }
        _hudElapsed += delta;
        if (_hudElapsed >= 0.25) { _hudElapsed = 0; RefreshTrainingHud(); }
    }

    private void CaptureLiveFrame(double angularVelocity = 0)
    {
        var body = _liveLocomotion!;
        var mount = CameraMountEstimate.ForwardFacing;
        var origin = body.PositionEnu + Vector3d.UnitZ;
        var time = _training.ClockSeconds;
        EstimatedBodyPose Pose(double dt) => new(time + dt, origin + body.VelocityEnu * dt,
            Quaterniond.FromAxisAngle(Vector3d.UnitZ, body.HeadingRadians + angularVelocity * dt));
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
        if (_training.CanCapture && _liveCamera.TryEnqueueSimulatedCapture(new CameraCaptureRequest(
            _liveTick, time, start, end, readout, Pose(0), mount, CameraEnvironmentSnapshot.DryDay,
            _liveCandidates, Occluded), body, out var sample, out _) && sample is not null)
        {
            _training.RecordObservation(sample);
            _lastObservation = string.Join(", ", sample.ObservedFeatures.Select(f => f.TargetId));
            if (_lastObservation.Length == 0) _lastObservation = Text("training.nothing_visible");
            _trainingFeedback.Text = Text(sample.Quality.Passed ? "training.capture_ok" : "training.capture_blurred");
        }
        // Ownership moves to custody; do not retain a second unbounded thumbnail buffer.
        _liveCamera.FlushBatchManifest("live_flush");
        RefreshTrainingHud();
    }

    private void RefreshTrainingHud()
    {
        var position = _liveLocomotion!.PositionEnu;
        var stateKey = "training.state." + _training.Contract.State;
        _trainingStatus.Text = string.Format(Text("training.status"), Text(stateKey), _training.SampleCount,
            _training.PendingCount, _training.ProtectedCount, _training.Ledger.BalanceCoords,
            _training.OfficeDistance(position).ToString("F0"), _training.UnloadDistance(position).ToString("F0"));
        _trainingButtons["save"].Disabled = _training.SaveBlocked;
        var resolved = _training.ResolvedTargetIds;
        _trainingTargets.Text = Text("training.targets") + " " + string.Join(" · ", TrainingContractSession.TargetIds
            .Select(id => id[7..] + (resolved.Contains(id) ? " ✓" : " …"))) + "\n" + Text("training.observed") + " " + _lastObservation;
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
            GD.Print($"GSA_PLAYABLE_TRAINING_SMOKE_OK samples={_training.SampleCount} targets=3 rework=true reload=true balance={paidBalance}");
        }
        finally
        {
            if (_smokeSaveDirectory is not null && Directory.Exists(_smokeSaveDirectory)) Directory.Delete(_smokeSaveDirectory, true);
        }
    }
}
