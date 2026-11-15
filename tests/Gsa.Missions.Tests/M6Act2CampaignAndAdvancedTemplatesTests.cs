using Gsa.Core.Geodesy;
using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.World;
using Gsa.Missions.Campaign;
using Gsa.Missions.Content;
using Xunit;

namespace Gsa.Missions.Tests;

public sealed class M6Act2CampaignAndAdvancedTemplatesTests
{
    private static WorldSceneManifest LoadTutorialManifest()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "game", "world", "tutorial-block.json")))
            dir = dir.Parent;
        var path = Path.Combine(dir!.FullName, "game", "world", "tutorial-block.json");
        return WorldSceneManifest.Parse(File.ReadAllText(path));
    }

    [Fact]
    public void T_F10_04_Act2PlaythroughHist05To09HeightSwapCheckSerraUnlockCalmParityAndAdvancedTemplates100Seeds()
    {
        // 1. Validate campaign graph & Act 2 mission specifications
        var graph = FullCampaignCatalog.ValidateCampaignGraphAndCalmParity();
        Assert.True(graph.Valid, string.Join("; ", graph.Errors));
        Assert.Equal(5, FullCampaignCatalog.AllMissions.Count(m => m.Act == 2));

        // Higher platforms remain usable when lower minimum is specified (e.g., HIST-09 minimum car allows pickup)
        Assert.False(Act2CampaignRunner.CanExecuteMissionWithPlatform("HIST-09", LocomotionMode.Scooter));
        Assert.True(Act2CampaignRunner.CanExecuteMissionWithPlatform("HIST-09", LocomotionMode.Car));
        Assert.True(Act2CampaignRunner.CanExecuteMissionWithPlatform("HIST-09", LocomotionMode.Kombi));
        Assert.True(Act2CampaignRunner.CanExecuteMissionWithPlatform("HIST-09", LocomotionMode.Pickup));

        // 2. Execute Act 2 playthrough in standard mode vs Calm City mode on Serra manifest
        var serraManifest = SerraWorldAssembler.BuildSerraExpandedManifest(LoadTutorialManifest());
        var standardRun = Act2CampaignRunner.RunAct2Playthrough(serraManifest, calmCityEnabled: false);
        var calmRun = Act2CampaignRunner.RunAct2Playthrough(serraManifest, calmCityEnabled: true);

        Assert.True(standardRun.Passed);
        Assert.True(calmRun.Passed);
        Assert.True(standardRun.KombiUnlocked);
        Assert.True(standardRun.PickupUnlocked);
        Assert.True(standardRun.SerraUnlocked);
        Assert.True(standardRun.Act3Unlocked);
        Assert.True(standardRun.TunnelTestOverrideOpened);
        Assert.True(standardRun.SwappedHeightsRejected);
        Assert.True(standardRun.RainOutlierFilterPassed);
        Assert.Equal(6, standardRun.MovedBenchmarksDetectedCount);

        // AC-F10-03: Calm City guarantees exact parity on payouts, reputation, products, and unlocks
        Assert.Equal(standardRun.TotalRewardCoords, calmRun.TotalRewardCoords);
        Assert.Equal(standardRun.TotalReputationDelta, calmRun.TotalReputationDelta);
        Assert.Equal(standardRun.UnlockedPlatforms, calmRun.UnlockedPlatforms);
        Assert.Equal(standardRun.UnlockedRegions, calmRun.UnlockedRegions);
        Assert.All(calmRun.Missions, m => Assert.True(m.HostileHazardsDisabled));

        // Explicit verification that swapping h and H rejects HIST-07 vertical conformance
        var pt = new Vector3d(500620.0, 7401200.0, 112.5);
        var ortho = LocalGeoidModel.ToOrthometricHeight(pt);
        var swapped = Act2CampaignRunner.ValidateViaductHeightReference([new("viaduct_01", pt, ortho, pt.Z)]);
        Assert.False(swapped.Compliant);
        Assert.Equal("cq.error.vertical_reference_h_and_orthometric_H_swapped", swapped.ReasonKey);

        // 3. Generate & schema-validate 100 seeds for PROC-ENERGIA, PROC-OBRAS, and PROC-TUNEL (300 total)
        var seedReport = Act2AdvancedProceduralGenerator.ValidateOneHundredSeedsPerAdvancedTemplate();
        Assert.True(seedReport.AllPassed);
        Assert.Equal(3, seedReport.TemplatesValidated);
        Assert.Equal(300, seedReport.TotalContractsGenerated);
        Assert.Equal(300, seedReport.TotalContractsPassed);
        Assert.All(seedReport.ByTemplate, t =>
        {
            Assert.Equal(100, t.SeedsTested);
            Assert.Equal(100, t.SeedsPassed);
            Assert.True(t.AllCompatibleWithFleet);
            Assert.True(t.AllWitnessRoutesFeasible);
            Assert.True(t.AllSchemaValid);
        });

        // Witness certificate invalidation on map/profile hash drift
        Assert.Throws<InvalidOperationException>(() =>
            Act2AdvancedProceduralGenerator.GenerateFromAdvancedTemplate("PROC-TUNEL", 2026UL, currentMapSha256: "drifted_map_hash"));

        // Board gates Serra/L4 templates by platform level & unlocked regions while keeping a calm starter contract at balance=0
        var beforeSerra = Act2AdvancedProceduralGenerator.GenerateAvailableBoardWithAdvancedTemplates(
            balanceCoords: 0, fineDebtCoords: 100, ownedPlatformLevel: 3, new HashSet<string>(["old_center", "port"]), seed: 100UL);
        Assert.DoesNotContain(beforeSerra, o => o.Contract.RegionId == "serra");
        Assert.Contains(beforeSerra, o => o.Contract.Calm && o.ExecutableWithStarterKit);

        var afterSerra = Act2AdvancedProceduralGenerator.GenerateAvailableBoardWithAdvancedTemplates(
            balanceCoords: 5000, fineDebtCoords: 0, ownedPlatformLevel: 5, new HashSet<string>(["old_center", "port", "waterfront", "serra"]), seed: 100UL);
        Assert.Contains(afterSerra, o => o.TemplateId == "PROC-ENERGIA");
        Assert.Contains(afterSerra, o => o.TemplateId == "PROC-OBRAS");
        Assert.Contains(afterSerra, o => o.TemplateId == "PROC-TUNEL");
    }
}

