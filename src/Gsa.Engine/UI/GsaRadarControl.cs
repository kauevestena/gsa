using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Traffic;
using Gsa.Core.World;
using Godot;

namespace Gsa.Engine.UI;

public readonly record struct RadarBlip(
    string Id,
    Vector3d PositionEnu,
    Color Color,
    string Label,
    bool ClampToRim = true);

/// <summary>Iconic GTA III bottom-left circular radar minimap with heading rotation, orbiting N marker, heatmap, and zero-alloc clipping.</summary>
public sealed partial class GsaRadarControl : Control
{
    private readonly record struct StaticRadarRect(
        double CenterE,
        double CenterN,
        double HalfW,
        double HalfH,
        double MaxHalfExtent,
        Color Color);

    private const int ClipSegments = 20;
    private readonly Vector2[] _clipUnit = new Vector2[ClipSegments];
    private readonly Vector2[] _clipScaled = new Vector2[ClipSegments];
    private readonly Vector2[] _polyBufA = new Vector2[ClipSegments + 8];
    private readonly Vector2[] _polyBufB = new Vector2[ClipSegments + 8];
    private readonly Vector2[] _playerArrow = new Vector2[4];
    private readonly List<StaticRadarRect> _staticRects = [];

    private Vector3d _playerPosEnu;
    private double _headingRadians = Math.PI / 2.0;
    private IReadOnlyList<RadarBlip> _blips = [];
    private IReadOnlyList<Vector3d> _coverageSpots = [];
    private IReadOnlyList<ObstacleBoxEnu>? _dynamicObstacles;
    private IReadOnlyList<TrafficVisualAgent>? _trafficAgents;
    private float _rangeMetres = 95f;

    public void UpdateDynamicWorld(
        IReadOnlyList<ObstacleBoxEnu>? dynamicObstacles,
        IReadOnlyList<TrafficVisualAgent>? trafficAgents)
    {
        _dynamicObstacles = dynamicObstacles;
        _trafficAgents = trafficAgents;
    }

    public GsaRadarControl()
    {
        for (var i = 0; i < ClipSegments; i++)
        {
            var ang = i * (MathF.Tau / ClipSegments);
            _clipUnit[i] = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
        }
    }

    public void Configure(WorldSceneManifest manifest, float rangeMetres = 95f)
    {
        _rangeMetres = rangeMetres;
        _staticRects.Clear();
        if (manifest is not null)
        {
            foreach (var obj in manifest.Objects)
            {
                if (obj.ObjectClass is not ("building_shell" or "warehouse_facade" or "commercial_tower" or "training_park"))
                    continue;
                var hw = obj.SizeEnu.X * 0.5;
                var hh = obj.SizeEnu.Y * 0.5;
                var col = obj.ObjectClass == "training_park"
                    ? new Color(0.16f, 0.36f, 0.18f, 0.72f)
                    : new Color(0.38f, 0.44f, 0.49f, 0.90f);
                _staticRects.Add(new StaticRadarRect(
                    obj.PositionEnu.X,
                    obj.PositionEnu.Y,
                    hw,
                    hh,
                    Math.Sqrt(hw * hw + hh * hh),
                    col));
            }
        }
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

        var scale = radius / _rangeMetres;
        var cosH = (float)Math.Cos(_headingRadians);
        var sinH = (float)Math.Sin(_headingRadians);
        var clipRadius = radius - 2f;
        var clipRadiusSq = clipRadius * clipRadius;

        for (var i = 0; i < ClipSegments; i++)
            _clipScaled[i] = _clipUnit[i] * clipRadius;

        Vector2 EnuToRadarOffset(double enuX, double enuY)
        {
            var dE = (float)(enuX - _playerPosEnu.X);
            var dN = (float)(enuY - _playerPosEnu.Y);
            var fwd = dE * cosH + dN * sinH;
            var right = dE * sinH - dN * cosH;
            return new Vector2(right, -fwd) * scale;
        }

        void DrawClippedRect(double centerE, double centerN, double widthE, double heightN, Color color)
        {
            var halfW = (float)widthE * 0.5f * scale;
            var halfH = (float)heightN * 0.5f * scale;
            if (halfW < 0.6f || halfH < 0.6f)
                return;

            var dE = (float)(centerE - _playerPosEnu.X);
            var dN = (float)(centerN - _playerPosEnu.Y);
            var maxExtentWorld = (float)(Math.Sqrt(widthE * widthE + heightN * heightN) * 0.5) + _rangeMetres;
            if (MathF.Abs(dE) > maxExtentWorld || MathF.Abs(dN) > maxExtentWorld)
                return;

            var off = new Vector2(dE * sinH - dN * cosH, -(dE * cosH + dN * sinH)) * scale;
            var boundR = MathF.Sqrt(halfW * halfW + halfH * halfH);
            if (off.Length() > clipRadius + boundR)
                return;

            var axisE = new Vector2(sinH, -cosH) * halfW;
            var axisN = new Vector2(-cosH, -sinH) * halfH;
            var p0 = off - axisE - axisN;
            var p1 = off + axisE - axisN;
            var p2 = off + axisE + axisN;
            var p3 = off - axisE + axisN;

            if (p0.LengthSquared() <= clipRadiusSq &&
                p1.LengthSquared() <= clipRadiusSq &&
                p2.LengthSquared() <= clipRadiusSq &&
                p3.LengthSquared() <= clipRadiusSq)
            {
                _polyBufA[0] = center + p0;
                _polyBufA[1] = center + p1;
                _polyBufA[2] = center + p2;
                _polyBufA[3] = center + p3;
                DrawColoredPolygon(new ReadOnlySpan<Vector2>(_polyBufA, 0, 4), color, default(ReadOnlySpan<Vector2>), null);
                return;
            }

            _polyBufA[0] = p0;
            _polyBufA[1] = p1;
            _polyBufA[2] = p2;
            _polyBufA[3] = p3;
            var count = ClipConvexToCircle(_polyBufA, 4, _polyBufB, _clipScaled);
            if (count < 3)
                return;

            for (var i = 0; i < count; i++)
                _polyBufA[i] += center;
            if (ComputePolygonArea(_polyBufA.AsSpan(0, count)) > 1.5f)
                DrawColoredPolygon(new ReadOnlySpan<Vector2>(_polyBufA, 0, count), color, default(ReadOnlySpan<Vector2>), null);
        }

        // District land/water/park backing inside circular radar
        DrawClippedRect(502250, 7400500, 950, 1400, new Color(0.08f, 0.24f, 0.42f, 0.88f));
        DrawClippedRect(501500, 7401500, 1000, 1000, new Color(0.24f, 0.21f, 0.16f, 0.72f));
        DrawClippedRect(500350, 7400380, 130, 130, new Color(0.15f, 0.36f, 0.18f, 0.85f));

        // Road grid corridors
        var roadCol = new Color(0.22f, 0.25f, 0.28f, 0.92f);
        var centerLineCol = new Color(0.55f, 0.48f, 0.22f, 0.50f);
        DrawClippedRect(500120, 7399989.0, 244, 15.0, roadCol);
        DrawClippedRect(500120, 7400047.0, 240, 8.5, roadCol);
        DrawClippedRect(500112, 7400020.0, 10.0, 76.0, roadCol);
        if (_rangeMetres > 105f)
        {
            var minN = Math.Max(7400000.0, Math.Floor((_playerPosEnu.Y - _rangeMetres - 20.0) / 120.0) * 120.0);
            var maxN = Math.Min(7401800.0, Math.Ceiling((_playerPosEnu.Y + _rangeMetres + 20.0) / 120.0) * 120.0);
            for (var n = minN; n <= maxN; n += 120.0)
            {
                DrawClippedRect(500950, n - 4.0, 1900, 14.0, roadCol);
                DrawClippedRect(500950, n - 4.0, 1900, 1.5, centerLineCol);
            }
            var minE = Math.Max(500000.0, Math.Floor((_playerPosEnu.X - _rangeMetres - 20.0) / 120.0) * 120.0);
            var maxE = Math.Min(501800.0, Math.Ceiling((_playerPosEnu.X + _rangeMetres + 20.0) / 120.0) * 120.0);
            for (var e = minE; e <= maxE; e += 120.0)
            {
                DrawClippedRect(e, 7400600, 14.0, 1200, roadCol);
                DrawClippedRect(e, 7400600, 1.5, 1200, centerLineCol);
            }
        }

        DrawArc(center, radius * 0.5f, 0f, Mathf.Tau, 28, new Color(0.28f, 0.38f, 0.42f, 0.25f), 1.0f);

        // Nearby cached buildings & parks (only test items near radar range!)
        var rangePlusPad = (double)_rangeMetres + 40.0;
        for (var i = 0; i < _staticRects.Count; i++)
        {
            var rect = _staticRects[i];
            if (Math.Abs(rect.CenterE - _playerPosEnu.X) > rangePlusPad + rect.MaxHalfExtent ||
                Math.Abs(rect.CenterN - _playerPosEnu.Y) > rangePlusPad + rect.MaxHalfExtent)
                continue;
            DrawClippedRect(rect.CenterE, rect.CenterN, rect.HalfW * 2.0, rect.HalfH * 2.0, rect.Color);
        }

        if (_dynamicObstacles is { Count: > 0 } dynObs)
        {
            var dynBldgCol = new Color(0.34f, 0.40f, 0.45f, 0.85f);
            for (var i = 0; i < dynObs.Count; i++)
            {
                var box = dynObs[i];
                if (!box.Collision || box.SizeEnu.X < 4.0 || box.SizeEnu.Z < 3.0)
                    continue;
                if (Math.Abs(box.CenterEnu.X - _playerPosEnu.X) > rangePlusPad + box.SizeEnu.X * 0.5 ||
                    Math.Abs(box.CenterEnu.Y - _playerPosEnu.Y) > rangePlusPad + box.SizeEnu.Y * 0.5)
                    continue;
                DrawClippedRect(box.CenterEnu.X, box.CenterEnu.Y, box.SizeEnu.X, box.SizeEnu.Y, dynBldgCol);
            }
        }

        if (_trafficAgents is { Count: > 0 } agents)
        {
            var carCol = new Color(0.95f, 0.72f, 0.22f, 0.88f);
            var erraticCol = new Color(1.0f, 0.28f, 0.22f, 0.95f);
            var pedCol = new Color(0.45f, 0.90f, 0.96f, 0.80f);
            var innerLimitSq = (radius - 4f) * (radius - 4f);
            for (var i = 0; i < agents.Count; i++)
            {
                var a = agents[i];
                var off = EnuToRadarOffset(a.PositionEnu.X, a.PositionEnu.Y);
                if (off.LengthSquared() > innerLimitSq)
                    continue;
                DrawCircle(center + off, a.IsVehicle ? 2.5f : 1.5f, a.IsVehicle ? (a.IsErratic ? erraticCol : carCol) : pedCol);
            }
        }

        // Coverage heatmap cells collected by player
        var spotRadius = MathF.Max(4.5f, 3.5f * scale);
        var spotCol = new Color(0.22f, 0.86f, 0.48f, 0.32f);
        for (var i = 0; i < _coverageSpots.Count; i++)
        {
            var spot = _coverageSpots[i];
            var off = EnuToRadarOffset(spot.X, spot.Y);
            if (off.LengthSquared() <= (radius - 5f) * (radius - 5f))
                DrawCircle(center + off, spotRadius, spotCol);
        }

        var font = ThemeDB.FallbackFont;
        var rimDist = radius - 6f;
        for (var i = 0; i < _blips.Count; i++)
        {
            var blip = _blips[i];
            var off = EnuToRadarOffset(blip.PositionEnu.X, blip.PositionEnu.Y);
            var dist = off.Length();
            if (dist > rimDist)
            {
                if (!blip.ClampToRim)
                    continue;
                off = off * (rimDist / dist);
            }
            var p = center + off;
            DrawCircle(p, 5.2f, Colors.Black);
            DrawCircle(p, 4.0f, blip.Color);
            if (!string.IsNullOrEmpty(blip.Label))
                DrawString(font, p + new Vector2(-4f, -7f), blip.Label, fontSize: 10, modulate: blip.Color);
        }

        // Player arrow centered pointing straight UP
        _playerArrow[0] = center + new Vector2(0f, -8f);
        _playerArrow[1] = center + new Vector2(5.5f, 6f);
        _playerArrow[2] = center + new Vector2(0f, 3f);
        _playerArrow[3] = center + new Vector2(-5.5f, 6f);
        DrawColoredPolygon(_playerArrow, Colors.White);

        // Orbiting North indicator around outer rim
        var northOff = new Vector2(-cosH, -sinH) * (radius + 2f);
        var nPos = center + northOff;
        DrawCircle(nPos, 8.5f, new Color(0.08f, 0.09f, 0.11f, 0.96f));
        DrawArc(nPos, 8.5f, 0f, Mathf.Tau, 16, new Color(0.88f, 0.76f, 0.28f), 1.5f);
        DrawString(font, nPos + new Vector2(-4f, 4f), "N", fontSize: 11, modulate: new Color(1.0f, 0.85f, 0.30f));

        DrawArc(center, radius, 0f, Mathf.Tau, 40, new Color(0.68f, 0.74f, 0.78f, 0.75f), 2.0f);
    }

    private static int ClipConvexToCircle(Vector2[] poly, int count, Vector2[] scratch, Vector2[] clipPoly)
    {
        for (var edge = 0; edge < clipPoly.Length; edge++)
        {
            if (count == 0)
                return 0;

            var a = clipPoly[edge];
            var b = clipPoly[(edge + 1) % clipPoly.Length];
            var abX = b.X - a.X;
            var abY = b.Y - a.Y;

            var outCount = 0;
            var prev = poly[count - 1];
            var prevCross = abX * (prev.Y - a.Y) - abY * (prev.X - a.X);

            for (var i = 0; i < count; i++)
            {
                var curr = poly[i];
                var currCross = abX * (curr.Y - a.Y) - abY * (curr.X - a.X);

                if (currCross >= 0f)
                {
                    if (prevCross < 0f && outCount < scratch.Length)
                    {
                        var t = prevCross / (prevCross - currCross);
                        scratch[outCount++] = prev + (curr - prev) * t;
                    }
                    if (outCount < scratch.Length)
                        scratch[outCount++] = curr;
                }
                else if (prevCross >= 0f && outCount < scratch.Length)
                {
                    var t = prevCross / (prevCross - currCross);
                    scratch[outCount++] = prev + (curr - prev) * t;
                }

                prev = curr;
                prevCross = currCross;
            }

            count = outCount;
            for (var i = 0; i < count; i++)
                poly[i] = scratch[i];
        }
        return count;
    }

    private static float ComputePolygonArea(ReadOnlySpan<Vector2> poly)
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
