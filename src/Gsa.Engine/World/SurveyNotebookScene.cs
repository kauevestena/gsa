using Gsa.Core.Sensors.Camera;
using Gsa.Missions.Presentation;
using Godot;

namespace Gsa.Engine.World;

public partial class TutorialBlockScene
{
    private PanelContainer _notebookPanel = null!;
    private TabContainer _notebookTabs = null!;
    private TrainingSurveyMap _surveyMap = null!;
    private Label _notebookTargets = null!;
    private Label _notebookQuality = null!;
    private Label _photoDetails = null!;
    private ItemList _photoList = null!;
    private TextureRect _photoPreview = null!;
    private SurveyNotebookView? _notebookView;
    private string? _selectedPhotoId;
    private string? _selectedThumbnailHash;
    private string _photoListVersion = "";
    private ImageTexture? _reviewTexture;

    private static Label NotebookLabel(string text = "") => new()
    {
        Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart,
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
    };

    private void BuildSurveyNotebook(CanvasLayer canvas)
    {
        _notebookPanel = new PanelContainer
        {
            Name = "SurveyNotebook",
            AnchorLeft = 0.14f,
            AnchorTop = 0.08f,
            AnchorRight = 0.86f,
            AnchorBottom = 0.86f,
            Visible = false,
        };
        _notebookPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.04f, 0.06f, 0.08f, 0.96f),
            BorderColor = new Color(0.32f, 0.44f, 0.50f, 0.95f),
            BorderWidthBottom = 2,
            BorderWidthLeft = 2,
            BorderWidthRight = 2,
            BorderWidthTop = 2,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
            ContentMarginTop = 10,
            ContentMarginBottom = 10,
        });
        canvas.AddChild(_notebookPanel);
        var layout = new VBoxContainer();
        _notebookPanel.AddChild(layout);
        var header = new HBoxContainer();
        layout.AddChild(header);
        header.AddChild(NotebookLabel(Text("notebook.title")));
        var close = new Button { Text = Text("notebook.close"), FocusMode = Control.FocusModeEnum.None };
        close.Pressed += () => _notebookPanel.Hide();
        header.AddChild(close);
        _notebookTabs = new TabContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        layout.AddChild(_notebookTabs);
        VBoxContainer Tab(string id)
        {
            var scroll = new ScrollContainer { Name = id, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            _notebookTabs.AddChild(scroll);
            _notebookTabs.SetTabTitle(_notebookTabs.GetTabCount() - 1, Text("notebook.tab." + id));
            var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            box.AddThemeConstantOverride("separation", 10);
            scroll.AddChild(box);
            return box;
        }
        var mapTab = Tab("map");
        _surveyMap = new TrainingSurveyMap { CustomMinimumSize = new Vector2(0, 250), MouseFilter = Control.MouseFilterEnum.Ignore };
        _surveyMap.Configure(_manifest);
        mapTab.AddChild(_surveyMap);
        mapTab.AddChild(NotebookLabel(Text("notebook.map_legend")));
        var targetTab = Tab("targets");
        targetTab.AddChild(NotebookLabel(Text("notebook.field_progress")));
        _notebookTargets = NotebookLabel();
        targetTab.AddChild(_notebookTargets);
        var photoTab = Tab("photos");
        photoTab.AddChild(NotebookLabel(Text("notebook.synthetic")));
        var photoRow = new HBoxContainer();
        photoTab.AddChild(photoRow);
        _photoList = new ItemList { CustomMinimumSize = new Vector2(160, 160), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _photoList.ItemSelected += SelectNotebookPhoto;
        photoRow.AddChild(_photoList);
        _photoPreview = new TextureRect
        {
            CustomMinimumSize = new Vector2(256, 144), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        photoRow.AddChild(_photoPreview);
        _photoDetails = NotebookLabel();
        photoTab.AddChild(_photoDetails);
        var qualityTab = Tab("quality");
        qualityTab.AddChild(NotebookLabel(Text("notebook.processed_only")));
        _notebookQuality = NotebookLabel();
        qualityTab.AddChild(_notebookQuality);
        var guideTab = Tab("guide");
        guideTab.AddChild(NotebookLabel(Text("notebook.guide")));
    }

    private string Number(double value, int decimals = 1) => PresentationNumberFormatter.Format(
        value, TranslationServer.GetLocale().ToString(), decimals, false);

    private void RefreshSurveyNotebook()
    {
        if (_notebookPanel is null || !_notebookPanel.Visible) return;
        _notebookView = SurveyNotebookPresenter.Build(_training, TranslationServer.GetLocale().ToString());
        _notebookTargets.Text = string.Join("\n\n", _notebookView.Targets.Select(t => string.Format(Text("notebook.target_row"),
            t.TargetId[7..], t.UsableObservations, t.Observations, Number(t.ParallaxDegrees),
            Number(FeatureInventoryTriangulator.MinimumParallaxDegrees), Text(t.ExplanationKey))));
        var version = string.Join("|", _notebookView.Photos.Select(p => p.Sample.SampleId + p.Protected));
        if (version != _photoListVersion)
        {
            _photoListVersion = version;
            _photoList.Clear();
            for (var i = 0; i < _notebookView.Photos.Count; i++)
            {
                var photo = _notebookView.Photos[i];
                _photoList.AddItem($"{i + 1:000} · " + Text(photo.Protected ? "notebook.protected" : "notebook.field"));
            }
        }
        if (_notebookView.Photos.Count > 0)
        {
            var index = _notebookView.Photos.ToList().FindIndex(p => p.Sample.SampleId == _selectedPhotoId);
            if (index < 0) index = _notebookView.Photos.Count - 1;
            _photoList.Select(index);
            SelectNotebookPhoto(index);
        }
        else
        {
            _selectedPhotoId = null; _selectedThumbnailHash = null;
            _photoPreview.Texture = null; _reviewTexture?.Dispose(); _reviewTexture = null;
            _photoDetails.Text = Text("notebook.no_photos");
        }
        var report = _notebookView.LastProcessedReport;
        _notebookQuality.Text = string.Format(Text("notebook.unprocessed"), _notebookView.UnprocessedPhotoCount) + "\n\n" +
            (report is null ? Text("notebook.no_report") :
            Text(report.Passed ? "training.qc_passed" : "training.qc_rework") + "\n\n" +
            string.Join("\n", report.Metrics.Select(m => string.Format(Text("notebook.metric_row"),
                Text("cq.metric." + m.Metric), m.FormattedValue ?? "—", m.FormattedLimit ?? "—", m.Unit == "ratio" ? Text("notebook.unit.ratio") : m.Unit, Text(m.StatusKey)))));
        var selected = _notebookView.Photos.FirstOrDefault(p => p.Sample.SampleId == _selectedPhotoId);
        _surveyMap.UpdateSurvey(_liveLocomotion!.PositionEnu, _liveLocomotion.HeadingRadians,
            _notebookView.Targets.Where(t => t.Resolved).Select(t => t.TargetId).ToHashSet(StringComparer.Ordinal),
            selected?.Sample.EstimatedPose.PositionEnu);
    }

    private void SelectNotebookPhoto(long selectedIndex)
    {
        if (_notebookView is null || selectedIndex < 0 || selectedIndex >= _notebookView.Photos.Count) return;
        var photo = _notebookView.Photos[(int)selectedIndex];
        var sample = photo.Sample;
        _selectedPhotoId = sample.SampleId;
        if (_selectedThumbnailHash != sample.Thumbnail.Sha256)
        {
            using var image = Image.CreateFromData(sample.Thumbnail.Width, sample.Thumbnail.Height, false,
                Image.Format.Rgb8, sample.Thumbnail.CopyRgbBytes());
            var texture = ImageTexture.CreateFromImage(image);
            _photoPreview.Texture = texture;
            _reviewTexture?.Dispose(); _reviewTexture = texture;
            _selectedThumbnailHash = sample.Thumbnail.Sha256;
        }
        _photoDetails.Text = string.Format(Text("notebook.photo_row"), Number(sample.TimestampSeconds),
            Text(photo.Protected ? "notebook.protected" : "notebook.field"), Number(sample.Quality.MotionBlurPixels, 2),
            Number(CameraSensorProfile.Initial.MaxBlurPixels, 2), Number(sample.Quality.ClippingFraction * 100),
            Number(CameraSensorProfile.Initial.MaxClippingFraction * 100),
            string.Join(", ", sample.ObservedFeatures.Select(f => f.TargetId)),
            photo.FailureKeys.Count == 0 ? Text("notebook.photo.accepted") : string.Join(" · ", photo.FailureKeys.Select(Text)));
    }

    public override void _ExitTree()
    {
        if (_photoPreview is not null) _photoPreview.Texture = null;
        _reviewTexture?.Dispose();
        _reviewTexture = null;
    }

    private void VerifySurveyNotebook()
    {
        _trainingButtons["notebook"].EmitSignal(BaseButton.SignalName.Pressed);
        Require(_notebookPanel.Visible, "Notebook button did not open the notebook.");
        RefreshSurveyNotebook();
        Require(_notebookView!.Targets.Count(t => t.Resolved) == 3 && _notebookView.UnprocessedPhotoCount == 0,
            "Notebook does not reflect processed training observations.");
        Require(_photoList.ItemCount == _training.SampleCount, "Photo browser lost saved observations.");
        _photoList.EmitSignal(ItemList.SignalName.ItemSelected, 0L);
        Require(_selectedPhotoId == _training.Samples[0].SampleId && _photoPreview.Texture is not null,
            "Photo selection failed to load the observation thumbnail.");
        Require(_notebookView.LastProcessedReport?.Passed == true, "Notebook CQ differs from authoritative report.");
        _notebookTabs.CurrentTab = 3;
        Require(_notebookTabs.GetTabCount() == 5, "Notebook is missing a section.");
        var north = _surveyMap.Project(new Gsa.Core.Geometry.Vector3d(500100, 7400040, 100));
        var south = _surveyMap.Project(new Gsa.Core.Geometry.Vector3d(500100, 7400020, 100));
        Require(north.Y < south.Y, "Survey map is not north-up.");
        GD.Print($"GSA_SURVEY_NOTEBOOK_SMOKE_OK photos={_photoList.ItemCount} targets=3 report=passed sections=5 north_up=true");
        _notebookPanel.Hide();
    }
}
