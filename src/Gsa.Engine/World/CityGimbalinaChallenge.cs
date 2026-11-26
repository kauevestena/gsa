using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Engine.UI;
using Gsa.Missions.Activities;
using Godot;

namespace Gsa.Engine.World;

public partial class SaoDatumCityScene
{
    private GimbalinaChallenge _parkRun = new();
    private GimbalinaRunHud _parkHud = null!;
    private bool _quickParkRun;
    private bool _newParkRecord;
    private bool _parkRewardPaid;
    private double _parkResultSeconds;
    private readonly Dictionary<LocomotionMode, double> _parkBest = new();
    private string ParkRecordsPath
    {
        get
        {
            if (_citySmokeDirectory is { } dir) return Path.Combine(dir, "park-records.cfg");
            if (OS.GetCmdlineUserArgs().Contains("--park-run-smoke"))
                return Path.Combine(OS.GetEnvironment("GSA_PARK_TEST_DIR"), "park-records.cfg");
            return ProjectSettings.GlobalizePath("user://park-records.cfg");
        }
    }

    private void BuildParkHud()
    {
        var layer = new CanvasLayer { Name = "ParkRunLayer", Layer = 4 };
        AddChild(layer);
        _parkHud = new GimbalinaRunHud { Name = "ParkRunHud", Visible = _quickParkRun };
        layer.AddChild(_parkHud);
        _parkHud.Build(_quickParkRun, RestartQuickParkRun, () => GetTree().ChangeSceneToFile("res://Bootstrap.tscn"));
        ReadParkRecords(ParkRecordsPath);
    }

    private void ReadParkRecords(string path)
    {
        using var records = new ConfigFile();
        if (records.Load(path) != Error.Ok) return;
        foreach (var mode in new[] { LocomotionMode.Bicycle, LocomotionMode.Handcart })
        {
            var time = records.GetValue("seconds", mode.ToString(), 0d).AsDouble();
            if (double.IsFinite(time) && time > 0 && time < TimeSpan.MaxValue.TotalSeconds &&
                (!_parkBest.TryGetValue(mode, out var known) || time < known))
                _parkBest[mode] = time;
        }
    }

    private void RestartQuickParkRun()
    {
        if (!_quickParkRun) return;
        _parkRun = new();
        _newParkRecord = _parkRewardPaid = false;
        _carSim = null;
        _gimbalina.RecoverAtOffice();
        _locomotion.RepositionSafeAfterRecovery(GimbalinaChallenge.Grid, LocomotionMode.Bicycle, 0);
        _mobility.SetVisualPlatformMode(LocomotionMode.Bicycle);
        _mobility.SyncAvatarPose(GimbalinaChallenge.Grid, 0);
        _lastSpeedMps = _parkBankRadians = 0;
        _tabletPanel.Hide();
        _gtaHud.Layer.Hide();
        UpdateSectorStreaming(GimbalinaChallenge.Grid);
        SnapCamera();
        RefreshParkHud();
    }

    private void StepParkRun(double dt, Vector3d previous, Vector3d position, LocomotionMode mode, GimbalinaStepResult head)
    {
        if (_parkRun.IsOver)
        {
            if (!_quickParkRun && (_parkResultSeconds -= dt) <= 0) _parkRun = new();
            return;
        }
        if (!_quickParkRun && _activeContract is not null && _parkRun.State == ParkRunState.Ready) return;
        var state = _parkRun.State;
        var gate = _parkRun.NextGate;
        _parkRun.Step(dt, previous, position, mode, head);
        if (_parkRun.IsOver)
        {
            _parkResultSeconds = 5;
            _parkRewardPaid = false;
            if (_parkRun.State == ParkRunState.Finished)
            {
                _newParkRecord = RecordParkTime(mode, _parkRun.Seconds);
                if (!_quickParkRun)
                {
                    _ledger.RecordActivityReward($"chicken_win_{Guid.NewGuid():N}", "chicken_challenge",
                        _calendar.CalendarTimeSeconds, 100, "challenge", "city.challenge.passed");
                    _parkRewardPaid = true;
                    if (!_isSandbox) SaveCityCareerState(announce: false);
                }
                _audio.PlayMissionPassedFanfare();
            }
            else _audio.PlayAlert();
        }
        else if (_parkRun.NextGate != gate)
        {
            if (state == ParkRunState.Ready) _newParkRecord = _parkRewardPaid = false;
            _audio.PlayOffloadChime();
        }
    }

    private bool RecordParkTime(LocomotionMode mode, double seconds)
    {
        if (_parkBest.TryGetValue(mode, out var best) && best <= seconds) return false;
        var path = ParkRecordsPath;
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using var writer = LockParkRecords(path);
            ReadParkRecords(path);
            if (_parkBest.TryGetValue(mode, out best) && best <= seconds) return false;
            _parkBest[mode] = seconds;
            using var records = new ConfigFile();
            foreach (var (platform, time) in _parkBest)
                records.SetValue("seconds", platform.ToString(), time);
            var error = records.Save(temporary);
            if (error != Error.Ok) GD.PushWarning($"Park sprint record could not be written ({error}).");
            else File.Move(temporary, path, overwrite: true);
        }
        catch (IOException error) { GD.PushWarning(error.Message); }
        catch (UnauthorizedAccessException error) { GD.PushWarning(error.Message); }
        finally { if (File.Exists(temporary)) Godot.DirAccess.RemoveAbsolute(temporary); }
        _parkBest[mode] = seconds;
        return true;
    }

    private static FileStream LockParkRecords(string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { return new FileStream(path + ".lock", FileMode.OpenOrCreate, System.IO.FileAccess.Write, FileShare.None); }
            catch (IOException) when (attempt < 6) { System.Threading.Thread.Sleep(5); }
        }
    }

    private void RefreshParkHud()
    {
        var show = _quickParkRun || (_parkRun.State != ParkRunState.Ready && !_tabletPanel.Visible);
        _parkHud.Visible = show;
        _gtaHud.Layer.Visible = !show;
        if (!show) return;
        var mode = _parkRun.State == ParkRunState.Ready ? CurrentActiveMode() : _parkRun.Platform;
        _parkHud.Present(_parkRun, CurrentPositionEnu(), mode, _parkBest.GetValueOrDefault(mode),
            _newParkRecord, _quickParkRun, _parkRewardPaid);
    }
}
