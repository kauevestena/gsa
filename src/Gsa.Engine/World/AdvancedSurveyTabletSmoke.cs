using System.Text.Json;
using Gsa.Core.Mobility;
using Gsa.Core.Traffic;
using Gsa.Missions.Presentation;
using Godot;

namespace Gsa.Engine.World;

public partial class SaoDatumCityScene
{
    private void RunAdvancedSurveyTabletSmoke()
    {
        static void Require(bool condition, string reason)
        {
            if (!condition) throw new InvalidOperationException("Advanced tablet smoke: " + reason);
        }

        var locale = TranslationServer.GetLocale();
        var currentBytes = File.ReadAllBytes(_saveManager.CurrentSavePath);
        var previousBytes = File.ReadAllBytes(_saveManager.PreviousSavePath);
        _locomotion.RepositionSafeAfterRecovery(CurrentPositionEnu(), LocomotionMode.Van, CurrentHeadingRadians(),
            refillAutonomy: false, clearStorage: false);
        SwitchPlatform(LocomotionMode.Van);
        _tabletPanel.Show();
        _tabletTabs.CurrentTab = TechnicalTabletTab;
        TranslationServer.SetLocale("en_US");
        RefreshTabletContents();
        Require(_geopediaSelector.ItemCount == 30, "advanced catalog missing from actual scene");
        Require(_technicalStatus.Text.Contains("16000") && _technicalStatus.Text.Contains("4000"), "nominal/preview rates mixed");
        Require(_technicalStatus.Text.Contains("0.005"), "profile noise missing");
        Require(_technicalStatus.Text.Contains(Text("navigation.preview_separate")), "diagnostics confused with preview acquisition");
        Require(_technicalStatus.Text.Contains(Text("navigation.status.waiting_gnss")), "reset trajectory silently reported as precise");
        Require(_technicalStatus.Text.Contains(Text("survey.technical.required_true")), "active contract predicate not shown");

        var samples = _fieldSamples.Count;
        var contract = JsonSerializer.Serialize(_activeMachine!.ExportSnapshot());
        var enabled = _lidarEnabled;
        _showCloudButton.ButtonPressed = false;
        Require(!_lidarCloudMesh.Visible && _lidarEnabled == enabled, "hiding preview changed capture");
        var slice = _lidarSliceIndex;
        StepLiveLidar(1.0 / 60, CurrentPositionEnu(), CurrentHeadingRadians(), LocomotionMode.Van, false);
        Require(_lidarSliceIndex == slice + 1, "hidden cloud stopped the acquisition scheduler");
        _showCloudButton.ButtonPressed = true;
        Require(_lidarCloudMesh.Visible && _lidarEnabled == enabled, "showing preview changed capture");
        Require(_fieldSamples.Count == samples && JsonSerializer.Serialize(_activeMachine.ExportSnapshot()) == contract,
            "presentation changed acquisition or contract state");

        var entryIndex = GeopediaCatalogCoordinator.AllEntries.ToList().FindIndex(e => e.ConceptId == "lidar_density");
        _geopediaSelector.Select(entryIndex);
        _geopediaSelector.EmitSignal(OptionButton.SignalName.ItemSelected, (long)entryIndex);
        var english = _geopediaArticle.Text;
        Require(english.Contains(Text("geopedia.lidar_density.unit_effect")) && english.Contains(Text("geopedia.lidar_density.model_limit")),
            "article omits units or model limits");
        var unlocked = string.Join(",", _geopedia.ExportUnlockedIds());
        TranslationServer.SetLocale("pt_BR");
        RefreshAdvancedGeopedia();
        RefreshTechnicalTablet();
        Require(_geopediaSelector.Selected == entryIndex && _geopediaArticle.Text != english, "language change lost selection or text");
        Require(_technicalStatus.Text.Contains("0,005"), "numeric locale did not update");
        Require(_geopediaArticle.Text.Contains(Text("geopedia.lidar_density.model_limit")), "Portuguese limitation missing");
        Require(string.Join(",", _geopedia.ExportUnlockedIds()) == unlocked, "locale change changed unlock state");

        var zone = _planningZones[0];
        _attention = new TerritoryAttentionTracker(_planningZones);
        _attention.Step(zone.ZoneId, true, 1, false, 41);
        _showAttentionButton.ButtonPressed = true;
        RefreshTechnicalTablet();
        Require(_cityMap.AttentionOverlayCount == _planningZones.Count, "territories not connected to map");
        Require(_technicalStatus.Text.Contains("41,0/100"), "attention does not match tracker");
        Require(_technicalStatus.Text.Contains(Text("survey.technical.protected")), "calm protection missing");
        _showAttentionButton.ButtonPressed = false;
        Require(_cityMap.AttentionOverlayCount == 0, "territories did not hide");
        Require(_attention.GetAttention(zone.ZoneId) == 41, "overlay toggle altered attention");

        SwitchPlatform(LocomotionMode.Handcart);
        _geopedia = new GeopediaCatalogCoordinator();
        _lidarPointVisibleCount = 0;
        RefreshAdvancedGeopedia();
        RefreshTechnicalTablet();
        Require(!_showCloudButton.Visible && !_showAttentionButton.Visible, "unavailable sensors exposed");
        Require(!_technicalStatus.Text.Contains(Text("survey.technical.range_sigma")), "unavailable LiDAR numbers exposed");
        _geopediaSelector.Select(entryIndex);
        _geopediaSelector.EmitSignal(OptionButton.SignalName.ItemSelected, (long)entryIndex);
        Require(!_geopediaArticle.Text.Contains(Text("geopedia.lidar_density.example")), "locked body shown");
        Require(_geopediaArticle.Text.Contains("M5"), "locked article lacks unlock milestone");
        Require(currentBytes.SequenceEqual(File.ReadAllBytes(_saveManager.CurrentSavePath)) &&
                previousBytes.SequenceEqual(File.ReadAllBytes(_saveManager.PreviousSavePath)), "presentation wrote career files");
        TranslationServer.SetLocale(locale);
        GD.Print("GSA_ADVANCED_SURVEY_SMOKE_OK articles=30 locales=2 preview=bounded trajectory=unavailable overlays=tracker capture=unchanged");
    }
}
