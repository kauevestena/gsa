using Gsa.Core.Geometry;
using Gsa.Core.World;
using Gsa.Missions.Presentation;
using Godot;

namespace Gsa.Engine.UI;

public sealed record GtaHudState(
    string ClockText,
    string WeatherText,
    string RadioTitleText,
    string RadioSubtitleText,
    int BalanceCoords,
    int Xp,
    string PlatformTitleText,
    string GnssText,
    Color GnssColor,
    bool ShowLidar,
    bool LidarActive,
    double AutonomyFraction,
    int StorageUsed,
    int StorageCapacity,
    bool ShowGimbalinaStress,
    double GimbalinaStressFraction,
    bool ShowVehicleCondition,
    double VehicleDamageFraction,
    bool ShowAttentionStars,
    int AttentionStars,
    string ObjectiveTitle,
    string ObjectiveDetails,
    string FeedbackText,
    string ContextPromptText,
    bool SurveyViewfinderActive,
    string? DistrictPopup,
    string? VehiclePopup,
    string SpeedText = "");

/// <summary>GTA III-styled HUD overlay: bottom-left circular radar, top-right telemetry/money/stars, and district banners.</summary>
public sealed class GsaGtaHud
{
    public CanvasLayer Layer { get; }
    public GsaRadarControl Radar { get; }
    public HFlowContainer QuickActionBar { get; }
    public Control ViewfinderOverlay { get; }

    private readonly Label _clockLabel;
    private readonly Label _radioLabel;
    private readonly Label _moneyLabel;
    private readonly Label _starsLabel;
    private readonly Label _platformLabel;
    private readonly Label _gnssLabel;
    private readonly Label _sensorsLabel;
    private readonly ProgressBar _autonomyBar;
    private readonly ProgressBar _storageBar;
    private readonly Label _storageLabel;
    private readonly Control _stressRow;
    private readonly ProgressBar _stressBar;
    private readonly Control _damageRow;
    private readonly ProgressBar _damageBar;

    private readonly Label _objectiveTitleLabel;
    private readonly Label _objectiveDetailsLabel;
    private readonly Label _feedbackLabel;
    private readonly Label _promptLabel;
    private readonly Label _subtitleLabel;
    private readonly Label _districtBanner;
    private readonly Label _vehicleBanner;
    private readonly Label _radioCenterBanner;
    private readonly Label _sensorSlotIconLabel;
    private readonly Label _sensorSlotAmmoLabel;
    private readonly VBoxContainer _celebrationBox;
    private readonly Label _celebrationTitle;
    private readonly Label _celebrationSub;
    private readonly Control _cutsceneLayer;
    private readonly Label _cutsceneSpeakerLabel;
    private readonly Label _cutsceneBodyLabel;
    private Action? _cutsceneSkipCallback;

    private double _districtTimer;
    private double _vehicleTimer;
    private double _radioBannerTimer;
    private double _celebrationTimer;
    private double _starPulseTime;
    private string _lastDistrict = "";
    private string _lastVehicle = "";
    private string _lastRadioTitle = "";
    private string _cachedClockWeather = "";
    private string _cachedRadioLabel = "";
    private int _cachedBalance = int.MinValue;
    private string _cachedPlatformText = "";
    private string _cachedGnssText = "";
    private Color _cachedGnssColor = new(-1f, -1f, -1f);
    private string _cachedSensorsText = "";
    private string _cachedSlotIcon = "";
    private string _cachedSlotAmmo = "";
    private string _cachedObjectiveTitle = "";
    private string _cachedObjectiveDetails = "";
    private string _cachedFeedbackText = "";
    private string _cachedPromptText = "";
    private string _cachedSubtitleText = "";
    private int _cachedStarsLit = -1;
    private double _cachedAutonomy = -1.0;
    private double _cachedStoragePct = -1.0;
    private double _cachedStressPct = -1.0;
    private double _cachedDamagePct = -1.0;

    public GsaGtaHud(Node parent)
    {
        Layer = new CanvasLayer { Name = "GtaHudLayer", Layer = 3 };
        parent.AddChild(Layer);

        // Subtle camera viewfinder overlay when SurveyView is active
        ViewfinderOverlay = BuildViewfinderOverlay();
        ViewfinderOverlay.Visible = false;
        Layer.AddChild(ViewfinderOverlay);

        // 1. Bottom-Left Circular Radar
        Radar = new GsaRadarControl
        {
            Name = "CircularRadar",
            AnchorTop = 1f,
            AnchorBottom = 1f,
            OffsetLeft = 14f,
            OffsetTop = -194f,
            OffsetRight = 194f,
            OffsetBottom = -14f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        Layer.AddChild(Radar);

        // 2. Top-Left Compact Objective Card
        var objPanel = CreateDarkCard("ObjectivePanel");
        objPanel.OffsetLeft = 14f;
        objPanel.OffsetTop = 12f;
        objPanel.CustomMinimumSize = new Vector2(315f, 0f);
        Layer.AddChild(objPanel);

        var objBox = new VBoxContainer();
        objBox.AddThemeConstantOverride("separation", 3);
        objPanel.AddChild(objBox);
        _objectiveTitleLabel = StyledLabel("", 15, new Color(1.0f, 0.84f, 0.28f));
        _objectiveDetailsLabel = StyledLabel("", 12, new Color(0.92f, 0.95f, 0.96f), wrap: true);
        _feedbackLabel = StyledLabel("", 12, new Color(0.55f, 0.92f, 0.72f), wrap: true);
        objBox.AddChild(_objectiveTitleLabel);
        objBox.AddChild(_objectiveDetailsLabel);
        objBox.AddChild(_feedbackLabel);

        // 3. Top-Right Iconic GTA III Status Cluster + Weapon/Sensor Frame (floating crisp text, no clunky spreadsheet card)
        var topRightRow = new HBoxContainer
        {
            Name = "GtaStatusCluster",
            AnchorLeft = 1f,
            AnchorRight = 1f,
            OffsetLeft = -334f,
            OffsetTop = 12f,
            OffsetRight = -14f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        topRightRow.AddThemeConstantOverride("separation", 10);
        Layer.AddChild(topRightRow);

        var statusBox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        statusBox.AddThemeConstantOverride("separation", 2);
        topRightRow.AddChild(statusBox);

        var topRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        topRow.AddThemeConstantOverride("separation", 8);
        statusBox.AddChild(topRow);
        _radioLabel = StyledLabel("", 11, new Color(0.98f, 0.78f, 0.34f));
        _clockLabel = StyledLabel("08:00", 19, new Color(0.62f, 0.80f, 1.0f));
        topRow.AddChild(_radioLabel);
        topRow.AddChild(_clockLabel);

        _moneyLabel = StyledLabel("C$ 00000250", 27, new Color(0.24f, 0.88f, 0.42f));
        _moneyLabel.HorizontalAlignment = HorizontalAlignment.Right;
        _moneyLabel.AddThemeConstantOverride("outline_size", 5);
        statusBox.AddChild(_moneyLabel);

        _starsLabel = StyledLabel("☆☆☆☆☆", 18, new Color(1.0f, 0.80f, 0.18f));
        _starsLabel.HorizontalAlignment = HorizontalAlignment.Right;
        _starsLabel.AddThemeConstantOverride("outline_size", 4);
        _starsLabel.Visible = false;
        statusBox.AddChild(_starsLabel);

        _platformLabel = StyledLabel("", 12, new Color(0.92f, 0.94f, 0.96f));
        _platformLabel.HorizontalAlignment = HorizontalAlignment.Right;
        statusBox.AddChild(_platformLabel);

        var sensorRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        sensorRow.AddThemeConstantOverride("separation", 8);
        statusBox.AddChild(sensorRow);
        _gnssLabel = StyledLabel("GNSS", 11, new Color(0.38f, 0.92f, 0.56f));
        _sensorsLabel = StyledLabel("CAM", 11, new Color(0.75f, 0.85f, 0.92f));
        sensorRow.AddChild(_gnssLabel);
        sensorRow.AddChild(_sensorsLabel);

        (_, _autonomyBar) = AddMeterRow(statusBox, "ENG", new Color(0.24f, 0.78f, 0.96f));
        (_storageLabel, _storageBar) = AddMeterRow(statusBox, "SSD", new Color(0.94f, 0.72f, 0.24f));
        (_stressRow, _stressBar) = AddMeterRowContainer(statusBox, "GMB", new Color(0.96f, 0.42f, 0.22f));
        (_damageRow, _damageBar) = AddMeterRowContainer(statusBox, "DMG", new Color(0.94f, 0.22f, 0.24f));
        _damageRow.Visible = false;

        // Classic GTA III Top-Right Active Equipment / Sensor Square Slot!
        var slotPanel = new PanelContainer
        {
            Name = "SensorWeaponSlot",
            CustomMinimumSize = new Vector2(68f, 68f),
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        slotPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.08f, 0.11f, 0.72f),
            BorderColor = new Color(0.36f, 0.78f, 0.92f, 0.85f),
            BorderWidthBottom = 2,
            BorderWidthLeft = 2,
            BorderWidthRight = 2,
            BorderWidthTop = 2,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
        });
        var slotVBox = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        slotPanel.AddChild(slotVBox);
        _sensorSlotIconLabel = StyledLabel("CAM", 14, new Color(0.38f, 0.92f, 0.96f));
        _sensorSlotIconLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _sensorSlotAmmoLabel = StyledLabel("0/200", 11, new Color(0.95f, 0.88f, 0.56f));
        _sensorSlotAmmoLabel.HorizontalAlignment = HorizontalAlignment.Center;
        slotVBox.AddChild(_sensorSlotIconLabel);
        slotVBox.AddChild(_sensorSlotAmmoLabel);
        topRightRow.AddChild(slotPanel);

        // 3b. Top-Center GTA III Radio Tuning Flash Banner
        _radioCenterBanner = StyledLabel("", 20, new Color(0.98f, 0.76f, 0.22f));
        _radioCenterBanner.Name = "RadioCenterBanner";
        _radioCenterBanner.AnchorLeft = 0.25f;
        _radioCenterBanner.AnchorRight = 0.75f;
        _radioCenterBanner.OffsetTop = 18f;
        _radioCenterBanner.HorizontalAlignment = HorizontalAlignment.Center;
        _radioCenterBanner.AddThemeConstantOverride("outline_size", 5);
        _radioCenterBanner.Modulate = new Color(1f, 1f, 1f, 0f);
        Layer.AddChild(_radioCenterBanner);

        // 3c. Center GTA III "MISSION PASSED!" Celebration Banner
        _celebrationBox = new VBoxContainer
        {
            Name = "MissionPassedBanner",
            AnchorLeft = 0.15f,
            AnchorRight = 0.85f,
            AnchorTop = 0.30f,
            AnchorBottom = 0.48f,
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        _celebrationTitle = StyledLabel("", 38, new Color(0.98f, 0.82f, 0.22f));
        _celebrationTitle.HorizontalAlignment = HorizontalAlignment.Center;
        _celebrationTitle.AddThemeConstantOverride("outline_size", 7);
        _celebrationSub = StyledLabel("", 22, new Color(0.42f, 0.92f, 0.96f));
        _celebrationSub.HorizontalAlignment = HorizontalAlignment.Center;
        _celebrationSub.AddThemeConstantOverride("outline_size", 5);
        _celebrationBox.AddChild(_celebrationTitle);
        _celebrationBox.AddChild(_celebrationSub);
        Layer.AddChild(_celebrationBox);

        // 3d. Skippable GTA III Story Briefing / Cutscene Letterbox Bar
        _cutsceneLayer = new Control
        {
            Name = "CutsceneLetterbox",
            AnchorRight = 1f,
            AnchorBottom = 1f,
            Visible = false,
        };
        var topBar = new ColorRect { AnchorRight = 1f, CustomMinimumSize = new Vector2(0f, 52f), Color = new Color(0f, 0f, 0f, 0.92f) };
        var botBar = new PanelContainer { AnchorTop = 1f, AnchorRight = 1f, AnchorBottom = 1f, OffsetTop = -124f };
        botBar.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0f, 0f, 0f, 0.92f), ContentMarginLeft = 24, ContentMarginRight = 24, ContentMarginTop = 12, ContentMarginBottom = 12 });
        var botRow = new HBoxContainer();
        botRow.AddThemeConstantOverride("separation", 16);
        botBar.AddChild(botRow);
        var textCol = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _cutsceneSpeakerLabel = StyledLabel("", 15, new Color(0.98f, 0.80f, 0.24f));
        _cutsceneBodyLabel = StyledLabel("", 15, Colors.White, wrap: true);
        textCol.AddChild(_cutsceneSpeakerLabel);
        textCol.AddChild(_cutsceneBodyLabel);
        botRow.AddChild(textCol);
        var skipBtn = new Button { Text = "Skip [Space]", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(120f, 36f) };
        skipBtn.Pressed += () => { _cutsceneSkipCallback?.Invoke(); };
        botRow.AddChild(skipBtn);
        _cutsceneLayer.AddChild(topBar);
        _cutsceneLayer.AddChild(botBar);
        Layer.AddChild(_cutsceneLayer);

        // 4. Bottom-Center Context Prompt, Radio/Dialogue Subtitles, and Compact Action Dock
        var bottomCenter = new VBoxContainer
        {
            Name = "BottomCenterStack",
            AnchorLeft = 0.21f,
            AnchorRight = 0.98f,
            AnchorTop = 1f,
            AnchorBottom = 1f,
            OffsetTop = -80f,
            OffsetBottom = -10f,
            GrowVertical = Control.GrowDirection.Begin,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        bottomCenter.AddThemeConstantOverride("separation", 3);
        Layer.AddChild(bottomCenter);

        _subtitleLabel = StyledLabel("", 13, new Color(0.98f, 0.94f, 0.68f));
        _subtitleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        bottomCenter.AddChild(_subtitleLabel);

        _promptLabel = StyledLabel("", 12, new Color(0.92f, 0.96f, 0.98f));
        _promptLabel.HorizontalAlignment = HorizontalAlignment.Center;
        bottomCenter.AddChild(_promptLabel);

        QuickActionBar = new HFlowContainer
        {
            Name = "QuickActionBar",
            Alignment = FlowContainer.AlignmentMode.Center,
            LastWrapAlignment = FlowContainer.LastWrapAlignmentMode.Center,
        };
        QuickActionBar.AddThemeConstantOverride("h_separation", 4);
        QuickActionBar.AddThemeConstantOverride("v_separation", 4);
        bottomCenter.AddChild(QuickActionBar);

        // 5. Bottom-Right GTA III District & Vehicle Popup Banners (safely above QuickActionBar)
        var bannerBox = new VBoxContainer
        {
            Name = "PopupBanners",
            AnchorLeft = 1f,
            AnchorRight = 1f,
            AnchorTop = 1f,
            AnchorBottom = 1f,
            OffsetLeft = -440f,
            OffsetTop = -190f,
            OffsetRight = -16f,
            OffsetBottom = -98f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        bannerBox.Alignment = BoxContainer.AlignmentMode.End;
        Layer.AddChild(bannerBox);

        _vehicleBanner = StyledLabel("", 18, new Color(0.32f, 0.86f, 0.94f));
        _vehicleBanner.HorizontalAlignment = HorizontalAlignment.Right;
        _vehicleBanner.Modulate = new Color(1f, 1f, 1f, 0f);
        bannerBox.AddChild(_vehicleBanner);

        _districtBanner = StyledLabel("", 24, new Color(0.98f, 0.82f, 0.22f));
        _districtBanner.HorizontalAlignment = HorizontalAlignment.Right;
        _districtBanner.AddThemeConstantOverride("outline_size", 5);
        _districtBanner.Modulate = new Color(1f, 1f, 1f, 0f);
        bannerBox.AddChild(_districtBanner);
    }

    public void TriggerCelebrationBanner(string title, string subtext, double durationSeconds = 4.2)
    {
        _celebrationTitle.Text = title;
        _celebrationSub.Text = subtext;
        _celebrationTimer = durationSeconds;
        _celebrationBox.Visible = true;
    }

    public void ShowCutsceneLetterbox(string speaker, string dialogueBody, Action onAdvanceOrSkip)
    {
        _cutsceneSpeakerLabel.Text = speaker;
        _cutsceneBodyLabel.Text = dialogueBody;
        _cutsceneSkipCallback = onAdvanceOrSkip;
        _cutsceneLayer.Visible = true;
    }

    public void HideCutsceneLetterbox()
    {
        _cutsceneLayer.Visible = false;
        _cutsceneSkipCallback = null;
    }

    public bool IsCutsceneVisible => _cutsceneLayer.Visible;

    public void ConfigureRadar(WorldSceneManifest manifest, float rangeMetres = 95f) =>
        Radar.Configure(manifest, rangeMetres);

    public Button AddQuickButton(string text, Action onPressed)
    {
        var btn = new Button
        {
            Text = text,
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(0f, 28f),
        };
        btn.AddThemeFontSizeOverride("font_size", 11);
        btn.AddThemeStyleboxOverride("normal", new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.08f, 0.11f, 0.86f),
            BorderColor = new Color(0.32f, 0.42f, 0.50f, 0.85f),
            BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1,
            CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3, CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
            ContentMarginLeft = 7, ContentMarginRight = 7, ContentMarginTop = 3, ContentMarginBottom = 3,
        });
        btn.Pressed += onPressed;
        QuickActionBar.AddChild(btn);
        return btn;
    }

    public void Update(
        double dt,
        GtaHudState state,
        Vector3d playerPosEnu,
        double headingRadians,
        IReadOnlyList<RadarBlip> blips,
        IReadOnlyList<Vector3d> coverageSpots)
    {
        var clockWeather = $"{state.ClockText} · {state.WeatherText}";
        if (clockWeather != _cachedClockWeather)
        {
            _cachedClockWeather = clockWeather;
            _clockLabel.Text = clockWeather;
        }
        if (state.RadioTitleText != _cachedRadioLabel)
        {
            _cachedRadioLabel = state.RadioTitleText;
            _radioLabel.Text = state.RadioTitleText;
        }
        if (state.BalanceCoords != _cachedBalance)
        {
            _cachedBalance = state.BalanceCoords;
            _moneyLabel.Text = $"C$ {state.BalanceCoords:00000000}";
        }
        var platformText = string.IsNullOrEmpty(state.SpeedText)
            ? $"{state.PlatformTitleText} · {state.Xp} XP"
            : $"{state.PlatformTitleText} · {state.SpeedText} · {state.Xp} XP";
        if (platformText != _cachedPlatformText)
        {
            _cachedPlatformText = platformText;
            _platformLabel.Text = platformText;
        }
        if (state.GnssText != _cachedGnssText)
        {
            _cachedGnssText = state.GnssText;
            _gnssLabel.Text = state.GnssText;
        }
        if (!_cachedGnssColor.IsEqualApprox(state.GnssColor))
        {
            _cachedGnssColor = state.GnssColor;
            _gnssLabel.AddThemeColorOverride("font_color", state.GnssColor);
        }
        var sensorsText = state.ShowLidar
            ? (state.LidarActive ? "CAM · GNSS · LiDAR●" : "CAM · GNSS · LiDAR○")
            : "CAM · GNSS";
        if (sensorsText != _cachedSensorsText)
        {
            _cachedSensorsText = sensorsText;
            _sensorsLabel.Text = sensorsText;
        }

        var slotIcon = state.ShowLidar
            ? (state.LidarActive ? "LiDAR" : "360°")
            : (state.ShowVehicleCondition ? "CAM+" : "CAM");
        if (slotIcon != _cachedSlotIcon)
        {
            _cachedSlotIcon = slotIcon;
            _sensorSlotIconLabel.Text = slotIcon;
        }
        var slotAmmo = $"{state.StorageUsed}/{state.StorageCapacity}";
        if (slotAmmo != _cachedSlotAmmo)
        {
            _cachedSlotAmmo = slotAmmo;
            _sensorSlotAmmoLabel.Text = slotAmmo;
            _storageLabel.Text = slotAmmo;
        }

        if (_starsLabel.Visible != state.ShowAttentionStars)
            _starsLabel.Visible = state.ShowAttentionStars;
        if (state.ShowAttentionStars)
        {
            var lit = Math.Clamp(state.AttentionStars, 0, 5);
            if (lit != _cachedStarsLit)
            {
                _cachedStarsLit = lit;
                _starsLabel.Text = new string('★', lit) + new string('☆', 5 - lit);
            }
            _starPulseTime += dt * 4.5;
            var starAlpha = lit > 0 ? 0.68f + 0.32f * MathF.Sin((float)_starPulseTime) : 0.45f;
            _starsLabel.Modulate = new Color(1f, 1f, 1f, starAlpha);
        }

        if (!string.IsNullOrEmpty(state.RadioTitleText) && state.RadioTitleText != _lastRadioTitle)
        {
            _lastRadioTitle = state.RadioTitleText;
            _radioCenterBanner.Text = state.RadioTitleText;
            _radioBannerTimer = 3.0;
            _radioCenterBanner.Visible = true;
        }
        else if (string.IsNullOrEmpty(state.RadioTitleText))
        {
            _lastRadioTitle = "";
        }
        if (_radioBannerTimer > 0.0)
        {
            _radioBannerTimer = Math.Max(0.0, _radioBannerTimer - dt);
            _radioCenterBanner.Modulate = new Color(1f, 1f, 1f, (float)Math.Clamp(_radioBannerTimer, 0.0, 1.0));
            if (_radioBannerTimer <= 0.0)
                _radioCenterBanner.Visible = false;
        }
        else if (_radioCenterBanner.Visible)
        {
            _radioCenterBanner.Visible = false;
        }

        if (_celebrationTimer > 0.0)
        {
            _celebrationTimer = Math.Max(0.0, _celebrationTimer - dt);
            _celebrationBox.Modulate = new Color(1f, 1f, 1f, (float)Math.Clamp(_celebrationTimer, 0.0, 1.0));
            if (_celebrationTimer <= 0.0)
                _celebrationBox.Visible = false;
        }

        var autoPct = Math.Clamp(state.AutonomyFraction * 100.0, 0.0, 100.0);
        if (Math.Abs(autoPct - _cachedAutonomy) > 0.1)
        {
            _cachedAutonomy = autoPct;
            _autonomyBar.Value = autoPct;
        }
        var storagePct = state.StorageCapacity > 0
            ? Math.Clamp(100.0 * state.StorageUsed / state.StorageCapacity, 0.0, 100.0)
            : 0.0;
        if (Math.Abs(storagePct - _cachedStoragePct) > 0.1)
        {
            _cachedStoragePct = storagePct;
            _storageBar.Value = storagePct;
        }

        if (_stressRow.Visible != state.ShowGimbalinaStress)
            _stressRow.Visible = state.ShowGimbalinaStress;
        if (state.ShowGimbalinaStress)
        {
            var stressPct = Math.Clamp(state.GimbalinaStressFraction * 100.0, 0.0, 100.0);
            if (Math.Abs(stressPct - _cachedStressPct) > 0.1)
            {
                _cachedStressPct = stressPct;
                _stressBar.Value = stressPct;
            }
        }

        if (_damageRow.Visible != state.ShowVehicleCondition)
            _damageRow.Visible = state.ShowVehicleCondition;
        if (state.ShowVehicleCondition)
        {
            var dmgPct = Math.Clamp(state.VehicleDamageFraction * 100.0, 0.0, 100.0);
            if (Math.Abs(dmgPct - _cachedDamagePct) > 0.1)
            {
                _cachedDamagePct = dmgPct;
                _damageBar.Value = dmgPct;
            }
        }

        if (state.ObjectiveTitle != _cachedObjectiveTitle)
        {
            _cachedObjectiveTitle = state.ObjectiveTitle;
            _objectiveTitleLabel.Text = state.ObjectiveTitle;
        }
        if (state.ObjectiveDetails != _cachedObjectiveDetails)
        {
            _cachedObjectiveDetails = state.ObjectiveDetails;
            _objectiveDetailsLabel.Text = state.ObjectiveDetails;
        }
        if (state.FeedbackText != _cachedFeedbackText)
        {
            _cachedFeedbackText = state.FeedbackText;
            _feedbackLabel.Text = state.FeedbackText;
        }
        if (state.ContextPromptText != _cachedPromptText)
        {
            _cachedPromptText = state.ContextPromptText;
            _promptLabel.Text = state.ContextPromptText;
        }
        if (state.RadioSubtitleText != _cachedSubtitleText)
        {
            _cachedSubtitleText = state.RadioSubtitleText;
            _subtitleLabel.Text = state.RadioSubtitleText;
        }
        if (ViewfinderOverlay.Visible != state.SurveyViewfinderActive)
            ViewfinderOverlay.Visible = state.SurveyViewfinderActive;

        if (!string.IsNullOrEmpty(state.DistrictPopup) && state.DistrictPopup != _lastDistrict)
        {
            _lastDistrict = state.DistrictPopup;
            _districtBanner.Text = state.DistrictPopup;
            _districtTimer = 3.8;
            _districtBanner.Visible = true;
        }
        if (!string.IsNullOrEmpty(state.VehiclePopup) && state.VehiclePopup != _lastVehicle)
        {
            _lastVehicle = state.VehiclePopup;
            _vehicleBanner.Text = state.VehiclePopup;
            _vehicleTimer = 3.2;
            _vehicleBanner.Visible = true;
        }

        if (_districtTimer > 0.0)
        {
            _districtTimer = Math.Max(0.0, _districtTimer - dt);
            _districtBanner.Modulate = new Color(1f, 1f, 1f, (float)Math.Clamp(_districtTimer, 0.0, 1.0));
            if (_districtTimer <= 0.0) _districtBanner.Visible = false;
        }
        else if (_districtBanner.Visible)
        {
            _districtBanner.Visible = false;
        }

        if (_vehicleTimer > 0.0)
        {
            _vehicleTimer = Math.Max(0.0, _vehicleTimer - dt);
            _vehicleBanner.Modulate = new Color(1f, 1f, 1f, (float)Math.Clamp(_vehicleTimer, 0.0, 1.0));
            if (_vehicleTimer <= 0.0) _vehicleBanner.Visible = false;
        }
        else if (_vehicleBanner.Visible)
        {
            _vehicleBanner.Visible = false;
        }

        Radar.UpdateTelemetry(playerPosEnu, headingRadians, blips, coverageSpots);
    }

    private static PanelContainer CreateDarkCard(string name)
    {
        var panel = new PanelContainer { Name = name };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.04f, 0.06f, 0.08f, 0.78f),
            BorderColor = new Color(0.28f, 0.36f, 0.42f, 0.85f),
            BorderWidthBottom = 1,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            ContentMarginLeft = 9,
            ContentMarginRight = 9,
            ContentMarginTop = 6,
            ContentMarginBottom = 6,
        });
        return panel;
    }

    private static Label StyledLabel(string text, int fontSize, Color color, bool wrap = false)
    {
        var label = new Label
        {
            Text = text,
            AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off,
        };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 3);
        return label;
    }

    private static (Label ValueLabel, ProgressBar Bar) AddMeterRow(VBoxContainer parent, string tag, Color fillColor)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        parent.AddChild(row);
        var tagLbl = StyledLabel(tag, 10, new Color(0.82f, 0.86f, 0.90f));
        tagLbl.CustomMinimumSize = new Vector2(30f, 0f);
        var bar = CreateStyledBar(fillColor);
        var valLbl = StyledLabel("", 10, new Color(0.82f, 0.86f, 0.90f));
        valLbl.CustomMinimumSize = new Vector2(46f, 0f);
        valLbl.HorizontalAlignment = HorizontalAlignment.Right;
        row.AddChild(tagLbl);
        row.AddChild(bar);
        row.AddChild(valLbl);
        return (valLbl, bar);
    }

    private static (Control Row, ProgressBar Bar) AddMeterRowContainer(VBoxContainer parent, string tag, Color fillColor)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        parent.AddChild(row);
        var tagLbl = StyledLabel(tag, 10, new Color(0.82f, 0.86f, 0.90f));
        tagLbl.CustomMinimumSize = new Vector2(30f, 0f);
        var bar = CreateStyledBar(fillColor);
        var spacer = new Control { CustomMinimumSize = new Vector2(46f, 0f) };
        row.AddChild(tagLbl);
        row.AddChild(bar);
        row.AddChild(spacer);
        return (row, bar);
    }

    private static ProgressBar CreateStyledBar(Color fillColor)
    {
        var bar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 100,
            Value = 100,
            ShowPercentage = false,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(110f, 10f),
        };
        bar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new Color(0.12f, 0.14f, 0.16f, 0.9f) });
        bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = fillColor });
        return bar;
    }

    private static Control BuildViewfinderOverlay()
    {
        var root = new Control
        {
            Name = "ViewfinderOverlay",
            AnchorRight = 1f,
            AnchorBottom = 1f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        var reticle = StyledLabel("┌   +   ┐\n└       ┘", 26, new Color(0.25f, 0.95f, 0.85f, 0.72f));
        reticle.AnchorLeft = 0.5f;
        reticle.AnchorTop = 0.5f;
        reticle.AnchorRight = 0.5f;
        reticle.AnchorBottom = 0.5f;
        reticle.OffsetLeft = -70f;
        reticle.OffsetTop = -40f;
        reticle.OffsetRight = 70f;
        reticle.OffsetBottom = 40f;
        reticle.HorizontalAlignment = HorizontalAlignment.Center;
        reticle.VerticalAlignment = VerticalAlignment.Center;
        root.AddChild(reticle);
        return root;
    }
}

