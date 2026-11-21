using System.Globalization;
using Gsa.Core.Geodesy;
using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.World;
using Gsa.Engine.Visuals;
using Gsa.Missions.Campaign;
using Gsa.Missions.Contracts;
using Gsa.Missions.Persistence;
using Gsa.Missions.Quality;
using Godot;

namespace Gsa.Engine.World;

public partial class SaoDatumCityScene
{
    private PortViaductReview? _lastViaductReview;
    private VBoxContainer _viaductForm = null!;
    private readonly List<LineEdit> _viaductHeightFields = [];

    private void BuildViaductOfficeForm(VBoxContainer tab)
    {
        _viaductForm = new VBoxContainer { Visible = false };
        tab.AddChild(_viaductForm);
        _viaductForm.AddChild(new Label
        {
            Text = Text("city.viaduct.height_instructions"), AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        for (var i = 0; i < PortViaductSurvey.VerticalControls.Count; i++)
        {
            var p = PortViaductSurvey.VerticalControls[i];
            var row = new HBoxContainer();
            row.AddChild(new Label
            {
                Text = string.Format(Text("city.viaduct.control"), PortViaductQuality.ControlId(i), p.Z, LocalGeoidModel.Separation(p)),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            });
            foreach (var key in new[] { "city.viaduct.h", "city.viaduct.big_h" })
            {
                var field = new LineEdit
                {
                    PlaceholderText = Text(key), TooltipText = Text(key), MaxLength = 16, CustomMinimumSize = new(110, 0),
                    VirtualKeyboardType = LineEdit.VirtualKeyboardTypeEnum.NumberDecimal,
                };
                _viaductHeightFields.Add(field);
                row.AddChild(field);
            }
            _viaductForm.AddChild(row);
        }
    }

    private void RestoreViaductDraft(IReadOnlyList<string>? heights)
    {
        _lastViaductReview = null;
        for (var i = 0; i < _viaductHeightFields.Count; i++)
            _viaductHeightFields[i].Text = heights?[i] ?? "";
    }

    private ViaductControlHeightSubmission[] ReadViaductHeights()
    {
        static bool ReadMetres(string value, out double metres) =>
            double.TryParse(value.Trim().Replace(',', '.'), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out metres) && double.IsFinite(metres);

        var controls = PortViaductSurvey.VerticalControls;
        var submissions = new List<ViaductControlHeightSubmission>(controls.Count);
        for (var i = 0; i < controls.Count; i++)
            if (ReadMetres(_viaductHeightFields[2 * i].Text, out var h) && ReadMetres(_viaductHeightFields[2 * i + 1].Text, out var H))
                submissions.Add(new(PortViaductQuality.ControlId(i), controls[i], h, H));
        return submissions.ToArray();
    }

    private void ProcessViaductDelivery(ContractStateMachine machine, double now)
    {
        var batches = ReadProtectedCityLidarBatches();
        if (batches.Length == 0)
        {
            _feedbackText = Text("city.office.requires_lidar");
            return;
        }

        var slices = batches.SelectMany(batch => batch.LidarSlices!).Where(slice =>
            slice.TunnelRanges?.Corridor == PortViaductSurvey.AreaId && !slice.RackNeedsCalibration);
        var cloud = BuildProtectedCityCloud(slices);
        var review = PortViaductQuality.Evaluate(machine.ContractId, batches.Select(batch => batch.BatchId).ToArray(), cloud, ReadViaductHeights());
        _lastViaductReview = review;
        if (!machine.MarkReadyToProcess(now).Succeeded || !machine.StartProcessing(now).Succeeded ||
            !machine.CompleteProcessing(now).Succeeded ||
            !machine.RecordQualityEvaluation(now, review.Delivery.Passed, primaryFailureKey: review.Delivery.FailureReasonKeys.FirstOrDefault()).Succeeded)
        {
            _feedbackText = Text("city.office.processing_failed");
            return;
        }

        if (review.Delivery.Passed)
            FinishCityDelivery(machine.Snapshot!.Definition, machine, review.Delivery, now);
        else
        {
            _feedbackText = Text(review.Cloud.Passed ? review.Heights.ReasonKey : "city.viaduct.cloud_rework");
            _audio.PlayAlert();
            SaveCityCareerState(SaveTrigger.Manual, announce: false);
        }
    }

    private string ViaductReviewText()
    {
        if (_lastViaductReview is not { } review) return "";
        var cloud = review.Cloud;
        static string Metres(double? value) => value is { } number && double.IsFinite(number) ? number.ToString("F3") : "—";
        var report = string.Format(Text("city.viaduct.report"), cloud.CoveredUsefulCells, cloud.TotalUsefulCells,
            cloud.CoverageFraction, cloud.MeanDensityPointsPerM2, Metres(cloud.WorstPlaneRmseMetres),
            Metres(cloud.RelativeDistanceRmseMetres), Text(review.Heights.ReasonKey));
        if (!cloud.Passed)
        {
            var sections = cloud.Cells.Where(cell => !cell.IsCovered).Select(cell =>
                (int)(cell.Cell.ClippedBounds.MinEast - PortViaductSurvey.WestEast) / 20 * 20).Distinct();
            var gaps = string.Join(", ", sections.Select(section => $"{section}–{section + 20} m"));
            if (gaps.Length > 0) report += "\n" + string.Format(Text("city.viaduct.gaps"), gaps);
        }
        return report;
    }

    private void BuildPortViaduct(Node3D parent)
    {
        var concrete = GsaMeshBuilder.SolidMat(new Color(.43f, .46f, .48f));
        var dark = GsaMeshBuilder.SolidMat(new Color(.21f, .23f, .24f));
        var orange = GsaMeshBuilder.SolidMat(new Color(.98f, .61f, .12f));
        var blue = GsaMeshBuilder.SolidMat(new Color(.21f, .58f, .72f));
        var deck = PortViaductSurvey.DeckCenter;
        var length = PortViaductSurvey.EastEast - PortViaductSurvey.WestEast;

        void Box(string name, Vector3d center, Vector3d extent, Material material, uint layer = 0)
        {
            var size = new Vector3((float)extent.X, (float)extent.Z, (float)extent.Y);
            var body = new StaticBody3D { Name = name, Position = _coordinates.ToGodot(center), CollisionLayer = layer, CollisionMask = 0 };
            body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = material });
            if (layer != 0) body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
            parent.AddChild(body);
        }

        Box("PortViaductDeck", deck + Vector3d.UnitZ * .5, new(length, 10, 1), concrete, 1);
        Box("PortViaductAsphalt", deck + Vector3d.UnitZ * 1.04, new(length, 9, .08), dark);
        foreach (var side in new[] { -1, 1 })
        {
            var offset = Vector3d.UnitY * side;
            Box($"PortViaductParapet{side}", deck + offset * 4.75 + Vector3d.UnitZ * 1.35, new(length, .5, .7), concrete);
            Box($"PortViaductLane{side}", new Vector3d(deck.X, deck.Y, 99.6) + offset * PortViaductSurvey.SurveyHalfWidth,
                new(length + 20, .12, .025), orange);
        }
        foreach (var (edge, index) in new[] { PortViaductSurvey.WestPad, PortViaductSurvey.EastPad }.Select((p, i) => (p, i)))
            Box($"ViaductUpload{index}", new(edge.X, edge.Y, 99.61), new(3, 3.8, .04), blue);
        for (var i = 0; i < PortViaductSurvey.VerticalControls.Count; i++)
        {
            var control = PortViaductSurvey.VerticalControls[i];
            var tag = PortViaductQuality.ControlId(i);
            var height = control.Z - 99.575;
            Box($"PortViaductControl{tag}", control - Vector3d.UnitZ * height / 2, new(.35, .35, height), orange, PortLidarDeckLayer);
            parent.AddChild(new Label3D
            {
                Text = tag, Position = _coordinates.ToGodot(control + Vector3d.UnitZ * .5),
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 48, PixelSize = .012f, VisibilityRangeEnd = 55,
            });

            var panel = PortViaductSurvey.CheckPanels[i];
            var drop = deck.Z - panel.Z;
            Box($"ViaductCheckPanel{i}", panel + Vector3d.UnitZ * drop / 2, new(2, 2, drop), blue, PortLidarDeckLayer);
        }
        foreach (var pier in PortViaductPiers())
            Box(pier.Id, pier.CenterEnu, pier.SizeEnu, concrete, 1);
    }

    private static IEnumerable<ObstacleBoxEnu> PortViaductPiers()
    {
        var deck = PortViaductSurvey.DeckCenter;
        foreach (var (x, index) in new[] { PortViaductSurvey.WestEast + 5, deck.X, PortViaductSurvey.EastEast - 5 }.Select((x, i) => (x, i)))
            foreach (var side in new[] { -1, 1 })
                yield return new($"PortViaductPier{index}_{side}", new(x, deck.Y + side * 4.3, 102.6), new(1.1, 1.1, 6));
    }
}
