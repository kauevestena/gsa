using Gsa.Missions.Campaign;
using Gsa.Missions.Content;
using Gsa.Missions.Contracts;
using Xunit;

namespace Gsa.Missions.Tests;

public sealed class M4Act1ClosureAndScriptsTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Gsa.Domain.slnf")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    [Fact]
    public void T_F10_03_Hist04ClosesAct1UnlocksCarPortWaterfrontAndValidatesFullCampaignGraphWithCalmParity()
    {
        var root = FindRepoRoot();
        var contentDir = Path.Combine(root, "content");
        var worldPath = Path.Combine(root, "game", "world", "tutorial-block.json");

        // 1. Validate full 11-mission graph, 5 beats, cutscene skip parity, zero weapons, and calm variants
        var graph = FullCampaignCatalog.ValidateCampaignGraphAndCalmParity();
        Assert.True(graph.Valid, string.Join("; ", graph.Errors));
        Assert.Equal(11, graph.MissionCount);
        Assert.True(graph.CalmVariantCount >= 7);

        // Cutscene full-watch vs skip must yield identical unlock and objective state idempotently
        var hist04Spec = FullCampaignCatalog.GetMission("HIST-04");
        var watchedState = new CampaignSaveProgressState();
        var skippedState = new CampaignSaveProgressState();
        Assert.True(CutsceneSkipAndStateCoordinator.ExecuteFull(hist04Spec.Cutscene, watchedState));
        Assert.True(CutsceneSkipAndStateCoordinator.SkipCutscene(hist04Spec.Cutscene, skippedState));
        Assert.False(CutsceneSkipAndStateCoordinator.SkipCutscene(hist04Spec.Cutscene, skippedState)); // idempotent
        Assert.Equal(watchedState, skippedState);

        // 2. Execute HIST-04 end-to-end (12 avenue signs by Scooter -> Car unlocked + Port/Waterfront opened)
        var closure = Act1ClosureRunner.RunAct1Closure(contentDir, worldPath);
        Assert.True(closure.Passed);
        Assert.Equal("HIST-04", closure.Hist04Summary.MissionId);
        Assert.Equal("contract_hist_04", closure.Hist04Summary.ContractId);
        Assert.True(closure.Hist04Summary.PrivacyCompliant);
        Assert.Equal(5, closure.Hist04Summary.FinalStars);
        Assert.True(closure.CarUnlockedInFleet);
        Assert.True(closure.PortUnlocked);
        Assert.True(closure.WaterfrontUnlocked);
        Assert.True(closure.GlobalReputation >= 10);

        // 3. Verify HIST-04.json & hist-04-signs.json parse cleanly alongside HIST-01..03
        var hist04Def = MissionDefinition.Parse(File.ReadAllText(Path.Combine(contentDir, "missions", "HIST-04.json")), "HIST-04.json");
        var hist04Contract = ContractDefinition.Parse(File.ReadAllText(Path.Combine(contentDir, "contracts", "hist-04-signs.json")), "hist-04-signs.json");
        Assert.Equal(1, hist04Def.Act);
        Assert.Equal(2, hist04Def.MinimumPlatformLevel);
        Assert.Equal(12, hist04Contract.RequiredFeatureIds.Count);
        Assert.Equal("disabled", hist04Contract.PartialPolicy.Mode);
    }

    [Fact]
    public void T_F10_06_RecordingScriptCatalogCoversAllMissionsNpcsActivitiesAndThreeRadioStationsBilinguallyWithoutAudioFiles()
    {
        var report = RecordingScriptCatalog.ValidateCatalogCompleteness();
        Assert.True(report.Valid, string.Join("; ", report.Errors));
        Assert.True(report.TotalCues >= 110);
        Assert.Equal(9, report.RadioProgramCount);
        Assert.True(report.SubtitlesPlayWithoutAudioFiles);
        Assert.Equal(3, RecordingScriptCatalog.RadioStations.Count);

        // Subtitle playback works cleanly for both pt-BR and en-US with zero audio assets on disk
        var ptSub = RecordingScriptCatalog.PlaybackSubtitlesWithoutAudio("hist_04_dlg_proposal", "pt-BR");
        var enSub = RecordingScriptCatalog.PlaybackSubtitlesWithoutAudio("hist_04_dlg_proposal", "en-US");
        Assert.False(ptSub.AudioFileRequired);
        Assert.False(enSub.AudioFileRequired);
        Assert.False(string.IsNullOrWhiteSpace(ptSub.Text));
        Assert.False(string.IsNullOrWhiteSpace(enSub.Text));
        Assert.NotEqual(ptSub.Text, enSub.Text);
    }
}

