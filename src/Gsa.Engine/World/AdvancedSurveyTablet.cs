using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Lidar;
using Gsa.Core.Traffic;
using Gsa.Missions.Activities;
using Gsa.Missions.Presentation;
using Godot;

namespace Gsa.Engine.World;

public partial class SaoDatumCityScene
{
    private const int MapTabletTab = 3;
    private const int TechnicalTabletTab = 6;
    private OptionButton _geopediaSelector = null!;
    private Label _geopediaSummary = null!;
    private Label _geopediaArticle = null!;
    private Label _technicalStatus = null!;
    private Label _technicalCloudHelp = null!;
    private Label _cityMapLegend = null!;
    private CheckButton _showCloudButton = null!;
    private CheckButton _showAttentionButton = null!;
    private bool _showAttentionOverlay;
    private readonly IReadOnlyList<TerritoryZoneDefinition> _planningZones =
        FactionCatalog.GetZonesForPlanningMap(["old_center", "port", "waterfront", "serra"]);

    private static Label TechnicalLabel(string text = "") => new()
    {
        Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart,
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
    };

    private string TechnicalNumber(double value, int decimals = 1) =>
        PresentationNumberFormatter.Format(value, TranslationServer.GetLocale().ToString(), decimals, false);

    private void BuildAdvancedGeopedia(VBoxContainer tab)
    {
        _geopediaSummary = TechnicalLabel();
        tab.AddChild(_geopediaSummary);
        _geopediaSelector = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FitToLongestItem = false };
        _geopediaSelector.ItemSelected += _ => RefreshSelectedGeopediaArticle();
        tab.AddChild(_geopediaSelector);
        _geopediaArticle = TechnicalLabel();
        tab.AddChild(_geopediaArticle);
    }

    private void RefreshAdvancedGeopedia()
    {
        // Equipment explanations unlock on use, including a platform restored from an older save.
        _geopedia.UnlockAdvancedForPlatformLevel(PlatformProfile.ForMode(_locomotion.MountedVehicleMode).Level);
        var selected = _geopediaSelector.Selected;
        _geopediaSummary.Text = string.Format(Text("geopedia.ui.summary"),
            _collectibles.CollectedEggIds.Count, MvpCollectiblesCoordinator.MaxDailyEggs,
            _collectibles.OccupiedLandmarkIds.Count, _collectibles.CollectedCardIds.Count,
            MvpCollectiblesCoordinator.AllSixProjectionCards.Count);
        _geopediaSelector.TooltipText = Text("geopedia.ui.select");
        _geopediaSelector.Clear();
        foreach (var entry in GeopediaCatalogCoordinator.AllEntries)
            _geopediaSelector.AddItem($"{(_geopedia.IsUnlocked(entry.Id) ? "✓" : "○")} {Text(entry.TitleKey)}");
        _geopediaSelector.Select(Math.Clamp(selected, 0, GeopediaCatalogCoordinator.AllEntries.Count - 1));
        RefreshSelectedGeopediaArticle();
    }

    private void RefreshSelectedGeopediaArticle()
    {
        if (_geopediaSelector.Selected < 0) return;
        var entry = GeopediaCatalogCoordinator.AllEntries[_geopediaSelector.Selected];
        if (!_geopedia.IsUnlocked(entry.Id))
        {
            _geopediaArticle.Text = Text(entry.TitleKey) + "\n\n" + string.Format(Text("geopedia.ui.locked"), entry.UnlockMilestone);
            return;
        }
        // Translate only the selected article; do not rebuild/translate the whole catalog at the HUD tick rate.
        _geopediaArticle.Text = $"✓ {Text(entry.TitleKey)}\n\n" +
            $"{Text("geopedia.ui.example")}\n{Text(entry.ExampleKey)}\n\n" +
            $"{Text("geopedia.ui.units")}\n{Text(entry.UnitAndEffectKey)}\n\n" +
            $"{Text("geopedia.ui.limit")}\n{Text(entry.ModelLimitKey)}";
    }

    private void BuildTechnicalTablet(VBoxContainer tab)
    {
        _showCloudButton = new CheckButton { ButtonPressed = true };
        _showCloudButton.Toggled += visible =>
        {
            _lidarCloudMesh.Visible = visible;
        };
        tab.AddChild(_showCloudButton);
        _showAttentionButton = new CheckButton();
        _showAttentionButton.Toggled += visible =>
        {
            _showAttentionOverlay = visible;
            RefreshTechnicalTablet();
        };
        tab.AddChild(_showAttentionButton);
        _technicalCloudHelp = TechnicalLabel();
        tab.AddChild(_technicalCloudHelp);
        _technicalStatus = TechnicalLabel();
        tab.AddChild(_technicalStatus);
    }

    private static LidarProfile CityLidarProfile(LocomotionMode mode) => mode switch
    {
        LocomotionMode.Van => LidarProfile.L6,
        LocomotionMode.Pickup => LidarProfile.L5,
        _ => LidarProfile.L4,
    };

    private static int CityLidarChannelStep(LidarProfile profile) => profile.Grade >= 6 ? 4 : profile.Grade == 5 ? 2 : 1;

    private void RefreshTechnicalTablet()
    {
        var locale = TranslationServer.GetLocale().ToString();
        var mode = CurrentActiveMode();
        var hasLidar = mode >= LocomotionMode.Kombi;
        var hasAttention = _geopedia.IsUnlocked("geopedia_territory_attention");
        _showCloudButton.Visible = hasLidar || _lidarPointVisibleCount > 0;
        _showCloudButton.Text = Text("survey.technical.cloud_show");
        _showAttentionButton.Text = Text("survey.technical.attention_show");
        _showAttentionButton.Visible = hasAttention;
        _technicalCloudHelp.Visible = _showCloudButton.Visible;
        _technicalCloudHelp.Text = Text("survey.technical.cloud_help");
        _cityMapLegend.Text = Text("survey.map.legend");
        var lines = new List<string> { Text("survey.technical.preview"), Text("survey.technical.ideal_pose"), "" };
        if (hasLidar)
        {
            var profile = CityLidarProfile(mode);
            var preview = AdvancedSurveyPresenter.BuildLidarPreview(profile, _lidarEnabled, CityLidarChannelStep(profile),
                _lidarPointVisibleCount, MaxLidarDisplayPoints, locale);
            lines.Add(Text(preview.StatusKey));
            lines.Add($"{Text("survey.technical.profile")}: {preview.ProfileId}");
            lines.Add($"{Text("survey.technical.nominal_rays")}: {preview.NominalPrimaryRaysPerSecond} {Text("survey.technical.rate_unit")}");
            lines.Add($"{Text("survey.technical.sampled_rays")}: {preview.ScheduledPrimaryRaysPerSecond} {Text("survey.technical.rate_unit")}");
            lines.Add($"{Text("survey.technical.range")}: {preview.MaximumRangeMetres} m");
            lines.Add($"{Text("survey.technical.range_sigma")}: {preview.RangeSigmaMetres} m");
            lines.Add($"{Text("survey.technical.visible_points")}: {preview.VisiblePoints}/{preview.DisplayCapacity}");
            lines.Add("");
        }

        lines.Add(Text("survey.technical.requirements"));
        if (_activeContract is null) lines.Add(Text("survey.technical.no_requirements"));
        foreach (var row in AdvancedSurveyPresenter.BuildRequirements(_activeContract, locale))
        {
            if (row.DeliverableId is not null)
                lines.Add(string.Format(Text("survey.technical.deliverable"), row.DeliverableId, row.AreaId));
            var key = "cq.metric." + row.Metric;
            var label = Text(key);
            if (label == key) label = row.Metric; // A custom contract can introduce a metric identifier.
            if (row.Expected.HasValue)
                lines.Add(label + ": " + Text(row.Expected.Value ? "survey.technical.required_true" : "survey.technical.required_false"));
            else
            {
                var unit = row.Unit == "ratio" ? Text("notebook.unit.ratio") : row.Unit;
                lines.Add(string.Format(Text("survey.technical.requirement"), label, row.Comparison, row.Limit, unit, row.Excellent));
                if (row.SurfaceId is not null) lines.Add(Text("survey.technical.surface") + ": " + row.SurfaceId);
                if (row.CellSizeMetres is not null) lines.Add(Text("survey.technical.cell_size") + ": " + row.CellSizeMetres + " m");
                if (row.TargetDensityPointsPerM2 is not null) lines.Add(Text("survey.technical.density_target") + ": " + row.TargetDensityPointsPerM2 + " " + Text("survey.technical.density_unit"));
            }
        }
        var zones = hasAttention
            ? AdvancedSurveyPresenter.BuildAttentionZones(_planningZones, _attention, _calmPolicy.IsProtectionActive)
            : [];
        if (hasAttention)
        {
            lines.Add("");
            lines.Add(Text("survey.technical.attention"));
            lines.Add(Text(_calmPolicy.IsProtectionActive ? "survey.technical.protected" : "survey.technical.unprotected"));
        }
        for (var i = 0; i < zones.Count; i++)
        {
            var z = zones[i];
            lines.Add($"[{i + 1}] " + string.Format(Text("survey.technical.zone"), z.ZoneId, Text(z.FactionNameKey),
                TechnicalNumber(z.AttentionScore), z.ThreatLevel, TechnicalNumber(z.CooldownSeconds)));
        }
        _technicalStatus.Text = string.Join("\n", lines);
        _cityMap.UpdateAttention(_showAttentionOverlay ? zones : []);
        if (_tabletTabs.CurrentTab == MapTabletTab) RefreshCityMapSnapshot();
    }

    public override void _Notification(int what)
    {
        if (what != NotificationTranslationChanged || _technicalStatus is null || !IsInsideTree()) return;
        RefreshAdvancedGeopedia();
        RefreshTechnicalTablet();
        string[] tabKeys = ["city.tab.contracts", "city.tab.office", "city.tab.fleet", "notebook.tab.map",
            "city.tab.geopedia", "city.tab.settings", "city.tab.technical"];
        for (var i = 0; i < tabKeys.Length; i++) _tabletTabs.SetTabTitle(i, Text(tabKeys[i]));
    }
}
