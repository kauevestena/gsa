using Gsa.Missions.Campaign;
using Gsa.Missions.Diagnostics;
using Xunit;

namespace Gsa.Missions.Tests;

public sealed class M2PlaytestAndGateTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Gsa.Domain.slnf")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    [Fact]
    public void EvaluateM2Gate_VerifiesFullLoopAndLinksAll15M2Tasks()
    {
        var root = FindRepoRoot();
        var tutorialReport = TutorialMissionRunner.RunHist01Playthrough(
            Path.Combine(root, "content"),
            Path.Combine(root, "game", "world", "tutorial-block.json"));

        var gateReport = M2PlaytestAndGateValidator.BuildM2GateReport(tutorialReport);

        Assert.Equal("M2", gateReport.Milestone);
        Assert.Equal("passed", gateReport.TechnicalLoopStatus);
        Assert.Equal("pending_human_playtest_cohort", gateReport.PlaytestGateStatus);
        Assert.Equal("pending_human_playtest_cohort", gateReport.OverallGateStatus);
        Assert.Equal(5, gateReport.ProtocolThresholds.RequiredParticipants);
        Assert.Equal(30.0, gateReport.ProtocolThresholds.MaxDurationMinutes);
        Assert.Equal(4, gateReport.ProtocolThresholds.MinCompletedWithinTwoInterventions);
        Assert.Equal(4, gateReport.ProtocolThresholds.MinIdentifiedCauseAndCorrection);
        Assert.Equal(3, gateReport.ProtocolThresholds.MinChoseAnotherContract);

        Assert.Equal(15, gateReport.Tasks.Count);
        Assert.Equal(15, gateReport.Tasks.Count(t => t.Accepted));
        Assert.All(gateReport.Tasks, t => Assert.True(t.EvidenceVerified));
        Assert.Contains(gateReport.Tasks, t => t.TaskId == "T-F10-01" && t.Accepted);
        Assert.Contains(gateReport.Tasks, t => t.TaskId == "T-F19-02" && t.Accepted);
        Assert.All(gateReport.CorrectionsLog, c => Assert.True(c.VerifiedByRetest));
    }

    [Fact]
    public void EvaluateCohort_PassingNoviceCohort_MeetsAllProtocolThresholds()
    {
        var sessions = new[]
        {
            new M2PlaytestSessionRecord("p01", false, 20.0, true, 0, true, true, true, "none", "smooth", "none", IsHumanCohortSession: true),
            new M2PlaytestSessionRecord("p02", false, 24.0, true, 1, true, true, true, "none", "smooth", "minor hint", IsHumanCohortSession: true),
            new M2PlaytestSessionRecord("p03", false, 26.5, true, 2, true, true, true, "none", "ok", "ok", IsHumanCohortSession: true),
            new M2PlaytestSessionRecord("p04", false, 28.0, true, 1, true, true, false, "none", "ok", "ok", IsHumanCohortSession: true),
            new M2PlaytestSessionRecord("p05", false, 29.5, false, 3, false, false, false, "fatigue", "hard camera", "missed unload", IsHumanCohortSession: true),
        };

        var result = M2PlaytestAndGateValidator.EvaluateCohort(sessions);

        Assert.True(result.CriteriaMet);
        Assert.Equal("passed", result.PlaytestGateStatus);
        Assert.Equal(5, result.ObservedCounts.EligibleParticipants);
        Assert.Equal(4, result.ObservedCounts.QualifiedCompletedWithinTwoInterventions);
        Assert.Equal(4, result.ObservedCounts.QualifiedIdentifiedCauseAndCorrection);
        Assert.Equal(3, result.ObservedCounts.QualifiedChoseAnotherContract);
        Assert.False(result.ObservedCounts.ContainsDisqualifiedParticipants);
    }

    [Fact]
    public void EvaluateCohort_FailingCohort_GeneratesCorrectionItemsWithoutLoweringThresholds()
    {
        var sessions = new[]
        {
            new M2PlaytestSessionRecord("p01", false, 20.0, true, 0, true, true, true, "none", "ok", "none", IsHumanCohortSession: true),
            new M2PlaytestSessionRecord("p02", false, 22.0, true, 1, true, false, false, "none", "ok", "report unclear", IsHumanCohortSession: true),
            new M2PlaytestSessionRecord("p03", false, 25.0, false, 3, false, false, false, "none", "hard", "lost", IsHumanCohortSession: true),
            new M2PlaytestSessionRecord("p04", false, 27.0, true, 4, true, true, true, "none", "slow", "ok", IsHumanCohortSession: true),
            new M2PlaytestSessionRecord("p05", false, 29.0, false, 2, false, false, false, "none", "hard", "quit", IsHumanCohortSession: true),
        };

        var result = M2PlaytestAndGateValidator.EvaluateCohort(sessions);

        Assert.False(result.CriteriaMet);
        Assert.Equal("failed_rework_required", result.PlaytestGateStatus);
        Assert.Contains(result.EmittedCorrections, c => !c.VerifiedByRetest && c.Component == "TutorialGuidanceAndControls");
        Assert.Contains(result.EmittedCorrections, c => !c.VerifiedByRetest && c.Component == "QualityReportPresentation");
        Assert.Contains(result.EmittedCorrections, c => !c.VerifiedByRetest && c.Component == "ContractLoopPacingAndReward");
    }

    [Fact]
    public void EvaluateCohort_ParticipantWithPriorGeomaticsOrOver30Min_IsRejected()
    {
        var sessions = new[]
        {
            new M2PlaytestSessionRecord("p01", HasPriorGeomaticsKnowledge: true, 15.0, true, 0, true, true, true, "none", "ok", "none", IsHumanCohortSession: true),
            new M2PlaytestSessionRecord("p02", false, 31.5, true, 0, true, true, true, "none", "ok", "none", IsHumanCohortSession: true),
            new M2PlaytestSessionRecord("p03", false, 20.0, true, 0, true, true, true, "none", "ok", "none", IsHumanCohortSession: true),
            new M2PlaytestSessionRecord("p04", false, 21.0, true, 0, true, true, true, "none", "ok", "none", IsHumanCohortSession: true),
            new M2PlaytestSessionRecord("p05", false, 22.0, true, 0, true, true, true, "none", "ok", "none", IsHumanCohortSession: true),
        };

        var result = M2PlaytestAndGateValidator.EvaluateCohort(sessions);

        Assert.False(result.CriteriaMet);
        Assert.Equal("failed_rework_required", result.PlaytestGateStatus);
        Assert.True(result.ObservedCounts.ContainsDisqualifiedParticipants);
        Assert.Equal(3, result.ObservedCounts.EligibleParticipants);
        Assert.Contains(result.EmittedCorrections, c => c.Component == "PlaytestCohortScreening");
        Assert.Contains(result.EmittedCorrections, c => c.Component == "TutorialPacingAndOnboarding");
    }
}

