using System.Text.Json;
using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Fusion;
using Gsa.Core.Sensors.Gnss;
using Gsa.Missions.Presentation;
using Godot;

namespace Gsa.Engine.World;

public partial class SaoDatumCityScene
{
    private void RunCityNavigationSmoke()
    {
        static void Require(bool condition, string reason)
        {
            if (!condition) throw new InvalidOperationException("City navigation smoke: " + reason);
        }
        var saved = File.ReadAllBytes(_saveManager.CurrentSavePath);
        var samples = _fieldSamples.Count;
        var contract = JsonSerializer.Serialize(_activeMachine!.ExportSnapshot());
        var calendar = _calendar.CalendarTimeSeconds;
        var locale = TranslationServer.GetLocale();
        var position = new Vector3d(500228, 7400000.7, 100);
        _locomotion.RepositionSafeAfterRecovery(position, LocomotionMode.Van);
        SwitchPlatform(LocomotionMode.Van);
        _navigationEnvironmentAt = ResolveCityNavigationEnvironment;
        for (var i = 0; i < 181; i++) StepCityNavigation(1.0 / 60, position, 0, LocomotionMode.Van, false);
        Require(_navigationSkyQueries >= 15, "actual city sky physics not sampled at 5 Hz");
        Require(_navigationRig?.Estimator?.Current is not null, "dual-antenna van did not initialize from actual observations");
        Require(_navigationRig!.ImuSamples == 300 && _navigationRig.GnssEpochs == 15 && _navigationRig.DmiSamples == 60,
            "physical sensor rates changed");
        Require(NavigationTelemetryPresenter.GnssStatusKey(_navigationRig.LatestGnss) == "hud.gnss.autonomous",
            "vehicle capability advertised an RTK fix without a base");
        var fixCount = _navigationRig.Estimator!.GnssUpdatesApplied;

        // Deterministic outage fixture after exercising the real physics resolver above.
        _navigationEnvironmentAt = _ => new GnssEnvironment(0, Vector3d.Zero);
        for (var i = 0; i < 600; i++) StepCityNavigation(1.0 / 60, position, 0, LocomotionMode.Van, false);
        Require(_navigationRig.Estimator.GnssUpdatesApplied == fixCount, "outage fabricated GNSS corrections");
        Require(_navigationRig.Estimator.Current!.BlackoutAlertActive, "outage age did not propagate");
        Require(NavigationTelemetryPresenter.GnssStatusKey(_navigationRig.LatestGnss) == "hud.gnss.unavailable", "HUD ignored real GNSS outage");
        _tabletPanel.Show(); _tabletTabs.CurrentTab = MapTabletTab;
        _showNavigationButton.ButtonPressed = true;
        RefreshTechnicalTablet();
        Require(_cityMap.NavigationOverlayCount > 0 && _cityMap.NavigationOverlayCount <= LiveNavigationRig.TrailCapacity,
            "estimated trail not connected to the map or unbounded");
        TranslationServer.SetLocale("en_US"); RefreshTechnicalTablet();
        Require(_technicalStatus.Text.Contains(Text("navigation.alert.blackout")) &&
                _technicalStatus.Text.Contains(Text("navigation.model_limit")), "uncertainty explanation or blackout alert missing");
        TranslationServer.SetLocale("pt_BR"); RefreshTechnicalTablet();
        var sigma = _navigationRig.Estimator.Current!.PositionSigma3DMetres;
        Require(_technicalStatus.Text.Contains(TechnicalNumber(sigma, 3)), "Portuguese uncertainty not localized");
        var imuCount = _navigationRig.ImuSamples;
        _showNavigationButton.ButtonPressed = false;
        Require(_cityMap.NavigationOverlayCount == 0, "navigation overlay did not hide");
        for (var i = 0; i < 6; i++) StepCityNavigation(1.0 / 60, position, 0, LocomotionMode.Van, false);
        Require(_navigationRig.ImuSamples == imuCount + 10, "hiding the overlay stopped sensors");
        _showNavigationButton.ButtonPressed = true;
        Require(_calendar.CalendarTimeSeconds == calendar && _fieldSamples.Count == samples &&
                JsonSerializer.Serialize(_activeMachine.ExportSnapshot()) == contract, "diagnostics changed acquisition, contract or career clock");
        Require(saved.SequenceEqual(File.ReadAllBytes(_saveManager.CurrentSavePath)), "diagnostics wrote career files");
        _navigationEnvironmentAt = ResolveCityNavigationEnvironment;
        TranslationServer.SetLocale(locale);
        TryLoadCareer();
        Require(_navigationRig is null && _cityMap.NavigationOverlayCount == 0, "save load reused a diagnostic trajectory");
        GD.Print("GSA_CITY_NAVIGATION_SMOKE_OK imu_hz=100 dmi_hz=20 gnss_hz=5 initialization=observed outage=propagated trail=bounded save=unchanged");
    }
}
