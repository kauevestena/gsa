using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Missions.Activities;
using Godot;

namespace Gsa.Engine.UI;

internal sealed partial class GimbalinaRunHud : Control
{
    private static readonly Color Ink = new("11272df2"), Mint = new("78ebc8"), Cream = new("fff0d7"), Amber = new("ffc259");
    private Label _target = null!, _split = null!, _time = null!, _steady = null!, _hint = null!;
    private Label _resultTitle = null!, _resultTime = null!, _resultDetails = null!, _record = null!;
    private ProgressBar _progress = null!;
    private PanelContainer _result = null!, _timingCard = null!, _ridingHint = null!;
    private Button _retry = null!;
    private ParkMedalIcon _medal = null!;

    private static string T(string key) => TranslationServer.Translate(key).ToString();
    internal static string RaceTime(double seconds)
    {
        var hundredths = (long)Math.Round(seconds * 100);
        return $"{hundredths / 6000}:{hundredths / 100 % 60:00}.{hundredths % 100:00}";
    }

    public void Build(bool quickPlay, Action retry, Action menu)
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        var left = Card("Course", 0, 0, 16, 16, 260, 0);
        var info = Column(left);
        Line(info, "CourseName", T("park.title"), 21, Cream);
        _split = Line(info, "Split", "", 14, Mint);
        _target = Line(info, "Target", "", 14, Cream);
        _progress = new ProgressBar { MaxValue = 200, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 5) };
        _progress.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new Color("365056") });
        _progress.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = Mint });
        info.AddChild(_progress);

        _timingCard = Card("Timing", 1, 0, -188, 16, -16, 0);
        var timing = Column(_timingCard);
        _time = Line(timing, "Time", "", 28, Cream);
        _steady = Line(timing, "Stability", "", 14, Mint);

        _ridingHint = Card("RidingHint", .5f, 1, -236, -66, 236, -12);
        _hint = Line(_ridingHint, "Hint", "", 15, Cream);
        _hint.HorizontalAlignment = HorizontalAlignment.Center;
        _hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;

        _result = Card("Result", .5f, .5f, -184, -110, 184, 110);
        var result = Column(_result);
        result.Alignment = BoxContainer.AlignmentMode.Center;
        var podium = new HBoxContainer();
        result.AddChild(podium);
        var score = Column(podium);
        score.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _resultTitle = Line(score, "Grade", "", 22, Amber);
        _resultTime = Line(score, "FinishTime", "", 39, Cream);
        _medal = new ParkMedalIcon { CustomMinimumSize = new Vector2(80, 82), MouseFilter = MouseFilterEnum.Ignore };
        podium.AddChild(_medal);
        _resultDetails = Line(result, "Details", "", 15, Cream);
        _resultDetails.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _record = Line(result, "Best", "", 16, Mint);
        var buttons = new HBoxContainer { Visible = quickPlay };
        result.AddChild(buttons);
        _retry = ResultButton(buttons, "Retry", T("park.retry"), retry);
        ResultButton(buttons, "Menu", T("park.menu"), menu);
        _result.Hide();
    }

    public void Present(GimbalinaChallenge run, Vector3d position, LocomotionMode mode,
        double best, bool newBest, bool quickPlay, bool rewardPaid)
    {
        var ready = run.State == ParkRunState.Ready;
        var distance = Math.Clamp(position.X - GimbalinaChallenge.Start.X, 0, 200);
        _progress.Value = distance;
        var next = Math.Min(run.NextGate, GimbalinaChallenge.Gates.Count - 1);
        var metres = (int)Math.Ceiling((GimbalinaChallenge.Gates[next] - position).Length);
        _split.Text = ready ? T("park.at_grid") :
            string.Format(T(next == GimbalinaChallenge.Gates.Count - 1 ? "park.last_split" : "park.split"), next, metres);
        _time.Text = RaceTime(run.Seconds);
        var percentage = (int)Math.Floor(100 * run.SteadyFraction + 1e-9);
        _steady.Text = string.Format(T("park.steadiness"), percentage);
        _steady.AddThemeColorOverride("font_color", run.HeadIsLevel && run.SteadyFraction >= GimbalinaChallenge.RequiredSteadyFraction ? Mint : Amber);
        _hint.Text = ready ? T("park.ride_to_start") :
            run.HeadIsLevel ? T("park.ride_hint") : T("park.banking");
        _ridingHint.Visible = !run.IsOver;
        _target.Visible = ready;
        if (ready) _target.Text = best > 0 ? string.Format(T("park.best"), RaceTime(best)) :
            string.Format(T("park.gold_target"), RaceTime(GimbalinaChallenge.GoldSeconds(mode)));
        _timingCard.Visible = !run.IsOver;
        var wasVisible = _result.Visible;
        _result.Visible = run.IsOver;
        if (!run.IsOver) return;
        _split.Text = run.State == ParkRunState.Finished ? T("park.all_gates") : T("park.run_ended");
        var medalKey = run.Medal switch
        {
            ParkRunMedal.Gold => "park.gold", ParkRunMedal.Silver => "park.silver",
            ParkRunMedal.Bronze => "park.bronze", _ => "park.try_again",
        };
        _resultTitle.Text = T(medalKey);
        _medal.Visible = run.Medal != ParkRunMedal.None;
        _medal.SetMedal(run.Medal);
        _resultTime.Text = RaceTime(run.Seconds);
        _resultDetails.Text = run.State switch
        {
            ParkRunState.Finished => _steady.Text + (rewardPaid ? "  ·  +100 C$" : ""),
            ParkRunState.MissedGate when next == GimbalinaChallenge.Gates.Count - 1 => T("park.missed_finish"),
            ParkRunState.MissedGate => string.Format(T("park.missed"), next),
            ParkRunState.Unsteady => string.Format(T("park.need_steady"), percentage),
            ParkRunState.LostPartner => T("park.partner"),
            _ => T("park.off_course"),
        };
        _record.Text = newBest ? T("park.new_best") : best > 0 ? string.Format(T("park.best"), RaceTime(best)) :
            string.Format(T("park.gold_target"), RaceTime(GimbalinaChallenge.GoldSeconds(mode)));
        if (!wasVisible && quickPlay) _retry.GrabFocus();
    }

    private PanelContainer Card(string name, float anchorX, float anchorY, float left, float top, float right, float bottom)
    {
        var card = new PanelContainer
        {
            Name = name, AnchorLeft = anchorX, AnchorRight = anchorX, AnchorTop = anchorY, AnchorBottom = anchorY,
            OffsetLeft = left, OffsetTop = top, OffsetRight = right, OffsetBottom = bottom, MouseFilter = MouseFilterEnum.Ignore,
        };
        card.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Ink, BorderColor = Mint, BorderWidthLeft = 3,
            ContentMarginLeft = 13, ContentMarginRight = 13, ContentMarginTop = 7, ContentMarginBottom = 8,
            CornerRadiusTopRight = 5, CornerRadiusBottomRight = 5,
        });
        AddChild(card);
        return card;
    }

    private static VBoxContainer Column(Control parent)
    {
        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 4);
        parent.AddChild(column);
        return column;
    }

    private static Label Line(Control parent, string name, string text, int size, Color color)
    {
        var label = new Label { Name = name, Text = text, MouseFilter = MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        parent.AddChild(label);
        return label;
    }

    private static Button ResultButton(Control parent, string name, string text, Action action)
    {
        var button = new Button { Name = name, Text = text, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var main = name == "Retry";
        button.AddThemeStyleboxOverride("normal", new StyleBoxFlat { BgColor = main ? Mint : new Color("344b51"),
            ContentMarginTop = 6, ContentMarginBottom = 6 });
        button.AddThemeColorOverride("font_color", main ? Ink : Cream);
        button.AddThemeColorOverride("font_focus_color", main ? Ink : Cream);
        button.Pressed += action;
        parent.AddChild(button);
        return button;
    }
}

/// <summary>Original enamel hen badge for the finish screen.</summary>
internal sealed partial class ParkMedalIcon : Control
{
    private ParkRunMedal _medal;
    public void SetMedal(ParkRunMedal medal)
    {
        if (_medal == medal) return;
        _medal = medal;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var metal = new Color(_medal == ParkRunMedal.Gold ? "ffca61" : _medal == ParkRunMedal.Silver ? "c5d8dc" : "d58c65");
        var dark = new Color("20454c");
        DrawColoredPolygon([new(20, 50), new(40, 55), new(23, 81), new(20, 69), new(11, 70)], new Color("4ab7a0"));
        DrawColoredPolygon([new(40, 55), new(59, 49), new(70, 70), new(59, 69), new(55, 81)], new Color("359487"));
        DrawCircle(new Vector2(40, 34), 29, metal, true, -1, true);
        DrawCircle(new Vector2(40, 34), 24, dark, true, -1, true);
        DrawCircle(new Vector2(43, 29), 9, metal, true, -1, true);
        DrawColoredPolygon([new(22, 31), new(34, 36), new(42, 25), new(48, 32), new(45, 45), new(36, 49), new(27, 45)], metal);
        DrawColoredPolygon([new(49, 26), new(58, 30), new(49, 32)], metal);
        DrawColoredPolygon([new(35, 23), new(35, 15), new(40, 19), new(43, 14), new(46, 20), new(50, 17), new(48, 24)], metal);
        DrawCircle(new Vector2(46, 27), 1.8f, dark, true, -1, true);
    }
}
