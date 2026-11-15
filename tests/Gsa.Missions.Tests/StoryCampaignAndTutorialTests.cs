using Gsa.Missions.Campaign;
using Gsa.Missions.Contracts;
using Xunit;

namespace Gsa.Missions.Tests;

public sealed class StoryCampaignAndTutorialTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Gsa.Domain.slnf")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private static (IReadOnlyDictionary<string, MissionDefinition> Missions, IReadOnlyDictionary<string, ContractDefinition> Contracts)
        LoadRepoMissionsAndContracts(string root)
    {
        var missions = Directory.GetFiles(Path.Combine(root, "content", "missions"), "*.json")
            .Order(StringComparer.Ordinal)
            .Select(path => MissionDefinition.Parse(File.ReadAllText(path), Path.GetFileName(path)))
            .ToDictionary(m => m.Id, StringComparer.Ordinal);

        var contracts = Directory.GetFiles(Path.Combine(root, "content", "contracts"), "*.json")
            .Order(StringComparer.Ordinal)
            .Select(path => ContractDefinition.Parse(File.ReadAllText(path), Path.GetFileName(path)))
            .ToDictionary(c => c.Id, StringComparer.Ordinal);

        return (missions, contracts);
    }

    [Fact]
    public void Hist01Playthrough_TeachesCaptureUnloadCorrectionAndPayment_WithoutFutureSensors()
    {
        var root = FindRepoRoot();
        var contentDir = Path.Combine(root, "content");
        var worldPath = Path.Combine(root, "game", "world", "tutorial-block.json");

        var report = TutorialMissionRunner.RunHist01Playthrough(contentDir, worldPath);

        Assert.Equal("HIST-01", report.MissionId);
        Assert.Equal("contract_hist_01", report.ContractId);
        Assert.Equal(0, report.MinimumPlatformLevel);
        Assert.False(report.RequiresFutureSensors);
        Assert.DoesNotContain("lidar", report.RequiredCapabilities);
        Assert.DoesNotContain("imu", report.RequiredCapabilities);
        Assert.DoesNotContain("dmi", report.RequiredCapabilities);
        Assert.DoesNotContain("rtk", report.RequiredCapabilities);
        Assert.DoesNotContain("ppk", report.RequiredCapabilities);

        Assert.True(report.ControlsUsedInEstimation);
        Assert.True(report.CheckpointsReservedForQualityEvaluationOnly);

        Assert.False(report.InitialReportPassed);
        Assert.Equal(0, report.InitialStars);
        Assert.NotEmpty(report.InitialFailureReasons);
        Assert.False(report.InitialPrivacyCompliant);

        Assert.True(report.FinalReportPassed);
        Assert.Equal(5, report.FinalStars);
        Assert.True(report.NetPayoutCoords > 0);
        Assert.True(report.FinalLedgerCoords > report.NetPayoutCoords);
        Assert.True(report.IdempotentReplayConfirmed);
        Assert.Equal(report.RawBatchSha256BeforeProcessing, report.RawBatchSha256AfterProcessing);
        Assert.True(report.FinalPrivacyCompliant);
        Assert.True(report.SaveRoundTripVerified);

        Assert.Equal(4, report.Phases.Count);
        Assert.All(report.Phases, p => Assert.True(p.Passed));
        Assert.Equal(new[] { "HIST-01", "HIST-02" }, report.UnlockedMissions);
        Assert.Contains("cart", report.UnlockedVehicles);
    }

    [Fact]
    public void InitialStoryCatalog_Hist01To03_ProgressesAndUnlocksBicycleAndScooterWithoutCycles()
    {
        var root = FindRepoRoot();
        var (missions, contracts) = LoadRepoMissionsAndContracts(root);

        var validation = StoryCampaignCoordinator.ValidateInitialStoryCatalog(missions, contracts);
        Assert.True(validation.Valid, string.Join("; ", validation.Errors));
        Assert.False(validation.RequiresFutureSensors);
        Assert.Equal(new[] { "HIST-01", "HIST-02", "HIST-03" }, validation.OrderedMissionIds);
        Assert.Equal(new[] { "cart", "bicycle", "scooter" }, validation.UnlockedVehicleProgression);

        var coordinator = new StoryCampaignCoordinator(missions.Values);
        Assert.True(coordinator.CanStartMission("HIST-01", activePlatformLevel: 0));
        Assert.False(coordinator.CanStartMission("HIST-02", activePlatformLevel: 0));
        Assert.False(coordinator.CanStartMission("HIST-03", activePlatformLevel: 0));

        Assert.True(coordinator.CompleteMission("HIST-01", activePlatformLevel: 0));
        Assert.True(coordinator.CanStartMission("HIST-02", activePlatformLevel: 0));

        Assert.True(coordinator.CompleteMission("HIST-02", activePlatformLevel: 0));
        Assert.True(coordinator.State.IsVehicleUnlocked("bicycle"));
        Assert.False(coordinator.CanStartMission("HIST-03", activePlatformLevel: 0));
        Assert.True(coordinator.CanStartMission("HIST-03", activePlatformLevel: 1));

        Assert.True(coordinator.CompleteMission("HIST-03", activePlatformLevel: 1));
        Assert.True(coordinator.State.IsVehicleUnlocked("scooter"));
    }

    [Fact]
    public void CutsceneSkip_AppliesObjectivesAndUnlocksIdempotently_WithoutDuplicateEvents()
    {
        var root = FindRepoRoot();
        var (missions, _) = LoadRepoMissionsAndContracts(root);
        var hist01 = missions["HIST-01"];

        var coordinator = new StoryCampaignCoordinator(missions.Values);
        var skippedResults = hist01.Events
            .Select(ev => coordinator.TriggerOrSkipBeatEvent("HIST-01", ev.Id, skipCutscene: true))
            .ToArray();

        Assert.All(skippedResults, r =>
        {
            Assert.True(r.Applied);
            Assert.False(r.WasIdempotentNoOp);
            Assert.True(r.CutsceneSkipped);
        });

        var appliedCountAfterFirstPass = coordinator.State.AppliedEventIds.Count;
        Assert.Equal(hist01.Events.Count, appliedCountAfterFirstPass);
        Assert.True(coordinator.State.IsMissionUnlocked("HIST-02"));

        // Re-triggering or completing the mission again must stay strictly idempotent.
        var duplicate = coordinator.TriggerOrSkipBeatEvent("HIST-01", hist01.Events[0].Id, skipCutscene: true);
        Assert.True(duplicate.Applied);
        Assert.True(duplicate.WasIdempotentNoOp);

        Assert.True(coordinator.CompleteMission("HIST-01", activePlatformLevel: 0, skipAllCutscenes: true));
        Assert.Equal(appliedCountAfterFirstPass, coordinator.State.AppliedEventIds.Count);
        Assert.Single(coordinator.State.UnlockedMissions, m => m == "HIST-02");
    }

    [Fact]
    public void Hist02_AnnouncedRain_ActsAsQualityObstacle_NotInstantFailure()
    {
        var root = FindRepoRoot();
        var (missions, _) = LoadRepoMissionsAndContracts(root);
        var hist02 = missions["HIST-02"];

        Assert.Equal("announced_rain_quality_obstacle", hist02.WeatherMode);

        var unadjusted = StoryCampaignCoordinator.EvaluateAnnouncedRainObstacle(hist02, rainIntensity: 0.6, exposureAdjusted: false);
        Assert.True(unadjusted.IsRainActive);
        Assert.False(unadjusted.InstantFailureTriggered);
        Assert.True(unadjusted.CorrectionRequired);
        Assert.True(unadjusted.QualityPenaltyFraction > 0.1);

        var adjusted = StoryCampaignCoordinator.EvaluateAnnouncedRainObstacle(hist02, rainIntensity: 0.6, exposureAdjusted: true);
        Assert.True(adjusted.IsRainActive);
        Assert.False(adjusted.InstantFailureTriggered);
        Assert.False(adjusted.CorrectionRequired);
        Assert.True(adjusted.QualityPenaltyFraction < unadjusted.QualityPenaltyFraction);
    }

    [Fact]
    public void Hist03_LocalControlTargets_AreSeparatedFromEvaluationCheckpoints()
    {
        var root = FindRepoRoot();
        var (missions, contracts) = LoadRepoMissionsAndContracts(root);
        var hist03 = missions["HIST-03"];
        var hist03Contract = contracts[hist03.Objectives.Single().ContractId];

        Assert.Equal("local_control_planes_and_targets", hist03.ControlMode);
        Assert.Contains(hist03Contract.Requirements, r => r.Metric == "relative_rmse");

        var boundary = new ControlVsCheckpointReference(
            ["ctrl_plane_east", "ctrl_target_north"],
            ["chk_sidewalk_01", "chk_sidewalk_02"],
            controlsUsedInEstimation: true,
            checkpointsReservedForQualityEvaluationOnly: true);

        Assert.True(boundary.ControlsUsedInEstimation);
        Assert.True(boundary.CheckpointsReservedForQualityEvaluationOnly);
        Assert.Empty(boundary.ControlTargetIds.Intersect(boundary.CheckpointTargetIds, StringComparer.Ordinal));

        Assert.Throws<ArgumentException>(() => new ControlVsCheckpointReference(
            ["shared_point"],
            ["shared_point", "chk_02"],
            controlsUsedInEstimation: true,
            checkpointsReservedForQualityEvaluationOnly: true));

        Assert.Throws<ArgumentException>(() => new ControlVsCheckpointReference(
            ["ctrl_01"],
            ["chk_01"],
            controlsUsedInEstimation: true,
            checkpointsReservedForQualityEvaluationOnly: false));
    }
}
