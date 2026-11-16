using Gsa.Core.Geometry;
using Gsa.Core.World;
using Godot;

namespace Gsa.Engine.UI;

public readonly record struct RadarBlip(
    string Id,
    Vector3d PositionEnu,
    Color Color,
    string Label,
    bool ClampToRim = true);

/// <summary>Iconic GTA III bottom-left circular radar minimap with heading rotation, orbiting N marker, heatmap, and blips.</summary>
public sealed partial class GsaRadarControl : Control
{
    private WorldSceneManifest? _manifest;
    private Vector3d _playerPosEnu;
    private double _headingRadians = Math.PI / 2.0;
    private IReadOnlyList<RadarBlip> _blips = [];
    private IReadOnlyList<Vector3d> _coverageSpots = [];
    private float _rangeMetres = 95f;

    public void Configure(WorldSceneManifest manifest, float rangeMetres = 95f)
    {
        _manifest = manifest;
        _rangeMetres = rangeMetres;
        QueueRedraw();
    }

    public void UpdateTelemetry(
        Vector3d playerPosEnu,
        double headingRadians,
        IReadOnlyList<RadarBlip> blips,
        IReadOnlyList<Vector3d> coverageSpots)
    {
        _playerPosEnu = playerPosEnu;
        _headingRadians = headingRadians;
        _blips = blips;
        _coverageSpots = coverageSpots;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var center = Size * 0.5f;
        var radius = MathF.Min(Size.X, Size.Y) * 0.5f - 8f;
        if (radius <= 12f)
            return;

        // Outer drop shadow + gunmetal bezel + dark map disc
        DrawCircle(center + new Vector2(2f, 3f), radius + 5f, new Color(0f, 0f, 0f, 0.65f));
        DrawCircle(center, radius + 4.5f, new Color(0.20f, 0.23f, 0.27f, 0.98f));
        DrawCircle(center, radius, new Color(0.08f, 0.13f, 0.14f, 0.96f));

        // Rotate relative to player heading so UP on radar = player forward!
        var scale = radius / _rangeMetres;
        var cosH = (float)Math.Cos(_headingRadians);
        var sinH = (float)Math.Sin(_headingRadians);

        Vector2 EnuToRadarOffset(Vector3d enu)
        {
            var dE = (float)(enu.X - _playerPosEnu.X);
            var dN = (float)(enu.Y - _playerPosEnu.Y);
            // Forward dot product -> -Y screen, Right dot product -> +X screen
            var fwd = dE * cosH + dN * sinH;
            var right = dE * sinH - dN * cosH;
            return new Vector2(right, -fwd) * scale;
        }

        var clipCircle = new Vector2[24];
        for (var i = 0; i < clipCircle.Length; i++)
        {
            var ang = i * (MathF.Tau / clipCircle.Length);
            clipCircle[i] = center + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * (radius - 2f);
        }

        void DrawClippedRect(double centerE, double centerN, double widthE, double heightN, Color color)
        {
            var off = EnuToRadarOffset(new Vector3d(centerE, centerN, 100.0));
            var halfW = (float)widthE * 0.5f * scale;
            var halfH = (float)heightN * 0.5f * scale;
            if (off.Length() > radius + halfW + halfH || halfW < 0.6f || halfH < 0.6f)
                return;

            var axisE = new Vector2(sinH, -cosH) * halfW;
            var axisN = new Vector2(-cosH, -sinH) * halfH;
            Vector2[] rawPoly =
            [
                center + off - axisE - axisN,
                center + off + axisE - axisN,
                center + off + axisE + axisN,
                center + off - axisE + axisN,
            ];
            foreach (var poly in Geometry2D.IntersectPolygons(rawPoly, clipCircle))
            {
                if (poly.Length >= 3 && ComputePolygonArea(poly) > 1.5f)
                    DrawColoredPolygon(poly, color);
            }
        }

        // District land/water/park backing inside circular radar
        DrawClippedRect(502250, 7400500, 950, 1400, new Color(0.08f, 0.24f, 0.42f, 0.88f)); // Ocean Bay
        DrawClippedRect(501500, 7401500, 1000, 1000, new Color(0.24f, 0.21f, 0.16f, 0.72f)); // Serra hills
        DrawClippedRect(500350, 7400380, 130, 130, new Color(0.15f, 0.36f, 0.18f, 0.85f)); // Central Park

        // Road grid corridors (Commerce Blvd + North Ave + full city arterial avenues)
        var roadCol = new Color(0.22f, 0.25f, 0.28f, 0.92f);
        var centerLineCol = new Color(0.55f, 0.48f, 0.22f, 0.50f);
        DrawClippedRect(500120, 7399989.0, 244, 15.0, roadCol);
        DrawClippedRect(500120, 7400047.0, 240, 8.5, roadCol);
        DrawClippedRect(500112, 7400020.0, 10.0, 76.0, roadCol);
        if (_rangeMetres > 105f)
        {
            for (var n = 7400000.0; n <= 7401800.0; n += 120.0)
            {
                DrawClippedRect(500950, n - 4.0, 1900, 14.0, roadCol);
                DrawClippedRect(500950, n - 4.0, 1900, 1.5, centerLineCol);
            }
            for (var e = 500000.0; e <= 501800.0; e += 120.0)
            {
                DrawClippedRect(e, 7400600, 14.0, 1200, roadCol);
                DrawClippedRect(e, 7400600, 1.5, 1200, centerLineCol);
            }
        }

        // Subtle range ring
        DrawArc(center, radius * 0.5f, 0f, Mathf.Tau, 32, new Color(0.28f, 0.38f, 0.42f, 0.25f), 1.0f);

        if (_manifest is not null)
        {
            // Draw nearby buildings & parks as rotated mini-rectangles clipped cleanly against the radar circle
            foreach (var obj in _manifest.Objects)
            {
                if (obj.ObjectClass is not ("building_shell" or "warehouse_facade" or "commercial_tower" or "training_park"))
                    continue;
                var col = obj.ObjectClass == "training_park"
                    ? new Color(0.16f, 0.36f, 0.18f, 0.72f)
                    : new Color(0.38f, 0.44f, 0.49f, 0.90f);
                DrawClippedRect(obj.PositionEnu.X, obj.PositionEnu.Y, obj.SizeEnu.X, obj.SizeEnu.Y, col);
            }
        }

        // Coverage heatmap cells collected by player
        foreach (var spot in _coverageSpots)
        {
            var off = EnuToRadarOffset(spot);
            if (off.Length() <= radius - 5f)
                DrawCircle(center + off, MathF.Max(4.5f, 3.5f * scale), new Color(0.22f, 0.86f, 0.48f, 0.32f));
        }

        var font = ThemeDB.FallbackFont;
        // Blips (targets, office, unload, controls)
        foreach (var blip in _blips)
        {
            var off = EnuToRadarOffset(blip.PositionEnu);
            var dist = off.Length();
            if (dist > radius - 6f)
            {
                if (!blip.ClampToRim)
                    continue;
                off = off.Normalized() * (radius - 6f);
            }
            var p = center + off;
            DrawCircle(p, 5.2f, Colors.Black);
            DrawCircle(p, 4.0f, blip.Color);
            if (!string.IsNullOrEmpty(blip.Label))
                DrawString(font, p + new Vector2(-4f, -7f), blip.Label, fontSize: 10, modulate: blip.Color);
        }

        // Player arrow centered pointing straight UP
        DrawColoredPolygon(
        [
            center + new Vector2(0f, -8f),
            center + new Vector2(5.5f, 6f),
            center + new Vector2(0f, 3f),
            center + new Vector2(-5.5f, 6f),
        ], Colors.White);

        // Orbiting North indicator around outer rim (classic GTA III signature!)
        var northOff = new Vector2(-cosH, -sinH) * (radius + 2f);
        var nPos = center + northOff;
        DrawCircle(nPos, 8.5f, new Color(0.08f, 0.09f, 0.11f, 0.96f));
        DrawArc(nPos, 8.5f, 0f, Mathf.Tau, 16, new Color(0.88f, 0.76f, 0.28f), 1.5f);
        DrawString(font, nPos + new Vector2(-4f, 4f), "N", fontSize: 11, modulate: new Color(1.0f, 0.85f, 0.30f));

        // Crisp outer rim ring
        DrawArc(center, radius, 0f, Mathf.Tau, 48, new Color(0.68f, 0.74f, 0.78f, 0.75f), 2.0f);
    }

    private static float ComputePolygonArea(Vector2[] poly)
    {
        var area = 0f;
        for (var i = 0; i < poly.Length; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % poly.Length];
            area += a.X * b.Y - b.X * a.Y;
        }
        return MathF.Abs(area) * 0.5f;
    }
}

