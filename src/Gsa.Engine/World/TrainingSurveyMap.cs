using Gsa.Core.Geometry;
using Gsa.Core.World;
using Gsa.Missions.Gameplay;
using Godot;

namespace Gsa.Engine.World;

/// <summary>North-up map of the known training block. Rendered geometry is never used for reconstruction.</summary>
public partial class TrainingSurveyMap : Control
{
    private WorldSceneManifest _world = null!;
    private Vector3d _player;
    private double _heading;
    private IReadOnlySet<string> _resolved = new HashSet<string>();
    private Vector3d? _selectedPhoto;
    private double PixelsPerMetre => Math.Max(0.01, Math.Min((Size.X - 48) / 240.0, (Size.Y - 90) / 60.0));

    public void Configure(WorldSceneManifest world) { _world = world; QueueRedraw(); }
    public void UpdateSurvey(Vector3d player, double heading, IReadOnlySet<string> resolved, Vector3d? selectedPhoto)
    {
        _player = player; _heading = heading; _resolved = resolved; _selectedPhoto = selectedPhoto; QueueRedraw();
    }
    public Vector2 Project(Vector3d enu) => new(24 + (float)((enu.X - 500000) * PixelsPerMetre),
        40 + (float)((7400060 - enu.Y) * PixelsPerMetre));

    public override void _Notification(int what) { if (what == NotificationResized) QueueRedraw(); }
    public override void _Draw()
    {
        if (_world is null) return;
        var font = ThemeDB.FallbackFont;
        var bounds = new Rect2(Project(new Vector3d(500000, 7400060, 100)), new Vector2((float)(240 * PixelsPerMetre), (float)(60 * PixelsPerMetre)));
        DrawRect(bounds, new Color(0.075f, 0.12f, 0.15f));
        foreach (var item in _world.Objects.Where(o => o.ObjectClass == "building_facade"))
        {
            var p = Project(item.PositionEnu);
            DrawRect(new Rect2(p - new Vector2((float)(item.SizeEnu.X * PixelsPerMetre / 2), 2),
                new Vector2((float)(item.SizeEnu.X * PixelsPerMetre), 4)), new Color(0.38f, 0.46f, 0.5f));
        }
        foreach (var id in TrainingContractSession.TargetIds)
        {
            var item = _world.Objects.Single(o => o.Id == id);
            var p = Project(item.PositionEnu);
            var resolved = _resolved.Contains(id);
            DrawCircle(p, 5, resolved ? new Color(0.35f, 0.9f, 0.65f) : new Color(1, 0.75f, 0.25f));
            DrawString(font, p + new Vector2(-8, -10), id[7..] + (resolved ? "✓" : "?"), fontSize: 12);
        }
        foreach (var id in new[] { "office_entry", "unload_point" })
        {
            var p = Project(_world.Objects.Single(o => o.Id == id).PositionEnu);
            DrawRect(new Rect2(p - Vector2.One * 4, Vector2.One * 8), Colors.LightSkyBlue, filled: false, width: 2);
            DrawString(font, p + new Vector2(-10, id == "office_entry" ? -28 : 22), id == "office_entry" ? "O" : "D", fontSize: 12);
        }
        if (_selectedPhoto is { } photo)
        {
            var p = Project(photo);
            DrawCircle(p, 8, Colors.Orchid, filled: false, width: 2);
        }
        var here = Project(_player);
        var forward = new Vector2((float)Math.Cos(_heading), -(float)Math.Sin(_heading));
        var side = new Vector2(-forward.Y, forward.X);
        DrawColoredPolygon([here + forward * 10, here - forward * 6 + side * 5, here - forward * 6 - side * 5], Colors.White);
        DrawString(font, new Vector2(24, 16), "N ↑", fontSize: 14);
        var bar = new Vector2(24, bounds.End.Y + 34);
        DrawLine(bar, bar + Vector2.Right * (float)(20 * PixelsPerMetre), Colors.White, 2);
        DrawLine(bar + Vector2.Up * 4, bar + Vector2.Down * 4, Colors.White, 2);
        DrawLine(bar + Vector2.Right * (float)(20 * PixelsPerMetre) + Vector2.Up * 4,
            bar + Vector2.Right * (float)(20 * PixelsPerMetre) + Vector2.Down * 4, Colors.White, 2);
        DrawString(font, bar + new Vector2(0, 18), "0", fontSize: 12);
        DrawString(font, bar + new Vector2((float)(20 * PixelsPerMetre) - 12, 18), "20 m", fontSize: 12);
    }
}
