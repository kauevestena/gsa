using Gsa.Core.Mobility;
using Gsa.Core.World;
using Gsa.Missions.Campaign;
using Xunit;

namespace Gsa.Missions.Tests;

public sealed class M7Act3CampaignAndPavementTemplatesTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Gsa.Domain.slnf")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    [Fact]
    public void T_F10_05_ProcPavimento_100SeedsValidatesSchemaStorageRegionsAndHashInvalidation()
    {
        var report = Act3PavementProceduralGenerator.ValidateOneHundredSeedsForPavementTemplate();
        Assert.True(report.AllPassed);
        Assert.Equal(1, report.TemplatesValidated);
        Assert.Equal(100, report.TotalContractsGenerated);
        Assert.Equal(100, report.TotalContractsPassed);

        var entry = Assert.Single(report.ByTemplate);
        Assert.Equal(Act3PavementProceduralGenerator.TemplateId, entry.TemplateId);
        Assert.True(entry.AllCompatibleWithFleet);
        Assert.True(entry.AllWitnessRoutesFeasible);
        Assert.True(entry.AllSchemaValid);

        // Covers all 4 city regions
        var regions = new HashSet<string>(StringComparer.Ordinal);
        for (ulong s = 0; s < 4; s++)
            regions.Add(Act3PavementProceduralGenerator.GenerateFromPavementTemplate(s).Contract.RegionId);
        Assert.Equal(["old_center", "port", "serra", "waterfront"], regions.OrderBy(r => r, StringComparer.Ordinal).ToArray());

        // Map/profile hash drift invalidates witness certificate
        Assert.Throws<InvalidOperationException>(() =>
            Act3PavementProceduralGenerator.GenerateFromPavementTemplate(100UL, currentMapSha256: "stale_map_sha"));

        // Board gates L6 pavement offers by platform level and unlocked regions while retaining calm starter jobs at balance=0
        var l5Board = Act3PavementProceduralGenerator.GenerateFullCampaignBoard(
            balanceCoords: 0, fineDebtCoords: 50, ownedPlatformLevel: 5,
            new HashSet<string>(["old_center", "port", "waterfront", "serra"], StringComparer.Ordinal), seed: 100UL);
        Assert.DoesNotContain(l5Board, o => o.TemplateId == Act3PavementProceduralGenerator.TemplateId);
        Assert.Contains(l5Board, o => o.Contract.Calm && o.ExecutableWithStarterKit);

        var l6Board = Act3PavementProceduralGenerator.GenerateFullCampaignBoard(
            balanceCoords: 5000, fineDebtCoords: 0, ownedPlatformLevel: 6,
            new HashSet<string>(["old_center", "port", "waterfront", "serra"], StringComparer.Ordinal), seed: 100UL);
        Assert.Equal(4, l6Board.Count(o => o.TemplateId == Act3PavementProceduralGenerator.TemplateId));
    }

    [Fact]
    public void T_F10_05_FullThreeActCampaign_CalmParitySkipEquivalenceAnd24h30hFinale()
    {
        var root = FindRepoRoot();
        var contentDir = Path.Combine(root, "content");
        var manifestPath = Path.Combine(root, "game", "world", "tutorial-block.json");

        var standardWatched = Act3AndFullCampaignRunner.RunAct3AndFullCampaignPlaythrough(
            contentDir, manifestPath, calmCityEnabled: false, skipCutscenes: false);
        var standardSkipped = Act3AndFullCampaignRunner.RunAct3AndFullCampaignPlaythrough(
            contentDir, manifestPath, calmCityEnabled: false, skipCutscenes: true);
        var calmSkipped = Act3AndFullCampaignRunner.RunAct3AndFullCampaignPlaythrough(
            contentDir, manifestPath, calmCityEnabled: true, skipCutscenes: true);

        foreach (var run in new[] { standardWatched, standardSkipped, calmSkipped })
        {
            Assert.True(run.Act1Passed);
            Assert.True(run.Act2Passed);
            Assert.True(run.InterActSavesVerified);
            Assert.True(run.Hist10Passed);
            Assert.True(run.Hist11Passed);
            Assert.True(run.VanUnlocked);
            Assert.True(run.CloudFilterUnlocked);
            Assert.True(run.MasterSealUnlocked);
            Assert.True(run.OnTimeNoPenaltyVerified);
            Assert.True(run.LatePenaltyVerified);
            Assert.True(run.HardExpiryRejected);
            Assert.True(run.PostGameAndCutsceneIdempotent);
            Assert.True(run.Passed);
            Assert.Equal(4, run.Hist11Review.Sectors.Count);
            Assert.All(run.Hist11Review.Sectors, s => Assert.True(s.Passed));
            Assert.True(run.Hist10Review.Delivery.Passed);
        }

        // Watched vs skipped cutscenes produce identical unlocks, platforms, regions, and objective state
        Assert.Equal(standardWatched.FinalProgressState, standardSkipped.FinalProgressState);
        // Standard vs Calm City produce identical final payout, reputation, and progression unlocks
        Assert.Equal(standardSkipped.FinalBalanceCoords, calmSkipped.FinalBalanceCoords);
        Assert.Equal(standardSkipped.GlobalReputation, calmSkipped.GlobalReputation);
        Assert.Equal(standardSkipped.FinalProgressState, calmSkipped.FinalProgressState);
    }
}

