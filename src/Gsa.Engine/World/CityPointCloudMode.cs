using Gsa.Core.Geometry;
using Gsa.Core.World;
using Godot;

namespace Gsa.Engine.World;

public partial class SaoDatumCityScene
{
    private const double CloudViewRadius = 100;
    private const double CloudViewSpacing = 2.5;
    private const int CloudViewPointLimit = 18_000;
    private Node3D _cityBaseVisuals = null!;
    private MeshInstance3D? _cityPointView;
    private CheckButton _cityPointViewButton = null!;
    private Vector3d? _cityPointViewAnchor;
    private bool _cityPointViewEnabled;

    private bool FinalCloudFilterUnlocked() => _campaignState.UnlockedEquipment.Contains(FinalCityPavementSurvey.CloudFilterUnlock)
        || CityMissionCompleted(FinalCityPavementSurvey.MissionId);

    private void SetCityPointView(bool enabled)
    {
        _cityPointViewEnabled = enabled && FinalCloudFilterUnlocked();
        _cityPointViewAnchor = null;
        _worldRoot.Visible = _streamedBlocksRoot.Visible = _cityBaseVisuals.Visible = !_cityPointViewEnabled;
        if (_cityPointView is not null) _cityPointView.Visible = false;
        _lidarCloudMesh.Visible = !_cityPointViewEnabled && _showCloudButton.ButtonPressed;
    }

    private void StepCityPointView(Vector3d position)
    {
        if (!_cityPointViewEnabled ||
            _cityPointViewAnchor is { } anchor && (position - anchor).Length < 30)
            return;
        _cityPointViewAnchor = position;
        if (_cityPointView is null)
        {
            _cityPointView = new MeshInstance3D
            {
                Name = "CityPointCloudFilter",
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                MaterialOverride = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    VertexColorUseAsAlbedo = true,
                    UsePointSize = true,
                    PointSize = 3,
                },
            };
            AddChild(_cityPointView);
        }

        using var points = new SurfaceTool();
        points.Begin(Mesh.PrimitiveType.Points);
        var count = 0;
        void AddPoint(double x, double y, double z, Color color)
        {
            if (count >= CloudViewPointLimit) return;
            points.SetColor(color);
            points.AddVertex(_coordinates.ToGodot(new(x, y, z)));
            count++;
        }

        // This point view samples streamed scene proxies. It never reads scanner or custody data.
        var bounds = _fullManifest.Bounds;
        var west = Math.Max(bounds.Min.X, CloudViewSpacing * Math.Floor((position.X - CloudViewRadius) / CloudViewSpacing));
        var south = Math.Max(bounds.Min.Y, CloudViewSpacing * Math.Floor((position.Y - CloudViewRadius) / CloudViewSpacing));
        var east = Math.Min(bounds.Max.X, position.X + CloudViewRadius);
        var north = Math.Min(bounds.Max.Y, position.Y + CloudViewRadius);
        for (var x = west; x <= east; x += CloudViewSpacing)
            for (var y = south; y <= north; y += CloudViewSpacing)
                AddPoint(x, y, GetTerrainElevationEnu(x, y), new Color(.32f, .68f, .80f));

        var nearby = _cityBlockObstacles.Where(box => box.MaxX >= west && box.MinX <= east &&
            box.MaxY >= south && box.MinY <= north && box.SizeEnu.Z >= 1)
            .OrderBy(box => (box.CenterEnu - position).Length);
        foreach (var box in nearby)
        {
            if (count >= CloudViewPointLimit) break;
            var minX = Math.Max(west, box.MinX);
            var maxX = Math.Min(east, box.MaxX);
            var minY = Math.Max(south, box.MinY);
            var maxY = Math.Min(north, box.MaxY);
            var bottom = box.MinZ;
            var top = box.MaxZ;
            var roof = new Color(.77f, .93f, 1f);
            var wall = new Color(.40f, .82f, .91f);
            for (var x = minX; x <= maxX; x += CloudViewSpacing)
            {
                for (var y = minY; y <= maxY; y += CloudViewSpacing)
                    AddPoint(x, y, top, roof);
                for (var z = bottom; z < top; z += CloudViewSpacing)
                {
                    if (minY == box.MinY) AddPoint(x, minY, z, wall);
                    if (maxY == box.MaxY) AddPoint(x, maxY, z, wall);
                }
            }
            for (var y = minY; y <= maxY; y += CloudViewSpacing)
                for (var z = bottom; z < top; z += CloudViewSpacing)
                {
                    if (minX == box.MinX) AddPoint(minX, y, z, wall);
                    if (maxX == box.MaxX) AddPoint(maxX, y, z, wall);
                }
        }
        _cityPointView.Mesh = count > 0 ? points.Commit() : null;
        _cityPointView.Visible = count > 0;
    }
}
