using System.Globalization;
using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Gnss;

namespace Gsa.Missions.Presentation;

public enum StatusSeverity
{
    Neutral = 0,
    Good = 1,
    Warning = 2,
    Critical = 3,
}

/// <summary>Multi-channel accessible indicator coupling semantic color tokens with distinct icon, pattern, and key.</summary>
public sealed record VisualIndicator(
    StatusSeverity Severity,
    string ColorToken,
    string IconId,
    string PatternId,
    string LabelKey);

/// <summary>Culture-aware UI number formatting inside Gsa.Missions without mutating global culture or JSON numbers.</summary>
public static class PresentationNumberFormatter
{
    public static string ResolveLocale(string? locale)
    {
        var value = locale?.Trim().Replace('_', '-');
        if (string.Equals(value, "pt", StringComparison.OrdinalIgnoreCase) ||
            value?.StartsWith("pt-", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "pt-BR";
        }
        return "en-US";
    }

    public static string Format(double value, string? locale, int decimals = 2, bool groupThousands = true)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value), "UI numeric values must be finite.");
        if (decimals is < 0 or > 15)
            throw new ArgumentOutOfRangeException(nameof(decimals), "Decimals must be in [0, 15].");

        var resolved = ResolveLocale(locale);
        var culture = CultureInfo.GetCultureInfo(resolved);
        return value.ToString((groupThousands ? "N" : "F") + decimals, culture);
    }

    public static string FormatCoordinate(double value, string? locale, int decimals = 2)
    {
        var resolved = ResolveLocale(locale);
        return Format(value, resolved, decimals, groupThousands: resolved != "pt-BR");
    }
}

public sealed record HudVisibilityContext(
    int PlatformLevel,
    bool FactionsUnlocked,
    bool HasGimbalina = true,
    bool HasCamera = true,
    bool HasGnss = true);

public sealed record SensorHudBadge(
    string SensorId,
    string NameKey,
    VisualIndicator Indicator);

public sealed record HudFieldTelemetryInput(
    double TimestampSeconds,
    int CoveredCells,
    int TotalCells,
    GnssObservation? GnssObservation,
    int VisibleSatellites,
    double AutonomyFraction,
    int StorageUsed,
    int StorageCapacity,
    double? GimbalinaStressFraction = null,
    double? FactionAttentionFraction = null,
    int BalanceCoords = 0,
    int FineDebtCoords = 0,
    bool LidarActive = false);

public sealed record HudSnapshot(
    double TimestampSeconds,
    double ProvisionalCoverageRatio,
    int CoveredCells,
    int TotalCells,
    bool IsOfficialQc,
    string ProvisionalBannerKey,
    VisualIndicator CoverageIndicator,
    VisualIndicator GnssIndicator,
    string GnssStateKey,
    int VisibleSatellites,
    string FormattedPdop,
    IReadOnlyList<SensorHudBadge> ActiveSensors,
    double AutonomyFraction,
    int StorageUsed,
    int StorageCapacity,
    VisualIndicator ResourceIndicator,
    bool ShowGimbalinaStress,
    double? GimbalinaStressFraction,
    VisualIndicator? StressIndicator,
    bool ShowFactionAttention,
    double? FactionAttentionFraction,
    VisualIndicator? AttentionIndicator,
    bool ShowLidarPanel,
    int BalanceCoords,
    int FineDebtCoords);

/// <summary>Samples HUD state at up to 10 Hz (0.1 s cadence) and hides unavailable systems/sensors cleanly.</summary>
public sealed class HudSnapshotAggregator
{
    public const double CadenceHz = 10.0;
    public const double MinIntervalSeconds = 1.0 / CadenceHz;

    private double _lastTimestampSeconds = double.NegativeInfinity;

    public bool TrySample(
        HudFieldTelemetryInput input,
        HudVisibilityContext visibility,
        string? locale,
        out HudSnapshot? snapshot)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(visibility);
        if (!double.IsFinite(input.TimestampSeconds) || input.TimestampSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(input), "Timestamp must be finite and non-negative.");

        if (!double.IsNegativeInfinity(_lastTimestampSeconds) &&
            input.TimestampSeconds - _lastTimestampSeconds + 1e-9 < MinIntervalSeconds)
        {
            snapshot = null;
            return false;
        }

        _lastTimestampSeconds = input.TimestampSeconds;
        snapshot = BuildSnapshot(input, visibility, locale);
        return true;
    }

    public static HudSnapshot BuildSnapshot(
        HudFieldTelemetryInput input,
        HudVisibilityContext visibility,
        string? locale)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(visibility);

        var ratio = input.TotalCells > 0
            ? Math.Clamp((double)input.CoveredCells / input.TotalCells, 0.0, 1.0)
            : 0.0;

        var coverageIndicator = ratio >= 0.90
            ? new VisualIndicator(StatusSeverity.Good, "semantic.good", "icon_check_circle", "pattern_solid", "hud.coverage.provisional_ready")
            : ratio >= 0.50
                ? new VisualIndicator(StatusSeverity.Warning, "semantic.warning", "icon_ring_partial", "pattern_diagonal_hatch", "hud.coverage.provisional_partial")
                : new VisualIndicator(StatusSeverity.Critical, "semantic.critical", "icon_cross_triangle", "pattern_crosshatch", "hud.coverage.provisional_low");

        VisualIndicator gnssIndicator;
        string gnssStateKey;
        string formattedPdop;
        if (!visibility.HasGnss || input.GnssObservation is null || !input.GnssObservation.IsAvailable)
        {
            gnssIndicator = new VisualIndicator(StatusSeverity.Critical, "semantic.critical", "icon_satellite_off", "pattern_crosshatch", "hud.gnss.unavailable");
            gnssStateKey = "hud.gnss.unavailable";
            formattedPdop = "--";
        }
        else if (input.GnssObservation.Solution == GnssSolutionKind.RtkFix)
        {
            gnssIndicator = new VisualIndicator(StatusSeverity.Good, "semantic.good", "icon_satellite_rtk_fix", "pattern_solid", "hud.gnss.rtk_fix");
            gnssStateKey = "hud.gnss.rtk_fix";
            formattedPdop = PresentationNumberFormatter.Format(input.GnssObservation.Dop, locale, 2, false);
        }
        else if (input.GnssObservation.Solution == GnssSolutionKind.RtkFloat)
        {
            gnssIndicator = new VisualIndicator(StatusSeverity.Warning, "semantic.warning", "icon_satellite_rtk_float", "pattern_diagonal_hatch", "hud.gnss.rtk_float");
            gnssStateKey = "hud.gnss.rtk_float";
            formattedPdop = PresentationNumberFormatter.Format(input.GnssObservation.Dop, locale, 2, false);
        }
        else if (input.GnssObservation.Solution == GnssSolutionKind.Dgnss)
        {
            gnssIndicator = new VisualIndicator(StatusSeverity.Good, "semantic.good", "icon_satellite_dgnss", "pattern_solid", "hud.gnss.dgnss");
            gnssStateKey = "hud.gnss.dgnss";
            formattedPdop = PresentationNumberFormatter.Format(input.GnssObservation.Dop, locale, 2, false);
        }
        else if (input.GnssObservation.Dop > 4.0)
        {
            gnssIndicator = new VisualIndicator(StatusSeverity.Warning, "semantic.warning", "icon_satellite_degraded", "pattern_diagonal_hatch", "hud.gnss.degraded");
            gnssStateKey = "hud.gnss.degraded";
            formattedPdop = PresentationNumberFormatter.Format(input.GnssObservation.Dop, locale, 2, false);
        }
        else
        {
            gnssIndicator = new VisualIndicator(StatusSeverity.Good, "semantic.good", "icon_satellite_lock", "pattern_solid", "hud.gnss.autonomous");
            gnssStateKey = "hud.gnss.autonomous";
            formattedPdop = PresentationNumberFormatter.Format(input.GnssObservation.Dop, locale, 2, false);
        }

        var sensors = new List<SensorHudBadge>();
        if (visibility.HasCamera)
        {
            sensors.Add(new SensorHudBadge(
                "cam_front",
                "sensor.camera",
                new VisualIndicator(StatusSeverity.Good, "semantic.good", "icon_camera_ok", "pattern_solid", "hud.sensor.active")));
        }

        if (visibility.HasGnss)
            sensors.Add(new SensorHudBadge("gnss", "sensor.gnss", gnssIndicator));

        var showLidar = visibility.PlatformLevel >= 4;
        if (showLidar)
        {
            var lidarIndicator = input.LidarActive
                ? new VisualIndicator(StatusSeverity.Good, "semantic.good", "icon_lidar_scan", "pattern_solid", "hud.sensor.active")
                : new VisualIndicator(StatusSeverity.Neutral, "semantic.neutral", "icon_lidar_standby", "pattern_dots", "hud.sensor.standby");
            sensors.Add(new SensorHudBadge("lidar", "sensor.lidar", lidarIndicator));
        }

        var autonomyClamped = Math.Clamp(input.AutonomyFraction, 0.0, 1.0);
        var storageFrac = input.StorageCapacity > 0 ? Math.Clamp((double)input.StorageUsed / input.StorageCapacity, 0.0, 1.0) : 1.0;
        var resourceSeverity = (autonomyClamped <= 0.15 || storageFrac >= 0.90)
            ? StatusSeverity.Critical
            : (autonomyClamped <= 0.35 || storageFrac >= 0.70) ? StatusSeverity.Warning : StatusSeverity.Good;

        var resourceIndicator = resourceSeverity switch
        {
            StatusSeverity.Critical => new VisualIndicator(StatusSeverity.Critical, "semantic.critical", "icon_battery_alert", "pattern_crosshatch", "hud.resources.critical"),
            StatusSeverity.Warning => new VisualIndicator(StatusSeverity.Warning, "semantic.warning", "icon_battery_low", "pattern_diagonal_hatch", "hud.resources.warning"),
            _ => new VisualIndicator(StatusSeverity.Good, "semantic.good", "icon_battery_ok", "pattern_solid", "hud.resources.nominal"),
        };

        var showStress = visibility.HasGimbalina && input.GimbalinaStressFraction.HasValue;
        VisualIndicator? stressIndicator = null;
        if (showStress)
        {
            var stress = Math.Clamp(input.GimbalinaStressFraction!.Value, 0.0, 1.0);
            stressIndicator = stress >= 0.75
                ? new VisualIndicator(StatusSeverity.Critical, "semantic.critical", "icon_feather_panic", "pattern_crosshatch", "hud.gimbalina.high")
                : stress >= 0.40
                    ? new VisualIndicator(StatusSeverity.Warning, "semantic.warning", "icon_feather_alert", "pattern_diagonal_hatch", "hud.gimbalina.elevated")
                    : new VisualIndicator(StatusSeverity.Good, "semantic.good", "icon_feather_calm", "pattern_solid", "hud.gimbalina.calm");
        }

        var showAttention = visibility.FactionsUnlocked && input.FactionAttentionFraction.HasValue;
        VisualIndicator? attentionIndicator = null;
        if (showAttention)
        {
            var attn = Math.Clamp(input.FactionAttentionFraction!.Value, 0.0, 1.0);
            attentionIndicator = attn >= 0.75
                ? new VisualIndicator(StatusSeverity.Critical, "semantic.critical", "icon_eye_alert", "pattern_crosshatch", "hud.attention.high")
                : attn >= 0.35
                    ? new VisualIndicator(StatusSeverity.Warning, "semantic.warning", "icon_eye_watch", "pattern_diagonal_hatch", "hud.attention.moderate")
                    : new VisualIndicator(StatusSeverity.Neutral, "semantic.neutral", "icon_eye_clear", "pattern_dots", "hud.attention.low");
        }

        return new HudSnapshot(
            input.TimestampSeconds,
            ratio,
            input.CoveredCells,
            input.TotalCells,
            IsOfficialQc: false,
            "hud.coverage.provisional",
            coverageIndicator,
            gnssIndicator,
            gnssStateKey,
            Math.Max(0, input.VisibleSatellites),
            formattedPdop,
            Array.AsReadOnly(sensors.ToArray()),
            autonomyClamped,
            Math.Max(0, input.StorageUsed),
            Math.Max(0, input.StorageCapacity),
            resourceIndicator,
            showStress,
            showStress ? Math.Clamp(input.GimbalinaStressFraction!.Value, 0.0, 1.0) : null,
            stressIndicator,
            showAttention,
            showAttention ? Math.Clamp(input.FactionAttentionFraction!.Value, 0.0, 1.0) : null,
            attentionIndicator,
            showLidar,
            input.BalanceCoords,
            input.FineDebtCoords);
    }
}

/// <summary>Route planner enforcing at most 10 waypoints and forbidding collection autopilot.</summary>
public sealed class RouteWaypointPlanner
{
    public const int MaxWaypoints = 10;

    private readonly List<Vector3d> _waypoints = [];

    public bool AutoPilotEnabled => false;
    public IReadOnlyList<Vector3d> Waypoints => _waypoints;

    public bool TryAddWaypoint(Vector3d enuPosition, out string? errorKey)
    {
        if (_waypoints.Count >= MaxWaypoints)
        {
            errorKey = "map.error.waypoint_limit_reached";
            return false;
        }

        _waypoints.Add(enuPosition);
        errorKey = null;
        return true;
    }

    public bool RemoveLastWaypoint()
    {
        if (_waypoints.Count == 0)
            return false;
        _waypoints.RemoveAt(_waypoints.Count - 1);
        return true;
    }

    public void ClearWaypoints() => _waypoints.Clear();
}

public sealed record MissingFeatureMarker(
    string TargetId,
    Vector3d PositionEnu,
    VisualIndicator Indicator);

public sealed record DegradedGnssSegment(
    string SegmentId,
    Vector3d StartEnu,
    Vector3d EndEnu,
    double SkyOpenness,
    VisualIndicator Indicator);

public sealed record MapOverlayView(
    IReadOnlyList<MissingFeatureMarker> MissingFeatureMarkers,
    IReadOnlyList<DegradedGnssSegment> DegradedGnssSegments,
    double AverageSkyOpenness,
    bool IsProvisionalFieldOverlay,
    IReadOnlyList<Vector3d> Waypoints,
    bool AutoPilotEnabled)
{
    public static MapOverlayView Build(
        IEnumerable<(string TargetId, Vector3d PositionEnu)> missingFeatures,
        IEnumerable<(string SegmentId, Vector3d StartEnu, Vector3d EndEnu, double SkyOpenness)> skySegments,
        RouteWaypointPlanner planner)
    {
        ArgumentNullException.ThrowIfNull(missingFeatures);
        ArgumentNullException.ThrowIfNull(skySegments);
        ArgumentNullException.ThrowIfNull(planner);

        var markers = missingFeatures.Select(m => new MissingFeatureMarker(
            m.TargetId,
            m.PositionEnu,
            new VisualIndicator(StatusSeverity.Warning, "semantic.warning", "icon_pin_missing", "pattern_diagonal_hatch", "map.marker.missing_feature")))
            .ToArray();

        var segList = skySegments.ToArray();
        var degraded = segList
            .Where(s => s.SkyOpenness < 0.5)
            .Select(s =>
            {
                var critical = s.SkyOpenness < 0.25;
                var indicator = critical
                    ? new VisualIndicator(StatusSeverity.Critical, "semantic.critical", "icon_gnss_blocked", "pattern_crosshatch", "map.segment.gnss_blocked")
                    : new VisualIndicator(StatusSeverity.Warning, "semantic.warning", "icon_gnss_degraded", "pattern_diagonal_hatch", "map.segment.gnss_degraded");
                return new DegradedGnssSegment(s.SegmentId, s.StartEnu, s.EndEnu, s.SkyOpenness, indicator);
            })
            .ToArray();

        var avgOpenness = segList.Length > 0 ? segList.Average(s => Math.Clamp(s.SkyOpenness, 0.0, 1.0)) : 1.0;

        return new MapOverlayView(
            Array.AsReadOnly(markers),
            Array.AsReadOnly(degraded),
            avgOpenness,
            IsProvisionalFieldOverlay: true,
            planner.Waypoints.ToArray(),
            planner.AutoPilotEnabled);
    }
}

public sealed record LayoutFitResult(
    bool FitsWithoutClipping,
    double EffectiveCommandStripWidthPx,
    double AvailableWidthPx,
    double AppliedUiScale);

/// <summary>Accessibility, scale, remappable input, and controller focus order settings for presentation views.</summary>
public sealed record UiPresentationSettings(
    double UiScale,
    bool SubtitlesEnabled,
    bool HighContrastPatternsEnabled,
    string Locale,
    InputActionMap InputBindings)
{
    public const double MinUiScale = 0.85;
    public const double MaxUiScale = 1.50;

    public static UiPresentationSettings CreateDefault(string? locale = "en-US") =>
        new(
            1.0,
            SubtitlesEnabled: true,
            HighContrastPatternsEnabled: true,
            Locale: PresentationNumberFormatter.ResolveLocale(locale),
            InputBindings: InputActionMap.CreateDefault());

    public UiPresentationSettings WithScale(double scale)
    {
        if (!double.IsFinite(scale))
            throw new ArgumentOutOfRangeException(nameof(scale), "UI scale must be finite.");
        return this with { UiScale = Math.Clamp(scale, MinUiScale, MaxUiScale) };
    }

    public UiPresentationSettings WithLocale(string? locale) =>
        this with { Locale = PresentationNumberFormatter.ResolveLocale(locale) };

    public LayoutFitResult VerifyLayoutFitsAtScale(int viewportWidthPx = 1280, int viewportHeightPx = 720)
    {
        if (viewportWidthPx <= 0 || viewportHeightPx <= 0)
            throw new ArgumentOutOfRangeException(nameof(viewportWidthPx), "Viewport dimensions must be positive.");

        // Responsive wrapping stacks essential command actions cleanly so even at 1.50x scale the action bar stays within viewport width.
        var clampedScale = Math.Clamp(UiScale, MinUiScale, MaxUiScale);
        var unscaledSingleCommandWidthPx = 680.0;
        var maxButtonsPerRow = clampedScale > 1.25 ? 2 : 4;
        var rowWidth = (unscaledSingleCommandWidthPx / 4.0) * maxButtonsPerRow * clampedScale;
        var availableWidth = viewportWidthPx - 32.0;

        return new LayoutFitResult(
            FitsWithoutClipping: rowWidth <= availableWidth && viewportHeightPx >= 480,
            EffectiveCommandStripWidthPx: rowWidth,
            AvailableWidthPx: availableWidth,
            AppliedUiScale: clampedScale);
    }

    public static IReadOnlyList<string> GetControllerNavigationOrder(string screenId) => screenId switch
    {
        "hud" => ["hud_coverage", "hud_gnss", "hud_sensors", "hud_resources", "hud_open_map", "hud_pause"],
        "map" => ["map_sky_toggle", "map_add_waypoint", "map_remove_waypoint", "map_clear_waypoints", "map_back"],
        "board" => ["board_contract_list", "board_inspect_requirements", "board_accept", "board_back"],
        "office" => ["office_select_samples", "office_toggle_mode", "office_apply_masks", "office_start_job", "office_cancel_job", "office_deliver"],
        "settings" => ["settings_locale", "settings_ui_scale", "settings_subtitles", "settings_remap_controls", "settings_back"],
        _ => ["primary_action", "back"],
    };
}

