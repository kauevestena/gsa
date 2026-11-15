using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Gnss;
using Gsa.Missions.Presentation;
using Xunit;

namespace Gsa.Missions.Tests;

public sealed class HudAndMapPresenterTests
{
    [Fact]
    public void UnavailableSystemsStayHiddenIndicatorsCarryIconAndPatternAndCadenceIs10Hz()
    {
        var sim = new GnssSimulator(42UL);
        var pos = new Vector3d(500010, 7400010, 100);

        var obsGood = sim.Observe(0.0, pos, GnssProfile.Cellphone, new GnssEnvironment(0.9, Vector3d.Zero, true), Vector3d.Zero);
        var obsDegraded = sim.Observe(0.1, pos, GnssProfile.Cellphone, new GnssEnvironment(0.2, Vector3d.Zero, true), Vector3d.Zero);
        var obsOutage = sim.Observe(0.2, pos, GnssProfile.Cellphone, new GnssEnvironment(0.0, Vector3d.Zero, false), Vector3d.Zero);

        var aggregator = new HudSnapshotAggregator();
        var level0Context = new HudVisibilityContext(PlatformLevel: 0, FactionsUnlocked: false, HasGimbalina: true, HasCamera: true, HasGnss: true);

        // 0.0 s accepted
        Assert.True(aggregator.TrySample(
            new HudFieldTelemetryInput(0.0, 4, 10, obsGood, 8, 0.80, 10, 50, GimbalinaStressFraction: 0.2, FactionAttentionFraction: 0.5, BalanceCoords: 250),
            level0Context, "pt-BR", out var snap0));
        Assert.NotNull(snap0);
        Assert.False(snap0!.IsOfficialQc);
        Assert.Equal("hud.coverage.provisional", snap0.ProvisionalBannerKey);
        Assert.False(snap0.ShowLidarPanel);
        Assert.DoesNotContain(snap0.ActiveSensors, s => s.SensorId == "lidar");
        Assert.False(snap0.ShowFactionAttention);
        Assert.Null(snap0.AttentionIndicator);
        Assert.True(snap0.ShowGimbalinaStress);
        Assert.Equal("1,40", snap0.FormattedPdop); // pt-BR decimal comma

        // 0.05 s rejected by 10 Hz throttle
        Assert.False(aggregator.TrySample(
            new HudFieldTelemetryInput(0.05, 5, 10, obsGood, 8, 0.79, 12, 50),
            level0Context, "pt-BR", out var skipped));
        Assert.Null(skipped);

        // 0.10 s accepted with degraded GNSS: icon + pattern + label differ from Good
        Assert.True(aggregator.TrySample(
            new HudFieldTelemetryInput(0.10, 6, 10, obsDegraded, 4, 0.30, 40, 50),
            level0Context, "en-US", out var snapDegraded));
        Assert.NotEqual(snap0.GnssIndicator.IconId, snapDegraded!.GnssIndicator.IconId);
        Assert.NotEqual(snap0.GnssIndicator.PatternId, snapDegraded.GnssIndicator.PatternId);
        Assert.NotEqual(snap0.GnssIndicator.LabelKey, snapDegraded.GnssIndicator.LabelKey);
        Assert.Equal("4.20", snapDegraded.FormattedPdop); // en-US decimal dot

        // Level 4 + factions unlocked + outage: reveals LiDAR & attention, Critical distinct icon/pattern
        var advancedCtx = new HudVisibilityContext(PlatformLevel: 4, FactionsUnlocked: true);
        Assert.True(aggregator.TrySample(
            new HudFieldTelemetryInput(0.20, 6, 10, obsOutage, 0, 0.10, 48, 50, GimbalinaStressFraction: 0.85, FactionAttentionFraction: 0.80, BalanceCoords: 900),
            advancedCtx, "en-US", out var snapAdvanced));
        Assert.True(snapAdvanced!.ShowLidarPanel);
        Assert.Contains(snapAdvanced.ActiveSensors, s => s.SensorId == "lidar");
        Assert.True(snapAdvanced.ShowFactionAttention);
        Assert.Equal(StatusSeverity.Critical, snapAdvanced.AttentionIndicator!.Severity);
        Assert.NotEqual(snapDegraded.GnssIndicator.IconId, snapAdvanced.GnssIndicator.IconId);
        Assert.NotEqual(snapDegraded.GnssIndicator.PatternId, snapAdvanced.GnssIndicator.PatternId);
    }

    [Fact]
    public void MapOverlayPlannerLimits10WaypointsForbidsAutopilotAndUiScaleRemainsAccessible()
    {
        var planner = new RouteWaypointPlanner();
        Assert.False(planner.AutoPilotEnabled);

        for (var i = 0; i < RouteWaypointPlanner.MaxWaypoints; i++)
            Assert.True(planner.TryAddWaypoint(new Vector3d(500000 + i * 5, 7400000, 100), out _));

        Assert.False(planner.TryAddWaypoint(new Vector3d(500100, 7400000, 100), out var errorKey));
        Assert.Equal("map.error.waypoint_limit_reached", errorKey);
        Assert.Equal(10, planner.Waypoints.Count);

        var overlay = MapOverlayView.Build(
            [("facade_05", new Vector3d(500020, 7400010, 102))],
            [("seg_canyon", new Vector3d(500000, 7400000, 100), new Vector3d(500050, 7400000, 100), 0.15)],
            planner);

        Assert.True(overlay.IsProvisionalFieldOverlay);
        Assert.False(overlay.AutoPilotEnabled);
        Assert.Single(overlay.MissingFeatureMarkers);
        Assert.Single(overlay.DegradedGnssSegments);
        Assert.Equal(StatusSeverity.Critical, overlay.DegradedGnssSegments[0].Indicator.Severity);

        // Settings scale, remapping, controller focus order, and locale coordinate fixture
        var settings = UiPresentationSettings.CreateDefault("en-US")
            .WithScale(1.50);
        settings = settings with
        {
            InputBindings = settings.InputBindings.RemapKeyboard(GameInputAction.Capture, "F"),
        };

        Assert.Equal("F", settings.InputBindings.GetBinding(GameInputAction.Capture).Keyboard);
        Assert.True(settings.VerifyLayoutFitsAtScale(1280, 720).FitsWithoutClipping);
        Assert.NotEmpty(UiPresentationSettings.GetControllerNavigationOrder("hud"));
        Assert.NotEmpty(UiPresentationSettings.GetControllerNavigationOrder("settings"));
        Assert.Equal("1234,56", PresentationNumberFormatter.FormatCoordinate(1234.56, "pt-BR"));
        Assert.Equal("1,234.56", PresentationNumberFormatter.FormatCoordinate(1234.56, "en-US"));
    }
}

